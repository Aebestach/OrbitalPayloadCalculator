using System;
using System.Collections.Generic;
using UnityEngine;

namespace OrbitalPayloadCalculator.Calculation
{
    internal enum EngineRole
    {
        Main = 0,
        Solid = 1,
        Electric = 2,
        Retro = 3,
        Settling = 4,
        EscapeTower = 5
    }

    /// <summary>
    /// Parts that separate together when decouplers fire. When all engines in this group exhaust,
    /// we subtract GroupDryMassTons from the stack (simulate dropping empty boosters).
    /// GroupLiquidPropellantTons: liquid fuel in released parts - engines in this group consume this first, then exhaust.
    /// </summary>
    internal sealed class SeparationGroup
    {
        public double GroupDryMassTons;
        public double GroupLiquidPropellantTons;
        public HashSet<int> EngineIndices;
    }

    /// <summary>
    /// Per-engine data used for dynamic ISP / thrust simulation.
    /// IspSamples[i] = ISP at pressure PressureSamples[i] (atm, 0=vacuum..1=sea-level).
    /// ThrustCurveSamples: normalised thrust multiplier vs burn-time fraction (for SRBs).
    /// </summary>
    internal sealed class EngineEntry
    {
        public double ThrustkN;
        public double VacuumIsp;
        public double SeaLevelIsp;
        public EngineRole Role;
        public double PropellantMassTons;
        public List<string> PropellantNames = new List<string>();

        /// <summary> Dry mass of the part containing this engine. Dropped when separation group exhausts. </summary>
        public double PartDryMassTons;

        /// <summary> Index into StageInfo.SeparationGroups, or -1 if not in a group. </summary>
        public int SeparationGroupIndex;

        /// <summary> Part.GetInstanceID() for mapping part to separation groups. </summary>
        public int PartInstanceId;

        /// <summary> Localized display name of the part (from partInfo.title). </summary>
        public string PartDisplayName = string.Empty;

        public double[] PressureSamples;
        public double[] IspSamples;

        public double[] ThrustCurveFractions;
        public double[] ThrustCurveMultipliers;

        public bool IsSolid => Role == EngineRole.Solid;

        public double GetIspAtPressure(double pressureAtm)
        {
            if (PressureSamples == null || PressureSamples.Length == 0)
                return VacuumIsp + (SeaLevelIsp - VacuumIsp) * Math.Max(0d, Math.Min(1d, pressureAtm));
            var p = Math.Max(PressureSamples[0], Math.Min(PressureSamples[PressureSamples.Length - 1], pressureAtm));
            for (int i = 0; i < PressureSamples.Length - 1; i++)
            {
                if (p <= PressureSamples[i + 1])
                {
                    double span = PressureSamples[i + 1] - PressureSamples[i];
                    double t = span > 1e-12d ? (p - PressureSamples[i]) / span : 0d;
                    return IspSamples[i] * (1d - t) + IspSamples[i + 1] * t;
                }
            }
            return IspSamples[IspSamples.Length - 1];
        }

        public double GetThrustMultiplier(double burnFraction)
        {
            if (ThrustCurveFractions == null || ThrustCurveFractions.Length == 0)
                return 1.0d;
            var f = Math.Max(0d, Math.Min(1d, burnFraction));
            for (int i = 0; i < ThrustCurveFractions.Length - 1; i++)
            {
                if (f <= ThrustCurveFractions[i + 1])
                {
                    double span = ThrustCurveFractions[i + 1] - ThrustCurveFractions[i];
                    double t = span > 1e-12d ? (f - ThrustCurveFractions[i]) / span : 0d;
                    return ThrustCurveMultipliers[i] * (1d - t) + ThrustCurveMultipliers[i + 1] * t;
                }
            }
            return ThrustCurveMultipliers[ThrustCurveMultipliers.Length - 1];
        }
    }

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

        public List<EngineEntry> Engines = new List<EngineEntry>();
        public Dictionary<string, double> PropellantMassByName = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);

        /// <summary> Fairing mass jettisoned at this stage ignition (excluded from burn mass). </summary>
        public double FairingMassTons;

        /// <summary> When engines in a group exhaust, we drop GroupDryMassTons from the stack. </summary>
        public List<SeparationGroup> SeparationGroups = new List<SeparationGroup>();
    }

    internal sealed class VesselStats
    {
        public string VesselName = string.Empty;
        public string VesselPersistentKey = string.Empty;
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
        public string WarningMessageKey = string.Empty;
        public double RequiredDv;
        public double AvailableDv;
        public double AvailableDvSeaLevel;
        public double AvailableDvVacuum;
        public double EstimatedPayloadTons;
        public double OrbitalSpeed;
        public double RotationDv;
        public double PlaneChangeDv;
        public double IdealDvFromSurface;
        public bool IdealDvUsesModelA;
        public double Burn1Dv;
        public double Burn2Dv;
        public double Burn3Dv;
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
        private const double OneAtmKPa = 101.325d;
        private const double AirRSpecific = 287.058d;
        private const double KerbinAtmoDepthMeters = 70000.0d;

        internal static bool IsDvParticipatingRole(EngineRole role)
        {
            return role == EngineRole.Main || role == EngineRole.Solid || role == EngineRole.Electric;
        }

        private static bool EngineUsesPropellant(IList<string> propellantNames, string poolName)
        {
            if (propellantNames == null || string.IsNullOrEmpty(poolName)) return false;
            foreach (var p in propellantNames)
            {
                if (string.Equals(p, poolName, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        private static bool StageHasDvEngines(StageInfo stage)
        {
            if (stage?.Engines == null) return false;
            foreach (var e in stage.Engines)
            {
                if (e != null && IsDvParticipatingRole(e.Role))
                    return true;
            }
            return false;
        }

        /// <summary> Mass remaining after stage fires (propellant burned, fairing jettisoned). </summary>
        private static double StageResidualMass(StageInfo stage)
        {
            if (stage == null) return 0d;
            var fairing = stage.FairingMassTons;
            return Math.Max(0.001d, stage.WetMassTons - stage.PropellantMassTons - fairing);
        }

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

            var needsPlaneChange = effectiveInc + 0.5d < absLat;
            var launchIncDeg = orbitTargets.TargetInclinationDegrees;
            if (needsPlaneChange)
            {
                launchIncDeg = orbitTargets.TargetInclinationDegrees > 90.0d
                    ? 180.0d - absLat
                    : absLat;
            }

            if (needsPlaneChange && effectiveInc + 0.5d < absLat)
                result.WarningMessageKey = "#LOC_OPC_InclinationBelowLatitudeWarning";

            var rPe = body.Radius + orbitTargets.PeriapsisAltitudeMeters;
            var rAp = body.Radius + orbitTargets.ApoapsisAltitudeMeters;
            var r0 = body.Radius;
            var mu = body.gravParameter;
            var a = (rPe + rAp) * 0.5d;
            var eccentricity = a > 0.0d ? (rAp - rPe) / (rAp + rPe) : 0.0d;
            var orbitalSpeed = Math.Sqrt(mu * ((2.0d / rPe) - (1.0d / a)));

            ComputeIdealDvFromSurfaceToOrbit(mu, r0, rPe, rAp, out var idealDvFromSurface, out var idealDvUsesModelA, out var burn1Dv, out var burn2Dv, out var burn3Dv);

            var planeChangeDv = 0.0d;
            if (needsPlaneChange)
            {
                var planeChangeAngleRad = (absLat - effectiveInc) * Math.PI / 180.0d;
                planeChangeDv = 2.0d * orbitalSpeed * Math.Sin(planeChangeAngleRad * 0.5d);
            }

            var inertialDv = idealDvFromSurface;
            if (body.rotationPeriod > 0.0d)
            {
                var equatorialSpeed = 2.0d * Math.PI * body.Radius / body.rotationPeriod;
                var latRad = orbitTargets.LaunchLatitudeDegrees * Math.PI / 180.0d;
                var incRad = launchIncDeg * Math.PI / 180.0d;
                var surfaceSpeed = equatorialSpeed * Math.Abs(Math.Cos(latRad));
                var cosInc = Math.Cos(incRad);
                var dvSq = idealDvFromSurface * idealDvFromSurface
                           - 2.0d * idealDvFromSurface * equatorialSpeed * cosInc
                           + surfaceSpeed * surfaceSpeed;
                inertialDv = Math.Sqrt(Math.Max(0.0d, dvSq));
            }

            var activeStages = new List<StageInfo>();
            var totalDv = 0.0d;
            int maxPropStageNum = -1;

            LossModel.ResolveTurnParams(body, lossConfig.EstimateMode, lossConfig.TurnStartSpeed,
                lossConfig.TurnStartAltitude, out var turnStartSpeed, out var turnStartAltitude);

            var mode = lossConfig.EstimateMode;
            double userCdA = lossConfig.CdACoefficient > 0d ? lossConfig.CdACoefficient : LossModel.GetCdForMode(mode);
            double userTurnExpBottom = LossModel.GetTurnExponentBottomFromSpeed(turnStartSpeed);
            if (stats.Stages.Count > 0)
            {
                totalDv = ComputeStagedDv(stats, body, -1, activeStages, out maxPropStageNum, turnStartSpeed, turnStartAltitude, userCdA, userTurnExpBottom);
                result.AvailableDvSeaLevel = ComputeStagedDvForDisplay(stats, body, -1, useSeaLevelIsp: true);
                result.AvailableDvVacuum = ComputeStagedDvForDisplay(stats, body, -1, useSeaLevelIsp: false);
            }
            else
            {
                totalDv = ComputeSimpleDv(stats, body, userCdA, userTurnExpBottom);
                result.AvailableDvSeaLevel = ComputeSimpleDvWithMode(stats, body, useSeaLevelIsp: true);
                result.AvailableDvVacuum = ComputeSimpleDvWithMode(stats, body, useSeaLevelIsp: false);
            }

            if (totalDv <= 0.0d)
            {
                result.ErrorMessageKey = "#LOC_OPC_ZeroDv";
                return result;
            }

            double payloadGuess = 0.0d;
            LossEstimate losses = null;
            double requiredDv = 0.0d;

            for (int iter = 0; iter < 4; iter++)
            {
                var extraForLoss = payloadGuess;
                losses = LossModel.Estimate(body, orbitTargets, lossConfig, stats, extraForLoss);
                requiredDv = Math.Max(0.0d, inertialDv + losses.TotalDv + planeChangeDv);
                payloadGuess = EstimatePayload(stats, body, requiredDv, -1, maxPropStageNum, turnStartSpeed, turnStartAltitude, userCdA, userTurnExpBottom);
            }

            losses.UsedTurnExponentBottom = userTurnExpBottom;
            losses.UsedTurnExponentBottomManual = false;

            result.Success = true;
            result.RequiredDv = requiredDv;
            result.AvailableDv = totalDv;
            result.EstimatedPayloadTons = payloadGuess;
            result.OrbitalSpeed = orbitalSpeed;
            result.RotationDv = inertialDv - idealDvFromSurface;
            result.PlaneChangeDv = planeChangeDv;
            result.IdealDvFromSurface = idealDvFromSurface;
            result.IdealDvUsesModelA = idealDvUsesModelA;
            result.Burn1Dv = burn1Dv;
            result.Burn2Dv = burn2Dv;
            result.Burn3Dv = burn3Dv;
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
            List<StageInfo> outActiveStages, out int maxPropStageNum, double turnStartSpeed = -1.0d, double turnStartAltitude = -1.0d, double userCdA = 0.6d, double userTurnExponentBottom = 0.58d)
        {
            ClearSimulateBottomStageDvCache();
            var totalDv = 0.0d;
            var cumulativeMassAbove = 0.0d;
            maxPropStageNum = -1;

            var sortedStages = new List<StageInfo>(stats.Stages);
            sortedStages.Sort((a, b) => a.StageNumber.CompareTo(b.StageNumber));

            foreach (var stage in sortedStages)
            {
                if (payloadCutoffStage >= 0 && stage.StageNumber <= payloadCutoffStage)
                    continue;
                if (StageHasDvEngines(stage) && stage.PropellantMassTons > 0.0d && stage.StageNumber > maxPropStageNum)
                    maxPropStageNum = stage.StageNumber;
            }

            foreach (var stage in sortedStages)
            {
                if (payloadCutoffStage >= 0 && stage.StageNumber <= payloadCutoffStage)
                {
                    cumulativeMassAbove += stage.WetMassTons;
                    continue;
                }

                if (!StageHasDvEngines(stage) || stage.PropellantMassTons <= 0.0d)
                {
                    cumulativeMassAbove += StageResidualMass(stage);
                    continue;
                }

                var isBottomStage = stage.StageNumber == maxPropStageNum;
                var stageDv = ComputeStageDvWithDynamicBottomAscent(stats, body, stage, stage.WetMassTons + cumulativeMassAbove,
                    (stage.WetMassTons + cumulativeMassAbove) - stage.PropellantMassTons, isBottomStage, out var effectiveIsp, turnStartSpeed, turnStartAltitude, userCdA, userTurnExponentBottom);
                if (stageDv <= 0.0d)
                {
                    cumulativeMassAbove += StageResidualMass(stage);
                    continue;
                }

                var stageWet = stage.WetMassTons + cumulativeMassAbove;
                var stageDry = stageWet - stage.PropellantMassTons;

                if (stageDry <= 0.0d || stageWet <= stageDry)
                {
                    cumulativeMassAbove += StageResidualMass(stage);
                    continue;
                }

                stage.DeltaV = stageDv;
                stage.EffectiveIspUsed = effectiveIsp;
                stage.UsedSeaLevelIsp = isBottomStage && body != null && body.atmosphere &&
                    effectiveIsp + 1e-6d < stage.VacuumIsp;
                stage.MassAtIgnition = stageWet;
                stage.MassAfterBurn = stageDry;
                var surfG = body != null ? body.GeeASL * G0 : G0;
                var slThrust = stage.VacuumIsp > 0d
                    ? stage.ThrustkN * (stage.SeaLevelIsp / stage.VacuumIsp)
                    : stage.ThrustkN;
                stage.TWRAtIgnition = stageWet > 0d && surfG > 0d
                    ? slThrust / (stageWet * surfG)
                    : 0d;
                totalDv += stageDv;
                outActiveStages.Add(stage);

                cumulativeMassAbove += StageResidualMass(stage);
            }

            return totalDv;
        }

        private static double ComputeStagedDvForDisplay(VesselStats stats, CelestialBody body, int payloadCutoffStage, bool useSeaLevelIsp)
        {
            var totalDv = 0.0d;
            var cumulativeMassAbove = 0.0d;

            int maxPropStageNum = -1;
            foreach (var s in stats.Stages)
            {
                if (StageHasDvEngines(s) && s.PropellantMassTons > 0.0d && s.StageNumber > maxPropStageNum)
                    maxPropStageNum = s.StageNumber;
            }

            var sortedStages = new List<StageInfo>(stats.Stages);
            sortedStages.Sort((a, b) => a.StageNumber.CompareTo(b.StageNumber));

            foreach (var stage in sortedStages)
            {
                if (payloadCutoffStage >= 0 && stage.StageNumber <= payloadCutoffStage)
                {
                    cumulativeMassAbove += stage.WetMassTons;
                    continue;
                }

                if (!StageHasDvEngines(stage) || stage.PropellantMassTons <= 0.0d)
                {
                    cumulativeMassAbove += StageResidualMass(stage);
                    continue;
                }

                var isp = ResolveDisplayIsp(stage, body, useSeaLevelIsp);
                if (isp <= 0.0d)
                {
                    cumulativeMassAbove += StageResidualMass(stage);
                    continue;
                }

                var stageWet = stage.WetMassTons + cumulativeMassAbove;
                var stageDry = stageWet - stage.PropellantMassTons;

                // Bottom stage with separation groups: dropped booster dry mass reduces effective dry
                var isBottomStage = stage.StageNumber == maxPropStageNum;
                if (isBottomStage && stage.SeparationGroups != null && stage.SeparationGroups.Count > 0)
                {
                    double droppedDry = 0d;
                    foreach (var grp in stage.SeparationGroups)
                        if (grp != null && grp.GroupDryMassTons > 0d)
                            droppedDry += grp.GroupDryMassTons;
                    stageDry -= droppedDry;
                }

                // Fairing jettisoned at stage ignition: exclude from burn mass
                if (stage.FairingMassTons > 0d)
                {
                    stageWet = Math.Max(0.001d, stageWet - stage.FairingMassTons);
                    stageDry = Math.Max(0.001d, stageDry - stage.FairingMassTons);
                }

                if (stageDry <= 0.0d || stageWet <= stageDry)
                {
                    cumulativeMassAbove += StageResidualMass(stage);
                    continue;
                }

                totalDv += isp * G0 * Math.Log(stageWet / stageDry);
                cumulativeMassAbove += StageResidualMass(stage);
            }

            return totalDv;
        }

        /// <summary>
        /// Same as ComputeStagedDv but adds extraPayloadTons to the top of the rocket.
        /// Used by binary search to find max payload capacity.
        /// </summary>
        private static double ComputeStagedDvWithExtraPayload(VesselStats stats, CelestialBody body,
            int payloadCutoffStage, double extraPayloadTons, int maxPropStageNum, double turnStartSpeed = -1.0d, double turnStartAltitude = -1.0d, double userCdA = 0.6d, double userTurnExponentBottom = 0.58d)
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

                if (!StageHasDvEngines(stage) || stage.PropellantMassTons <= 0.0d)
                {
                    cumulativeMassAbove += StageResidualMass(stage);
                    continue;
                }

                var isBottomStage = stage.StageNumber == maxPropStageNum;
                var stageWet = stage.WetMassTons + cumulativeMassAbove;
                var stageDry = stageWet - stage.PropellantMassTons;
                var stageDv = ComputeStageDvWithDynamicBottomAscent(stats, body, stage, stageWet, stageDry, isBottomStage, out _, turnStartSpeed, turnStartAltitude, userCdA, userTurnExponentBottom);
                if (stageDv <= 0.0d)
                {
                    cumulativeMassAbove += StageResidualMass(stage);
                    continue;
                }

                if (stageDry <= 0.0d || stageWet <= stageDry)
                {
                    cumulativeMassAbove += StageResidualMass(stage);
                    continue;
                }

                totalDv += stageDv;
                cumulativeMassAbove += StageResidualMass(stage);
            }

            return totalDv;
        }

        private static double ComputeSimpleDv(VesselStats stats, CelestialBody body, double userCdA = 0.6d, double userTurnExponentBottom = 0.58d)
        {
            if (stats.WetMassTons <= 0.0d || stats.DryMassTons <= 0.0d || stats.WetMassTons <= stats.DryMassTons)
                return 0.0d;
            return ComputeSimpleDvWithExtraPayload(stats, body, 0.0d, userCdA, userTurnExponentBottom);
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
            int payloadCutoffStage, int maxPropStageNum, double turnStartSpeed = -1.0d, double turnStartAltitude = -1.0d, double userCdA = 0.6d, double userTurnExponentBottom = 0.58d)
        {
            if (stats.Stages.Count == 0)
            {
                var dvZeroSimple = ComputeSimpleDvWithExtraPayload(stats, body, 0.0d, userCdA, userTurnExponentBottom);
                if (dvZeroSimple < requiredDv)
                    return 0.0d;

                double loSimple = 0.0d, hiSimple = stats.WetMassTons * 5.0d;
                for (int i = 0; i < 64; i++)
                {
                    var mid = (loSimple + hiSimple) * 0.5d;
                    var dv = ComputeSimpleDvWithExtraPayload(stats, body, mid, userCdA, userTurnExponentBottom);
                    if (dv >= requiredDv)
                        loSimple = mid;
                    else
                        hiSimple = mid;
                }
                return loSimple;
            }

            var dvZero = ComputeStagedDvWithExtraPayload(stats, body, payloadCutoffStage, 0.0d, maxPropStageNum, turnStartSpeed, turnStartAltitude, userCdA, userTurnExponentBottom);
            if (dvZero < requiredDv)
                return 0.0d;

            double lo = 0.0d, hi = stats.WetMassTons * 5.0d;
            for (int i = 0; i < 64; i++)
            {
                var mid = (lo + hi) * 0.5d;
                var dv = ComputeStagedDvWithExtraPayload(stats, body, payloadCutoffStage, mid, maxPropStageNum, turnStartSpeed, turnStartAltitude, userCdA, userTurnExponentBottom);
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

        private static double ComputeSimpleDvWithExtraPayload(VesselStats stats, CelestialBody body, double extraPayloadTons, double userCdA = 0.6d, double userTurnExponentBottom = 0.58d)
        {
            var wet = stats.WetMassTons + extraPayloadTons;
            var dry = stats.DryMassTons + extraPayloadTons;
            if (wet <= 0.0d || dry <= 0.0d || wet <= dry)
                return 0.0d;

            var tmp = new StageInfo
            {
                VacuumIsp = stats.VacuumIspSeconds,
                SeaLevelIsp = stats.SeaLevelIspSeconds,
                ThrustkN = stats.TotalThrustkN
            };
            return ComputeStageDvWithDynamicBottomAscent(stats, body, tmp, wet, dry, isBottomStage: true, out _, userCdA: userCdA, userTurnExponentBottom: userTurnExponentBottom);
        }

        /// <summary>
        /// Uses dynamic pressure->ISP integration for the launch (bottom) stage.
        /// Other stages still use classic rocket-equation with vacuum ISP.
        /// </summary>
        private static double ComputeStageDvWithDynamicBottomAscent(VesselStats stats, CelestialBody body, StageInfo stage,
            double stageWetTons, double stageDryTons, bool isBottomStage, out double effectiveIsp, double turnStartSpeed = -1.0d, double turnStartAltitude = -1.0d, double userCdA = 0.6d, double userTurnExponentBottom = 0.58d)
        {
            effectiveIsp = 0.0d;
            var fairingMass = stage?.FairingMassTons ?? 0d;
            if (fairingMass > 0d)
            {
                stageWetTons = Math.Max(0.001d, stageWetTons - fairingMass);
                stageDryTons = Math.Max(0.001d, stageDryTons - fairingMass);
            }
            if (stage == null || stageWetTons <= stageDryTons || stageDryTons <= 0.0d)
                return 0.0d;

            var dryTons = stageDryTons;
            if (isBottomStage && stage.SeparationGroups != null && stage.SeparationGroups.Count > 0)
            {
                double droppedDry = 0d;
                foreach (var grp in stage.SeparationGroups)
                    if (grp != null && grp.GroupDryMassTons > 0d)
                        droppedDry += grp.GroupDryMassTons;
                dryTons = Math.Max(0.01d, dryTons - droppedDry);
            }
            if (stageWetTons <= dryTons) return 0.0d;

            var lnMassRatio = Math.Log(stageWetTons / dryTons);
            if (lnMassRatio <= 0.0d)
                return 0.0d;

            // Dynamic simulation only applies to the launch stage in atmosphere.
            if (isBottomStage && body != null && body.atmosphere && body.atmosphereDepth > 0.0d &&
                stage.VacuumIsp > 0.0d && stage.ThrustkN > 0.0d)
            {
                var dynamicDv = SimulateBottomStageDv(body, stats, stage, stageWetTons, stageDryTons, turnStartSpeed, turnStartAltitude, userCdA, userTurnExponentBottom);
                if (dynamicDv > 0.0d)
                {
                    effectiveIsp = dynamicDv / (G0 * lnMassRatio);
                    return dynamicDv;
                }
            }

            var fallbackIsp = GetEffectiveIsp(stage.VacuumIsp, stage.SeaLevelIsp, body, isBottomStage);
            if (fallbackIsp <= 0.0d)
                return 0.0d;
            effectiveIsp = fallbackIsp;
            var fallbackDv = fallbackIsp * G0 * lnMassRatio;
            return fallbackDv;
        }

        /// <summary>
        /// Per-engine runtime state used within the simulation loop.
        /// </summary>
        private sealed class EngineRuntime
        {
            public EngineEntry Entry;
            public double PropRemainingKg;
            public double InitialPropKg;
            public bool Exhausted;
        }

        private const int SimulateBottomStageDvCacheCapacity = 128;
        private static readonly Dictionary<(long, long, long, long, long, long), double> SimulateBottomStageDvCache = new Dictionary<(long, long, long, long, long, long), double>();

        private static void ClearSimulateBottomStageDvCache()
        {
            SimulateBottomStageDvCache.Clear();
        }

        private static double SimulateBottomStageDv(CelestialBody body, VesselStats stats, StageInfo stage,
            double stageWetTons, double stageDryTons, double userTurnStartSpeed = -1.0d, double userTurnStartAltitude = -1.0d, double userCdA = 0.6d, double userTurnExponentBottom = 0.58d)
        {
            var massKg = stageWetTons * 1000.0d;
            var dryMassKg = stageDryTons * 1000.0d;
            if (massKg <= dryMassKg || stage.ThrustkN <= 0.0d)
            {
                return 0.0d;
            }

            var turnStartSpeed = userTurnStartSpeed > 0d ? userTurnStartSpeed : 70.0d;
            var turnStartAlt = userTurnStartAltitude > 0d ? userTurnStartAltitude : Math.Max(600.0d, Math.Min(18000.0d, body.atmosphereDepth * 0.012d));
            var keyWet = (long)Math.Round(stageWetTons, 1);
            var keyDry = (long)Math.Round(stageDryTons, 1);
            var keyTurnS = (long)Math.Round(turnStartSpeed);
            var keyTurnA = (long)Math.Round(turnStartAlt);
            var keyCd = (long)(userCdA * 100.0d + 0.5d);
            var keyExp = (long)(userTurnExponentBottom * 100.0d + 0.5d);
            var key = (keyWet, keyDry, keyTurnS, keyTurnA, keyCd, keyExp);
            if (SimulateBottomStageDvCache.TryGetValue(key, out var cached))
                return cached;
            if (SimulateBottomStageDvCache.Count >= SimulateBottomStageDvCacheCapacity)
            {
                SimulateBottomStageDvCache.Clear();
            }

            const double dt = 0.5d;
            const double maxTime = 600.0d;
            var seaP = body.atmospherePressureSeaLevel > 0d ? body.atmospherePressureSeaLevel : OneAtmKPa;

            var engines = stage.Engines;
            bool hasPerEngineData = engines != null && engines.Count > 0;

            EngineRuntime[] runtimes = null;
            double fallbackVacIsp = stage.VacuumIsp > 0d ? stage.VacuumIsp : stage.SeaLevelIsp;
            double fallbackSeaIsp = stage.SeaLevelIsp > 0d ? stage.SeaLevelIsp : fallbackVacIsp;
            double totalThrustVacN = 0d;

            if (hasPerEngineData)
            {
                runtimes = new EngineRuntime[engines.Count];
                double totalSolidProp = 0d;
                double totalLiquidPropKg = (stageWetTons - stageDryTons) * 1000d;

                for (int i = 0; i < engines.Count; i++)
                {
                    var e = engines[i];
                    runtimes[i] = new EngineRuntime { Entry = e, Exhausted = false };
                    if (!IsDvParticipatingRole(e.Role))
                    {
                        runtimes[i].Exhausted = true;
                        continue;
                    }
                    totalThrustVacN += e.ThrustkN * 1000d;

                    if (e.Role == EngineRole.Solid && e.PropellantMassTons > 0d)
                    {
                        runtimes[i].InitialPropKg = e.PropellantMassTons * 1000d;
                        runtimes[i].PropRemainingKg = runtimes[i].InitialPropKg;
                        totalSolidProp += runtimes[i].InitialPropKg;
                    }
                }

                totalLiquidPropKg -= totalSolidProp;
                if (totalLiquidPropKg < 0d) totalLiquidPropKg = 0d;
                var poolByNameKg = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
                if (stage.PropellantMassByName != null && stage.PropellantMassByName.Count > 0)
                {
                    foreach (var kv in stage.PropellantMassByName)
                        poolByNameKg[kv.Key] = Math.Max(0d, kv.Value * 1000d);
                }

                if (poolByNameKg.Count == 0 && totalLiquidPropKg > 0d)
                {
                    poolByNameKg["__fallback_liquid__"] = totalLiquidPropKg;
                }

                // Solid engines consume part-contained propellant and should not draw from shared pools.
                for (int i = 0; i < runtimes.Length; i++)
                {
                    var rt = runtimes[i];
                    var e = rt.Entry;
                    if (e == null || e.Role != EngineRole.Solid || rt.InitialPropKg <= 0d) continue;
                    foreach (var propName in e.PropellantNames)
                    {
                        if (string.IsNullOrEmpty(propName) || !poolByNameKg.ContainsKey(propName)) continue;
                        poolByNameKg[propName] = Math.Max(0d, poolByNameKg[propName] - rt.InitialPropKg);
                    }
                }

                const int maxAllocationPasses = 6;
                for (int pass = 0; pass < maxAllocationPasses; pass++)
                {
                    bool anyAllocation = false;
                    foreach (var poolName in new List<string>(poolByNameKg.Keys))
                    {
                        var remaining = poolByNameKg[poolName];
                        if (remaining <= 1e-6d) continue;

                        double totalCompatThrust = 0d;
                        for (int i = 0; i < runtimes.Length; i++)
                        {
                            var rt = runtimes[i];
                            if (rt.Exhausted) continue;
                            if (rt.Entry == null || rt.Entry.Role == EngineRole.Solid) continue;
                        if (poolName != "__fallback_liquid__" && (rt.Entry.PropellantNames == null || !EngineUsesPropellant(rt.Entry.PropellantNames, poolName)))
                                continue;
                        totalCompatThrust += rt.Entry.ThrustkN;
                        }

                        if (totalCompatThrust <= 1e-9d) continue;
                        anyAllocation = true;
                        for (int i = 0; i < runtimes.Length; i++)
                        {
                            var rt = runtimes[i];
                            if (rt.Exhausted) continue;
                            if (rt.Entry == null || rt.Entry.Role == EngineRole.Solid) continue;
                            if (poolName != "__fallback_liquid__" && (rt.Entry.PropellantNames == null || !EngineUsesPropellant(rt.Entry.PropellantNames, poolName)))
                                continue;
                            var share = rt.Entry.ThrustkN / totalCompatThrust;
                            var add = remaining * share;
                            rt.InitialPropKg += add;
                            rt.PropRemainingKg += add;
                        }
                        poolByNameKg[poolName] = 0d;
                    }

                    if (!anyAllocation) break;
                }

                for (int i = 0; i < runtimes.Length; i++)
                {
                    if (runtimes[i].InitialPropKg <= 1e-6d)
                        runtimes[i].Exhausted = true;
                }
            }
            else
            {
                totalThrustVacN = stage.ThrustkN * 1000d;
            }

            if (totalThrustVacN <= 0d && fallbackVacIsp <= 0d)
            {
                return 0d;
            }

            var altitude = 0.0d;
            var velocity = 0.1d;
            var gamma = Math.PI * 0.5d;
            var turnStarted = false;
            var turnEndAlt = Math.Max(turnStartAlt + 1000.0d, body.atmosphereDepth * 0.85d);
            var totalDv = 0.0d;

            var cdaFixed = userCdA * Math.Sqrt(Math.Max(0.01d, stageWetTons));

            var separationGroups = stage.SeparationGroups;
            var droppedGroups = new HashSet<int>();

            for (double t = 0.0d; t < maxTime && massKg > dryMassKg; t += dt)
            {
                var pKPa = 0.0d;
                var tempK = 0.0d;
                if (altitude < body.atmosphereDepth)
                {
                    pKPa = Math.Max(0.0d, body.GetPressure(altitude));
                    tempK = Math.Max(0.0d, body.GetTemperature(altitude));
                }
                var pressureAtm = Math.Max(0.0d, Math.Min(1.0d, pKPa / seaP));

                double combinedThrustN = 0d;
                double combinedMassFlowKgS = 0d;

                if (hasPerEngineData)
                {
                    for (int i = 0; i < runtimes.Length; i++)
                    {
                        var rt = runtimes[i];
                        if (rt.Exhausted) continue;
                        var e = rt.Entry;

                        double eIsp = e.GetIspAtPressure(pressureAtm);
                        if (eIsp <= 0d) eIsp = e.VacuumIsp;
                        double eVacIsp = e.VacuumIsp > 0d ? e.VacuumIsp : eIsp;

                        double nominalThrustN = e.ThrustkN * 1000d * (eIsp / eVacIsp);

                        if (e.Role == EngineRole.Solid && rt.InitialPropKg > 0d)
                        {
                            double burnFrac = 1d - rt.PropRemainingKg / rt.InitialPropKg;
                            double tMult = e.GetThrustMultiplier(burnFrac);
                            nominalThrustN *= tMult;
                        }

                        double eMassFlow = nominalThrustN / (eIsp * G0);
                        combinedThrustN += nominalThrustN;
                        combinedMassFlowKgS += eMassFlow;
                    }
                }
                else
                {
                    double isp = fallbackVacIsp + (fallbackSeaIsp - fallbackVacIsp) * pressureAtm;
                    if (isp <= 0d) isp = fallbackVacIsp;
                    combinedThrustN = totalThrustVacN * (isp / fallbackVacIsp);
                    combinedMassFlowKgS = combinedThrustN / (isp * G0);
                }

                if (combinedThrustN <= 0d || combinedMassFlowKgS <= 0d)
                {
                    break;
                }

                var step = Math.Min(dt, (massKg - dryMassKg) / combinedMassFlowKgS);
                if (step <= 0d) break;

                totalDv += (combinedThrustN / massKg) * step;

                var R = body.Radius + altitude;
                var g = body.gravParameter / (R * R);
                var density = 0.0d;
                if (tempK > 0.0d && pKPa > 0.0d)
                    density = pKPa * 1000.0d / (AirRSpecific * tempK);
                var cda = cdaFixed;
                double machMult = 1.0d;
                if (tempK > 0.0d && velocity > 1.0d)
                {
                    var soundSpeed = Math.Sqrt(1.4d * AirRSpecific * tempK);
                    if (soundSpeed > 0.0d)
                    {
                        var dm = velocity / soundSpeed - 1.05d;
                        machMult = 1.0d + 1.4d * Math.Exp(-10.0d * dm * dm);
                    }
                }
                var drag = 0.5d * density * velocity * velocity * cda * machMult;
                var sinG = Math.Sin(gamma);
                var accel = (combinedThrustN - drag) / massKg - g * sinG;

                velocity += accel * step;
                if (velocity < 0.1d) velocity = 0.1d;
                altitude += velocity * sinG * step;
                if (altitude < 0.0d) altitude = 0.0d;

                if (!turnStarted && velocity > turnStartSpeed && altitude > turnStartAlt)
                    turnStarted = true;
                if (turnStarted)
                {
                    var progress = Math.Max(0.0d, Math.Min(1.0d, (altitude - turnStartAlt) / (turnEndAlt - turnStartAlt)));
                    var turnExponent = userTurnExponentBottom;
                    gamma = (Math.PI * 0.5d) * (1.0d - Math.Pow(progress, turnExponent));
                    if (gamma < 0.02d) gamma = 0.02d;
                }

                if (hasPerEngineData)
                {
                    for (int i = 0; i < runtimes.Length; i++)
                    {
                        var rt = runtimes[i];
                        if (rt.Exhausted) continue;
                        var e = rt.Entry;

                        double eIsp = e.GetIspAtPressure(pressureAtm);
                        if (eIsp <= 0d) eIsp = e.VacuumIsp;
                        double eVacIsp = e.VacuumIsp > 0d ? e.VacuumIsp : eIsp;
                        double nominalThrustN = e.ThrustkN * 1000d * (eIsp / eVacIsp);
                        if (e.Role == EngineRole.Solid && rt.InitialPropKg > 0d)
                        {
                            double burnFrac = 1d - rt.PropRemainingKg / rt.InitialPropKg;
                            nominalThrustN *= e.GetThrustMultiplier(burnFrac);
                        }
                        double eMassFlow = nominalThrustN / (eIsp * G0);

                        double consumed = eMassFlow * step;
                        if (consumed >= rt.PropRemainingKg)
                        {
                            consumed = rt.PropRemainingKg;
                            rt.Exhausted = true;
                        }
                        rt.PropRemainingKg -= consumed;
                    }

                    if (separationGroups != null && separationGroups.Count > 0)
                    {
                        for (int gi = 0; gi < separationGroups.Count; gi++)
                        {
                            if (droppedGroups.Contains(gi)) continue;
                            var grp = separationGroups[gi];
                            if (grp == null || grp.EngineIndices == null) continue;
                            bool allExhausted = true;
                            foreach (var idx in grp.EngineIndices)
                            {
                                if (idx < 0 || idx >= runtimes.Length || !runtimes[idx].Exhausted)
                                {
                                    allExhausted = false;
                                    break;
                                }
                            }
                            if (allExhausted && grp.GroupDryMassTons > 0d)
                            {
                                var dropKg = grp.GroupDryMassTons * 1000d;
                                massKg -= dropKg;
                                dryMassKg -= dropKg;
                                droppedGroups.Add(gi);
                            }
                        }
                    }
                }

                massKg -= combinedMassFlowKgS * step;
                if (massKg < dryMassKg) massKg = dryMassKg;
            }

            SimulateBottomStageDvCache[(keyWet, keyDry, keyTurnS, keyTurnA, keyCd, keyExp)] = totalDv;
            return totalDv;
        }

        private static string _cachedBlendBody;
        private static double _cachedBlendFactor = double.NaN;

        /// <summary>
        /// Samples the body's pressure curve at multiple altitudes and computes a
        /// velocity-weighted atmospheric fraction (lower altitudes weighted more
        /// because the rocket is slower there and spends more burn time).
        /// </summary>
        private static double GetAtmosphereBlendFactor(CelestialBody body)
        {
            if (body == null || !body.atmosphere || body.atmosphereDepth <= 0d)
                return 0.0d;

            if (body.bodyName == _cachedBlendBody &&
                !double.IsNaN(_cachedBlendFactor) &&
                !double.IsInfinity(_cachedBlendFactor))
                return _cachedBlendFactor;

            var seaP = body.atmospherePressureSeaLevel;
            if (seaP <= 0d) seaP = 101.325d;
            var dynamicDefault = GetDefaultBlendFactor(body);

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
            _cachedBlendFactor = wSum > 0d ? Math.Min(0.5d, sum / wSum) : dynamicDefault;
            if (double.IsNaN(_cachedBlendFactor) || double.IsInfinity(_cachedBlendFactor))
                _cachedBlendFactor = dynamicDefault;
            return _cachedBlendFactor;
        }

        private static double GetDefaultBlendFactor(CelestialBody body)
        {
            if (body == null || !body.atmosphere || body.atmosphereDepth <= 0d)
                return 0.0d;

            var pN = Math.Max(0.0d, Math.Min(15.0d, body.atmospherePressureSeaLevel / OneAtmKPa));
            var dN = Math.Max(0.0d, Math.Min(12.0d, body.atmosphereDepth / KerbinAtmoDepthMeters));
            var raw = 0.18d + 0.10d * Math.Log(1.0d + pN) + 0.06d * Math.Pow(dN, 0.35d);
            return Math.Max(0.12d, Math.Min(0.55d, raw));
        }

        /// <summary>
        /// Ideal delta-V from surface (r0) to target orbit (rPe, rAp).
        /// Uses Model A (energy-optimal lower bound) or Model B (Hohmann-structured)
        /// according to alpha/eccentricity boundaries in IDEAL_DV_MODELS.md.
        /// </summary>
        private static void ComputeIdealDvFromSurfaceToOrbit(double mu, double r0, double rPe, double rAp,
            out double totalDv, out bool useModelA, out double burn1Dv, out double burn2Dv, out double burn3Dv)
        {
            burn1Dv = burn2Dv = burn3Dv = totalDv = 0.0d;
            useModelA = false;
            if (mu <= 0.0d || r0 <= 0.0d || rPe < r0 || rAp < rPe)
                return;

            var radiusSum = rPe + rAp;
            if (radiusSum <= 0.0d)
                return;

            var semiMajorAxis = radiusSum * 0.5d;
            var alpha = semiMajorAxis / r0;
            var eccentricity = (rAp - rPe) / radiusSum;
            useModelA = alpha < 1.5d || (alpha <= 2.0d && eccentricity < 0.1d);

            if (useModelA)
            {
                // Model A: energy-optimal lower bound.
                var dvSq = 2.0d * mu * ((1.0d / r0) - (1.0d / radiusSum));
                totalDv = Math.Sqrt(Math.Max(0.0d, dvSq));
                burn1Dv = totalDv;
                return;
            }

            const double circularTolerance = 1.0e-6;
            var isCircular = Math.Abs(rAp - rPe) < circularTolerance * (rPe + rAp);

            if (isCircular)
            {
                var r = rPe;
                burn1Dv = Math.Sqrt(mu / r0) * Math.Sqrt(2.0d * r / (r0 + r));
                burn2Dv = Math.Max(0.0d, Math.Sqrt(mu / r) * (1.0d - Math.Sqrt(2.0d * r0 / (r0 + r))));
                burn3Dv = 0.0d;
            }
            else
            {
                burn1Dv = Math.Sqrt(mu / r0) * Math.Sqrt(2.0d * rPe / (r0 + rPe));
                burn2Dv = Math.Max(0.0d, Math.Sqrt(mu / rPe) * (1.0d - Math.Sqrt(2.0d * r0 / (r0 + rPe))));
                burn3Dv = Math.Max(0.0d, Math.Sqrt(2.0d * mu * rAp / (rPe * (rPe + rAp))) - Math.Sqrt(mu / rPe));
            }
            totalDv = burn1Dv + burn2Dv + burn3Dv;
        }
    }
}
