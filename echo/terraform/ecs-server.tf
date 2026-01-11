variable "ecs_type" {
  default = "ecs.sn1ne.large"
}

data "alicloud_zones" "default" {
  available_instance_type = var.ecs_type
}

variable "image_id" {
  default = "debian_12_12_x64_20G_alibase_20251030.vhd"
}

resource "alicloud_vswitch" "vsw" {
  vswitch_name = "test_vsw"
  vpc_id       = alicloud_vpc.default.id
  cidr_block   = var.ecs_cidr
  zone_id      = data.alicloud_zones.default.zones[0].id
}

resource "alicloud_security_group" "ecs" {
  security_group_name = "ecs_sg"
  resource_group_id   = alicloud_resource_manager_resource_group.default.id
  vpc_id              = alicloud_vpc.default.id
}

resource "alicloud_security_group_rule" "ssh" {
  security_group_id = alicloud_security_group.ecs.id
  type              = "ingress"
  ip_protocol       = "tcp"
  nic_type          = "intranet"
  policy            = "accept"
  port_range        = "22/22"
  priority          = 1
  cidr_ip           = "0.0.0.0/0"
  description       = "ssh"
}

resource "alicloud_security_group_rule" "app" {
  security_group_id = alicloud_security_group.ecs.id
  type              = "ingress"
  ip_protocol       = "tcp"
  nic_type          = "intranet"
  policy            = "accept"
  port_range        = "3010/3019"
  priority          = 1
  cidr_ip           = "0.0.0.0/0"
  description       = "app ports"
}

resource "alicloud_instance" "instance" {
  availability_zone          = alicloud_vswitch.vsw.zone_id
  security_groups            = alicloud_security_group.ecs.*.id
  instance_type              = var.ecs_type
  image_id                   = var.image_id
  instance_charge_type       = "PostPaid"
  instance_name              = "test-server-${count.index + 1}"
  host_name                  = "test-server-${count.index + 1}"
  count                      = 1
  resource_group_id          = alicloud_resource_manager_resource_group.default.id
  vswitch_id                 = alicloud_vswitch.vsw.id
  internet_max_bandwidth_out = 100
  user_data = templatefile("${path.module}/scripts/init.sh", {
    DOCKER_MIRROR = "bruce48li/cyberbullfight-server-go"
  })
  image_options {
    login_as_non_root = true
  }
}

output "server_public_ip" {
  value = alicloud_instance.instance.*.public_ip
}

output "server_private_ip" {
  value = alicloud_instance.instance.*.private_ip
}

resource "alicloud_ecs_key_pair" "default" {
  key_pair_name = "my-key"
  public_key    = file("~/.ssh/id_rsa.pub")
}

resource "alicloud_ecs_key_pair_attachment" "attach" {
  key_pair_name = alicloud_ecs_key_pair.default.id
  instance_ids  = alicloud_instance.instance.*.id
}