using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CMS.Domain.Common;
using CMS.Domain.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace CMS.Infrastructure.Persistence;

public static class DbSeeder
{
    // Seeds users and complaints for load testing.
    // Controlled by environment variables (optional):
    //   SEED_USER_COUNT -> number of users to create (default 500)
    //   SEED_COMPLAINTS_PER_USER -> complaints per user (default 20)
    public static async Task SeedUsersAsync(IServiceProvider serviceProvider)
    {
        using var scope = serviceProvider.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var userCountEnv = Environment.GetEnvironmentVariable("SEED_USER_COUNT");
        var complaintsPerUserEnv = Environment.GetEnvironmentVariable("SEED_COMPLAINTS_PER_USER");

        var userCount = 500;
        var complaintsPerUser = 20;

        if (!string.IsNullOrEmpty(userCountEnv) && int.TryParse(userCountEnv, out var uc))
            userCount = Math.Max(1, uc);

        if (!string.IsNullOrEmpty(complaintsPerUserEnv) && int.TryParse(complaintsPerUserEnv, out var cp))
            complaintsPerUser = Math.Max(0, cp);

        Console.WriteLine($"Seeding {userCount} users, {complaintsPerUser} complaints each (total ~{(long)userCount * complaintsPerUser})");

        var rnd = new Random(12345);

        // Create users and their complaints in batches to avoid memory pressure
        for (int i = 1; i <= userCount; i++)
        {
            var email = $"user{i}@test.com";

            // Check if user exists
            var userExists = await userManager.FindByEmailAsync(email);

            ApplicationUser user;

            if (userExists == null)
            {
                user = new ApplicationUser
                {
                    UserName = email,
                    Email = email,
                    UserType = UserType.Citizen,
                    EmailConfirmed = true,
                    IsActive = true
                };

                var result = await userManager.CreateAsync(user, "Password123!");

                if (result.Succeeded)
                {
                    Console.WriteLine($"Created {email}");
                }
                else
                {
                    Console.WriteLine($"Failed to create {email}: {result.Errors.FirstOrDefault()?.Description ?? "unknown"}");
                    // Try to retrieve the user if it was partially created
                    user = await userManager.FindByEmailAsync(email) ?? user;
                }
            }
            else
            {
                user = userExists;
            }

            // Create complaints for this user
            if (complaintsPerUser > 0)
            {
                var complaints = new List<Complaint>(complaintsPerUser);

                for (int j = 1; j <= complaintsPerUser; j++)
                {
                    var c = new Complaint
                    {
                        Id = Guid.NewGuid(),
                        Title = $"Load test complaint {i}-{j}",
                        Description = "This is a generated complaint for load testing. Ignore.",
                        CitizenId = user.Id,
                        DepartmentId = (j % 5 == 0) ? "sanitation" : "general",
                        Priority = (ComplaintPriority)(rnd.Next(0, 4)),
                        Status = ComplaintStatus.Pending,
                        Address = "123 Test St",
                        Latitude = 31 + (decimal)rnd.NextDouble() / 10m,
                        Longitude = 35 + (decimal)rnd.NextDouble() / 10m,
                        Metadata = "{}"
                    };

                    complaints.Add(c);
                }

                // Add and save in smaller batches to avoid huge transaction
                await context.Complaints.AddRangeAsync(complaints);

                if (i % 10 == 0) // commit every 10 users
                {
                    await context.SaveChangesAsync();
                    Console.WriteLine($"Saved complaints for users up to {i}");
                }
            }
        }

        // Final save for any remaining complaints
        await context.SaveChangesAsync();

        Console.WriteLine("Seeding complete.");
    }
}