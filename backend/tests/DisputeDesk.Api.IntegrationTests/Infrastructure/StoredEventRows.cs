using System.Text.Json;
using DisputeDesk.Application.Disputes;
using DisputeDesk.Contracts.Json;
using DisputeDesk.Domain.Disputes;

namespace DisputeDesk.Api.IntegrationTests.Infrastructure;

/// <summary>
/// The row a test writes by hand for a stored event another action would have appended. It maps the fields as
/// <see cref="StoredEventRow.From"/> does, without the dispute navigation, so adding it never touches the dispute.
/// </summary>
public static class StoredEventRows
{
    public static StoredEventRow For(Guid disputeId, StoredEvent storedEvent) => new()
    {
        Id = Guid.CreateVersion7(storedEvent.OccurredAtUtc),
        DisputeId = disputeId,
        Version = storedEvent.Version,
        EventType = storedEvent.GetType().Name,
        OccurredAtUtc = storedEvent.OccurredAtUtc,
        ActorType = storedEvent.ActorType,
        ActorId = storedEvent.ActorId,
        TraceId = storedEvent.TraceId,
        Data = JsonSerializer.Serialize(storedEvent, storedEvent.GetType(), DisputeDeskJson.Options),
    };
}
