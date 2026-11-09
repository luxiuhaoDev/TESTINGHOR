using System.Drawing;

namespace McPetOverlay;

internal sealed record EntitySnapshot(
	EntityKind Kind,
	string Name,
	RectangleF ScreenBounds,
	float Health,
	float MaxHealth,
	float Distance,
	bool Visible = true,
	long EntityId = 0L,
	int NameColorCode = -1,
	PlayerEquipmentSnapshot? Equipment = null,
	double X = 0.0,
	double Y = 0.0,
	double Z = 0.0,
	double MotionX = 0.0,
	double MotionY = 0.0,
	double MotionZ = 0.0,
	float Width = 0f,
	float Height = 0f);
