namespace McPetOverlay;

internal readonly record struct JvmJavaField(string Name, string Signature, ushort AccessFlags, int Offset, uint PackedOffset)
{
	internal bool IsStatic => (AccessFlags & 8) != 0;
}
