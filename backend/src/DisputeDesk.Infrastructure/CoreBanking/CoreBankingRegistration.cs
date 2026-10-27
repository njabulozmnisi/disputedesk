using DisputeDesk.Application.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;

namespace DisputeDesk.Infrastructure.CoreBanking;

public static class CoreBankingRegistration
{
    /// <summary>
    /// Registers <see cref="ICoreBankingClient"/> and <see cref="ISimSwapCheck"/>, both typed HttpClients for the
    /// core-banking stub behind the standard resilience handler. Needs <c>CoreBanking:BaseUrl</c>.
    /// </summary>
    public static IServiceCollection AddCoreBanking(this IServiceCollection services)
    {
        services.AddOptions<CoreBankingOptions>()
            .BindConfiguration(CoreBankingOptions.SectionName)
            .Validate(options => options.BaseUrl is not null, "CoreBanking:BaseUrl is required.")
            .ValidateOnStart();

        services.AddHttpClient<ICoreBankingClient, CoreBankingClient>(UseCoreBankingBaseUrl)
            .AddStandardResilienceHandler(TuneResilience);
        services.AddHttpClient<ISimSwapCheck, SimSwapCheck>(UseCoreBankingBaseUrl)
            .AddStandardResilienceHandler(TuneResilience);

        return services;
    }

    private static void UseCoreBankingBaseUrl(IServiceProvider provider, HttpClient client) =>
        client.BaseAddress = provider.GetRequiredService<IOptions<CoreBankingOptions>>().Value.BaseUrl;

    /// <summary>
    /// How long a whole call may take, reading the response body included. The resilience handler's total timeout ends
    /// when the response headers arrive, and it switches HttpClient.Timeout off, so both clients apply this deadline
    /// themselves around sending and reading.
    /// </summary>
    internal static readonly TimeSpan WholeCallDeadline = TimeSpan.FromSeconds(5);

    /// <summary>
    /// AD-21: tuned so stopping the stub shows the policies at work. Each attempt gets 2 seconds; 2 retries start at
    /// 200 ms with jitter; the whole call gives up after 5 seconds. The circuit opens for 15 seconds only when every
    /// attempt in a 30-second window fails, with at least 5 attempts (each retry counts); one success in the window
    /// keeps it closed. Retries cover every method, POST included, on purpose: every stub call is read-only.
    /// </summary>
    private static void TuneResilience(HttpStandardResilienceOptions options)
    {
        options.AttemptTimeout.Timeout = TimeSpan.FromSeconds(2);
        options.Retry.MaxRetryAttempts = 2;
        options.Retry.Delay = TimeSpan.FromMilliseconds(200);
        options.Retry.UseJitter = true;
        options.TotalRequestTimeout.Timeout = WholeCallDeadline;
        options.CircuitBreaker.FailureRatio = 1.0;
        options.CircuitBreaker.MinimumThroughput = 5;
        options.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(30);
        options.CircuitBreaker.BreakDuration = TimeSpan.FromSeconds(15);
    }
}
