﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using CM3D2.MaidFiddler.Hook;
using CM3D2.MaidFiddler.Plugin.Gui;
using CM3D2.MaidFiddler.Plugin.Utils;
using ExIni;
using UnityEngine;
using UnityInjector;
using UnityInjector.Attributes;
using Application = System.Windows.Forms.Application;

namespace CM3D2.MaidFiddler.Plugin
{
    [PluginName("Maid Fiddler"), PluginVersion(VERSION)]
    public class MaidFiddler : PluginBase
    {
        public const string CONTRIBUTORS = "denikson";
        public const string VERSION = "BETA 0.8";
        public const string WIKI_PAGE = "https://github.com/denikson/CM3D2.MaidFiddler/wiki";
        public const uint SUPPORTED_PATCH_MAX = 1000;
        public const uint SUPPORTED_PATCH_MIN = 1000;
        private const bool DEFAULT_USE_JAPANESE_NAME_STYLE = false;
        private const MaidOrderDirection DEFAULT_ORDER_DIRECTION = Plugin.MaidOrderDirection.Ascending;
        private const string DEFAULT_LANGUAGE_FILE = "ENG";
        private static readonly KeyCode[] DEFAULT_KEY_CODE = {KeyCode.KeypadEnter, KeyCode.Keypad0};

        private static readonly MaidFiddlerGUI.MaidCompareMethod[] COMPARE_METHODS =
        {
            MaidFiddlerGUI.MaidCompareID,
            MaidFiddlerGUI.MaidCompareCreateTime,
            MaidFiddlerGUI.MaidCompareFirstLastName,
            MaidFiddlerGUI.MaidCompareLastFirstName,
            MaidFiddlerGUI.MaidCompareEmployedDay
        };

        private readonly MaidOrderStyle[] DEFAULT_ORDER_STYLES = {MaidOrderStyle.GUID};
        private KeyHelper keyCreateGUI;
        public static string DATA_PATH { get; private set; }
        public static MaidFiddlerGUI Gui { get; set; }
        public static Thread GuiThread { get; set; }
        public static MaidFiddlerGUI.MaidCompareMethod[] MaidCompareMethods { get; private set; }
        public static int MaidOrderDirection { get; private set; }

        public string SelectedDefaultLanguage
        {
            get
            {
                string result = Preferences["GUI"]["DefaultTranslation"].Value;
                if (result != null && (result = result.Trim()) != string.Empty && Translation.Exists(result))
                    return result;
                Preferences["GUI"]["DefaultTranslation"].Value = result = DEFAULT_LANGUAGE_FILE;
                SaveConfig();
                return result;
            }
            set
            {
                if (value != null && (value = value.Trim()) != string.Empty && Translation.Exists(value))
                    Preferences["GUI"]["DefaultTranslation"].Value = value;
                else
                    Preferences["GUI"]["DefaultTranslation"].Value = DEFAULT_LANGUAGE_FILE;
                SaveConfig();
            }
        }

        public static bool UseJapaneseNameStyle { get; private set; }

        public void Dispose()
        {
            Gui?.Dispose();
        }

        public void Awake()
        {
            if (!FiddlerUtils.CheckPatcherVersion())
            {
                Destroy(this);
                return;
            }
            DontDestroyOnLoad(this);

            DATA_PATH = DataPath;
            LoadConfig();

            FiddlerHooks.SaveLoadedEvent += OnSaveLoaded;

            Debugger.WriteLine(LogLevel.Info, "Creating the GUI thread");
            GuiThread = new Thread(LoadGUI);
            GuiThread.Start();

            Debugger.WriteLine($"MaidFiddler {VERSION} loaded!");
        }

        private static string EnumsToString<T>(IList<T> keys, char separator)
        {
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < keys.Count; i++)
            {
                sb.Append(EnumHelper.GetName(keys[i]));
                if (i != keys.Count - 1)
                    sb.Append(separator);
            }

            return sb.ToString();
        }

        public void LateUpdate()
        {
            Gui?.DoIfVisible(Gui.UpdateSelectedMaidValues);
            Gui?.DoIfVisible(Gui.UpdatePlayerValues);
        }

        private void LoadConfig()
        {
            Debugger.WriteLine(LogLevel.Info, "Loading launching key combination...");
            List<KeyCode> keys = new List<KeyCode>();
            IniKey value = Preferences["Keys"]["StartGUIKey"];
            if (value.Value == null || value.Value.Trim() == string.Empty)
            {
                value.Value = EnumsToString(DEFAULT_KEY_CODE, '+');
                keys.AddRange(DEFAULT_KEY_CODE);
                SaveConfig();
            }
            else
            {
                try
                {
                    string[] keyCodes = value.Value.Split(new[] {'+'}, StringSplitOptions.RemoveEmptyEntries);
                    if (keyCodes.Length == 0)
                        throw new Exception();
                    foreach (KeyCode kc in
                    keyCodes.Select(keyCode => (KeyCode) Enum.Parse(typeof (KeyCode), keyCode.Trim(), true))
                            .Where(kc => !keys.Contains(kc)))
                        keys.Add(kc);
                    if (keyCodes.Length != keys.Count)
                    {
                        value.Value = EnumsToString(keys, '+');
                        SaveConfig();
                    }
                }
                catch (Exception)
                {
                    Debugger.WriteLine(LogLevel.Warning, "Failed to parse given key combo. Using default combination");
                    value.Value = EnumsToString(DEFAULT_KEY_CODE, '+');
                    keys.AddRange(DEFAULT_KEY_CODE);
                    SaveConfig();
                }
            }
            keyCreateGUI = new KeyHelper(keys.ToArray());
            Debugger.WriteLine(LogLevel.Info, $"Loaded {keys.Count} long key combo: {EnumsToString(keys, '+')}");

            Debugger.WriteLine(LogLevel.Info, "Loading name style info...");
            value = Preferences["GUI"]["UseJapaneseNameStyle"];
            bool useJapNameStyle;
            if (value.Value == null || value.Value.Trim() == string.Empty
                || !bool.TryParse(value.Value, out useJapNameStyle))
            {
                Debugger.WriteLine(LogLevel.Warning, "Failed to get name style info. Setting do default...");
                value.Value = DEFAULT_USE_JAPANESE_NAME_STYLE.ToString();
                UseJapaneseNameStyle = DEFAULT_USE_JAPANESE_NAME_STYLE;
                SaveConfig();
            }
            else
                UseJapaneseNameStyle = useJapNameStyle;

            Debugger.WriteLine(LogLevel.Info, $"Using Japanese name style: {UseJapaneseNameStyle}");


            Debugger.WriteLine(LogLevel.Info, "Loading order style info...");
            value = Preferences["GUI"]["OrderStyle"];
            IEnumerable<MaidOrderStyle> orderStyles;
            string v;
            if (value.Value == null || (v = value.Value.Trim()) == string.Empty)
            {
                Debugger.WriteLine(LogLevel.Warning, "Failed to get order style. Setting do default...");
                value.Value = EnumsToString(DEFAULT_ORDER_STYLES, '|');
                orderStyles = DEFAULT_ORDER_STYLES;
                SaveConfig();
            }
            else
            {
                string[] vals = v.Split(new[] {'|'}, StringSplitOptions.RemoveEmptyEntries);
                try
                {
                    List<MaidOrderStyle> os = new List<MaidOrderStyle>();
                    foreach (MaidOrderStyle s in
                    vals.Select(val => (MaidOrderStyle) Enum.Parse(typeof (MaidOrderStyle), val.Trim(), true))
                        .Where(s => !os.Contains(s)))
                    {
                        os.Add(s);
                    }
                    orderStyles = os;
                }
                catch (Exception)
                {
                    Debugger.WriteLine(LogLevel.Warning, "Failed to get order style. Setting do default...");
                    value.Value = EnumsToString(DEFAULT_ORDER_STYLES, '|');
                    orderStyles = DEFAULT_ORDER_STYLES;
                    SaveConfig();
                }
            }

            MaidCompareMethods = orderStyles.Select(o => COMPARE_METHODS[(int) o]).ToArray();

            Debugger.WriteLine(
            LogLevel.Info,
            $"Sorting maids by method order {EnumsToString(orderStyles.ToList(), '>')}");


            Debugger.WriteLine(LogLevel.Info, "Loading order direction info...");
            value = Preferences["GUI"]["OrderDirection"];
            MaidOrderDirection orderDirection;
            if (value.Value == null || (v = value.Value.Trim()) == string.Empty
                || !EnumHelper.TryParse(v, out orderDirection, true))
            {
                Debugger.WriteLine(LogLevel.Warning, "Failed to get order direction. Setting do default...");
                value.Value = EnumHelper.GetName(DEFAULT_ORDER_DIRECTION);
                MaidOrderDirection = (int) DEFAULT_ORDER_DIRECTION;
                SaveConfig();
            }
            else
                MaidOrderDirection = (int) orderDirection;

            Debugger.WriteLine(
            LogLevel.Info,
            $"Sorting maids in {EnumHelper.GetName((MaidOrderDirection) MaidOrderDirection)} direction");
        }

        public void LoadGUI()
        {
            try
            {
                Application.SetCompatibleTextRenderingDefault(false);
                if (Gui == null)
                    Gui = new MaidFiddlerGUI(this);
                Application.Run(Gui);
            }
            catch (Exception e)
            {
                FiddlerUtils.ThrowErrorMessage(e, "Generic error");
            }
        }

        public void OnDestroy()
        {
            if (Gui == null)
                return;
            Debugger.WriteLine("Closing GUI...");
            Gui.Close(true);
            Gui = null;
            Debugger.WriteLine("GUI closed. Suspending the thread...");
            GuiThread.Abort();
            Debugger.WriteLine("Thread suspended");
        }

        public void OnSaveLoaded(int saveNo)
        {
            Debugger.WriteLine(LogLevel.Info, $"Level loading! Save no. {saveNo}");
            Gui?.DoIfVisible(Gui.ReloadMaids);
            Gui?.DoIfVisible(Gui.ReloadPlayer);
        }

        public void OpenGUI()
        {
            Gui?.Show();
        }

        public void Update()
        {
            keyCreateGUI.Update();

            if (keyCreateGUI.HasBeenPressed())
                OpenGUI();
            Gui?.DoIfVisible(Gui.UpdateMaids);
        }
    }

    public enum MaidOrderStyle
    {
        GUID = 0,
        CreationTime = 1,
        FirstName_LastName = 2,
        LastName_FirstName = 3,
        EmployedDay = 4
    }

    public enum MaidOrderDirection
    {
        Descending = -1,
        Ascending = 1
    }
}