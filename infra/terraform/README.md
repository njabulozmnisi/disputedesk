# The AWS dev environment

**This procedure has not been run.** The AWS account does not exist yet, so the Terraform here has been checked only
with `terraform fmt`, `terraform validate` and `terraform test` plans against mocked providers, and no AWS command has
been run (decided 2026-10-30). Before any real use, run the procedure below once, end to end, with its post-deploy
checks.

One environment (AD-28): one EC2 instance runs the same Compose stack as local development plus
`infra/deploy/aws/compose.yml`, RDS PostgreSQL holds both databases, and three CloudFront distributions (app, auth, mail)
are the only public entry. GitHub Actions deploys a commit through SSM Run Command with an OIDC role and no stored key.

| Folder | What |
|---|---|
| `bootstrap/` | The S3 bucket for `envs/dev`'s state. Local state, applied once; `terraform destroy` of `envs/dev` leaves it. |
| `envs/dev/` | The one root: the KMS key, the SSM values the deploy script reads, and the modules below. |
| `modules/network` | VPC, a public subnet with an internet gateway (no NAT gateway), two private subnets for RDS. |
| `modules/compute` | The c7i-flex.large instance (AL2023, IMDSv2, no SSH), its Elastic IP, security group, role and user data. |
| `modules/database` | RDS PostgreSQL 18 on db.t4g.micro, private, TLS required; the master password through write-only arguments. |
| `modules/edge` | The three CloudFront distributions and the origin-verify value. |
| `modules/deploy-role` | GitHub's OIDC provider, the deploy role, and the Run Command document it may send. The role trusts GitHub's immutable subject for `main`, `repo:<owner>@<owner-id>/<name>@<repo-id>:ref:refs/heads/main`, pinned by `envs/dev/tests/deploy_role.tftest.hcl` and confirmed once against the repository's OIDC settings before the first deploy (step 8). |
| `modules/observability` | The collector's log group and stream, and X-Ray Transaction Search. |
| `modules/budget` | The monthly cost budget and its two email alerts. |

Every IAM policy document is a `<name>.json.tftpl` file beside its `main.tf` (the two trust policies and the two role
policies in `modules/deploy-role` and `modules/compute`, the Transaction Search policy in `modules/observability`, the
state bucket policy in `bootstrap`), rendered with `templatefile()`. Each is valid JSON before rendering, with its
placeholders only inside JSON strings, so `IamNamesTests` in the infra test project (`infra/tests`) reads every action
and condition key from the files with `System.Text.Json` and checks it against AWS's public service reference (no AWS
call). The values are ARNs, IDs, the region, paths and the OIDC subject, none of which holds a `"` or `\`; a value that
could would need `jsonencode()`. The mocked Terraform tests pin each rendered policy.

What to know before applying it:

- **The fixture logins are live.** The customer and operations logins whose passwords are published in
  [`backend/fixtures/local.json`](../../backend/fixtures/local.json) work on the AWS demo. It holds only synthetic data.
- **CloudFront reaches the origin over plain HTTP**, and the default `*.cloudfront.net` certificate allows TLS 1.0
  viewers. There is no WAF. An HTTPS or VPC origin, a domain with an ACM certificate (TLS 1.2 minimum) and WAF come
  before any real data (production path).
- **The instance's managed policies are broader than least privilege.** `AmazonSSMManagedInstanceCore` can read every
  SSM parameter in the account and `CloudWatchAgentServerPolicy` can write to any log group. This is accepted because
  the account is dedicated to this environment; AWS's documented minimal instance policy is a production-path item.
- **A compromise of the Api or the collector exposes every deploy secret.** Those two containers reach the instance
  metadata service, so they hold the instance role, which can read and decrypt everything under `/disputedesk/dev`:
  the RDS master password, Zitadel's master key and every other SecureString. Separating the deploy's secrets from the
  containers' credentials is a production-path item.
- **The gateway's realip file can drift.** The deploy script writes CloudFront's origin-facing ranges from
  `ip-ranges.json` at each deploy; the security group's managed prefix list follows AWS's changes at once, the file only
  at the next deploy.
- **One secret is in Terraform state:** the origin-verify value, because CloudFront's origin header needs it in the
  plan. The RDS master password reaches RDS and SSM through write-only arguments and never enters state. The state
  bucket is private, versioned, encrypted and HTTPS only.
- **The Zitadel console is not exposed in AWS.** The gateway answers 404 on `/ui/console`; nothing in the demo needs it.

## Tools

- Terraform 1.17.1 (CI's version; any 1.11 or later works).
- AWS CLI 2.32.0 or later, for `aws login`.
- `gh`, signed in to the repository, for the repository variables and the deploy.

Every command below runs from the repository root unless it says otherwise, with `REGION` set to one of `us-east-1`,
`us-west-2`, `eu-west-1` or `eu-central-1`:

```sh
export REGION=eu-west-1
```

## 1. The account

1. Create a new AWS account on the free plan.
2. Turn on MFA for the root user. Create no access keys for the root user, ever.

## 2. The administrator's sign-in (temporary credentials only)

The maintainer signs in as an IAM user with a console password and MFA, and uses `aws login`, which turns that console
sign-in into temporary credentials for the CLI (AWS CLI, *Login for AWS local development using console credentials*).
IAM Identity Center is not used: an account instance of Identity Center gives no access to AWS accounts, and an
organization instance needs AWS Organizations, which ends the free plan's credits (IAM Identity Center, *Account
instances*; AWS Free Tier FAQs).

1. Signed in as root, create the IAM user `admin` with console access and a password, attach `AdministratorAccess` and
   `SignInLocalDevelopmentAccess`, and give it **no access keys**.
2. Sign in to the console as `admin` and assign an MFA device to it. Sign out of the root user.
3. On the maintainer's machine:

   ```sh
   aws login --profile disputedesk --region "$REGION"
   export AWS_PROFILE=disputedesk
   aws sts get-caller-identity          # shows the admin user's ARN
   ```

   If Terraform's AWS provider does not yet read the `aws login` session (step 4's `terraform plan` stops with "No
   valid credential sources found"), give it the same temporary credentials through a process profile, the workaround
   the AWS CLI page for `aws login` gives (*Sharing Login credentials as process credentials*). The CLI keeps
   refreshing them:

   ```sh
   cat >> ~/.aws/config <<EOF

   [profile disputedesk-process]
   credential_process = aws configure export-credentials --profile disputedesk --format process
   region = $REGION
   EOF
   export AWS_PROFILE=disputedesk-process
   aws sts get-caller-identity          # the same admin user's ARN
   ```

Never run `aws iam create-access-key`, and never put static keys into `aws configure` (AWS IAM, *Security best
practices*, temporary credentials for people).

## 3. The state bucket (once)

```sh
cd infra/terraform/bootstrap
terraform init
terraform apply -var "aws_region=$REGION" -var "state_bucket_name=disputedesk-tfstate-<something unique>"
cd -
```

`bootstrap/terraform.tfstate` is local and ignored by git. Keep it to destroy the bucket at the very end.

## 4. The environment

First check that the region offers the instance and database classes (each command lists at least one row):

```sh
aws ec2 describe-instance-type-offerings --region "$REGION" --filters Name=instance-type,Values=c7i-flex.large
aws rds describe-orderable-db-instance-options --region "$REGION" --engine postgres --engine-version 18 \
  --db-instance-class db.t4g.micro --query 'OrderableDBInstanceOptions[0].EngineVersion'
```

The budget's address is personal data: pass it through the environment, never in a committed file. The repository's
name, in GitHub's exact case, and its two numeric IDs come from GitHub's API: the deploy role's subject is built from
them and compared case-sensitively.

```sh
cd infra/terraform/envs/dev
terraform init -backend-config="bucket=disputedesk-tfstate-<something unique>" -backend-config="region=$REGION"
export TF_VAR_aws_region=$REGION
export TF_VAR_github_repository=$(gh api repos/<owner>/<name> --jq .full_name)
export TF_VAR_github_repository_owner_id=$(gh api repos/<owner>/<name> --jq .owner.id)
export TF_VAR_github_repository_id=$(gh api repos/<owner>/<name> --jq .id)
export TF_VAR_budget_limit_usd=10
export TF_VAR_budget_alert_email=<an address you read>
plan_file=$(mktemp -d)/dev.tfplan
terraform plan -out="$plan_file"
terraform apply "$plan_file"
rm -rf "$(dirname "$plan_file")"
cd -
```

The saved plan holds the origin-verify value and the budget address in clear text (Terraform, *Command: plan*), so it
is written to a temporary directory outside the repository and removed after the apply; `*.tfplan` is also ignored by
git. Plans and applies run only from this machine, never from CI (AD-28).

## 5. The secrets a person creates

Each is a SecureString under `/disputedesk/dev`, encrypted with the environment's own KMS key, generated (never chosen
by hand) within the bound `infra/deploy/aws/input-bounds.sh` checks. The value goes to the CLI from a file of mode 0600, so it
is never on a command line.

```sh
SSM_PATH=/disputedesk/dev
KEY_ARN=$(aws ssm get-parameter --name "$SSM_PATH/DATAPROTECTION_KMS_KEY_ID" --query Parameter.Value --output text)
letters_and_digits(){ LC_ALL=C tr -dc 'A-Za-z0-9' < /dev/urandom | head -c "$1"; }
put_secret(){ # NAME: the value on standard input
  local file
  file=$(mktemp)          # mode 0600
  cat > "$file"
  aws ssm put-parameter --name "$SSM_PATH/$1" --type SecureString --key-id "$KEY_ARN" --value "file://$file" \
    --overwrite > /dev/null
  rm -f "$file"
}

letters_and_digits 64 | put_secret APP_DB_PASSWORD                  # [A-Za-z0-9]{32,128}
letters_and_digits 64 | put_secret ZITADEL_DB_PASSWORD              # [A-Za-z0-9]{32,128}
letters_and_digits 64 | put_secret LOGIN_SESSION_COOKIE_SECRET      # [A-Za-z0-9]{32,128}
letters_and_digits 64 | put_secret QA_DB_PASSWORD                   # [A-Za-z0-9]{32,128}
letters_and_digits 32 | put_secret ZITADEL_MASTERKEY                # exactly 32 of [A-Za-z0-9]
{ letters_and_digits 60; printf 'Aa1-'; } | put_secret ZITADEL_ADMIN_PASSWORD   # [A-Za-z0-9-]{32,128}, each class
openssl rand -base64 32 | tr -d '\n' | put_secret LINKING_OTP_HMAC_KEY        # base64 of 32 bytes
{ printf 'mailpit:'; letters_and_digits 64; } | put_secret MAILPIT_UI_AUTH     # user:password, [A-Za-z0-9]
```

Terraform wrote the rest of what the deploy reads: `APP_HOST`, `AUTH_HOST`, `MAILPIT_HOST`, `DB_HOST`,
`OTEL_LOG_GROUP`, `DATAPROTECTION_SSM_PATH`, `DATAPROTECTION_KMS_KEY_ID`, `GATEWAY_ORIGIN_VERIFY` and
`RDS_MASTER_PASSWORD`. The Api writes its Data Protection key ring under `/disputedesk-dataprotection/dev`, outside
`/disputedesk/dev`, so the deploy never reads it.

## 6. The budget alert

AWS Budgets emails the address in `TF_VAR_budget_alert_email` at 80 % of the actual monthly cost and at 100 % of the
forecast, without credits. Check the recipient, and accept any confirmation email that arrives:

```sh
aws budgets describe-budgets --account-id "$(aws sts get-caller-identity --query Account --output text)" \
  --query 'Budgets[].[BudgetName,BudgetLimit.Amount,CostTypes.IncludeCredit]'
```

A budget alerts; it does not stop spending.

## 7. The images

Story 7.3's `publish` job pushes five packages to GHCR from main: `disputedesk-api`, `disputedesk-worker`,
`disputedesk-gateway`, `disputedesk-core-banking` and `disputedesk-zitadel`. GHCR creates packages private. After the
first publish, set each one public once: the package's page, **Package settings**, **Change visibility**, **Public**
(GitHub, *Configuring a package's access control and visibility*). The instance then pulls them without a credential.
While one is still private, `deploy.yml` fails at its digest lookup, before it sends anything to AWS.

## 8. The repository variables and the first deploy

First, once, check the subject the deploy role trusts against the repository's OIDC settings (GitHub REST API, *Get the
customization template for an OIDC subject claim for a repository*):

```sh
gh api repos/<owner>/<name>/actions/oidc/customization/sub
gh api repos/<owner>/<name> --jq '.owner.login, .owner.id, .name, .id'
```

`use_immutable_subject` must be `true`, and `sub_claim_prefix` must carry the same owner, owner ID, repository name and
repository ID as the subject pinned in `envs/dev/tests/deploy_role.tftest.hcl`,
`repo:<owner>@<owner-id>/<name>@<repo-id>:ref:refs/heads/main` (story 7.15), with the IDs given to step 4 as
`TF_VAR_github_repository_owner_id` and `TF_VAR_github_repository_id`. On a mismatch, stop: do not deploy until the
pinned subject and `modules/deploy-role` are corrected. Repeat the check after a repository transfer or rename, or a
change to the repository's OIDC subject customisation. The API returns the repository's configuration, not a token
issued to a job, so the first deploy's `AssumeRoleWithWebIdentity` is the end-to-end proof.

```sh
cd infra/terraform/envs/dev
gh variable set AWS_DEPLOY_ROLE_ARN  --body "$(terraform output -raw deploy_role_arn)"
gh variable set AWS_REGION           --body "$(terraform output -raw aws_region)"
gh variable set DEPLOY_INSTANCE_ID   --body "$(terraform output -raw deploy_instance_id)"
gh variable set DEPLOY_DOCUMENT_NAME --body "$(terraform output -raw deploy_document_name)"
cd -
gh workflow run deploy.yml --ref main -f sha="$(git rev-parse origin/main)"
gh run watch "$(gh run list --workflow deploy.yml --limit 1 --json databaseId --jq '.[0].databaseId')"
```

The commit must be on main (the deploy document stops on the instance, before checking it out, unless
`git merge-base --is-ancestor <sha> origin/main` succeeds) and its five images published. The workflow sends the deploy document to the instance and
waits for its result; the job fails unless the command ends `Success`, and then prints the deploy script's standard
error.

## A half-done database init

The first deploy creates the roles `disputedesk`, `zitadel` and `qa_readonly` and the databases `disputedesk` and
`zitadel`. If it stopped part-way, every later deploy stops with "RDS holds only some of the roles and databases". No
data exists yet, so drop what was created and deploy again. Open a shell on the instance:

```sh
aws ssm start-session --target "$(terraform -chdir=infra/terraform/envs/dev output -raw deploy_instance_id)"
```

Then, on the instance:

```sh
sudo -i
cd /opt/disputedesk
export PGPASSWORD=$(aws ssm get-parameter --name /disputedesk/dev/RDS_MASTER_PASSWORD --with-decryption \
  --query Parameter.Value --output text)
export PGHOST=$(grep '^DB_HOST=' .env | cut -d= -f2)
docker run --rm -i -e PGPASSWORD -e PGHOST -e PGSSLMODE=verify-full -e PGSSLROOTCERT=/run/rds/ca-bundle.pem \
  -e PGUSER=dbmaster -e PGDATABASE=postgres -v "$PWD/rds-ca-bundle.pem:/run/rds/ca-bundle.pem:ro" \
  "$(grep '^POSTGRES_IMAGE=' infra/deploy/aws/deploy.sh | cut -d= -f2)" psql -v ON_ERROR_STOP=1 <<'SQL'
DROP DATABASE IF EXISTS disputedesk;
DROP DATABASE IF EXISTS zitadel;
DROP ROLE IF EXISTS qa_readonly;
DROP ROLE IF EXISTS zitadel;
DROP ROLE IF EXISTS disputedesk;
SQL
unset PGPASSWORD
exit
```

Then run `deploy.yml` again with the same SHA.

## Post-deploy checks

Run each once after the first deploy, and after any change to the Terraform or the AWS override.

1. Sign in over the app distribution (`https://<APP_HOST>`) as a fixture customer, and raise a dispute.
2. See the dispute's trace in X-Ray Transaction Search, its metrics in CloudWatch (namespace `DisputeDesk`) and its
   logs in the `/disputedesk-dev/otel` log group. From the maintainer's machine, within an hour of step 1, each command
   prints at least one line:

   ```sh
   since=$(( ($(date +%s) - 3600) * 1000 ))
   # Transaction Search stores spans in the aws/spans log group.
   aws logs filter-log-events --log-group-name aws/spans --start-time "$since" --filter-pattern '"disputes"' \
     --max-items 5 --query 'events[].message' --output text
   aws cloudwatch list-metrics --namespace DisputeDesk --query 'Metrics[].MetricName' --output text
   aws logs filter-log-events --log-group-name /disputedesk-dev/otel --log-stream-names otlp --start-time "$since" \
     --max-items 5 --query 'events[].message' --output text
   ```
3. Mailpit answers 401 without credentials on `https://<MAILPIT_HOST>/`, and the app distribution answers 404 on
   `/mailpit/`.
4. A direct request to the Elastic IP is refused: `curl -m 10 http://<elastic IP>/` times out (the security group
   admits only CloudFront).
5. RDS is not reachable from outside: `nc -z -w 5 <DB_HOST> 5432` from the maintainer's machine fails.
6. The init's `CREATE DATABASE ... OWNER disputedesk` worked under RDS's `rds_superuser` master: both databases exist,
   owned by `disputedesk` and `zitadel`. On the instance, as in *A half-done database init* (`sudo -i`,
   `cd /opt/disputedesk`, `PGPASSWORD` and `PGHOST` exported), the query prints `disputedesk|disputedesk` and
   `zitadel|zitadel`:

   ```sh
   docker run --rm -e PGPASSWORD -e PGHOST -e PGSSLMODE=verify-full -e PGSSLROOTCERT=/run/rds/ca-bundle.pem \
     -e PGUSER=dbmaster -e PGDATABASE=postgres -v "$PWD/rds-ca-bundle.pem:/run/rds/ca-bundle.pem:ro" \
     "$(grep '^POSTGRES_IMAGE=' infra/deploy/aws/deploy.sh | cut -d= -f2)" psql -tA -c \
     "SELECT datname, pg_get_userbyid(datdba) FROM pg_database WHERE datname IN ('disputedesk', 'zitadel') ORDER BY 1"
   unset PGPASSWORD
   ```
7. A metadata request from the Mailpit container fails while the Api gets credentials, and both still hold after a
   reboot (`sudo reboot`, then repeat). On the instance, from `/opt/disputedesk`:

   ```sh
   docker compose -f docker-compose.yml -f infra/deploy/aws/compose.yml exec mailpit \
     wget -T 5 -O - http://169.254.169.254/latest/meta-data/
   ```

   It must time out: a 401 answer means the container reached the metadata service (which then wants an IMDSv2
   token).

   The Api has credentials when its key ring exists: from the maintainer's machine,
   `aws ssm get-parameters-by-path --path /disputedesk-dataprotection/dev --query 'length(Parameters)'` is at least 1.

8. Redpanda stays within its limit: `docker stats --no-stream` shows it under its 768 MiB `mem_limit`.
9. The CloudFront origin-facing prefix list fits the security group's rule quota (a prefix list counts as its maximum
   entries; Amazon VPC, *Reference prefix lists in your AWS resources*). The first number must be below the second,
   the quota "Inbound or outbound rules per security group":

   ```sh
   aws ec2 describe-managed-prefix-lists --region "$REGION" \
     --filters Name=prefix-list-name,Values=com.amazonaws.global.cloudfront.origin-facing \
     --query 'PrefixLists[0].MaxEntries' --output text
   aws service-quotas get-service-quota --region "$REGION" --service-code vpc --quota-code L-0EA8095F \
     --query Quota.Value --output text
   ```
10. The budget alert email arrives once a threshold is crossed (or check the recipient as in step 6).
11. Plain HTTP is refused, not redirected, on the APIs and on the whole identity origin, and still redirected for the
    app's pages. The first three print `403` and an empty `Location`; the last two print `301` and an `https://`
    `Location`. `/api` without a trailing slash is a client route the SPA serves, not an Api path, so it redirects:

    ```sh
    for url in "http://<APP_HOST>/api/health/live" "http://<MAILPIT_HOST>/api/v1/messages" \
               "http://<AUTH_HOST>/oauth/v2/token" "http://<APP_HOST>/" "http://<APP_HOST>/api"; do
      curl -sS -m 10 -o /dev/null -w "%{http_code} Location: %header{location} $url\n" "$url"
    done
    ```

    The three host names are CloudFront default domains, under which DisputeDesk serves no other name, so the gateway's
    `Strict-Transport-Security: max-age=63072000; includeSubDomains` reaches only HTTPS hosts.

## Rollback

Run `deploy.yml` with an earlier SHA of main whose images are still in GHCR. The Api does not roll migrations back, so
a rollback past a migration relies on that migration staying compatible with the earlier code.

## Destroy

Before `terraform destroy`, delete what Terraform did not create under its paths: the person-created SecureStrings and
the Api's key ring.

```sh
for name in APP_DB_PASSWORD ZITADEL_DB_PASSWORD LOGIN_SESSION_COOKIE_SECRET QA_DB_PASSWORD ZITADEL_MASTERKEY \
  ZITADEL_ADMIN_PASSWORD LINKING_OTP_HMAC_KEY MAILPIT_UI_AUTH; do
  aws ssm delete-parameter --name "/disputedesk/dev/$name"
done
aws ssm get-parameters-by-path --path /disputedesk-dataprotection/dev --recursive --query 'Parameters[].Name' \
  --output text | tr '\t' '\n' | xargs -r -n 10 aws ssm delete-parameters --names

cd infra/terraform/envs/dev
# The same variables as step 4, or destroy prompts for each.
export TF_VAR_aws_region=$REGION
export TF_VAR_github_repository=$(gh api repos/<owner>/<name> --jq .full_name)
export TF_VAR_github_repository_owner_id=$(gh api repos/<owner>/<name> --jq .owner.id)
export TF_VAR_github_repository_id=$(gh api repos/<owner>/<name> --jq .id)
export TF_VAR_budget_limit_usd=10
export TF_VAR_budget_alert_email=<the same address>
terraform destroy
cd -
```

After it, exactly these remain:

- the state bucket (`bootstrap/`; `terraform destroy` there removes it once it is emptied of every object version);
- the KMS key, scheduled for deletion at the end of its 7-day window;
- Transaction Search's `aws/spans` log group, which X-Ray created. Delete it with:

  ```sh
  aws logs delete-log-group --log-group-name aws/spans
  ```
