"""相容舊名稱：部署設定已改為依發佈目標顯示欄位。"""

from .deploy_settings_dialog import show_deploy_settings as show_gcp_settings

__all__ = ["show_gcp_settings"]
