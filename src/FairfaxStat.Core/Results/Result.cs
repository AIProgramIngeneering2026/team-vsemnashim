using System.Collections.Immutable;

namespace FairfaxStat.Core.Results;

public sealed record ErrorDetail(string Location, string Field, string Reason);

public sealed record Error(string Code, string Message, bool Retryable = false,
    DateTimeOffset? RetryAt = null, ImmutableArray<ErrorDetail> Details = default)
{
    public ImmutableArray<ErrorDetail> Details { get; init; } = Details.IsDefault ? [] : Details;

    public static Error Field(string code, string location, string field, string reason) =>
        new(code, $"Некорректное поле {field}.", Details: [new(location, field, reason)]);
}

public sealed class Result<T> where T : notnull
{
    private readonly T? value;
    private readonly Error? error;

    private Result(T value) => this.value = value;
    private Result(Error error) => this.error = error;

    public bool IsSuccess => error is null;
    public T Value => IsSuccess ? value! : throw new InvalidOperationException("У результата нет значения.");
    public Error Error => error ?? throw new InvalidOperationException("У результата нет ошибки.");

    public static Result<T> Ok(T value) => new(value ?? throw new ArgumentNullException(nameof(value)));
    public static Result<T> Fail(Error error) => new(error ?? throw new ArgumentNullException(nameof(error)));
}
