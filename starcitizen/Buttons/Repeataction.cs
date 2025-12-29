using System;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BarRaider.SdTools;
using BarRaider.SdTools.Events;
using BarRaider.SdTools.Wrappers;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace starcitizen.Buttons
{
    [PluginActionId("com.mhwlng.starcitizen.holdrepeat")]
    public class Repeataction : StarCitizenKeypadBase
    {
        protected class PluginSettings
        {
            public static PluginSettings CreateDefaultSettings()
            {
                return new PluginSettings
                {
                    Function = string.Empty,
                    RepeatRate = 100
                };
            }

            [JsonProperty(PropertyName = "function")]
            public string Function { get; set; }

            [JsonProperty(PropertyName = "repeatRate")]
            public int RepeatRate { get; set; }
        }

        private PluginSettings settings;
        private CancellationTokenSource repeatToken;

        private int currentRepeatRate = 100;
        private bool isRepeating;

        public Repeataction(SDConnection connection, InitialPayload payload)
            : base(connection, payload)
        {
            settings = PluginSettings.CreateDefaultSettings();

            if (payload.Settings != null && payload.Settings.Count > 0)
            {
                Tools.AutoPopulateSettings(settings, payload.Settings);
                ParseRepeatRate(payload.Settings);
            }
            else
            {
                _ = Connection.SetSettingsAsync(JObject.FromObject(settings));
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

            if (payload != null && payload.Settings != null)
            {
                ParseRepeatRate(payload.Settings);
            }

            var action = Program.dpReader.GetBinding(settings.Function);
            if (action == null)
            {
                return;
            }

            var keyInfo = CommandTools.ConvertKeyString(action.Keyboard);
            if (string.IsNullOrWhiteSpace(keyInfo))
            {
                return;
            }

            StopRepeater();

            repeatToken = new CancellationTokenSource();
            isRepeating = true;

            // Switch to "active" state (state 1 image is managed by Stream Deck UI)
            _ = Connection.SetStateAsync(1);

            _ = Task.Run(() => RepeatWhileHeldAsync(keyInfo, currentRepeatRate, repeatToken.Token));
        }

        public override void KeyReleased(KeyPayload payload)
        {
            if (!isRepeating)
            {
                return;
            }

            StopRepeater();

            // Switch back to "idle" state (state 0 image is managed by Stream Deck UI)
            _ = Connection.SetStateAsync(0);
        }

        public override void ReceivedSettings(ReceivedSettingsPayload payload)
        {
            if (payload.Settings != null)
            {
                Tools.AutoPopulateSettings(settings, payload.Settings);
                ParseRepeatRate(payload.Settings);
            }
        }

        public override void Dispose()
        {
            repeatToken?.Cancel();
            Connection.OnPropertyInspectorDidAppear -= Connection_OnPropertyInspectorDidAppear;
            Connection.OnSendToPlugin -= Connection_OnSendToPlugin;
            Program.KeyBindingsLoaded -= OnKeyBindingsLoaded;
            base.Dispose();
        }

        private async Task RepeatWhileHeldAsync(string keyInfo, int repeatRate, CancellationToken token)
        {
            SendSingleKeypress(keyInfo);

            while (!token.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(Math.Max(1, repeatRate), token);
                }
                catch (TaskCanceledException)
                {
                    break;
                }

                if (token.IsCancellationRequested)
                {
                    break;
                }

                SendSingleKeypress(keyInfo);
            }
        }

        private void SendSingleKeypress(string keyInfo)
        {
            try
            {
                StreamDeckCommon.SendKeypress(keyInfo, 40);
            }
            catch (Exception ex)
            {
                Logger.Instance.LogMessage(TracingLevel.ERROR, $"Failed to send repeat keypress: {ex}");
            }
        }

        private void ParseRepeatRate(JObject settingsObj)
        {
            JToken rateToken;
            if (settingsObj != null &&
                settingsObj.TryGetValue("repeatRate", out rateToken) &&
                int.TryParse(rateToken.ToString(), out var parsedRate))
            {
                currentRepeatRate = Math.Max(1, parsedRate);
            }
            else
            {
                currentRepeatRate = Math.Max(1, settings.RepeatRate);
            }
        }

        private void StopRepeater()
        {
            if (repeatToken != null)
            {
                try
                {
                    repeatToken.Cancel();
                }
                catch { }

                repeatToken.Dispose();
                repeatToken = null;
            }

            isRepeating = false;
        }

        private void Connection_OnPropertyInspectorDidAppear(object sender, EventArgs e)
        {
            UpdatePropertyInspector();
        }

        private void Connection_OnSendToPlugin(object sender, EventArgs e)
        {
            var payload = e.ExtractPayload();

            if (payload != null && payload["property_inspector"] != null &&
                payload["property_inspector"].ToString() == "propertyInspectorConnected")
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

        private JArray BuildFunctionsData()
        {
            var result = new JArray();

            try
            {
                var keyboard = KeyboardLayouts.GetThreadKeyboardLayout();
                CultureInfo culture;

                try
                {
                    culture = new CultureInfo(keyboard.KeyboardId);
                }
                catch
                {
                    culture = new CultureInfo("en-US");
                }

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
                                $"{(action.UIDescription ?? "").ToLower()} " +
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
    }
}
