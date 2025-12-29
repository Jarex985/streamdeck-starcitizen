using System;
using System.Globalization;
using System.IO;
using System.Linq;
using BarRaider.SdTools;
using BarRaider.SdTools.Events;
using BarRaider.SdTools.Wrappers;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace starcitizen.Buttons
{
    [PluginActionId("com.mhwlng.starcitizen.dualaction")]
    public class DualAction : StarCitizenKeypadBase
    {
        protected class PluginSettings
        {
            public static PluginSettings CreateDefaultSettings()
            {
                return new PluginSettings
                {
                    DownFunction = string.Empty,
                    UpFunction = string.Empty
                };
            }

            [JsonProperty(PropertyName = "downFunction")]
            public string DownFunction { get; set; }

            [JsonProperty(PropertyName = "upFunction")]
            public string UpFunction { get; set; }

            [FilenameProperty]
            [JsonProperty(PropertyName = "clickSound")]
            public string ClickSoundFilename { get; set; }
        }

        private PluginSettings settings;
        private CachedSound _clickSound;

        public DualAction(SDConnection connection, InitialPayload payload)
            : base(connection, payload)
        {
            if (payload.Settings == null || payload.Settings.Count == 0)
            {
                settings = PluginSettings.CreateDefaultSettings();
                Connection.SetSettingsAsync(JObject.FromObject(settings)).Wait();
            }
            else
            {
                settings = payload.Settings.ToObject<PluginSettings>();
                LoadClickSound();
            }

            Connection.OnPropertyInspectorDidAppear += Connection_OnPropertyInspectorDidAppear;
            Connection.OnSendToPlugin += Connection_OnSendToPlugin;
            Program.KeyBindingsLoaded += OnKeyBindingsLoaded;

            UpdatePropertyInspector();
        }

        public override void KeyPressed(KeyPayload payload)
        {
            if (Program.dpReader == null)
            {
                StreamDeckCommon.ForceStop = true;
                return;
            }

            StreamDeckCommon.ForceStop = false;

            SendDownAction();
            _ = Connection.SetStateAsync(1);
            PlayClickSound();
        }

        public override void KeyReleased(KeyPayload payload)
        {
            if (Program.dpReader == null)
            {
                StreamDeckCommon.ForceStop = true;
                return;
            }

            StreamDeckCommon.ForceStop = false;

            SendUpAction();
            _ = Connection.SetStateAsync(0);
        }

        private void SendDownAction()
        {
            var action = Program.dpReader.GetBinding(settings.DownFunction);
            if (action == null)
            {
                return;
            }

            StreamDeckCommon.SendKeypressDown(CommandTools.ConvertKeyString(action.Keyboard));
        }

        private void SendUpAction()
        {
            var downAction = Program.dpReader.GetBinding(settings.DownFunction);
            if (downAction != null)
            {
                StreamDeckCommon.SendKeypressUp(CommandTools.ConvertKeyString(downAction.Keyboard));
            }

            var upAction = Program.dpReader.GetBinding(settings.UpFunction);

            if (upAction == null || settings.UpFunction == settings.DownFunction)
            {
                return;
            }

            StreamDeckCommon.SendKeypress(CommandTools.ConvertKeyString(upAction.Keyboard), 40);
        }

        public override void ReceivedSettings(ReceivedSettingsPayload payload)
        {
            Tools.AutoPopulateSettings(settings, payload.Settings);
            LoadClickSound();
        }

        private void Connection_OnPropertyInspectorDidAppear(object sender, EventArgs e)
        {
            UpdatePropertyInspector();
        }

        private void Connection_OnSendToPlugin(object sender, EventArgs e)
        {
            var payload = e.ExtractPayload();

            if (payload?["property_inspector"]?.ToString() == "propertyInspectorConnected")
            {
                UpdatePropertyInspector();
            }
        }

        private void OnKeyBindingsLoaded(object sender, EventArgs e)
        {
            UpdatePropertyInspector();
        }

        private void UpdatePropertyInspector()
        {
            if (Program.dpReader == null)
            {
                return;
            }

            Connection.SendToPropertyInspectorAsync(new JObject
            {
                ["functionsLoaded"] = true,
                ["functions"] = BuildFunctionsData()
            });
        }

        private void LoadClickSound()
        {
            _clickSound = null;

            if (!string.IsNullOrEmpty(settings.ClickSoundFilename) &&
                File.Exists(settings.ClickSoundFilename))
            {
                try
                {
                    _clickSound = new CachedSound(settings.ClickSoundFilename);
                }
                catch
                {
                    settings.ClickSoundFilename = null;
                }
            }
        }

        private void PlayClickSound()
        {
            if (_clickSound == null)
            {
                return;
            }

            try
            {
                AudioPlaybackEngine.Instance.PlaySound(_clickSound);
            }
            catch
            {
                // intentionally ignore
            }
        }

        private JArray BuildFunctionsData()
        {
            var result = new JArray();

            try
            {
                var keyboard = KeyboardLayouts.GetThreadKeyboardLayout();
                CultureInfo culture;

                try { culture = new CultureInfo(keyboard.KeyboardId); }
                catch { culture = new CultureInfo("en-US"); }

                var actions = Program.dpReader.GetAllActions().Values
                    .Where(x =>
                        !string.IsNullOrWhiteSpace(x.Keyboard) ||
                        !string.IsNullOrWhiteSpace(x.Mouse) ||
                        !string.IsNullOrWhiteSpace(x.Joystick) ||
                        !string.IsNullOrWhiteSpace(x.Gamepad))
                    .OrderBy(x => x.MapUILabel)
                    .GroupBy(x => x.MapUILabel);

                foreach (var group in actions)
                {
                    var groupObj = new JObject
                    {
                        ["label"] = group.Key,
                        ["options"] = new JArray()
                    };

                    foreach (var action in group.OrderBy(x => x.MapUICategory).ThenBy(x => x.UILabel))
                    {
                        string primaryBinding = "";

                        if (!string.IsNullOrWhiteSpace(action.Keyboard))
                        {
                            var keyString = CommandTools.ConvertKeyStringToLocale(action.Keyboard, culture.Name);
                            primaryBinding = keyString
                                .Replace("Dik", "")
                                .Replace("}{", "+")
                                .Replace("{", "")
                                .Replace("}", "");
                        }
                        else if (!string.IsNullOrWhiteSpace(action.Mouse))
                        {
                            primaryBinding = action.Mouse;
                        }
                        else if (!string.IsNullOrWhiteSpace(action.Joystick))
                        {
                            primaryBinding = action.Joystick;
                        }
                        else if (!string.IsNullOrWhiteSpace(action.Gamepad))
                        {
                            primaryBinding = action.Gamepad;
                        }

                        ((JArray)groupObj["options"]).Add(new JObject
                        {
                            ["value"] = action.Name,
                            ["text"] = $"{action.UILabel}{(string.IsNullOrWhiteSpace(primaryBinding) ? "" : $" [{primaryBinding}]")}",
                            ["searchText"] =
                                $"{action.UILabel.ToLower()} " +
                                $"{action.UIDescription?.ToLower() ?? ""} " +
                                $"{primaryBinding.ToLower()}"
                        });
                    }

                    if (((JArray)groupObj["options"]).Count > 0)
                    {
                        result.Add(groupObj);
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Instance.LogMessage(TracingLevel.ERROR, ex.ToString());
            }

            return result;
        }

        public override void Dispose()
        {
            Connection.OnPropertyInspectorDidAppear -= Connection_OnPropertyInspectorDidAppear;
            Connection.OnSendToPlugin -= Connection_OnSendToPlugin;
            Program.KeyBindingsLoaded -= OnKeyBindingsLoaded;
            base.Dispose();
        }
    }
}
