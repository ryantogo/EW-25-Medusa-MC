using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using MedusaMultiCrew.Core;
using MedusaMultiCrew.Crew;
using MedusaMultiCrew.Net;
using MedusaMultiCrew.Recon;
using UnityEngine;

namespace MedusaMultiCrew
{
    [BepInPlugin(Guid, Name, Version)]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "com.ryantogo.medusamulticrew";
        public const string Name = "EW-25 Medusa Multi-Crew";
        public const string Version = "0.2.0";

        internal static ManualLogSource Log;

        internal static ConfigEntry<string> AircraftFilter;
        internal static ConfigEntry<bool> AllowAiAircraft;
        internal static ConfigEntry<KeyCode> JoinLeaveKey;
        internal static ConfigEntry<KeyCode> AcceptKey;
        internal static ConfigEntry<KeyCode> DeclineKey;
        internal static ConfigEntry<bool> ReconLogging;
        internal static ConfigEntry<KeyCode> ReconDumpKey;

        private Harmony harmony;
        private bool loggedUpdateError;

        private void Awake()
        {
            Log = Logger;

            AircraftFilter = Config.Bind("General", "AircraftFilter", "Medusa",
                "Comma-separated text matched against the aircraft's unit name or JSON key. Multi-crew only applies to matching aircraft.");
            AllowAiAircraft = Config.Bind("General", "AllowAiAircraft", true,
                "Let players crew AI-flown aircraft (no pilot to ask). Host setting.");
            JoinLeaveKey = Config.Bind("Keys", "JoinLeaveKey", KeyCode.F6,
                "While spectating a friendly Medusa: ask to join as WSO. While seated: leave the seat.");
            AcceptKey = Config.Bind("Keys", "AcceptKey", KeyCode.F7,
                "Pilot: accept a crew join request.");
            DeclineKey = Config.Bind("Keys", "DeclineKey", KeyCode.F8,
                "Pilot: decline a crew join request.");
            ReconLogging = Config.Bind("Debug", "ReconLogging", true,
                "Log the seat, weapon station and camera layout of matching aircraft when their weapons load.");
            ReconDumpKey = Config.Bind("Debug", "ReconDumpKey", KeyCode.F10,
                "Dump the layout of the aircraft you are currently flying to the BepInEx log.");

            harmony = new Harmony(Guid);
            harmony.PatchAll(typeof(Plugin).Assembly);

            Log.LogInfo($"{Name} {Version} loaded");
        }

        private void Update()
        {
            try
            {
                CrewNet.Tick();
                CrewClient.Update();

                if (RewiredInput.GetKeyDown(ReconDumpKey.Value))
                {
                    if (GameManager.GetLocalAircraft(out Aircraft aircraft) && aircraft != null)
                    {
                        MedusaRecon.Dump(aircraft, "hotkey");
                    }
                    else
                    {
                        Log.LogInfo("Recon dump: you are not flying an aircraft");
                    }
                }
            }
            catch (System.Exception e)
            {
                if (!loggedUpdateError)
                {
                    loggedUpdateError = true;
                    Log.LogError($"Update failed (logged once): {e}");
                }
            }
        }

        private void OnDestroy()
        {
            CrewNet.ResetAll();
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
