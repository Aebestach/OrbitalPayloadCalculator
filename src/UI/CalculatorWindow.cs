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
        private string _altitudeInput = "80000";
        private string _inclinationInput = "0";
        private string _eccentricityInput = "0";
        private string _fontSizeInput;
        private string _manualGravityLossInput = "0";
        private string _manualAtmoLossInput = "0";
        private string _manualAttitudeLossInput = "0";

        private string _payloadStageInput = "-1";
        private int _payloadCutoffStage = -1;

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
            GUILayout.Space(4);
            DrawBodyRow();
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

            if (GUILayout.Button(currentBodyName, _styleManager.ButtonStyle, GUILayout.Width(220)))
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
                var label = i == _bodyIndex ? $">> {bodyName} <<" : bodyName;

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
            GUILayout.Label(Loc("#LOC_OPC_InputHeader"), _styleManager.HeaderStyle);

            DrawVesselSourcePanel();
            DrawPayloadStagePanel();
            DrawTargetOrbitPanel();
            DrawLossPanel();

            GUILayout.Space(6);
            if (GUILayout.Button(Loc("#LOC_OPC_Calculate"), _styleManager.ButtonStyle, GUILayout.Height(32)))
                Compute();

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
            GUILayout.Label(Loc("#LOC_OPC_CurrentVessel"), _styleManager.LabelStyle, GUILayout.Width(120));

            if (GUILayout.Button(vesselName, _styleManager.ButtonStyle, GUILayout.Width(240)))
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
                var name = candidates[i].vesselName;
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

        private void DrawPayloadStagePanel()
        {
            GUILayout.Space(6);
            GUILayout.Label(Loc("#LOC_OPC_PayloadStageHeader"), _styleManager.HeaderStyle);
            GUILayout.Space(2);

            GUILayout.BeginHorizontal();
            GUILayout.Label(Loc("#LOC_OPC_PayloadCutoffStage"), _styleManager.LabelStyle, GUILayout.Width(260));
            _payloadStageInput = GUILayout.TextField(_payloadStageInput, _styleManager.FieldStyle, GUILayout.Width(80));
            GUILayout.EndHorizontal();
            GUILayout.Space(2);

            GUILayout.Label(Loc("#LOC_OPC_PayloadStageHint"), _styleManager.SmallLabelStyle ?? _styleManager.LabelStyle);
        }

        private void DrawTargetOrbitPanel()
        {
            GUILayout.Space(6);
            GUILayout.Label(Loc("#LOC_OPC_TargetOrbit"), _styleManager.HeaderStyle);
            GUILayout.Space(2);

            DrawLabeledField(Loc("#LOC_OPC_TargetAltitude"), ref _altitudeInput);
            GUILayout.Space(4);
            DrawLabeledField(Loc("#LOC_OPC_TargetInclination"), ref _inclinationInput);
            GUILayout.Space(4);
            DrawLatitudeRow();
            GUILayout.Space(4);

            _targets.UseEccentricity = GUILayout.Toggle(_targets.UseEccentricity, Loc("#LOC_OPC_UseEccentricity"), _styleManager.ToggleStyle);
            if (_targets.UseEccentricity)
            {
                GUILayout.Space(4);
                DrawLabeledField(Loc("#LOC_OPC_TargetEccentricity"), ref _eccentricityInput);
            }
        }

        private void DrawLossPanel()
        {
            GUILayout.Space(6);
            _lossConfig.AutoEstimate = GUILayout.Toggle(_lossConfig.AutoEstimate, Loc("#LOC_OPC_AutoLoss"), _styleManager.ToggleStyle);
            GUILayout.Space(4);

            DrawOverrideRow(Loc("#LOC_OPC_GravityLoss"), ref _lossConfig.OverrideGravityLoss, ref _manualGravityLossInput);
            GUILayout.Space(4);
            DrawOverrideRow(Loc("#LOC_OPC_AtmosphereLoss"), ref _lossConfig.OverrideAtmosphericLoss, ref _manualAtmoLossInput);
            GUILayout.Space(4);
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

            GUILayout.Label($"{Loc("#LOC_OPC_LaunchBody")}: {_lastBodyName}", _styleManager.LabelStyle);
            GUILayout.Label($"{Loc("#LOC_OPC_VesselName")}: {_lastStats.VesselName}", _styleManager.LabelStyle);
            GUILayout.Label($"{Loc("#LOC_OPC_WetMass")}: {FormatNum(_lastStats.WetMassTons)} t", _styleManager.LabelStyle);
            GUILayout.Label($"{Loc("#LOC_OPC_DryMass")}: {FormatNum(_lastStats.DryMassTons)} t", _styleManager.LabelStyle);
            GUILayout.Label($"{Loc("#LOC_OPC_RequiredDv")}: {FormatNum(_lastResult.RequiredDv)} m/s", _styleManager.LabelStyle);
            GUILayout.Label($"{Loc("#LOC_OPC_AvailableDv")} (Profile): {FormatNum(_lastResult.AvailableDv)} m/s", _styleManager.LabelStyle);
            GUILayout.Label($"{Loc("#LOC_OPC_AvailableDv")} (Sea): {FormatNum(_lastResult.AvailableDvSeaLevel)} m/s", _styleManager.LabelStyle);
            GUILayout.Label($"{Loc("#LOC_OPC_AvailableDv")} (Vac): {FormatNum(_lastResult.AvailableDvVacuum)} m/s", _styleManager.LabelStyle);
            GUILayout.Label($"{Loc("#LOC_OPC_OrbitalSpeed")}: {FormatNum(_lastResult.OrbitalSpeed)} m/s", _styleManager.LabelStyle);
            var rotSign = _lastResult.RotationDv >= 0.0d ? "+" : "";
            GUILayout.Label($"{Loc("#LOC_OPC_RotationDv")}: {rotSign}{FormatNum(_lastResult.RotationDv)} m/s", _styleManager.LabelStyle);

            GUILayout.Space(4);
            if (_payloadCutoffStage >= 0)
            {
                GUILayout.Label($"{Loc("#LOC_OPC_PayloadStageLabel")}: 0~{_payloadCutoffStage}", _styleManager.LabelStyle);
            }
            GUILayout.Label($"{Loc("#LOC_OPC_EstimatedPayload")}: {FormatNum(_lastResult.EstimatedPayloadTons)} t", _styleManager.HeaderStyle);

            GUILayout.Space(8);
            GUILayout.Label(Loc("#LOC_OPC_LossBreakdown"), _styleManager.HeaderStyle);
            GUILayout.Label($"  {Loc("#LOC_OPC_GravityLoss")}: {FormatNum(_lastResult.Losses.GravityLossDv)} m/s", _styleManager.LabelStyle);
            GUILayout.Label($"  {Loc("#LOC_OPC_AtmosphereLoss")}: {FormatNum(_lastResult.Losses.AtmosphericLossDv)} m/s", _styleManager.LabelStyle);
            GUILayout.Label($"  {Loc("#LOC_OPC_AttitudeLoss")}: {FormatNum(_lastResult.Losses.AttitudeLossDv)} m/s", _styleManager.LabelStyle);

            if (_lastResult.ActiveStages != null && _lastResult.ActiveStages.Count > 0)
            {
                GUILayout.Space(8);
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
        }

        private void DrawStagePopup(int id)
        {
            if (_lastResult?.ActiveStages == null || _lastResult.ActiveStages.Count == 0)
            {
                _showStagePopup = false;
                return;
            }

            var minW = _settings.FontSize * 25f;
            GUILayout.BeginVertical(GUILayout.MinWidth(minW));
            GUILayout.Space(6);

            GUILayout.BeginVertical(_styleManager.PanelStyle);
            foreach (var stage in _lastResult.ActiveStages)
            {
                var solidTag = stage.HasSolidFuel ? " [SRB]" : "";
                var uiStage = Math.Max(0, _lastStats.TotalStages - stage.StageNumber);
                GUILayout.Label(
                    $"  S{uiStage}{solidTag}: " +
                    $"\u0394V={FormatNum(stage.DeltaV)} m/s  " +
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

        private void Compute()
        {
            if (!TryParse(_altitudeInput, out var altitude))
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

            _targets.LaunchLatitudeDegrees = latitude;
            _targets.TargetOrbitAltitudeMeters = altitude;
            _targets.TargetInclinationDegrees = inclination;
            if (_targets.UseEccentricity && TryParse(_eccentricityInput, out var eccentricity))
                _targets.TargetEccentricity = eccentricity;

            if (TryParse(_manualGravityLossInput, out var gravityLoss))
                _lossConfig.ManualGravityLossDv = gravityLoss;
            if (TryParse(_manualAtmoLossInput, out var atmoLoss))
                _lossConfig.ManualAtmosphericLossDv = atmoLoss;
            if (TryParse(_manualAttitudeLossInput, out var attitudeLoss))
                _lossConfig.ManualAttitudeLossDv = attitudeLoss;

            if (int.TryParse(_payloadStageInput, NumberStyles.Integer, CultureInfo.InvariantCulture, out var pStage))
                _payloadCutoffStage = pStage;
            else
                _payloadCutoffStage = -1;

            _lastBodyName = _targets.LaunchBody != null
                ? _targets.LaunchBody.displayName.LocalizeBodyName()
                : Loc("#LOC_OPC_None");
            _lastStats = _vesselService.ReadCurrentStats();
            _lastResult = PayloadCalculator.Compute(_lastStats, _targets, _lossConfig, _payloadCutoffStage);
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

        private static string FormatNum(double value)
        {
            return value.ToString("N3", CultureInfo.InvariantCulture);
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
