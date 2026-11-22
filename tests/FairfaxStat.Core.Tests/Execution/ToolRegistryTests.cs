using FairfaxStat.Core.Execution;
using FairfaxStat.Core.Jobs;
using FairfaxStat.Core.Results;

namespace FairfaxStat.Core.Tests.Execution;

public sealed class ToolRegistryTests
{
    internal sealed class TestTool : ITool<int, int>
    {
        public ToolKey Key { get; } = new("double", 7);
        public ToolDescriptor Descriptor => new(Key, "Integer", "Integer", TimeSpan.FromSeconds(10), new("PROCESSING_TIMEOUT", "Истёк срок."));
        public Func<int, Result<int>> Execute { get; set; } = value => Result<int>.Ok(value * 2);
        public bool AcceptOutput { get; set; } = true;
        public int Calls { get; private set; }
        public ValueTask<Result<int>> RunAsync(int input, ToolContext context, CancellationToken cancellation)
        {
            Calls++;
            return ValueTask.FromResult(Execute(input));
        }
        public bool AcceptsOutput(int input, int output, ToolContext context) => AcceptOutput;
    }

    [Fact]
    public void Registry_distinguishes_public_names_from_pinned_internal_versions()
    {
        var registry = new ToolRegistry();
        var tool = new TestTool();
        registry.Register(tool);
        Assert.True(registry.FindTool(tool.Key).IsSuccess);
        Assert.Equal("TOOL_VERSION_UNAVAILABLE", registry.FindTool(tool.Key with { Version = 1 }).Error.Code);
        var error = registry.FindOperation(tool.Key.Name).Error;
        Assert.Equal("UNKNOWN_TOOL", error.Code);
        Assert.Equal("/operation", Assert.Single(error.Details).Field);
    }
}
