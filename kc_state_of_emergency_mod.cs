using Harmony;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using UnityEngine;

namespace KCMod {
    public class StateOfEmergencyMod: MonoBehaviour {
        public KCModHelper helper;
        
        //After scene loads
        void SceneLoaded(KCModHelper helper) { }

        //Before scene loads
        void Preload(KCModHelper helper)
        {
            //Demonstrating how KCModHelper is used 
            this.helper = helper;
            helper.Log(helper.modPath);

            //Load up Harmony
            var harmony = HarmonyInstance.Create("harmony");
            harmony.PatchAll(Assembly.GetExecutingAssembly());
        }

        private void Update() { }

        [HarmonyPatch(typeof(ChamberOfWar))]
        [HarmonyPatch("Update")]
        public static class StateOfEmergencyPatch {
            private static int state = 0;

            static void Postfix(ChamberOfWar __instance) {
                bool dragonAttack = DragonSpawn.inst.currentDragons.Count > 0;
                bool vikingAttack = RaiderSystem.inst.IsRaidInProgress();

                // Conditions for activating hazard pay
                bool fullyStaffed = (double)__instance.b.GetWorkerPercent() > 0.95;
                bool hasEnoughGold = World.GetLandmassOwner(__instance.b.LandMass()).Gold >= 50;
                bool canActivate = fullyStaffed && hasEnoughGold;

                switch(state) {
                    case 0: // Activation
                        if (!Player.inst.hazardPay && (dragonAttack|| vikingAttack) && canActivate) {
                            // Activate hazard pay when dragons or vikings spawn
                            World.GetLandmassOwner(__instance.b.LandMass()).Gold -= 50;
			                SfxSystem.inst.PlayFromBank("ui_merchant_sellto", Camera.main.transform.position);
                            Player.inst.ChangeHazardPayActive(true, true);
                            state = 1;
                        }
                        else if (Player.inst.hazardPay || Player.inst.hazardPayWarmup.Enabled) {
                            // If hazard pay is already activated, it was done 
                            // so manually, so do not deactivate automatically
                            state = 3;
                        }
                        break;
                    
                    case 1: // Warmup
                        if (!Player.inst.hazardPayWarmup.Enabled) {
                            state = 2;
                        }
                        break;
                    
                    case 2: // Deactivation
                        if (Player.inst.hazardPay && !dragonAttack && !vikingAttack) {
                            // Deactivate hazard pay when dragons and vikings 
                            // despawn
                            Player.inst.ChangeHazardPayActive(false, false);
                            state = 0;
                        }
                        else if (!Player.inst.hazardPay) { 
                            // Manually deactivated, do not auto activate again
                            // until next invasion
                            state = 3;
                        }
                        break;
                    
                    case 3: // Manual mode
                        if (!dragonAttack && !vikingAttack) {
                            // Manually activated/deactivated, do not use auto
                            // until the next invasion by waiting out the 
                            // current one
                            state = 0;
                        }
                        break;
                    
                    default:
                        break;
                }
            }
        }
    }
}

