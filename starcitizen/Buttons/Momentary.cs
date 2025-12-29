using System;
using System.IO;
using System.Linq;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using BarRaider.SdTools;
using BarRaider.SdTools.Events;
using BarRaider.SdTools.Wrappers;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace starcitizen.Buttons
{
    [PluginActionId("com.mhwlng.starcitizen.momentary")]
    public class Momentary : StarCitizenKeypadBase
    {
        protected class PluginSettings
        {
            public static PluginSettings CreateDefaultSettings()
            {
                return new PluginSettings
                {
                    Function = string.Empty
                };
            }

            [JsonProperty(PropertyName = "function")]
            public string Function { get; set; }

            [FilenameProperty]
            [JsonProperty(PropertyName = "clickSound")]
            public string ClickSoundFilename { get; set; }
        }

        private PluginSettings settings;
        private CachedSound _clickSound;
        private CancellationTokenSource resetToken;
        private int visualSequence;

        // 🔑 runtime-authoritative delay (updated via ReceivedSettings)
        private int currentDelay = 1000;

        public Momentary(SDConnection connection, InitialPayload payload)
            : base(connection, payload)
        {
            settings = PluginSettings.CreateDefaultSettings();

            if (payload.Settings != null)
            {
                Tools.AutoPopulateSettings(settings, payload.Settings);
                ParseDelay(payload.Settings);
            }

            Connection.OnPropertyInspectorDidAppear += Connection_OnPropertyInspectorDidAppear;
            Connection.OnSendToPlugin += Connection_OnSendToPlugin;
            Program.KeyBindingsLoaded += OnKeyBindingsLoaded;

            LoadClickSound();
            UpdatePropertyInspector();
        }

        // ================= KEY EVENTS =================

        public override void KeyPressed(KeyPayload payload)
        {
            if (Program.dpReader == null) return;

            var action = Program.dpReader.GetBinding(settings.Function);
            if (action != null)
            {
                var keyString = CommandTools.ConvertKeyString(action.Keyboard);

                if (!string.IsNullOrEmpty(keyString))
                {
                    StreamDeckCommon.SendKeypressDown(keyString);
                }
                else
                {
                    Logger.Instance.LogMessage(TracingLevel.WARN, $"Momentary action '{settings.Function}' missing keyboard binding; skipping KeyPressed send.");
                }
            }

            PlayClickSound();
        }

        public override void KeyReleased(KeyPayload payload)
        {
            if (Program.dpReader == null) return;

            var action = Program.dpReader.GetBinding(settings.Function);
            if (action != null)
            {
                var keyString = CommandTools.ConvertKeyString(action.Keyboard);

                if (!string.IsNullOrEmpty(keyString))
                {
                    StreamDeckCommon.SendKeypressUp(keyString);
                }
                else
                {
                    Logger.Instance.LogMessage(TracingLevel.WARN, $"Momentary action '{settings.Function}' missing keyboard binding; skipping KeyReleased send.");
                }
            }

            // 🔑 ALWAYS prefer live payload value
            int delayToUse = currentDelay;

            if (payload?.Settings != null &&
                payload.Settings.TryGetValue("delay", out var delayToken) &&
                int.TryParse(delayToken.ToString(), out int liveDelay))
            {
                delayToUse = liveDelay;
                currentDelay = liveDelay; // keep cache in sync
            }

            TriggerMomentaryVisual(delayToUse);
        }

        // ================= MOMENTARY VISUAL =================

        private void TriggerMomentaryVisual(int delay)
        {
            resetToken?.Cancel();

            resetToken = new CancellationTokenSource();
            var token = resetToken.Token;

            // increment the visual sequence so older cycles cannot override newer ones
            var sequence = Interlocked.Increment(ref visualSequence);

            _ = RunMomentaryVisualAsync(delay, token, sequence);
        }

        private async Task RunMomentaryVisualAsync(int delay, CancellationToken token, int sequence)
        {
            // always ensure we start (or restart) at ACTIVE
            await Connection.SetStateAsync(1);

            try
            {
                await Task.Delay(Math.Max(0, delay), token);
            }
            catch (TaskCanceledException)
            {
                // swallowed; we still want the finally guard below
            }
            finally
            {
                // only the latest sequence is allowed to revert to idle
                if (sequence == visualSequence)
                {
                    await Connection.SetStateAsync(0); // BACK TO IDLE
                }
            }
        }

        // ================= SETTINGS =================

        public override void ReceivedSettings(ReceivedSettingsPayload payload)
        {
            if (payload.Settings != null)
            {
                Tools.AutoPopulateSettings(settings, payload.Settings);
                ParseDelay(payload.Settings);
            }

            LoadClickSound();
        }

        private void ParseDelay(JObject settingsObj)
        {
            if (settingsObj.TryGetValue("delay", out var delayToken) &&
                int.TryParse(delayToken.ToString(), out int parsed))
            {
                currentDelay = parsed;
            }
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
            if (_clickSound == null) return;

            try
            {
                AudioPlaybackEngine.Instance.PlaySound(_clickSound);
            }
            catch { }
        }

        // ================= PROPERTY INSPECTOR =================

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
            if (Program.dpReader == null) return;

            Connection.SendToPropertyInspectorAsync(new JObject
            {
                ["functionsLoaded"] = true,
                ["functions"] = BuildFunctionsData()
            });
        }

        // ================= FUNCTION LIST (STATIC-PARITY) =================

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
                        result.Add(groupObj);
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
            resetToken?.Cancel();
            Connection.OnPropertyInspectorDidAppear -= Connection_OnPropertyInspectorDidAppear;
            Connection.OnSendToPlugin -= Connection_OnSendToPlugin;
            Program.KeyBindingsLoaded -= OnKeyBindingsLoaded;
            base.Dispose();
        }
    }
}
