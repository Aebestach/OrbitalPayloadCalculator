using System;
using UnityEngine;

namespace OrbitalPayloadCalculator.Calculation
{
    internal sealed class LossModelConfig
    {
        public bool AutoEstimate = true;
        public bool AggressiveEstimate;
        public bool OverrideGravityLoss;
        public bool OverrideAtmosphericLoss;
        public bool OverrideAttitudeLoss;

        public double TurnStartSpeed = -1.0d;
        public double ManualGravityLossDv;
        public double ManualAtmosphericLossDv;
        public double ManualAttitudeLossDv;
    }

    internal sealed class LossEstimate
    {
        public double GravityLossDv;
        public double AtmosphericLossDv;
        public double AttitudeLossDv;
        public double TotalDv;
    }

    internal static class LossModel
    {
        private const double G0 = 9.80665d;
        private const double OneAtmKPa = 101.325d;
        private const double AirRSpecific = 287.058d;

        public static LossEstimate Estimate(CelestialBody body, OrbitTargets target,
            LossModelConfig config, VesselStats stats, double extraPayloadTons = 0.0d)
        {
            var estimate = new LossEstimate();

            if (config.AutoEstimate && body != null)
            {
                bool canSim = stats != null && stats.HasVessel &&
                              stats.TotalThrustkN > 0d && stats.VacuumIspSeconds > 0d &&
                              stats.WetMassTons > 0d && stats.DryMassTons > 0d;

                if (canSim)
                    SimulateAscent(body, stats, target.PeriapsisAltitudeMeters,
                        target.TargetInclinationDegrees, target.LaunchLatitudeDegrees,
                        estimate, config.AggressiveEstimate, config.TurnStartSpeed, extraPayloadTons);
                else
                    FallbackEstimate(body, target.TargetInclinationDegrees,
                        target.LaunchLatitudeDegrees, estimate, config.AggressiveEstimate);
            }

            if (config.OverrideGravityLoss)
                estimate.GravityLossDv = config.ManualGravityLossDv;
            if (config.OverrideAtmosphericLoss)
                estimate.AtmosphericLossDv = config.ManualAtmosphericLossDv;
            if (config.OverrideAttitudeLoss)
                estimate.AttitudeLossDv = config.ManualAttitudeLossDv;

            estimate.TotalDv =
                estimate.GravityLossDv +
                estimate.AtmosphericLossDv +
                estimate.AttitudeLossDv;

            return estimate;
        }

        /// <summary>
        /// Time-stepped ascent simulation that reads the body's atmospheric pressure
        /// and temperature curves to compute gravity and drag losses.
        ///
        /// Gravity turn profile: vertical until turnStartSpeed, then a power-law
        /// pitch-over  gamma = 90° * (1 - progress^0.7)  that keeps the rocket
        /// near-vertical in the dense lower atmosphere and pitches over gradually.
        ///
        /// Drag area (CdA) is estimated heuristically from wet mass because
        /// actual part geometry is unavailable in the editor.
        /// </summary>
        private static void SimulateAscent(CelestialBody body, VesselStats stats,
            double targetAltM, double inclinationDeg, double launchLatDeg,
            LossEstimate result, bool aggressive, double userTurnStartSpeed, double extraPayloadTons = 0.0d)
        {
            const double dt = 1.0d;
            const double maxTime = 900.0d;
            double turnStartSpeed = userTurnStartSpeed > 0.0d
                ? userTurnStartSpeed
                : (aggressive ? 60.0d : 80.0d);

            double massKg = (stats.WetMassTons + extraPayloadTons) * 1000.0d;
            double dryMassKg = (stats.DryMassTons + extraPayloadTons) * 1000.0d;
            double thrustVacN = stats.TotalThrustkN * 1000.0d;
            double vacIsp = stats.VacuumIspSeconds;
            double seaIsp = stats.SeaLevelIspSeconds > 0d ? stats.SeaLevelIspSeconds : vacIsp;

            double totalWetTons = stats.WetMassTons + extraPayloadTons;
            double cdaCoeff = aggressive ? 0.7d : 1.0d;
            double CdA = cdaCoeff * Math.Pow(totalWetTons, 0.5d);

            bool hasAtmo = body.atmosphere && body.atmosphereDepth > 0d;
            double atmoHeight = hasAtmo ? body.atmosphereDepth : 0d;
            double speedRatio = turnStartSpeed / 80.0d;
            double turnStartAlt = hasAtmo
                ? Math.Min(1000.0d * speedRatio, atmoHeight * 0.015d * speedRatio)
                : 500.0d * speedRatio;
            double turnEndAlt = Math.Max(turnStartAlt + 1000.0d, targetAltM);

            double altitude = 0d;
            double velocity = 0.1d;
            double gamma = Math.PI / 2.0d;

            double gravityLoss = 0d;
            double dragLoss = 0d;
            bool turnStarted = false;
            double mass = massKg;

            for (double t = 0d; t < maxTime; t += dt)
            {
                if (altitude >= targetAltM || mass <= dryMassKg)
                    break;

                double R = body.Radius + altitude;
                double g = body.gravParameter / (R * R);

                double density = 0d;
                double pressureAtm = 0d;
                double tempK = 0d;
                if (hasAtmo && altitude < atmoHeight)
                {
                    double pKPa = body.GetPressure(altitude);
                    tempK = body.GetTemperature(altitude);
                    pressureAtm = pKPa / OneAtmKPa;
                    if (tempK > 0d && pKPa > 0d)
                        density = pKPa * 1000.0d / (AirRSpecific * tempK);
                }

                double isp = vacIsp + (seaIsp - vacIsp) *
                             Math.Max(0d, Math.Min(1d, pressureAtm));
                if (isp <= 0d) isp = vacIsp;

                double thrust = thrustVacN * (isp / vacIsp);

                double machMult = 1.0d;
                if (tempK > 0d && velocity > 1d)
                {
                    double soundSpeed = Math.Sqrt(1.4d * AirRSpecific * tempK);
                    if (soundSpeed > 0d)
                    {
                        double dm = velocity / soundSpeed - 1.05d;
                        machMult = 1.0d + 1.4d * Math.Exp(-10.0d * dm * dm);
                    }
                }

                double drag = 0.5d * density * velocity * velocity * CdA * machMult;
                double sinG = Math.Sin(gamma);

                gravityLoss += g * sinG * dt;
                if (mass > 0d)
                    dragLoss += (drag / mass) * dt;

                double accel = (thrust - drag) / mass - g * sinG;
                velocity += accel * dt;
                if (velocity < 0.1d) velocity = 0.1d;

                altitude += velocity * sinG * dt;
                if (altitude < 0d) altitude = 0d;

                if (!turnStarted && velocity > turnStartSpeed && altitude > turnStartAlt)
                    turnStarted = true;

                if (turnStarted)
                {
                    double progress = Math.Max(0d, Math.Min(1d,
                        (altitude - turnStartAlt) / (turnEndAlt - turnStartAlt)));
                    double turnExponent = aggressive ? 0.55d : 0.7d;
                    gamma = (Math.PI / 2.0d) * (1.0d - Math.Pow(progress, turnExponent));
                    if (gamma < 0.02d) gamma = 0.02d;
                }

                double massFlow = thrust / (isp * G0);
                mass -= massFlow * dt;
            }

            result.GravityLossDv = gravityLoss;
            result.AtmosphericLossDv = dragLoss;

            double rawIncFactor = Math.Max(0d, Math.Min(1d, inclinationDeg / 90.0d));
            double latScale = Math.Abs(Math.Cos(launchLatDeg * Math.PI / 180.0d));
            double incFactor = rawIncFactor * latScale;
            if (body != null && body.atmosphere)
            {
                double pressureScale = Math.Max(0.01d, body.atmospherePressureSeaLevel / OneAtmKPa);
                double geeScale = Math.Max(0.1d, body.GeeASL);
                double baseA = aggressive ? 15.0d : 20.0d;
                double baseB = aggressive ? 15.0d : 20.0d;
                double baseLoss = baseA + baseB * Math.Sqrt(pressureScale) * geeScale;
                result.AttitudeLossDv = baseLoss * (1.0d + incFactor);
            }
            else if (body != null)
            {
                double geeScale = Math.Max(0.05d, body.GeeASL);
                double vacA = aggressive ? 2.0d : 3.0d;
                double vacB = aggressive ? 3.5d : 5.0d;
                result.AttitudeLossDv = (vacA + vacB * geeScale) * (1.0d + incFactor);
            }
        }

        /// <summary>
        /// Fallback when vessel stats are incomplete (no thrust/ISP data).
        /// Uses simple empirical formulas.
        /// </summary>
        private static void FallbackEstimate(CelestialBody body, double inclinationDeg,
            double launchLatDeg, LossEstimate result, bool aggressive)
        {
            double surfGee = body.GeeASL;
            double gravCoeff = aggressive ? 750.0d : 900.0d;
            float gravMin = aggressive ? 300f : 400f;
            result.GravityLossDv = Mathf.Clamp(
                (float)(gravCoeff * Mathf.Pow((float)surfGee, 0.35f)), gravMin, 2200f);

            if (body.atmosphere)
            {
                float pressureScale = Mathf.Clamp(
                    (float)(body.atmospherePressureSeaLevel / OneAtmKPa), 0.01f, 10f);
                float atmoA = aggressive ? 60f : 80f;
                float atmoB = aggressive ? 80f : 100f;
                result.AtmosphericLossDv = Mathf.Clamp(
                    atmoA + atmoB * pressureScale, 50f, 800f);
            }

            double latScale = Math.Abs(Math.Cos(launchLatDeg * Math.PI / 180.0d));
            double incFactor = 0.2d * Math.Min(1.0d, inclinationDeg / 90.0d) * latScale;
            double attA = aggressive ? 25.0d : 35.0d;
            double attB = aggressive ? 40.0d : 55.0d;
            result.AttitudeLossDv = attA + attB * incFactor;
        }
    }
}
