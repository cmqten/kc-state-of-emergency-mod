/*
For patching automatic hazard pay activation/deactivation.

Author: cmjten10
*/
using Harmony;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace StateOfEmergency
{
    public static class AutoHazardPay
    {
        private static int hazardPayState = 0;
        private static Dictionary<int, float> taxRates = new Dictionary<int, float>();

        // =====================================================================
        // Utility Functions
        // =====================================================================

        private static void MaximizeTaxRates()
        {
            // Only set hazard pay tax rate to higher than 30% if possible.
            float hazardPayTaxRate = StateOfEmergencyMod.maximumHazardPayTaxRate;
            if (hazardPayTaxRate > 3f && !StateOfEmergencyMod.higherTaxesExists)
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
                            if (!Player.inst.hazardPay && StateOfEmergencyMod.InvasionInProgress() && canActivate) 
                            {
                                // Refer to: ChamberOfWarUI::OnHazardButtonToggled if hazard pay activation changes.
                                World.GetLandmassOwner(__instance.b.LandMass()).Gold -= goldNeeded;
                                SfxSystem.inst.PlayFromBank("ui_merchant_sellto", Camera.main.transform.position);
                                Player.inst.ChangeHazardPayActive(true, true);
                                StateOfEmergencyMod.chamberOfWarUI_hazardPayToggle_m_IsOn.SetValue(false);

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
                                StateOfEmergencyMod.chamberOfWarUI_hazardPayToggle_m_IsOn.SetValue(true);
                                hazardPayState = 2;
                            }
                            break;
                        
                        case 2: 
                            // Deactivation state
                            // If the invasion is over, deactivates an auto-activated hazard pay. Else if hazard pay is 
                            // deactivated during an invasion (manually or out of gold), prevents auto-activation until 
                            // the next invasion. Restores tax rates to original in both cases.
                            if (Player.inst.hazardPay && !StateOfEmergencyMod.InvasionInProgress()) 
                            {
                                Player.inst.ChangeHazardPayActive(false, false);
                                StateOfEmergencyMod.chamberOfWarUI_hazardPayToggle_m_IsOn.SetValue(false);
                                RestoreTaxRates();
                                hazardPayState = 0;
                            }
                            else if (!Player.inst.hazardPay) 
                            {
                                StateOfEmergencyMod.chamberOfWarUI_hazardPayToggle_m_IsOn.SetValue(false);
                                RestoreTaxRates();
                                hazardPayState = 3;
                            }
                            break;
                        
                        case 3: 
                            // Auto-activation/deactivation disabled state
                            // Waits out the current invasion before going back to the activation state. Goes to this 
                            // state when hazard pay is activated before, or deactivated during an invasion.
                            if (!StateOfEmergencyMod.InvasionInProgress()) 
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
                    StateOfEmergencyMod.helper.Log("ERROR: Exception raised in AutoHazardPayPatch.");
                    StateOfEmergencyMod.helper.Log(e.ToString());
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