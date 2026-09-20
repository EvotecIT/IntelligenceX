using System;
using System.Linq;

namespace IntelligenceX.Telemetry.Limits;

/// <summary>Converts an explicitly entered local expiry date to its last valid instant.</summary>
public static class BankedResetExpiry {
    /// <summary>
    /// Resolves the final tick of the date in the supplied zone. For a repeated local time,
    /// uses its last occurrence; for a skipped end of day, locates the last valid tick.
    /// A date skipped in its entirety is rejected instead of inventing an expiry on another date.
    /// </summary>
    public static DateTimeOffset ResolveLocalDateEnd(DateTime date, TimeZoneInfo timeZone) {
        if (timeZone is null) throw new ArgumentNullException(nameof(timeZone));
        var day = DateTime.SpecifyKind(date.Date, DateTimeKind.Unspecified);
        var end = day.AddTicks(TimeSpan.TicksPerDay - 1);
        while (timeZone.IsInvalidTime(end)) {
            if (end.Ticks - day.Ticks < TimeSpan.TicksPerMinute) {
                throw new ArgumentException("The selected local date does not exist in this time zone.", nameof(date));
            }
            end = end.AddMinutes(-1);
        }
        // Transition times can include seconds or milliseconds in custom zones.
        if (end.Date == day && end.Ticks < day.Ticks + TimeSpan.TicksPerDay - 1) {
            var invalid = end.AddMinutes(1).Ticks;
            var valid = end.Ticks;
            while (invalid - valid > 1) {
                var middle = valid + (invalid - valid) / 2;
                if (timeZone.IsInvalidTime(new DateTime(middle, DateTimeKind.Unspecified))) invalid = middle;
                else valid = middle;
            }
            end = new DateTime(valid, DateTimeKind.Unspecified);
        }
        var offset = timeZone.IsAmbiguousTime(end)
            ? timeZone.GetAmbiguousTimeOffsets(end).Min()
            : timeZone.GetUtcOffset(end);
        return new DateTimeOffset(end, offset);
    }
}
