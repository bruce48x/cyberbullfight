variable "vpc_cidr" {
  default = "172.20.0.0/16"
}

# 172.20.1.* & 172.20.2.*
variable "alb_cidr" {
  default = "172.20.%d.0/24"
}

variable "ecs_cidr" {
  default = "172.20.3.0/24"
}

variable "ecs_client_cidr" {
  default = "172.20.4.0/24"
}