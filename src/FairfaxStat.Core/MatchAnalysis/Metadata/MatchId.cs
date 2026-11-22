using System.Globalization;
using System.Text.Json;
using FairfaxStat.Core.Results;

namespace FairfaxStat.Core.MatchAnalysis.Metadata;

public static class MatchId
{
    public static Result<JsonElement> NormalizeParameters(JsonElement parameters)
    {
        const string path = "/params/match_id";
        if (parameters.ValueKind != JsonValueKind.Object)
            return Invalid("/params", "expected_object");
        string? raw = null;
        bool found = false;
        foreach (var property in parameters.EnumerateObject())
        {
            if (property.Name != "match_id")
                return Invalid("/params/" + property.Name.Replace("~", "~0").Replace("/", "~1"), "unexpected");
            if (found || property.Value.ValueKind != JsonValueKind.String)
                return Invalid(path, found ? "duplicate" : "expected_string");
            found = true;
            raw = property.Value.GetString();
        }
        if (raw is null || raw.Length > 64)
            return Invalid(path, raw is null ? "missing" : "too_long");
        var digits = raw.Trim(' ', '\t', '\r', '\n');
        if (!TryParse(digits, out var id) || id == 0)
            return Invalid(path, "invalid_uint64");
        return Result<JsonElement>.Ok(JsonSerializer.SerializeToElement(new { match_id = id.ToString(CultureInfo.InvariantCulture) }));
    }

    internal static bool TryParse(string text, out ulong id)
    {
        id = 0;
        return text.Length > 0 && text.All(char.IsAsciiDigit) &&
            ulong.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out id);
    }

    internal static bool IsCanonical(string text) =>
        TryParse(text, out var id) && id > 0 && text[0] != '0';

    private static Result<JsonElement> Invalid(string path, string reason) =>
        Result<JsonElement>.Fail(Error.Field("VALIDATION_ERROR", "body", path, reason));
}
