using System;
using UnityEngine;

namespace OrbitalPayloadCalculator.Calculation
{
    internal sealed class OrbitTargets
    {
        public CelestialBody LaunchBody { get; set; }
        public double LaunchLatitudeDegrees { get; set; } = 0.0d;
        public double PeriapsisAltitudeMeters { get; set; } = 80000.0d;
        public double ApoapsisAltitudeMeters { get; set; } = 80000.0d;
        public double TargetInclinationDegrees { get; set; } = 0.0d;

        public double ClampLatitude()
        {
            LaunchLatitudeDegrees = Mathf.Clamp((float)LaunchLatitudeDegrees, -90.0f, 90.0f);
            return LaunchLatitudeDegrees;
        }

        public string ClampAltitudes()
        {
            var minAltitude = 1000.0d;
            var maxAltitude = LaunchBody != null
                ? (double)Mathf.Max((float)(LaunchBody.sphereOfInfluence - LaunchBody.Radius - 1000.0d), 1000.0f)
                : 1e12d;

            if (PeriapsisAltitudeMeters > ApoapsisAltitudeMeters)
            {
                var tmp = PeriapsisAltitudeMeters;
                PeriapsisAltitudeMeters = ApoapsisAltitudeMeters;
                ApoapsisAltitudeMeters = tmp;
            }

            PeriapsisAltitudeMeters = Math.Max(minAltitude, Math.Min(PeriapsisAltitudeMeters, maxAltitude));
            ApoapsisAltitudeMeters = Math.Max(PeriapsisAltitudeMeters, Math.Min(ApoapsisAltitudeMeters, maxAltitude));

            if (LaunchBody != null)
            {
                var rAp = LaunchBody.Radius + ApoapsisAltitudeMeters;
                if (rAp >= LaunchBody.sphereOfInfluence)
                    return "#LOC_OPC_ApoapsisExceedsSOI";
            }

            return null;
        }

        public double ClampInclination()
        {
            TargetInclinationDegrees = Mathf.Clamp((float)TargetInclinationDegrees, 0.0f, 180.0f);
            return TargetInclinationDegrees;
        }
    }
}
