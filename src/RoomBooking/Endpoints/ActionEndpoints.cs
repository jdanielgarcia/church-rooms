using System.Security.Claims;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RoomBooking.Bookings;
using RoomBooking.Components.Account;
using RoomBooking.Data;

namespace RoomBooking.Endpoints;

// One-click actions (buttons in lists) are plain HTML form POSTs to these minimal API endpoints.
// Each does its work and redirects back with a status message: Post/Redirect/Get, same as a Laravel
// controller action ending in `return back()->with('status', ...)`.
// Antiforgery is validated automatically for [FromForm] endpoints; forms include <AntiforgeryToken />.
public static class ActionEndpoints
{
    public static void MapActionEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/Account/Logout", async (SignInManager<ApplicationUser> signIn) =>
        {
            await signIn.SignOutAsync();
            return TypedResults.LocalRedirect("~/");
        });

        // ---- Leader (or Admin) on their own Bookings ----
        var bookings = app.MapGroup("/actions/bookings/{id:int}").RequireAuthorization();

        bookings.MapPost("/withdraw", async (int id, [FromForm] string returnUrl, ClaimsPrincipal user, BookingService svc, HttpContext ctx) =>
            Back(ctx, returnUrl, await svc.WithdrawAsync(id, UserId(user)), "Request withdrawn."));

        bookings.MapPost("/cancel", async (int id, [FromForm] string returnUrl, ClaimsPrincipal user, BookingService svc, HttpContext ctx) =>
            Back(ctx, returnUrl, await svc.CancelAsync(id, UserId(user), IsAdmin(user)), "Booking cancelled."));

        bookings.MapPost("/end", async (int id, [FromForm] DateOnly from, [FromForm] string returnUrl, ClaimsPrincipal user, BookingService svc, HttpContext ctx) =>
            Back(ctx, returnUrl, await svc.EndWeeklyAsync(id, UserId(user), IsAdmin(user), from), "End date set."));

        bookings.MapPost("/skip", async (int id, [FromForm] DateOnly date, [FromForm] string returnUrl, ClaimsPrincipal user, BookingService svc, HttpContext ctx) =>
            Back(ctx, returnUrl, await svc.SkipAsync(id, UserId(user), IsAdmin(user), date), "Date skipped. The room shows free that day."));

        // ---- Admin ----
        var admin = app.MapGroup("/actions/admin").RequireAuthorization(Seed.AdminRole);

        admin.MapPost("/bookings/{id:int}/approve", async (int id, [FromForm] string returnUrl, BookingService svc, HttpContext ctx) =>
        {
            var result = await svc.ApproveAsync(id);
            // A clash needs fixing on the edit page, which lists the clashing Occurrences.
            return result.Clashes is { Count: > 0 }
                ? Back(ctx, $"/admin/bookings/{id}", result, "")
                : Back(ctx, returnUrl, result, "Approved.");
        });

        admin.MapPost("/bookings/{id:int}/reject", async (int id, [FromForm] string reason, [FromForm] string returnUrl, BookingService svc, HttpContext ctx) =>
            Back(ctx, returnUrl, await svc.RejectAsync(id, reason), "Rejected."));

        admin.MapPost("/users/{id}/{action}", async (string id, string action, [FromForm] string returnUrl, [FromForm] string? password,
            ClaimsPrincipal me, UserManager<ApplicationUser> users, HttpContext ctx) =>
        {
            var user = await users.FindByIdAsync(id);
            if (user is null)
                return Back(ctx, returnUrl, BookingResult.Fail("User not found."), "");
            if (user.Id == UserId(me) && action is "remove-admin" or "reject")
                return Back(ctx, returnUrl, BookingResult.Fail("You can't do that to your own account."), "");

            IdentityResult result;
            switch (action)
            {
                case "approve":
                    user.Status = AccountStatus.Approved;
                    result = await users.UpdateAsync(user);
                    break;
                case "reject":
                    user.Status = AccountStatus.Rejected;
                    result = await users.UpdateAsync(user);
                    break;
                case "make-admin":
                    result = await users.AddToRoleAsync(user, Seed.AdminRole);
                    break;
                case "remove-admin":
                    result = await users.RemoveFromRoleAsync(user, Seed.AdminRole);
                    break;
                case "password":
                    // No email yet, so no self-service reset: Admin sets a temp password and tells the person.
                    if (string.IsNullOrWhiteSpace(password))
                        return Back(ctx, returnUrl, BookingResult.Fail("Enter a temporary password."), "");
                    await users.RemovePasswordAsync(user);
                    result = await users.AddPasswordAsync(user, password);
                    if (result.Succeeded)
                        await users.SetLockoutEndDateAsync(user, null);
                    break;
                default:
                    return Back(ctx, returnUrl, BookingResult.Fail("Unknown action."), "");
            }

            if (result.Succeeded)
                await users.UpdateSecurityStampAsync(user); // forces their cookie to refresh roles within a minute

            var outcome = result.Succeeded
                ? BookingResult.Success
                : BookingResult.Fail(string.Join(" ", result.Errors.Select(e => e.Description)));
            return Back(ctx, returnUrl, outcome, $"Done: {action.Replace('-', ' ')} for {user.DisplayName}.");
        });

        admin.MapPost("/rooms/{id:int}/toggle", async (int id, [FromForm] string returnUrl, ApplicationDbContext db, HttpContext ctx) =>
        {
            var room = await db.Rooms.FindAsync(id);
            if (room is null)
                return Back(ctx, returnUrl, BookingResult.Fail("Room not found."), "");
            room.IsActive = !room.IsActive;
            await db.SaveChangesAsync();
            return Back(ctx, returnUrl, BookingResult.Success, room.IsActive ? $"{room.Name} reactivated." : $"{room.Name} deactivated.");
        });
        admin.MapPost("/rooms/{id:int}/delete", async (int id, [FromForm] string returnUrl, ApplicationDbContext db, HttpContext ctx) =>
        {
            var room = await db.Rooms.FindAsync(id);
            if (room is null)
                return Back(ctx, returnUrl, BookingResult.Fail("Room not found."), "");
            // Only rooms created by mistake: anything ever booked keeps its history and gets deactivated instead.
            if (await db.Bookings.AnyAsync(b => b.RoomId == id))
                return Back(ctx, returnUrl, BookingResult.Fail($"{room.Name} has booking history. Deactivate it instead."), "");
            db.Rooms.Remove(room);
            await db.SaveChangesAsync();
            return Back(ctx, returnUrl, BookingResult.Success, $"{room.Name} deleted.");
        });
    }

    private static string UserId(ClaimsPrincipal user) => user.FindFirstValue(ClaimTypes.NameIdentifier)!;

    private static bool IsAdmin(ClaimsPrincipal user) => user.IsInRole(Seed.AdminRole);

    private static RedirectHttpResult Back(HttpContext ctx, string? returnUrl, BookingResult result, string successMessage)
    {
        var message = result.Ok ? successMessage : $"Error: {result.Error}";
        if (!string.IsNullOrEmpty(message))
            ctx.Response.Cookies.Append(IdentityRedirectManager.StatusCookieName, message,
                new CookieOptions { HttpOnly = true, SameSite = SameSiteMode.Strict, MaxAge = TimeSpan.FromSeconds(5) });

        // Only ever redirect within this site (prevents open redirects).
        var url = returnUrl is { Length: > 0 } && returnUrl[0] == '/' && !returnUrl.StartsWith("//") && !returnUrl.StartsWith("/\\")
            ? returnUrl
            : "/";
        return TypedResults.Redirect(url);
    }
}
