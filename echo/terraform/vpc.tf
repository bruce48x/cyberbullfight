resource "alicloud_vpc" "default" {
  vpc_name          = "test_vpc"
  cidr_block        = var.vpc_cidr
  resource_group_id = alicloud_resource_manager_resource_group.default.id

  tags = {
    "app" = "cyberbullfight"
  }
}