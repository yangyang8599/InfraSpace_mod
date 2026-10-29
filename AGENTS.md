# InfraSpace Mods 开发约定

## 目标

维护 `src/` 下四个边界清晰、可独立安装的 InfraSpace Mod。修改前先判断功能属于建造、生产分析、物流模拟还是车站拓扑，不跨域堆放代码。

## 必须遵守

- 导入语句只放在文件顶部。
- Harmony ID 必须按发布 Mod 唯一，禁止两个项目 patch 同一功能实现。
- 跨 Mod 集成使用反射或可选 Harmony patch；禁止项目引用形成安装依赖。
- 新增持久字段必须可序列化、可为空恢复，并写入兼容性文档。
- 产品显示名、源码目录、程序集、配置和根命名空间统一使用四个产品标识；入口类型见 `docs/COMPATIBILITY.md`。
- 2026-10-26 存档迁移后的四个持久身份已冻结，不再直接重命名程序集、配置名、根命名空间、入口类型、字段名或 Harmony ID。
- `InfraSpaceMultiResourceLogistics` 的持久身份固定为 `InfraSpaceMultiResourceLogistics`；所有物流中心写入 `MultiResourceLogisticsMod.centers`。
- `InfraSpaceStationTools` 的持久身份固定为 `InfraSpaceStationTools`，不得与物流 Mod 共用程序集、配置名、入口类型或 Harmony ID。
- 旧版四个 Mod 的存档只通过 `tools/migrate_save_identities.py` 迁移到新副本；迁移后必须执行“载入 → 另存 → 完全重启 → 重载”验证。
- `research/` 只作证据和实验，不得被发布项目编译。
- 修改 Harmony patch 后至少构建整个解决方案；涉及存档状态时还要列出游戏内验证步骤。
- 物流 Mod 改完后直接运行 `src/InfraSpaceMultiResourceLogistics/Install.ps1` 覆盖安装目录。游戏由用户自己重启加载；文件被占用导致覆盖失败时再说明。

## 文件操作

- 搜索、读取和单文件修改优先使用 Cursor 专用文件工具，以保留清晰差异。
- 批量移动、复制或重命名应优先使用专用文件 API；当前运行环境允许时可使用可审计的 PowerShell 命令。
- 仅在需要回收站、系统对话框，或没有可用文件/命令工具时使用屏幕视觉操作；使用前说明原因。
- 删除、覆盖和跨目录迁移前先确认源、目标与回退方式，不使用不可恢复的命令。

## 完成定义

代码、对应 Mod 的 README、架构/兼容性文档保持一致；四个项目构建无警告无错误；安装脚本复制的 DLL 名与 `config.json` 名称满足游戏加载器的前缀要求。
