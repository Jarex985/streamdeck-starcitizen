using System;
using System.Globalization;
using System.IO;
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
    public class IncreaseDecreaseHold : StarCitizenKeypadBase
    {
        protected class PluginSettings
        {
            public static PluginSettings CreateDefaultSettings()
            {
                return new PluginSettings
                {
                    Function = string.Empty,
                    RepeatRate = 100,
                    IdleImage = string.Empty,
                    ActiveImage = string.Empty
                };
            }

            [JsonProperty(PropertyName = "function")]
            public string Function { get; set; }

            [JsonProperty(PropertyName = "repeatRate")]
            public int RepeatRate { get; set; }

            [FilenameProperty]
            [JsonProperty(PropertyName = "idleImage")]
            public string IdleImage { get; set; }

            [FilenameProperty]
            [JsonProperty(PropertyName = "activeImage")]
            public string ActiveImage { get; set; }

            [FilenameProperty]
            [JsonProperty(PropertyName = "startSound")]
            public string StartSoundFilename { get; set; }

            [FilenameProperty]
            [JsonProperty(PropertyName = "stopSound")]
            public string StopSoundFilename { get; set; }
        }

        private PluginSettings settings;
        private CachedSound _startSound;
        private CachedSound _stopSound;
        private CancellationTokenSource repeatToken;

        private int currentRepeatRate = 100;
        private string idleImageBase64;
        private string activeImageBase64;
        private bool isRepeating;

        public IncreaseDecreaseHold(SDConnection connection, InitialPayload payload)
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

            LoadAssets();

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

            if (payload?.Settings != null)
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

            _ = Connection.SetStateAsync(1);
            PlayStartSound();

            _ = Task.Run(() => RepeatWhileHeldAsync(keyInfo, currentRepeatRate, repeatToken.Token));
        }

        public override void KeyReleased(KeyPayload payload)
        {
            if (!isRepeating)
            {
                return;
            }

            StopRepeater();

            _ = Connection.SetStateAsync(0);
            PlayStopSound();
        }

        public override void ReceivedSettings(ReceivedSettingsPayload payload)
        {
            if (payload.Settings != null)
            {
                Tools.AutoPopulateSettings(settings, payload.Settings);
                ParseRepeatRate(payload.Settings);
            }

            LoadAssets();
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
            if (settingsObj.TryGetValue("repeatRate", out var rateToken) &&
                int.TryParse(rateToken.ToString(), out var parsedRate))
            {
                currentRepeatRate = Math.Max(1, parsedRate);
            }
        }

        private void LoadAssets()
        {
            LoadSounds();
            LoadImages();
        }

        private void LoadSounds()
        {
            _startSound = LoadSound(settings.StartSoundFilename);
            _stopSound = LoadSound(settings.StopSoundFilename);
        }

        private CachedSound LoadSound(string filename)
        {
            if (string.IsNullOrWhiteSpace(filename) || !File.Exists(filename))
            {
                return null;
            }

            try
            {
                return new CachedSound(filename);
            }
            catch
            {
                return null;
            }
        }

        private void LoadImages()
        {
            idleImageBase64 = ConvertFileToBase64(GetImagePath(settings.IdleImage, "Momentary0.png"));
            activeImageBase64 = ConvertFileToBase64(GetImagePath(settings.ActiveImage, "Momentary1.png"));

            _ = ApplyImagesAsync();
        }

        private string ConvertFileToBase64(string path)
        {
            try
            {
                if (string.IsNullOrEmpty(path) || !File.Exists(path))
                {
                    return null;
                }

                var bytes = File.ReadAllBytes(path);
                var extension = Path.GetExtension(path);
                if (extension == null)
                {
                    extension = "";
                }
                extension = extension.ToLowerInvariant();

                var mime = "image/png";
                if (extension == ".jpg" || extension == ".jpeg")
                {
                    mime = "image/jpeg";
                }
                else if (extension == ".gif")
                {
                    mime = "image/gif";
                }

                var base64 = Convert.ToBase64String(bytes);
                return "data:" + mime + ";base64," + base64;
            }
            catch (Exception ex)
            {
                Logger.Instance.LogMessage(TracingLevel.ERROR, $"Failed to load image {path}: {ex}");
                return null;
            }
        }

        private async Task ApplyImagesAsync()
        {
            try
            {
                if (!string.IsNullOrEmpty(idleImageBase64))
                {
                    await Connection.SetImageAsync(idleImageBase64, 0);
                }

                if (!string.IsNullOrEmpty(activeImageBase64))
                {
                    await Connection.SetImageAsync(activeImageBase64, 1);
                }
            }
            catch (Exception ex)
            {
                Logger.Instance.LogMessage(TracingLevel.ERROR, $"Failed to apply images: {ex}");
            }
        }

        private void PlayStartSound()
        {
            if (_startSound == null)
            {
                return;
            }

            try
            {
                AudioPlaybackEngine.Instance.PlaySound(_startSound);
            }
            catch (Exception ex)
            {
                Logger.Instance.LogMessage(TracingLevel.ERROR, $"Start sound failed: {ex}");
            }
        }

        private void PlayStopSound()
        {
            if (_stopSound == null)
            {
                return;
            }

            try
            {
                AudioPlaybackEngine.Instance.PlaySound(_stopSound);
            }
            catch (Exception ex)
            {
                Logger.Instance.LogMessage(TracingLevel.ERROR, $"Stop sound failed: {ex}");
            }
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

        private string GetImagePath(string configuredPath, string fallbackFileName)
        {
            if (!string.IsNullOrWhiteSpace(configuredPath) && File.Exists(configuredPath))
            {
                return configuredPath;
            }

            var baseDir = AppDomain.CurrentDomain.BaseDirectory;
            var defaultPath = Path.Combine(baseDir, "Images", fallbackFileName);

            return File.Exists(defaultPath) ? defaultPath : null;
        }
    }
}
