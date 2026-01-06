using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using WindowsInput.Native;
using BarRaider.SdTools;
using BarRaider.SdTools.Events;
using BarRaider.SdTools.Wrappers;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SCJMapper_V2.SC;

// ReSharper disable StringLiteralTypo

namespace starcitizen.Buttons
{

    [PluginActionId("com.mhwlng.starcitizen.static")]
    public class ActionKey : StarCitizenKeypadBase
    {
        protected class PluginSettings
        {
            public static PluginSettings CreateDefaultSettings()
            {
                var instance = new PluginSettings
                {
                    Function = string.Empty,
                };

                return instance;
            }

            [JsonProperty(PropertyName = "function")]
            public string Function { get; set; }

            [FilenameProperty]
            [JsonProperty(PropertyName = "clickSound")]
            public string ClickSoundFilename { get; set; }

        }


        PluginSettings settings;
        private CachedSound _clickSound = null;


        public ActionKey(SDConnection connection, InitialPayload payload) : base(connection, payload)
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

            // Subscribe to Property Inspector events
            Connection.OnPropertyInspectorDidAppear += Connection_OnPropertyInspectorDidAppear;
            Connection.OnSendToPlugin += Connection_OnSendToPlugin;

            // Subscribe to key bindings loaded event
            Program.KeyBindingsLoaded += OnKeyBindingsLoaded;

            // Send functions data immediately if PI is already open
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

            var action = Program.dpReader.GetBinding(settings.Function);
            if (action != null)
            {
                Logger.Instance.LogMessage(TracingLevel.INFO, CommandTools.ConvertKeyString(action.Keyboard));

                StreamDeckCommon.SendKeypressDown(CommandTools.ConvertKeyString(action.Keyboard));
            }

            if (_clickSound != null)
            {
                try
                {
                    AudioPlaybackEngine.Instance.PlaySound(_clickSound);
                }
                catch (Exception ex)
                {
                    Logger.Instance.LogMessage(TracingLevel.FATAL, $"PlaySound: {ex}");
                }

            }

        }

        public override void KeyReleased(KeyPayload payload)
		{

            if (Program.dpReader == null)
            {
                StreamDeckCommon.ForceStop = true;
                return;
            }

            StreamDeckCommon.ForceStop = false;

            var action = Program.dpReader.GetBinding(settings.Function);
            if (action != null)
            {
                Logger.Instance.LogMessage(TracingLevel.INFO, CommandTools.ConvertKeyString(action.Keyboard));

                StreamDeckCommon.SendKeypressUp(CommandTools.ConvertKeyString(action.Keyboard));
            }

        }


        public override void ReceivedSettings(ReceivedSettingsPayload payload)
        {
            Logger.Instance.LogMessage(TracingLevel.INFO, $"ReceivedSettings - Function: {payload.Settings?["function"]?.ToString() ?? "null"}");

            // New in StreamDeck-Tools v2.0:
            BarRaider.SdTools.Tools.AutoPopulateSettings(settings, payload.Settings);
            
            Logger.Instance.LogMessage(TracingLevel.INFO, $"After AutoPopulateSettings - Function: {settings.Function ?? "null"}");
            
            HandleFileNames();
        }

        private void Connection_OnPropertyInspectorDidAppear(object sender, EventArgs e)
        {
            Logger.Instance.LogMessage(TracingLevel.INFO, "Property Inspector appeared, sending functions data");
            UpdatePropertyInspector();
        }

        private void Connection_OnSendToPlugin(object sender, EventArgs e)
        {
            // Check if the Property Inspector is sending a log message
            try
            {
                var payload = e.ExtractPayload();

                if (payload != null && payload.ContainsKey("jslog"))
                {
                    var logMessage = payload["jslog"]?.ToString();
                    Logger.Instance.LogMessage(TracingLevel.INFO, $"[JS-PI] {logMessage}");
                    return; // Handled, exit early
                }
            }
            catch (Exception ex)
            {
                Logger.Instance.LogMessage(TracingLevel.ERROR, $"Error processing jslog: {ex.Message}");
            }

            // Check if the Property Inspector is sending a connection message
            string propertyInspectorStatus = null;
            try
            {
                var payload = e.ExtractPayload();

                if (payload != null && payload.ContainsKey("property_inspector"))
                {
                    propertyInspectorStatus = payload["property_inspector"]?.ToString();
                }
            }
            catch
            {
                // Ignore parsing errors
            }

            if (propertyInspectorStatus == "propertyInspectorConnected")
            {
                Logger.Instance.LogMessage(TracingLevel.INFO, "Property Inspector connected message received, sending functions data");
                UpdatePropertyInspector();
            }
        }

        private void HandleFileNames()
        {
            _clickSound = null;
            if (File.Exists(settings.ClickSoundFilename))
            {
                try
                {
                    _clickSound = new CachedSound(settings.ClickSoundFilename);
                }
                catch (Exception ex)
                {
                    Logger.Instance.LogMessage(TracingLevel.FATAL, $"CachedSound: {settings.ClickSoundFilename} {ex}");

                    _clickSound = null;
                    settings.ClickSoundFilename = null;
                }
            }

            Connection.SetSettingsAsync(JObject.FromObject(settings)).Wait();
        }

        private void OnKeyBindingsLoaded(object sender, EventArgs e)
        {
            // Update Property Inspector when key bindings are loaded
            UpdatePropertyInspector();
        }

        public override void Dispose()
        {
            // Unsubscribe from events
            Connection.OnPropertyInspectorDidAppear -= Connection_OnPropertyInspectorDidAppear;
            Connection.OnSendToPlugin -= Connection_OnSendToPlugin;
            Program.KeyBindingsLoaded -= OnKeyBindingsLoaded;
            base.Dispose();
        }

        private void UpdatePropertyInspector()
        {
            try
            {
                if (Program.dpReader == null)
                {
                    Logger.Instance.LogMessage(TracingLevel.WARN, "dpReader is null, cannot update Property Inspector");
                    return;
                }

                // Build the functions data as JSON
                var functionsData = BuildFunctionsData();
                
                var payload = new JObject
                {
                    ["functionsLoaded"] = true,
                    ["functions"] = functionsData
                };

                Connection.SendToPropertyInspectorAsync(payload);
                Logger.Instance.LogMessage(TracingLevel.INFO, $"Sent {functionsData.Count} function groups to Property Inspector");
            }
            catch (Exception ex)
            {
                Logger.Instance.LogMessage(TracingLevel.ERROR, $"Failed to update Property Inspector: {ex.Message}");
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

                // Get all actions with any input binding
                var allActions = Program.dpReader.GetAllActions();
                var actionsWithBindings = allActions.Values
                    .Where(x => !string.IsNullOrWhiteSpace(x.Keyboard) ||
                                !string.IsNullOrWhiteSpace(x.Mouse) ||
                                !string.IsNullOrWhiteSpace(x.Joystick) ||
                                !string.IsNullOrWhiteSpace(x.Gamepad))
                    .ToList();

                // Group by MapUILabel
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
                        // Determine the primary input binding for display
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

                // Add unbound actions
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
