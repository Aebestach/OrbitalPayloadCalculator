using System;
using System.Collections.Generic;
using UnityEngine;

namespace OrbitalPayloadCalculator.Calculation
{
    internal sealed class StageInfo
    {
        public int StageNumber;
        public double WetMassTons;
        public double DryMassTons;
        public double PropellantMassTons;
        public double VacuumIsp;
        public double SeaLevelIsp;
        public double ThrustkN;
        public bool HasEngines;
        public bool HasSolidFuel;
        public double DeltaV;
        public double EffectiveIspUsed;
        public bool UsedSeaLevelIsp;
        public double MassAtIgnition;
        public double MassAfterBurn;
        public double TWRAtIgnition;
    }

    internal sealed class VesselStats
    {
        public string VesselName = string.Empty;
        public bool HasVessel;
        public bool FromFlight;
        public int TotalStages;
        public List<StageInfo> Stages = new List<StageInfo>();

        public double WetMassTons;
        public double DryMassTons;
        public double VacuumIspSeconds;
        public double SeaLevelIspSeconds;
        public double TotalThrustkN;
    }

    internal sealed class PayloadCalculationResult
    {
        public bool Success;
        public string ErrorMessageKey = string.Empty;
        public double RequiredDv;
        public double AvailableDv;
        public double AvailableDvSeaLevel;
        public double AvailableDvVacuum;
        public double EstimatedPayloadTons;
        public double OrbitalSpeed;
        public double RotationDv;
        public double PeriapsisAltitudeMeters;
        public double ApoapsisAltitudeMeters;
        public double Eccentricity;
        public double InclinationDegrees;
        public LossEstimate Losses;
        public List<StageInfo> ActiveStages = new List<StageInfo>();
    }

    internal static class PayloadCalculator
    {
        private const double G0 = 9.80665d;

        public static PayloadCalculationResult Compute(VesselStats stats, OrbitTargets orbitTargets, LossModelConfig lossConfig)
        {
            var result = new PayloadCalculationResult
            {
                Losses = new LossEstimate()
            };

            if (stats == null || !stats.HasVessel)
            {
                result.ErrorMessageKey = "#LOC_OPC_NoVessel";
                return result;
            }

            if (orbitTargets?.LaunchBody == null)
            {
                result.ErrorMessageKey = "#LOC_OPC_NoBody";
                return result;
            }

            var body = orbitTargets.LaunchBody;
            orbitTargets.ClampLatitude();
            var altError = orbitTargets.ClampAltitudes();
            orbitTargets.ClampInclination();

            if (altError != null)
            {
                result.ErrorMessageKey = altError;
                return result;
            }

            var absLat = Math.Abs(orbitTargets.LaunchLatitudeDegrees);
            var effectiveInc = orbitTargets.TargetInclinationDegrees;
            if (effectiveInc > 90.0d) effectiveInc = 180.0d - effectiveInc;
            if (effectiveInc + 1.0d < absLat)
            {
                result.ErrorMessageKey = "#LOC_OPC_InclinationBelowLatitude";
                return result;
            }

            var rPe = body.Radius + orbitTargets.PeriapsisAltitudeMeters;
            var rAp = body.Radius + orbitTargets.ApoapsisAltitudeMeters;
            var mu = body.gravParameter;
            var a = (rPe + rAp) * 0.5d;
            var eccentricity = a > 0.0d ? (rAp - rPe) / (rAp + rPe) : 0.0d;
            var orbitalSpeed = Math.Sqrt(mu * ((2.0d / rPe) - (1.0d / a)));

            var inertialDv = orbitalSpeed;
            if (body.rotationPeriod > 0.0d)
            {
                var equatorialSpeed = 2.0d * Math.PI * body.Radius / body.rotationPeriod;
                var latRad = orbitTargets.LaunchLatitudeDegrees * Math.PI / 180.0d;
                var incRad = orbitTargets.TargetInclinationDegrees * Math.PI / 180.0d;
                var surfaceSpeed = equatorialSpeed * Math.Abs(Math.Cos(latRad));
                var cosInc = Math.Cos(incRad);
                var dvSq = orbitalSpeed * orbitalSpeed
                           - 2.0d * orbitalSpeed * equatorialSpeed * cosInc
                           + surfaceSpeed * surfaceSpeed;
                inertialDv = Math.Sqrt(Math.Max(0.0d, dvSq));
            }

            var activeStages = new List<StageInfo>();
            var totalDv = 0.0d;
            int maxPropStageNum = -1;

            if (stats.Stages.Count > 0)
            {
                totalDv = ComputeStagedDv(stats, body, -1, activeStages, out maxPropStageNum);
                result.AvailableDvSeaLevel = ComputeStagedDvForDisplay(stats, body, -1, useSeaLevelIsp: true);
                result.AvailableDvVacuum = ComputeStagedDvForDisplay(stats, body, -1, useSeaLevelIsp: false);
            }
            else
            {
                totalDv = ComputeSimpleDv(stats, body);
                result.AvailableDvSeaLevel = ComputeSimpleDvWithMode(stats, body, useSeaLevelIsp: true);
                result.AvailableDvVacuum = ComputeSimpleDvWithMode(stats, body, useSeaLevelIsp: false);
            }

            double payloadGuess = 0.0d;
            LossEstimate losses = null;
            double requiredDv = 0.0d;

            for (int iter = 0; iter < 4; iter++)
            {
                var extraForLoss = payloadGuess;
                losses = LossModel.Estimate(body, orbitTargets, lossConfig, stats, extraForLoss);
                requiredDv = Math.Max(0.0d, inertialDv + losses.TotalDv);
                payloadGuess = EstimatePayload(stats, body, requiredDv, -1, maxPropStageNum);
            }

            // Debug: dump per-stage and cumulative mass info
            if (stats.Stages.Count > 0)
            {
                var sortedForLog = new List<StageInfo>(stats.Stages);
                sortedForLog.Sort((x, y) => x.StageNumber.CompareTo(y.StageNumber));
                var cumulative = 0.0d;
                foreach (var s in sortedForLog)
                {
                    cumulative += s.WetMassTons;
                    Debug.Log($"[OPC] Stage {s.StageNumber}: Wet={s.WetMassTons:F4}t  Dry={s.DryMassTons:F4}t  Prop={s.PropellantMassTons:F4}t  CumulativeWet={cumulative:F4}t");
                }
                Debug.Log($"[OPC] Estimated={payloadGuess:F4}t  RequiredDv={requiredDv:F1}m/s  AvailDv={totalDv:F1}m/s");
            }

            result.Success = true;
            result.RequiredDv = requiredDv;
            result.AvailableDv = totalDv;
            result.EstimatedPayloadTons = payloadGuess;
            result.OrbitalSpeed = orbitalSpeed;
            result.RotationDv = orbitalSpeed - inertialDv;
            result.PeriapsisAltitudeMeters = orbitTargets.PeriapsisAltitudeMeters;
            result.ApoapsisAltitudeMeters = orbitTargets.ApoapsisAltitudeMeters;
            result.Eccentricity = eccentricity;
            result.InclinationDegrees = orbitTargets.TargetInclinationDegrees;
            result.Losses = losses;
            result.ActiveStages = activeStages;
            return result;
        }

        /// <summary>
        /// Iterates stages ascending (stage 0 = last to fire, topmost → highest = first to fire, bottom).
        /// Each stage's DV = Isp * g0 * ln((stageWet + massAbove) / (stageDry + massAbove)).
        /// After computing a stage, its WET mass is added to cumulativeMassAbove,
        /// because lower stages (higher number, fire earlier) carry this stage fully fueled.
        /// </summary>
        private static double ComputeStagedDv(VesselStats stats, CelestialBody body, int payloadCutoffStage,
            List<StageInfo> outActiveStages, out int maxPropStageNum)
        {
            var totalDv = 0.0d;
            var cumulativeMassAbove = 0.0d;
            maxPropStageNum = -1;

            var sortedStages = new List<StageInfo>(stats.Stages);
            sortedStages.Sort((a, b) => a.StageNumber.CompareTo(b.StageNumber));

            foreach (var stage in sortedStages)
            {
                if (payloadCutoffStage >= 0 && stage.StageNumber <= payloadCutoffStage)
                    continue;
                if (stage.HasEngines && stage.PropellantMassTons > 0.0d && stage.StageNumber > maxPropStageNum)
                    maxPropStageNum = stage.StageNumber;
            }

            foreach (var stage in sortedStages)
            {
                if (payloadCutoffStage >= 0 && stage.StageNumber <= payloadCutoffStage)
                {
                    cumulativeMassAbove += stage.WetMassTons;
                    continue;
                }

                if (!stage.HasEngines || stage.PropellantMassTons <= 0.0d)
                {
                    cumulativeMassAbove += stage.WetMassTons;
                    continue;
                }

                var isBottomStage = stage.StageNumber == maxPropStageNum;
                var isp = GetEffectiveIsp(stage.VacuumIsp, stage.SeaLevelIsp, body, isBottomStage);
                if (isp <= 0.0d)
                {
                    cumulativeMassAbove += stage.WetMassTons;
                    continue;
                }

                var stageWet = stage.WetMassTons + cumulativeMassAbove;
                var stageDry = stageWet - stage.PropellantMassTons;

                if (stageDry <= 0.0d || stageWet <= stageDry)
                {
                    cumulativeMassAbove += stage.WetMassTons;
                    continue;
                }

                var dv = isp * G0 * Math.Log(stageWet / stageDry);
                stage.DeltaV = dv;
                stage.EffectiveIspUsed = isp;
                stage.UsedSeaLevelIsp = isBottomStage && body != null && body.atmosphere;
                stage.MassAtIgnition = stageWet;
                stage.MassAfterBurn = stageDry;
                var surfG = body != null ? body.GeeASL * G0 : G0;
                var slThrust = stage.VacuumIsp > 0d
                    ? stage.ThrustkN * (stage.SeaLevelIsp / stage.VacuumIsp)
                    : stage.ThrustkN;
                stage.TWRAtIgnition = stageWet > 0d && surfG > 0d
                    ? slThrust / (stageWet * surfG)
                    : 0d;
                totalDv += dv;
                outActiveStages.Add(stage);

                cumulativeMassAbove += stage.WetMassTons;
            }

            return totalDv;
        }

        private static double ComputeStagedDvForDisplay(VesselStats stats, CelestialBody body, int payloadCutoffStage, bool useSeaLevelIsp)
        {
            var totalDv = 0.0d;
            var cumulativeMassAbove = 0.0d;

            var sortedStages = new List<StageInfo>(stats.Stages);
            sortedStages.Sort((a, b) => a.StageNumber.CompareTo(b.StageNumber));

            foreach (var stage in sortedStages)
            {
                if (payloadCutoffStage >= 0 && stage.StageNumber <= payloadCutoffStage)
                {
                    cumulativeMassAbove += stage.WetMassTons;
                    continue;
                }

                if (!stage.HasEngines || stage.PropellantMassTons <= 0.0d)
                {
                    cumulativeMassAbove += stage.WetMassTons;
                    continue;
                }

                var isp = ResolveDisplayIsp(stage, body, useSeaLevelIsp);
                if (isp <= 0.0d)
                {
                    cumulativeMassAbove += stage.WetMassTons;
                    continue;
                }

                var stageWet = stage.WetMassTons + cumulativeMassAbove;
                var stageDry = stageWet - stage.PropellantMassTons;
                if (stageDry <= 0.0d || stageWet <= stageDry)
                {
                    cumulativeMassAbove += stage.WetMassTons;
                    continue;
                }

                totalDv += isp * G0 * Math.Log(stageWet / stageDry);
                cumulativeMassAbove += stage.WetMassTons;
            }

            return totalDv;
        }

        /// <summary>
        /// Same as ComputeStagedDv but adds extraPayloadTons to the top of the rocket.
        /// Used by binary search to find max payload capacity.
        /// </summary>
        private static double ComputeStagedDvWithExtraPayload(VesselStats stats, CelestialBody body,
            int payloadCutoffStage, double extraPayloadTons, int maxPropStageNum)
        {
            var totalDv = 0.0d;
            var cumulativeMassAbove = extraPayloadTons;

            var sortedStages = new List<StageInfo>(stats.Stages);
            sortedStages.Sort((a, b) => a.StageNumber.CompareTo(b.StageNumber));

            foreach (var stage in sortedStages)
            {
                if (payloadCutoffStage >= 0 && stage.StageNumber <= payloadCutoffStage)
                {
                    cumulativeMassAbove += stage.WetMassTons;
                    continue;
                }

                if (!stage.HasEngines || stage.PropellantMassTons <= 0.0d)
                {
                    cumulativeMassAbove += stage.WetMassTons;
                    continue;
                }

                var isBottomStage = stage.StageNumber == maxPropStageNum;
                var isp = GetEffectiveIsp(stage.VacuumIsp, stage.SeaLevelIsp, body, isBottomStage);
                if (isp <= 0.0d)
                {
                    cumulativeMassAbove += stage.WetMassTons;
                    continue;
                }

                var stageWet = stage.WetMassTons + cumulativeMassAbove;
                var stageDry = stageWet - stage.PropellantMassTons;

                if (stageDry <= 0.0d || stageWet <= stageDry)
                {
                    cumulativeMassAbove += stage.WetMassTons;
                    continue;
                }

                totalDv += isp * G0 * Math.Log(stageWet / stageDry);
                cumulativeMassAbove += stage.WetMassTons;
            }

            return totalDv;
        }

        private static double ComputeSimpleDv(VesselStats stats, CelestialBody body)
        {
            var isp = GetEffectiveIsp(stats.VacuumIspSeconds, stats.SeaLevelIspSeconds, body, true);
            if (isp <= 0.0d || stats.WetMassTons <= 0.0d || stats.DryMassTons <= 0.0d || stats.WetMassTons <= stats.DryMassTons)
                return 0.0d;
            return isp * G0 * Math.Log(stats.WetMassTons / stats.DryMassTons);
        }

        private static double ComputeSimpleDvWithMode(VesselStats stats, CelestialBody body, bool useSeaLevelIsp)
        {
            var isp = useSeaLevelIsp && body != null && body.atmosphere ? stats.SeaLevelIspSeconds : stats.VacuumIspSeconds;
            if (isp <= 0.0d || stats.WetMassTons <= 0.0d || stats.DryMassTons <= 0.0d || stats.WetMassTons <= stats.DryMassTons)
                return 0.0d;
            return isp * G0 * Math.Log(stats.WetMassTons / stats.DryMassTons);
        }

        private static double ResolveDisplayIsp(StageInfo stage, CelestialBody body, bool useSeaLevelIsp)
        {
            if (!useSeaLevelIsp || body == null || !body.atmosphere)
                return stage.VacuumIsp;
            return stage.SeaLevelIsp;
        }

        private static double EstimatePayload(VesselStats stats, CelestialBody body, double requiredDv,
            int payloadCutoffStage, int maxPropStageNum)
        {
            if (stats.Stages.Count == 0)
            {
                var isp = GetEffectiveIsp(stats.VacuumIspSeconds, stats.SeaLevelIspSeconds, body, true);
                if (isp <= 0.0d) return 0.0d;
                var ratio = Math.Exp(requiredDv / (isp * G0));
                if (ratio <= 1.0d) return 0.0d;
                var payload = (stats.WetMassTons - ratio * stats.DryMassTons) / (ratio - 1.0d);
                return Math.Max(0.0d, payload);
            }

            var dvZero = ComputeStagedDvWithExtraPayload(stats, body, payloadCutoffStage, 0.0d, maxPropStageNum);
            if (dvZero < requiredDv)
                return 0.0d;

            double lo = 0.0d, hi = stats.WetMassTons * 5.0d;
            for (int i = 0; i < 64; i++)
            {
                var mid = (lo + hi) * 0.5d;
                var dv = ComputeStagedDvWithExtraPayload(stats, body, payloadCutoffStage, mid, maxPropStageNum);
                if (dv >= requiredDv)
                    lo = mid;
                else
                    hi = mid;
            }
            return lo;
        }

        /// <summary>
        /// Bottom stage uses a pressure-profile-weighted ISP blend from body atmospheric curves.
        /// Upper stages always use vacuum ISP.
        /// </summary>
        private static double GetEffectiveIsp(double vacIsp, double seaIsp, CelestialBody body, bool isBottomStage)
        {
            if (body == null || !body.atmosphere)
                return vacIsp;

            if (isBottomStage)
            {
                var f = GetAtmosphereBlendFactor(body);
                return vacIsp * (1.0d - f) + seaIsp * f;
            }

            return vacIsp;
        }

        private static string _cachedBlendBody;
        private static double _cachedBlendFactor = 0.3d;

        /// <summary>
        /// Samples the body's pressure curve at multiple altitudes and computes a
        /// velocity-weighted atmospheric fraction (lower altitudes weighted more
        /// because the rocket is slower there and spends more burn time).
        /// </summary>
        private static double GetAtmosphereBlendFactor(CelestialBody body)
        {
            if (body == null || !body.atmosphere || body.atmosphereDepth <= 0d)
                return 0.0d;

            if (body.bodyName == _cachedBlendBody)
                return _cachedBlendFactor;

            var seaP = body.atmospherePressureSeaLevel;
            if (seaP <= 0d) seaP = 101.325d;

            double sum = 0d, wSum = 0d;
            const int N = 20;
            for (int i = 0; i <= N; i++)
            {
                var h = body.atmosphereDepth * i / N;
                var p = Math.Max(0d, Math.Min(1d, body.GetPressure(h) / seaP));
                var w = 1.0d - 0.5d * i / N;
                sum += p * w;
                wSum += w;
            }

            _cachedBlendBody = body.bodyName;
            _cachedBlendFactor = wSum > 0d ? Math.Min(0.5d, sum / wSum) : 0.3d;
            return _cachedBlendFactor;
        }
    }
}
