namespace McPetOverlay;

internal readonly record struct MinecraftRuntimePointers(ulong NarrowOopBase, int NarrowOopShift, ulong NarrowKlassBase, int NarrowKlassShift, nint ClassMirror, nint Minecraft, nint World, nint Player, nint RenderManager);
