# kmx_msgs/PlanRequest
# Unity → ROS2：始点/終点（関節角・度）で関節空間の経路生成を要求する。
# 単位は他の kmx トピックと同じく「度」。ROS2ノード側で必要に応じ rad へ変換する。
string[] names    # 関節名。例 ["J1","J2","J3","J4","J5","J6"]。空なら受信側の既定順。
float64[] start   # 始点 関節角（度）
float64[] goal    # 終点 関節角（度）
# --- 任意（Unity から計画の粘り具合を制御。0以下 or 未設定なら ROS2 ノード既定を使う＝後方互換） ---
float64 time_budget   # 計画の総時間予算(秒)。難しい姿勢は大きく、簡単なら小さく。0以下=既定 plan_time_budget_sec
float64 good_ratio    # 大回り回避の許容倍率(始点→終点の直線関節距離比)。小さいほど短経路を要求。0以下=既定 plan_good_ratio
# --- 複数ロボット対応（MULTI_ROBOT_ROS2_SPEC.md）。空="" なら受信側の既定ロボ/group（後方互換） ---
string robot_id       # どのロボット/機種で計画するか。ROS2 側 robot_map のキーと一致させる（例 "crx30ia_1"）
