namespace SivayaanHMS.Core;

/// <summary>One stay as the bed-day calculation needs it.</summary>
public record StaySpan(
    string BedNo, string RoomTypeName, decimal DailyRent, decimal NursingPerDay, decimal GstPercent,
    DateTime From, DateTime? To);

/// <summary>One chargeable bed day and the stay it is charged at.</summary>
public record BedDay(DateTime Date, StaySpan Stay);

/// <summary>Consecutive days at the same room type and rates, as one bill line.</summary>
public record BedDayRun(DateTime FirstDate, DateTime LastDate, int Days, StaySpan Stay);

/// <summary>
/// How many bed days a stay comes to, and which bed each one is charged at.
/// This is where most of an IP bill's money is decided, so it is a pure function
/// with its own tests, and the bill never stores what it can work out from here.
///
/// The two counting rules (a clinic setting):
/// <list type="bullet">
/// <item><b>Midnight count</b> - one day for each midnight crossed; a same-day
/// admission and discharge is one day. Day <i>n</i> is the calendar date it starts
/// on.</item>
/// <item><b>24-hour blocks</b> - elapsed hours less a grace period, divided into
/// 24-hour blocks and rounded up, with at least one. Day <i>n</i> runs 24 hours
/// from the admission time.</item>
/// </list>
/// A day on which the patient was in more than one bed (a transfer) is charged once,
/// at the bed with the higher daily rent - the usual rule for an ICU step-down, and
/// one a family can follow without hour-by-hour pro-rating.
/// </summary>
public static class BedDays
{
    public static List<BedDay> Count(
        DateTime admittedOn, DateTime upTo, IReadOnlyList<StaySpan> stays, BedDayRule rule, int graceHours = 2)
    {
        if (stays.Count == 0) return [];
        if (upTo < admittedOn) upTo = admittedOn;

        var windows = rule == BedDayRule.TwentyFourHours
            ? TwentyFourHourWindows(admittedOn, upTo, graceHours)
            : MidnightWindows(admittedOn, upTo);

        var days = new List<BedDay>(windows.Count);

        foreach (var (start, end) in windows)
        {
            // Stays that overlap this day. The last day of a midnight count can lie
            // beyond the discharge (a same-day stay), so the window is clipped to the
            // stay itself rather than to the discharge time.
            var overlapping = stays
                .Where(s => s.From < end && (s.To ?? upTo) > start)
                .ToList();

            // A same-day admission and discharge: the only window is the admission
            // date, and the stay began and ended inside it.
            if (overlapping.Count == 0)
                overlapping = stays.Where(s => s.From <= end).OrderByDescending(s => s.From).Take(1).ToList();

            if (overlapping.Count == 0) continue;

            var charged = overlapping.OrderByDescending(s => s.DailyRent).ThenBy(s => s.From).First();
            days.Add(new BedDay(start.Date, charged));
        }

        return days;
    }

    /// <summary>Groups consecutive days charged at the same stay into one line each.</summary>
    public static List<BedDayRun> Runs(IReadOnlyList<BedDay> days)
    {
        var runs = new List<BedDayRun>();

        foreach (var day in days)
        {
            if (runs.Count > 0 && SameRate(runs[^1].Stay, day.Stay))
            {
                var last = runs[^1];
                runs[^1] = last with { LastDate = day.Date, Days = last.Days + 1 };
            }
            else
            {
                runs.Add(new BedDayRun(day.Date, day.Date, 1, day.Stay));
            }
        }

        return runs;
    }

    private static bool SameRate(StaySpan a, StaySpan b)
        => a.RoomTypeName == b.RoomTypeName && a.DailyRent == b.DailyRent
           && a.NursingPerDay == b.NursingPerDay && a.GstPercent == b.GstPercent;

    private static List<(DateTime Start, DateTime End)> MidnightWindows(DateTime admittedOn, DateTime upTo)
    {
        var first = admittedOn.Date;
        var count = Math.Max(1, (upTo.Date - first).Days);

        return Enumerable.Range(0, count)
            .Select(i => (Start: i == 0 ? admittedOn : first.AddDays(i), End: first.AddDays(i + 1)))
            .ToList();
    }

    private static List<(DateTime Start, DateTime End)> TwentyFourHourWindows(DateTime admittedOn, DateTime upTo, int graceHours)
    {
        var chargeable = (upTo - admittedOn).TotalHours - Math.Max(0, graceHours);
        var count = Math.Max(1, (int)Math.Ceiling(chargeable / 24.0));

        return Enumerable.Range(0, count)
            .Select(i => (Start: admittedOn.AddHours(24 * i), End: admittedOn.AddHours(24 * (i + 1))))
            .ToList();
    }
}
