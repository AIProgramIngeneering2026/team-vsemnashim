using FairfaxStat.Core.Results;

namespace FairfaxStat.Core.MatchAnalysis.Metadata;

public interface IMatchMetadataSource
{
    ValueTask<Result<ReadOnlyMemory<byte>>> ReadAsync(string matchId, CancellationToken cancellation);
}

public sealed class InMemoryMetadataSource(IReadOnlyDictionary<string, byte[]> matches) : IMatchMetadataSource
{
    private readonly Dictionary<string, byte[]> snapshots = matches.ToDictionary(p => p.Key, p => p.Value.ToArray());

    public ValueTask<Result<ReadOnlyMemory<byte>>> ReadAsync(string matchId, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        return ValueTask.FromResult(snapshots.TryGetValue(matchId, out var data)
            ? Result<ReadOnlyMemory<byte>>.Ok(data)
            : Result<ReadOnlyMemory<byte>>.Fail(new("MATCH_DATA_UNAVAILABLE", "Данные матча отсутствуют в источнике.")));
    }
}
