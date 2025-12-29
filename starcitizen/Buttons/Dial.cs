using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using WindowsInput.Native;
using BarRaider.SdTools;
using BarRaider.SdTools.Events;
using BarRaider.SdTools.Payloads;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Threading;
using System.Diagnostics;

// ReSharper disable StringLiteralTypo

namespace starcitizen.Buttons
{

    [PluginActionId("com.mhwlng.starcitizen.dial")]
    public class Dial : StarCitizenDialBase
    {
        protected class PluginSettings
        {
            public static PluginSettings CreateDefaultSettings()
            {
                var instance = new PluginSettings
                {
                    FunctionCw = string.Empty,
                    FunctionCcw = string.Empty,
                    FunctionPress = string.Empty,
                    FunctionTouchLongPress = string.Empty,
                    FunctionTouchPress = string.Empty
                };

                return instance;
            }

            [JsonProperty(PropertyName = "functioncw")]
            public string FunctionCw { get; set; }

            [JsonProperty(PropertyName = "functionccw")]
            public string FunctionCcw { get; set; }

            [JsonProperty(PropertyName = "delay")]
            public string Delay { get; set; }

            [JsonProperty(PropertyName = "functionpress")]
            public string FunctionPress { get; set; }

            [JsonProperty(PropertyName = "functiontouchpress")]
            public string FunctionTouchPress { get; set; }

            [JsonProperty(PropertyName = "functiontouchlongpress")]
            public string FunctionTouchLongPress { get; set; }
        }

        PluginSettings settings;
        private int? _delay = null;

        private bool ccwIsDown;
        private bool cwIsDown;
        
        private int ccwPending;
        private int cwPending;

        private DateTime? lastDialTime = null;

        private Thread dialWatcherThread = null;
        private CancellationTokenSource cancellationTokenSource;

        public Dial(SDConnection connection, InitialPayload payload) : base(connection, payload)
        {
            if (payload.Settings == null || payload.Settings.Count == 0)
            {
                //Logger.Instance.LogMessage(TracingLevel.DEBUG, "Repeating Static Constructor #1");

                settings = PluginSettings.CreateDefaultSettings();
                Connection.SetSettingsAsync(JObject.FromObject(settings)).Wait();

            }
            else
            {
                //Logger.Instance.LogMessage(TracingLevel.DEBUG, "Repeating Static Constructor #2");

                settings = payload.Settings.ToObject<PluginSettings>();
                HandleFileNames();
            }

            cancellationTokenSource = new CancellationTokenSource();

            dialWatcherThread = new Thread(state =>
            {
                while (true)
                {
                    if (Program.dpReader == null)
                    {
                        StreamDeckCommon.ForceStop = true;
                        return;
                    }
                    else if (cancellationTokenSource.IsCancellationRequested)
                    {
                        return;
                    }

                    var timeDiff = DateTime.Now - (lastDialTime ?? DateTime.Now);

                    if ((ccwIsDown || cwIsDown) && timeDiff.TotalMilliseconds >= 100)
                    {
                        //Logger.Instance.LogMessage(TracingLevel.INFO, $"DialRotate Released");

                        ReleaseCcw();

                        ReleaseCw();

                        lastDialTime = DateTime.Now;

                    }

                    Thread.Sleep(100);

                }

            })
            {
                Name = "Dial Watcher",
                IsBackground = true
            };
            dialWatcherThread.Start();

            Connection.OnPropertyInspectorDidAppear += Connection_OnPropertyInspectorDidAppear;
            Connection.OnSendToPlugin += Connection_OnSendToPlugin;
            Program.KeyBindingsLoaded += OnKeyBindingsLoaded;

            UpdatePropertyInspector();
        }

        public override void Dispose()
        {
            if (cancellationTokenSource != null)
                cancellationTokenSource.Cancel();

            if (dialWatcherThread != null)
                dialWatcherThread.Join();

            Connection.OnPropertyInspectorDidAppear -= Connection_OnPropertyInspectorDidAppear;
            Connection.OnSendToPlugin -= Connection_OnSendToPlugin;
            Program.KeyBindingsLoaded -= OnKeyBindingsLoaded;

            base.Dispose();

            //Logger.Instance.LogMessage(TracingLevel.DEBUG, "Destructor called #1");

        }

        public override void TouchPress(TouchpadPressPayload payload)
        {
            if (Program.dpReader == null)
            {
                StreamDeckCommon.ForceStop = true;
                return;
            }

            StreamDeckCommon.ForceStop = false;

            if (payload.IsLongPress)
            {
                //Logger.Instance.LogMessage(TracingLevel.INFO, $"TouchPress: LongPress");

                var action = Program.dpReader.GetBinding(settings.FunctionTouchLongPress);
                if (action != null)
                {
                    Logger.Instance.LogMessage(TracingLevel.INFO, CommandTools.ConvertKeyString(action.Keyboard));

                    StreamDeckCommon.SendKeypress(CommandTools.ConvertKeyString(action.Keyboard), _delay ?? 40);
                }
            }
            else
            {
                //Logger.Instance.LogMessage(TracingLevel.INFO, $"TouchPress: Press");

                var action = Program.dpReader.GetBinding(settings.FunctionTouchPress);
                if (action != null)
                {
                    Logger.Instance.LogMessage(TracingLevel.INFO, CommandTools.ConvertKeyString(action.Keyboard));

                    StreamDeckCommon.SendKeypress(CommandTools.ConvertKeyString(action.Keyboard), _delay ?? 40);
                }
            }
        }
        public override void DialDown(DialPayload payload)
        {
            if (Program.dpReader == null)
            {
                StreamDeckCommon.ForceStop = true;
                return;
            }

            StreamDeckCommon.ForceStop = false;

            //Logger.Instance.LogMessage(TracingLevel.INFO, $"Dial Down");
            var action = Program.dpReader.GetBinding(settings.FunctionPress);
            if (action != null)
            {
                Logger.Instance.LogMessage(TracingLevel.INFO, CommandTools.ConvertKeyString(action.Keyboard));

                StreamDeckCommon.SendKeypressDown(CommandTools.ConvertKeyString(action.Keyboard));
            }
        }

        public override void DialUp(DialPayload payload)
        {

            if (Program.dpReader == null)
            {
                StreamDeckCommon.ForceStop = true;
                return;
            }

            StreamDeckCommon.ForceStop = false;

            //Logger.Instance.LogMessage(TracingLevel.INFO, $"Dial Up");
            var action = Program.dpReader.GetBinding(settings.FunctionPress);
            if (action != null)
            {
                Logger.Instance.LogMessage(TracingLevel.INFO, CommandTools.ConvertKeyString(action.Keyboard));

                StreamDeckCommon.SendKeypressUp(CommandTools.ConvertKeyString(action.Keyboard));
            }
        }

        private void ReleaseCw()
        {
            if (cwIsDown)
            {
                var action = Program.dpReader.GetBinding(settings.FunctionCw);
                if (action != null)
                {
                    Logger.Instance.LogMessage(TracingLevel.INFO, CommandTools.ConvertKeyString(action.Keyboard));

                    StreamDeckCommon.SendKeypressUp(CommandTools.ConvertKeyString(action.Keyboard));
                    Thread.Sleep(100);
                    cwIsDown = false;
                    cwPending = 0;
                }
            }
        }

        private void ReleaseCcw()
        {
            if (ccwIsDown)
            {
                var action = Program.dpReader.GetBinding(settings.FunctionCcw);
                if (action != null)
                {
                    Logger.Instance.LogMessage(TracingLevel.INFO, CommandTools.ConvertKeyString(action.Keyboard));

                    StreamDeckCommon.SendKeypressUp(CommandTools.ConvertKeyString(action.Keyboard));
                    Thread.Sleep(100);
                    ccwIsDown = false;
                    ccwPending = 0;
                }
            }
        }
        public override void DialRotate(DialRotatePayload payload)
        {

            if (Program.dpReader == null)
            {
                StreamDeckCommon.ForceStop = true;
                return;
            }

            StreamDeckCommon.ForceStop = false;

            lastDialTime = DateTime.Now;

            if (payload.Ticks > 0)
            {
                ReleaseCcw();

                //Logger.Instance.LogMessage(TracingLevel.INFO, $"DialRotate CW: {payload.Ticks}");

                if (!cwIsDown)
                {
                    var action = Program.dpReader.GetBinding(settings.FunctionCw);
                    if (action != null)
                    {
                        Logger.Instance.LogMessage(TracingLevel.INFO,
                            CommandTools.ConvertKeyString(action.Keyboard));

                        StreamDeckCommon.SendKeypressDown(CommandTools.ConvertKeyString(action.Keyboard));
                        cwIsDown = true;
                        cwPending += payload.Ticks;
                    }
                }
            }
            else if (payload.Ticks < 0)
            {
                ReleaseCw();

                //Logger.Instance.LogMessage(TracingLevel.INFO, $"DialRotate CCW: {payload.Ticks}");

                if (!ccwIsDown)
                {
                    var action = Program.dpReader.GetBinding(settings.FunctionCcw);
                    if (action != null)
                    {
                   
                        Logger.Instance.LogMessage(TracingLevel.INFO,
                            CommandTools.ConvertKeyString(action.Keyboard));

                        StreamDeckCommon.SendKeypressDown(CommandTools.ConvertKeyString(action.Keyboard));
                        ccwIsDown = true;
                        ccwPending += -payload.Ticks;
                    }
                }
            }

        }


 
        public override void ReceivedSettings(ReceivedSettingsPayload payload)
        {
            //Logger.Instance.LogMessage(TracingLevel.DEBUG, "ReceivedSettings");

            // New in StreamDeck-Tools v2.0:
            BarRaider.SdTools.Tools.AutoPopulateSettings(settings, payload.Settings);
            HandleFileNames();
        }

        private void HandleFileNames()
        {
            _delay = null;

            if (!string.IsNullOrEmpty(settings.Delay))
            {
                var ok = int.TryParse(settings.Delay, out var delay);
                if (ok && (delay > 0))
                {
                    _delay = delay;
                }
            }

            Connection.SetSettingsAsync(JObject.FromObject(settings)).Wait();
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
                        string bindingType = "";

                        if (!string.IsNullOrWhiteSpace(action.Keyboard))
                        {
                            var keyString = CommandTools.ConvertKeyStringToLocale(action.Keyboard, culture.Name);
                            primaryBinding = keyString
                                .Replace("Dik", "")
                                .Replace("}{", "+")
                                .Replace("}", "")
                                .Replace("{", "");
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

                        ((JArray)groupObj["options"]).Add(new JObject
                        {
                            ["value"] = action.Name,
                            ["text"] = $"{action.UILabel}{bindingDisplay}{overruleIndicator}",
                            ["bindingType"] = bindingType,
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
                        ((JArray)unboundGroup["options"]).Add(new JObject
                        {
                            ["value"] = action.Value.Name,
                            ["text"] = $"{action.Value.UILabel} (unbound)",
                            ["bindingType"] = "unbound",
                            ["searchText"] = $"{action.Value.UILabel.ToLower()} {action.Value.UIDescription?.ToLower() ?? ""}"
                        });
                    }

                    result.Add(unboundGroup);
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
