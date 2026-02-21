using UnityEngine;

namespace OrbitalPayloadCalculator.Calculation
{
    internal sealed class OrbitTargets
    {
        public CelestialBody LaunchBody { get; set; }
        public double TargetOrbitAltitudeMeters { get; set; } = 80000.0d;
        public double TargetInclinationDegrees { get; set; } = 0.0d;
        public bool UseEccentricity { get; set; }
        public double TargetEccentricity { get; set; } = 0.0d;

        public double ClampAltitude()
        {
            if (LaunchBody == null)
            {
                return TargetOrbitAltitudeMeters;
            }

            var minAltitude = 1000.0d;
            var maxAltitude = Mathf.Max((float)(LaunchBody.sphereOfInfluence - 1000.0d), 1000.0f);
            TargetOrbitAltitudeMeters = Mathf.Clamp((float)TargetOrbitAltitudeMeters, (float)minAltitude, maxAltitude);
            return TargetOrbitAltitudeMeters;
        }

        public double ClampInclination()
        {
            TargetInclinationDegrees = Mathf.Clamp((float)TargetInclinationDegrees, 0.0f, 180.0f);
            return TargetInclinationDegrees;
        }

        public double ClampEccentricity()
        {
            TargetEccentricity = Mathf.Clamp((float)TargetEccentricity, 0.0f, 0.99f);
            return TargetEccentricity;
        }
    }
}
