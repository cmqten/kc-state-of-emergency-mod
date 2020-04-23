using Harmony;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Threading;
using UnityEngine;

namespace KCMod {
    public class StateOfEmergencyMod: MonoBehaviour
    {
        public KCModHelper helper;
        
        //After scene loads
        void SceneLoaded(KCModHelper helper)
        {
        
        }

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

        private void Update()
        {
            //Code here if mod needed updated per frame
        }

        [HarmonyPatch(typeof(ChamberOfWar))]
        [HarmonyPatch("Update")]
        public static class StateOfEmergencyPatch
        {
            private static int state = 0;

            static void Postfix(ChamberOfWar __instance)
            {
                switch(state) 
                {
                    case 0: // Activate hazard pay when Dragons spawn
                        if (!Player.inst.hazardPay && DragonSpawn.inst.currentDragons.Count > 0) {
                            Player.inst.ChangeHazardPayActive(true, true);
                            state = 1;
                        }
                        break;
                    
                    case 1: // Wait for warmup
                        if (Player.inst.hazardPay) {
                            state = 2;
                        }
                        break;
                    
                    case 2: // Deactivate hazard pay when Dragons despawn
                        if (Player.inst.hazardPay && DragonSpawn.inst.currentDragons.Count == 0) {
                            Player.inst.ChangeHazardPayActive(false, false);
                            state = 0;
                        }
                        break;
                }
            }
        }
    }
}

