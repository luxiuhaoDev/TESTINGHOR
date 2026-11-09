# 逐日开发日志（证据重建）

> 文件时间只能证明本地材料在相应时间存在或被修改，不能等价为公开提交时间。缺少证据的日期明确留空，不补写推测。

## 2026-11-01

现存最早工程目录和项目文件创建，宠物原始素材已存在。项目实际起源可能更早，但更早材料目前遗失。

**源码证据**
- 无独立 `.cs` 文件时间证据。

**其他工程证据**
- 23:28 左右 — 遗留工程目录创建。
- 23:36:53 — `assets/pet-source.jpg` 的现存修改时间。
- 23:46:28 — 早期 `McPetOverlay.csproj` 与 `README.md` 创建时间。
- 现存最早工程已采用 .NET 8 WinForms、自包含 win-x64 与单文件发布目标。

**构建/测试节点**
- 无独立 `artifacts` 节点。

**备份证据**
- 无独立备份目录。

## 2026-11-02

形成早期“不注入游戏、直接只读内存”的完整链路：进程读取、HotSpot、ESP、Overlay、设置、输入和多次发布尝试。

**源码证据**
- 00:07:34 — `旧源码备份-20261103-195240/BackgroundRemover.cs`
- 19:01:28 — `旧源码备份-20261103-195240/Program.cs`
- 20:00:01 — `旧源码备份-20261103-195240/PeExportReader.cs`
- 20:11:54 — `旧源码备份-20261103-195240/MinecraftMemoryReader.cs`
- 22:19:01 — `旧源码备份-20261103-195240/AppConfig.cs`
- 22:19:01 — `旧源码备份-20261103-195240/HotSpotVmStructReader.cs`
- 22:19:01 — `旧源码备份-20261103-195240/EspModels.cs`
- 22:19:01 — `旧源码备份-20261103-195240/OverlayForm.cs`
- 22:21:21 — `旧源码备份-20261103-195240/JvmEspDataSource.cs`
- 23:30:46 — `旧源码备份-20261103-195240/AutoInputController.cs`
- 23:44:12 — `旧源码备份-20261103-195240/NativeMethods.cs`
- 23:44:12 — `旧源码备份-20261103-195240/SettingsForm.cs`

**构建/测试节点**
- 21:28:13 — **P001** `PublishOutputs.a84b075332.txt`：早期 MSBuild 发布输出清单，具体功能说明已遗失。
- 21:39:24 — **P002** `PublishOutputs.fbee2b8470.txt`：早期 MSBuild 发布输出清单，具体功能说明已遗失。
- 21:41:43 — **P003** `PublishOutputs.cb63a672e5.txt`：早期 MSBuild 发布输出清单，具体功能说明已遗失。
- 21:50:01 — **P004** `PublishOutputs.e3ca8958e2.txt`：早期 MSBuild 发布输出清单，具体功能说明已遗失。
- 21:52:36 — **P005** `PublishOutputs.0a352c762c.txt`：早期 MSBuild 发布输出清单，具体功能说明已遗失。
- 22:01:02 — **P006** `PublishOutputs.fe18ca8fb3.txt`：早期 MSBuild 发布输出清单，具体功能说明已遗失。
- 22:16:52 — **P007** `PublishOutputs.474a26bab3.txt`：早期 MSBuild 发布输出清单，具体功能说明已遗失。

**备份证据**
- 无独立备份目录。

## 2026-11-03

整理并拆分早期源码，建立更清晰的 PE/内存/JVM 数据结构模型和瀑布窗口。

**源码证据**
- 15:10:28 — `Infrastructure/Jvm/HotSpotDiagnostic.cs`
- 15:10:28 — `Infrastructure/Jvm/JvmClassLayout.cs`
- 15:10:28 — `Infrastructure/Jvm/JvmJavaField.cs`
- 15:10:28 — `Infrastructure/Jvm/LoadedJvmClass.cs`
- 15:10:28 — `Infrastructure/Jvm/MinecraftRuntimePointers.cs`
- 15:10:28 — `Infrastructure/Jvm/VmStructField.cs`
- 15:10:28 — `Infrastructure/Memory/MemoryReadStatus.cs`
- 15:10:28 — `Infrastructure/Memory/PeExportReader.cs`
- 15:10:28 — `Infrastructure/Memory/RemoteModule.cs`
- 15:10:28 — `Models/DisplayMode.cs`
- 15:10:28 — `Properties/AssemblyInfo.cs`
- 15:10:28 — `UI/Forms/BackgroundRemover.cs`
- 15:17:38 — `App/ApplicationConfiguration.cs`
- 19:54:34 — `Infrastructure/Memory/MinecraftMemoryReader.cs`
- 22:09:42 — `UI/Controls/WaterfallWindow.cs`

**构建/测试节点**
- 无独立 `artifacts` 节点。

**备份证据**
- 无独立备份目录。

## 2026-11-04

加入主题、BlockHit/击退控制和实体类型基础。

**源码证据**
- 18:47:12 — `UI/Theme/EpsilonTheme.cs`
- 20:55:52 — `Features/BlockHit/MyauBlockHitController.cs`
- 21:17:46 — `Features/Esp/EntityKind.cs`
- 21:21:24 — `Features/Knockback/KnockbackController.cs`

**构建/测试节点**
- 无独立 `artifacts` 节点。

**备份证据**
- 无独立备份目录。

## 2026-11-05

建立输入控制器、左右键逻辑、自动潜行和自动瞄准策略。

**源码证据**
- 18:09:41 — `Features/Input/AutoInputController.cs`
- 18:09:41 — `Features/Clickers/AutoInputController.Clickers.cs`
- 18:09:41 — `Features/AutoSneak/AutoInputController.AutoSneak.cs`
- 21:32:10 — `Features/AutoAim/AutoAimPolicy.cs`

**构建/测试节点**
- 无独立 `artifacts` 节点。

**备份证据**
- 无独立备份目录。

## 2026-11-06

没有可核验的独立源码、发布目录或备份时间点；为保持逐日记录完整，本日不推测开发内容。

**源码证据**
- 无独立 `.cs` 文件时间证据。

**构建/测试节点**
- 无独立 `artifacts` 节点。

**备份证据**
- 无独立备份目录。

## 2026-11-07

集中完成诊断日志、装备/HUD、目标布局与 ESP 分类、缓存和过滤修复。

**源码证据**
- 11:14:41 — `Infrastructure/Diagnostics/AppLog.cs`
- 20:46:17 — `Features/Esp/EquipmentSnapshot.cs`
- 21:27:56 — `UI/Forms/HudEditorForm.cs`

**构建/测试节点**
- 21:11:21 — **R001** `publish-hud-20261107`：建立 HUD 编辑与显示基础。
- 21:31:08 — **R002** `publish-hud-style-20261107`：完善 HUD 字体、颜色、背景和样式。
- 21:47:44 — **R003** `publish-equipment-multitarget-20261107`：增加装备显示与多目标表现。
- 22:06:51 — **R004** `publish-target-layout-animation-20261107`：增加目标卡片布局与进入动画。
- 22:25:41 — **R005** `publish-nearby-range-20261107`：增加附近目标范围配置。
- 22:34:14 — **R006** `publish-esp-regression-fix-20261107`：修复 ESP 回归问题。
- 22:58:17 — **R007** `publish-esp-diagnostics-20261107`：增加 ESP 诊断信息。
- 23:00:31 — **R008** `publish-esp-candidates-20261107`：增加实体候选诊断。
- 23:04:07 — **R009** `publish-esp-class-diagnostics-20261107`：增加 JVM 实体类诊断。
- 23:06:17 — **R010** `publish-player-classification-fix-20261107`：修复玩家实体分类。
- 23:08:54 — **R011** `publish-player-cache-fix-20261107`：修复玩家缓存更新。
- 23:13:23 — **R012** `publish-player-diagnostic-20261107`：增加玩家读取诊断。
- 23:19:45 — **R013** `publish-hidden-filter-fix-20261107`：修复隐藏名称过滤。
- 23:23:33 — **R014** `publish-esp-final-20261107`：形成当日 ESP 稳定整合版本。

**备份证据**
- 无独立备份目录。

## 2026-11-08

高强度扩展启动动画、箱子/背包、床 ESP、火球/弓箭预测、统一热键和 Lunar 兼容稳定性。

**源码证据**
- 00:16:16 — `UI/Forms/StartupAnimationForm.cs`
- 00:29:05 — `App/Program.cs`
- 01:57:51 — `Models/ChestInventorySnapshot.cs`
- 01:58:26 — `Features/Inventory/InventoryItemEvaluator.cs`
- 01:58:41 — `Features/Inventory/ChestStealerController.cs`
- 12:44:05 — `Features/Inventory/InventoryManagerController.cs`
- 14:29:33 — `Features/Esp/BedEspSnapshot.cs`
- 15:22:17 — `Features/Esp/EntitySnapshot.cs`
- 15:22:17 — `Features/Esp/GameSnapshot.cs`
- 15:22:17 — `Models/LocalPlayerState.cs`
- 15:22:40 — `Infrastructure/Jvm/HotSpotVmStructReader.cs`
- 16:19:20 — `Features/Esp/FireballPredictionSnapshot.cs`
- 16:30:32 — `Infrastructure/Minecraft/IEspDataSource.cs`
- 16:30:32 — `Infrastructure/Minecraft/EmptyEspDataSource.cs`
- 16:58:54 — `Features/AutoAim/BowBallistics.cs`
- 22:30:05 — `Configuration/ModuleHotkeyIds.cs`
- 22:30:05 — `Models/PlayerInventorySnapshot.cs`
- 22:32:22 — `Features/Input/QuickItemUseController.cs`
- 22:41:28 — `Infrastructure/Windows/NativeMethods.cs`
- 23:25:17 — `UI/Forms/OverlayForm.cs`
- 23:25:27 — `Infrastructure/Minecraft/JvmEspDataSource.cs`

**构建/测试节点**
- 00:19:29 — **R015** `publish-startup-animation-20261108`：加入启动动画。
- 00:31:15 — **R016** `publish-focus-restore-20261108-003023`：修复启动动画后的游戏焦点恢复。
- 00:47:28 — **R017** `publish-chest-stealer-20261108-004640`：加入箱子物品选择与自动操作基础。
- 00:53:15 — **R018** `publish-chest-stealer-settings-20261108-005223`：加入箱子管理设置。
- 01:00:34 — **R019** `publish-chest-confirm-20261108-005942`：加入操作后读取确认。
- 01:02:03 — **R020** `publish-chest-two-pass-20261108-010113`：加入两遍扫描流程。
- 01:10:21 — **R021** `publish-chest-controls-20261108-010934`：完善箱子管理控制项。
- 01:14:16 — **R022** `publish-chest-auto-close-20261108-011326`：加入自动关闭容器。
- 01:15:28 — **R023** `publish-chest-dual-close-20261108-011435`：修复/兼容双路径关闭。
- 12:40:40 — **R024** `publish-inventory-v14`：加入背包管理 v14。
- 12:45:44 — **R025** `publish-inventory-v14-fix`：修复背包管理 v14。
- 14:33:08 — **R026** `publish-bed-esp-v15`：加入床 ESP v15。
- 14:37:40 — **R027** `publish-bed-esp-v15-final`：完成床 ESP v15 稳定版。
- 14:43:36 — **R028** `publish-bed-esp-hotkey`：加入床 ESP 热键。
- 14:48:22 — **R029** `publish-bed-custom-hotkey-v16`：加入可自定义床 ESP 热键 v16。
- 14:57:24 — **R030** `publish-unified-hotkeys-v17`：统一模块热键系统 v17。
- 15:49:21 — **R031** `publish-nearest-stable`：形成邻近目标/实体读取稳定节点。
- 16:02:50 — **R032** `publish-held-fireball-preview`：加入手持火球轨迹预览。
- 16:23:19 — **R033** `publish-world-fireball-trails`：加入世界火球历史轨迹。
- 16:33:17 — **R034** `publish-fireball-trajectory-toggle-owner`：加入火球轨迹开关与所有者处理。
- 17:04:19 — **R035** `publish-bow-prediction-toggle`：加入弓箭预测开关。
- 22:15:02 — **R036** `publish-lunar-stability-fix`：修复 Lunar 环境下的稳定性问题。
- 22:22:34 — **R037** `publish-autoaim-cadence-fix`：修复自动瞄准更新节奏。
- 22:33:56 — **R038** `publish-split-aim-quick-items`：拆分瞄准与快捷物品处理。
- 22:42:47 — **R039** `publish-lunar-fast-mouse-hotkeys`：优化 Lunar 环境鼠标/热键响应。
- 22:49:48 — **R040** `publish-no-stale-mouse-input`：清理过期鼠标输入。
- 22:52:31 — **R041** `publish-mouse-input-isolation`：隔离不同输入通道状态。
- 22:57:11 — **R042** `publish-lunar-fast-turn-v2`：迭代快速转向 v2。
- 23:07:21 — **R043** `publish-lunar-freeze-worker-main`：尝试将冻结相关处理移入工作线程。
- 23:07:59 — **R044** `publish-lunar-freeze-worker-clean`：清理工作线程实现。
- 23:08:57 — **R045** `publish-lunar-freeze-worker-final`：形成工作线程最终候选。
- 23:19:15 — **R046** `publish-rollback-lunar-freeze-worker`：因稳定性原因回滚该工作线程方案。
- 23:27:34 — **R047** `publish-pre-lunar-aim-with-quick-items`：保留回滚前瞄准与快捷物品整合基线。

**备份证据**
- 无独立备份目录。

## 2026-11-09

完成 UI 动画、环境特效、三套 UI、自由工作台、磁吸缩放、正式发布与回滚备份。

**源码证据**
- 00:00:11 — `UI/Controls/EpsilonToggle.cs`
- 00:32:16 — `UI/Forms/SettingsBackdropForm.cs`
- 00:45:03 — `UI/Effects/SettingsAmbientEffectRenderer.cs`
- 19:45:43 — `Configuration/AppConfig.cs`
- 20:34:50 — `UI/Controls/EpsilonPanelControls.cs`
- 20:34:50 — `UI/Controls/EpsilonDropdownControls.cs`
- 20:34:50 — `UI/Forms/EpsilonWorkbenchCoordinator.cs`
- 20:44:45 — `UI/Forms/SettingsForm.cs`

**构建/测试节点**
- 00:01:44 — **R048** `publish-ui-motion`：加入 UI 运动与切换动画。
- 00:20:02 — **R049** `publish-ambient-effects`：加入设置界面环境特效。
- 00:25:15 — **R050** `test-ambient-layer`：测试环境特效层级。
- 00:28:23 — **R051** `test-ambient-zorder`：测试环境特效 Z 顺序。
- 00:29:39 — **R052** `publish-ambient-effects-fixed`：修复环境特效合成。
- 00:33:25 — **R053** `test-ambient-dedicated-layer`：测试独立环境特效层。
- 00:37:33 — **R054** `publish-ambient-visible`：修复环境特效可见性。
- 00:41:59 — **R055** `publish-ambient-card-clipped`：处理卡片裁剪问题。
- 00:44:29 — **R056** `publish-ambient-outside-ui`：允许特效在设置卡片外绘制。
- 00:46:07 — **R057** `publish-ambient-size`：加入环境特效尺寸调整。
- 01:01:31 — **R058** `bundle-integration-verify`：验证整合包内容和运行。
- 01:02:14 — **R059** `bundle-integration-stage`：形成最终整合阶段包。

**备份证据**
- 00:18:36 — `20261109-before-ambient-effects`
- 01:01:32 — `20261109-010051-before-bundle-replace`
- 01:02:14 — `20261109-010214-before-final-bundle-replace`
- 18:17:44 — `20261109-181744-before-workbench-prototype`
- 19:12:48 — `20261109-191248-before-docked-workbench`
- 20:54:16 — `20261109-205416-before-official-workbench-release`
