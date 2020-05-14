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
        public static Traverse chamberOfWarUI_hazardPayToggle_m_IsOn;

        // Higher Taxes mod integration
        public static bool higherTaxesExists = false; 

        public static float maximumHazardPayTaxRate = 3f;

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

            higherTaxesExists = HigherTaxesModExists(harmony);
            if (higherTaxesExists)
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
    }
}
