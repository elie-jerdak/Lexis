using Amazon.Runtime;
using Amazon.S3;
using Lexis.API.Data;
using Lexis.API.Hubs;
using Lexis.API.Services;
using Lexis.Core.BackgroundServices;
using Lexis.Core.Enums;
using Lexis.Core.Helpers;
using Lexis.Core.Interfaces;
using Lexis.Core.Models;
using Lexis.Core.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore; 
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Npgsql;
using Resend;
using Scalar.AspNetCore;
using System.Security.Claims;
using System.Text;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<JwtSettings>(
    builder.Configuration.GetSection("Jwt"));

builder.Services.AddControllers();

builder.Services.AddOpenApi();

builder.Services
    .AddIdentity<ApplicationUser, IdentityRole<int>>(options =>
    {
        options.Password.RequiredLength = 8;
        options.Password.RequireUppercase = true;
        options.Password.RequireLowercase = true;
        options.Password.RequireDigit = true;
        options.User.RequireUniqueEmail = true;
    })
    .AddEntityFrameworkStores<ApplicationDbContext>()
    .AddDefaultTokenProviders();

builder.Services
    .AddAuthentication(options =>
    {
        options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
        options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
    })
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = builder.Configuration["Jwt:Issuer"],
            ValidAudience = builder.Configuration["Jwt:Audience"],
            IssuerSigningKey = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(builder.Configuration["Jwt:Key"]!)
            )
        };

        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                var accessToken = context.Request.Query["access_token"];
                var requestPath = context.HttpContext.Request.Path;

                // SignalR browser transports may send the JWT in the query string.
                // Restrict this behavior to the document-status hub.
                if (!string.IsNullOrEmpty(accessToken) &&
                    requestPath.StartsWithSegments(
                        DocumentStatusHub.Route))
                {
                    context.Token = accessToken;
                    return Task.CompletedTask;
                }

                // Preserve your existing cookie authentication fallback.
                if (string.IsNullOrEmpty(
                        context.Request.Headers.Authorization) &&
                    context.Request.Cookies.TryGetValue(
                        "lexis_access_token",
                        out var token))
                {
                    context.Token = token;
                }

                return Task.CompletedTask;
            }
        };
    });

builder.Services.AddSingleton(
    builder.Configuration
        .GetSection("Resend")
        .Get<ResendSettings>()!
);


builder.Services.AddResend(options =>
{
    options.ApiToken =
        builder.Configuration["Resend:ApiKey"]!;
});

builder.Services.AddSingleton<NpgsqlDataSource>(_ =>
{
    var connectionString =
        builder.Configuration.GetConnectionString("DefaultConnection")
        ?? throw new InvalidOperationException(
            "Connection string 'DefaultConnection' is missing.");

    var dataSourceBuilder = new NpgsqlDataSourceBuilder(connectionString);

    dataSourceBuilder.MapEnum<DocumentStatus>("document_status");
    dataSourceBuilder.MapEnum<MessageRole>("message_role");

    return dataSourceBuilder.Build();
});

builder.Services.AddDbContext<ApplicationDbContext>((serviceProvider, options) =>
{
    var dataSource = serviceProvider.GetRequiredService<NpgsqlDataSource>();

    options.UseNpgsql(dataSource, npgsql =>
    {
        npgsql.MapEnum<DocumentStatus>("document_status");
        npgsql.MapEnum<MessageRole>("message_role");
    });
});

var allowedFrontendOrigins =
    builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
    ?? new[] { "http://localhost:3000" };

builder.Services.AddCors(options =>
{
    options.AddPolicy("Frontend", policy =>
    {
        policy
            .WithOrigins(allowedFrontendOrigins)
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials();
    });
});


builder.Services.Configure<DataProtectionTokenProviderOptions>(options =>
{
    options.TokenLifespan = TimeSpan.FromHours(24);
});

builder.Services.Configure<OllamaEmbeddingOptions>(
    builder.Configuration.GetSection(OllamaEmbeddingOptions.SectionName));

builder.Services.AddHttpClient<IEmbeddingService, OllamaEmbeddingService>();

builder.Services.Configure<QdrantOptions>(
    builder.Configuration.GetSection(QdrantOptions.SectionName));

builder.Services.AddHttpClient<IVectorStoreService, QdrantVectorStoreService>();

builder.Services.AddAuthorization();

builder.Services.AddHostedService<DocumentProcessingWorker>();

builder.Services.AddHttpClient<IDocumentExtractionService, DoclingExtractionService>(client =>
{
    //client.BaseAddress = new Uri("http://docling:8000/"); use docker-compose on the yaml file
    client.BaseAddress = new Uri("http://host.docker.internal:8000/");
    client.Timeout = TimeSpan.FromMinutes(235);
});

builder.Services
    .AddOptions<CloudflareR2Options>()
    .Bind(builder.Configuration.GetSection(
        CloudflareR2Options.SectionName))
    .Validate(
        options =>
            Uri.TryCreate(
                options.ServiceUrl,
                UriKind.Absolute,
                out var uri) &&
            uri.Scheme == Uri.UriSchemeHttps &&
            !string.IsNullOrWhiteSpace(options.BucketName) &&
            !string.IsNullOrWhiteSpace(options.AccessKeyId) &&
            !string.IsNullOrWhiteSpace(options.SecretAccessKey),
        "Cloudflare R2 configuration is invalid.")
    .ValidateOnStart();

builder.Services.AddSingleton<Amazon.S3.IAmazonS3>(serviceProvider =>
{
    var options = serviceProvider
        .GetRequiredService<IOptions<CloudflareR2Options>>()
        .Value;

    var credentials = new BasicAWSCredentials(
        options.AccessKeyId,
        options.SecretAccessKey);

    var config = new AmazonS3Config
    {
        ServiceURL = options.ServiceUrl,
        AuthenticationRegion = "auto",
        ForcePathStyle = true
    };

    return new AmazonS3Client(credentials, config);
});

builder.Services
    .AddOptions<DocumentProcessingOptions>()
    .Bind(builder.Configuration.GetSection(
        DocumentProcessingOptions.SectionName))
    .Validate(
        options =>
            options.MaxConcurrency is >= 1 and <= 16 &&
            options.MaxAttempts >= 1 &&
            options.LeaseDurationSeconds >= 30 &&
            options.LeaseRenewalSeconds >= 5 &&
            options.LeaseRenewalSeconds < options.LeaseDurationSeconds &&
            options.IdleDelaySeconds >= 1,
        "DocumentProcessing configuration is invalid.")
    .ValidateOnStart();

builder.Services.AddSignalR();

builder.Services.AddSingleton<IDocumentStatusNotifier, DocumentStatusNotifier>();

builder.Services.AddRateLimiter(options =>
{
    // General limit: 120 requests per minute per IP.
    options.GlobalLimiter =
        PartitionedRateLimiter.Create<HttpContext, string>(context =>
            RateLimitPartition.GetFixedWindowLimiter(
                partitionKey: GetClientIp(context),
                factory: _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = 120,
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0,
                    AutoReplenishment = true
                }));

    // Authentication endpoints: partition by IP.
    options.AddPolicy("Login", context =>
        CreateIpLimiter(context, 5));

    options.AddPolicy("Register", context =>
        CreateIpLimiter(context, 3));

    options.AddPolicy("EmailConfirmation", context =>
        CreateIpLimiter(context, 10));

    options.AddPolicy("ResendConfirmation", context =>
        CreateIpLimiter(context, 3));

    options.AddPolicy("ForgotPassword", context =>
        CreateIpLimiter(context, 3));

    options.AddPolicy("ResetPassword", context =>
        CreateIpLimiter(context, 5));

    options.AddPolicy("ChangePassword", context =>
        CreateUserLimiter(context, 5));

    options.AddPolicy("Refresh", context =>
        CreateIpLimiter(context, 10));

    // Expensive operations: partition by authenticated user.
    options.AddPolicy("DocumentUpload", context =>
        CreateUserLimiter(context, 10));

    options.AddPolicy("RagQuery", context =>
        CreateUserLimiter(context, 10));

    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    options.OnRejected = async (context, cancellationToken) =>
    {
        context.HttpContext.Response.ContentType = "application/json";

        await context.HttpContext.Response.WriteAsJsonAsync(
            new
            {
                message = "Too many requests. Please try again later."
            },
            cancellationToken);
    };
});

static string GetClientIp(HttpContext context) =>
    context.Connection.RemoteIpAddress?.ToString() ?? "unknown";

static FixedWindowRateLimiterOptions CreateLimiterOptions(int limit) =>
    new()
    {
        PermitLimit = limit,
        Window = TimeSpan.FromMinutes(1),
        QueueLimit = 0,
        AutoReplenishment = true
    };

static RateLimitPartition<string> CreateIpLimiter(
    HttpContext context,
    int limit) =>
    RateLimitPartition.GetFixedWindowLimiter(
        GetClientIp(context),
        _ => CreateLimiterOptions(limit));

static RateLimitPartition<string> CreateUserLimiter(
    HttpContext context,
    int limit)
{
    var userId =
        context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value
        ?? context.User.FindFirst("sub")?.Value;

    // Fall back to IP for unauthenticated requests.
    var partitionKey = userId is not null
        ? $"user:{userId}"
        : $"ip:{GetClientIp(context)}";

    return RateLimitPartition.GetFixedWindowLimiter(
        partitionKey,
        _ => CreateLimiterOptions(limit));
}

builder.Services.AddStackExchangeRedisCache(options =>
{
    options.Configuration =
        builder.Configuration["Redis:ConnectionString"];

    options.InstanceName = "Lexis:";
});

builder.Services.AddScoped<IJwtService, JwtService>();
builder.Services.AddScoped<IAuthService, AuthenticationService>();
builder.Services.AddScoped<IEmailService, EmailService>();
builder.Services.AddScoped<IUserService, UserService>();
builder.Services.AddScoped<IRefreshTokenService, RefreshTokenService>();
//builder.Services.AddScoped<IFileStorageService, LocalFileStorageService>();
builder.Services.AddScoped<IFileHashService, FileHashService>();
builder.Services.AddScoped<IDocumentService, DocumentService>();
builder.Services.AddScoped<IDocumentProcessingService, DocumentProcessingService>(); 
builder.Services.AddScoped<ITextCleaningService, TextCleaningService>(); 
builder.Services.AddScoped<IDocumentPreprocessingService, DocumentPreprocessingService>();
builder.Services.AddScoped<IDocumentStructureService, DocumentStructureService>();
builder.Services.AddScoped<IDocumentChunkingService, DocumentChunkingService>();
builder.Services.AddScoped<IDocumentChunkRepository, DocumentChunkRepository>();
builder.Services.AddScoped<ITableContinuationService, TableContinuationService>();
builder.Services.AddScoped<TableContinuationHelpers>();
builder.Services.AddScoped<TableContinuationAnalyzer>();
builder.Services.AddScoped<IDocumentSearchService, DocumentSearchService>();
builder.Services.AddScoped<IRagService, RagService>();
builder.Services.AddScoped<IConversationService, ConversationService>();
builder.Services.AddHttpClient<IAnswerGenerationService, OpenAiAnswerGenerationService>();
builder.Services.AddScoped<IUsageLogService, UsageLogService>();
builder.Services.AddScoped<IFileStorageService, R2FileStorageService>();
builder.Services.AddScoped<IDocumentProcessingLeaseService, DocumentProcessingLeaseService>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}

app.UseRouting();

app.UseCors("Frontend");

app.Use(async (context, next) =>
{
    var request = context.Request;

    var isStateChangingMethod =
        HttpMethods.IsPost(request.Method) ||
        HttpMethods.IsPut(request.Method) ||
        HttpMethods.IsPatch(request.Method) ||
        HttpMethods.IsDelete(request.Method);

    var hasAuthCookie =
        request.Cookies.ContainsKey("lexis_access_token") ||
        request.Cookies.ContainsKey("lexis_refresh_token");

    if (isStateChangingMethod && hasAuthCookie)
    {
        var origin = request.Headers["Origin"].ToString();

        var isAllowedOrigin =
            !string.IsNullOrWhiteSpace(origin) &&
            allowedFrontendOrigins.Contains(
                origin,
                StringComparer.OrdinalIgnoreCase);

        if (!isAllowedOrigin)
        {
            context.Response.StatusCode =
                StatusCodes.Status403Forbidden;

            context.Response.ContentType = "application/json";

            await context.Response.WriteAsJsonAsync(new
            {
                message = "Request origin is not allowed."
            });

            return;
        }
    }

    await next();
});

app.UseAuthentication();
app.UseRateLimiter();
app.UseAuthorization();

app.MapControllers();

using (var scope = app.Services.CreateScope())
{
    var userManager = scope.ServiceProvider
        .GetRequiredService<UserManager<ApplicationUser>>();

    var roleManager = scope.ServiceProvider
        .GetRequiredService<RoleManager<IdentityRole<int>>>();

    await IdentitySeeder.SeedAsync(userManager, roleManager);
}

app.MapHub<DocumentStatusHub>(DocumentStatusHub.Route);

app.Run();
