namespace McPetOverlay;

internal readonly record struct VmStructField(string TypeName, string FieldName, string TypeString, bool IsStatic, long Offset, nint Address);
