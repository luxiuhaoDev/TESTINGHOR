# 总体架构

McPetOverlay 是一个 Windows x64、.NET 8、WinForms 外部桌面覆盖程序。它不作为 Minecraft Mod 加载，不修改游戏 JAR，也不向 Java 进程注入 DLL。程序由“窗口跟随、只读状态采集、快照模型、覆盖渲染、设置 UI、可选输入控制器”六个层次组成。

```text
Minecraft Java 窗口
  │
  ├─ Win32 窗口定位 / 客户区坐标
  │       └─ OverlayForm 跟随位置和尺寸
  │
  ├─ OpenProcess(PROCESS_VM_READ | PROCESS_QUERY_INFORMATION)
  │       └─ MinecraftMemoryReader
  │              └─ HotSpotVmStructReader
  │                     └─ JvmEspDataSource
  │                            └─ GameSnapshot / InventorySnapshot / BedSnapshot
  │
  └─ 用户允许时的 Windows 输入接口
          └─ AutoInputController / Inventory controllers

快照模型 ──> OverlayForm ──> GDI+ 离屏位图 ──> UpdateLayeredWindow ──> 透明覆盖层
设置模型 ──> 单窗口 / 瀑布式 / 工作台 ──> AppConfig JSON
```

## 目录职责

| 目录 | 职责 |
| --- | --- |
| `App/` | 程序入口、WinForms 初始化。 |
| `Configuration/` | 配置模型、热键 ID、配置迁移与持久化。 |
| `Infrastructure/Memory/` | 进程附加、只读字节读取、PE 导出解析。 |
| `Infrastructure/Jvm/` | HotSpot VMStruct、类布局、字段布局和运行时指针解析。 |
| `Infrastructure/Minecraft/` | 将 JVM 对象转换为实体、床、火球、背包等领域快照。 |
| `Infrastructure/Windows/` | Win32、DWM、分层窗口、输入 API 声明。 |
| `Models/` | 跨模块共享的数据结构。 |
| `Features/` | 瞄准、连点、边缘潜行、背包管理、箱子管理等控制器。 |
| `UI/Forms/` | 主覆盖窗口、设置窗口、HUD 编辑器、启动动画和工作台协调器。 |
| `UI/Controls/` | 单窗口与瀑布式自绘控件、统一设置模型。 |
| `UI/Theme/` | 颜色混合、主题和强调色。 |

## 启动过程

1. `Program.Main` 初始化 WinForms。
2. `AppConfig.Load` 从 `%LocalAppData%\TESTING HOR DESKTOP PET FEATURES\config.json` 加载并规范化配置。
3. 如果启用启动动画，先显示 `StartupAnimationForm`。
4. 创建 `OverlayForm`，定位 Minecraft Java 窗口。
5. 主窗口创建只读内存读取器并验证 Java 模块的 `MZ` 签名。
6. 解析 `jvm.dll` 的 HotSpot 导出表，建立类和字段布局。
7. 数据源按周期产生不可变快照，渲染循环消费快照。
8. 设置窗口、HUD 编辑器和各功能控制器共享同一份 `AppConfig`。

## 数据与线程边界

- WinForms UI 与渲染窗口主要在 UI 线程处理窗口消息。
- 高频任务使用短周期 `Timer` 或专用循环，但不把远程进程指针直接暴露给绘制代码。
- 内存层失败时返回空快照和诊断文本，不以伪造的零值覆盖上一次有效状态。
- 实体、装备、生命值、床方块和火球轨迹使用缓存与不同刷新周期，减少跨进程读取压力。
- 配置通过结构化 JSON 保存；窗口布局使用归一化位置或明确的尺寸字段。

## 关键设计原则

1. **只读采集**：源代码没有 `WriteProcessMemory`、远程线程或驱动代码。
2. **快照隔离**：渲染层只接收领域快照，不直接遍历 JVM 对象。
3. **失败可降级**：无法识别运行时布局时显示诊断并停止对应功能。
4. **统一设置模型**：三套 UI 使用同一组 `EpsilonModuleModel` 和设置对象，避免功能行为分叉。
5. **可审计**：运行日志、第三方声明、重建历史和源码都保留在仓库中。

## 主要入口

- `App/Program.cs`
- `UI/Forms/OverlayForm.cs`
- `Infrastructure/Memory/MinecraftMemoryReader.cs`
- `Infrastructure/Jvm/HotSpotVmStructReader.cs`
- `Infrastructure/Minecraft/JvmEspDataSource.cs`
- `UI/Forms/SettingsForm.cs`
