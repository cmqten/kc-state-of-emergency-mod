/*
For patching automatic tower open/close.

Author: cmjten10
*/
using Assets;
using Harmony;
using I2.Loc;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace StateOfEmergency
{
    public static class AutoTowers
    {
        // Archer and ballista towers to be auto-opened/closed
        private static int autoTowersState = 0;
        private static List<Building> autoTowers = new List<Building>();

        // =====================================================================
        // Utility Functions
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
                StateOfEmergencyMod.helper.Log("ERROR: Exception raised while opening/closing tower.");
                StateOfEmergencyMod.helper.Log(e.ToString());
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
                OpenCloseTower(tower, false);
            }
        }

        private static void ResetAutoTowers()
        {
            autoTowersState = 0;
            autoTowers.Clear();
        }

        // =====================================================================
        // Patches
        // =====================================================================

        // Player::Update patch for tower auto-open/close.
        [HarmonyPatch(typeof(Player), "Update")]
        public static class AutoOpenCloseTowersPatch 
        {
            public static void Postfix(Player __instance) 
            {
                switch (autoTowersState)
                {
                    case 0:
                        // Open all archer and ballista towers.
                        if (StateOfEmergencyMod.InvasionInProgress())
                        {
                            OpenTowers();
                            autoTowersState = 1;
                        }
                        break;

                    case 1:
                        // Close towers that were closed before the invasion.
                        if (!StateOfEmergencyMod.InvasionInProgress())
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

        // Player::Reset patch for resetting auto towers state when loading a different game.
        [HarmonyPatch(typeof(Player), "Reset")]
        public static class ResetAutoTowersPatch
        {
            static void Postfix(Player __instance) 
            {
                ResetAutoTowers();
            }
        }
    }
}