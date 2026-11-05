# Where the collector sends telemetry (AD-29): one log group for OTLP logs and the metrics' EMF records, and X-Ray
# Transaction Search, which stores spans in CloudWatch Logs (AWS X-Ray, "Transaction Search").

variable "name" {
  description = "The prefix for every resource name."
  type        = string
}

variable "aws_region" {
  type = string
}

variable "account_id" {
  type = string
}

resource "aws_cloudwatch_log_group" "otel" {
  name              = "/${var.name}/otel"
  retention_in_days = 7
}

# The CloudWatch Logs OTLP endpoint writes only to an existing stream; infra/images/otel-collector/config.yaml names this one.
resource "aws_cloudwatch_log_stream" "otlp" {
  name           = "otlp"
  log_group_name = aws_cloudwatch_log_group.otel.name
}

# Lets X-Ray write spans to CloudWatch Logs, the documented resource policy for enabling Transaction Search through the
# API (CloudWatch, "Enable transaction search").
resource "aws_cloudwatch_log_resource_policy" "transaction_search" {
  policy_name = "${var.name}-transaction-search"
  # A JSON template whose names the infra tests' IamNamesTests check.
  policy_document = templatefile("${path.module}/transaction-search-policy.json.tftpl", {
    aws_region = var.aws_region
    account_id = var.account_id
  })
}

# Transaction Search: spans go to CloudWatch Logs (UpdateTraceSegmentDestination). X-Ray creates the aws/spans log
# group, which destroy leaves behind (infra/terraform/README.md).
resource "aws_xray_trace_segment_destination" "this" {
  destination = "CloudWatchLogs"

  depends_on = [aws_cloudwatch_log_resource_policy.transaction_search]
}

output "log_group_name" {
  value = aws_cloudwatch_log_group.otel.name
}
