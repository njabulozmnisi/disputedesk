# One monthly cost budget that emails one address at 25 %, 50 % and 80 % of the actual spend and at 100 % of the
# forecast (NFR-INFRA-4). AWS Budgets allows five notifications per budget. Credits are left out, because AWS Budgets
# includes them by default and the free plan's credits would hide the real spend. A budget alerts; it does not stop
# spending.

variable "name" {
  description = "The prefix for every resource name."
  type        = string
}

variable "limit_usd" {
  type = number
}

variable "alert_email" {
  type      = string
  sensitive = true
}

resource "aws_budgets_budget" "monthly" {
  name         = "${var.name}-monthly"
  budget_type  = "COST"
  limit_amount = tostring(var.limit_usd)
  limit_unit   = "USD"
  time_unit    = "MONTHLY"

  cost_types {
    include_credit = false
  }

  dynamic "notification" {
    for_each = [25, 50, 80]

    content {
      notification_type          = "ACTUAL"
      comparison_operator        = "GREATER_THAN"
      threshold                  = notification.value
      threshold_type             = "PERCENTAGE"
      subscriber_email_addresses = [var.alert_email]
    }
  }

  notification {
    notification_type          = "FORECASTED"
    comparison_operator        = "GREATER_THAN"
    threshold                  = 100
    threshold_type             = "PERCENTAGE"
    subscriber_email_addresses = [var.alert_email]
  }
}
