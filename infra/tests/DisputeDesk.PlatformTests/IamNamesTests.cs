namespace DisputeDesk.PlatformTests;

/// <summary>
/// AWS's service reference, fetched once for IamNamesTests: the index, the global condition keys and the file of each
/// service the real tree's actions name. The whole fetch, retries included, is bounded by this fixture's own 5-minute
/// deadline. With no network (a local run offline) the fetch fails, naming the URL, and so does every test that needs it.
/// </summary>
public sealed class ServiceReferenceFixture : IAsyncLifetime
{
    internal static readonly TimeSpan Deadline = TimeSpan.FromMinutes(5);

    internal ServiceReference Reference { get; private set; } = new(new HashSet<string>(), new Dictionary<string, string[]>(), []);

    public async ValueTask InitializeAsync()
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        deadline.CancelAfter(Deadline);
        using HttpClient client = ServiceReference.CreateClient(ServiceReference.CreateHandler());

        PolicyNames names = IamNames.Read(IamNamesTests.TerraformDirectory);
        IEnumerable<string> services = names.Actions.Select(action => action.Name.Split(':')[0]);
        Reference = await ServiceReference.FetchAsync(client, ServiceReference.RetryDelay, services, deadline.Token);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

/// <summary>
/// Story 7.16's check: every IAM action and condition key in the policy templates
/// under infra/terraform exists in AWS's service reference, and a copy with one name misspelt fails naming exactly it.
/// The rules themselves are proved offline in IamNamesRulesTests and the fetch in ServiceReferenceFetchTests.
/// </summary>
public sealed class IamNamesTests(ServiceReferenceFixture fixture) : IClassFixture<ServiceReferenceFixture>
{
    internal static readonly string TerraformDirectory = Path.Combine(SourceRoots.Repo, "infra/terraform");

    [Fact]
    public void IamNames_InTheTerraformTree_AllExistInTheServiceReference()
    {
        List<string> hclProblems = IamNames.CheckTerraformFiles(TerraformDirectory);
        PolicyNames names = IamNames.Read(TerraformDirectory);
        (List<string> lines, List<string> failures) = IamNames.Check(names, fixture.Reference);

        foreach (string line in lines)
        {
            TestContext.Current.TestOutputHelper?.WriteLine(line);
        }

        string[] all = [.. hclProblems, .. names.Problems, .. failures];
        Assert.True(all.Length == 0, $"infra/terraform:\n{string.Join('\n', all)}");
        Assert.Equal(names.Actions.Count + names.ConditionKeys.Count, lines.Count);
    }

    [Theory]
    // Criterion 6's three cases.
    [InlineData("ssm:SendCommand", "ssm:SendComand")]
    [InlineData("kms:ViaService", "kms:ViaServices")]
    [InlineData("token.actions.githubusercontent.com:sub", "token.actions.githubusercontent.cmo:sub")]
    // An action whose name is unknown, an unknown service prefix, and the OIDC exception misspelt.
    [InlineData("xray:PutSpans", "xray:PutSpan")]
    [InlineData("logs:PutLogEvents", "cloudwatchlogs:PutLogEvents")]
    [InlineData("token.actions.githubusercontent.com:aud", "token.actions.githubusercontnet.com:aud")]
    public void IamNames_OneMisspeltInACopy_FailsNamingExactlyIt(string name, string misspelling)
    {
        string copy = Directory.CreateTempSubdirectory("disputedesk-iam-names-").FullName;
        try
        {
            CopyTemplatesWithOneNameChanged(copy, name, misspelling);

            PolicyNames names = IamNames.Read(copy);
            (_, List<string> failures) = IamNames.Check(names, fixture.Reference);

            Assert.Empty(names.Problems);
            Assert.NotEmpty(failures);
            Assert.All(failures, failure => Assert.Contains($" {misspelling} in ", failure, StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(copy, recursive: true);
        }
    }

    /// <summary>Copies every policy template, with the JSON string "NAME" written as "MISSPELLING" wherever it occurs.</summary>
    private static void CopyTemplatesWithOneNameChanged(string copy, string name, string misspelling)
    {
        int changed = 0;
        foreach (string file in IamNames.FilesUnder(TerraformDirectory, "*.json.tftpl"))
        {
            string text = File.ReadAllText(file);
            string changedText = text.Replace($"\"{name}\"", $"\"{misspelling}\"", StringComparison.Ordinal);
            if (changedText != text)
            {
                changed++;
            }

            string target = Path.Combine(copy, Path.GetRelativePath(TerraformDirectory, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target) ?? copy);
            File.WriteAllText(target, changedText);
        }

        Assert.True(changed > 0, $"No policy template holds \"{name}\" to misspell.");
    }
}
