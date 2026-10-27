using System.Net.Http.Json;
using System.Text.Json;
using DisputeDesk.Application.Abstractions;
using Polly;

namespace DisputeDesk.Infrastructure.CoreBanking;

/// <summary>
/// <see cref="ISimSwapCheck"/> over HTTP to the core-banking stub, which stands in for the mobile network (AD-21).
/// It fails closed: anything other than a clear "no recent SIM swap" answer counts as failed.
/// </summary>
public sealed class SimSwapCheck(HttpClient http) : ISimSwapCheck
{
    public async Task<bool> PassesAsync(string cellphone, CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(CoreBankingRegistration.WholeCallDeadline);
        try
        {
            using HttpResponseMessage response = await http.PostAsJsonAsync("sim-swap/check", new { cellphone }, deadline.Token);
            if (!response.IsSuccessStatusCode)
            {
                return false;
            }

            SimSwapAnswer? answer = await response.Content.ReadFromJsonAsync<SimSwapAnswer>(deadline.Token);
            return answer is { RecentSimSwap: false };
        }
        catch (Exception exception) when (exception is HttpRequestException or ExecutionRejectedException or JsonException)
        {
            return false;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // The whole-call deadline passed: a timeout fails closed. A cancellation by the caller still propagates.
            return false;
        }
    }

    // Nullable, so an answer without the field is not read as "no recent swap".
    private sealed record SimSwapAnswer(bool? RecentSimSwap);
}
