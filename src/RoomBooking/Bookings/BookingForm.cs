using System.ComponentModel.DataAnnotations;
using RoomBooking.Data;
using RoomBooking.Scheduling;

namespace RoomBooking.Bookings;

// What a person types into the booking form, in church-local time.
// Converted to/from the UTC fields on Booking at the edge (ADR-0004).
public class BookingForm : IValidatableObject
{
    [Range(1, int.MaxValue, ErrorMessage = "Pick a room.")]
    public int RoomId { get; set; }

    [Required, MaxLength(100)]
    [Display(Name = "Group name")]
    public string GroupName { get; set; } = "";

    [Display(Name = "Date")]
    public DateOnly Date { get; set; }

    [Display(Name = "Start time")]
    public TimeOnly Start { get; set; } = new(19, 0);

    [Display(Name = "End time")]
    public TimeOnly End { get; set; } = new(21, 0);

    public bool IsWeekly { get; set; }

    // Weekly only. Last date the group meets (inclusive). Empty = no end date.
    [Display(Name = "Last meeting date")]
    public DateOnly? LastDate { get; set; }

    // Admin only.
    public string? OwnerId { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext context)
    {
        // Start and End share one Date, so overnight Bookings are impossible by construction.
        if (End <= Start)
            yield return new("End time must be after start time.", [nameof(End)]);

        if (Start.Minute % 15 != 0 || End.Minute % 15 != 0)
            yield return new("Use 15-minute steps (e.g. 7:00, 7:15, 7:30).", [nameof(Start)]);

        if (IsWeekly && LastDate is { } last && last < Date)
            yield return new("Last meeting date can't be before the first date.", [nameof(LastDate)]);
    }

    public void ApplyTo(Booking b, ChurchClock clock)
    {
        b.RoomId = RoomId;
        b.GroupName = GroupName.Trim();
        b.StartUtc = clock.ToUtc(Date, Start);
        b.EndUtc = clock.ToUtc(Date, End);
        b.IsWeekly = IsWeekly;
        // Stored exclusive: midnight after the last meeting date.
        b.UntilUtc = IsWeekly && LastDate is { } last ? clock.ToUtc(last.AddDays(1), TimeOnly.MinValue) : null;
        if (OwnerId is not null)
            b.OwnerId = OwnerId;
    }

    public static BookingForm From(Booking b, ChurchClock clock) => new()
    {
        RoomId = b.RoomId,
        GroupName = b.GroupName,
        Date = clock.LocalDate(b.StartUtc),
        Start = clock.LocalTime(b.StartUtc),
        End = clock.LocalTime(b.EndUtc),
        IsWeekly = b.IsWeekly,
        LastDate = b.UntilUtc is { } u ? clock.LocalDate(u).AddDays(-1) : null,
        OwnerId = b.OwnerId,
    };
}
