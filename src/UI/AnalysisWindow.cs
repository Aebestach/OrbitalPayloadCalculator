using System;
using System.Collections.Generic;
using System.Linq;
using ClickThroughFix;
using KSP.Localization;
using OrbitalPayloadCalculator.Calculation;
using OrbitalPayloadCalculator.Services;
using OrbitalPayloadCalculator.Settings;
using UnityEngine;

namespace OrbitalPayloadCalculator.UI
{
    internal sealed class AnalysisWindow : IDisposable
    {
        private const int WindowId = 940209;
        private Rect _windowRect;
        private bool _visible;
        private readonly UIStyleManager _styleManager;
        private readonly VesselSourceService _vesselService;
        private readonly LossModelConfig _lossConfig;
        
        // Settings
        private string _minAltInput = "100";
        private string _maxAltInput = "1000";
        private string _stepsInput = "20";
        
        // Data
        private List<Vector2> _dataPoints = new List<Vector2>(); // x=Alt(km), y=Payload(t)
        private bool _hasData = false;
        private float _maxPayload = 0f;
        private float _minPayload = 0f;
        private float _lastUiScaleFactor = -1f;

        // Graph
        private Texture2D _lineTexture;

        // External context
        private CelestialBody _body;
        private double _inclination;
        private double _latitude;

        public bool Visible
        {
            get => _visible;
            set
            {
                if (_visible != value)
                {
                    _visible = value;
                    if (_visible)
                    {
                        // Reset or init position if needed
                        if (_windowRect.width < 10)
                        {
                            var screen = UIScale.GuiScreenSize();
                            _windowRect = new Rect(screen.x * 0.5f - 450, screen.y * 0.5f - 250, 900, 500);
                        }
                    }
                }
            }
        }

        public AnalysisWindow(UIStyleManager styleManager, VesselSourceService vesselService, LossModelConfig lossConfig)
        {
            _styleManager = styleManager;
            _vesselService = vesselService;
            _lossConfig = lossConfig;
            var initScreen = UIScale.GuiScreenSize();
            _windowRect = new Rect(initScreen.x * 0.5f - 450, initScreen.y * 0.5f - 250, 900, 500);
            
            _lineTexture = new Texture2D(1, 1);
            _lineTexture.SetPixel(0, 0, Color.white);
            _lineTexture.Apply();
        }

        public void SetContext(CelestialBody body, double inclination, double latitude)
        {
            bool bodyChanged = _body != body;
            _body = body;
            _inclination = inclination;
            _latitude = latitude;
            
            if (bodyChanged)
            {
                double defaultAltMeters = OrbitTargets.GetDefaultOrbitAltitudeMeters(body);
                _minAltInput = (defaultAltMeters / 1000.0).ToString("G", System.Globalization.CultureInfo.InvariantCulture);
            }
            
            // If window is visible and we have data, auto-refresh
            if (_visible && _hasData)
            {
                RunAnalysis();
            }
        }

        public void OnGUI()
        {
            if (!_visible) return;
            float uiScale = UIScale.Factor;
            if (!Mathf.Approximately(uiScale, _lastUiScaleFactor))
            {
                if (_lastUiScaleFactor > 0f)
                    OnUiScaleChanged(_lastUiScaleFactor, uiScale);
                _lastUiScaleFactor = uiScale;
                _windowRect.height = 500f;
            }
            _windowRect = ClickThruBlocker.GUILayoutWindow(WindowId, _windowRect, DrawWindow, Localizer.Format("#LOC_OPC_AnalysisTitle"), _styleManager.WindowStyle);
            _windowRect = UIScale.ClampToGuiScreen(_windowRect);
        }

        public void OnUiScaleChanged(float oldScale, float newScale)
        {
            if (oldScale <= 0f || newScale <= 0f)
                return;

            float ratio = oldScale / newScale;
            _windowRect.x *= ratio;
            _windowRect.y *= ratio;
            _windowRect = UIScale.ClampToGuiScreen(_windowRect);
            _lastUiScaleFactor = newScale;
        }

        private void DrawWindow(int id)
        {
            GUILayout.BeginVertical();
            GUILayout.Space(6);
            
            // Settings Row
            DrawSettingsRow();

            // Graph Area
            GUILayout.Space(20);
            
            // Reserve space for graph
            Rect graphArea = GUILayoutUtility.GetRect(860, 360);
            if (Event.current.type == EventType.Repaint)
            {
                DrawGraph(graphArea);
            }
            // Handle Tooltip logic separately after Repaint to ensure we have the rect but before EndVertical? 
            // Actually Repaint is fine for drawing, but input needs to be checked.
            // For simplicity, we draw tooltip inside DrawGraph during Repaint, assuming mouse position is valid.

            GUILayout.Space(10);
            if (GUILayout.Button(Localizer.Format("#LOC_OPC_Close"), _styleManager.ButtonStyle, GUILayout.Height(36f), GUILayout.ExpandWidth(true)))
            {
                Visible = false;
            }

            GUILayout.EndVertical();
            GUI.DragWindow();
        }

        private void RunAnalysis()
        {
            if (_body == null) return;
            
            if (!double.TryParse(_minAltInput, out double minAltKm) || !double.TryParse(_maxAltInput, out double maxAltKm))
                return;

            if (minAltKm >= maxAltKm) return;

            if (!int.TryParse(_stepsInput, out int steps))
                steps = 20;
            
            steps = Mathf.Clamp(steps, 2, 100); // Enforce reasonable limits
            double stepSize = (maxAltKm - minAltKm) / (steps - 1);
            
            _dataPoints.Clear();
            _maxPayload = 0f;
            _minPayload = float.MaxValue;

            // Prepare common objects
            var stats = _vesselService.ReadCurrentStats();
            var targets = new OrbitTargets
            {
                LaunchBody = _body,
                TargetInclinationDegrees = _inclination,
                LaunchLatitudeDegrees = _latitude
            };

            for (int i = 0; i < steps; i++)
            {
                double altKm = minAltKm + i * stepSize;
                double altM = altKm * 1000.0;
                
                targets.ApoapsisAltitudeMeters = altM;
                targets.PeriapsisAltitudeMeters = altM; // Circular orbit analysis

                var result = PayloadCalculator.Compute(stats, targets, _lossConfig);
                
                float payload = (float)result.EstimatedPayloadTons;
                _dataPoints.Add(new Vector2((float)altKm, payload));
                
                if (payload > _maxPayload) _maxPayload = payload;
                if (payload < _minPayload) _minPayload = payload;
            }
            
            // Adjust min payload for better visualization
            // If all positive, let min be 0 or slightly below min
            if (_minPayload > 0) _minPayload = 0;
            else if (_minPayload > -10 && _maxPayload > 10) _minPayload = -5; // Cap negative if small
            
            _hasData = true;
        }

        private void DrawSettingsRow()
        {
            var labelW = 130f;
            var fieldW = 60f;
            var rowH = 36f;

            GUILayout.BeginHorizontal(_styleManager.PanelStyle, GUILayout.ExpandWidth(true));
            DrawSettingField(Localizer.Format("#LOC_OPC_MinAlt") + " (km):", ref _minAltInput, labelW, fieldW, rowH);
            GUILayout.Space(16);
            DrawSettingField(Localizer.Format("#LOC_OPC_MaxAlt") + " (km):", ref _maxAltInput, labelW, fieldW, rowH);
            GUILayout.Space(16);
            DrawSettingField(Localizer.Format("#LOC_OPC_Steps") + ":", ref _stepsInput, 100f, 40f, rowH);
            GUILayout.FlexibleSpace();
            var analyzeLabel = Localizer.Format("#LOC_OPC_Analyze");
            if (GUILayout.Button(analyzeLabel, _styleManager.ButtonStyle, GUILayout.Width(ButtonWidth(analyzeLabel, 100f)), GUILayout.Height(rowH)))
                RunAnalysis();
            GUILayout.EndHorizontal();
        }

        private void DrawSettingField(string label, ref string input, float labelWidth, float fieldWidth, float rowHeight)
        {
            GUILayout.BeginHorizontal(GUILayout.Width(labelWidth + fieldWidth + 8f), GUILayout.Height(rowHeight));
            GUILayout.Label(label, _styleManager.LabelStyleRow, GUILayout.Width(labelWidth), GUILayout.Height(rowHeight));
            input = GUILayout.TextField(input, _styleManager.FieldStyle, GUILayout.Width(fieldWidth), GUILayout.Height(rowHeight));
            GUILayout.EndHorizontal();
        }

        private float ButtonWidth(string label, float minWidth)
        {
            var style = _styleManager.ButtonStyle ?? GUI.skin.button;
            var width = style.CalcSize(new GUIContent(label ?? string.Empty)).x + 28f;
            return Mathf.Ceil(Mathf.Max(minWidth, width));
        }

        private void DrawGraph(Rect rect)
        {
            GUI.BeginGroup(rect);
            var local = new Rect(0f, 0f, rect.width, rect.height);

            GUI.Box(local, "", _styleManager.PanelStyle);
            
            if (!_hasData || _dataPoints.Count < 2)
            {
                GUI.Label(new Rect(local.center.x - 50f, local.center.y - 10f, 100f, 20f), "No Data", _styleManager.LabelStyle);
                GUI.EndGroup();
                return;
            }

            const float leftM = 50f;
            const float bottomM = 40f;
            const float topM = 36f;
            const float rightM = 20f;
            
            var graphW = local.width - leftM - rightM;
            var graphH = local.height - topM - bottomM;
            
            var xMin = _dataPoints[0].x;
            var xMax = _dataPoints[_dataPoints.Count - 1].x;
            var yMin = _minPayload;
            var yRange = Mathf.Max(0.001f, _maxPayload - yMin);
            var yMax = _maxPayload + yRange * 0.12f;
            
            if (yMax <= yMin) yMax = yMin + 1f;

            var plotRect = new Rect(leftM, topM, graphW, graphH);
            var plotBottom = topM + graphH;

            DrawLine(new Vector2(leftM, topM), new Vector2(leftM, plotBottom), Color.gray, 2f);
            DrawLine(new Vector2(leftM, plotBottom), new Vector2(local.width - rightM, plotBottom), Color.gray, 2f);

            const int ySteps = 5;
            for (var i = 0; i <= ySteps; i++)
            {
                var t = i / (float)ySteps;
                var val = Mathf.Lerp(yMin, yMax, t);
                var yPos = plotBottom - t * graphH;
                
                GUI.Label(new Rect(0f, yPos - 10f, leftM - 5f, 20f), val.ToString("F1"), _styleManager.SmallLabelStyle);
                DrawLine(new Vector2(leftM, yPos), new Vector2(local.width - rightM, yPos), new Color(1f, 1f, 1f, 0.1f), 1f, plotRect);
            }

            const int xSteps = 5;
            for (var i = 0; i <= xSteps; i++)
            {
                var t = i / (float)xSteps;
                var val = Mathf.Lerp(xMin, xMax, t);
                var xPos = leftM + t * graphW;
                
                GUI.Label(new Rect(xPos - 20f, plotBottom + 5f, 40f, 20f), val.ToString("F0"), _styleManager.SmallLabelStyle);
                DrawLine(new Vector2(xPos, topM), new Vector2(xPos, plotBottom), new Color(1f, 1f, 1f, 0.1f), 1f, plotRect);
            }
            
            var axisLabelStyle = _styleManager.SmallBoldLabelStyle ?? _styleManager.LabelStyleRow;
            var axisLabelHeight = 24f;
            GUI.Label(new Rect(leftM + graphW * 0.5f - 50f, local.height - 28f, 100f, axisLabelHeight), Localizer.Format("#LOC_OPC_AltitudeKm"), axisLabelStyle);
            GUI.Label(new Rect(5f, 8f, leftM - 8f, axisLabelHeight), Localizer.Format("#LOC_OPC_PayloadTons"), axisLabelStyle);

            GUI.BeginGroup(plotRect);
            var plotLocal = new Rect(0f, 0f, plotRect.width, plotRect.height);
            Vector2? prevPos = null;
            for (var i = 0; i < _dataPoints.Count; i++)
            {
                var pt = _dataPoints[i];
                var xNorm = (pt.x - xMin) / (xMax - xMin);
                var yNorm = (pt.y - yMin) / (yMax - yMin);
                
                var screenPos = new Vector2(
                    xNorm * plotLocal.width,
                    plotLocal.height - yNorm * plotLocal.height
                );
                
                if (prevPos.HasValue)
                    DrawLine(prevPos.Value, screenPos, Color.green, 2f);
                
                DrawColoredTexture(new Rect(screenPos.x - 2f, screenPos.y - 2f, 4f, 4f), Color.green);
                prevPos = screenPos;
            }
            GUI.EndGroup();

            var mouse = Event.current.mousePosition - rect.position;
            if (local.Contains(mouse) && _hasData)
            {
                var mouseXRel = mouse.x - leftM;
                var t = Mathf.Clamp01(mouseXRel / graphW);
                
                var index = Mathf.RoundToInt(t * (_dataPoints.Count - 1));
                index = Mathf.Clamp(index, 0, _dataPoints.Count - 1);
                var closest = _dataPoints[index];
                
                var xNorm = (closest.x - xMin) / (xMax - xMin);
                var yNorm = (closest.y - yMin) / (yMax - yMin);
                var ptPos = new Vector2(
                    leftM + xNorm * graphW,
                    plotBottom - yNorm * graphH
                );
                
                DrawColoredTexture(new Rect(ptPos.x - 4f, ptPos.y - 4f, 8f, 8f), Color.green);
                DrawGraphTooltip(local, ptPos, closest, topM, plotBottom, leftM);
            }

            GUI.EndGroup();
        }

        private void DrawGraphTooltip(Rect graphBounds, Vector2 ptPos, Vector2 closest, float plotTop, float plotBottom, float plotLeft)
        {
            var altText = $"{closest.x:F0} km";
            var payloadText = $"{closest.y:F2} t";
            var tipStyle = _styleManager.TooltipStyle ?? _styleManager.SmallBoldLabelStyle ?? _styleManager.LabelStyle;
            const float padX = 14f;
            const float padY = 10f;
            const float lineGap = 3f;
            const float pointGap = 12f;
            const float edgeMargin = 6f;

            var lineH = tipStyle.lineHeight;
            var contentW = Mathf.Max(
                tipStyle.CalcSize(new GUIContent(altText)).x,
                tipStyle.CalcSize(new GUIContent(payloadText)).x);
            var tipW = contentW + padX * 2f;
            var tipH = lineH * 2f + lineGap + padY * 2f;

            var tipRectAbove = new Rect(ptPos.x - tipW * 0.5f, ptPos.y - tipH - pointGap, tipW, tipH);
            var tipRectBelow = new Rect(ptPos.x - tipW * 0.5f, ptPos.y + pointGap, tipW, tipH);

            var upperThreshold = plotTop + tipH + pointGap + 8f;
            var preferBelow = ptPos.y <= upperThreshold;
            var tipRect = preferBelow ? tipRectBelow : tipRectAbove;

            if (tipRect.yMax > plotBottom - edgeMargin)
                tipRect = tipRectAbove;
            if (tipRect.yMin < plotTop + edgeMargin)
                tipRect = tipRectBelow;
            if (tipRect.yMax > plotBottom - edgeMargin)
                tipRect.y = plotBottom - edgeMargin - tipH;
            if (tipRect.yMin < plotTop + edgeMargin)
                tipRect.y = plotTop + edgeMargin;

            if (tipRect.xMin < plotLeft + edgeMargin)
                tipRect.x = plotLeft + edgeMargin;
            if (tipRect.xMax > graphBounds.width - edgeMargin)
                tipRect.x = graphBounds.width - edgeMargin - tipW;

            GUI.Box(tipRect, GUIContent.none, _styleManager.SectionStyle);

            var lineRect = new Rect(tipRect.x + padX, tipRect.y + padY, contentW, lineH);
            GUI.Label(lineRect, altText, tipStyle);
            lineRect.y += lineH + lineGap;
            GUI.Label(lineRect, payloadText, tipStyle);
        }

        private void DrawLine(Vector2 start, Vector2 end, Color color, float width, Rect? clipRect = null)
        {
            if (clipRect.HasValue)
            {
                if (!ClipSegment(ref start, ref end, clipRect.Value))
                    return;
            }

            var delta = end - start;
            var length = delta.magnitude;
            if (length < 0.01f)
                return;

            if (Mathf.Abs(delta.y) < 0.01f)
            {
                DrawColoredTexture(new Rect(Mathf.Min(start.x, end.x), start.y - width * 0.5f, Mathf.Abs(delta.x), width), color);
                return;
            }

            if (Mathf.Abs(delta.x) < 0.01f)
            {
                DrawColoredTexture(new Rect(start.x - width * 0.5f, Mathf.Min(start.y, end.y), width, Mathf.Abs(delta.y)), color);
                return;
            }

            var stepCount = Mathf.CeilToInt(length / Mathf.Max(1f, width * 0.65f));
            for (var i = 0; i <= stepCount; i++)
            {
                var p = Vector2.Lerp(start, end, i / (float)stepCount);
                DrawColoredTexture(new Rect(p.x - width * 0.5f, p.y - width * 0.5f, width, width), color);
            }
        }

        private void DrawColoredTexture(Rect rect, Color color)
        {
            var savedColor = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(rect, _lineTexture);
            GUI.color = savedColor;
        }

        private static bool ClipSegment(ref Vector2 start, ref Vector2 end, Rect clip)
        {
            var code0 = ComputeOutCode(start, clip);
            var code1 = ComputeOutCode(end, clip);
            while (true)
            {
                if ((code0 | code1) == 0)
                    return true;
                if ((code0 & code1) != 0)
                    return false;

                var outCode = code0 != 0 ? code0 : code1;
                Vector2 p;
                if ((outCode & 8) != 0)
                {
                    p.x = start.x + (end.x - start.x) * (clip.yMax - start.y) / (end.y - start.y);
                    p.y = clip.yMax;
                }
                else if ((outCode & 4) != 0)
                {
                    p.x = start.x + (end.x - start.x) * (clip.yMin - start.y) / (end.y - start.y);
                    p.y = clip.yMin;
                }
                else if ((outCode & 2) != 0)
                {
                    p.y = start.y + (end.y - start.y) * (clip.xMax - start.x) / (end.x - start.x);
                    p.x = clip.xMax;
                }
                else
                {
                    p.y = start.y + (end.y - start.y) * (clip.xMin - start.x) / (end.x - start.x);
                    p.x = clip.xMin;
                }

                if (outCode == code0)
                {
                    start = p;
                    code0 = ComputeOutCode(start, clip);
                }
                else
                {
                    end = p;
                    code1 = ComputeOutCode(end, clip);
                }
            }
        }

        private static int ComputeOutCode(Vector2 p, Rect clip)
        {
            var code = 0;
            if (p.x < clip.xMin) code |= 1;
            if (p.x > clip.xMax) code |= 2;
            if (p.y < clip.yMin) code |= 4;
            if (p.y > clip.yMax) code |= 8;
            return code;
        }

        public void Dispose()
        {
            if (_lineTexture != null)
            {
                UnityEngine.Object.Destroy(_lineTexture);
                _lineTexture = null;
            }
        }
    }
}
