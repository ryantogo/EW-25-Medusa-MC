using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using MedusaMultiCrew.Recon;
using UnityEngine;

namespace MedusaMultiCrew
{
    [BepInPlugin(Guid, Name, Version)]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "com.ryantogo.medusamulticrew";
        public const string Name = "EW-25 Medusa Multi-Crew";
        public const string Version = "0.1.0";

        internal static ManualLogSource Log;

        internal static ConfigEntry<string> AircraftFilter;
        internal static ConfigEntry<bool> ReconLogging;
        internal static ConfigEntry<KeyboardShortcut> ReconDumpKey;

        private Harmony harmony;

        private void Awake()
        {
            Log = Logger;

            AircraftFilter = Config.Bind("General", "AircraftFilter", "Medusa",
                "Comma-separated text matched against the aircraft's unit name or JSON key. Multi-crew only applies to matching aircraft.");
            ReconLogging = Config.Bind("Debug", "ReconLogging", true,
                "Log the seat, weapon station and camera layout of matching aircraft when their weapons load.");
            ReconDumpKey = Config.Bind("Debug", "ReconDumpKey", new KeyboardShortcut(KeyCode.F10),
                "Dump the layout of the aircraft you are currently flying to the BepInEx log.");

            harmony = new Harmony(Guid);
            harmony.PatchAll(typeof(Plugin).Assembly);

            Log.LogInfo($"{Name} {Version} loaded");
        }

        private void Update()
        {
            if (ReconDumpKey.Value.IsDown())
            {
                Aircraft aircraft = GameManager.GetLocalAircraft(out Aircraft local) ? local : null;
                if (aircraft == null)
                {
                    Log.LogInfo("Recon dump: you are not flying an aircraft");
                    return;
                }
                MedusaRecon.Dump(aircraft, "hotkey");
            }
        }

        private void OnDestroy()
        {
            harmony?.UnpatchSelf();
        }

        internal static bool IsMultiCrewAircraft(Aircraft aircraft)
        {
            if (aircraft == null || aircraft.definition == null)
            {
                return false;
            }
            string unitName = aircraft.definition.unitName ?? string.Empty;
            string jsonKey = aircraft.definition.jsonKey ?? string.Empty;
            foreach (string raw in AircraftFilter.Value.Split(','))
            {
                string token = raw.Trim();
                if (token.Length == 0)
                {
                    continue;
                }
                if (unitName.IndexOf(token, System.StringComparison.OrdinalIgnoreCase) >= 0
                    || jsonKey.IndexOf(token, System.StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }
            return false;
        }
    }
}
