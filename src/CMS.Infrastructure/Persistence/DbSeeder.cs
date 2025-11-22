using CMS.Domain.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace CMS.Infrastructure.Persistence;

public static class DbSeeder
{
    public static async Task SeedUsersAsync(IServiceProvider serviceProvider)
    {
        using var scope = serviceProvider.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        // We will create users: user1@test.com to user100@test.com
        // Password: Password123!
        
        for (int i = 1; i <= 100; i++)
        {
            var email = $"user{i}@test.com";
            
            // Check if user exists
            var userExists = await userManager.FindByEmailAsync(email);
            
            if (userExists == null)
            {
                var newUser = new ApplicationUser
                {
                    UserName = email, // Identity requires UserName
                    Email = email,
                    UserType = UserType.Citizen, // Assuming 1 = Citizen
                    EmailConfirmed = true, // <--- Auto Verify!
                    IsActive = true
                };

                var result = await userManager.CreateAsync(newUser, "Password123!");
                
                if (result.Succeeded)
                {
                    Console.WriteLine($"Created {email}");
                }
                else
                {
                    Console.WriteLine($"Failed to create {email}: {result.Errors.First().Description}");
                }
            }
        }
    }
}