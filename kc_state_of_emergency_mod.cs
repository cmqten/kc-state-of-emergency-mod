/*
A mod that auto-activates/deactivates hazard pay, tax increase, archer towers, 
and ballista towers during dragon and viking invasions.

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

        // ChamberOfWar::Update patch for engaging hazard pay auto-activation/deactivation system.
        [HarmonyPatch(typeof(ChamberOfWar))]
        [HarmonyPatch("Update")]
        public static class HazardPayPatch 
        {
            static void Postfix(ChamberOfWar __instance) 
            {
                bool dragonAttack = DragonSpawn.inst.currentDragons.Count > 0;
                bool vikingAttack = RaiderSystem.inst.IsRaidInProgress();

                // Refer to ChamberOfWarUI::Update if hazard pay requirements change.
                int goldNeeded = 50;
                bool fullyStaffed = (double)__instance.b.GetWorkerPercent() > 0.95;
                bool hasEnoughGold = World.GetLandmassOwner(__instance.b.LandMass()).Gold >= goldNeeded;
                bool canActivate = fullyStaffed && hasEnoughGold;

                switch(hazardPayState) 
                {
                    case 0: 
                        // Activation state
                        // If an invasion starts and hazard pay is not activated, auto-activates it if the requirements 
                        // are met, then maximizes tax rates. Else if hazard pay is already activated, prevents from
                        // auto-deactivation.
                        if (!Player.inst.hazardPay && (dragonAttack || vikingAttack) && canActivate) 
                        {
                            // Refer to: ChamberOfWarUI::OnHazardButtonToggled if hazard pay activation changes.
                            World.GetLandmassOwner(__instance.b.LandMass()).Gold -= goldNeeded;
                            SfxSystem.inst.PlayFromBank("ui_merchant_sellto", Camera.main.transform.position);
                            Player.inst.ChangeHazardPayActive(true, true);

                            // Crank up the tax rates to maximum in order to afford hazard pay. Save the previous rates 
                            // to restore them when the invasion is over.
                            int landmassCount = Player.inst.PlayerLandmassOwner.ownedLandMasses.Count;
                            for (int i = 0; i < landmassCount; i++)
                            {
                                int landmassId = Player.inst.PlayerLandmassOwner.ownedLandMasses.data[i];
                                float landmassTaxRate = Player.inst.GetTaxRate(landmassId);
                                taxRates.Add(landmassId, landmassTaxRate);
                                Player.inst.SetTaxRate(landmassId, 3f);
                            }
                            hazardPayState = 1;
                        }
                        else if (Player.inst.hazardPay || Player.inst.hazardPayWarmup.Enabled) 
                        {
                            hazardPayState = 3;
                        }
                        break;
                    
                    case 1: 
                        // Activation warmup state/#!/
                        // Hazard pay must finish activation before auto-deactivation. 
                        if (!Player.inst.hazardPayWarmup.Enabled) 
                        {
                            hazardPayState = 2;
                        }
                        break;
                    
                    case 2: 
                        // Deactivation state
                        // If the invasion is over, deactivates an auto-activated hazard pay. Else if hazard pay is 
                        // deactivated during an invasion (manually or out of gold), prevents auto-activation until the 
                        // next invasion. Restores tax rates to original in both cases.
                        if (Player.inst.hazardPay && !dragonAttack && !vikingAttack) 
                        {
                            Player.inst.ChangeHazardPayActive(false, false);
                            foreach (KeyValuePair<int, float> entry in taxRates) 
                            {
                                Player.inst.SetTaxRate(entry.Key, entry.Value);
                            }
                            taxRates.Clear();
                            hazardPayState = 0;
                        }
                        else if (!Player.inst.hazardPay) 
                        {
                            foreach (KeyValuePair<int, float> entry in taxRates) 
                            {
                                Player.inst.SetTaxRate(entry.Key, entry.Value);
                            }
                            taxRates.Clear();
                            hazardPayState = 3;
                        }
                        break;
                    
                    case 3: 
                        // Auto-activation/deactivation disabled state
                        // Waits out the current invasion before going back to the activation state. Goes to this state 
                        // when hazard pay is activated before, or deactivated during an invasion.
                        if (!dragonAttack && !vikingAttack) 
                        {
                            hazardPayState = 0;
                        }
                        break;
                    
                    default:
                        break;
                }
            }
        }

        // Test
        [HarmonyPatch(typeof(Player))]
        [HarmonyPatch("Update")]
        public static class ArcherBallistaPatch 
        {
            static void Postfix(Player __instance) 
            {
                bool dragonAttack = DragonSpawn.inst.currentDragons.Count > 0;
                bool vikingAttack = RaiderSystem.inst.IsRaidInProgress();

                switch (autoTowersState)
                {
                    case 0:
                        // Open all archer and ballista towers.
                        if (dragonAttack || vikingAttack)
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
                                        // Refer to WorkerUI::Init if tower opening changes.
                                        tower.Open = true;
                                        ResourceTextStackManager.inst.ShowText(GameUI.inst.workerUI, tower.Center(), ScriptLocalization.Open);
                                        autoTowers.Add(tower);
                                    }
                                }
                            }
                            autoTowersState = 1;
                        }
                        break;

                    case 1:
                        // Close towers that were closed before the invasion.
                        if (!dragonAttack && !vikingAttack)
                        {
                            foreach (Building tower in autoTowers)
                            {
                                // Refer to WorkerUI::Init if tower closing changes.
                                if (tower.Open)
                                {
                                    tower.Open = false;
                                    ResourceTextStackManager.inst.ShowText(GameUI.inst.workerUI, tower.Center(), ScriptLocalization.OutputUIClosed);
                                }
                            }
                            autoTowersState = 0;
                            autoTowers.Clear();
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
        public static class ResetStateOfEmergencyPatch 
        {
            static void Postfix(Player __instance) 
            {
                hazardPayState = 0;
                taxRates.Clear();
                autoTowersState = 0;
                autoTowers.Clear();
            }
        }
    }
}
