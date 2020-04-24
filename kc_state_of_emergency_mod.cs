using Harmony;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using UnityEngine;

namespace KCMod 
{
    public class StateOfEmergencyMod: MonoBehaviour 
    {
        public static KCModHelper helper;
        private static int state = 0;
        
        // After scene loads
        void SceneLoaded(KCModHelper _helper) { }

        // Before scene loads
        void Preload(KCModHelper _helper) 
        {
            // Demonstrating how KCModHelper is used 
            helper = _helper;
            helper.Log(helper.modPath);

            // Load up Harmony
            var harmony = HarmonyInstance.Create("harmony");
            harmony.PatchAll(Assembly.GetExecutingAssembly());
        }

        private void Update() { }

        [HarmonyPatch(typeof(ChamberOfWar))]
        [HarmonyPatch("Update")]
        public static class StateOfEmergencyPatch 
        {
            static void Postfix(ChamberOfWar __instance) 
            {
                // Conditions for activating hazard pay.
                bool dragonAttack = DragonSpawn.inst.currentDragons.Count > 0;
                bool vikingAttack = RaiderSystem.inst.IsRaidInProgress();

                // Requirements for activating hazard pay.
                bool fullyStaffed = (double)__instance.b.GetWorkerPercent() > 0.95;
                bool hasEnoughGold = World.GetLandmassOwner(__instance.b.LandMass()).Gold >= 50;
                bool canActivate = fullyStaffed && hasEnoughGold;

                switch(state) 
                {
                    case 0:
                        if (!Player.inst.hazardPay && (dragonAttack|| vikingAttack) && canActivate) 
                        {
                            // Activate hazard pay automatically during an invasion. Refer to:
                            // ChamberOfWarUI::OnHazardButtonToggled
                            World.GetLandmassOwner(__instance.b.LandMass()).Gold -= 50;
                            SfxSystem.inst.PlayFromBank("ui_merchant_sellto", Camera.main.transform.position);
                            Player.inst.ChangeHazardPayActive(true, true);
                            state = 1;
                        }
                        else if (Player.inst.hazardPay || Player.inst.hazardPayWarmup.Enabled) {
                            // Do not enable deactivation detection if already enabled before the invasion, i.e., 
                            // manually activated.
                            state = 3;
                        }
                        break;
                    
                    case 1:
                        if (!Player.inst.hazardPayWarmup.Enabled) 
                        {
                            // This state waits out the warmup period before enabling the deactivation detection.
                            state = 2;
                        }
                        break;
                    
                    case 2:
                        if (Player.inst.hazardPay && !dragonAttack && !vikingAttack) 
                        {
                            // This state deactivates an automatically-activated hazard pay when the invasion is over.
                            Player.inst.ChangeHazardPayActive(false, false);
                            state = 0;
                        }
                        else if (!Player.inst.hazardPay) {
                            // Premature deactivation means the player manually deactivated or ran out of gold. Do not
                            // automatically activate again until the next invasion.
                            state = 3;
                        }
                        break;
                    
                    case 3:
                        if (!dragonAttack && !vikingAttack) 
                        {
                            // This state disables automatic hazard pay activation/deactivation by waiting out the
                            // current invasion. Goes here when hazard pay is manually activated/deactivated.
                            state = 0;
                        }
                        break;
                    
                    default:
                        break;
                }
            }
        }

        [HarmonyPatch(typeof(Player))]
        [HarmonyPatch("Reset")]
        public static class ResetStateOfEmergency 
        {
            static void Postfix(Player __instance) 
            {
                helper.Log("Reset");
                // Reset state variables when Player state is reset, i.e., loading a new game.
                state = 0;
            }
        }
    }
}

