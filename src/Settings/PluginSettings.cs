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
        private const KeyCode DefaultHotkeyKey = KeyCode.P;
        private const bool DefaultHotkeyAlt = true;
        private const bool DefaultHotkeyCtrl = false;
        private const bool DefaultHotkeyShift = false;
        private const bool DefaultTreatCargoBayAsFairing = false;
        private const string ConfigFileName = "config.xml";
        private const string ConfigRootName = "OrbitalPayloadCalculator";
        private const string FontSizeElementName = "FontSize";
        private const string HotkeyKeyElementName = "HotkeyKey";
        private const string HotkeyAltElementName = "HotkeyAlt";
        private const string HotkeyCtrlElementName = "HotkeyCtrl";
        private const string HotkeyShiftElementName = "HotkeyShift";
        private const string TreatCargoBayAsFairingElementName = "TreatCargoBayAsFairing";

        private static readonly string ConfigDirectory =
            Path.Combine(KSPUtil.ApplicationRootPath, "GameData", "OrbitalPayloadCalculator", "PluginData");
        private static readonly string ConfigPath = Path.Combine(ConfigDirectory, ConfigFileName);

        public int FontSize { get; private set; }
        public KeyCode HotkeyKey { get; private set; }
        public bool HotkeyAlt { get; private set; }
        public bool HotkeyCtrl { get; private set; }
        public bool HotkeyShift { get; private set; }
        public bool TreatCargoBayAsFairing { get; private set; }

        private PluginSettings(int fontSize, KeyCode hotkeyKey, bool hotkeyAlt, bool hotkeyCtrl, bool hotkeyShift, bool treatCargoBayAsFairing)
        {
            FontSize = Mathf.Clamp(fontSize, 13, 20);
            HotkeyKey = hotkeyKey;
            HotkeyAlt = hotkeyAlt;
            HotkeyCtrl = hotkeyCtrl;
            HotkeyShift = hotkeyShift;
            TreatCargoBayAsFairing = treatCargoBayAsFairing;
        }

        public static PluginSettings LoadOrDefault()
        {
            var savedFontSize = DefaultFontSize;
            var savedHotkeyKey = DefaultHotkeyKey;
            var savedHotkeyAlt = DefaultHotkeyAlt;
            var savedHotkeyCtrl = DefaultHotkeyCtrl;
            var savedHotkeyShift = DefaultHotkeyShift;
            var savedTreatCargoBayAsFairing = DefaultTreatCargoBayAsFairing;
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

                    var keyMatch = Regex.Match(text, $"<{HotkeyKeyElementName}>(.*?)</{HotkeyKeyElementName}>", RegexOptions.Singleline);
                    if (keyMatch.Success && Enum.TryParse(keyMatch.Groups[1].Value, true, out KeyCode parsedKey))
                    {
                        savedHotkeyKey = parsedKey;
                    }

                    var altMatch = Regex.Match(text, $"<{HotkeyAltElementName}>(.*?)</{HotkeyAltElementName}>", RegexOptions.Singleline);
                    if (altMatch.Success && bool.TryParse(altMatch.Groups[1].Value, out var parsedAlt))
                    {
                        savedHotkeyAlt = parsedAlt;
                    }

                    var ctrlMatch = Regex.Match(text, $"<{HotkeyCtrlElementName}>(.*?)</{HotkeyCtrlElementName}>", RegexOptions.Singleline);
                    if (ctrlMatch.Success && bool.TryParse(ctrlMatch.Groups[1].Value, out var parsedCtrl))
                    {
                        savedHotkeyCtrl = parsedCtrl;
                    }

                    var shiftMatch = Regex.Match(text, $"<{HotkeyShiftElementName}>(.*?)</{HotkeyShiftElementName}>", RegexOptions.Singleline);
                    if (shiftMatch.Success && bool.TryParse(shiftMatch.Groups[1].Value, out var parsedShift))
                    {
                        savedHotkeyShift = parsedShift;
                    }

                    var cargoMatch = Regex.Match(text, $"<{TreatCargoBayAsFairingElementName}>(.*?)</{TreatCargoBayAsFairingElementName}>", RegexOptions.Singleline);
                    if (cargoMatch.Success && bool.TryParse(cargoMatch.Groups[1].Value, out var parsedCargo))
                    {
                        savedTreatCargoBayAsFairing = parsedCargo;
                    }
                }
            }
            catch
            {
                savedFontSize = DefaultFontSize;
                savedHotkeyKey = DefaultHotkeyKey;
                savedHotkeyAlt = DefaultHotkeyAlt;
                savedHotkeyCtrl = DefaultHotkeyCtrl;
                savedHotkeyShift = DefaultHotkeyShift;
                savedTreatCargoBayAsFairing = DefaultTreatCargoBayAsFairing;
            }

            return new PluginSettings(savedFontSize, savedHotkeyKey, savedHotkeyAlt, savedHotkeyCtrl, savedHotkeyShift, savedTreatCargoBayAsFairing);
        }

        public void SetFontSize(int fontSize)
        {
            FontSize = Mathf.Clamp(fontSize, 13, 20);
            Save();
        }

        public void SetHotkey(KeyCode hotkeyKey, bool hotkeyAlt, bool hotkeyCtrl, bool hotkeyShift)
        {
            HotkeyKey = hotkeyKey;
            HotkeyAlt = hotkeyAlt;
            HotkeyCtrl = hotkeyCtrl;
            HotkeyShift = hotkeyShift;
            Save();
        }

        public void SetTreatCargoBayAsFairing(bool value)
        {
            TreatCargoBayAsFairing = value;
            Save();
        }

        private void Save()
        {
            try
            {
                Directory.CreateDirectory(ConfigDirectory);
                var content =
                    $"<{ConfigRootName}>\n" +
                    $"  <{FontSizeElementName}>{FontSize.ToString(CultureInfo.InvariantCulture)}</{FontSizeElementName}>\n" +
                    $"  <{HotkeyKeyElementName}>{HotkeyKey}</{HotkeyKeyElementName}>\n" +
                    $"  <{HotkeyAltElementName}>{HotkeyAlt}</{HotkeyAltElementName}>\n" +
                    $"  <{HotkeyCtrlElementName}>{HotkeyCtrl}</{HotkeyCtrlElementName}>\n" +
                    $"  <{HotkeyShiftElementName}>{HotkeyShift}</{HotkeyShiftElementName}>\n" +
                    $"  <{TreatCargoBayAsFairingElementName}>{TreatCargoBayAsFairing}</{TreatCargoBayAsFairingElementName}>\n" +
                    $"</{ConfigRootName}>\n";
                File.WriteAllText(ConfigPath, content);
            }
            catch
            {
            }
        }
    }
}
