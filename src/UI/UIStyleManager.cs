using System;
using UnityEngine;

namespace OrbitalPayloadCalculator.UI
{
    internal sealed class UIStyleManager : IDisposable
    {
        public GUIStyle WindowStyle { get; private set; }
        public GUIStyle LabelStyle { get; private set; }
        /// <summary>Label style with vertical centering for use in horizontal rows (e.g. engine classification).</summary>
        public GUIStyle LabelStyleRow { get; private set; }
        public GUIStyle HeaderStyle { get; private set; }
        public GUIStyle SmallLabelStyle { get; private set; }
        public GUIStyle SmallBoldLabelStyle { get; private set; }
        public GUIStyle FieldStyle { get; private set; }
        public GUIStyle ButtonStyle { get; private set; }
        public GUIStyle SelectedButtonStyle { get; private set; }
        public GUIStyle ToggleStyle { get; private set; }
        public GUIStyle CenteredHeaderStyle { get; private set; }
        public GUIStyle PanelStyle { get; private set; }
        public GUIStyle SectionStyle { get; private set; }
        public GUIStyle TooltipStyle { get; private set; }
        public GUIStyle WarningLabelStyle { get; private set; }
        /// <summary>Single-line warning text; avoids orphan punctuation when wrapping.</summary>
        public GUIStyle WarningLabelRowStyle { get; private set; }
        /// <summary>Multi-line hint text only; all other labels use single-line styles.</summary>
        public GUIStyle HintLabelStyle { get; private set; }
        /// <summary>Multi-line help text in popups (advanced settings help, etc.).</summary>
        public GUIStyle HelpLabelStyle { get; private set; }

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
        }

        public void Dispose()
        {
            DisposeStyles();
        }

        private void BuildStyles(int fontSize)
        {
            var skin = HighLogic.Skin ?? GUI.skin;
            WindowStyle = new GUIStyle(skin.window) { fontSize = fontSize + 2 };
            LabelStyle = CreateSingleLineLabel(skin.label, fontSize);
            LabelStyleRow = CreateSingleLineLabel(skin.label, fontSize, TextAnchor.MiddleLeft);
            HeaderStyle = CreateSingleLineLabel(skin.label, fontSize + 1, TextAnchor.MiddleLeft, FontStyle.Bold);
            CenteredHeaderStyle = CreateSingleLineLabel(skin.label, fontSize + 1, TextAnchor.MiddleCenter, FontStyle.Bold);
            SmallLabelStyle = CreateSingleLineLabel(skin.label, Mathf.Max(11, fontSize - 2));
            SmallBoldLabelStyle = CreateSingleLineLabel(skin.label, Mathf.Max(11, fontSize - 2), TextAnchor.MiddleLeft, FontStyle.Bold);
            FieldStyle = new GUIStyle(skin.textField)
            {
                fontSize = fontSize,
                alignment = TextAnchor.MiddleLeft,
                wordWrap = false,
                clipping = TextClipping.Clip,
                padding = new RectOffset(skin.textField.padding.left, skin.textField.padding.right, 4, 4)
            };
            ButtonStyle = new GUIStyle(skin.button)
            {
                fontSize = fontSize,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                wordWrap = false,
                clipping = TextClipping.Clip,
                padding = new RectOffset(skin.button.padding.left, skin.button.padding.right, 6, 6)
            };
            SelectedButtonStyle = new GUIStyle(skin.button)
            {
                fontSize = fontSize,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                wordWrap = false,
                clipping = TextClipping.Clip,
                padding = new RectOffset(skin.button.padding.left, skin.button.padding.right, 6, 6),
                normal = { textColor = new Color(0.4f, 1f, 0.4f) },
                hover = { textColor = new Color(0.4f, 1f, 0.4f) }
            };
            ToggleStyle = new GUIStyle(skin.toggle)
            {
                fontSize = fontSize,
                alignment = TextAnchor.MiddleLeft,
                wordWrap = false,
                clipping = TextClipping.Clip,
                padding = new RectOffset(0, 0, 0, 0),
                margin = new RectOffset(0, 0, 0, 0)
            };

            WarningLabelStyle = new GUIStyle(skin.label)
            {
                fontSize = fontSize,
                normal = { textColor = new Color(1f, 0.85f, 0.2f) },
                wordWrap = true,
                clipping = TextClipping.Overflow,
                padding = new RectOffset(2, 2, 4, 4)
            };
            WarningLabelRowStyle = new GUIStyle(skin.label)
            {
                fontSize = fontSize,
                normal = { textColor = new Color(1f, 0.85f, 0.2f) },
                alignment = TextAnchor.MiddleLeft,
                wordWrap = false,
                clipping = TextClipping.Overflow,
                padding = new RectOffset(2, 2, 4, 4)
            };
            HintLabelStyle = new GUIStyle(skin.label)
            {
                fontSize = Mathf.Max(11, fontSize - 2),
                wordWrap = true,
                clipping = TextClipping.Overflow,
                alignment = TextAnchor.UpperLeft,
                padding = new RectOffset(2, 2, 4, 4)
            };
            HelpLabelStyle = new GUIStyle(skin.label)
            {
                fontSize = Mathf.Max(11, fontSize - 2),
                wordWrap = false,
                clipping = TextClipping.Overflow,
                alignment = TextAnchor.UpperLeft,
                padding = new RectOffset(2, 2, 4, 4)
            };

            _panelBgTexture = new Texture2D(1, 1, TextureFormat.ARGB32, false);
            _panelBgTexture.SetPixel(0, 0, new Color(0f, 0f, 0f, 0.35f));
            _panelBgTexture.Apply(false, false);

            PanelStyle = new GUIStyle
            {
                normal = { background = _panelBgTexture },
                padding = new RectOffset(8, 8, 8, 8),
                margin = new RectOffset(0, 0, 0, 0),
                stretchWidth = true
            };

            _sectionBgTexture = new Texture2D(1, 1, TextureFormat.ARGB32, false);
            _sectionBgTexture.SetPixel(0, 0, new Color(1f, 1f, 1f, 0.06f));
            _sectionBgTexture.Apply(false, false);

            SectionStyle = new GUIStyle
            {
                normal = { background = _sectionBgTexture },
                padding = new RectOffset(8, 8, 8, 6),
                margin = new RectOffset(0, 0, 2, 2),
                stretchWidth = true
            };

            TooltipStyle = new GUIStyle(skin.label)
            {
                fontSize = Mathf.Max(12, fontSize - 1),
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                wordWrap = false,
                clipping = TextClipping.Overflow,
                padding = new RectOffset(0, 0, 0, 0),
                margin = new RectOffset(0, 0, 0, 0),
                normal = { textColor = new Color(0.55f, 1f, 0.55f) }
            };
        }

        private static GUIStyle CreateSingleLineLabel(GUIStyle template, int fontSize, TextAnchor alignment = TextAnchor.MiddleLeft, FontStyle fontStyle = FontStyle.Normal)
        {
            return new GUIStyle(template)
            {
                fontSize = fontSize,
                fontStyle = fontStyle,
                alignment = alignment,
                wordWrap = false,
                clipping = TextClipping.Overflow,
                padding = new RectOffset(2, 2, 4, 4)
            };
        }

        private void DisposeStyles()
        {
            WindowStyle = null;
            LabelStyle = null;
            LabelStyleRow = null;
            HeaderStyle = null;
            CenteredHeaderStyle = null;
            SmallLabelStyle = null;
            SmallBoldLabelStyle = null;
            FieldStyle = null;
            ButtonStyle = null;
            SelectedButtonStyle = null;
            ToggleStyle = null;
            PanelStyle = null;
            SectionStyle = null;
            TooltipStyle = null;
            WarningLabelStyle = null;
            WarningLabelRowStyle = null;
            HintLabelStyle = null;
            HelpLabelStyle = null;

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
