using System.Collections.Generic;

namespace McPetOverlay;

internal readonly record struct JvmClassLayout(string Name, nint KlassAddress, IReadOnlyList<JvmJavaField> Fields);
