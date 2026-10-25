using System;
using System.Collections;
using System.Collections.Generic;
using MedusaMultiCrew.Core;
using UnityEngine;

namespace MedusaMultiCrew.Crew
{
    // Puts the local player in the game's own cockpit camera of an aircraft they don't fly,
    // with that aircraft's CombatHUD / FlightHud / StatusDisplay, and restores the previous
    // spectator camera and HUD on exit. Adapted from SimpleWSO's VanillaGunnerView (CC0,
    // Bongus/Motmir), including its fixes for vanilla teardown traps noted inline.
    internal class CrewView
    {
        private Unit restoreUnit;
        private CameraBaseState restoreState;
        private bool restoreMapMaximized;
        private Aircraft restoreHudAircraft;
        private WeaponStation restoreHudStation;
        private List<Unit> restoreHudTargetList;
        private bool restoreCombatHud;

        private Aircraft laserAircraft;
        private List<Unit> restoreLaserTargetList;
        private GameObject statusDisplay;
        private Aircraft statusDisplayAircraft;
        private Cockpit cockpit;
        private bool restoreCockpitEnabled;
        private GameObject tacScreen;

        public bool Active { get; private set; }

        public void Enter(Aircraft aircraft, WeaponStation station)
        {
            if (Active || aircraft == null)
            {
                return;
            }
            CameraStateManager csm = CameraStateManager.i;
            if (csm == null)
            {
                Plugin.Log.LogWarning("[View] No CameraStateManager; can't enter the crew view");
                return;
            }

            try
            {
                restoreUnit = csm.followingUnit;
                restoreState = csm.currentState;
                restoreMapMaximized = DynamicMap.mapMaximized;
                restoreCombatHud = IsFlyingOwnAircraftInCockpit(csm);

                // Bind CombatHUD before SetFollowingUnit so the spectator UnitDebug overlay sees
                // followed unit == CombatHUD.aircraft and stays hidden.
                BindCombatHud(aircraft, station);
                csm.SetFollowingUnit(aircraft);
                csm.SwitchState(csm.cockpitState);
                SetupCockpitUi(aircraft);

                Active = true;
                Plugin.Log.LogInfo($"[View] Entered crew cockpit view of {aircraft.unitName}");
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"[View] Enter failed: {e}");
            }
        }

        public void Exit()
        {
            if (!Active)
            {
                return;
            }
            Active = false;
            CameraStateManager csm = CameraStateManager.i;

            try
            {
                DestroyStatusDisplay();
                if (csm != null)
                {
                    bool restoreUnitValid = !IsGone(restoreUnit);
                    csm.SetFollowingUnit(restoreUnitValid ? restoreUnit : null);
                    csm.SwitchState(ResolveRestoreState(csm, restoreUnitValid));
                    ForceSpectatorUi(restoreMapMaximized);
                }
                RestoreCombatHud();
                Plugin.Log.LogInfo("[View] Left crew view; camera and HUD restored");
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"[View] Exit failed: {e}");
            }
            finally
            {
                DestroyStatusDisplay();
                RestoreLaserTargetList();
                restoreUnit = null;
                restoreState = null;
                restoreHudAircraft = null;
                restoreHudStation = null;
                restoreHudTargetList = null;
            }
        }

        // The cockpit camera got detached (map click, view cycling back from free cam): put it back.
        public void Reattach(Aircraft aircraft, WeaponStation station)
        {
            CameraStateManager csm = CameraStateManager.i;
            if (!Active || aircraft == null || csm == null)
            {
                return;
            }
            BindCombatHud(aircraft, station);
            csm.SetFollowingUnit(aircraft);
            csm.SwitchState(csm.cockpitState);
            SetupCockpitUi(aircraft);
        }

        // Rebind the HUD (station display, target list) after the seat's stations changed or the
        // camera came back to the cockpit from another view.
        public void Refresh(Aircraft aircraft, WeaponStation station)
        {
            if (!Active || aircraft == null)
            {
                return;
            }
            BindCombatHud(aircraft, station);
            SetupCockpitUi(aircraft);
        }

        private static bool IsFlyingOwnAircraftInCockpit(CameraStateManager csm)
        {
            return GameManager.GetLocalAircraft(out Aircraft local) && local != null
                && csm.followingUnit == local && csm.currentState == csm.cockpitState;
        }

        private CameraBaseState ResolveRestoreState(CameraStateManager csm, bool restoreUnitValid)
        {
            if (!restoreUnitValid || restoreState == null)
            {
                return csm.freeState;
            }
            // Don't drop back into map/unit selection mode, or into someone else's cockpit.
            if (restoreState == csm.selectionState || restoreState == csm.cockpitState)
            {
                return csm.orbitState;
            }
            return restoreState;
        }

        private static bool IsGone(Unit unit)
        {
            if (unit == null)
            {
                return false;
            }
            return unit is Aircraft aircraft && (aircraft.disabled || aircraft.HasEjected());
        }

        // ---- HUD binding ----

        private void BindCombatHud(Aircraft aircraft, WeaponStation station)
        {
            CombatHUD hud = CombatHUD.i;
            if (hud == null)
            {
                return;
            }

            if (restoreHudAircraft == null)
            {
                restoreHudAircraft = hud.aircraft;
                restoreHudStation = hud.GetWeaponStation();
                Reflect.TryGetField(hud, "targetList", out restoreHudTargetList);
            }

            if (hud.aircraft != aircraft)
            {
                if (statusDisplayAircraft != null && statusDisplayAircraft != aircraft)
                {
                    DestroyStatusDisplay();
                }
                CleanupHudAircraft(hud.aircraft);
                if (hud.aircraft != null)
                {
                    hud.RemoveAircraft();
                }
                hud.SetAircraft(aircraft);
            }

            if (station != null)
            {
                hud.ShowWeaponStation(station);
            }
            // ShowWeaponStation toggles HUD filters (A2A/A2G); push them now instead of after
            // HUDOptions' 1 s debounce.
            HUDOptions.i?.ApplyHUDSettings();
            TargetListSelector.i?.SetFilters();

            BindCrewTargetList(hud, aircraft);
            HideUnitDebug();
        }

        // The HUD (and the laser designator) select into the crew's own list, never the pilot's.
        private void BindCrewTargetList(CombatHUD hud, Aircraft aircraft)
        {
            Reflect.SetField(hud, "targetList", CrewSession.TargetList);
            if (laserAircraft == aircraft)
            {
                return;
            }
            RestoreLaserTargetList();
            laserAircraft = aircraft;
            LaserDesignator designator = aircraft.GetLaserDesignator();
            if (designator != null)
            {
                Reflect.TryGetField(designator, "targetList", out restoreLaserTargetList);
                Reflect.SetField(designator, "targetList", CrewSession.TargetList);
            }
        }

        private void RestoreLaserTargetList()
        {
            if (laserAircraft != null && restoreLaserTargetList != null)
            {
                LaserDesignator designator = laserAircraft.GetLaserDesignator();
                if (designator != null)
                {
                    Reflect.SetField(designator, "targetList", restoreLaserTargetList);
                }
            }
            laserAircraft = null;
            restoreLaserTargetList = null;
        }

        private void RestoreCombatHud()
        {
            CombatHUD hud = CombatHUD.i;
            if (hud == null)
            {
                return;
            }
            CleanupHudAircraft(hud.aircraft);
            if (hud.aircraft != null)
            {
                hud.RemoveAircraft();
            }

            if (restoreCombatHud && restoreHudAircraft != null)
            {
                hud.SetAircraft(restoreHudAircraft);
                hud.ShowWeaponStation(restoreHudStation);
                if (restoreHudTargetList != null)
                {
                    Reflect.SetField(hud, "targetList", restoreHudTargetList);
                }
                FlightHud flightHud = FlightHud.i;
                if (flightHud != null)
                {
                    flightHud.SetAircraft(restoreHudAircraft);
                    FlightHud.EnableCanvas(true);
                }
            }
            else
            {
                FlightHud.EnableCanvas(false);
            }
        }

        // CombatHUD.RemoveAircraft cleans up onJam, but ThreatList subscribes missile-warning
        // callbacks and owns looping missile alarms that would otherwise keep playing.
        public static void CleanupHudAircraft(Aircraft aircraft)
        {
            CombatHUD hud = CombatHUD.i;
            if (aircraft == null || hud == null)
            {
                return;
            }
            try
            {
                if (!Reflect.TryGetField(hud, "threatList", out ThreatList threatList) || threatList == null)
                {
                    return;
                }
                try
                {
                    Reflect.Call(threatList, "ThreatList_OnAircraftDisable", aircraft);
                }
                catch (Exception)
                {
                    if (Reflect.TryGetField(threatList, "alarmTypes", out Array alarms) && alarms != null)
                    {
                        foreach (object alarm in alarms)
                        {
                            if (alarm != null)
                            {
                                Reflect.TryCall(alarm, "Remove");
                            }
                        }
                    }
                }

                if (Reflect.TryGetField(threatList, "itemLookup", out IDictionary items) && items != null)
                {
                    foreach (DictionaryEntry entry in items)
                    {
                        if (entry.Value is Component component)
                        {
                            UnityEngine.Object.Destroy(component.gameObject);
                        }
                        else if (entry.Value is GameObject gameObject)
                        {
                            UnityEngine.Object.Destroy(gameObject);
                        }
                    }
                    items.Clear();
                }

                MissileWarning warning = aircraft.GetMissileWarningSystem();
                if (warning != null)
                {
                    Reflect.RemoveEventHandler(warning, "remove_onMissileWarning", threatList, "ThreatList_OnMissileWarning");
                    Reflect.RemoveEventHandler(warning, "remove_offMissileWarning", threatList, "ThreatList_OffMissileWarning");
                }
                Reflect.RemoveEventHandler(aircraft, "remove_onDisableUnit", threatList, "ThreatList_OnAircraftDisable");
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"[View] Threat list cleanup failed: {e.GetType().Name}: {e.Message}");
            }
        }

        // The spectator follow overlay (g-force, coordinates, ...). Vanilla hides it when the
        // followed unit is CombatHUD.aircraft, but SetFollowingUnit may have shown it already.
        private static void HideUnitDebug()
        {
            UnitDebug debug = UnityEngine.Object.FindObjectOfType<UnitDebug>();
            if (debug != null)
            {
                debug.enabled = false;
                debug.gameObject.SetActive(false);
            }
        }

        // ---- cockpit UI (the UI-only slice of Aircraft.SetupLocalPlayerAndUI) ----

        private void SetupCockpitUi(Aircraft aircraft)
        {
            if (statusDisplayAircraft != aircraft)
            {
                DestroyStatusDisplay();
                SpawnStatusDisplay(aircraft);
                SpawnCockpitScreens(aircraft);
                // Set even if the aircraft has no StatusDisplay, so a refresh doesn't rebuild the screens.
                statusDisplayAircraft = aircraft;
            }

            FlightHud flightHud = FlightHud.i;
            if (flightHud != null)
            {
                flightHud.SetAircraft(aircraft);
            }
            FlightHud.EnableCanvas(true);

            try
            {
                DynamicMap.AllowedToOpen = true;
                if (DynamicMap.i != null)
                {
                    DynamicMap.i.Minimize();
                }
                else
                {
                    DynamicMap.EnableCanvas(true);
                }
                CursorManager.SetFlag(CursorFlags.Map, false);
                CursorManager.Refresh();
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"[View] Cockpit UI reset failed: {e.GetType().Name}: {e.Message}");
            }

            RefreshChargeIndicators();
            ResetCameraEffects();
        }

        private void SpawnStatusDisplay(Aircraft aircraft)
        {
            AircraftParameters parameters = aircraft.GetAircraftParameters();
            if (parameters == null || parameters.StatusDisplay == null)
            {
                return;
            }
            statusDisplay = UnityEngine.Object.Instantiate(parameters.StatusDisplay, Vector3.zero, Quaternion.identity);
            StatusDisplay display = statusDisplay.GetComponent<StatusDisplay>();
            if (display != null)
            {
                display.Initialize(aircraft);
            }
            statusDisplayAircraft = aircraft;
        }

        // StatusDisplay.Initialize subscribes aircraft.onDisableUnit and never unsubscribes
        // (vanilla expects the display to live until the aircraft dies). Destroying ours without
        // removing that handler leaves a dead delegate that throws inside Unit.UnitDisabled,
        // which aborts vanilla teardown so the airframe never despawns.
        // Cockpit only builds its tactical screen (map/radar MFD, with the target-cam and landing
        // displays) at spawn, and only for the aircraft the local HUD is bound to, i.e. on the
        // pilot's game. Build it for the crew seat too, and let the stick/throttle animate.
        // TacScreen.OnDestroy unsubscribes everything it hooks, and it destroys itself if the
        // aircraft is disabled, so it doesn't leave vanilla teardown traps.
        private void SpawnCockpitScreens(Aircraft aircraft)
        {
            cockpit = aircraft.GetComponentInChildren<Cockpit>(true);
            if (cockpit == null)
            {
                return;
            }
            restoreCockpitEnabled = cockpit.enabled;
            cockpit.enabled = true;

            if (Reflect.TryGetField(cockpit, "tacScreen", out TacScreen existing) && existing != null)
            {
                return;
            }
            if (aircraft.targetCam == null)
            {
                Plugin.Log.LogInfo("[View] No targeting camera on this client; skipping the cockpit tactical screen");
                return;
            }
            if (!Reflect.TryGetField(cockpit, "tacScreenUIPrefab", out GameObject prefab) || prefab == null)
            {
                return;
            }
            try
            {
                tacScreen = UnityEngine.Object.Instantiate(prefab, cockpit.transform);
                tacScreen.GetComponent<TacScreen>().Initialize(aircraft, cockpit);
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"[View] Cockpit tactical screen failed: {e.GetType().Name}: {e.Message}");
                if (tacScreen != null)
                {
                    UnityEngine.Object.Destroy(tacScreen);
                    tacScreen = null;
                }
            }
        }

        private void DestroyCockpitScreens()
        {
            if (tacScreen != null)
            {
                UnityEngine.Object.Destroy(tacScreen);
            }
            tacScreen = null;
            if (cockpit != null)
            {
                cockpit.enabled = restoreCockpitEnabled;
            }
            cockpit = null;
        }

        private void DestroyStatusDisplay()
        {
            DestroyCockpitScreens();
            if (statusDisplay != null)
            {
                try
                {
                    StatusDisplay display = statusDisplay.GetComponent<StatusDisplay>();
                    if (display != null && statusDisplayAircraft != null)
                    {
                        Reflect.RemoveEventHandler(statusDisplayAircraft, "remove_onDisableUnit", display, "StatusDisplay_OnDisable");
                    }
                }
                catch (Exception e)
                {
                    Plugin.Log.LogWarning($"[View] StatusDisplay detach failed: {e.GetType().Name}: {e.Message}");
                }
                UnityEngine.Object.Destroy(statusDisplay);
                statusDisplay = null;
            }
            statusDisplayAircraft = null;
        }

        // Some airframe displays (capacitor/charge) subscribe through CombatHUD's aircraft
        // binding, which happened before the status display existed.
        private void RefreshChargeIndicators()
        {
            if (statusDisplay == null)
            {
                return;
            }
            foreach (ChargeIndicator indicator in statusDisplay.GetComponentsInChildren<ChargeIndicator>(true))
            {
                if (indicator != null)
                {
                    Reflect.TryCall(indicator, "ChargeIndicator_OnSetAircraft", CombatHUD.i);
                    Reflect.TryCall(indicator, "UpdateDisplay");
                }
            }
        }

        // Leftover G-LOC blackout from the spectator's previous life.
        private static void ResetCameraEffects()
        {
            try
            {
                foreach (GLOC gloc in UnityEngine.Object.FindObjectsOfType<GLOC>())
                {
                    gloc.ResetGLOC();
                }
                UnityEngine.UI.Image blackout = CameraStateManager.i != null ? CameraStateManager.i.GetBlackoutImage() : null;
                if (blackout != null)
                {
                    Color color = blackout.color;
                    color.a = 0f;
                    blackout.color = color;
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"[View] Camera effect reset failed: {e.GetType().Name}: {e.Message}");
            }
        }

        private static void ForceSpectatorUi(bool mapMaximized)
        {
            try
            {
                DynamicMap.AllowedToOpen = true;
                if (DynamicMap.i != null)
                {
                    DynamicMap.i.UnselectAll();
                    if (mapMaximized)
                    {
                        DynamicMap.i.Maximize();
                    }
                    else
                    {
                        DynamicMap.i.Minimize();
                    }
                }
                CursorManager.SetFlag(CursorFlags.SelectionMenu, false);
                if (!mapMaximized)
                {
                    CursorManager.SetFlag(CursorFlags.Map, false);
                }
                CursorManager.Refresh();
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"[View] Spectator UI reset failed: {e.GetType().Name}: {e.Message}");
            }
        }
    }
}
