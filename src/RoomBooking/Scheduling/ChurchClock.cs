using System.Globalization;
using RoomBooking.Data;

namespace RoomBooking.Scheduling;

// ADR-0004: store UTC, show church-local time. All conversions go through here.
public class ChurchClock(IConfiguration config)
{
    private static readonly CultureInfo Culture = CultureInfo.GetCultureInfo("en-PH");

    public TimeZoneInfo Zone { get; } =
        TimeZoneInfo.FindSystemTimeZoneById(config["Church:TimeZone"] ?? "Asia/Manila");

    public DateTime ToLocal(DateTime utc) => TimeZoneInfo.ConvertTimeFromUtc(utc, Zone);

    public DateTime ToUtc(DateOnly date, TimeOnly time) =>
        TimeZoneInfo.ConvertTimeToUtc(date.ToDateTime(time), Zone);

    public DateOnly Today => DateOnly.FromDateTime(ToLocal(DateTime.UtcNow));

    public DateOnly LocalDate(DateTime utc) => DateOnly.FromDateTime(ToLocal(utc));

    public TimeOnly LocalTime(DateTime utc) => TimeOnly.FromDateTime(ToLocal(utc));

    // UTC range covering one local calendar day.
    public (DateTime FromUtc, DateTime ToUtc) Day(DateOnly date) =>
        (ToUtc(date, TimeOnly.MinValue), ToUtc(date.AddDays(1), TimeOnly.MinValue));

    public string Time(DateTime utc) => ToLocal(utc).ToString("h:mm tt", Culture);

    public string Date(DateTime utc) => ToLocal(utc).ToString("ddd, MMM d", Culture);

    public string Date(DateOnly date) => date.ToString("ddd, MMM d, yyyy", Culture);

    public string Span(Occurrence o) => $"{Date(o.StartUtc)} · {Time(o.StartUtc)}–{Time(o.EndUtc)}";

    // "Every Tuesday · 7:00 PM–9:00 PM · from Dec 2" / "Tue, Dec 9 · 7:00 PM–9:00 PM"
    public string Describe(Booking b)
    {
        var times = $"{Time(b.StartUtc)}–{Time(b.EndUtc)}";
        if (!b.IsWeekly)
            return $"{Date(b.StartUtc)} · {times}";

        var day = ToLocal(b.StartUtc).ToString("dddd", Culture);
        var text = $"Every {day} · {times} · from {Date(b.StartUtc)}";
        if (b.UntilUtc is { } until)
            text += $" to {Date(LocalDate(until).AddDays(-1))}";
        return text;
    }
}
