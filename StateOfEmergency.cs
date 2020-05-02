/*
A mod that auto-activates/deactivates hazard pay and tax increase, and auto-opens/closes archer and ballista towers
during dragon or viking invasions.

Author: cmjten10 (https://steamcommunity.com/id/cmjten10/)
Mod Version: 1.1
Target K&C Version: 117r5s-mods
Date: 2020-04-28
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

namespace StateOfEmergency
{
    public class StateOfEmergencyMod : MonoBehaviour 
    {
        public static KCModHelper helper;

        // For accessing ChamberOfWarUI and private fields/methods
        private static ChamberOfWarUI chamberOfWarUI;
        private static Traverse chamberOfWarUITraverse;
        private static Traverse chamberOfWarUI_hazardPayToggle_m_IsOn;

        // Hazard pay
        private static int hazardPayState = 0;
        private static Dictionary<int, float> taxRates = new Dictionary<int, float>();

        // Archer and ballista towers to be auto-opened/closed
        private static int autoTowersState = 0;
        private static List<Building> autoTowers = new List<Building>();

        void Preload(KCModHelper __helper) 
        {
            helper = __helper;
            var harmony = HarmonyInstance.Create("harmony");
            harmony.PatchAll(Assembly.GetExecutingAssembly());
        }

        void SceneLoaded(KCModHelper __helper)
        {
            chamberOfWarUI = GameUI.inst.chamberOfWarUI;
            chamberOfWarUITraverse = Traverse.Create(chamberOfWarUI);
            chamberOfWarUI_hazardPayToggle_m_IsOn = chamberOfWarUITraverse.Field("hazardPayToggle").Field("m_IsOn");
        }

        // =====================================================================
        // Shared Utility Functions
        // =====================================================================

        private static bool InvasionInProgress()
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
            int landmassesCount = Player.inst.PlayerLandmassOwner.ownedLandMasses.Count;
            for (int i = 0; i < landmassesCount; i++)
            {
                int landmassId = Player.inst.PlayerLandmassOwner.ownedLandMasses.data[i];
                float landmassTaxRate = Player.inst.GetTaxRate(landmassId);
                taxRates.Add(landmassId, landmassTaxRate);
                Player.inst.SetTaxRate(landmassId, 3f);
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
        // Auto-Open/Close Towers Utility Functions
        // =====================================================================

        // Refer to WorkerUI::Init for opening and closing buildings.
        private static void OpenCloseTower(Building tower, bool open)
        {
            try
            {
                if (open != tower.Open)
                {
                    tower.Open = open;
                    string popUpText = open ? ScriptLocalization.Open : ScriptLocalization.OutputUIClosed;
                    ResourceTextStackManager.inst.ShowText(GameUI.inst.workerUI, tower.Center(), popUpText);
                }
            }
            catch (Exception e)
            {
                helper.Log("ERROR: Exception raised while opening/closing tower.");
                helper.Log(e.ToString());
            }
        }

        private static void OpenTowers()
        {
            ArrayExt<Building> archerTowers = Player.inst.GetBuildingList(World.archerTowerName);
            ArrayExt<Building> ballistaTowers = Player.inst.GetBuildingList(World.ballistaTowerName);
            ArrayExt<Building>[] allTowers = new ArrayExt<Building>[] { archerTowers, ballistaTowers };

            // Track all archer and ballista towers that were opened to close them later.
            foreach (ArrayExt<Building> towers in allTowers)
            {
                for (int i = 0; i < towers.Count; i++)
                {
                    Building tower = towers.data[i];
                    if (!tower.Open)
                    {
                        OpenCloseTower(tower, true);
                        autoTowers.Add(tower);
                    }
                }
            }
        }

        private static void CloseTowers()
        {
            foreach (Building tower in autoTowers)
            {
                if (tower.Open)
                {
                    OpenCloseTower(tower, false);
                }
            }
        }

        private static void ResetAutoOpenCloseTowers()
        {
            autoTowersState = 0;
            autoTowers.Clear();
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

        // Player::Update patch for tower auto-open/close.
        [HarmonyPatch(typeof(Player))]
        [HarmonyPatch("Update")]
        public static class AutoOpenCloseTowersPatch 
        {
            static void Postfix(Player __instance) 
            {
                switch (autoTowersState)
                {
                    case 0:
                        // Open all archer and ballista towers.
                        if (InvasionInProgress())
                        {
                            OpenTowers();
                            autoTowersState = 1;
                        }
                        break;

                    case 1:
                        // Close towers that were closed before the invasion.
                        if (!InvasionInProgress())
                        {
                            CloseTowers();
                            autoTowers.Clear();
                            autoTowersState = 0;
                        }
                        break;

                    default:
                        break;
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
                ResetAutoOpenCloseTowers();
            }
        }
    }
}
