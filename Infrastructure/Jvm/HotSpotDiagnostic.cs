using System;
using System.Collections.Generic;

namespace McPetOverlay;

internal readonly record struct HotSpotDiagnostic(bool Success, nint JvmBase, string JvmPath, int FieldCount, IReadOnlyList<VmStructField> KeyFields, IReadOnlyList<LoadedJvmClass> ClassMatches, IReadOnlyList<JvmJavaField> MinecraftFields, IReadOnlyList<JvmClassLayout> ClassLayouts, MinecraftRuntimePointers RuntimePointers, string? Error)
{
	internal static HotSpotDiagnostic Failed(string error)
	{
		return new HotSpotDiagnostic(Success: false, IntPtr.Zero, string.Empty, 0, Array.Empty<VmStructField>(), Array.Empty<LoadedJvmClass>(), Array.Empty<JvmJavaField>(), Array.Empty<JvmClassLayout>(), default(MinecraftRuntimePointers), error);
	}
}
