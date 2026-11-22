using FairfaxStat.Core.MatchAnalysis.Catalog;

namespace FairfaxStat.Core.Tests.MatchAnalysis;

public sealed class CatalogTests
{
    [Fact]
    public void Local_catalog_contains_labels_and_retains_its_version()
    {
        var provider = new StaticCatalogProvider();
        var snapshot = provider.GetSnapshot();
        Assert.Same(snapshot, provider.GetSnapshot());
        Assert.Equal("Graves", snapshot.Heroes["76"]);
        Assert.Equal("Cold Front", snapshot.Items["1976391348"]);
        Assert.False(snapshot.Heroes.ContainsKey("18446744073709551615"));
    }
}
