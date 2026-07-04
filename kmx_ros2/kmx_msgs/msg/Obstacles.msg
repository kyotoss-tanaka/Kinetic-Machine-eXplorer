# kmx_msgs/Obstacles
# Unity → ROS2：ロボット周辺の障害物プリミティブ群。ROS2側で CollisionObject 化して planning scene へ。
string frame_id                  # 通常 "base_link"（items の pose の基準フレーム）
ObstaclePrimitive[] items
