using System;
using System.Collections.Generic;
using System.Configuration;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Serialization;
using BarRaider.SdTools;
using p4ktest.SC;
using SCJMapper_V2.SC;

namespace starcitizen
{
    public class KeyBindingWatcher : FileSystemWatcher
    {
        public event EventHandler KeyBindingUpdated;

        protected KeyBindingWatcher()
        {

        }

        public KeyBindingWatcher(string path, string fileName)
        {
            Filter = fileName;
            NotifyFilter = NotifyFilters.CreationTime | NotifyFilters.LastWrite;
            Path = path;
        }

        public virtual void StartWatching()
        {
            if (EnableRaisingEvents)
            {
                return;
            }

            Changed -= UpdateStatus;
            Changed += UpdateStatus;

            EnableRaisingEvents = true;
        }

        public virtual void StopWatching()
        {
            try
            {
                if (EnableRaisingEvents)
                {
                    Changed -= UpdateStatus;

                    EnableRaisingEvents = false;
                }
            }
            catch (Exception e)
            {
                Trace.TraceError($"Error while stopping Status watcher: {e.Message}");
                Trace.TraceInformation(e.StackTrace);
            }
        }

        protected void UpdateStatus(object sender, FileSystemEventArgs e)
        {
            Thread.Sleep(50);

            KeyBindingUpdated?.Invoke(this, EventArgs.Empty);
        }


    }
    class Program
    {
        public static FifoExecution keywatcherjob = new FifoExecution();

        public static KeyBindingWatcher KeyBindingWatcher;

        public static DProfileReader dpReader = new DProfileReader(); 

        public static string profile;

        private static bool enableCsvExport;

        // Event to notify buttons when key bindings are loaded
        public static event EventHandler KeyBindingsLoaded;

        public static void HandleKeyBindingEvents(object sender, object evt)
        {
            Logger.Instance.LogMessage(TracingLevel.INFO, $"Reloading Key Bindings");

            keywatcherjob.QueueUserWorkItem(GetKeyBindings, null);
        }


        private static void GetKeyBindings(Object threadContext)
        {
            if (KeyBindingWatcher != null)
            {
                KeyBindingWatcher.StopWatching();
                KeyBindingWatcher.Dispose();
                KeyBindingWatcher = null;
            }

            try
            {
                Logger.Instance.LogMessage(TracingLevel.INFO, "Loading Star Citizen key bindings...");

                // load stuff
                var actionmaps = SCDefaultProfile.ActionMaps();

                dpReader = new DProfileReader();

                dpReader.fromXML(profile);

                if (!string.IsNullOrEmpty(actionmaps))
                {
                    dpReader.fromActionProfile(actionmaps);
                }

                dpReader.Actions();

                dpReader.CreateCsv(enableCsvExport);

                string profilePath = SCPath.SCClientProfilePath;
                if (!string.IsNullOrEmpty(profilePath) && Directory.Exists(profilePath))
                {
                    Logger.Instance.LogMessage(TracingLevel.INFO, $"Monitoring key binding file at: {profilePath}\\actionmaps.xml");
                    KeyBindingWatcher = new KeyBindingWatcher(profilePath, "actionmaps.xml");
                    KeyBindingWatcher.KeyBindingUpdated += HandleKeyBindingEvents;
                    KeyBindingWatcher.StartWatching();
                }
                else
                {
                    Logger.Instance.LogMessage(TracingLevel.WARN, "Could not find profile directory to monitor for changes");
                }

                // Notify all buttons that key bindings are loaded
                KeyBindingsLoaded?.Invoke(null, EventArgs.Empty);
                Logger.Instance.LogMessage(TracingLevel.INFO, "Key bindings loaded - notifying buttons");
            }
            catch (Exception ex)
            {
                Logger.Instance.LogMessage(TracingLevel.ERROR, $"Error loading key bindings: {ex.Message}");
            }
        }


        static void Main(string[] args)
        {
            Logger.Instance.LogMessage(TracingLevel.INFO, "Init Star Citizen");

            try
            {
                LoadConfiguration();

                SCFiles.Instance.UpdatePack(); // update game files

                profile = SCDefaultProfile.DefaultProfile();

                GetKeyBindings(null);

            }
            catch (Exception ex)
            {
                Logger.Instance.LogMessage(TracingLevel.FATAL, $"DProfileReader: {ex}");
            }

            Logger.Instance.LogMessage(TracingLevel.INFO, "Finished Init Star Citizen");
            
            //var langFiles = SCFiles.Instance.LangFiles;
            //var langFile = SCFiles.Instance.LangFile(langFiles[0]);
            //var txt = SCUiText.Instance.Text("@ui_COMiningThrottle", "???");

            // Write the string array to a new file named "WriteLines.txt".


            // Uncomment this line of code to allow for debugging
            //while (!System.Diagnostics.Debugger.IsAttached) { System.Threading.Thread.Sleep(100); }

            SDWrapper.Run(args);


        }

        private static void LoadConfiguration()
        {
            try
            {
                var csvSetting = ConfigurationManager.AppSettings["EnableCsvExport"];

                if (bool.TryParse(csvSetting, out bool parsedSetting))
                {
                    enableCsvExport = parsedSetting;
                }
                else
                {
                    enableCsvExport = false;
                }

                Logger.Instance.LogMessage(TracingLevel.INFO, $"CSV export setting: {(enableCsvExport ? "enabled" : "disabled")}");
            }
            catch (Exception ex)
            {
                enableCsvExport = false;
                Logger.Instance.LogMessage(TracingLevel.WARN, $"Could not read CSV export setting, defaulting to disabled. {ex.Message}");
            }
        }
    }
}
