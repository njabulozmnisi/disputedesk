using System.Runtime.Versioning;

namespace DisputeDesk.PlatformTests;

/// <summary>One call a stub received: the command, its arguments, its standard input and its environment.</summary>
internal sealed record StubCall(string Command, string[] Arguments, string Input, IReadOnlyDictionary<string, string> Environment);

/// <summary>
/// Stand-ins for commands, in a temporary folder placed first on a child process's PATH. Each stub records every call
/// (in one sequence across all the stubs) and answers only the exact argument lists the test set up with
/// <see cref="Answer"/>; any other call prints "stub: unexpected call: COMMAND ARGUMENTS" and exits 97, so a changed
/// command never gets a success-shaped answer. Optionally `docker run` goes through to the real Docker with
/// `--network` added, while `docker compose` stays stubbed. Dispose removes the folder.
/// </summary>
[UnsupportedOSPlatform("windows")] // the stubs need their execute bit
internal sealed class PathStubs : IDisposable
{
    // Each call takes the next free number (set -C makes taking it atomic), records itself, then answers.
    private const string Stub = """
        #!/usr/bin/env bash
        dir=$(dirname "$0"); command=$(basename "$0")
        n=1; until (set -C; : > "$dir/call.$n.args") 2>/dev/null; do n=$((n + 1)); done
        [ $# -eq 0 ] || printf '%s\n' "$@" > "$dir/call.$n.args"
        printf '%s' "$command" > "$dir/call.$n.command"
        env > "$dir/call.$n.env"
        if [ "$command" = docker ] && [ "${1-}" = run ] && [ -e "$dir/docker-run-network" ]; then
          shift
          exec "$(cat "$dir/real-docker")" run --network "$(cat "$dir/docker-run-network")" "$@"
        fi
        cat > "$dir/call.$n.stdin"
        for answer in "$dir"/answer.*.args; do
          [ -e "$answer" ] || break
          stem=${answer%.args}
          if [ "$(cat "$stem.command")" = "$command" ] && cmp -s "$answer" "$dir/call.$n.args"; then
            cat "$stem.out"; cat "$stem.err" >&2
            exit "$(cat "$stem.exit")"
          fi
        done
        echo "stub: unexpected call: $command $*" >&2
        exit 97
        """;

    private int _answers;

    /// <summary>Writes a stub for each command; until a test sets up an answer, every call to one exits 97.</summary>
    public PathStubs(params string[] commands)
    {
        foreach (string command in commands)
        {
            string stub = Path.Combine(Directory, command);
            File.WriteAllText(stub, Stub + "\n");
            File.SetUnixFileMode(stub, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
    }

    /// <summary>The temporary folder that holds the stubs and their records.</summary>
    public string Directory { get; } = System.IO.Directory.CreateTempSubdirectory("disputedesk-stubs-").FullName;

    /// <summary>A call to the command with exactly these arguments prints the output and errors and exits with the code.</summary>
    public void Answer(string command, string[] arguments, string output = "", string errors = "", int exit = 0)
    {
        _answers++;
        string stem = Path.Combine(Directory, $"answer.{_answers}");
        File.WriteAllText($"{stem}.command", command);
        File.WriteAllText($"{stem}.args", string.Concat(arguments.Select(argument => argument + "\n")));
        File.WriteAllText($"{stem}.out", output);
        File.WriteAllText($"{stem}.err", errors);
        File.WriteAllText($"{stem}.exit", $"{exit}");
    }

    /// <summary>Every `docker run` goes to the real Docker, as `docker run --network NETWORK ...`.</summary>
    public void PassDockerRunThrough(string network)
    {
        string realDocker = (System.Environment.GetEnvironmentVariable("PATH") ?? "")
            .Split(Path.PathSeparator)
            .Select(folder => Path.Combine(folder, "docker"))
            .First(File.Exists);
        File.WriteAllText(Path.Combine(Directory, "real-docker"), realDocker);
        File.WriteAllText(Path.Combine(Directory, "docker-run-network"), network);
    }

    /// <summary>The base environment with the stubs first on PATH.</summary>
    public Dictionary<string, string> Environment()
    {
        Dictionary<string, string> environment = Processes.BaseEnvironment();
        environment["PATH"] = $"{Directory}{Path.PathSeparator}{environment["PATH"]}";
        return environment;
    }

    /// <summary>Every call the stubs received, in order.</summary>
    public StubCall[] Calls()
    {
        List<StubCall> calls = [];
        for (int call = 1; File.Exists(Path.Combine(Directory, $"call.{call}.args")); call++)
        {
            string stem = Path.Combine(Directory, $"call.{call}");
            calls.Add(new StubCall(
                File.ReadAllText($"{stem}.command"),
                File.ReadAllLines($"{stem}.args"),
                File.Exists($"{stem}.stdin") ? File.ReadAllText($"{stem}.stdin") : "",
                ReadEnvironment($"{stem}.env")));
        }

        return [.. calls];
    }

    /// <summary>The calls to one command, in order.</summary>
    public StubCall[] Calls(string command) => [.. Calls().Where(call => call.Command == command)];

    public void Dispose() => System.IO.Directory.Delete(Directory, recursive: true);

    /// <summary>The NAME=value lines `env` printed (no value in these tests spans lines).</summary>
    private static Dictionary<string, string> ReadEnvironment(string file) =>
        File.ReadAllLines(file)
            .Select(line => line.Split('=', 2))
            .Where(pair => pair.Length == 2)
            .GroupBy(pair => pair[0], StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Last()[1], StringComparer.Ordinal);
}
