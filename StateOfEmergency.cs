/*
A mod that auto-activates/deactivates hazard pay and tax increase, and auto-opens/closes archer and ballista towers
during dragon or viking invasions.

Author: cmjten10 (https://steamcommunity.com/id/cmjten10/)
Mod Version: 1.2.2
Target K&C Version: 117r6s
Date: 2020-05-06
*/
using Assets;
using Harmony;
using I2.Loc;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using UnityEngine;
using Zat.Shared.ModMenu.API;

namespace StateOfEmergency
{
    public class StateOfEmergencyMod : MonoBehaviour 
    {
        private const string authorName = "cmjten10";
        private const string modName = "State of Emergency";
        private const string modNameNoSpace = "StateOfEmergency";
        private const string version = "v1.2.2";

        private static HarmonyInstance harmony;
        public static KCModHelper helper;
        public static ModSettingsProxy settingsProxy;

        // For accessing ChamberOfWarUI and private fields/methods
        private static ChamberOfWarUI chamberOfWarUI;
        private static Traverse chamberOfWarUITraverse;
        private static Traverse chamberOfWarUI_hazardPayToggle_m_IsOn;

        // Hazard pay
        private static int hazardPayState = 0;
        private static bool maximumTaxRateHigherThan30 = false;
        private static float maximumHazardPayTaxRate = 3f;
        private static Dictionary<int, float> taxRates = new Dictionary<int, float>();

        void Preload(KCModHelper __helper) 
        {
            helper = __helper;
            harmony = HarmonyInstance.Create($"{authorName}.{modNameNoSpace}");
            harmony.PatchAll(Assembly.GetExecutingAssembly());
        }

        void SceneLoaded(KCModHelper __helper)
        {
            chamberOfWarUI = GameUI.inst.chamberOfWarUI;
            chamberOfWarUITraverse = Traverse.Create(chamberOfWarUI);
            chamberOfWarUI_hazardPayToggle_m_IsOn = chamberOfWarUITraverse.Field("hazardPayToggle").Field("m_IsOn");

            maximumTaxRateHigherThan30 = HigherTaxesModExists(harmony);
            if (maximumTaxRateHigherThan30)
            {
                helper.Log("INFO: Higher Taxes mod found.");
            }
            else
            {
                helper.Log("INFO: Higher Taxes mod not found.");
            }

            if (!settingsProxy)
            {
                ModConfig config = ModConfigBuilder
                    .Create(modName, version, authorName)
                    .AddSlider("State of Emergency/Tax Rate", 
                        "Tax rate during invasions. May go beyond 30% if Higher Taxes mod is installed.", 
                        "30%", 0, 10, true, maximumHazardPayTaxRate * 2)
                    .Build();
                ModSettingsBootstrapper.Register(config, OnProxyRegistered, OnProxyRegisterError);
            }
        }

        // =====================================================================
        // Mod Menu Functions
        // =====================================================================

        private void OnProxyRegistered(ModSettingsProxy proxy, SettingsEntry[] saved)
        {
            try
            {
                settingsProxy = proxy;
                helper.Log("SUCCESS: Registered proxy for State of Emergency Mod Config");
                proxy.AddSettingsChangedListener("State of Emergency/Tax Rate", (setting) =>
                {
                    maximumHazardPayTaxRate = (float)setting.slider.value * 0.5f;
                    setting.slider.label = ((int)(maximumHazardPayTaxRate * 10)).ToString() + "%";
                    proxy.UpdateSetting(setting, null, null);
                });

                // Apply saved values.
                foreach (var setting in saved)
                {
                    var own = proxy.Config[setting.path];
                    if (own != null)
                    {
                        own.CopyFrom(setting);
                        proxy.UpdateSetting(own, null, null);
                    }
                }
            }
            catch (Exception ex)
            {
                helper.Log($"ERROR: Failed to register proxy for State of Emergency Mod config: {ex.Message}");
                helper.Log(ex.StackTrace);
            }
        }

        private void OnProxyRegisterError(Exception ex)
        {
            helper.Log($"ERROR: Failed to register proxy for State of Emergency Mod config: {ex.Message}");
            helper.Log($"{ex.StackTrace}");
        }

        // =====================================================================
        // Shared Utility Functions
        // =====================================================================

        private static bool HigherTaxesModExists(HarmonyInstance harmonyInstance)
        {
            try
            {
                // Check if HigherTaxes exists by looking for specific patches.
                var Player_IncreaseTaxRate = typeof(Player).GetMethod("IncreaseTaxRate");
                var Home_GetHappinessFromTax = typeof(Home).GetMethod("GetHappinessFromTax", 
                    BindingFlags.NonPublic | BindingFlags.Instance);
                
                bool Player_IncreaseTaxRate_patched = false;
                bool Home_GetHappinessFromTax_patched = false;

                // Check for patch that removes the 30% limit.
                var info = harmonyInstance.GetPatchInfo(Player_IncreaseTaxRate);
                if (info == null)
                {
                    return false;
                }
                foreach (var patch in info.Prefixes)
                {
                    if (patch.owner == "cmjten10.HigherTaxes")
                    {
                        Player_IncreaseTaxRate_patched = true;
                        break;
                    }
                }
                if (!Player_IncreaseTaxRate_patched)
                {
                    return false;
                }

                // Check for patch that decreases happiness further past 30%.
                info = harmonyInstance.GetPatchInfo(Home_GetHappinessFromTax);
                if (info == null)
                {
                    return false;
                }
                foreach (var patch in info.Postfixes)
                {
                    if (patch.owner == "cmjten10.HigherTaxes")
                    {
                        Home_GetHappinessFromTax_patched = true;
                        break;
                    }
                }
                return Home_GetHappinessFromTax_patched;
            }
            catch
            {
                return false;
            }
        }

        public static bool InvasionInProgress()
        {
            bool dragonAttack = DragonSpawn.inst.currentDragons.Count > 0;
            bool vikingAttack = RaiderSystem.inst.IsRaidInProgress();
            return dragonAttack || vikingAttack;
        }

        // =====================================================================
        // Auto-Activate/Deactivate Hazard Pay Utility Functions
        // =====================================================================

        private static void MaximizeTaxRates()
        {
            // Only set hazard pay tax rate to higher than 30% if possible.
            float hazardPayTaxRate = maximumHazardPayTaxRate;
            if (hazardPayTaxRate > 3f && !maximumTaxRateHigherThan30)
            {
                hazardPayTaxRate = 3f;
            }

            int landmassesCount = Player.inst.PlayerLandmassOwner.ownedLandMasses.Count;
            for (int i = 0; i < landmassesCount; i++)
            {
                int landmassId = Player.inst.PlayerLandmassOwner.ownedLandMasses.data[i];
                float landmassTaxRate = Player.inst.GetTaxRate(landmassId);
                if (landmassTaxRate < hazardPayTaxRate)
                {
                    taxRates[landmassId] = landmassTaxRate;
                    Player.inst.SetTaxRate(landmassId, hazardPayTaxRate);
                }
            }
        }

        private static void RestoreTaxRates()
        {
            foreach (KeyValuePair<int, float> entry in taxRates) 
            {
                int landmassId = entry.Key;
                float taxRate = entry.Value;
                Player.inst.SetTaxRate(landmassId, taxRate);
            }
            taxRates.Clear();
        }

        private static void ResetAutoHazardPay()
        {
            hazardPayState = 0;
            taxRates.Clear();
        }

        // =====================================================================
        // Patches
        // =====================================================================

        // ChamberOfWar::Update patch for engaging hazard pay auto-activation/deactivation system.
        [HarmonyPatch(typeof(ChamberOfWar))]
        [HarmonyPatch("Update")]
        public static class AutoHazardPayPatch 
        {
            static void Postfix(ChamberOfWar __instance) 
            {
                try 
                {
                    // Refer to ChamberOfWarUI::Update if hazard pay requirements change.
                    int goldNeeded = 50;
                    bool fullyStaffed = (double)__instance.b.GetWorkerPercent() > 0.95;
                    bool hasEnoughGold = World.GetLandmassOwner(__instance.b.LandMass()).Gold >= goldNeeded;
                    bool canActivate = fullyStaffed && hasEnoughGold;

                    switch(hazardPayState) 
                    {
                        case 0: 
                            // Activation state
                            // If an invasion starts and hazard pay is not activated, auto-activates it if the 
                            // requirements are met, then maximizes tax rates. Else if hazard pay is already activated, 
                            // prevents from auto-deactivation.
                            if (!Player.inst.hazardPay && InvasionInProgress() && canActivate) 
                            {
                                // Refer to: ChamberOfWarUI::OnHazardButtonToggled if hazard pay activation changes.
                                World.GetLandmassOwner(__instance.b.LandMass()).Gold -= goldNeeded;
                                SfxSystem.inst.PlayFromBank("ui_merchant_sellto", Camera.main.transform.position);
                                Player.inst.ChangeHazardPayActive(true, true);
                                chamberOfWarUI_hazardPayToggle_m_IsOn.SetValue(false);

                                // Crank up the tax rates to maximum in order to afford hazard pay. Save the previous 
                                // rates to restore them when the invasion is over.
                                MaximizeTaxRates();
                                hazardPayState = 1;
                            }
                            else if (Player.inst.hazardPay || Player.inst.hazardPayWarmup.Enabled) 
                            {
                                hazardPayState = 3;
                            }
                            break;
                        
                        case 1: 
                            // Activation warmup state
                            // Hazard pay must finish activation before auto-deactivation. 
                            if (!Player.inst.hazardPayWarmup.Enabled && Player.inst.hazardPay) 
                            {
                                chamberOfWarUI_hazardPayToggle_m_IsOn.SetValue(true);
                                hazardPayState = 2;
                            }
                            break;
                        
                        case 2: 
                            // Deactivation state
                            // If the invasion is over, deactivates an auto-activated hazard pay. Else if hazard pay is 
                            // deactivated during an invasion (manually or out of gold), prevents auto-activation until 
                            // the next invasion. Restores tax rates to original in both cases.
                            if (Player.inst.hazardPay && !InvasionInProgress()) 
                            {
                                Player.inst.ChangeHazardPayActive(false, false);
                                chamberOfWarUI_hazardPayToggle_m_IsOn.SetValue(false);
                                RestoreTaxRates();
                                hazardPayState = 0;
                            }
                            else if (!Player.inst.hazardPay) 
                            {
                                chamberOfWarUI_hazardPayToggle_m_IsOn.SetValue(false);
                                RestoreTaxRates();
                                hazardPayState = 3;
                            }
                            break;
                        
                        case 3: 
                            // Auto-activation/deactivation disabled state
                            // Waits out the current invasion before going back to the activation state. Goes to this 
                            // state when hazard pay is activated before, or deactivated during an invasion.
                            if (!InvasionInProgress()) 
                            {
                                hazardPayState = 0;
                            }
                            break;
                        
                        default:
                            break;
                    }
                }
                catch (Exception e)
                {
                    helper.Log("ERROR: Exception raised in AutoHazardPayPatch.");
                    helper.Log(e.ToString());
                }
            }
        }

        // Player::Reset patch for resetting mod state when loading a different game.
        [HarmonyPatch(typeof(Player))]
        [HarmonyPatch("Reset")]
        public static class ResetStateOfEmergency
        {
            static void Postfix(Player __instance) 
            {
                ResetAutoHazardPay();
            }
        }
    }
}
