# 兼容性分级

## A：原版存档安全

判定条件：

- 不引入自定义建筑、道路、资源或模型；
- 不要求自定义字段才能解释已经落盘的世界对象；
- 卸载后，原版仍能识别最终生成的建筑和网络。

属于此级别：

- `InfraSpaceConstructionTools`
- `InfraSpaceProductionInsights`

注意：ConstructionTools 会改变玩家执行建造操作的方式；“安全”仅指世界对象仍为原版对象。InfraSpace 会把每个 `Mod` 入口类型写入 `scriptMods`，因此加载已启用 Mod 的存档时仍需要对应程序集。

## B：可回退，但功能状态降级

判定条件：

- 最终网络仍由原版对象组成；
- Mod 额外保存编辑状态；
- 卸载后存档预计仍可读取，但再次编辑时无法恢复全部意图。

属于此级别：

- `InfraSpaceStationTools`

车站端口设置保存在 Mod 字典中。已建连接通常仍存在，但卸载后端口面板、镜像记录和后续编辑行为不可保证。

## C：需要 Mod 保持完整语义

判定条件：

- Mod 保存原版没有的业务状态；
- Mod 替换关键模拟循环；
- 卸载后即使存档可打开，模拟含义也会改变。

属于此级别：

- `InfraSpaceMultiResourceLogistics`

额外物资列表、每种物资的容量，以及仅存储开关保存在 Mod 状态中，多物资中心的模拟也由 Mod 实现。卸载后通常只剩原版主物资语义，库存、连接和配送结果可能降级。`CenterSettings.storageOnly` 记录不参与搜集和配送的物资名称；旧存档缺少该列表时视为空，全部物资保持正常。

## 持久身份注册表

InfraSpace 会序列化全部 Mod 入口，不仅是包含业务字段的 Mod。2026-10-26 一次性迁移后，四个身份统一并冻结为：

| 产品名称 | 稳定程序集/config | 序列化入口类型 | 主要持久字段 |
| --- | --- | --- | --- |
| `InfraSpaceConstructionTools` | `InfraSpaceConstructionTools` | `InfraSpaceConstructionTools.ConstructionToolsMod` | `suppressRoadMagnet` |
| `InfraSpaceProductionInsights` | `InfraSpaceProductionInsights` | `InfraSpaceProductionInsights.ProductionInsightsMod` | 无业务字段 |
| `InfraSpaceMultiResourceLogistics` | `InfraSpaceMultiResourceLogistics` | `InfraSpaceMultiResourceLogistics.MultiResourceLogisticsMod` | `centers`，以及迁移期车站字典 |
| `InfraSpaceStationTools` | `InfraSpaceStationTools` | `InfraSpaceStationTools.StationToolsMod` | 三个车站端口字典 |

旧身份只由 `tools/migrate_save_identities.py` 离线迁移：

- `InfraSpaceAreaMove.AreaMoveMod, AreaMove` → ConstructionTools；
- `InfraSpaceRoadSnap.RoadSnapMod, RoadSnap` 的开关合并到 ConstructionTools；
- `InfraSpaceProductionFilter.ProductionFilterMod, ProductionFilter` → ProductionInsights；
- `InfraSpaceMultiLogistics.MultiLogisticsMod, InfraSpaceMultiLogistics` → MultiResourceLogistics，原样保留 `centers` 和车站字典。

迁移工具只能写新存档副本，不得覆盖源存档。上述新身份从迁移完成后冻结；再次改名必须提供新的显式迁移并完成真实存档重载测试。

## 尚未证明的事项

以下结论需要游戏内存档测试，不能只凭编译成功宣称：

1. 迁移副本能否完整加载四个统一身份；
2. 禁用各 Mod 后，原版能否无警告载入同一存档；
3. 已在途车辆、正在移动的建筑和未完成施工在迁移时的行为；
4. Mod 加载顺序变化是否影响一次性状态导入；
5. InfraSpace 游戏版本升级后的 Harmony patch 稳定性。
