using Microsoft.EntityFrameworkCore;
using RoomBooking.Data;
using RoomBooking.Scheduling;

namespace RoomBooking.Bookings;

public record BookingResult(string? Error = null, List<Clash>? Clashes = null)
{
    public bool Ok => Error is null;
    public static readonly BookingResult Success = new();
    public static BookingResult Fail(string error) => new(error);
}

// Every change to a Booking goes through here, so the rules in CONTEXT.md / ADR-0001 live in one place.
// Registered as scoped: one instance per HTTP request, sharing that request's DbContext.
public class BookingService(ApplicationDbContext db, ChurchClock clock)
{
    // ---------- Reading ----------

    // Approved Occurrences in [fromUtc, toUtc), optionally for one Room, in time order.
    public async Task<List<Occurrence>> OccurrencesAsync(DateTime fromUtc, DateTime toUtc, int? roomId = null)
    {
        var bookings = await db.Bookings
            .AsNoTracking()
            .Include(b => b.Room)
            .Include(b => b.Owner)
            .Include(b => b.SkippedDates)
            .Where(b => b.Status == BookingStatus.Approved && b.StartUtc < toUtc)
            .Where(b => b.IsWeekly ? b.UntilUtc == null || b.UntilUtc > fromUtc : b.EndUtc > fromUtc)
            .Where(b => roomId == null || b.RoomId == roomId)
            .ToListAsync();

        return bookings
            .SelectMany(b => Schedule.Expand(b, fromUtc, toUtc))
            .OrderBy(o => o.StartUtc)
            .ToList();
    }

    // Approved Bookings that ADR-0001 says the candidate must not overlap.
    public async Task<List<Clash>> ClashesAsync(Booking candidate) =>
        Schedule.FindClashes(candidate, await ApprovedRivalsAsync(candidate));

    // Pending Requests in the same Room that overlap. Informational only: pending isn't the schedule.
    public async Task<List<Clash>> PendingOverlapsAsync(Booking candidate)
    {
        var pending = await db.Bookings
            .AsNoTracking()
            .Include(b => b.Owner)
            .Include(b => b.SkippedDates)
            .Where(b => b.RoomId == candidate.RoomId && b.Status == BookingStatus.Pending && b.Id != candidate.Id)
            .ToListAsync();
        return Schedule.FindClashes(candidate, pending);
    }

    private Task<List<Booking>> ApprovedRivalsAsync(Booking candidate)
    {
        // A Change request may overlap the Booking it replaces: that one ends when this is approved.
        var replaces = candidate.ReplacesBookingId ?? 0;
        return db.Bookings
            .AsNoTracking()
            .Include(b => b.Owner)
            .Include(b => b.SkippedDates)
            .Where(b => b.RoomId == candidate.RoomId
                        && b.Status == BookingStatus.Approved
                        && b.Id != candidate.Id
                        && b.Id != replaces)
            .Where(b => b.IsWeekly ? b.UntilUtc == null || b.UntilUtc > candidate.StartUtc : b.EndUtc > candidate.StartUtc)
            .ToListAsync();
    }

    // ---------- Leader or Admin ----------

    // New Booking or Change request. Leaders' go in as Pending; Admins' are approved immediately.
    public async Task<BookingResult> SubmitAsync(Booking booking, bool byAdmin)
    {
        if (booking.StartUtc < DateTime.UtcNow)
            return BookingResult.Fail("That time has already passed.");

        if (!await db.Rooms.AnyAsync(r => r.Id == booking.RoomId && r.IsActive))
            return BookingResult.Fail("That room isn't available for booking.");

        if (booking.ReplacesBookingId is int oldId
            && !await db.Bookings.AnyAsync(b => b.Id == oldId && b.OwnerId == booking.OwnerId && b.Status == BookingStatus.Approved))
            return BookingResult.Fail("The booking you're changing is no longer active.");

        db.Bookings.Add(booking);

        if (byAdmin)
            return await ApproveTrackedAsync(booking);

        var clashes = await ClashesAsync(booking);
        if (clashes.Count > 0)
            return new("That time is already taken.", clashes);

        booking.Status = BookingStatus.Pending;
        await db.SaveChangesAsync();
        return BookingResult.Success;
    }

    public async Task<BookingResult> WithdrawAsync(int id, string userId)
    {
        var b = await db.Bookings.FindAsync(id);
        if (b is null || b.OwnerId != userId || b.Status != BookingStatus.Pending)
            return BookingResult.Fail("Only your own pending requests can be withdrawn.");

        b.Status = BookingStatus.Withdrawn;
        await db.SaveChangesAsync();
        return BookingResult.Success;
    }

    // Frees space, so no approval and no conflict check needed.
    public async Task<BookingResult> CancelAsync(int id, string userId, bool byAdmin)
    {
        var b = await db.Bookings.FindAsync(id);
        if (b is null || b.Status != BookingStatus.Approved || !(byAdmin || b.OwnerId == userId))
            return BookingResult.Fail("Booking not found.");
        if (!byAdmin && b.IsWeekly)
            return BookingResult.Fail("For a weekly booking, set an end date instead.");

        b.Status = BookingStatus.Cancelled;
        await db.SaveChangesAsync();
        return BookingResult.Success;
    }

    // Weekly: no more Occurrences from this local date on. Frees space.
    public async Task<BookingResult> EndWeeklyAsync(int id, string userId, bool byAdmin, DateOnly fromDate)
    {
        var b = await db.Bookings.FindAsync(id);
        if (b is null || !b.IsWeekly || b.Status != BookingStatus.Approved || !(byAdmin || b.OwnerId == userId))
            return BookingResult.Fail("Booking not found.");
        if (fromDate < clock.Today)
            return BookingResult.Fail("Pick today or a later date.");

        var cut = clock.ToUtc(fromDate, TimeOnly.MinValue);
        if (b.UntilUtc is { } until && cut >= until)
            return BookingResult.Fail("This booking already ends before that date.");

        EndFrom(b, cut);
        await db.SaveChangesAsync();
        return BookingResult.Success;
    }

    // Weekly: skip one Occurrence. Frees space.
    public async Task<BookingResult> SkipAsync(int id, string userId, bool byAdmin, DateOnly date)
    {
        var b = await db.Bookings.Include(x => x.SkippedDates).FirstOrDefaultAsync(x => x.Id == id);
        if (b is null || !b.IsWeekly || b.Status != BookingStatus.Approved || !(byAdmin || b.OwnerId == userId))
            return BookingResult.Fail("Booking not found.");
        if (date < clock.Today)
            return BookingResult.Fail("Pick today or a later date.");

        var start = clock.ToUtc(date, clock.LocalTime(b.StartUtc));
        var isOccurrence = Schedule.Expand(b, start, start.AddMinutes(1)).Any(o => o.StartUtc == start);
        if (!isOccurrence)
            return BookingResult.Fail($"{b.GroupName} doesn't meet on {clock.Date(date)}.");

        b.SkippedDates.Add(new SkippedDate { OccurrenceStartUtc = start });
        await db.SaveChangesAsync();
        return BookingResult.Success;
    }

    // ---------- Admin ----------

    public async Task<BookingResult> ApproveAsync(int id)
    {
        var b = await db.Bookings.Include(x => x.SkippedDates).FirstOrDefaultAsync(x => x.Id == id);
        if (b is null || b.Status != BookingStatus.Pending)
            return BookingResult.Fail("Only pending requests can be approved.");
        return await ApproveTrackedAsync(b);
    }

    public async Task<BookingResult> RejectAsync(int id, string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
            return BookingResult.Fail("Give the Leader a reason.");

        var b = await db.Bookings.FindAsync(id);
        if (b is null || b.Status != BookingStatus.Pending)
            return BookingResult.Fail("Only pending requests can be rejected.");

        b.Status = BookingStatus.Rejected;
        b.RejectReason = reason.Trim();
        await db.SaveChangesAsync();
        return BookingResult.Success;
    }

    // Admin edits a Pending or Approved Booking. Approved ones must stay conflict-free (ADR-0001);
    // pending ones can optionally be approved in the same step.
    public async Task<BookingResult> AdminSaveAsync(int id, BookingForm form, bool approve)
    {
        var b = await db.Bookings.Include(x => x.Room).Include(x => x.SkippedDates).FirstOrDefaultAsync(x => x.Id == id);
        if (b is null || b.Status is not (BookingStatus.Pending or BookingStatus.Approved))
            return BookingResult.Fail("Only pending or approved bookings can be edited.");
        if (!await db.Rooms.AnyAsync(r => r.Id == form.RoomId))
            return BookingResult.Fail("Room not found.");

        var before = $"{b.Room.Name}, {clock.Describe(b)}";
        form.ApplyTo(b, clock);
        var roomName = await db.Rooms.Where(r => r.Id == b.RoomId).Select(r => r.Name).SingleAsync();
        var after = $"{roomName}, {clock.Describe(b)}";
        if (before != after)
            b.AdminNote = $"Changed by Admin. Was: {before}";

        if (b.Status == BookingStatus.Approved || approve)
            return await ApproveTrackedAsync(b);

        await db.SaveChangesAsync();
        return BookingResult.Success;
    }

    // The only path that puts Occurrences on the schedule. Locks the Room row so two Admins approving
    // clashing Bookings at the same moment are serialized (ADR-0003), then re-checks ADR-0001.
    private async Task<BookingResult> ApproveTrackedAsync(Booking b)
    {
        await using var tx = await db.Database.BeginTransactionAsync();
        await db.Database.ExecuteSqlAsync($"""SELECT 1 FROM "Rooms" WHERE "Id" = {b.RoomId} FOR UPDATE""");

        var clashes = await ClashesAsync(b);
        if (clashes.Count > 0)
            return new("Clashes with the approved schedule. Adjust this booking or the other one first.", clashes);

        b.Status = BookingStatus.Approved;

        if (b.ReplacesBookingId is int oldId && await db.Bookings.FindAsync(oldId) is { Status: BookingStatus.Approved } old)
        {
            // Old Booking ends where the new one takes over: from the new first Occurrence's local date.
            EndFrom(old, clock.ToUtc(clock.LocalDate(b.StartUtc), TimeOnly.MinValue));
            old.AdminNote = "Replaced by your approved change request.";
        }

        await db.SaveChangesAsync();
        await tx.CommitAsync();
        return BookingResult.Success;
    }

    private static void EndFrom(Booking b, DateTime cutUtc)
    {
        if (!b.IsWeekly || cutUtc <= b.StartUtc)
            b.Status = BookingStatus.Cancelled;
        else
            b.UntilUtc = b.UntilUtc is { } u && u < cutUtc ? u : cutUtc;
    }
}
