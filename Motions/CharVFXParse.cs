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

        /// <summary>Parsed-once lookup of a character's CharacterVFX.json.</summary>
        private static CharacterVFX GetVFX(string appearanceID)
        {
            if (MotionData.AppearanceVFXCache.TryGetValue(appearanceID, out var cached))
                return cached;

            MotionData.CustomAppearanceVFX.TryGetValue(appearanceID, out string jsonPath);
            var parsed = jsonPath != null ? Parse(jsonPath) : null;

            MotionData.AppearanceVFXCache[appearanceID] = parsed; // negative results cached too
            return parsed;
        }

        /// <summary>Bundle lookup for a VFX prefab, scanned once per (appearance, vfxName).</summary>
        private static GameObject GetPrefab(string appearanceID, string vfxName)
        {
            string key = appearanceID + "/" + vfxName;
            if (MotionData.AppearanceVFXPrefabs.TryGetValue(key, out var cached))
                return cached;

            GameObject prefab = null;
            var bundles = MotionData.GetAssetBundlesFromAppearance(appearanceID);
            if (bundles != null)
            {
                foreach (var bundle in bundles)
                {
                    prefab = FindGameObjectInBundle(bundle, vfxName);
                    if (prefab != null)
                        break;
                }
            }

            MotionData.AppearanceVFXPrefabs[key] = prefab;
            return prefab;
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
            => SyncVFX(__instance);

        // Buffs that lapse or drop below their threshold never raise a typo, so the aura would stay
        // up until the next unrelated trigger. Re-sync at round start to catch that.
        [HarmonyPatch(typeof(BattleUnitView), nameof(BattleUnitView.OnRoundStart))]
        [HarmonyPostfix]
        public static void CharacterAppearanceVFXRoundStart(BattleUnitView __instance)
            => SyncVFX(__instance);

        private static void SyncVFX(BattleUnitView __instance)
        {
            string appearanceID = __instance?.unitModel?.GetAppearanceID();
            if (string.IsNullOrEmpty(appearanceID))
                return;

            CharacterVFX characterVFX = GetVFX(appearanceID);
            if (characterVFX == null)
                return;

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

                // find prefab for dominant entry
                GameObject prefab = GetPrefab(appearanceID, selected.vfxName);

                if (prefab == null)
                    continue;

                // Instantiate renames the clone to "<name>(Clone)", so the front/back check has to
                // read the source prefab's name, not the instance's.
                bool isFront = prefab.name.EndsWith("_Front", StringComparison.OrdinalIgnoreCase);

                // copy paste from buff
                GameObject charVfxInstance = UnityEngine.Object.Instantiate(prefab);

                charVfxInstance.transform.SetParent(
                    isFront
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