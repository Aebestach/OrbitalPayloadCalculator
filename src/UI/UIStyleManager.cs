using System;
using UnityEngine;

namespace OrbitalPayloadCalculator.UI
{
    internal sealed class UIStyleManager : IDisposable
    {
        public GUIStyle WindowStyle { get; private set; }
        public GUIStyle LabelStyle { get; private set; }
        public GUIStyle HeaderStyle { get; private set; }
        public GUIStyle SmallLabelStyle { get; private set; }
        public GUIStyle FieldStyle { get; private set; }
        public GUIStyle ButtonStyle { get; private set; }
        public GUIStyle ToggleStyle { get; private set; }
        public GUIStyle CenteredHeaderStyle { get; private set; }
        public GUIStyle PanelStyle { get; private set; }
        public GUIStyle SectionStyle { get; private set; }

        private int _fontSize = -1;
        private Texture2D _panelBgTexture;
        private Texture2D _sectionBgTexture;

        public void RebuildIfNeeded(int fontSize)
        {
            var clamped = Mathf.Clamp(fontSize, 13, 20);
            if (clamped == _fontSize && WindowStyle != null)
                return;

            DisposeStyles();
            _fontSize = clamped;
            BuildStyles(clamped);
            GUIUtility.ExitGUI();
        }

        public void Dispose()
        {
            DisposeStyles();
        }

        private void BuildStyles(int fontSize)
        {
            var skin = HighLogic.Skin ?? GUI.skin;
            WindowStyle = new GUIStyle(skin.window) { fontSize = fontSize + 2 };
            LabelStyle = new GUIStyle(skin.label) { fontSize = fontSize };
            HeaderStyle = new GUIStyle(skin.label)
            {
                fontSize = fontSize + 1,
                fontStyle = FontStyle.Bold
            };
            CenteredHeaderStyle = new GUIStyle(skin.label)
            {
                fontSize = fontSize + 1,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter
            };
            SmallLabelStyle = new GUIStyle(skin.label)
            {
                fontSize = Mathf.Max(11, fontSize - 2),
                fontStyle = FontStyle.Italic
            };
            FieldStyle = new GUIStyle(skin.textField) { fontSize = fontSize };
            ButtonStyle = new GUIStyle(skin.button) { fontSize = fontSize, alignment = TextAnchor.MiddleCenter };
            ToggleStyle = new GUIStyle(skin.toggle) { fontSize = fontSize };

            _panelBgTexture = new Texture2D(1, 1, TextureFormat.ARGB32, false);
            _panelBgTexture.SetPixel(0, 0, new Color(0f, 0f, 0f, 0.35f));
            _panelBgTexture.Apply(false, false);

            PanelStyle = new GUIStyle
            {
                normal = { background = _panelBgTexture },
                padding = new RectOffset(8, 8, 6, 6),
                margin = new RectOffset(0, 0, 0, 0)
            };

            _sectionBgTexture = new Texture2D(1, 1, TextureFormat.ARGB32, false);
            _sectionBgTexture.SetPixel(0, 0, new Color(1f, 1f, 1f, 0.06f));
            _sectionBgTexture.Apply(false, false);

            SectionStyle = new GUIStyle
            {
                normal = { background = _sectionBgTexture },
                padding = new RectOffset(6, 6, 4, 4),
                margin = new RectOffset(0, 0, 2, 2)
            };
        }

        private void DisposeStyles()
        {
            WindowStyle = null;
            LabelStyle = null;
            HeaderStyle = null;
            CenteredHeaderStyle = null;
            SmallLabelStyle = null;
            FieldStyle = null;
            ButtonStyle = null;
            ToggleStyle = null;
            PanelStyle = null;
            SectionStyle = null;

            if (_panelBgTexture != null)
            {
                UnityEngine.Object.Destroy(_panelBgTexture);
                _panelBgTexture = null;
            }

            if (_sectionBgTexture != null)
            {
                UnityEngine.Object.Destroy(_sectionBgTexture);
                _sectionBgTexture = null;
            }
        }
    }
}
