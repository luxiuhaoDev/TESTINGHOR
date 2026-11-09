# 渲染层原理

## 窗口模型

`OverlayForm` 是无边框、置顶、可穿透点击的透明窗口。它跟随 Minecraft 客户区，而不是桌面全屏固定绘制。窗口尺寸或位置改变时，程序重新计算覆盖区域。

主要 Win32 行为包括：

- `GetWindowRect`、`ClientToScreen`：获取游戏客户区；
- 扩展窗口样式：透明点击、工具窗口、分层窗口；
- `SetWindowPos`：保持覆盖层位置和置顶顺序；
- `UpdateLayeredWindow`：提交带 alpha 通道的整帧位图；
- DWM 相关 API：为设置界面提供背景/合成效果。

覆盖窗口默认不激活，因此显示 ESP 或宠物时不会抢走游戏键盘焦点。

## 每帧流程

```text
跟随游戏窗口
  -> 获取最新 GameSnapshot
  -> 创建/复用 32 位 ARGB 离屏位图
  -> Graphics 清空为透明
  -> 绘制宠物、ESP、HUD、轨迹和动画
  -> 获取 HBITMAP / 内存 DC
  -> UpdateLayeredWindow 一次提交
```

项目复用分层窗口资源，避免每帧重复创建所有 GDI 对象。窗口销毁时显式释放位图、DC、字体和计时器。

## 世界坐标投影

`JvmEspDataSource` 读取游戏正在使用的模型视图矩阵、投影矩阵与 viewport。对世界坐标执行：

1. 减去渲染原点；
2. 乘模型视图矩阵；
3. 乘投影矩阵；
4. 除以齐次坐标 `w`；
5. 映射到 viewport 像素。

实体盒使用碰撞箱八角点投影；床和黑曜石使用方块包围盒投影。点位在相机后方或矩阵无效时不绘制。

## ESP 元素

- 实体方框或角标框；
- 名称、距离、生命值条；
- 追踪线；
- 装备槽与耐久度；
- 床/黑曜石投影盒；
- 火球当前实体、历史轨迹与手持预览；
- 弓箭锁定角标；
- 目标列表、目标 HUD、附近玩家卡片；
- 已启用模块列表。

颜色、透明度、圆角、字体大小、位置和显示开关均来自 `AppConfig`。

## HUD 编辑

`HudEditorForm` 使用与正式渲染相同的布局数据。HUD 坐标以客户区比例保存：

- `X`、`Y` 表示归一化位置；
- `AnchorRight` 允许右对齐元素在窗口宽度变化后保持边距；
- 保存后无需修改绘制代码即可适配不同分辨率。

## 动画

- 启动动画：独立窗体按时间线绘制；
- 设置窗口：定时器驱动透明度和缩放；
- 模块开关：保存目标状态，逐帧平滑到目标值；
- 目标 HUD：按实体 ID 记录进入时间，实现卡片出现动画；
- 工作台：弹簧模型同时更新位置、尺寸和开关脉冲；
- 环境特效：独立背景绘制层，不参与模块逻辑。

## 抗锯齿与质量

启用抗锯齿时，GDI+ 使用 `SmoothingMode.AntiAlias`、高质量像素偏移与 ClearType 文本。关闭后切换到更低成本模式，便于低性能环境使用。

## 性能与资源管理

- 不在绘制阶段进行全量 JVM 类扫描；
- 快照刷新与渲染频率分离；
- 缓存缩放后的宠物位图和字体；
- 对床、装备和生命值使用不同缓存周期；
- 所有 `Pen`、`Brush`、`Font`、`Bitmap`、Timer 和 Win32 句柄在生命周期结束时释放。

## 相关源码

- `UI/Forms/OverlayForm.cs`
- `UI/Forms/HudEditorForm.cs`
- `UI/Forms/StartupAnimationForm.cs`
- `UI/Effects/SettingsAmbientEffectRenderer.cs`
- `Infrastructure/Windows/NativeMethods.cs`
