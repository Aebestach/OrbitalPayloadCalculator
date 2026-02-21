using System;
using OrbitalPayloadCalculator.Services;
using OrbitalPayloadCalculator.Settings;
using OrbitalPayloadCalculator.UI;
using KSP.UI.Screens;
using UnityEngine;

namespace OrbitalPayloadCalculator
{
    [KSPAddon(KSPAddon.Startup.EditorAny, false)]
    public class OrbitalPayloadCalculatorEditorAddon : MonoBehaviour
    {
        private OrbitalPayloadCalculatorController _controller;

        private void Awake()
        {
            _controller = new OrbitalPayloadCalculatorController(true);
        }

        private void Start()
        {
            _controller.Start();
        }

        private void Update()
        {
            _controller.Update();
        }

        private void OnGUI()
        {
            _controller.OnGUI();
        }

        private void OnDestroy()
        {
            _controller.Dispose();
        }
    }

    [KSPAddon(KSPAddon.Startup.Flight, false)]
    public class OrbitalPayloadCalculatorFlightAddon : MonoBehaviour
    {
        private OrbitalPayloadCalculatorController _controller;

        private void Awake()
        {
            _controller = new OrbitalPayloadCalculatorController(false);
        }

        private void Start()
        {
            _controller.Start();
        }

        private void Update()
        {
            _controller.Update();
        }

        private void OnGUI()
        {
            _controller.OnGUI();
        }

        private void OnDestroy()
        {
            _controller.Dispose();
        }
    }

    internal sealed class OrbitalPayloadCalculatorController
    {
        private readonly bool _isEditor;
        private readonly PluginSettings _settings;
        private readonly VesselSourceService _vesselSourceService;
        private readonly CalculatorWindow _window;

        private ApplicationLauncherButton _button;
        private Texture2D _iconTexture;
        private bool _iconFromGameDatabase;
        private bool _buttonRegistered;
        private bool _eventsRegistered;

        public OrbitalPayloadCalculatorController(bool isEditor)
        {
            _isEditor = isEditor;
            _settings = PluginSettings.LoadOrDefault();
            _vesselSourceService = new VesselSourceService(isEditor);
            _window = new CalculatorWindow(_settings, _vesselSourceService, isEditor);
        }

        public void Start()
        {
            CreateIcon();
            GameEvents.onGUIApplicationLauncherReady.Add(OnAppLauncherReady);
            GameEvents.onGUIApplicationLauncherDestroyed.Add(OnAppLauncherDestroyed);
            GameEvents.onGUIApplicationLauncherUnreadifying.Add(OnAppLauncherUnreadifying);
            _eventsRegistered = true;
            TryRegisterAppLauncher();
        }

        public void Update()
        {
            if (ApplicationLauncher.Ready && !_buttonRegistered)
            {
                TryRegisterAppLauncher();
            }

            if (ShouldToggleWithHotkey())
            {
                _window.Visible = !_window.Visible;
                SyncButtonState();
            }
        }

        public void OnGUI()
        {
            _window.OnGUI();
        }

        public void Dispose()
        {
            if (_eventsRegistered)
            {
                GameEvents.onGUIApplicationLauncherReady.Remove(OnAppLauncherReady);
                GameEvents.onGUIApplicationLauncherDestroyed.Remove(OnAppLauncherDestroyed);
                GameEvents.onGUIApplicationLauncherUnreadifying.Remove(OnAppLauncherUnreadifying);
                _eventsRegistered = false;
            }

            UnregisterAppLauncher();
            _window.Dispose();

            if (_iconTexture != null && !_iconFromGameDatabase)
            {
                UnityEngine.Object.Destroy(_iconTexture);
            }

            _iconTexture = null;
            _iconFromGameDatabase = false;
        }

        private bool ShouldToggleWithHotkey()
        {
            if (_settings.HotkeyAlt && !(Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt)))
                return false;
            if (_settings.HotkeyCtrl && !(Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl)))
                return false;
            if (_settings.HotkeyShift && !(Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift)))
                return false;

            return _settings.HotkeyKey != KeyCode.None && Input.GetKeyDown(_settings.HotkeyKey);
        }

        private void OnAppLauncherReady()
        {
            TryRegisterAppLauncher();
        }

        private void OnAppLauncherDestroyed()
        {
            _button = null;
            _buttonRegistered = false;
        }

        private void OnAppLauncherUnreadifying(GameScenes scene)
        {
            UnregisterAppLauncher();
        }

        private void TryRegisterAppLauncher()
        {
            if (!ApplicationLauncher.Ready || _buttonRegistered || _iconTexture == null)
            {
                return;
            }

            var scenes = _isEditor
                ? ApplicationLauncher.AppScenes.SPH | ApplicationLauncher.AppScenes.VAB
                : ApplicationLauncher.AppScenes.FLIGHT;

            _button = ApplicationLauncher.Instance.AddModApplication(
                OnAppLauncherTrue,
                OnAppLauncherFalse,
                null,
                null,
                null,
                null,
                scenes,
                _iconTexture);

            _buttonRegistered = _button != null;
            SyncButtonState();
        }

        private void UnregisterAppLauncher()
        {
            if (_button != null)
            {
                try
                {
                    if (ApplicationLauncher.Instance != null)
                    {
                        ApplicationLauncher.Instance.RemoveModApplication(_button);
                    }
                }
                catch (Exception)
                {
                    // Swallow exceptions during scene transitions
                }
            }

            _button = null;
            _buttonRegistered = false;
        }

        private void OnAppLauncherTrue()
        {
            _window.Visible = true;
        }

        private void OnAppLauncherFalse()
        {
            _window.Visible = false;
        }

        private void SyncButtonState()
        {
            if (_button == null)
            {
                return;
            }

            if (_window.Visible)
            {
                _button.SetTrue(false);
            }
            else
            {
                _button.SetFalse(false);
            }
        }

        private void CreateIcon()
        {
            var texture = GameDatabase.Instance != null
                ? GameDatabase.Instance.GetTexture("OrbitalPayloadCalculator/Textures/icon", false)
                : null;

            if (texture != null)
            {
                _iconTexture = texture;
                _iconFromGameDatabase = true;
                _iconTexture.wrapMode = TextureWrapMode.Clamp;
                _iconTexture.filterMode = FilterMode.Bilinear;
                return;
            }

            _iconFromGameDatabase = false;
            _iconTexture = new Texture2D(38, 38, TextureFormat.ARGB32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };

            var background = new Color32(255, 255, 255, 255);
            var accent = new Color32(255, 255, 255, 255);
            var pixels = new Color32[38 * 38];

            for (var y = 0; y < 38; y++)
            {
                for (var x = 0; x < 38; x++)
                {
                    var idx = y * 38 + x;
                    var border = x < 2 || y < 2 || x > 35 || y > 35;
                    var diagonal = x == y || x + y == 37;
                    pixels[idx] = border || diagonal ? accent : background;
                }
            }

            _iconTexture.SetPixels32(pixels);
            _iconTexture.Apply(false, true);
        }
    }
}
