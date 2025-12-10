# 需要环境变量
# export ALICLOUD_ACCESS_KEY="***"
# export ALICLOUD_SECRET_KEY="***"
# export ALICLOUD_REGION="cn-hangzhou"

terraform {
  required_providers {
    alicloud = {
      source  = "aliyun/alicloud"
      version = "~> 1.263"
    }
  }
}

provider "alicloud" {}

resource "alicloud_resource_manager_resource_group" "default" {
  resource_group_name = "cyberbullfight-echo-test"
  display_name        = "cyberbullfight-echo-横向测试"
}
