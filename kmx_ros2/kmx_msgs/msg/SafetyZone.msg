# DCS(Dual Check Safety) CPC の1ゾーン（$DCSS_CPC[i] 由来）。
# robot World/base フレーム・単位 mm（KMX 側で ×0.001・軸写像）。DCS_ZONE_ROS2_LIVE_SPEC.md §4。
string id                 # 例 "CPC1" or $COMMENT（"KMX_TEST"）。ゾーン識別
bool enabled              # $ENABLE（1=有効）
bool inside_allowed       # true=内側が安全域 / false=内側が進入禁止(keep-out)。$MODE から変換
float64[3] min_mm         # [X1,Y1,Z1] 下限（$X[1],$Y[1],$Z1）
float64[3] max_mm         # [X2,Y2,Z2] 上限（$X[2],$Y[2],$Z2）
