namespace McPetOverlay;

internal sealed class EmptyEspDataSource : IEspDataSource
{
	public bool IsLocalPlayerHurt => false;

	public int HurtTime => 0;

	public bool IsInWorld => false;

	public bool IsContainerOpen => false;

	public GameSnapshot GetSnapshot()
	{
		return GameSnapshot.Empty;
	}

	public BedEspSnapshot GetBedSnapshot(int range, bool includeObsidian, bool fullHeight)
	{
		return BedEspSnapshot.Empty;
	}

	public FireballPredictionSnapshot GetFireballPredictionSnapshot(float range, int ticks, int maxEntities, float thickness, bool trajectoryEnabled)
	{
		return FireballPredictionSnapshot.Empty;
	}

	public LocalPlayerState GetLocalPlayerState()
	{
		return LocalPlayerState.Empty;
	}

	public BlockHitState GetBlockHitState()
	{
		return BlockHitState.Empty;
	}

	public bool? HasBlockSupport(double x, double footY, double z)
	{
		return null;
	}

	public bool? IsHoldingBlockItem()
	{
		return null;
	}

	public bool? HasClearLineOfSight(double startX, double startY, double startZ, double endX, double endY, double endZ)
	{
		return null;
	}

	public bool? IsHoldingSwordItem()
	{
		return null;
	}

	public bool? IsHoldingBowItem()
	{
		return null;
	}
}
