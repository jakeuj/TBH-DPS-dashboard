using System;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using Il2CppInterop.Runtime.Injection;
using UnityEngine;

namespace TbhDpsMeter
{
    [BepInPlugin(Guid, Name, Version)]
    public class Plugin : BasePlugin
    {
        public const string Guid = "tbh.dpsmeter";
        public const string Name = "TBH DPS Meter";
        public const string Version = "0.6.1";

        public static DpsTracker Tracker;
        public static DamageTakenTracker TakenTracker;
        public static ManualLogSource Logger;

        /// <summary>Current wave index, written by the DPS overlay's stage poll, read by the taken panel.</summary>
        public static int CurrentWave;

        // config
        public static ConfigEntry<float> PosX;
        public static ConfigEntry<float> PosY;
        public static ConfigEntry<float> PanelWidth;
        public static ConfigEntry<float> Opacity;
        public static ConfigEntry<bool> StartVisible;
        public static ConfigEntry<int> FontSize;
        public static ConfigEntry<float> UIScale;
        public static ConfigEntry<float> WindowSeconds;
        public static ConfigEntry<bool> DebugDamage;
        public static ConfigEntry<bool> AutoCheckUpdate;
        private static ConfigEntry<string> _toggleKeyName;

        // damage-taken panel config
        public static ConfigEntry<float> TakenPosX;
        public static ConfigEntry<float> TakenPosY;
        public static ConfigEntry<float> TakenPanelWidth;
        public static ConfigEntry<bool> TakenStartVisible;
        private static ConfigEntry<string> _takenToggleKeyName;

        // stage-compare panel config
        public static ConfigEntry<float> ComparePosX;
        public static ConfigEntry<float> ComparePosY;
        public static ConfigEntry<float> ComparePanelWidth;
        public static ConfigEntry<bool> CompareStartVisible;
        private static ConfigEntry<string> _compareToggleKeyName;
        public static ConfigEntry<bool> DebugSnapshot;

        // farming-planner panel config
        public static ConfigEntry<float> FarmPosX;
        public static ConfigEntry<float> FarmPosY;
        public static ConfigEntry<float> FarmPanelWidth;
        public static ConfigEntry<bool> FarmStartVisible;
        private static ConfigEntry<string> _farmToggleKeyName;

        // box-log panel config
        public static ConfigEntry<float> BoxPosX;
        public static ConfigEntry<float> BoxPosY;
        public static ConfigEntry<float> BoxPanelWidth;
        public static ConfigEntry<bool> BoxStartVisible;
        private static ConfigEntry<string> _boxToggleKeyName;
        public static ConfigEntry<bool> BoxSoundEnabled;
        public static ConfigEntry<float> BoxSoundVolume;
        public static ConfigEntry<string> BoxSoundFile;

        public static KeyCode ToggleKey = KeyCode.F9;
        public static KeyCode TakenToggleKey = KeyCode.F10;
        public static KeyCode CompareToggleKey = KeyCode.F11;
        public static KeyCode FarmToggleKey = KeyCode.F6;
        public static KeyCode BoxToggleKey = KeyCode.F5;

        private static int _dbgCount;

        public override void Load()
        {
            Logger = Log;

            PosX = Config.Bind("UI", "PosX", -1f, "Overlay X (auto-saved when dragged). -1 = auto bottom-left.");
            PosY = Config.Bind("UI", "PosY", -1f, "Overlay Y (auto-saved when dragged). -1 = auto bottom-left.");
            PanelWidth = Config.Bind("UI", "PanelWidth", 300f, "Overlay panel width in pixels.");
            Opacity = Config.Bind("UI", "Opacity", 0.35f, "Background opacity 0..1 (PageUp/PageDown to adjust live).");
            StartVisible = Config.Bind("UI", "StartVisible", true, "Show the overlay on launch.");
            FontSize = Config.Bind("UI", "FontSize", 15, "Base font size.");
            UIScale = Config.Bind("UI", "UIScale", 1.0f, "Global overlay scale (0.6–1.5). Panels auto-shrink further if they'd exceed the screen. Adjust live with Ctrl+PageUp/PageDown or the DPS panel's −/+ control.");
            WindowSeconds = Config.Bind("Meter", "LiveWindowSeconds", 5f, "Sliding window length for the live DPS number.");
            _toggleKeyName = Config.Bind("General", "ToggleKey", "F9", "Key to show/hide the DPS overlay (UnityEngine.KeyCode name).");
            DebugDamage = Config.Bind("Debug", "LogDamageSamples", false, "Log the first damage hits to verify the hook is correct.");
            AutoCheckUpdate = Config.Bind("General", "AutoCheckUpdate", true, "On launch, check GitHub for a newer release and show a download prompt in the panel.");

            var lang = Config.Bind("General", "Language", "Auto",
                "UI language: Auto / zh-Hant / zh-Hans / en / ja / es.");
            Loc.Init(lang.Value);

            TakenPosX = Config.Bind("TakenUI", "PosX", -1f, "Damage-taken overlay X (auto-saved when dragged). -1 = auto.");
            TakenPosY = Config.Bind("TakenUI", "PosY", -1f, "Damage-taken overlay Y (auto-saved when dragged). -1 = auto.");
            TakenPanelWidth = Config.Bind("TakenUI", "PanelWidth", 300f, "Damage-taken overlay panel width in pixels.");
            TakenStartVisible = Config.Bind("TakenUI", "StartVisible", true, "Show the damage-taken overlay on launch.");
            _takenToggleKeyName = Config.Bind("TakenUI", "ToggleKey", "F10", "Key to show/hide the damage-taken overlay (UnityEngine.KeyCode name).");

            ComparePosX = Config.Bind("CompareUI", "PosX", -1f, "Stage-compare overlay X (auto-saved when dragged). -1 = auto.");
            ComparePosY = Config.Bind("CompareUI", "PosY", -1f, "Stage-compare overlay Y (auto-saved when dragged). -1 = auto.");
            ComparePanelWidth = Config.Bind("CompareUI", "PanelWidth", 380f, "Stage-compare overlay panel width in pixels.");
            CompareStartVisible = Config.Bind("CompareUI", "StartVisible", false, "Show the stage-compare overlay on launch.");
            _compareToggleKeyName = Config.Bind("CompareUI", "ToggleKey", "F11", "Key to show/hide the stage-compare overlay (UnityEngine.KeyCode name).");
            DebugSnapshot = Config.Bind("Debug", "LogSnapshot", false, "Log character-snapshot reflection diagnostics to verify obfuscated member picks.");

            FarmPosX = Config.Bind("FarmUI", "PosX", -1f, "Farming-planner overlay X (auto-saved when dragged). -1 = auto.");
            FarmPosY = Config.Bind("FarmUI", "PosY", -1f, "Farming-planner overlay Y (auto-saved when dragged). -1 = auto.");
            FarmPanelWidth = Config.Bind("FarmUI", "PanelWidth", 520f, "Farming-planner overlay panel width in pixels.");
            FarmStartVisible = Config.Bind("FarmUI", "StartVisible", false, "Show the farming-planner overlay on launch.");
            _farmToggleKeyName = Config.Bind("FarmUI", "ToggleKey", "F6", "Key to show/hide the farming-planner overlay (UnityEngine.KeyCode name).");

            BoxPosX = Config.Bind("BoxUI", "PosX", -1f, "Box-log overlay X (auto-saved when dragged). -1 = auto.");
            BoxPosY = Config.Bind("BoxUI", "PosY", -1f, "Box-log overlay Y (auto-saved when dragged). -1 = auto.");
            BoxPanelWidth = Config.Bind("BoxUI", "PanelWidth", 420f, "Box-log overlay panel width in pixels.");
            BoxStartVisible = Config.Bind("BoxUI", "StartVisible", false, "Show the box-log overlay on launch.");
            _boxToggleKeyName = Config.Bind("BoxUI", "ToggleKey", "F5", "Key to show/hide the box-log overlay (UnityEngine.KeyCode name).");
            BoxSoundEnabled = Config.Bind("BoxUI", "SoundEnabled", true, "Play a sound when a box is picked up.");
            BoxSoundVolume = Config.Bind("BoxUI", "SoundVolume", 0.6f, "Box pickup sound volume (0..1). Adjustable live in the F5 panel.");
            BoxSoundFile = Config.Bind("BoxUI", "SoundFile", "", "Optional path to a custom .wav to play on box pickup. Blank = built-in beep.");

            if (!Enum.TryParse(_toggleKeyName.Value, true, out ToggleKey))
                ToggleKey = KeyCode.F9;
            if (!Enum.TryParse(_takenToggleKeyName.Value, true, out TakenToggleKey))
                TakenToggleKey = KeyCode.F10;
            if (!Enum.TryParse(_compareToggleKeyName.Value, true, out CompareToggleKey))
                CompareToggleKey = KeyCode.F11;
            if (!Enum.TryParse(_farmToggleKeyName.Value, true, out FarmToggleKey))
                FarmToggleKey = KeyCode.F6;
            if (!Enum.TryParse(_boxToggleKeyName.Value, true, out BoxToggleKey))
                BoxToggleKey = KeyCode.F5;

            Tracker = new DpsTracker(WindowSeconds.Value);
            TakenTracker = new DamageTakenTracker(WindowSeconds.Value);

            var harmony = new Harmony(Guid);
            TryPatch(harmony, typeof(Monster_TakeDamage_Patch), "Monster.ebj damage hook");
            TryPatch(harmony, typeof(Hero_TakeDamage_Patch), "Hero.gnr damage-taken hook");
            TryPatch(harmony, typeof(StageState_Patch), "StageManager.set_stageState wave hook");
            StageProbe.TryHook(harmony);

            try
            {
                ClassInjector.RegisterTypeInIl2Cpp<OverlayBehaviour>();
                ClassInjector.RegisterTypeInIl2Cpp<TakenOverlayBehaviour>();
                ClassInjector.RegisterTypeInIl2Cpp<CompareOverlayBehaviour>();
                ClassInjector.RegisterTypeInIl2Cpp<FarmOverlayBehaviour>();
                ClassInjector.RegisterTypeInIl2Cpp<BoxOverlayBehaviour>();
                var go = new GameObject("TbhDpsOverlay");
                UnityEngine.Object.DontDestroyOnLoad(go);
                go.hideFlags = HideFlags.HideAndDontSave;
                go.AddComponent<OverlayBehaviour>();
                go.AddComponent<TakenOverlayBehaviour>();
                go.AddComponent<CompareOverlayBehaviour>();
                go.AddComponent<FarmOverlayBehaviour>();
                go.AddComponent<BoxOverlayBehaviour>();
                Logger.LogInfo("Overlays created. DPS " + ToggleKey + ", taken " + TakenToggleKey + ", compare " + CompareToggleKey + ", farm " + FarmToggleKey + ", box " + BoxToggleKey + ".");
            }
            catch (Exception ex)
            {
                Logger.LogError("Failed to create overlay: " + ex);
            }


            SelfCheck.Run();
            if (AutoCheckUpdate.Value) Updater.CheckAsync();
            Logger.LogInfo($"{Name} {Version} loaded.");
        }

        private static void TryPatch(Harmony harmony, Type patchClass, string label)
        {
            try { harmony.PatchAll(patchClass); Logger.LogInfo("Patched: " + label); }
            catch (Exception ex) { Logger.LogError("Patch FAILED (" + label + "): " + ex.Message); }
        }

        public static void LogDamageSample(float amount, bool crit, int type)
        {
            if (_dbgCount >= 25) return;
            _dbgCount++;
            Logger.LogInfo($"[dmg #{_dbgCount}] amount={amount:0.##} crit={crit} type={type}");
        }
    }
}
