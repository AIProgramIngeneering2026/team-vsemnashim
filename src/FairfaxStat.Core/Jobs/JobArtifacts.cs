using System.Text.Json;

namespace FairfaxStat.Core.Jobs;

public sealed record ToolKey(string Name, int Version);

public sealed class JobArtifacts
{
    public JsonElement Snapshot { get; }
    public JsonElement Report { get; }

    public JobArtifacts(JsonElement snapshot, JsonElement report)
    {
        Snapshot = snapshot.Clone();
        Report = report.Clone();
    }

    internal bool IsComplete => Snapshot.ValueKind == JsonValueKind.Object && Report.ValueKind == JsonValueKind.Object;
}

public enum JobState { Queued, Running, RetryWait, Succeeded, Failed }
