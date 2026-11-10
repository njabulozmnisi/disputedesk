# aws-admin-lockdown: fix round 1

The high and medium findings from the post-build stages, for one fresh builder. Records:
`aws-admin-lockdown-review.md`, `aws-admin-lockdown-verify.md`, `aws-admin-lockdown-security.md`.

Human decision (2026-11-10) on H1: close public organization registration and harden the step.

## H1 (security, high): public organization registration

Zitadel's `DisallowPublicOrgRegistration` restriction is unset, and the AWS gateway forwards
`/ui/login/register/org`. An anonymous visitor can create an organization and become its human `ORG_OWNER` in state
`INITIAL`. `DeactivateUser` refuses `INITIAL` (`CantDeactivateInitial`), so `provision.sh` exits 1 and every AWS deploy
fails until the organization is removed by hand. With the Mailpit inbox public, the registrant can also finish the
init code and keep `ORG_OWNER` with the management API.

Fix:
- Close public organization registration on the running instance: Admin API `SetRestrictions`
  (`disallowPublicOrgRegistration: true`), called idempotently from `provision.sh`, on AWS and locally unless there is a
  reason not to (customer self-registration inside the DisputeDesk organization is a different feature and must keep
  working; step 5 of the README's Try it depends on it).
- Close it for a fresh instance through Zitadel's DefaultInstance restrictions setting
  (`ZITADEL_DEFAULTINSTANCE_RESTRICTIONS_DISALLOWPUBLICORGREGISTRATION`); confirm the key for v4.20.0.
- Scope the lock-down to the DisputeDesk organization and the instance (`ListAdministrators` filters on the user's
  organization and resource), so a user of another organization can never fail the deploy.
- A test for each, failing before the fix.

## M1 (security, medium; review low 1 and verify low 1 are the same point): no postcondition

The jq filters use `[]?` and `.result[]?`, so an unexpected response shape deactivates nothing and still logs
success. Only the first page (100) is read, sorted by creation date descending, so the first instance's admin is the
first to drop off.

Fix:
- Require the provisioner itself in the administrator list (proves the list was read).
- Fail when `pagination.totalResult` is larger than the rows returned.
- List again afterwards and fail if any in-scope human administrator is not inactive.
- Remove the `# shortcut:` for the first page if this closes it.
- Tests for each, failing before the fix.

## Also in this round (review low 2, security low 1)

Post-deploy check 12 (`infra/terraform/README.md`) can pass vacuously: an inactive user and an unknown login name
both skip the password step. Replace it with an API check run with the provisioner's token from the zitadel
container: `ListAdministrators` returns exactly the provisioner (active machine) and `admin` (inactive), only the
DisputeDesk organization exists, and the only machine users are `provisioner` and `operations-tools`.

## Not in this round

- Mailpit's Send API uses the published UI login (`MP_SEND_API_AUTH` unset): security low 2, to `lows.md`.
- A refresh token issued to `admin` before deactivation: unverified, covered by the supervisor's event check before
  publishing.
