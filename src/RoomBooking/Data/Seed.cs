using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace RoomBooking.Data;

public static class Seed
{
    public const string AdminRole = "Admin";

    public static async Task RunAsync(IServiceProvider services, IConfiguration config, bool isDevelopment)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        // ponytail: migrate on startup. Fine for one app instance; run `dotnet ef database update` in a deploy step if there are ever several.
        await db.Database.MigrateAsync();

        var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        if (!await roles.RoleExistsAsync(AdminRole))
            await roles.CreateAsync(new IdentityRole(AdminRole));

        // First Admin comes from config (user secrets in dev). Only when no Admin exists yet.
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var email = config["SeedAdmin:Email"];
        var password = config["SeedAdmin:Password"];
        if (!string.IsNullOrEmpty(email) && !string.IsNullOrEmpty(password)
            && (await users.GetUsersInRoleAsync(AdminRole)).Count == 0)
        {
            var admin = await users.FindByEmailAsync(email);
            if (admin is null)
            {
                admin = new ApplicationUser
                {
                    UserName = email,
                    Email = email,
                    DisplayName = config["SeedAdmin:Name"] ?? "Admin",
                    Status = AccountStatus.Approved,
                };
                var created = await users.CreateAsync(admin, password);
                if (!created.Succeeded)
                    throw new InvalidOperationException("Seed admin: " + string.Join("; ", created.Errors.Select(e => e.Description)));
            }
            admin.Status = AccountStatus.Approved;
            await users.UpdateAsync(admin);
            await users.AddToRoleAsync(admin, AdminRole);
        }

        if (isDevelopment && !await db.Rooms.AnyAsync())
        {
            db.Rooms.AddRange(
                new Room { Name = "Fellowship Hall", Description = "Ground floor, 60 chairs" },
                new Room { Name = "Room 201", Description = "2nd floor, aircon, 15 chairs" },
                new Room { Name = "Room 202", Description = "2nd floor, whiteboard, 12 chairs" });
            await db.SaveChangesAsync();
        }
    }
}
