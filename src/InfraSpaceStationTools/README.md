# InfraSpace Station Tools

车站端口和列车方向辅助 Mod。

## 功能

- 放置车站时切换每个轨道端口的方向；
- 切换公路端口为入口或出口；
- 对已有车站进入端口编辑模式；
- 复制蓝图时复制车站端口配置；
- 新增列车时优先选择朝向线路下一站的出生轨道；
- 车站移动、拆除和重建期间维护端口状态。

具体按键和面板提示由 `StationMirror` 根据当前放置/编辑状态显示。

## 旧存档迁移

早期版本把车站状态保存在旧物流入口中。离线迁移会把这些字段保留在 `InfraSpaceMultiResourceLogistics.MultiResourceLogisticsMod`；本 Mod 启动后会在有限帧数内寻找该实例，并把以下字典中尚未存在的记录复制到自己的状态：

- `mirroredTrainStations`
- `trainStationRailPorts`
- `trainStationRoadPorts`

迁移后必须保存一次，再重载新存档验证。完成一个发布周期前，不删除旧物流 Mod 中的兼容字段。

## 兼容性

- 已建网络仍是原版对象；
- 端口选择和后续编辑意图依赖 Mod 状态；
- 兼容等级：B（可回退，但功能状态降级）。

## 可选集成

如果检测到 ConstructionTools 的蓝图复制会话，本 Mod 使用 Harmony 后缀复制端口状态；未安装 ConstructionTools 时不影响加载。
