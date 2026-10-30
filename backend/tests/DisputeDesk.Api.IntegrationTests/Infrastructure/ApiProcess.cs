using System.Diagnostics;

namespace DisputeDesk.Api.IntegrationTests.Infrastructure;

/// <summary>
/// Runs the built Api as its own process (<c>dotnet DisputeDesk.Api.dll</c>) until it exits, for the tests of a refused
/// start. <see cref="ApiFactory"/> cannot report one reliably: when the Api's own <c>RunAsync</c> fails and disposes the
/// host before WebApplicationFactory starts waiting on it, the factory throws <see cref="ObjectDisposedException"/>
/// instead of the start-up error (Microsoft.AspNetCore.Mvc.Testing, DeferredHost.StartAsync).
/// </summary>
internal static class ApiProcess
{
    /// <summary>A refused start exits in about a second; this leaves room for a loaded CI runner.</summary>
    private static readonly TimeSpan ExitTimeout = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Starts the Api in Development with <see cref="ApiFactory.DefaultSettings"/> and then <paramref name="settings"/>
    /// as environment variables (a null value leaves the key unset), and returns its exit code and its console output.
    /// </summary>
    public static async Task<(int ExitCode, string Output)> RunUntilExitAsync(
        IReadOnlyDictionary<string, string?> settings,
        CancellationToken cancellationToken)
    {
        var start = new ProcessStartInfo("dotnet", [Path.Combine(AppContext.BaseDirectory, "DisputeDesk.Api.dll")])
        {
            // The Api project's folder is its content root, as WebApplicationFactory sets it.
            WorkingDirectory = Path.Combine(RepoRoot.Backend, "src", "DisputeDesk.Api"),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        start.Environment["ASPNETCORE_ENVIRONMENT"] = "Development";
        // Any free loopback port, should the Api start after all.
        start.Environment["ASPNETCORE_URLS"] = "http://127.0.0.1:0";
        foreach ((string key, string? value) in ApiFactory.DefaultSettings.Concat(settings))
        {
            string variable = key.Replace(":", "__", StringComparison.Ordinal);
            if (value is null)
            {
                start.Environment.Remove(variable);
            }
            else
            {
                start.Environment[variable] = value;
            }
        }

        using Process process = Process.Start(start) ?? throw new InvalidOperationException("dotnet did not start.");
        Task<string> output = process.StandardOutput.ReadToEndAsync(cancellationToken);
        Task<string> error = process.StandardError.ReadToEndAsync(cancellationToken);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(ExitTimeout);
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException($"The Api was still running {ExitTimeout.TotalSeconds} seconds after it started.");
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }

        return (process.ExitCode, await output + await error);
    }
}
