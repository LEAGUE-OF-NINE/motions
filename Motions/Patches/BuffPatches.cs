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
            BuffTypo typo = null;
            triggerdData.GetBuffData(out typo);
            if (typo == null) return;

            BUFF_UNIQUE_KEYWORD keyword = typo.GetBuffKeyword();

            if (!MotionData.CreatedAbilityEffects.TryGetValue(keyword, out var cached))
                return;

            Effect_Label existing = __instance.GetEffect_Ability(keyword);

            if (existing != null)
            {
                if (BattleUnitView.IsEffectDestroyed(existing) || existing.effectObj == null)
                {
                    Logger.LogWarning($"Effect {(int)keyword} was destroyed, recreating.");
                    __instance.UnregisterEffect(existing);
                }
                else if (existing.effectObj.activeSelf)
                {
                    return;
                }
                else
                {
                    existing.effectObj.SetActive(true);
                    foreach (var ps in existing.effectObj.GetComponentsInChildren<ParticleSystem>())
                    {
                        ps.Clear();
                        ps.Play();
                    }
                    return;
                }
            }

            GameObject instance = Object.Instantiate(cached.effectObj);

            var label = new Effect_Label
            {
                effectObj = instance,
                IsSetOverrideDie = cached.IsSetOverrideDie
            };

            instance.transform.SetParent(
                cached.effectObj.name.EndsWith("_Front")
                    ? __instance.viewEffectRootDirection
                    : __instance.viewEffectRootBack, true);

            instance.transform.localPosition = Vector3.zero;
            instance.transform.localRotation = Quaternion.identity;
            instance.transform.localScale =
                Vector3.one * __instance.Appearance.charInfo.transform_Height.localPosition.y * 0.25f;

            __instance.RegisterEffect(label);
            __instance.RegisterEffectAbility(keyword, label);

            instance.SetActive(false);
            instance.SetActive(true);
        }

        [HarmonyPatch(typeof(BattleUnitView), nameof(BattleUnitView.OnRoundStart))]
        [HarmonyPostfix]
        public static void TriggerBuffVFXRS(BattleUnitView __instance)
        {
            var buff = __instance.unitModel.GetBuffInfos(0);
            foreach (var info in buff)
            {
                BUFF_UNIQUE_KEYWORD keyword = info.GetKeyword();
                if (!MotionData.CreatedAbilityEffects.TryGetValue(keyword, out var cached))
                    continue;
                Effect_Label existing = __instance.GetEffect_Ability(keyword);

                if (existing != null)
                {
                    if (BattleUnitView.IsEffectDestroyed(existing) || existing.effectObj == null)
                    {
                        Logger.LogWarning($"Effect {(int)keyword} was destroyed, recreating.");
                        __instance.UnregisterEffect(existing);
                    }
                    else if (existing.effectObj.activeSelf)
                    {
                        continue;
                    }
                    else
                    {
                        existing.effectObj.SetActive(true);
                        foreach (var ps in existing.effectObj.GetComponentsInChildren<ParticleSystem>())
                        {
                            ps.Clear();
                            ps.Play();
                        }
                        continue;
                    }
                }

                GameObject instance = Object.Instantiate(cached.effectObj);

                var label = new Effect_Label
                {
                    effectObj = instance,
                    IsSetOverrideDie = cached.IsSetOverrideDie
                };

                instance.transform.SetParent(
                    cached.effectObj.name.EndsWith("_Front")
                        ? __instance.viewEffectRootDirection
                        : __instance.viewEffectRootBack, true);

                instance.transform.localPosition = Vector3.zero;
                instance.transform.localRotation = Quaternion.identity;
                instance.transform.localScale =
                    Vector3.one * __instance.Appearance.charInfo.transform_Height.localPosition.y * 0.25f;
                __instance.RegisterEffect(label);
                __instance.RegisterEffectAbility(keyword, label);

                instance.SetActive(false);
                instance.SetActive(true);
            }
        }
        [HarmonyPatch(typeof(BattleUnitView), nameof(BattleUnitView.OnBattleStart))]
        [HarmonyPostfix]
        public static void TriggerBuffVFXBS(BattleUnitView __instance)
        {
            var buff = __instance.unitModel.GetBuffInfos(0);
            foreach (var info in buff)
            {
                BUFF_UNIQUE_KEYWORD keyword = info.GetKeyword();
                if (!MotionData.CreatedAbilityEffects.TryGetValue(keyword, out var cached))
                    continue;
                Effect_Label existing = __instance.GetEffect_Ability(keyword);

                if (existing != null)
                {
                    if (BattleUnitView.IsEffectDestroyed(existing) || existing.effectObj == null)
                    {
                        Logger.LogWarning($"Effect {(int)keyword} was destroyed, recreating.");
                        __instance.UnregisterEffect(existing);
                    }
                    else if (existing.effectObj.activeSelf)
                    {
                        continue;
                    }
                    else
                    {
                        existing.effectObj.SetActive(true);
                        foreach (var ps in existing.effectObj.GetComponentsInChildren<ParticleSystem>())
                        {
                            ps.Clear();
                            ps.Play();
                        }
                        continue;
                    }
                }

                GameObject instance = Object.Instantiate(cached.effectObj);

                var label = new Effect_Label
                {
                    effectObj = instance,
                    IsSetOverrideDie = cached.IsSetOverrideDie
                };

                instance.transform.SetParent(
                    cached.effectObj.name.EndsWith("_Front")
                        ? __instance.viewEffectRootDirection
                        : __instance.viewEffectRootBack, true);

                instance.transform.localPosition = Vector3.zero;
                instance.transform.localRotation = Quaternion.identity;
                instance.transform.localScale =
                    Vector3.one * __instance.Appearance.charInfo.transform_Height.localPosition.y * 0.25f;
                __instance.RegisterEffect(label);
                __instance.RegisterEffectAbility(keyword, label);

                instance.SetActive(false);
                instance.SetActive(true);
            }
        }
    }
}