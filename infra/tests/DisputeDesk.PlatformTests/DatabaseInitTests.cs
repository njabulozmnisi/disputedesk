using System.Runtime.Versioning;

namespace DisputeDesk.PlatformTests;

/// <summary>
/// Story 7.17: the deploy script's database setup against a real server. The unchanged infra/deploy/aws/deploy.sh runs from a
/// throwaway copy (<see cref="DeployCopy"/>) with aws, curl, iptables, iptables-save, systemctl and `docker compose`
/// stubbed; every `docker run` reaches the real Docker with only `--network` added, so deploy.sh's verify-full count
/// query and the unchanged infra/images/postgres/10-roles-and-databases.sh run against <see cref="DatabaseInitFixture"/>'s
/// PostgreSQL 18. No AWS endpoint is reached and no host port is bound. Every captured output is checked for passwords
/// and private keys.
/// </summary>
[UnsupportedOSPlatform("windows")] // the stubs need their execute bit
public sealed class DatabaseInitTests(DatabaseInitFixture database) : IClassFixture<DatabaseInitFixture>
{
    [Fact]
    public void Certificates_AreATestCaAndAServerCertificateForTheRdsHostAlone()
    {
        AssertNoSecret(database.OpensslOutput + database.CaText + database.ServerCertificateText);
        Assert.Contains("CA:TRUE", database.CaText, StringComparison.Ordinal);
        string[] lines = database.ServerCertificateText.Split('\n');
        int names = Array.FindIndex(lines, line => line.Contains("X509v3 Subject Alternative Name", StringComparison.Ordinal));
        Assert.Equal($"DNS:{DatabaseInitFixture.DbHost}", lines[names + 1].Trim());
    }

    [Fact]
    public async Task Server_IsShapedLikeRdsWithNoHostPort()
    {
        ProcessResult ports = await Processes.DockerAsync(TimeSpan.FromSeconds(60), "inspect", "-f", "{{json .HostConfig.PortBindings}}", database.Server);
        string postgresHasNoPassword = await database.SqlAsync("postgres", "SELECT rolpassword IS NULL FROM pg_authid WHERE rolname = 'postgres'");

        Assert.Equal("{}", ports.Output.Trim());
        Assert.Equal("t", postgresHasNoPassword);
        // dbmaster as created, before any init: CREATEDB and CREATEROLE; not superuser, REPLICATION or BYPASSRLS; no role memberships.
        Assert.Equal("f|f|f|t|t|0", database.MasterShape);
    }

    [Fact]
    public async Task ConnectionWithoutTls_IsRefusedByTheHostsslRule()
    {
        ProcessResult plain = await Processes.RunAsync(
            "docker",
            ["run", "--rm", "--network", database.Network, "-e", "PGPASSWORD", DeployCopy.PostgresImage,
                "psql", $"host={DatabaseInitFixture.DbHost} user=dbmaster dbname=postgres sslmode=disable", "-c", "SELECT 1"],
            new Dictionary<string, string>(Processes.BaseEnvironment()) { ["PGPASSWORD"] = DeployCopy.MasterPassword },
            TimeSpan.FromSeconds(120));

        AssertNoSecret(plain.Output + plain.Errors);
        Assert.NotEqual(0, plain.ExitCode);
        Assert.Matches("""no pg_hba.conf entry for host "[^"]+", user "dbmaster", database "postgres", no encryption""", plain.Errors);
    }

    [Theory]
    [InlineData(DatabaseInitFixture.DbHost, true, "certificate verify failed")]
    [InlineData(DatabaseInitFixture.WrongHost, false, $"server certificate for \"{DatabaseInitFixture.DbHost}\" does not match host name \"{DatabaseInitFixture.WrongHost}\"")]
    public async Task WrongCaOrHostName_FailsVerifyFullBeforeTheInit(string dbHost, bool wrongCa, string libpqError)
    {
        // The server's roles and databases are the same before and after: this run created none of them.
        string before = await database.SqlAsync("postgres", DeployCopy.CountQuery);

        (ProcessResult run, StubCall[] calls) = await DeployAsync(dbHost, wrongCa ? database.WrongCaFile : database.CaFile);

        Assert.NotEqual(0, run.ExitCode);
        Assert.Contains("reading the roles and databases on RDS failed", run.Errors, StringComparison.Ordinal);
        Assert.Contains(libpqError, run.Errors, StringComparison.Ordinal);
        Assert.DoesNotContain(calls, call => call.Arguments.Contains("/init/10-roles-and-databases.sh"));
        Assert.DoesNotContain(calls, call => call.Arguments is ["compose", ..]);
        Assert.Equal(before, await database.SqlAsync("postgres", DeployCopy.CountQuery));
    }

    [Fact]
    public async Task FirstThenLaterDeploy_CreatesTheRolesAndDatabasesOnceOverVerifyFull()
    {
        // One ordered test: the later deploy depends on what the first created.
        string? repositoryEnvBefore = ReadRepositoryEnv();
        Assert.Equal("0 0", await database.SqlAsync("postgres", DeployCopy.CountQuery));

        using var deploy = new DeployCopy();
        (ProcessResult first, StubCall[] firstCalls) = await DeployAsync(deploy, DatabaseInitFixture.DbHost, database.CaFile);

        Assert.True(first.ExitCode == 0, $"The first deploy failed: {first.Output}{first.Errors}");
        Assert.Contains(firstCalls, call => call.Arguments.Contains("/init/10-roles-and-databases.sh"));
        Assert.DoesNotContain("WARNING", first.Output + first.Errors, StringComparison.Ordinal);
        // The copy holds the .env and the CA bundle the run wrote; the repository's own .env is unchanged.
        Assert.NotEqual(DeployCopy.Stale, await File.ReadAllTextAsync(Path.Combine(deploy.Repo, ".env"), TestContext.Current.CancellationToken));
        Assert.Equal(
            await File.ReadAllTextAsync(database.CaFile, TestContext.Current.CancellationToken),
            await File.ReadAllTextAsync(Path.Combine(deploy.Repo, "rds-ca-bundle.pem"), TestContext.Current.CancellationToken));
        Assert.Equal(repositoryEnvBefore, ReadRepositoryEnv());

        Assert.Equal("3 2", await database.SqlAsync("postgres", DeployCopy.CountQuery));
        Assert.Equal("disputedesk disputedesk\nzitadel zitadel", await database.SqlAsync("postgres", """
            SELECT datname || ' ' || pg_get_userbyid(datdba) FROM pg_database WHERE datname IN ('disputedesk', 'zitadel') ORDER BY datname
            """));
        // The init's own GRANT gives dbmaster SET on both owners, which CREATE DATABASE ... OWNER needs, and INHERIT,
        // which granting and revoking on their databases and schema as the owner needs.
        Assert.Equal("disputedesk|t\nzitadel|t", await database.SqlAsync("postgres", """
            SELECT r.rolname, m.inherit_option FROM pg_auth_members m JOIN pg_roles r ON r.oid = m.roleid
            WHERE m.member = 'dbmaster'::regrole AND m.set_option ORDER BY r.rolname
            """));
        Assert.Equal("{default_transaction_read_only=on}", await database.SqlAsync("postgres", "SELECT rolconfig FROM pg_roles WHERE rolname = 'qa_readonly'"));
        // CONNECT for qa_readonly on disputedesk and zitadel, and for zitadel on disputedesk.
        Assert.Equal("t|f|f", await database.SqlAsync("postgres", """
            SELECT has_database_privilege('qa_readonly', 'disputedesk', 'CONNECT'), has_database_privilege('qa_readonly', 'zitadel', 'CONNECT'),
              has_database_privilege('zitadel', 'disputedesk', 'CONNECT')
            """));
        // PostgreSQL 15+ gives every role USAGE on public by default, so zitadel's f shows the init's REVOKE took effect.
        Assert.Equal("t|f", await database.SqlAsync("disputedesk", "SELECT has_schema_privilege('qa_readonly', 'public', 'USAGE'), has_schema_privilege('zitadel', 'public', 'USAGE')"));
        Assert.Contains("qa_readonly=r/disputedesk", await database.SqlAsync("disputedesk", "SELECT defaclacl FROM pg_default_acl"), StringComparison.Ordinal);

        (ProcessResult later, StubCall[] laterCalls) = await DeployAsync(DatabaseInitFixture.DbHost, database.CaFile);

        Assert.True(later.ExitCode == 0, $"The later deploy failed: {later.Output}{later.Errors}");
        Assert.DoesNotContain(laterCalls, call => call.Arguments.Contains("/init/10-roles-and-databases.sh"));
        Assert.Contains(laterCalls, call => call.Arguments.SequenceEqual(DeployCopy.ComposeUpCall));
    }

    /// <summary>Fails if the text holds any password of the run or a private key.</summary>
    internal static void AssertNoSecret(string text)
    {
        foreach (string secret in DeployCopy.Secrets.Append("PRIVATE KEY"))
        {
            Assert.False(text.Contains(secret, StringComparison.Ordinal), "A captured output holds a password or a private key.");
        }
    }

    /// <summary>Runs deploy.sh in a fresh copy with DB_HOST and the truststore answering the CA file.</summary>
    private async Task<(ProcessResult Run, StubCall[] Calls)> DeployAsync(string dbHost, string caFile)
    {
        using var deploy = new DeployCopy();
        return await DeployAsync(deploy, dbHost, caFile);
    }

    private async Task<(ProcessResult Run, StubCall[] Calls)> DeployAsync(DeployCopy deploy, string dbHost, string caFile)
    {
        deploy.Set("DB_HOST", dbHost);
        deploy.CaBundle = await File.ReadAllTextAsync(caFile, TestContext.Current.CancellationToken);
        deploy.Stubs.PassDockerRunThrough(database.Network);

        ProcessResult run = await deploy.RunAsync();
        StubCall[] calls = deploy.Stubs.Calls();
        AssertNoSecret(run.Output + run.Errors + string.Join('\n', calls.SelectMany(call => call.Arguments)));
        return (run, calls);
    }

    /// <summary>The repository's own .env, or null when it has none.</summary>
    private static string? ReadRepositoryEnv()
    {
        string env = Path.Combine(SourceRoots.Repo, ".env");
        return File.Exists(env) ? File.ReadAllText(env) : null;
    }
}
