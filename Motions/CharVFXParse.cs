using HarmonyLib;
using Il2CppInterop.Runtime;
using Lethe.Patches;
using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using UnityEngine;

namespace Motions
{
    internal static class CharVFXParse
    {
        public static CharacterVFX Parse(string jsonPath)
        {
            if (string.IsNullOrEmpty(jsonPath) || !File.Exists(jsonPath))
            {
                Logger.LogWarning($"[CharVFXParse] Character VFX file not found: {jsonPath}");
                return null;
            }
            try
            {
                string json = File.ReadAllText(jsonPath);

                CharacterVFX data = JsonSerializer.Deserialize<CharacterVFX>(json, TimelineBuilder.JsonOptions);

                if (data == null)
                {
                    Logger.LogWarning($"[CharVFXParse] Failed to deserialize '{jsonPath}'.");
                    return null;
                }

                if (data.allVFX == null)
                    data.allVFX = Array.Empty<CharVFX>();

                Logger.LogInfo($"[CharVFXParse] Loaded {data.allVFX.Length} character VFX entries.");
                return data;
            }
            catch (Exception ex)
            {
                Logger.LogError($"[CharVFXParse] Failed parsing '{jsonPath}': {ex}");
                return null;
            }
        }

        private static bool SatisfiesVFXRequirement(int stackThreshold, int turnThreshold, BUFF_UNIQUE_KEYWORD keyword, BattleUnitModel unit)
        {
            var buffInfo = unit._buffDetail.GetBuffInfoAll(0);
            foreach (BuffInfo buff in buffInfo)
            {
                if (!buff.IsKeyword(keyword))
                {
                    continue;
                }

                if (buff._stack < stackThreshold || buff._turn < turnThreshold)
                {
                    return false;
                }

                return true;
            }
            return false;
        }

        public static GameObject FindGameObjectInBundle(AssetBundle bundle, string vfxName)
        {
            foreach (var assetName in bundle.AllAssetNames())
            {
                if (!assetName.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
                    continue;

                if (assetName.IndexOf(vfxName, StringComparison.OrdinalIgnoreCase) < 0)
                {
                    Logger.LogError($"Asset {assetName} did not contain the name {vfxName}");
                    continue;
                }

                var asset = bundle.LoadAsset(assetName, Il2CppType.Of<GameObject>());
                if (asset != null)
                {
                    Logger.LogWarning($"Found matching asset: {assetName}");
                    return asset.Cast<GameObject>();
                }
            }

            Logger.LogError($"Bundle {bundle.GetName()} did not contain a suitable VFX for {vfxName}");
            return null;
        }

        [HarmonyPatch(typeof(BattleUnitView), nameof(BattleUnitView.ViewAbilityTypo))]
        [HarmonyPostfix]
        public static void CharacterAppearanceVFXHandler(BattleUnitView __instance, AbilityTriggeredData triggerdData)
        {
            if (!MotionData.CustomAppearanceVFX.ContainsKey(__instance.unitModel.GetAppearanceID()))
            {
                Logger.LogError($"Character {__instance.unitModel.GetName()} is not valid for vfx with appearance {__instance.unitModel.GetAppearanceID()}");
                return;
            }
            string jsonPath;
            MotionData.CustomAppearanceVFX.TryGetValue(__instance.unitModel.GetAppearanceID(), out jsonPath);
            CharacterVFX characterVFX = Parse(jsonPath);

            var allAttr = characterVFX.allVFX;

            foreach (var keywordGroup in allAttr.GroupBy(x => x.keyword)) // this groups all entries via keyword to select dominant entries for each keyword to be utilized, higher ints = more dominant (might add dominance field later)
            {
                BUFF_UNIQUE_KEYWORD keyword = CustomBuffs.ParseBuffUniqueKeyword(keywordGroup.Key);
                CharVFX selected = null;

                foreach (var entry in keywordGroup) // select dominant entry to be prioritized
                {
                    if (!SatisfiesVFXRequirement(entry.stackThres, entry.turnThres, keyword, __instance.unitModel))
                        continue;

                    if (selected == null || entry.stackThres > selected.stackThres || (entry.stackThres == selected.stackThres && entry.turnThres > selected.turnThres))
                    {
                        selected = entry;
                    }
                }

                // copy pasted checks from buff patch
                Effect_Ability existing = null;
                foreach (var effect in __instance._effects_ability)
                {
                    if (effect.keyword == keyword)
                    {
                        existing = effect;
                        break;
                    }
                }
                if (selected == null)
                {
                    if (existing != null && existing.effectObj != null)
                        existing.effectObj.SetActive(false);

                    continue;
                }
                if (!selected.active)
                {
                    if (existing != null && existing.effectObj != null)
                        existing.effectObj.SetActive(false);

                    continue;
                }
                if (existing != null)
                {
                    if (existing.effectObj == null)
                    {
                        __instance._effects_ability.Remove(existing);
                        existing = null;
                    }
                    else
                    {
                        if (!existing.effectObj.activeSelf)
                        {
                            existing.effectObj.SetActive(true);

                            foreach (var ps in existing.effectObj.GetComponentsInChildren<ParticleSystem>())
                            {
                                ps.Clear();
                                ps.Play();
                            }
                        }

                        continue;
                    }
                }

                GameObject prefab = null;

                // find prefab for dominant entry
                foreach (var bundle in MotionData.GetAssetBundlesFromAppearance(__instance.unitModel.GetAppearanceID()))
                {
                    prefab = FindGameObjectInBundle(bundle, selected.vfxName);

                    if (prefab != null)
                        break;
                }

                if (prefab == null)
                    continue;
                // copy paste from buff
                GameObject charVfxInstance = UnityEngine.Object.Instantiate(prefab);

                charVfxInstance.transform.SetParent(
                    charVfxInstance.name.EndsWith("_Front")
                        ? __instance.viewEffectRootDirection
                        : __instance.viewEffectRootBack);

                charVfxInstance.transform.localPosition = Vector3.zero;
                charVfxInstance.transform.localRotation = Quaternion.identity;
                charVfxInstance.transform.localScale =
                    Vector3.one * __instance.Appearance.charInfo.transform_Height.localPosition.y * 0.25f;

                __instance._effects_ability.Add(new Effect_Ability
                {
                    keyword = keyword,
                    effectObj = charVfxInstance,
                    IsSetOverrideDie = false
                });

                charVfxInstance.SetActive(false);
                charVfxInstance.SetActive(true);
            }
        }

    }
}