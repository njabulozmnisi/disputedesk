using System.Text.Json.Nodes;

namespace DisputeDesk.PlatformTests;

/// <summary>
/// The Compose model as `docker compose config --format json` renders it (Docker, "docker compose config"): the tests
/// assert on its values, so a reformatted file renders the same model and passes.
/// </summary>
internal static class ComposeModel
{
    /// <summary>
    /// Renders the files (relative to the repository root) with this env file, or an empty one so no local .env is read,
    /// and with only the base environment plus these variables, so no shell variable takes part in the interpolation.
    /// </summary>
    public static async Task<JsonNode> RenderAsync(string[] files, string? envFile = null, IReadOnlyDictionary<string, string>? variables = null)
    {
        string emptyEnvFile = Path.GetTempFileName();
        try
        {
            List<string> arguments = ["compose"];
            foreach (string file in files)
            {
                arguments.AddRange(["-f", file]);
            }

            arguments.AddRange(["--env-file", envFile ?? emptyEnvFile, "config", "--format", "json"]);
            Dictionary<string, string> environment = Processes.BaseEnvironment();
            foreach ((string name, string value) in variables ?? new Dictionary<string, string>())
            {
                environment[name] = value;
            }

            ProcessResult render = await Processes.RunAsync("docker", arguments, environment, TimeSpan.FromSeconds(60));
            Assert.True(render.ExitCode == 0, $"docker compose config failed: {render.Errors}");
            return JsonNode.Parse(render.Output) ?? throw new InvalidOperationException("docker compose config printed no model.");
        }
        finally
        {
            File.Delete(emptyEnvFile);
        }
    }
}
