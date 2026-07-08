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
        private const int FontSize = 20;
        private const float LeftPanelWidth = 400f;
        private const float RightPanelWidth = 420f;

        private readonly VesselSourceService _vesselService;
        private readonly bool _isEditor;
        private readonly UIStyleManager _styleManager = new UIStyleManager();
        private readonly OrbitTargets _targets = new OrbitTargets();
        private readonly LossModelConfig _lossConfig = new LossModelConfig();
        private readonly AnalysisWindow _analysisWindow;

        private Rect _windowRect;

        private string _latitudeInput = "0";
        private string _apoapsisInput = "100";
        private string _periapsisInput = "100";
        private string _prevApoapsisInput = "100";
        private string _inclinationInput = "0";

        private int _altitudeUnitIndex = 1;
        private static readonly string[] AltitudeUnitLabels = { "m", "km", "Mm" };
        private static readonly double[] AltitudeUnitScales = { 1.0d, 1e3d, 1e6d };
        private string _manualGravityLossInput = "";
        private string _manualAtmoLossInput = "";
        private string _manualAttitudeLossInput = "";
        private string _turnStartSpeedInput = "";
        private string _cdaCoefficientInput = "";
        private string _turnStartAltInput = "";
        private bool _showAdvancedLoss = false;

        private CelestialBody[] _bodies = Array.Empty<CelestialBody>();
        private bool _bodiesCached;
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
        private bool _treatCargoBayAsFairing;

        /// <summary>Per-vessel ground altitude recorded before takeoff, used for "Takeoff Altitude" when in flight.</summary>
        private readonly Dictionary<Guid, double> _takeoffAltitudeByVessel = new Dictionary<Guid, double>();

        /// <summary>Per-vessel latitude at takeoff; fixed when in flight until Landed/Prelaunch.</summary>
        private readonly Dictionary<Guid, double> _takeoffLatitudeByVessel = new Dictionary<Guid, double>();

        private float _lastUiScaleFactor = -1f;
        private bool _needsHeightReset;

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

        public CalculatorWindow(VesselSourceService vesselService, bool isEditor)
        {
            _vesselService = vesselService;
            _isEditor = isEditor;
            float screenW = UIScale.GuiScreenSize().x;
            float screenH = UIScale.GuiScreenSize().y;
            float x = Mathf.Clamp(screenW * 0.18f, 20f, screenW - WindowWidth - 20f);
            float y = Mathf.Clamp(screenH * 0.48f, 40f, screenH - 120f);
            _windowRect = new Rect(x, y, WindowWidth, 100);
            RefreshBodies();
            ApplyDefaultOrbitInputsForBody(_targets.LaunchBody);
            _analysisWindow = new AnalysisWindow(_styleManager, _vesselService, _lossConfig);
        }

        public void OnGUI()
        {
            if (!Visible || _disposed) return;

            var savedSkin = GUI.skin;
            GUI.skin = HighLogic.Skin ?? GUI.skin;

            float uiScale = UIScale.Factor;
            if (!Mathf.Approximately(uiScale, _lastUiScaleFactor))
            {
                if (_lastUiScaleFactor > 0f)
                    ApplyUiScaleChange(_lastUiScaleFactor, uiScale);
                _lastUiScaleFactor = uiScale;
                _needsHeightReset = true;
            }

            _styleManager.RebuildIfNeeded(FontSize);

            if (_needsHeightReset)
            {
                _needsHeightReset = false;
                _windowRect = new Rect(_windowRect.x, _windowRect.y, WindowWidth, 100);
            }
            _windowRect = UIScale.ClampToGuiScreen(_windowRect);

            UIScale.BeginGUI();
            try
            {
                _windowRect = ClickThruBlocker.GUILayoutWindow(WindowId, _windowRect, DrawWindow, Loc("#LOC_OPC_Title"), _styleManager.WindowStyle);
                _windowRect = UIScale.ClampToGuiScreen(_windowRect);

                if (_showBodyPopup)
                {
                    _bodyPopupRect = ClickThruBlocker.GUILayoutWindow(BodyPopupId, _bodyPopupRect, DrawBodyPopup,
                        Loc("#LOC_OPC_SelectBody"), _styleManager.WindowStyle);
                    _bodyPopupRect = UIScale.ClampToGuiScreen(_bodyPopupRect);
                }

                if (_showVesselPopup)
                {
                    _vesselPopupRect = ClickThruBlocker.GUILayoutWindow(VesselPopupId, _vesselPopupRect, DrawVesselPopup,
                        Loc("#LOC_OPC_SelectVessel"), _styleManager.WindowStyle);
                    _vesselPopupRect = UIScale.ClampToGuiScreen(_vesselPopupRect);
                }

                if (_showStagePopup)
                {
                    _stagePopupRect = ClickThruBlocker.GUILayoutWindow(StagePopupId, _stagePopupRect, DrawStagePopup,
                        Loc("#LOC_OPC_StageBreakdown"), _styleManager.WindowStyle);

                    if (_stagePopupNeedsCenter && _stagePopupRect.width > 20)
                    {
                        var screen = UIScale.GuiScreenSize();
                        _stagePopupRect.x = (screen.x - _stagePopupRect.width) * 0.5f;
                        _stagePopupRect.y = (screen.y - _stagePopupRect.height) * 0.5f;
                        _stagePopupNeedsCenter = false;
                    }
                    _stagePopupRect = UIScale.ClampToGuiScreen(_stagePopupRect);
                }

                if (_showAdvancedHelpPopup)
                {
                    _advancedHelpPopupRect = ClickThruBlocker.GUILayoutWindow(AdvancedHelpPopupId, _advancedHelpPopupRect,
                        DrawAdvancedHelpPopup, Loc("#LOC_OPC_AdvancedHelpTitle"), _styleManager.WindowStyle);
                    _advancedHelpPopupRect = UIScale.ClampToGuiScreen(_advancedHelpPopupRect);
                }

                if (_showDvDetailsPopup)
                {
                    _dvDetailsPopupRect = ClickThruBlocker.GUILayoutWindow(DvDetailsPopupId, _dvDetailsPopupRect,
                        DrawDvDetailsPopup, Loc("#LOC_OPC_DvDetailsTitle"), _styleManager.WindowStyle);
                    _dvDetailsPopupRect = UIScale.ClampToGuiScreen(_dvDetailsPopupRect);
                }

                if (_showEngineRolePopup)
                {
                    _engineRolePopupRect = ClickThruBlocker.GUILayoutWindow(EngineRolePopupId, _engineRolePopupRect,
                        DrawEngineRolePopup, Loc("#LOC_OPC_EngineClassification"), _styleManager.WindowStyle);
                    _engineRolePopupRect = UIScale.ClampToGuiScreen(_engineRolePopupRect);
                }

                if (_showEngineRoleSelectPopup)
                {
                    _engineRoleSelectPopupRect = ClickThruBlocker.GUILayoutWindow(EngineRoleSelectPopupId, _engineRoleSelectPopupRect,
                        DrawEngineRoleSelectPopup, Loc("#LOC_OPC_SelectEngineRole"), _styleManager.WindowStyle);
                    _engineRoleSelectPopupRect = UIScale.ClampToGuiScreen(_engineRoleSelectPopupRect);
                }

                _analysisWindow.OnGUI();
            }
            finally
            {
                UIScale.EndGUI();
            }

            GUI.skin = savedSkin;
        }

        private void ApplyUiScaleChange(float oldScale, float newScale)
        {
            if (oldScale <= 0f || newScale <= 0f)
                return;

            _windowRect = ScaleWindowPosition(_windowRect, oldScale, newScale);
            _bodyPopupRect = ScaleWindowPosition(_bodyPopupRect, oldScale, newScale);
            _vesselPopupRect = ScaleWindowPosition(_vesselPopupRect, oldScale, newScale);
            _stagePopupRect = ScaleWindowPosition(_stagePopupRect, oldScale, newScale);
            _advancedHelpPopupRect = ScaleWindowPosition(_advancedHelpPopupRect, oldScale, newScale);
            _dvDetailsPopupRect = ScaleWindowPosition(_dvDetailsPopupRect, oldScale, newScale);
            _engineRolePopupRect = ScaleWindowPosition(_engineRolePopupRect, oldScale, newScale);
            _engineRoleSelectPopupRect = ScaleWindowPosition(_engineRoleSelectPopupRect, oldScale, newScale);
            _stagePopupNeedsCenter = _showStagePopup;
            _analysisWindow.OnUiScaleChanged(oldScale, newScale);
        }

        private static Rect ScaleWindowPosition(Rect rect, float oldScale, float newScale)
        {
            if (rect.width <= 0f && rect.height <= 0f)
                return rect;

            float ratio = oldScale / newScale;
            rect.x *= ratio;
            rect.y *= ratio;
            return UIScale.ClampToGuiScreen(rect);
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
            _analysisWindow.Dispose();
            _lastResult = new PayloadCalculationResult();
            _lastStats = new VesselStats();
            _bodies = Array.Empty<CelestialBody>();
            _bodiesCached = false;
            _styleManager.Dispose();
        }

        private void DrawWindow(int id)
        {
            GUILayout.BeginVertical();
            GUILayout.Space(12);

            GUILayout.BeginHorizontal();
            DrawLeftPanel();
            GUILayout.Space(8);
            DrawRightPanel();
            GUILayout.EndHorizontal();

            if (GUILayout.Button(Loc("#LOC_OPC_Close"), _styleManager.ButtonStyle, GUILayout.Height(ButtonHeight)))
            {
                Visible = false;
                _showBodyPopup = false;
                _showVesselPopup = false;
                _showStagePopup = false;
            }

            GUILayout.EndVertical();
            GUI.DragWindow(new Rect(0, 0, 10000, 30));
        }

        private void DrawBodyRow()
        {
            RefreshBodies();

            var fs = FontSize;
            var rowH = fs + 14f;
            var headerLabelWidth = fs * 14f;

            GUILayout.BeginHorizontal(GUILayout.Height(rowH));
            GUILayout.Label(Loc("#LOC_OPC_LaunchBody"), _styleManager.LabelStyle, GUILayout.Width(headerLabelWidth), GUILayout.Height(rowH));

            var currentBodyName = _bodies.Length > 0
                ? _bodies[_bodyIndex].bodyName ?? ""
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
                        (UIScale.GuiScreenSize().x - pw) * 0.5f,
                        (UIScale.GuiScreenSize().y - ph) * 0.5f,
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
                var bodyName = _bodies[i].bodyName ?? "";
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
            GUILayout.BeginVertical(GUILayout.Width(LeftPanelWidth));
            GUILayout.Label(Loc("#LOC_OPC_InputHeader"), _styleManager.CenteredHeaderStyle, GUILayout.ExpandWidth(true));
            GUILayout.Space(4);

            GUILayout.BeginVertical(_styleManager.PanelStyle, GUILayout.ExpandWidth(true));

            GUILayout.BeginVertical(_styleManager.SectionStyle, GUILayout.ExpandWidth(true));
            DrawBodyRow();
            GUILayout.EndVertical();

            GUILayout.Space(4);
            GUILayout.BeginVertical(_styleManager.SectionStyle, GUILayout.ExpandWidth(true));
            DrawVesselSourcePanel();
            GUILayout.EndVertical();

            GUILayout.Space(4);
            GUILayout.BeginVertical(_styleManager.SectionStyle, GUILayout.ExpandWidth(true));
            DrawTargetOrbitPanel();
            GUILayout.EndVertical();

            GUILayout.Space(4);
            GUILayout.BeginVertical(_styleManager.SectionStyle, GUILayout.ExpandWidth(true));
            DrawLossPanel();
            GUILayout.EndVertical();

            var rowH = ButtonHeight;
            if (DrawLabeledToggle(_treatCargoBayAsFairing, Loc("#LOC_OPC_TreatCargoBayAsFairing"), rowH, out var cargoBayAsFairing))
            {
                _treatCargoBayAsFairing = cargoBayAsFairing;
                Compute();
            }
            GUILayout.Space(FontSize * 1.1f);
            GUILayout.Space(FontSize * 1.1f);
            GUILayout.Label(Loc("#LOC_OPC_TreatCargoBayAsFairingHint"), _styleManager.HintLabelStyle ?? _styleManager.SmallLabelStyle);
            GUILayout.Space(2);
            GUILayout.Label(Loc("#LOC_OPC_SeparatorEngineHint"), _styleManager.LabelStyleRow);
            
            DrawCalculateAnalysisButtons();

            GUILayout.Space(4);
            if (GUILayout.Button(Loc("#LOC_OPC_Reset"), _styleManager.ButtonStyle, GUILayout.Height(ButtonHeight)))
                ResetAll();

            GUILayout.EndVertical();
            GUILayout.EndVertical();
        }

        private void DrawCalculateAnalysisButtons()
        {
            var calculateLabel = Loc("#LOC_OPC_Calculate");
            var analysisLabel = Loc("#LOC_OPC_AnalysisButton");
            var analysisWidth = ButtonWidth(analysisLabel, 100f);
            var minCalculateWidth = ButtonWidth(calculateLabel, 140f);
            var availableWidth = LeftPanelWidth - 28f;

            if (minCalculateWidth + analysisWidth + 8f > availableWidth)
            {
                if (GUILayout.Button(calculateLabel, _styleManager.ButtonStyle, GUILayout.Height(ButtonHeight), GUILayout.ExpandWidth(true)))
                    Compute();
                GUILayout.Space(4);
                if (GUILayout.Button(analysisLabel, _styleManager.ButtonStyle, GUILayout.Height(ButtonHeight), GUILayout.ExpandWidth(true)))
                    ToggleAnalysisWindow();
                return;
            }

            GUILayout.BeginHorizontal();
            if (GUILayout.Button(calculateLabel, _styleManager.ButtonStyle, GUILayout.ExpandWidth(true), GUILayout.MinWidth(minCalculateWidth), GUILayout.Height(ButtonHeight)))
                Compute();
            if (GUILayout.Button(analysisLabel, _styleManager.ButtonStyle, GUILayout.Height(ButtonHeight), GUILayout.Width(analysisWidth)))
                ToggleAnalysisWindow();
            GUILayout.EndHorizontal();
        }

        private void ToggleAnalysisWindow()
        {
            Compute();
            _analysisWindow.SetContext(_targets.LaunchBody, _targets.TargetInclinationDegrees, _targets.LaunchLatitudeDegrees);
            _analysisWindow.Visible = !_analysisWindow.Visible;
        }

        private void DrawRightPanel()
        {
            GUILayout.BeginVertical(GUILayout.Width(RightPanelWidth));
            GUILayout.Label(Loc("#LOC_OPC_ResultHeader"), _styleManager.CenteredHeaderStyle, GUILayout.ExpandWidth(true));
            GUILayout.Space(4);
            GUILayout.BeginVertical(_styleManager.PanelStyle, GUILayout.ExpandWidth(true));
            DrawResultPanel();
            GUILayout.EndVertical();
            GUILayout.EndVertical();
        }

        private void DrawVesselSourcePanel()
        {
            var rowH = ButtonHeight;
            GUILayout.Label(Loc("#LOC_OPC_VesselSource"), _styleManager.HeaderStyle, GUILayout.Height(rowH));
            GUILayout.Space(6);

            if (_isEditor)
            {
                var hasEditorVessel = EditorLogic.fetch != null
                    && EditorLogic.fetch.ship != null
                    && EditorLogic.fetch.ship.parts != null
                    && EditorLogic.fetch.ship.parts.Count > 0;

                if (hasEditorVessel)
                    GUILayout.Label(Loc("#LOC_OPC_EditorAutoRead"), _styleManager.LabelStyleRow, GUILayout.ExpandWidth(true));
                else
                    GUILayout.Label(Loc("#LOC_OPC_EditorNoVessel"), _styleManager.WarningLabelRowStyle, GUILayout.ExpandWidth(true));

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

            var fs = FontSize;
            var headerLabelWidth = fs * 14f;

            GUILayout.BeginHorizontal(GUILayout.Height(rowH));
            GUILayout.Label(Loc("#LOC_OPC_CurrentVessel"), _styleManager.LabelStyleRow, GUILayout.Width(headerLabelWidth), GUILayout.Height(rowH));

            if (GUILayout.Button(Truncate(vesselName, 20), _styleManager.ButtonStyle, GUILayout.MaxWidth(220), GUILayout.Height(rowH)))
            {
                _showVesselPopup = !_showVesselPopup;
                if (_showVesselPopup)
                {
                    _showBodyPopup = false;
                    var pw = 320f;
                    var ph = Mathf.Min(candidates.Count * 30 + 60, 350f);
                    _vesselPopupRect = new Rect(
                        (UIScale.GuiScreenSize().x - pw) * 0.5f,
                        (UIScale.GuiScreenSize().y - ph) * 0.5f,
                        pw, ph);
                }
            }

            GUILayout.EndHorizontal();
        }

        private void DrawVesselPopup(int id)
        {
            GUILayout.Space(4);
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
            var fs = FontSize;
            var btnW = fs * 3.5f;
            var rowH = fs + 14f;
            var headerLabelWidth = fs * 14f;

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
            var fs = FontSize;
            var rowHeight = fs + 14f;
            var rowSpacing = Mathf.Max(6f, fs * 0.4f);

            if (DrawModeToggle(_lossConfig.EstimateMode == LossEstimateMode.Pessimistic, Loc("#LOC_OPC_PessimisticLoss"), rowHeight))
                _lossConfig.EstimateMode = LossEstimateMode.Pessimistic;
            GUILayout.Space(rowSpacing);
            if (DrawModeToggle(_lossConfig.EstimateMode == LossEstimateMode.Normal, Loc("#LOC_OPC_NormalLoss"), rowHeight))
                _lossConfig.EstimateMode = LossEstimateMode.Normal;
            GUILayout.Space(rowSpacing);
            if (DrawModeToggle(_lossConfig.EstimateMode == LossEstimateMode.Optimistic, Loc("#LOC_OPC_AggressiveLoss"), rowHeight))
                _lossConfig.EstimateMode = LossEstimateMode.Optimistic;
            GUILayout.Space(rowSpacing);

            DrawAdvancedLossPanel(fs, rowHeight, rowSpacing);
        }

        private bool DrawModeToggle(bool selected, string label, float rowHeight)
        {
            DrawLabeledToggle(selected, label, rowHeight, out var newSelected);
            return newSelected;
        }

        private bool DrawLabeledToggle(bool selected, string label, float rowHeight, out bool newSelected)
        {
            const float toggleSize = 22f;
            var toggleStyle = _styleManager.ToggleStyle ?? GUI.skin.toggle;

            GUILayout.BeginHorizontal(GUILayout.Height(rowHeight));
            var slot = GUILayoutUtility.GetRect(toggleSize + 6f, rowHeight, GUILayout.Width(toggleSize + 6f));
            var toggleRect = new Rect(slot.x, slot.y + (slot.height - toggleSize) * 0.5f, toggleSize, toggleSize);
            newSelected = GUI.Toggle(toggleRect, selected, GUIContent.none, toggleStyle);
            GUILayout.Label(label, _styleManager.LabelStyleRow, GUILayout.ExpandWidth(true), GUILayout.Height(rowHeight));
            GUILayout.EndHorizontal();
            return newSelected != selected;
        }

        private void DrawSectionLabel(string text, GUIStyle style = null, bool useHeader = false)
        {
            var rowH = RowHeight;
            var labelStyle = style ?? (useHeader ? _styleManager.HeaderStyle : _styleManager.LabelStyleRow);
            GUILayout.Label(text, labelStyle, GUILayout.ExpandWidth(true), GUILayout.MinHeight(rowH));
        }

        private void DrawAdvancedLossPanel(float fs, float rowHeight, float rowSpacing)
        {
            var isOpen = _showAdvancedLoss;

            GUILayout.BeginHorizontal();

            var arrow = isOpen ? "\u25bc" : "\u25b6";
            var toggleLabel = $"{arrow} {Loc("#LOC_OPC_AdvancedSettings")}";
            if (GUILayout.Button(toggleLabel, _styleManager.ButtonStyle, GUILayout.ExpandWidth(true), GUILayout.Height(rowHeight)))
            {
                _showAdvancedLoss = !_showAdvancedLoss;
                InvalidateWindowHeight();
            }

            var helpBtnWidth = rowHeight * 1.2f;
            if (GUILayout.Button("?", _styleManager.ButtonStyle, GUILayout.Width(helpBtnWidth), GUILayout.Height(rowHeight)))
            {
                _showAdvancedHelpPopup = true;
                var screen = UIScale.GuiScreenSize();
                var pw = Mathf.Min(480f, screen.x * 0.9f);
                var ph = Mathf.Min(360f, screen.y * 0.7f);
                _advancedHelpPopupRect = new Rect((screen.x - pw) * 0.5f, (screen.y - ph) * 0.5f, pw, ph);
            }
            GUILayout.EndHorizontal();

            if (!isOpen) return;

            GUILayout.BeginVertical(_styleManager.SectionStyle, GUILayout.ExpandWidth(true));

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
            var screen = UIScale.GuiScreenSize();
            var maxW = Mathf.Min(520f, screen.x * 0.9f);
            GUILayout.BeginVertical(GUILayout.MinWidth(maxW), GUILayout.MaxWidth(maxW));
            GUILayout.Space(6);

            _advancedHelpPopupScroll = GUILayout.BeginScrollView(_advancedHelpPopupScroll, GUILayout.ExpandHeight(true));

            var helpStyle = _styleManager.HelpLabelStyle ?? _styleManager.HintLabelStyle ?? _styleManager.LabelStyle;
            var helpHeaderStyle = _styleManager.SmallBoldLabelStyle ?? helpStyle;

            GUILayout.Label(Loc("#LOC_OPC_AdvancedHelpPriority"), helpHeaderStyle);
            DrawHelpSeparator();
            GUILayout.Label(Loc("#LOC_OPC_AdvancedHelpTurnExponentDerived"), helpStyle);
            DrawHelpSeparator();
            GUILayout.Label(Loc("#LOC_OPC_AdvancedHelpTurnSpeed"), helpStyle);
            DrawHelpSeparator();
            GUILayout.Label(Loc("#LOC_OPC_AdvancedHelpTurnAlt"), helpStyle);
            DrawHelpSeparator();
            GUILayout.Label(Loc("#LOC_OPC_AdvancedHelpCda"), helpStyle);
            DrawHelpSeparator();
            GUILayout.Label(Loc("#LOC_OPC_AdvancedHelpLossOverrides"), helpStyle);
            GUILayout.Label(Loc("#LOC_OPC_AdvancedHelpAttitudeTable"), helpStyle);

            GUILayout.EndScrollView();

            GUILayout.Space(6);
            if (GUILayout.Button(Loc("#LOC_OPC_Close"), _styleManager.ButtonStyle, GUILayout.Height(ButtonHeight), GUILayout.ExpandWidth(true)))
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
                var style = isWarning ? _styleManager.WarningLabelStyle : _styleManager.LabelStyleRow;
                GUILayout.Label(msg, style ?? _styleManager.LabelStyleRow, GUILayout.ExpandWidth(true), GUILayout.MinHeight(RowHeight));
                return;
            }

            if (!string.IsNullOrEmpty(_lastResult.WarningMessageKey))
            {
                GUILayout.BeginVertical(_styleManager.SectionStyle, GUILayout.ExpandWidth(true));
                GUILayout.Label(Loc(_lastResult.WarningMessageKey), _styleManager.WarningLabelStyle);
                GUILayout.EndVertical();
                GUILayout.Space(4);
            }

            GUILayout.BeginVertical(_styleManager.SectionStyle, GUILayout.ExpandWidth(true));
            DrawResultRow(Loc("#LOC_OPC_VesselName"), _lastStats.VesselName);
            DrawResultRow(Loc("#LOC_OPC_WetMass"), FormatNum(_lastStats.WetMassTons), "t");
            DrawResultRow(Loc("#LOC_OPC_DryMass"), FormatNum(_lastStats.DryMassTons), "t");
            GUILayout.EndVertical();

            GUILayout.Space(4);
            GUILayout.BeginVertical(_styleManager.SectionStyle, GUILayout.ExpandWidth(true));
            DrawResultRow(Loc("#LOC_OPC_LaunchBody"), _lastBodyName);
            DrawResultRow(Loc("#LOC_OPC_ResultApoapsis"), FormatAltitude(_lastResult.ApoapsisAltitudeMeters));
            DrawResultRow(Loc("#LOC_OPC_ResultPeriapsis"), FormatAltitude(_lastResult.PeriapsisAltitudeMeters));
            DrawResultRow(Loc("#LOC_OPC_ResultInclination"), $"{_lastResult.InclinationDegrees:F1}\u00b0");
            DrawResultRow(Loc("#LOC_OPC_ResultEccentricity"), _lastResult.Eccentricity.ToString("F6", CultureInfo.InvariantCulture));
            GUILayout.EndVertical();

            if (_lastResult.Losses.UsedTurnStartSpeed >= 0d)
            {
                GUILayout.Space(4);
                GUILayout.BeginVertical(_styleManager.SectionStyle, GUILayout.ExpandWidth(true));
                GUILayout.Label(Loc("#LOC_OPC_ParamsUsedHeader"), _styleManager.HeaderStyle, GUILayout.Height(FontSize + 10f));
                if (_lastResult.Losses.UsedTurnExponentBottom >= 0d)
                    DrawResultRow(Loc("#LOC_OPC_TurnExponentBottom"), FormatNum(_lastResult.Losses.UsedTurnExponentBottom), indent: true);
                if (_lastResult.Losses.UsedTurnExponentFull >= 0d)
                    DrawResultRow(Loc("#LOC_OPC_TurnExponentFull"), FormatNum(_lastResult.Losses.UsedTurnExponentFull), indent: true);
                var srcTurn = _lastResult.Losses.UsedTurnStartSpeedManual ? $" ({Loc("#LOC_OPC_ParamSourceManual")})" : "";
                DrawResultRow(Loc("#LOC_OPC_TurnStartSpeed"), $"{_lastResult.Losses.UsedTurnStartSpeed:F0} m/s{srcTurn}", indent: true);
                var srcAlt = _lastResult.Losses.UsedTurnStartAltitudeManual ? $" ({Loc("#LOC_OPC_ParamSourceManual")})" : "";
                DrawResultRow(Loc("#LOC_OPC_TurnStartAlt"), $"{_lastResult.Losses.UsedTurnStartAltitude:F0} m{srcAlt}", indent: true);
                var srcCda = _lastResult.Losses.UsedCdAManual ? $" ({Loc("#LOC_OPC_ParamSourceManual")})" : "";
                if (_lastResult.Losses.UsedCdACoefficient >= 0d)
                    DrawResultRow(Loc("#LOC_OPC_CdACoeffLabel"), $"{FormatNum(_lastResult.Losses.UsedCdACoefficient)}{srcCda}", indent: true);
                DrawResultRow(Loc("#LOC_OPC_CdAAreaLabel"), $"{FormatNum(_lastResult.Losses.UsedCdA)} m\u00b2{srcCda}", indent: true);
                GUILayout.EndVertical();
            }

            GUILayout.Space(6);
            DrawResultRow(Loc("#LOC_OPC_EstimatedPayload"), FormatNum(_lastResult.EstimatedPayloadTons), "t", useHeaderStyle: true);

            GUILayout.Space(6);
            var btnStyle = _styleManager.ButtonStyle;
            var btnHeight = ButtonHeight;
            if (GUILayout.Button(Loc("#LOC_OPC_ShowDvDetails"), btnStyle, GUILayout.Height(btnHeight)))
            {
                _showDvDetailsPopup = !_showDvDetailsPopup;
                if (_showDvDetailsPopup)
                {
                    var popupScreen = UIScale.GuiScreenSize();
                    var pw = Mathf.Min(320f, popupScreen.x * 0.9f);
                    var ph = Mathf.Min(420f, popupScreen.y * 0.7f);
                    _dvDetailsPopupRect = new Rect((popupScreen.x - pw) * 0.5f, (popupScreen.y - ph) * 0.5f, pw, ph);
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
                        var centerScreen = UIScale.GuiScreenSize();
                        _stagePopupRect = new Rect(centerScreen.x * 0.5f, centerScreen.y * 0.5f, 10, 10);
                    }
                }
                GUILayout.Space(4);
                if (GUILayout.Button(Loc("#LOC_OPC_EngineClassification"), btnStyle, GUILayout.Height(btnHeight)))
                {
                    _showEngineRolePopup = !_showEngineRolePopup;
                    _showEngineRoleSelectPopup = false;
                    if (_showEngineRolePopup)
                    {
                        var roleScreen = UIScale.GuiScreenSize();
                        var minRoleWidth = 180f + LabelWidth($"{Loc("#LOC_OPC_CurrentRole")}: {LocEngineRole(EngineRole.Main)}", 220f) +
                            ButtonWidth(Loc("#LOC_OPC_CycleRole"), 120f) + ButtonWidth(Loc("#LOC_OPC_AutoRole"), 90f) + 48f;
                        var pw = Mathf.Min(Mathf.Max(920f, minRoleWidth), roleScreen.x * 0.95f);
                        var ph = Mathf.Min(440f, roleScreen.y * 0.75f);
                        _engineRolePopupRect = new Rect((roleScreen.x - pw) * 0.5f, (roleScreen.y - ph) * 0.5f, pw, ph);
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

            var minW = FontSize * 30f;
            GUILayout.BeginVertical(GUILayout.MinWidth(minW));
            GUILayout.Space(6);

            GUILayout.BeginVertical(_styleManager.PanelStyle);
            foreach (var stage in _lastResult.ActiveStages)
            {
                var roleTag = stage.HasSolidFuel ? " [SRB]" : "";
                if (stage.Engines != null && stage.Engines.Any(e => e.Role == EngineRole.Electric))
                    roleTag += " [ELEC]";
                // KSP inverseStage: smaller = top (fires last), larger = bottom (fires first).
                // Display S1=top .. SN=bottom to match physical order in the popup list.
                var uiStage = Math.Max(1, stage.StageNumber);
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
            GUILayout.BeginVertical(_styleManager.PanelStyle, GUILayout.ExpandWidth(true));

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

                GUILayout.BeginVertical(_styleManager.SectionStyle, GUILayout.ExpandWidth(true));
                DrawSectionLabel($"{Loc(labelKey)}: {altStr}");
                GUILayout.EndVertical();
                GUILayout.Space(4);
            }

            GUILayout.BeginVertical(_styleManager.SectionStyle, GUILayout.ExpandWidth(true));
            DrawSectionLabel($"{Loc("#LOC_OPC_AvailableDv")}: {FormatDv(_lastResult.AvailableDv)} m/s");
            GUILayout.EndVertical();

            GUILayout.Space(4);
            GUILayout.BeginVertical(_styleManager.SectionStyle, GUILayout.ExpandWidth(true));
            DrawSectionLabel($"{Loc("#LOC_OPC_IdealDv")}: {FormatDv(_lastResult.IdealDvFromSurface)} m/s");
            if (_lastResult.IdealDvUsesModelA)
                DrawSectionLabel($"  ({Loc("#LOC_OPC_IdealDvModelAHint")})");
            else
            {
                DrawSectionLabel($"  {Loc("#LOC_OPC_Burn1Dv")}: {FormatDv(_lastResult.Burn1Dv)} m/s");
                DrawSectionLabel($"  {Loc("#LOC_OPC_Burn2Dv")}: {FormatDv(_lastResult.Burn2Dv)} m/s");
                if (_lastResult.Burn3Dv > 0.5d)
                    DrawSectionLabel($"  {Loc("#LOC_OPC_Burn3Dv")}: {FormatDv(_lastResult.Burn3Dv)} m/s");
            }
            GUILayout.EndVertical();

            GUILayout.Space(4);
            GUILayout.BeginVertical(_styleManager.SectionStyle, GUILayout.ExpandWidth(true));
            DrawSectionLabel($"{Loc("#LOC_OPC_TotalLossDv")}: {FormatDv(_lastResult.Losses.TotalDv)} m/s");
            var rotSign = _lastResult.RotationDv >= 0.0d ? "+" : "";
            var rotHint = _lastResult.RotationDv < -0.5d
                ? $" ({Loc("#LOC_OPC_RotationAssist")})"
                : _lastResult.RotationDv > 0.5d
                    ? $" ({Loc("#LOC_OPC_RotationPenalty")})"
                    : "";
            DrawSectionLabel($"{Loc("#LOC_OPC_RotationDv")}: {rotSign}{FormatDv(_lastResult.RotationDv)} m/s{rotHint}");
            if (_lastResult.PlaneChangeDv > 0.5d)
                DrawSectionLabel($"{Loc("#LOC_OPC_PlaneChangeDv")}: {FormatDv(_lastResult.PlaneChangeDv)} m/s");
            DrawSectionLabel($"{Loc("#LOC_OPC_RequiredDv")}: {FormatDv(_lastResult.RequiredDv)} m/s");
            GUILayout.EndVertical();

            GUILayout.Space(4);
            GUILayout.BeginVertical(_styleManager.SectionStyle, GUILayout.ExpandWidth(true));
            DrawSectionLabel(Loc("#LOC_OPC_LossBreakdown"), useHeader: true);
            DrawSectionLabel($"  {Loc("#LOC_OPC_GravityLoss")}: {FormatDv(_lastResult.Losses.GravityLossDv)} m/s");
            DrawSectionLabel($"  {Loc("#LOC_OPC_AtmosphereLoss")}: {FormatDv(_lastResult.Losses.AtmosphericLossDv)} m/s");
            DrawSectionLabel($"  {Loc("#LOC_OPC_AttitudeLoss")}: {FormatDv(_lastResult.Losses.AttitudeLossDv)} m/s");
            GUILayout.EndVertical();

            GUILayout.Space(6);
            if (GUILayout.Button(Loc("#LOC_OPC_Close"), _styleManager.ButtonStyle, GUILayout.Height(ButtonHeight), GUILayout.ExpandWidth(true)))
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
                if (GUILayout.Button(Loc("#LOC_OPC_Close"), _styleManager.ButtonStyle, GUILayout.Height(ButtonHeight), GUILayout.ExpandWidth(true)))
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
                // KSP inverseStage: smaller = top, larger = bottom. S1=top, SN=bottom.
                var uiStage = Math.Max(1, stage.StageNumber);
                GUILayout.Label($"{Loc("#LOC_OPC_StageBreakdown")} S{uiStage}", _styleManager.HeaderStyle);
                for (int i = 0; i < stage.Engines.Count; i++)
                {
                    var engine = stage.Engines[i];
                    if (engine == null) continue;
                    var partName = string.IsNullOrEmpty(engine.PartDisplayName) ? $"#{engine.PartInstanceId}" : TruncateForDisplay(engine.PartDisplayName);
                    var rowHeight = ButtonHeight;
                    var currentRoleLabel = $"{Loc("#LOC_OPC_CurrentRole")}: {LocEngineRole(engine.Role)}";
                    var currentRoleWidth = LabelWidth(currentRoleLabel, 220f);
                    var cycleWidth = ButtonWidth(Loc("#LOC_OPC_CycleRole"), 120f);
                    var autoWidth = ButtonWidth(Loc("#LOC_OPC_AutoRole"), 90f);
                    GUILayout.BeginHorizontal(_styleManager.SectionStyle, GUILayout.Height(rowHeight));
                    GUILayout.Label(partName, _styleManager.LabelStyleRow, GUILayout.MinWidth(160), GUILayout.ExpandWidth(true), GUILayout.Height(rowHeight));
                    GUILayout.Label(currentRoleLabel, _styleManager.LabelStyleRow, GUILayout.Width(currentRoleWidth), GUILayout.Height(rowHeight));

                    if (GUILayout.Button(Loc("#LOC_OPC_CycleRole"), _styleManager.ButtonStyle, GUILayout.Width(cycleWidth), GUILayout.Height(rowHeight)))
                    {
                        _engineRoleSelectPartId = engine.PartInstanceId;
                        _engineRoleSelectPartName = partName;
                        _showEngineRoleSelectPopup = true;
                        var rw = 320f;
                        var rh = 280f;
                        var selectScreen = UIScale.GuiScreenSize();
                        _engineRoleSelectPopupRect = new Rect((selectScreen.x - rw) * 0.5f, (selectScreen.y - rh) * 0.5f, rw, rh);
                    }

                    if (GUILayout.Button(Loc("#LOC_OPC_AutoRole"), _styleManager.ButtonStyle, GUILayout.Width(autoWidth), GUILayout.Height(rowHeight)))
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
            if (GUILayout.Button(Loc("#LOC_OPC_ResetAllRoles"), _styleManager.ButtonStyle, GUILayout.Height(ButtonHeight)))
            {
                _vesselService.ClearAllEngineRoleOverrides(_lastStats.VesselPersistentKey);
                Compute();
                GUIUtility.ExitGUI();
            }
            if (GUILayout.Button(Loc("#LOC_OPC_Close"), _styleManager.ButtonStyle, GUILayout.Height(ButtonHeight)))
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
                if (GUILayout.Button(LocEngineRole(role), _styleManager.ButtonStyle, GUILayout.Height(ButtonHeight)))
                {
                    _vesselService.SetEngineRoleOverride(_lastStats.VesselPersistentKey, _engineRoleSelectPartId, role);
                    _showEngineRoleSelectPopup = false;
                    Compute();
                    GUIUtility.ExitGUI();
                }
            }

            GUILayout.Space(8);
            if (GUILayout.Button(Loc("#LOC_OPC_Close"), _styleManager.ButtonStyle, GUILayout.Height(ButtonHeight)))
                _showEngineRoleSelectPopup = false;

            GUI.DragWindow(new Rect(0, 0, 10000, 20));
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
                ? _targets.LaunchBody.bodyName ?? ""
                : Loc("#LOC_OPC_None");
            _vesselService.TreatCargoBayAsFairing = _treatCargoBayAsFairing;
            _lastStats = _vesselService.ReadCurrentStats();
            _lastResult = PayloadCalculator.Compute(_lastStats, _targets, _lossConfig);
            
            // Sync with analysis window if visible
            if (_analysisWindow.Visible)
            {
                _analysisWindow.SetContext(_targets.LaunchBody, _targets.TargetInclinationDegrees, _targets.LaunchLatitudeDegrees);
                // SetContext will trigger RunAnalysis inside AnalysisWindow if visible
            }
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
            var rowH = fs + 14f;

            GUILayout.BeginHorizontal(GUILayout.Height(rowH));
            GUILayout.Label(label, _styleManager.LabelStyleRow, GUILayout.Width(labelWidth), GUILayout.Height(rowH));
            GUILayout.Space(4);
            input = GUILayout.TextField(input, _styleManager.FieldStyle, GUILayout.Width(fieldWidth), GUILayout.Height(rowH));
            GUILayout.Space(4);
            GUILayout.Label(unit, _styleManager.LabelStyleRow, GUILayout.Width(unitWidth), GUILayout.Height(rowH));
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
        }

        private void DrawResultRow(string label, string value, string unit = null, bool indent = false, bool useHeaderStyle = false)
        {
            var fs = FontSize;
            var rowH = RowHeight;
            var labelW = fs * (indent ? 12f : 11f);
            var labelStyle = useHeaderStyle ? _styleManager.HeaderStyle : _styleManager.LabelStyleRow;
            var valueStyle = useHeaderStyle ? _styleManager.HeaderStyle : _styleManager.LabelStyleRow;
            var displayValue = string.IsNullOrEmpty(unit) ? value : $"{value} {unit}";

            GUILayout.BeginHorizontal(GUILayout.Height(rowH));
            if (indent)
                GUILayout.Space(fs * 0.5f);
            GUILayout.Label(label + ":", labelStyle, GUILayout.Width(labelW), GUILayout.Height(rowH));
            GUILayout.Label(displayValue ?? "", valueStyle, GUILayout.ExpandWidth(true), GUILayout.Height(rowH));
            GUILayout.EndHorizontal();
        }

        private static float ButtonHeight => FontSize + 16f;
        private static float RowHeight => FontSize + 16f;

        private float ButtonWidth(string label, float minWidth)
        {
            var style = _styleManager.ButtonStyle ?? GUI.skin.button;
            var width = style.CalcSize(new GUIContent(label ?? string.Empty)).x + 28f;
            return Mathf.Ceil(Mathf.Max(minWidth, width));
        }

        private float LabelWidth(string label, float minWidth)
        {
            var style = _styleManager.LabelStyleRow ?? _styleManager.LabelStyle ?? GUI.skin.label;
            var width = style.CalcSize(new GUIContent(label ?? string.Empty)).x + 10f;
            return Mathf.Ceil(Mathf.Max(minWidth, width));
        }

        private void RefreshBodies()
        {
            if (!_bodiesCached)
            {
                var bodies = FlightGlobals.Bodies?.Where(b => b != null).ToArray() ?? Array.Empty<CelestialBody>();
                if (bodies.Length > 0)
                {
                    _bodies = bodies;
                    _bodiesCached = true;
                }
            }

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

}
