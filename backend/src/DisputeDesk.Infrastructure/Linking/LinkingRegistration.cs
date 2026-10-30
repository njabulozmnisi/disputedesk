using System.Threading.Channels;
using DisputeDesk.Application.Linking;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace DisputeDesk.Infrastructure.Linking;

public static class LinkingRegistration
{
    /// <summary>
    /// Registers the linking job (AD-12): <see cref="LinkingOptions"/>, checked at start-up; one bounded channel, whose
    /// writer the start handler takes and whose reader the job service takes; the linking metrics; and the job service.
    /// The Api only: only the Api writes <c>LinkingChallenges</c> (AD-22). Needs <c>Linking:OtpHmacKey</c>.
    /// </summary>
    public static IServiceCollection AddLinking(this IServiceCollection services)
    {
        // The error names the key, never its value (Microsoft Learn, Options pattern: options validation).
        services.AddOptions<LinkingOptions>()
            .BindConfiguration(LinkingOptions.SectionName)
            .Validate(options => LinkingOptions.IsValidKey(options.OtpHmacKey), "Linking:OtpHmacKey must be base64 for at least 32 bytes.")
            .Validate(
                options => options.QueueCapacity is >= LinkingOptions.MinimumQueueCapacity and <= LinkingOptions.MaximumQueueCapacity,
                $"Linking:QueueCapacity must be from {LinkingOptions.MinimumQueueCapacity} to {LinkingOptions.MaximumQueueCapacity}.")
            .Validate(
                options => options.Budget >= LinkingOptions.MinimumBudget && options.Budget <= LinkingOptions.MaximumBudget,
                $"Linking:Budget must be from {LinkingOptions.MinimumBudget.TotalSeconds} to {LinkingOptions.MaximumBudget.TotalSeconds} seconds.")
            .ValidateOnStart();

        // A full channel makes TryWrite return false rather than wait (System.Threading.Channels, bounded channels).
        services.AddSingleton(provider => Channel.CreateBounded<LinkingJob>(
            new BoundedChannelOptions(provider.GetRequiredService<IOptions<LinkingOptions>>().Value.QueueCapacity)
            {
                FullMode = BoundedChannelFullMode.Wait,
            }));
        services.AddSingleton(provider => provider.GetRequiredService<Channel<LinkingJob>>().Writer);
        services.AddSingleton(provider => provider.GetRequiredService<Channel<LinkingJob>>().Reader);
        services.AddSingleton<LinkingMetrics>();
        services.AddScoped<LinkingJobSteps>();
        services.AddHostedService<LinkingJobService>();
        return services;
    }
}
