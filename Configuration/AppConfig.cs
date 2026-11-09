using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace McPetOverlay;

internal sealed class AppConfig
{
	public int ConfigVersion { get; set; } = 26;

	public int GuiHotkey { get; set; } = 161;

	public bool UseWaterfallGui { get; set; }

	public int GuiMode { get; set; } = -1;

	public int UiScalePercent { get; set; } = 100;

	public int UiAccentColorArgb { get; set; } = Color.FromArgb(255, 208, 188, 255).ToArgb();

	public int UiBackgroundColorArgb { get; set; } = Color.FromArgb(255, 20, 18, 24).ToArgb();

	public int UiPanelColorArgb { get; set; } = Color.FromArgb(255, 33, 31, 38).ToArgb();

	public int UiTextColorArgb { get; set; } = Color.FromArgb(255, 230, 224, 233).ToArgb();

	public bool AntiAliasingEnabled { get; set; } = true;

	public bool PanelModuleOutlineEnabled { get; set; }

	public int PanelModuleOutlineColorArgb { get; set; } = Color.FromArgb(255, 208, 188, 255).ToArgb();

	public bool GuiBackgroundBlurEnabled { get; set; }

	public int GuiBackgroundTransparencyPercent { get; set; } = 100;

	public bool GuiAmbientEffectEnabled { get; set; } = true;

	public int GuiAmbientEffectType { get; set; }

	public int GuiAmbientEffectSpeedPercent { get; set; } = 70;

	public int GuiAmbientEffectDensityPercent { get; set; } = 35;

	public int GuiAmbientEffectSizePercent { get; set; } = 100;

	public bool EspEnabled { get; set; } = true;

	public bool EspPlayers { get; set; } = true;

	public bool EspMobs { get; set; } = true;

	public bool EspBoxes { get; set; } = true;

	public bool EspNames { get; set; } = true;

	public bool EspHealth { get; set; } = true;

	public int EspHealthBarWidth { get; set; } = 5;

	public bool EspHealthBarOnRight { get; set; }

	public int EspHealthBarColorArgb { get; set; } = Color.FromArgb(255, 65, 220, 95).ToArgb();

	public bool EspDistance { get; set; } = true;

	public bool EspTracers { get; set; }

	public float EspRange { get; set; } = 128f;

	public int EspColorArgb { get; set; } = Color.FromArgb(255, 177, 122, 255).ToArgb();

	public bool BedEspEnabled { get; set; }

	[JsonPropertyName("BedEspHotkey")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
	public int LegacyBedEspHotkey { get; set; }

	public int BedEspRange { get; set; } = 32;

	public int BedEspColorArgb { get; set; } = Color.FromArgb(255, 255, 84, 124).ToArgb();

	public int BedEspOpacityPercent { get; set; } = 25;

	public bool BedEspOutline { get; set; } = true;

	public bool BedEspFullHeight { get; set; }

	public bool BedEspObsidian { get; set; } = true;

	public int BedEspObsidianColorArgb { get; set; } = Color.FromArgb(255, 170, 0, 170).ToArgb();

	public bool FireballPredictionEnabled { get; set; }

	public bool FireballPredictionTrajectoryEnabled { get; set; } = true;

	public float FireballPredictionRange { get; set; } = 96f;

	public int FireballPredictionTicks { get; set; } = 60;

	public int FireballPredictionMaxEntities { get; set; } = 8;

	public int FireballPredictionColorArgb { get; set; } = Color.FromArgb(255, 255, 146, 54).ToArgb();

	public float FireballPredictionThickness { get; set; } = 3f;

	public bool FireballPredictionBoxes { get; set; } = true;

	public bool EspTargetListEnabled { get; set; } = true;

	public bool EspTargetListPlayers { get; set; } = true;

	public bool EspTargetListMobs { get; set; } = true;

	public bool EspTargetListDistance { get; set; } = true;

	public int EspTargetListMaxCount { get; set; } = 12;

	public float EspTargetListFontSize { get; set; } = 10f;

	public int EspTargetListColorArgb { get; set; } = Color.White.ToArgb();

	public int EspTargetListBackgroundColorArgb { get; set; } = Color.FromArgb(185, 18, 18, 22).ToArgb();

	public int EspTargetListAccentColorArgb { get; set; } = Color.FromArgb(255, 177, 122, 255).ToArgb();

	public bool EspTargetListShowBackground { get; set; } = true;

	public int EspTargetListBackgroundOpacityPercent { get; set; } = 72;

	public HudElementLayout TargetListHud { get; set; } = new HudElementLayout { X = 0.82f, Y = 0.05f, AnchorRight = true };

	public string EspHiddenNameKeywords { get; set; } = string.Empty;

	public bool EspEquipmentEnabled { get; set; } = true;

	public bool EspEquipmentDurability { get; set; } = true;

	public float EspEquipmentScale { get; set; } = 1f;

	public int EspEquipmentBackgroundColorArgb { get; set; } = Color.FromArgb(180, 15, 15, 18).ToArgb();

	public bool EnabledModulesHudEnabled { get; set; } = true;

	public float EnabledModulesHudFontSize { get; set; } = 10f;

	public int EnabledModulesHudTextColorArgb { get; set; } = Color.White.ToArgb();

	public int EnabledModulesHudAccentColorArgb { get; set; } = Color.FromArgb(255, 177, 122, 255).ToArgb();

	public int EnabledModulesHudBackgroundColorArgb { get; set; } = Color.FromArgb(165, 18, 18, 22).ToArgb();

	public bool EnabledModulesHudShowBackground { get; set; } = true;

	public int EnabledModulesHudBackgroundOpacityPercent { get; set; } = 65;

	public bool EnabledModulesHudEnglish { get; set; }

	public int EnabledModulesHudCornerRadius { get; set; } = 6;

	public HudElementLayout EnabledModulesHud { get; set; } = new HudElementLayout { X = 0.985f, Y = 0.28f, AnchorRight = true };

	public bool TargetHudEnabled { get; set; } = true;

	public bool TargetHudShowDistance { get; set; } = true;

	public bool TargetHudShowEquipment { get; set; } = true;

	public bool TargetHudShowNearbyPlayers { get; set; }

	public int TargetHudNearbyMaxCount { get; set; } = 8;

	public float TargetHudNearbyRange { get; set; } = 32f;

	public bool TargetHudNearbyVerticalLayout { get; set; }

	public bool TargetHudPlayerEnterAnimation { get; set; } = true;

	public float TargetHudScale { get; set; } = 1f;

	public int TargetHudTextColorArgb { get; set; } = Color.White.ToArgb();

	public int TargetHudAccentColorArgb { get; set; } = Color.FromArgb(255, 177, 122, 255).ToArgb();

	public int TargetHudBackgroundColorArgb { get; set; } = Color.FromArgb(210, 18, 18, 22).ToArgb();

	public bool TargetHudShowBackground { get; set; } = true;

	public int TargetHudBackgroundOpacityPercent { get; set; } = 82;

	public bool TargetHudEnglish { get; set; }

	public int TargetHudCornerRadius { get; set; } = 8;

	public HudElementLayout TargetHud { get; set; } = new HudElementLayout { X = 0.5f, Y = 0.72f };

	public int WaterfallScalePercent { get; set; } = 100;

	public int WaterfallCornerRadius { get; set; } = 12;

	public bool PetEnabled { get; set; } = true;

	public bool StartupAnimationEnabled { get; set; } = true;

	public bool ChestStealerEnabled { get; set; }

	public int ChestStealerSpeedCps { get; set; } = 12;

	public string ChestStealerBlacklist { get; set; } = string.Empty;

	public int ChestStealerModifierKey { get; set; } = 160;

	public int ChestStealerMouseButton { get; set; }

	public bool ChestStealerAutoClose { get; set; }

	public bool ChestStealerExitProgramOnComplete { get; set; }

	public bool InventoryManagerEnabled { get; set; }

	public int InventoryManagerMinDelayMs { get; set; } = 95;

	public int InventoryManagerMaxDelayMs { get; set; } = 135;

	public bool InventoryManagerPauseOnManualInput { get; set; } = true;

	public bool InventoryManagerAutoArmor { get; set; }

	public bool InventoryManagerSwitchSword { get; set; } = true;
	public int InventoryManagerSwordSlot { get; set; } = 1;
	public bool InventoryManagerSwitchBlock { get; set; } = true;
	public int InventoryManagerBlockSlot { get; set; } = 2;
	public int InventoryManagerMaxBlockSize { get; set; } = 256;
	public bool InventoryManagerSwitchPickaxe { get; set; } = true;
	public int InventoryManagerPickaxeSlot { get; set; } = 3;
	public bool InventoryManagerSwitchAxe { get; set; } = true;
	public int InventoryManagerAxeSlot { get; set; } = 4;
	public bool InventoryManagerSwitchBow { get; set; } = true;
	public int InventoryManagerBowSlot { get; set; } = 5;
	public int InventoryManagerBowPriority { get; set; }
	public int InventoryManagerMaxArrowSize { get; set; } = 256;
	public bool InventoryManagerSwitchWaterBucket { get; set; } = true;
	public int InventoryManagerWaterBucketSlot { get; set; } = 6;
	public bool InventoryManagerSwitchEnderPearl { get; set; } = true;
	public int InventoryManagerEnderPearlSlot { get; set; } = 7;
	public bool InventoryManagerSwitchFireball { get; set; } = true;
	public int InventoryManagerFireballSlot { get; set; } = 8;
	public bool InventoryManagerSwitchGoldenApple { get; set; } = true;
	public int InventoryManagerGoldenAppleSlot { get; set; } = 9;
	public bool InventoryManagerSwitchRod { get; set; }
	public int InventoryManagerRodSlot { get; set; } = 9;
	public bool InventoryManagerKeepProjectile { get; set; } = true;
	public bool InventoryManagerSwitchProjectile { get; set; }
	public int InventoryManagerProjectileSlot { get; set; } = 9;
	public int InventoryManagerMaxProjectileSize { get; set; } = 64;
	public int InventoryManagerKeepWaterBuckets { get; set; } = 1;
	public int InventoryManagerKeepLavaBuckets { get; set; } = 1;
	public bool InventoryManagerThrowItems { get; set; }
	public bool InventoryManagerProtectCustomNamed { get; set; } = true;
	public bool ChestStealerUseInventoryPriority { get; set; } = true;

	public int PetOffsetX { get; set; } = 18;

	public int PetOffsetY { get; set; } = 12;

	public int PetHeight { get; set; } = 320;

	public bool LeftClickerEnabled { get; set; }

	public int LeftClickerMinCps { get; set; } = 8;

	public int LeftClickerMaxCps { get; set; } = 12;

	public int LeftClickerJitterPercent { get; set; } = 12;

	public bool LeftClickerHoldRequired { get; set; } = true;

	public bool LeftClickerAutoBlockEnabled { get; set; }

	public float LeftClickerAutoBlockTicks { get; set; } = 2f;

	public bool KnockbackEnabled { get; set; }

	public int KnockbackForwardHoldMs { get; set; } = 25;

	public int KnockbackBackHoldMs { get; set; } = 55;

	public bool KnockbackRequireSprint { get; set; } = true;

	public int KnockbackChancePercent { get; set; } = 60;

	public bool RightClickerEnabled { get; set; }

	public int RightClickerMinCps { get; set; } = 6;

	public int RightClickerMaxCps { get; set; } = 10;

	public int RightClickerJitterPercent { get; set; } = 15;

	public bool RightClickerHoldRequired { get; set; } = true;

	public bool RightClickerBlocksOnly { get; set; } = true;

	public int RightClickerPlaceCooldownTicks { get; set; } = 2;

	public bool QuickPearlEnabled { get; set; }

	public bool QuickFireballEnabled { get; set; }

	public bool AutoSneakEnabled { get; set; }

	public int AutoSneakMinDelayTicks { get; set; } = 2;

	public bool AutoSneakPitchCheck { get; set; } = true;

	public float AutoSneakMinPitch { get; set; } = 60f;

	public bool AutoSneakGroundOnly { get; set; } = true;

	public bool AutoAimEnabled { get; set; }

	public bool AutoAimDriftEnabled { get; set; }

	public float AutoAimSmoothness { get; set; } = 60f;

	public bool AutoAimClosestHitboxPoint { get; set; } = true;

	public float AutoAimStrength { get; set; } = 130f;

	public float AutoAimRange { get; set; } = 7f;

	public float AutoAimHorizontalSpeed { get; set; } = 25f;

	public float AutoAimVerticalSpeed { get; set; } = 25f;

	public float AutoAimMaxAngle { get; set; } = 180f;

	public bool AutoAimStrafeIncrease { get; set; }

	public bool AutoAimAimVertically { get; set; } = true;

	public bool AutoAimRequireMouseDown { get; set; }

	public bool AutoAimTeamCheckEnabled { get; set; } = true;

	public bool AutoAimPauseInContainers { get; set; } = true;

	public bool BowAimEnabled { get; set; }

	public bool BowAimPredictionEnabled { get; set; } = true;

	public float BowAimRange { get; set; } = 64f;

	public float BowAimMaxAngle { get; set; } = 45f;

	public float BowAimSmoothness { get; set; } = 65f;

	public int BowAimMinChargeTicks { get; set; } = 3;

	public bool BowAimTeamCheckEnabled { get; set; } = true;

	public bool BowAimPauseInContainers { get; set; } = true;

	public bool BowAimLockAnimationEnabled { get; set; } = true;

	public int BowAimLockAnimationSpeedPercent { get; set; } = 100;

	public int BowAimLockAnimationSizePercent { get; set; } = 100;

	public float BowAimLockAnimationThickness { get; set; } = 2f;

	public int BowAimLockAnimationColorArgb { get; set; } = Color.FromArgb(255, 208, 188, 255).ToArgb();

	public bool ClickGuiSidebarExpanded { get; set; } = true;

	public Dictionary<string, DropdownPanelLayoutState> DropdownPanels { get; set; } = new Dictionary<string, DropdownPanelLayoutState>();

	public Dictionary<string, WorkbenchPanelLayoutState> WorkbenchPanels { get; set; } = new Dictionary<string, WorkbenchPanelLayoutState>();

	public bool WorkbenchLogVisible { get; set; }

	public Dictionary<string, bool> DropdownModuleExpanded { get; set; } = new Dictionary<string, bool>();

	public Dictionary<string, int> ModuleHotkeys { get; set; } = new Dictionary<string, int>();

	public static string ConfigDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TESTING HOR DESKTOP PET FEATURES");

	public static string FilePath => Path.Combine(ConfigDirectory, "config.json");

	private static string LegacyFilePath => Path.Combine(AppContext.BaseDirectory, "config.json");

	public static AppConfig Load()
	{
		try
		{
			if (File.Exists(FilePath))
			{
				return Normalize(JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(FilePath)) ?? new AppConfig());
			}
			if (File.Exists(LegacyFilePath))
			{
				AppConfig migrated = Normalize(JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(LegacyFilePath)) ?? new AppConfig());
				migrated.Save();
				Console.WriteLine("[配置] 已迁移旧版本参数：" + LegacyFilePath);
				return migrated;
			}
		}
		catch (Exception ex)
		{
			Console.WriteLine("[配置] 读取失败，使用默认设置：" + ex.Message);
		}
		return Normalize(new AppConfig());
	}

	private static AppConfig Normalize(AppConfig config)
	{
		int previousVersion = config.ConfigVersion;
		if (previousVersion < 14) config.InventoryManagerAutoArmor = true;
		if (previousVersion < 15) config.BedEspEnabled = false;
		config.ModuleHotkeys ??= new Dictionary<string, int>();
		if (previousVersion < 17)
		{
			config.ModuleHotkeys.Clear();
			if (previousVersion >= 16 && config.LegacyBedEspHotkey is >= 1 and <= 255)
			{
				config.ModuleHotkeys[ModuleHotkeyIds.BedEsp] = config.LegacyBedEspHotkey;
			}
		}
		config.LegacyBedEspHotkey = 0;
		if (config.GuiMode < 0)
		{
			config.GuiMode = config.UseWaterfallGui ? 1 : 0;
		}
		config.GuiMode = Math.Clamp(config.GuiMode, 0, 2);
		config.UseWaterfallGui = config.GuiMode == 1;
		config.ConfigVersion = 26;
		config.DropdownPanels ??= new Dictionary<string, DropdownPanelLayoutState>();
		config.WorkbenchPanels ??= new Dictionary<string, WorkbenchPanelLayoutState>();
		config.DropdownModuleExpanded ??= new Dictionary<string, bool>();
		config.ModuleHotkeys = NormalizeModuleHotkeys(config.ModuleHotkeys);
		foreach (string moduleId in config.ModuleHotkeys.Where(pair => pair.Value == config.GuiHotkey).Select(pair => pair.Key).ToArray())
		{
			config.ModuleHotkeys.Remove(moduleId);
		}
		config.TargetListHud ??= new HudElementLayout { X = 0.82f, Y = 0.05f, AnchorRight = true };
		config.EnabledModulesHud ??= new HudElementLayout { X = 0.985f, Y = 0.28f, AnchorRight = true };
		config.TargetHud ??= new HudElementLayout { X = 0.5f, Y = 0.72f };
		NormalizeHud(config.TargetListHud);
		NormalizeHud(config.EnabledModulesHud);
		NormalizeHud(config.TargetHud);
		config.EspHealthBarWidth = config.EspHealthBarWidth > 12 ? 5 : Math.Clamp(config.EspHealthBarWidth, 2, 12);
		config.BedEspRange = Math.Clamp(config.BedEspRange, 8, 48);
		config.BedEspOpacityPercent = Math.Clamp(config.BedEspOpacityPercent, 5, 80);
		config.FireballPredictionRange = Math.Clamp(config.FireballPredictionRange, 8f, 256f);
		config.FireballPredictionTicks = Math.Clamp(config.FireballPredictionTicks, 10, 120);
		config.FireballPredictionMaxEntities = Math.Clamp(config.FireballPredictionMaxEntities, 1, 16);
		config.FireballPredictionThickness = Math.Clamp(config.FireballPredictionThickness, 1f, 10f);
		config.LeftClickerAutoBlockTicks = Math.Clamp(config.LeftClickerAutoBlockTicks, 1f, 20f);
		config.KnockbackForwardHoldMs = Math.Clamp(config.KnockbackForwardHoldMs, 0, 300);
		config.KnockbackBackHoldMs = Math.Clamp(config.KnockbackBackHoldMs, 20, 250);
		config.KnockbackChancePercent = Math.Clamp(config.KnockbackChancePercent, 0, 100);
		config.ChestStealerSpeedCps = Math.Clamp(config.ChestStealerSpeedCps, 1, 20);
		config.ChestStealerBlacklist ??= string.Empty;
		config.ChestStealerModifierKey = config.ChestStealerModifierKey is >= 1 and <= 255 ? config.ChestStealerModifierKey : NativeMethods.VkLShift;
		config.ChestStealerMouseButton = Math.Clamp(config.ChestStealerMouseButton, 0, 1);
		config.InventoryManagerMinDelayMs = Math.Clamp(config.InventoryManagerMinDelayMs, 0, 1000);
		config.InventoryManagerMaxDelayMs = Math.Clamp(config.InventoryManagerMaxDelayMs, config.InventoryManagerMinDelayMs, 1000);
		config.InventoryManagerSwordSlot = Math.Clamp(config.InventoryManagerSwordSlot, 1, 9);
		config.InventoryManagerBlockSlot = Math.Clamp(config.InventoryManagerBlockSlot, 1, 9);
		config.InventoryManagerPickaxeSlot = Math.Clamp(config.InventoryManagerPickaxeSlot, 1, 9);
		config.InventoryManagerAxeSlot = Math.Clamp(config.InventoryManagerAxeSlot, 1, 9);
		config.InventoryManagerBowSlot = Math.Clamp(config.InventoryManagerBowSlot, 1, 9);
		config.InventoryManagerWaterBucketSlot = Math.Clamp(config.InventoryManagerWaterBucketSlot, 1, 9);
		config.InventoryManagerEnderPearlSlot = Math.Clamp(config.InventoryManagerEnderPearlSlot, 1, 9);
		config.InventoryManagerFireballSlot = Math.Clamp(config.InventoryManagerFireballSlot, 1, 9);
		config.InventoryManagerGoldenAppleSlot = Math.Clamp(config.InventoryManagerGoldenAppleSlot, 1, 9);
		config.InventoryManagerRodSlot = Math.Clamp(config.InventoryManagerRodSlot, 1, 9);
		config.InventoryManagerProjectileSlot = Math.Clamp(config.InventoryManagerProjectileSlot, 1, 9);
		config.InventoryManagerBowPriority = Math.Clamp(config.InventoryManagerBowPriority, 0, 1);
		config.InventoryManagerMaxBlockSize = Math.Clamp(config.InventoryManagerMaxBlockSize, 64, 512);
		config.InventoryManagerMaxArrowSize = Math.Clamp(config.InventoryManagerMaxArrowSize, 64, 512);
		config.InventoryManagerMaxProjectileSize = Math.Clamp(config.InventoryManagerMaxProjectileSize, 16, 256);
		config.InventoryManagerKeepWaterBuckets = Math.Clamp(config.InventoryManagerKeepWaterBuckets, 0, 5);
		config.InventoryManagerKeepLavaBuckets = Math.Clamp(config.InventoryManagerKeepLavaBuckets, 0, 5);
		config.EspTargetListBackgroundOpacityPercent = Math.Clamp(config.EspTargetListBackgroundOpacityPercent, 0, 100);
		config.EnabledModulesHudBackgroundOpacityPercent = Math.Clamp(config.EnabledModulesHudBackgroundOpacityPercent, 0, 100);
		config.TargetHudBackgroundOpacityPercent = Math.Clamp(config.TargetHudBackgroundOpacityPercent, 0, 100);
		config.EnabledModulesHudCornerRadius = Math.Clamp(config.EnabledModulesHudCornerRadius, 0, 32);
		config.TargetHudCornerRadius = Math.Clamp(config.TargetHudCornerRadius, 0, 32);
		config.TargetHudNearbyMaxCount = Math.Clamp(config.TargetHudNearbyMaxCount, 2, 20);
		config.TargetHudNearbyRange = Math.Clamp(config.TargetHudNearbyRange, 4f, 256f);
		config.BowAimRange = Math.Clamp(config.BowAimRange, 4f, 128f);
		config.BowAimMaxAngle = Math.Clamp(config.BowAimMaxAngle, 1f, 180f);
		config.BowAimSmoothness = Math.Clamp(config.BowAimSmoothness, 0f, 100f);
		config.BowAimMinChargeTicks = Math.Clamp(config.BowAimMinChargeTicks, 1, 20);
		config.BowAimLockAnimationSpeedPercent = Math.Clamp(config.BowAimLockAnimationSpeedPercent, 25, 300);
		config.BowAimLockAnimationSizePercent = Math.Clamp(config.BowAimLockAnimationSizePercent, 50, 200);
		config.BowAimLockAnimationThickness = Math.Clamp(config.BowAimLockAnimationThickness, 1f, 8f);
		config.GuiAmbientEffectType = Math.Clamp(config.GuiAmbientEffectType, 0, 2);
		config.GuiAmbientEffectSpeedPercent = Math.Clamp(config.GuiAmbientEffectSpeedPercent, 25, 200);
		config.GuiAmbientEffectDensityPercent = Math.Clamp(config.GuiAmbientEffectDensityPercent, 10, 100);
		config.GuiAmbientEffectSizePercent = Math.Clamp(config.GuiAmbientEffectSizePercent, 50, 250);
		return config;
	}

	private static Dictionary<string, int> NormalizeModuleHotkeys(Dictionary<string, int> source)
	{
		Dictionary<string, int> normalized = new Dictionary<string, int>(StringComparer.Ordinal);
		HashSet<int> assignedKeys = new HashSet<int>();
		foreach ((string id, int key) in source)
		{
			if (string.IsNullOrWhiteSpace(id) || key is < 1 or > 255 || !assignedKeys.Add(key))
			{
				continue;
			}
			normalized[id] = key;
		}
		return normalized;
	}

	public int GetModuleHotkey(string moduleId)
	{
		return ModuleHotkeys.TryGetValue(moduleId, out int key) ? key : 0;
	}

	public void SetModuleHotkey(string moduleId, int key)
	{
		key = Math.Clamp(key, 0, 255);
		foreach (string conflictingModule in ModuleHotkeys.Where(pair => pair.Key != moduleId && pair.Value == key && key != 0).Select(pair => pair.Key).ToArray())
		{
			ModuleHotkeys.Remove(conflictingModule);
		}
		if (key == 0)
		{
			ModuleHotkeys.Remove(moduleId);
		}
		else
		{
			ModuleHotkeys[moduleId] = key;
		}
	}

	public void RemoveModuleHotkeyConflicts(int key)
	{
		foreach (string moduleId in ModuleHotkeys.Where(pair => pair.Value == key).Select(pair => pair.Key).ToArray())
		{
			ModuleHotkeys.Remove(moduleId);
		}
	}

	private static void NormalizeHud(HudElementLayout layout)
	{
		layout.X = Math.Clamp(layout.X, 0f, 1f);
		layout.Y = Math.Clamp(layout.Y, 0f, 1f);
	}

	public bool Save()
	{
		try
		{
			string? directoryName = Path.GetDirectoryName(FilePath);
			if (!string.IsNullOrWhiteSpace(directoryName))
			{
				Directory.CreateDirectory(directoryName);
			}
			string contents = JsonSerializer.Serialize(this, new JsonSerializerOptions
			{
				WriteIndented = true
			});
			string text = FilePath + ".tmp";
			File.WriteAllText(text, contents, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
			if (File.Exists(FilePath))
			{
				File.Copy(FilePath, FilePath + ".bak", overwrite: true);
			}
			File.Move(text, FilePath, overwrite: true);
			Console.WriteLine("[配置] 已保存：" + FilePath);
			return true;
		}
		catch (Exception ex)
		{
			Console.WriteLine("[配置] 保存失败：" + ex.Message);
			return false;
		}
	}
}

internal sealed class HudElementLayout
{
	public float X { get; set; }

	public float Y { get; set; }

	public bool AnchorRight { get; set; }
}

internal sealed class DropdownPanelLayoutState
{
	public float X { get; set; }

	public float Y { get; set; }

	public bool Opened { get; set; } = true;

	public bool Visible { get; set; } = true;
}

internal sealed class WorkbenchPanelLayoutState
{
	public float X { get; set; }

	public float Y { get; set; }

	public float Width { get; set; }

	public float Height { get; set; }
}
