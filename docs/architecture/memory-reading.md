# 只读内存读取链路

本文说明项目如何读取本机 Minecraft Java 进程状态。内容用于源码审计、兼容性维护和学习 Windows/JVM 数据布局，不包含反作弊规避、隐藏进程访问或逃避检测的方法。

## 安全边界

- 只请求 `PROCESS_QUERY_INFORMATION` 与 `PROCESS_VM_READ` 所需权限。
- 只调用 `OpenProcess`、`ReadProcessMemory`、模块枚举和普通窗口 API。
- 不调用 `WriteProcessMemory`、`VirtualAllocEx`、`CreateRemoteThread` 或内核驱动。
- 不修改游戏代码、对象字段、网络数据包或 JVM 指令。
- 输入自动化与内存读取是两条独立链路；读取成功不代表允许自动输入。

## 第一步：识别目标进程

`OverlayForm` 从可见窗口中查找 Minecraft 窗口，并验证：

1. 窗口属于 Java 进程；
2. 进程名是 `javaw` 或 `java`；
3. 进程加载了 LWJGL 相关模块；
4. 窗口客户区尺寸有效。

找到进程 ID 后，`MinecraftMemoryReader.AttachAndVerify` 打开进程，读取主模块前两个字节并检查 `MZ` 签名。验证失败会关闭句柄并返回可读诊断。

## 第二步：基础读取器

`MinecraftMemoryReader` 提供受限的类型读取方法：

- `TryReadBytes`：单次最多读取 1 MiB；
- `TryReadInt32` / `TryReadInt64` / `TryReadUInt32` / `TryReadUInt64`；
- `TryReadSingle` / `TryReadDouble`：同时拒绝 NaN 和 Infinity；
- `TryReadPointer`：读取 64 位远程地址；
- `TryReadCString`：分块读取 UTF-8 C 字符串；
- `TryGetModule`：定位 `jvm.dll` 等模块。

所有方法都使用 `Try...` 语义：失败返回 `false`，不会让无效地址继续进入绘制层。

## 第三步：解析 HotSpot 导出表

`PeExportReader` 从磁盘上的 `jvm.dll` 读取 PE 导出表。`HotSpotVmStructReader` 关注 HotSpot 提供的服务性符号：

- `gHotSpotVMStructs`
- `gHotSpotVMStructEntryArrayStride`
- `gHotSpotVMStructEntryTypeNameOffset`
- `gHotSpotVMStructEntryFieldNameOffset`
- `gHotSpotVMStructEntryTypeStringOffset`
- `gHotSpotVMStructEntryIsStaticOffset`
- `gHotSpotVMStructEntryOffsetOffset`
- `gHotSpotVMStructEntryAddressOffset`

这些符号描述 JVM 自身 C++ 结构的字段名称、偏移和静态地址。项目通过它们建立 `VmStructField` 列表，而不是把所有 JVM 偏移硬编码成单一版本。

## 第四步：枚举 JVM 类

读取器优先从 `SystemDictionary` 枚举已加载类；不适用时回退到 `ClassLoaderDataGraph`。随后只保留项目需要的 Minecraft/Java 类，例如：

- `net/minecraft/client/Minecraft`
- 世界、实体、玩家、渲染管理器与计时器；
- 背包、物品、装备、GUI 容器；
- 区块、方块存储、床和黑曜石；
- 火球、记分板、队伍、字符串和集合类型。

类名来自 HotSpot `Symbol` 对象。`InstanceKlass._fields` 与 `ConstantPool` 用于恢复 Java 字段名、签名和偏移。

## 第五步：压缩对象指针

Java 8 HotSpot 常使用 compressed oops。读取器从 `Universe`、`CompressedOops` 和 `CompressedKlassPointers` 获得：

- oop base；
- oop shift；
- klass base；
- klass shift。

窄指针解码公式为：

```text
decoded = base + (narrow << shift)
```

空窄指针始终映射为空地址。读取器先解析 `Minecraft.field_71432_P` 静态实例，再读取世界、玩家与渲染管理器对象。

## 第六步：构建领域快照

`JvmEspDataSource` 把远程对象转换为本地只读模型：

1. 读取当前 Minecraft、世界、玩家和 GUI 对象；
2. 判断是否已进入世界、是否打开容器；
3. 读取渲染原点、模型视图矩阵、投影矩阵和 viewport；
4. 枚举并分类实体；
5. 读取位置、上一帧位置、碰撞箱、名称、血量、装备和队伍信息；
6. 将世界坐标投影到屏幕坐标；
7. 输出 `GameSnapshot`，供渲染和选择逻辑消费。

床扫描、火球轨迹和背包快照走独立方法，并有独立刷新间隔。错误会写入日志和诊断字段，不会让读取异常直接终止 UI。

## 缓存策略

- 类分类结果按 klass 地址缓存；
- 玩家名、生命值和装备设置短时缓存；
- 世界实体集合按周期刷新，帧间使用插值位置；
- 床方块扫描默认以较低频率运行；
- 火球轨迹为每个实体维护有限历史点；
- 世界或本地玩家地址变化时清空相关缓存。

## 兼容性限制

- 当前字段名主要面向 Minecraft Java 1.8.9 的混淆字段布局；
- 不同 Java 版本、启动器、客户端重映射或自定义 JVM 可能改变 HotSpot 结构；
- 32 位 Java 不在当前 `win-x64` 构建目标内；
- 无法获得投影矩阵时，ESP 应停止绘制而不是猜测坐标；
- 服务器规则与反作弊政策不由本项目保证，使用前必须自行确认。

## 相关源码

- `Infrastructure/Memory/MinecraftMemoryReader.cs`
- `Infrastructure/Memory/PeExportReader.cs`
- `Infrastructure/Jvm/HotSpotVmStructReader.cs`
- `Infrastructure/Jvm/JvmClassLayout.cs`
- `Infrastructure/Minecraft/JvmEspDataSource.cs`
