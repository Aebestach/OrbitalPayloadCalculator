using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using KSP.Localization;
using UnityEngine;

namespace OrbitalPayloadCalculator.Settings
{
    public class OrbitalPayloadCalculatorParameters : GameParameters.CustomParameterNode
    {
        public override string Title => Localizer.Format("#LOC_OPC_ParamTitle");
        public override GameParameters.GameMode GameMode => GameParameters.GameMode.ANY;
        public override string Section => Localizer.Format("#LOC_OPC_ParamSection");
        public override string DisplaySection => Section;
        public override int SectionOrder => 0;
        public override bool HasPresets => false;

        [GameParameters.CustomParameterUI(
            "#LOC_OPC_ParamUiScaleAuto",
            toolTip = "#LOC_OPC_ParamUiScaleAuto_tip")]
        public bool uiScaleAuto = true;

        [GameParameters.CustomFloatParameterUI(
            "#LOC_OPC_ParamUiScalePercent",
            toolTip = "#LOC_OPC_ParamUiScalePercent_tip",
            minValue = 50f,
            maxValue = 150f,
            stepCount = 100,
            displayFormat = "N0")]
        public float uiScalePercent = 100f;

        [GameParameters.CustomStringParameterUI(
            "#LOC_OPC_ParamHotkeyKey",
            toolTip = "#LOC_OPC_ParamHotkeyKey_tip")]
        public string hotkeyKey = "P";

        [GameParameters.CustomParameterUI(
            "#LOC_OPC_ParamHotkeyAlt",
            toolTip = "#LOC_OPC_ParamHotkeyAlt_tip")]
        public bool hotkeyAlt = true;

        [GameParameters.CustomParameterUI(
            "#LOC_OPC_ParamHotkeyCtrl",
            toolTip = "#LOC_OPC_ParamHotkeyCtrl_tip")]
        public bool hotkeyCtrl = false;

        [GameParameters.CustomParameterUI(
            "#LOC_OPC_ParamHotkeyShift",
            toolTip = "#LOC_OPC_ParamHotkeyShift_tip")]
        public bool hotkeyShift = false;

        private static OrbitalPayloadCalculatorParameters instance;

        public static OrbitalPayloadCalculatorParameters Instance
        {
            get
            {
                if (instance == null && HighLogic.CurrentGame != null)
                    instance = HighLogic.CurrentGame.Parameters.CustomParams<OrbitalPayloadCalculatorParameters>();
                return instance;
            }
        }

        internal KeyCode ResolveHotkeyKey()
        {
            if (string.IsNullOrEmpty(hotkeyKey) || string.Equals(hotkeyKey, "None", StringComparison.OrdinalIgnoreCase))
                return KeyCode.None;

            return Enum.TryParse(hotkeyKey, true, out KeyCode parsed) ? parsed : KeyCode.P;
        }

        internal bool IsHotkeyPressed()
        {
            KeyCode key = ResolveHotkeyKey();
            if (key == KeyCode.None || !Input.GetKeyDown(key))
                return false;
            if (hotkeyAlt && !(Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt)))
                return false;
            if (hotkeyCtrl && !(Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl)))
                return false;
            if (hotkeyShift && !(Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift)))
                return false;
            return true;
        }

        public override void OnLoad(ConfigNode node)
        {
            bool hadAutoFlag = node != null && node.HasValue("uiScaleAuto");
            bool hadUiScale = node != null && node.HasValue("uiScalePercent");
            base.OnLoad(node);
            instance = null;

            if (!hadAutoFlag)
            {
                uiScaleAuto = !hadUiScale ||
                    Mathf.Approximately(uiScalePercent, 100f) ||
                    Mathf.Approximately(uiScalePercent, 80f);
            }

            ApplyAutoUiScale();
        }

        internal void ApplyAutoUiScale()
        {
            if (!uiScaleAuto)
                return;
            uiScalePercent = UI.UIScale.DefaultUiScalePercent;
        }

        public override bool Enabled(MemberInfo member, GameParameters parameters)
        {
            var opc = parameters?.CustomParams<OrbitalPayloadCalculatorParameters>();
            if (opc != null && opc.uiScaleAuto)
                opc.ApplyAutoUiScale();
            return true;
        }

        public override bool Interactible(MemberInfo member, GameParameters parameters)
        {
            var opc = parameters?.CustomParams<OrbitalPayloadCalculatorParameters>();
            if (member.Name == "uiScalePercent" && opc != null && opc.uiScaleAuto)
                return false;
            return true;
        }

        public override IList ValidValues(MemberInfo member)
        {
            if (member.Name != "hotkeyKey")
                return null;

            var keys = new List<string> { "None" };
            for (char letter = 'A'; letter <= 'Z'; letter++)
                keys.Add(letter.ToString());
            return keys;
        }
    }
}
