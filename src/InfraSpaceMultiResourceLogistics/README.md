# InfraSpace Multi-Resource Logistics

让配送中心和搜集中心同时处理多种物资。

## 功能

- 顶部直接排列所有已选物资图标，末尾的 `+` 用于添加物资。
- 右键物资图标可移除；存在库存或在途车辆时会先弹出确认框。
- 原版“存储”区域改为物资列表，每种物资都有独立库存显示和容量滑块。
- 每种已选物资使用独立库存槽和独立容量。
- 搜集中心会从范围内生产建筑拉取所有已选物资。
- 配送中心会按库存缺口向范围内消费建筑配送所有已选物资。
- 已连接的搜集中心与配送中心会转运双方共同选择的物资。
- 物资选择随游戏存档保存。
- 配送按原版上报的需求分数排序，再结合库存缺口派车。
- 物流中心出口车辆会延后执行必要变道，减少出口附近交叉阻塞。

## 不属于本 Mod 的功能

车站轨道/道路端口方向、镜像和列车出生轨道已经拆到 `InfraSpaceStationTools`。

## 存档状态

以下数据由 `MultiResourceLogisticsMod` 随存档保存：

- 每个中心选择的物资名称；
- 每种物资的独立容量；
- 为迁移旧存档暂时保留的车站端口字典。

旧车站字典只供 StationTools 一次性导入，不再驱动车站功能。

## 构建

项目默认引用：

`F:\SteamLibrary\steamapps\common\InfraSpace\InfraSpace_Data\Managed`

如果游戏安装位置不同，可通过 `GameManagedPath` 属性覆盖：

```powershell
dotnet build .\InfraSpaceMultiResourceLogistics.csproj -c Release -p:GameManagedPath="D:\Games\InfraSpace\InfraSpace_Data\Managed"
```

## 安装

把以下文件复制到：

`%USERPROFILE%\AppData\LocalLow\Dionic Software\InfraSpace\mods\InfraSpaceMultiResourceLogistics`

- `InfraSpaceMultiResourceLogistics.dll`
- `0Harmony.dll`
- `config.json`

进入游戏后，在模组列表启用该模组。

## 兼容性

- 兼容等级：C（需要 Mod 保持完整语义）。
- 卸载后存档可能仍能打开，但额外物资、容量和多物资调度会丢失或降级。
- 首次迁移请使用备份存档验证。

## 旧标识

程序集、配置名和根命名空间统一为 `InfraSpaceMultiResourceLogistics`。旧 `InfraSpaceMultiLogistics` 存档必须先使用仓库迁移工具生成副本。
