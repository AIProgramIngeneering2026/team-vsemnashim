using System.Collections.Immutable;
using System.Text.Json;
using FairfaxStat.Core.Jobs;
using FairfaxStat.Core.Results;

namespace FairfaxStat.Core.Execution;

public sealed record OperationStep(ToolKey Key, Func<object, object> BindInput);
public sealed record Operation(string Name, Func<JsonElement, Result<JsonElement>> Normalize,
    ImmutableArray<OperationStep> Steps, Func<IReadOnlyList<object>, JobArtifacts> Publish);

public sealed class ToolRegistry
{
    private readonly Dictionary<string, Operation> operations = new(StringComparer.Ordinal);
    private readonly Dictionary<ToolKey, RegisteredTool> tools = [];

    // Registrations are completed at application composition, before submitting work.
    public void Register(Operation operation)
    {
        if (operation.Steps.IsDefaultOrEmpty)
            throw new ArgumentException("У операции должен быть хотя бы один шаг.", nameof(operation));
        operations.Add(operation.Name, operation);
    }

    public Result<Operation> FindOperation(string name) => operations.TryGetValue(name, out var operation)
        ? Result<Operation>.Ok(operation)
        : Result<Operation>.Fail(Error.Field("UNKNOWN_TOOL", "body", "/operation", "unknown_operation"));

    public void Register<TInput, TOutput>(ITool<TInput, TOutput> tool)
        where TInput : notnull where TOutput : notnull
    {
        var descriptor = tool.Descriptor;
        if (descriptor.Key.Version <= 0 || descriptor.Timeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(tool), "Некорректная версия или срок Tool.");

        async ValueTask<Result<object>> Invoke(object value, ToolContext context, CancellationToken token)
        {
            if (value is not TInput input)
                return Result<object>.Fail(Error.Field("TOOL_INPUT_INVALID", "job", "/input", "wrong_type"));
            var output = await tool.RunAsync(input, context, token);
            if (!output.IsSuccess)
                return Result<object>.Fail(output.Error);
            return tool.AcceptsOutput(input, output.Value, context)
                ? Result<object>.Ok(output.Value)
                : Result<object>.Fail(new("TOOL_OUTPUT_INVALID", "Выход шага нарушает контракт."));
        }

        tools.Add(descriptor.Key, new(descriptor, Invoke));
    }

    public Result<RegisteredTool> FindTool(ToolKey key) => tools.TryGetValue(key, out var tool)
        ? Result<RegisteredTool>.Ok(tool)
        : Result<RegisteredTool>.Fail(new("TOOL_VERSION_UNAVAILABLE", "Закреплённая версия шага недоступна."));
}

public sealed record RegisteredTool(ToolDescriptor Descriptor,
    Func<object, ToolContext, CancellationToken, ValueTask<Result<object>>> Invoke);
