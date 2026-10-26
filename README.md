# InfraSpace Mods

本仓库集中维护自有的 InfraSpace Mod。代码按功能域拆成四个可独立安装的 Mod，而不是按“兼容/不兼容”做成两个大包。

## Mod

- **InfraSpaceConstructionTools**：批量移动、蓝图复制、道路磁吸开关、铁路链式升级。
- **InfraSpaceProductionInsights**：生产概览空行筛选、最终产物需求反推、电力供电范围分色，以及按产物突出生产建筑。
- **InfraSpaceMultiResourceLogistics**：让收集中心和配送中心同时处理多种物资。
- **InfraSpaceStationTools**：车站轨道/道路端口方向、镜像及列车出生方向辅助。

## 目录

- `src/`：四个发布 Mod。
- `docs/`：架构、兼容性、历史对话和设计记录。
- `research/`：逆向分析工具、反编译片段和实验结果；不属于发布包。
- `.cursor/rules/`：工作区内 AI 开发规范。

## 构建

项目默认从以下位置引用游戏程序集：

`F:\SteamLibrary\steamapps\common\InfraSpace\InfraSpace_Data\Managed`

可通过 MSBuild 属性覆盖：

```powershell
dotnet build .\InfraSpaceMods.sln -c Release -p:GameManagedPath="D:\Games\InfraSpace\InfraSpace_Data\Managed"
```

默认路径正确时：

```powershell
dotnet build .\InfraSpaceMods.sln -c Release
```

## 兼容性原则

“原版存档安全”表示 Mod 最终只生成原版可识别的对象，卸载后不依赖自定义世界资产；不代表运行时行为完全不变。

“Mod 语义依赖”表示存档通常仍可打开，但卸载后自定义设置或模拟行为会丢失或降级。详细定义见 `docs/COMPATIBILITY.md`。

## 发布约束

- 每个 `src/` 子项目独立构建、安装和版本管理。
- 不把 `research/`、`bin/`、`obj/` 或历史对话导出放入发布包。
- 四个 Mod 的源码目录、程序集、配置和根命名空间统一使用产品标识。
- 旧版存档必须先用 `tools/migrate_save_identities.py` 生成迁移副本；不要覆盖原存档。
