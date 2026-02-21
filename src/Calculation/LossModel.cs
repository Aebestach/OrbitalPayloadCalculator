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
        private const double KerbinAtmoDepthMeters = 70000.0d;
        private const double KerbinRadiusMeters = 600000.0d;

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
            GetNormalizedBodyScales(body, out var gN, out var pN, out var dN, out var rN);
            var baseTurn = aggressive ? 60.0d : 80.0d;
            var autoTurn = baseTurn * Math.Pow(gN, 0.25d) *
                           (0.92d + 0.18d * Math.Log(1.0d + pN) + 0.12d * Math.Pow(dN, 0.3d));
            double turnStartSpeed = userTurnStartSpeed > 0.0d
                ? userTurnStartSpeed
                : Clamp(autoTurn, 40.0d, 220.0d);
            if (double.IsNaN(turnStartSpeed) || double.IsInfinity(turnStartSpeed))
                turnStartSpeed = baseTurn;

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
                ? Clamp(atmoHeight * (0.010d + 0.004d * Math.Log(1.0d + pN)), 800.0d, 22000.0d) * speedRatio
                : 300.0d * speedRatio;
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
                double baseA0 = aggressive ? 15.0d : 20.0d;
                double baseB0 = aggressive ? 15.0d : 20.0d;
                double baseA = baseA0 * (0.90d + 0.15d * Math.Pow(gN, 0.3d) + 0.10d * Math.Pow(dN, 0.25d));
                double baseB = baseB0 * (0.90d + 0.10d * Math.Log(1.0d + pN) + 0.10d * Math.Pow(gN, 0.25d));
                double baseLoss = baseA + baseB * Math.Sqrt(Math.Max(0.01d, pN)) * gN;
                result.AttitudeLossDv = baseLoss * (1.0d + incFactor);
            }
            else if (body != null)
            {
                double vacA0 = aggressive ? 2.0d : 3.0d;
                double vacB0 = aggressive ? 3.5d : 5.0d;
                double vacA = vacA0 * (0.90d + 0.20d * Math.Pow(gN, 0.3d));
                double vacB = vacB0 * (0.90d + 0.15d * Math.Pow(gN, 0.25d) + 0.10d * Math.Pow(rN, 0.2d));
                result.AttitudeLossDv = (vacA + vacB * gN) * (1.0d + incFactor);
            }
        }

        /// <summary>
        /// Fallback when vessel stats are incomplete (no thrust/ISP data).
        /// Uses simple empirical formulas.
        /// </summary>
        private static void FallbackEstimate(CelestialBody body, double inclinationDeg,
            double launchLatDeg, LossEstimate result, bool aggressive)
        {
            GetNormalizedBodyScales(body, out var gN, out var pN, out var dN, out var rN);
            double gravCoeff0 = aggressive ? 750.0d : 900.0d;
            double gravMin0 = aggressive ? 300.0d : 400.0d;
            double gravCoeff = gravCoeff0 * Math.Pow(rN, 0.30d) * Math.Pow(gN, 0.30d);
            double gravMin = gravMin0 * Math.Pow(rN, 0.25d) * Math.Pow(gN, 0.20d);
            double gravMax = 2200.0d * Math.Pow(rN, 0.30d);
            result.GravityLossDv = Clamp(gravCoeff, gravMin, gravMax);

            result.AtmosphericLossDv = 0.0d;
            if (body.atmosphere)
            {
                double atmoA0 = aggressive ? 60.0d : 80.0d;
                double atmoB0 = aggressive ? 80.0d : 100.0d;
                double atmoA = atmoA0 * Math.Pow(Math.Max(0.05d, dN), 0.30d);
                double atmoB = atmoB0 * Math.Pow(Math.Max(0.01d, pN), 0.60d) * Math.Pow(Math.Max(0.05d, dN), 0.20d);
                double atmoMax = 800.0d * Math.Pow(rN, 0.30d) * Math.Pow(Math.Max(1.0d, pN), 0.20d);
                result.AtmosphericLossDv = Clamp(atmoA + atmoB, 30.0d, atmoMax);
            }

            double latScale = Math.Abs(Math.Cos(launchLatDeg * Math.PI / 180.0d));
            double incPrefix = 0.2d * (0.95d + 0.08d * Math.Min(1.5d, Math.Log(1.0d + rN)));
            double incFactor = incPrefix * Math.Min(1.0d, inclinationDeg / 90.0d) * latScale;
            double attA0 = aggressive ? 25.0d : 35.0d;
            double attB0 = aggressive ? 40.0d : 55.0d;
            double attA = attA0 * (0.90d + 0.20d * Math.Pow(gN, 0.30d) + 0.10d * Math.Pow(rN, 0.20d));
            double attB = attB0 * (0.90d + 0.20d * Math.Pow(gN, 0.25d));
            result.AttitudeLossDv = attA + attB * incFactor;
        }

        private static double Clamp(double value, double min, double max)
        {
            return Math.Max(min, Math.Min(max, value));
        }

        private static void GetNormalizedBodyScales(CelestialBody body, out double gN, out double pN, out double dN, out double rN)
        {
            if (body == null)
            {
                gN = 1.0d;
                pN = 1.0d;
                dN = 1.0d;
                rN = 1.0d;
                return;
            }

            gN = Clamp(body.GeeASL, 0.05d, 4.0d);
            var seaLevelPressure = body.atmosphere ? body.atmospherePressureSeaLevel : 0.0d;
            pN = Clamp(seaLevelPressure / OneAtmKPa, 0.0d, 15.0d);
            var atmosphereDepth = body.atmosphere ? body.atmosphereDepth : 0.0d;
            dN = Clamp(atmosphereDepth / KerbinAtmoDepthMeters, 0.0d, 12.0d);
            rN = Clamp(body.Radius / KerbinRadiusMeters, 0.2d, 15.0d);
        }
    }
}
