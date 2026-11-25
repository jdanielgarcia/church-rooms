using System.ComponentModel.DataAnnotations;

namespace RoomBooking.Data;

// See CONTEXT.md. A one-off Booking has one Occurrence; a weekly Booking repeats every 7 days
// from its first Occurrence (ADR-0004). All DateTimes are UTC.
public class Booking
{
    public int Id { get; set; }

    public int RoomId { get; set; }
    public Room Room { get; set; } = null!;

    public string OwnerId { get; set; } = "";
    public ApplicationUser Owner { get; set; } = null!;

    [Required, MaxLength(100)]
    public string GroupName { get; set; } = "";

    // First Occurrence.
    public DateTime StartUtc { get; set; }
    public DateTime EndUtc { get; set; }

    public bool IsWeekly { get; set; }

    // Weekly only. Exclusive: Occurrences starting at or after this instant don't happen.
    // Null = open-ended.
    public DateTime? UntilUtc { get; set; }

    public BookingStatus Status { get; set; } = BookingStatus.Pending;

    [MaxLength(500)]
    public string? RejectReason { get; set; }

    // Shown to the Leader, e.g. "Changed by Admin: Room 3 → Room 5".
    [MaxLength(500)]
    public string? AdminNote { get; set; }

    // Set on a Change request: the approved Booking this one replaces once approved.
    public int? ReplacesBookingId { get; set; }
    public Booking? ReplacesBooking { get; set; }

    public List<SkippedDate> SkippedDates { get; set; } = [];

    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
}

public enum BookingStatus
{
    Pending,
    Approved,
    Rejected,
    Withdrawn,
    Cancelled,
}

public class SkippedDate
{
    public int Id { get; set; }
    public int BookingId { get; set; }

    // UTC start of the skipped Occurrence.
    public DateTime OccurrenceStartUtc { get; set; }
}
