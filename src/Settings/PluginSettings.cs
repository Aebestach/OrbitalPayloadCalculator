using UnityEngine;

namespace OrbitalPayloadCalculator.Settings
{
    internal sealed class PluginSettings
    {
        private const string FontSizeKey = "OPC_FontSize";
        private const int DefaultFontSize = 13;

        public int FontSize { get; private set; }

        private PluginSettings(int fontSize)
        {
            FontSize = Mathf.Clamp(fontSize, 13, 20);
        }

        public static PluginSettings LoadOrDefault()
        {
            var savedFontSize = PlayerPrefs.GetInt(FontSizeKey, DefaultFontSize);
            return new PluginSettings(savedFontSize);
        }

        public void SetFontSize(int fontSize)
        {
            FontSize = Mathf.Clamp(fontSize, 13, 20);
            PlayerPrefs.SetInt(FontSizeKey, FontSize);
            PlayerPrefs.Save();
        }
    }
}
