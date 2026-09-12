terraform {
  required_providers { aws = { source = "hashicorp/aws", version = "~> 5.60" } }
}

variable "name" { type = string }

# Placeholders only. Values are set out-of-band (console/CLI) during the production cutover
# (docs/runbooks/production-cutover.md). Terraform never sees a real credential.
locals {
  secrets = {
    "identityserver/certificate-base64"   = "PFX (base64) for token signing — never reuse a lower environment's key"
    "identityserver/certificate-password" = "PFX password"
    "portal/oidc-client-secret"           = "portal-bff client secret (must match IdentityServer:Clients)"
    "portal/session-secret"               = "iron-session cookie encryption key (≥ 32 chars)"
    "public-site/api-key"                 = "PublicApi:ApiKey shared between the public site and the API"
    "turnstile/secret-key"                = "Cloudflare Turnstile secret"
    "payments/mpesa/consumer-key"         = "Daraja consumer key"
    "payments/mpesa/consumer-secret"      = "Daraja consumer secret"
    "payments/mpesa/shortcode"            = "Paybill/till shortcode"
    "payments/mpesa/passkey"              = "STK push passkey"
    "payments/mpesa/initiator-name"       = "B2C initiator"
    "payments/mpesa/security-credential"  = "B2C security credential"
    "payments/airtel/client-id"           = "Airtel Money client id"
    "payments/airtel/client-secret"       = "Airtel Money client secret"
    "payments/bank/api-key"               = "Settlement bank API credential"
  }
}

resource "aws_secretsmanager_secret" "this" {
  for_each    = local.secrets
  name        = "${var.name}/${each.key}"
  description = each.value
}

output "secret_arns" { value = { for k, s in aws_secretsmanager_secret.this : k => s.arn } }
