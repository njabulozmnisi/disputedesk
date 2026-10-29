using System.Text.Json;
using DisputeDesk.Api.IntegrationTests.CoreBanking;
using DisputeDesk.Api.IntegrationTests.Infrastructure;
using DisputeDesk.Contracts.Disputes;
using DisputeDesk.Contracts.Json;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace DisputeDesk.Api.IntegrationTests.Disputes;

/// <summary>
/// AD-5: the outbox payload, the stored event data and the stored 201 body are all serialised with one shared,
/// read-only <see cref="JsonSerializerOptions"/> instance defined once in Contracts (Microsoft Learn, "Configure
/// JsonSerializerOptions": reuse one instance), and the framework's own instance has the same settings.
/// </summary>
public class SharedJsonOptionsTests(PostgresFixture postgres)
{
    [Fact]
    public void Options_SharedInstance_IsReadOnlyWithTheAd5Settings()
    {
        Assert.True(DisputeDeskJson.Options.IsReadOnly);
        Assert.Same(JsonNamingPolicy.CamelCase, DisputeDeskJson.Options.PropertyNamingPolicy);
        Assert.Equal("\"underReviewPrioritised\"", JsonSerializer.Serialize(CustomerStatus.UnderReviewPrioritised, DisputeDeskJson.Options));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<Reason>("\"wrongAmount, unrecognised\"", DisputeDeskJson.Options));
    }

    [Fact]
    public async Task HttpJsonOptions_FrameworkInstance_WritesWhatTheSharedInstanceWrites()
    {
        await using var factory = new ApiFactory(postgres.ConnectionString);
        JsonSerializerOptions framework = factory.Services.GetRequiredService<IOptions<JsonOptions>>().Value.SerializerOptions;
        var sample = new TimelineEntryResponse(DisputeAction.StartReview, CustomerStatus.UnderReviewPrioritised, TimelineActor.TheBank, DateTimeOffset.UnixEpoch, null);

        Assert.Equal(JsonSerializer.Serialize(sample, DisputeDeskJson.Options), JsonSerializer.Serialize(sample, framework));
    }

    [Fact]
    public void ProductionSource_NewJsonSerializerOptions_OnlyInDisputeDeskJson()
    {
        // One instance, defined once: no other production file builds its own options for these bodies.
        string src = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(FixturesFile.FindPath()) ?? "", "..", "backend", "src"));

        List<string> builders = [.. Directory.EnumerateFiles(src, "*.cs", SearchOption.AllDirectories)
            .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(file => File.ReadAllText(file).Contains("new JsonSerializerOptions(", StringComparison.Ordinal))
            .Select(file => Path.GetRelativePath(src, file))];

        Assert.Equal([Path.Combine("DisputeDesk.Contracts", "Json", "DisputeDeskJson.cs")], builders);
    }
}
