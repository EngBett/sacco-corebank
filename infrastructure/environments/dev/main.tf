terraform {
  required_version = ">= 1.6"
  required_providers {
    aws    = { source = "hashicorp/aws", version = "~> 5.60" }
    random = { source = "hashicorp/random", version = "~> 3.6" }
  }
  backend "s3" {
    # Bootstrap: create the bucket (versioned, encrypted) and the DynamoDB lock table once, then
    #   terraform init -backend-config="bucket=<state-bucket>" -backend-config="dynamodb_table=<lock-table>"
    key    = "sacco-platform/dev/terraform.tfstate"
    region = "eu-west-1"
  }
}

provider "aws" {
  region = var.region
  default_tags {
    tags = { Project = "sacco-platform", Environment = "dev" }
  }
}

locals { name = "sacco-dev" }

module "network" {
  source = "../../modules/network"
  name   = local.name
  azs    = var.availability_zones
}

module "secrets" {
  source = "../../modules/secrets"
  name   = local.name
}

module "app" {
  source                   = "../../modules/app"
  name                     = local.name
  environment              = "dev"
  vpc_id                   = module.network.vpc_id
  public_subnet_ids        = module.network.public_subnet_ids
  private_subnet_ids       = module.network.private_subnet_ids
  certificate_arn          = var.certificate_arn
  domain                   = var.domain
  db_connection_secret_arn = module.database.connection_secret_arn
  secret_arns              = module.secrets.secret_arns
  image_tag                = var.image_tag
  mpesa_mode               = var.mpesa_mode
  airtel_mode              = var.airtel_mode
  bank_mode                = var.bank_mode
  turnstile_sandbox        = var.turnstile_sandbox
  default_tenant_slug      = var.default_tenant_slug
  webhook_allowed_cidrs    = var.webhook_allowed_cidrs
  desired_count            = var.desired_count
}

module "database" {
  source                = "../../modules/database"
  name                  = local.name
  vpc_id                = module.network.vpc_id
  subnet_ids            = module.network.private_subnet_ids
  app_security_group_id = module.app.app_security_group_id
  instance_class        = var.db_instance_class
  multi_az              = false
  backup_retention_days = 7
  deletion_protection   = false
}

output "alb_dns_name" { value = module.app.alb_dns_name }
output "ecr_repositories" { value = module.app.ecr_repository_urls }
output "db_endpoint" { value = module.database.endpoint }
