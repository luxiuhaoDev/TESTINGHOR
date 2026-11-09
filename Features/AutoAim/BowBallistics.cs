using System;

namespace McPetOverlay;

internal readonly record struct BowAimSolution(float Yaw, float Pitch, float FlightTicks, double AimX, double AimY, double AimZ);

internal static class BowBallistics
{
	private const double Drag = 0.99;
	private const double Gravity = 0.05;

	private const float MaximumLowArcUpwardPitch = 25f;

	internal static double GetArrowSpeed(int chargeTicks)
	{
		double charge = Math.Clamp(chargeTicks, 0, 20) / 20.0;
		charge = Math.Min(1.0, (charge * charge + charge * 2.0) / 3.0);
		return charge * 3.0;
	}

	internal static bool TrySolve(LocalPlayerState shooter, EntitySnapshot target, int chargeTicks, bool predictTargetMotion, out BowAimSolution solution)
	{
		solution = default;
		double speed = GetArrowSpeed(chargeTicks);
		if (speed < 0.15 || !shooter.Valid || target.Height <= 0f) return false;

		double startX = shooter.X;
		double startY = shooter.Y + 1.52;
		double startZ = shooter.Z;
		double targetY = target.Y + Math.Clamp(target.Height * 0.55, 0.5f, 1.35f);
		double shooterMotionY = shooter.OnGround ? 0.0 : shooter.MotionY;
		double previousTime = 0.25;
		double previousError = RequiredLaunchSpeed(previousTime, startX, startY, startZ, shooter, shooterMotionY, target, targetY, predictTargetMotion, out _, out _, out _) - speed;
		for (double time = 0.5; time <= 100.0; time += 0.25)
		{
			double error = RequiredLaunchSpeed(time, startX, startY, startZ, shooter, shooterMotionY, target, targetY, predictTargetMotion, out _, out _, out _) - speed;
			if (previousError > 0.0 && error <= 0.0)
			{
				double low = previousTime;
				double high = time;
				for (int i = 0; i < 24; i++)
				{
					double mid = (low + high) * 0.5;
					double midError = RequiredLaunchSpeed(mid, startX, startY, startZ, shooter, shooterMotionY, target, targetY, predictTargetMotion, out _, out _, out _) - speed;
					if (midError > 0.0) low = mid; else high = mid;
				}
				return BuildSolution((low + high) * 0.5, startX, startY, startZ, shooter, shooterMotionY, target, targetY, predictTargetMotion, out solution);
			}
			previousTime = time;
			previousError = error;
		}

		return false;
	}

	private static bool BuildSolution(double time, double startX, double startY, double startZ, LocalPlayerState shooter, double shooterMotionY, EntitySnapshot target, double targetY, bool predictTargetMotion, out BowAimSolution solution)
	{
		solution = default;
		RequiredLaunchSpeed(time, startX, startY, startZ, shooter, shooterMotionY, target, targetY, predictTargetMotion, out double vx, out double vy, out double vz);
		double horizontal = Math.Sqrt(vx * vx + vz * vz);
		if (!double.IsFinite(horizontal) || horizontal < 0.0001 || !double.IsFinite(vy)) return false;
		float yaw = (float)(Math.Atan2(-vx, vz) * 180.0 / Math.PI);
		float pitch = (float)(-Math.Atan2(vy, horizontal) * 180.0 / Math.PI);
		if (pitch < -MaximumLowArcUpwardPitch) return false;
		double targetMotionX = predictTargetMotion ? target.MotionX : 0.0;
		double targetMotionY = predictTargetMotion ? target.MotionY : 0.0;
		double targetMotionZ = predictTargetMotion ? target.MotionZ : 0.0;
		solution = new BowAimSolution(yaw, pitch, (float)time, target.X + targetMotionX * time, targetY + targetMotionY * time, target.Z + targetMotionZ * time);
		return float.IsFinite(yaw) && float.IsFinite(pitch);
	}

	private static double RequiredLaunchSpeed(double time, double startX, double startY, double startZ, LocalPlayerState shooter, double shooterMotionY, EntitySnapshot target, double targetY, bool predictTargetMotion, out double launchX, out double launchY, out double launchZ)
	{
		double distanceFactor = (1.0 - Math.Pow(Drag, time)) / (1.0 - Drag);
		double gravityDrop = Gravity / (1.0 - Drag) * (time - distanceFactor);
		double targetMotionX = predictTargetMotion ? target.MotionX : 0.0;
		double targetMotionY = predictTargetMotion ? target.MotionY : 0.0;
		double targetMotionZ = predictTargetMotion ? target.MotionZ : 0.0;
		double worldVx = (target.X + targetMotionX * time - startX) / distanceFactor;
		double worldVy = (targetY + targetMotionY * time - startY + gravityDrop) / distanceFactor;
		double worldVz = (target.Z + targetMotionZ * time - startZ) / distanceFactor;
		launchX = worldVx - shooter.MotionX;
		launchY = worldVy - shooterMotionY;
		launchZ = worldVz - shooter.MotionZ;
		return Math.Sqrt(launchX * launchX + launchY * launchY + launchZ * launchZ);
	}
}
