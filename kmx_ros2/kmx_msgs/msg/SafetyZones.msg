# DCS 安全ゾーン一覧（latched topic /kmx/safety_zones 用）。DCS_ZONE_ROS2_LIVE_SPEC.md §4。
string robot_id           # 対象ロボ（"" =既定/単機）。Ros2Info robots と対応
string frame              # 通常 "world"（UF0）
string unit               # "mm"
SafetyZone[] zones
