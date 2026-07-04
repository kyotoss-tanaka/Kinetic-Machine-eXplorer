# kmx_msgs/PlanRequest
# Unity → ROS2：始点/終点（関節角・度）で関節空間の経路生成を要求する。
# 単位は他の kmx トピックと同じく「度」。ROS2ノード側で必要に応じ rad へ変換する。
string[] names    # 関節名。例 ["J1","J2","J3","J4","J5","J6"]。空なら受信側の既定順。
float64[] start   # 始点 関節角（度）
float64[] goal    # 終点 関節角（度）
