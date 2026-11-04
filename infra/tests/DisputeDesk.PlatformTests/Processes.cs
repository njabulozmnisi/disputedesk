using System.Diagnostics;

namespace DisputeDesk.PlatformTests;

/// <summary>The exit code and both outputs of a finished process.</summary>
internal sealed record ProcessResult(int ExitCode, string Output, string Errors);

/// <summary>
/// How the tests run docker, docker compose, bash and curl: no shell, the arguments as a list, only the environment the
/// caller gives, standard input closed, both outputs read while the process runs (so a full pipe never blocks it), and
/// the whole process tree killed when the timeout passes.
/// </summary>
internal static class Processes
{
    /// <summary>
    /// What docker, bash and curl need to find themselves and the Docker daemon (PATH, HOME, TMPDIR and Docker's own
    /// settings), copied from this process; nothing else is inherited, so no shell variable reaches a script or a
    /// Compose render.
    /// </summary>
    public static Dictionary<string, string> BaseEnvironment()
    {
        var environment = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (string name in new[] { "PATH", "HOME", "TMPDIR", "DOCKER_HOST", "DOCKER_CONTEXT", "DOCKER_CONFIG" })
        {
            if (Environment.GetEnvironmentVariable(name) is { } value)
            {
                environment[name] = value;
            }
        }

        return environment;
    }

    /// <summary>Runs the program from the repository root with the given environment and returns how it ended.</summary>
    public static async Task<ProcessResult> RunAsync(
        string program, IEnumerable<string> arguments, IReadOnlyDictionary<string, string> environment, TimeSpan timeout)
    {
        var start = new ProcessStartInfo(program)
        {
            WorkingDirectory = SourceRoots.Repo,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (string argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        start.Environment.Clear();
        foreach ((string name, string value) in environment)
        {
            start.Environment[name] = value;
        }

        using Process process = Process.Start(start) ?? throw new InvalidOperationException($"{program} did not start.");
        process.StandardInput.Close();
        Task<string> output = process.StandardOutput.ReadToEndAsync();
        Task<string> errors = process.StandardError.ReadToEndAsync();
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        deadline.CancelAfter(timeout);
        try
        {
            await process.WaitForExitAsync(deadline.Token);
            return new ProcessResult(process.ExitCode, await output.WaitAsync(deadline.Token), await errors.WaitAsync(deadline.Token));
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException($"{program} {string.Join(' ', start.ArgumentList)} did not finish within {timeout}.");
        }
    }

    /// <summary>Runs docker with the base environment.</summary>
    public static Task<ProcessResult> DockerAsync(TimeSpan timeout, params string[] arguments) =>
        RunAsync("docker", arguments, BaseEnvironment(), timeout);
}
