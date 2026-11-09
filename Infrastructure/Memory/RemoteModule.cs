namespace McPetOverlay;

internal readonly record struct RemoteModule(nint BaseAddress, int Size, string FilePath);
