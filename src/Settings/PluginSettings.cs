using System;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using UnityEngine;

namespace OrbitalPayloadCalculator.Settings
{
    internal sealed class PluginSettings
    {
        private const int DefaultFontSize = 13;
        private const string ConfigFileName = "config.xml";
        private const string ConfigRootName = "OrbitalPayloadCalculator";
        private const string FontSizeElementName = "FontSize";

        private static readonly string ConfigDirectory =
            Path.Combine(KSPUtil.ApplicationRootPath, "GameData", "OrbitalPayloadCalculator", "PluginData");
        private static readonly string ConfigPath = Path.Combine(ConfigDirectory, ConfigFileName);

        public int FontSize { get; private set; }

        private PluginSettings(int fontSize)
        {
            FontSize = Mathf.Clamp(fontSize, 13, 20);
        }

        public static PluginSettings LoadOrDefault()
        {
            var savedFontSize = DefaultFontSize;
            try
            {
                if (File.Exists(ConfigPath))
                {
                    var text = File.ReadAllText(ConfigPath);
                    var match = Regex.Match(text, $"<{FontSizeElementName}>(.*?)</{FontSizeElementName}>", RegexOptions.Singleline);
                    if (match.Success && int.TryParse(match.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
                    {
                        savedFontSize = parsed;
                    }
                }
            }
            catch
            {
                savedFontSize = DefaultFontSize;
            }

            return new PluginSettings(savedFontSize);
        }

        public void SetFontSize(int fontSize)
        {
            FontSize = Mathf.Clamp(fontSize, 13, 20);
            try
            {
                Directory.CreateDirectory(ConfigDirectory);
                var content =
                    $"<{ConfigRootName}>\n  <{FontSizeElementName}>{FontSize.ToString(CultureInfo.InvariantCulture)}</{FontSizeElementName}>\n</{ConfigRootName}>\n";
                File.WriteAllText(ConfigPath, content);
            }
            catch
            {
            }
        }
    }
}
