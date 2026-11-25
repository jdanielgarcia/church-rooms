using Microsoft.Extensions.Configuration;
using RoomBooking.Bookings;
using RoomBooking.Data;
using RoomBooking.Scheduling;

namespace RoomBooking.Tests;

// The two rules that can silently corrupt the schedule: weekly expansion and the ADR-0001 clash check.
public class ScheduleTests
{
    // Tuesday 2026-12-01 11:00 UTC = 19:00 Manila.
    private static readonly DateTime Tue = new(2026, 12, 1, 11, 0, 0, DateTimeKind.Utc);

    private static Booking OneOff(DateTime start, double hours = 2) =>
        new() { StartUtc = start, EndUtc = start.AddHours(hours) };

    private static Booking Weekly(DateTime start, double hours = 2, DateTime? until = null, params DateTime[] skips) =>
        new()
        {
            StartUtc = start,
            EndUtc = start.AddHours(hours),
            IsWeekly = true,
            UntilUtc = until,
            SkippedDates = skips.Select(s => new SkippedDate { OccurrenceStartUtc = s }).ToList(),
        };

    private static bool Clashes(Booking candidate, params Booking[] others) =>
        Schedule.FindClashes(candidate, others).Count > 0;

    [Fact]
    public void Overlapping_one_offs_clash()
    {
        Assert.True(Clashes(OneOff(Tue.AddHours(1)), OneOff(Tue)));
    }

    [Fact]
    public void Back_to_back_one_offs_do_not_clash()
    {
        Assert.False(Clashes(OneOff(Tue.AddHours(2)), OneOff(Tue)));
    }

    [Fact]
    public void One_off_on_a_weekly_slot_clashes_only_on_that_weekday()
    {
        var weekly = Weekly(Tue);
        Assert.True(Clashes(OneOff(Tue.AddDays(14).AddMinutes(30)), weekly));
        Assert.False(Clashes(OneOff(Tue.AddDays(15)), weekly));
    }

    [Fact]
    public void Skipped_date_frees_the_slot()
    {
        var weekly = Weekly(Tue, skips: Tue.AddDays(21));
        Assert.False(Clashes(OneOff(Tue.AddDays(21)), weekly));
        Assert.True(Clashes(OneOff(Tue.AddDays(28)), weekly));
    }

    [Fact]
    public void Ended_weekly_does_not_clash_after_its_end()
    {
        var weekly = Weekly(Tue, until: Tue.AddDays(14)); // Dec 1 and Dec 8 only
        Assert.True(Clashes(OneOff(Tue.AddDays(7)), weekly));
        Assert.False(Clashes(OneOff(Tue.AddDays(14)), weekly));
    }

    [Fact]
    public void Open_ended_weeklies_clash_on_same_slot_but_not_other_days()
    {
        Assert.True(Clashes(Weekly(Tue.AddMinutes(90)), Weekly(Tue)));
        Assert.False(Clashes(Weekly(Tue.AddDays(1)), Weekly(Tue)));
    }

    [Fact]
    public void Weekly_starting_years_later_still_clashes_with_old_open_ended_weekly()
    {
        Assert.True(Clashes(Weekly(Tue.AddDays(7 * 150)), Weekly(Tue)));
    }

    [Fact]
    public void Open_ended_weeklies_clash_even_if_first_weeks_are_skipped()
    {
        var existing = Weekly(Tue, skips: [Tue.AddDays(7), Tue.AddDays(14), Tue.AddDays(21)]);
        Assert.True(Clashes(Weekly(Tue.AddDays(7)), existing));
    }

    [Fact]
    public void Weekly_after_existing_one_ends_does_not_clash()
    {
        var existing = Weekly(Tue, until: Tue.AddDays(28));
        Assert.False(Clashes(Weekly(Tue.AddDays(28)), existing));
    }

    [Fact]
    public void Expand_returns_weekly_occurrences_in_range_minus_skips_and_until()
    {
        var weekly = Weekly(Tue, until: Tue.AddDays(35), skips: Tue.AddDays(14));
        var starts = Schedule.Expand(weekly, Tue.AddDays(1), Tue.AddDays(100)).Select(o => o.StartUtc).ToList();
        Assert.Equal([Tue.AddDays(7), Tue.AddDays(21), Tue.AddDays(28)], starts);
    }

    [Fact]
    public void Expand_includes_occurrence_already_in_progress()
    {
        var weekly = Weekly(Tue);
        var inProgress = Tue.AddDays(70).AddHours(1);
        Assert.Single(Schedule.Expand(weekly, inProgress, inProgress.AddMinutes(1)));
    }

    [Fact]
    public void Early_morning_Manila_weekly_stays_on_its_local_weekday()
    {
        // ADR-0004: Tue 07:00 Manila is Mon 23:00 UTC; local weekday must stay Tuesday.
        var clock = new ChurchClock(new ConfigurationBuilder().Build());
        var form = new BookingForm
        {
            RoomId = 1,
            GroupName = "Prayer",
            Date = new DateOnly(2026, 12, 1),
            Start = new TimeOnly(7, 0),
            End = new TimeOnly(8, 0),
            IsWeekly = true,
            LastDate = new DateOnly(2026, 12, 15), // inclusive
        };
        var b = new Booking();
        form.ApplyTo(b, clock);

        Assert.Equal(DayOfWeek.Monday, b.StartUtc.DayOfWeek);
        var local = Schedule.Expand(b, b.StartUtc, b.StartUtc.AddDays(365))
            .Select(o => clock.LocalDate(o.StartUtc)).ToList();
        Assert.Equal([new DateOnly(2026, 12, 1), new DateOnly(2026, 12, 8), new DateOnly(2026, 12, 15)], local);
    }
}
