using System.Net;
using System.Text;
using DisputeDesk.Api.IntegrationTests.Disputes;
using DisputeDesk.Api.IntegrationTests.Infrastructure;
using DisputeDesk.Api.IntegrationTests.Transactions;
using DisputeDesk.Api.Operations;
using DisputeDesk.Contracts.Problems;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace DisputeDesk.Api.IntegrationTests.Operations;

/// <summary>
/// R21, AD-9: <c>Operations:AllowedClients</c> is an allowlist of token <c>sub</c>s, deny by
/// default (OWASP Authorization Cheat Sheet). Only an exact (ordinal) match is allowed; an empty or missing list refuses
/// every token, whatever its roles; a token without a <c>sub</c> is refused before the list is read; and no
/// <c>appsettings*.json</c> adds an entry, so the provisioner's clients file is the list's only source.
/// </summary>
public class OperationsAllowlistTests(PostgresFixture postgres, TransactionsStub stub) : IClassFixture<TransactionsStub>
{
    private static readonly string[] AllRoles = [OperationsApi.ReadRole, OperationsApi.StartReviewRole, OperationsApi.EscalateRole, OperationsApi.ResolveRole];

    [Theory]
    [InlineData("OPS-TOOL-TEST")]
    [InlineData("Ops-tool-test")]
    [InlineData("ops-tool-test ")]
    public async Task ReadDispute_SubDiffersFromAListedOneOnlyInCaseOrSpace_Returns403(string sub)
    {
        // The list holds FakeZitadel.OperationsSub ("ops-tool-test"), which reads (OperationsRolesTests).
        await using ApiFactory factory = await TransactionsApi.StartAsync(postgres, stub);
        Guid disputeId = await factory.RaiseForCustomerAAsync();
        using HttpClient tool = factory.CreateTool();

        HttpResponseMessage response = await tool.ReadDisputeAsync(disputeId.ToString(), factory.Zitadel.Issue(sub, AllRoles));

        await AssertForbiddenAsync(response);
    }

    [Theory]
    [InlineData(OperationsApi.ReadRole)]
    [InlineData("every role")]
    public async Task ReadDispute_EmptyList_Returns403ForTheOnceListedSub(string roles)
    {
        // Post-configuration runs after binding, so the list the handler reads is empty.
        await using ApiFactory factory = await TransactionsApi.StartAsync(
            postgres, stub, services => services.PostConfigure<OperationsOptions>(operations => operations.AllowedClients = []));
        Guid disputeId = await factory.RaiseForCustomerAAsync();
        using HttpClient tool = factory.CreateTool();
        string[] granted = roles == "every role" ? AllRoles : [roles];

        HttpResponseMessage response = await tool.ReadDisputeAsync(disputeId.ToString(), factory.Zitadel.Issue(FakeZitadel.OperationsSub, granted));

        await AssertForbiddenAsync(response);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("""{ "Operations": {} }""")]
    [InlineData("""{ "Operations": { "AllowedClients": [] } }""")]
    public void Options_NoListConfigured_BindAnEmptyList(string json)
    {
        // A missing list binds as the empty one, which ReadDispute_EmptyList_Returns403ForTheOnceListedSub refuses.
        IConfiguration configuration = new ConfigurationBuilder().AddJsonStream(new MemoryStream(Encoding.UTF8.GetBytes(json))).Build();
        using ServiceProvider services = new ServiceCollection().AddSingleton(configuration).AddOperationsAuthentication().BuildServiceProvider();

        Assert.Empty(services.GetRequiredService<IOptions<OperationsOptions>>().Value.AllowedClients);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public async Task ReadDispute_NoSubOrABlankOne_Returns403EvenWhenTheListHoldsIt(string? sub)
    {
        // The list holds the blank subs too, so only the check before the lookup refuses them.
        await using ApiFactory factory = await TransactionsApi.StartAsync(
            postgres, stub, services => services.PostConfigure<OperationsOptions>(operations => operations.AllowedClients = ["", " "]));
        Guid disputeId = await factory.RaiseForCustomerAAsync();
        using HttpClient tool = factory.CreateTool();
        SecurityTokenDescriptor token = factory.Zitadel.AccessToken(sub ?? "", AllRoles);
        if (sub is null)
        {
            token.Claims.Remove("sub");
        }

        HttpResponseMessage response = await tool.ReadDisputeAsync(disputeId.ToString(), FakeZitadel.Sign(token));

        await AssertForbiddenAsync(response);
    }

    public static TheoryData<string> AppSettingsFiles() =>
        [.. Directory.GetFiles(Path.Combine(RepoRoot.Backend, "src", "DisputeDesk.Api"), "appsettings*.json").Select(Path.GetFileName).OfType<string>()];

    [Theory]
    [MemberData(nameof(AppSettingsFiles))]
    public void AppSettings_HoldNoOperationsSection(string file)
    {
        // Read as the Api reads it: the binder merges arrays by index across providers, so any entry here would add to or
        // overwrite the provisioner's list silently.
        IConfiguration configuration = new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(RepoRoot.Backend, "src", "DisputeDesk.Api", file), optional: false, reloadOnChange: false)
            .Build();

        Assert.False(configuration.GetSection(OperationsOptions.Section).Exists());
    }

    private static async Task AssertForbiddenAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(ProblemType.For(CommonProblems.Forbidden), await response.ProblemTypeAsync());
    }
}
