using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using ClickThroughFix;
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
        private const int AdvancedHelpPopupId = 940205;
        private const int DvDetailsPopupId = 940206;
        private const int EngineRolePopupId = 940207;
        private const int EngineRoleSelectPopupId = 940208;

        private readonly PluginSettings _settings;
        private readonly VesselSourceService _vesselService;
        private readonly bool _isEditor;
        private readonly UIStyleManager _styleManager = new UIStyleManager();
        private readonly OrbitTargets _targets = new OrbitTargets();
        private readonly LossModelConfig _lossConfig = new LossModelConfig();

        private Rect _windowRect;

        private string _latitudeInput = "0";
        private string _apoapsisInput = "100";
        private string _periapsisInput = "100";
        private string _prevApoapsisInput = "100";
        private string _inclinationInput = "0";

        private int _altitudeUnitIndex = 1;
        private static readonly string[] AltitudeUnitLabels = { "m", "km", "Mm" };
        private static readonly double[] AltitudeUnitScales = { 1.0d, 1e3d, 1e6d };
        private string _fontSizeInput;
        private string _hotkeyKeyInput;
        private bool _hotkeyAltInput;
        private bool _hotkeyCtrlInput;
        private bool _hotkeyShiftInput;
        private string _manualGravityLossInput = "";
        private string _manualAtmoLossInput = "";
        private string _manualAttitudeLossInput = "";
        private string _turnStartSpeedInput = "";
        private string _cdaCoefficientInput = "";
        private string _turnStartAltInput = "";
        private bool _showAdvancedLoss = false;

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

        private bool _showAdvancedHelpPopup;
        private Rect _advancedHelpPopupRect;
        private Vector2 _advancedHelpPopupScroll;

        private bool _showDvDetailsPopup;
        private Rect _dvDetailsPopupRect;
        private bool _showEngineRolePopup;
        private Rect _engineRolePopupRect;
        private Vector2 _engineRolePopupScroll;
        private bool _showEngineRoleSelectPopup;
        private Rect _engineRoleSelectPopupRect;
        private int _engineRoleSelectPartId;
        private string _engineRoleSelectPartName = string.Empty;

        /// <summary>Per-vessel ground altitude recorded before takeoff, used for "Takeoff Altitude" when in flight.</summary>
        private readonly Dictionary<Guid, double> _takeoffAltitudeByVessel = new Dictionary<Guid, double>();

        /// <summary>Per-vessel latitude at takeoff; fixed when in flight until Landed/Prelaunch.</summary>
        private readonly Dictionary<Guid, double> _takeoffLatitudeByVessel = new Dictionary<Guid, double>();

        private int _lastAppliedFontSize = -1;
        private bool _needsHeightReset;
        private bool _showSettingsPanel;

        private bool _disposed;
        private bool _visible;
        public bool Visible
        {
            get => _visible;
            set
            {
                if (value == _visible) return;
                if (value && !_visible)
                    _targets.LaunchBody = null;
                if (!value)
                {
                    _showBodyPopup = false;
                    _showVesselPopup = false;
                    _showStagePopup = false;
                    _showAdvancedHelpPopup = false;
                    _showDvDetailsPopup = false;
                    _showEngineRolePopup = false;
                    _showEngineRoleSelectPopup = false;
                }
                _visible = value;
            }
        }

        private const float WindowWidth = 840f;

        public CalculatorWindow(PluginSettings settings, VesselSourceService vesselService, bool isEditor)
        {
            _settings = settings;
            _vesselService = vesselService;
            _isEditor = isEditor;
            float x = Mathf.Clamp(Screen.width * 0.18f, 20f, Screen.width - WindowWidth - 20f);
            float y = Mathf.Clamp(Screen.height * 0.48f, 40f, Screen.height - 120f);
            _windowRect = new Rect(x, y, WindowWidth, 100);
            _fontSizeInput = _settings.FontSize.ToString(CultureInfo.InvariantCulture);
            _hotkeyKeyInput = _settings.HotkeyKey.ToString();
            _hotkeyAltInput = _settings.HotkeyAlt;
            _hotkeyCtrlInput = _settings.HotkeyCtrl;
            _hotkeyShiftInput = _settings.HotkeyShift;
            RefreshBodies();
            ApplyDefaultOrbitInputsForBody(_targets.LaunchBody);
        }

        public void OnGUI()
        {
            if (!Visible || _disposed) return;

            var savedSkin = GUI.skin;
            GUI.skin = HighLogic.Skin ?? GUI.skin;

            _styleManager.RebuildIfNeeded(_settings.FontSize);

            if (_settings.FontSize != _lastAppliedFontSize)
            {
                _lastAppliedFontSize = _settings.FontSize;
                _needsHeightReset = true;
                if (_showStagePopup)
                {
                    _stagePopupNeedsCenter = true;
                    _stagePopupRect = new Rect(Screen.width * 0.5f, Screen.height * 0.5f, 10, 10);
                }
            }

            if (_needsHeightReset)
            {
                _needsHeightReset = false;
                _windowRect = new Rect(_windowRect.x, _windowRect.y, WindowWidth, 100);
            }

            _windowRect = ClickThruBlocker.GUILayoutWindow(WindowId, _windowRect, DrawWindow, Loc("#LOC_OPC_Title"), _styleManager.WindowStyle);

            if (_showBodyPopup)
            {
                _bodyPopupRect = ClickThruBlocker.GUILayoutWindow(BodyPopupId, _bodyPopupRect, DrawBodyPopup,
                    Loc("#LOC_OPC_SelectBody"), _styleManager.WindowStyle);
            }

            if (_showVesselPopup)
            {
                _vesselPopupRect = ClickThruBlocker.GUILayoutWindow(VesselPopupId, _vesselPopupRect, DrawVesselPopup,
                    Loc("#LOC_OPC_SelectVessel"), _styleManager.WindowStyle);
            }

            if (_showStagePopup)
            {
                _stagePopupRect = ClickThruBlocker.GUILayoutWindow(StagePopupId, _stagePopupRect, DrawStagePopup,
                    Loc("#LOC_OPC_StageBreakdown"), _styleManager.WindowStyle);

                if (_stagePopupNeedsCenter && _stagePopupRect.width > 20)
                {
                    _stagePopupRect.x = (Screen.width - _stagePopupRect.width) * 0.5f;
                    _stagePopupRect.y = (Screen.height - _stagePopupRect.height) * 0.5f;
                    _stagePopupNeedsCenter = false;
                }
            }

            if (_showAdvancedHelpPopup)
            {
                _advancedHelpPopupRect = ClickThruBlocker.GUILayoutWindow(AdvancedHelpPopupId, _advancedHelpPopupRect,
                    DrawAdvancedHelpPopup, Loc("#LOC_OPC_AdvancedHelpTitle"), _styleManager.WindowStyle);
            }

            if (_showDvDetailsPopup)
            {
                _dvDetailsPopupRect = ClickThruBlocker.GUILayoutWindow(DvDetailsPopupId, _dvDetailsPopupRect,
                    DrawDvDetailsPopup, Loc("#LOC_OPC_DvDetailsTitle"), _styleManager.WindowStyle);
            }

            if (_showEngineRolePopup)
            {
                _engineRolePopupRect = ClickThruBlocker.GUILayoutWindow(EngineRolePopupId, _engineRolePopupRect,
                    DrawEngineRolePopup, Loc("#LOC_OPC_EngineClassification"), _styleManager.WindowStyle);
            }

            if (_showEngineRoleSelectPopup)
            {
                _engineRoleSelectPopupRect = ClickThruBlocker.GUILayoutWindow(EngineRoleSelectPopupId, _engineRoleSelectPopupRect,
                    DrawEngineRoleSelectPopup, Loc("#LOC_OPC_SelectEngineRole"), _styleManager.WindowStyle);
            }

            GUI.skin = savedSkin;
        }

        public void Dispose()
        {
            _disposed = true;
            _visible = false;
            _showBodyPopup = false;
            _showVesselPopup = false;
            _showStagePopup = false;
            _showAdvancedHelpPopup = false;
            _showDvDetailsPopup = false;
            _showEngineRolePopup = false;
            _lastResult = new PayloadCalculationResult();
            _lastStats = new VesselStats();
            _bodies = Array.Empty<CelestialBody>();
            _styleManager.Dispose();
        }

        private void DrawWindow(int id)
        {
            GUILayout.BeginVertical();
            GUILayout.Space(12);
            DrawGuiSettingsPanel();
            GUILayout.Space(6);

            GUILayout.BeginHorizontal();
            DrawLeftPanel();
            GUILayout.Space(8);
            DrawRightPanel();
            GUILayout.EndHorizontal();

            //GUILayout.Space(4);
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

        private void DrawGuiSettingsPanel()
        {
            var toggleLabel = _showSettingsPanel
                ? $"\u25bc {Loc("#LOC_OPC_GuiSettings")}"
                : $"\u25b6 {Loc("#LOC_OPC_GuiSettings")}";

            if (GUILayout.Button(toggleLabel, _styleManager.ButtonStyle, GUILayout.ExpandWidth(true), GUILayout.Height(28)))
            {
                _showSettingsPanel = !_showSettingsPanel;
                InvalidateWindowHeight();
            }

            if (!_showSettingsPanel) return;

            GUILayout.BeginVertical(_styleManager.PanelStyle);

            var fs = _settings.FontSize;
            var labelWidth = fs * 12f;
            var fieldWidth = fs * 5f;
            var toggleMinWidth = fs * 4f;
            var rowHeight = fs + 10f;

            GUILayout.BeginHorizontal(GUILayout.Height(rowHeight));
            GUILayout.Label($"{Loc("#LOC_OPC_FontSize")} [13-20]", _styleManager.LabelStyle, GUILayout.Width(labelWidth));
            _fontSizeInput = GUILayout.TextField(_fontSizeInput, _styleManager.FieldStyle, GUILayout.Width(fieldWidth));
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            GUILayout.Space(6);
            GUILayout.BeginHorizontal(GUILayout.Height(rowHeight));
            GUILayout.Label(Loc("#LOC_OPC_HotkeyKey"), _styleManager.LabelStyle, GUILayout.Width(labelWidth));
            _hotkeyKeyInput = GUILayout.TextField(_hotkeyKeyInput, _styleManager.FieldStyle, GUILayout.Width(fieldWidth));
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            GUILayout.Space(6);
            GUILayout.BeginHorizontal(GUILayout.Height(rowHeight));
            GUILayout.Label(Loc("#LOC_OPC_HotkeyModifiers"), _styleManager.LabelStyle, GUILayout.Width(labelWidth));
            _hotkeyAltInput = GUILayout.Toggle(_hotkeyAltInput, "Alt", _styleManager.ToggleStyle, GUILayout.MinWidth(toggleMinWidth));
            _hotkeyCtrlInput = GUILayout.Toggle(_hotkeyCtrlInput, "Ctrl", _styleManager.ToggleStyle, GUILayout.MinWidth(toggleMinWidth));
            _hotkeyShiftInput = GUILayout.Toggle(_hotkeyShiftInput, "Shift", _styleManager.ToggleStyle, GUILayout.MinWidth(toggleMinWidth));
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            GUILayout.Space(4);
            GUILayout.Label($"{Loc("#LOC_OPC_CurrentHotkey")} {BuildHotkeyLabel(_hotkeyKeyInput, _hotkeyAltInput, _hotkeyCtrlInput, _hotkeyShiftInput)}", _styleManager.SmallLabelStyle ?? _styleManager.LabelStyle);

            GUILayout.Space(6);
            if (GUILayout.Button(Loc("#LOC_OPC_ApplySettings"), _styleManager.ButtonStyle, GUILayout.Height(28), GUILayout.ExpandWidth(true)))
            {
                ApplyGuiSettings();
            }

            GUILayout.EndVertical();
        }

        private void DrawBodyRow()
        {
            RefreshBodies();

            var fs = _settings.FontSize;
            var rowH = fs + 10f;
            var headerLabelWidth = fs * 12f;

            GUILayout.BeginHorizontal(GUILayout.Height(rowH));
            GUILayout.Label(Loc("#LOC_OPC_LaunchBody"), _styleManager.LabelStyle, GUILayout.Width(headerLabelWidth), GUILayout.Height(rowH));

            var currentBodyName = _bodies.Length > 0
                ? _bodies[_bodyIndex].displayName.LocalizeBodyName()
                : Loc("#LOC_OPC_None");

            if (GUILayout.Button(Truncate(currentBodyName, 20), _styleManager.ButtonStyle, GUILayout.MaxWidth(220), GUILayout.Height(rowH)))
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

                if (GUILayout.Button(display, i == _bodyIndex ? _styleManager.SelectedButtonStyle : _styleManager.ButtonStyle))
                {
                    _bodyIndex = i;
                    _targets.LaunchBody = _bodies[_bodyIndex];
                    ApplyDefaultOrbitInputsForBody(_targets.LaunchBody);
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
            GUILayout.BeginVertical(GUILayout.Width(400));
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
            var cargoBayAsFairing = GUILayout.Toggle(_settings.TreatCargoBayAsFairing, Loc("#LOC_OPC_TreatCargoBayAsFairing"), _styleManager.ToggleStyle ?? GUI.skin.toggle, GUILayout.ExpandWidth(true));
            if (cargoBayAsFairing != _settings.TreatCargoBayAsFairing)
            {
                _settings.SetTreatCargoBayAsFairing(cargoBayAsFairing);
                Compute();
            }
            GUILayout.Label(Loc("#LOC_OPC_TreatCargoBayAsFairingHint"), _styleManager.SmallLabelStyle ?? _styleManager.LabelStyle);
            GUILayout.Space(2);
            GUILayout.Label(Loc("#LOC_OPC_SeparatorEngineHint"), _styleManager.LabelStyle);
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
            GUILayout.BeginVertical(GUILayout.Width(420));
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
                var hasEditorVessel = EditorLogic.fetch != null
                    && EditorLogic.fetch.ship != null
                    && EditorLogic.fetch.ship.parts != null
                    && EditorLogic.fetch.ship.parts.Count > 0;

                if (hasEditorVessel)
                    GUILayout.Label(Loc("#LOC_OPC_EditorAutoRead"), _styleManager.LabelStyle);
                else
                    GUILayout.Label(Loc("#LOC_OPC_EditorNoVessel"), _styleManager.WarningLabelStyle);

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

            var fs = _settings.FontSize;
            var rowH = fs + 10f;
            var headerLabelWidth = fs * 12f;

            GUILayout.BeginHorizontal(GUILayout.Height(rowH));
            GUILayout.Label(Loc("#LOC_OPC_CurrentVessel"), _styleManager.LabelStyle, GUILayout.Width(headerLabelWidth), GUILayout.Height(rowH));

            if (GUILayout.Button(Truncate(vesselName, 20), _styleManager.ButtonStyle, GUILayout.MaxWidth(220), GUILayout.Height(rowH)))
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
                var label = $"{name} ({situation})";

                if (GUILayout.Button(label, i == currentIdx ? _styleManager.SelectedButtonStyle : _styleManager.ButtonStyle))
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
            var fs = _settings.FontSize;
            var btnW = fs * 3.5f;
            var rowH = fs + 10f;
            var headerLabelWidth = fs * 12f;

            GUILayout.BeginHorizontal(GUILayout.Height(rowH));
            GUILayout.Label(Loc("#LOC_OPC_TargetOrbit"), _styleManager.HeaderStyle, GUILayout.Width(headerLabelWidth), GUILayout.Height(rowH));
            for (var i = 0; i < AltitudeUnitLabels.Length; i++)
            {
                if (GUILayout.Button(AltitudeUnitLabels[i], _styleManager.ButtonStyle, GUILayout.Width(btnW), GUILayout.Height(rowH)))
                    SwitchAltitudeUnit(i);
            }
            GUILayout.EndHorizontal();
            GUILayout.Space(4);

            var unit = AltitudeUnitLabels[_altitudeUnitIndex];
            var orbitLabelWidth = fs * 14f;
            DrawLabeledFieldWithUnit(Loc("#LOC_OPC_ApoapsisAltLabel"), ref _apoapsisInput, unit, fs, orbitLabelWidth);
            if (_apoapsisInput != _prevApoapsisInput)
            {
                _periapsisInput = _apoapsisInput;
                _prevApoapsisInput = _apoapsisInput;
            }

            GUILayout.Space(8);
            DrawLabeledFieldWithUnit(Loc("#LOC_OPC_PeriapsisAltLabel"), ref _periapsisInput, unit, fs, orbitLabelWidth);
            GUILayout.Space(8);
            DrawLabeledFieldWithUnit(Loc("#LOC_OPC_TargetInclination"), ref _inclinationInput, "\u00b0", fs, orbitLabelWidth);
            GUILayout.Space(8);
            DrawLatitudeRow(fs, orbitLabelWidth);
            GUILayout.Space(4);
        }

        private void DrawLossPanel()
        {
            var fs = _settings.FontSize;
            var rowHeight = fs + 10f;
            var rowSpacing = Mathf.Max(6f, fs * 0.4f);

            if (GUILayout.Toggle(_lossConfig.EstimateMode == LossEstimateMode.Pessimistic, Loc("#LOC_OPC_PessimisticLoss"), _styleManager.ToggleStyle, GUILayout.Height(rowHeight)))
                _lossConfig.EstimateMode = LossEstimateMode.Pessimistic;
            GUILayout.Space(rowSpacing);
            if (GUILayout.Toggle(_lossConfig.EstimateMode == LossEstimateMode.Normal, Loc("#LOC_OPC_NormalLoss"), _styleManager.ToggleStyle, GUILayout.Height(rowHeight)))
                _lossConfig.EstimateMode = LossEstimateMode.Normal;
            GUILayout.Space(rowSpacing);
            if (GUILayout.Toggle(_lossConfig.EstimateMode == LossEstimateMode.Optimistic, Loc("#LOC_OPC_AggressiveLoss"), _styleManager.ToggleStyle, GUILayout.Height(rowHeight)))
                _lossConfig.EstimateMode = LossEstimateMode.Optimistic;
            GUILayout.Space(rowSpacing);

            DrawAdvancedLossPanel(fs, rowHeight, rowSpacing);
        }

        private void DrawAdvancedLossPanel(float fs, float rowHeight, float rowSpacing)
        {
            var isOpen = _showAdvancedLoss;

            GUILayout.BeginHorizontal();

            var arrow = isOpen ? "\u25bc" : "\u25b6";
            var toggleLabel = $"{arrow} {Loc("#LOC_OPC_AdvancedSettings")}";
            if (GUILayout.Button(toggleLabel, _styleManager.ButtonStyle, GUILayout.ExpandWidth(true), GUILayout.Height(rowHeight + 4f)))
            {
                _showAdvancedLoss = !_showAdvancedLoss;
                InvalidateWindowHeight();
            }

            var helpBtnWidth = (rowHeight + 4f) * 1.2f;
            if (GUILayout.Button("?", _styleManager.ButtonStyle, GUILayout.Width(helpBtnWidth), GUILayout.Height(rowHeight + 4f)))
            {
                _showAdvancedHelpPopup = true;
                var pw = Mathf.Min(480f, Screen.width * 0.9f);
                var ph = Mathf.Min(360f, Screen.height * 0.7f);
                _advancedHelpPopupRect = new Rect((Screen.width - pw) * 0.5f, (Screen.height - ph) * 0.5f, pw, ph);
            }
            GUILayout.EndHorizontal();

            if (!isOpen) return;

            GUILayout.BeginVertical(_styleManager.SectionStyle);

            DrawAdvancedInputRow(Loc("#LOC_OPC_TurnStartSpeed"), ref _turnStartSpeedInput, "m/s", fs, rowHeight);
            GUILayout.Space(rowSpacing);
            DrawAdvancedInputRow(Loc("#LOC_OPC_TurnStartAlt"), ref _turnStartAltInput, "m", fs, rowHeight);
            GUILayout.Space(rowSpacing);
            DrawAdvancedInputRow(Loc("#LOC_OPC_CdACoefficient"), ref _cdaCoefficientInput, "", fs, rowHeight);
            GUILayout.Space(rowSpacing);

            DrawAdvancedInputRow(Loc("#LOC_OPC_GravityLoss"), ref _manualGravityLossInput, "m/s", fs, rowHeight);
            GUILayout.Space(rowSpacing);
            DrawAdvancedInputRow(Loc("#LOC_OPC_AtmosphereLoss"), ref _manualAtmoLossInput, "m/s", fs, rowHeight);
            GUILayout.Space(rowSpacing);
            DrawAdvancedInputRow(Loc("#LOC_OPC_AttitudeLoss"), ref _manualAttitudeLossInput, "m/s", fs, rowHeight);

            GUILayout.EndVertical();
            GUILayout.Space(rowSpacing);
        }

        private void DrawAdvancedInputRow(string label, ref string input, string unit, float fs, float rowHeight)
        {
            var labelWidth = fs * 10f;
            var fieldWidth = fs * 5f;
            var unitWidth = fs * 3f;

            GUILayout.BeginHorizontal(GUILayout.Height(rowHeight));
            GUILayout.Label(label, _styleManager.LabelStyle, GUILayout.Width(labelWidth), GUILayout.Height(rowHeight));
            GUILayout.Space(4);
            input = GUILayout.TextField(input, _styleManager.FieldStyle, GUILayout.Width(fieldWidth), GUILayout.Height(rowHeight));
            if (!string.IsNullOrEmpty(unit))
            {
                GUILayout.Space(4);
                GUILayout.Label(unit, _styleManager.LabelStyle, GUILayout.Width(unitWidth), GUILayout.Height(rowHeight));
            }
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
        }

        private void ResetAdvancedLossSettings()
        {
            _showAdvancedLoss = false;
            _turnStartSpeedInput = "";
            _cdaCoefficientInput = "";
            _turnStartAltInput = "";
            _manualGravityLossInput = "";
            _manualAtmoLossInput = "";
            _manualAttitudeLossInput = "";
            _lossConfig.TurnStartSpeed = -1.0d;
            _lossConfig.CdACoefficient = -1.0d;
            _lossConfig.TurnStartAltitude = -1.0d;
            _lossConfig.OverrideGravityLoss = false;
            _lossConfig.OverrideAtmosphericLoss = false;
            _lossConfig.OverrideAttitudeLoss = false;
            _lossConfig.ManualGravityLossDv = 0.0d;
            _lossConfig.ManualAtmosphericLossDv = 0.0d;
            _lossConfig.ManualAttitudeLossDv = 0.0d;
        }

        private void DrawAdvancedHelpPopup(int id)
        {
            var maxW = Mathf.Min(480f, Screen.width * 0.9f);
            GUILayout.BeginVertical(GUILayout.MinWidth(maxW), GUILayout.MaxWidth(maxW));
            GUILayout.Space(6);

            _advancedHelpPopupScroll = GUILayout.BeginScrollView(_advancedHelpPopupScroll, GUILayout.ExpandHeight(true));

            var smallStyle = _styleManager.SmallLabelStyle ?? _styleManager.LabelStyle;

            GUILayout.Label(Loc("#LOC_OPC_AdvancedHelpPriority"), _styleManager.SmallBoldLabelStyle ?? smallStyle);
            DrawHelpSeparator();
            GUILayout.Label(Loc("#LOC_OPC_AdvancedHelpTurnExponentDerived"), smallStyle);
            DrawHelpSeparator();
            GUILayout.Label(Loc("#LOC_OPC_AdvancedHelpTurnSpeed"), smallStyle);
            DrawHelpSeparator();
            GUILayout.Label(Loc("#LOC_OPC_AdvancedHelpTurnAlt"), smallStyle);
            DrawHelpSeparator();
            GUILayout.Label(Loc("#LOC_OPC_AdvancedHelpCda"), smallStyle);
            DrawHelpSeparator();
            GUILayout.Label(Loc("#LOC_OPC_AdvancedHelpLossOverrides"), smallStyle);
            GUILayout.Label(Loc("#LOC_OPC_AdvancedHelpAttitudeTable"), smallStyle);

            GUILayout.EndScrollView();

            GUILayout.Space(6);
            if (GUILayout.Button(Loc("#LOC_OPC_Close"), _styleManager.ButtonStyle, GUILayout.Height(28), GUILayout.ExpandWidth(true)))
                _showAdvancedHelpPopup = false;

            GUILayout.EndVertical();
            GUI.DragWindow(new Rect(0, 0, 10000, 24));
        }

        private static void DrawHelpSeparator()
        {
            GUILayout.Space(6);
            GUILayout.Box("", GUILayout.Height(1), GUILayout.ExpandWidth(true));
            GUILayout.Space(6);
        }

        private void DrawResultPanel()
        {
            if (!_lastResult.Success)
            {
                var msg = string.IsNullOrEmpty(_lastResult.ErrorMessageKey)
                    ? Loc("#LOC_OPC_ClickToCalculate")
                    : Loc(_lastResult.ErrorMessageKey);
                var isWarning = _lastResult.ErrorMessageKey == "#LOC_OPC_ApoapsisExceedsSOI"
                    || _lastResult.ErrorMessageKey == "#LOC_OPC_InvalidLatitude"
                    || _lastResult.ErrorMessageKey == "#LOC_OPC_LatitudeOutOfRange"
                    || _lastResult.ErrorMessageKey == "#LOC_OPC_InvalidInclination"
                    || _lastResult.ErrorMessageKey == "#LOC_OPC_InclinationOutOfRange";
                var style = isWarning ? _styleManager.WarningLabelStyle : _styleManager.LabelStyle;
                GUILayout.Label(msg, style ?? _styleManager.LabelStyle);
                return;
            }

            if (!string.IsNullOrEmpty(_lastResult.WarningMessageKey))
            {
                GUILayout.BeginVertical(_styleManager.SectionStyle);
                GUILayout.Label(Loc(_lastResult.WarningMessageKey), _styleManager.WarningLabelStyle);
                GUILayout.EndVertical();
                GUILayout.Space(4);
            }

            GUILayout.BeginVertical(_styleManager.SectionStyle);
            GUILayout.Label($"{Loc("#LOC_OPC_VesselName")}: {_lastStats.VesselName}", _styleManager.LabelStyle);
            GUILayout.Label($"{Loc("#LOC_OPC_WetMass")}: {FormatNum(_lastStats.WetMassTons)} t", _styleManager.LabelStyle);
            GUILayout.Label($"{Loc("#LOC_OPC_DryMass")}: {FormatNum(_lastStats.DryMassTons)} t", _styleManager.LabelStyle);
            GUILayout.EndVertical();

            GUILayout.Space(4);
            GUILayout.BeginVertical(_styleManager.SectionStyle);
            GUILayout.Label($"{Loc("#LOC_OPC_LaunchBody")}: {_lastBodyName}", _styleManager.LabelStyle);
            GUILayout.Label($"{Loc("#LOC_OPC_ResultApoapsis")}: {FormatAltitude(_lastResult.ApoapsisAltitudeMeters)}", _styleManager.LabelStyle);
            GUILayout.Label($"{Loc("#LOC_OPC_ResultPeriapsis")}: {FormatAltitude(_lastResult.PeriapsisAltitudeMeters)}", _styleManager.LabelStyle);
            GUILayout.Label($"{Loc("#LOC_OPC_ResultInclination")}: {_lastResult.InclinationDegrees:F1}\u00b0", _styleManager.LabelStyle);
            GUILayout.Label($"{Loc("#LOC_OPC_ResultEccentricity")}: {_lastResult.Eccentricity:F6}", _styleManager.LabelStyle);
            GUILayout.EndVertical();

            if (_lastResult.Losses.UsedTurnStartSpeed >= 0d)
            {
                GUILayout.Space(4);
                GUILayout.BeginVertical(_styleManager.SectionStyle);
                GUILayout.Label(Loc("#LOC_OPC_ParamsUsedHeader"), _styleManager.HeaderStyle);
                if (_lastResult.Losses.UsedTurnExponentBottom >= 0d)
                    GUILayout.Label($"  {Loc("#LOC_OPC_TurnExponentBottom")}: {FormatNum(_lastResult.Losses.UsedTurnExponentBottom)}", _styleManager.LabelStyle);
                if (_lastResult.Losses.UsedTurnExponentFull >= 0d)
                    GUILayout.Label($"  {Loc("#LOC_OPC_TurnExponentFull")}: {FormatNum(_lastResult.Losses.UsedTurnExponentFull)}", _styleManager.LabelStyle);
                var srcTurn = _lastResult.Losses.UsedTurnStartSpeedManual ? $" ({Loc("#LOC_OPC_ParamSourceManual")})" : "";
                GUILayout.Label($"  {Loc("#LOC_OPC_TurnStartSpeed")}: {_lastResult.Losses.UsedTurnStartSpeed:F0} m/s{srcTurn}", _styleManager.LabelStyle);
                var srcAlt = _lastResult.Losses.UsedTurnStartAltitudeManual ? $" ({Loc("#LOC_OPC_ParamSourceManual")})" : "";
                GUILayout.Label($"  {Loc("#LOC_OPC_TurnStartAlt")}: {_lastResult.Losses.UsedTurnStartAltitude:F0} m{srcAlt}", _styleManager.LabelStyle);
                var srcCda = _lastResult.Losses.UsedCdAManual ? $" ({Loc("#LOC_OPC_ParamSourceManual")})" : "";
                if (_lastResult.Losses.UsedCdACoefficient >= 0d)
                    GUILayout.Label($"  {Loc("#LOC_OPC_CdACoeffLabel")}: {FormatNum(_lastResult.Losses.UsedCdACoefficient)}{srcCda}", _styleManager.LabelStyle);
                GUILayout.Label($"  {Loc("#LOC_OPC_CdAAreaLabel")}: {FormatNum(_lastResult.Losses.UsedCdA)} m²{srcCda}", _styleManager.LabelStyle);
                GUILayout.EndVertical();
            }

            GUILayout.Space(6);
            GUILayout.Label($"{Loc("#LOC_OPC_EstimatedPayload")}: {FormatNum(_lastResult.EstimatedPayloadTons)} t", _styleManager.HeaderStyle);

            GUILayout.Space(6);
            var btnStyle = _styleManager.ButtonStyle;
            var btnHeight = 28f;
            if (GUILayout.Button(Loc("#LOC_OPC_ShowDvDetails"), btnStyle, GUILayout.Height(btnHeight)))
            {
                _showDvDetailsPopup = !_showDvDetailsPopup;
                if (_showDvDetailsPopup)
                {
                    var pw = Mathf.Min(320f, Screen.width * 0.9f);
                    var ph = Mathf.Min(420f, Screen.height * 0.7f);
                    _dvDetailsPopupRect = new Rect((Screen.width - pw) * 0.5f, (Screen.height - ph) * 0.5f, pw, ph);
                }
            }
            GUILayout.Space(4);
            if (_lastResult.ActiveStages != null && _lastResult.ActiveStages.Count > 0)
            {
                if (GUILayout.Button(Loc("#LOC_OPC_ShowStageDetails"), btnStyle, GUILayout.Height(btnHeight)))
                {
                    _showStagePopup = !_showStagePopup;
                    if (_showStagePopup)
                    {
                        _stagePopupNeedsCenter = true;
                        _stagePopupRect = new Rect(Screen.width * 0.5f, Screen.height * 0.5f, 10, 10);
                    }
                }
                GUILayout.Space(4);
                if (GUILayout.Button(Loc("#LOC_OPC_EngineClassification"), btnStyle, GUILayout.Height(btnHeight)))
                {
                    _showEngineRolePopup = !_showEngineRolePopup;
                    _showEngineRoleSelectPopup = false;
                    if (_showEngineRolePopup)
                    {
                        var pw = Mathf.Min(920f, Screen.width * 0.95f);
                        var ph = Mathf.Min(440f, Screen.height * 0.75f);
                        _engineRolePopupRect = new Rect((Screen.width - pw) * 0.5f, (Screen.height - ph) * 0.5f, pw, ph);
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
                var roleTag = stage.HasSolidFuel ? " [SRB]" : "";
                if (stage.Engines != null && stage.Engines.Any(e => e.Role == EngineRole.Electric))
                    roleTag += " [ELEC]";
                var uiStage = Math.Max(0, _lastStats.TotalStages - stage.StageNumber);
                GUILayout.Label(
                    $"  S{uiStage}{roleTag}: " +
                    $"Delta-V={FormatDv(stage.DeltaV)} m/s  " +
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

        private void DrawDvDetailsPopup(int id)
        {
            GUILayout.Space(6);
            GUILayout.BeginVertical(_styleManager.PanelStyle);

            if (!_isEditor && FlightGlobals.ActiveVessel != null)
            {
                var vessel = FlightGlobals.ActiveVessel;
                var isOnGround = vessel.situation == Vessel.Situations.LANDED ||
                    vessel.situation == Vessel.Situations.SPLASHED ||
                    vessel.situation == Vessel.Situations.PRELAUNCH;

                if (isOnGround)
                    _takeoffAltitudeByVessel[vessel.id] = vessel.altitude;

                double displayAltM = isOnGround ? vessel.altitude :
                    (_takeoffAltitudeByVessel.TryGetValue(vessel.id, out var stored) ? stored : -1d);
                string labelKey = isOnGround ? "#LOC_OPC_CurrentLaunchAltitude" : "#LOC_OPC_TakeoffAltitude";
                string altStr = displayAltM >= 0d
                    ? (displayAltM >= 1000d ? $"{displayAltM / 1000.0:F2} km" : $"{displayAltM:F1} m")
                    : "—";

                GUILayout.BeginVertical(_styleManager.SectionStyle);
                GUILayout.Label($"{Loc(labelKey)}: {altStr}", _styleManager.LabelStyle);
                GUILayout.EndVertical();
                GUILayout.Space(4);
            }

            GUILayout.BeginVertical(_styleManager.SectionStyle);
            GUILayout.Label($"{Loc("#LOC_OPC_AvailableDv")}: {FormatDv(_lastResult.AvailableDv)} m/s", _styleManager.LabelStyle);
            GUILayout.EndVertical();

            GUILayout.Space(4);
            GUILayout.BeginVertical(_styleManager.SectionStyle);
            GUILayout.Label($"{Loc("#LOC_OPC_IdealDv")}: {FormatDv(_lastResult.IdealDvFromSurface)} m/s", _styleManager.LabelStyle);
            if (_lastResult.IdealDvUsesModelA)
                GUILayout.Label($"  ({Loc("#LOC_OPC_IdealDvModelAHint")})", _styleManager.LabelStyle);
            else
            {
                GUILayout.Label($"  {Loc("#LOC_OPC_Burn1Dv")}: {FormatDv(_lastResult.Burn1Dv)} m/s", _styleManager.LabelStyle);
                GUILayout.Label($"  {Loc("#LOC_OPC_Burn2Dv")}: {FormatDv(_lastResult.Burn2Dv)} m/s", _styleManager.LabelStyle);
                if (_lastResult.Burn3Dv > 0.5d)
                    GUILayout.Label($"  {Loc("#LOC_OPC_Burn3Dv")}: {FormatDv(_lastResult.Burn3Dv)} m/s", _styleManager.LabelStyle);
            }
            GUILayout.EndVertical();

            GUILayout.Space(4);
            GUILayout.BeginVertical(_styleManager.SectionStyle);
            GUILayout.Label($"{Loc("#LOC_OPC_TotalLossDv")}: {FormatDv(_lastResult.Losses.TotalDv)} m/s", _styleManager.LabelStyle);
            var rotSign = _lastResult.RotationDv >= 0.0d ? "+" : "";
            var rotHint = _lastResult.RotationDv < -0.5d
                ? $" ({Loc("#LOC_OPC_RotationAssist")})"
                : _lastResult.RotationDv > 0.5d
                    ? $" ({Loc("#LOC_OPC_RotationPenalty")})"
                    : "";
            GUILayout.Label($"{Loc("#LOC_OPC_RotationDv")}: {rotSign}{FormatDv(_lastResult.RotationDv)} m/s{rotHint}", _styleManager.LabelStyle);
            if (_lastResult.PlaneChangeDv > 0.5d)
                GUILayout.Label($"{Loc("#LOC_OPC_PlaneChangeDv")}: {FormatDv(_lastResult.PlaneChangeDv)} m/s", _styleManager.LabelStyle);
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
            if (GUILayout.Button(Loc("#LOC_OPC_Close"), _styleManager.ButtonStyle, GUILayout.Height(28), GUILayout.ExpandWidth(true)))
                _showDvDetailsPopup = false;

            GUILayout.EndVertical();
            GUI.DragWindow(new Rect(0, 0, 10000, 24));
        }

        private void DrawEngineRolePopup(int id)
        {
            GUILayout.Space(6);
            if (_lastStats?.Stages == null || _lastStats.Stages.Count == 0)
            {
                GUILayout.Label(Loc("#LOC_OPC_NoVessel"), _styleManager.LabelStyle);
                if (GUILayout.Button(Loc("#LOC_OPC_Close"), _styleManager.ButtonStyle, GUILayout.Height(28), GUILayout.ExpandWidth(true)))
                    _showEngineRolePopup = false;
                GUI.DragWindow(new Rect(0, 0, 10000, 24));
                return;
            }

            GUILayout.Label(Loc("#LOC_OPC_EngineClassificationHint"), _styleManager.SmallLabelStyle ?? _styleManager.LabelStyle);
            GUILayout.Space(4);

            _engineRolePopupScroll = GUILayout.BeginScrollView(_engineRolePopupScroll, GUILayout.ExpandWidth(true), GUILayout.MaxHeight(360));

            GUILayout.BeginVertical();

            foreach (var stage in _lastStats.Stages.OrderByDescending(s => s.StageNumber))
            {
                if (stage?.Engines == null || stage.Engines.Count == 0) continue;
                var uiStage = Math.Max(0, _lastStats.TotalStages - stage.StageNumber);
                GUILayout.Label($"{Loc("#LOC_OPC_StageBreakdown")} S{uiStage}", _styleManager.HeaderStyle);
                for (int i = 0; i < stage.Engines.Count; i++)
                {
                    var engine = stage.Engines[i];
                    if (engine == null) continue;
                    var partName = string.IsNullOrEmpty(engine.PartDisplayName) ? $"#{engine.PartInstanceId}" : TruncateForDisplay(engine.PartDisplayName);
                    const float rowHeight = 28f;
                    GUILayout.BeginHorizontal(_styleManager.SectionStyle, GUILayout.Height(rowHeight));
                    GUILayout.Label(partName, _styleManager.LabelStyleRow, GUILayout.MinWidth(160), GUILayout.ExpandWidth(true), GUILayout.Height(rowHeight));
                    GUILayout.Label($"{Loc("#LOC_OPC_CurrentRole")}: {LocEngineRole(engine.Role)}", _styleManager.LabelStyleRow, GUILayout.Width(200), GUILayout.Height(rowHeight));

                    if (GUILayout.Button(Loc("#LOC_OPC_CycleRole"), _styleManager.ButtonStyle, GUILayout.Width(100), GUILayout.Height(rowHeight)))
                    {
                        _engineRoleSelectPartId = engine.PartInstanceId;
                        _engineRoleSelectPartName = partName;
                        _showEngineRoleSelectPopup = true;
                        var rw = 320f;
                        var rh = 280f;
                        _engineRoleSelectPopupRect = new Rect((Screen.width - rw) * 0.5f, (Screen.height - rh) * 0.5f, rw, rh);
                    }

                    if (GUILayout.Button(Loc("#LOC_OPC_AutoRole"), _styleManager.ButtonStyle, GUILayout.Width(80), GUILayout.Height(rowHeight)))
                    {
                        _vesselService.ClearEngineRoleOverride(_lastStats.VesselPersistentKey, engine.PartInstanceId);
                        Compute();
                        GUIUtility.ExitGUI();
                    }

                    GUILayout.EndHorizontal();
                }
                GUILayout.Space(4);
            }

            GUILayout.EndVertical();
            GUILayout.EndScrollView();

            GUILayout.Space(4);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(Loc("#LOC_OPC_ResetAllRoles"), _styleManager.ButtonStyle, GUILayout.Height(28)))
            {
                _vesselService.ClearAllEngineRoleOverrides(_lastStats.VesselPersistentKey);
                Compute();
                GUIUtility.ExitGUI();
            }
            if (GUILayout.Button(Loc("#LOC_OPC_Close"), _styleManager.ButtonStyle, GUILayout.Height(28)))
            {
                _showEngineRolePopup = false;
                _showEngineRoleSelectPopup = false;
            }
            GUILayout.EndHorizontal();

            GUI.DragWindow(new Rect(0, 0, 10000, 24));
        }

        private void DrawEngineRoleSelectPopup(int id)
        {
            GUILayout.Space(8);
            GUILayout.Label($"{Loc("#LOC_OPC_SelectRoleFor")}: {TruncateForDisplay(_engineRoleSelectPartName, 26, 12)}", _styleManager.LabelStyle);
            GUILayout.Space(8);

            var roles = new[] { EngineRole.Main, EngineRole.Solid, EngineRole.Electric, EngineRole.Retro, EngineRole.Settling, EngineRole.EscapeTower };
            foreach (var role in roles)
            {
                if (GUILayout.Button(LocEngineRole(role), _styleManager.ButtonStyle, GUILayout.Height(28)))
                {
                    _vesselService.SetEngineRoleOverride(_lastStats.VesselPersistentKey, _engineRoleSelectPartId, role);
                    _showEngineRoleSelectPopup = false;
                    Compute();
                    GUIUtility.ExitGUI();
                }
            }

            GUILayout.Space(8);
            if (GUILayout.Button(Loc("#LOC_OPC_Close"), _styleManager.ButtonStyle, GUILayout.Height(28)))
                _showEngineRoleSelectPopup = false;

            GUI.DragWindow(new Rect(0, 0, 10000, 20));
        }

        private static EngineRole NextEngineRole(EngineRole current)
        {
            var values = new[]
            {
                EngineRole.Main,
                EngineRole.Solid,
                EngineRole.Electric,
                EngineRole.Retro,
                EngineRole.Settling,
                EngineRole.EscapeTower
            };
            var idx = Array.IndexOf(values, current);
            if (idx < 0) return EngineRole.Main;
            return values[(idx + 1) % values.Length];
        }

        private static string LocEngineRole(EngineRole role)
        {
            switch (role)
            {
                case EngineRole.Main: return Loc("#LOC_OPC_EngineRoleMain");
                case EngineRole.Solid: return Loc("#LOC_OPC_EngineRoleSolid");
                case EngineRole.Electric: return Loc("#LOC_OPC_EngineRoleElectric");
                case EngineRole.Retro: return Loc("#LOC_OPC_EngineRoleRetro");
                case EngineRole.Settling: return Loc("#LOC_OPC_EngineRoleSettling");
                case EngineRole.EscapeTower: return Loc("#LOC_OPC_EngineRoleEscapeTower");
                default: return role.ToString();
            }
        }

        private void ResetAll()
        {
            InvalidateWindowHeight();
            _latitudeInput = "0";
            _inclinationInput = "0";
            _altitudeUnitIndex = 1;

            _lossConfig.EstimateMode = LossEstimateMode.Normal;
            ResetAdvancedLossSettings();

            _targets.LaunchBody = null;
            _lastResult = new PayloadCalculationResult();
            _lastStats = new VesselStats();
            _lastBodyName = string.Empty;

            _showBodyPopup = false;
            _showVesselPopup = false;
            _showStagePopup = false;
            _showDvDetailsPopup = false;
            _showEngineRolePopup = false;
            _showEngineRoleSelectPopup = false;

            RefreshBodies();
            ApplyDefaultOrbitInputsForBody(_targets.LaunchBody);
        }

        private void InvalidateWindowHeight()
        {
            _needsHeightReset = true;
        }

        private void Compute()
        {
            InvalidateWindowHeight();

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

            if (inclination < 0.0d || inclination > 180.0d)
            {
                _lastResult = new PayloadCalculationResult { ErrorMessageKey = "#LOC_OPC_InclinationOutOfRange" };
                return;
            }

            double latitude;
            if (!_isEditor && FlightGlobals.ActiveVessel != null)
            {
                var vessel = FlightGlobals.ActiveVessel;
                var isOnGround = vessel.situation == Vessel.Situations.LANDED ||
                    vessel.situation == Vessel.Situations.SPLASHED ||
                    vessel.situation == Vessel.Situations.PRELAUNCH;
                latitude = isOnGround ? vessel.latitude :
                    (_takeoffLatitudeByVessel.TryGetValue(vessel.id, out var stored) ? stored : vessel.latitude);
            }
            else if (!TryParse(_latitudeInput, out latitude))
            {
                _lastResult = new PayloadCalculationResult { ErrorMessageKey = "#LOC_OPC_InvalidLatitude" };
                return;
            }

            if (latitude < -90.0d || latitude > 90.0d)
            {
                _lastResult = new PayloadCalculationResult { ErrorMessageKey = "#LOC_OPC_LatitudeOutOfRange" };
                return;
            }

            var unitScale = AltitudeUnitScales[_altitudeUnitIndex];
            _targets.ApoapsisAltitudeMeters = apoapsis * unitScale;
            _targets.PeriapsisAltitudeMeters = periapsis * unitScale;
            _targets.TargetInclinationDegrees = inclination;
            _targets.LaunchLatitudeDegrees = latitude;

            _lossConfig.TurnStartSpeed = TryParse(_turnStartSpeedInput, out var turnSpeed) && turnSpeed > 0d
                ? turnSpeed : -1.0d;
            _lossConfig.CdACoefficient = TryParse(_cdaCoefficientInput, out var cdaCoeff) && cdaCoeff > 0d
                ? cdaCoeff : -1.0d;
            _lossConfig.TurnStartAltitude = TryParse(_turnStartAltInput, out var turnAlt) && turnAlt > 0d
                ? turnAlt : -1.0d;

            if (TryParse(_manualGravityLossInput, out var gravityLoss))
            {
                _lossConfig.OverrideGravityLoss = true;
                _lossConfig.ManualGravityLossDv = gravityLoss;
            }
            else
                _lossConfig.OverrideGravityLoss = false;
            if (TryParse(_manualAtmoLossInput, out var atmoLoss))
            {
                _lossConfig.OverrideAtmosphericLoss = true;
                _lossConfig.ManualAtmosphericLossDv = atmoLoss;
            }
            else
                _lossConfig.OverrideAtmosphericLoss = false;
            if (TryParse(_manualAttitudeLossInput, out var attitudeLoss))
            {
                _lossConfig.OverrideAttitudeLoss = true;
                _lossConfig.ManualAttitudeLossDv = attitudeLoss;
            }
            else
                _lossConfig.OverrideAttitudeLoss = false;

            _lastBodyName = _targets.LaunchBody != null
                ? _targets.LaunchBody.displayName.LocalizeBodyName()
                : Loc("#LOC_OPC_None");
            _vesselService.TreatCargoBayAsFairing = _settings.TreatCargoBayAsFairing;
            _lastStats = _vesselService.ReadCurrentStats();
            _lastResult = PayloadCalculator.Compute(_lastStats, _targets, _lossConfig);
        }

        private void ApplyGuiSettings()
        {
            var fontSize = _settings.FontSize;
            if (int.TryParse(_fontSizeInput, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
                fontSize = Mathf.Clamp(parsed, 13, 20);
            _fontSizeInput = fontSize.ToString(CultureInfo.InvariantCulture);
            _settings.SetFontSize(fontSize);

            if (!TryParseKeyCode(_hotkeyKeyInput, out var key))
            {
                key = _settings.HotkeyKey;
                _hotkeyKeyInput = key.ToString();
            }

            _settings.SetHotkey(key, _hotkeyAltInput, _hotkeyCtrlInput, _hotkeyShiftInput);
        }

        private static bool TryParseKeyCode(string input, out KeyCode key)
        {
            key = KeyCode.None;
            if (string.IsNullOrWhiteSpace(input))
                return false;

            var text = input.Trim();
            if (text.Length == 1)
            {
                var ch = char.ToUpperInvariant(text[0]);
                if (ch >= 'A' && ch <= 'Z')
                {
                    key = (KeyCode)Enum.Parse(typeof(KeyCode), ch.ToString());
                    return true;
                }

                if (ch >= '0' && ch <= '9')
                {
                    key = (KeyCode)Enum.Parse(typeof(KeyCode), $"Alpha{ch}");
                    return true;
                }
            }

            if (Enum.TryParse(text, true, out KeyCode parsed) && parsed != KeyCode.None)
            {
                key = parsed;
                return true;
            }

            return false;
        }

        private static string BuildHotkeyLabel(string inputKey, bool alt, bool ctrl, bool shift)
        {
            if (!TryParseKeyCode(inputKey, out var key))
                key = KeyCode.None;

            var parts = new List<string>();
            if (alt) parts.Add("Alt");
            if (ctrl) parts.Add("Ctrl");
            if (shift) parts.Add("Shift");
            if (key != KeyCode.None) parts.Add(key.ToString());
            if (parts.Count == 0) return "-";
            return string.Join("+", parts);
        }

        private void DrawLatitudeRow(float fs, float labelWidth)
        {
            var fieldWidth = fs * 5f;
            var unitWidth = fs * 3f;

            GUILayout.BeginHorizontal();
            var label = _isEditor
                ? $"{Loc("#LOC_OPC_LaunchLatitude")} {Loc("#LOC_OPC_LaunchLatitudeRange")}"
                : Loc("#LOC_OPC_LaunchLatitude");
            GUILayout.Label(label, _styleManager.LabelStyle, GUILayout.Width(labelWidth));
            GUILayout.Space(4);
            if (!_isEditor && FlightGlobals.ActiveVessel != null)
            {
                var vessel = FlightGlobals.ActiveVessel;
                var isOnGround = vessel.situation == Vessel.Situations.LANDED ||
                    vessel.situation == Vessel.Situations.SPLASHED ||
                    vessel.situation == Vessel.Situations.PRELAUNCH;
                if (isOnGround)
                    _takeoffLatitudeByVessel[vessel.id] = vessel.latitude;
                var lat = isOnGround ? vessel.latitude :
                    (_takeoffLatitudeByVessel.TryGetValue(vessel.id, out var stored) ? stored : vessel.latitude);
                var dir = lat >= 0 ? Loc("#LOC_OPC_NorthLatitude") : Loc("#LOC_OPC_SouthLatitude");
                var dms = FormatLatitudeDms(Math.Abs(lat));
                GUILayout.Label($"{dms} {dir}", _styleManager.LabelStyle, GUILayout.Width(fs * 8f));
            }
            else
            {
                _latitudeInput = GUILayout.TextField(_latitudeInput, _styleManager.FieldStyle, GUILayout.Width(fieldWidth));
            }
            GUILayout.Space(4);
            if (_isEditor || FlightGlobals.ActiveVessel == null)
                GUILayout.Label("\u00b0", _styleManager.LabelStyle, GUILayout.Width(unitWidth));
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
        }

        private void DrawLabeledFieldWithUnit(string label, ref string input, string unit, float fs, float labelWidth)
        {
            var fieldWidth = fs * 5f;
            var unitWidth = fs * 3f;

            GUILayout.BeginHorizontal();
            GUILayout.Label(label, _styleManager.LabelStyle, GUILayout.Width(labelWidth));
            GUILayout.Space(4);
            input = GUILayout.TextField(input, _styleManager.FieldStyle, GUILayout.Width(fieldWidth));
            GUILayout.Space(4);
            GUILayout.Label(unit, _styleManager.LabelStyle, GUILayout.Width(unitWidth));
            GUILayout.FlexibleSpace();
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

        /// <summary>
        /// Truncate for display, using smaller limit for CJK (wide) characters.
        /// CJK chars display ~2x Latin width; Latin limit ~36, CJK limit ~16.
        /// </summary>
        private static string TruncateForDisplay(string text, int maxLatinChars = 36, int maxCjkChars = 16)
        {
            if (text == null || text.Length == 0) return "";
            bool hasCjk = false;
            foreach (var c in text)
            {
                if (c >= '\u4e00' && c <= '\u9fff') { hasCjk = true; break; }
            }
            var limit = hasCjk ? maxCjkChars : maxLatinChars;
            return text.Length <= limit ? text : text.Substring(0, limit - 1) + "\u2026";
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

        private static string FormatLatitudeDms(double decimalDegrees)
        {
            var abs = Math.Abs(decimalDegrees);
            var deg = (int)abs;
            var frac = abs - deg;
            var minutes = (int)(frac * 60.0);
            var seconds = (frac * 60.0 - minutes) * 60.0;
            return $"{deg}\u00b0 {minutes:D2}\u2032 {seconds:F1}\u2033";
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

        private void ApplyDefaultOrbitInputsForBody(CelestialBody body)
        {
            _targets.ApplyDefaultAltitudesForBody(body);
            var unitScale = AltitudeUnitScales[_altitudeUnitIndex];
            var defaultInput = (_targets.ApoapsisAltitudeMeters / unitScale).ToString("G", CultureInfo.InvariantCulture);
            _apoapsisInput = defaultInput;
            _periapsisInput = defaultInput;
            _prevApoapsisInput = defaultInput;
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
