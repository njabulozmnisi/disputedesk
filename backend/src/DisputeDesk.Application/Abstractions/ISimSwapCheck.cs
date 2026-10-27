namespace DisputeDesk.Application.Abstractions;

/// <summary>Asks the mobile network whether a cellphone number's SIM was swapped recently (AD-21).</summary>
public interface ISimSwapCheck
{
    /// <summary>
    /// True only when the check positively answers that there was no recent SIM swap. A recent swap, an error
    /// or a timeout all return false: the check fails closed.
    /// </summary>
    Task<bool> PassesAsync(string cellphone, CancellationToken cancellationToken);
}
