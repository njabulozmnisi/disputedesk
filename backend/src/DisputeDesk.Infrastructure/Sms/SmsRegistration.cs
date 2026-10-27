using DisputeDesk.Application.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace DisputeDesk.Infrastructure.Sms;

public static class SmsRegistration
{
    /// <summary>Registers <see cref="ISmsSender"/> over SMTP. Needs <c>Sms:Host</c> and <c>Sms:Port</c>.</summary>
    public static IServiceCollection AddSmsSender(this IServiceCollection services)
    {
        services.AddOptions<SmsOptions>()
            .BindConfiguration(SmsOptions.SectionName)
            .Validate(options => !string.IsNullOrWhiteSpace(options.Host) && options.Port is > 0 and <= 65535, "Sms:Host and Sms:Port are required.")
            .ValidateOnStart();

        services.AddSingleton<ISmsSender, SmtpSmsSender>();
        return services;
    }
}
