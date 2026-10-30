using DisputeDesk.Domain.Disputes;

namespace DisputeDesk.Application.Abstractions;

/// <summary>
/// The operations actor a request acts as (AD-9, AD-10). Scoped, one per request. The operations policies' actor
/// handler in the Api sets it from <c>Operations:Actors</c> by the token's <c>sub</c> before an operations route runs;
/// handlers read only this, never the request or the token. It is recorded, never used to decide.
/// </summary>
public sealed class CurrentOperationsActor
{
    private Actor? _actor;

    /// <summary>The actor, of type <see cref="ActorType.Operations"/>. Reading it before the handler set it is a programming error.</summary>
    public Actor Actor => _actor ?? throw new InvalidOperationException("No operations actor was resolved for this request.");

    public void Set(Actor actor) => _actor = actor;
}
