using OrbitalPayloadCalculator.Settings;
using UnityEngine;

namespace OrbitalPayloadCalculator.UI
{
    /// <summary>
    /// UI scaling for IMGUI mod windows.
    /// Uses KSP's UI scale settings, then applies mod UI scale (50%–150%).
    /// When KSP scale is at default, gently compensates for resolutions above 1080p.
    /// </summary>
    internal static class UIScale
    {
        private static Matrix4x4 _savedMatrix;

        public static bool IsActive { get; private set; }

        internal static float DefaultUiScalePercent =>
            Screen.height >= 2160 ? 75f : 100f;

        internal static float ModUiScalePercent =>
            OrbitalPayloadCalculatorParameters.Instance?.uiScalePercent ?? DefaultUiScalePercent;

        public static float Factor
        {
            get
            {
                float ksp = GameSettings.UI_SCALE;
                if (GameSettings.UI_SCALE_APPS > 1f)
                    ksp *= GameSettings.UI_SCALE_APPS;
                if (ksp <= 1.01f && Screen.height > 1080)
                    ksp = Mathf.Max(ksp, Mathf.Min(1.5f, Mathf.Sqrt((float)Screen.height / 1080f)));

                float mod = Mathf.Clamp(ModUiScalePercent / 100f, 0.5f, 1.5f);
                return ksp * mod;
            }
        }

        public static int Scale(int value) => Mathf.Max(1, Mathf.RoundToInt(value * Factor));

        public static float Scale(float value) => value * Factor;

        public static void BeginGUI()
        {
            float factor = Factor;
            if (Mathf.Abs(factor - 1f) < 0.001f)
                return;

            _savedMatrix = GUI.matrix;
            GUIUtility.ScaleAroundPivot(new Vector2(factor, factor), Vector2.zero);
            IsActive = true;
        }

        public static void EndGUI()
        {
            if (!IsActive)
                return;

            GUI.matrix = _savedMatrix;
            IsActive = false;
        }

        public static Vector2 GuiScreenSize()
        {
            float factor = Factor;
            return Mathf.Abs(factor - 1f) < 0.001f
                ? new Vector2(Screen.width, Screen.height)
                : new Vector2(Screen.width / factor, Screen.height / factor);
        }

        public static Rect ClampToGuiScreen(Rect rect, float padding = 12f)
        {
            Vector2 screen = GuiScreenSize();
            float maxX = Mathf.Max(padding, screen.x - rect.width - padding);
            float maxY = Mathf.Max(padding, screen.y - rect.height - padding);
            rect.x = Mathf.Clamp(rect.x, padding, maxX);
            rect.y = Mathf.Clamp(rect.y, padding, maxY);
            return rect;
        }
    }
}
