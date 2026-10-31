# Story 7.5, criterion 7: plans of the observability and budget modules against a mocked AWS provider.
#   terraform init -backend=false && terraform test

mock_provider "aws" {
  override_during = plan
}

run "criterion_7_the_log_group_and_transaction_search" {
  command = plan

  module {
    source = "../../modules/observability"
  }

  variables {
    name       = "disputedesk-dev"
    aws_region = "eu-west-1"
    account_id = "0000-0000-0000"
  }

  assert {
    condition = (
      aws_cloudwatch_log_group.otel.name == "/disputedesk-dev/otel" &&
      aws_cloudwatch_log_group.otel.retention_in_days == 7 &&
      aws_cloudwatch_log_stream.otlp.name == "otlp" &&
      aws_cloudwatch_log_stream.otlp.log_group_name == "/disputedesk-dev/otel" &&
      output.log_group_name == "/disputedesk-dev/otel"
    )
    error_message = "The log group does not keep 7 days or lacks the otlp stream."
  }

  assert {
    condition = (
      aws_xray_trace_segment_destination.this.destination == "CloudWatchLogs" &&
      jsondecode(aws_cloudwatch_log_resource_policy.transaction_search.policy_document).Statement == [{
        Sid       = "TransactionSearchXRayAccess"
        Effect    = "Allow"
        Principal = { Service = "xray.amazonaws.com" }
        Action    = "logs:PutLogEvents"
        Resource = [
          "arn:aws:logs:eu-west-1:0000-0000-0000:log-group:aws/spans:*",
          "arn:aws:logs:eu-west-1:0000-0000-0000:log-group:/aws/application-signals/data:*",
        ]
        Condition = {
          ArnLike      = { "aws:SourceArn" = "arn:aws:xray:eu-west-1:0000-0000-0000:*" }
          StringEquals = { "aws:SourceAccount" = "0000-0000-0000" }
        }
      }]
    )
    error_message = "Transaction Search is not enabled with the X-Ray log resource policy."
  }
}

run "criterion_7_one_monthly_cost_budget_without_credits" {
  command = plan

  module {
    source = "../../modules/budget"
  }

  variables {
    name        = "disputedesk-dev"
    limit_usd   = 10
    alert_email = "budget@example.com"
  }

  assert {
    condition = (
      aws_budgets_budget.monthly.budget_type == "COST" &&
      aws_budgets_budget.monthly.time_unit == "MONTHLY" &&
      aws_budgets_budget.monthly.limit_amount == "10" &&
      aws_budgets_budget.monthly.limit_unit == "USD" &&
      aws_budgets_budget.monthly.cost_types[0].include_credit == false
    )
    error_message = "The budget is not a monthly USD 10 cost budget without credits."
  }

  assert {
    condition = (
      length(aws_budgets_budget.monthly.notification) == 2 &&
      length([for alert in aws_budgets_budget.monthly.notification : alert if
        alert.notification_type == "ACTUAL" && alert.comparison_operator == "GREATER_THAN" && alert.threshold == 80 &&
      alert.threshold_type == "PERCENTAGE" && toset(alert.subscriber_email_addresses) == toset(["budget@example.com"])]) == 1 &&
      length([for alert in aws_budgets_budget.monthly.notification : alert if
        alert.notification_type == "FORECASTED" && alert.comparison_operator == "GREATER_THAN" && alert.threshold == 100 &&
      alert.threshold_type == "PERCENTAGE" && toset(alert.subscriber_email_addresses) == toset(["budget@example.com"])]) == 1
    )
    error_message = "The budget does not email an alert at 80 % actual and 100 % forecasted."
  }
}
