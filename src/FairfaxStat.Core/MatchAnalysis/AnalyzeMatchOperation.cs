using System.Text.Json;
using FairfaxStat.Core.Execution;
using FairfaxStat.Core.Jobs;
using FairfaxStat.Core.MatchAnalysis.Catalog;
using FairfaxStat.Core.MatchAnalysis.Metadata;
using FairfaxStat.Core.MatchAnalysis.Reports;

namespace FairfaxStat.Core.MatchAnalysis;

public static class AnalyzeMatchOperation
{
    public static void Register(ToolRegistry registry, IMatchMetadataSource source, ICatalogProvider? catalogs = null)
    {
        catalogs ??= new StaticCatalogProvider();
        registry.Register(new FetchMatchMetadataTool(source));
        registry.Register(new BuildMatchReportTool());
        registry.Register(new Operation("analyze_match", MatchId.NormalizeParameters,
        [
            new(FetchMatchMetadataTool.Key, value =>
                new FetchMatchInput(((JsonElement)value).GetProperty("match_id").GetString()!)),
            new(BuildMatchReportTool.Key, value => new BuildReportInput((ValidatedMatchSnapshot)value, catalogs.GetSnapshot()))
        ],
        outputs => new JobArtifacts(((ValidatedMatchSnapshot)outputs[0]).Raw, ((MatchReport)outputs[1]).ToJson())));
    }
}
