# 架构设计

## 决策

采用一个本地仓库、四个发布 Mod。兼容等级只用于文档和发布分组，程序集边界按功能域划分。

## 模块边界

### InfraSpaceConstructionTools

职责是玩家主动触发的建造辅助：

- `Ctrl+M` 框选批量移动建筑和网络；
- `Ctrl+C` 框选复制为即时蓝图；
- `H` 在道路施工时切换道路磁吸；
- 升级铁路时按住 `Shift` 批量升级连通铁路。

它可以调用原版建造、销毁、连接和资源结算流程，但不得保存自定义世界实体。

### InfraSpaceProductionInsights

职责是只读采样与界面呈现：

- 隐藏地区生产面板中就业、生产、消费均为零的行；
- 从最终产物需求沿生产链反推中间物资需求；
- 电力可视化按电网给供电范围分色，不改建筑外观；
- 可视化面板下列出当前有工厂生产的物品，点选后突出对应生产建筑；
- 不得写回工厂、住宅、市场、地区或电网模拟状态。

### InfraSpaceMultiResourceLogistics

职责是物流中心模拟：

- 多物资选择和独立容量，单项最高 2000；
- 收集、配送、中心间转运和需求评分，一种或多种物资使用同一套调度；
- 搜集中心没有一整车空位时，把生产建筑的多余整车交给公共需求，并包含已挂到配送中心的仓库；
- 工厂配方显示输入来源、输出去向，以及搜集中心优先、其次全局处理；工业机器人和高科技工具的提示显示是否正在配送；
- 物流中心面板、库存和在途信息；
- 仅针对物流中心出口拥堵的变道修正。

该模块允许替换收集/配送中心的原版模拟，但不得包含车站端口或列车线路编辑功能。

### InfraSpaceStationTools

职责是车站和端口拓扑：

- 放置或编辑车站时切换轨道方向；
- 切换公路入口/出口；
- 保存每个车站的端口设置；
- 新增列车时优先选择朝向下一站的出生轨道。

它不依赖多物资物流功能。迁移后的物流入口仍暂存旧车站字典，StationTools 会通过反射一次性导入。

## 可选集成

跨 Mod 集成必须是可选的，不允许建立编译时项目引用：

- ConstructionTools 复制建筑后，物流 Mod 可通过 Harmony 后缀复制多物资设置；
- StationTools 可用同样方式复制车站端口设置；
- 任一 Mod 未安装时，其他 Mod 仍须正常加载和运行。

## 产品名称与持久身份

产品名称用于源码目录、README、发布页和日常交流。程序集、配置、根命名空间和入口类型统一使用同一产品标识，并参与存档反序列化，一经发布即冻结。

| 产品名称 | 稳定持久身份 | 说明 |
| --- | --- | --- |
| InfraSpace Construction Tools | `InfraSpaceConstructionTools` | 不保存自定义世界状态 |
| InfraSpace Production Insights | `InfraSpaceProductionInsights` | 不保存自定义世界状态 |
| InfraSpace Multi-Resource Logistics | `InfraSpaceMultiResourceLogistics` | 旧身份通过离线存档迁移转换 |
| InfraSpace Station Tools | `InfraSpaceStationTools` | 从首次独立发布起冻结 |

持久身份包括程序集名、配置名、根命名空间、入口类型、Harmony ID，以及已落盘字段的名称和类型。每个发布 Mod 必须使用唯一身份，不能为了视觉统一让两个 Mod 共用一个入口或 Harmony ID。

2026-10-26 的一次性迁移把 `AreaMove`、`RoadSnap`、`ProductionFilter` 和 `InfraSpaceMultiLogistics` 转换为上表身份。迁移仅作用于新存档副本，原存档及完整备份保持不变。

所有多物资中心继续以建筑 ID 为键写入迁移后的 `MultiResourceLogisticsMod.centers`。三个旧车站状态字段暂时留在物流入口中，StationTools 通过反射导入。删除字段前，必须完成真实存档的“载入 → 另存 → 完全重启 → 重载”测试。
