namespace DisputeDesk.PlatformTests;

/// <summary>
/// The inputs of infra/aws/compose.yml (story 7.4): their names and bounds as infra/aws/input-bounds.sh defines them,
/// read by sourcing that file in bash, and the dummy values in infra/aws/compose.test.env.
/// </summary>
internal static class AwsInputs
{
    public const string TestEnvFile = "infra/aws/compose.test.env";

    /// <summary>compose.test.env's NAME=value lines, in file order.</summary>
    public static Dictionary<string, string> TestValues() =>
        File.ReadAllLines(Path.Combine(SourceRoots.Repo, TestEnvFile))
            .Where(line => line.Length > 0 && !line.StartsWith('#'))
            .Select(line => line.Split('=', 2))
            .ToDictionary(pair => pair[0], pair => pair[1], StringComparer.Ordinal);

    /// <summary>input-bounds.sh's AWS_INPUTS, in order.</summary>
    public static async Task<string[]> NamesAsync()
    {
        ProcessResult names = await BashAsync(". infra/aws/input-bounds.sh && printf '%s\\n' \"${AWS_INPUTS[@]}\"");
        Assert.True(names.ExitCode == 0, $"Reading AWS_INPUTS failed: {names.Errors}");
        return names.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
    }

    /// <summary>Whether input-bounds.sh's within_bounds accepts the value for the input.</summary>
    public static async Task<bool> WithinBoundsAsync(string name, string value) =>
        (await BashAsync(". infra/aws/input-bounds.sh && within_bounds \"$1\" \"$2\"", name, value)).ExitCode == 0;

    private static Task<ProcessResult> BashAsync(string script, params string[] arguments) =>
        Processes.RunAsync("bash", ["-c", script, "bounds", .. arguments], Processes.BaseEnvironment(), TimeSpan.FromSeconds(30));
}
