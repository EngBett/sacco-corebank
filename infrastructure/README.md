# Infrastructure

AWS, Terraform ≥ 1.6. One VPC per environment; the API, staff portal and public site run as ECS Fargate services
behind one ALB (host-based routing: `api.<domain>`, `portal.<domain>`, everything else → public site, so tenant
subdomains and custom domains work without infra changes); PostgreSQL 16 on RDS in private subnets.

```
modules/network    VPC, public/private subnets, NAT
modules/database   RDS Postgres 16, KMS-encrypted, automated backups + point-in-time recovery, connection string in Secrets Manager
modules/secrets    Empty Secrets Manager placeholders for every credential the platform needs (values set out-of-band)
modules/app        ECR, ECS cluster/services, ALB + HTTPS, CloudWatch logs, IAM scoped to this environment's secrets
environments/*     dev / staging / production compositions with per-environment defaults
```

## Bootstrap and use

1. Create the remote state bucket (versioning + SSE) and a DynamoDB lock table once per account.
2. `cd environments/dev && cp terraform.tfvars.example terraform.tfvars` and fill in the domain and ACM certificate.
3. `terraform init -backend-config="bucket=<state-bucket>" -backend-config="dynamodb_table=<lock-table>"`
4. `terraform plan` / `terraform apply`.
5. Build and push images (CI does this on `main`): `docker build -f backend/Dockerfile --target api -t <ecr>/api:<tag> .` etc., then `terraform apply -var image_tag=<tag>`.
6. Run the migration/seed image once against the environment database: the `seed` target of `backend/Dockerfile`
   with `SACCO_CONNECTION` from Secrets Manager (dev/staging only — production data is migrated per the cutover runbook,
   never seeded).

## What the production cutover changes (no code)

`docs/runbooks/production-cutover.md` maps onto these secrets and variables:

| Runbook step | Where |
|---|---|
| M-Pesa production credentials | `payments/mpesa/*` secrets; `mpesa_mode = "Live"` |
| Airtel Money / bank credentials | `payments/airtel/*`, `payments/bank/*` secrets; modes → `Live` once the live provider classes exist |
| IdentityServer signing keys | `identityserver/certificate-base64` + password — generated for production, never reused |
| Turnstile real keys | `turnstile/secret-key`; `turnstile_sandbox = false` |
| Public site server key | `public-site/api-key` |
| Callback IP restriction | `webhook_allowed_cidrs` |
| Backups / PITR | RDS `backup_retention_days` (35 in production) + `deletion_protection`, Multi-AZ |

Dev and staging default every provider to **Sandbox**; only `environments/production/terraform.tfvars.example` shows `Live`.
