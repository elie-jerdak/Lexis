using Lexis.Core.Models;
using Microsoft.AspNetCore.Identity;

namespace Lexis.API.Data
{
    public class IdentitySeeder
    {
        public static async Task SeedAsync(UserManager<ApplicationUser> userManager, RoleManager<IdentityRole<int>> roleManager)
        {
            // Create roles
            string[] roles = {"Admin", "User"};

            foreach (var role in roles)
            {
                if (!await roleManager.RoleExistsAsync(role))
                {
                    await roleManager.CreateAsync(new IdentityRole<int>(role));
                }
            }


            // Create admin user
            var adminEmail = "jardakelie@gmail.com";

            var adminUser = await userManager.FindByEmailAsync(adminEmail);

            if (adminUser == null)
            {
                adminUser = new ApplicationUser
                {
                    UserName = adminEmail,
                    Email = adminEmail,

                    FirstName = "Elie",
                    LastName = "Jerdak",

                    EmailConfirmed = true,

                    CreatedAt = DateTime.UtcNow,
                    IsActive = true,

                    MonthlyTokenAllowance = 100000000
                };


                var result = await userManager.CreateAsync(adminUser, "Admin@123");


                if (!result.Succeeded)
                {
                    throw new Exception(
                        string.Join(
                            ", ",
                            result.Errors.Select(e => e.Description)
                        )
                    );
                }
            }


            // Assign Admin role
            if (!await userManager.IsInRoleAsync(adminUser, "Admin"))
            {
                await userManager.AddToRoleAsync(adminUser, "Admin");
            }
        }
    }
}
