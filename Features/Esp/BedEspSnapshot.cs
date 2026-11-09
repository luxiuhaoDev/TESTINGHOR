using System;
using System.Collections.Generic;
using System.Drawing;

namespace McPetOverlay;

internal sealed record ProjectedBoxSnapshot(PointF[] Corners, float Distance);

internal sealed record BedEspSnapshot(
	IReadOnlyList<ProjectedBoxSnapshot> Beds,
	IReadOnlyList<ProjectedBoxSnapshot> Obsidian,
	int ScannedBedCount,
	bool Faulted = false,
	string Diagnostic = "")
{
	internal static readonly BedEspSnapshot Empty = new BedEspSnapshot(
		Array.Empty<ProjectedBoxSnapshot>(),
		Array.Empty<ProjectedBoxSnapshot>(),
		0);
}
