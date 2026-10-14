using Lexis.API.Data;
using Lexis.Core.BackgroundServices;
using Lexis.Core.Enums;
using Lexis.Core.Helpers;
using Lexis.Core.Interfaces;
using Lexis.Core.Models;
using Lexis.Core.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Resend;
using Scalar.AspNetCore;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<JwtSettings>(
    builder.Configuration.GetSection("Jwt"));

builder.Services.AddControllers();
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
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

builder.Services.AddDbContext<ApplicationDbContext>(options =>
{
    options.UseNpgsql(
        builder.Configuration.GetConnectionString("DefaultConnection"),
        npgsql =>
        {
            npgsql.MapEnum<DocumentStatus>("document_status");
            npgsql.MapEnum<MessageRole>("message_role");
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

builder.Services.AddScoped<IJwtService, JwtService>();
builder.Services.AddScoped<IAuthService, AuthenticationService>();
builder.Services.AddScoped<IEmailService, EmailService>();
builder.Services.AddScoped<IUserService, UserService>();
builder.Services.AddScoped<IRefreshTokenService, RefreshTokenService>();
builder.Services.AddScoped<IFileStorageService, LocalFileStorageService>();
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

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}

app.UseHttpsRedirection();

app.UseAuthentication();
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

app.Run();
