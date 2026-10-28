using System.Diagnostics.Metrics;
using System.Text.Json;
using DisputeDesk.Contracts.Disputes;
using DisputeDesk.Infrastructure.Observability;

namespace DisputeDesk.Api.Disputes;

/// <summary>
/// The raise metrics of AD-20 and NFR-OBS-3, created through <see cref="IMeterFactory"/> (Microsoft Learn: "Creating
/// metrics"). Labels carry only the reason and the HTTP status, never personal data.
/// </summary>
public sealed class RaiseMetrics
{
    private readonly Counter<long> _raised;
    private readonly Counter<long> _rejections;

    public RaiseMetrics(IMeterFactory meterFactory)
    {
        Meter meter = meterFactory.Create(Telemetry.MeterName);
        _raised = meter.CreateCounter<long>("disputedesk.disputes.raised", description: "Disputes raised, by reason.");
        _rejections = meter.CreateCounter<long>("disputedesk.raise.rejections", description: "Raises refused with 409 or 422, by status.");
    }

    public void RecordRaised(Reason reason) =>
        _raised.Add(1, new KeyValuePair<string, object?>("reason", JsonNamingPolicy.CamelCase.ConvertName(reason.ToString())));

    /// <summary>Counts a refused raise when it is a 409 (already disputed) or a 422 (not disputable); other statuses are not rejections.</summary>
    public void RecordRefusal(int status)
    {
        if (status is StatusCodes.Status409Conflict or StatusCodes.Status422UnprocessableEntity)
        {
            _rejections.Add(1, new KeyValuePair<string, object?>("status", status));
        }
    }
}
