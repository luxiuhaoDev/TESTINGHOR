using System;

namespace McPetOverlay;

internal readonly record struct MemoryReadStatus(bool Connected, uint ProcessId, nint ModuleBase, string ProcessPath, string Signature, string? Error)
{
	internal static MemoryReadStatus Disconnected(string error)
	{
		return new MemoryReadStatus(Connected: false, 0u, IntPtr.Zero, string.Empty, string.Empty, error);
	}
}
