using System.Diagnostics.CodeAnalysis;

namespace DisputeDesk.Domain;

/// <summary>The outcome of an operation that can fail in an expected way.</summary>
public class Result
{
    private protected Result(Error? error) => Error = error;

    public Error? Error { get; }

    [MemberNotNullWhen(false, nameof(Error))]
    public bool IsSuccess => Error is null;

    [MemberNotNullWhen(true, nameof(Error))]
    public bool IsFailure => Error is not null;

    public static Result Success() => new(null);

    public static Result<T> Success<T>(T value) where T : notnull => new(value);

    public static Result Failure(Error error) => new(error ?? throw new ArgumentNullException(nameof(error)));

    public static Result<T> Failure<T>(Error error) where T : notnull => new(error);

    public static implicit operator Result(Error error) => Failure(error);
}

/// <summary>The outcome of an operation that returns a value when it succeeds.</summary>
public sealed class Result<T> : Result
    where T : notnull
{
    private readonly T? _value;

    internal Result(T value) : base(null) => _value = value ?? throw new ArgumentNullException(nameof(value));

    internal Result(Error error) : base(error ?? throw new ArgumentNullException(nameof(error))) { }

    /// <summary>The value. Reading it from a failed result is a programming error.</summary>
    public T Value => IsSuccess && _value is T value
        ? value
        : throw new InvalidOperationException($"A failed result has no value ({Error?.Code}).");

    public static implicit operator Result<T>(T value) => new(value);

    public static implicit operator Result<T>(Error error) => new(error);
}
