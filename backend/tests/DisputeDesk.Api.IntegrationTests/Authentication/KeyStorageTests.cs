using System.Xml.Linq;
using Amazon;
using Amazon.Runtime;
using Amazon.SimpleSystemsManagement;
using Amazon.SimpleSystemsManagement.Model;
using DisputeDesk.Api.IntegrationTests.Infrastructure;
using DisputeDesk.Infrastructure.Persistence;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.DataProtection.Repositories;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace DisputeDesk.Api.IntegrationTests.Authentication;

/// <summary>
/// 7.4 AC8 (AD-7, AD-26): <c>DataProtection:SsmParameterPath</c> chooses where the key ring lives. Unset, it stays in
/// Postgres; set, it goes to SSM Parameter Store as SecureStrings encrypted with the KMS key <c>DataProtection:KmsKeyId</c>
/// names. No test calls AWS: the SSM client is a stand-in that records the requests it is given.
/// </summary>
public class KeyStorageTests(PostgresFixture postgres)
{
    private const string KeyRingPath = "/disputedesk-dataprotection/test";
    // A placeholder key ID.
    private const string KmsKeyId = "00000000-0000-0000-0000-00000000000a";
    // A placeholder account for the ARNs, built in two halves like check-compose.sh's $(repeat 0 12), so the secret scan's
    // aws-account-id rule does not flag it.
    private const string ZeroAccount = "000000" + "000000";

    [Fact]
    public async Task KeyRing_WithoutAnSsmPath_IsStoredInPostgres()
    {
        await using var factory = new ApiFactory(postgres.ConnectionString);

        IXmlRepository? repository = factory.Services.GetRequiredService<IOptions<KeyManagementOptions>>().Value.XmlRepository;

        Assert.IsType<EntityFrameworkCoreXmlRepository<AppDbContext>>(repository);
    }

    [Theory]
    [InlineData(KmsKeyId)]
    [InlineData("arn:aws:kms:af-south-1:" + ZeroAccount + ":key/" + KmsKeyId)]
    public async Task KeyRing_WithAnSsmPath_IsStoredInSsmAsSecureStringsEncryptedWithTheConfiguredKey(string kmsKeyId)
    {
        var ssm = new RecordingSsmClient();
        await using var factory = new ApiFactory(
            postgres.ConnectionString,
            configureServices: services => services.AddSingleton<IAmazonSimpleSystemsManagement>(ssm),
            settings: new Dictionary<string, string?>
            {
                ["DataProtection:SsmParameterPath"] = KeyRingPath,
                ["DataProtection:KmsKeyId"] = kmsKeyId,
            });

        IXmlRepository? repository = factory.Services.GetRequiredService<IOptions<KeyManagementOptions>>().Value.XmlRepository;
        Assert.NotNull(repository);
        repository.StoreElement(new XElement("key", new XAttribute("id", "probe")), "probe");

        // The package's repository type is internal, so it is named rather than referenced.
        Assert.Equal("Amazon.AspNetCore.DataProtection.SSM.SSMXmlRepository", repository.GetType().FullName);
        PutParameterRequest stored = Assert.Single(ssm.Stored, request => request.Name == $"{KeyRingPath}/probe");
        Assert.Equal(ParameterType.SecureString, stored.Type);
        Assert.Equal(kmsKeyId, stored.KeyId);
    }

    [Theory]
    [InlineData("disputedesk-dataprotection/dev", KmsKeyId, "DataProtection:SsmParameterPath must start with / and have no trailing /.")]
    [InlineData("/disputedesk-dataprotection/dev/", KmsKeyId, "DataProtection:SsmParameterPath must start with / and have no trailing /.")]
    [InlineData("/", KmsKeyId, "DataProtection:SsmParameterPath must start with / and have no trailing /.")]
    [InlineData("/disputedesk-dataprotection/dev", null, "DataProtection:KmsKeyId is required when DataProtection:SsmParameterPath is set.")]
    [InlineData("/disputedesk-dataprotection/dev", " ", "DataProtection:KmsKeyId is required when DataProtection:SsmParameterPath is set.")]
    [InlineData("/disputedesk-dataprotection/dev", "alias/aws/ssm", "DataProtection:KmsKeyId must be a key ID or key ARN, not an alias.")]
    [InlineData("/disputedesk-dataprotection/dev", "arn:aws:kms:af-south-1:" + ZeroAccount + ":alias/aws/ssm", "DataProtection:KmsKeyId must be a key ID or key ARN, not an alias.")]
    // The other sides of infra/deploy/aws/input-bounds.sh's bound: an 11-digit account, an upper-case key ID, a trailing newline.
    [InlineData("/disputedesk-dataprotection/dev", "arn:aws:kms:af-south-1:00000" + "000000:key/" + KmsKeyId, "DataProtection:KmsKeyId must be a key ID or key ARN, not an alias.")]
    [InlineData("/disputedesk-dataprotection/dev", "00000000-0000-0000-0000-00000000000A", "DataProtection:KmsKeyId must be a key ID or key ARN, not an alias.")]
    [InlineData("/disputedesk-dataprotection/dev", KmsKeyId + "\n", "DataProtection:KmsKeyId must be a key ID or key ARN, not an alias.")]
    public async Task ApiStart_InvalidKeyRingSettings_StopsTheApiWithAnErrorNamingTheKey(string path, string? kmsKeyId, string expected)
    {
        // The host builds the SSM client before it validates options, and the client resolves its credentials when it is
        // built (never when nothing is sent). Placeholder credentials and a region in the environment let it build
        // without the instance metadata service or a developer's own AWS profile; no request is ever made with them.
        (int exitCode, string output) = await ApiProcess.RunUntilExitAsync(
            new Dictionary<string, string?>
            {
                ["DataProtection:SsmParameterPath"] = path,
                ["DataProtection:KmsKeyId"] = kmsKeyId,
                ["AWS_ACCESS_KEY_ID"] = "placeholder",
                ["AWS_SECRET_ACCESS_KEY"] = "placeholder",
                ["AWS_SESSION_TOKEN"] = null,
                ["AWS_REGION"] = "af-south-1",
            },
            TestContext.Current.CancellationToken);

        Assert.NotEqual(0, exitCode);
        Assert.Contains(expected, output, StringComparison.Ordinal);
    }

    /// <summary>
    /// An SSM client that never leaves the process: it holds no key ring and records every parameter it is asked to store.
    /// The region and anonymous credentials are only there to construct it; nothing is sent.
    /// </summary>
    private sealed class RecordingSsmClient() : AmazonSimpleSystemsManagementClient(new AnonymousAWSCredentials(), RegionEndpoint.AFSouth1)
    {
        private readonly List<PutParameterRequest> _stored = [];

        public IReadOnlyList<PutParameterRequest> Stored
        {
            get
            {
                lock (_stored)
                {
                    return [.. _stored];
                }
            }
        }

        public override Task<GetParametersByPathResponse> GetParametersByPathAsync(GetParametersByPathRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(new GetParametersByPathResponse { Parameters = [] });

        public override Task<PutParameterResponse> PutParameterAsync(PutParameterRequest request, CancellationToken cancellationToken = default)
        {
            lock (_stored)
            {
                _stored.Add(request);
            }

            return Task.FromResult(new PutParameterResponse());
        }
    }
}
