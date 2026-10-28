using DisputeDesk.Domain;

namespace DisputeDesk.Application.Abstractions;

/// <summary>
/// A commit lost to a unique index that stands for an expected failure, such as a second dispute on one transaction
/// raised at the same moment (AD-15). Infrastructure maps the index to <see cref="Error"/> in one place; the handler
/// returns that error.
/// </summary>
public sealed class UniqueViolationException(Error error, Exception innerException) : Exception(error.Message, innerException)
{
    public Error Error { get; } = error;
}
