using System.Text.Json;
using FairfaxStat.Core.Execution;
using FairfaxStat.Core.Jobs;
using FairfaxStat.Core.MatchAnalysis;
using FairfaxStat.Core.MatchAnalysis.Catalog;
using FairfaxStat.Core.MatchAnalysis.Metadata;
using FairfaxStat.Core.Results;

namespace FairfaxStat.Core.Tests.MatchAnalysis;

public sealed class AnalyzeMatchTests
{
    private const string Id = "132598836";
    private static readonly byte[] Fixture = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "match-132598836.json"));
    private readonly InMemoryJobStore store = new(TimeProvider.System);

    private Dispatcher Create(IMatchMetadataSource source, ICatalogProvider? catalog = null)
    {
        var registry = new ToolRegistry();
        AnalyzeMatchOperation.Register(registry, source, catalog);
        return new Dispatcher(registry, store);
    }

    [Fact]
    public async Task From_user_request_to_completed_match_report()
    {
        var dispatcher = Create(new InMemoryMetadataSource(new Dictionary<string, byte[]> { [Id] = Fixture }));
        var job = dispatcher.Submit("analyze_match", JsonSerializer.SerializeToElement(new { match_id = " 0132598836 " })).Value;
        Assert.Equal(JobState.Queued, job.State);
        Assert.Equal(2, job.Versions.Length);
        var done = (await dispatcher.RunAsync(job.Id)).Value;
        Assert.Equal(JobState.Succeeded, done.State);
        var report = store.Get(job.Id).Value.Artifacts!.Report;
        Assert.Equal(Id, report.GetProperty("match_id").GetString());
        Assert.Equal(job.Id, report.GetProperty("job_id").GetGuid());
        Assert.Equal(2535, report.GetProperty("duration_s").GetDouble());
        var player = report.GetProperty("players")[0];
        Assert.Equal("Graves", player.GetProperty("hero_name").GetString());
        Assert.Equal(2, player.GetProperty("player_slot").GetInt32());
        var sections = report.GetProperty("sections");
        Assert.Equal(6, sections.EnumerateObject().Count());
        var death = sections.GetProperty("deaths").GetProperty("data").EnumerateArray()
            .Single(row => row.GetProperty("victim_slot").GetInt32() == 2);
        Assert.Equal(925, death.GetProperty("time_s").GetDouble());
        Assert.Equal(11, death.GetProperty("killer_slot").GetInt32());
        Assert.Equal("unsupported", sections.GetProperty("trajectories").GetProperty("availability").GetString());
        Assert.Equal(2, done.Artifacts!.Snapshot.GetProperty("match_info").GetProperty("players").GetArrayLength());
    }

    [Fact]
    public async Task Wrong_match_stops_before_catalog_or_report()
    {
        var catalog = new CountingCatalog();
        var dispatcher = Create(new InMemoryMetadataSource(new Dictionary<string, byte[]> { ["1"] = Fixture }), catalog);
        var job = dispatcher.Submit("analyze_match", JsonSerializer.SerializeToElement(new { match_id = "1" })).Value;
        var failed = (await dispatcher.RunAsync(job.Id)).Value;
        Assert.Equal(JobState.Failed, failed.State);
        Assert.Equal("SOURCE_DATA_INVALID", failed.Error!.Code);
        Assert.Equal(0, catalog.Calls);
        Assert.Null(failed.Artifacts);
    }

    [Theory]
    [InlineData("fetch_match_metadata", "1", "UNKNOWN_TOOL", "/operation")]
    [InlineData("absent", "1", "UNKNOWN_TOOL", "/operation")]
    [InlineData("analyze_match", "0", "VALIDATION_ERROR", "/params/match_id")]
    public void Rejected_submission_does_not_start_a_tool(string operation, string id, string code, string path)
    {
        var source = new CountingSource();
        var dispatcher = Create(source);
        var result = dispatcher.Submit(operation, JsonSerializer.SerializeToElement(new { match_id = id }));
        Assert.Equal(code, result.Error.Code);
        Assert.Equal(path, Assert.Single(result.Error.Details).Field);
        Assert.Equal(0, source.Calls);
    }

    private sealed class CountingCatalog : ICatalogProvider
    {
        public int Calls { get; private set; }
        public CatalogSnapshot? GetSnapshot() { Calls++; return null; }
    }

    private sealed class CountingSource : IMatchMetadataSource
    {
        public int Calls { get; private set; }
        public ValueTask<Result<ReadOnlyMemory<byte>>> ReadAsync(string id, CancellationToken cancellation)
        {
            Calls++;
            return ValueTask.FromResult(Result<ReadOnlyMemory<byte>>.Ok(Fixture));
        }
    }
}
