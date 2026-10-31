# One monthly cost budget with two email alerts (NFR-INFRA-4). Credits are left out, because AWS Budgets includes them
# by default and the free plan's credits would hide the real spend. A budget alerts; it does not stop spending.

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

  notification {
    notification_type          = "ACTUAL"
    comparison_operator        = "GREATER_THAN"
    threshold                  = 80
    threshold_type             = "PERCENTAGE"
    subscriber_email_addresses = [var.alert_email]
  }

  notification {
    notification_type          = "FORECASTED"
    comparison_operator        = "GREATER_THAN"
    threshold                  = 100
    threshold_type             = "PERCENTAGE"
    subscriber_email_addresses = [var.alert_email]
  }
}
