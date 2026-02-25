using System;

namespace OrbitalPayloadCalculator.Calculation
{
    /// <summary>Loss estimate mode: Optimistic (best case), Normal (typical), Pessimistic (worst case).</summary>
    internal enum LossEstimateMode
    {
        Optimistic = 0,
        Normal = 1,
        Pessimistic = 2
    }

    internal sealed class LossModelConfig
    {
        public LossEstimateMode EstimateMode = LossEstimateMode.Normal;
        public bool OverrideGravityLoss;
        public bool OverrideAtmosphericLoss;
        public bool OverrideAttitudeLoss;

        public double TurnStartSpeed = -1.0d;
        public double CdACoefficient = -1.0d;
        public double TurnStartAltitude = -1.0d;
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

        /// <summary>Values actually used in simulation; -1 if N/A (e.g. fallback).</summary>
        public double UsedTurnExponentBottom = -1.0d;
        public double UsedTurnExponentFull = -1.0d;
        public double UsedTurnStartSpeed = -1.0d;
        public double UsedCdA = -1.0d;
        public double UsedCdACoefficient = -1.0d;
        public double UsedTurnStartAltitude = -1.0d;
        public bool UsedTurnExponentBottomManual;
        public bool UsedTurnExponentFullManual;
        public bool UsedTurnStartSpeedManual;
        /// <summary>Mode used for this computation; used for source display.</summary>
        public LossEstimateMode UsedEstimateMode;
        public bool UsedCdAManual;
        public bool UsedCdAFromFlight;
        public bool UsedTurnStartAltitudeManual;
        /// <summary>True when altitude was derived from user's manual turn speed (not from mode).</summary>
        public bool UsedTurnStartAltitudeDerivedFromSpeed;
    }

    internal static class LossModel
    {
        private const double G0 = 9.80665d;
        private const double OneAtmKPa = 101.325d;
        private const double AirRSpecific = 287.058d;
        private const double KerbinAtmoDepthMeters = 70000.0d;
        private const double KerbinRadiusMeters = 600000.0d;

        internal static double GetCdForMode(LossEstimateMode mode) =>
            mode == LossEstimateMode.Optimistic ? 0.50d : (mode == LossEstimateMode.Pessimistic ? 1.5d : 1.0d);
        private static double GetBaseTurnForMode(LossEstimateMode mode) =>
            mode == LossEstimateMode.Optimistic ? 55.0d : (mode == LossEstimateMode.Pessimistic ? 95.0d : 80.0d);
        private static double GetTwrRefForMode(LossEstimateMode mode) =>
            mode == LossEstimateMode.Optimistic ? 1.4d : (mode == LossEstimateMode.Pessimistic ? 1.6d : 1.5d);
        /// <summary>Derives bottom-stage turn exponent from turn start speed. Linear fit: 55→0.40, 80→0.58, 95→0.65.</summary>
        internal static double GetTurnExponentBottomFromSpeed(double turnStartSpeed)
        {
            if (turnStartSpeed <= 0d) return 0.58d;
            double exp = 0.05625d + 0.00625d * Math.Min(220d, Math.Max(40d, turnStartSpeed));
            return Math.Max(0.30d, Math.Min(0.90d, exp));
        }

        /// <summary>Derives full-segment turn exponent from turn start speed. Linear fit: 55→0.45, 80→0.70, 95→0.80.</summary>
        private static double GetTurnExponentFullFromSpeed(double turnStartSpeed)
        {
            if (turnStartSpeed <= 0d) return 0.70d;
            double exp = -0.03125d + 0.00875d * Math.Min(220d, Math.Max(40d, turnStartSpeed));
            return Math.Max(0.30d, Math.Min(0.90d, exp));
        }

        public static LossEstimate Estimate(CelestialBody body, OrbitTargets target,
            LossModelConfig config, VesselStats stats, double extraPayloadTons = 0.0d)
        {
            var estimate = new LossEstimate { UsedEstimateMode = config.EstimateMode };

            if (body != null)
            {
                bool canSim = stats != null && stats.HasVessel &&
                              stats.TotalThrustkN > 0d && stats.VacuumIspSeconds > 0d &&
                              stats.WetMassTons > 0d && stats.DryMassTons > 0d;

                if (canSim)
                    SimulateAscent(body, stats, target.PeriapsisAltitudeMeters,
                        target.TargetInclinationDegrees, target.LaunchLatitudeDegrees,
                        estimate, config.EstimateMode, config.TurnStartSpeed,
                        config.CdACoefficient, config.TurnStartAltitude, extraPayloadTons);
                else
                    FallbackEstimate(body, target.TargetInclinationDegrees,
                        target.LaunchLatitudeDegrees, estimate, config.EstimateMode);
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
        /// Drag area (CdA): both Editor and Flight use user CdA coefficient or heuristic (mass^0.5).
        /// </summary>
        private static void SimulateAscent(CelestialBody body, VesselStats stats,
            double targetAltM, double inclinationDeg, double launchLatDeg,
            LossEstimate result, LossEstimateMode mode, double userTurnStartSpeed,
            double userCdACoefficient, double userTurnStartAltitude,
            double extraPayloadTons = 0.0d)
        {
            const double dt = 1.0d;
            const double maxTime = 900.0d;
            GetNormalizedBodyScales(body, out var gN, out var pN, out var dN, out var rN);
            var baseTurn = GetBaseTurnForMode(mode);
            var autoTurn = baseTurn * Math.Pow(gN, 0.25d) *
                           (0.92d + 0.18d * Math.Log(1.0d + pN) + 0.12d * Math.Pow(dN, 0.3d));
            double turnStartSpeed = userTurnStartSpeed > 0.0d
                ? userTurnStartSpeed
                : Clamp(autoTurn, 40.0d, 220.0d);
            if (double.IsNaN(turnStartSpeed) || double.IsInfinity(turnStartSpeed))
                turnStartSpeed = baseTurn;
            if (userTurnStartSpeed <= 0.0d && stats != null)
            {
                var twr = PayloadCalculator.GetBottomStageSeaLevelTWR(stats, body);
                var twrRef = GetTwrRefForMode(mode);
                if (twr >= 1.05d && twr <= 3.0d)
                    turnStartSpeed = Clamp(turnStartSpeed * Math.Sqrt(twrRef / twr), 40.0d, 220.0d);
            }
            result.UsedTurnStartSpeed = turnStartSpeed;
            result.UsedTurnStartSpeedManual = userTurnStartSpeed > 0.0d;

            double massKg = (stats.WetMassTons + extraPayloadTons) * 1000.0d;
            double dryMassKg = (stats.DryMassTons + extraPayloadTons) * 1000.0d;
            double thrustVacN = stats.TotalThrustkN * 1000.0d;
            double vacIsp = stats.VacuumIspSeconds;
            double seaIsp = stats.SeaLevelIspSeconds > 0d ? stats.SeaLevelIspSeconds : vacIsp;

            double totalWetTons = stats.WetMassTons + extraPayloadTons;
            double coeff = userCdACoefficient > 0d ? userCdACoefficient : GetCdForMode(mode);
            double CdAFixed = coeff * Math.Pow(totalWetTons, 0.5d);
            result.UsedCdA = CdAFixed;
            result.UsedCdACoefficient = coeff;
            result.UsedCdAManual = userCdACoefficient > 0d;
            result.UsedCdAFromFlight = false;

            bool hasAtmo = body.atmosphere && body.atmosphereDepth > 0d;
            double atmoHeight = hasAtmo ? body.atmosphereDepth : 0d;
            double speedRatio = turnStartSpeed / 80.0d;
            double turnStartAlt;
            if (userTurnStartAltitude > 0d)
                turnStartAlt = userTurnStartAltitude;
            else
                turnStartAlt = hasAtmo
                    ? Clamp(atmoHeight * (0.010d + 0.004d * Math.Log(1.0d + pN)), 800.0d, 22000.0d) * speedRatio
                    : 300.0d * speedRatio;
            result.UsedTurnStartAltitude = turnStartAlt;
            result.UsedTurnStartAltitudeManual = userTurnStartAltitude > 0d;
            result.UsedTurnStartAltitudeDerivedFromSpeed = userTurnStartAltitude <= 0d && userTurnStartSpeed > 0d;
            double turnEndAlt = Math.Max(turnStartAlt + 1000.0d, targetAltM);

            double turnExponentFull = GetTurnExponentFullFromSpeed(turnStartSpeed);
            result.UsedTurnExponentFull = turnExponentFull;
            result.UsedTurnExponentFullManual = false;

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

                var pClamp = Math.Max(0d, Math.Min(15d, pressureAtm));
                double isp = Math.Max(1d, vacIsp + (seaIsp - vacIsp) * pClamp);

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

                double CdA = CdAFixed;
                if (CdA <= 0d) CdA = GetCdForMode(mode) * Math.Pow(totalWetTons, 0.5d);
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
                    gamma = (Math.PI / 2.0d) * (1.0d - Math.Pow(progress, turnExponentFull));
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
                double baseA0 = mode == LossEstimateMode.Optimistic ? 13.0d : (mode == LossEstimateMode.Pessimistic ? 25.0d : 20.0d);
                double baseB0 = mode == LossEstimateMode.Optimistic ? 13.0d : (mode == LossEstimateMode.Pessimistic ? 25.0d : 20.0d);
                double baseA = baseA0 * (0.90d + 0.15d * Math.Pow(gN, 0.3d) + 0.10d * Math.Pow(dN, 0.25d));
                double baseB = baseB0 * (0.90d + 0.10d * Math.Log(1.0d + pN) + 0.10d * Math.Pow(gN, 0.25d));
                double baseLoss = baseA + baseB * Math.Sqrt(Math.Max(0.01d, pN)) * gN;
                result.AttitudeLossDv = baseLoss * (1.0d + incFactor);
            }
            else if (body != null)
            {
                double vacA0 = mode == LossEstimateMode.Optimistic ? 1.8d : (mode == LossEstimateMode.Pessimistic ? 4.0d : 3.0d);
                double vacB0 = mode == LossEstimateMode.Optimistic ? 3.0d : (mode == LossEstimateMode.Pessimistic ? 6.0d : 5.0d);
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
            double launchLatDeg, LossEstimate result, LossEstimateMode mode)
        {
            GetNormalizedBodyScales(body, out var gN, out var pN, out var dN, out var rN);
            double gravCoeff0 = mode == LossEstimateMode.Optimistic ? 700.0d : (mode == LossEstimateMode.Pessimistic ? 1050.0d : 900.0d);
            double gravMin0 = mode == LossEstimateMode.Optimistic ? 280.0d : (mode == LossEstimateMode.Pessimistic ? 500.0d : 400.0d);
            double gravCoeff = gravCoeff0 * Math.Pow(rN, 0.30d) * Math.Pow(gN, 0.30d);
            double gravMin = gravMin0 * Math.Pow(rN, 0.25d) * Math.Pow(gN, 0.20d);
            double gravMax = 2200.0d * Math.Pow(rN, 0.30d);
            result.GravityLossDv = Clamp(gravCoeff, gravMin, gravMax);

            result.AtmosphericLossDv = 0.0d;
            if (body.atmosphere)
            {
                double atmoA0 = mode == LossEstimateMode.Optimistic ? 55.0d : (mode == LossEstimateMode.Pessimistic ? 100.0d : 80.0d);
                double atmoB0 = mode == LossEstimateMode.Optimistic ? 75.0d : (mode == LossEstimateMode.Pessimistic ? 120.0d : 100.0d);
                double atmoA = atmoA0 * Math.Pow(Math.Max(0.05d, dN), 0.30d);
                double atmoB = atmoB0 * Math.Pow(Math.Max(0.01d, pN), 0.60d) * Math.Pow(Math.Max(0.05d, dN), 0.20d);
                double atmoMax = 800.0d * Math.Pow(rN, 0.30d) * Math.Pow(Math.Max(1.0d, pN), 0.20d);
                result.AtmosphericLossDv = Clamp(atmoA + atmoB, 30.0d, atmoMax);
            }

            double latScale = Math.Abs(Math.Cos(launchLatDeg * Math.PI / 180.0d));
            double incPrefix = 0.2d * (0.95d + 0.08d * Math.Min(1.5d, Math.Log(1.0d + rN)));
            double incFactor = incPrefix * Math.Min(1.0d, inclinationDeg / 90.0d) * latScale;
            double attA0 = mode == LossEstimateMode.Optimistic ? 22.0d : (mode == LossEstimateMode.Pessimistic ? 45.0d : 35.0d);
            double attB0 = mode == LossEstimateMode.Optimistic ? 36.0d : (mode == LossEstimateMode.Pessimistic ? 70.0d : 55.0d);
            double attA = attA0 * (0.90d + 0.20d * Math.Pow(gN, 0.30d) + 0.10d * Math.Pow(rN, 0.20d));
            double attB = attB0 * (0.90d + 0.20d * Math.Pow(gN, 0.25d));
            result.AttitudeLossDv = attA + attB * incFactor;
        }

        /// <summary>Resolves turn start speed and altitude for use by ascent simulations. Returns the same values that SimulateAscent would use.</summary>
        public static void ResolveTurnParams(CelestialBody body, LossEstimateMode mode, double userTurnStartSpeed,
            double userTurnStartAltitude, out double turnStartSpeed, out double turnStartAltitude, VesselStats stats = null)
        {
            if (body == null)
            {
                turnStartSpeed = 70.0d;
                turnStartAltitude = 840.0d;
                return;
            }
            GetNormalizedBodyScales(body, out var gN, out var pN, out var dN, out var rN);
            var baseTurn = GetBaseTurnForMode(mode);
            var autoTurn = baseTurn * Math.Pow(gN, 0.25d) *
                           (0.92d + 0.18d * Math.Log(1.0d + pN) + 0.12d * Math.Pow(dN, 0.3d));
            turnStartSpeed = userTurnStartSpeed > 0.0d
                ? userTurnStartSpeed
                : Clamp(autoTurn, 40.0d, 220.0d);
            if (double.IsNaN(turnStartSpeed) || double.IsInfinity(turnStartSpeed))
                turnStartSpeed = baseTurn;

            if (userTurnStartSpeed <= 0.0d && stats != null)
            {
                var twr = PayloadCalculator.GetBottomStageSeaLevelTWR(stats, body);
                var twrRef = GetTwrRefForMode(mode);
                if (twr >= 1.05d && twr <= 3.0d)
                    turnStartSpeed = Clamp(turnStartSpeed * Math.Sqrt(twrRef / twr), 40.0d, 220.0d);
            }

            bool hasAtmo = body.atmosphere && body.atmosphereDepth > 0d;
            double atmoHeight = hasAtmo ? body.atmosphereDepth : 0d;
            double speedRatio = turnStartSpeed / 80.0d;
            if (userTurnStartAltitude > 0d)
                turnStartAltitude = userTurnStartAltitude;
            else
                turnStartAltitude = hasAtmo
                    ? Clamp(atmoHeight * (0.010d + 0.004d * Math.Log(1.0d + pN)), 800.0d, 22000.0d) * speedRatio
                    : 300.0d * speedRatio;
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
