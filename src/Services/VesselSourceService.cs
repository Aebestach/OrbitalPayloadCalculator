using System;
using System.Collections.Generic;
using System.Linq;
using OrbitalPayloadCalculator.Calculation;
using UnityEngine;

namespace OrbitalPayloadCalculator.Services
{
    internal sealed class VesselSourceService
    {
        private readonly bool _isEditor;
        private readonly List<Vessel> _flightCandidates = new List<Vessel>();
        private int _selectedFlightIndex;

        public VesselSourceService(bool isEditor)
        {
            _isEditor = isEditor;
        }

        public IReadOnlyList<Vessel> GetFlightCandidates()
        {
            _flightCandidates.Clear();
            if (_isEditor || FlightGlobals.VesselsLoaded == null)
                return _flightCandidates;

            foreach (var vessel in FlightGlobals.VesselsLoaded)
            {
                if (vessel == null || !vessel.loaded || !vessel.IsControllable)
                    continue;

                if (vessel.situation == Vessel.Situations.LANDED ||
                    vessel.situation == Vessel.Situations.SPLASHED ||
                    vessel.situation == Vessel.Situations.PRELAUNCH)
                {
                    _flightCandidates.Add(vessel);
                }
            }

            if (_selectedFlightIndex >= _flightCandidates.Count)
                _selectedFlightIndex = Mathf.Max(0, _flightCandidates.Count - 1);

            return _flightCandidates;
        }

        public int GetSelectedFlightIndex()
        {
            return _selectedFlightIndex;
        }

        public void SetSelectedFlightIndex(int index)
        {
            _selectedFlightIndex = Mathf.Clamp(index, 0, Mathf.Max(0, _flightCandidates.Count - 1));
        }

        public VesselStats ReadCurrentStats()
        {
            return _isEditor ? ReadFromEditor() : ReadFromFlightSelection();
        }

        private VesselStats ReadFromFlightSelection()
        {
            GetFlightCandidates();
            if (_flightCandidates.Count == 0)
                return new VesselStats { HasVessel = false };

            var selected = _flightCandidates[_selectedFlightIndex];
            if (selected == null)
                return new VesselStats { HasVessel = false };

            return BuildStatsFromParts(selected.vesselName, selected.Parts, true, GetStageCount(selected));
        }

        private VesselStats ReadFromEditor()
        {
            if (EditorLogic.fetch == null || EditorLogic.fetch.ship == null)
                return new VesselStats { HasVessel = false };

            var ship = EditorLogic.fetch.ship;
            if (ship.parts == null || ship.parts.Count == 0)
                return new VesselStats { HasVessel = false };

            var stageCount = KSP.UI.Screens.StageManager.StageCount;
            return BuildStatsFromParts(ship.shipName, ship.parts, false, stageCount);
        }

        /// <summary>
        /// Fuel tanks and engines may end up in different inverseStage groups.
        /// KSP allows crossfeed so engines can burn fuel from tanks in other stages.
        /// Move propellant from non-engine stages to the engine stage that would consume it
        /// (the highest-numbered engine stage that fires while the fuel is still attached).
        /// </summary>
        private static void RedistributePropellant(Dictionary<int, StageInfo> stageMap)
        {
            foreach (var si in stageMap.Values)
            {
                if (si.HasEngines || si.PropellantMassTons <= 0.0d)
                    continue;

                StageInfo target = null;
                foreach (var candidate in stageMap.Values)
                {
                    if (!candidate.HasEngines)
                        continue;
                    if (candidate.StageNumber < si.StageNumber)
                        continue;
                    if (target == null || candidate.StageNumber < target.StageNumber)
                        target = candidate;
                }

                if (target == null)
                {
                    foreach (var candidate in stageMap.Values)
                    {
                        if (!candidate.HasEngines)
                            continue;
                        if (target == null ||
                            Math.Abs(candidate.StageNumber - si.StageNumber) < Math.Abs(target.StageNumber - si.StageNumber))
                            target = candidate;
                    }
                }

                if (target != null)
                {
                    target.PropellantMassTons += si.PropellantMassTons;
                    si.PropellantMassTons = 0.0d;
                }
            }
        }

        private static int GetStageCount(Vessel vessel)
        {
            var max = -1;
            if (vessel == null || vessel.Parts == null) return 0;
            foreach (var part in vessel.Parts)
            {
                if (part != null)
                {
                    if (part.inverseStage > max) max = part.inverseStage;
                    if (part.originalStage > max) max = part.originalStage;
                }
            }
            return max + 1;
        }

        private static VesselStats BuildStatsFromParts(string vesselName, IList<Part> parts, bool fromFlight, int stageCount)
        {
            var stats = new VesselStats
            {
                HasVessel = parts != null && parts.Count > 0,
                VesselName = vesselName ?? string.Empty,
                FromFlight = fromFlight,
                TotalStages = stageCount
            };

            if (!stats.HasVessel) return stats;

            var stageMap = new Dictionary<int, StageInfo>();
            var globalPropNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            double totalWet = 0, totalThrust = 0, totalWeightedVac = 0, totalWeightedSea = 0;

            foreach (var part in parts)
            {
                if (part == null) continue;
                if (IsLaunchClamp(part)) continue;

                var stageNum = part.inverseStage;
                if (!stageMap.TryGetValue(stageNum, out var si))
                {
                    si = new StageInfo { StageNumber = stageNum };
                    stageMap[stageNum] = si;
                }

                var partDryMass = (double)part.mass;
                var partResourceMass = 0.0d;
                foreach (PartResource res in part.Resources)
                    partResourceMass += res.amount * res.info.density;

                var partWetMass = partDryMass + partResourceMass;
                si.WetMassTons += partWetMass;
                si.DryMassTons += partDryMass;
                totalWet += partWetMass;

                foreach (PartModule module in part.Modules)
                {
                    var engine = module as ModuleEngines;
                    if (engine == null || engine.maxThrust <= 0.0f) continue;

                    si.HasEngines = true;
                    si.ThrustkN += engine.maxThrust;
                    totalThrust += engine.maxThrust;

                    var vacIsp = (double)engine.atmosphereCurve.Evaluate(0.0f);
                    var seaIsp = (double)engine.atmosphereCurve.Evaluate(1.0f);
                    si.VacuumIsp = si.ThrustkN > 0
                        ? (si.VacuumIsp * (si.ThrustkN - engine.maxThrust) + vacIsp * engine.maxThrust) / si.ThrustkN
                        : vacIsp;
                    si.SeaLevelIsp = si.ThrustkN > 0
                        ? (si.SeaLevelIsp * (si.ThrustkN - engine.maxThrust) + seaIsp * engine.maxThrust) / si.ThrustkN
                        : seaIsp;

                    totalWeightedVac += engine.maxThrust * vacIsp;
                    totalWeightedSea += engine.maxThrust * seaIsp;

                    bool isSolid = false;
                    double solidPropMass = 0d;
                    foreach (var prop in engine.propellants)
                    {
                        if (!string.IsNullOrEmpty(prop.name))
                            globalPropNames.Add(prop.name);

                        if (prop.name == "SolidFuel")
                        {
                            si.HasSolidFuel = true;
                            isSolid = true;
                            foreach (PartResource res in part.Resources)
                            {
                                if (res.resourceName == "SolidFuel")
                                    solidPropMass += res.amount * res.info.density;
                            }
                        }
                    }

                    var entry = new EngineEntry
                    {
                        ThrustkN = engine.maxThrust,
                        VacuumIsp = vacIsp,
                        SeaLevelIsp = seaIsp,
                        IsSolid = isSolid,
                        PropellantMassTons = isSolid ? solidPropMass : 0d,
                        PartDryMassTons = partDryMass,
                        PartInstanceId = part.GetInstanceID(),
                        SeparationGroupIndex = -1
                    };

                    SampleAtmosphereCurve(engine, entry);
                    if (isSolid)
                        SampleThrustCurve(engine, entry);

                    si.Engines.Add(entry);
                }
            }

            foreach (var part in parts)
            {
                if (part == null) continue;
                if (IsLaunchClamp(part)) continue;
                var stageNum = part.inverseStage;
                if (!stageMap.TryGetValue(stageNum, out var si)) continue;

                foreach (PartResource res in part.Resources)
                {
                    if (globalPropNames.Contains(res.resourceName))
                        si.PropellantMassTons += res.amount * res.info.density;
                }
            }

            RedistributePropellant(stageMap);

            foreach (var si in stageMap.Values)
            {
                var actualDry = si.WetMassTons - si.PropellantMassTons;
                if (actualDry > 0) si.DryMassTons = actualDry;
            }

            double totalPropellant = 0;
            foreach (var si in stageMap.Values)
                totalPropellant += si.PropellantMassTons;

            var stageList = new List<StageInfo>(stageMap.Values);
            stageList.Sort((a, b) => a.StageNumber.CompareTo(b.StageNumber));
            stats.Stages = stageList;

            BuildSeparationGroups(parts, stageMap, globalPropNames);

            stats.WetMassTons = totalWet;
            stats.DryMassTons = Math.Max(0.01d, totalWet - totalPropellant);
            stats.TotalThrustkN = totalThrust;
            stats.VacuumIspSeconds = totalThrust > 0 ? totalWeightedVac / totalThrust : 0;
            stats.SeaLevelIspSeconds = totalThrust > 0 ? totalWeightedSea / totalThrust : 0;

            return stats;
        }

        private static void SampleAtmosphereCurve(ModuleEngines engine, EngineEntry entry)
        {
            if (engine.atmosphereCurve == null) return;
            const int n = 11;
            entry.PressureSamples = new double[n];
            entry.IspSamples = new double[n];
            for (int i = 0; i < n; i++)
            {
                double p = i / (double)(n - 1);
                entry.PressureSamples[i] = p;
                entry.IspSamples[i] = (double)engine.atmosphereCurve.Evaluate((float)p);
            }
        }

        private static void SampleThrustCurve(ModuleEngines engine, EngineEntry entry)
        {
            if (!engine.useThrustCurve || engine.thrustCurve == null) return;
            const int n = 21;
            entry.ThrustCurveFractions = new double[n];
            entry.ThrustCurveMultipliers = new double[n];
            for (int i = 0; i < n; i++)
            {
                double f = i / (double)(n - 1);
                entry.ThrustCurveFractions[i] = f;
                entry.ThrustCurveMultipliers[i] = (double)engine.thrustCurve.Evaluate((float)f);
            }
        }

        /// <summary>
        /// Finds decouplers in ALL stages that fire after the bottom stage. When decouplers fire,
        /// they separate boosters (liquid or solid). We model: when engines on separated parts exhaust,
        /// subtract that mass from the stack. Multi-pair boosters (SRBs/liquid) separate at different
        /// stages (e.g. stage 5, 4, 3); we must detect all of them, not just nextStage.
        /// Uses multiple detection paths: part.children, AttachNode.attachedPart (excluding parent), and parent-inverse.
        /// </summary>
        private static void BuildSeparationGroups(IList<Part> parts, Dictionary<int, StageInfo> stageMap, HashSet<string> liquidPropNames)
        {
            if (parts == null || parts.Count == 0 || stageMap == null) return;

            int maxPropStageNum = -1;
            foreach (var si in stageMap.Values)
            {
                if (si.HasEngines && si.PropellantMassTons > 0.0d && si.StageNumber > maxPropStageNum)
                    maxPropStageNum = si.StageNumber;
            }
            if (maxPropStageNum <= 0)
                return;

            StageInfo bottomStage;
            if (!stageMap.TryGetValue(maxPropStageNum, out bottomStage)
                || bottomStage == null || bottomStage.Engines == null || bottomStage.Engines.Count == 0)
                return;

            var decouplers = new List<Part>();
            for (int st = maxPropStageNum - 1; st >= 0; st--)
            {
                foreach (var part in parts)
                {
                    if (part == null || IsLaunchClamp(part)) continue;
                    if (part.inverseStage != st) continue;
                    if (!part.Modules.Contains("ModuleDecouple") && !part.Modules.Contains("ModuleAnchoredDecoupler"))
                        continue;
                    decouplers.Add(part);
                }
            }
            if (decouplers.Count == 0)
                return;

            var releasedByDecoupler = new List<HashSet<int>>();
            foreach (var dec in decouplers)
            {
                var released = new HashSet<int>();
                // Path 1: part.children (standard hierarchy)
                foreach (Part child in dec.children ?? Enumerable.Empty<Part>())
                    CollectPartAndDescendants(child, released);
                // Path 2: AttachNode.attachedPart for radial/stack (exclude parent - core stays)
                if (dec.attachNodes != null)
                {
                    foreach (var an in dec.attachNodes)
                    {
                        if (an?.attachedPart == null) continue;
                        if (an.attachedPart == dec.parent) continue;
                        CollectPartAndDescendants(an.attachedPart, released);
                    }
                }
                // Path 3: inverse - parts whose parent is this decoupler (radial booster attached to decoupler)
                foreach (var p in parts)
                {
                    if (p == null || p.parent != dec) continue;
                    CollectPartAndDescendants(p, released);
                }
                if (released.Count > 0)
                {
                    releasedByDecoupler.Add(released);
                }
            }

            if (releasedByDecoupler.Count == 0)
                return;

            var liquidNames = liquidPropNames != null
                ? new HashSet<string>(liquidPropNames.Where(n => !string.Equals(n, "SolidFuel", StringComparison.OrdinalIgnoreCase)), StringComparer.OrdinalIgnoreCase)
                : new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var releasedIds in releasedByDecoupler)
            {
                double groupDryMass = 0d;
                double groupLiquidPropTons = 0d;
                foreach (var p in parts)
                {
                    if (p == null || !releasedIds.Contains(p.GetInstanceID())) continue;
                    double partTotalMass = (double)p.mass;
                    double partResourceMass = 0d;
                    if (p.Resources != null)
                    {
                        foreach (PartResource res in p.Resources)
                        {
                            if (res?.info == null) continue;
                            double m = res.amount * res.info.density;
                            partResourceMass += m;
                            if (liquidNames.Count > 0 && liquidNames.Contains(res.resourceName))
                                groupLiquidPropTons += m;
                        }
                    }
                    double partDryMass = partResourceMass < partTotalMass - 0.001d
                        ? partTotalMass - partResourceMass
                        : partTotalMass;
                    groupDryMass += Math.Max(0.001d, partDryMass);
                }
                if (groupDryMass <= 0d) continue;

                var group = new SeparationGroup
                {
                    GroupDryMassTons = groupDryMass,
                    GroupLiquidPropellantTons = groupLiquidPropTons,
                    EngineIndices = new HashSet<int>()
                };
                for (int i = 0; i < bottomStage.Engines.Count; i++)
                {
                    if (releasedIds.Contains(bottomStage.Engines[i].PartInstanceId))
                    {
                        group.EngineIndices.Add(i);
                        bottomStage.Engines[i].SeparationGroupIndex = bottomStage.SeparationGroups.Count;
                    }
                }
                if (group.EngineIndices.Count > 0)
                    bottomStage.SeparationGroups.Add(group);
            }
        }

        private static void CollectPartAndDescendants(Part p, HashSet<int> outIds)
        {
            if (p == null || outIds.Contains(p.GetInstanceID())) return;
            outIds.Add(p.GetInstanceID());
            if (p.children == null) return;
            foreach (Part c in p.children)
                CollectPartAndDescendants(c, outIds);
        }

        private static bool IsLaunchClamp(Part part)
        {
            return part.Modules.Contains("LaunchClamp")
                || part.Modules.Contains("ModuleLaunchClamp");
        }
    }
}
