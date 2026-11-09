using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Text;

namespace McPetOverlay;

internal sealed class JvmEspDataSource : IEspDataSource
{
	private readonly record struct EntityTransform(double X, double Y, double Z, float Width, float Height);

	private readonly record struct RenderOrigin(double X, double Y, double Z)
	{
		internal bool IsValid
		{
			get
			{
				if (double.IsFinite(X) && double.IsFinite(Y))
				{
					return double.IsFinite(Z);
				}
				return false;
			}
		}
	}

	private readonly record struct RenderMatrices(float[] ModelView, float[] Projection, int[] Viewport);

	private readonly record struct EntityClassInfo(EntityKind? Kind, bool IsFireball, string ClassName);

	private readonly record struct TimedHealth(float Value, long ReadAtMs);

	private readonly record struct TimedEquipment(PlayerEquipmentSnapshot Value, long ReadAtMs);

	private readonly record struct CachedWorldEntity(long EntityId, EntityKind Kind, string Name, EntityTransform Transform, double MotionX, double MotionY, double MotionZ, float Health, float MaxHealth, int NameColorCode, PlayerEquipmentSnapshot Equipment);

	private readonly record struct CachedFireball(long EntityId, string Name, EntityTransform Transform, double MotionX, double MotionY, double MotionZ, double AccelerationX, double AccelerationY, double AccelerationZ);

	private readonly record struct WorldPoint(double X, double Y, double Z);

	private sealed class FireballTrailState
	{
		internal List<WorldPoint> Points { get; } = new List<WorldPoint>();

		internal long LastSeenMs { get; set; }
	}

	private readonly record struct BlockCoordinate(int X, int Y, int Z);

	private readonly record struct CachedBlockBox(double MinX, double MinY, double MinZ, double MaxX, double MaxY, double MaxZ, bool IsObsidian);

	private readonly MinecraftMemoryReader memory;

	private readonly Func<Size> viewportProvider;

	private readonly JvmClassLayout minecraftLayout;

	private readonly JvmClassLayout worldLayout;

	private readonly JvmClassLayout worldClientLayout;

	private readonly JvmClassLayout chunkProviderClientLayout;

	private readonly JvmClassLayout longHashMapLayout;

	private readonly JvmClassLayout longHashMapEntryLayout;

	private readonly JvmClassLayout chunkLayout;

	private readonly JvmClassLayout extendedBlockStorageLayout;

	private readonly JvmClassLayout entityLayout;

	private readonly JvmClassLayout livingLayout;

	private readonly JvmClassLayout playerLayout;

	private readonly JvmClassLayout fireballLayout;

	private readonly JvmClassLayout inventoryPlayerLayout;

	private readonly JvmClassLayout itemStackLayout;

	private readonly JvmClassLayout itemLayout;
	private readonly JvmClassLayout itemSwordLayout;
	private readonly JvmClassLayout itemArmorLayout;
	private readonly JvmClassLayout itemToolLayout;

	private readonly JvmClassLayout dataWatcherLayout;

	private readonly JvmClassLayout watchableObjectLayout;

	private readonly JvmClassLayout arrayListLayout;

	private readonly JvmClassLayout hashMapLayout;

	private readonly JvmClassLayout hashMapNodeLayout;

	private readonly JvmClassLayout floatLayout;

	private readonly JvmClassLayout integerLayout;

	private readonly JvmClassLayout stringLayout;

	private readonly JvmClassLayout gameProfileLayout;

	private readonly JvmClassLayout scoreboardLayout;

	private readonly JvmClassLayout scorePlayerTeamLayout;

	private readonly JvmClassLayout renderManagerLayout;

	private readonly JvmClassLayout timerLayout;

	private readonly JvmClassLayout activeRenderInfoLayout;

	private readonly JvmClassLayout bufferLayout;

	private readonly JvmClassLayout movingObjectPositionLayout;

	private readonly JvmClassLayout blockPosLayout;

	private readonly JvmClassLayout vec3Layout;

	private readonly JvmClassLayout enumLayout;

	private readonly JvmClassLayout gameSettingsLayout;

	private readonly JvmClassLayout keyBindingLayout;

	private readonly JvmClassLayout guiChestLayout;
	private readonly JvmClassLayout guiInventoryLayout;

	private readonly JvmClassLayout guiScreenLayout;

	private readonly JvmClassLayout guiContainerLayout;

	private readonly JvmClassLayout inventoryBasicLayout;

	private readonly JvmClassLayout inventoryLargeChestLayout;

	private readonly JvmClassLayout tileEntityChestLayout;

	private readonly JvmClassLayout inventoryEnderChestLayout;
	private readonly JvmClassLayout nbtCompoundLayout;
	private readonly JvmClassLayout nbtListLayout;
	private readonly JvmClassLayout nbtShortLayout;
	private readonly JvmClassLayout nbtStringLayout;

	private readonly MinecraftRuntimePointers runtime;

	private readonly long klassNameOffset;

	private readonly long symbolLengthOffset;

	private readonly long symbolBodyOffset;

	private readonly long klassSuperOffset;

	private readonly long klassMirrorOffset;

	private readonly bool klassMirrorIsOopHandle;

	private readonly Dictionary<long, EntityClassInfo> entityClassCache = new Dictionary<long, EntityClassInfo>();

	private readonly Dictionary<long, string> playerNameCache = new Dictionary<long, string>();

	private readonly Dictionary<long, TimedHealth> healthCache = new Dictionary<long, TimedHealth>();

	private readonly Dictionary<long, TimedEquipment> equipmentCache = new Dictionary<long, TimedEquipment>();

	private readonly Dictionary<string, int> playerNameColorCache = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

	private readonly Stopwatch cacheClock = Stopwatch.StartNew();

	private readonly List<CachedWorldEntity> cachedWorldEntities = new List<CachedWorldEntity>();

	private readonly List<CachedFireball> cachedFireballs = new List<CachedFireball>();

	private readonly Dictionary<long, FireballTrailState> fireballTrails = new Dictionary<long, FireballTrailState>();

	private readonly List<CachedBlockBox> cachedBedBoxes = new List<CachedBlockBox>();

	private long nextEntityScanMs;

	private long nextTeamScanMs;

	private long nextBedScanMs;

	private int cachedBedRange;

	private bool cachedBedObsidian;

	private bool cachedBedFullHeight;

	private int cachedBedCount;

	private bool bedScanFaulted;

	private string bedScanDiagnostic = string.Empty;

	private int localPlayerNameColorCode = -1;

	private long activeWorldAddress;

	private long activePlayerAddress;

	public bool IsLocalPlayerHurt { get; private set; }

	public int HurtTime { get; private set; }

	public bool IsInWorld { get; private set; }

	public bool IsSnapshotValid { get; private set; }

	public bool IsContainerOpen { get; private set; }

	internal bool ChestLayoutsReady => guiChestLayout.KlassAddress != IntPtr.Zero;
	internal bool InventoryLayoutsReady => guiInventoryLayout.KlassAddress != IntPtr.Zero && inventoryPlayerLayout.KlassAddress != IntPtr.Zero;

	internal string LastSnapshotDiagnostic { get; private set; } = string.Empty;

	internal JvmEspDataSource(MinecraftMemoryReader memory, HotSpotDiagnostic diagnostic, Func<Size> viewportProvider)
	{
		this.memory = memory;
		this.viewportProvider = viewportProvider;
		runtime = diagnostic.RuntimePointers;
		minecraftLayout = FindLayout(diagnostic, "net/minecraft/client/Minecraft");
		worldLayout = FindLayout(diagnostic, "net/minecraft/world/World");
		worldClientLayout = FindLayout(diagnostic, "net/minecraft/client/multiplayer/WorldClient");
		chunkProviderClientLayout = FindLayout(diagnostic, "net/minecraft/client/multiplayer/ChunkProviderClient");
		longHashMapLayout = FindLayout(diagnostic, "net/minecraft/util/LongHashMap");
		longHashMapEntryLayout = FindLayout(diagnostic, "net/minecraft/util/LongHashMap$Entry");
		chunkLayout = FindLayout(diagnostic, "net/minecraft/world/chunk/Chunk");
		extendedBlockStorageLayout = FindLayout(diagnostic, "net/minecraft/world/chunk/storage/ExtendedBlockStorage");
		entityLayout = FindLayout(diagnostic, "net/minecraft/entity/Entity");
		livingLayout = FindLayout(diagnostic, "net/minecraft/entity/EntityLivingBase");
		playerLayout = FindLayout(diagnostic, "net/minecraft/entity/player/EntityPlayer");
		fireballLayout = FindLayout(diagnostic, "net/minecraft/entity/projectile/EntityFireball");
		inventoryPlayerLayout = FindLayout(diagnostic, "net/minecraft/entity/player/InventoryPlayer");
		itemStackLayout = FindLayout(diagnostic, "net/minecraft/item/ItemStack");
		itemLayout = FindLayout(diagnostic, "net/minecraft/item/Item");
		itemSwordLayout = FindLayout(diagnostic, "net/minecraft/item/ItemSword");
		itemArmorLayout = FindLayout(diagnostic, "net/minecraft/item/ItemArmor");
		itemToolLayout = FindLayout(diagnostic, "net/minecraft/item/ItemTool");
		dataWatcherLayout = FindLayout(diagnostic, "net/minecraft/entity/DataWatcher");
		watchableObjectLayout = FindLayout(diagnostic, "net/minecraft/entity/DataWatcher$WatchableObject");
		arrayListLayout = FindLayout(diagnostic, "java/util/ArrayList");
		hashMapLayout = FindLayout(diagnostic, "java/util/HashMap");
		hashMapNodeLayout = FindLayout(diagnostic, "java/util/HashMap$Node");
		floatLayout = FindLayout(diagnostic, "java/lang/Float");
		integerLayout = FindLayout(diagnostic, "java/lang/Integer");
		stringLayout = FindLayout(diagnostic, "java/lang/String");
		gameProfileLayout = FindLayout(diagnostic, "com/mojang/authlib/GameProfile");
		scoreboardLayout = FindLayout(diagnostic, "net/minecraft/scoreboard/Scoreboard");
		scorePlayerTeamLayout = FindLayout(diagnostic, "net/minecraft/scoreboard/ScorePlayerTeam");
		renderManagerLayout = FindLayout(diagnostic, "net/minecraft/client/renderer/entity/RenderManager");
		timerLayout = FindLayout(diagnostic, "net/minecraft/util/Timer");
		activeRenderInfoLayout = FindLayout(diagnostic, "net/minecraft/client/renderer/ActiveRenderInfo");
		bufferLayout = FindLayout(diagnostic, "java/nio/Buffer");
		movingObjectPositionLayout = FindLayout(diagnostic, "net/minecraft/util/MovingObjectPosition");
		blockPosLayout = FindLayout(diagnostic, "net/minecraft/util/BlockPos");
		vec3Layout = FindLayout(diagnostic, "net/minecraft/util/Vec3");
		enumLayout = FindLayout(diagnostic, "java/lang/Enum");
		gameSettingsLayout = FindLayout(diagnostic, "net/minecraft/client/settings/GameSettings");
		keyBindingLayout = FindLayout(diagnostic, "net/minecraft/client/settings/KeyBinding");
		guiChestLayout = FindLayout(diagnostic, "net/minecraft/client/gui/inventory/GuiChest");
		guiInventoryLayout = FindLayout(diagnostic, "net/minecraft/client/gui/inventory/GuiInventory");
		guiScreenLayout = FindLayout(diagnostic, "net/minecraft/client/gui/GuiScreen");
		guiContainerLayout = FindLayout(diagnostic, "net/minecraft/client/gui/inventory/GuiContainer");
		inventoryBasicLayout = FindLayout(diagnostic, "net/minecraft/inventory/InventoryBasic");
		inventoryLargeChestLayout = FindLayout(diagnostic, "net/minecraft/inventory/InventoryLargeChest");
		tileEntityChestLayout = FindLayout(diagnostic, "net/minecraft/tileentity/TileEntityChest");
		inventoryEnderChestLayout = FindLayout(diagnostic, "net/minecraft/entity/player/InventoryEnderChest");
		nbtCompoundLayout = FindLayout(diagnostic, "net/minecraft/nbt/NBTTagCompound");
		nbtListLayout = FindLayout(diagnostic, "net/minecraft/nbt/NBTTagList");
		nbtShortLayout = FindLayout(diagnostic, "net/minecraft/nbt/NBTTagShort");
		nbtStringLayout = FindLayout(diagnostic, "net/minecraft/nbt/NBTTagString");
		klassNameOffset = FindVmOffset(diagnostic, "Klass", "_name", 16L);
		symbolLengthOffset = FindVmOffset(diagnostic, "Symbol", "_length", 0L);
		symbolBodyOffset = FindVmOffset(diagnostic, "Symbol", "_body", 8L);
		klassSuperOffset = FindVmOffset(diagnostic, "Klass", "_super", 112L);
		klassMirrorOffset = FindVmOffset(diagnostic, "Klass", "_java_mirror", 104L);
		klassMirrorIsOopHandle = diagnostic.KeyFields.Any((VmStructField field) => field.TypeName == "Klass" && field.FieldName == "_java_mirror" && field.TypeString.Contains("OopHandle", StringComparison.Ordinal));
	}

	public GameSnapshot GetSnapshot()
	{
		try
		{
			IsLocalPlayerHurt = false;
			IsInWorld = false;
			IsSnapshotValid = false;
			IsContainerOpen = false;
			LastSnapshotDiagnostic = string.Empty;
			Size viewport = viewportProvider();
			if (viewport.Width < 100 || viewport.Height < 100)
			{
				LastSnapshotDiagnostic = $"viewport={viewport.Width}x{viewport.Height}";
				return GameSnapshot.Empty;
			}
			nint num = ReadMinecraftInstance();
			if (num == IntPtr.Zero)
			{
				LastSnapshotDiagnostic = "minecraft=zero";
				ResetWorldState();
				return GameSnapshot.Empty;
			}
			IsContainerOpen = IsContainerScreen(ReadObjectField(num, minecraftLayout, "field_71462_r"));
			nint num2 = ReadObjectField(num, minecraftLayout, "field_71441_e");
			nint num3 = ReadObjectField(num, minecraftLayout, "field_71439_g");
			if (num2 == IntPtr.Zero || num3 == IntPtr.Zero)
			{
				LastSnapshotDiagnostic = $"world=0x{((IntPtr)num2).ToInt64():X} player=0x{((IntPtr)num3).ToInt64():X}";
				ResetWorldState();
				return GameSnapshot.Empty;
			}
			long worldAddress = ((IntPtr)num2).ToInt64();
			long playerAddress = ((IntPtr)num3).ToInt64();
			if (activeWorldAddress != worldAddress || activePlayerAddress != playerAddress)
			{
				ResetWorldState();
				activeWorldAddress = worldAddress;
				activePlayerAddress = playerAddress;
			}
			IsInWorld = true;
			int hurtTime = ReadIntField(num3, livingLayout, "field_70737_aN");
			IsLocalPlayerHurt = hurtTime > 0;
			HurtTime = hurtTime;
			nint num4 = ReadObjectField(num, minecraftLayout, "field_175616_W");
			nint owner = ReadObjectField(num, minecraftLayout, "field_71428_T");
			if (num4 == IntPtr.Zero)
			{
				LastSnapshotDiagnostic = "renderManager=zero";
				cachedWorldEntities.Clear();
				return GameSnapshot.Empty;
			}
			float partialTicks = Math.Clamp(ReadFloatField(owner, timerLayout, "field_74281_c"), 0f, 1f);
			RenderOrigin renderOrigin = new RenderOrigin(ReadDoubleField(num4, renderManagerLayout, "field_78730_l"), ReadDoubleField(num4, renderManagerLayout, "field_78731_m"), ReadDoubleField(num4, renderManagerLayout, "field_78728_n"));
			if (!renderOrigin.IsValid || !TryReadRenderMatrices(out var matrices))
			{
				LastSnapshotDiagnostic = $"renderOrigin={renderOrigin.IsValid} matrices=invalid";
				cachedWorldEntities.Clear();
				return GameSnapshot.Empty;
			}
			long elapsedMilliseconds = cacheClock.ElapsedMilliseconds;
			if (elapsedMilliseconds >= nextEntityScanMs)
			{
				nextEntityScanMs = elapsedMilliseconds + 33;
				RefreshWorldEntities(num2, num3, partialTicks);
			}
			List<EntitySnapshot> list = new List<EntitySnapshot>(cachedWorldEntities.Count);
			foreach (CachedWorldEntity cachedWorldEntity in cachedWorldEntities)
			{
				EntityTransform transform = cachedWorldEntity.Transform;
				double num5 = transform.X - renderOrigin.X;
				double num6 = transform.Y - renderOrigin.Y;
				double num7 = transform.Z - renderOrigin.Z;
				float distance = (float)Math.Sqrt(num5 * num5 + num6 * num6 + num7 * num7);
				if (ProjectBounds(transform, renderOrigin, matrices, viewport, out var bounds))
				{
					list.Add(new EntitySnapshot(cachedWorldEntity.Kind, cachedWorldEntity.Name, bounds, cachedWorldEntity.Health, cachedWorldEntity.MaxHealth, distance, Visible: true, cachedWorldEntity.EntityId, cachedWorldEntity.NameColorCode, cachedWorldEntity.Equipment, transform.X, transform.Y, transform.Z, cachedWorldEntity.MotionX, cachedWorldEntity.MotionY, cachedWorldEntity.MotionZ, transform.Width, transform.Height));
				}
				else
				{
					list.Add(new EntitySnapshot(cachedWorldEntity.Kind, cachedWorldEntity.Name, RectangleF.Empty, cachedWorldEntity.Health, cachedWorldEntity.MaxHealth, distance, Visible: false, cachedWorldEntity.EntityId, cachedWorldEntity.NameColorCode, cachedWorldEntity.Equipment, transform.X, transform.Y, transform.Z, cachedWorldEntity.MotionX, cachedWorldEntity.MotionY, cachedWorldEntity.MotionZ, transform.Width, transform.Height));
				}
			}
			IsSnapshotValid = true;
			LastSnapshotDiagnostic = $"viewport={viewport.Width}x{viewport.Height} worldEntities={cachedWorldEntities.Count} projected={list.Count} renderOrigin={renderOrigin.IsValid}";
			float horizontalProjectionScale = Math.Abs(matrices.Projection[0]) * viewport.Width * 0.5f;
			float verticalProjectionScale = Math.Abs(matrices.Projection[5]) * viewport.Height * 0.5f;
			return new GameSnapshot(list, localPlayerNameColorCode, horizontalProjectionScale, verticalProjectionScale);
		}
		catch
		{
			LastSnapshotDiagnostic = "exception";
			ResetWorldState();
			return GameSnapshot.Empty;
		}
	}

	public BedEspSnapshot GetBedSnapshot(int range, bool includeObsidian, bool fullHeight)
	{
		range = Math.Clamp(range, 8, 48);
		try
		{
			Size viewport = viewportProvider();
			nint minecraft = ReadMinecraftInstance();
			nint world = ReadObjectField(minecraft, minecraftLayout, "field_71441_e");
			nint player = ReadObjectField(minecraft, minecraftLayout, "field_71439_g");
			nint renderManager = ReadObjectField(minecraft, minecraftLayout, "field_175616_W");
			if (viewport.Width < 1 || viewport.Height < 1 || world == IntPtr.Zero || player == IntPtr.Zero || renderManager == IntPtr.Zero)
			{
				return new BedEspSnapshot(Array.Empty<ProjectedBoxSnapshot>(), Array.Empty<ProjectedBoxSnapshot>(), 0, Diagnostic: "world/player/render manager unavailable");
			}
			long elapsedMilliseconds = cacheClock.ElapsedMilliseconds;
			if (elapsedMilliseconds >= nextBedScanMs || range != cachedBedRange || includeObsidian != cachedBedObsidian || fullHeight != cachedBedFullHeight)
			{
				try
				{
					RefreshBedBoxes(world, player, range, includeObsidian, fullHeight);
					bedScanFaulted = false;
					nextBedScanMs = elapsedMilliseconds + 750;
				}
				catch (Exception ex)
				{
					cachedBedBoxes.Clear();
					cachedBedCount = 0;
					bedScanFaulted = true;
					bedScanDiagnostic = ex.GetType().Name + ": " + ex.Message;
					nextBedScanMs = elapsedMilliseconds + 5000;
					AppLog.Write($"[BED-ESP-ERROR] {ex}");
				}
			}
			if (bedScanFaulted)
			{
				return new BedEspSnapshot(Array.Empty<ProjectedBoxSnapshot>(), Array.Empty<ProjectedBoxSnapshot>(), 0, Faulted: true, bedScanDiagnostic);
			}
			RenderOrigin renderOrigin = new RenderOrigin(ReadDoubleField(renderManager, renderManagerLayout, "field_78730_l"), ReadDoubleField(renderManager, renderManagerLayout, "field_78731_m"), ReadDoubleField(renderManager, renderManagerLayout, "field_78728_n"));
			if (!renderOrigin.IsValid || !TryReadRenderMatrices(out RenderMatrices matrices))
			{
				return new BedEspSnapshot(Array.Empty<ProjectedBoxSnapshot>(), Array.Empty<ProjectedBoxSnapshot>(), cachedBedCount, Diagnostic: "render matrices unavailable");
			}
			List<ProjectedBoxSnapshot> beds = new List<ProjectedBoxSnapshot>();
			List<ProjectedBoxSnapshot> obsidian = new List<ProjectedBoxSnapshot>();
			foreach (CachedBlockBox box in cachedBedBoxes)
			{
				if (!ProjectBox(box, renderOrigin, matrices, viewport, out PointF[] corners))
				{
					continue;
				}
				double centerX = (box.MinX + box.MaxX) * 0.5 - renderOrigin.X;
				double centerY = (box.MinY + box.MaxY) * 0.5 - renderOrigin.Y;
				double centerZ = (box.MinZ + box.MaxZ) * 0.5 - renderOrigin.Z;
				ProjectedBoxSnapshot projected = new ProjectedBoxSnapshot(corners, (float)Math.Sqrt(centerX * centerX + centerY * centerY + centerZ * centerZ));
				(box.IsObsidian ? obsidian : beds).Add(projected);
			}
			return new BedEspSnapshot(beds, obsidian, cachedBedCount, Diagnostic: bedScanDiagnostic);
		}
		catch (Exception ex)
		{
			AppLog.Write($"[BED-ESP-ERROR] snapshot failure: {ex}");
			return new BedEspSnapshot(Array.Empty<ProjectedBoxSnapshot>(), Array.Empty<ProjectedBoxSnapshot>(), 0, Faulted: true, ex.GetType().Name + ": " + ex.Message);
		}
	}

	public FireballPredictionSnapshot GetFireballPredictionSnapshot(float range, int ticks, int maxEntities, float thickness, bool trajectoryEnabled)
	{
		range = Math.Clamp(range, 8f, 256f);
		ticks = Math.Clamp(ticks, 10, 120);
		maxEntities = Math.Clamp(maxEntities, 1, 16);
		thickness = Math.Clamp(thickness, 1f, 10f);
		try
		{
			Size viewport = viewportProvider();
			nint minecraft = ReadMinecraftInstance();
			nint world = ReadObjectField(minecraft, minecraftLayout, "field_71441_e");
			nint player = ReadObjectField(minecraft, minecraftLayout, "field_71439_g");
			nint renderManager = ReadObjectField(minecraft, minecraftLayout, "field_175616_W");
			if (viewport.Width < 1 || viewport.Height < 1 || world == IntPtr.Zero || player == IntPtr.Zero || renderManager == IntPtr.Zero)
			{
				return FireballPredictionSnapshot.Empty;
			}
			RenderOrigin renderOrigin = new RenderOrigin(ReadDoubleField(renderManager, renderManagerLayout, "field_78730_l"), ReadDoubleField(renderManager, renderManagerLayout, "field_78731_m"), ReadDoubleField(renderManager, renderManagerLayout, "field_78728_n"));
			if (!renderOrigin.IsValid || !TryReadRenderMatrices(out RenderMatrices matrices))
			{
				return new FireballPredictionSnapshot(Array.Empty<FireballSnapshot>(), Diagnostic: "render matrices unavailable");
			}
			double playerX = ReadDoubleField(player, entityLayout, "field_70165_t");
			double playerY = ReadDoubleField(player, entityLayout, "field_70163_u");
			double playerZ = ReadDoubleField(player, entityLayout, "field_70161_v");
			List<FireballSnapshot> snapshots = new List<FireballSnapshot>();
			bool heldPreview = trajectoryEnabled && IsHoldingItemClass("net/minecraft/item/ItemFireball") == true;
			if (heldPreview)
			{
				float yaw = ReadFloatField(player, entityLayout, "field_70177_z");
				float pitch = ReadFloatField(player, entityLayout, "field_70125_A");
				if (float.IsFinite(yaw) && float.IsFinite(pitch))
				{
					double yawRadians = yaw * Math.PI / 180.0;
					double pitchRadians = pitch * Math.PI / 180.0;
					double directionX = -Math.Sin(yawRadians) * Math.Cos(pitchRadians);
					double directionY = -Math.Sin(pitchRadians);
					double directionZ = Math.Cos(yawRadians) * Math.Cos(pitchRadians);
					double startX = playerX;
					double startY = playerY + 1.62;
					double startZ = playerZ;
					List<WorldPoint> previewTrajectory = SimulateHeldFireballRay(world, range, startX, startY, startZ, directionX, directionY, directionZ, out bool previewCollision);
					ProjectedFireballTrajectory projectedPreview = ProjectWorldTrajectory(previewTrajectory, renderOrigin, matrices, viewport, includeCrosshair: true, thickness);
					snapshots.Add(new FireballSnapshot(0L, "Held Fireball", RectangleF.Empty, 0f, projectedPreview.Segments, projectedPreview.Faces, previewCollision));
				}
			}
			foreach (CachedFireball fireball in cachedFireballs
				.OrderBy(item => DistanceSquared(item.Transform.X, item.Transform.Y, item.Transform.Z, playerX, playerY, playerZ))
				.Take(maxEntities))
			{
				double distanceSquared = DistanceSquared(fireball.Transform.X, fireball.Transform.Y, fireball.Transform.Z, playerX, playerY, playerZ);
				if (!double.IsFinite(distanceSquared) || distanceSquared > range * range)
				{
					continue;
				}
				ProjectBounds(fireball.Transform, renderOrigin, matrices, viewport, out RectangleF bounds);
				double x = fireball.Transform.X;
				double y = fireball.Transform.Y + fireball.Transform.Height * 0.5;
				double z = fireball.Transform.Z;
				bool collisionPredicted = false;
				ProjectedFireballTrajectory projectedTrajectory = new ProjectedFireballTrajectory(Array.Empty<IReadOnlyList<PointF>>(), Array.Empty<PointF[]>());
				if (trajectoryEnabled)
				{
					List<WorldPoint> trajectory = fireballTrails.TryGetValue(fireball.EntityId, out FireballTrailState? trail) ? new List<WorldPoint>(trail.Points) : new List<WorldPoint>();
					if (trajectory.Count == 0) trajectory.Add(new WorldPoint(x, y, z));
					List<WorldPoint> future = SimulateFireballTrajectory(world, ticks, x, y, z, fireball.MotionX, fireball.MotionY, fireball.MotionZ, fireball.AccelerationX, fireball.AccelerationY, fireball.AccelerationZ, out collisionPredicted);
					if (future.Count > 1) trajectory.AddRange(future.Skip(1));
					projectedTrajectory = ProjectWorldTrajectory(trajectory, renderOrigin, matrices, viewport, includeCrosshair: false, thickness);
				}
				snapshots.Add(new FireballSnapshot(fireball.EntityId, fireball.Name, bounds, (float)Math.Sqrt(distanceSquared), projectedTrajectory.Segments, projectedTrajectory.Faces, collisionPredicted));
			}
			return new FireballPredictionSnapshot(snapshots, Diagnostic: $"cached={cachedFireballs.Count} projected={snapshots.Count} heldPreview={heldPreview}");
		}
		catch (Exception ex)
		{
			return new FireballPredictionSnapshot(Array.Empty<FireballSnapshot>(), Faulted: true, ex.GetType().Name + ": " + ex.Message);
		}
	}

	private List<WorldPoint> SimulateHeldFireballRay(nint world, double maxDistance, double startX, double startY, double startZ, double directionX, double directionY, double directionZ, out bool collisionPredicted)
	{
		List<WorldPoint> trajectory = new List<WorldPoint>((int)Math.Ceiling(maxDistance) + 2)
		{
			new WorldPoint(startX + directionX * 0.25, startY + directionY * 0.25, startZ + directionZ * 0.25)
		};
		collisionPredicted = false;
		int lastBlockX = FloorToInt(startX);
		int lastBlockY = FloorToInt(startY);
		int lastBlockZ = FloorToInt(startZ);
		double lastRecordedDistance = 0.0;
		for (double distance = 0.25; distance <= maxDistance; distance += 0.25)
		{
			double x = startX + directionX * distance;
			double y = startY + directionY * distance;
			double z = startZ + directionZ * distance;
			int blockX = FloorToInt(x);
			int blockY = FloorToInt(y);
			int blockZ = FloorToInt(z);
			bool enteredNewBlock = blockX != lastBlockX || blockY != lastBlockY || blockZ != lastBlockZ;
			if (enteredNewBlock)
			{
				lastBlockX = blockX;
				lastBlockY = blockY;
				lastBlockZ = blockZ;
				if (blockY is < 0 or >= 256 || (TryReadPackedBlockState(world, blockX, blockY, blockZ, out ushort packedState) && packedState >> 4 != 0))
				{
					collisionPredicted = true;
					trajectory.Add(new WorldPoint(x, y, z));
					break;
				}
			}
			if (distance - lastRecordedDistance >= 0.75 || distance + 0.25 > maxDistance)
			{
				trajectory.Add(new WorldPoint(x, y, z));
				lastRecordedDistance = distance;
			}
		}
		return trajectory;
	}

	private List<WorldPoint> SimulateFireballTrajectory(nint world, int ticks, double x, double y, double z, double motionX, double motionY, double motionZ, double accelerationX, double accelerationY, double accelerationZ, out bool collisionPredicted)
	{
		List<WorldPoint> trajectory = new List<WorldPoint>(ticks + 1)
		{
			new WorldPoint(x, y, z)
		};
		collisionPredicted = false;
		int lastBlockX = int.MinValue;
		int lastBlockY = int.MinValue;
		int lastBlockZ = int.MinValue;
		int collisionChecks = 0;
		for (int tick = 0; tick < ticks; tick++)
		{
			double nextX = x + motionX;
			double nextY = y + motionY;
			double nextZ = z + motionZ;
			int substeps = Math.Clamp((int)Math.Ceiling(Math.Sqrt(motionX * motionX + motionY * motionY + motionZ * motionZ) * 2.0), 1, 8);
			for (int step = 1; step <= substeps; step++)
			{
				double t = step / (double)substeps;
				int blockX = FloorToInt(x + (nextX - x) * t);
				int blockY = FloorToInt(y + (nextY - y) * t);
				int blockZ = FloorToInt(z + (nextZ - z) * t);
				if (blockX == lastBlockX && blockY == lastBlockY && blockZ == lastBlockZ) continue;
				lastBlockX = blockX;
				lastBlockY = blockY;
				lastBlockZ = blockZ;
				if (collisionChecks++ < 128 && blockY is >= 0 and < 256 && TryReadPackedBlockState(world, blockX, blockY, blockZ, out ushort packedState) && packedState >> 4 != 0)
				{
					collisionPredicted = true;
					nextX = x + (nextX - x) * t;
					nextY = y + (nextY - y) * t;
					nextZ = z + (nextZ - z) * t;
					break;
				}
			}
			x = nextX;
			y = nextY;
			z = nextZ;
			trajectory.Add(new WorldPoint(x, y, z));
			if (collisionPredicted) break;
			motionX = (motionX + accelerationX) * 0.95;
			motionY = (motionY + accelerationY) * 0.95;
			motionZ = (motionZ + accelerationZ) * 0.95;
		}
		return trajectory;
	}

	private readonly record struct ProjectedFireballTrajectory(IReadOnlyList<IReadOnlyList<PointF>> Segments, IReadOnlyList<PointF[]> Faces);

	private static ProjectedFireballTrajectory ProjectWorldTrajectory(IReadOnlyList<WorldPoint> worldPoints, RenderOrigin renderOrigin, RenderMatrices matrices, Size viewport, bool includeCrosshair, float thickness)
	{
		List<IReadOnlyList<PointF>> segments = new List<IReadOnlyList<PointF>>();
		List<PointF[]> faces = new List<PointF[]>();
		List<PointF> current = new List<PointF>();
		if (includeCrosshair) current.Add(new PointF(viewport.Width * 0.5f, viewport.Height * 0.5f));
		foreach (WorldPoint point in worldPoints)
		{
			if (ProjectPoint(point.X - renderOrigin.X, point.Y - renderOrigin.Y, point.Z - renderOrigin.Z, matrices, viewport, out PointF projected))
			{
				current.Add(projected);
			}
			else if (current.Count > 0)
			{
				if (current.Count >= 2) segments.Add(current);
				current = new List<PointF>();
			}
		}
		if (current.Count >= 2) segments.Add(current);
		double radius = 0.018 + thickness * 0.006;
		for (int index = 1; index < worldPoints.Count; index++)
		{
			WorldPoint a = worldPoints[index - 1];
			WorldPoint b = worldPoints[index];
			double dx = b.X - a.X;
			double dy = b.Y - a.Y;
			double dz = b.Z - a.Z;
			double horizontal = Math.Sqrt(dx * dx + dz * dz);
			if (horizontal < 0.0001) continue;
			double sideX = -dz / horizontal * radius;
			double sideZ = dx / horizontal * radius;
			TryAddProjectedFace(faces, new[]
			{
				new WorldPoint(a.X + sideX, a.Y, a.Z + sideZ),
				new WorldPoint(b.X + sideX, b.Y, b.Z + sideZ),
				new WorldPoint(b.X - sideX, b.Y, b.Z - sideZ),
				new WorldPoint(a.X - sideX, a.Y, a.Z - sideZ)
			}, renderOrigin, matrices, viewport);
			TryAddProjectedFace(faces, new[]
			{
				new WorldPoint(a.X, a.Y + radius, a.Z),
				new WorldPoint(b.X, b.Y + radius, b.Z),
				new WorldPoint(b.X, b.Y - radius, b.Z),
				new WorldPoint(a.X, a.Y - radius, a.Z)
			}, renderOrigin, matrices, viewport);
		}
		return new ProjectedFireballTrajectory(segments, faces);
	}

	private static void TryAddProjectedFace(List<PointF[]> faces, IReadOnlyList<WorldPoint> points, RenderOrigin renderOrigin, RenderMatrices matrices, Size viewport)
	{
		PointF[] projected = new PointF[points.Count];
		for (int index = 0; index < points.Count; index++)
		{
			WorldPoint point = points[index];
			if (!ProjectPoint(point.X - renderOrigin.X, point.Y - renderOrigin.Y, point.Z - renderOrigin.Z, matrices, viewport, out projected[index])) return;
		}
		faces.Add(projected);
	}

	private static double DistanceSquared(double x1, double y1, double z1, double x2, double y2, double z2)
	{
		double dx = x1 - x2;
		double dy = y1 - y2;
		double dz = z1 - z2;
		return dx * dx + dy * dy + dz * dz;
	}

	private void RefreshBedBoxes(nint world, nint player, int range, bool includeObsidian, bool fullHeight)
	{
		cachedBedRange = range;
		cachedBedObsidian = includeObsidian;
		cachedBedFullHeight = fullHeight;
		cachedBedBoxes.Clear();
		cachedBedCount = 0;
		double playerX = ReadDoubleField(player, entityLayout, "field_70165_t");
		double playerY = ReadDoubleField(player, entityLayout, "field_70163_u");
		double playerZ = ReadDoubleField(player, entityLayout, "field_70161_v");
		if (!double.IsFinite(playerX) || !double.IsFinite(playerY) || !double.IsFinite(playerZ))
		{
			bedScanDiagnostic = "player position invalid";
			return;
		}
		nint provider = ReadObjectField(world, worldClientLayout, "field_73033_b");
		nint loadedChunks = ReadObjectField(provider, chunkProviderClientLayout, "field_73237_c");
		int loadedCount = Math.Clamp(ReadIntField(loadedChunks, arrayListLayout, "size"), 0, 4096);
		nint elements = ReadObjectField(loadedChunks, arrayListLayout, "elementData");
		if (provider == IntPtr.Zero || loadedChunks == IntPtr.Zero || elements == IntPtr.Zero || loadedCount == 0)
		{
			bedScanDiagnostic = "loaded chunks unavailable";
			return;
		}
		if (!memory.TryReadInt32(Add(elements, 12L), out int elementCapacity) || elementCapacity < loadedCount || elementCapacity > 16384)
		{
			bedScanDiagnostic = "loaded chunk array invalid";
			return;
		}
		int centerX = FloorToInt(playerX);
		int centerY = FloorToInt(playerY);
		int centerZ = FloorToInt(playerZ);
		int minChunkX = (centerX - range - 1) >> 4;
		int maxChunkX = (centerX + range + 1) >> 4;
		int minChunkZ = (centerZ - range - 1) >> 4;
		int maxChunkZ = (centerZ + range + 1) >> 4;
		int minY = Math.Clamp(centerY - range - 1, 0, 255);
		int maxY = Math.Clamp(centerY + range + 1, 0, 255);
		Dictionary<BlockCoordinate, byte> bedStates = new Dictionary<BlockCoordinate, byte>();
		HashSet<BlockCoordinate> obsidianStates = new HashSet<BlockCoordinate>();
		int scannedChunks = 0;
		for (int chunkIndex = 0; chunkIndex < loadedCount; chunkIndex++)
		{
			if (!memory.TryReadUInt32(Add(elements, 16 + (long)chunkIndex * 4L), out uint compressedChunk))
			{
				continue;
			}
			nint chunk = DecodeOop(compressedChunk);
			if (chunk == IntPtr.Zero)
			{
				continue;
			}
			int chunkX = ReadIntField(chunk, chunkLayout, "field_76635_g");
			int chunkZ = ReadIntField(chunk, chunkLayout, "field_76647_h");
			if (chunkX < minChunkX || chunkX > maxChunkX || chunkZ < minChunkZ || chunkZ > maxChunkZ)
			{
				continue;
			}
			scannedChunks++;
			nint storageArray = ReadObjectField(chunk, chunkLayout, "field_76652_q");
			if (storageArray == IntPtr.Zero || !memory.TryReadInt32(Add(storageArray, 12L), out int storageCount) || storageCount <= 0 || storageCount > 32)
			{
				continue;
			}
			int firstSection = Math.Clamp(minY >> 4, 0, storageCount - 1);
			int lastSection = Math.Clamp(maxY >> 4, 0, storageCount - 1);
			for (int sectionIndex = firstSection; sectionIndex <= lastSection; sectionIndex++)
			{
				if (!memory.TryReadUInt32(Add(storageArray, 16 + (long)sectionIndex * 4L), out uint compressedStorage))
				{
					continue;
				}
				nint storage = DecodeOop(compressedStorage);
				if (storage == IntPtr.Zero)
				{
					continue;
				}
				nint stateArray = ReadObjectField(storage, extendedBlockStorageLayout, "field_177488_d");
				if (stateArray == IntPtr.Zero || !memory.TryReadInt32(Add(stateArray, 12L), out int stateCount) || stateCount < 4096 || !memory.TryReadBytes(Add(stateArray, 16L), 8192, out byte[] states))
				{
					continue;
				}
				for (int blockIndex = 0; blockIndex < 4096; blockIndex++)
				{
					int byteIndex = blockIndex * 2;
					ushort packedState = (ushort)(states[byteIndex] | states[byteIndex + 1] << 8);
					int blockId = packedState >> 4;
					if (blockId != 26 && blockId != 49)
					{
						continue;
					}
					int localX = blockIndex & 15;
					int localZ = blockIndex >> 4 & 15;
					int localY = blockIndex >> 8 & 15;
					int blockX = (chunkX << 4) + localX;
					int blockY = (sectionIndex << 4) + localY;
					int blockZ = (chunkZ << 4) + localZ;
					if (blockY < minY || blockY > maxY || Math.Abs(blockX - centerX) > range + 1 || Math.Abs(blockZ - centerZ) > range + 1)
					{
						continue;
					}
					BlockCoordinate coordinate = new BlockCoordinate(blockX, blockY, blockZ);
					if (blockId == 26)
					{
						bedStates[coordinate] = (byte)(packedState & 15);
					}
					else if (includeObsidian)
					{
						obsidianStates.Add(coordinate);
					}
				}
			}
		}
		HashSet<BlockCoordinate> markedObsidian = new HashSet<BlockCoordinate>();
		foreach (KeyValuePair<BlockCoordinate, byte> entry in bedStates)
		{
			if ((entry.Value & 8) == 0 || cachedBedCount >= 256)
			{
				continue;
			}
			BlockCoordinate head = entry.Key;
			(int dx, int dz) = (entry.Value & 3) switch
			{
				0 => (0, -1),
				1 => (1, 0),
				2 => (0, 1),
				_ => (-1, 0)
			};
			BlockCoordinate foot = new BlockCoordinate(head.X + dx, head.Y, head.Z + dz);
			if (!bedStates.TryGetValue(foot, out byte footMeta) || (footMeta & 8) != 0 || (footMeta & 3) != (entry.Value & 3))
			{
				continue;
			}
			double bedCenterX = (head.X + foot.X + 1.0) * 0.5;
			double bedCenterY = head.Y + 0.5;
			double bedCenterZ = (head.Z + foot.Z + 1.0) * 0.5;
			double distanceX = bedCenterX - playerX;
			double distanceY = bedCenterY - playerY;
			double distanceZ = bedCenterZ - playerZ;
			if (distanceX * distanceX + distanceY * distanceY + distanceZ * distanceZ > (double)range * range)
			{
				continue;
			}
			double height = fullHeight ? 1.0 : 0.5625;
			cachedBedBoxes.Add(new CachedBlockBox(Math.Min(head.X, foot.X), head.Y, Math.Min(head.Z, foot.Z), Math.Max(head.X, foot.X) + 1.0, head.Y + height, Math.Max(head.Z, foot.Z) + 1.0, IsObsidian: false));
			cachedBedCount++;
			if (!includeObsidian)
			{
				continue;
			}
			foreach (BlockCoordinate part in new[] { head, foot })
			{
				foreach ((int ox, int oy, int oz) in new[] { (0, 1, 0), (0, 0, -1), (1, 0, 0), (0, 0, 1), (-1, 0, 0) })
				{
					BlockCoordinate adjacent = new BlockCoordinate(part.X + ox, part.Y + oy, part.Z + oz);
					if (obsidianStates.Contains(adjacent) && markedObsidian.Add(adjacent) && markedObsidian.Count <= 1024)
					{
						cachedBedBoxes.Add(new CachedBlockBox(adjacent.X, adjacent.Y, adjacent.Z, adjacent.X + 1.0, adjacent.Y + 1.0, adjacent.Z + 1.0, IsObsidian: true));
					}
				}
			}
		}
		bedScanDiagnostic = $"chunks={scannedChunks} bedStates={bedStates.Count} beds={cachedBedCount} obsidian={markedObsidian.Count}";
	}

	private void ResetWorldState()
	{
		IsLocalPlayerHurt = false;
		HurtTime = 0;
		IsInWorld = false;
		IsSnapshotValid = false;
		IsContainerOpen = false;
		activeWorldAddress = 0L;
		activePlayerAddress = 0L;
		cachedWorldEntities.Clear();
		cachedFireballs.Clear();
		fireballTrails.Clear();
		cachedBedBoxes.Clear();
		entityClassCache.Clear();
		playerNameCache.Clear();
		healthCache.Clear();
		equipmentCache.Clear();
		playerNameColorCache.Clear();
		localPlayerNameColorCode = -1;
		nextEntityScanMs = 0L;
		nextTeamScanMs = 0L;
		nextBedScanMs = 0L;
		cachedBedCount = 0;
		bedScanFaulted = false;
		bedScanDiagnostic = string.Empty;
	}

	private void RefreshWorldEntities(nint world, nint player, float partialTicks)
	{
		long elapsedMilliseconds = cacheClock.ElapsedMilliseconds;
		if (elapsedMilliseconds >= nextTeamScanMs)
		{
			nextTeamScanMs = elapsedMilliseconds + 250;
			RefreshPlayerNameColors(world, player);
		}
		nint num = ReadObjectField(world, worldLayout, "field_72996_f");
		if (num == IntPtr.Zero)
		{
			cachedWorldEntities.Clear();
			cachedFireballs.Clear();
			entityClassCache.Clear();
			playerNameCache.Clear();
			healthCache.Clear();
			equipmentCache.Clear();
			playerNameColorCache.Clear();
			localPlayerNameColorCode = -1;
			return;
		}
		int num2 = ReadIntField(num, arrayListLayout, "size");
		nint num3 = ReadObjectField(num, arrayListLayout, "elementData");
			if (num2 <= 0 || num2 > 4096 || num3 == IntPtr.Zero || !memory.TryReadInt32(Add(num3, 12L), out var value))
			{
				cachedWorldEntities.Clear();
				cachedFireballs.Clear();
				healthCache.Clear();
				equipmentCache.Clear();
				playerNameCache.Clear();
				return;
			}
		int num4 = Math.Min(Math.Min(num2, value), 1024);
		cachedWorldEntities.Clear();
		cachedFireballs.Clear();
		entityClassCache.Clear();
		HashSet<long> activeEntityIds = new HashSet<long>();
		HashSet<long> activeFireballIds = new HashSet<long>();
		for (int i = 0; i < num4; i++)
		{
			if (!memory.TryReadUInt32(Add(num3, 16 + (long)i * 4L), out var value2))
			{
				continue;
			}
			nint num5 = DecodeOop(value2);
			if (num5 == IntPtr.Zero || num5 == player)
			{
				continue;
			}
			EntityKind? entityKind = ClassifyEntity(num5, out string className, out bool isFireball);
			if (entityKind.HasValue && ReadEntityTransform(num5, partialTicks, out var transform))
			{
				string text = ((entityKind == EntityKind.Player) ? ReadPlayerName(num5) : className.Substring(className.LastIndexOf('/') + 1));
				if (string.IsNullOrWhiteSpace(text))
				{
					text = ((entityKind == EntityKind.Player) ? "Player" : className.Substring(className.LastIndexOf('/') + 1));
				}
				float num6 = ReadCachedLivingHealth(num5);
				float maxHealth = ((num6 > 0f) ? Math.Max(20f, num6) : 0f);
				int nameColorCode = ((entityKind == EntityKind.Player && playerNameColorCache.TryGetValue(text, out int colorCode)) ? colorCode : -1);
				PlayerEquipmentSnapshot equipment = entityKind == EntityKind.Player ? ReadCachedPlayerEquipment(num5) : PlayerEquipmentSnapshot.Empty;
				double motionX = ReadDoubleField(num5, entityLayout, "field_70159_w");
				double motionY = ReadDoubleField(num5, entityLayout, "field_70181_x");
				double motionZ = ReadDoubleField(num5, entityLayout, "field_70179_y");
				if (!double.IsFinite(motionX)) motionX = 0.0;
				if (!double.IsFinite(motionY)) motionY = 0.0;
				if (!double.IsFinite(motionZ)) motionZ = 0.0;
				cachedWorldEntities.Add(new CachedWorldEntity(((IntPtr)num5).ToInt64(), entityKind.Value, text, transform, motionX, motionY, motionZ, num6, maxHealth, nameColorCode, equipment));
				activeEntityIds.Add(((IntPtr)num5).ToInt64());
			}
			else if (isFireball && ReadEntityTransform(num5, partialTicks, out EntityTransform fireballTransform))
			{
				double motionX = ReadDoubleField(num5, entityLayout, "field_70159_w");
				double motionY = ReadDoubleField(num5, entityLayout, "field_70181_x");
				double motionZ = ReadDoubleField(num5, entityLayout, "field_70179_y");
				double accelerationX = ReadDoubleField(num5, fireballLayout, "field_70232_b");
				double accelerationY = ReadDoubleField(num5, fireballLayout, "field_70233_c");
				double accelerationZ = ReadDoubleField(num5, fireballLayout, "field_70230_d");
				if (double.IsFinite(motionX) && double.IsFinite(motionY) && double.IsFinite(motionZ) && double.IsFinite(accelerationX) && double.IsFinite(accelerationY) && double.IsFinite(accelerationZ))
				{
					long entityId = ((IntPtr)num5).ToInt64();
					string name = ReadFireballShooterName(num5);
					cachedFireballs.Add(new CachedFireball(entityId, name, fireballTransform, motionX, motionY, motionZ, accelerationX, accelerationY, accelerationZ));
					activeFireballIds.Add(entityId);
					AppendFireballTrailPoint(entityId, fireballTransform.X, fireballTransform.Y + fireballTransform.Height * 0.5, fireballTransform.Z);
				}
			}
		}
		for (int index = 0; index < cachedFireballs.Count; index++)
		{
			CachedFireball fireball = cachedFireballs[index];
			if (!string.IsNullOrWhiteSpace(fireball.Name)) continue;
			string fallbackName = ResolveFireballOwnerFromSpawn(fireball.EntityId);
			cachedFireballs[index] = fireball with { Name = string.IsNullOrWhiteSpace(fallbackName) ? "Unknown" : fallbackName };
		}
		foreach (long staleId in healthCache.Keys.Where((long id) => !activeEntityIds.Contains(id)).ToArray())
		{
			healthCache.Remove(staleId);
			equipmentCache.Remove(staleId);
			playerNameCache.Remove(staleId);
		}
		long nowMs = cacheClock.ElapsedMilliseconds;
		foreach (long staleId in fireballTrails.Where(pair => !activeFireballIds.Contains(pair.Key) && nowMs - pair.Value.LastSeenMs > 1000L).Select(pair => pair.Key).ToArray())
		{
			fireballTrails.Remove(staleId);
		}
	}

	private string ReadFireballShooterName(nint fireball)
	{
		nint shooter = ReadObjectField(fireball, fireballLayout, "field_70235_a");
		if (shooter == IntPtr.Zero || !IsInstanceOf(shooter, "net/minecraft/entity/player/EntityPlayer")) return string.Empty;
		return ReadPlayerName(shooter);
	}

	private string ResolveFireballOwnerFromSpawn(long entityId)
	{
		if (!fireballTrails.TryGetValue(entityId, out FireballTrailState? trail) || trail.Points.Count == 0) return string.Empty;
		WorldPoint spawn = trail.Points[0];
		CachedWorldEntity? nearest = cachedWorldEntities
			.Where(entity => entity.Kind == EntityKind.Player)
			.Select(entity => (Entity: entity, DistanceSquared: DistanceSquared(entity.Transform.X, entity.Transform.Y + entity.Transform.Height * 0.5, entity.Transform.Z, spawn.X, spawn.Y, spawn.Z)))
			.Where(candidate => candidate.DistanceSquared <= 36.0)
			.OrderBy(candidate => candidate.DistanceSquared)
			.Select(candidate => (CachedWorldEntity?)candidate.Entity)
			.FirstOrDefault();
		return nearest?.Name ?? string.Empty;
	}

	private void AppendFireballTrailPoint(long entityId, double x, double y, double z)
	{
		if (!fireballTrails.TryGetValue(entityId, out FireballTrailState? trail))
		{
			trail = new FireballTrailState();
			fireballTrails[entityId] = trail;
		}
		trail.LastSeenMs = cacheClock.ElapsedMilliseconds;
		WorldPoint point = new WorldPoint(x, y, z);
		if (trail.Points.Count > 0)
		{
			WorldPoint last = trail.Points[^1];
			double dx = point.X - last.X;
			double dy = point.Y - last.Y;
			double dz = point.Z - last.Z;
			if (dx * dx + dy * dy + dz * dz < 0.01) return;
		}
		trail.Points.Add(point);
		if (trail.Points.Count > 256) trail.Points.RemoveRange(0, trail.Points.Count - 256);
	}

	private PlayerEquipmentSnapshot ReadCachedPlayerEquipment(nint player)
	{
		long entityId = ((IntPtr)player).ToInt64();
		long now = cacheClock.ElapsedMilliseconds;
		if (equipmentCache.TryGetValue(entityId, out TimedEquipment cached) && now - cached.ReadAtMs < 150L)
		{
			return cached.Value;
		}
		PlayerEquipmentSnapshot equipment = ReadPlayerEquipment(player);
		equipmentCache[entityId] = new TimedEquipment(equipment, now);
		return equipment;
	}

	private PlayerEquipmentSnapshot ReadPlayerEquipment(nint player)
	{
		if (inventoryPlayerLayout.KlassAddress == IntPtr.Zero || itemStackLayout.KlassAddress == IntPtr.Zero || itemLayout.KlassAddress == IntPtr.Zero)
		{
			return PlayerEquipmentSnapshot.Empty;
		}
		nint inventory = ReadObjectField(player, playerLayout, "field_71071_by");
		if (inventory == IntPtr.Zero)
		{
			return PlayerEquipmentSnapshot.Empty;
		}
		int selectedSlot = ReadIntField(inventory, inventoryPlayerLayout, "field_70461_c");
		nint mainInventory = ReadObjectField(inventory, inventoryPlayerLayout, "field_70462_a");
		nint armorInventory = ReadObjectField(inventory, inventoryPlayerLayout, "field_70460_b");
		EquipmentItemSnapshot? held = selectedSlot is >= 0 and < 9 ? ReadEquipmentItem(ReadObjectArrayElement(mainInventory, selectedSlot)) : null;
		return new PlayerEquipmentSnapshot(
			held,
			ReadEquipmentItem(ReadObjectArrayElement(armorInventory, 0)),
			ReadEquipmentItem(ReadObjectArrayElement(armorInventory, 1)),
			ReadEquipmentItem(ReadObjectArrayElement(armorInventory, 2)),
			ReadEquipmentItem(ReadObjectArrayElement(armorInventory, 3)));
	}

	private EquipmentItemSnapshot? ReadEquipmentItem(nint stack)
	{
		if (stack == IntPtr.Zero)
		{
			return null;
		}
		nint item = ReadObjectField(stack, itemStackLayout, "field_151002_e");
		if (item == IntPtr.Zero || !IsInstanceOf(item, "net/minecraft/item/Item"))
		{
			return null;
		}
		string unlocalizedName = ReadJavaString(ReadObjectField(item, itemLayout, "field_77774_bZ"));
		string kind = ClassifyItemKind(item, unlocalizedName);
		string material = ClassifyItemMaterial(unlocalizedName);
		int maxDamage = Math.Clamp(ReadFirstIntField(item, itemLayout, "field_77699_b"), 0, 100000);
		int damage = maxDamage > 0 ? Math.Clamp(ReadFirstIntField(stack, itemStackLayout, "field_77991_e"), 0, maxDamage) : 0;
		return new EquipmentItemSnapshot(kind, material, damage, maxDamage);
	}

	private nint ReadObjectArrayElement(nint array, int index)
	{
		if (array == IntPtr.Zero || index < 0 || !memory.TryReadInt32(Add(array, 12L), out int length) || index >= length || !memory.TryReadUInt32(Add(array, 16L + index * 4L), out uint narrow))
		{
			return IntPtr.Zero;
		}
		return DecodeOop(narrow);
	}

	private int ReadFirstIntField(nint owner, JvmClassLayout layout, params string[] names)
	{
		foreach (string name in names)
		{
			JvmJavaField field = FindField(layout, name);
			if (field.Name != null && memory.TryReadInt32(Add(owner, field.Offset), out int value))
			{
				return value;
			}
		}
		return 0;
	}

	private string ClassifyItemKind(nint item, string name)
	{
		if (IsInstanceOf(item, "net/minecraft/item/ItemArmor"))
		{
			if (name.Contains("helmet", StringComparison.OrdinalIgnoreCase)) return "H";
			if (name.Contains("chestplate", StringComparison.OrdinalIgnoreCase)) return "C";
			if (name.Contains("leggings", StringComparison.OrdinalIgnoreCase)) return "L";
			if (name.Contains("boots", StringComparison.OrdinalIgnoreCase)) return "B";
			return "A";
		}
		if (IsInstanceOf(item, "net/minecraft/item/ItemSword")) return "S";
		if (IsInstanceOf(item, "net/minecraft/item/ItemBow")) return "BW";
		if (IsInstanceOf(item, "net/minecraft/item/ItemTool")) return "T";
		if (IsInstanceOf(item, "net/minecraft/item/ItemBlock")) return "BL";
		if (IsInstanceOf(item, "net/minecraft/item/ItemPotion")) return "P";
		if (IsInstanceOf(item, "net/minecraft/item/ItemFood")) return "F";
		return "I";
	}

	private static string ClassifyItemMaterial(string name)
	{
		if (name.Contains("diamond", StringComparison.OrdinalIgnoreCase)) return "Diamond";
		if (name.Contains("iron", StringComparison.OrdinalIgnoreCase)) return "Iron";
		if (name.Contains("gold", StringComparison.OrdinalIgnoreCase)) return "Gold";
		if (name.Contains("chain", StringComparison.OrdinalIgnoreCase)) return "Chain";
		if (name.Contains("leather", StringComparison.OrdinalIgnoreCase)) return "Leather";
		if (name.Contains("stone", StringComparison.OrdinalIgnoreCase)) return "Stone";
		if (name.Contains("wood", StringComparison.OrdinalIgnoreCase)) return "Wood";
		return "Other";
	}

	private bool IsContainerScreen(nint screen)
	{
		return screen != IntPtr.Zero && IsInstanceOf(screen, "net/minecraft/client/gui/inventory/GuiContainer");
	}

	internal bool IsPlayerInventoryScreenOpen()
	{
		try
		{
			nint minecraft = ReadMinecraftInstance();
			nint screen = ReadObjectField(minecraft, minecraftLayout, "field_71462_r");
			return screen != IntPtr.Zero && IsInstanceOf(screen, "net/minecraft/client/gui/inventory/GuiInventory");
		}
		catch
		{
			return false;
		}
	}

	internal bool IsGameplayScreenClear()
	{
		try
		{
			nint minecraft = ReadMinecraftInstance();
			return ReadObjectField(minecraft, minecraftLayout, "field_71462_r") == IntPtr.Zero;
		}
		catch
		{
			return false;
		}
	}

	internal int GetInventoryKeyVirtualCode()
	{
		try
		{
			nint minecraft = ReadMinecraftInstance();
			nint settings = ReadObjectField(minecraft, minecraftLayout, "field_71474_y");
			nint binding = ReadObjectField(settings, gameSettingsLayout, "field_151445_Q");
			int lwjglCode = ReadIntField(binding, keyBindingLayout, "field_74512_d");
			return LwjglKeyToVirtualCode(lwjglCode);
		}
		catch
		{
			return 0;
		}
	}

	private static int LwjglKeyToVirtualCode(int code)
	{
		if (code is >= 2 and <= 10) return 0x31 + code - 2;
		if (code == 11) return 0x30;
		if (code is >= 59 and <= 68) return 0x70 + code - 59;
		return code switch
		{
			1 => 0x1B,
			14 => 0x08,
			15 => 0x09,
			16 => 0x51,
			17 => 0x57,
			18 => 0x45,
			19 => 0x52,
			20 => 0x54,
			21 => 0x59,
			22 => 0x55,
			23 => 0x49,
			24 => 0x4F,
			25 => 0x50,
			30 => 0x41,
			31 => 0x53,
			32 => 0x44,
			33 => 0x46,
			34 => 0x47,
			35 => 0x48,
			36 => 0x4A,
			37 => 0x4B,
			38 => 0x4C,
			44 => 0x5A,
			45 => 0x58,
			46 => 0x43,
			47 => 0x56,
			48 => 0x42,
			49 => 0x4E,
			50 => 0x4D,
			57 => 0x20,
			211 => 0x2E,
			_ => 0
		};
	}

	internal PlayerInventorySnapshot GetOpenPlayerInventorySnapshot()
	{
		return GetPlayerInventorySnapshot(requireInventoryScreen: true);
	}

	internal PlayerInventorySnapshot GetPlayerInventorySnapshot()
	{
		return GetPlayerInventorySnapshot(requireInventoryScreen: false);
	}

	private PlayerInventorySnapshot GetPlayerInventorySnapshot(bool requireInventoryScreen)
	{
		try
		{
			if (inventoryPlayerLayout.KlassAddress == IntPtr.Zero || itemStackLayout.KlassAddress == IntPtr.Zero || itemLayout.KlassAddress == IntPtr.Zero)
			{
				return PlayerInventorySnapshot.Closed;
			}
			nint minecraft = ReadMinecraftInstance();
			nint screen = ReadObjectField(minecraft, minecraftLayout, "field_71462_r");
			bool inventoryOpen = screen != IntPtr.Zero && IsInstanceOf(screen, "net/minecraft/client/gui/inventory/GuiInventory");
			if (requireInventoryScreen && !inventoryOpen)
			{
				return PlayerInventorySnapshot.Closed;
			}
			nint player = ReadObjectField(minecraft, minecraftLayout, "field_71439_g");
			nint inventory = ReadObjectField(player, playerLayout, "field_71071_by");
			nint main = ReadObjectField(inventory, inventoryPlayerLayout, "field_70462_a");
			nint armor = ReadObjectField(inventory, inventoryPlayerLayout, "field_70460_b");
			if (player == IntPtr.Zero || inventory == IntPtr.Zero || main == IntPtr.Zero || armor == IntPtr.Zero)
			{
				return PlayerInventorySnapshot.Closed;
			}
			List<InventoryItemSnapshot> mainSlots = new List<InventoryItemSnapshot>(36);
			for (int i = 0; i < 36; i++)
			{
				mainSlots.Add(ReadInventoryItem(ReadObjectArrayElement(main, i), i));
			}
			List<InventoryItemSnapshot> armorSlots = new List<InventoryItemSnapshot>(4);
			for (int i = 0; i < 4; i++)
			{
				armorSlots.Add(ReadInventoryItem(ReadObjectArrayElement(armor, i), 36 + i));
			}
			return new PlayerInventorySnapshot(
				inventoryOpen, mainSlots, armorSlots,
				inventoryOpen ? ReadIntField(screen, guiContainerLayout, "field_147003_i") : 0,
				inventoryOpen ? ReadIntField(screen, guiContainerLayout, "field_147009_r") : 0,
				inventoryOpen ? ReadIntField(screen, guiScreenLayout, "field_146294_l") : 0,
				inventoryOpen ? ReadIntField(screen, guiScreenLayout, "field_146295_m") : 0,
				Math.Clamp(ReadIntField(inventory, inventoryPlayerLayout, "field_70461_c"), 0, 8));
		}
		catch (Exception ex)
		{
			AppLog.Write($"[INV-ERROR] {ex.GetType().Name}: {ex.Message}");
			return PlayerInventorySnapshot.Closed;
		}
	}

	private InventoryItemSnapshot ReadInventoryItem(nint stack, int inventoryIndex)
	{
		if (stack == IntPtr.Zero)
		{
			return new InventoryItemSnapshot(inventoryIndex, string.Empty, InventoryItemKind.Unknown, 0, 0, 0, 0f, 0f, 0, -1, new Dictionary<int, int>(), false);
		}
		nint item = ReadObjectField(stack, itemStackLayout, "field_151002_e");
		if (item == IntPtr.Zero)
		{
			return new InventoryItemSnapshot(inventoryIndex, string.Empty, InventoryItemKind.Unknown, 0, 0, 0, 0f, 0f, 0, -1, new Dictionary<int, int>(), false);
		}
		string name = ReadJavaString(ReadObjectField(item, itemLayout, "field_77774_bZ"));
		int count = Math.Clamp(ReadIntField(stack, itemStackLayout, "field_77994_a"), 0, 4096);
		int damage = Math.Max(0, ReadIntField(stack, itemStackLayout, "field_77991_e"));
		int maxDamage = Math.Max(0, ReadIntField(item, itemLayout, "field_77699_b"));
		float attack = 0f;
		float efficiency = 0f;
		int armorReduction = 0;
		int armorType = -1;
		if (IsInstanceOf(item, "net/minecraft/item/ItemSword"))
		{
			attack = ReadFloatField(item, itemSwordLayout, "field_150934_a");
		}
		else if (IsInstanceOf(item, "net/minecraft/item/ItemTool"))
		{
			attack = ReadFloatField(item, itemToolLayout, "field_77865_bY");
			efficiency = ReadFloatField(item, itemToolLayout, "field_77864_a");
		}
		if (IsInstanceOf(item, "net/minecraft/item/ItemArmor"))
		{
			armorReduction = Math.Max(0, ReadIntField(item, itemArmorLayout, "field_77879_b"));
			armorType = ReadIntField(item, itemArmorLayout, "field_77881_a");
		}
		nint tag = ReadObjectField(stack, itemStackLayout, "field_77990_d");
		return new InventoryItemSnapshot(inventoryIndex, name, ClassifyInventoryItem(item, name, armorType), count, damage, maxDamage, attack, efficiency, armorReduction, armorType, ReadEnchantments(tag), HasCustomName(tag));
	}

	private InventoryItemKind ClassifyInventoryItem(nint item, string name, int armorType)
	{
		if (IsInstanceOf(item, "net/minecraft/item/ItemArmor"))
		{
			return armorType switch { 0 => InventoryItemKind.Helmet, 1 => InventoryItemKind.Chestplate, 2 => InventoryItemKind.Leggings, 3 => InventoryItemKind.Boots, _ => InventoryItemKind.Unknown };
		}
		if (IsInstanceOf(item, "net/minecraft/item/ItemSword")) return InventoryItemKind.Sword;
		if (IsInstanceOf(item, "net/minecraft/item/ItemBow")) return InventoryItemKind.Bow;
		if (IsInstanceOf(item, "net/minecraft/item/ItemTool"))
		{
			if (name.Contains("pickaxe", StringComparison.OrdinalIgnoreCase)) return InventoryItemKind.Pickaxe;
			if (name.Contains("hatchet", StringComparison.OrdinalIgnoreCase) || name.Contains("axe", StringComparison.OrdinalIgnoreCase)) return InventoryItemKind.Axe;
			if (name.Contains("shovel", StringComparison.OrdinalIgnoreCase) || name.Contains("spade", StringComparison.OrdinalIgnoreCase)) return InventoryItemKind.Shovel;
		}
		if (IsInstanceOf(item, "net/minecraft/item/ItemBlock")) return InventoryItemKind.Block;
		if (name.Contains("fishingRod", StringComparison.OrdinalIgnoreCase)) return InventoryItemKind.FishingRod;
		if (name.Contains("bucketWater", StringComparison.OrdinalIgnoreCase)) return InventoryItemKind.WaterBucket;
		if (name.Contains("bucketLava", StringComparison.OrdinalIgnoreCase)) return InventoryItemKind.LavaBucket;
		if (name.Contains("enderPearl", StringComparison.OrdinalIgnoreCase)) return InventoryItemKind.EnderPearl;
		if (name.Contains("fireball", StringComparison.OrdinalIgnoreCase) || name.Contains("fireCharge", StringComparison.OrdinalIgnoreCase)) return InventoryItemKind.FireCharge;
		if (name.Contains("appleGold", StringComparison.OrdinalIgnoreCase)) return InventoryItemKind.GoldenApple;
		if (name.EndsWith("arrow", StringComparison.OrdinalIgnoreCase)) return InventoryItemKind.Arrow;
		if (name.EndsWith("egg", StringComparison.OrdinalIgnoreCase)) return InventoryItemKind.Egg;
		if (name.Contains("snowball", StringComparison.OrdinalIgnoreCase)) return InventoryItemKind.Snowball;
		if (name.Contains("compass", StringComparison.OrdinalIgnoreCase)) return InventoryItemKind.Compass;
		if (IsInstanceOf(item, "net/minecraft/item/ItemPotion")) return InventoryItemKind.Potion;
		if (IsInstanceOf(item, "net/minecraft/item/ItemFood")) return InventoryItemKind.Food;
		return InventoryItemKind.Unknown;
	}

	private IReadOnlyDictionary<int, int> ReadEnchantments(nint tag)
	{
		Dictionary<int, int> result = new Dictionary<int, int>();
		nint list = ReadNbtCompoundValue(tag, "ench");
		if (list == IntPtr.Zero) list = ReadNbtCompoundValue(tag, "StoredEnchantments");
		if (list == IntPtr.Zero) return result;
		nint tags = ReadObjectField(list, nbtListLayout, "field_74747_a");
		int size = ReadIntField(tags, arrayListLayout, "size");
		nint elements = ReadObjectField(tags, arrayListLayout, "elementData");
		for (int i = 0; i < Math.Clamp(size, 0, 128); i++)
		{
			nint enchantment = ReadObjectArrayElement(elements, i);
			int id = ReadNbtShort(ReadNbtCompoundValue(enchantment, "id"));
			int level = ReadNbtShort(ReadNbtCompoundValue(enchantment, "lvl"));
			if (id >= 0 && level > 0) result[id] = Math.Max(result.TryGetValue(id, out int previous) ? previous : 0, level);
		}
		return result;
	}

	private bool HasCustomName(nint tag)
	{
		nint display = ReadNbtCompoundValue(tag, "display");
		nint nameTag = ReadNbtCompoundValue(display, "Name");
		return nameTag != IntPtr.Zero && !string.IsNullOrWhiteSpace(ReadJavaString(ReadObjectField(nameTag, nbtStringLayout, "field_74751_a")));
	}

	private nint ReadNbtCompoundValue(nint compound, string key)
	{
		if (compound == IntPtr.Zero || nbtCompoundLayout.KlassAddress == IntPtr.Zero) return IntPtr.Zero;
		nint map = ReadObjectField(compound, nbtCompoundLayout, "field_74784_a");
		nint table = ReadObjectField(map, hashMapLayout, "table");
		if (table == IntPtr.Zero || !memory.TryReadInt32(Add(table, 12L), out int length)) return IntPtr.Zero;
		for (int i = 0; i < Math.Clamp(length, 0, 4096); i++)
		{
			if (!memory.TryReadUInt32(Add(table, 16L + i * 4L), out uint narrow)) continue;
			nint node = DecodeOop(narrow);
			int guard = 0;
			while (node != IntPtr.Zero && guard++ < 256)
			{
				if (string.Equals(ReadJavaString(ReadObjectField(node, hashMapNodeLayout, "key")), key, StringComparison.Ordinal))
				{
					return ReadObjectField(node, hashMapNodeLayout, "value");
				}
				node = ReadObjectField(node, hashMapNodeLayout, "next");
			}
		}
		return IntPtr.Zero;
	}

	private int ReadNbtShort(nint tag)
	{
		if (tag == IntPtr.Zero) return -1;
		JvmJavaField field = FindField(nbtShortLayout, "field_74752_a");
		return field.Name != null && memory.TryReadUInt16(Add(tag, field.Offset), out ushort value) ? unchecked((short)value) : -1;
	}

	internal ChestInventorySnapshot GetOpenChestSnapshot()
	{
		try
		{
			nint minecraft = ReadMinecraftInstance();
			nint screen = ReadObjectField(minecraft, minecraftLayout, "field_71462_r");
			if (screen == IntPtr.Zero || !IsInstanceOf(screen, "net/minecraft/client/gui/inventory/GuiChest"))
			{
				return ChestInventorySnapshot.Closed;
			}

			int rows = ReadIntField(screen, guiChestLayout, "field_147018_x");
			if (rows is < 1 or > 6)
			{
				return ChestInventorySnapshot.Closed;
			}

			int scaledWidth = ReadIntField(screen, guiScreenLayout, "field_146294_l");
			int scaledHeight = ReadIntField(screen, guiScreenLayout, "field_146295_m");
			int guiLeft = ReadIntField(screen, guiContainerLayout, "field_147003_i");
			int guiTop = ReadIntField(screen, guiContainerLayout, "field_147009_r");
			if (scaledWidth <= 0 || scaledHeight <= 0)
			{
				return ChestInventorySnapshot.Closed;
			}

			List<ChestSlotSnapshot> occupied = new List<ChestSlotSnapshot>();
			nint chestInventory = ReadObjectField(screen, guiChestLayout, "field_147015_w");
			bool reliable = TryCollectInventorySlots(chestInventory, 0, rows * 9, occupied);
			return new ChestInventorySnapshot(true, rows, occupied, reliable, guiLeft, guiTop, scaledWidth, scaledHeight);
		}
		catch
		{
			return ChestInventorySnapshot.Closed;
		}
	}

	internal bool IsChestScreenOpen()
	{
		try
		{
			nint minecraft = ReadMinecraftInstance();
			nint screen = ReadObjectField(minecraft, minecraftLayout, "field_71462_r");
			return screen != IntPtr.Zero && IsInstanceOf(screen, "net/minecraft/client/gui/inventory/GuiChest");
		}
		catch
		{
			return false;
		}
	}

	private bool TryCollectInventorySlots(nint inventory, int baseSlot, int expectedCount, List<ChestSlotSnapshot> occupied)
	{
		if (inventory == IntPtr.Zero || expectedCount <= 0)
		{
			return false;
		}
		if (IsInstanceOf(inventory, "net/minecraft/inventory/InventoryLargeChest"))
		{
			nint upper = ReadObjectField(inventory, inventoryLargeChestLayout, "field_70477_b");
			nint lower = ReadObjectField(inventory, inventoryLargeChestLayout, "field_70478_c");
			int half = expectedCount / 2;
			return TryCollectInventorySlots(upper, baseSlot, half, occupied)
				& TryCollectInventorySlots(lower, baseSlot + half, expectedCount - half, occupied);
		}

		nint contents = IntPtr.Zero;
		if (IsInstanceOf(inventory, "net/minecraft/inventory/InventoryBasic"))
		{
			contents = ReadObjectField(inventory, inventoryBasicLayout, "field_70482_c");
		}
		else if (IsInstanceOf(inventory, "net/minecraft/tileentity/TileEntityChest"))
		{
			contents = ReadObjectField(inventory, tileEntityChestLayout, "field_145985_p");
		}
		else if (IsInstanceOf(inventory, "net/minecraft/entity/player/InventoryEnderChest"))
		{
			contents = ReadObjectField(inventory, inventoryEnderChestLayout, "field_70488_a");
		}
		if (contents == IntPtr.Zero || !memory.TryReadInt32(Add(contents, 12L), out int length) || length < expectedCount)
		{
			return false;
		}
		for (int index = 0; index < expectedCount; index++)
		{
			nint stack = ReadObjectArrayElement(contents, index);
			if (stack != IntPtr.Zero)
			{
				nint item = ReadObjectField(stack, itemStackLayout, "field_151002_e");
				if (item != IntPtr.Zero) occupied.Add(new ChestSlotSnapshot(baseSlot + index, ReadInventoryItem(stack, baseSlot + index)));
			}
		}
		return true;
	}

	private void RefreshPlayerNameColors(nint world, nint localPlayer)
	{
		playerNameColorCache.Clear();
		localPlayerNameColorCode = -1;
		if (scoreboardLayout.KlassAddress == IntPtr.Zero || scorePlayerTeamLayout.KlassAddress == IntPtr.Zero)
		{
			return;
		}
		nint scoreboard = ReadObjectField(world, worldLayout, "field_96442_D");
		nint memberships = ReadObjectField(scoreboard, scoreboardLayout, "field_96540_f");
		nint table = ReadObjectField(memberships, hashMapLayout, "table");
		if (table == IntPtr.Zero || !memory.TryReadInt32(Add(table, 12L), out var tableLength) || tableLength <= 0 || tableLength > 65536)
		{
			return;
		}
		for (int bucket = 0; bucket < tableLength; bucket++)
		{
			if (!memory.TryReadUInt32(Add(table, 16 + (long)bucket * 4L), out var nodeNarrow))
			{
				continue;
			}
			nint node = DecodeOop(nodeNarrow);
			int chainDepth = 0;
			while (node != IntPtr.Zero && chainDepth < 256)
			{
				string playerName = ReadJavaString(ReadObjectField(node, hashMapNodeLayout, "key"));
				nint team = ReadObjectField(node, hashMapNodeLayout, "value");
				int colorCode = ReadTeamColorCode(team);
				if (!string.IsNullOrWhiteSpace(playerName) && colorCode >= 0)
				{
					playerNameColorCache[playerName] = colorCode;
				}
				node = ReadObjectField(node, hashMapNodeLayout, "next");
				chainDepth++;
			}
		}
		string localPlayerName = ReadPlayerName(localPlayer);
		if (!string.IsNullOrWhiteSpace(localPlayerName) && playerNameColorCache.TryGetValue(localPlayerName, out int localColorCode))
		{
			localPlayerNameColorCode = localColorCode;
		}
	}

	private int ReadTeamColorCode(nint team)
	{
		if (team == IntPtr.Zero)
		{
			return -1;
		}
		string prefix = ReadJavaString(ReadObjectField(team, scorePlayerTeamLayout, "field_96674_e"));
		return ParseLastMinecraftColorCode(prefix);
	}

	private static int ParseLastMinecraftColorCode(string text)
	{
		for (int i = text.Length - 2; i >= 0; i--)
		{
			if (text[i] != '§')
			{
				continue;
			}
			char value = char.ToLowerInvariant(text[i + 1]);
			if (value >= '0' && value <= '9')
			{
				return value - '0';
			}
			if (value >= 'a' && value <= 'f')
			{
				return value - 'a' + 10;
			}
		}
		return -1;
	}

	public LocalPlayerState GetLocalPlayerState()
	{
		try
		{
			nint num = ReadMinecraftInstance();
			if (num == IntPtr.Zero)
			{
				return LocalPlayerState.Empty;
			}
			nint num2 = ReadObjectField(num, minecraftLayout, "field_71441_e");
			nint num3 = ReadObjectField(num, minecraftLayout, "field_71439_g");
			if (num2 == IntPtr.Zero || num3 == IntPtr.Zero)
			{
				return LocalPlayerState.Empty;
			}
			bool guiOpen = ReadObjectField(num, minecraftLayout, "field_71462_r") != IntPtr.Zero;
			double num4 = ReadDoubleField(num3, entityLayout, "field_70165_t");
			double num5 = ReadDoubleField(num3, entityLayout, "field_70163_u");
			double num6 = ReadDoubleField(num3, entityLayout, "field_70161_v");
			float num7 = ReadFloatField(num3, entityLayout, "field_70177_z");
			float num8 = ReadFloatField(num3, entityLayout, "field_70125_A");
			bool onGround = ReadBooleanField(num3, entityLayout, "field_70122_E");
			double num9 = ReadDoubleField(num3, entityLayout, "field_70159_w");
			double motionY = ReadDoubleField(num3, entityLayout, "field_70181_x");
			double num10 = ReadDoubleField(num3, entityLayout, "field_70179_y");
			bool holdingBow = IsHoldingItemClass("net/minecraft/item/ItemBow") == true;
			int itemInUseCount = ReadIntField(num3, playerLayout, "field_71072_f");
			int bowUseTicks = holdingBow && itemInUseCount > 0 ? Math.Clamp(72000 - itemInUseCount, 0, 72000) : 0;
			if (!double.IsFinite(num4) || !double.IsFinite(num5) || !double.IsFinite(num6) || !float.IsFinite(num7) || !float.IsFinite(num8) || !double.IsFinite(num9) || !double.IsFinite(motionY) || !double.IsFinite(num10))
			{
				return LocalPlayerState.Empty;
			}
			return new LocalPlayerState(Valid: true, InWorld: true, guiOpen, num4, num5, num6, num7, num8, onGround, num9, motionY, num10, holdingBow, bowUseTicks);
		}
		catch
		{
			return LocalPlayerState.Empty;
		}
	}

	public bool? HasBlockSupport(double x, double footY, double z)
	{
		try
		{
			nint num = ReadMinecraftInstance();
			if (num == IntPtr.Zero)
			{
				return null;
			}
			nint num2 = ReadObjectField(num, minecraftLayout, "field_71441_e");
			if (num2 == IntPtr.Zero)
			{
				return null;
			}
			int blockX = FloorToInt(x);
			int num3 = FloorToInt(footY - 0.05);
			int blockZ = FloorToInt(z);
			if (num3 < 0 || num3 >= 256)
			{
				return false;
			}
			if (!TryReadPackedBlockState(num2, blockX, num3, blockZ, out var packedState))
			{
				return null;
			}
			return packedState >> 4 != 0;
		}
		catch
		{
			return null;
		}
	}

	public bool? IsHoldingBlockItem()
	{
		return IsHoldingItemClass("net/minecraft/item/ItemBlock");
	}

	public bool? HasClearLineOfSight(double startX, double startY, double startZ, double endX, double endY, double endZ)
	{
		try
		{
			if (!double.IsFinite(startX) || !double.IsFinite(startY) || !double.IsFinite(startZ) || !double.IsFinite(endX) || !double.IsFinite(endY) || !double.IsFinite(endZ)) return null;
			nint minecraft = ReadMinecraftInstance();
			nint world = ReadObjectField(minecraft, minecraftLayout, "field_71441_e");
			if (world == IntPtr.Zero) return null;
			double dx = endX - startX;
			double dy = endY - startY;
			double dz = endZ - startZ;
			double distance = Math.Sqrt(dx * dx + dy * dy + dz * dz);
			if (!double.IsFinite(distance) || distance < 0.01) return true;
			int steps = Math.Clamp((int)Math.Ceiling(distance * 4.0), 1, 512);
			int lastX = int.MinValue;
			int lastY = int.MinValue;
			int lastZ = int.MinValue;
			for (int step = 1; step < steps; step++)
			{
				double t = step / (double)steps;
				int blockX = FloorToInt(startX + dx * t);
				int blockY = FloorToInt(startY + dy * t);
				int blockZ = FloorToInt(startZ + dz * t);
				if (blockX == lastX && blockY == lastY && blockZ == lastZ) continue;
				lastX = blockX;
				lastY = blockY;
				lastZ = blockZ;
				if (blockY is < 0 or >= 256) return false;
				if (!TryReadPackedBlockState(world, blockX, blockY, blockZ, out ushort packedState)) return null;
				if (packedState >> 4 != 0) return false;
			}
			return true;
		}
		catch
		{
			return null;
		}
	}

	public BlockHitState GetBlockHitState()
	{
		try
		{
			if (movingObjectPositionLayout.KlassAddress == IntPtr.Zero || blockPosLayout.KlassAddress == IntPtr.Zero || vec3Layout.KlassAddress == IntPtr.Zero || enumLayout.KlassAddress == IntPtr.Zero)
				return BlockHitState.Empty;
			nint minecraft = ReadMinecraftInstance();
			if (minecraft == IntPtr.Zero)
				return BlockHitState.Empty;
			nint hit = ReadObjectField(minecraft, minecraftLayout, "field_71476_x");
			if (hit == IntPtr.Zero || !IsInstanceOf(hit, "net/minecraft/util/MovingObjectPosition"))
				return BlockHitState.Empty;
			nint type = ReadObjectField(hit, movingObjectPositionLayout, "field_72313_a");
			nint blockPos = ReadObjectField(hit, movingObjectPositionLayout, "field_178783_e");
			nint hitVec = ReadObjectField(hit, movingObjectPositionLayout, "field_72307_f");
			nint side = ReadObjectField(hit, movingObjectPositionLayout, "field_72310_e");
			if (type == IntPtr.Zero || blockPos == IntPtr.Zero || hitVec == IntPtr.Zero || side == IntPtr.Zero)
				return BlockHitState.Empty;
			JvmJavaField enumNameField = FindField(enumLayout, "name");
			JvmJavaField faceOrdinalField = FindField(enumLayout, "ordinal");
			JvmJavaField blockXField = FindField(blockPosLayout, "field_177962_a");
			JvmJavaField blockYField = FindField(blockPosLayout, "field_177960_b");
			JvmJavaField blockZField = FindField(blockPosLayout, "field_177961_c");
			JvmJavaField hitXField = FindField(vec3Layout, "field_72450_a");
			JvmJavaField hitYField = FindField(vec3Layout, "field_72448_b");
			JvmJavaField hitZField = FindField(vec3Layout, "field_72449_c");
			if (enumNameField.Name == null || faceOrdinalField.Name == null || blockXField.Name == null || blockYField.Name == null || blockZField.Name == null || hitXField.Name == null || hitYField.Name == null || hitZField.Name == null)
				return BlockHitState.Empty;
			if (!string.Equals(ReadJavaString(ReadObjectField(type, enumLayout, "name")), "BLOCK", StringComparison.Ordinal))
				return BlockHitState.Empty;
			if (!memory.TryReadInt32(Add(side, faceOrdinalField.Offset), out int face) ||
				!memory.TryReadInt32(Add(blockPos, blockXField.Offset), out int x) ||
				!memory.TryReadInt32(Add(blockPos, blockYField.Offset), out int y) ||
				!memory.TryReadInt32(Add(blockPos, blockZField.Offset), out int z))
				return BlockHitState.Empty;
			double hitX = ReadDoubleField(hitVec, vec3Layout, "field_72450_a");
			double hitY = ReadDoubleField(hitVec, vec3Layout, "field_72448_b");
			double hitZ = ReadDoubleField(hitVec, vec3Layout, "field_72449_c");
			if (face < 0 || face > 5 || !double.IsFinite(hitX) || !double.IsFinite(hitY) || !double.IsFinite(hitZ))
				return BlockHitState.Empty;
			return new BlockHitState(true, x, y, z, face, hitX, hitY, hitZ);
		}
		catch
		{
			return BlockHitState.Empty;
		}
	}

	public bool? IsHoldingSwordItem()
	{
		return IsHoldingItemClass("net/minecraft/item/ItemSword");
	}

	public bool? IsHoldingBowItem()
	{
		return IsHoldingItemClass("net/minecraft/item/ItemBow");
	}

	private bool? IsHoldingItemClass(string className)
	{
		try
		{
			if (inventoryPlayerLayout.KlassAddress == IntPtr.Zero || itemStackLayout.KlassAddress == IntPtr.Zero)
			{
				return null;
			}
			nint num = ReadMinecraftInstance();
			nint num2 = ReadObjectField(num, minecraftLayout, "field_71439_g");
			nint num3 = ReadObjectField(num2, playerLayout, "field_71071_by");
			if (num == IntPtr.Zero || num2 == IntPtr.Zero || num3 == IntPtr.Zero)
			{
				return null;
			}
			int num4 = ReadIntField(num3, inventoryPlayerLayout, "field_70461_c");
			if (num4 < 0 || num4 >= 9)
			{
				return false;
			}
			nint num5 = ReadObjectField(num3, inventoryPlayerLayout, "field_70462_a");
			if (num5 == IntPtr.Zero || !memory.TryReadInt32(Add(num5, 12L), out var value) || num4 >= value || !memory.TryReadUInt32(Add(num5, 16 + (long)num4 * 4L), out var value2))
			{
				return null;
			}
			nint num6 = DecodeOop(value2);
			if (num6 == IntPtr.Zero)
			{
				return false;
			}
			nint num7 = ReadObjectField(num6, itemStackLayout, "field_151002_e");
			return num7 != IntPtr.Zero && IsInstanceOf(num7, className);
		}
		catch
		{
			return null;
		}
	}

	private bool IsInstanceOf(nint instance, string targetClassName)
	{
		if (instance == IntPtr.Zero || !memory.TryReadUInt32(Add(instance, 8L), out var value) || value == 0)
		{
			return false;
		}
		nint value2 = DecodeKlass(value);
		int num = 0;
		while (value2 != IntPtr.Zero && num < 64)
		{
			if (ReadKlassName(value2).Equals(targetClassName, StringComparison.Ordinal))
			{
				return true;
			}
			if (!memory.TryReadPointer(Add(value2, klassSuperOffset), out value2))
			{
				break;
			}
			num++;
		}
		return false;
	}

	private bool TryReadPackedBlockState(nint world, int blockX, int blockY, int blockZ, out ushort packedState)
	{
		packedState = 0;
		if (worldClientLayout.KlassAddress == IntPtr.Zero || chunkProviderClientLayout.KlassAddress == IntPtr.Zero || longHashMapLayout.KlassAddress == IntPtr.Zero || longHashMapEntryLayout.KlassAddress == IntPtr.Zero || chunkLayout.KlassAddress == IntPtr.Zero || extendedBlockStorageLayout.KlassAddress == IntPtr.Zero)
		{
			return false;
		}
		nint num = ReadObjectField(world, worldClientLayout, "field_73033_b");
		nint num2 = ReadObjectField(num, chunkProviderClientLayout, "field_73236_b");
		if (num == IntPtr.Zero || num2 == IntPtr.Zero)
		{
			return false;
		}
		int num3 = blockX >> 4;
		int num4 = blockZ >> 4;
		long key = (long)(((ulong)(uint)num4 << 32) | (uint)num3);
		nint num5 = FindLoadedChunk(num, num3, num4);
		if (num5 == IntPtr.Zero)
		{
			num5 = FindChunkInLongHashMap(num2, key);
		}
		if (num5 == IntPtr.Zero)
		{
			return false;
		}
		nint num6 = ReadObjectField(num5, chunkLayout, "field_76652_q");
		if (num6 == IntPtr.Zero || !memory.TryReadInt32(Add(num6, 12L), out var value) || value <= 0 || value > 32)
		{
			return false;
		}
		int num7 = blockY >> 4;
		if (num7 >= value || !memory.TryReadUInt32(Add(num6, 16 + (long)num7 * 4L), out var value2))
		{
			return false;
		}
		nint num8 = DecodeOop(value2);
		if (num8 == IntPtr.Zero)
		{
			packedState = 0;
			return true;
		}
		nint num9 = ReadObjectField(num8, extendedBlockStorageLayout, "field_177488_d");
		if (num9 == IntPtr.Zero || !memory.TryReadInt32(Add(num9, 12L), out var value3) || value3 < 4096)
		{
			return false;
		}
		int num10 = blockX & 0xF;
		int num11 = blockY & 0xF;
		int num12 = blockZ & 0xF;
		int num13 = (num11 << 8) | (num12 << 4) | num10;
		return memory.TryReadUInt16(Add(num9, 16 + (long)num13 * 2L), out packedState);
	}

	private nint FindLoadedChunk(nint provider, int chunkX, int chunkZ)
	{
		nint num = ReadObjectField(provider, chunkProviderClientLayout, "field_73237_c");
		if (num == IntPtr.Zero)
		{
			return IntPtr.Zero;
		}
		int num2 = ReadIntField(num, arrayListLayout, "size");
		nint num3 = ReadObjectField(num, arrayListLayout, "elementData");
		if (num2 <= 0 || num2 > 4096 || num3 == IntPtr.Zero || !memory.TryReadInt32(Add(num3, 12L), out var value))
		{
			return IntPtr.Zero;
		}
		int num4 = Math.Min(num2, value);
		for (int i = 0; i < num4; i++)
		{
			if (!memory.TryReadUInt32(Add(num3, 16 + (long)i * 4L), out var value2))
			{
				continue;
			}
			nint num5 = DecodeOop(value2);
			if (num5 != IntPtr.Zero)
			{
				int num6 = ReadIntField(num5, chunkLayout, "field_76635_g");
				int num7 = ReadIntField(num5, chunkLayout, "field_76647_h");
				if (num6 == chunkX && num7 == chunkZ)
				{
					return num5;
				}
			}
		}
		return IntPtr.Zero;
	}

	private nint FindChunkInLongHashMap(nint map, long key)
	{
		nint num = ReadObjectField(map, longHashMapLayout, "field_76169_a");
		if (num == IntPtr.Zero || !memory.TryReadInt32(Add(num, 12L), out var value) || value <= 0 || value > 1048576)
		{
			return IntPtr.Zero;
		}
		int num2 = HashLongKey(key);
		int num3 = ReadIntField(map, longHashMapLayout, "field_180201_c");
		int num4 = ((num3 > 0) ? (num2 & num3) : (num2 & (value - 1)));
		if ((uint)num4 >= (uint)value || !memory.TryReadUInt32(Add(num, 16 + (long)num4 * 4L), out var value2))
		{
			return IntPtr.Zero;
		}
		nint num5 = DecodeOop(value2);
		int num6 = 0;
		while (num5 != IntPtr.Zero && num6 < 256)
		{
			JvmJavaField jvmJavaField = FindField(longHashMapEntryLayout, "field_76150_a");
			if (jvmJavaField.Name == null || !memory.TryReadInt64(Add(num5, jvmJavaField.Offset), out var value3))
			{
				return IntPtr.Zero;
			}
			if (value3 == key)
			{
				return ReadObjectField(num5, longHashMapEntryLayout, "field_76148_b");
			}
			num5 = ReadObjectField(num5, longHashMapEntryLayout, "field_76149_c");
			num6++;
		}
		return IntPtr.Zero;
	}

	private static int HashLongKey(long key)
	{
		int num = (int)(key ^ (key >>> 32));
		num ^= (num >>> 20) ^ (num >>> 12);
		return num ^ (num >>> 7) ^ (num >>> 4);
	}

	private static int FloorToInt(double value)
	{
		int num = (int)value;
		if (!(value < (double)num))
		{
			return num;
		}
		return num - 1;
	}

	private nint ReadMinecraftInstance()
	{
		nint value = ReadClassMirror(minecraftLayout.KlassAddress);
		if (value == IntPtr.Zero)
		{
			return runtime.Minecraft;
		}
		JvmJavaField jvmJavaField = FindField(minecraftLayout, "field_71432_P");
		if (jvmJavaField.Name == null || !memory.TryReadUInt32(Add(value, jvmJavaField.Offset), out var value2))
		{
			return runtime.Minecraft;
		}
		nint instance = DecodeOop(value2);
		return instance != IntPtr.Zero ? instance : runtime.Minecraft;
	}

	private bool TryReadRenderMatrices(out RenderMatrices matrices)
	{
		matrices = default(RenderMatrices);
		nint buffer = ReadStaticObjectField(activeRenderInfoLayout, "field_178812_b");
		nint buffer2 = ReadStaticObjectField(activeRenderInfoLayout, "field_178813_c");
		nint buffer3 = ReadStaticObjectField(activeRenderInfoLayout, "field_178814_a");
		if (!TryReadDirectBufferBytes(buffer, 64, 4, out byte[] bytes) || !TryReadDirectBufferBytes(buffer2, 64, 4, out byte[] bytes2) || !TryReadDirectBufferBytes(buffer3, 16, 4, out byte[] bytes3))
		{
			return false;
		}
		float[] array = new float[16];
		float[] array2 = new float[16];
		int[] array3 = new int[4];
		Buffer.BlockCopy(bytes, 0, array, 0, bytes.Length);
		Buffer.BlockCopy(bytes2, 0, array2, 0, bytes2.Length);
		Buffer.BlockCopy(bytes3, 0, array3, 0, bytes3.Length);
		if (array.Any((float value) => !float.IsFinite(value)) || array2.Any((float value) => !float.IsFinite(value)) || array3[2] < 100 || array3[3] < 100 || array3[2] > 16384 || array3[3] > 16384)
		{
			return false;
		}
		matrices = new RenderMatrices(array, array2, array3);
		return true;
	}

	private nint ReadStaticObjectField(JvmClassLayout layout, string name)
	{
		nint value = ReadClassMirror(layout.KlassAddress);
		if (value == IntPtr.Zero)
		{
			return IntPtr.Zero;
		}
		JvmJavaField jvmJavaField = FindField(layout, name);
		if (jvmJavaField.Name == null || !jvmJavaField.IsStatic || !memory.TryReadUInt32(Add(value, jvmJavaField.Offset), out var value2))
		{
			return IntPtr.Zero;
		}
		return DecodeOop(value2);
	}

	private nint ReadClassMirror(nint klass)
	{
		if (klass == IntPtr.Zero || !memory.TryReadPointer(Add(klass, klassMirrorOffset), out var mirror) || mirror == IntPtr.Zero)
		{
			return IntPtr.Zero;
		}
		if (klassMirrorIsOopHandle && (!memory.TryReadPointer(mirror, out mirror) || mirror == IntPtr.Zero))
		{
			return IntPtr.Zero;
		}
		return mirror;
	}

	private bool TryReadDirectBufferBytes(nint buffer, int byteCount, int elementSize, out byte[] bytes)
	{
		bytes = Array.Empty<byte>();
		if (buffer == IntPtr.Zero)
		{
			return false;
		}
		JvmJavaField jvmJavaField = FindField(bufferLayout, "address");
		JvmJavaField jvmJavaField2 = FindField(bufferLayout, "position");
		if (jvmJavaField.Name == null || !memory.TryReadInt64(Add(buffer, jvmJavaField.Offset), out var value) || value <= 65536)
		{
			return false;
		}
		int num = ((jvmJavaField2.Name != null && memory.TryReadInt32(Add(buffer, jvmJavaField2.Offset), out var value2)) ? Math.Clamp(value2, 0, 1024) : 0);
		return memory.TryReadBytes(new IntPtr(value + (long)num * (long)elementSize), byteCount, out bytes);
	}

	private bool ReadEntityTransform(nint entity, float partialTicks, out EntityTransform transform)
	{
		transform = default(EntityTransform);
		double num = ReadDoubleField(entity, entityLayout, "field_70165_t");
		double num2 = ReadDoubleField(entity, entityLayout, "field_70163_u");
		double num3 = ReadDoubleField(entity, entityLayout, "field_70161_v");
		double num4 = ReadDoubleField(entity, entityLayout, "field_70142_S");
		double num5 = ReadDoubleField(entity, entityLayout, "field_70137_T");
		double num6 = ReadDoubleField(entity, entityLayout, "field_70136_U");
		float num7 = ReadFloatField(entity, entityLayout, "field_70130_N");
		float num8 = ReadFloatField(entity, entityLayout, "field_70131_O");
		if (!double.IsFinite(num) || !double.IsFinite(num2) || !double.IsFinite(num3) || num7 <= 0f || num7 > 20f || num8 <= 0f || num8 > 20f)
		{
			return false;
		}
		if (double.IsFinite(num4) && double.IsFinite(num5) && double.IsFinite(num6))
		{
			num = num4 + (num - num4) * (double)partialTicks;
			num2 = num5 + (num2 - num5) * (double)partialTicks;
			num3 = num6 + (num3 - num6) * (double)partialTicks;
		}
		transform = new EntityTransform(num, num2, num3, num7, num8);
		return true;
	}

	private static bool IsPlayerClassName(string className)
	{
		return !string.IsNullOrWhiteSpace(className) &&
			(className.Contains("EntityOtherPlayerMP", StringComparison.OrdinalIgnoreCase) ||
			 className.Contains("AbstractClientPlayer", StringComparison.OrdinalIgnoreCase) ||
			 className.Contains("EntityPlayer", StringComparison.OrdinalIgnoreCase));
	}

	private EntityKind? ClassifyEntity(nint entity, out string className, out bool isFireball)
	{
		className = string.Empty;
		isFireball = false;
		if (!memory.TryReadUInt32(Add(entity, 8L), out var value) || value == 0)
		{
			return null;
		}
		nint value2 = DecodeKlass(value);
		long klassAddress = ((IntPtr)value2).ToInt64();
		if (entityClassCache.TryGetValue(klassAddress, out var value3))
		{
			className = value3.ClassName;
			isFireball = value3.IsFireball;
			return value3.Kind;
		}
		EntityKind? entityKind = null;
		bool foundLivingBase = false;
		int num = 0;
		while (value2 != IntPtr.Zero && num < 64)
		{
			string text = ReadKlassName(value2);
			if (num == 0)
			{
				className = text;
			}
			if (IsPlayerClassName(text))
			{
				entityKind = EntityKind.Player;
				break;
			}
			if (text == "net/minecraft/entity/EntityLivingBase")
			{
				foundLivingBase = true;
			}
			if (text == "net/minecraft/entity/projectile/EntityFireball")
			{
				isFireball = true;
			}
			if (!memory.TryReadPointer(Add(value2, klassSuperOffset), out value2))
			{
				break;
			}
			num++;
		}
		if (entityKind == null && foundLivingBase)
		{
			entityKind = EntityKind.Mob;
		}
		if (!string.IsNullOrEmpty(className))
		{
			entityClassCache[klassAddress] = new EntityClassInfo(entityKind, isFireball, className);
		}
		return entityKind;
	}

	private string ReadPlayerName(nint entity)
	{
		if (playerNameCache.TryGetValue(((IntPtr)entity).ToInt64(), out string? value))
		{
			return value;
		}
		nint owner = ReadObjectField(entity, playerLayout, "field_146106_i");
		nint javaString = ReadObjectField(owner, gameProfileLayout, "name");
		string text = ReadJavaString(javaString);
		if (!string.IsNullOrWhiteSpace(text))
		{
			playerNameCache[((IntPtr)entity).ToInt64()] = text;
		}
		return text;
	}

	private string ReadJavaString(nint javaString)
	{
		if (javaString == IntPtr.Zero)
		{
			return string.Empty;
		}
		JvmJavaField valueField = FindField(stringLayout, "value");
		if (valueField.Name == null)
		{
			return string.Empty;
		}
		nint num = ReadObjectField(javaString, stringLayout, "value");
		if (num == IntPtr.Zero || !memory.TryReadInt32(Add(num, 12L), out var value) || value <= 0 || value > 512)
		{
			return string.Empty;
		}
		if (valueField.Signature == "[B")
		{
			if (!memory.TryReadBytes(Add(num, 16L), value, out byte[] compactData))
			{
				return string.Empty;
			}
			JvmJavaField coderField = FindField(stringLayout, "coder");
			byte coder = 0;
			if (coderField.Name != null && memory.TryReadBytes(Add(javaString, coderField.Offset), 1, out byte[] coderData) && coderData.Length == 1)
			{
				coder = coderData[0];
			}
			return (coder == 0 ? Encoding.Latin1.GetString(compactData) : Encoding.Unicode.GetString(compactData)).TrimEnd('\0');
		}
		if (!memory.TryReadBytes(Add(num, 16L), value * 2, out byte[] data))
		{
			return string.Empty;
		}
		return Encoding.Unicode.GetString(data).TrimEnd('\0');
	}

	private float ReadLivingHealth(nint entity)
	{
		nint owner = ReadObjectField(entity, entityLayout, "field_70180_af");
		if (klassMirrorIsOopHandle)
		{
			float lunarHealth = ReadLunarWatcherHealth(owner);
			if (lunarHealth >= 0f)
			{
				return lunarHealth;
			}
		}
		nint owner2 = ReadObjectField(owner, dataWatcherLayout, "field_75695_b");
		nint num = ReadObjectField(owner2, hashMapLayout, "table");
		if (num == IntPtr.Zero || !memory.TryReadInt32(Add(num, 12L), out var value) || value <= 0 || value > 1024)
		{
			return 0f;
		}
		for (int i = 0; i < value; i++)
		{
			if (!memory.TryReadUInt32(Add(num, 16 + (long)i * 4L), out var value2))
			{
				continue;
			}
			nint num2 = DecodeOop(value2);
			int num3 = 0;
			while (num2 != IntPtr.Zero && num3 < 32)
			{
				nint owner3 = ReadObjectField(num2, hashMapNodeLayout, "key");
				if (ReadIntField(owner3, integerLayout, "value") == 6)
				{
					nint owner4 = ReadObjectField(num2, hashMapNodeLayout, "value");
					nint owner5 = ReadObjectField(owner4, watchableObjectLayout, "field_75677_c");
					float num4 = ReadFloatField(owner5, floatLayout, "value");
					if (!float.IsFinite(num4) || !(num4 >= 0f) || !(num4 < 100000f))
					{
						return 0f;
					}
					return num4;
				}
				num2 = ReadObjectField(num2, hashMapNodeLayout, "next");
				num3++;
			}
		}
		return 0f;
	}

	private float ReadLunarWatcherHealth(nint watcher)
	{
		if (watcher == IntPtr.Zero || watchableObjectLayout.KlassAddress == IntPtr.Zero)
		{
			return -1f;
		}
		float health = -1f;
		for (long offset = 12L; offset <= 512L; offset += 4L)
		{
			if (!memory.TryReadUInt32(Add(watcher, offset), out var narrow) || narrow == 0)
			{
				continue;
			}
			nint candidate = DecodeOop(narrow);
			if (!IsInstanceOf(candidate, "net/minecraft/entity/DataWatcher$WatchableObject"))
			{
				continue;
			}
			int id = ReadIntField(candidate, watchableObjectLayout, "field_75678_a");
			if (id != 6 && id != 3)
			{
				continue;
			}
			nint value = ReadObjectField(candidate, watchableObjectLayout, "field_75677_c");
			if (value == IntPtr.Zero || !IsInstanceOf(value, "java/lang/Float"))
			{
				continue;
			}
			float currentHealth = ReadFloatField(value, floatLayout, "value");
			if (!float.IsFinite(currentHealth) || currentHealth < 0f || currentHealth > 100000f)
			{
				continue;
			}
			if (id == 6)
			{
				return currentHealth;
			}
			if (health < 0f)
			{
				health = currentHealth;
			}
		}
		return health;
	}

	private float ReadCachedLivingHealth(nint entity)
	{
		long key = ((IntPtr)entity).ToInt64();
		long elapsedMilliseconds = cacheClock.ElapsedMilliseconds;
		if (healthCache.TryGetValue(key, out var value) && elapsedMilliseconds - value.ReadAtMs < 100)
		{
			return value.Value;
		}
		float num = ReadLivingHealth(entity);
		healthCache[key] = new TimedHealth(num, elapsedMilliseconds);
		return num;
	}

	private string ReadKlassName(nint klass)
	{
		if (!memory.TryReadPointer(Add(klass, klassNameOffset), out var value) || value == IntPtr.Zero || !memory.TryReadUInt16(Add(value, symbolLengthOffset), out var value2) || value2 == 0 || value2 > 1024 || !memory.TryReadBytes(Add(value, symbolBodyOffset), value2, out byte[] data))
		{
			return string.Empty;
		}
		return Encoding.UTF8.GetString(data);
	}

	private static bool ProjectBounds(EntityTransform entity, RenderOrigin renderOrigin, RenderMatrices matrices, Size viewport, out RectangleF bounds)
	{
		bounds = RectangleF.Empty;
		double num = Math.Max(0.1, (double)entity.Width * 0.5);
		double num2 = double.PositiveInfinity;
		double num3 = double.PositiveInfinity;
		double num4 = double.NegativeInfinity;
		double num5 = double.NegativeInfinity;
		int num6 = 0;
		for (int i = -1; i <= 1; i += 2)
		{
			for (int j = 0; j <= 1; j++)
			{
				for (int k = -1; k <= 1; k += 2)
				{
					double num7 = entity.X + num * (double)i;
					double num8 = entity.Y + (double)(entity.Height * (float)j);
					if (ProjectPoint(z: entity.Z + num * (double)k - renderOrigin.Z, x: num7 - renderOrigin.X, y: num8 - renderOrigin.Y, matrices: matrices, viewport: viewport, point: out var point))
					{
						num2 = Math.Min(num2, point.X);
						num3 = Math.Min(num3, point.Y);
						num4 = Math.Max(num4, point.X);
						num5 = Math.Max(num5, point.Y);
						num6++;
					}
				}
			}
		}
		if (num6 < 4 || !double.IsFinite(num2) || !double.IsFinite(num3))
		{
			return false;
		}
		bounds = RectangleF.FromLTRB((float)num2, (float)num3, (float)num4, (float)num5);
		if (bounds.Width >= 1f && bounds.Height >= 1f && bounds.Right >= -40f && bounds.Left <= (float)(viewport.Width + 40) && bounds.Bottom >= -40f)
		{
			return bounds.Top <= (float)(viewport.Height + 40);
		}
		return false;
	}

	private static bool ProjectPoint(double x, double y, double z, RenderMatrices matrices, Size viewport, out PointF point)
	{
		point = PointF.Empty;
		double num = (double)matrices.ModelView[0] * x + (double)matrices.ModelView[4] * y + (double)matrices.ModelView[8] * z + (double)matrices.ModelView[12];
		double num2 = (double)matrices.ModelView[1] * x + (double)matrices.ModelView[5] * y + (double)matrices.ModelView[9] * z + (double)matrices.ModelView[13];
		double num3 = (double)matrices.ModelView[2] * x + (double)matrices.ModelView[6] * y + (double)matrices.ModelView[10] * z + (double)matrices.ModelView[14];
		double num4 = (double)matrices.ModelView[3] * x + (double)matrices.ModelView[7] * y + (double)matrices.ModelView[11] * z + (double)matrices.ModelView[15];
		double num5 = (double)matrices.Projection[0] * num + (double)matrices.Projection[4] * num2 + (double)matrices.Projection[8] * num3 + (double)matrices.Projection[12] * num4;
		double num6 = (double)matrices.Projection[1] * num + (double)matrices.Projection[5] * num2 + (double)matrices.Projection[9] * num3 + (double)matrices.Projection[13] * num4;
		double num7 = (double)matrices.Projection[2] * num + (double)matrices.Projection[6] * num2 + (double)matrices.Projection[10] * num3 + (double)matrices.Projection[14] * num4;
		double num8 = (double)matrices.Projection[3] * num + (double)matrices.Projection[7] * num2 + (double)matrices.Projection[11] * num3 + (double)matrices.Projection[15] * num4;
		if (!double.IsFinite(num8) || num8 <= 0.0001)
		{
			return false;
		}
		double num9 = num5 / num8;
		double num10 = num6 / num8;
		double num11 = num7 / num8;
		if (num11 < -1.0 || num11 > 1.0)
		{
			return false;
		}
		int[] viewport2 = matrices.Viewport;
		double num12 = (double)viewport2[0] + (num9 + 1.0) * (double)viewport2[2] * 0.5;
		double num13 = (double)viewport2[1] + (num10 + 1.0) * (double)viewport2[3] * 0.5;
		double num14 = (double)viewport.Width / (double)viewport2[2];
		double num15 = (double)viewport.Height / (double)viewport2[3];
		point = new PointF((float)((num12 - (double)viewport2[0]) * num14), (float)((double)viewport.Height - (num13 - (double)viewport2[1]) * num15));
		if (float.IsFinite(point.X))
		{
			return float.IsFinite(point.Y);
		}
		return false;
	}

	private nint ReadObjectField(nint owner, JvmClassLayout layout, string name)
	{
		JvmJavaField jvmJavaField = FindField(layout, name);
		if (jvmJavaField.Name == null || owner == IntPtr.Zero || !memory.TryReadUInt32(Add(owner, jvmJavaField.Offset), out var value))
		{
			return IntPtr.Zero;
		}
		return DecodeOop(value);
	}

	private int ReadIntField(nint owner, JvmClassLayout layout, string name)
	{
		JvmJavaField jvmJavaField = FindField(layout, name);
		if (jvmJavaField.Name == null || !memory.TryReadInt32(Add(owner, jvmJavaField.Offset), out var value))
		{
			return 0;
		}
		return value;
	}

	private float ReadFloatField(nint owner, JvmClassLayout layout, string name)
	{
		JvmJavaField jvmJavaField = FindField(layout, name);
		if (jvmJavaField.Name == null || !memory.TryReadSingle(Add(owner, jvmJavaField.Offset), out var value))
		{
			return 0f;
		}
		return value;
	}

	private double ReadDoubleField(nint owner, JvmClassLayout layout, string name)
	{
		JvmJavaField jvmJavaField = FindField(layout, name);
		if (jvmJavaField.Name == null || !memory.TryReadDouble(Add(owner, jvmJavaField.Offset), out var value))
		{
			return double.NaN;
		}
		return value;
	}

	private bool ReadBooleanField(nint owner, JvmClassLayout layout, string name)
	{
		JvmJavaField jvmJavaField = FindField(layout, name);
		if (jvmJavaField.Name != null && memory.TryReadBytes(Add(owner, jvmJavaField.Offset), 1, out byte[] data) && data.Length == 1)
		{
			return data[0] != 0;
		}
		return false;
	}

	private nint DecodeOop(uint narrow)
	{
		if (narrow != 0)
		{
			return new IntPtr((long)(runtime.NarrowOopBase + ((ulong)narrow << runtime.NarrowOopShift)));
		}
		return IntPtr.Zero;
	}

	private nint DecodeKlass(uint narrow)
	{
		if (narrow != 0)
		{
			return new IntPtr((long)(runtime.NarrowKlassBase + ((ulong)narrow << runtime.NarrowKlassShift)));
		}
		return IntPtr.Zero;
	}

	private static JvmClassLayout FindLayout(HotSpotDiagnostic diagnostic, string name)
	{
		return diagnostic.ClassLayouts.FirstOrDefault((JvmClassLayout layout) => layout.Name == name);
	}

	private static JvmJavaField FindField(JvmClassLayout layout, string name)
	{
		return layout.Fields?.FirstOrDefault((JvmJavaField field) => field.Name == name) ?? default(JvmJavaField);
	}

	private static long FindVmOffset(HotSpotDiagnostic diagnostic, string type, string field, long fallback)
	{
		VmStructField vmStructField = diagnostic.KeyFields.FirstOrDefault((VmStructField item) => item.TypeName == type && item.FieldName == field);
		if (vmStructField.TypeName != null)
		{
			return vmStructField.Offset;
		}
		return fallback;
	}

	private static nint Add(nint address, long offset)
	{
		return new IntPtr(((IntPtr)address).ToInt64() + offset);
	}

	private static bool ProjectBox(CachedBlockBox box, RenderOrigin renderOrigin, RenderMatrices matrices, Size viewport, out PointF[] corners)
	{
		corners = new PointF[8];
		for (int index = 0; index < 8; index++)
		{
			double x = ((index & 1) == 0 ? box.MinX : box.MaxX) - renderOrigin.X;
			double y = ((index & 2) == 0 ? box.MinY : box.MaxY) - renderOrigin.Y;
			double z = ((index & 4) == 0 ? box.MinZ : box.MaxZ) - renderOrigin.Z;
			if (!ProjectPoint(x, y, z, matrices, viewport, out corners[index]))
			{
				corners = Array.Empty<PointF>();
				return false;
			}
		}
		return true;
	}
}
