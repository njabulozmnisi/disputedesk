namespace DisputeDesk.Infrastructure.CoreBanking;

/// <summary>The <c>CoreBanking</c> configuration section (AD-26).</summary>
public sealed class CoreBankingOptions
{
    public const string SectionName = "CoreBanking";

    /// <summary>Where the core-banking stub listens, such as <c>http://core-banking:8080</c> in Compose.</summary>
    public Uri? BaseUrl { get; set; }
}
