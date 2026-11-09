using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace McPetOverlay;

internal sealed class MinecraftMemoryReader : IDisposable
{
	private nint processHandle;

	private uint attachedProcessId;

	private nint moduleBase;

	private string processPath = string.Empty;

	internal uint AttachedProcessId => attachedProcessId;

	internal MemoryReadStatus AttachAndVerify(uint processId)
	{
		if (processId == 0)
		{
			return MemoryReadStatus.Disconnected("没有找到 Minecraft 窗口进程");
		}
		if (processId != attachedProcessId || processHandle == IntPtr.Zero)
		{
			Detach();
			processHandle = NativeMethods.OpenProcess(4112u, inheritHandle: false, processId);
			if (processHandle == IntPtr.Zero)
			{
				return MemoryReadStatus.Disconnected("OpenProcess 失败：" + new Win32Exception(Marshal.GetLastWin32Error()).Message);
			}
			try
			{
				using Process process = Process.GetProcessById((int)processId);
				if (!process.ProcessName.Equals("javaw", StringComparison.OrdinalIgnoreCase) && !process.ProcessName.Equals("java", StringComparison.OrdinalIgnoreCase))
				{
					string processName = process.ProcessName;
					Detach();
					return MemoryReadStatus.Disconnected("Minecraft 窗口进程不是 Java：" + processName);
				}
				ProcessModule? mainModule = process.MainModule;
				if (mainModule == null)
				{
					Detach();
					return MemoryReadStatus.Disconnected("无法读取 Java 主模块");
				}
				attachedProcessId = processId;
				moduleBase = mainModule.BaseAddress;
				processPath = mainModule.FileName;
			}
			catch (Exception ex)
			{
				Detach();
				return MemoryReadStatus.Disconnected("读取进程信息失败：" + ex.Message);
			}
		}
		byte[] array = new byte[2];
		if (!NativeMethods.ReadProcessMemory(processHandle, moduleBase, array, array.Length, out var bytesRead) || ((IntPtr)bytesRead).ToInt64() != array.Length)
		{
			int lastWin32Error = Marshal.GetLastWin32Error();
			Detach();
			return MemoryReadStatus.Disconnected("ReadProcessMemory 失败：" + new Win32Exception(lastWin32Error).Message);
		}
		if (array[0] != 77 || array[1] != 90)
		{
			return MemoryReadStatus.Disconnected($"Java 模块签名异常：{array[0]:X2} {array[1]:X2}");
		}
		return new MemoryReadStatus(Connected: true, attachedProcessId, moduleBase, processPath, "MZ", null);
	}

	private void Detach()
	{
		if (processHandle != IntPtr.Zero)
		{
			NativeMethods.CloseHandle(processHandle);
		}
		processHandle = IntPtr.Zero;
		attachedProcessId = 0u;
		moduleBase = IntPtr.Zero;
		processPath = string.Empty;
	}

	public void Dispose()
	{
		Detach();
	}

	internal bool TryGetModule(string moduleName, out RemoteModule module)
	{
		module = default(RemoteModule);
		if (attachedProcessId == 0)
		{
			return false;
		}
		try
		{
			using Process process = Process.GetProcessById((int)attachedProcessId);
			foreach (ProcessModule module2 in process.Modules)
			{
				if (module2.ModuleName.Equals(moduleName, StringComparison.OrdinalIgnoreCase))
				{
					module = new RemoteModule(module2.BaseAddress, module2.ModuleMemorySize, module2.FileName);
					return true;
				}
			}
		}
		catch
		{
		}
		return false;
	}

	internal bool TryReadBytes(nint address, int size, out byte[] data)
	{
		data = Array.Empty<byte>();
		if (processHandle == IntPtr.Zero || address == IntPtr.Zero || size <= 0 || size > 1048576)
		{
			return false;
		}
		byte[] array = new byte[size];
		if (!NativeMethods.ReadProcessMemory(processHandle, address, array, size, out var bytesRead) || ((IntPtr)bytesRead).ToInt64() != size)
		{
			return false;
		}
		data = array;
		return true;
	}

	internal bool TryReadInt64(nint address, out long value)
	{
		value = 0L;
		if (!TryReadBytes(address, 8, out byte[] data))
		{
			return false;
		}
		value = BitConverter.ToInt64(data, 0);
		return true;
	}

	internal bool TryReadInt32(nint address, out int value)
	{
		value = 0;
		if (!TryReadBytes(address, 4, out byte[] data))
		{
			return false;
		}
		value = BitConverter.ToInt32(data, 0);
		return true;
	}

	internal bool TryReadUInt32(nint address, out uint value)
	{
		value = 0u;
		if (!TryReadBytes(address, 4, out byte[] data))
		{
			return false;
		}
		value = BitConverter.ToUInt32(data, 0);
		return true;
	}

	internal bool TryReadUInt16(nint address, out ushort value)
	{
		value = 0;
		if (!TryReadBytes(address, 2, out byte[] data))
		{
			return false;
		}
		value = BitConverter.ToUInt16(data, 0);
		return true;
	}

	internal bool TryReadSingle(nint address, out float value)
	{
		value = 0f;
		if (!TryReadBytes(address, 4, out byte[] data))
		{
			return false;
		}
		value = BitConverter.ToSingle(data, 0);
		return float.IsFinite(value);
	}

	internal bool TryReadDouble(nint address, out double value)
	{
		value = 0.0;
		if (!TryReadBytes(address, 8, out byte[] data))
		{
			return false;
		}
		value = BitConverter.ToDouble(data, 0);
		return double.IsFinite(value);
	}

	internal bool TryReadUInt64(nint address, out ulong value)
	{
		value = 0uL;
		if (!TryReadBytes(address, 8, out byte[] data))
		{
			return false;
		}
		value = BitConverter.ToUInt64(data, 0);
		return true;
	}

	internal bool TryReadPointer(nint address, out nint value)
	{
		value = IntPtr.Zero;
		if (!TryReadInt64(address, out var value2))
		{
			return false;
		}
		value = new IntPtr(value2);
		return true;
	}

	internal bool TryReadCString(nint address, int maximumLength, out string value)
	{
		value = string.Empty;
		if (address == IntPtr.Zero || maximumLength <= 0)
		{
			return false;
		}
		List<byte> list = new List<byte>(Math.Min(maximumLength, 128));
		for (int i = 0; i < maximumLength; i += 64)
		{
			int size = Math.Min(64, maximumLength - i);
			if (!TryReadBytes(IntPtr.Add(address, i), size, out byte[] data))
			{
				return false;
			}
			int num = Array.IndexOf(data, (byte)0);
			if (num >= 0)
			{
				list.AddRange(data.AsSpan(0, num).ToArray());
				value = Encoding.UTF8.GetString(list.ToArray());
				return true;
			}
			list.AddRange(data);
		}
		value = Encoding.UTF8.GetString(list.ToArray());
		return true;
	}
}
