using System.Globalization;
using System.Text.Json;

namespace FairfaxStat.Core.MatchAnalysis.Metadata;

internal static class JsonFields
{
    internal static JsonElement Field(this JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) ? value : default;

    internal static int? NonNegativeInt(this JsonElement element) =>
        element.ValueKind == JsonValueKind.Number && element.TryGetInt32(out var number) && number >= 0 ? number : null;

    internal static double? NonNegativeNumber(this JsonElement element) =>
        element.ValueKind == JsonValueKind.Number && element.TryGetDouble(out var number) &&
        double.IsFinite(number) && number >= 0 ? number : null;

    internal static string? Id(this JsonElement element)
    {
        var text = element.ValueKind switch
        {
            JsonValueKind.Number => element.GetRawText(),
            JsonValueKind.String => element.GetString(),
            _ => null
        };
        if (text is null || (text.Length > 1 && text[0] == '0') || !MatchId.TryParse(text, out var value))
            return null;
        return value.ToString(CultureInfo.InvariantCulture);
    }
}
