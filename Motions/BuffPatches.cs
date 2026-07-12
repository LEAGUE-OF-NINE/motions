using HarmonyLib;
using System;
using UnityEngine;

namespace Motions
{
    /// <summary>
    /// Manages buff aura VFX using the game's native BattleUnitViewAura system.
    /// When a buff with a registered MOTIONBUFF_ folder is applied to a unit, the corresponding
    /// prefab is instantiated and managed through the game's aura effect root and dictionary,
    /// ensuring proper lifecycle (auto-cleanup on death, root enable/disable, etc.).
    /// </summary>
    internal class BuffPatches
    {
        /// <summary>
        /// Hooks BattleUnitView.ViewAbilityTypo to detect when a buff is applied
        /// or refreshed, and activates the corresponding aura effect through the game's
        /// BattleUnitViewAura component.
        /// </summary>
        [HarmonyPatch(typeof(BattleUnitView), nameof(BattleUnitView.ViewAbilityTypo))]
        [HarmonyPostfix]
        public static void TriggerBuffVFX(BattleUnitView __instance, AbilityTriggeredData triggerdData)
        {
            try
            {
                // Extract the buff keyword from the triggered data
                BuffTypo typo = null;
                triggerdData.GetBuffData(out typo);
                if (typo == null) return;

                BUFF_UNIQUE_KEYWORD keyword = typo.GetBuffKeyword();
                Logger.LogInfo($"[BuffAura] Buff typo fired for keyword {(int)keyword} ({keyword})");

                // Check if we have a prefab registered for this buff keyword
                if (!MotionData.BuffAuraPrefabs.TryGetValue(keyword, out var prefab))
                    return;

                // Get the unit's BattleUnitViewAura component (the game's native aura system)
                var aura = __instance.GetComponent<BattleUnitViewAura>();
                if (aura == null)
                {
                    Logger.LogWarning($"[BuffAura] No BattleUnitViewAura component found on unit. " +
                                      $"Falling back to direct parenting method.");
                    TriggerBuffVFX_Legacy(__instance, keyword, prefab);
                    return;
                }

                // Use the buff keyword as the aura effect key
                string auraKey = $"MOTIONBUFF_{(int)keyword}";

                // Check if the aura effect already exists in the game's a  ura dictionary
                var auraDict = aura._auraEffectDict;
                if (auraDict != null && auraDict.TryGetValue(auraKey, out var existingObj))
                {
                    // Effect already exists — reactivate it
                    if (existingObj != null)
                    {
                        if (!existingObj.activeSelf)
                        {
                            aura.ActiveAuraEffect(auraKey);
                            Logger.LogInfo($"[BuffAura] Reactivated existing aura for {(int)keyword}");
                        }
                        // Clear and replay particles for a visual "refresh"
                        foreach (var ps in existingObj.GetComponentsInChildren<ParticleSystem>())
                        {
                            ps.Clear();
                            ps.Play();
                        }
                        return;
                    }
                    else
                    {
                        // Stale null entry — clean it up
                        auraDict.Remove(auraKey);
                    }
                }

                // Create a new aura effect instance
                bool isFront = MotionData.BuffAuraIsFront.TryGetValue(keyword, out var front) && front;

                // Instantiate the prefab and parent it under the game's aura root
                var auraRoot = aura._auraEffectRoot;
                if (auraRoot == null)
                {
                    Logger.LogWarning($"[BuffAura] No _auraEffectRoot on BattleUnitViewAura. " +
                                      $"Falling back to legacy method.");
                    TriggerBuffVFX_Legacy(__instance, keyword, prefab);
                    return;
                }

                GameObject instance = UnityEngine.Object.Instantiate(prefab, auraRoot);
                instance.name = prefab.name;
                instance.transform.localPosition = Vector3.zero;
                instance.transform.localRotation = Quaternion.identity;
                instance.transform.localScale =
                    Vector3.one * __instance.Appearance.charInfo.transform_Height.localPosition.y * 0.25f;

                // Add to the game's aura dictionary for proper lifecycle management
                // (OnDieView will auto-cleanup, EnableRoot will toggle visibility, etc.)
                if (auraDict != null)
                {
                    auraDict.Add(auraKey, instance);
                }

                // Activate through the game's API
                instance.SetActive(false);
                aura.ActiveAuraEffect(auraKey);

                Logger.LogInfo($"[BuffAura] Created aura effect for {(int)keyword} " +
                               $"(isFront={isFront}, parented under _auraEffectRoot)");
            }
            catch (Exception ex)
            {
                Logger.LogError($"[BuffAura] Error in TriggerBuffVFX: {ex}");
            }
        }

        /// <summary>
        /// Fallback for units that don't have a BattleUnitViewAura component.
        /// Uses the same parenting as the old hacky method but avoids the Effect_Ability list.
        /// </summary>
        private static void TriggerBuffVFX_Legacy(
            BattleUnitView view, BUFF_UNIQUE_KEYWORD keyword, GameObject prefab)
        {
            bool isFront = MotionData.BuffAuraIsFront.TryGetValue(keyword, out var front) && front;

            GameObject instance = UnityEngine.Object.Instantiate(prefab);
            instance.name = prefab.name;

            instance.transform.SetParent(isFront
                ? view.viewEffectRootDirection
                : view.viewEffectRootBack);
            instance.transform.localPosition = Vector3.zero;
            instance.transform.localRotation = Quaternion.identity;
            instance.transform.localScale =
                Vector3.one * view.Appearance.charInfo.transform_Height.localPosition.y * 0.25f;

            instance.SetActive(true);

            Logger.LogInfo($"[BuffAura] Created legacy aura effect for {(int)keyword}");
        }
    }
}
