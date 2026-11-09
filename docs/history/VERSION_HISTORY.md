# 逐版本开发日志（证据重建）

> 本记录由本地 `artifacts` 目录名称和最后修改时间重建，不是原始 Git 提交历史。编号 `Rxxx` 仅为本次整理生成的稳定索引。

| 重建编号 | 时间 | 类型 | 证据目录 | 说明 |
| --- | --- | --- | --- | --- |
| P001 | 2026-11-02 21:28:13 | 早期发布痕迹 | `PublishOutputs.a84b075332.txt` | MSBuild 生成的 PublishOutputs 清单；只能证明当时执行过发布流程，无法恢复对应版本说明。 |
| P002 | 2026-11-02 21:39:24 | 早期发布痕迹 | `PublishOutputs.fbee2b8470.txt` | MSBuild 生成的 PublishOutputs 清单；只能证明当时执行过发布流程，无法恢复对应版本说明。 |
| P003 | 2026-11-02 21:41:43 | 早期发布痕迹 | `PublishOutputs.cb63a672e5.txt` | MSBuild 生成的 PublishOutputs 清单；只能证明当时执行过发布流程，无法恢复对应版本说明。 |
| P004 | 2026-11-02 21:50:01 | 早期发布痕迹 | `PublishOutputs.e3ca8958e2.txt` | MSBuild 生成的 PublishOutputs 清单；只能证明当时执行过发布流程，无法恢复对应版本说明。 |
| P005 | 2026-11-02 21:52:36 | 早期发布痕迹 | `PublishOutputs.0a352c762c.txt` | MSBuild 生成的 PublishOutputs 清单；只能证明当时执行过发布流程，无法恢复对应版本说明。 |
| P006 | 2026-11-02 22:01:02 | 早期发布痕迹 | `PublishOutputs.fe18ca8fb3.txt` | MSBuild 生成的 PublishOutputs 清单；只能证明当时执行过发布流程，无法恢复对应版本说明。 |
| P007 | 2026-11-02 22:16:52 | 早期发布痕迹 | `PublishOutputs.474a26bab3.txt` | MSBuild 生成的 PublishOutputs 清单；只能证明当时执行过发布流程，无法恢复对应版本说明。 |
| R001 | 2026-11-07 21:11:21 | 发布 | `publish-hud-20261107` | 建立 HUD 编辑与显示基础。 |
| R002 | 2026-11-07 21:31:08 | 发布 | `publish-hud-style-20261107` | 完善 HUD 字体、颜色、背景和样式。 |
| R003 | 2026-11-07 21:47:44 | 发布 | `publish-equipment-multitarget-20261107` | 增加装备显示与多目标表现。 |
| R004 | 2026-11-07 22:06:51 | 发布 | `publish-target-layout-animation-20261107` | 增加目标卡片布局与进入动画。 |
| R005 | 2026-11-07 22:25:41 | 发布 | `publish-nearby-range-20261107` | 增加附近目标范围配置。 |
| R006 | 2026-11-07 22:34:14 | 发布 | `publish-esp-regression-fix-20261107` | 修复 ESP 回归问题。 |
| R007 | 2026-11-07 22:58:17 | 发布 | `publish-esp-diagnostics-20261107` | 增加 ESP 诊断信息。 |
| R008 | 2026-11-07 23:00:31 | 发布 | `publish-esp-candidates-20261107` | 增加实体候选诊断。 |
| R009 | 2026-11-07 23:04:07 | 发布 | `publish-esp-class-diagnostics-20261107` | 增加 JVM 实体类诊断。 |
| R010 | 2026-11-07 23:06:17 | 发布 | `publish-player-classification-fix-20261107` | 修复玩家实体分类。 |
| R011 | 2026-11-07 23:08:54 | 发布 | `publish-player-cache-fix-20261107` | 修复玩家缓存更新。 |
| R012 | 2026-11-07 23:13:23 | 发布 | `publish-player-diagnostic-20261107` | 增加玩家读取诊断。 |
| R013 | 2026-11-07 23:19:45 | 发布 | `publish-hidden-filter-fix-20261107` | 修复隐藏名称过滤。 |
| R014 | 2026-11-07 23:23:33 | 发布 | `publish-esp-final-20261107` | 形成当日 ESP 稳定整合版本。 |
| R015 | 2026-11-08 00:19:29 | 发布 | `publish-startup-animation-20261108` | 加入启动动画。 |
| R016 | 2026-11-08 00:31:15 | 发布 | `publish-focus-restore-20261108-003023` | 修复启动动画后的游戏焦点恢复。 |
| R017 | 2026-11-08 00:47:28 | 发布 | `publish-chest-stealer-20261108-004640` | 加入箱子物品选择与自动操作基础。 |
| R018 | 2026-11-08 00:53:15 | 发布 | `publish-chest-stealer-settings-20261108-005223` | 加入箱子管理设置。 |
| R019 | 2026-11-08 01:00:34 | 发布 | `publish-chest-confirm-20261108-005942` | 加入操作后读取确认。 |
| R020 | 2026-11-08 01:02:03 | 发布 | `publish-chest-two-pass-20261108-010113` | 加入两遍扫描流程。 |
| R021 | 2026-11-08 01:10:21 | 发布 | `publish-chest-controls-20261108-010934` | 完善箱子管理控制项。 |
| R022 | 2026-11-08 01:14:16 | 发布 | `publish-chest-auto-close-20261108-011326` | 加入自动关闭容器。 |
| R023 | 2026-11-08 01:15:28 | 发布 | `publish-chest-dual-close-20261108-011435` | 修复/兼容双路径关闭。 |
| R024 | 2026-11-08 12:40:40 | 发布 | `publish-inventory-v14` | 加入背包管理 v14。 |
| R025 | 2026-11-08 12:45:44 | 发布 | `publish-inventory-v14-fix` | 修复背包管理 v14。 |
| R026 | 2026-11-08 14:33:08 | 发布 | `publish-bed-esp-v15` | 加入床 ESP v15。 |
| R027 | 2026-11-08 14:37:40 | 发布 | `publish-bed-esp-v15-final` | 完成床 ESP v15 稳定版。 |
| R028 | 2026-11-08 14:43:36 | 发布 | `publish-bed-esp-hotkey` | 加入床 ESP 热键。 |
| R029 | 2026-11-08 14:48:22 | 发布 | `publish-bed-custom-hotkey-v16` | 加入可自定义床 ESP 热键 v16。 |
| R030 | 2026-11-08 14:57:24 | 发布 | `publish-unified-hotkeys-v17` | 统一模块热键系统 v17。 |
| R031 | 2026-11-08 15:49:21 | 发布 | `publish-nearest-stable` | 形成邻近目标/实体读取稳定节点。 |
| R032 | 2026-11-08 16:02:50 | 发布 | `publish-held-fireball-preview` | 加入手持火球轨迹预览。 |
| R033 | 2026-11-08 16:23:19 | 发布 | `publish-world-fireball-trails` | 加入世界火球历史轨迹。 |
| R034 | 2026-11-08 16:33:17 | 发布 | `publish-fireball-trajectory-toggle-owner` | 加入火球轨迹开关与所有者处理。 |
| R035 | 2026-11-08 17:04:19 | 发布 | `publish-bow-prediction-toggle` | 加入弓箭预测开关。 |
| R036 | 2026-11-08 22:15:02 | 发布 | `publish-lunar-stability-fix` | 修复 Lunar 环境下的稳定性问题。 |
| R037 | 2026-11-08 22:22:34 | 发布 | `publish-autoaim-cadence-fix` | 修复自动瞄准更新节奏。 |
| R038 | 2026-11-08 22:33:56 | 发布 | `publish-split-aim-quick-items` | 拆分瞄准与快捷物品处理。 |
| R039 | 2026-11-08 22:42:47 | 发布 | `publish-lunar-fast-mouse-hotkeys` | 优化 Lunar 环境鼠标/热键响应。 |
| R040 | 2026-11-08 22:49:48 | 发布 | `publish-no-stale-mouse-input` | 清理过期鼠标输入。 |
| R041 | 2026-11-08 22:52:31 | 发布 | `publish-mouse-input-isolation` | 隔离不同输入通道状态。 |
| R042 | 2026-11-08 22:57:11 | 发布 | `publish-lunar-fast-turn-v2` | 迭代快速转向 v2。 |
| R043 | 2026-11-08 23:07:21 | 发布 | `publish-lunar-freeze-worker-main` | 尝试将冻结相关处理移入工作线程。 |
| R044 | 2026-11-08 23:07:59 | 发布 | `publish-lunar-freeze-worker-clean` | 清理工作线程实现。 |
| R045 | 2026-11-08 23:08:57 | 发布 | `publish-lunar-freeze-worker-final` | 形成工作线程最终候选。 |
| R046 | 2026-11-08 23:19:15 | 发布 | `publish-rollback-lunar-freeze-worker` | 因稳定性原因回滚该工作线程方案。 |
| R047 | 2026-11-08 23:27:34 | 发布 | `publish-pre-lunar-aim-with-quick-items` | 保留回滚前瞄准与快捷物品整合基线。 |
| R048 | 2026-11-09 00:01:44 | 发布 | `publish-ui-motion` | 加入 UI 运动与切换动画。 |
| R049 | 2026-11-09 00:20:02 | 发布 | `publish-ambient-effects` | 加入设置界面环境特效。 |
| R050 | 2026-11-09 00:25:15 | 测试 | `test-ambient-layer` | 测试环境特效层级。 |
| R051 | 2026-11-09 00:28:23 | 测试 | `test-ambient-zorder` | 测试环境特效 Z 顺序。 |
| R052 | 2026-11-09 00:29:39 | 发布 | `publish-ambient-effects-fixed` | 修复环境特效合成。 |
| R053 | 2026-11-09 00:33:25 | 测试 | `test-ambient-dedicated-layer` | 测试独立环境特效层。 |
| R054 | 2026-11-09 00:37:33 | 发布 | `publish-ambient-visible` | 修复环境特效可见性。 |
| R055 | 2026-11-09 00:41:59 | 发布 | `publish-ambient-card-clipped` | 处理卡片裁剪问题。 |
| R056 | 2026-11-09 00:44:29 | 发布 | `publish-ambient-outside-ui` | 允许特效在设置卡片外绘制。 |
| R057 | 2026-11-09 00:46:07 | 发布 | `publish-ambient-size` | 加入环境特效尺寸调整。 |
| R058 | 2026-11-09 01:01:31 | 整合 | `bundle-integration-verify` | 验证整合包内容和运行。 |
| R059 | 2026-11-09 01:02:14 | 整合 | `bundle-integration-stage` | 形成最终整合阶段包。 |

共收录 **7** 个早期发布痕迹和 **59** 个命名构建、测试或整合节点。
