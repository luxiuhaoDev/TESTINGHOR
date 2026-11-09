namespace McPetOverlay;

internal readonly record struct LocalPlayerState(bool Valid, bool InWorld, bool GuiOpen, double X, double Y, double Z, float Yaw, float Pitch, bool OnGround, double MotionX, double MotionY, double MotionZ, bool HoldingBow, int BowUseTicks)
{
	internal static readonly LocalPlayerState Empty = new LocalPlayerState(Valid: false, InWorld: false, GuiOpen: false, 0.0, 0.0, 0.0, 0f, 0f, OnGround: false, 0.0, 0.0, 0.0, HoldingBow: false, BowUseTicks: 0);
}

internal readonly record struct BlockHitState(bool Valid, int X, int Y, int Z, int Face, double HitX, double HitY, double HitZ)
{
	internal static readonly BlockHitState Empty = new BlockHitState(false, 0, 0, 0, -1, 0.0, 0.0, 0.0);
}
