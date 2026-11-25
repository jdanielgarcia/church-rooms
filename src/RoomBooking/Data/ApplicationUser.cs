using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Identity;

namespace RoomBooking.Data;

// A Booker: Leader or Admin. Admins are Bookers in the "Admin" role.
public class ApplicationUser : IdentityUser
{
    [MaxLength(100)]
    public string DisplayName { get; set; } = "";

    public AccountStatus Status { get; set; } = AccountStatus.Pending;
}

public enum AccountStatus
{
    Pending,
    Approved,
    Rejected,
}
