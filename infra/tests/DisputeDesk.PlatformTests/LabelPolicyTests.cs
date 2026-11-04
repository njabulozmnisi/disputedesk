using System.Text.Json;

namespace DisputeDesk.PlatformTests;

/// <summary>
/// 8.1 AC3: label-policy.sh reads each token from the one line of its block whose first non-blank text is the token's
/// name, and otherwise exits non-zero naming the token. The values it prints for the real theme file are checked in CI
/// ("Hosted-login label policy is generated from tokens.css").
/// </summary>
public class LabelPolicyTests
{
    private static readonly string Tokens = File.ReadAllText(Path.Combine(SourceRoots.Repo, "frontend/src/theme/tokens.css"));

    [Theory]
    // A token missing from the dark block.
    [InlineData("    --primary: #8caaf7;\n", "", "--primary is declared 0 times in the dark block")]
    // A token declared twice in the light block.
    [InlineData("  --background: #f4f5f7; /* canvas */\n", "  --background: #f4f5f7; /* canvas */\n  --background: #f4f5f7;\n", "--background is declared 2 times in the light block")]
    // Values on each side of #rrggbb: eight hex digits, three, and a named colour.
    [InlineData("  --destructive: #b42318;", "  --destructive: #b42318ff;", "--destructive in the light block is '#b42318ff', not #rrggbb")]
    [InlineData("    --foreground: #eef1f5;", "    --foreground: #fff;", "--foreground in the dark block is '#fff', not #rrggbb")]
    [InlineData("  --primary: #2b57c9;", "  --primary: blue;", "--primary in the light block is 'blue', not #rrggbb")]
    public async Task LabelPolicy_TokenOutsideItsBounds_ExitsNonZeroNamingTheToken(string text, string replacement, string error)
    {
        ProcessResult result = await RunOnChangedTokensAsync(text, replacement);

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains(error, result.Errors, StringComparison.Ordinal);
        Assert.Empty(result.Output);
    }

    [Fact]
    public async Task LabelPolicy_CommentLines_AreNeverRead()
    {
        // A comment line's first non-blank text is /* or *, never the token's name, so neither copy below is read.
        const string Comments = "  /* --primary: #000000; */\n  /*\n   * --primary: #000000;\n   */\n";

        ProcessResult result = await RunOnChangedTokensAsync("  --primary: #2b57c9;\n", Comments + "  --primary: #2b57c9;\n");

        Assert.Equal(0, result.ExitCode);
        using var body = JsonDocument.Parse(result.Output);
        Assert.Equal("#2b57c9", body.RootElement.GetProperty("primaryColor").GetString());
    }

    /// <summary>Runs label-policy.sh on a temporary copy of tokens.css in which TEXT, found exactly once, is replaced.</summary>
    private static async Task<ProcessResult> RunOnChangedTokensAsync(string text, string replacement)
    {
        Assert.Equal(2, Tokens.Split(text).Length);
        CancellationToken cancellation = TestContext.Current.CancellationToken;
        string copy = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(copy, Tokens.Replace(text, replacement, StringComparison.Ordinal), cancellation);
            return await Processes.RunAsync(
                "bash", [Path.Combine(SourceRoots.Repo, "infra/zitadel/provision/label-policy.sh"), copy], Processes.BaseEnvironment(), TimeSpan.FromSeconds(30));
        }
        finally
        {
            File.Delete(copy);
        }
    }
}
