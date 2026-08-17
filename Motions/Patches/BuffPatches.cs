using HarmonyLib;
using UnityEngine;

namespace Motions
{
    internal class BuffPatches
    {
        [HarmonyPatch(typeof(BattleUnitView), nameof(BattleUnitView.ViewAbilityTypo))]
        [HarmonyPostfix]
        public static void TriggerBuffVFX(BattleUnitView __instance, AbilityTriggeredData triggerdData)
        {
            Logger.LogInfo("[VIEWABILITYTYPOPATCH] Entered patch");

            BuffTypo typo = null;
            triggerdData.GetBuffData(out typo);
            if (typo == null)
                return;

            BUFF_UNIQUE_KEYWORD keyword = typo.GetBuffKeyword();
            Logger.LogInfo($"[VIEWABILITYTYPO] Instance this time is {keyword}");

            if (!MotionData.CreatedAbilityEffects.TryGetValue(keyword, out var cachedAbility))
                return;

            Effect_Label existing = null;

            if (__instance._effects_ability.TryGetValue(keyword, out existing))
            {
                if (existing.effectObj == null)
                {
                    Logger.LogWarning($"Existing effect ability {(int)keyword} had a destroyed GameObject, recreating.");
                    __instance._effects_ability.Remove(keyword);
                    existing = null;
                }
                else if (existing.effectObj.activeSelf)
                {
                    Logger.LogInfo($"Skipping existing active effect ability {(int)keyword}");
                    return;
                }
                else
                {
                    existing.effectObj.SetActive(false);
                    existing.effectObj.SetActive(true);

                    foreach (var ps in existing.effectObj.GetComponentsInChildren<ParticleSystem>())
                    {
                        ps.Clear();
                        ps.Play();
                    }

                    Logger.LogInfo($"Reactivated existing effect ability {(int)keyword}");
                    return;
                }
            }

            GameObject instance = Object.Instantiate(cachedAbility.effectObj);

            var ability = new Effect_Ability
            {
                keyword = cachedAbility.keyword,
                effectObj = instance,
                IsSetOverrideDie = cachedAbility.IsSetOverrideDie
            };

            instance.transform.SetParent(
                cachedAbility.effectObj.name.EndsWith("_Front")
                    ? __instance.viewEffectRootDirection
                    : __instance.viewEffectRootBack);

            instance.transform.localPosition = Vector3.zero;
            instance.transform.localRotation = Quaternion.identity;
            instance.transform.localScale =
                Vector3.one * __instance.Appearance.charInfo.transform_Height.localPosition.y * 0.25f;

            __instance._effects_ability.Add(keyword, (Effect_Label)ability);

            instance.SetActive(false);
            instance.SetActive(true);
        }
    }
}