using DisputeDesk.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace DisputeDesk.Infrastructure.Seeding;

public static class SeedRegistration
{
    /// <summary>
    /// Hands the <see cref="Seeder"/> to EF Core as the context's seeding delegates (<c>UseAsyncSeeding</c>, and
    /// <c>UseSeeding</c> for the tools), so every migrate seeds inside the migration lock. Only the Api calls this.
    /// With <c>Seed:Enabled</c> on, it needs <c>Seed:FixturesPath</c>.
    /// </summary>
    public static IServiceCollection AddSeeder(this IServiceCollection services)
    {
        services.AddOptions<SeedOptions>()
            .BindConfiguration(SeedOptions.SectionName)
            .Validate(options => !options.Enabled || !string.IsNullOrWhiteSpace(options.FixturesPath), "Seed:FixturesPath is required when Seed:Enabled is on.")
            .ValidateOnStart();

        services.AddSingleton<Seeder>();
        services.ConfigureDbContext<AppDbContext>((provider, options) =>
        {
            Seeder seeder = provider.GetRequiredService<Seeder>();
            options.UseAsyncSeeding((context, _, cancellationToken) => seeder.SeedAsync(context, cancellationToken));
            options.UseSeeding((context, _) => seeder.Seed(context));
        });
        return services;
    }
}
