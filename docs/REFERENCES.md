# 参考来源与实现边界

本页只列出可以由现有源码、原始第三方声明或公开仓库确认的来源。没有证据的“可能参考”不会写成事实。

## 已确认来源

### 1. Epsilon

- 项目：`NekoyaHouse/Epsilon`
- 地址：https://github.com/NekoyaHouse/Epsilon
- 许可证：GPL-3.0
- 构建方式：其上游是 Gradle 多加载器项目，按上游 README 使用 `./gradlew build`。
- 本项目参考范围：深色模块化设置界面、分类/模块/设置层级、圆角视觉、动画节奏与 Epsilon 命名线索。
- 本项目改动：使用 C# WinForms/GDI+ 重新实现；新增单窗口、瀑布式与五窗口工作台；加入磁吸、弹簧、日志工具窗、统一配置和 Windows 桌面覆盖集成。

### 2. Myau / Poorcatgirl 视觉参考

- 历史声明名称：`Myau-Poorcatgirl-Fresh`
- 原有 `THIRD_PARTY_NOTICES.md` 记录其为 GPLv3 项目。
- 本项目参考范围：ESP、装备、目标卡片等表现惯例。
- 精确仓库：当前本地材料中没有保存可核验 URL，公开搜索也未找到完全同名仓库，因此暂不伪造链接。
- 相关公开上下文：`60124808866/OpenMyau`，https://github.com/60124808866/OpenMyau ，GPL-3.0。该链接仅用于帮助读者理解 Myau 开源谱系，不表示它就是缺失的同名来源。

## 平台与技术资料

### .NET / Windows

- .NET 8、Windows Forms、System.Drawing；
- Windows 分层窗口、DWM、进程查询、模块枚举和 `ReadProcessMemory`；
- 相关 API 的最终语义以 Microsoft Learn 和 Windows SDK 为准。

### OpenJDK HotSpot

- `jvm.dll` 导出的 VMStruct/VMType 服务性符号；
- `SystemDictionary`、`ClassLoaderDataGraph`、`InstanceKlass`、`ConstantPool`、compressed oops 等 HotSpot 内部结构；
- 这些结构会随 JVM 版本变化，本项目代码中的动态解析和回退值不能替代上游源码审计。

### Minecraft Java 1.8.9

- 使用 1.8.9 混淆字段名和对象布局；
- Minecraft、Mojang 和相关标识归其权利人所有；
- 本项目不是 Mojang/Microsoft 官方产品，也不获其背书。

## 未使用或不应声称的技术

当前源码没有以下实现，因此文档和宣传不得声称拥有：

- 内核驱动；
- DLL 注入或远程线程；
- `WriteProcessMemory`；
- 网络包修改；
- 反作弊隐藏、混淆或规避检测机制；
- 对任意 Minecraft 版本的通用支持。

## 贡献者添加来源时的要求

任何新增第三方代码、纹理、字体、动画或算法都应同时更新：

1. `THIRD_PARTY_NOTICES.md`；
2. 本文件；
3. 对应源码头或资源旁的许可证文件；
4. README 中受影响的功能描述。
