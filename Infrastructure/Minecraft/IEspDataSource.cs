namespace McPetOverlay;

internal interface IEspDataSource
{
	bool IsLocalPlayerHurt { get; }

	int HurtTime { get; }

	bool IsInWorld { get; }

	bool IsContainerOpen { get; }

	GameSnapshot GetSnapshot();

	BedEspSnapshot GetBedSnapshot(int range, bool includeObsidian, bool fullHeight);

	FireballPredictionSnapshot GetFireballPredictionSnapshot(float range, int ticks, int maxEntities, float thickness, bool trajectoryEnabled);

	LocalPlayerState GetLocalPlayerState();

	BlockHitState GetBlockHitState();

	bool? HasBlockSupport(double x, double footY, double z);

	bool? HasClearLineOfSight(double startX, double startY, double startZ, double endX, double endY, double endZ);

	bool? IsHoldingBlockItem();

	bool? IsHoldingSwordItem();

	bool? IsHoldingBowItem();
}
