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
                            _windowRect = new Rect(Screen.width * 0.5f - 450, Screen.height * 0.5f - 250, 900, 500);
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
            _windowRect = new Rect(Screen.width * 0.5f - 450, Screen.height * 0.5f - 250, 900, 500);
            
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
            _windowRect = ClickThruBlocker.GUILayoutWindow(WindowId, _windowRect, DrawWindow, Localizer.Format("#LOC_OPC_AnalysisTitle"), _styleManager.WindowStyle);
        }

        private void DrawWindow(int id)
        {
            GUILayout.BeginVertical();
            GUILayout.Space(6);
            
            // Settings Row
            GUILayout.BeginHorizontal(_styleManager.PanelStyle, GUILayout.Height(32));
            
            GUILayout.BeginHorizontal(GUILayout.Width(240));
            GUILayout.FlexibleSpace();
            GUILayout.Label(Localizer.Format("#LOC_OPC_MinAlt") + " (km):", _styleManager.LabelStyle, GUILayout.ExpandWidth(true));
            _minAltInput = GUILayout.TextField(_minAltInput, _styleManager.FieldStyle, GUILayout.Width(60));
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            
            GUILayout.Space(10);
            
            GUILayout.BeginHorizontal(GUILayout.Width(240));
            GUILayout.FlexibleSpace();
            GUILayout.Label(Localizer.Format("#LOC_OPC_MaxAlt") + " (km):", _styleManager.LabelStyle, GUILayout.ExpandWidth(true));
            _maxAltInput = GUILayout.TextField(_maxAltInput, _styleManager.FieldStyle, GUILayout.Width(60));
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            
            GUILayout.Space(10);

            GUILayout.BeginHorizontal(GUILayout.Width(150));
            GUILayout.FlexibleSpace();
            GUILayout.Label(Localizer.Format("#LOC_OPC_Steps") + ":", _styleManager.LabelStyle, GUILayout.ExpandWidth(true));
            _stepsInput = GUILayout.TextField(_stepsInput, _styleManager.FieldStyle, GUILayout.Width(40));
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            
            GUILayout.FlexibleSpace();
            
            if (GUILayout.Button(Localizer.Format("#LOC_OPC_Analyze"), _styleManager.ButtonStyle, GUILayout.Width(100), GUILayout.Height(28)))
            {
                RunAnalysis();
            }
            
            GUILayout.EndHorizontal();

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
            if (GUILayout.Button(Localizer.Format("#LOC_OPC_Close"), _styleManager.ButtonStyle))
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

        private void DrawGraph(Rect rect)
        {
            // Background
            GUI.Box(rect, "", _styleManager.PanelStyle);
            
            if (!_hasData || _dataPoints.Count < 2)
            {
                GUI.Label(new Rect(rect.center.x - 50, rect.center.y - 10, 100, 20), "No Data", _styleManager.LabelStyle);
                return;
            }

            // Margins
            float leftM = 50;
            float bottomM = 40;
            float topM = 30;
            float rightM = 20;
            
            float graphW = rect.width - leftM - rightM;
            float graphH = rect.height - bottomM - topM;
            
            float xMin = _dataPoints[0].x;
            float xMax = _dataPoints[_dataPoints.Count - 1].x;
            float yMin = _minPayload;
            float yMax = _maxPayload * 1.1f; // Add 10% headroom
            
            if (yMax <= yMin) yMax = yMin + 1f;

            // Draw Axes
            DrawLine(new Vector2(rect.x + leftM, rect.y + topM), new Vector2(rect.x + leftM, rect.y + rect.height - bottomM), Color.gray, 2); // Y Axis
            DrawLine(new Vector2(rect.x + leftM, rect.y + rect.height - bottomM), new Vector2(rect.x + rect.width - rightM, rect.y + rect.height - bottomM), Color.gray, 2); // X Axis

            // Draw Grid & Labels
            // Y Axis Labels (Payload)
            int ySteps = 5;
            for (int i = 0; i <= ySteps; i++)
            {
                float t = i / (float)ySteps;
                float val = Mathf.Lerp(yMin, yMax, t);
                float yPos = rect.y + rect.height - bottomM - (t * graphH);
                
                GUI.Label(new Rect(rect.x, yPos - 10, leftM - 5, 20), val.ToString("F1"), _styleManager.SmallLabelStyle);
                DrawLine(new Vector2(rect.x + leftM, yPos), new Vector2(rect.x + rect.width - rightM, yPos), new Color(1,1,1,0.1f), 1);
            }

            // X Axis Labels (Alt)
            int xSteps = 5;
            for (int i = 0; i <= xSteps; i++)
            {
                float t = i / (float)xSteps;
                float val = Mathf.Lerp(xMin, xMax, t);
                float xPos = rect.x + leftM + (t * graphW);
                
                // Align center
                GUI.Label(new Rect(xPos - 20, rect.y + rect.height - bottomM + 5, 40, 20), val.ToString("F0"), _styleManager.SmallLabelStyle);
                DrawLine(new Vector2(xPos, rect.y + topM), new Vector2(xPos, rect.y + rect.height - bottomM), new Color(1,1,1,0.1f), 1);
            }
            
            // Axis Titles
            GUI.Label(new Rect(rect.x + leftM + graphW/2 - 50, rect.y + rect.height - 25, 100, 20), Localizer.Format("#LOC_OPC_AltitudeKm"), _styleManager.SmallBoldLabelStyle);
            GUI.Label(new Rect(rect.x + 5, rect.y + 0, 100, 20), Localizer.Format("#LOC_OPC_PayloadTons"), _styleManager.SmallBoldLabelStyle);

            // Draw Data Line
            Vector2? prevPos = null;
            for (int i = 0; i < _dataPoints.Count; i++)
            {
                var pt = _dataPoints[i];
                float xNorm = (pt.x - xMin) / (xMax - xMin);
                float yNorm = (pt.y - yMin) / (yMax - yMin);
                
                Vector2 screenPos = new Vector2(
                    rect.x + leftM + xNorm * graphW,
                    rect.y + rect.height - bottomM - yNorm * graphH
                );
                
                if (prevPos.HasValue)
                {
                    DrawLine(prevPos.Value, screenPos, Color.green, 2);
                }
                
                // Draw point
                GUI.DrawTexture(new Rect(screenPos.x - 2, screenPos.y - 2, 4, 4), _lineTexture);
                
                prevPos = screenPos;
            }
            
            // Tooltip (Simple)
            Vector2 mouse = Event.current.mousePosition;
            if (rect.Contains(mouse) && _hasData)
            {
                float mouseXRel = mouse.x - (rect.x + leftM);
                float t = Mathf.Clamp01(mouseXRel / graphW);
                float hoverAlt = Mathf.Lerp(xMin, xMax, t);
                
                // Find closest point by distance in array index to avoid search
                // Index ~ t * (steps - 1)
                int index = Mathf.RoundToInt(t * (_dataPoints.Count - 1));
                index = Mathf.Clamp(index, 0, _dataPoints.Count - 1);
                var closest = _dataPoints[index];
                
                float xNorm = (closest.x - xMin) / (xMax - xMin);
                float yNorm = (closest.y - yMin) / (yMax - yMin);
                Vector2 ptPos = new Vector2(
                    rect.x + leftM + xNorm * graphW,
                    rect.y + rect.height - bottomM - yNorm * graphH
                );
                
                GUI.DrawTexture(new Rect(ptPos.x - 4, ptPos.y - 4, 8, 8), _lineTexture);
                
                // Draw tooltip box
                Rect tipRect = new Rect(ptPos.x + 10, ptPos.y - 40, 100, 40);
                // Adjust if off screen
                if (tipRect.xMax > rect.xMax) tipRect.x -= 120;
                
                GUI.Box(tipRect, "", _styleManager.PanelStyle);
                GUI.Label(tipRect, $"{closest.x:F0} km\n{closest.y:F2} t", _styleManager.SmallBoldLabelStyle);
            }
        }

        private void DrawLine(Vector2 start, Vector2 end, Color color, float width)
        {
            var savedColor = GUI.color;
            var savedMatrix = GUI.matrix;
            
            GUI.color = color;
            float angle = Mathf.Atan2(end.y - start.y, end.x - start.x) * Mathf.Rad2Deg;
            
            GUIUtility.RotateAroundPivot(angle, start);
            GUI.DrawTexture(new Rect(start.x, start.y - width/2, (end-start).magnitude, width), _lineTexture);
            
            GUI.matrix = savedMatrix;
            GUI.color = savedColor;
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
