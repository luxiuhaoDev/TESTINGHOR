namespace McPetOverlay;

internal static class AutoAimPolicy
{
	internal static bool ShouldPause(AppConfig config, IEspDataSource dataSource)
	{
		return config.AutoAimPauseInContainers && dataSource.IsContainerOpen;
	}

	internal static bool IsTeammate(AppConfig config, GameSnapshot snapshot, EntitySnapshot entity)
	{
		return config.AutoAimTeamCheckEnabled
			&& snapshot.LocalPlayerNameColorCode >= 0
			&& entity.NameColorCode >= 0
			&& snapshot.LocalPlayerNameColorCode == entity.NameColorCode;
	}
}
