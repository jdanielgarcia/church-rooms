using System.ComponentModel.DataAnnotations;

namespace RoomBooking.Data;

public class Room
{
    public int Id { get; set; }

    [Required, MaxLength(100)]
    public string Name { get; set; } = "";

    [MaxLength(500)]
    public string? Description { get; set; }

    // Rooms are deactivated, never deleted, so old Bookings keep pointing at them.
    public bool IsActive { get; set; } = true;
}
