using System.Text.Json;
using FairfaxStat.Core.MatchAnalysis.Metadata;

namespace FairfaxStat.Core.Tests.MatchAnalysis;

public sealed class MatchIdTests
{
    [Theory]
    [InlineData("0")]
    [InlineData("-2")]
    [InlineData("1e2")]
    [InlineData("１")]
    [InlineData("18446744073709551616")]
    public void Invalid_user_id_reports_the_exact_field(string input)
    {
        var error = MatchId.NormalizeParameters(JsonSerializer.SerializeToElement(new { match_id = input })).Error;
        Assert.Equal("VALIDATION_ERROR", error.Code);
        Assert.Equal("/params/match_id", Assert.Single(error.Details).Field);
    }

    [Theory]
    [InlineData("  00042\t", "42")]
    [InlineData("18446744073709551615", "18446744073709551615")]
    [InlineData("9007199254740993", "9007199254740993")]
    public void Decimal_ids_are_normalized_without_floating_point(string input, string expected)
    {
        var result = MatchId.NormalizeParameters(JsonSerializer.SerializeToElement(new { match_id = input }));
        Assert.Equal(expected, result.Value.GetProperty("match_id").GetString());
    }
}
