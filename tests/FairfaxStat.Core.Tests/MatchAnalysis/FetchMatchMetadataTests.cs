using System.Text;
using FairfaxStat.Core.Execution;
using FairfaxStat.Core.MatchAnalysis.Metadata;
using FairfaxStat.Core.Results;

namespace FairfaxStat.Core.Tests.MatchAnalysis;

public sealed class FetchMatchMetadataTests
{
    private const string Valid = """{"match_info":{"match_id":9007199254740993,"duration_s":12,"players":[{"player_slot":1,"account_id":0,"hero_id":76,"team":0}]}}""";
    private const string Id = "9007199254740993";
    private static readonly ToolContext Context = new(Guid.NewGuid(), 1, [FetchMatchMetadataTool.Key],
        DateTimeOffset.UtcNow.AddMinutes(1), TimeProvider.System);

    private sealed class Source(string payload) : IMatchMetadataSource
    {
        public int Calls { get; private set; }
        public ValueTask<Result<ReadOnlyMemory<byte>>> ReadAsync(string matchId, CancellationToken cancellation)
        {
            Calls++;
            return ValueTask.FromResult(Result<ReadOnlyMemory<byte>>.Ok(Encoding.UTF8.GetBytes(payload)));
        }
    }

    [Fact]
    public async Task Minimal_snapshot_keeps_exact_ids_and_performs_one_read()
    {
        var source = new Source(Valid);
        var result = await new FetchMatchMetadataTool(source).RunAsync(new(Id), Context, TestContext.Current.CancellationToken);
        Assert.Equal(Id, result.Value.MatchId);
        Assert.Equal("0", Assert.Single(result.Value.Players).AccountId);
        Assert.Equal(1, source.Calls);
    }

    [Theory]
    [InlineData("9007199254740993", "9007199254740992", "/match_info/match_id")]
    [InlineData("9007199254740993", "9e15", "/match_info/match_id")]
    [InlineData("\"duration_s\":12", "\"duration_s\":0", "/match_info/duration_s")]
    [InlineData("\"hero_id\":76", "\"hero_id\":7,\"hero_id\":6", "body")]
    public async Task Invalid_source_is_distinct_from_invalid_job_input(string before, string after, string field)
    {
        var tool = new FetchMatchMetadataTool(new Source(Valid.Replace(before, after)));
        var result = await tool.RunAsync(new(Id), Context, TestContext.Current.CancellationToken);
        Assert.Equal("SOURCE_DATA_INVALID", result.Error.Code);
        Assert.Equal(field, Assert.Single(result.Error.Details).Field);
    }

    [Fact]
    public async Task Wrong_attempt_context_is_rejected_before_reading()
    {
        var source = new Source(Valid);
        var tool = new FetchMatchMetadataTool(source);
        var result = await tool.RunAsync(new(Id), Context with { Attempt = 0 }, TestContext.Current.CancellationToken);
        Assert.Equal("TOOL_INPUT_INVALID", result.Error.Code);
        Assert.Equal("/attempt", Assert.Single(result.Error.Details).Field);
        Assert.Equal(0, source.Calls);
    }

    [Fact]
    public async Task Invalid_internal_input_never_calls_the_source()
    {
        var source = new Source(Valid);
        var result = await new FetchMatchMetadataTool(source).RunAsync(new("007"), Context, TestContext.Current.CancellationToken);
        Assert.Equal("TOOL_INPUT_INVALID", result.Error.Code);
        Assert.Equal(0, source.Calls);
    }
}
