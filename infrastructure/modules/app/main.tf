terraform {
  required_providers { aws = { source = "hashicorp/aws", version = "~> 5.60" } }
}

variable "name" { type = string }
variable "environment" { type = string }
variable "vpc_id" { type = string }
variable "public_subnet_ids" { type = list(string) }
variable "private_subnet_ids" { type = list(string) }
variable "certificate_arn" {
  type        = string
  description = "ACM certificate covering api., portal. and *.public host names"
}
variable "domain" {
  type        = string
  description = "Base domain, e.g. sacco.example.co.ke"
}
variable "db_connection_secret_arn" { type = string }
variable "secret_arns" { type = map(string) }
variable "image_tag" {
  type    = string
  default = "latest"
}
variable "mpesa_mode" {
  type    = string
  default = "Sandbox"
}
variable "airtel_mode" {
  type    = string
  default = "Sandbox"
}
variable "bank_mode" {
  type    = string
  default = "Sandbox"
}
variable "turnstile_sandbox" {
  type    = bool
  default = true
}
variable "default_tenant_slug" {
  type    = string
  default = ""
}
variable "enable_redis_backplane" {
  type        = bool
  default     = false
  description = "Provision ElastiCache Redis as the SignalR backplane. Required before desired_count > 1."
}
variable "redis_node_type" {
  type    = string
  default = "cache.t4g.micro"
}
variable "sms_mode" {
  type    = string
  default = "Sandbox"
}
variable "email_mode" {
  type    = string
  default = "Sandbox"
}
variable "credit_bureau_mode" {
  type    = string
  default = "Sandbox"
}
variable "webhook_allowed_cidrs" {
  type    = list(string)
  default = []
}
variable "api_cpu" {
  type    = number
  default = 512
}
variable "api_memory" {
  type    = number
  # 1536, not 1024: the nightly PDF digest (ADR 0013) launches headless Chromium in-process for each tenant's
  # report (sequentially, never in parallel), which needs headroom above the .NET runtime's own baseline.
  default = 1536
}
variable "desired_count" {
  type    = number
  default = 1
}
locals {
  api_host    = "api.${var.domain}"
  portal_host = "portal.${var.domain}"
  services = {
    api         = { port = 8080, cpu = var.api_cpu, memory = var.api_memory }
    portal      = { port = 3000, cpu = 256, memory = 512 }
    public-site = { port = 3001, cpu = 256, memory = 512 }
  }
}

resource "aws_ecr_repository" "this" {
  for_each             = local.services
  name                 = "${var.name}/${each.key}"
  image_tag_mutability = "IMMUTABLE"
  image_scanning_configuration { scan_on_push = true }
}

resource "aws_ecs_cluster" "this" {
  name = var.name
  setting {
    name  = "containerInsights"
    value = "enabled"
  }
}

resource "aws_cloudwatch_log_group" "this" {
  for_each          = local.services
  name              = "/ecs/${var.name}/${each.key}"
  retention_in_days = 90
}

resource "aws_security_group" "alb" {
  name   = "${var.name}-alb"
  vpc_id = var.vpc_id
  ingress {
    from_port   = 443
    to_port     = 443
    protocol    = "tcp"
    cidr_blocks = ["0.0.0.0/0"]
  }
  ingress {
    from_port   = 80
    to_port     = 80
    protocol    = "tcp"
    cidr_blocks = ["0.0.0.0/0"]
  }
  egress {
    from_port   = 0
    to_port     = 0
    protocol    = "-1"
    cidr_blocks = ["0.0.0.0/0"]
  }
}

resource "aws_security_group" "app" {
  name   = "${var.name}-app"
  vpc_id = var.vpc_id
  ingress {
    from_port       = 0
    to_port         = 65535
    protocol        = "tcp"
    security_groups = [aws_security_group.alb.id]
  }
  ingress {
    from_port = 0
    to_port   = 65535
    protocol  = "tcp"
    self      = true
  }
  egress {
    from_port   = 0
    to_port     = 0
    protocol    = "-1"
    cidr_blocks = ["0.0.0.0/0"]
  }
}

resource "aws_lb" "this" {
  name                       = substr(var.name, 0, 32)
  load_balancer_type         = "application"
  security_groups            = [aws_security_group.alb.id]
  subnets                    = var.public_subnet_ids
  drop_invalid_header_fields = true
}

resource "aws_lb_target_group" "this" {
  for_each    = local.services
  name        = substr("${var.name}-${each.key}", 0, 32)
  port        = each.value.port
  protocol    = "HTTP"
  target_type = "ip"
  vpc_id      = var.vpc_id
  health_check {
    path                = each.key == "api" ? "/health" : "/"
    healthy_threshold   = 2
    unhealthy_threshold = 3
    interval            = 30
  }
}

resource "aws_lb_listener" "http" {
  load_balancer_arn = aws_lb.this.arn
  port              = 80
  protocol          = "HTTP"
  default_action {
    type = "redirect"
    redirect {
      port        = "443"
      protocol    = "HTTPS"
      status_code = "HTTP_301"
    }
  }
}

resource "aws_lb_listener" "https" {
  load_balancer_arn = aws_lb.this.arn
  port              = 443
  protocol          = "HTTPS"
  ssl_policy        = "ELBSecurityPolicy-TLS13-1-2-2021-06"
  certificate_arn   = var.certificate_arn
  # Any other host (tenant subdomains, custom domains) is the public site; the app resolves the tenant from the Host header.
  default_action {
    type             = "forward"
    target_group_arn = aws_lb_target_group.this["public-site"].arn
  }
}

resource "aws_lb_listener_rule" "api" {
  listener_arn = aws_lb_listener.https.arn
  priority     = 10
  action {
    type             = "forward"
    target_group_arn = aws_lb_target_group.this["api"].arn
  }
  condition {
    host_header { values = [local.api_host] }
  }
}

resource "aws_lb_listener_rule" "portal" {
  listener_arn = aws_lb_listener.https.arn
  priority     = 20
  action {
    type             = "forward"
    target_group_arn = aws_lb_target_group.this["portal"].arn
  }
  condition {
    host_header { values = [local.portal_host, "*.${local.portal_host}"] }
  }
}

data "aws_iam_policy_document" "task_assume" {
  statement {
    actions = ["sts:AssumeRole"]
    principals {
      type        = "Service"
      identifiers = ["ecs-tasks.amazonaws.com"]
    }
  }
}

resource "aws_iam_role" "execution" {
  name               = "${var.name}-ecs-execution"
  assume_role_policy = data.aws_iam_policy_document.task_assume.json
}

resource "aws_iam_role_policy_attachment" "execution" {
  role       = aws_iam_role.execution.name
  policy_arn = "arn:aws:iam::aws:policy/service-role/AmazonECSTaskExecutionRolePolicy"
}

# The execution role injects secrets at task start: scoped to exactly this environment's secrets.
data "aws_iam_policy_document" "secrets" {
  statement {
    actions   = ["secretsmanager:GetSecretValue"]
    resources = concat([var.db_connection_secret_arn], values(var.secret_arns))
  }
  statement {
    actions   = ["kms:Decrypt"]
    resources = ["*"]
  }
}

resource "aws_iam_role_policy" "secrets" {
  role   = aws_iam_role.execution.id
  policy = data.aws_iam_policy_document.secrets.json
}

resource "aws_iam_role" "task" {
  name               = "${var.name}-ecs-task"
  assume_role_policy = data.aws_iam_policy_document.task_assume.json
}

# Environment → .NET configuration keys (exact names the API reads). Provider modes and Turnstile
# are configuration: switching to Live is the production cutover, never a code change.
locals {
  api_env = [
    { name = "ASPNETCORE_ENVIRONMENT", value = var.environment == "production" ? "Production" : "Staging" },
    { name = "IdentityServer__IssuerUri", value = "https://${local.api_host}" },
    { name = "IdentityServer__SigningCredential", value = "Certificate" },
    { name = "IdentityServer__Clients__0__ClientId", value = "portal-bff" },
    { name = "IdentityServer__Clients__0__GrantTypes__0", value = "code" },
    { name = "IdentityServer__Clients__0__RedirectUris__0", value = "https://${local.portal_host}/api/auth/callback" },
    { name = "IdentityServer__Clients__0__PostLogoutRedirectUris__0", value = "https://${local.portal_host}/" },
    { name = "Cors__AllowedOrigins__0", value = "https://${local.portal_host}" },
    { name = "Cors__AllowedOrigins__1", value = "https://*.${local.portal_host}" },
    { name = "Notifications__Redis", value = var.enable_redis_backplane ? "${aws_elasticache_replication_group.redis[0].primary_endpoint_address}:6379,ssl=true,abortConnect=false" : "" },
    { name = "Notifications__Sms__Mode", value = var.sms_mode },
    { name = "Notifications__Email__Mode", value = var.email_mode },
    { name = "Lending__CreditBureau__Mode", value = var.credit_bureau_mode },
    { name = "IdentityServer__Clients__1__ClientId", value = "mobile" },
    { name = "IdentityServer__Clients__1__GrantTypes__0", value = "code" },
    { name = "IdentityServer__Clients__1__RequireClientSecret", value = "false" },
    { name = "IdentityServer__Clients__1__RedirectUris__0", value = "sacco://auth/callback" },
    { name = "Payments__MPesa__Mode", value = var.mpesa_mode },
    { name = "Payments__MPesa__BaseUrl", value = var.mpesa_mode == "Live" ? "https://api.safaricom.co.ke/" : "https://sandbox.safaricom.co.ke/" },
    { name = "Payments__MPesa__CallbackBaseUrl", value = "https://${local.api_host}/api/payments/webhooks/{tenant}" },
    { name = "Payments__AirtelMoney__Mode", value = var.airtel_mode },
    { name = "Payments__Bank__Mode", value = var.bank_mode },
    { name = "Turnstile__Sandbox", value = tostring(var.turnstile_sandbox) },
    { name = "Tenancy__DefaultTenantSlug", value = var.default_tenant_slug },
    { name = "Database__MigrateOnStartup", value = "false" },
    { name = "Payments__WebhookAllowedCidrs", value = join(",", var.webhook_allowed_cidrs) },
  ]
  api_secrets = [
    { name = "ConnectionStrings__Sacco", valueFrom = var.db_connection_secret_arn },
    { name = "IdentityServer__CertificateBase64", valueFrom = var.secret_arns["identityserver/certificate-base64"] },
    { name = "IdentityServer__CertificatePassword", valueFrom = var.secret_arns["identityserver/certificate-password"] },
    { name = "IdentityServer__Clients__0__ClientSecret", valueFrom = var.secret_arns["portal/oidc-client-secret"] },
    { name = "PublicApi__ApiKey", valueFrom = var.secret_arns["public-site/api-key"] },
    { name = "Turnstile__SecretKey", valueFrom = var.secret_arns["turnstile/secret-key"] },
    { name = "Payments__MPesa__ConsumerKey", valueFrom = var.secret_arns["payments/mpesa/consumer-key"] },
    { name = "Payments__MPesa__ConsumerSecret", valueFrom = var.secret_arns["payments/mpesa/consumer-secret"] },
    { name = "Payments__MPesa__ShortCode", valueFrom = var.secret_arns["payments/mpesa/shortcode"] },
    { name = "Payments__MPesa__Passkey", valueFrom = var.secret_arns["payments/mpesa/passkey"] },
    { name = "Payments__MPesa__InitiatorName", valueFrom = var.secret_arns["payments/mpesa/initiator-name"] },
    { name = "Payments__MPesa__SecurityCredential", valueFrom = var.secret_arns["payments/mpesa/security-credential"] },
  ]
  portal_env = [
    { name = "API_BASE_URL", value = "https://${local.api_host}" },
    { name = "API_BROWSER_URL", value = "https://${local.api_host}" },
    { name = "OIDC_ISSUER", value = "https://${local.api_host}" },
    { name = "OIDC_CLIENT_ID", value = "portal-bff" },
    { name = "OIDC_REDIRECT_URI", value = "https://${local.portal_host}/api/auth/callback" },
    { name = "OIDC_POST_LOGOUT_REDIRECT_URI", value = "https://${local.portal_host}/" },
    { name = "NEXT_PUBLIC_DEFAULT_TENANT", value = var.default_tenant_slug },
  ]
  portal_secrets = [
    { name = "OIDC_CLIENT_SECRET", valueFrom = var.secret_arns["portal/oidc-client-secret"] },
    { name = "SESSION_SECRET", valueFrom = var.secret_arns["portal/session-secret"] },
  ]
  public_env = [
    { name = "API_BASE_URL", value = "https://${local.api_host}" },
    { name = "NEXT_PUBLIC_DEFAULT_TENANT", value = var.default_tenant_slug },
    { name = "TURNSTILE_SANDBOX", value = tostring(var.turnstile_sandbox) },
  ]
  public_secrets = [
    { name = "PUBLIC_API_KEY", valueFrom = var.secret_arns["public-site/api-key"] },
    { name = "TURNSTILE_SECRET_KEY", valueFrom = var.secret_arns["turnstile/secret-key"] },
  ]
  container_env     = { api = local.api_env, portal = local.portal_env, public-site = local.public_env }
  container_secrets = { api = local.api_secrets, portal = local.portal_secrets, public-site = local.public_secrets }
}

resource "aws_ecs_task_definition" "this" {
  for_each                 = local.services
  family                   = "${var.name}-${each.key}"
  network_mode             = "awsvpc"
  requires_compatibilities = ["FARGATE"]
  cpu                      = each.value.cpu
  memory                   = each.value.memory
  execution_role_arn       = aws_iam_role.execution.arn
  task_role_arn            = aws_iam_role.task.arn
  container_definitions = jsonencode([{
    name         = each.key
    image        = "${aws_ecr_repository.this[each.key].repository_url}:${var.image_tag}"
    essential    = true
    portMappings = [{ containerPort = each.value.port, protocol = "tcp" }]
    environment  = local.container_env[each.key]
    secrets      = local.container_secrets[each.key]
    logConfiguration = {
      logDriver = "awslogs"
      options   = { awslogs-group = aws_cloudwatch_log_group.this[each.key].name, awslogs-region = data.aws_region.current.name, awslogs-stream-prefix = each.key }
    }
  }])
}

data "aws_region" "current" {}

resource "aws_ecs_service" "this" {
  for_each        = local.services
  name            = each.key
  cluster         = aws_ecs_cluster.this.id
  task_definition = aws_ecs_task_definition.this[each.key].arn
  desired_count   = var.desired_count
  launch_type     = "FARGATE"
  network_configuration {
    subnets         = var.private_subnet_ids
    security_groups = [aws_security_group.app.id]
  }
  load_balancer {
    target_group_arn = aws_lb_target_group.this[each.key].arn
    container_name   = each.key
    container_port   = each.value.port
  }
  deployment_circuit_breaker {
    enable   = true
    rollback = true
  }
  depends_on = [aws_lb_listener.https]
}

output "app_security_group_id" { value = aws_security_group.app.id }
output "alb_dns_name" { value = aws_lb.this.dns_name }
output "ecr_repository_urls" { value = { for k, r in aws_ecr_repository.this : k => r.repository_url } }

# ---- Optional Redis backplane so SignalR pushes reach every API task (ADR 0009) ----
resource "aws_elasticache_subnet_group" "redis" {
  count      = var.enable_redis_backplane ? 1 : 0
  name       = "${var.name}-redis"
  subnet_ids = var.private_subnet_ids
}

resource "aws_security_group" "redis" {
  count  = var.enable_redis_backplane ? 1 : 0
  name   = "${var.name}-redis"
  vpc_id = var.vpc_id
  ingress {
    from_port       = 6379
    to_port         = 6379
    protocol        = "tcp"
    security_groups = [aws_security_group.app.id]
  }
  egress {
    from_port   = 0
    to_port     = 0
    protocol    = "-1"
    cidr_blocks = ["0.0.0.0/0"]
  }
}

resource "aws_elasticache_replication_group" "redis" {
  count                      = var.enable_redis_backplane ? 1 : 0
  replication_group_id       = substr("${var.name}-redis", 0, 40)
  description                = "SignalR backplane for ${var.name}"
  engine                     = "redis"
  node_type                  = var.redis_node_type
  num_cache_clusters         = var.environment == "production" ? 2 : 1
  automatic_failover_enabled = var.environment == "production"
  at_rest_encryption_enabled = true
  transit_encryption_enabled = true
  subnet_group_name          = aws_elasticache_subnet_group.redis[0].name
  security_group_ids         = [aws_security_group.redis[0].id]
  port                       = 6379
}
