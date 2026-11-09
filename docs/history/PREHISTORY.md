# 项目前史与遗失历史

## 结论

`D:\mc 1\不注入游戏，然后直接读取内存` 证明项目在 2026-11-03 之前已经存在。当前最早可核验材料来自 **2026-11-01**，但原作者明确说明项目貌似还要更早；由于更早目录、提交或备份暂未找到，不能把 11 月 1 日写成绝对的“项目诞生日”。

## 2026-11-01 现存证据

- 遗留工程目录创建时间约为 23:28；
- `McPetOverlay.csproj` 与 `README.md` 的创建时间约为 23:46；
- `assets/pet-source.jpg` 已存在，文件修改时间为 23:36；
- 工程已经采用 .NET 8 Windows Forms、自包含 win-x64 和单文件发布配置。

这些证据说明：至少在 11 月 1 日晚，项目骨架、桌面宠物资源和发布目标已经形成。

## 2026-11-02 现存证据

遗留快照中保存了当天修改的：

- `BackgroundRemover.cs`；
- `Program.cs`；
- `PeExportReader.cs`；
- `MinecraftMemoryReader.cs`；
- `AppConfig.cs`；
- `HotSpotVmStructReader.cs`；
- `EspModels.cs`；
- `OverlayForm.cs`；
- `JvmEspDataSource.cs`；
- `AutoInputController.cs`；
- `NativeMethods.cs`；
- `SettingsForm.cs`；
- `THIRD_PARTY_NOTICES.md` 和早期项目文件。

旧 README 已明确写出“不注入 Minecraft、不加载模组、只读取 JVM 对象、不修改游戏文件”，并包含窗口跟随、宠物状态、点击穿透和自包含发布说明。

当天还保留 7 个 `PublishOutputs.*.txt`，时间从 21:28 到 22:16。这些文件是 MSBuild 发布流程的输出清单，只能证明当时多次执行了发布，无法恢复每次具体功能差异，因此在版本日志中使用 `P001` 至 `P007` 标为“早期发布痕迹”。

## 与后续工程的关系

遗留目录包含与当前工程同名或直接演进的关键类：

- `MinecraftMemoryReader`
- `HotSpotVmStructReader`
- `JvmEspDataSource`
- `OverlayForm`
- `SettingsForm`
- `AutoInputController`

11 月 3 日以后，代码逐步按 `Infrastructure/`、`Features/`、`Models/` 和 `UI/` 重组，并继续增加 HUD、装备、背包、预测和三套 UI。该结构和类名连续性构成了前史与当前源码之间的重要证据。

## 无法恢复的部分

- 11 月 1 日以前的源码和发布包；
- 最初创建项目的准确日期；
- 早期每次发布对应的功能说明；
- 更早提交平台、账号和提交记录；
- 可能已删除、移动或覆盖的原始备份。

如果后续找到旧硬盘、聊天附件、网盘、平台通知、构建包或截图，应保留原文件时间和哈希，并追加到证据索引，而不是改写现有时间戳。
