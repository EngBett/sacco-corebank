terraform {
  required_providers {
    aws    = { source = "hashicorp/aws", version = "~> 5.60" }
    random = { source = "hashicorp/random", version = "~> 3.6" }
  }
}

variable "name" { type = string }
variable "vpc_id" { type = string }
variable "subnet_ids" { type = list(string) }
variable "app_security_group_id" { type = string }
variable "instance_class" {
  type    = string
  default = "db.t4g.medium"
}
variable "allocated_storage" {
  type    = number
  default = 50
}
variable "multi_az" {
  type    = bool
  default = false
}
variable "backup_retention_days" {
  type    = number
  default = 7
}
variable "deletion_protection" {
  type    = bool
  default = false
}
# SASRA expectation (infrastructure/CLAUDE.md): backups and point-in-time recovery are configured
# before any environment holds real member data. RDS automated backups with retention ≥ 7 days
# provide PITR to any second within the retention window.
resource "random_password" "db" {
  length  = 32
  special = false
}

resource "aws_kms_key" "db" {
  description         = "${var.name} RDS encryption"
  enable_key_rotation = true
}

resource "aws_db_subnet_group" "this" {
  name       = var.name
  subnet_ids = var.subnet_ids
}

resource "aws_security_group" "db" {
  name   = "${var.name}-db"
  vpc_id = var.vpc_id
  ingress {
    from_port       = 5432
    to_port         = 5432
    protocol        = "tcp"
    security_groups = [var.app_security_group_id]
  }
  egress {
    from_port   = 0
    to_port     = 0
    protocol    = "-1"
    cidr_blocks = ["0.0.0.0/0"]
  }
}

resource "aws_db_instance" "this" {
  identifier                            = var.name
  engine                                = "postgres"
  engine_version                        = "16"
  instance_class                        = var.instance_class
  allocated_storage                     = var.allocated_storage
  max_allocated_storage                 = var.allocated_storage * 4
  storage_encrypted                     = true
  kms_key_id                            = aws_kms_key.db.arn
  db_name                               = "sacco"
  username                              = "sacco"
  password                              = random_password.db.result
  db_subnet_group_name                  = aws_db_subnet_group.this.name
  vpc_security_group_ids                = [aws_security_group.db.id]
  multi_az                              = var.multi_az
  backup_retention_period               = var.backup_retention_days
  backup_window                         = "01:00-02:00"
  maintenance_window                    = "sun:02:30-sun:03:30"
  copy_tags_to_snapshot                 = true
  deletion_protection                   = var.deletion_protection
  skip_final_snapshot                   = !var.deletion_protection
  final_snapshot_identifier             = "${var.name}-final"
  performance_insights_enabled          = true
  performance_insights_retention_period = 7
  publicly_accessible                   = false
  apply_immediately                     = false
}

resource "aws_secretsmanager_secret" "connection" {
  name        = "${var.name}/db/connection-string"
  description = "ConnectionStrings__Sacco for the API and seed tool"
  kms_key_id  = aws_kms_key.db.arn
}

resource "aws_secretsmanager_secret_version" "connection" {
  secret_id     = aws_secretsmanager_secret.connection.id
  secret_string = "Host=${aws_db_instance.this.address};Port=5432;Database=sacco;Username=sacco;Password=${random_password.db.result};SSL Mode=Require"
}

output "connection_secret_arn" { value = aws_secretsmanager_secret.connection.arn }
output "endpoint" { value = aws_db_instance.this.address }
