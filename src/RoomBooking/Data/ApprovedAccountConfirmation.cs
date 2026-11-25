using Microsoft.AspNetCore.Identity;

namespace RoomBooking.Data;

// Identity calls this at sign-in when SignIn.RequireConfirmedAccount is on.
// "Confirmed" here means an Admin approved the account, not that an email was verified.
public class ApprovedAccountConfirmation : IUserConfirmation<ApplicationUser>
{
    public Task<bool> IsConfirmedAsync(UserManager<ApplicationUser> manager, ApplicationUser user) =>
        Task.FromResult(user.Status == AccountStatus.Approved);
}
