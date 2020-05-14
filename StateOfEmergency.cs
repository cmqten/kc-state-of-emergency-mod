/*
A mod that auto-activates/deactivates hazard pay and tax increase, and auto-opens/closes archer and ballista towers
during dragon or viking invasions.

Author: cmjten10 (https://steamcommunity.com/id/cmjten10/)
Mod Version: 1.3
Target K&C Version: 117r7s
Date: 2020-05-14
*/
using Harmony;
using System.Reflection;
using UnityEngine;
using Zat.Shared.ModMenu.API;
using Zat.Shared.ModMenu.Interactive;

namespace StateOfEmergency
{
    public class ModMain : MonoBehaviour 
    {
        public const string authorName = "cmjten10";
        public const string modName = "State of Emergency";
        public const string modNameNoSpace = "StateOfEmergency";
        public const string version = "v1.3";

        private static HarmonyInstance harmony;
        public static KCModHelper helper;
        public static ModSettingsProxy proxy;
        public static StateOfEmergencySettings settings;

        // For making sure Chamber of War UI state is consistent when activating hazard pay automatically.
        public static Traverse chamberOfWarUI_hazardPayToggle_m_IsOn;

        // Higher Taxes mod integration
        public static bool higherTaxesExists = false;

        void Preload(KCModHelper __helper) 
        {
            helper = __helper;
            harmony = HarmonyInstance.Create($"{authorName}.{modNameNoSpace}");
            harmony.PatchAll(Assembly.GetExecutingAssembly());
        }

        void SceneLoaded(KCModHelper __helper)
        {
            ChamberOfWarUI chamberOfWarUI = GameUI.inst.chamberOfWarUI;
            Traverse chamberOfWarUITraverse = Traverse.Create(chamberOfWarUI);
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

            if (!proxy)
            {
                var config = new InteractiveConfiguration<StateOfEmergencySettings>();
                settings = config.Settings;
                ModSettingsBootstrapper.Register(config.ModConfig, (_proxy, saved) =>
                {
                    config.Install(_proxy, saved);
                    proxy = _proxy;
                    settings.autoHazardPaySettings.Setup();
                }, (ex) =>
                {
                    helper.Log($"ERROR: Failed to register proxy for {modName} Mod config: {ex.Message}");
                    helper.Log(ex.StackTrace);
                });
            }
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
        // Settings
        // =====================================================================
        [Mod(ModMain.modName, ModMain.version, ModMain.authorName)]
        public class StateOfEmergencySettings
        {
            [Setting("State of Emergency Enabled", 
            "Enable or disable mod. If disabled, the rest of the settings do not apply.")]
            [Toggle(true, "")]
            public InteractiveToggleSetting enabled { get; private set; }

            [Category("Auto Hazard Pay")]
            public AutoHazardPay.Settings autoHazardPaySettings { get; private set; }

            [Category("Auto Towers")]
            public AutoTowers.Settings autoTowersSettings { get; private set; }
        }
    }
}
