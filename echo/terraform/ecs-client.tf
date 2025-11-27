variable "ecs_client_type" {
  default = "ecs.t5-c1m1.large"
}

data "alicloud_zones" "client" {
  available_instance_type = var.ecs_client_type
}

resource "alicloud_vswitch" "vsw_client" {
  vswitch_name = "test_vsw_client"
  vpc_id       = alicloud_vpc.default.id
  cidr_block   = var.ecs_client_cidr
  zone_id      = data.alicloud_zones.client.zones.1.id
}

resource "alicloud_security_group" "ecs-client" {
  security_group_name = "ecs_sg_client"
  resource_group_id   = alicloud_resource_manager_resource_group.default.id
  vpc_id              = alicloud_vpc.default.id
}

resource "alicloud_security_group_rule" "client-ssh" {
  type              = "ingress"
  ip_protocol       = "tcp"
  nic_type          = "intranet"
  policy            = "accept"
  port_range        = "22/22"
  priority          = 1
  security_group_id = alicloud_security_group.ecs-client.id
  cidr_ip           = "0.0.0.0/0"
}

resource "alicloud_instance" "client" {
  availability_zone          = alicloud_vswitch.vsw_client.zone_id
  security_groups            = alicloud_security_group.ecs-client.*.id
  instance_type              = var.ecs_client_type
  image_id                   = var.image_id
  instance_charge_type       = "PostPaid"
  instance_name              = "test-client-${count.index + 1}"
  host_name                  = "test-client-${count.index + 1}"
  count                      = 1
  resource_group_id          = alicloud_resource_manager_resource_group.default.id
  vswitch_id                 = alicloud_vswitch.vsw_client.id
  internet_max_bandwidth_out = 100
  user_data = templatefile("${path.module}/scripts/init.sh", {
    DOCKER_MIRROR = "bruce48li/cyberbullfight-client-go"
  })
  image_options {
    login_as_non_root = true
  }
}

output "client_public_ip" {
  value = alicloud_instance.client.*.public_ip
}

output "client_private_ip" {
  value = alicloud_instance.client.*.private_ip
}

resource "alicloud_ecs_key_pair_attachment" "attach-client" {
  key_pair_name = alicloud_ecs_key_pair.default.id
  instance_ids  = alicloud_instance.client.*.id
}