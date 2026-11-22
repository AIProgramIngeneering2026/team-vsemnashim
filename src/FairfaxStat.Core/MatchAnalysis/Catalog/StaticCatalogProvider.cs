using System.Collections.Immutable;

namespace FairfaxStat.Core.MatchAnalysis.Catalog;

public sealed record CatalogSnapshot(string Version, DateTimeOffset LoadedAt,
    ImmutableDictionary<string, string> Heroes, ImmutableDictionary<string, string> Items);

public interface ICatalogProvider
{
    CatalogSnapshot? GetSnapshot();
}

public sealed class StaticCatalogProvider : ICatalogProvider
{
    private static readonly CatalogSnapshot Catalog = new("fixture-en-1",
        new DateTimeOffset(2026, 11, 8, 16, 36, 42, TimeSpan.Zero),
        new Dictionary<string, string> { ["76"] = "Graves", ["31"] = "Lash", ["15"] = "Bebop" }.ToImmutableDictionary(),
        new Dictionary<string, string> { ["1976391348"] = "Cold Front", ["381961617"] = "Active Reload" }.ToImmutableDictionary());

    public CatalogSnapshot GetSnapshot() => Catalog;
}
