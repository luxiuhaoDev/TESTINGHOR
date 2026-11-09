using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace McPetOverlay;

internal sealed class SettingsForm : Form
{
    private const double GuiOpacity = 0.98;
    private const double AnimationDurationMs = 390d;
    private static readonly Color TransparencyColor = Color.FromArgb(1, 2, 3);
    private readonly AppConfig config;
    private readonly Action changed;
    private readonly Action openHudEditor;
    private readonly List<EpsilonModuleModel> modules;
    private readonly Timer animationTimer = new Timer { Interval = 8 };
    private readonly Timer ambientEffectTimer = new Timer { Interval = 15 };
    private readonly SettingsAmbientEffectRenderer ambientRenderer = new SettingsAmbientEffectRenderer(9471);
    private readonly EpsilonPanelControl panel;
    private readonly EpsilonDropdownControl dropdown;
    private readonly EpsilonWorkbenchCoordinator workbench;
    private readonly SettingsBackdropForm backdrop;
    private ContextMenuStrip? guiModeMenu;
    private bool opening;
    private bool closing;
    private long animationStarted;
    private double animationProgress;
    private double animationFrom;
    private double animationTo;
    private double currentAnimationDurationMs = AnimationDurationMs;
    private Rectangle animationBaseBounds;
    private bool capturingGuiHotkey;
    private string? capturingModuleHotkeyId;
    private bool capturingChestModifierKey;
    private Rectangle gameBounds;
    private bool timerPeriodActive;
    private readonly NativeMethods.LowLevelMouseProc mouseHookCallback;
    private nint mouseHook;

    internal bool IsCapturingHotkey => capturingGuiHotkey || capturingModuleHotkeyId != null || capturingChestModifierKey;
    internal IReadOnlyList<EpsilonModuleModel> Modules => modules;
    private IWin32Window DialogOwner => workbench.DialogOwner ?? this;

    internal SettingsForm(AppConfig config, Action changed, Action openHudEditor)
    {
        this.config = config;
        this.changed = changed;
        this.openHudEditor = openHudEditor;
        Text = "TESTING HOR DESKTOP PET FEATURES - 单窗口";
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        KeyPreview = true;
        DoubleBuffered = true;
        BackColor = TransparencyColor;
        TransparencyKey = TransparencyColor;
        Opacity = 0;
        ClientSize = new Size(960, 540);

        modules = BuildModules();
        backdrop = new SettingsBackdropForm(config, HideAnimated);
        panel = new EpsilonPanelControl(modules, config, config.ClickGuiSidebarExpanded, Commit, HideAnimated)
        {
            Dock = DockStyle.Fill,
            BackColor = Color.Transparent
        };
        dropdown = new EpsilonDropdownControl(modules, config, Commit, HideAnimated)
        {
            Dock = DockStyle.Fill,
            BackColor = Color.Transparent
        };
        workbench = new EpsilonWorkbenchCoordinator(modules, config, Commit, HideAnimated, HandleSettingsKeyDown, HandleSettingsKeyPress);
        Controls.Add(panel);
        Controls.Add(dropdown);
        ApplyGuiMode(false);
        KeyDown += OnSettingsKeyDown;
        KeyPress += OnSettingsKeyPress;
        animationTimer.Tick += Animate;
        ambientEffectTimer.Tick += delegate
        {
            if (ambientRenderer.Advance(this.config, ClientSize))
                Invalidate();
        };
        ambientEffectTimer.Start();
        mouseHookCallback = OnLowLevelMouse;
    }

    private List<EpsilonModuleModel> BuildModules()
    {
        List<EpsilonModuleModel> modules = new List<EpsilonModuleModel>();

        EpsilonModuleModel aim = Module(ModuleHotkeyIds.AutoAim, "辅助瞄准", EpsilonCategory.Combat, () => config.AutoAimEnabled, value => config.AutoAimEnabled = value);
        Slider(aim, "瞄准范围", () => config.AutoAimRange, value => config.AutoAimRange = (float)value, 1, 64, 0.5);
        Slider(aim, "水平速度", () => config.AutoAimHorizontalSpeed, value => config.AutoAimHorizontalSpeed = (float)value, 1, 200, 1);
        Slider(aim, "垂直速度", () => config.AutoAimVerticalSpeed, value => config.AutoAimVerticalSpeed = (float)value, 1, 200, 1);
        Slider(aim, "最大视野角", () => config.AutoAimMaxAngle, value => config.AutoAimMaxAngle = (float)value, 1, 360, 1);
        Slider(aim, "平滑度", () => config.AutoAimSmoothness, value => config.AutoAimSmoothness = (float)value, 0, 100, 1);
        Slider(aim, "瞄准强度", () => config.AutoAimStrength, value => config.AutoAimStrength = (float)value, 25, 300, 1);
        Bool(aim, "真人漂移", () => config.AutoAimDriftEnabled, value => config.AutoAimDriftEnabled = value);
        Bool(aim, "垂直瞄准", () => config.AutoAimAimVertically, value => config.AutoAimAimVertically = value);
        Bool(aim, "移动加速", () => config.AutoAimStrafeIncrease, value => config.AutoAimStrafeIncrease = value);
        Bool(aim, "仅按住鼠标", () => config.AutoAimRequireMouseDown, value => config.AutoAimRequireMouseDown = value);
        Bool(aim, "最近碰撞箱点", () => config.AutoAimClosestHitboxPoint, value => config.AutoAimClosestHitboxPoint = value);
        Bool(aim, "队伍检测", () => config.AutoAimTeamCheckEnabled, value => config.AutoAimTeamCheckEnabled = value);
        Bool(aim, "背包/箱子暂停", () => config.AutoAimPauseInContainers, value => config.AutoAimPauseInContainers = value);
        modules.Add(aim);

        EpsilonModuleModel bowAim = Module(ModuleHotkeyIds.BowAim, "\u5f13\u7bad\u81ea\u7784", EpsilonCategory.Combat, () => config.BowAimEnabled, value => config.BowAimEnabled = value);
        Bool(bowAim, "\u79fb\u52a8\u9884\u6d4b", () => config.BowAimPredictionEnabled, value => config.BowAimPredictionEnabled = value);
        Slider(bowAim, "\u7784\u51c6\u8303\u56f4", () => config.BowAimRange, value => config.BowAimRange = (float)value, 4, 128, 1);
        Slider(bowAim, "\u6700\u5927\u89d2\u5ea6", () => config.BowAimMaxAngle, value => config.BowAimMaxAngle = (float)value, 1, 180, 1);
        Slider(bowAim, "\u5e73\u6ed1\u5ea6", () => config.BowAimSmoothness, value => config.BowAimSmoothness = (float)value, 0, 100, 1);
        Slider(bowAim, "\u6700\u5c0f\u84c4\u529b Tick", () => config.BowAimMinChargeTicks, value => config.BowAimMinChargeTicks = (int)value, 1, 20, 1);
        Bool(bowAim, "\u961f\u4f0d\u68c0\u6d4b", () => config.BowAimTeamCheckEnabled, value => config.BowAimTeamCheckEnabled = value);
        Bool(bowAim, "\u5bb9\u5668\u5185\u6682\u505c", () => config.BowAimPauseInContainers, value => config.BowAimPauseInContainers = value);
        Bool(bowAim, "\u9501\u5b9a\u52a8\u753b", () => config.BowAimLockAnimationEnabled, value => config.BowAimLockAnimationEnabled = value);
        Slider(bowAim, "\u52a8\u753b\u901f\u5ea6 %", () => config.BowAimLockAnimationSpeedPercent, value => config.BowAimLockAnimationSpeedPercent = (int)value, 25, 300, 5);
        Slider(bowAim, "\u52a8\u753b\u5927\u5c0f %", () => config.BowAimLockAnimationSizePercent, value => config.BowAimLockAnimationSizePercent = (int)value, 50, 200, 5);
        Slider(bowAim, "\u7ebf\u6761\u7c97\u7ec6", () => config.BowAimLockAnimationThickness, value => config.BowAimLockAnimationThickness = (float)value, 1, 8, 0.5);
        ColorSetting(bowAim, "\u52a8\u753b\u989c\u8272", () => config.BowAimLockAnimationColorArgb, value => config.BowAimLockAnimationColorArgb = value);
        modules.Add(bowAim);

        EpsilonModuleModel leftClicker = Module(ModuleHotkeyIds.LeftClicker, "左键连点", EpsilonCategory.Combat, () => config.LeftClickerEnabled, value => config.LeftClickerEnabled = value);
        Slider(leftClicker, "最低 CPS", () => config.LeftClickerMinCps, value => config.LeftClickerMinCps = (int)value, 1, 30, 1);
        Slider(leftClicker, "最高 CPS", () => config.LeftClickerMaxCps, value => config.LeftClickerMaxCps = (int)value, 1, 30, 1);
        Slider(leftClicker, "波动百分比", () => config.LeftClickerJitterPercent, value => config.LeftClickerJitterPercent = (int)value, 0, 45, 1);
        Bool(leftClicker, "需要按住", () => config.LeftClickerHoldRequired, value => config.LeftClickerHoldRequired = value);
        Bool(leftClicker, "格挡", () => config.LeftClickerAutoBlockEnabled, value => config.LeftClickerAutoBlockEnabled = value);
        Slider(leftClicker, "格挡延迟 Tick", () => config.LeftClickerAutoBlockTicks, value => config.LeftClickerAutoBlockTicks = (float)value, 1, 20, 0.5);
        modules.Add(leftClicker);

        EpsilonModuleModel knockback = Module(ModuleHotkeyIds.Knockback, "防击退", EpsilonCategory.Movement, () => config.KnockbackEnabled, value => config.KnockbackEnabled = value);
        Slider(knockback, "前进保持 ms", () => config.KnockbackForwardHoldMs, value => config.KnockbackForwardHoldMs = (int)value, 0, 300, 1);
        Slider(knockback, "后退保持 ms", () => config.KnockbackBackHoldMs, value => config.KnockbackBackHoldMs = (int)value, 20, 250, 1);
        Slider(knockback, "触发概率 %", () => config.KnockbackChancePercent, value => config.KnockbackChancePercent = (int)value, 0, 100, 1);
        Bool(knockback, "需要疾跑", () => config.KnockbackRequireSprint, value => config.KnockbackRequireSprint = value);
        modules.Add(knockback);

        EpsilonModuleModel rightClicker = Module(ModuleHotkeyIds.RightClicker, "右键连点", EpsilonCategory.Combat, () => config.RightClickerEnabled, value => config.RightClickerEnabled = value);
        Slider(rightClicker, "最低 CPS", () => config.RightClickerMinCps, value => config.RightClickerMinCps = (int)value, 1, 30, 1);
        Slider(rightClicker, "最高 CPS", () => config.RightClickerMaxCps, value => config.RightClickerMaxCps = (int)value, 1, 30, 1);
        Slider(rightClicker, "波动百分比", () => config.RightClickerJitterPercent, value => config.RightClickerJitterPercent = (int)value, 0, 45, 1);
        Bool(rightClicker, "需要按住", () => config.RightClickerHoldRequired, value => config.RightClickerHoldRequired = value);
        Bool(rightClicker, "仅方块生效", () => config.RightClickerBlocksOnly, value => config.RightClickerBlocksOnly = value);
        Slider(rightClicker, "放置冷却 Tick", () => config.RightClickerPlaceCooldownTicks, value => config.RightClickerPlaceCooldownTicks = (int)value, 1, 20, 1);
        modules.Add(rightClicker);

        modules.Add(Module(ModuleHotkeyIds.QuickPearl, "\u6309\u952e\u73cd\u73e0", EpsilonCategory.Combat, () => config.QuickPearlEnabled, value => config.QuickPearlEnabled = value));
        modules.Add(Module(ModuleHotkeyIds.QuickFireball, "\u6309\u952e\u706b\u7403", EpsilonCategory.Combat, () => config.QuickFireballEnabled, value => config.QuickFireballEnabled = value));

        EpsilonModuleModel pet = Module(ModuleHotkeyIds.Pet, "桌面宠物", EpsilonCategory.Player, () => config.PetEnabled, value => config.PetEnabled = value);
        Slider(pet, "高度", () => config.PetHeight, value => config.PetHeight = (int)value, 100, 900, 1);
        Slider(pet, "横向偏移", () => config.PetOffsetX, value => config.PetOffsetX = (int)value, -1000, 2000, 1);
        Slider(pet, "纵向偏移", () => config.PetOffsetY, value => config.PetOffsetY = (int)value, -1000, 2000, 1);
        modules.Add(pet);

        modules.Add(Module(ModuleHotkeyIds.StartupAnimation, "\u542F\u52A8\u52A8\u753B", EpsilonCategory.Player, () => config.StartupAnimationEnabled, value => config.StartupAnimationEnabled = value));

        EpsilonModuleModel chestStealer = Module(ModuleHotkeyIds.ChestStealer, "箱子小偷", EpsilonCategory.Player, () => config.ChestStealerEnabled, value => config.ChestStealerEnabled = value);
        Slider(chestStealer, "拿取速度 CPS", () => config.ChestStealerSpeedCps, value => config.ChestStealerSpeedCps = (int)value, 1, 20, 1);
        ActionSetting(chestStealer, "快速移动修饰键", () => capturingChestModifierKey ? "..." : ((Keys)config.ChestStealerModifierKey).ToString(), delegate
        {
            capturingChestModifierKey = true;
            ActivateGui();
            InvalidateGui();
        });
        ActionSetting(chestStealer, "拿取点击键", () => config.ChestStealerMouseButton == 1 ? "鼠标右键" : "鼠标左键", delegate
        {
            config.ChestStealerMouseButton = config.ChestStealerMouseButton == 0 ? 1 : 0;
            Commit();
        });
        Bool(chestStealer, "拿完关闭箱子", () => config.ChestStealerAutoClose, value => config.ChestStealerAutoClose = value);
        Bool(chestStealer, "拿完退出程序", () => config.ChestStealerExitProgramOnComplete, value => config.ChestStealerExitProgramOnComplete = value);
        ActionSetting(chestStealer, "物品黑名单", () => string.IsNullOrWhiteSpace(config.ChestStealerBlacklist) ? "无" : "编辑", delegate
        {
            string? value = PromptText("物品黑名单（逗号/分号/换行分隔）", config.ChestStealerBlacklist);
            if (value != null)
            {
                config.ChestStealerBlacklist = value;
                Commit();
            }
        });
        ActionSetting(chestStealer, "设置说明", () => "查看", delegate
        {
            MessageBox.Show(DialogOwner,
                "快速移动修饰键：自动拿取时先按住的键。原版箱子快速转移通常是 Shift，也经常与潜行键相同；若客户端修改了快速转移方式，请设为实际使用的键。\n\n" +
                "拿取点击键：配合修饰键点击箱子槽位。原版通常是鼠标左键。\n\n" +
                "拿完关闭箱子：两轮结束且箱子已空或只剩黑名单物品时，发送一次 Esc 关闭 Minecraft 箱子。\n\n" +
                "拿完退出程序：完成拿取后关闭本桌面宠物程序，不会替代“关闭箱子”开关；两项可独立或同时开启。\n\n" +
                "功能只在真实箱子界面 GuiChest 中执行，普通玩家背包不会触发。功能关闭时不会发送任何鼠标或键盘输入。",
                "箱子小偷设置说明", MessageBoxButtons.OK, MessageBoxIcon.Information);
        });
        modules.Add(chestStealer);

        EpsilonModuleModel inventoryManager = Module(ModuleHotkeyIds.InventoryManager, "背包管理", EpsilonCategory.Player, () => config.InventoryManagerEnabled, value => config.InventoryManagerEnabled = value);
        Slider(inventoryManager, "最小延迟 ms", () => config.InventoryManagerMinDelayMs, value => config.InventoryManagerMinDelayMs = (int)value, 0, 1000, 5);
        Slider(inventoryManager, "最大延迟 ms", () => config.InventoryManagerMaxDelayMs, value => config.InventoryManagerMaxDelayMs = (int)value, 0, 1000, 5);
        Bool(inventoryManager, "手动鼠标时暂停", () => config.InventoryManagerPauseOnManualInput, value => config.InventoryManagerPauseOnManualInput = value);
        Bool(inventoryManager, "自动装备最佳护甲", () => config.InventoryManagerAutoArmor, value => config.InventoryManagerAutoArmor = value);
        Bool(inventoryManager, "整理剑/战斗斧", () => config.InventoryManagerSwitchSword, value => config.InventoryManagerSwitchSword = value);
        Slider(inventoryManager, "武器槽位", () => config.InventoryManagerSwordSlot, value => config.InventoryManagerSwordSlot = (int)value, 1, 9, 1);
        Bool(inventoryManager, "整理方块", () => config.InventoryManagerSwitchBlock, value => config.InventoryManagerSwitchBlock = value);
        Slider(inventoryManager, "方块槽位", () => config.InventoryManagerBlockSlot, value => config.InventoryManagerBlockSlot = (int)value, 1, 9, 1);
        Slider(inventoryManager, "方块上限", () => config.InventoryManagerMaxBlockSize, value => config.InventoryManagerMaxBlockSize = (int)value, 64, 512, 64);
        Bool(inventoryManager, "整理镐", () => config.InventoryManagerSwitchPickaxe, value => config.InventoryManagerSwitchPickaxe = value);
        Slider(inventoryManager, "镐槽位", () => config.InventoryManagerPickaxeSlot, value => config.InventoryManagerPickaxeSlot = (int)value, 1, 9, 1);
        Bool(inventoryManager, "整理斧", () => config.InventoryManagerSwitchAxe, value => config.InventoryManagerSwitchAxe = value);
        Slider(inventoryManager, "斧槽位", () => config.InventoryManagerAxeSlot, value => config.InventoryManagerAxeSlot = (int)value, 1, 9, 1);
        Bool(inventoryManager, "整理弓", () => config.InventoryManagerSwitchBow, value => config.InventoryManagerSwitchBow = value);
        Slider(inventoryManager, "弓槽位", () => config.InventoryManagerBowSlot, value => config.InventoryManagerBowSlot = (int)value, 1, 9, 1);
        ActionSetting(inventoryManager, "弓优先级", () => config.InventoryManagerBowPriority == 1 ? "冲击优先" : "力量优先", delegate
        {
            config.InventoryManagerBowPriority = config.InventoryManagerBowPriority == 0 ? 1 : 0;
            Commit();
        });
        Slider(inventoryManager, "箭上限", () => config.InventoryManagerMaxArrowSize, value => config.InventoryManagerMaxArrowSize = (int)value, 64, 512, 64);
        Bool(inventoryManager, "整理水桶", () => config.InventoryManagerSwitchWaterBucket, value => config.InventoryManagerSwitchWaterBucket = value);
        Slider(inventoryManager, "水桶槽位", () => config.InventoryManagerWaterBucketSlot, value => config.InventoryManagerWaterBucketSlot = (int)value, 1, 9, 1);
        Slider(inventoryManager, "保留水桶", () => config.InventoryManagerKeepWaterBuckets, value => config.InventoryManagerKeepWaterBuckets = (int)value, 0, 5, 1);
        Slider(inventoryManager, "保留岩浆桶", () => config.InventoryManagerKeepLavaBuckets, value => config.InventoryManagerKeepLavaBuckets = (int)value, 0, 5, 1);
        Bool(inventoryManager, "整理末影珍珠", () => config.InventoryManagerSwitchEnderPearl, value => config.InventoryManagerSwitchEnderPearl = value);
        Slider(inventoryManager, "珍珠槽位", () => config.InventoryManagerEnderPearlSlot, value => config.InventoryManagerEnderPearlSlot = (int)value, 1, 9, 1);
        Bool(inventoryManager, "整理火焰弹", () => config.InventoryManagerSwitchFireball, value => config.InventoryManagerSwitchFireball = value);
        Slider(inventoryManager, "火焰弹槽位", () => config.InventoryManagerFireballSlot, value => config.InventoryManagerFireballSlot = (int)value, 1, 9, 1);
        Bool(inventoryManager, "整理金苹果", () => config.InventoryManagerSwitchGoldenApple, value => config.InventoryManagerSwitchGoldenApple = value);
        Slider(inventoryManager, "金苹果槽位", () => config.InventoryManagerGoldenAppleSlot, value => config.InventoryManagerGoldenAppleSlot = (int)value, 1, 9, 1);
        Bool(inventoryManager, "整理鱼竿", () => config.InventoryManagerSwitchRod, value => config.InventoryManagerSwitchRod = value);
        Slider(inventoryManager, "鱼竿槽位", () => config.InventoryManagerRodSlot, value => config.InventoryManagerRodSlot = (int)value, 1, 9, 1);
        Bool(inventoryManager, "保留投掷物", () => config.InventoryManagerKeepProjectile, value => config.InventoryManagerKeepProjectile = value);
        Bool(inventoryManager, "整理投掷物", () => config.InventoryManagerSwitchProjectile, value => config.InventoryManagerSwitchProjectile = value);
        Slider(inventoryManager, "投掷物槽位", () => config.InventoryManagerProjectileSlot, value => config.InventoryManagerProjectileSlot = (int)value, 1, 9, 1);
        Slider(inventoryManager, "投掷物上限", () => config.InventoryManagerMaxProjectileSize, value => config.InventoryManagerMaxProjectileSize = (int)value, 16, 256, 16);
        Bool(inventoryManager, "保护自定义名称", () => config.InventoryManagerProtectCustomNamed, value => config.InventoryManagerProtectCustomNamed = value);
        Bool(inventoryManager, "自动丢弃（默认关闭）", () => config.InventoryManagerThrowItems, value => config.InventoryManagerThrowItems = value);
        Bool(inventoryManager, "箱子使用背包优先级", () => config.ChestStealerUseInventoryPriority, value => config.ChestStealerUseInventoryPriority = value);
        ActionSetting(inventoryManager, "安全说明", () => "查看", delegate
        {
            MessageBox.Show(DialogOwner,
                "背包管理仅在普通玩家背包 GuiInventory 打开时运行，每次只执行一次数字键快捷栏交换，然后等待随机延迟并重新读取确认。\n\n" +
                "按住任意鼠标键时自动暂停；关闭功能后不会发送键盘或鼠标输入。自动丢弃默认关闭，开启后只处理明显垃圾和低评分装备；带自定义名称的服务器物品默认受保护。\n\n" +
                "多个物品使用同一个快捷栏槽位时，按设置顺序保留高优先级项并在日志中提示冲突。",
                "背包管理安全说明", MessageBoxButtons.OK, MessageBoxIcon.Information);
        });
        modules.Add(inventoryManager);

        modules.Add(Module(ModuleHotkeyIds.AntiAliasing, "抗锯齿", EpsilonCategory.Player, () => config.AntiAliasingEnabled, value => config.AntiAliasingEnabled = value));

        EpsilonModuleModel ambientEffect = Module(null, "背景特效", EpsilonCategory.Player, () => config.GuiAmbientEffectEnabled, value => config.GuiAmbientEffectEnabled = value);
        ActionSetting(ambientEffect, "特效类型", AmbientEffectTypeName, delegate
        {
            config.GuiAmbientEffectType = (config.GuiAmbientEffectType + 1) % 3;
            Commit();
        });
        Slider(ambientEffect, "速度 %", () => config.GuiAmbientEffectSpeedPercent, value => config.GuiAmbientEffectSpeedPercent = (int)value, 25, 200, 5);
        Slider(ambientEffect, "密度 %", () => config.GuiAmbientEffectDensityPercent, value => config.GuiAmbientEffectDensityPercent = (int)value, 10, 100, 5);
        Slider(ambientEffect, "大小 %", () => config.GuiAmbientEffectSizePercent, value => config.GuiAmbientEffectSizePercent = (int)value, 50, 250, 5);
        modules.Add(ambientEffect);

        EpsilonModuleModel edgeSneak = Module(ModuleHotkeyIds.AutoSneak, "自动潜行", EpsilonCategory.Movement, () => config.AutoSneakEnabled, value => config.AutoSneakEnabled = value);
        Slider(edgeSneak, "最低俯角", () => config.AutoSneakMinPitch, value => config.AutoSneakMinPitch = (float)value, -90, 90, 1);
        Slider(edgeSneak, "最小延迟 Tick", () => config.AutoSneakMinDelayTicks, value => config.AutoSneakMinDelayTicks = (int)value, 0, 10, 1);
        Bool(edgeSneak, "视角检查", () => config.AutoSneakPitchCheck, value => config.AutoSneakPitchCheck = value);
        Bool(edgeSneak, "仅地面生效", () => config.AutoSneakGroundOnly, value => config.AutoSneakGroundOnly = value);
        modules.Add(edgeSneak);

        EpsilonModuleModel esp = Module(ModuleHotkeyIds.Esp, "ESP", EpsilonCategory.Render, () => config.EspEnabled, value => config.EspEnabled = value);
        Bool(esp, "玩家", () => config.EspPlayers, value => config.EspPlayers = value);
        Bool(esp, "生物", () => config.EspMobs, value => config.EspMobs = value);
        Bool(esp, "方框", () => config.EspBoxes, value => config.EspBoxes = value);
        Bool(esp, "名称", () => config.EspNames, value => config.EspNames = value);
        Bool(esp, "生命值", () => config.EspHealth, value => config.EspHealth = value);
        Bool(esp, "距离", () => config.EspDistance, value => config.EspDistance = value);
        Bool(esp, "追踪线", () => config.EspTracers, value => config.EspTracers = value);
        Slider(esp, "显示范围", () => config.EspRange, value => config.EspRange = (float)value, 8, 512, 1);
        Slider(esp, "血条粗细", () => config.EspHealthBarWidth, value => config.EspHealthBarWidth = (int)value, 2, 12, 1);
        ActionSetting(esp, "血条位置", () => config.EspHealthBarOnRight ? "右侧" : "左侧", delegate
        {
            config.EspHealthBarOnRight = !config.EspHealthBarOnRight;
            Commit();
        });
        ColorSetting(esp, "ESP 颜色", () => config.EspColorArgb, value => config.EspColorArgb = value);
        ColorSetting(esp, "血条颜色", () => config.EspHealthBarColorArgb, value => config.EspHealthBarColorArgb = value);
        Bool(esp, "头顶装备", () => config.EspEquipmentEnabled, value => config.EspEquipmentEnabled = value);
        Bool(esp, "装备耐久", () => config.EspEquipmentDurability, value => config.EspEquipmentDurability = value);
        Slider(esp, "装备缩放", () => config.EspEquipmentScale, value => config.EspEquipmentScale = (float)value, 0.6, 2, 0.1);
        ColorSetting(esp, "装备槽背景", () => config.EspEquipmentBackgroundColorArgb, value => config.EspEquipmentBackgroundColorArgb = value);
        modules.Add(esp);

        EpsilonModuleModel bedEsp = Module(ModuleHotkeyIds.BedEsp, "床 ESP", EpsilonCategory.Render, () => config.BedEspEnabled, value => config.BedEspEnabled = value);
        Slider(bedEsp, "扫描范围", () => config.BedEspRange, value => config.BedEspRange = (int)value, 8, 48, 1);
        Slider(bedEsp, "填充透明度 %", () => config.BedEspOpacityPercent, value => config.BedEspOpacityPercent = (int)value, 5, 80, 1);
        Bool(bedEsp, "描边", () => config.BedEspOutline, value => config.BedEspOutline = value);
        Bool(bedEsp, "完整高度", () => config.BedEspFullHeight, value => config.BedEspFullHeight = value);
        Bool(bedEsp, "标记黑曜石", () => config.BedEspObsidian, value => config.BedEspObsidian = value);
        ColorSetting(bedEsp, "床颜色", () => config.BedEspColorArgb, value => config.BedEspColorArgb = value);
        ColorSetting(bedEsp, "黑曜石颜色", () => config.BedEspObsidianColorArgb, value => config.BedEspObsidianColorArgb = value);
        modules.Add(bedEsp);

        EpsilonModuleModel fireballPrediction = Module(ModuleHotkeyIds.FireballPrediction, "\u706b\u7403\u9884\u6d4b", EpsilonCategory.Render, () => config.FireballPredictionEnabled, value => config.FireballPredictionEnabled = value);
        Bool(fireballPrediction, "\u663e\u793a\u8f68\u8ff9", () => config.FireballPredictionTrajectoryEnabled, value => config.FireballPredictionTrajectoryEnabled = value);
        Slider(fireballPrediction, "\u9884\u6d4b\u8303\u56f4", () => config.FireballPredictionRange, value => config.FireballPredictionRange = (float)value, 8, 256, 1);
        Slider(fireballPrediction, "\u9884\u6d4b Tick", () => config.FireballPredictionTicks, value => config.FireballPredictionTicks = (int)value, 10, 120, 5);
        Slider(fireballPrediction, "\u6700\u5927\u5b9e\u4f53", () => config.FireballPredictionMaxEntities, value => config.FireballPredictionMaxEntities = (int)value, 1, 16, 1);
        Bool(fireballPrediction, "\u9ad8\u4eae\u6846", () => config.FireballPredictionBoxes, value => config.FireballPredictionBoxes = value);
        ColorSetting(fireballPrediction, "\u8f68\u8ff9\u989c\u8272", () => config.FireballPredictionColorArgb, value => config.FireballPredictionColorArgb = value);
        Slider(fireballPrediction, "\u8f68\u8ff9\u7c97\u7ec6", () => config.FireballPredictionThickness, value => config.FireballPredictionThickness = (float)value, 1, 10, 0.5);
        modules.Add(fireballPrediction);

        EpsilonModuleModel targetList = Module(ModuleHotkeyIds.TargetList, "目标列表", EpsilonCategory.Render, () => config.EspTargetListEnabled, value => config.EspTargetListEnabled = value);
        Bool(targetList, "玩家", () => config.EspTargetListPlayers, value => config.EspTargetListPlayers = value);
        Bool(targetList, "生物", () => config.EspTargetListMobs, value => config.EspTargetListMobs = value);
        Bool(targetList, "距离", () => config.EspTargetListDistance, value => config.EspTargetListDistance = value);
        Slider(targetList, "最大数量", () => config.EspTargetListMaxCount, value => config.EspTargetListMaxCount = (int)value, 1, 64, 1);
        Slider(targetList, "字体大小", () => config.EspTargetListFontSize, value => config.EspTargetListFontSize = (float)value, 6, 32, 0.5);
        ColorSetting(targetList, "文字颜色", () => config.EspTargetListColorArgb, value => config.EspTargetListColorArgb = value);
        ColorSetting(targetList, "强调颜色", () => config.EspTargetListAccentColorArgb, value => config.EspTargetListAccentColorArgb = value);
        ColorSetting(targetList, "背景颜色", () => config.EspTargetListBackgroundColorArgb, value => config.EspTargetListBackgroundColorArgb = value);
        Bool(targetList, "显示背景", () => config.EspTargetListShowBackground, value => config.EspTargetListShowBackground = value);
        Slider(targetList, "背景透明度 %", () => config.EspTargetListBackgroundOpacityPercent, value => config.EspTargetListBackgroundOpacityPercent = (int)value, 0, 100, 1);
        ActionSetting(targetList, "隐藏名称", () => string.IsNullOrWhiteSpace(config.EspHiddenNameKeywords) ? "无" : "编辑", delegate
        {
            string? value = PromptText("隐藏名称关键词", config.EspHiddenNameKeywords);
            if (value != null)
            {
                config.EspHiddenNameKeywords = value;
                Commit();
            }
        });
        modules.Add(targetList);

        EpsilonModuleModel enabledModules = Module(ModuleHotkeyIds.EnabledModules, "功能列表", EpsilonCategory.Render, () => config.EnabledModulesHudEnabled, value => config.EnabledModulesHudEnabled = value);
        ActionSetting(enabledModules, "语言", () => config.EnabledModulesHudEnglish ? "English" : "中文", delegate
        {
            config.EnabledModulesHudEnglish = !config.EnabledModulesHudEnglish;
            Commit();
        });
        Slider(enabledModules, "字体大小", () => config.EnabledModulesHudFontSize, value => config.EnabledModulesHudFontSize = (float)value, 6, 32, 0.5);
        Slider(enabledModules, "圆角", () => config.EnabledModulesHudCornerRadius, value => config.EnabledModulesHudCornerRadius = (int)value, 0, 32, 1);
        ColorSetting(enabledModules, "文字颜色", () => config.EnabledModulesHudTextColorArgb, value => config.EnabledModulesHudTextColorArgb = value);
        ColorSetting(enabledModules, "强调颜色", () => config.EnabledModulesHudAccentColorArgb, value => config.EnabledModulesHudAccentColorArgb = value);
        ColorSetting(enabledModules, "背景颜色", () => config.EnabledModulesHudBackgroundColorArgb, value => config.EnabledModulesHudBackgroundColorArgb = value);
        Bool(enabledModules, "显示背景", () => config.EnabledModulesHudShowBackground, value => config.EnabledModulesHudShowBackground = value);
        Slider(enabledModules, "背景透明度 %", () => config.EnabledModulesHudBackgroundOpacityPercent, value => config.EnabledModulesHudBackgroundOpacityPercent = (int)value, 0, 100, 1);
        modules.Add(enabledModules);

        EpsilonModuleModel targetHud = Module(ModuleHotkeyIds.TargetHud, "目标显示", EpsilonCategory.Render, () => config.TargetHudEnabled, value => config.TargetHudEnabled = value);
        ActionSetting(targetHud, "语言", () => config.TargetHudEnglish ? "English" : "中文", delegate
        {
            config.TargetHudEnglish = !config.TargetHudEnglish;
            Commit();
        });
        Bool(targetHud, "显示距离", () => config.TargetHudShowDistance, value => config.TargetHudShowDistance = value);
        Bool(targetHud, "显示装备", () => config.TargetHudShowEquipment, value => config.TargetHudShowEquipment = value);
        Bool(targetHud, "显示周围玩家", () => config.TargetHudShowNearbyPlayers, value => config.TargetHudShowNearbyPlayers = value);
        Slider(targetHud, "多人最大数量", () => config.TargetHudNearbyMaxCount, value => config.TargetHudNearbyMaxCount = (int)value, 2, 20, 1);
        Slider(targetHud, "多人显示距离", () => config.TargetHudNearbyRange, value => config.TargetHudNearbyRange = (float)value, 4, 256, 1);
        Bool(targetHud, "竖向排列", () => config.TargetHudNearbyVerticalLayout, value => config.TargetHudNearbyVerticalLayout = value);
        Bool(targetHud, "玩家出现动画", () => config.TargetHudPlayerEnterAnimation, value => config.TargetHudPlayerEnterAnimation = value);
        Bool(targetHud, "显示背景", () => config.TargetHudShowBackground, value => config.TargetHudShowBackground = value);
        Slider(targetHud, "缩放", () => config.TargetHudScale, value => config.TargetHudScale = (float)value, 0.6, 2, 0.1);
        Slider(targetHud, "圆角", () => config.TargetHudCornerRadius, value => config.TargetHudCornerRadius = (int)value, 0, 32, 1);
        Slider(targetHud, "背景透明度 %", () => config.TargetHudBackgroundOpacityPercent, value => config.TargetHudBackgroundOpacityPercent = (int)value, 0, 100, 1);
        ColorSetting(targetHud, "文字颜色", () => config.TargetHudTextColorArgb, value => config.TargetHudTextColorArgb = value);
        ColorSetting(targetHud, "强调颜色", () => config.TargetHudAccentColorArgb, value => config.TargetHudAccentColorArgb = value);
        ColorSetting(targetHud, "背景颜色", () => config.TargetHudBackgroundColorArgb, value => config.TargetHudBackgroundColorArgb = value);
        modules.Add(targetHud);

        EpsilonModuleModel hudEditor = Module(null, "HUD 编辑", EpsilonCategory.Render, () => true, delegate { });
        ActionSetting(hudEditor, "打开编辑器", () => "拖动布局", openHudEditor);
        modules.Add(hudEditor);

        EpsilonModuleModel clickGui = Module(null, "界面设置", EpsilonCategory.Player, () => true, delegate { });
        ActionSetting(clickGui, "绑定按键", () => capturingGuiHotkey ? "..." : ((Keys)config.GuiHotkey).ToString(), delegate
        {
            capturingGuiHotkey = true;
            ActivateGui();
            InvalidateGui();
        });
        Slider(clickGui, "界面缩放 %", () => config.UiScalePercent, value => config.UiScalePercent = (int)value, 60, 180, 5);
        ColorSetting(clickGui, "主题主色", () => config.UiAccentColorArgb, value => config.UiAccentColorArgb = value);
        ColorSetting(clickGui, "背景颜色", () => config.UiBackgroundColorArgb, value => config.UiBackgroundColorArgb = value);
        ColorSetting(clickGui, "面板颜色", () => config.UiPanelColorArgb, value => config.UiPanelColorArgb = value);
        ColorSetting(clickGui, "文字颜色", () => config.UiTextColorArgb, value => config.UiTextColorArgb = value);
        Bool(clickGui, "背景模糊", () => config.GuiBackgroundBlurEnabled, value => config.GuiBackgroundBlurEnabled = value);
        Slider(clickGui, "背景透明度 %", () => config.GuiBackgroundTransparencyPercent, value => config.GuiBackgroundTransparencyPercent = (int)value, 0, 100, 1);
        Bool(clickGui, "单窗口功能描边", () => config.PanelModuleOutlineEnabled, value => config.PanelModuleOutlineEnabled = value);
        ColorSetting(clickGui, "功能描边颜色", () => config.PanelModuleOutlineColorArgb, value => config.PanelModuleOutlineColorArgb = value);
        ActionSetting(clickGui, "界面模式", GuiModeName, ToggleGuiMode, ShowGuiModeMenu);
        ActionSetting(clickGui, "保存配置", () => "config.json", Commit);
        modules.Add(clickGui);

        foreach (EpsilonModuleModel module in modules)
        {
            if (module.HotkeyId == null)
                continue;
            EpsilonModuleModel hotkeyModule = module;
            module.Settings.Insert(0, new EpsilonActionSettingModel("绑定按键",
                () => capturingModuleHotkeyId == hotkeyModule.HotkeyId ? "..." : FormatHotkey(config.GetModuleHotkey(hotkeyModule.HotkeyId)),
                () => BeginModuleHotkeyCapture(hotkeyModule.HotkeyId)));
        }
        return modules;
    }

    private static EpsilonModuleModel Module(string? hotkeyId, string name, EpsilonCategory category, Func<bool> getter, Action<bool> setter)
    {
        return new EpsilonModuleModel(hotkeyId, name, category, getter, setter);
    }

    private static string FormatHotkey(int key) => key switch
    {
        0 => "无",
        NativeMethods.VkMButton => "鼠标中键",
        NativeMethods.VkXButton1 => "鼠标侧键 1",
        NativeMethods.VkXButton2 => "鼠标侧键 2",
        _ => ((Keys)key).ToString()
    };

    private string AmbientEffectTypeName()
    {
        return config.GuiAmbientEffectType switch
        {
            1 => "代码字符",
            2 => "数学符号",
            _ => "流金"
        };
    }

    private string GuiModeName()
    {
        return config.GuiMode switch
        {
            1 => "瀑布式",
            2 => "工作台模式",
            _ => "单窗口"
        };
    }

    private void BeginModuleHotkeyCapture(string moduleId)
    {
        capturingModuleHotkeyId = moduleId;
        StartMouseCaptureHook();
        Activate();
        InvalidateGui();
    }

    private void StartMouseCaptureHook()
    {
        if (mouseHook != IntPtr.Zero) return;
        mouseHook = NativeMethods.SetWindowsHookExW(NativeMethods.WhMouseLl, mouseHookCallback, NativeMethods.GetModuleHandleW(null), 0);
    }

    private void StopMouseCaptureHook()
    {
        if (mouseHook == IntPtr.Zero) return;
        NativeMethods.UnhookWindowsHookEx(mouseHook);
        mouseHook = IntPtr.Zero;
    }

    private nint OnLowLevelMouse(int code, nint wParam, nint lParam)
    {
        if (code == NativeMethods.HcAction && capturingModuleHotkeyId != null)
        {
            int key = 0;
            int message = (int)wParam;
            if (message == NativeMethods.WmMButtonDown)
            {
                key = NativeMethods.VkMButton;
            }
            else if (message == NativeMethods.WmXButtonDown)
            {
                NativeMethods.LowLevelMouseData data = Marshal.PtrToStructure<NativeMethods.LowLevelMouseData>(lParam);
                key = ((data.MouseData >> 16) & 0xFFFF) == 1 ? NativeMethods.VkXButton1 : NativeMethods.VkXButton2;
            }
            if (key != 0)
            {
                CompleteModuleHotkeyCapture(key);
                return 1;
            }
        }
        return NativeMethods.CallNextHookEx(mouseHook, code, wParam, lParam);
    }

    private void CompleteModuleHotkeyCapture(int key)
    {
        string? moduleId = capturingModuleHotkeyId;
        if (moduleId == null) return;
        capturingModuleHotkeyId = null;
        StopMouseCaptureHook();
        if (key == config.GuiHotkey)
        {
            MessageBox.Show(this, "该按键已用于打开设置界面，请选择其他按键。", "按键冲突", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        else
        {
            config.SetModuleHotkey(moduleId, key);
        }
        Commit();
        InvalidateGui();
    }

    private static void Bool(EpsilonModuleModel module, string name, Func<bool> getter, Action<bool> setter)
    {
        module.Settings.Add(new EpsilonBoolSettingModel(name, getter, setter));
    }

    private static void Slider(EpsilonModuleModel module, string name, Func<double> getter, Action<double> setter, double minimum, double maximum, double step)
    {
        module.Settings.Add(new EpsilonSliderSettingModel(name, getter, setter, minimum, maximum, step));
    }

    private static void ActionSetting(EpsilonModuleModel module, string name, Func<string> valueGetter, Action action, Action? secondaryAction = null)
    {
        module.Settings.Add(new EpsilonActionSettingModel(name, valueGetter, action, secondaryAction: secondaryAction));
    }

    private void ColorSetting(EpsilonModuleModel module, string name, Func<int> getter, Action<int> setter)
    {
        module.Settings.Add(new EpsilonActionSettingModel(name, () => "#" + (getter() & 0xFFFFFF).ToString("X6"), delegate
        {
            using ColorDialog dialog = new ColorDialog { Color = Color.FromArgb(getter()), FullOpen = true };
            if (dialog.ShowDialog(DialogOwner) == DialogResult.OK)
            {
                setter(dialog.Color.ToArgb());
                Commit();
            }
        }, () => Color.FromArgb(getter())));
    }

    private string? PromptText(string title, string initial)
    {
        using Form prompt = new Form
        {
            Text = title,
            FormBorderStyle = FormBorderStyle.FixedToolWindow,
            StartPosition = FormStartPosition.CenterParent,
            ClientSize = new Size(360, 104),
            BackColor = Color.FromArgb(20, 18, 24),
            ForeColor = Color.FromArgb(230, 224, 233),
            TopMost = true
        };
        TextBox box = new TextBox
        {
            Text = initial,
            Location = new Point(12, 14),
            Size = new Size(336, 26),
            BackColor = Color.FromArgb(43, 41, 48),
            ForeColor = Color.FromArgb(230, 224, 233),
            BorderStyle = BorderStyle.FixedSingle
        };
        Button ok = new Button
        {
            Text = "确定",
            Location = new Point(258, 57),
            Size = new Size(90, 30),
            DialogResult = DialogResult.OK,
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(74, 68, 88),
            ForeColor = Color.FromArgb(232, 222, 248)
        };
        ok.FlatAppearance.BorderSize = 0;
        prompt.Controls.Add(box);
        prompt.Controls.Add(ok);
        prompt.AcceptButton = ok;
        return prompt.ShowDialog(DialogOwner) == DialogResult.OK ? box.Text : null;
    }

    private void ActivateGui()
    {
        if (config.GuiMode == 2)
            workbench.FocusPrimary();
        else
            Activate();
    }

    internal void SetGameBounds(Rectangle bounds)
    {
        gameBounds = bounds;
        Bounds = bounds;
        backdrop.Bounds = bounds;
        workbench.SetGameBounds(bounds);
    }

    internal void ShowAnimated()
    {
        if (Visible && !closing)
        {
            ActivateGui();
            FocusActiveGui();
            return;
        }
        if (!Visible && !gameBounds.IsEmpty)
            Bounds = gameBounds;
        animationBaseBounds = !gameBounds.IsEmpty ? gameBounds : Bounds;
        backdrop.Bounds = animationBaseBounds;
        backdrop.Configure(config);
        if (!Visible)
        {
            animationProgress = 0d;
            ApplyAnimationFrame(animationProgress);
            backdrop.SetAnimationProgress(0d);
            backdrop.Show();
            Show();
            NativeMethods.SetWindowPos(backdrop.Handle, NativeMethods.HwndTopmost, 0, 0, 0, 0, NativeMethods.SwpNoMove | NativeMethods.SwpNoSize | NativeMethods.SwpNoActivate);
            NativeMethods.SetWindowPos(Handle, NativeMethods.HwndTopmost, 0, 0, 0, 0, NativeMethods.SwpNoMove | NativeMethods.SwpNoSize | NativeMethods.SwpNoActivate);
        }
        else if (!backdrop.Visible)
        {
            backdrop.Show();
        }
        if (config.GuiMode == 2)
            workbench.ShowWorkspace(animationBaseBounds);
        ActivateGui();
        FocusActiveGui();
        StartAnimation(1d);
    }

    internal void HideAnimated()
    {
        if (!Visible || closing)
            return;
        animationBaseBounds = !gameBounds.IsEmpty ? gameBounds : Bounds;
        config.ClickGuiSidebarExpanded = panel.SidebarExpanded;
        dropdown.SaveLayout();
        workbench.SaveLayout();
        Commit();
        StartAnimation(0d);
    }

    internal void HideForHudEditor()
    {
        animationTimer.Stop();
        EndTimerPeriod();
        opening = false;
        closing = false;
        animationProgress = 0d;
        config.ClickGuiSidebarExpanded = panel.SidebarExpanded;
        dropdown.SaveLayout();
        workbench.SaveLayout();
        Commit();
        workbench.HideImmediate();
        Hide();
        backdrop.Hide();
        Opacity = 0d;
    }

    private void Animate(object? sender, EventArgs e)
    {
        double t = Math.Clamp((Environment.TickCount64 - animationStarted) / currentAnimationDurationMs, 0d, 1d);
        double eased = SmootherStep(t);
        animationProgress = animationFrom + (animationTo - animationFrom) * eased;
        ApplyAnimationFrame(animationProgress);
        if (t < 1d)
            return;
        animationTimer.Stop();
        EndTimerPeriod();
        animationProgress = animationTo;
        ApplyAnimationFrame(animationProgress);
        if (animationTo <= 0d)
        {
            closing = false;
            opening = false;
            Hide();
            workbench.HideImmediate();
            backdrop.Hide();
            Location = animationBaseBounds.Location;
            Opacity = 0d;
        }
        else
        {
            Location = animationBaseBounds.Location;
            Opacity = GuiOpacity;
            backdrop.SetAnimationProgress(1d);
            opening = false;
            closing = false;
        }
    }

    private void StartAnimation(double target)
    {
        target = Math.Clamp(target, 0d, 1d);
        animationFrom = animationProgress;
        animationTo = target;
        opening = target > animationProgress;
        closing = target < animationProgress;
        double distance = Math.Abs(animationTo - animationFrom);
        if (distance < 0.001d)
        {
            animationProgress = target;
            ApplyAnimationFrame(animationProgress);
            return;
        }
        currentAnimationDurationMs = Math.Max(120d, AnimationDurationMs * distance);
        animationStarted = Environment.TickCount64;
        BeginTimerPeriod();
        animationTimer.Start();
    }

    private static double SmootherStep(double value)
    {
        value = Math.Clamp(value, 0d, 1d);
        return value * value * value * (value * (value * 6d - 15d) + 10d);
    }

    private void BeginTimerPeriod()
    {
        if (timerPeriodActive)
            return;
        NativeMethods.TimeBeginPeriod(1);
        timerPeriodActive = true;
    }

    private void EndTimerPeriod()
    {
        if (!timerPeriodActive)
            return;
        NativeMethods.TimeEndPeriod(1);
        timerPeriodActive = false;
    }

    private void ApplyAnimationFrame(double progress)
    {
        double clamped = Math.Clamp(progress, 0d, 1d);
        if (config.GuiMode == 2)
        {
            if (!gameBounds.IsEmpty)
                Bounds = new Rectangle(gameBounds.Location, new Size(1, 1));
            Opacity = 0d;
            workbench.SetAnimationProgress(clamped);
            backdrop.SetAnimationProgress(SmootherStep(clamped));
            return;
        }
        // 小幅滑动配合双端缓动，避免突然加速或硬停。
        double remaining = 1d - clamped;
        double slide = remaining * remaining * Math.Max(22d, animationBaseBounds.Height * 0.06d);
        Location = new Point(animationBaseBounds.X, animationBaseBounds.Y + (int)Math.Round(slide));
        double fade = clamped <= 0d ? 0d : 1d - Math.Pow(1d - clamped, 2.2d);
        Opacity = GuiOpacity * Math.Clamp(fade, 0d, 1d);
        backdrop.SetAnimationProgress(SmootherStep(clamped));
    }

    private void OnSettingsKeyDown(object? sender, KeyEventArgs e)
    {
        if (HandleSettingsKeyDown(e.KeyCode))
            e.SuppressKeyPress = true;
    }

    private bool HandleSettingsKeyDown(Keys keyCode)
    {
        if (capturingModuleHotkeyId != null)
        {
            int key = keyCode == Keys.Escape ? 0 : (int)keyCode;
            CompleteModuleHotkeyCapture(key);
            return true;
        }
        if (capturingChestModifierKey)
        {
            capturingChestModifierKey = false;
            if (keyCode != Keys.Escape)
            {
                config.ChestStealerModifierKey = (int)keyCode;
                Commit();
            }
            InvalidateGui();
            return true;
        }
        if (capturingGuiHotkey)
        {
            capturingGuiHotkey = false;
            if (keyCode != Keys.Escape)
            {
                config.RemoveModuleHotkeyConflicts((int)keyCode);
                config.GuiHotkey = (int)keyCode;
                Commit();
            }
            InvalidateGui();
            return true;
        }
        bool handled = config.GuiMode switch
        {
            1 => dropdown.HandleKeyDown(keyCode),
            2 => workbench.HandleKeyDown(keyCode),
            _ => panel.HandleKeyDown(keyCode)
        };
        if (handled)
            return true;
        if (keyCode == Keys.Escape)
        {
            HideAnimated();
            return true;
        }
        return false;
    }

    private void OnSettingsKeyPress(object? sender, KeyPressEventArgs e)
    {
        if (HandleSettingsKeyPress(e.KeyChar))
            e.Handled = true;
    }

    private bool HandleSettingsKeyPress(char keyChar)
    {
        return config.GuiMode switch
        {
            1 => dropdown.HandleKeyPress(keyChar),
            2 => workbench.HandleKeyPress(keyChar),
            _ => panel.HandleKeyPress(keyChar)
        };
    }

    private void ToggleGuiMode()
    {
        SetGuiMode((config.GuiMode + 1) % 3);
    }

    private void ShowGuiModeMenu()
    {
        guiModeMenu ??= CreateGuiModeMenu();
        foreach (ToolStripItem rawItem in guiModeMenu.Items)
        {
            if (rawItem is not ToolStripMenuItem item)
                continue;
            if (item.Tag is int mode)
                item.Checked = config.GuiMode == mode;
        }
        guiModeMenu.Show(Cursor.Position);
    }

    private ContextMenuStrip CreateGuiModeMenu()
    {
        ContextMenuStrip menu = new ContextMenuStrip
        {
            ShowImageMargin = false,
            ShowCheckMargin = true,
            BackColor = EpsilonTheme.PanelHighest(config),
            ForeColor = EpsilonTheme.Text(config),
            Font = new Font("Microsoft YaHei UI", 9f, FontStyle.Regular)
        };
        AddGuiModeMenuItem(menu, "单窗口", 0);
        AddGuiModeMenuItem(menu, "瀑布式", 1);
        AddGuiModeMenuItem(menu, "工作台模式", 2);
        return menu;
    }

    private void AddGuiModeMenuItem(ContextMenuStrip menu, string text, int mode)
    {
        ToolStripMenuItem item = new ToolStripMenuItem(text)
        {
            Checked = config.GuiMode == mode,
            CheckOnClick = false,
            Tag = mode,
            BackColor = EpsilonTheme.PanelHighest(config),
            ForeColor = EpsilonTheme.Text(config)
        };
        item.Click += delegate { SetGuiMode(mode); };
        menu.Items.Add(item);
    }

    private void SetGuiMode(int mode)
    {
        mode = Math.Clamp(mode, 0, 2);
        if (config.GuiMode == mode)
        {
            InvalidateGui();
            return;
        }
        config.GuiMode = mode;
        config.UseWaterfallGui = mode == 1;
        ApplyGuiMode(true);
    }

    private void ApplyGuiMode(bool save)
    {
        panel.Visible = config.GuiMode == 0;
        dropdown.Visible = config.GuiMode == 1;
        Text = "TESTING HOR DESKTOP PET FEATURES - " + GuiModeName();
        if (config.GuiMode == 2)
        {
            if (Visible)
            {
                Rectangle bounds = gameBounds.IsEmpty ? animationBaseBounds : gameBounds;
                workbench.ShowWorkspace(bounds);
                workbench.SetAnimationProgress(1d);
                Bounds = new Rectangle(bounds.Location, new Size(1, 1));
                Opacity = 0d;
            }
        }
        else
        {
            workbench.HideImmediate();
            if (!gameBounds.IsEmpty)
                Bounds = gameBounds;
            if (Visible)
                Opacity = GuiOpacity;
            Control active = config.GuiMode == 1 ? (Control)dropdown : panel;
            active.BringToFront();
        }
        FocusActiveGui();
        InvalidateGui();
        if (save)
            Commit();
    }

    private void FocusActiveGui()
    {
        if (config.GuiMode == 1)
            dropdown.Focus();
        else if (config.GuiMode == 2)
            workbench.FocusPrimary();
        else
            panel.Focus();
    }

    private void InvalidateGui()
    {
        panel?.Invalidate();
        dropdown?.Invalidate();
        workbench?.Invalidate();
    }

    private void Commit()
    {
        changed();
        config.Save();
        backdrop.Configure(config);
        Invalidate();
        InvalidateGui();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            HideAnimated();
            return;
        }
        base.OnFormClosing(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        ambientRenderer.Draw(e.Graphics, config, ClientSize);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            StopMouseCaptureHook();
            EndTimerPeriod();
            animationTimer.Dispose();
            ambientEffectTimer.Dispose();
            ambientRenderer.Dispose();
            guiModeMenu?.Dispose();
            workbench.Dispose();
            backdrop.Dispose();
        }
        base.Dispose(disposing);
    }
}
