using System.Diagnostics;
using System.Net;

namespace DisputeDesk.Application.Linking;

/// <summary>
/// The work a linking start leaves for the background job (AD-12): which challenge, whose login, from which address,
/// the request's trace to link to, and the ID and account numbers, which live only here, in memory, and are never
/// stored, logged or traced.
/// </summary>
public sealed record LinkingJob(
    Guid ChallengeId,
    string LoginSub,
    IPAddress? ClientIp,
    ActivityContext RequestContext,
    string IdNumber,
    string AccountNumber)
{
    /// <summary>
    /// Only the type name: a record's generated <c>ToString</c> prints every property, and this one holds the ID number
    /// (OWASP Logging Cheat Sheet: data to exclude).
    /// </summary>
    public override string ToString() => nameof(LinkingJob);
}
