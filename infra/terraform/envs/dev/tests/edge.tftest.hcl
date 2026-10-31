# Story 7.5, criteria 3 (the origin-verify value) and 4: a plan of the edge module against a mocked AWS provider. The
# random provider runs for real; the origin-verify value is overridden so the plan knows it.
#   terraform init -backend=false && terraform test

mock_provider "aws" {
  override_during = plan
}

override_resource {
  target          = random_password.origin_verify
  override_during = plan
  values          = { result = "TestOnlyOriginVerifyValueNotASecret0" }
}

variables {
  name               = "disputedesk-dev"
  origin_domain_name = "ec2-192-0-2-10.eu-west-1.compute.amazonaws.com"
}

run "criterion_3_the_origin_verify_value_is_64_letters_and_digits" {
  command = plan

  module {
    source = "../../modules/edge"
  }

  assert {
    condition     = random_password.origin_verify.length == 64 && random_password.origin_verify.special == false
    error_message = "The origin-verify value is not 64 letters and digits."
  }
}

run "criterion_4_three_distributions_forward_what_the_gateway_needs" {
  command = plan

  module {
    source = "../../modules/edge"
  }

  assert {
    condition     = keys(aws_cloudfront_distribution.this) == ["app", "auth", "mail"]
    error_message = "There are not exactly the app, auth and mail distributions."
  }

  # The IDs AWS publishes: 4135ea2d-... is CachingDisabled, 33f36d7e-... is AllViewerAndCloudFrontHeaders-2022-06
  # (CloudFront, "Use managed cache policies"; "Use managed origin request policies").
  assert {
    condition = alltrue([for distribution in aws_cloudfront_distribution.this :
      distribution.enabled == true &&
      distribution.is_ipv6_enabled == false &&
      distribution.default_cache_behavior[0].viewer_protocol_policy == "redirect-to-https" &&
      distribution.default_cache_behavior[0].cache_policy_id == "4135ea2d-6df8-44a3-9df3-4b5a84be39ad" &&
      distribution.default_cache_behavior[0].origin_request_policy_id == "33f36d7e-f396-46d9-90e0-52428a34d9dc" &&
      length(distribution.default_cache_behavior[0].allowed_methods) == 7
    ])
    error_message = "A distribution has IPv6, does not redirect to HTTPS, is not CachingDisabled, or does not forward every method and viewer header with AllViewerAndCloudFrontHeaders-2022-06."
  }

  assert {
    condition = alltrue([for distribution in aws_cloudfront_distribution.this :
      one(distribution.origin).domain_name == "ec2-192-0-2-10.eu-west-1.compute.amazonaws.com" &&
      one(distribution.origin).custom_origin_config[0].origin_protocol_policy == "http-only" &&
      one(distribution.origin).custom_origin_config[0].http_port == 80 &&
      one(one(distribution.origin).custom_header).name == "X-Origin-Verify" &&
      one(one(distribution.origin).custom_header).value == "TestOnlyOriginVerifyValueNotASecret0"
    ])
    error_message = "A distribution's origin is not the Elastic IP's DNS name over HTTP port 80 with the X-Origin-Verify header."
  }

  assert {
    condition     = output.origin_verify == "TestOnlyOriginVerifyValueNotASecret0"
    error_message = "The module does not output the origin-verify value for SSM."
  }
}
