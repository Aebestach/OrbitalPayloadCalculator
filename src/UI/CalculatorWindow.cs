using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using KSP.Localization;
using OrbitalPayloadCalculator.Calculation;
using OrbitalPayloadCalculator.Services;
using OrbitalPayloadCalculator.Settings;
using UnityEngine;

namespace OrbitalPayloadCalculator.UI
{
    internal sealed class CalculatorWindow : IDisposable
    {
        private const int WindowId = 940201;
        private const int BodyPopupId = 940202;
        private const int VesselPopupId = 940203;
        private const int StagePopupId = 940204;

        private readonly PluginSettings _settings;
        private readonly VesselSourceService _vesselService;
        private readonly bool _isEditor;
        private readonly UIStyleManager _styleManager = new UIStyleManager();
        private readonly OrbitTargets _targets = new OrbitTargets();
        private readonly LossModelConfig _lossConfig = new LossModelConfig();

        private Rect _windowRect = new Rect(220, 120, 780, 100);

        private string _latitudeInput = "0";
        private string _apoapsisInput = "80";
        private string _periapsisInput = "80";
        private string _prevApoapsisInput = "80";
        private string _inclinationInput = "0";

        private int _altitudeUnitIndex = 1;
        private static readonly string[] AltitudeUnitLabels = { "m", "km", "Mm" };
        private static readonly double[] AltitudeUnitScales = { 1.0d, 1e3d, 1e6d };
        private string _fontSizeInput;
        private string _manualGravityLossInput = "0";
        private string _manualAtmoLossInput = "0";
        private string _manualAttitudeLossInput = "0";

        private CelestialBody[] _bodies = Array.Empty<CelestialBody>();
        private int _bodyIndex;
        private PayloadCalculationResult _lastResult = new PayloadCalculationResult();
        private VesselStats _lastStats = new VesselStats();
        private string _lastBodyName = string.Empty;

        private bool _showBodyPopup;
        private Rect _bodyPopupRect;
        private Vector2 _bodyPopupScroll;

        private bool _showVesselPopup;
        private Rect _vesselPopupRect;
        private Vector2 _vesselPopupScroll;

        private bool _showStagePopup;
        private Rect _stagePopupRect;
        private bool _stagePopupNeedsCenter;

        private int _lastAppliedFontSize = -1;

        private bool _visible;
        public bool Visible
        {
            get => _visible;
            set
            {
                if (value && !_visible)
                    _targets.LaunchBody = null;
                _visible = value;
            }
        }

        public CalculatorWindow(PluginSettings settings, VesselSourceService vesselService, bool isEditor)
        {
            _settings = settings;
            _vesselService = vesselService;
            _isEditor = isEditor;
            _fontSizeInput = _settings.FontSize.ToString(CultureInfo.InvariantCulture);
            RefreshBodies();
        }

        public void OnGUI()
        {
            if (!Visible) return;

            _styleManager.RebuildIfNeeded(_settings.FontSize);

            if (_settings.FontSize != _lastAppliedFontSize)
            {
                _lastAppliedFontSize = _settings.FontSize;
                _windowRect = new Rect(_windowRect.x, _windowRect.y, _windowRect.width, 100);
                if (_showStagePopup)
                {
                    _stagePopupNeedsCenter = true;
                    _stagePopupRect = new Rect(Screen.width * 0.5f, Screen.height * 0.5f, 10, 10);
                }
            }

            _windowRect = GUILayout.Window(WindowId, _windowRect, DrawWindow, Loc("#LOC_OPC_Title"), _styleManager.WindowStyle);

            if (_showBodyPopup)
            {
                _bodyPopupRect = GUILayout.Window(BodyPopupId, _bodyPopupRect, DrawBodyPopup,
                    Loc("#LOC_OPC_SelectBody"), _styleManager.WindowStyle);
            }

            if (_showVesselPopup)
            {
                _vesselPopupRect = GUILayout.Window(VesselPopupId, _vesselPopupRect, DrawVesselPopup,
                    Loc("#LOC_OPC_SelectVessel"), _styleManager.WindowStyle);
            }

            if (_showStagePopup)
            {
                _stagePopupRect = GUILayout.Window(StagePopupId, _stagePopupRect, DrawStagePopup,
                    Loc("#LOC_OPC_StageBreakdown"), _styleManager.WindowStyle);

                if (_stagePopupNeedsCenter && _stagePopupRect.width > 20)
                {
                    _stagePopupRect.x = (Screen.width - _stagePopupRect.width) * 0.5f;
                    _stagePopupRect.y = (Screen.height - _stagePopupRect.height) * 0.5f;
                    _stagePopupNeedsCenter = false;
                }
            }
        }

        public void Dispose()
        {
            _styleManager.Dispose();
        }

        private void DrawWindow(int id)
        {
            GUILayout.BeginVertical();
            GUILayout.Space(6);
            DrawFontSizeRow();
            GUILayout.Space(6);

            GUILayout.BeginHorizontal();
            DrawLeftPanel();
            GUILayout.Space(8);
            DrawRightPanel();
            GUILayout.EndHorizontal();

            GUILayout.Space(8);
            if (GUILayout.Button(Loc("#LOC_OPC_Close"), _styleManager.ButtonStyle, GUILayout.Height(30)))
            {
                Visible = false;
                _showBodyPopup = false;
                _showVesselPopup = false;
                _showStagePopup = false;
            }

            GUILayout.EndVertical();
            GUI.DragWindow(new Rect(0, 0, 10000, 30));
        }

        private void DrawFontSizeRow()
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(Loc("#LOC_OPC_FontSize"), _styleManager.LabelStyle, GUILayout.Width(120));
            _fontSizeInput = GUILayout.TextField(_fontSizeInput, _styleManager.FieldStyle, GUILayout.Width(50));
            GUILayout.Label("(13-20)", _styleManager.SmallLabelStyle ?? _styleManager.LabelStyle, GUILayout.Width(60));

            if (GUILayout.Button(Loc("#LOC_OPC_Save"), _styleManager.ButtonStyle, GUILayout.Width(60)))
            {
                if (int.TryParse(_fontSizeInput, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
                {
                    var clamped = Mathf.Clamp(parsed, 13, 20);
                    _fontSizeInput = clamped.ToString(CultureInfo.InvariantCulture);
                    _settings.SetFontSize(clamped);
                }
            }

            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
        }

        private void DrawBodyRow()
        {
            RefreshBodies();

            GUILayout.BeginHorizontal();
            GUILayout.Label(Loc("#LOC_OPC_LaunchBody"), _styleManager.LabelStyle, GUILayout.Width(180));

            var currentBodyName = _bodies.Length > 0
                ? _bodies[_bodyIndex].displayName.LocalizeBodyName()
                : Loc("#LOC_OPC_None");

            if (GUILayout.Button(Truncate(currentBodyName, 20), _styleManager.ButtonStyle, GUILayout.MaxWidth(220)))
            {
                _showBodyPopup = !_showBodyPopup;
                if (_showBodyPopup)
                {
                    _showVesselPopup = false;
                    var pw = 280f;
                    var ph = Mathf.Min(_bodies.Length * 30 + 60, 500f);
                    _bodyPopupRect = new Rect(
                        (Screen.width - pw) * 0.5f,
                        (Screen.height - ph) * 0.5f,
                        pw, ph);
                }
            }

            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            _targets.LaunchBody = _bodies.Length > 0 ? _bodies[_bodyIndex] : null;
        }

        private void DrawBodyPopup(int id)
        {
            if (_bodies.Length > 10)
                _bodyPopupScroll = GUILayout.BeginScrollView(_bodyPopupScroll, GUILayout.MaxHeight(440));

            for (var i = 0; i < _bodies.Length; i++)
            {
                var bodyName = _bodies[i].displayName.LocalizeBodyName();
                var display = Truncate(bodyName, 26);
                var label = i == _bodyIndex ? $">> {display} <<" : display;

                if (GUILayout.Button(label, _styleManager.ButtonStyle))
                {
                    _bodyIndex = i;
                    _targets.LaunchBody = _bodies[_bodyIndex];
                    _showBodyPopup = false;
                }
            }

            if (_bodies.Length > 10)
                GUILayout.EndScrollView();

            GUILayout.Space(4);
            if (GUILayout.Button(Loc("#LOC_OPC_Close"), _styleManager.ButtonStyle))
                _showBodyPopup = false;

            GUI.DragWindow(new Rect(0, 0, 10000, 20));
        }

        private void DrawLeftPanel()
        {
            GUILayout.BeginVertical(GUILayout.Width(380));
            GUILayout.Label(Loc("#LOC_OPC_InputHeader"), _styleManager.CenteredHeaderStyle, GUILayout.ExpandWidth(true));
            GUILayout.Space(4);

            GUILayout.BeginVertical(_styleManager.PanelStyle);

            GUILayout.BeginVertical(_styleManager.SectionStyle);
            DrawBodyRow();
            GUILayout.EndVertical();

            GUILayout.Space(4);
            GUILayout.BeginVertical(_styleManager.SectionStyle);
            DrawVesselSourcePanel();
            GUILayout.EndVertical();

            GUILayout.Space(4);
            GUILayout.BeginVertical(_styleManager.SectionStyle);
            DrawTargetOrbitPanel();
            GUILayout.EndVertical();

            GUILayout.Space(4);
            GUILayout.BeginVertical(_styleManager.SectionStyle);
            DrawLossPanel();
            GUILayout.EndVertical();

            GUILayout.Space(6);
            if (GUILayout.Button(Loc("#LOC_OPC_Calculate"), _styleManager.ButtonStyle, GUILayout.Height(32)))
                Compute();

            GUILayout.Space(4);
            if (GUILayout.Button(Loc("#LOC_OPC_Reset"), _styleManager.ButtonStyle, GUILayout.Height(32)))
                ResetAll();

            GUILayout.EndVertical();
            GUILayout.EndVertical();
        }

        private void DrawRightPanel()
        {
            GUILayout.BeginVertical(GUILayout.Width(380));
            GUILayout.Label(Loc("#LOC_OPC_ResultHeader"), _styleManager.CenteredHeaderStyle, GUILayout.ExpandWidth(true));
            GUILayout.Space(4);
            GUILayout.BeginVertical(_styleManager.PanelStyle);
            DrawResultPanel();
            GUILayout.EndVertical();
            GUILayout.EndVertical();
        }

        private void DrawVesselSourcePanel()
        {
            GUILayout.Label(Loc("#LOC_OPC_VesselSource"), _styleManager.HeaderStyle);
            GUILayout.Space(2);

            if (_isEditor)
            {
                GUILayout.Label(Loc("#LOC_OPC_EditorAutoRead"), _styleManager.LabelStyle);
                return;
            }

            var candidates = _vesselService.GetFlightCandidates();
            if (candidates.Count == 0)
            {
                GUILayout.Label(Loc("#LOC_OPC_NoFlightCandidates"), _styleManager.LabelStyle);
                return;
            }

            var idx = _vesselService.GetSelectedFlightIndex();
            var vesselName = candidates[idx].vesselName;

            GUILayout.BeginHorizontal();
            GUILayout.Label(Loc("#LOC_OPC_CurrentVessel"), _styleManager.LabelStyle, GUILayout.Width(180));

            if (GUILayout.Button(Truncate(vesselName, 22), _styleManager.ButtonStyle, GUILayout.MaxWidth(220)))
            {
                _showVesselPopup = !_showVesselPopup;
                if (_showVesselPopup)
                {
                    _showBodyPopup = false;
                    var pw = 320f;
                    var ph = Mathf.Min(candidates.Count * 30 + 60, 350f);
                    _vesselPopupRect = new Rect(
                        (Screen.width - pw) * 0.5f,
                        (Screen.height - ph) * 0.5f,
                        pw, ph);
                }
            }

            GUILayout.EndHorizontal();
        }

        private void DrawVesselPopup(int id)
        {
            var candidates = _vesselService.GetFlightCandidates();
            var currentIdx = _vesselService.GetSelectedFlightIndex();

            if (candidates.Count > 8)
                _vesselPopupScroll = GUILayout.BeginScrollView(_vesselPopupScroll, GUILayout.MaxHeight(280));

            for (var i = 0; i < candidates.Count; i++)
            {
                var name = Truncate(candidates[i].vesselName, 22);
                var situation = FormatSituation(candidates[i].situation);
                var label = i == currentIdx
                    ? $">> {name} ({situation}) <<"
                    : $"{name} ({situation})";

                if (GUILayout.Button(label, _styleManager.ButtonStyle))
                {
                    _vesselService.SetSelectedFlightIndex(i);
                    _showVesselPopup = false;
                }
            }

            if (candidates.Count > 8)
                GUILayout.EndScrollView();

            GUILayout.Space(4);
            if (GUILayout.Button(Loc("#LOC_OPC_Close"), _styleManager.ButtonStyle))
                _showVesselPopup = false;

            GUI.DragWindow(new Rect(0, 0, 10000, 20));
        }

        private void DrawTargetOrbitPanel()
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(Loc("#LOC_OPC_TargetOrbit"), _styleManager.HeaderStyle);
            GUILayout.FlexibleSpace();
            for (var i = 0; i < AltitudeUnitLabels.Length; i++)
            {
                if (GUILayout.Button(AltitudeUnitLabels[i], _styleManager.ButtonStyle))
                    SwitchAltitudeUnit(i);
            }
            GUILayout.EndHorizontal();
            GUILayout.Space(4);

            var unit = AltitudeUnitLabels[_altitudeUnitIndex];
            DrawLabeledField($"{Loc("#LOC_OPC_ApoapsisAltLabel")} ({unit})", ref _apoapsisInput);
            if (_apoapsisInput != _prevApoapsisInput)
            {
                _periapsisInput = _apoapsisInput;
                _prevApoapsisInput = _apoapsisInput;
            }

            GUILayout.Space(8);
            DrawLabeledField($"{Loc("#LOC_OPC_PeriapsisAltLabel")} ({unit})", ref _periapsisInput);
            GUILayout.Space(8);
            DrawLabeledField(Loc("#LOC_OPC_TargetInclination"), ref _inclinationInput);
            GUILayout.Space(8);
            DrawLatitudeRow();
        }

        private void DrawLossPanel()
        {
            _lossConfig.AutoEstimate = GUILayout.Toggle(_lossConfig.AutoEstimate, Loc("#LOC_OPC_AutoLoss"), _styleManager.ToggleStyle);
            GUILayout.Space(8);

            DrawOverrideRow(Loc("#LOC_OPC_GravityLoss"), ref _lossConfig.OverrideGravityLoss, ref _manualGravityLossInput);
            GUILayout.Space(8);
            DrawOverrideRow(Loc("#LOC_OPC_AtmosphereLoss"), ref _lossConfig.OverrideAtmosphericLoss, ref _manualAtmoLossInput);
            GUILayout.Space(8);
            DrawOverrideRow(Loc("#LOC_OPC_AttitudeLoss"), ref _lossConfig.OverrideAttitudeLoss, ref _manualAttitudeLossInput);
        }

        private void DrawResultPanel()
        {
            if (!_lastResult.Success)
            {
                var msg = string.IsNullOrEmpty(_lastResult.ErrorMessageKey)
                    ? Loc("#LOC_OPC_ClickToCalculate")
                    : Loc(_lastResult.ErrorMessageKey);
                GUILayout.Label(msg, _styleManager.LabelStyle);
                return;
            }

            GUILayout.BeginVertical(_styleManager.SectionStyle);
            GUILayout.Label($"{Loc("#LOC_OPC_LaunchBody")}: {_lastBodyName}", _styleManager.LabelStyle);
            GUILayout.Label($"{Loc("#LOC_OPC_ResultApoapsis")}: {FormatAltitude(_lastResult.ApoapsisAltitudeMeters)}", _styleManager.LabelStyle);
            GUILayout.Label($"{Loc("#LOC_OPC_ResultPeriapsis")}: {FormatAltitude(_lastResult.PeriapsisAltitudeMeters)}", _styleManager.LabelStyle);
            GUILayout.Label($"{Loc("#LOC_OPC_ResultInclination")}: {_lastResult.InclinationDegrees:F1}\u00b0", _styleManager.LabelStyle);
            GUILayout.Label($"{Loc("#LOC_OPC_ResultEccentricity")}: {_lastResult.Eccentricity:F6}", _styleManager.LabelStyle);
            GUILayout.EndVertical();

            GUILayout.Space(4);
            GUILayout.BeginVertical(_styleManager.SectionStyle);
            GUILayout.Label($"{Loc("#LOC_OPC_VesselName")}: {_lastStats.VesselName}", _styleManager.LabelStyle);
            GUILayout.Label($"{Loc("#LOC_OPC_WetMass")}: {FormatNum(_lastStats.WetMassTons)} t", _styleManager.LabelStyle);
            GUILayout.Label($"{Loc("#LOC_OPC_DryMass")}: {FormatNum(_lastStats.DryMassTons)} t", _styleManager.LabelStyle);
            GUILayout.Label($"{Loc("#LOC_OPC_AvailableDv")}: {FormatDv(_lastResult.AvailableDv)} m/s", _styleManager.LabelStyle);
            GUILayout.EndVertical();

            GUILayout.Space(4);
            GUILayout.BeginVertical(_styleManager.SectionStyle);
            GUILayout.Label($"{Loc("#LOC_OPC_OrbitalSpeed")}: {FormatDv(_lastResult.OrbitalSpeed)} m/s", _styleManager.LabelStyle);
            var rotSign = _lastResult.RotationDv >= 0.0d ? "+" : "";
            GUILayout.Label($"{Loc("#LOC_OPC_RotationDv")}: {rotSign}{FormatDv(_lastResult.RotationDv)} m/s", _styleManager.LabelStyle);
            GUILayout.Label($"{Loc("#LOC_OPC_TotalLossDv")}: {FormatDv(_lastResult.Losses.TotalDv)} m/s", _styleManager.LabelStyle);
            GUILayout.Label($"{Loc("#LOC_OPC_RequiredDv")}: {FormatDv(_lastResult.RequiredDv)} m/s", _styleManager.LabelStyle);
            GUILayout.EndVertical();

            GUILayout.Space(4);
            GUILayout.BeginVertical(_styleManager.SectionStyle);
            GUILayout.Label(Loc("#LOC_OPC_LossBreakdown"), _styleManager.HeaderStyle);
            GUILayout.Label($"  {Loc("#LOC_OPC_GravityLoss")}: {FormatDv(_lastResult.Losses.GravityLossDv)} m/s", _styleManager.LabelStyle);
            GUILayout.Label($"  {Loc("#LOC_OPC_AtmosphereLoss")}: {FormatDv(_lastResult.Losses.AtmosphericLossDv)} m/s", _styleManager.LabelStyle);
            GUILayout.Label($"  {Loc("#LOC_OPC_AttitudeLoss")}: {FormatDv(_lastResult.Losses.AttitudeLossDv)} m/s", _styleManager.LabelStyle);
            GUILayout.EndVertical();

            GUILayout.Space(6);
            GUILayout.Label($"{Loc("#LOC_OPC_EstimatedPayload")}: {FormatNum(_lastResult.EstimatedPayloadTons)} t", _styleManager.HeaderStyle);

            GUILayout.Space(6);
            if (_lastResult.ActiveStages != null && _lastResult.ActiveStages.Count > 0)
            {
                if (GUILayout.Button(Loc("#LOC_OPC_ShowStageDetails"), _styleManager.ButtonStyle, GUILayout.Height(28)))
                {
                    _showStagePopup = !_showStagePopup;
                    if (_showStagePopup)
                    {
                        _stagePopupNeedsCenter = true;
                        _stagePopupRect = new Rect(Screen.width * 0.5f, Screen.height * 0.5f, 10, 10);
                    }
                }
            }
            else
            {
                GUILayout.Label("", _styleManager.LabelStyle);
            }
        }

        private void DrawStagePopup(int id)
        {
            if (_lastResult?.ActiveStages == null || _lastResult.ActiveStages.Count == 0)
            {
                _showStagePopup = false;
                return;
            }

            var minW = _settings.FontSize * 30f;
            GUILayout.BeginVertical(GUILayout.MinWidth(minW));
            GUILayout.Space(6);

            GUILayout.BeginVertical(_styleManager.PanelStyle);
            foreach (var stage in _lastResult.ActiveStages)
            {
                var solidTag = stage.HasSolidFuel ? " [SRB]" : "";
                var uiStage = Math.Max(0, _lastStats.TotalStages - stage.StageNumber);
                GUILayout.Label(
                    $"  S{uiStage}{solidTag}: " +
                    $"\u0394V={FormatDv(stage.DeltaV)} m/s  " +
                    $"Isp={FormatNum(stage.EffectiveIspUsed)}s ({FormatNum(stage.SeaLevelIsp)}/{FormatNum(stage.VacuumIsp)})",
                    _styleManager.LabelStyle);
                GUILayout.Label(
                    $"    {FormatNum(stage.MassAtIgnition)}t \u2192 {FormatNum(stage.MassAfterBurn)}t  " +
                    $"Propellant={FormatNum(stage.PropellantMassTons)}t  " +
                    $"TWR={stage.TWRAtIgnition:F2}",
                    _styleManager.LabelStyle);
                GUILayout.Space(4);
            }
            GUILayout.EndVertical();

            GUILayout.Space(4);
            if (GUILayout.Button(Loc("#LOC_OPC_Close"), _styleManager.ButtonStyle))
                _showStagePopup = false;

            GUILayout.EndVertical();
            GUI.DragWindow(new Rect(0, 0, 10000, 20));
        }

        private void ResetAll()
        {
            _latitudeInput = "0";
            _apoapsisInput = "80";
            _periapsisInput = "80";
            _prevApoapsisInput = "80";
            _inclinationInput = "0";
            _altitudeUnitIndex = 1;
            _manualGravityLossInput = "0";
            _manualAtmoLossInput = "0";
            _manualAttitudeLossInput = "0";

            _lossConfig.AutoEstimate = true;
            _lossConfig.OverrideGravityLoss = false;
            _lossConfig.OverrideAtmosphericLoss = false;
            _lossConfig.OverrideAttitudeLoss = false;
            _lossConfig.ManualGravityLossDv = 0.0d;
            _lossConfig.ManualAtmosphericLossDv = 0.0d;
            _lossConfig.ManualAttitudeLossDv = 0.0d;

            _targets.LaunchBody = null;
            _lastResult = new PayloadCalculationResult();
            _lastStats = new VesselStats();
            _lastBodyName = string.Empty;

            _showBodyPopup = false;
            _showVesselPopup = false;
            _showStagePopup = false;

            RefreshBodies();
        }

        private void Compute()
        {
            if (!TryParse(_apoapsisInput, out var apoapsis))
            {
                _lastResult = new PayloadCalculationResult { ErrorMessageKey = "#LOC_OPC_InvalidAltitude" };
                return;
            }

            if (!TryParse(_periapsisInput, out var periapsis))
            {
                _lastResult = new PayloadCalculationResult { ErrorMessageKey = "#LOC_OPC_InvalidAltitude" };
                return;
            }

            if (!TryParse(_inclinationInput, out var inclination))
            {
                _lastResult = new PayloadCalculationResult { ErrorMessageKey = "#LOC_OPC_InvalidInclination" };
                return;
            }

            if (!TryParse(_latitudeInput, out var latitude))
            {
                _lastResult = new PayloadCalculationResult { ErrorMessageKey = "#LOC_OPC_InvalidLatitude" };
                return;
            }

            var unitScale = AltitudeUnitScales[_altitudeUnitIndex];
            _targets.ApoapsisAltitudeMeters = apoapsis * unitScale;
            _targets.PeriapsisAltitudeMeters = periapsis * unitScale;
            _targets.TargetInclinationDegrees = inclination;
            _targets.LaunchLatitudeDegrees = latitude;

            if (TryParse(_manualGravityLossInput, out var gravityLoss))
                _lossConfig.ManualGravityLossDv = gravityLoss;
            if (TryParse(_manualAtmoLossInput, out var atmoLoss))
                _lossConfig.ManualAtmosphericLossDv = atmoLoss;
            if (TryParse(_manualAttitudeLossInput, out var attitudeLoss))
                _lossConfig.ManualAttitudeLossDv = attitudeLoss;

            _lastBodyName = _targets.LaunchBody != null
                ? _targets.LaunchBody.displayName.LocalizeBodyName()
                : Loc("#LOC_OPC_None");
            _lastStats = _vesselService.ReadCurrentStats();
            _lastResult = PayloadCalculator.Compute(_lastStats, _targets, _lossConfig);
        }

        private void DrawOverrideRow(string label, ref bool enabled, ref string input)
        {
            GUILayout.BeginHorizontal();
            enabled = GUILayout.Toggle(enabled, label, _styleManager.ToggleStyle, GUILayout.Width(260));
            input = GUILayout.TextField(input, _styleManager.FieldStyle, GUILayout.Width(100));
            GUILayout.Label("m/s", _styleManager.LabelStyle, GUILayout.Width(40));
            GUILayout.EndHorizontal();
        }

        private void DrawLatitudeRow()
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(Loc("#LOC_OPC_LaunchLatitude"), _styleManager.LabelStyle, GUILayout.Width(260));
            _latitudeInput = GUILayout.TextField(_latitudeInput, _styleManager.FieldStyle, GUILayout.Width(100));

            if (!_isEditor && FlightGlobals.ActiveVessel != null)
            {
                if (GUILayout.Button(Loc("#LOC_OPC_AutoLatitude"), _styleManager.ButtonStyle, GUILayout.Width(50)))
                {
                    _latitudeInput = FlightGlobals.ActiveVessel.latitude
                        .ToString("F2", CultureInfo.InvariantCulture);
                }
            }

            GUILayout.EndHorizontal();
        }

        private void DrawLabeledField(string label, ref string input)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, _styleManager.LabelStyle, GUILayout.Width(260));
            input = GUILayout.TextField(input, _styleManager.FieldStyle, GUILayout.Width(100));
            GUILayout.EndHorizontal();
        }

        private void RefreshBodies()
        {
            _bodies = FlightGlobals.Bodies?.Where(b => b != null).ToArray() ?? Array.Empty<CelestialBody>();
            if (_bodies.Length == 0)
            {
                _bodyIndex = 0;
                return;
            }

            _bodyIndex = Mathf.Clamp(_bodyIndex, 0, _bodies.Length - 1);
            if (_targets.LaunchBody == null)
            {
                CelestialBody defaultBody = null;

                if (!_isEditor && FlightGlobals.ActiveVessel != null)
                    defaultBody = FlightGlobals.ActiveVessel.mainBody;

                if (defaultBody == null)
                {
                    var homeIdx = Array.FindIndex(_bodies, b => b.isHomeWorld);
                    if (homeIdx < 0)
                        homeIdx = Array.FindIndex(_bodies, b => b.bodyName == "Kerbin");
                    if (homeIdx >= 0)
                        defaultBody = _bodies[homeIdx];
                }

                if (defaultBody != null)
                {
                    var idx = Array.IndexOf(_bodies, defaultBody);
                    _bodyIndex = idx >= 0 ? idx : 0;
                }
                else
                {
                    _bodyIndex = 0;
                }

                _targets.LaunchBody = _bodies[_bodyIndex];
            }
        }

        private static string FormatSituation(Vessel.Situations situation)
        {
            switch (situation)
            {
                case Vessel.Situations.LANDED: return "Landed";
                case Vessel.Situations.SPLASHED: return "Splashed";
                case Vessel.Situations.PRELAUNCH: return "PreLaunch";
                default: return situation.ToString();
            }
        }

        private static bool TryParse(string input, out double value)
        {
            return double.TryParse(input, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
        }

        private static string Truncate(string text, int maxChars)
        {
            if (text == null) return "";
            return text.Length <= maxChars ? text : text.Substring(0, maxChars - 1) + "\u2026";
        }

        private static string FormatNum(double value)
        {
            return value.ToString("N3", CultureInfo.InvariantCulture);
        }

        private static string FormatDv(double value)
        {
            return value.ToString("N0", CultureInfo.InvariantCulture);
        }

        private static string FormatAltitude(double meters)
        {
            var abs = Math.Abs(meters);
            if (abs >= 1e9d)
                return (meters / 1e9d).ToString("N3", CultureInfo.InvariantCulture) + " Gm";
            if (abs >= 1e6d)
                return (meters / 1e6d).ToString("N3", CultureInfo.InvariantCulture) + " Mm";
            if (abs >= 1e3d)
                return (meters / 1e3d).ToString("N3", CultureInfo.InvariantCulture) + " km";
            return meters.ToString("N3", CultureInfo.InvariantCulture) + " m";
        }

        private void SwitchAltitudeUnit(int newIndex)
        {
            if (newIndex == _altitudeUnitIndex) return;
            var oldScale = AltitudeUnitScales[_altitudeUnitIndex];
            var newScale = AltitudeUnitScales[newIndex];
            _apoapsisInput = ConvertUnitInput(_apoapsisInput, oldScale, newScale);
            _periapsisInput = ConvertUnitInput(_periapsisInput, oldScale, newScale);
            _prevApoapsisInput = _apoapsisInput;
            _altitudeUnitIndex = newIndex;
        }

        private static string ConvertUnitInput(string input, double oldScale, double newScale)
        {
            if (!TryParse(input, out var value)) return input;
            var meters = value * oldScale;
            return (meters / newScale).ToString("G", CultureInfo.InvariantCulture);
        }

        private static string Loc(string key)
        {
            return Localizer.Format(key);
        }
    }

    internal static class CelestialBodyLocalizationExtensions
    {
        public static string LocalizeBodyName(this string displayName)
        {
            if (string.IsNullOrEmpty(displayName))
                return displayName;

            var caretIdx = displayName.IndexOf('^');
            if (caretIdx >= 0)
                displayName = displayName.Substring(0, caretIdx);

            return displayName;
        }
    }
}
