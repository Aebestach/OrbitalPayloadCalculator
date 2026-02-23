using System;
using UnityEngine;

namespace OrbitalPayloadCalculator.Calculation
{
    internal sealed class OrbitTargets
    {
        private const double AtmospherePaddingMeters = 10000.0d;
        private const double VacuumDefaultOrbitMeters = 100000.0d;

        public CelestialBody LaunchBody { get; set; }
        public double LaunchLatitudeDegrees { get; set; } = 0.0d;
        public double PeriapsisAltitudeMeters { get; set; } = VacuumDefaultOrbitMeters;
        public double ApoapsisAltitudeMeters { get; set; } = VacuumDefaultOrbitMeters;
        public double TargetInclinationDegrees { get; set; } = 0.0d;

        public static double GetDefaultOrbitAltitudeMeters(CelestialBody body)
        {
            if (body != null && body.atmosphere && body.atmosphereDepth > 0.0d)
                return body.atmosphereDepth + AtmospherePaddingMeters;
            return VacuumDefaultOrbitMeters;
        }

        public void ApplyDefaultAltitudesForBody(CelestialBody body)
        {
            var defaultAltitude = GetDefaultOrbitAltitudeMeters(body);
            PeriapsisAltitudeMeters = defaultAltitude;
            ApoapsisAltitudeMeters = defaultAltitude;
        }

        public double ClampLatitude()
        {
            LaunchLatitudeDegrees = Mathf.Clamp((float)LaunchLatitudeDegrees, -90.0f, 90.0f);
            return LaunchLatitudeDegrees;
        }

        public string ClampAltitudes()
        {
            if (LaunchBody != null)
            {
                var soiLimit = LaunchBody.sphereOfInfluence - LaunchBody.Radius;
                if (ApoapsisAltitudeMeters >= soiLimit || PeriapsisAltitudeMeters >= soiLimit)
                    return "#LOC_OPC_ApoapsisExceedsSOI";
            }

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

            return null;
        }

        public double ClampInclination()
        {
            TargetInclinationDegrees = Mathf.Clamp((float)TargetInclinationDegrees, 0.0f, 180.0f);
            return TargetInclinationDegrees;
        }
    }
}
