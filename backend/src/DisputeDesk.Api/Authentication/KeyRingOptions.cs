using System.Diagnostics.CodeAnalysis;

namespace DisputeDesk.Api.Authentication;

/// <summary>
/// The <c>DataProtection</c> section (AD-7, AD-26): where the Data Protection key ring lives. Unset, it stays in Postgres;
/// in AWS, infra/deploy/aws/compose.yml sets both keys, and the key ring goes to SSM Parameter Store.
/// </summary>
public sealed class KeyRingOptions
{
    public const string Section = "DataProtection";

    /// <summary>The SSM parameter path the keys are stored under, such as <c>/disputedesk-dataprotection/dev</c>.</summary>
    public string? SsmParameterPath { get; set; }

    /// <summary>The KMS key that encrypts each key's SecureString; required with the path, so SSM never uses <c>alias/aws/ssm</c>.</summary>
    public string? KmsKeyId { get; set; }

    [MemberNotNullWhen(true, nameof(SsmParameterPath))]
    public bool StoresKeysInSsm => !string.IsNullOrEmpty(SsmParameterPath);
}
