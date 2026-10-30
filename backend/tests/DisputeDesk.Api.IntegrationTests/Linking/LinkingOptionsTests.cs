using DisputeDesk.Api.IntegrationTests.Infrastructure;
using DisputeDesk.Infrastructure.Linking;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace DisputeDesk.Api.IntegrationTests.Linking;

/// <summary>6.5 AC5: <c>Linking:QueueCapacity</c> and <c>Linking:Budget</c> are checked when the Api starts.</summary>
public class LinkingOptionsTests(PostgresFixture postgres)
{
    [Theory]
    [InlineData("Linking:QueueCapacity", "0", "Linking:QueueCapacity must be from 1 to 1000.")]
    [InlineData("Linking:QueueCapacity", "-1", "Linking:QueueCapacity must be from 1 to 1000.")]
    [InlineData("Linking:QueueCapacity", "1001", "Linking:QueueCapacity must be from 1 to 1000.")]
    [InlineData("Linking:Budget", "00:00:00", "Linking:Budget must be from 1 to 30 seconds.")]
    [InlineData("Linking:Budget", "-00:00:01", "Linking:Budget must be from 1 to 30 seconds.")]
    [InlineData("Linking:Budget", "00:00:00.999", "Linking:Budget must be from 1 to 30 seconds.")]
    [InlineData("Linking:Budget", "00:00:30.0010000", "Linking:Budget must be from 1 to 30 seconds.")]
    [InlineData("Linking:Budget", "00:00:31", "Linking:Budget must be from 1 to 30 seconds.")]
    public async Task ApiStart_OutOfBounds_StopsTheApiWithAnErrorNamingTheKey(string key, string value, string expected)
    {
        (int exitCode, string output) = await ApiProcess.RunUntilExitAsync(new Dictionary<string, string?> { [key] = value }, TestContext.Current.CancellationToken);

        Assert.NotEqual(0, exitCode);
        Assert.Contains(expected, output, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("1", "00:00:01")]
    [InlineData("1000", "00:00:30")]
    public async Task ApiStart_AtTheBounds_StartsWithThoseValues(string capacity, string budget)
    {
        await using var factory = new ApiFactory(
            await postgres.CreateMigratedDatabaseAsync(),
            settings: new Dictionary<string, string?> { ["Linking:QueueCapacity"] = capacity, ["Linking:Budget"] = budget });

        LinkingOptions options = factory.Services.GetRequiredService<IOptions<LinkingOptions>>().Value;

        Assert.Equal((int.Parse(capacity, System.Globalization.CultureInfo.InvariantCulture), TimeSpan.Parse(budget, System.Globalization.CultureInfo.InvariantCulture)), (options.QueueCapacity, options.Budget));
    }
}
