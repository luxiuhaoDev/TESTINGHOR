using System;
using System.Collections.Generic;
using System.Drawing;

namespace McPetOverlay;

internal sealed record FireballSnapshot(long EntityId, string Name, RectangleF ScreenBounds, float Distance, IReadOnlyList<IReadOnlyList<PointF>> TrajectorySegments, IReadOnlyList<PointF[]> TrajectoryFaces, bool CollisionPredicted);

internal sealed record FireballPredictionSnapshot(IReadOnlyList<FireballSnapshot> Fireballs, bool Faulted = false, string Diagnostic = "")
{
	internal static readonly FireballPredictionSnapshot Empty = new FireballPredictionSnapshot(Array.Empty<FireballSnapshot>());
}
