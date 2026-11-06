# Platform checks of infra files move to infra/tests/ (Small tier)

Slice of decision record `docs/decisions/ad-011-team-folders-in-one-repo.md` (accepted 2026-11-03), change 4. Needs `tf-aws-override` merged. Backend tests stop reading `infra/` files; DevOps owns the tests of its own files. Not one assertion is lost.

## Criteria

1. **Which cases move.** Every case in `backend/tests/DisputeDesk.ArchitectureTests/PlatformScriptTests.cs` (and any other backend test) that reads a file under `infra/`: the provision script cases (roles, clients file, actor mapping, current check, development mode, web app, curl max time, retry options and the three retry runs), and the gateway template cases. (The worker health-check case reads only the root `docker-compose.yml`, so it stays; found in `tf-aws-override`.) `ApiReferenceTests` (Api.IntegrationTests) also reads `infra/images/gateway/templates/scalar-headers.conf.template`, but it compares that file with the API's own Scalar headers in process, so it needs the API and stays in backend as a cross-team consistency check; record it in ad-011 as the second accepted exception. Cases reading only root files (`docker-compose.yml`, `scripts/`) or backend source stay. The report lists every case with its old and new location; moved plus kept equals the count before.
2. **The infra test project.** `infra/tests/DisputeDesk.PlatformTests/` holds the moved cases unchanged (only the path helper and namespace change). Its project file targets the backend's target framework and uses the same test packages at the same exact versions as the backend test projects, with the same `TreatWarningsAsErrors`, nullable and analyser settings, set in the project or a `Directory.Build.props` in `infra/tests/`. It has no reference to anything under `backend/` (MSBuild imports the nearest `Directory.Build.props` above the project, and none exists between `infra/tests/` and the root; Microsoft Learn, [Customize your build](https://learn.microsoft.com/visualstudio/msbuild/customize-by-directory)). Its repo-root helper finds the root the same way `SourceRoots` does.
3. **CI.** `ci.yml`'s `platform` job builds and runs the project on every pull request and push (`dotnet test infra/tests/DisputeDesk.PlatformTests`), with the .NET SDK the backend job uses. A failing moved case fails the job: prove it by breaking one moved assertion locally and recording the failure.
4. **Backend stays green.** The backend suite passes without the moved cases; `PlatformScriptTests` keeps the remaining ones (or is removed if none remain).
5. **Docs in your paths.** `infra/README.md` says how to run the infra tests. List references outside your paths (docs, root README) for the supervisor.

## Out of scope

Rewriting any case (shell or otherwise), new assertions, the infra shell tests in `infra/deploy/aws/`.
