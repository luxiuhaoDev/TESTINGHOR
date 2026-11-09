using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace McPetOverlay;

internal static class PeExportReader
{
	private readonly record struct Section(uint VirtualAddress, uint Size, uint RawPointer);

	internal static IReadOnlyDictionary<string, uint> ReadExports(string filePath)
	{
		byte[] array = File.ReadAllBytes(filePath);
		if (array.Length < 256 || array[0] != 77 || array[1] != 90)
		{
			throw new InvalidDataException("不是有效的 PE 文件");
		}
		int num = ReadInt32(array, 60);
		if (num < 0 || num + 256 >= array.Length || ReadUInt32(array, num) != 17744)
		{
			throw new InvalidDataException("PE 头无效");
		}
		int num2 = num + 4;
		ushort num3 = ReadUInt16(array, num2 + 2);
		ushort num4 = ReadUInt16(array, num2 + 16);
		int num5 = num2 + 20;
		ushort num6 = ReadUInt16(array, num5);
		int offset = num5 + ((num6 == 523) ? 112 : 96);
		uint num7 = ReadUInt32(array, offset);
		if (num7 == 0)
		{
			return new Dictionary<string, uint>();
		}
		int num8 = num5 + num4;
		List<Section> list = new List<Section>(num3);
		for (int i = 0; i < num3; i++)
		{
			int num9 = num8 + i * 40;
			uint val = ReadUInt32(array, num9 + 8);
			uint virtualAddress = ReadUInt32(array, num9 + 12);
			uint val2 = ReadUInt32(array, num9 + 16);
			uint rawPointer = ReadUInt32(array, num9 + 20);
			list.Add(new Section(virtualAddress, Math.Max(val, val2), rawPointer));
		}
		int num10 = RvaToOffset(num7, list, array.Length);
		uint num11 = ReadUInt32(array, num10 + 24);
		uint rva = ReadUInt32(array, num10 + 28);
		uint rva2 = ReadUInt32(array, num10 + 32);
		uint rva3 = ReadUInt32(array, num10 + 36);
		int num12 = RvaToOffset(rva, list, array.Length);
		int num13 = RvaToOffset(rva2, list, array.Length);
		int num14 = RvaToOffset(rva3, list, array.Length);
		Dictionary<string, uint> dictionary = new Dictionary<string, uint>(StringComparer.Ordinal);
		for (uint num15 = 0u; num15 < num11; num15++)
		{
			uint rva4 = ReadUInt32(array, num13 + checked((int)num15 * 4));
			string key = ReadAscii(array, RvaToOffset(rva4, list, array.Length));
			ushort num16 = ReadUInt16(array, num14 + checked((int)num15 * 2));
			uint value = ReadUInt32(array, num12 + num16 * 4);
			dictionary[key] = value;
		}
		return dictionary;
	}

	private static int RvaToOffset(uint rva, IEnumerable<Section> sections, int fileLength)
	{
		foreach (Section section in sections)
		{
			if (rva >= section.VirtualAddress && rva < section.VirtualAddress + section.Size)
			{
				long num = section.RawPointer + (rva - section.VirtualAddress);
				if (num < 0 || num >= fileLength)
				{
					break;
				}
				return (int)num;
			}
		}
		throw new InvalidDataException($"无法映射 RVA 0x{rva:X}");
	}

	private static string ReadAscii(byte[] data, int offset)
	{
		int i;
		for (i = offset; i < data.Length && data[i] != 0; i++)
		{
		}
		return Encoding.ASCII.GetString(data, offset, i - offset);
	}

	private static ushort ReadUInt16(byte[] data, int offset)
	{
		return BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(offset, 2));
	}

	private static uint ReadUInt32(byte[] data, int offset)
	{
		return BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset, 4));
	}

	private static int ReadInt32(byte[] data, int offset)
	{
		return BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(offset, 4));
	}
}
