using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BarRaider.SdTools;
using BarRaider.SdTools.Wrappers;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace starcitizen.Buttons
{
    [PluginActionId("com.mhwlng.starcitizen.actiondelay")]
    public class ActionDelay : StarCitizenKeypadBase
    {
        private enum DelayState
        {
            Idle,
            Pending,
            Confirm
        }

        protected class PluginSettings
        {
            public static PluginSettings CreateDefaultSettings()
            {
                return new PluginSettings
                {
                    Function = string.Empty,
                    ExecutionDelayMs = 800,
                    ConfirmationDurationMs = 500,
                    BlinkRateMs = 300,
                    HoldToCancel = true
                };
            }

            [JsonProperty(PropertyName = "function")]
            public string Function { get; set; }

            [JsonProperty(PropertyName = "executionDelayMs")]
            public int ExecutionDelayMs { get; set; }

            [JsonProperty(PropertyName = "confirmationDurationMs")]
            public int ConfirmationDurationMs { get; set; }

            [JsonProperty(PropertyName = "blinkRateMs")]
            public int BlinkRateMs { get; set; }

            [JsonProperty(PropertyName = "holdToCancel")]
            public bool HoldToCancel { get; set; }

            [FilenameProperty]
            [JsonProperty(PropertyName = "clickSound")]
            public string ClickSoundFilename { get; set; }
        }

        private const string TransparentPixel = "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMB/6XK5esAAAAASUVORK5CYII=";
        private const int MinExecutionDelay = 100;
        private const int MaxExecutionDelay = 5000;
        private const int MinConfirmationDuration = 100;
        private const int MaxConfirmationDuration = 3000;
        private const int MinBlinkRate = 100;
        private const int MaxBlinkRate = 1000;

        private readonly object stateLock = new object();

        private PluginSettings settings;
        private CachedSound clickSound;
        private DelayState currentState = DelayState.Idle;

        private CancellationTokenSource executionCts;
        private CancellationTokenSource confirmationCts;
        private Timer blinkTimer;
        private bool blinkVisible = true;

        public ActionDelay(SDConnection connection, InitialPayload payload) : base(connection, payload)
        {
            settings = PluginSettings.CreateDefaultSettings();

            if (payload.Settings != null && payload.Settings.Count > 0)
            {
                Tools.AutoPopulateSettings(settings, payload.Settings);
                ClampSettings();
                HandleFileNames();
            }
            else
            {
                Connection.SetSettingsAsync(JObject.FromObject(settings)).Wait();
            }

            Connection.OnPropertyInspectorDidAppear += Connection_OnPropertyInspectorDidAppear;
            Connection.OnSendToPlugin += Connection_OnSendToPlugin;
            Program.KeyBindingsLoaded += OnKeyBindingsLoaded;

            UpdatePropertyInspector();
        }

        public override void KeyPressed(KeyPayload payload)
        {
            if (currentState != DelayState.Pending || !settings.HoldToCancel)
            {
                return;
            }

            ResetToIdle();
        }

        public override void KeyReleased(KeyPayload payload)
        {
            if (currentState != DelayState.Idle)
            {
                return;
            }

            if (Program.dpReader == null)
            {
                StreamDeckCommon.ForceStop = true;
                return;
            }

            StreamDeckCommon.ForceStop = false;

            if (payload?.Settings != null)
            {
                Tools.AutoPopulateSettings(settings, payload.Settings);
                ClampSettings();
            }

            if (string.IsNullOrWhiteSpace(settings.Function))
            {
                return;
            }

            BeginPending();
        }

        public override void ReceivedSettings(ReceivedSettingsPayload payload)
        {
            if (payload.Settings != null)
            {
                Tools.AutoPopulateSettings(settings, payload.Settings);
                ClampSettings();
                HandleFileNames();
            }

            ResetToIdle();
        }

        public override void Dispose()
        {
            ResetToIdle(false);
            Connection.OnPropertyInspectorDidAppear -= Connection_OnPropertyInspectorDidAppear;
            Connection.OnSendToPlugin -= Connection_OnSendToPlugin;
            Program.KeyBindingsLoaded -= OnKeyBindingsLoaded;
            base.Dispose();
        }

        private void BeginPending()
        {
            lock (stateLock)
            {
                currentState = DelayState.Pending;
            }

            _ = Connection.SetStateAsync(0);
            StartBlinking();
            StartExecutionTimer();
        }

        private void StartExecutionTimer()
        {
            CancelExecutionTimer();

            executionCts = new CancellationTokenSource();
            var token = executionCts.Token;

            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(settings.ExecutionDelayMs, token);
                }
                catch (TaskCanceledException)
                {
                    return;
                }

                if (token.IsCancellationRequested)
                {
                    return;
                }

                await ExecuteActionAsync();
            });
        }

        private async Task ExecuteActionAsync()
        {
            StopBlinking();

            if (Program.dpReader == null)
            {
                StreamDeckCommon.ForceStop = true;
                ResetToIdle();
                return;
            }

            StreamDeckCommon.ForceStop = false;

            var action = Program.dpReader.GetBinding(settings.Function);
            if (action == null || string.IsNullOrWhiteSpace(action.Keyboard))
            {
                ResetToIdle();
                return;
            }

            var keyInfo = CommandTools.ConvertKeyString(action.Keyboard);
            if (string.IsNullOrWhiteSpace(keyInfo))
            {
                ResetToIdle();
                return;
            }

            try
            {
                StreamDeckCommon.SendKeypress(keyInfo, 40);
            }
            catch (Exception ex)
            {
                Logger.Instance.LogMessage(TracingLevel.ERROR, $"Failed to send delayed keypress: {ex}");
                ResetToIdle();
                return;
            }

            PlayClickSound();

            lock (stateLock)
            {
                currentState = DelayState.Confirm;
            }

            await Connection.SetStateAsync(1);
            StartConfirmationTimer();
        }

        private void StartConfirmationTimer()
        {
            CancelConfirmationTimer();

            confirmationCts = new CancellationTokenSource();
            var token = confirmationCts.Token;

            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(settings.ConfirmationDurationMs, token);
                }
                catch (TaskCanceledException)
                {
                    return;
                }

                if (token.IsCancellationRequested)
                {
                    return;
                }

                lock (stateLock)
                {
                    currentState = DelayState.Idle;
                }

                await Connection.SetStateAsync(0);
            });
        }

        private void StartBlinking()
        {
            StopBlinking();
            blinkVisible = false;

            blinkTimer = new Timer(async _ => await ToggleBlinkAsync(), null, 0, settings.BlinkRateMs);
        }

        private async Task ToggleBlinkAsync()
        {
            if (currentState != DelayState.Pending)
            {
                StopBlinking();
                return;
            }

            if (blinkVisible)
            {
                blinkVisible = false;
                await Connection.SetImageAsync(TransparentPixel);
            }
            else
            {
                blinkVisible = true;
                await Connection.SetStateAsync(0);
            }
        }

        private void StopBlinking()
        {
            blinkTimer?.Dispose();
            blinkTimer = null;
            blinkVisible = true;

            _ = Connection.SetStateAsync(0);
        }

        private void CancelExecutionTimer()
        {
            if (executionCts != null)
            {
                executionCts.Cancel();
                executionCts.Dispose();
                executionCts = null;
            }
        }

        private void CancelConfirmationTimer()
        {
            if (confirmationCts != null)
            {
                confirmationCts.Cancel();
                confirmationCts.Dispose();
                confirmationCts = null;
            }
        }

        private void ResetToIdle(bool resetImage = true)
        {
            CancelExecutionTimer();
            CancelConfirmationTimer();
            StopBlinking();

            lock (stateLock)
            {
                currentState = DelayState.Idle;
            }

            if (resetImage)
            {
                _ = Connection.SetStateAsync(0);
            }
        }

        private void HandleFileNames()
        {
            clickSound = null;

            if (!string.IsNullOrEmpty(settings.ClickSoundFilename) && File.Exists(settings.ClickSoundFilename))
            {
                try
                {
                    clickSound = new CachedSound(settings.ClickSoundFilename);
                }
                catch (Exception ex)
                {
                    Logger.Instance.LogMessage(TracingLevel.ERROR, $"CachedSound: {settings.ClickSoundFilename} {ex}");
                    clickSound = null;
                    settings.ClickSoundFilename = null;
                }
            }

            Connection.SetSettingsAsync(JObject.FromObject(settings)).Wait();
        }

        private void PlayClickSound()
        {
            if (clickSound == null)
            {
                return;
            }

            try
            {
                AudioPlaybackEngine.Instance.PlaySound(clickSound);
            }
            catch (Exception ex)
            {
                Logger.Instance.LogMessage(TracingLevel.ERROR, $"PlaySound: {ex}");
            }
        }

        private void ClampSettings()
        {
            settings.ExecutionDelayMs = Clamp(settings.ExecutionDelayMs, MinExecutionDelay, MaxExecutionDelay);
            settings.ConfirmationDurationMs = Clamp(settings.ConfirmationDurationMs, MinConfirmationDuration, MaxConfirmationDuration);
            settings.BlinkRateMs = Clamp(settings.BlinkRateMs, MinBlinkRate, MaxBlinkRate);
        }

        private int Clamp(int value, int min, int max)
        {
            if (value < min)
            {
                return min;
            }

            if (value > max)
            {
                return max;
            }

            return value;
        }

        private void Connection_OnPropertyInspectorDidAppear(object sender, EventArgs e)
        {
            UpdatePropertyInspector();
        }

        private void Connection_OnSendToPlugin(object sender, EventArgs e)
        {
            try
            {
                var payload = e.ExtractPayload();

                if (payload != null && payload.ContainsKey("jslog"))
                {
                    Logger.Instance.LogMessage(TracingLevel.INFO, $"[JS-PI] {payload["jslog"]}");
                    return;
                }

                if (payload != null && payload.ContainsKey("property_inspector") &&
                    payload["property_inspector"].ToString() == "propertyInspectorConnected")
                {
                    UpdatePropertyInspector();
                }
            }
            catch
            {
                // ignore malformed payloads
            }
        }

        private void OnKeyBindingsLoaded(object sender, EventArgs e)
        {
            UpdatePropertyInspector();
        }

        private void UpdatePropertyInspector()
        {
            try
            {
                if (Program.dpReader == null)
                {
                    return;
                }

                var functionsData = BuildFunctionsData();

                var payload = new JObject
                {
                    ["functionsLoaded"] = true,
                    ["functions"] = functionsData
                };

                Connection.SendToPropertyInspectorAsync(payload);
            }
            catch (Exception ex)
            {
                Logger.Instance.LogMessage(TracingLevel.ERROR, $"Failed to update PI: {ex.Message}");
            }
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

                var allActions = Program.dpReader.GetAllActions();
                var actionsWithBindings = allActions.Values
                    .Where(x => !string.IsNullOrWhiteSpace(x.Keyboard) ||
                                !string.IsNullOrWhiteSpace(x.Mouse) ||
                                !string.IsNullOrWhiteSpace(x.Joystick) ||
                                !string.IsNullOrWhiteSpace(x.Gamepad))
                    .ToList();

                var groups = actionsWithBindings
                    .OrderBy(x => x.MapUILabel)
                    .GroupBy(x => x.MapUILabel);

                foreach (var group in groups)
                {
                    var groupObj = new JObject
                    {
                        ["label"] = group.Key,
                        ["options"] = new JArray()
                    };

                    var options = group
                        .OrderBy(x => x.MapUICategory)
                        .ThenBy(x => x.UILabel);

                    foreach (var action in options)
                    {
                        string primaryBinding = "";
                        string bindingType = "";

                        if (!string.IsNullOrWhiteSpace(action.Keyboard))
                        {
                            var keyString = CommandTools.ConvertKeyStringToLocale(action.Keyboard, culture.Name);
                            primaryBinding = keyString.Replace("Dik", "").Replace("}{", "+").Replace("}", "").Replace("{", "");
                            bindingType = "keyboard";
                        }
                        else if (!string.IsNullOrWhiteSpace(action.Mouse))
                        {
                            primaryBinding = action.Mouse;
                            bindingType = "mouse";
                        }
                        else if (!string.IsNullOrWhiteSpace(action.Joystick))
                        {
                            primaryBinding = action.Joystick;
                            bindingType = "joystick";
                        }
                        else if (!string.IsNullOrWhiteSpace(action.Gamepad))
                        {
                            primaryBinding = action.Gamepad;
                            bindingType = "gamepad";
                        }

                        string bindingDisplay = string.IsNullOrWhiteSpace(primaryBinding) ? "" : $" [{primaryBinding}]";
                        string overruleIndicator = action.KeyboardOverRule || action.MouseOverRule ? " *" : "";

                        var optionObj = new JObject
                        {
                            ["value"] = action.Name,
                            ["text"] = $"{action.UILabel}{bindingDisplay}{overruleIndicator}",
                            ["bindingType"] = bindingType,
                            ["searchText"] = $"{action.UILabel.ToLower()} {action.UIDescription?.ToLower() ?? ""} {primaryBinding.ToLower()}"
                        };

                        ((JArray)groupObj["options"]).Add(optionObj);
                    }

                    if (((JArray)groupObj["options"]).Count > 0)
                    {
                        result.Add(groupObj);
                    }
                }

                var unboundActions = Program.dpReader.GetUnboundActions();
                if (unboundActions.Any())
                {
                    var unboundGroup = new JObject
                    {
                        ["label"] = "Unbound Actions",
                        ["options"] = new JArray()
                    };

                    foreach (var action in unboundActions.OrderBy(x => x.Value.MapUILabel).ThenBy(x => x.Value.UILabel))
                    {
                        var optionObj = new JObject
                        {
                            ["value"] = action.Value.Name,
                            ["text"] = $"{action.Value.UILabel} (unbound)",
                            ["bindingType"] = "unbound",
                            ["searchText"] = $"{action.Value.UILabel.ToLower()} {action.Value.UIDescription?.ToLower() ?? ""}"
                        };

                        ((JArray)unboundGroup["options"]).Add(optionObj);
                    }

                    result.Add(unboundGroup);
                }
            }
            catch (Exception ex)
            {
                Logger.Instance.LogMessage(TracingLevel.ERROR, $"BuildFunctionsData error: {ex.Message}");
            }

            return result;
        }
    }
}
