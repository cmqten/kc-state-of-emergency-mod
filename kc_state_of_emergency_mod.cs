/*
A mod that auto-activates/deactivates hazard pay and tax increase during dragon and viking invasions.

Author: https://steamcommunity.com/id/cmjten10/
Mod Version: 1
Target K&C Version: 117r5s-mods
Date: 2020-04-24
*/
using Harmony;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using UnityEngine;

namespace StateOfEmergencyMod 
{
    public class ModInit : MonoBehaviour 
    {
        public static KCModHelper helper;

        void Preload(KCModHelper __helper) 
        {
            helper = __helper;
            var harmony = HarmonyInstance.Create("harmony");
            harmony.PatchAll(Assembly.GetExecutingAssembly());
        }
    }

    // ChamberOfWar::Update patch for engaging hazard pay auto-activation/deactivation system.
    [HarmonyPatch(typeof(ChamberOfWar))]
    [HarmonyPatch("Update")]
    public static class StateOfEmergencyPatch 
    {
        private static int state = 0;
        private static Dictionary<int, float> taxRates = new Dictionary<int, float>();

        static void Postfix(ChamberOfWar __instance) 
        {
            bool dragonAttack = DragonSpawn.inst.currentDragons.Count > 0;
            bool vikingAttack = RaiderSystem.inst.IsRaidInProgress();

            // Refer to ChamberOfWarUI::Update if hazard pay requirements change.
            int goldNeeded = 50;
            bool fullyStaffed = (double)__instance.b.GetWorkerPercent() > 0.95;
            bool hasEnoughGold = World.GetLandmassOwner(__instance.b.LandMass()).Gold >= goldNeeded;
            bool canActivate = fullyStaffed && hasEnoughGold;

            switch(state) 
            {
                case 0: 
                    // Activation state
                    // If an invasion starts and hazard pay is not activated, auto-activates it if the requirements are 
                    // met, then maximizes tax rates. Else if hazard pay is already activated, prevents from
                    // auto-deactivation.
                    if (!Player.inst.hazardPay && (dragonAttack || vikingAttack) && canActivate) 
                    {
                        // Refer to: ChamberOfWarUI::OnHazardButtonToggled if hazard pay activation changes.
                        World.GetLandmassOwner(__instance.b.LandMass()).Gold -= goldNeeded;
                        SfxSystem.inst.PlayFromBank("ui_merchant_sellto", Camera.main.transform.position);
                        Player.inst.ChangeHazardPayActive(true, true);

                        // Crank up the tax rates to maximum in order to afford hazard pay. Save the previous rates to 
                        // restore them when the invasion is over.
                        int landmassCount = Player.inst.PlayerLandmassOwner.ownedLandMasses.Count;
                        for (int i = 0; i < landmassCount; i++)
                        {
                            int landmassId = Player.inst.PlayerLandmassOwner.ownedLandMasses.data[i];
                            float landmassTaxRate = Player.inst.GetTaxRate(landmassId);
                            taxRates.Add(landmassId, landmassTaxRate);
                            Player.inst.SetTaxRate(landmassId, 3f);
                        }
                        state = 1;
                    }
                    else if (Player.inst.hazardPay || Player.inst.hazardPayWarmup.Enabled) 
                    {
                        state = 3;
                    }
                    break;
                
                case 1: 
                    // Activation warmup state
                    // Hazard pay must finish activation before auto-deactivation. 
                    if (!Player.inst.hazardPayWarmup.Enabled) 
                    {
                        state = 2;
                    }
                    break;
                
                case 2: 
                    // Deactivation state
                    // If the invasion is over, deactivates an auto-activated hazard pay. Else if hazard pay is 
                    // deactivated during an invasion (manually or out of gold), prevents auto-activation until the next
                    // invasion. Restores tax rates to original in both cases.
                    if (Player.inst.hazardPay && !dragonAttack && !vikingAttack) 
                    {
                        Player.inst.ChangeHazardPayActive(false, false);
                        foreach (KeyValuePair<int, float> entry in taxRates) 
                        {
                            Player.inst.SetTaxRate(entry.Key, entry.Value);
                        }
                        taxRates.Clear();
                        state = 0;
                    }
                    else if (!Player.inst.hazardPay) 
                    {
                        foreach (KeyValuePair<int, float> entry in taxRates) 
                        {
                            Player.inst.SetTaxRate(entry.Key, entry.Value);
                        }
                        taxRates.Clear();
                        state = 3;
                    }
                    break;
                
                case 3: 
                    // Auto-activation/deactivation disabled state
                    // Waits out the current invasion before going back to the activation state. Goes to this state when 
                    // hazard pay is activated before, or deactivated during an invasion.
                    if (!dragonAttack && !vikingAttack) 
                    {
                        state = 0;
                    }
                    break;
                
                default:
                    break;
            }
        }

        // Player::Reset patch for resetting mod state when loading a different game.
        [HarmonyPatch(typeof(Player))]
        [HarmonyPatch("Reset")]
        public static class ResetStateOfEmergencyPatch 
        {
            static void Postfix(Player __instance) 
            {
                state = 0;
                taxRates.Clear();
            }
        }
    }
}
