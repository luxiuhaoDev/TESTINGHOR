using System;
using System.Collections.Generic;

namespace McPetOverlay;

internal sealed record GameSnapshot(IReadOnlyList<EntitySnapshot> Entities, int LocalPlayerNameColorCode = -1, float HorizontalProjectionScale = 0f, float VerticalProjectionScale = 0f)
{
	public static readonly GameSnapshot Empty = new GameSnapshot(Array.Empty<EntitySnapshot>());
}
