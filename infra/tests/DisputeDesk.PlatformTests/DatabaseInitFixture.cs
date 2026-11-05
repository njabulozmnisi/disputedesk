using System.Runtime.Versioning;

namespace DisputeDesk.PlatformTests;

/// <summary>
/// A throwaway PostgreSQL 18 shaped like RDS (story 7.17), on an internal Docker network with no host port: TLS with a
/// test CA and a server certificate for an RDS-style host name, a pg_hba.conf that admits TCP only over TLS, and a
/// master user with CREATEDB and CREATEROLE but not superuser. A second network alias reaches it under a name the
/// certificate does not hold, and an unrelated CA stands in for the wrong bundle. Started once per class; everything it
/// starts is removed when the class ends, also when a start fails.
/// </summary>
/// <remarks>
/// shortcut: a local CA and a CREATEROLE master stand in for RDS's certificate chain and rds_superuser; replace with README post-deploy step 6 once the account exists
/// </remarks>
[UnsupportedOSPlatform("windows")] // the stubs need their execute bit
public sealed class DatabaseInitFixture : IAsyncLifetime
{
    /// <summary>The shape of an RDS endpoint (dev.tftest.hcl's): the server's alias and its certificate's only DNS name.</summary>
    public const string DbHost = "disputedesk-dev.abcdefghijkl.eu-west-1.rds.amazonaws.com";

    /// <summary>The same server under a name the certificate does not hold.</summary>
    public const string WrongHost = "wrong-name.disputedesk.test";

    private static readonly TimeSpan CommandTimeout = TimeSpan.FromSeconds(120);

    private readonly string _work = Directory.CreateTempSubdirectory("disputedesk-rds-init-test-").FullName;

    /// <summary>The network's name, and the prefix of the server's.</summary>
    public string Network { get; } = $"disputedesk-rds-init-test-{Environment.ProcessId}";

    public string Server => $"{Network}-server";

    public string CaFile => Path.Combine(_work, "tls/ca.crt");

    public string WrongCaFile => Path.Combine(_work, "tls/wrong-ca.crt");

    /// <summary>openssl's output, and `openssl x509 -text` of the test CA and the server certificate.</summary>
    public string OpensslOutput { get; private set; } = "";

    public string CaText { get; private set; } = "";

    public string ServerCertificateText { get; private set; } = "";

    /// <summary>
    /// dbmaster's rolsuper, rolreplication, rolbypassrls, rolcreatedb, rolcreaterole and role membership count, read once
    /// before any test runs the init, which makes dbmaster a member of the roles it creates (createrole_self_grant).
    /// </summary>
    public string MasterShape { get; private set; } = "";

    public async ValueTask InitializeAsync()
    {
        try
        {
            await StartAsync();
        }
        catch
        {
            await DisposeAsync();
            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        // Every container on the network, then the network and the folder; anything left fails the class.
        ProcessResult containers = await Processes.DockerAsync(CommandTimeout, "ps", "-aq", "--filter", $"network={Network}");
        List<string> left = [];
        foreach (string container in containers.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            if ((await Processes.DockerAsync(CommandTimeout, "rm", "-fv", container)).ExitCode != 0)
            {
                left.Add($"container {container}");
            }
        }

        if ((await Processes.DockerAsync(CommandTimeout, "network", "inspect", Network)).ExitCode == 0
            && (await Processes.DockerAsync(CommandTimeout, "network", "rm", Network)).ExitCode != 0)
        {
            left.Add($"network {Network}");
        }

        Directory.Delete(_work, recursive: true);
        Assert.True(containers.ExitCode == 0 && left.Count == 0, $"Cleanup could not remove: {string.Join(", ", left)} {containers.Errors}");
    }

    /// <summary>
    /// SQL over the server's socket as postgres (peer authentication): its tuples, unaligned, without the last newline.
    /// Fails if the query fails or its output holds a secret.
    /// </summary>
    public async Task<string> SqlAsync(string database, string sql)
    {
        ProcessResult result = await Processes.DockerAsync(CommandTimeout, "exec", "-u", "postgres", Server, "psql", "-v", "ON_ERROR_STOP=1", "-tA", "-d", database, "-c", sql);
        DatabaseInitTests.AssertNoSecret(result.Output + result.Errors);
        Assert.True(result.ExitCode == 0, $"The query failed: {result.Errors}");
        return result.Output.TrimEnd('\n');
    }

    private async Task StartAsync()
    {
        await WriteCertificatesAsync();
        // The only TCP rule needs TLS, standing in for rds.force_ssl = 1; the socket admits only the postgres OS user.
        await File.WriteAllTextAsync(Path.Combine(_work, "server/pg_hba.conf"), """
            local   all  postgres        peer
            hostssl all  all       all   scram-sha-256

            """, TestContext.Current.CancellationToken);

        await MustAsync("network", "create", "--internal", Network);
        // Started as root, so it can install the key as a postgres-owned 0600 file (PostgreSQL refuses a key that group or
        // world can read) whoever owns the mounted copy, then hands over to the image's entrypoint. The entrypoint
        // initialises only with a superuser password or with POSTGRES_HOST_AUTH_METHOD=trust; trust leaves postgres
        // without a password and reaches only the data directory's pg_hba.conf, which hba_file replaces (the refused plain
        // connection shows it).
        await MustAsync("run", "-d", "--name", Server, "--network", Network, "--network-alias", DbHost, "--network-alias", WrongHost,
            "-e", "POSTGRES_HOST_AUTH_METHOD=trust", "-v", $"{Path.Combine(_work, "server")}:/run/server-files:ro", "--entrypoint", "sh", DeployCopy.PostgresImage, "-c", """
                set -e
                install -d -o postgres -g postgres -m 0700 /etc/postgresql
                install -o postgres -g postgres -m 0600 /run/server-files/server.key /etc/postgresql/server.key
                install -o postgres -g postgres -m 0644 /run/server-files/server.crt /etc/postgresql/server.crt
                install -o postgres -g postgres -m 0644 /run/server-files/pg_hba.conf /etc/postgresql/pg_hba.conf
                exec docker-entrypoint.sh postgres -c ssl=on -c ssl_cert_file=/etc/postgresql/server.crt \
                  -c ssl_key_file=/etc/postgresql/server.key -c hba_file=/etc/postgresql/pg_hba.conf
                """);
        await WaitUntilReadyAsync();

        ProcessResult master = await Processes.RunAsync(
            "docker", ["exec", "-i", "-e", "RDS_MASTER_PASSWORD", "-u", "postgres", Server, "psql", "-v", "ON_ERROR_STOP=1", "-q", "-d", "postgres"],
            new Dictionary<string, string>(Processes.BaseEnvironment()) { ["RDS_MASTER_PASSWORD"] = DeployCopy.MasterPassword },
            CommandTimeout,
            """
            \getenv master_password RDS_MASTER_PASSWORD
            CREATE ROLE dbmaster LOGIN CREATEDB CREATEROLE PASSWORD :'master_password';

            """);
        DatabaseInitTests.AssertNoSecret(master.Output + master.Errors);
        Assert.True(master.ExitCode == 0, $"Creating dbmaster failed: {master.Errors}");
        MasterShape = await SqlAsync("postgres", """
            SELECT rolsuper, rolreplication, rolbypassrls, rolcreatedb, rolcreaterole,
              (SELECT count(*) FROM pg_auth_members WHERE member = r.oid) FROM pg_roles r WHERE rolname = 'dbmaster'
            """);
    }

    /// <summary>A test CA, a server certificate for <see cref="DbHost"/> alone, and an unrelated CA.</summary>
    private async Task WriteCertificatesAsync()
    {
        string tls = Directory.CreateDirectory(Path.Combine(_work, "tls")).FullName;
        string server = Directory.CreateDirectory(Path.Combine(_work, "server")).FullName;
        // Only req -x509, req -new and x509 -req -CA -extfile, which LibreSSL 3.3 and OpenSSL 3 and 4 all accept.
        await File.WriteAllTextAsync(Path.Combine(tls, "openssl.cnf"), """
            [req]
            distinguished_name = subject
            x509_extensions = ca
            [subject]
            [ca]
            basicConstraints = critical, CA:TRUE
            keyUsage = critical, keyCertSign, cRLSign
            subjectKeyIdentifier = hash

            """, TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(Path.Combine(tls, "server.ext"), $"subjectAltName = DNS:{DbHost}\n", TestContext.Current.CancellationToken);
        foreach (string ca in new[] { "ca", "wrong-ca" })
        {
            await OpensslAsync("req", "-x509", "-newkey", "rsa:2048", "-nodes", "-days", "1", "-config", $"{tls}/openssl.cnf", "-subj", $"/CN=DisputeDesk {ca} test",
                "-keyout", $"{tls}/{ca}.key", "-out", $"{tls}/{ca}.crt");
        }

        await OpensslAsync("req", "-new", "-newkey", "rsa:2048", "-nodes", "-config", $"{tls}/openssl.cnf", "-subj", $"/CN={DbHost}",
            "-keyout", $"{server}/server.key", "-out", $"{tls}/server.csr");
        await OpensslAsync("x509", "-req", "-in", $"{tls}/server.csr", "-CA", $"{tls}/ca.crt", "-CAkey", $"{tls}/ca.key", "-set_serial", "1", "-days", "1",
            "-extfile", $"{tls}/server.ext", "-out", $"{server}/server.crt");
        CaText = (await OpensslAsync("x509", "-in", $"{tls}/ca.crt", "-noout", "-text")).Output;
        ServerCertificateText = (await OpensslAsync("x509", "-in", $"{server}/server.crt", "-noout", "-text")).Output;
    }

    private async Task<ProcessResult> OpensslAsync(params string[] arguments)
    {
        ProcessResult result = await Processes.RunAsync("openssl", arguments, Processes.BaseEnvironment(), CommandTimeout);
        OpensslOutput += result.Output + result.Errors;
        Assert.True(result.ExitCode == 0, $"openssl {arguments[0]} failed.");
        return result;
    }

    /// <summary>
    /// Over TCP, so the entrypoint's socket-only setup server does not count as ready. A server that stops fails at once,
    /// showing only its state and exit code, never its log.
    /// </summary>
    private async Task WaitUntilReadyAsync()
    {
        for (int attempt = 0; attempt < 60; attempt++)
        {
            if ((await Processes.DockerAsync(CommandTimeout, "exec", Server, "pg_isready", "-q", "-h", DbHost)).ExitCode == 0)
            {
                return;
            }

            string state = (await Processes.DockerAsync(CommandTimeout, "inspect", "-f", "{{.State.Status}} {{.State.ExitCode}}", Server)).Output.Trim();
            Assert.True(state.StartsWith("running", StringComparison.Ordinal), $"The PostgreSQL server stopped before it was ready: {state}");
            await Task.Delay(TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken);
        }

        Assert.Fail("The PostgreSQL server did not become ready within a minute.");
    }

    private static async Task MustAsync(params string[] arguments)
    {
        ProcessResult result = await Processes.DockerAsync(CommandTimeout, arguments);
        Assert.True(result.ExitCode == 0, $"docker {arguments[0]} {arguments[1]} failed: {result.Errors}");
    }
}
