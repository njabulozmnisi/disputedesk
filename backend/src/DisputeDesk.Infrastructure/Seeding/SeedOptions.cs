namespace DisputeDesk.Infrastructure.Seeding;

/// <summary>The <c>Seed</c> configuration section (AD-26, AD-30).</summary>
public sealed class SeedOptions
{
    public const string SectionName = "Seed";

    /// <summary>Off unless configured on: local Compose and the AWS demo turn it on.</summary>
    public bool Enabled { get; set; }

    /// <summary>Where <c>fixtures/local.json</c> is, such as <c>/fixtures/local.json</c> in Compose. Needed when enabled.</summary>
    public string FixturesPath { get; set; } = string.Empty;
}
