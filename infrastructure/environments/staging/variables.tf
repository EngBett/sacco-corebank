variable "region" {
  type    = string
  default = "eu-west-1"
}
variable "availability_zones" {
  type    = list(string)
  default = ["eu-west-1a", "eu-west-1b"]
}
variable "domain" {
  type        = string
  description = "Base domain; api./portal. and tenant subdomains hang off it"
}
variable "certificate_arn" {
  type        = string
  description = "ACM certificate for api.<domain>, portal.<domain>, *.<domain>"
}
variable "image_tag" {
  type    = string
  default = "latest"
}
variable "db_instance_class" {
  type    = string
  default = "db.t4g.medium"
}
variable "desired_count" {
  type    = number
  default = 1
}
# Provider modes are configuration: Sandbox everywhere except production after the cutover runbook.
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
  default = false
}
variable "default_tenant_slug" {
  type    = string
  default = ""
}
variable "webhook_allowed_cidrs" {
  type    = list(string)
  default = []
}