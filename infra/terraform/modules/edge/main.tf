# Three CloudFront distributions, app, auth and mail (Mailpit), in front of the one gateway (AD-23, AD-28). Nothing is
# cached, and the viewer's Host, cookies, query strings and CloudFront's viewer headers reach nginx, which answers by
# host name and rejects any request without the origin-verify header.

variable "name" {
  description = "The prefix for every resource name."
  type        = string
}

variable "origin_domain_name" {
  description = "The Elastic IP's public DNS name."
  type        = string
}

locals {
  distributions = toset(["app", "auth", "mail"])

  # AWS-managed policies, by the IDs AWS publishes for the API (CloudFront, "Use managed cache policies"; "Use managed
  # origin request policies", read 2026-10-31). Named constants rather than a lookup by name, so a plan knows them.
  caching_disabled_policy_id = "4135ea2d-6df8-44a3-9df3-4b5a84be39ad"

  # Every viewer header (Host included), cookies and query strings, plus CloudFront-Viewer-Address and
  # CloudFront-Forwarded-Proto, which the gateway reads for the client address and scheme.
  all_viewer_and_cloudfront_headers_policy_id = "33f36d7e-f396-46d9-90e0-52428a34d9dc"
}

# The shared secret CloudFront sends and nginx checks: letters and digits only, within story 7.4's
# ^[A-Za-z0-9]{32,128}$. It is in state because CloudFront's origin header needs it in the plan.
# shortcut: a secret in state (the bucket is private, versioned and encrypted); move it to an ephemeral value when
# CloudFront's origin headers accept one.
resource "random_password" "origin_verify" {
  length  = 64
  special = false
}

resource "aws_cloudfront_distribution" "this" {
  for_each = local.distributions

  comment         = "${var.name}-${each.key}"
  enabled         = true
  is_ipv6_enabled = false

  origin {
    origin_id   = "gateway"
    domain_name = var.origin_domain_name

    # shortcut: CloudFront to origin is plain HTTP for the free-plan demo with synthetic data; an HTTPS or VPC origin, a
    # domain, an ACM certificate (TLS 1.2 minimum viewer) and WAF before real data
    custom_origin_config {
      http_port              = 80
      https_port             = 443
      origin_protocol_policy = "http-only"
      origin_ssl_protocols   = ["TLSv1.2"]
    }

    # CloudFront, "Add custom headers to origin requests".
    custom_header {
      name  = "X-Origin-Verify"
      value = random_password.origin_verify.result
    }
  }

  # With https-only, CloudFront answers plain HTTP with 403 instead of a redirect (CloudFront, "Require HTTPS for
  # communication between viewers and CloudFront"; ASVS 5.0 V4.1.2), so a request sent over HTTP never reaches an API.
  # The identity origin is https-only throughout: its OAuth and OIDC endpoints are not under /api/, and it serves no
  # navigation that needs the redirect. The app and Mailpit keep the redirect for a browser's navigation.
  default_cache_behavior {
    target_origin_id         = "gateway"
    viewer_protocol_policy   = each.key == "auth" ? "https-only" : "redirect-to-https"
    allowed_methods          = ["GET", "HEAD", "OPTIONS", "PUT", "POST", "PATCH", "DELETE"]
    cached_methods           = ["GET", "HEAD"]
    cache_policy_id          = local.caching_disabled_policy_id
    origin_request_policy_id = local.all_viewer_and_cloudfront_headers_policy_id
  }

  # The app's and Mailpit's APIs: the default behaviour's settings, https-only. /api without the slash is a client route.
  dynamic "ordered_cache_behavior" {
    for_each = each.key == "auth" ? [] : ["/api/*"]

    content {
      path_pattern             = ordered_cache_behavior.value
      target_origin_id         = "gateway"
      viewer_protocol_policy   = "https-only"
      allowed_methods          = ["GET", "HEAD", "OPTIONS", "PUT", "POST", "PATCH", "DELETE"]
      cached_methods           = ["GET", "HEAD"]
      cache_policy_id          = local.caching_disabled_policy_id
      origin_request_policy_id = local.all_viewer_and_cloudfront_headers_policy_id
    }
  }

  restrictions {
    geo_restriction {
      restriction_type = "none"
    }
  }

  # The default *.cloudfront.net certificate; a domain and an ACM certificate are a production-path item.
  viewer_certificate {
    cloudfront_default_certificate = true
  }
}

output "domain_names" {
  description = "The app, auth and mail distributions' host names."
  value       = { for key, distribution in aws_cloudfront_distribution.this : key => distribution.domain_name }
}

output "origin_verify" {
  value     = random_password.origin_verify.result
  sensitive = true
}
