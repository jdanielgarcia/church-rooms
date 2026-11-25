using RoomBooking.Data;

namespace RoomBooking.Scheduling;

public record Occurrence(Booking Booking, DateTime StartUtc, DateTime EndUtc);

public record Clash(Occurrence Candidate, Occurrence Existing);

// Pure scheduling rules. No DB, no clock: everything passed in, so it's easy to test.
public static class Schedule
{
    private static readonly TimeSpan Week = TimeSpan.FromDays(7);

    // Occurrences of a Booking that overlap [fromUtc, toUtc), in time order.
    public static IEnumerable<Occurrence> Expand(Booking b, DateTime fromUtc, DateTime toUtc)
    {
        if (!b.IsWeekly)
        {
            if (b.StartUtc < toUtc && b.EndUtc > fromUtc)
                yield return new(b, b.StartUtc, b.EndUtc);
            yield break;
        }

        var duration = b.EndUtc - b.StartUtc;
        var start = b.StartUtc;

        // Jump straight to the first week that could overlap the range instead of looping from the first Occurrence.
        if (fromUtc - duration > start)
            start = start.AddDays(7 * Math.Floor((fromUtc - duration - start) / Week));

        var skipped = b.SkippedDates.Select(s => s.OccurrenceStartUtc).ToHashSet();

        for (; start < toUtc && (b.UntilUtc is null || start < b.UntilUtc); start += Week)
        {
            var end = start + duration;
            if (end <= fromUtc || skipped.Contains(start))
                continue;
            yield return new(b, start, end);
        }
    }

    // ADR-0001: every pair of overlapping Occurrences between the candidate and the other Bookings.
    // Caller decides which Bookings count (same Room, Approved, not the candidate itself).
    public static List<Clash> FindClashes(Booking candidate, IEnumerable<Booking> others)
    {
        var clashes = new List<Clash>();

        foreach (var other in others)
        {
            var from = Max(candidate.StartUtc, other.StartUtc);
            var to = Min(LastEnd(candidate), LastEnd(other));

            // Both weekly and open-ended: the pattern repeats forever. Skipped dates are a finite set,
            // so two weeks past the last skipped date is guaranteed to hit any real clash.
            to ??= Max(from, Max(LatestSkip(candidate), LatestSkip(other))) + 2 * Week;

            if (to <= from)
                continue;

            var theirs = Expand(other, from, to.Value).ToList();
            foreach (var mine in Expand(candidate, from, to.Value))
            {
                // ponytail: pairwise compare, O(n*m). Fine for a few hundred Occurrences; sweep if it ever isn't.
                foreach (var t in theirs)
                {
                    if (mine.StartUtc < t.EndUtc && t.StartUtc < mine.EndUtc)
                        clashes.Add(new(mine, t));
                }
            }
        }

        return clashes;
    }

    // When the Booking's last Occurrence ends. Null = never (open-ended weekly).
    private static DateTime? LastEnd(Booking b) =>
        b.IsWeekly ? b.UntilUtc + (b.EndUtc - b.StartUtc) : b.EndUtc;

    private static DateTime LatestSkip(Booking b) =>
        b.SkippedDates.Count == 0 ? DateTime.MinValue : b.SkippedDates.Max(s => s.OccurrenceStartUtc);

    private static DateTime Max(DateTime a, DateTime b) => a > b ? a : b;

    private static DateTime? Min(DateTime? a, DateTime? b) =>
        a is null ? b : b is null ? a : a < b ? a : b;
}
