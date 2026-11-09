using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using SpinWait = System.Threading.SpinWait;
using Thread = System.Threading.Thread;

namespace McPetOverlay;

internal sealed class OverlayForm : Form
{
	private const int MinGameClientWidth = 320;

	private const int MinGameClientHeight = 240;

	private readonly Timer windowTimer = new Timer
	{
		Interval = 16
	};

	private readonly Timer memoryTimer = new Timer
	{
		Interval = 1000
	};

	private readonly Timer snapshotTimer = new Timer
	{
		Interval = 16
	};

	private readonly MinecraftMemoryReader memoryReader = new MinecraftMemoryReader();

	private readonly HotSpotVmStructReader hotSpotReader;

	private IEspDataSource espDataSource = new EmptyEspDataSource();

	private readonly AppConfig config;

	private readonly SettingsForm settingsForm;

	private HudEditorForm? hudEditorForm;

	private readonly AutoInputController autoInputController;

	private readonly MyauBlockHitController myauBlockHitController;

	private readonly KnockbackController knockbackController;

	private readonly ChestStealerController chestStealerController;
	private readonly InventoryManagerController inventoryManagerController;

	private readonly QuickItemUseController quickItemUseController;

	private readonly Bitmap pet;

	private readonly NotifyIcon trayIcon;

	private Bitmap? layeredFrame;

	private nint layeredScreenDc;

	private nint layeredMemoryDc;

	private nint layeredDib;

	private nint layeredOldDib;

	private nint layeredBits;

	private Size layeredFrameSize;

	private long layeredPaintCount;

	private long layeredEnsureFailures;

	private long layeredUpdateFailures;

	private DateTime nextRenderLogUtc;

	private bool layeredUpdateLogged;

	private bool renderEntryLogged;

	private bool petAlphaLogged;

	private long lastRenderLogTicks;

	private long lastRenderLogFrames;

	private long drawTicksAccum;

	private long presentTicksAccum;

	private long timingFrameCount;

	private Bitmap? petScaled;

	private int petScaledWidth;

	private int petScaledHeight;

	private readonly Thread renderThread;

	private volatile bool renderPumpAllowed;

	private volatile bool renderThreadRunning = true;

	private DateTime nextFollowDiagUtc;

	private string? lastFollowStage;

	private bool overlayNativelyShown = true;

	private long nextTopMostTicks;

	private nint minecraft;

	private nint loggedMinecraftWindow;

	private long nextMinecraftSearchTicks;

	private uint minecraftProcessId;

	private DateTime nextLogUtc;

	private DateTime nextHotSpotRetryUtc = DateTime.MinValue;

	private bool memoryConnected;

	private DisplayMode displayMode = DisplayMode.Auto;

	private readonly Dictionary<string, bool> moduleHotkeyWasDown = new Dictionary<string, bool>(StringComparer.Ordinal);

	private Thread? autoAimThread;

	private volatile bool autoAimActive;

	private Thread? bowAimThread;

	private volatile bool bowAimActive;

	private volatile bool bowAimControlling;

	private float bowAimMouseAccumX;

	private float bowAimMouseAccumY;

	private float bowAimVelocityX;

	private float bowAimVelocityY;

	private long bowAimLastProcessedSnapshotTicks;

	private readonly object bowPlayerStateLock = new object();

	private LocalPlayerState cachedBowPlayerState = LocalPlayerState.Empty;

	private readonly object bowLockVisualStateLock = new object();

	private EntitySnapshot? bowAimLockedTarget;

	private long bowAimLockStartedAtMs;

	private long bowAimLockUpdatedAtMs;

	private long bowAimLockedEntityId;

	private long bowAimLineOfSightCheckedAtMs;

	private bool bowAimHasClearLineOfSight;

	private long bowAimLineOfSightEntityId;

	private long autoAimFrameCount;

	private bool guiHotkeyWasDown;

	private bool hotSpotInspected;

	private bool dataSourceReady;

	private bool hasSeenWorld;

	private DateTime lastWorldSeenUtc;

	private DateTime nextWorldRecoveryUtc;

	private int hotSpotRetryDelaySeconds = 2;

	private uint inspectedProcessId;

	private DateTime nextEspLogUtc;

	private DateTime nextEspDiagnosticLogUtc;

	private GameSnapshot cachedSnapshot = GameSnapshot.Empty;

	private GameSnapshot previousSnapshot = GameSnapshot.Empty;

	private BedEspSnapshot cachedBedSnapshot = BedEspSnapshot.Empty;

	private FireballPredictionSnapshot cachedFireballSnapshot = FireballPredictionSnapshot.Empty;

	private long nextFireballPredictionTicks;

	private DateTime nextBedEspLogUtc;

	private long snapshotUpdatedAtTicks;

	private long snapshotTicksAccum;

	private long snapshotCount;

	private DateTime nextSnapshotLogUtc;

	private readonly Font espTextFont = new Font("Segoe UI Semibold", 9f, FontStyle.Bold, GraphicsUnit.Point);

	private Font? targetListFont;

	private Font? targetListHeaderFont;

	private float cachedTargetFontSize = -1f;

	private string cachedHiddenKeywordsText = string.Empty;

	private string[] cachedHiddenKeywords = Array.Empty<string>();

	private volatile EntitySnapshot? autoAimTarget;

	private long autoAimTargetUpdatedAtMs;

	private EntitySnapshot? retainedHudTarget;

	private long retainedHudTargetUntilMs;

	private readonly Dictionary<long, long> targetHudCardEnteredAt = new Dictionary<long, long>();

	protected override bool ShowWithoutActivation => true;

	protected override CreateParams CreateParams
	{
		get
		{
			CreateParams createParams = base.CreateParams;
			createParams.ExStyle |= NativeMethods.WsExTransparent | NativeMethods.WsExToolWindow | NativeMethods.WsExLayered | NativeMethods.WsExNoActivate;
			return createParams;
		}
	}

	internal OverlayForm(AppConfig? loadedConfig = null)
	{
		config = loadedConfig ?? AppConfig.Load();
		config.Save();
		hotSpotReader = new HotSpotVmStructReader(memoryReader);
		settingsForm = new SettingsForm(config, SettingsChanged, ShowHudEditor);
		autoInputController = new AutoInputController(config, CanAutomateInput, () => espDataSource.GetLocalPlayerState(), (double x, double y, double z) => espDataSource.HasBlockSupport(x, y, z), () => espDataSource.IsHoldingBlockItem());
		myauBlockHitController = new MyauBlockHitController(config, CanAutomateInput, () => espDataSource.IsHoldingSwordItem());
		knockbackController = new KnockbackController(config, CanAutomateInput, () => espDataSource.GetLocalPlayerState(), () => espDataSource.HurtTime);
		chestStealerController = new ChestStealerController(config, CanAutomateInput, () => espDataSource is JvmEspDataSource source ? source.GetOpenChestSnapshot() : ChestInventorySnapshot.Closed, () => espDataSource is JvmEspDataSource source ? source.GetPlayerInventorySnapshot() : PlayerInventorySnapshot.Closed, () => minecraft != IntPtr.Zero && TryGetClientBounds(minecraft, out Rectangle bounds) ? bounds : null, () => BeginInvoke((Action)Close));
		inventoryManagerController = new InventoryManagerController(config, CanAutomateInput, () => espDataSource is JvmEspDataSource source && source.IsPlayerInventoryScreenOpen(), () => espDataSource is JvmEspDataSource source && source.IsGameplayScreenClear(), () => espDataSource is JvmEspDataSource source ? source.GetInventoryKeyVirtualCode() : 0, () => espDataSource is JvmEspDataSource source ? source.GetOpenPlayerInventorySnapshot() : PlayerInventorySnapshot.Closed, () => minecraft != IntPtr.Zero && TryGetClientBounds(minecraft, out Rectangle bounds) ? bounds : null);
		quickItemUseController = new QuickItemUseController(CanAutomateInput, () => espDataSource is JvmEspDataSource source && source.IsGameplayScreenClear(), () => espDataSource is JvmEspDataSource source ? source.GetPlayerInventorySnapshot() : PlayerInventorySnapshot.Closed);
		string text = Path.Combine(AppContext.BaseDirectory, "assets");
		Directory.CreateDirectory(text);
		string text2 = Path.Combine(text, "pet-source.jpg");
		if (!File.Exists(text2))
		{
			throw new FileNotFoundException("缺少宠物原图", text2);
		}
		pet = BackgroundRemover.LoadOrCreate(text2, Path.Combine(text, "pet.png"));
		base.FormBorderStyle = FormBorderStyle.None;
		SetStyle(ControlStyles.Opaque | ControlStyles.UserPaint, true);
		base.ShowInTaskbar = false;
		base.TopMost = true;
		DoubleBuffered = true;
		ContextMenuStrip contextMenuStrip = new ContextMenuStrip
		{
			Items = 
			{
				{
					"设置",
					(Image?)null,
					(EventHandler?)delegate
					{
						ShowSettings();
					}
				},
				{
					"自动瞄准",
					(Image?)null,
					(EventHandler?)delegate
					{
						ShowSettings();
					}
				},
				{
					"退出",
					(Image?)null,
					(EventHandler?)delegate
					{
						Close();
					}
				}
			}
		};
		trayIcon = new NotifyIcon
		{
			Text = "Minecraft 桌面宠物（外部内存读取）",
			Icon = SystemIcons.Application,
			ContextMenuStrip = contextMenuStrip,
			Visible = true
		};
		windowTimer.Tick += delegate
		{
			FollowMinecraft();
		};
		memoryTimer.Tick += delegate
		{
			VerifyMemoryRead();
		};
		snapshotTimer.Tick += delegate
		{
			RefreshSnapshot();
		};
		windowTimer.Start();
		memoryTimer.Start();
		snapshotTimer.Start();
		// 提高系统定时器分辨率，避免 WM_TIMER 被量化到 ~15.6ms 后实际降到 30~40fps。
		NativeMethods.TimeBeginPeriod(1);
		// WinForms Timer 的 WM_TIMER 会被系统合并到 ~25ms；渲染改由独立线程以 120fps 上限驱动。
		renderThread = new Thread(RenderLoop)
		{
			IsBackground = true,
			Name = "OverlayRender",
			Priority = System.Threading.ThreadPriority.AboveNormal
		};
		renderThread.Start();
	}

	private void RenderLoop()
	{
		Stopwatch clock = Stopwatch.StartNew();
		long frameInterval = Math.Max(1L, Stopwatch.Frequency / 120L);
		long nextFrameTicks = clock.ElapsedTicks;
		while (renderThreadRunning)
		{
			if (!renderPumpAllowed)
			{
				Thread.Sleep(4);
				nextFrameTicks = clock.ElapsedTicks;
				continue;
			}
			long nowTicks = clock.ElapsedTicks;
			if (nowTicks < nextFrameTicks)
			{
				Thread.Sleep(1);
				continue;
			}
			nextFrameTicks = nowTicks + frameInterval;
			try
			{
				if (IsHandleCreated && !IsDisposed)
				{
					Invoke((Action)RenderFrame);
				}
			}
			catch (InvalidOperationException)
			{
			}
		}
	}

	protected override void OnHandleCreated(EventArgs e)
	{
		base.OnHandleCreated(e);
		EnsureClickThroughStyle();
		ReassertTopMost();
	}

	private void EnsureClickThroughStyle()
	{
		int style = (int)((IntPtr)NativeMethods.GetWindowLongPtr(base.Handle, NativeMethods.GwlExStyle)).ToInt64();
		int required = NativeMethods.WsExTransparent | NativeMethods.WsExToolWindow | NativeMethods.WsExLayered | NativeMethods.WsExNoActivate;
		if ((style & required) != required)
		{
			NativeMethods.SetWindowLong(base.Handle, NativeMethods.GwlExStyle, style | required);
			NativeMethods.SetWindowPos(base.Handle, IntPtr.Zero, 0, 0, 0, 0, NativeMethods.SwpNoSize | NativeMethods.SwpNoMove | NativeMethods.SwpNoZOrder | NativeMethods.SwpFrameChanged);
		}
	}

	private void ReassertTopMost()
	{
		if (base.Handle == IntPtr.Zero || settingsForm is { Visible: true })
		{
			return;
		}
		NativeMethods.SetWindowPos(base.Handle, NativeMethods.HwndTopmost, 0, 0, 0, 0, NativeMethods.SwpNoMove | NativeMethods.SwpNoSize | NativeMethods.SwpNoActivate);
	}

	protected override void WndProc(ref Message m)
	{
		if (m.Msg == NativeMethods.WmNcHitTest)
		{
			m.Result = (IntPtr)NativeMethods.HtTransparent;
			return;
		}
		base.WndProc(ref m);
	}

	private void FollowMinecraft()
	{
		if (minecraft == IntPtr.Zero || !NativeMethods.IsWindowVisible(minecraft))
		{
			long nowTicks = Environment.TickCount64;
			if (nowTicks >= nextMinecraftSearchTicks)
			{
				nextMinecraftSearchTicks = nowTicks + 500;
				minecraft = FindMinecraftWindow();
				if (minecraft != IntPtr.Zero && minecraft != loggedMinecraftWindow)
				{
					loggedMinecraftWindow = minecraft;
					AppLog.Write($"[窗口] 已识别游戏窗口 | HWND=0x{minecraft.ToInt64():X} | Class={GetWindowClassName(minecraft)} | Title={GetWindowTitle(minecraft)}");
				}
			}
		}
		minecraftProcessId = 0u;
		if (minecraft != IntPtr.Zero)
		{
			NativeMethods.GetWindowThreadProcessId(minecraft, out minecraftProcessId);
		}
		ReadHotkeys();
		if (settingsForm.Visible)
		{
			// 设置界面打开时不再绘制全屏 Overlay：避免逐像素合成与 GUI 抢 UI 线程。
			renderPumpAllowed = false;
			if (overlayNativelyShown)
			{
				NativeMethods.ShowWindow(base.Handle, 0);
				overlayNativelyShown = false;
			}
			LogFollowDiag("设置界面打开，隐藏 Overlay");
			return;
		}
		nint foregroundWindow = NativeMethods.GetForegroundWindow();
		if (minecraft == IntPtr.Zero || NativeMethods.IsIconic(minecraft) || foregroundWindow != minecraft || !TryGetClientBounds(minecraft, out var bounds))
		{
			renderPumpAllowed = false;
			if (overlayNativelyShown)
			{
				NativeMethods.ShowWindow(base.Handle, 0);
				overlayNativelyShown = false;
			}
			LogFollowDiag("非前台/最小化/客户区无效，隐藏 Overlay");
			return;
		}
		if (base.Bounds != bounds)
		{
			base.Bounds = bounds;
			ReassertTopMost();
		}
		if (!overlayNativelyShown)
		{
			NativeMethods.ShowWindow(base.Handle, 4);
			overlayNativelyShown = true;
			ReassertTopMost();
		}
		long now = Environment.TickCount64;
		if (now >= nextTopMostTicks)
		{
			nextTopMostTicks = now + 1000L;
			ReassertTopMost();
		}
		EnsureClickThroughStyle();
		renderPumpAllowed = true;
		LogFollowDiag("跟随并渲染");
	}

	private void LogFollowDiag(string stage)
	{
		// 仅在状态变化时记录，另加 10s 心跳，避免高频日志膨胀。
		bool stageChanged = !string.Equals(lastFollowStage, stage, StringComparison.Ordinal);
		if (!stageChanged && DateTime.UtcNow < nextFollowDiagUtc)
		{
			return;
		}
		lastFollowStage = stage;
		nextFollowDiagUtc = DateTime.UtcNow.AddSeconds(10.0);
		AppLog.Write($"[跟随] {stage} | MC=0x{minecraft.ToInt64():X} | FG=0x{NativeMethods.GetForegroundWindow().ToInt64():X} | Iconic={NativeMethods.IsIconic(minecraft)} | 原生显示={overlayNativelyShown} | Bounds={base.Bounds.X},{base.Bounds.Y},{base.Bounds.Width}x{base.Bounds.Height} | Client={base.ClientSize.Width}x{base.ClientSize.Height}");
	}

	private void VerifyMemoryRead()
	{
		MemoryReadStatus memoryReadStatus = memoryReader.AttachAndVerify(minecraftProcessId);
		memoryConnected = memoryReadStatus.Connected;
		if (memoryReadStatus.Connected && memoryReadStatus.ProcessId != inspectedProcessId)
		{
			inspectedProcessId = memoryReadStatus.ProcessId;
			hotSpotInspected = false;
			dataSourceReady = false;
			hotSpotRetryDelaySeconds = 2;
			nextHotSpotRetryUtc = DateTime.MinValue;
			espDataSource = new EmptyEspDataSource();
			cachedSnapshot = GameSnapshot.Empty;
		}
		else if (!memoryReadStatus.Connected)
		{
			inspectedProcessId = 0u;
			hotSpotInspected = false;
			dataSourceReady = false;
			hotSpotRetryDelaySeconds = 2;
			nextHotSpotRetryUtc = DateTime.MinValue;
			espDataSource = new EmptyEspDataSource();
			cachedSnapshot = GameSnapshot.Empty;
			hasSeenWorld = false;
			nextWorldRecoveryUtc = DateTime.MinValue;
		}
		else if (hasSeenWorld && DateTime.UtcNow >= nextWorldRecoveryUtc && DateTime.UtcNow - lastWorldSeenUtc >= TimeSpan.FromSeconds(8))
		{
			hotSpotInspected = false;
			dataSourceReady = false;
			nextWorldRecoveryUtc = DateTime.UtcNow.AddSeconds(hotSpotRetryDelaySeconds);
			hotSpotRetryDelaySeconds = Math.Min(30, hotSpotRetryDelaySeconds * 2);
			AppLog.Write($"[JVM恢复] 快照连续 8 秒无效，重新解析 JVM（下次恢复尝试间隔 {hotSpotRetryDelaySeconds}s）");
		}
		else if (hotSpotInspected && !dataSourceReady && DateTime.UtcNow >= nextHotSpotRetryUtc)
		{
			// 类可能是在进入世界后才加载的：布局无效时逐步退避重试，而不是永久放弃。
			nextHotSpotRetryUtc = DateTime.UtcNow.AddSeconds(hotSpotRetryDelaySeconds);
			hotSpotRetryDelaySeconds = Math.Min(30, hotSpotRetryDelaySeconds * 2);
			hotSpotInspected = false;
			AppLog.Write($"[JVM校验] 重新解析内存布局（下次间隔 {hotSpotRetryDelaySeconds}s）");
		}
		else if (config.ChestStealerEnabled && espDataSource is JvmEspDataSource chestSource && chestSource.IsChestScreenOpen() && !chestSource.ChestLayoutsReady)
		{
			hotSpotInspected = false;
			dataSourceReady = false;
			nextHotSpotRetryUtc = DateTime.MinValue;
			AppLog.Write("[箱子小偷] 检测到首次加载箱子界面，正在刷新 JVM 类布局");
		}
		else if (config.InventoryManagerEnabled && espDataSource is JvmEspDataSource inventorySource && inventorySource.IsPlayerInventoryScreenOpen() && !inventorySource.InventoryLayoutsReady)
		{
			hotSpotInspected = false;
			dataSourceReady = false;
			nextHotSpotRetryUtc = DateTime.MinValue;
			AppLog.Write("[背包管理] 检测到首次加载普通背包界面，正在刷新 JVM 类布局");
		}
		if (DateTime.UtcNow < nextLogUtc)
		{
			return;
		}
		nextLogUtc = DateTime.UtcNow.AddSeconds(1.0);
		if (memoryReadStatus.Connected)
		{
			Console.WriteLine($"[内存] 已读取 Minecraft Java 进程 | PID={memoryReadStatus.ProcessId} | Base=0x{((IntPtr)memoryReadStatus.ModuleBase).ToInt64():X} | Signature={memoryReadStatus.Signature} | Path={memoryReadStatus.ProcessPath}");
			Console.WriteLine("[目标] 正在使用外部只读 JVM 对象解析；未使用截图、像素识别或游戏注入。");
			if (hotSpotInspected)
			{
				return;
			}
			hotSpotInspected = true;
			HotSpotDiagnostic diagnostic = hotSpotReader.Inspect();
			AppLog.Write($"[JVM] Inspect Success={diagnostic.Success} Fields={diagnostic.FieldCount} Error={diagnostic.Error ?? "无"}");
			if (diagnostic.Success)
			{
				if (diagnostic.RuntimePointers.Minecraft != IntPtr.Zero)
				{
					JvmEspDataSource jvmDataSource = new JvmEspDataSource(memoryReader, diagnostic, () => base.ClientSize);
					espDataSource = jvmDataSource;
					dataSourceReady = diagnostic.RuntimePointers.Minecraft != IntPtr.Zero;
					if (dataSourceReady)
					{
						hotSpotRetryDelaySeconds = 2;
					}
					AppLog.Write($"[ESP] 数据源已接入 | RuntimePointersValid={dataSourceReady}");
					Console.WriteLine("[ESP] 已接入真实 JVM 实体数据源");
				}
				Console.WriteLine($"[JVM] VMStructs 解析成功 | JVM=0x{((IntPtr)diagnostic.JvmBase).ToInt64():X} | 字段数={diagnostic.FieldCount}");
				foreach (VmStructField item in diagnostic.KeyFields.Take(240))
				{
					string value = (item.IsStatic ? $"Address=0x{((IntPtr)item.Address).ToInt64():X}" : $"Offset=0x{item.Offset:X}");
					Console.WriteLine($"[JVM字段] {item.TypeName}::{item.FieldName} | {item.TypeString} | {value}");
				}
				if (diagnostic.ClassMatches.Count == 0)
				{
					Console.WriteLine("[JVM类] 尚未在 SystemDictionary 中匹配到 Minecraft 目标类");
				}
				else
				{
					foreach (LoadedJvmClass classMatch in diagnostic.ClassMatches)
					{
						Console.WriteLine($"[JVM类] {classMatch.Name} | Klass=0x{((IntPtr)classMatch.KlassAddress).ToInt64():X}");
					}
				}
				foreach (JvmJavaField minecraftField in diagnostic.MinecraftFields)
				{
					if (minecraftField.Name.Contains("field_714", StringComparison.Ordinal) || minecraftField.Name.Contains("theMinecraft", StringComparison.OrdinalIgnoreCase) || minecraftField.Signature.Contains("WorldClient", StringComparison.Ordinal) || minecraftField.Signature.Contains("EntityPlayerSP", StringComparison.Ordinal) || minecraftField.Signature.Contains("RenderManager", StringComparison.Ordinal))
					{
						Console.WriteLine($"[Minecraft字段] {minecraftField.Name} | {minecraftField.Signature} | Static={minecraftField.IsStatic} | Offset=0x{minecraftField.Offset:X} | Packed=0x{minecraftField.PackedOffset:X8}");
					}
				}
				MinecraftRuntimePointers runtimePointers = diagnostic.RuntimePointers;
				Console.WriteLine($"[Minecraft对象] NarrowOopBase=0x{runtimePointers.NarrowOopBase:X} Shift={runtimePointers.NarrowOopShift} | Mirror=0x{((IntPtr)runtimePointers.ClassMirror).ToInt64():X} | Minecraft=0x{((IntPtr)runtimePointers.Minecraft).ToInt64():X} | World=0x{((IntPtr)runtimePointers.World).ToInt64():X} | Player=0x{((IntPtr)runtimePointers.Player).ToInt64():X} | RenderManager=0x{((IntPtr)runtimePointers.RenderManager).ToInt64():X}");
				{
					foreach (JvmClassLayout classLayout in diagnostic.ClassLayouts)
					{
						foreach (JvmJavaField field in classLayout.Fields)
						{
							bool flag = classLayout.Name == "java/util/ArrayList" || classLayout.Name == "java/nio/Buffer" || classLayout.Name == "net/minecraft/client/renderer/ActiveRenderInfo";
							bool flag2;
							if (!flag)
							{
								switch (classLayout.Name)
								{
								case "net/minecraft/client/multiplayer/WorldClient":
								case "net/minecraft/client/multiplayer/ChunkProviderClient":
								case "net/minecraft/util/LongHashMap":
								case "net/minecraft/util/LongHashMap$Entry":
								case "net/minecraft/world/chunk/Chunk":
								case "net/minecraft/world/chunk/storage/ExtendedBlockStorage":
									flag2 = true;
									break;
								default:
									flag2 = false;
									break;
								}
								flag = flag2;
							}
							bool flag3 = flag;
							if (!flag3)
							{
								switch (classLayout.Name)
								{
								case "java/lang/Integer":
								case "java/util/HashMap":
								case "net/minecraft/entity/player/EntityPlayer":
								case "net/minecraft/entity/DataWatcher":
								case "net/minecraft/entity/DataWatcher$WatchableObject":
								case "java/util/HashMap$Node":
								case "java/lang/Float":
								case "java/lang/String":
								case "com/mojang/authlib/GameProfile":
									flag2 = true;
									break;
								default:
									flag2 = false;
									break;
								}
								flag3 = flag2;
							}
							bool flag4 = flag3;
							string name;
							if (!flag4)
							{
								name = field.Name;
								if (name != null)
								{
									int length = name.Length;
									if (length != 13)
									{
										if (length == 14)
										{
											char c = name[10];
											if (c != '4')
											{
												if (c == '7' && name == "field_70737_aN")
												{
													goto IL_0b29;
												}
											}
											else if (name == "field_70734_aK")
											{
												goto IL_0b29;
											}
										}
									}
									else
									{
										switch (name[12])
										{
										case 'f':
											break;
										case 't':
											goto IL_09a8;
										case 'u':
											goto IL_09be;
										case 'v':
											goto IL_09d4;
										case 'S':
											goto IL_09ea;
										case 'T':
											goto IL_0a00;
										case 'U':
											goto IL_0a16;
										case 'N':
											goto IL_0a2c;
										case 'O':
											goto IL_0a42;
										case 'b':
											goto IL_0a58;
										case 'c':
											goto IL_0a6e;
										case 'd':
											goto IL_0a95;
										case 'l':
											goto IL_0aab;
										case 'm':
											goto IL_0abb;
										case 'n':
											goto IL_0acb;
										case 'i':
											goto IL_0adb;
										case 'j':
											goto IL_0aeb;
										case 'X':
											goto IL_0afb;
										default:
											goto IL_0b2e;
										}
										if (name == "field_72996_f")
										{
											goto IL_0b29;
										}
									}
								}
								goto IL_0b2e;
							}
							goto IL_0b35;
							IL_0adb:
							if (name == "field_78735_i")
							{
								goto IL_0b29;
							}
							goto IL_0b2e;
							IL_0aab:
							if (name == "field_78730_l")
							{
								goto IL_0b29;
							}
							goto IL_0b2e;
							IL_0a95:
							if (name == "field_78723_d")
							{
								goto IL_0b29;
							}
							goto IL_0b2e;
							IL_0abb:
							if (name == "field_78731_m")
							{
								goto IL_0b29;
							}
							goto IL_0b2e;
							IL_0acb:
							if (name == "field_78728_n")
							{
								goto IL_0b29;
							}
							goto IL_0b2e;
							IL_0b35:
							if (flag4)
							{
								Console.WriteLine($"[实体字段] {classLayout.Name}::{field.Name} | {field.Signature} | Offset=0x{field.Offset:X}");
							}
							continue;
							IL_0a58:
							if (name == "field_78725_b")
							{
								goto IL_0b29;
							}
							goto IL_0b2e;
							IL_0a6e:
							if (name == "field_78726_c" || name == "field_74281_c")
							{
								goto IL_0b29;
							}
							goto IL_0b2e;
							IL_0a2c:
							if (name == "field_70130_N")
							{
								goto IL_0b29;
							}
							goto IL_0b2e;
							IL_0a16:
							if (name == "field_70136_U")
							{
								goto IL_0b29;
							}
							goto IL_0b2e;
							IL_0a42:
							if (name == "field_70131_O")
							{
								goto IL_0b29;
							}
							goto IL_0b2e;
							IL_0a00:
							if (name == "field_70137_T")
							{
								goto IL_0b29;
							}
							goto IL_0b2e;
							IL_09ea:
							if (name == "field_70142_S")
							{
								goto IL_0b29;
							}
							goto IL_0b2e;
							IL_09d4:
							if (name == "field_70161_v")
							{
								goto IL_0b29;
							}
							goto IL_0b2e;
							IL_09a8:
							if (name == "field_70165_t")
							{
								goto IL_0b29;
							}
							goto IL_0b2e;
							IL_0b31:
							flag4 = flag2;
							goto IL_0b35;
							IL_0b2e:
							flag2 = false;
							goto IL_0b31;
							IL_0b29:
							flag2 = true;
							goto IL_0b31;
							IL_09be:
							if (name == "field_70163_u")
							{
								goto IL_0b29;
							}
							goto IL_0b2e;
							IL_0afb:
							if (name == "field_74334_X")
							{
								goto IL_0b29;
							}
							goto IL_0b2e;
							IL_0aeb:
							if (name == "field_78732_j")
							{
								goto IL_0b29;
							}
							goto IL_0b2e;
						}
					}
					return;
				}
			}
			Console.WriteLine("[JVM] VMStructs 解析失败：" + diagnostic.Error);
		}
		else
		{
			Console.WriteLine("[内存] 未连接：" + memoryReadStatus.Error);
		}
	}

	private void RefreshSnapshot()
	{
		if (settingsForm.Visible)
		{
			// 配置界面打开时冻结快照，避免高频内存读取拖慢 GUI。
			return;
		}
		nint foregroundWindow = NativeMethods.GetForegroundWindow();
		if (!memoryConnected || minecraft == IntPtr.Zero || NativeMethods.IsIconic(minecraft) || foregroundWindow != minecraft)
		{
			cachedSnapshot = GameSnapshot.Empty;
			cachedBedSnapshot = BedEspSnapshot.Empty;
			cachedFireballSnapshot = FireballPredictionSnapshot.Empty;
			nextFireballPredictionTicks = 0L;
			return;
		}
		previousSnapshot = cachedSnapshot;
		long snapshotStartTicks = Stopwatch.GetTimestamp();
		cachedSnapshot = espDataSource.GetSnapshot();
		lock (bowPlayerStateLock)
		{
			cachedBowPlayerState = config.BowAimEnabled ? espDataSource.GetLocalPlayerState() : LocalPlayerState.Empty;
		}
		if (config.BedEspEnabled)
		{
			cachedBedSnapshot = espDataSource.GetBedSnapshot(config.BedEspRange, config.BedEspObsidian, config.BedEspFullHeight);
			if (cachedBedSnapshot.Faulted)
			{
				config.BedEspEnabled = false;
				config.Save();
				AppLog.Write($"[BED-ESP] fault isolated; module disabled | {cachedBedSnapshot.Diagnostic}");
			}
		}
		else
		{
			cachedBedSnapshot = BedEspSnapshot.Empty;
		}
		long nowTicks = Stopwatch.GetTimestamp();
		if (config.FireballPredictionEnabled && nowTicks >= nextFireballPredictionTicks)
		{
			nextFireballPredictionTicks = nowTicks + Stopwatch.Frequency / 20L;
			cachedFireballSnapshot = espDataSource.GetFireballPredictionSnapshot(config.FireballPredictionRange, config.FireballPredictionTicks, config.FireballPredictionMaxEntities, config.FireballPredictionThickness, config.FireballPredictionTrajectoryEnabled);
			if (cachedFireballSnapshot.Faulted)
			{
				config.FireballPredictionEnabled = false;
				config.Save();
				AppLog.Write($"[FIREBALL] fault isolated; module disabled | {cachedFireballSnapshot.Diagnostic}");
			}
		}
		else
		{
			if (!config.FireballPredictionEnabled)
			{
				cachedFireballSnapshot = FireballPredictionSnapshot.Empty;
				nextFireballPredictionTicks = 0L;
			}
		}
		if (espDataSource is JvmEspDataSource jvmDataSource && jvmDataSource.IsSnapshotValid)
		{
			hasSeenWorld = true;
			lastWorldSeenUtc = DateTime.UtcNow;
			hotSpotRetryDelaySeconds = 2;
			nextWorldRecoveryUtc = DateTime.MinValue;
		}
		snapshotUpdatedAtTicks = Stopwatch.GetTimestamp();
		snapshotTicksAccum += snapshotUpdatedAtTicks - snapshotStartTicks;
		snapshotCount++;
		if (DateTime.UtcNow >= nextSnapshotLogUtc)
		{
			nextSnapshotLogUtc = DateTime.UtcNow.AddSeconds(5.0);
			double snapshotMs = snapshotCount > 0 ? snapshotTicksAccum * 1000.0 / Stopwatch.Frequency / snapshotCount : 0.0;
			string snapshotDiagnostic = espDataSource is JvmEspDataSource jvmSnapshotSource ? jvmSnapshotSource.LastSnapshotDiagnostic : string.Empty;
			bool snapshotValid = espDataSource is JvmEspDataSource validSource && validSource.IsSnapshotValid;
			AppLog.Write($"[快照] 平均耗时={snapshotMs:0.00}ms | 次数={snapshotCount} | 有效={snapshotValid} | {snapshotDiagnostic}");
			snapshotTicksAccum = 0L;
			snapshotCount = 0L;
		}
	}

	private void SettingsChanged()
	{
		cachedHiddenKeywordsText = string.Empty;
		if (!config.BedEspEnabled) cachedBedSnapshot = BedEspSnapshot.Empty;
		if (!config.FireballPredictionEnabled)
		{
			cachedFireballSnapshot = FireballPredictionSnapshot.Empty;
			nextFireballPredictionTicks = 0L;
		}
		SyncAutoAimState();
		SyncBowAimState();
		Invalidate();
	}

	private bool CanAutomateInput()
	{
		if (!memoryConnected || minecraft == IntPtr.Zero || NativeMethods.IsIconic(minecraft))
		{
			return false;
		}
		if (NativeMethods.GetForegroundWindow() == minecraft)
		{
			return !settingsForm.Visible;
		}
		return false;
	}

	private void ReadHotkeys()
	{
		short asyncKeyState = NativeMethods.GetAsyncKeyState(config.GuiHotkey);
		short num = (short)((config.GuiHotkey == 161) ? NativeMethods.GetAsyncKeyState(16) : 0);
		bool flag4 = !settingsForm.IsCapturingHotkey && ((asyncKeyState | num) & 0x8001) != 0;
		if (flag4 && !guiHotkeyWasDown)
		{
			Console.WriteLine($"[热键] 检测到 {(Keys)config.GuiHotkey}，设置窗口当前状态：{(settingsForm.Visible ? "已打开" : "已关闭")}");
			if (settingsForm.Visible)
			{
				settingsForm.HideAnimated();
			}
			else if (minecraft != IntPtr.Zero && NativeMethods.GetForegroundWindow() == minecraft)
			{
				ShowSettings();
			}
		}
		guiHotkeyWasDown = flag4;
		ReadModuleHotkeys();
		SyncAutoAimState();
		SyncBowAimState();
	}

	private void ReadModuleHotkeys()
	{
		bool canToggle = minecraft != IntPtr.Zero && NativeMethods.GetForegroundWindow() == minecraft && !settingsForm.IsCapturingHotkey;
		foreach (EpsilonModuleModel module in settingsForm.Modules)
		{
			if (module.HotkeyId == null)
			{
				continue;
			}
			int key = config.GetModuleHotkey(module.HotkeyId);
			bool isDown = key != 0 && (NativeMethods.GetAsyncKeyState(key) & 0x8000) != 0;
			bool wasDown = moduleHotkeyWasDown.TryGetValue(module.HotkeyId, out bool previous) && previous;
			if (canToggle && isDown && !wasDown)
			{
				if (module.HotkeyId == ModuleHotkeyIds.QuickPearl)
				{
					if (config.QuickPearlEnabled) quickItemUseController.TryUse(InventoryItemKind.EnderPearl);
					moduleHotkeyWasDown[module.HotkeyId] = isDown;
					continue;
				}
				if (module.HotkeyId == ModuleHotkeyIds.QuickFireball)
				{
					if (config.QuickFireballEnabled) quickItemUseController.TryUse(InventoryItemKind.FireCharge);
					moduleHotkeyWasDown[module.HotkeyId] = isDown;
					continue;
				}
				bool enabled = !module.EnabledGetter();
				module.EnabledSetter(enabled);
				SettingsChanged();
				config.Save();
				AppLog.Write($"[模块热键] {(Keys)key} toggled {module.HotkeyId} {(enabled ? "on" : "off")}");
			}
			moduleHotkeyWasDown[module.HotkeyId] = isDown;
		}
	}

	private void SyncBowAimState()
	{
		if (config.BowAimEnabled == bowAimActive)
		{
			return;
		}
		if (config.BowAimEnabled)
		{
			bowAimActive = true;
			bowAimThread = new Thread(BowAimLoop)
			{
				IsBackground = true,
				Name = "BowAim",
				Priority = System.Threading.ThreadPriority.AboveNormal
			};
			bowAimThread.Start();
		}
		else
		{
			bowAimActive = false;
			bowAimThread?.Join(250);
			bowAimThread = null;
			ResetBowAimState();
		}
	}


	private void SyncAutoAimState()
	{
		if (config.AutoAimEnabled == autoAimActive)
		{
			return;
		}
		if (config.AutoAimEnabled)
		{
			autoAimActive = true;
			autoAimFrameCount = 0L;
			autoAimThread = new Thread(AutoAimLoop)
			{
				IsBackground = true,
				Priority = System.Threading.ThreadPriority.Highest
			};
			autoAimThread.Start();
			Console.WriteLine($"[自动瞄准] 已开启 | 平滑度 {config.AutoAimSmoothness:0}");
		}
		else
		{
			autoAimActive = false;
			autoAimThread?.Join(250);
			autoAimThread = null;
			ResetVelocityState();
			Console.WriteLine($"[自动瞄准] 已关闭 | 线程共执行 {autoAimFrameCount} 帧");
		}
	}
	private float autoAimVelocityX;

	private float autoAimVelocityY;

	private float autoAimVelocityBufferX;

	private float autoAimVelocityBufferY;

	private bool autoAimPrevTargetOnLeft;

	private bool autoAimPrevTargetAbove;

	private int autoAimSwapCounter;

	private double autoAimLastPixelDelta;

	private int autoAimSampleCounter;

	private readonly Random autoAimRandom = new Random();

	private double autoAimDriftTimer;

	private int autoAimDriftX;

	private int autoAimDriftY;

	private int autoAimRandomOffsetX;

	private int autoAimRandomOffsetY;

	private float autoAimMouseAccumX;

	private float autoAimMouseAccumY;

	private void ResetVelocityState()
	{
		autoAimVelocityX = 0f;
		autoAimVelocityY = 0f;
		autoAimVelocityBufferX = 0f;
		autoAimVelocityBufferY = 0f;
		autoAimSwapCounter = 0;
		autoAimSampleCounter = 0;
		autoAimMouseAccumX = 0f;
		autoAimMouseAccumY = 0f;
	}

	private void UpdateDrift()
	{
		autoAimDriftTimer += 1.0;
		if (autoAimDriftTimer >= (250 + autoAimRandom.Next(50)))
		{
			autoAimDriftTimer = autoAimRandom.Next(-100, -50);
			autoAimRandomOffsetX = autoAimRandom.Next(-1, 2);
			autoAimRandomOffsetY = autoAimRandom.Next(-1, 2);
		}
		int dx = autoAimRandomOffsetX;
		int dy = autoAimRandomOffsetY;
		if (autoAimRandom.Next(10) < 2) { dx = 0; }
		if (autoAimRandom.Next(10) < 2) { dy = 0; }
		if (autoAimDriftTimer < 0.0) { dx = 0; dy = 0; }
		if (autoAimRandom.Next(20) == 1)
		{
			autoAimDriftX += dx;
			autoAimDriftY += dy;
		}
		// 偏移方向不与瞄准方向相反 — 保持自然手感
		if (autoAimMouseAccumX > 0f && autoAimDriftX < 0) autoAimDriftX = 0;
		if (autoAimMouseAccumX < 0f && autoAimDriftX > 0) autoAimDriftX = 0;
	}

	private void AutoAimLoop()
	{
		SpinWait spinWait = default(SpinWait);
		while (autoAimActive)
		{
			nint hwnd = minecraft;
			if (hwnd == IntPtr.Zero || NativeMethods.IsIconic(hwnd) || NativeMethods.GetForegroundWindow() != hwnd || settingsForm.Visible || !TryGetClientBounds(hwnd, out var bounds))
			{
				ResetVelocityState();
				spinWait.SpinOnce();
				continue;
			}
			if (bowAimControlling || (config.AutoAimRequireMouseDown && (NativeMethods.GetAsyncKeyState(1) & 0x8000) == 0))
			{
				ResetVelocityState();
				spinWait.SpinOnce();
				continue;
			}
			if (AutoAimPolicy.ShouldPause(config, espDataSource))
			{
				ResetVelocityState();
				spinWait.SpinOnce();
				continue;
			}
			GameSnapshot snapshot = cachedSnapshot;
			EntitySnapshot? nearest = null;
			float nearestDist = config.AutoAimRange;
			float maxAngle = config.AutoAimMaxAngle;
			float bestHorizontalAngle = float.MaxValue;
			float bestDistance = float.MaxValue;
			float selectedTargetX = 0f;
			float selectedTargetY = 0f;
			float screenCenterX = bounds.Width / 2f;
			float screenCenterY = bounds.Height / 2f;
			foreach (EntitySnapshot entity in snapshot.Entities)
			{
				if (entity.Kind != EntityKind.Player || !entity.Visible || !float.IsFinite(entity.Distance) || entity.Distance > config.AutoAimRange)
				{
					continue;
				}
				if (AutoAimPolicy.IsTeammate(config, snapshot, entity))
				{
					continue;
				}
				RectangleF hitbox = entity.ScreenBounds;
				float centerX = hitbox.X + hitbox.Width / 2f;
				float centerY = hitbox.Y + hitbox.Height / 2f;
				float targetX = centerX;
				float targetY = centerY;
				if (config.AutoAimClosestHitboxPoint)
				{
					float horizontalInset = Math.Clamp(hitbox.Width * 0.03f, 1f, 3f);
					float safeLeft = hitbox.Left + horizontalInset;
					float safeRight = hitbox.Right - horizontalInset;
					targetX = Math.Clamp(screenCenterX, safeLeft, safeRight);
				}
				float dx = targetX - screenCenterX;
				float dy = targetY - screenCenterY;
				float angleDeg = (float)(Math.Atan2(Math.Sqrt(dx * dx + dy * dy), bounds.Width / 2f) * 180.0 / Math.PI);
				if (angleDeg > maxAngle)
				{
					continue;
				}
				if (config.AutoAimClosestHitboxPoint)
				{
					float horizontalAngle = (float)(Math.Atan2(Math.Abs(dx), bounds.Width / 2f) * 180.0 / Math.PI);
					if (horizontalAngle > bestHorizontalAngle + 0.001f || (Math.Abs(horizontalAngle - bestHorizontalAngle) <= 0.001f && entity.Distance >= bestDistance))
					{
						continue;
					}
					bestHorizontalAngle = horizontalAngle;
					bestDistance = entity.Distance;
				}
				else if (entity.Distance >= nearestDist)
				{
					continue;
				}
				nearest = entity;
				nearestDist = entity.Distance;
				selectedTargetX = targetX;
				selectedTargetY = targetY;
			}
			if (nearest == null)
			{
				autoAimTarget = null;
				ResetVelocityState();
				spinWait.SpinOnce();
				continue;
			}
			autoAimTarget = nearest;
			autoAimTargetUpdatedAtMs = Environment.TickCount64;
			int targetAbsX = bounds.X + (int)selectedTargetX;
			int targetAbsY = bounds.Y + (int)selectedTargetY;
			ApplyVapeSmoothing(targetAbsX, targetAbsY, nearestDist, bounds);
			spinWait.SpinOnce();
		}
	}

	private void ApplyVapeSmoothing(int targetAbsX, int targetAbsY, float distance, Rectangle bounds)
	{
		System.Drawing.Point cursorPos = System.Windows.Forms.Cursor.Position;
		float deltaX = targetAbsX - cursorPos.X;
		float deltaY = targetAbsY - cursorPos.Y;
		float absDeltaX = Math.Abs(deltaX);
		float absDeltaY = Math.Abs(deltaY);

		bool targetOnLeft = deltaX < 0f;
		bool targetAbove = deltaY < 0f;

		float horizontalSpeed = Math.Clamp(config.AutoAimHorizontalSpeed, 1f, 200f);
		float verticalSpeed = Math.Clamp(config.AutoAimVerticalSpeed, 1f, 200f);
		float aimStrength = Math.Clamp(config.AutoAimStrength, 25f, 300f) / 100f;

		float horizontalForce = 1f + (float)(autoAimRandom.NextDouble() * 2.0) + absDeltaX / 50f;
		float verticalForce = 1f + (float)(autoAimRandom.NextDouble() * 2.0) + absDeltaY / 50f;

		if (Math.Abs((double)absDeltaX - autoAimLastPixelDelta) > 6.0)
		{
			horizontalForce += absDeltaX / 35f;
		}

		double proximityBoost = Math.Max(0.0, (9.0 - (double)distance) / 2.5 - 2.0);
		horizontalForce = (float)((double)horizontalForce + proximityBoost);
		verticalForce = (float)((double)verticalForce + proximityBoost);

		if (config.AutoAimStrafeIncrease)
		{
			horizontalForce *= 1.6f;
		}
		if (distance < 0.5f)
		{
			horizontalForce /= 5f;
			verticalForce /= 5f;
		}

		float horizontalAccel = horizontalForce / 25f * horizontalSpeed * aimStrength * (targetOnLeft ? (-1f) : 1f);
		float verticalAccel = verticalForce / 25f * verticalSpeed * aimStrength * (targetAbove ? (-1f) : 1f);

		// 死区：接近目标时减速
		if (absDeltaX < 5f)
		{
			horizontalAccel = 0f;
			autoAimVelocityX *= 0.7f;
		}
		if (absDeltaY < 5f)
		{
			verticalAccel = 0f;
			autoAimVelocityY *= 0.7f;
		}

		// 目标侧切换：反转速度（模拟真人重新瞄准）
		if (targetOnLeft != autoAimPrevTargetOnLeft)
		{
			autoAimVelocityX = -autoAimVelocityX;
			autoAimVelocityBufferX = -autoAimVelocityBufferX;
			autoAimMouseAccumX = 0f;
		}
		if (targetAbove != autoAimPrevTargetAbove)
		{
			autoAimVelocityY = -autoAimVelocityY;
			autoAimVelocityBufferY = -autoAimVelocityBufferY;
			autoAimMouseAccumY = 0f;
		}

		if (!config.AutoAimAimVertically)
		{
			verticalAccel = 0f;
			autoAimVelocityY = 0f;
		}

		autoAimVelocityBufferX += horizontalAccel;
		autoAimVelocityBufferY += verticalAccel;
		autoAimVelocityX += horizontalAccel;
		autoAimVelocityY += verticalAccel;

		float smoothedVX = autoAimVelocityX;
		float smoothedVY = autoAimVelocityBufferY;

		float horizontalVelocityLimit = 100f + horizontalSpeed * 4f * aimStrength;
		float verticalVelocityLimit = 100f + verticalSpeed * 4f * aimStrength;
		if (Math.Abs(smoothedVX) > horizontalVelocityLimit || Math.Abs(smoothedVY) > verticalVelocityLimit)
		{
			autoAimVelocityX = 0f;
			autoAimVelocityY = 0f;
			autoAimVelocityBufferX = 0f;
			autoAimVelocityBufferY = 0f;
			return;
		}

		float smoothingFactor = 0.30f - Math.Clamp(config.AutoAimSmoothness, 0f, 100f) * 0.0025f;
		float horizontalAdj = smoothedVX * smoothingFactor;
		if (absDeltaX <= 9f)
		{
			float t = absDeltaX + 1f;
			if (t > 0.1f) horizontalAdj /= t;
		}
		float verticalAdj = smoothedVY * smoothingFactor;
		if (absDeltaY <= 9f)
		{
			float t = absDeltaY + 1f;
			if (t > 0.1f) verticalAdj /= t;
		}

		if (float.IsNaN(horizontalAdj) || float.IsNaN(verticalAdj))
		{
			ResetVelocityState();
			return;
		}

		if (config.AutoAimDriftEnabled)
		{
			UpdateDrift();
		}
		else
		{
			autoAimDriftX = 0;
			autoAimDriftY = 0;
		}

		autoAimMouseAccumX += horizontalAdj + autoAimDriftX;
		autoAimMouseAccumY += (config.AutoAimAimVertically ? verticalAdj : 0f) + autoAimDriftY;
		autoAimDriftX = 0;
		autoAimDriftY = 0;

		int mouseStepsX = (int)autoAimMouseAccumX;
		int mouseStepsY = (int)autoAimMouseAccumY;
		autoAimMouseAccumX -= mouseStepsX;
		autoAimMouseAccumY -= mouseStepsY;

		if (mouseStepsX != 0 || mouseStepsY != 0)
		{
			NativeMethods.Input input = default;
			input.Type = 0u;
			input.Data.Mouse.Dx = mouseStepsX;
			input.Data.Mouse.Dy = mouseStepsY;
			input.Data.Mouse.Flags = 0x0001u;
			NativeMethods.SendInput(1u, new[] { input }, System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.Input>());
			autoAimFrameCount++;
		}

		autoAimPrevTargetOnLeft = targetOnLeft;
		autoAimPrevTargetAbove = targetAbove;

		autoAimSampleCounter++;
		if (autoAimSampleCounter > 10)
		{
			autoAimLastPixelDelta = absDeltaX;
			autoAimSampleCounter = 0;
		}

		autoAimSwapCounter++;
		if (autoAimSwapCounter > 10)
		{
			autoAimVelocityX = autoAimVelocityBufferX;
			autoAimVelocityY = autoAimVelocityBufferY;
			autoAimVelocityBufferX = 0f;
			autoAimVelocityBufferY = 0f;
			autoAimSwapCounter = 0;
		}
	}

	private void ShowSettings()
	{
		if (settingsForm.Visible)
		{
			settingsForm.Activate();
			return;
		}
		if (minecraft != IntPtr.Zero && TryGetClientBounds(minecraft, out var bounds))
		{
			settingsForm.StartPosition = FormStartPosition.Manual;
			settingsForm.SetGameBounds(bounds);
		}
		settingsForm.ShowAnimated();
	}

	private void BowAimLoop()
	{
		while (bowAimActive)
		{
			Thread.Sleep(5);
			nint hwnd = minecraft;
			if (hwnd == IntPtr.Zero || NativeMethods.IsIconic(hwnd) || NativeMethods.GetForegroundWindow() != hwnd || settingsForm.Visible)
			{
				ResetBowAimState();
				continue;
			}
			LocalPlayerState player;
			lock (bowPlayerStateLock)
			{
				player = cachedBowPlayerState;
			}
			if (!player.Valid || !player.HoldingBow || player.BowUseTicks < config.BowAimMinChargeTicks || (config.BowAimPauseInContainers && espDataSource.IsContainerOpen))
			{
				ResetBowAimState();
				continue;
			}
			GameSnapshot snapshot = cachedSnapshot;
			long currentSnapshotTicks = System.Threading.Volatile.Read(ref snapshotUpdatedAtTicks);
			if (currentSnapshotTicks == 0L || currentSnapshotTicks == bowAimLastProcessedSnapshotTicks)
			{
				continue;
			}
			bowAimLastProcessedSnapshotTicks = currentSnapshotTicks;
			if (snapshot.HorizontalProjectionScale <= 0f || snapshot.VerticalProjectionScale <= 0f)
			{
				ResetBowAimState();
				continue;
			}
			EntitySnapshot? selected = null;
			BowAimSolution selectedSolution = default;
			if (bowAimLockedEntityId != 0L)
			{
				selected = snapshot.Entities.FirstOrDefault(entity => entity.EntityId == bowAimLockedEntityId && entity.Kind == EntityKind.Player);
				if (selected == null)
				{
					PauseBowAimOutput(clearVisual: true);
					continue;
				}
			}
			else
			{
				foreach (EntitySnapshot entity in snapshot.Entities
					.Where(entity => entity.Kind == EntityKind.Player && entity.Visible && entity.Distance <= config.BowAimRange && entity.Height > 0f)
					.OrderBy(entity => entity.Distance)
					.ThenBy(entity => DistanceToRect(ClientSize.Width / 2f, ClientSize.Height / 2f, entity.ScreenBounds)))
				{
					if (config.BowAimTeamCheckEnabled && snapshot.LocalPlayerNameColorCode >= 0 && entity.NameColorCode >= 0 && snapshot.LocalPlayerNameColorCode == entity.NameColorCode) continue;
					if (!BowBallistics.TrySolve(player, entity, player.BowUseTicks, config.BowAimPredictionEnabled, out BowAimSolution solution)) continue;
					float yawDelta = NormalizeAngle(solution.Yaw - player.Yaw);
					float pitchDelta = solution.Pitch - player.Pitch;
					float angle = (float)Math.Sqrt(yawDelta * yawDelta + pitchDelta * pitchDelta);
					if (angle > config.BowAimMaxAngle || !HasBowAimLineOfSight(player, entity, forceRefresh: true)) continue;
					selected = entity;
					selectedSolution = solution;
					break;
				}
				if (selected != null)
				{
					bowAimLockedEntityId = selected.EntityId;
				}
			}
			if (selected == null)
			{
				PauseBowAimOutput(clearVisual: true);
				continue;
			}
			if (!HasBowAimLineOfSight(player, selected, forceRefresh: false) || !BowBallistics.TrySolve(player, selected, player.BowUseTicks, config.BowAimPredictionEnabled, out selectedSolution))
			{
				PauseBowAimOutput(clearVisual: true);
				continue;
			}
			bowAimControlling = true;
			autoAimTarget = selected;
			autoAimTargetUpdatedAtMs = Environment.TickCount64;
			UpdateBowAimLockTarget(selected);
			float yawError = NormalizeAngle(selectedSolution.Yaw - player.Yaw);
			float pitchError = selectedSolution.Pitch - player.Pitch;
			float horizontalPixels = (float)Math.Tan(yawError * Math.PI / 180.0) * snapshot.HorizontalProjectionScale;
			float verticalPixels = (float)Math.Tan(pitchError * Math.PI / 180.0) * snapshot.VerticalProjectionScale;
			ApplyBowAimSmoothing(horizontalPixels, verticalPixels);
		}
		ResetBowAimState();
	}

	private void ResetBowAimState()
	{
		PauseBowAimOutput(clearVisual: true);
		bowAimLockedEntityId = 0L;
		bowAimLineOfSightCheckedAtMs = 0L;
		bowAimHasClearLineOfSight = false;
		bowAimLineOfSightEntityId = 0L;
	}

	private void PauseBowAimOutput(bool clearVisual)
	{
		bowAimControlling = false;
		bowAimMouseAccumX = 0f;
		bowAimMouseAccumY = 0f;
		bowAimVelocityX = 0f;
		bowAimVelocityY = 0f;
		if (!clearVisual) return;
		lock (bowLockVisualStateLock)
		{
			bowAimLockedTarget = null;
			bowAimLockStartedAtMs = 0L;
			bowAimLockUpdatedAtMs = 0L;
		}
	}

	private void ApplyBowAimSmoothing(float horizontalPixels, float verticalPixels)
	{
		float smoothness = Math.Clamp(config.BowAimSmoothness, 0f, 100f);
		float response = 0.12f - smoothness * 0.0008f;
		float acceleration = 0.32f - smoothness * 0.0016f;
		float desiredX = Math.Abs(horizontalPixels) < 1.5f ? 0f : Math.Clamp(horizontalPixels * response, -7f, 7f);
		float desiredY = Math.Abs(verticalPixels) < 1.5f ? 0f : Math.Clamp(verticalPixels * response, -3.5f, 3.5f);

		if (desiredX != 0f && bowAimVelocityX != 0f && Math.Sign(desiredX) != Math.Sign(bowAimVelocityX)) bowAimVelocityX *= 0.25f;
		if (desiredY != 0f && bowAimVelocityY != 0f && Math.Sign(desiredY) != Math.Sign(bowAimVelocityY)) bowAimVelocityY *= 0.25f;
		bowAimVelocityX += (desiredX - bowAimVelocityX) * acceleration;
		bowAimVelocityY += (desiredY - bowAimVelocityY) * acceleration;
		if (desiredX == 0f) bowAimVelocityX *= 0.4f;
		if (desiredY == 0f) bowAimVelocityY *= 0.4f;
		if (Math.Abs(bowAimVelocityX) < 0.05f) bowAimVelocityX = 0f;
		if (Math.Abs(bowAimVelocityY) < 0.05f) bowAimVelocityY = 0f;

		bowAimMouseAccumX += bowAimVelocityX;
		bowAimMouseAccumY += bowAimVelocityY;
		int moveX = (int)bowAimMouseAccumX;
		int moveY = (int)bowAimMouseAccumY;
		bowAimMouseAccumX -= moveX;
		bowAimMouseAccumY -= moveY;
		if (moveX == 0 && moveY == 0) return;

		NativeMethods.Input input = default;
		input.Type = 0u;
		input.Data.Mouse.Dx = moveX;
		input.Data.Mouse.Dy = moveY;
		input.Data.Mouse.Flags = 0x0001u;
		NativeMethods.SendInput(1u, new[] { input }, System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.Input>());
	}

	private bool HasBowAimLineOfSight(LocalPlayerState player, EntitySnapshot target, bool forceRefresh)
	{
		long now = Environment.TickCount64;
		if (!forceRefresh && target.EntityId == bowAimLineOfSightEntityId && now - bowAimLineOfSightCheckedAtMs < 75L) return bowAimHasClearLineOfSight;
		bowAimLineOfSightCheckedAtMs = now;
		bowAimLineOfSightEntityId = target.EntityId;
		double targetY = target.Y + Math.Clamp(target.Height * 0.55, 0.5f, 1.35f);
		bowAimHasClearLineOfSight = espDataSource.HasClearLineOfSight(player.X, player.Y + 1.62, player.Z, target.X, targetY, target.Z) == true;
		return bowAimHasClearLineOfSight;
	}

	private void UpdateBowAimLockTarget(EntitySnapshot target)
	{
		long now = Environment.TickCount64;
		lock (bowLockVisualStateLock)
		{
			if (bowAimLockedTarget?.EntityId != target.EntityId)
			{
				bowAimLockStartedAtMs = now;
			}
			bowAimLockedTarget = target;
			bowAimLockUpdatedAtMs = now;
		}
	}

	private static float NormalizeAngle(float angle)
	{
		while (angle <= -180f) angle += 360f;
		while (angle > 180f) angle -= 360f;
		return angle;
	}

	private void ShowHudEditor()
	{
		if (hudEditorForm is { IsDisposed: false })
		{
			hudEditorForm.Activate();
			return;
		}
		Rectangle bounds = base.Bounds;
		if (minecraft != IntPtr.Zero && TryGetClientBounds(minecraft, out Rectangle gameBounds))
		{
			bounds = gameBounds;
		}
		if (bounds.Width < MinGameClientWidth || bounds.Height < MinGameClientHeight)
		{
			return;
		}
		settingsForm.HideForHudEditor();
		hudEditorForm = new HudEditorForm(config, bounds, SettingsChanged);
		hudEditorForm.FormClosed += delegate
		{
			hudEditorForm?.Dispose();
			hudEditorForm = null;
		};
		hudEditorForm.Show();
		hudEditorForm.Activate();
	}

	protected override void OnPaintBackground(PaintEventArgs e)
	{
	}

	protected override void OnPaint(PaintEventArgs e)
	{
		// 分层窗口的实际内容由 UpdateLayeredWindow 提交；这里仅作为 WM_PAINT 的兜底渲染入口。
		RenderFrame();
	}

	private void RenderFrame()
	{
		Size clientSize = base.ClientSize;
		if (clientSize.Width <= 0 || clientSize.Height <= 0 || !EnsureLayeredFrame(clientSize))
		{
			return;
		}
		if (!renderEntryLogged)
		{
			renderEntryLogged = true;
			AppLog.Write($"[渲染入口] 首次渲染 | client={clientSize.Width}x{clientSize.Height} | native={overlayNativelyShown}");
		}
		layeredPaintCount++;
		timingFrameCount++;
		long frameStartTicks = Stopwatch.GetTimestamp();
		using Graphics graphics = Graphics.FromImage(layeredFrame!);
		graphics.Clear(Color.Transparent);
		graphics.SmoothingMode = config.AntiAliasingEnabled ? SmoothingMode.HighQuality : SmoothingMode.None;
		graphics.InterpolationMode = config.AntiAliasingEnabled ? InterpolationMode.HighQualityBicubic : InterpolationMode.NearestNeighbor;
		graphics.PixelOffsetMode = config.AntiAliasingEnabled ? PixelOffsetMode.HighQuality : PixelOffsetMode.None;
		graphics.TextRenderingHint = config.AntiAliasingEnabled ? System.Drawing.Text.TextRenderingHint.AntiAliasGridFit : System.Drawing.Text.TextRenderingHint.SingleBitPerPixelGridFit;
		GameSnapshot gameSnapshot = cachedSnapshot;
		if (DateTime.UtcNow >= nextEspLogUtc)
		{
			nextEspLogUtc = DateTime.UtcNow.AddSeconds(5.0);
			Console.WriteLine($"[ESP] 真实实体快照数量：{gameSnapshot.Entities.Count}");
		}
		DrawEsp(graphics, gameSnapshot);
		DrawBedEsp(graphics, cachedBedSnapshot);
		DrawFireballPrediction(graphics, cachedFireballSnapshot);
		DrawBowAimLock(graphics, gameSnapshot);
		DrawHud(graphics, gameSnapshot);
		if (!config.PetEnabled)
		{
			drawTicksAccum += Stopwatch.GetTimestamp() - frameStartTicks;
			PresentLayeredFrame(clientSize);
			LogRenderDiagnostic(null, clientSize);
			return;
		}
		int num = Math.Clamp(config.PetHeight, 100, Math.Max(100, base.ClientSize.Height * 2));
		int num2 = (int)Math.Round((double)num * (double)pet.Width / (double)pet.Height);
		Rectangle destRect = new Rectangle(base.ClientSize.Width - num2 - config.PetOffsetX, base.ClientSize.Height - num - config.PetOffsetY, num2, num);
		bool num3 = displayMode == DisplayMode.ForceBlue;
		bool flag = displayMode == DisplayMode.ForceRed || (displayMode == DisplayMode.Auto && espDataSource.IsLocalPlayerHurt);
		float num4 = (num3 ? 0.62f : (flag ? 0.82f : 0f));
		float num5 = 1f - num4;
		ColorMatrix colorMatrix = (num3 ? new ColorMatrix(new float[5][]
		{
			new float[5] { num5, 0f, 0f, 0f, 0f },
			new float[5] { 0f, num5, 0f, 0f, 0f },
			new float[5] { 0f, 0f, num5, 0f, 0f },
			new float[5] { 0f, 0f, 0f, 1f, 0f },
			new float[5]
			{
				0.08f * num4,
				0.38f * num4,
				num4,
				0f,
				1f
			}
		}) : new ColorMatrix(new float[5][]
		{
			new float[5] { num5, 0f, 0f, 0f, 0f },
			new float[5] { 0f, num5, 0f, 0f, 0f },
			new float[5] { 0f, 0f, num5, 0f, 0f },
			new float[5] { 0f, 0f, 0f, 1f, 0f },
			new float[5]
			{
				num4,
				0.04f * num4,
				0.04f * num4,
				0f,
				1f
			}
		}));
		if (num4 <= 0.001f)
		{
			graphics.DrawImage(GetScaledPet(num2, num), destRect);
		}
		else
		{
			using ImageAttributes imageAttributes = new ImageAttributes();
			imageAttributes.SetColorMatrix(colorMatrix);
			graphics.DrawImage(GetScaledPet(num2, num), destRect, 0, 0, num2, num, GraphicsUnit.Pixel, imageAttributes);
		}
		if (!petAlphaLogged && destRect.Width > 0 && destRect.Height > 0 && layeredFrame != null)
		{
			petAlphaLogged = true;
			int opaqueSamples = 0;
			int totalSamples = 0;
			int maxAlpha = 0;
			for (int gy = 0; gy < 24; gy++)
			{
				for (int gx = 0; gx < 24; gx++)
				{
					int px = destRect.X + destRect.Width * gx / 24;
					int py = destRect.Y + destRect.Height * gy / 24;
					if (px < 0 || py < 0 || px >= layeredFrame.Width || py >= layeredFrame.Height)
					{
						continue;
					}
					totalSamples++;
					int alpha = layeredFrame.GetPixel(px, py).A;
					if (alpha > 0)
					{
						opaqueSamples++;
					}
					if (alpha > maxAlpha)
					{
						maxAlpha = alpha;
					}
				}
			}
			AppLog.Write($"[渲染] 宠物区域 alpha 采样 | rect={destRect.X},{destRect.Y},{destRect.Width}x{destRect.Height} | 采样={totalSamples} | 非透明={opaqueSamples} | maxAlpha={maxAlpha}");
		}
		drawTicksAccum += Stopwatch.GetTimestamp() - frameStartTicks;
		PresentLayeredFrame(clientSize);
		LogRenderDiagnostic(destRect, clientSize);
	}

	private Bitmap GetScaledPet(int width, int height)
	{
		int targetWidth = Math.Max(1, width);
		int targetHeight = Math.Max(1, height);
		if (petScaled != null && petScaledWidth == targetWidth && petScaledHeight == targetHeight)
		{
			return petScaled;
		}
		petScaled?.Dispose();
		petScaled = new Bitmap(targetWidth, targetHeight, PixelFormat.Format32bppPArgb);
		using (Graphics graphics = Graphics.FromImage(petScaled))
		{
			graphics.CompositingMode = CompositingMode.SourceCopy;
			graphics.SmoothingMode = SmoothingMode.HighQuality;
			graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
			graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
			graphics.DrawImage(pet, new Rectangle(0, 0, targetWidth, targetHeight));
		}
		petScaledWidth = targetWidth;
		petScaledHeight = targetHeight;
		return petScaled;
	}

	private void LogRenderDiagnostic(Rectangle? petRect, Size clientSize)
	{
		if (DateTime.UtcNow < nextRenderLogUtc)
		{
			return;
		}
		nextRenderLogUtc = DateTime.UtcNow.AddSeconds(5.0);
		long nowTicks = Stopwatch.GetTimestamp();
		double seconds = (nowTicks - lastRenderLogTicks) / (double)Stopwatch.Frequency;
		long frames = layeredPaintCount - lastRenderLogFrames;
		string fpsPart = seconds > 0.05 ? $" | 估算FPS={(frames / seconds):0.0}" : string.Empty;
		double ticksToMs = 1000.0 / Stopwatch.Frequency;
		double drawMs = timingFrameCount > 0 ? drawTicksAccum * ticksToMs / timingFrameCount : 0.0;
		double presentMs = timingFrameCount > 0 ? presentTicksAccum * ticksToMs / timingFrameCount : 0.0;
		drawTicksAccum = 0L;
		presentTicksAccum = 0L;
		timingFrameCount = 0L;
		lastRenderLogTicks = nowTicks;
		lastRenderLogFrames = layeredPaintCount;
		string petPart = petRect.HasValue ? $" | 宠物矩形={petRect.Value.X},{petRect.Value.Y},{petRect.Value.Width}x{petRect.Value.Height}" : " | 宠物=关闭";
		AppLog.Write($"[渲染] 帧={layeredPaintCount} | 原生显示={overlayNativelyShown} | 客户区={clientSize.Width}x{clientSize.Height} | DIB失败={layeredEnsureFailures} | 更新失败={layeredUpdateFailures}{petPart}{fpsPart} | 绘制={drawMs:0.0}ms | 提交={presentMs:0.0}ms");
	}

	private bool EnsureLayeredFrame(Size size)
	{
		if (layeredFrame != null && layeredFrameSize == size && layeredBits != IntPtr.Zero)
		{
			return true;
		}
		ReleaseLayeredResources();
		layeredScreenDc = NativeMethods.GetDC(IntPtr.Zero);
		layeredMemoryDc = layeredScreenDc != IntPtr.Zero ? NativeMethods.CreateCompatibleDC(layeredScreenDc) : IntPtr.Zero;
		if (layeredScreenDc == IntPtr.Zero || layeredMemoryDc == IntPtr.Zero)
		{
			layeredEnsureFailures++;
			AppLog.Write($"[渲染] 分层窗口 DC 创建失败 | ScreenDC=0x{layeredScreenDc.ToInt64():X} | MemDC=0x{layeredMemoryDc.ToInt64():X}");
			ReleaseLayeredResources();
			return false;
		}
		NativeMethods.BitmapInfo bitmapInfo = new NativeMethods.BitmapInfo
		{
			Header = new NativeMethods.BitmapInfoHeader
			{
				Size = 40u,
				Width = size.Width,
				Height = -size.Height,
				Planes = 1,
				BitCount = 32,
				Compression = 0u
			}
		};
		layeredDib = NativeMethods.CreateDIBSection(layeredScreenDc, ref bitmapInfo, 0u, out layeredBits, IntPtr.Zero, 0u);
		if (layeredDib == IntPtr.Zero || layeredBits == IntPtr.Zero)
		{
			layeredEnsureFailures++;
			AppLog.Write($"[渲染] DIB 创建失败 | DIB=0x{layeredDib.ToInt64():X} | Bits=0x{layeredBits.ToInt64():X} | LastError={System.Runtime.InteropServices.Marshal.GetLastWin32Error()} | Size={size.Width}x{size.Height}");
			ReleaseLayeredResources();
			return false;
		}
		layeredFrame = new Bitmap(size.Width, size.Height, size.Width * 4, PixelFormat.Format32bppPArgb, layeredBits);
		layeredOldDib = NativeMethods.SelectObject(layeredMemoryDc, layeredDib);
		layeredFrameSize = size;
		AppLog.Write($"[渲染] 分层帧已创建 | Size={size.Width}x{size.Height} | ScreenDC=0x{layeredScreenDc.ToInt64():X} | MemDC=0x{layeredMemoryDc.ToInt64():X} | DIB=0x{layeredDib.ToInt64():X} | Bits=0x{layeredBits.ToInt64():X} | OldDib=0x{layeredOldDib.ToInt64():X}");
		return true;
	}

	private void PresentLayeredFrame(Size size)
	{
		if (layeredFrame == null || layeredMemoryDc == IntPtr.Zero || layeredBits == IntPtr.Zero)
		{
			return;
		}
		// layeredFrame 直接包装 DIB 像素内存，GDI+ 绘制结果即 DIB 内容，无需逐帧复制。
		NativeMethods.Point destination = new NativeMethods.Point
		{
			X = base.Left,
			Y = base.Top
		};
		NativeMethods.Point source = default(NativeMethods.Point);
		NativeMethods.Size bitmapSize = new NativeMethods.Size
		{
			Width = size.Width,
			Height = size.Height
		};
		NativeMethods.BlendFunction blend = new NativeMethods.BlendFunction
		{
			BlendOp = NativeMethods.AcSrcOver,
			BlendFlags = 0,
			SourceConstantAlpha = byte.MaxValue,
			AlphaFormat = NativeMethods.AcSrcAlpha
		};
		long presentStartTicks = Stopwatch.GetTimestamp();
		bool updated = NativeMethods.UpdateLayeredWindow(base.Handle, layeredScreenDc, ref destination, ref bitmapSize, layeredMemoryDc, ref source, 0u, ref blend, NativeMethods.UlwAlpha);
		presentTicksAccum += Stopwatch.GetTimestamp() - presentStartTicks;
		if (!updated)
		{
			layeredUpdateFailures++;
			if (!layeredUpdateLogged)
			{
				layeredUpdateLogged = true;
				AppLog.Write($"[渲染] UpdateLayeredWindow 失败 | LastError={System.Runtime.InteropServices.Marshal.GetLastWin32Error()} | Handle=0x{base.Handle.ToInt64():X} | Size={size.Width}x{size.Height} | Dst={destination.X},{destination.Y} | Visible={base.Visible}");
			}
		}
		else if (!layeredUpdateLogged)
		{
			layeredUpdateLogged = true;
			AppLog.Write($"[渲染] UpdateLayeredWindow 成功 | Handle=0x{base.Handle.ToInt64():X} | Size={size.Width}x{size.Height} | Dst={destination.X},{destination.Y} | Visible={base.Visible}");
		}
	}

	private void ReleaseLayeredResources()
	{
		// 先释放直接包装 DIB 内存的 Bitmap，再删除 DIB，避免访问已释放内存。
		layeredFrame?.Dispose();
		layeredFrame = null;
		if (layeredMemoryDc != IntPtr.Zero && layeredOldDib != IntPtr.Zero)
		{
			NativeMethods.SelectObject(layeredMemoryDc, layeredOldDib);
			layeredOldDib = IntPtr.Zero;
		}
		if (layeredDib != IntPtr.Zero)
		{
			NativeMethods.DeleteObject(layeredDib);
			layeredDib = IntPtr.Zero;
		}
		if (layeredMemoryDc != IntPtr.Zero)
		{
			NativeMethods.DeleteDC(layeredMemoryDc);
			layeredMemoryDc = IntPtr.Zero;
		}
		if (layeredScreenDc != IntPtr.Zero)
		{
			NativeMethods.ReleaseDC(IntPtr.Zero, layeredScreenDc);
			layeredScreenDc = IntPtr.Zero;
		}
		layeredBits = IntPtr.Zero;
		layeredOldDib = IntPtr.Zero;
		layeredFrameSize = default(Size);
	}

	private void DrawEsp(Graphics graphics, GameSnapshot snapshot)
	{
		if (snapshot.Entities.Count == 0)
		{
			return;
		}
		Color color = Color.FromArgb(config.EspColorArgb);
		EntitySnapshot[] array = (from entity in snapshot.Entities.Where(IsEntityEligible)
			orderby entity.Distance
			select entity).ToArray();
		if (DateTime.UtcNow >= nextEspDiagnosticLogUtc)
		{
			nextEspDiagnosticLogUtc = DateTime.UtcNow.AddSeconds(5.0);
			AppLog.Write($"[ESP] 快照实体={snapshot.Entities.Count} | 符合范围={array.Length} | 启用={config.EspEnabled} | 玩家={config.EspPlayers} | 方框={config.EspBoxes} | 范围={config.EspRange:0.#}");
		}
		if (config.EspEnabled)
		{
			foreach (EntitySnapshot item in array.Where(IsMainEspEntityVisible))
			{
				RectangleF screenBounds = item.ScreenBounds;
				if (screenBounds.Width < 2f || screenBounds.Height < 2f)
				{
					continue;
				}
				if (config.EspBoxes) DrawCornerBox(graphics, screenBounds, color);
				if (config.EspHealth && item.MaxHealth > 0f) DrawHealthBar(graphics, screenBounds, item.Health, item.MaxHealth);
				if (config.EspNames || config.EspDistance)
				{
					string text = ((config.EspNames && config.EspDistance) ? $"{item.Name}  {item.Distance:0}m" : (config.EspNames ? item.Name : $"{item.Distance:0}m"));
					DrawOutlinedText(graphics, text, screenBounds.Left + screenBounds.Width / 2f, screenBounds.Top - 17f);
				}
				if (config.EspEquipmentEnabled && item.Kind == EntityKind.Player && item.Equipment is { HasAny: true })
				{
					float equipmentScale = Math.Clamp(config.EspEquipmentScale, 0.6f, 2f);
					float equipmentY = screenBounds.Top - ((config.EspNames || config.EspDistance) ? 42f : 22f) * equipmentScale;
					DrawEquipmentSlots(graphics, item.Equipment, screenBounds.Left + screenBounds.Width / 2f, equipmentY, equipmentScale, compact: false);
				}
				if (!config.EspTracers) continue;
				using Pen pen = new Pen(Color.FromArgb(210, 0, 0, 0), 3f);
				using Pen pen2 = new Pen(color, 1f);
				PointF pt = new PointF((float)base.ClientSize.Width / 2f, (float)base.ClientSize.Height - 2f);
				PointF pt2 = new PointF(screenBounds.Left + screenBounds.Width / 2f, screenBounds.Bottom);
				graphics.DrawLine(pen, pt, pt2);
				graphics.DrawLine(pen2, pt, pt2);
			}
		}
		DrawTargetList(graphics, array);
	}

	private bool IsEntityEligible(EntitySnapshot entity)
	{
		if (!entity.Visible || entity.Distance > config.EspRange)
		{
			return false;
		}
		return !HasHiddenEntityName(entity);
	}

	private void DrawBedEsp(Graphics graphics, BedEspSnapshot snapshot)
	{
		if (!config.BedEspEnabled)
		{
			return;
		}
		Color bedColor = Color.FromArgb(config.BedEspColorArgb);
		Color obsidianColor = Color.FromArgb(config.BedEspObsidianColorArgb);
		foreach (ProjectedBoxSnapshot box in snapshot.Beds.OrderByDescending(item => item.Distance))
		{
			DrawProjectedBox(graphics, box.Corners, bedColor, config.BedEspOpacityPercent, config.BedEspOutline);
		}
		if (config.BedEspObsidian)
		{
			foreach (ProjectedBoxSnapshot box in snapshot.Obsidian.OrderByDescending(item => item.Distance))
			{
				DrawProjectedBox(graphics, box.Corners, obsidianColor, config.BedEspOpacityPercent, config.BedEspOutline);
			}
		}
		if (DateTime.UtcNow >= nextBedEspLogUtc)
		{
			nextBedEspLogUtc = DateTime.UtcNow.AddSeconds(5);
			AppLog.Write($"[BED-ESP] beds={snapshot.ScannedBedCount} projected={snapshot.Beds.Count} obsidian={snapshot.Obsidian.Count} range={config.BedEspRange} | {snapshot.Diagnostic}");
		}
	}

	private static void DrawProjectedBox(Graphics graphics, PointF[] corners, Color color, int opacityPercent, bool outline)
	{
		if (corners.Length != 8)
		{
			return;
		}
		int[][] faces =
		{
			new[] { 0, 1, 5, 4 }, new[] { 2, 3, 7, 6 },
			new[] { 0, 1, 3, 2 }, new[] { 4, 5, 7, 6 },
			new[] { 0, 4, 6, 2 }, new[] { 1, 5, 7, 3 }
		};
		using SolidBrush fill = new SolidBrush(Color.FromArgb(Math.Clamp(opacityPercent, 0, 100) * 255 / 100, color.R, color.G, color.B));
		foreach (int[] face in faces)
		{
			graphics.FillPolygon(fill, new[] { corners[face[0]], corners[face[1]], corners[face[2]], corners[face[3]] });
		}
		if (!outline)
		{
			return;
		}
		(int start, int end)[] edges =
		{
			(0, 1), (1, 5), (5, 4), (4, 0),
			(2, 3), (3, 7), (7, 6), (6, 2),
			(0, 2), (1, 3), (4, 6), (5, 7)
		};
		using Pen pen = new Pen(Color.FromArgb(230, color.R, color.G, color.B), 1.5f);
		foreach ((int start, int end) in edges)
		{
			graphics.DrawLine(pen, corners[start], corners[end]);
		}
	}

	private void DrawFireballPrediction(Graphics graphics, FireballPredictionSnapshot snapshot)
	{
		if (!config.FireballPredictionEnabled || snapshot.Fireballs.Count == 0) return;
		Color color = Color.FromArgb(config.FireballPredictionColorArgb);
		float thickness = Math.Clamp(config.FireballPredictionThickness, 1f, 10f);
		using Pen glow = new Pen(Color.FromArgb(55, color), thickness + 7f) { LineJoin = LineJoin.Round, StartCap = LineCap.Round, EndCap = LineCap.Round };
		using Pen outline = new Pen(Color.FromArgb(220, 0, 0, 0), thickness + 3f) { LineJoin = LineJoin.Round, StartCap = LineCap.Round, EndCap = LineCap.Round };
		using Pen trajectory = new Pen(color, thickness) { LineJoin = LineJoin.Round, StartCap = LineCap.Round, EndCap = LineCap.Round };
		using SolidBrush faceBrush = new SolidBrush(Color.FromArgb(52, color));
		using SolidBrush impactBrush = new SolidBrush(Color.FromArgb(210, color));
		foreach (FireballSnapshot fireball in snapshot.Fireballs.OrderByDescending(item => item.Distance))
		{
			if (config.FireballPredictionTrajectoryEnabled)
			{
				foreach (PointF[] face in fireball.TrajectoryFaces)
				{
					if (face.Length >= 3) graphics.FillPolygon(faceBrush, face);
				}
			}
			if (config.FireballPredictionBoxes && fireball.ScreenBounds.Width >= 2f && fireball.ScreenBounds.Height >= 2f)
			{
				DrawCornerBox(graphics, fireball.ScreenBounds, color);
				DrawOutlinedText(graphics, $"{fireball.Name}  {fireball.Distance:0}m", fireball.ScreenBounds.Left + fireball.ScreenBounds.Width / 2f, fireball.ScreenBounds.Top - 17f);
			}
			if (!config.FireballPredictionTrajectoryEnabled) continue;
			foreach (IReadOnlyList<PointF> segment in fireball.TrajectorySegments)
			{
				PointF[] points = segment.ToArray();
				if (points.Length < 2) continue;
				graphics.DrawLines(glow, points);
				graphics.DrawLines(outline, points);
				graphics.DrawLines(trajectory, points);
			}
			IReadOnlyList<PointF>? lastSegment = fireball.TrajectorySegments.LastOrDefault(segment => segment.Count > 0);
			if (fireball.CollisionPredicted && lastSegment != null)
			{
				PointF impact = lastSegment[^1];
				float radius = Math.Max(4f, thickness + 2f);
				graphics.FillEllipse(impactBrush, impact.X - radius, impact.Y - radius, radius * 2f, radius * 2f);
			}
		}
	}

	private void DrawBowAimLock(Graphics graphics, GameSnapshot snapshot)
	{
		if (!config.BowAimEnabled || !config.BowAimLockAnimationEnabled) return;
		EntitySnapshot? locked;
		long startedAt;
		long updatedAt;
		lock (bowLockVisualStateLock)
		{
			locked = bowAimLockedTarget;
			startedAt = bowAimLockStartedAtMs;
			updatedAt = bowAimLockUpdatedAtMs;
		}
		long now = Environment.TickCount64;
		if (locked == null || now - updatedAt > 150L) return;
		EntitySnapshot target = snapshot.Entities.FirstOrDefault(entity => entity.EntityId == locked.EntityId) ?? locked;
		RectangleF bounds = target.ScreenBounds;
		if (bounds.Width < 2f || bounds.Height < 2f) return;

		float elapsed = Math.Max(0f, now - startedAt);
		float animationSpeed = Math.Clamp(config.BowAimLockAnimationSpeedPercent, 25, 300) / 100f;
		float animationSize = Math.Clamp(config.BowAimLockAnimationSizePercent, 50, 200) / 100f;
		float animationTime = elapsed * animationSpeed;
		float entry = Math.Clamp(animationTime / 280f, 0f, 1f);
		float eased = 1f - (float)Math.Pow(1f - entry, 3.0);
		float pulse = 0.5f + 0.5f * (float)Math.Sin(animationTime / 125f);
		float margin = (32f + (9f - 32f) * eased + pulse * 2f) * animationSize;
		RectangleF lockRect = RectangleF.Inflate(bounds, margin, margin);
		Color accent = Color.FromArgb(config.BowAimLockAnimationColorArgb);
		int alpha = Math.Clamp((int)(90f + eased * 125f + pulse * 35f), 0, 255);
		Color animatedColor = Color.FromArgb(alpha, accent.R, accent.G, accent.B);
		float thickness = Math.Clamp(config.BowAimLockAnimationThickness, 1f, 8f);
		using Pen shadow = new Pen(Color.FromArgb(Math.Min(190, alpha), 0, 0, 0), thickness + 2f);
		using Pen bright = new Pen(animatedColor, thickness);
		float corner = Math.Clamp(Math.Min(lockRect.Width, lockRect.Height) * 0.22f, 8f, 24f) * animationSize;
		DrawLockCorners(graphics, lockRect, corner, shadow);
		DrawLockCorners(graphics, lockRect, corner, bright);

		float diameter = Math.Max(lockRect.Width, lockRect.Height) + (16f + pulse * 5f) * animationSize;
		RectangleF ring = new RectangleF(bounds.Left + bounds.Width / 2f - diameter / 2f, bounds.Top + bounds.Height / 2f - diameter / 2f, diameter, diameter);
		float rotation = (animationTime * 0.18f) % 360f;
		graphics.DrawArc(shadow, ring, rotation, 68f);
		graphics.DrawArc(shadow, ring, rotation + 180f, 68f);
		graphics.DrawArc(bright, ring, rotation, 68f);
		graphics.DrawArc(bright, ring, rotation + 180f, 68f);

		PointF center = new PointF(bounds.Left + bounds.Width / 2f, bounds.Top + bounds.Height / 2f);
		float cross = (5f + pulse * 2f) * animationSize;
		graphics.DrawLine(shadow, center.X - cross, center.Y, center.X + cross, center.Y);
		graphics.DrawLine(shadow, center.X, center.Y - cross, center.X, center.Y + cross);
		graphics.DrawLine(bright, center.X - cross, center.Y, center.X + cross, center.Y);
		graphics.DrawLine(bright, center.X, center.Y - cross, center.X, center.Y + cross);
	}

	private static void DrawLockCorners(Graphics graphics, RectangleF rect, float length, Pen pen)
	{
		graphics.DrawLine(pen, rect.Left, rect.Top, rect.Left + length, rect.Top);
		graphics.DrawLine(pen, rect.Left, rect.Top, rect.Left, rect.Top + length);
		graphics.DrawLine(pen, rect.Right, rect.Top, rect.Right - length, rect.Top);
		graphics.DrawLine(pen, rect.Right, rect.Top, rect.Right, rect.Top + length);
		graphics.DrawLine(pen, rect.Left, rect.Bottom, rect.Left + length, rect.Bottom);
		graphics.DrawLine(pen, rect.Left, rect.Bottom, rect.Left, rect.Bottom - length);
		graphics.DrawLine(pen, rect.Right, rect.Bottom, rect.Right - length, rect.Bottom);
		graphics.DrawLine(pen, rect.Right, rect.Bottom, rect.Right, rect.Bottom - length);
	}

	private bool IsNearbyTargetHudEligible(EntitySnapshot entity, float maxDistance)
	{
		if (!entity.Visible || entity.Distance > maxDistance)
		{
			return false;
		}
		return !HasHiddenEntityName(entity);
	}

	private bool HasHiddenEntityName(EntitySnapshot entity)
	{
		string text = config.EspHiddenNameKeywords ?? string.Empty;
		if (!string.Equals(text, cachedHiddenKeywordsText, StringComparison.Ordinal))
		{
			cachedHiddenKeywordsText = text;
			cachedHiddenKeywords = text.Split(new char[5] { ',', ';', '|', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
		}
		if (cachedHiddenKeywords.Length == 0)
		{
			return false;
		}
		return cachedHiddenKeywords.Any((string keyword) => entity.Name.Contains(keyword, StringComparison.OrdinalIgnoreCase));
	}

	private bool IsMainEspEntityVisible(EntitySnapshot entity)
	{
		if (entity.Kind != EntityKind.Player)
		{
			return config.EspMobs;
		}
		return config.EspPlayers;
	}

	private void DrawTargetList(Graphics graphics, IReadOnlyList<EntitySnapshot> entities)
	{
		if (!config.EspTargetListEnabled)
		{
			return;
		}
		EntitySnapshot[] array = entities.Where((EntitySnapshot entity) => (entity.Kind != EntityKind.Player) ? config.EspTargetListMobs : config.EspTargetListPlayers).Take(Math.Clamp(config.EspTargetListMaxCount, 1, 64)).ToArray();
		if (array.Length == 0)
		{
			return;
		}
		float num = Math.Clamp(config.EspTargetListFontSize, 6f, 32f);
		if (targetListFont == null || Math.Abs(cachedTargetFontSize - num) > 0.01f)
		{
			targetListFont?.Dispose();
			targetListHeaderFont?.Dispose();
			cachedTargetFontSize = num;
			targetListFont = new Font("Segoe UI Semibold", num, FontStyle.Bold, GraphicsUnit.Point);
			targetListHeaderFont = new Font("Segoe UI Semibold", num + 1f, FontStyle.Bold, GraphicsUnit.Point);
		}
		Font font = targetListFont!;
		Font font2 = targetListHeaderFont!;
		Color color = Color.FromArgb(config.EspTargetListColorArgb);
		float num2 = 220f;
		PointF position = ResolveHudPosition(config.TargetListHud, new SizeF(num2, 34f + (font.GetHeight(graphics) + 5f) * array.Length));
		float num3 = position.X;
		float num4 = position.Y;
		float num5 = font.GetHeight(graphics) + 5f;
		using SolidBrush brush = new SolidBrush(color);
		using SolidBrush brush2 = new SolidBrush(Color.FromArgb(230, 0, 0, 0));
		if (config.EspTargetListShowBackground)
		{
			Color background = WithOpacity(config.EspTargetListBackgroundColorArgb, config.EspTargetListBackgroundOpacityPercent);
			using SolidBrush backgroundBrush = new SolidBrush(background);
			using Pen accentPen = new Pen(Color.FromArgb(config.EspTargetListAccentColorArgb), 2f);
			RectangleF panel = new RectangleF(num3, num4, num2, 34f + num5 * array.Length);
			graphics.FillRectangle(backgroundBrush, panel);
			graphics.DrawLine(accentPen, panel.Left, panel.Top, panel.Right, panel.Top);
		}
		graphics.DrawString($"目标列表  {array.Length}", font2, brush, num3 + 10f, num4 + 7f);
		for (int num6 = 0; num6 < array.Length; num6++)
		{
			EntitySnapshot entitySnapshot = array[num6];
			string text = ((entitySnapshot.Kind == EntityKind.Player) ? "玩家" : "生物");
			string s = (config.EspTargetListDistance ? $"{text}  {entitySnapshot.Name}  {entitySnapshot.Distance:0}m" : (text + "  " + entitySnapshot.Name));
			float num7 = num4 + 31f + num5 * (float)num6;
			graphics.DrawString(s, font, brush2, num3 + 10f + 1f, num7 + 1f);
			graphics.DrawString(s, font, brush, num3 + 10f, num7);
		}
	}

	private void DrawHud(Graphics graphics, GameSnapshot snapshot)
	{
		DrawEnabledModulesHud(graphics);
		DrawTargetHud(graphics, snapshot);
	}

	private void DrawEnabledModulesHud(Graphics graphics)
	{
		if (!config.EnabledModulesHudEnabled)
		{
			return;
		}
		bool english = config.EnabledModulesHudEnglish;
		List<string> enabled = new List<string>();
		if (config.AutoAimEnabled) enabled.Add(english ? "Aim Assist" : "辅助瞄准");
		if (config.BowAimEnabled) enabled.Add(english ? "Bow Aim" : "弓箭自瞄");
		if (config.LeftClickerEnabled) enabled.Add(english ? "Left Clicker" : "左键连点");
		if (config.RightClickerEnabled) enabled.Add(english ? "Right Clicker" : "右键连点");
		if (config.AutoSneakEnabled) enabled.Add(english ? "Auto Sneak" : "自动潜行");
		if (config.KnockbackEnabled) enabled.Add(english ? "Anti Knockback" : "防击退");
		if (config.ChestStealerEnabled) enabled.Add(english ? "Chest Stealer" : "箱子小偷");
		if (config.InventoryManagerEnabled) enabled.Add(english ? "Inventory Manager" : "背包管理");
		if (config.EspEnabled) enabled.Add("ESP");
		if (config.BedEspEnabled) enabled.Add(english ? "Bed ESP" : "床 ESP");
		if (config.FireballPredictionEnabled) enabled.Add(english ? "Fireball Prediction" : "火球预测");
		if (config.EspTargetListEnabled) enabled.Add(english ? "Target List" : "目标列表");
		if (config.TargetHudEnabled) enabled.Add(english ? "Target HUD" : "目标显示");
		if (config.StartupAnimationEnabled) enabled.Add(english ? "Startup Animation" : "\u542F\u52A8\u52A8\u753B");
		if (config.PetEnabled) enabled.Add(english ? "Desktop Pet" : "桌面宠物");
		if (config.AntiAliasingEnabled) enabled.Add(english ? "Anti Aliasing" : "抗锯齿");
		if (enabled.Count == 0)
		{
			return;
		}
		using Font font = new Font("Segoe UI Semibold", Math.Clamp(config.EnabledModulesHudFontSize, 6f, 32f), FontStyle.Bold, GraphicsUnit.Point);
		enabled = enabled.OrderByDescending(name => graphics.MeasureString(name, font).Width).ThenBy(name => name, StringComparer.OrdinalIgnoreCase).ToList();
		float lineHeight = font.GetHeight(graphics) + 4f;
		float width = enabled.Max(name => graphics.MeasureString(name, font).Width) + 22f;
		float height = lineHeight * enabled.Count + 12f;
		PointF position = ResolveHudPosition(config.EnabledModulesHud, new SizeF(width, height));
		RectangleF panel = new RectangleF(position, new SizeF(width, height));
		if (config.EnabledModulesHudShowBackground)
		{
			using SolidBrush background = new SolidBrush(WithOpacity(config.EnabledModulesHudBackgroundColorArgb, config.EnabledModulesHudBackgroundOpacityPercent));
			using GraphicsPath backgroundPath = CreateRoundedRectanglePath(panel, config.EnabledModulesHudCornerRadius);
			graphics.FillPath(background, backgroundPath);
			using SolidBrush accentBrush = new SolidBrush(Color.FromArgb(config.EnabledModulesHudAccentColorArgb));
			float accentRadius = Math.Min(config.EnabledModulesHudCornerRadius, 2);
			using GraphicsPath accentPath = CreateRoundedRectanglePath(new RectangleF(panel.Left, panel.Top, 2f, panel.Height), accentRadius);
			graphics.FillPath(accentBrush, accentPath);
		}
		using SolidBrush textBrush = new SolidBrush(Color.FromArgb(config.EnabledModulesHudTextColorArgb));
		for (int i = 0; i < enabled.Count; i++)
		{
			float x = config.EnabledModulesHud.AnchorRight ? panel.Right - 10f - graphics.MeasureString(enabled[i], font).Width : panel.Left + 10f;
			graphics.DrawString(enabled[i], font, textBrush, x, panel.Top + 6f + i * lineHeight);
		}
	}

	private void DrawTargetHud(Graphics graphics, GameSnapshot snapshot)
	{
		if (!config.TargetHudEnabled)
		{
			return;
		}
		EntitySnapshot? primaryTarget = SelectHudTarget(snapshot);
		float nearbyRange = Math.Clamp(config.TargetHudNearbyRange, 4f, 256f);
		List<EntitySnapshot> nearbyPlayers = snapshot.Entities
			.Where(entity => entity.Kind == EntityKind.Player && IsNearbyTargetHudEligible(entity, nearbyRange))
			.OrderBy(entity => entity.Distance)
			.ToList();
		if (config.TargetHudShowNearbyPlayers)
		{
			if (nearbyPlayers.Count == 0)
			{
				targetHudCardEnteredAt.Clear();
				return;
			}
			if (nearbyPlayers.Count == 1)
			{
				targetHudCardEnteredAt.Clear();
				float nearbyScale = Math.Clamp(config.TargetHudScale, 0.6f, 2f);
				SizeF nearbySize = new SizeF(250f * nearbyScale, 106f * nearbyScale);
				PointF nearbyPosition = ResolveHudPosition(config.TargetHud, nearbySize);
				DrawTargetCard(graphics, nearbyPlayers[0], new RectangleF(nearbyPosition, nearbySize), nearbyScale, compact: false);
				return;
			}
			List<EntitySnapshot> targets = new List<EntitySnapshot>();
			if (primaryTarget != null)
			{
				EntitySnapshot? currentPrimary = nearbyPlayers.FirstOrDefault(entity => entity.EntityId == primaryTarget.EntityId);
				if (currentPrimary != null)
				{
					targets.Add(currentPrimary);
				}
			}
			targets.AddRange(nearbyPlayers.Where(entity => targets.All(existing => existing.EntityId != entity.EntityId)));
			DrawNearbyTargetCards(graphics, targets.Take(Math.Clamp(config.TargetHudNearbyMaxCount, 2, 20)).ToArray());
			return;
		}
		targetHudCardEnteredAt.Clear();
		if (primaryTarget == null)
		{
			return;
		}
		float scale = Math.Clamp(config.TargetHudScale, 0.6f, 2f);
		SizeF size = new SizeF(250f * scale, 106f * scale);
		PointF position = ResolveHudPosition(config.TargetHud, size);
		DrawTargetCard(graphics, primaryTarget, new RectangleF(position, size), scale, compact: false);
	}

	private void DrawNearbyTargetCards(Graphics graphics, IReadOnlyList<EntitySnapshot> targets)
	{
		if (targets.Count == 0)
		{
			return;
		}
		long now = Environment.TickCount64;
		HashSet<long> activeIds = targets.Select(target => target.EntityId).ToHashSet();
		foreach (long staleId in targetHudCardEnteredAt.Keys.Where(id => !activeIds.Contains(id)).ToArray())
		{
			targetHudCardEnteredAt.Remove(staleId);
		}
		foreach (EntitySnapshot target in targets)
		{
			if (!targetHudCardEnteredAt.ContainsKey(target.EntityId))
			{
				targetHudCardEnteredAt[target.EntityId] = now;
			}
		}
		int columns = config.TargetHudNearbyVerticalLayout
			? 1
			: Math.Min(targets.Count, Math.Max(1, (int)Math.Ceiling(Math.Sqrt(targets.Count))));
		int rows = (int)Math.Ceiling(targets.Count / (double)columns);
		float scale = Math.Clamp(config.TargetHudScale * 0.82f, 0.62f, 1.4f);
		float unscaledWidth = columns * 188f + (columns - 1) * 6f;
		float unscaledHeight = rows * (config.TargetHudShowEquipment ? 82f : 64f) + (rows - 1) * 6f;
		float fitScale = Math.Min(
			Math.Max(0.12f, (ClientSize.Width - 12f) / Math.Max(1f, unscaledWidth)),
			Math.Max(0.12f, (ClientSize.Height - 12f) / Math.Max(1f, unscaledHeight)));
		scale = Math.Min(scale, fitScale);
		float cardWidth = 188f * scale;
		float cardHeight = (config.TargetHudShowEquipment ? 82f : 64f) * scale;
		float gap = 6f * scale;
		SizeF groupSize = new SizeF(columns * cardWidth + (columns - 1) * gap, rows * cardHeight + (rows - 1) * gap);
		PointF groupPosition = ResolveHudPosition(config.TargetHud, groupSize);
		for (int i = 0; i < targets.Count; i++)
		{
			int column = i % columns;
			int row = i / columns;
			RectangleF card = new RectangleF(
				groupPosition.X + column * (cardWidth + gap),
				groupPosition.Y + row * (cardHeight + gap),
				cardWidth,
				cardHeight);
			float progress = 1f;
			if (config.TargetHudPlayerEnterAnimation)
			{
				progress = Math.Clamp((now - targetHudCardEnteredAt[targets[i].EntityId]) / 280f, 0f, 1f);
				progress = 1f - (float)Math.Pow(1f - progress, 3f);
			}
			DrawAnimatedTargetCard(graphics, targets[i], card, scale, progress);
		}
	}

	private void DrawAnimatedTargetCard(Graphics graphics, EntitySnapshot target, RectangleF targetBounds, float scale, float progress)
	{
		if (progress >= 0.999f)
		{
			DrawTargetCard(graphics, target, targetBounds, scale, compact: true);
			return;
		}
		float animatedScale = 0.9f + 0.1f * progress;
		float width = targetBounds.Width * animatedScale;
		float height = targetBounds.Height * animatedScale;
		RectangleF animatedBounds = new RectangleF(
			targetBounds.Left + (targetBounds.Width - width) / 2f,
			targetBounds.Top + (1f - progress) * 10f * scale + (targetBounds.Height - height) / 2f,
			width,
			height);
		DrawTargetCard(graphics, target, animatedBounds, scale * animatedScale, compact: true, opacity: progress);
	}

	private void DrawTargetCard(Graphics graphics, EntitySnapshot target, RectangleF panel, float scale, bool compact, float opacity = 1f)
	{
		opacity = Math.Clamp(opacity, 0f, 1f);
		if (config.TargetHudShowBackground)
		{
			using SolidBrush background = new SolidBrush(ApplyOpacity(WithOpacity(config.TargetHudBackgroundColorArgb, config.TargetHudBackgroundOpacityPercent), opacity));
			using GraphicsPath panelPath = CreateRoundedRectanglePath(panel, config.TargetHudCornerRadius * scale);
			graphics.FillPath(background, panelPath);
			Color outlineColor = ApplyOpacity(Color.FromArgb(180, Color.FromArgb(config.TargetHudAccentColorArgb)), opacity);
			using Pen outline = new Pen(outlineColor, Math.Max(1f, scale));
			graphics.DrawPath(outline, panelPath);
		}
		Color accent = ApplyOpacity(Color.FromArgb(config.TargetHudAccentColorArgb), opacity);
		using Font nameFont = new Font("Segoe UI Semibold", (compact ? 10f : 11f) * scale, FontStyle.Bold, GraphicsUnit.Point);
		using Font detailFont = new Font("Segoe UI", (compact ? 7.2f : 8.5f) * scale, FontStyle.Regular, GraphicsUnit.Point);
		using SolidBrush text = new SolidBrush(ApplyOpacity(Color.FromArgb(config.TargetHudTextColorArgb), opacity));
		float padding = (compact ? 8f : 12f) * scale;
		RectangleF nameBounds = new RectangleF(panel.Left + padding, panel.Top + (compact ? 5f : 9f) * scale, panel.Width - padding * 2f, nameFont.GetHeight(graphics) + 2f);
		using StringFormat ellipsis = new StringFormat { Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap };
		graphics.DrawString(target.Name, nameFont, text, nameBounds, ellipsis);
		string detail = config.TargetHudEnglish
			? $"Health {target.Health:0.#}/{Math.Max(target.MaxHealth, target.Health):0.#}"
			: $"生命 {target.Health:0.#}/{Math.Max(target.MaxHealth, target.Health):0.#}";
		if (config.TargetHudShowDistance) detail += config.TargetHudEnglish ? $"   Distance {target.Distance:0.0}m" : $"   距离 {target.Distance:0.0}m";
		RectangleF detailBounds = new RectangleF(panel.Left + padding, panel.Top + (compact ? 24f : 35f) * scale, panel.Width - padding * 2f, detailFont.GetHeight(graphics) + 2f);
		graphics.DrawString(detail, detailFont, text, detailBounds, ellipsis);
		float healthRatio = target.MaxHealth > 0f ? Math.Clamp(target.Health / target.MaxHealth, 0f, 1f) : 0f;
		RectangleF healthBackground = new RectangleF(panel.Left + padding, panel.Top + (compact ? 43f : 58f) * scale, panel.Width - padding * 2f, (compact ? 4f : 6f) * scale);
		using SolidBrush health = new SolidBrush(accent);
		if (config.TargetHudShowBackground)
		{
			using SolidBrush healthBack = new SolidBrush(Color.FromArgb((int)(120f * opacity), 0, 0, 0));
			graphics.FillRectangle(healthBack, healthBackground);
		}
		graphics.FillRectangle(health, healthBackground.Left, healthBackground.Top, healthBackground.Width * healthRatio, healthBackground.Height);
		if (config.TargetHudShowEquipment && target.Equipment is { HasAny: true })
		{
			DrawEquipmentSlots(graphics, target.Equipment, panel.Left + padding, panel.Top + (compact ? 54f : 72f) * scale, compact ? scale * 0.8f : scale, compact: true, config.TargetHudShowBackground, opacity);
		}
	}

	private EntitySnapshot? SelectHudTarget(GameSnapshot snapshot)
	{
		long now = Environment.TickCount64;
		EntitySnapshot? aim = autoAimTarget;
		if (aim != null && now - autoAimTargetUpdatedAtMs <= 350L)
		{
			EntitySnapshot? current = snapshot.Entities.FirstOrDefault(entity => entity.EntityId == aim.EntityId);
			if (current != null)
			{
				retainedHudTarget = current;
				retainedHudTargetUntilMs = now + 700L;
				return current;
			}
		}
		float centerX = ClientSize.Width / 2f;
		float centerY = ClientSize.Height / 2f;
		EntitySnapshot? crosshair = snapshot.Entities
			.Where(entity => entity.Kind == EntityKind.Player && entity.Visible)
			.Select(entity => new { Entity = entity, Score = DistanceToRect(centerX, centerY, entity.ScreenBounds) })
			.Where(candidate => candidate.Score <= Math.Max(28f, candidate.Entity.ScreenBounds.Width * 0.8f))
			.OrderBy(candidate => candidate.Score)
			.ThenBy(candidate => candidate.Entity.Distance)
			.Select(candidate => candidate.Entity)
			.FirstOrDefault();
		if (crosshair != null)
		{
			retainedHudTarget = crosshair;
			retainedHudTargetUntilMs = now + 700L;
			return crosshair;
		}
		if (retainedHudTarget != null && now <= retainedHudTargetUntilMs)
		{
			return snapshot.Entities.FirstOrDefault(entity => entity.EntityId == retainedHudTarget.EntityId) ?? retainedHudTarget;
		}
		retainedHudTarget = null;
		return null;
	}

	private static float DistanceToRect(float x, float y, RectangleF rect)
	{
		float dx = Math.Max(rect.Left - x, Math.Max(0f, x - rect.Right));
		float dy = Math.Max(rect.Top - y, Math.Max(0f, y - rect.Bottom));
		return (float)Math.Sqrt(dx * dx + dy * dy);
	}

	private PointF ResolveHudPosition(HudElementLayout layout, SizeF size)
	{
		float anchorX = Math.Clamp(layout.X, 0f, 1f) * ClientSize.Width;
		float x = layout.AnchorRight ? anchorX - size.Width : anchorX;
		float y = Math.Clamp(layout.Y, 0f, 1f) * ClientSize.Height;
		return new PointF(Math.Clamp(x, 0f, Math.Max(0f, ClientSize.Width - size.Width)), Math.Clamp(y, 0f, Math.Max(0f, ClientSize.Height - size.Height)));
	}

	private static Color WithOpacity(int argb, int opacityPercent)
	{
		Color color = Color.FromArgb(argb);
		return Color.FromArgb(Math.Clamp(opacityPercent, 0, 100) * 255 / 100, color.R, color.G, color.B);
	}

	private static Color ApplyOpacity(Color color, float opacity)
	{
		return Color.FromArgb((int)Math.Round(color.A * Math.Clamp(opacity, 0f, 1f)), color.R, color.G, color.B);
	}

	private void DrawEquipmentSlots(Graphics graphics, PlayerEquipmentSnapshot equipment, float centerX, float y, float scale, bool compact, bool showSlotBackground = true, float opacity = 1f)
	{
		EquipmentItemSnapshot?[] slots = equipment.Slots;
		float slotSize = (compact ? 19f : 17f) * scale;
		float gap = 3f * scale;
		float totalWidth = slots.Length * slotSize + (slots.Length - 1) * gap;
		float left = compact ? centerX : centerX - totalWidth / 2f;
		using Font font = new Font("Segoe UI Semibold", Math.Max(5f, 6.5f * scale), FontStyle.Bold, GraphicsUnit.Point);
		for (int i = 0; i < slots.Length; i++)
		{
			EquipmentItemSnapshot? item = slots[i];
			if (item == null) continue;
			float x = left + i * (slotSize + gap);
			Color material = ApplyOpacity(GetEquipmentMaterialColor(item.Material), opacity);
			string label = i switch
			{
				1 => "H",
				2 => "C",
				3 => "L",
				4 => "B",
				_ => item.Kind
			};
			using Pen border = new Pen(material, Math.Max(1f, scale));
			if (showSlotBackground)
			{
				using SolidBrush back = new SolidBrush(ApplyOpacity(Color.FromArgb(config.EspEquipmentBackgroundColorArgb), opacity));
				graphics.FillRectangle(back, x, y, slotSize, slotSize);
			}
			graphics.DrawRectangle(border, x, y, slotSize, slotSize);
			using SolidBrush labelBrush = new SolidBrush(material);
			SizeF textSize = graphics.MeasureString(label, font);
			graphics.DrawString(label, font, labelBrush, x + (slotSize - textSize.Width) / 2f, y + (slotSize - textSize.Height) / 2f - 1f);
			if (config.EspEquipmentDurability && item.MaxDamage > 0)
			{
				using SolidBrush durability = new SolidBrush(Color.FromArgb((int)(230f * opacity), (int)(255 * (1f - item.Durability)), (int)(220 * item.Durability), 50));
				graphics.FillRectangle(durability, x + 2f, y + slotSize - 3f, (slotSize - 4f) * item.Durability, 2f);
			}
		}
	}

	private static GraphicsPath CreateRoundedRectanglePath(RectangleF rectangle, float radius)
	{
		GraphicsPath path = new GraphicsPath();
		float clampedRadius = Math.Clamp(radius, 0f, Math.Min(rectangle.Width, rectangle.Height) / 2f);
		if (clampedRadius < 0.5f)
		{
			path.AddRectangle(rectangle);
			return path;
		}
		float diameter = clampedRadius * 2f;
		path.AddArc(rectangle.Left, rectangle.Top, diameter, diameter, 180f, 90f);
		path.AddArc(rectangle.Right - diameter, rectangle.Top, diameter, diameter, 270f, 90f);
		path.AddArc(rectangle.Right - diameter, rectangle.Bottom - diameter, diameter, diameter, 0f, 90f);
		path.AddArc(rectangle.Left, rectangle.Bottom - diameter, diameter, diameter, 90f, 90f);
		path.CloseFigure();
		return path;
	}

	private static Color GetEquipmentMaterialColor(string material)
	{
		return material switch
		{
			"Diamond" => Color.FromArgb(90, 235, 225),
			"Iron" => Color.FromArgb(220, 224, 228),
			"Gold" => Color.FromArgb(255, 208, 64),
			"Chain" => Color.FromArgb(155, 165, 175),
			"Leather" => Color.FromArgb(174, 105, 68),
			"Stone" => Color.FromArgb(145, 145, 145),
			"Wood" => Color.FromArgb(181, 132, 76),
			_ => Color.White
		};
	}

	private static void DrawCornerBox(Graphics graphics, RectangleF box, Color color)
	{
		float num = Math.Max(5f, box.Width * 0.28f);
		float num2 = Math.Max(5f, box.Height * 0.18f);
		using Pen pen = new Pen(Color.FromArgb(230, 0, 0, 0), 3f);
		using Pen pen2 = new Pen(color, 1f);
		Pen[] array = new Pen[2] { pen, pen2 };
		foreach (Pen pen3 in array)
		{
			graphics.DrawLines(pen3, new PointF[3]
			{
				new PointF(box.Left, box.Top + num2),
				new PointF(box.Left, box.Top),
				new PointF(box.Left + num, box.Top)
			});
			graphics.DrawLines(pen3, new PointF[3]
			{
				new PointF(box.Right - num, box.Top),
				new PointF(box.Right, box.Top),
				new PointF(box.Right, box.Top + num2)
			});
			graphics.DrawLines(pen3, new PointF[3]
			{
				new PointF(box.Left, box.Bottom - num2),
				new PointF(box.Left, box.Bottom),
				new PointF(box.Left + num, box.Bottom)
			});
			graphics.DrawLines(pen3, new PointF[3]
			{
				new PointF(box.Right - num, box.Bottom),
				new PointF(box.Right, box.Bottom),
				new PointF(box.Right, box.Bottom - num2)
			});
		}
	}

	private void DrawHealthBar(Graphics graphics, RectangleF box, float health, float maxHealth)
	{
		float num = ((maxHealth <= 0f) ? 0f : Math.Clamp(health / maxHealth, 0f, 1f));
		float num2 = Math.Clamp(config.EspHealthBarWidth, 2, 12);
		float num3 = Math.Max(2f, box.Height);
		float num4 = config.EspHealthBarOnRight ? box.Right + 4f : box.Left - num2 - 4f;
		float num5 = box.Top;
		graphics.FillRectangle(Brushes.Black, num4 - 1f, num5 - 1f, num2 + 2f, num3 + 2f);
		using SolidBrush brush = new SolidBrush(Color.FromArgb(180, 38, 38, 42));
		graphics.FillRectangle(brush, num4, num5, num2, num3);
		using SolidBrush brush2 = new SolidBrush(Color.FromArgb(config.EspHealthBarColorArgb));
		float num6 = num3 * num;
		graphics.FillRectangle(brush2, num4, num5 + num3 - num6, num2, num6);
	}

	private void DrawOutlinedText(Graphics graphics, string text, float centerX, float top)
	{
		float num = centerX - graphics.MeasureString(text, espTextFont).Width / 2f;
		using SolidBrush brush = new SolidBrush(Color.FromArgb(235, 0, 0, 0));
		using SolidBrush brush2 = new SolidBrush(Color.White);
		graphics.DrawString(text, espTextFont, brush, num - 1f, top);
		graphics.DrawString(text, espTextFont, brush, num + 1f, top);
		graphics.DrawString(text, espTextFont, brush, num, top - 1f);
		graphics.DrawString(text, espTextFont, brush, num, top + 1f);
		graphics.DrawString(text, espTextFont, brush2, num, top);
	}

	internal static bool RestoreMinecraftForeground()
	{
		nint minecraftWindow = FindMinecraftWindow();
		if (minecraftWindow == IntPtr.Zero)
		{
			return false;
		}
		if (NativeMethods.IsIconic(minecraftWindow))
		{
			NativeMethods.ShowWindow(minecraftWindow, 9);
		}
		NativeMethods.BringWindowToTop(minecraftWindow);
		NativeMethods.SetForegroundWindow(minecraftWindow);
		return NativeMethods.GetForegroundWindow() == minecraftWindow;
	}
	private static nint FindMinecraftWindow()
	{
		nint foregroundWindow = NativeMethods.GetForegroundWindow();
		if (IsMinecraftWindow(foregroundWindow))
		{
			return foregroundWindow;
		}
		nint result = IntPtr.Zero;
		long bestArea = 0L;
		NativeMethods.EnumWindows(delegate(nint window, nint _)
		{
			if (!IsMinecraftWindow(window))
			{
				return true;
			}
			long area = 0L;
			if (TryGetClientBounds(window, out var clientBounds))
			{
				area = (long)clientBounds.Width * clientBounds.Height;
			}
			if (area > bestArea)
			{
				bestArea = area;
				result = window;
			}
			return true;
		}, IntPtr.Zero);
		return result;
	}

	private static bool IsMinecraftWindow(nint window)
	{
		if (window == IntPtr.Zero || !NativeMethods.IsWindowVisible(window))
		{
			return false;
		}
		NativeMethods.GetWindowThreadProcessId(window, out var processId);
		if (processId == 0 || processId == (uint)Environment.ProcessId)
		{
			return false;
		}
		string className = GetWindowClassName(window);
		if (className.Length == 0 || IsExcludedWindowClass(className))
		{
			return false;
		}
		// 游戏窗口必须有可用的可视客户区，借此刻意排除 IME / 工具 / 隐藏辅助窗口。
		if (!TryGetClientBounds(window, out var clientBounds) || clientBounds.Width < MinGameClientWidth || clientBounds.Height < MinGameClientHeight)
		{
			return false;
		}
		string processName;
		try
		{
			processName = Process.GetProcessById((int)processId).ProcessName;
		}
		catch
		{
			return false;
		}
		if (!processName.Equals("java", StringComparison.OrdinalIgnoreCase) && !processName.Equals("javaw", StringComparison.OrdinalIgnoreCase))
		{
			return false;
		}
		// 启动器/客户端可以改窗口标题，但 LWJGL 的窗口类名是稳定的：这是最可靠的识别依据。
		if (className.Equals("LWJGL", StringComparison.OrdinalIgnoreCase) || className.Equals("GLFW30", StringComparison.OrdinalIgnoreCase) || className.Equals("GLFW32", StringComparison.OrdinalIgnoreCase))
		{
			return true;
		}
		string windowTitle = GetWindowTitle(window);
		if (windowTitle.Contains("Minecraft", StringComparison.OrdinalIgnoreCase) || windowTitle.Contains("Lunar Client", StringComparison.OrdinalIgnoreCase) || windowTitle.Contains("Forge", StringComparison.OrdinalIgnoreCase))
		{
			return true;
		}
		// 最后兜底：Java 进程确实加载了 LWJGL 才认作游戏窗口，避免误识别其它 Java 应用。
		return JavaProcessLoadsLwjgl(processId);
	}

	private static bool IsExcludedWindowClass(string className)
	{
		return className.Equals("ConsoleWindowClass", StringComparison.OrdinalIgnoreCase) || className.Equals("IME", StringComparison.OrdinalIgnoreCase) || className.Equals("MSCTFIME UI", StringComparison.OrdinalIgnoreCase) || className.Equals("SunAwtToolkit", StringComparison.OrdinalIgnoreCase);
	}

	private static bool JavaProcessLoadsLwjgl(uint processId)
	{
		try
		{
			using Process process = Process.GetProcessById((int)processId);
			foreach (ProcessModule module in process.Modules)
			{
				string? moduleName = module.ModuleName;
				if (moduleName != null && moduleName.StartsWith("lwjgl", StringComparison.OrdinalIgnoreCase))
				{
					return true;
				}
			}
		}
		catch
		{
		}
		return false;
	}

	internal static string GetWindowClassName(nint window)
	{
		StringBuilder stringBuilder = new StringBuilder(256);
		NativeMethods.GetClassName(window, stringBuilder, stringBuilder.Capacity);
		return stringBuilder.ToString();
	}

	internal static string GetWindowTitle(nint window)
	{
		StringBuilder stringBuilder = new StringBuilder(512);
		NativeMethods.GetWindowText(window, stringBuilder, stringBuilder.Capacity);
		return stringBuilder.ToString();
	}

	private static bool TryGetClientBounds(nint window, out Rectangle bounds)
	{
		bounds = Rectangle.Empty;
		if (!NativeMethods.GetClientRect(window, out var rect))
		{
			return false;
		}
		NativeMethods.Point point = new NativeMethods.Point
		{
			X = 0,
			Y = 0
		};
		if (!NativeMethods.ClientToScreen(window, ref point))
		{
			return false;
		}
		bounds = new Rectangle(point.X, point.Y, rect.Right - rect.Left, rect.Bottom - rect.Top);
		if (bounds.Width > 0)
		{
			return bounds.Height > 0;
		}
		return false;
	}

	protected override void Dispose(bool disposing)
	{
		if (disposing)
		{
			windowTimer.Dispose();
			memoryTimer.Dispose();
			snapshotTimer.Dispose();
			renderThreadRunning = false;
			renderThread.Join(800);
			ReleaseLayeredResources();
			memoryReader.Dispose();
			settingsForm.Dispose();
			hudEditorForm?.Dispose();
			autoInputController.Dispose();
			myauBlockHitController.Dispose();
			knockbackController.Dispose();
			chestStealerController.Dispose();
			inventoryManagerController.Dispose();
			autoAimActive = false;
			autoAimThread?.Join(1000);
			bowAimActive = false;
			bowAimThread?.Join(1000);
			espTextFont.Dispose();
			targetListFont?.Dispose();
			targetListHeaderFont?.Dispose();
			petScaled?.Dispose();
			pet.Dispose();
			trayIcon.Visible = false;
			trayIcon.Dispose();
			NativeMethods.TimeEndPeriod(1);
		}
		base.Dispose(disposing);
	}
}
