using System.Globalization;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace HackAcad.Web.Services;

// One request-scoped display zone. Database timestamps and security deadlines stay UTC.
public sealed class UserTimeZone
{
    public const string DefaultId = "Asia/Kuala_Lumpur";
    private static readonly IReadOnlyDictionary<string, TimeZoneInfo> Zones = BuildZones();
    public string Id { get; private set; } = DefaultId;
    public TimeZoneInfo Zone => Zones[Id];

    private static IReadOnlyDictionary<string, TimeZoneInfo> BuildZones()
    {
        var zones = new Dictionary<string, TimeZoneInfo>(StringComparer.Ordinal);
        foreach (var zone in TimeZoneInfo.GetSystemTimeZones())
        {
            var id = zone.Id;
            if (!zone.HasIanaId && !TimeZoneInfo.TryConvertWindowsIdToIanaId(id, out id)) continue;
            zones[id] = zone;
        }
        zones[DefaultId] = TimeZoneInfo.FindSystemTimeZoneById(DefaultId);
        zones["UTC"] = TimeZoneInfo.Utc;
        return zones;
    }

    public static bool IsSupported(string? id) => id is not null && Zones.ContainsKey(id);
    public void Use(string? id) => Id = IsSupported(id) ? id! : DefaultId;
    public DateTimeOffset Convert(DateTime utc) => TimeZoneInfo.ConvertTime(
        new DateTimeOffset(DateTime.SpecifyKind(utc, DateTimeKind.Utc)), Zone);
    public string Format(DateTime utc, string format = "dd MMM yyyy, HH:mm:ss") =>
        Convert(utc).ToString(format, CultureInfo.InvariantCulture);

    public static IEnumerable<SelectListItem> Options(DateTimeOffset now) => Zones
        .OrderBy(pair => pair.Value.GetUtcOffset(now)).ThenBy(pair => pair.Key, StringComparer.Ordinal)
        .Select(pair => {
            var offset = pair.Value.GetUtcOffset(now);
            var sign = offset < TimeSpan.Zero ? "−" : "+";
            return new SelectListItem($"(UTC{sign}{offset.Duration().Hours:00}:{offset.Duration().Minutes:00}) {pair.Key.Replace('_', ' ')}", pair.Key);
        });

    // A date filter means a whole local calendar day, including 23/25-hour DST days.
    public DateTime StartOfDayUtc(DateOnly date)
    {
        var local = date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        // Some regions advance their clocks at midnight or skip a whole calendar day.
        while (Zone.IsInvalidTime(local)) local = local.AddMinutes(1);
        if (Zone.IsAmbiguousTime(local))
            return new DateTimeOffset(local, Zone.GetAmbiguousTimeOffsets(local).Max()).UtcDateTime;
        return TimeZoneInfo.ConvertTimeToUtc(local, Zone);
    }
}
