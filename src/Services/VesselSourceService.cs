using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using KSP.Localization;
using OrbitalPayloadCalculator.Calculation;
using UnityEngine;

namespace OrbitalPayloadCalculator.Services
{
    internal sealed class VesselSourceService
    {
        private const double SettlingThrustFractionOfMax = 0.01d;
        private static readonly string EngineOverridePath =
            Path.Combine(KSPUtil.ApplicationRootPath, "GameData", "OrbitalPayloadCalculator", "PluginData", "engine-role-overrides.cfg");
        private static readonly Dictionary<string, Dictionary<int, EngineRole>> EngineRoleOverrides =
            LoadEngineRoleOverrides();

        /// <summary> Cache expiry: rebuild list at most every N frames (~4/sec at 60 FPS). </summary>
        private const int CacheLifetimeFrames = 15;

        private readonly bool _isEditor;
        private readonly List<Vessel> _flightCandidates = new List<Vessel>();
        private int _selectedFlightIndex;
        private int _cachedVesselCount = -1;
        private int _lastBuildFrame = -1;

        /// <summary> When true, ModuleCargoBay parts are treated as fairings (excluded at jettison). </summary>
        public bool TreatCargoBayAsFairing { get; set; }

        public VesselSourceService(bool isEditor)
        {
            _isEditor = isEditor;
        }

        /// <summary> Force cache invalidation on next GetFlightCandidates call. </summary>
        public void InvalidateFlightCandidatesCache()
        {
            _cachedVesselCount = -1;
        }

        public IReadOnlyList<Vessel> GetFlightCandidates()
        {
            if (_isEditor || FlightGlobals.VesselsLoaded == null)
            {
                _flightCandidates.Clear();
                return _flightCandidates;
            }

            var currentCount = FlightGlobals.VesselsLoaded.Count;
            var currentFrame = Time.frameCount;
            var cacheValid = _cachedVesselCount == currentCount
                && _lastBuildFrame >= 0
                && (currentFrame - _lastBuildFrame) < CacheLifetimeFrames;

            if (cacheValid)
            {
                if (_selectedFlightIndex >= _flightCandidates.Count)
                    _selectedFlightIndex = Mathf.Max(0, _flightCandidates.Count - 1);
                return _flightCandidates;
            }

            _flightCandidates.Clear();
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

            _cachedVesselCount = currentCount;
            _lastBuildFrame = currentFrame;
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

        public void SetEngineRoleOverride(string vesselKey, int partInstanceId, EngineRole role)
        {
            if (string.IsNullOrEmpty(vesselKey)) return;
            if (!EngineRoleOverrides.TryGetValue(vesselKey, out var map))
            {
                map = new Dictionary<int, EngineRole>();
                EngineRoleOverrides[vesselKey] = map;
            }
            map[partInstanceId] = role;
            SaveEngineRoleOverrides();
        }

        public void ClearEngineRoleOverride(string vesselKey, int partInstanceId)
        {
            if (string.IsNullOrEmpty(vesselKey)) return;
            if (!EngineRoleOverrides.TryGetValue(vesselKey, out var map)) return;
            if (!map.Remove(partInstanceId)) return;
            if (map.Count == 0)
                EngineRoleOverrides.Remove(vesselKey);
            SaveEngineRoleOverrides();
        }

        public void ClearAllEngineRoleOverrides(string vesselKey)
        {
            if (string.IsNullOrEmpty(vesselKey)) return;
            if (!EngineRoleOverrides.Remove(vesselKey)) return;
            SaveEngineRoleOverrides();
        }

        private VesselStats ReadFromFlightSelection()
        {
            GetFlightCandidates();
            if (_flightCandidates.Count == 0)
                return new VesselStats { HasVessel = false };

            var selected = _flightCandidates[_selectedFlightIndex];
            if (selected == null)
                return new VesselStats { HasVessel = false };

            var vesselKey = $"flight:{selected.id:N}";
            return BuildStatsFromParts(selected.vesselName, vesselKey, selected.Parts, true, GetStageCount(selected), TreatCargoBayAsFairing);
        }

        private VesselStats ReadFromEditor()
        {
            if (EditorLogic.fetch == null || EditorLogic.fetch.ship == null)
                return new VesselStats { HasVessel = false };

            var ship = EditorLogic.fetch.ship;
            if (ship.parts == null || ship.parts.Count == 0)
                return new VesselStats { HasVessel = false };

            var stageCount = KSP.UI.Screens.StageManager.StageCount;
            var vesselKey = $"editor:{(ship.shipName ?? "untitled").Trim()}";
            return BuildStatsFromParts(ship.shipName, vesselKey, ship.parts, false, stageCount, TreatCargoBayAsFairing);
        }

        /// <summary>
        /// Fuel tanks and engines may end up in different inverseStage groups.
        /// KSP allows crossfeed so engines can burn fuel from tanks in other stages.
        /// Move propellant from stages that have no dV-participating engines (e.g. Retro/Settling only)
        /// to the engine stage that would consume it (Main/Solid/Electric).
        /// Source: stage has propellant but no Main/Solid/Electric engines (or no engines at all).
        /// Target: the NEAREST dV stage that fires before we drop the source (min StageNumber among
        /// candidates &gt;= source). Upper stage fuel (S0,S1) goes to S2; lower stage fuel (S3) goes to S4.
        /// </summary>
        private static void RedistributePropellant(Dictionary<int, StageInfo> stageMap)
        {
            foreach (var si in stageMap.Values)
            {
                if (si.PropellantMassTons <= 0.0d)
                    continue;
                if (StageHasDvParticipatingEngines(si))
                    continue;

                StageInfo target = null;
                foreach (var candidate in stageMap.Values)
                {
                    if (!StageHasDvParticipatingEngines(candidate))
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
                        if (!StageHasDvParticipatingEngines(candidate))
                            continue;
                        if (target == null ||
                            Math.Abs(candidate.StageNumber - si.StageNumber) < Math.Abs(target.StageNumber - si.StageNumber))
                            target = candidate;
                    }
                }

                if (target != null)
                {
                    target.PropellantMassTons += si.PropellantMassTons;
                    target.BoosterPhasePropellantTons += si.BoosterPhasePropellantTons;
                    if (si.PropellantMassByName != null)
                    {
                        foreach (var kv in si.PropellantMassByName)
                        {
                            if (!target.PropellantMassByName.ContainsKey(kv.Key))
                                target.PropellantMassByName[kv.Key] = 0d;
                            target.PropellantMassByName[kv.Key] += kv.Value;
                        }
                    }
                    si.PropellantMassTons = 0.0d;
                    si.BoosterPhasePropellantTons = 0.0d;
                    si.PropellantMassByName.Clear();
                }
            }
        }

        private static bool StageHasDvParticipatingEngines(StageInfo si)
        {
            if (si?.Engines == null) return false;
            return si.Engines.Any(e => PayloadCalculator.IsDvParticipatingRole(e.Role));
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

        private struct FuelLineEdge
        {
            public int SourcePartId;
            public int TargetPartId;
        }

        private static readonly string[] FuelLineModuleNames = { "CModuleFuelLine", "ModuleFuelLine", "FuelLine" };

        /// <summary>True if part is a fuel line (FTX-2 External Fuel Duct / CModuleFuelLine).</summary>
        private static bool IsFuelLinePart(Part part)
        {
            if (part == null) return false;
            var pname = part.partInfo?.name ?? part.name ?? "";
            if (string.Equals(pname, "fuelLine", StringComparison.OrdinalIgnoreCase))
                return true;
            if (HasFuelLineModule(part))
                return true;
            var typeName = part.GetType().Name;
            return string.Equals(typeName, "CompoundPart", StringComparison.OrdinalIgnoreCase)
                   && HasFuelLineModule(part);
        }

        private static bool HasFuelLineModule(Part part)
        {
            if (part?.Modules == null) return false;
            foreach (var modName in FuelLineModuleNames)
                if (part.Modules.Contains(modName)) return true;
            return false;
        }

        /// <summary>Gets fuel line endpoints: source (fuel flows FROM) and target (fuel flows TO).
        /// Asparagus: source=booster tank, target=core. Parent is usually the booster (source).
        /// CompoundPart has attachNodes=0, so we use reflection to find the other Part.</summary>
        private static bool TryGetFuelLineEndpoints(Part fuelLinePart, out Part source, out Part target)
        {
            source = null;
            target = null;
            if (fuelLinePart == null || !IsFuelLinePart(fuelLinePart))
                return false;
            source = fuelLinePart.parent;
            if (source == null)
                return false;

            Part other = null;
            if (fuelLinePart.attachNodes != null)
            {
                foreach (var an in fuelLinePart.attachNodes)
                {
                    if (an?.attachedPart == null) continue;
                    if (an.attachedPart == source) continue;
                    other = an.attachedPart;
                    break;
                }
            }
            if (other == null)
                other = GetCompoundPartOtherViaReflection(fuelLinePart, source);
            if (other == null)
                return false;
            target = other;
            return true;
        }

        /// <summary>CompoundPart has attachNodes=0; get the other-end Part via reflection.</summary>
        private static Part GetCompoundPartOtherViaReflection(Part part, Part exclude)
        {
            if (part == null) return null;
            try
            {
                var type = part.GetType();
                foreach (var f in type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
                {
                    if (f.FieldType != typeof(Part)) continue;
                    var val = f.GetValue(part) as Part;
                    if (val == null || val == part || val == exclude) continue;
                    return val;
                }
            }
            catch { }
            return null;
        }

        /// <summary>Builds fuel flow graph: sourcePartId -> list of targetPartIds. Only includes parts present in parts set.</summary>
        private static Dictionary<int, List<int>> BuildFuelFlowGraph(IList<Part> parts, List<FuelLineEdge> edges)
        {
            var partIds = new HashSet<int>();
            if (parts != null)
            {
                foreach (var p in parts)
                {
                    if (p != null)
                        partIds.Add(p.GetInstanceID());
                }
            }
            var graph = new Dictionary<int, List<int>>();
            if (edges == null) return graph;
            foreach (var e in edges)
            {
                if (!partIds.Contains(e.SourcePartId) || !partIds.Contains(e.TargetPartId))
                    continue;
                if (!graph.TryGetValue(e.SourcePartId, out var targets))
                {
                    targets = new List<int>();
                    graph[e.SourcePartId] = targets;
                }
                if (!targets.Contains(e.TargetPartId))
                    targets.Add(e.TargetPartId);
            }
            return graph;
        }

        /// <summary>For tanks that are fuel line sources, move their propellant to the target stage (where fuel flows to).</summary>
        private static void AssignPropellantByFuelLines(
            IList<Part> parts,
            Dictionary<int, StageInfo> stageMap,
            Dictionary<int, List<int>> flowGraph,
            Dictionary<int, int> partIdToStageNum,
            HashSet<string> dvPropNames,
            HashSet<int> stagesWithDvEngines)
        {
            if (parts == null || stageMap == null || flowGraph == null || flowGraph.Count == 0)
                return;
            const int maxBfsDepth = 32;
            foreach (var part in parts)
            {
                if (part == null || IsExcludedFromCalculation(part)) continue;
                var sourceId = part.GetInstanceID();
                if (!flowGraph.TryGetValue(sourceId, out var targets) || targets == null || targets.Count == 0)
                    continue;
                if (!stageMap.TryGetValue(part.inverseStage, out var sourceStage))
                    continue;
                double partPropMass = 0d;
                var partPropByName = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
                foreach (PartResource res in part.Resources ?? Enumerable.Empty<PartResource>())
                {
                    if (res?.info == null || !dvPropNames.Contains(res.resourceName))
                        continue;
                    var mass = res.amount * res.info.density;
                    if (mass <= 1e-9d) continue;
                    partPropMass += mass;
                    partPropByName[res.resourceName] = mass;
                }
                if (partPropMass <= 1e-9d) continue;

                int bestTargetStage = -1;
                var visited = new HashSet<int> { sourceId };
                var queue = new Queue<int>();
                foreach (var t in targets)
                    queue.Enqueue(t);
                var depth = 0;
                while (queue.Count > 0 && depth < maxBfsDepth)
                {
                    var count = queue.Count;
                    for (var i = 0; i < count; i++)
                    {
                        var pid = queue.Dequeue();
                        if (!visited.Add(pid)) continue;
                        if (!partIdToStageNum.TryGetValue(pid, out var targetStageNum))
                            continue;
                        if (stagesWithDvEngines.Contains(targetStageNum))
                        {
                            if (bestTargetStage < 0 || targetStageNum > bestTargetStage)
                                bestTargetStage = targetStageNum;
                        }
                        if (flowGraph.TryGetValue(pid, out var nextTargets))
                        {
                            foreach (var nt in nextTargets)
                            {
                                if (!visited.Contains(nt))
                                    queue.Enqueue(nt);
                            }
                        }
                    }
                    if (bestTargetStage >= 0) break;
                    depth++;
                }
                if (bestTargetStage < 0)
                {
                    var maxDvStage = stagesWithDvEngines.Count > 0 ? stagesWithDvEngines.Max() : -1;
                    if (maxDvStage >= 0)
                        bestTargetStage = maxDvStage;
                    else
                    {
                        foreach (var t in targets)
                        {
                            if (partIdToStageNum.TryGetValue(t, out var ts))
                            {
                                if (bestTargetStage < 0 || ts > bestTargetStage)
                                    bestTargetStage = ts;
                            }
                        }
                    }
                }
                if (bestTargetStage < 0 || !stageMap.TryGetValue(bestTargetStage, out var targetStage))
                    continue;

                sourceStage.PropellantMassTons -= partPropMass;
                foreach (var kv in partPropByName)
                {
                    if (sourceStage.PropellantMassByName.TryGetValue(kv.Key, out var curr))
                    {
                        var next = Math.Max(0d, curr - kv.Value);
                        if (next <= 1e-9d)
                            sourceStage.PropellantMassByName.Remove(kv.Key);
                        else
                            sourceStage.PropellantMassByName[kv.Key] = next;
                    }
                }
                targetStage.PropellantMassTons += partPropMass;
                targetStage.BoosterPhasePropellantTons += partPropMass;
                foreach (var kv in partPropByName)
                {
                    if (!targetStage.PropellantMassByName.ContainsKey(kv.Key))
                        targetStage.PropellantMassByName[kv.Key] = 0d;
                    targetStage.PropellantMassByName[kv.Key] += kv.Value;
                }
            }
        }

        private sealed class EngineBuildRecord
        {
            public int StageNumber;
            public Part Part;
            public ModuleEngines Engine;
            public double ThrustkN;
            public double VacuumIsp;
            public double SeaLevelIsp;
            public bool SelfContainedPropellant;
            public bool HasAbortAction;
            public Vector3d ThrustDirection;
            public List<string> PropellantNames = new List<string>();
            public EngineRole Role = EngineRole.Main;
        }

        private static VesselStats BuildStatsFromParts(string vesselName, string vesselKey, IList<Part> parts, bool fromFlight, int stageCount, bool treatCargoBayAsFairing = false)
        {
            var stats = new VesselStats
            {
                HasVessel = parts != null && parts.Count > 0,
                VesselName = vesselName ?? string.Empty,
                VesselPersistentKey = vesselKey ?? string.Empty,
                FromFlight = fromFlight,
                TotalStages = stageCount
            };

            if (!stats.HasVessel) return stats;

            var stageMap = new Dictionary<int, StageInfo>();
            var engineRecords = new List<EngineBuildRecord>();

            double totalWet = 0, totalThrust = 0, totalWeightedVac = 0, totalWeightedSea = 0;

            foreach (var part in parts)
            {
                if (part == null) continue;
                if (IsExcludedFromCalculation(part)) continue;

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

                    var vacIsp = (double)engine.atmosphereCurve.Evaluate(0.0f);
                    var seaIsp = (double)engine.atmosphereCurve.Evaluate(1.0f);
                    engineRecords.Add(new EngineBuildRecord
                    {
                        StageNumber = stageNum,
                        Part = part,
                        Engine = engine,
                        ThrustkN = engine.maxThrust,
                        VacuumIsp = vacIsp,
                        SeaLevelIsp = seaIsp,
                        SelfContainedPropellant = IsSelfContainedPropellantEngine(engine, part),
                        HasAbortAction = HasAbortAction(engine),
                        ThrustDirection = GetEngineThrustDirection(engine, part),
                        PropellantNames = GetEnginePropellantNames(engine)
                    });
                }
            }

            ClassifyEngines(engineRecords);

            var partIdToStageNum = new Dictionary<int, int>();
            foreach (var part in parts)
            {
                if (part == null || IsExcludedFromCalculation(part)) continue;
                partIdToStageNum[part.GetInstanceID()] = part.inverseStage;
            }

            var fuelLineEdges = new List<FuelLineEdge>();
            foreach (var part in parts)
            {
                if (part == null || IsExcludedFromCalculation(part)) continue;
                if (IsFuelLinePart(part) && TryGetFuelLineEndpoints(part, out var src, out var tgt) && src != null && tgt != null)
                {
                    fuelLineEdges.Add(new FuelLineEdge
                    {
                        SourcePartId = src.GetInstanceID(),
                        TargetPartId = tgt.GetInstanceID()
                    });
                }
            }
            var stagesWithDvEngines = new HashSet<int>();
            foreach (var record in engineRecords)
            {
                if (!PayloadCalculator.IsDvParticipatingRole(record.Role)) continue;
                stagesWithDvEngines.Add(record.StageNumber);
            }

            var dvPropNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var record in engineRecords)
            {
                if (!PayloadCalculator.IsDvParticipatingRole(record.Role))
                    continue;
                foreach (var prop in record.PropellantNames)
                {
                    if (string.IsNullOrEmpty(prop))
                        continue;
                    dvPropNames.Add(prop);
                }
            }

            foreach (var record in engineRecords)
            {
                if (!stageMap.TryGetValue(record.StageNumber, out var si))
                    continue;
                var role = ApplyRoleOverride(vesselKey, record.Part.GetInstanceID(), record.Role);
                var solidPropMass = role == EngineRole.Solid
                    ? GetPartPropellantMass(record.Part, record.PropellantNames)
                    : 0d;
                var partTitle = record.Part?.partInfo?.title;
                var partDisplayName = !string.IsNullOrEmpty(partTitle)
                    ? Localizer.Format(partTitle)
                    : (record.Part?.partInfo?.name ?? "?");
                var entry = new EngineEntry
                {
                    ThrustkN = record.ThrustkN,
                    VacuumIsp = record.VacuumIsp,
                    SeaLevelIsp = record.SeaLevelIsp,
                    Role = role,
                    PropellantMassTons = solidPropMass,
                    PropellantNames = new List<string>(record.PropellantNames),
                    PartDryMassTons = (double)record.Part.mass,
                    PartInstanceId = record.Part.GetInstanceID(),
                    PartDisplayName = partDisplayName ?? string.Empty,
                    SeparationGroupIndex = -1
                };
                SampleAtmosphereCurve(record.Engine, entry);
                if (entry.Role == EngineRole.Solid)
                    SampleThrustCurve(record.Engine, entry);
                si.Engines.Add(entry);
            }

            foreach (var part in parts)
            {
                if (part == null || IsExcludedFromCalculation(part)) continue;
                if (!stageMap.TryGetValue(part.inverseStage, out var si)) continue;

                foreach (PartResource res in part.Resources)
                {
                    if (!dvPropNames.Contains(res.resourceName))
                        continue;
                    var mass = res.amount * res.info.density;
                    si.PropellantMassTons += mass;
                    if (!si.PropellantMassByName.ContainsKey(res.resourceName))
                        si.PropellantMassByName[res.resourceName] = 0d;
                    si.PropellantMassByName[res.resourceName] += mass;
                }
            }

            var flowGraph = BuildFuelFlowGraph(parts, fuelLineEdges);
            AssignPropellantByFuelLines(parts, stageMap, flowGraph, partIdToStageNum, dvPropNames, stagesWithDvEngines);

            foreach (var si in stageMap.Values)
            {
                si.HasEngines = si.Engines.Count > 0;
            }
            RedistributePropellant(stageMap);
            BuildFairingMasses(parts, stageMap, treatCargoBayAsFairing);

            foreach (var si in stageMap.Values)
            {
                si.HasSolidFuel = si.Engines.Any(e => e.Role == EngineRole.Solid);
                si.ThrustkN = 0d;
                si.VacuumIsp = 0d;
                si.SeaLevelIsp = 0d;
                double stageWeightedVac = 0d;
                double stageWeightedSea = 0d;
                foreach (var e in si.Engines)
                {
                    if (!PayloadCalculator.IsDvParticipatingRole(e.Role))
                        continue;
                    si.ThrustkN += e.ThrustkN;
                    stageWeightedVac += e.ThrustkN * e.VacuumIsp;
                    stageWeightedSea += e.ThrustkN * e.SeaLevelIsp;
                }
                if (si.ThrustkN > 0d)
                {
                    si.VacuumIsp = stageWeightedVac / si.ThrustkN;
                    si.SeaLevelIsp = stageWeightedSea / si.ThrustkN;
                }
            }

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

            var liquidDvPropNames = new HashSet<string>(dvPropNames.Where(n => !string.Equals(n, "SolidFuel", StringComparison.OrdinalIgnoreCase)), StringComparer.OrdinalIgnoreCase);
            var fuelLineSourcePartIds = new HashSet<int>();
            foreach (var e in fuelLineEdges)
                fuelLineSourcePartIds.Add(e.SourcePartId);
            BuildSeparationGroups(parts, stageMap, liquidDvPropNames, fuelLineSourcePartIds);

            foreach (var si in stageList)
            {
                totalThrust += si.ThrustkN;
                totalWeightedVac += si.ThrustkN * si.VacuumIsp;
                totalWeightedSea += si.ThrustkN * si.SeaLevelIsp;
            }

            stats.WetMassTons = totalWet;
            stats.DryMassTons = Math.Max(0.01d, totalWet - totalPropellant);
            stats.TotalThrustkN = totalThrust;
            stats.VacuumIspSeconds = totalThrust > 0 ? totalWeightedVac / totalThrust : 0;
            stats.SeaLevelIspSeconds = totalThrust > 0 ? totalWeightedSea / totalThrust : 0;

            return stats;
        }

        private static EngineRole ApplyRoleOverride(string vesselKey, int partInstanceId, EngineRole autoRole)
        {
            if (string.IsNullOrEmpty(vesselKey))
                return autoRole;
            if (!EngineRoleOverrides.TryGetValue(vesselKey, out var map))
                return autoRole;
            return map.TryGetValue(partInstanceId, out var overrideRole) ? overrideRole : autoRole;
        }

        private static List<string> GetEnginePropellantNames(ModuleEngines engine)
        {
            var names = new List<string>();
            if (engine?.propellants == null) return names;
            foreach (var prop in engine.propellants)
            {
                if (prop == null || string.IsNullOrEmpty(prop.name)) continue;
                if (!names.Contains(prop.name))
                    names.Add(prop.name);
            }
            return names;
        }

        /// <summary>
        /// True if the part has fuel capability for all engine propellants (PartResource exists),
        /// not whether it currently has fuel. Empty tanks still qualify as self-contained.
        /// </summary>
        private static bool IsSelfContainedPropellantEngine(ModuleEngines engine, Part part)
        {
            if (engine?.propellants == null || part?.Resources == null) return false;
            bool hasRelevantPropellant = false;
            foreach (var prop in engine.propellants)
            {
                if (prop == null || string.IsNullOrEmpty(prop.name))
                    continue;
                if (string.Equals(prop.name, "ElectricCharge", StringComparison.OrdinalIgnoreCase))
                    continue;
                hasRelevantPropellant = true;
                bool found = false;
                foreach (PartResource res in part.Resources)
                {
                    if (!string.Equals(res.resourceName, prop.name, StringComparison.OrdinalIgnoreCase))
                        continue;
                    if (res.maxAmount >= 1e-9d)
                    {
                        found = true;
                        break;
                    }
                }
                if (!found)
                    return false;
            }
            return hasRelevantPropellant;
        }

        private static double GetPartPropellantMass(Part part, IEnumerable<string> propellantNames)
        {
            if (part?.Resources == null || propellantNames == null) return 0d;
            var nameSet = new HashSet<string>(propellantNames.Where(n => !string.IsNullOrEmpty(n)), StringComparer.OrdinalIgnoreCase);
            double mass = 0d;
            foreach (PartResource res in part.Resources)
            {
                if (!nameSet.Contains(res.resourceName))
                    continue;
                mass += res.amount * res.info.density;
            }
            return mass;
        }

        private static bool HasAbortAction(ModuleEngines engine)
        {
            if (engine?.Actions == null) return false;
            foreach (BaseAction action in engine.Actions)
            {
                if (action == null) continue;
                if ((action.actionGroup & KSPActionGroup.Abort) != 0)
                    return true;
            }
            return false;
        }

        private static Vector3d GetEngineThrustDirection(ModuleEngines engine, Part part)
        {
            if (engine?.thrustTransforms != null && engine.thrustTransforms.Count > 0 && part?.transform != null)
            {
                Vector3d sum = Vector3d.zero;
                for (int i = 0; i < engine.thrustTransforms.Count; i++)
                {
                    var tr = engine.thrustTransforms[i];
                    if (tr == null) continue;
                    var dir = part.transform.TransformDirection(-tr.forward);
                    if (dir.sqrMagnitude <= 1e-12d) continue;
                    sum += dir.normalized;
                }
                if (sum.sqrMagnitude > 1e-12d)
                    return sum.normalized;
            }

            if (part?.transform != null)
            {
                var fallback = part.transform.up;
                if (fallback.sqrMagnitude > 1e-12d)
                    return fallback.normalized;
            }
            return Vector3d.up;
        }

        private static void ClassifyEngines(List<EngineBuildRecord> records)
        {
            if (records == null || records.Count == 0) return;

            foreach (var r in records)
            {
                bool isElectric = r.PropellantNames.Any(p => string.Equals(p, "ElectricCharge", StringComparison.OrdinalIgnoreCase));
                if (isElectric)
                    r.Role = EngineRole.Electric;
            }

            foreach (var r in records)
            {
                if (r.Role == EngineRole.Electric) continue;
                if (r.SelfContainedPropellant && r.HasAbortAction)
                    r.Role = EngineRole.EscapeTower;
            }

            double maxThrustkN = records
                .Where(r => r.Role != EngineRole.Electric && r.Role != EngineRole.EscapeTower)
                .Sum(r => r.ThrustkN);
            double settlingThresholdkN = Math.Max(0.1d, maxThrustkN * SettlingThrustFractionOfMax);
            var bottomMainDirection = GetBottomMainThrustDirection(records);

            foreach (var r in records)
            {
                if (r.Role == EngineRole.Electric || r.Role == EngineRole.EscapeTower)
                    continue;
                var dot = Vector3d.Dot(r.ThrustDirection.normalized, bottomMainDirection);
                if (r.SelfContainedPropellant && r.ThrustkN < settlingThresholdkN && dot > 0.9d)
                    r.Role = EngineRole.Settling;
            }

            foreach (var r in records)
            {
                if (r.Role == EngineRole.Electric || r.Role == EngineRole.EscapeTower || r.Role == EngineRole.Settling)
                    continue;
                var dot = Vector3d.Dot(r.ThrustDirection.normalized, bottomMainDirection);
                if (dot < 0.8d)
                    r.Role = EngineRole.Retro;
            }

            foreach (var r in records)
            {
                if (r.Role == EngineRole.Electric || r.Role == EngineRole.EscapeTower || r.Role == EngineRole.Settling || r.Role == EngineRole.Retro)
                    continue;
                r.Role = r.SelfContainedPropellant ? EngineRole.Solid : EngineRole.Main;
            }
        }

        /// <summary>Thrust direction of the highest-thrust engine in the bottom-most stage (for retro detection).</summary>
        private static Vector3d GetBottomMainThrustDirection(List<EngineBuildRecord> records)
        {
            var bottom = records
                .Where(r => r.Role != EngineRole.Electric && r.Role != EngineRole.EscapeTower)
                .OrderByDescending(r => r.StageNumber)
                .ThenByDescending(r => r.ThrustkN)
                .FirstOrDefault();
            if (bottom == null)
                return Vector3d.up;
            var dir = bottom.ThrustDirection;
            if (dir.sqrMagnitude <= 1e-12d)
                return Vector3d.up;
            return dir.normalized;
        }

        private static Dictionary<string, Dictionary<int, EngineRole>> LoadEngineRoleOverrides()
        {
            var result = new Dictionary<string, Dictionary<int, EngineRole>>(StringComparer.OrdinalIgnoreCase);
            try
            {
                if (!File.Exists(EngineOverridePath))
                    return result;
                var node = ConfigNode.Load(EngineOverridePath);
                if (node == null) return result;
                foreach (var vesselNode in node.GetNodes("VESSEL"))
                {
                    var vesselKey = vesselNode.GetValue("key");
                    if (string.IsNullOrEmpty(vesselKey)) continue;
                    var map = new Dictionary<int, EngineRole>();
                    foreach (var ov in vesselNode.GetNodes("OVERRIDE"))
                    {
                        var partStr = ov.GetValue("partId");
                        var roleStr = ov.GetValue("role");
                        if (!int.TryParse(partStr, NumberStyles.Integer, CultureInfo.InvariantCulture, out var partId))
                            continue;
                        if (!Enum.TryParse(roleStr, true, out EngineRole role))
                            continue;
                        map[partId] = role;
                    }
                    if (map.Count > 0)
                        result[vesselKey] = map;
                }
            }
            catch
            {
                // ignore malformed override file and continue with defaults.
            }
            return result;
        }

        private static void SaveEngineRoleOverrides()
        {
            try
            {
                var root = new ConfigNode("OPC_ENGINE_ROLE_OVERRIDES");
                foreach (var vesselKv in EngineRoleOverrides)
                {
                    var vesselNode = root.AddNode("VESSEL");
                    vesselNode.AddValue("key", vesselKv.Key);
                    foreach (var roleKv in vesselKv.Value)
                    {
                        var ov = vesselNode.AddNode("OVERRIDE");
                        ov.AddValue("partId", roleKv.Key.ToString(CultureInfo.InvariantCulture));
                        ov.AddValue("role", roleKv.Value.ToString());
                    }
                }
                var dir = Path.GetDirectoryName(EngineOverridePath);
                if (!string.IsNullOrEmpty(dir))
                    Directory.CreateDirectory(dir);
                root.Save(EngineOverridePath);
            }
            catch
            {
                // best-effort persistence
            }
        }

        /// <summary>
        /// Samples engine atmosphereCurve from 0 (vacuum) to 10 atm (Eve-level) for all-body support.
        /// </summary>
        private static void SampleAtmosphereCurve(ModuleEngines engine, EngineEntry entry)
        {
            if (engine.atmosphereCurve == null) return;
            var pressures = new[] { 0.0, 0.25, 0.5, 1.0, 2.0, 3.0, 5.0, 7.0, 10.0 };
            entry.PressureSamples = new double[pressures.Length];
            entry.IspSamples = new double[pressures.Length];
            for (int i = 0; i < pressures.Length; i++)
            {
                entry.PressureSamples[i] = pressures[i];
                entry.IspSamples[i] = (double)engine.atmosphereCurve.Evaluate((float)pressures[i]);
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
        /// Identifies fairing parts (jettisoned at staging): stock, Procedural Fairings mod, cargo bays.
        /// Uses part.inverseStage as jettison stage (when that stage fires, fairing is released).
        /// </summary>
        private static void BuildFairingMasses(IList<Part> parts, Dictionary<int, StageInfo> stageMap, bool treatCargoBayAsFairing = false)
        {
            if (parts == null || stageMap == null) return;

            foreach (var part in parts)
            {
                if (part == null || IsExcludedFromCalculation(part)) continue;
                if (!IsFairingPart(part, treatCargoBayAsFairing)) continue;
                if (!stageMap.TryGetValue(part.inverseStage, out var si)) continue;

                double partMass = (double)part.mass;
                if (part.Resources != null)
                    foreach (PartResource res in part.Resources)
                        if (res?.info != null)
                            partMass += res.amount * res.info.density;

                if (partMass > 1e-9d)
                    si.FairingMassTons += partMass;
            }
        }

        private static bool IsFairingPart(Part part, bool treatCargoBayAsFairing = false)
        {
            if (part?.Modules == null) return false;

            if (part.Modules.Contains("ModuleProceduralFairing"))
                return true;
            if (treatCargoBayAsFairing && part.Modules.Contains("ModuleCargoBay"))
                return true;
            if (part.Modules.Contains("ProceduralFairingSide"))
                return true;

            var name = part.partInfo?.name ?? "";
            if (string.IsNullOrEmpty(name)) return false;

            return name.IndexOf("fairing", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("shroud", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("nosecone", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>
        /// Finds decouplers in ALL stages that fire after the bottom stage. When decouplers fire,
        /// they separate boosters (liquid or solid). We model: when engines on separated parts exhaust,
        /// subtract that mass from the stack. Multi-pair boosters (SRBs/liquid) separate at different
        /// stages (e.g. stage 5, 4, 3); we must detect all of them, not just nextStage.
        /// Uses multiple detection paths: part.children, AttachNode.attachedPart (excluding parent), and parent-inverse.
        /// When a separation group contains any fuel line source (part feeding core via duct), the booster is
        /// empty at separation—all its fuel was drained to the core. Set GroupLiquidPropellantTons = 0 for such groups.
        /// </summary>
        private static void BuildSeparationGroups(IList<Part> parts, Dictionary<int, StageInfo> stageMap, HashSet<string> liquidPropNames, HashSet<int> fuelLineSourcePartIds = null)
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
                    if (part == null || IsExcludedFromCalculation(part)) continue;
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

            var excludeLiquidFromSources = fuelLineSourcePartIds ?? new HashSet<int>();

            var allReleasedPartIds = new HashSet<int>();
            var partDryMassById = new Dictionary<int, double>();

            foreach (var releasedIds in releasedByDecoupler)
            {
                double groupDryMass = 0d;
                double groupLiquidPropTons = 0d;
                var fuelLineSrcCountInGroup = 0;
                foreach (var p in parts)
                {
                    if (p == null || !releasedIds.Contains(p.GetInstanceID())) continue;
                    if (IsExcludedFromCalculation(p)) continue;
                    double partTotalMass = (double)p.mass;
                    double partResourceMass = 0d;
                    var isFuelLineSource = excludeLiquidFromSources.Contains(p.GetInstanceID());
                    if (isFuelLineSource) fuelLineSrcCountInGroup++;
                    if (p.Resources != null)
                    {
                        foreach (PartResource res in p.Resources)
                        {
                            if (res?.info == null) continue;
                            double m = res.amount * res.info.density;
                            partResourceMass += m;
                            if (liquidNames.Count > 0 && liquidNames.Contains(res.resourceName) && !isFuelLineSource)
                                groupLiquidPropTons += m;
                        }
                    }
                    double partDryMass = partResourceMass < partTotalMass - 0.001d
                        ? partTotalMass - partResourceMass
                        : partTotalMass;
                    groupDryMass += Math.Max(0.001d, partDryMass);
                }
                if (groupDryMass <= 0d) continue;

                if (fuelLineSrcCountInGroup > 0)
                    groupLiquidPropTons = 0d;

                var group = new SeparationGroup
                {
                    GroupDryMassTons = groupDryMass,
                    GroupLiquidPropellantTons = groupLiquidPropTons,
                    EngineIndices = new HashSet<int>(),
                    ReleasedPartIds = new HashSet<int>(releasedIds)
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
                {
                    bottomStage.SeparationGroups.Add(group);
                    foreach (var p in parts)
                    {
                        if (p == null || !releasedIds.Contains(p.GetInstanceID())) continue;
                        if (IsExcludedFromCalculation(p)) continue;
                        var pid = p.GetInstanceID();
                        allReleasedPartIds.Add(pid);
                        double partResourceMass = 0d;
                        if (p.Resources != null)
                        {
                            foreach (PartResource res in p.Resources)
                            {
                                if (res?.info == null) continue;
                                partResourceMass += res.amount * res.info.density;
                            }
                        }
                        var partDry = (double)p.mass - partResourceMass;
                        if (partDry < 0.001d) partDry = (double)p.mass;
                        partDryMassById[pid] = Math.Max(0.001d, partDry);
                    }
                }
            }

            if (allReleasedPartIds.Count > 0)
            {
                double uniqueDropped = 0d;
                bottomStage.PartDryMassTonsById = new Dictionary<int, double>();
                foreach (var pid in allReleasedPartIds)
                {
                    if (partDryMassById.TryGetValue(pid, out var dm))
                    {
                        var tons = dm;
                        uniqueDropped += tons;
                        bottomStage.PartDryMassTonsById[pid] = tons;
                    }
                }
                bottomStage.UniqueDroppedDryMassTons = uniqueDropped;
            }

            if (bottomStage.BoosterPhasePropellantTons > 0d)
            {
                bottomStage.BoosterEngineIndices = new HashSet<int>();
                foreach (var grp in bottomStage.SeparationGroups)
                {
                    if (grp == null || grp.EngineIndices == null) continue;
                    if (grp.EngineIndices.Count == 1 && grp.GroupDryMassTons < 15d)
                        foreach (var idx in grp.EngineIndices)
                            bottomStage.BoosterEngineIndices.Add(idx);
                }
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

        /// <summary>Excludes parts that should not participate in any calculation: launch clamps, launch pads (MLP, AASA, etc.).</summary>
        private static bool IsExcludedFromCalculation(Part part)
        {
            if (part == null) return true;
            if (IsLaunchClamp(part)) return true;
            var manufacturer = part.partInfo?.manufacturer ?? "";
            var partName = part.partInfo?.name ?? part.name ?? "";
            // Modular Launch Pads (Alphadyne)
            if (manufacturer.IndexOf("Alphadyne", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            if (partName.IndexOf("AM.MLP", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            if (partName.IndexOf("AM_MLP", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            if (partName.IndexOf("_MLP", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            // Andromeda AeroSpace Agency launch pad (aasa.ag.launch.pad) and similar
            if (partName.IndexOf("launch.pad", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            return false;
        }
    }
}
