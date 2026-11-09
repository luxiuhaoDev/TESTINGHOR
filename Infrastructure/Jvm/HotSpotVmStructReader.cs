using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace McPetOverlay;

internal sealed class HotSpotVmStructReader
{
	private readonly MinecraftMemoryReader memory;

	internal HotSpotVmStructReader(MinecraftMemoryReader memory)
	{
		this.memory = memory;
	}

	internal HotSpotDiagnostic Inspect()
	{
		if (!memory.TryGetModule("jvm.dll", out var module))
		{
			return HotSpotDiagnostic.Failed("没有找到 jvm.dll 模块");
		}
		IReadOnlyDictionary<string, uint> readOnlyDictionary;
		try
		{
			readOnlyDictionary = PeExportReader.ReadExports(module.FilePath);
		}
		catch (Exception ex)
		{
			return HotSpotDiagnostic.Failed("解析 jvm.dll 导出表失败：" + ex.Message);
		}
		string[] array = new string[8] { "gHotSpotVMStructs", "gHotSpotVMStructEntryArrayStride", "gHotSpotVMStructEntryTypeNameOffset", "gHotSpotVMStructEntryFieldNameOffset", "gHotSpotVMStructEntryTypeStringOffset", "gHotSpotVMStructEntryIsStaticOffset", "gHotSpotVMStructEntryOffsetOffset", "gHotSpotVMStructEntryAddressOffset" };
		foreach (string text in array)
		{
			if (!readOnlyDictionary.ContainsKey(text))
			{
				return HotSpotDiagnostic.Failed("jvm.dll 缺少导出变量：" + text);
			}
		}
		if (!ReadExportPointer(module, readOnlyDictionary, "gHotSpotVMStructs", out var value) || !ReadExportValue(module, readOnlyDictionary, "gHotSpotVMStructEntryArrayStride", out var value2) || !ReadExportValue(module, readOnlyDictionary, "gHotSpotVMStructEntryTypeNameOffset", out var value3) || !ReadExportValue(module, readOnlyDictionary, "gHotSpotVMStructEntryFieldNameOffset", out var value4) || !ReadExportValue(module, readOnlyDictionary, "gHotSpotVMStructEntryTypeStringOffset", out var value5) || !ReadExportValue(module, readOnlyDictionary, "gHotSpotVMStructEntryIsStaticOffset", out var value6) || !ReadExportValue(module, readOnlyDictionary, "gHotSpotVMStructEntryOffsetOffset", out var value7) || !ReadExportValue(module, readOnlyDictionary, "gHotSpotVMStructEntryAddressOffset", out var value8))
		{
			return HotSpotDiagnostic.Failed("无法读取 VMStructs 表描述信息");
		}
		if (value == IntPtr.Zero || value2 < 24 || value2 > 256)
		{
			return HotSpotDiagnostic.Failed($"VMStructs 表参数异常：table=0x{((IntPtr)value).ToInt64():X}, stride={value2}");
		}
		List<VmStructField> list = new List<VmStructField>();
		for (int j = 0; j < 10000; j++)
		{
			nint address = Add(value, value2 * j);
			if (!memory.TryReadPointer(Add(address, value3), out var value9) || value9 == IntPtr.Zero || !memory.TryReadCString(value9, 256, out string value10) || string.IsNullOrWhiteSpace(value10))
			{
				break;
			}
			memory.TryReadPointer(Add(address, value4), out var value11);
			memory.TryReadPointer(Add(address, value5), out var value12);
			memory.TryReadCString(value11, 256, out string value13);
			memory.TryReadCString(value12, 256, out string value14);
			memory.TryReadBytes(Add(address, value6), 4, out byte[] data);
			bool isStatic = data.Length == 4 && BitConverter.ToInt32(data, 0) != 0;
			memory.TryReadInt64(Add(address, value7), out var value15);
			memory.TryReadPointer(Add(address, value8), out var value16);
			list.Add(new VmStructField(value10, value13, value14, isStatic, value15, value16));
		}
		if (list.Count == 0)
		{
			return HotSpotDiagnostic.Failed("VMStructs 表为空或无法访问");
		}
		string[] targets = new string[16]
		{
			"ClassLoaderDataGraph", "ClassLoaderData", "Klass", "InstanceKlass", "Symbol", "ConstantPool", "Universe", "CompressedOops", "CompressedKlassPointers", "SystemDictionary", "Dictionary",
			"DictionaryEntry", "HashtableBucket", "HashtableEntry", "BasicHashtableEntry", "BasicHashtable"
		};
		VmStructField[] keyFields = list.Where((VmStructField field) => targets.Any((string target) => field.TypeName.Equals(target, StringComparison.Ordinal) || field.TypeName.StartsWith(target + "::", StringComparison.Ordinal) || field.TypeName.StartsWith(target + "<", StringComparison.Ordinal))).Where(delegate(VmStructField field)
		{
			bool flag = !field.TypeName.Equals("SystemDictionary", StringComparison.Ordinal);
			if (!flag)
			{
				string fieldName = field.FieldName;
				bool flag2 = ((fieldName == "_dictionary" || fieldName == "_shared_dictionary") ? true : false);
				flag = flag2;
			}
			return flag;
		}).OrderBy(delegate(VmStructField field)
		{
			int num = Array.IndexOf(targets, field.TypeName);
			return (num >= 0) ? num : int.MaxValue;
		})
			.ThenBy((VmStructField field) => field.FieldName)
			.Take(300)
			.ToArray();
		IReadOnlyList<LoadedJvmClass> readOnlyList = EnumerateTargetClasses(list);
		IReadOnlyList<JvmJavaField> readOnlyList2 = Array.Empty<JvmJavaField>();
		List<JvmClassLayout> list2 = new List<JvmClassLayout>();
		long constantPoolSize = ReadVmTypeSize(module, readOnlyDictionary, "ConstantPool", 64L);
		LoadedJvmClass minecraftClass = readOnlyList.FirstOrDefault((LoadedJvmClass item) => item.Name == "net/minecraft/client/Minecraft");
		if (minecraftClass.KlassAddress != IntPtr.Zero)
		{
			readOnlyList2 = ReadJavaFields(minecraftClass.KlassAddress, list, constantPoolSize);
		}
		foreach (LoadedJvmClass item in readOnlyList)
		{
			if (!item.Name.StartsWith("net/minecraft/client/Minecraft$", StringComparison.Ordinal))
			{
				IReadOnlyList<JvmJavaField> fields = ((item.Name == "net/minecraft/client/Minecraft") ? readOnlyList2 : ReadJavaFields(item.KlassAddress, list, constantPoolSize));
				list2.Add(new JvmClassLayout(item.Name, item.KlassAddress, fields));
			}
		}
		MinecraftRuntimePointers runtimePointers = ReadMinecraftRuntime(minecraftClass, readOnlyList2, list);
		return new HotSpotDiagnostic(Success: true, module.BaseAddress, module.FilePath, list.Count, keyFields, readOnlyList, readOnlyList2, list2, runtimePointers, null);
	}

	private MinecraftRuntimePointers ReadMinecraftRuntime(LoadedJvmClass minecraftClass, IReadOnlyList<JvmJavaField> minecraftFields, IReadOnlyList<VmStructField> vmFields)
	{
		if (minecraftClass.KlassAddress == IntPtr.Zero)
		{
			return default(MinecraftRuntimePointers);
		}
		VmStructField vmStructField = FindStaticField(vmFields, "Universe", "_narrow_oop._base", "CompressedOops", "_narrow_oop._base");
		VmStructField vmStructField2 = FindStaticField(vmFields, "Universe", "_narrow_oop._shift", "CompressedOops", "_narrow_oop._shift");
		VmStructField vmStructField3 = FindStaticField(vmFields, "Universe", "_narrow_klass._base", "CompressedKlassPointers", "_narrow_klass._base");
		VmStructField vmStructField4 = FindStaticField(vmFields, "Universe", "_narrow_klass._shift", "CompressedKlassPointers", "_narrow_klass._shift");
		memory.TryReadUInt64(vmStructField.Address, out var value);
		memory.TryReadInt32(vmStructField2.Address, out var value2);
		memory.TryReadUInt64(vmStructField3.Address, out var value3);
		memory.TryReadInt32(vmStructField4.Address, out var value4);
		VmStructField mirrorField = vmFields.FirstOrDefault((VmStructField field) => field.TypeName == "Klass" && field.FieldName == "_java_mirror");
		long offset = mirrorField.TypeName == null ? 104L : mirrorField.Offset;
		if (!memory.TryReadPointer(Add(minecraftClass.KlassAddress, offset), out var value5) || value5 == IntPtr.Zero)
		{
			return new MinecraftRuntimePointers(value, value2, value3, value4, value5, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
		}
		if (mirrorField.TypeString != null && mirrorField.TypeString.Contains("OopHandle", StringComparison.Ordinal))
		{
			if (!memory.TryReadPointer(value5, out value5))
			{
				return new MinecraftRuntimePointers(value, value2, value3, value4, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
			}
			if (value5 == IntPtr.Zero)
			{
				return new MinecraftRuntimePointers(value, value2, value3, value4, value5, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
			}
		}
		JvmJavaField jvmJavaField = minecraftFields.FirstOrDefault((JvmJavaField field) => field.Name == "field_71432_P");
		if (jvmJavaField.Name == null || !memory.TryReadUInt32(Add(value5, jvmJavaField.Offset), out var value6))
		{
			return new MinecraftRuntimePointers(value, value2, value3, value4, value5, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
		}
		nint num = DecodeNarrowOop(value6, value, value2);
		nint world = ReadObjectField(num, minecraftFields, "field_71441_e", value, value2);
		nint player = ReadObjectField(num, minecraftFields, "field_71439_g", value, value2);
		nint renderManager = ReadObjectField(num, minecraftFields, "field_175616_W", value, value2);
		return new MinecraftRuntimePointers(value, value2, value3, value4, value5, num, world, player, renderManager);
	}

	private nint ReadObjectField(nint owner, IReadOnlyList<JvmJavaField> fields, string name, ulong oopBase, int oopShift)
	{
		if (owner == IntPtr.Zero)
		{
			return IntPtr.Zero;
		}
		JvmJavaField jvmJavaField = fields.FirstOrDefault((JvmJavaField item) => item.Name == name);
		if (jvmJavaField.Name == null || !memory.TryReadUInt32(Add(owner, jvmJavaField.Offset), out var value))
		{
			return IntPtr.Zero;
		}
		return DecodeNarrowOop(value, oopBase, oopShift);
	}

	private static nint DecodeNarrowOop(uint narrow, ulong oopBase, int oopShift)
	{
		if (narrow == 0)
		{
			return IntPtr.Zero;
		}
		return new IntPtr((long)(oopBase + ((ulong)narrow << oopShift)));
	}

	private IReadOnlyList<JvmJavaField> ReadJavaFields(nint klass, IReadOnlyList<VmStructField> fields, long constantPoolSize)
	{
		long offset = FindOffset(fields, "InstanceKlass", "_fields", 432L);
		long offset2 = FindOffset(fields, "InstanceKlass", "_constants", 216L);
		long symbolLengthOffset = FindOffset(fields, "Symbol", "_length", 0L);
		long symbolBodyOffset = FindOffset(fields, "Symbol", "_body", 8L);
		if (!memory.TryReadPointer(Add(klass, offset), out var value) || !memory.TryReadPointer(Add(klass, offset2), out var value2) || value == IntPtr.Zero || value2 == IntPtr.Zero || !memory.TryReadInt32(value, out var value3) || value3 <= 0 || value3 > 6000)
		{
			return Array.Empty<JvmJavaField>();
		}
		int num = value3 / 6;
		List<JvmJavaField> list = new List<JvmJavaField>(num);
		for (int i = 0; i < num; i++)
		{
			nint address = Add(value, 4 + (long)(i * 6) * 2L);
			if (memory.TryReadUInt16(address, out var value4) && memory.TryReadUInt16(Add(address, 2L), out var value5) && memory.TryReadUInt16(Add(address, 4L), out var value6) && memory.TryReadUInt16(Add(address, 8L), out var value7) && memory.TryReadUInt16(Add(address, 10L), out var value8))
			{
				string name = ReadConstantPoolSymbol(value2, constantPoolSize, value5, symbolLengthOffset, symbolBodyOffset);
				string signature = ReadConstantPoolSymbol(value2, constantPoolSize, value6, symbolLengthOffset, symbolBodyOffset);
				uint num2 = (uint)(value7 | (value8 << 16));
				int offset3 = (int)(num2 >> 2);
				list.Add(new JvmJavaField(name, signature, value4, offset3, num2));
			}
		}
		return list;
	}

	private string ReadConstantPoolSymbol(nint constantPool, long constantPoolSize, int index, long symbolLengthOffset, long symbolBodyOffset)
	{
		if (index <= 0 || index > 65535)
		{
			return string.Empty;
		}
		if (!memory.TryReadPointer(Add(constantPool, constantPoolSize + index * IntPtr.Size), out var value) || value == IntPtr.Zero || !memory.TryReadUInt16(Add(value, symbolLengthOffset), out var value2) || value2 == 0 || value2 > 4096 || !memory.TryReadBytes(Add(value, symbolBodyOffset), value2, out byte[] data))
		{
			return string.Empty;
		}
		return Encoding.UTF8.GetString(data);
	}

	private long ReadVmTypeSize(RemoteModule module, IReadOnlyDictionary<string, uint> exports, string wantedType, long fallback)
	{
		string[] array = new string[4] { "gHotSpotVMTypes", "gHotSpotVMTypeEntryArrayStride", "gHotSpotVMTypeEntryTypeNameOffset", "gHotSpotVMTypeEntrySizeOffset" };
		if (array.Any((string name) => !exports.ContainsKey(name)))
		{
			return fallback;
		}
		if (!ReadExportPointer(module, exports, array[0], out var value) || !ReadExportValue(module, exports, array[1], out var value2) || !ReadExportValue(module, exports, array[2], out var value3) || !ReadExportValue(module, exports, array[3], out var value4) || value == IntPtr.Zero || value2 <= 0 || value2 > 256)
		{
			return fallback;
		}
		for (int num = 0; num < 5000; num++)
		{
			nint address = Add(value, num * value2);
			if (!memory.TryReadPointer(Add(address, value3), out var value5) || value5 == IntPtr.Zero || !memory.TryReadCString(value5, 256, out string value6))
			{
				break;
			}
			if (value6.Equals(wantedType, StringComparison.Ordinal))
			{
				if (!memory.TryReadInt64(Add(address, value4), out var value7) || value7 <= 0 || value7 >= 4096)
				{
					return fallback;
				}
				return value7;
			}
		}
		return fallback;
	}

	private IReadOnlyList<LoadedJvmClass> EnumerateTargetClasses(IReadOnlyList<VmStructField> fields)
	{
		IReadOnlyList<LoadedJvmClass> dictionaryClasses = EnumerateSystemDictionaryClasses(fields);
		if (dictionaryClasses.Count > 0)
		{
			return dictionaryClasses;
		}
		return EnumerateClassLoaderDataGraphClasses(fields);
	}

	private static VmStructField FindStaticField(IReadOnlyList<VmStructField> fields, string legacyType, string legacyName, string modernType, string modernName)
	{
		VmStructField field = fields.FirstOrDefault((VmStructField item) => item.TypeName == legacyType && item.FieldName == legacyName);
		if (field.TypeName != null)
		{
			return field;
		}
		return fields.FirstOrDefault((VmStructField item) => item.TypeName == modernType && item.FieldName == modernName);
	}

	private IReadOnlyList<LoadedJvmClass> EnumerateSystemDictionaryClasses(IReadOnlyList<VmStructField> fields)
	{
		VmStructField vmStructField = fields.FirstOrDefault((VmStructField field) => field.TypeName == "SystemDictionary" && field.FieldName == "_dictionary");
		long offset = FindOffset(fields, "Klass", "_name", 16L);
		long offset2 = FindOffset(fields, "Symbol", "_length", 0L);
		long offset3 = FindOffset(fields, "Symbol", "_body", 8L);
		long offset4 = FindTemplateOffset(fields, "BasicHashtable", "_table_size", 0L);
		long offset5 = FindTemplateOffset(fields, "BasicHashtable", "_buckets", 8L);
		long num = FindTemplateOffset(fields, "HashtableBucket", "_entry", 0L);
		long num2 = FindTemplateOffset(fields, "BasicHashtableEntry", "_next", 8L);
		long offset6 = num2 + IntPtr.Size;
		if (vmStructField.Address == IntPtr.Zero || !memory.TryReadPointer(vmStructField.Address, out var value) || value == IntPtr.Zero || !memory.TryReadInt32(Add(value, offset4), out var value2) || value2 <= 0 || value2 > 100000 || !memory.TryReadPointer(Add(value, offset5), out var value3) || value3 == IntPtr.Zero)
		{
			return Array.Empty<LoadedJvmClass>();
		}
		List<LoadedJvmClass> list = new List<LoadedJvmClass>();
		HashSet<long> hashSet = new HashSet<long>();
		for (int num3 = 0; num3 < value2; num3++)
		{
			if (!memory.TryReadPointer(Add(value3, num3 * IntPtr.Size + num), out var value4))
			{
				continue;
			}
			int num4 = 0;
			while (value4 != IntPtr.Zero && num4 < 10000 && hashSet.Add(((IntPtr)value4).ToInt64()))
			{
				if (memory.TryReadPointer(Add(value4, offset6), out var value5) && value5 != IntPtr.Zero && memory.TryReadPointer(Add(value5, offset), out var value6) && value6 != IntPtr.Zero && memory.TryReadUInt16(Add(value6, offset2), out var value7) && value7 > 0 && value7 < 1024 && memory.TryReadBytes(Add(value6, offset3), value7, out byte[] data))
				{
					string name = Encoding.UTF8.GetString(data);
					if (IsTargetClassName(name))
					{
						list.Add(new LoadedJvmClass(name, value5));
					}
				}
				if (!memory.TryReadPointer(Add(value4, num2), out value4))
				{
					break;
				}
				num4++;
			}
		}
		return list;
	}

	private IReadOnlyList<LoadedJvmClass> EnumerateClassLoaderDataGraphClasses(IReadOnlyList<VmStructField> fields)
	{
		VmStructField headField = fields.FirstOrDefault((VmStructField field) => field.TypeName == "ClassLoaderDataGraph" && field.FieldName == "_head");
		long klassesOffset = FindOffset(fields, "ClassLoaderData", "_klasses", -1L);
		long nextLoaderOffset = FindOffset(fields, "ClassLoaderData", "_next", -1L);
		long nextKlassOffset = FindOffset(fields, "Klass", "_next_link", -1L);
		long nameOffset = FindOffset(fields, "Klass", "_name", -1L);
		long symbolLengthOffset = FindOffset(fields, "Symbol", "_length", -1L);
		long symbolBodyOffset = FindOffset(fields, "Symbol", "_body", -1L);
		if (headField.Address == IntPtr.Zero || klassesOffset < 0L || nextLoaderOffset < 0L || nextKlassOffset < 0L || nameOffset < 0L || symbolLengthOffset < 0L || symbolBodyOffset < 0L || !memory.TryReadPointer(headField.Address, out var loader))
		{
			return Array.Empty<LoadedJvmClass>();
		}
		List<LoadedJvmClass> result = new List<LoadedJvmClass>();
		HashSet<long> visitedLoaders = new HashSet<long>();
		HashSet<long> visitedKlasses = new HashSet<long>();
		int loaderCount = 0;
		while (loader != IntPtr.Zero && loaderCount < 10000 && visitedLoaders.Add(((IntPtr)loader).ToInt64()))
		{
			if (memory.TryReadPointer(Add(loader, klassesOffset), out var klass))
			{
				int klassCount = 0;
				while (klass != IntPtr.Zero && klassCount < 100000 && visitedKlasses.Add(((IntPtr)klass).ToInt64()))
				{
					if (TryReadKlassName(klass, nameOffset, symbolLengthOffset, symbolBodyOffset, out string name) && IsTargetClassName(name))
					{
						result.Add(new LoadedJvmClass(name, klass));
					}
					if (!memory.TryReadPointer(Add(klass, nextKlassOffset), out klass))
					{
						break;
					}
					klassCount++;
				}
			}
			if (!memory.TryReadPointer(Add(loader, nextLoaderOffset), out loader))
			{
				break;
			}
			loaderCount++;
		}
		return result;
	}

	private bool TryReadKlassName(nint klass, long nameOffset, long symbolLengthOffset, long symbolBodyOffset, out string name)
	{
		name = string.Empty;
		if (!memory.TryReadPointer(Add(klass, nameOffset), out var symbol) || symbol == IntPtr.Zero || !memory.TryReadUInt16(Add(symbol, symbolLengthOffset), out var length) || length == 0 || length > 4096 || !memory.TryReadBytes(Add(symbol, symbolBodyOffset), length, out byte[] data))
		{
			return false;
		}
		name = Encoding.UTF8.GetString(data);
		return true;
	}

	private static bool IsTargetClassName(string name)
	{
		string[] targets = new string[58]
		{
			"net/minecraft/client/Minecraft", "net/minecraft/client/multiplayer/WorldClient", "net/minecraft/client/entity/EntityPlayerSP", "net/minecraft/entity/Entity", "net/minecraft/entity/EntityLivingBase", "net/minecraft/entity/player/EntityPlayer", "net/minecraft/entity/player/InventoryPlayer", "net/minecraft/item/ItemStack", "net/minecraft/item/Item", "net/minecraft/item/ItemBlock", "net/minecraft/item/ItemSword", "net/minecraft/item/ItemArmor", "net/minecraft/item/ItemBow", "net/minecraft/item/ItemTool", "net/minecraft/item/ItemFood", "net/minecraft/item/ItemPotion",
			"net/minecraft/entity/DataWatcher", "net/minecraft/entity/DataWatcher$WatchableObject", "net/minecraft/entity/projectile/EntityFireball", "net/minecraft/world/World", "net/minecraft/client/multiplayer/ChunkProviderClient", "net/minecraft/util/LongHashMap", "net/minecraft/util/LongHashMap$Entry", "net/minecraft/world/chunk/Chunk", "net/minecraft/world/chunk/storage/ExtendedBlockStorage", "net/minecraft/client/renderer/entity/RenderManager", "net/minecraft/client/renderer/EntityRenderer",
			"net/minecraft/client/renderer/ActiveRenderInfo", "net/minecraft/client/settings/GameSettings", "net/minecraft/client/settings/KeyBinding", "net/minecraft/util/Timer", "java/util/ArrayList", "java/util/HashMap", "java/util/HashMap$Node", "java/nio/Buffer", "java/lang/String", "java/lang/Float", "java/lang/Integer",
			"com/mojang/authlib/GameProfile", "net/minecraft/scoreboard/Scoreboard", "net/minecraft/scoreboard/ScorePlayerTeam", "ave",
			"net/minecraft/util/MovingObjectPosition", "net/minecraft/util/BlockPos", "net/minecraft/util/Vec3", "java/lang/Enum",
			"net/minecraft/client/gui/GuiScreen", "net/minecraft/client/gui/inventory/GuiContainer", "net/minecraft/client/gui/inventory/GuiChest", "net/minecraft/client/gui/inventory/GuiInventory", "net/minecraft/inventory/InventoryBasic", "net/minecraft/inventory/InventoryLargeChest", "net/minecraft/tileentity/TileEntityChest", "net/minecraft/entity/player/InventoryEnderChest",
			"net/minecraft/nbt/NBTTagCompound", "net/minecraft/nbt/NBTTagList", "net/minecraft/nbt/NBTTagShort", "net/minecraft/nbt/NBTTagString"
		};
		return targets.Any((string target) => name.Equals(target, StringComparison.Ordinal));
	}

	private static long FindOffset(IEnumerable<VmStructField> fields, string type, string field, long fallback)
	{
		VmStructField match = fields.FirstOrDefault((VmStructField item) => item.TypeName == type && item.FieldName == field);
		if (match.TypeName == null)
		{
			return fallback;
		}
		return match.Offset;
	}

	private static long FindTemplateOffset(IEnumerable<VmStructField> fields, string typePrefix, string field, long fallback)
	{
		VmStructField vmStructField = fields.FirstOrDefault((VmStructField item) => item.TypeName.StartsWith(typePrefix + "<", StringComparison.Ordinal) && item.FieldName == field);
		if (vmStructField.TypeName != null)
		{
			return vmStructField.Offset;
		}
		return fallback;
	}

	private bool ReadExportValue(RemoteModule module, IReadOnlyDictionary<string, uint> exports, string name, out long value)
	{
		return memory.TryReadInt64(Add(module.BaseAddress, exports[name]), out value);
	}

	private bool ReadExportPointer(RemoteModule module, IReadOnlyDictionary<string, uint> exports, string name, out nint value)
	{
		return memory.TryReadPointer(Add(module.BaseAddress, exports[name]), out value);
	}

	private static nint Add(nint address, long offset)
	{
		return new IntPtr(((IntPtr)address).ToInt64() + offset);
	}
}
