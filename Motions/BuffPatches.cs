using HarmonyLib;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Motions
{
    internal class BuffPatches
    {
        [HarmonyPatch(typeof(BattleUnitView), nameof(BattleUnitView.ViewAbilityTypo))]
        [HarmonyPostfix]
        public static void TriggerBuffVFX(BattleUnitView __instance, AbilityTriggeredData triggerdData)
        {
            try
            {
                BuffTypo typo = null;
                triggerdData.GetBuffData(out typo);
                if (typo == null) return;

                BUFF_UNIQUE_KEYWORD keyword = typo.GetBuffKeyword();
                int currentStack = typo.GetBuffStack();
                int activeRound = typo.GetBuffActiveRound();

                if (!MotionData.CreatedAbilityEffects.TryGetValue(keyword, out var cachedAbility))
                    return;

                if (activeRound == 1)
                    return;

                if (cachedAbility == null || cachedAbility.effectObj == null)
                    return;

                BuffVfxEntry vfxEntry = FindVfxEntry(__instance, keyword);

                if (vfxEntry != null && !vfxEntry.ActiveOrNot)
                    return;

                var model = __instance._unitModel;
                string buffString = vfxEntry?.Keyword;
                int actualStack = (model != null) ? FindStack(model, buffString, keyword) : currentStack;
                int actualTurn = (model != null) ? FindTurnCount(model, buffString, keyword) : activeRound;

                if (vfxEntry != null)
                {
                    if (actualStack < vfxEntry.StackThreshold)
                        return;
                    if (actualTurn < vfxEntry.TurnThreshold)
                        return;
                }
                else if (actualStack <= 0)
                {
                    return;
                }

                CreateOrRefreshAura(__instance, keyword, cachedAbility, vfxEntry);
            }
            catch (Exception ex)
            {
                Logger.LogError($"[BuffAura] Error in TriggerBuffVFX: {ex}");
            }
        }

        [HarmonyPatch(typeof(BattleUnitView), nameof(BattleUnitView.OnRoundStart))]
        [HarmonyPostfix]
        public static void OnRoundStart_ManageAuras(BattleUnitView __instance)
        {
            SyncAurasForView(__instance);
        }

        [HarmonyPatch(typeof(StageController), nameof(StageController.StartRound))]
        [HarmonyPostfix]
        public static void StartRound_SyncAllAuras()
        {
            var views = UnityEngine.Object.FindObjectsOfType<BattleUnitView>();
            foreach (var view in views)
            {
                if (view == null) continue;
                SyncAurasForView(view);
            }
        }

        private static void SyncAurasForView(BattleUnitView view)
        {
            var effects = view._effects_ability;
            var model = view._unitModel;

            if (model == null && effects == null)
                return;

            if (effects != null)
            {
                for (int i = effects.Count - 1; i >= 0; i--)
                {
                    var effect = effects[i];
                    if (effect == null || effect.effectObj == null)
                        effects.RemoveAt(i);
                }
            }

            if (model == null)
                return;

            foreach (var kvp in MotionData.CreatedAbilityEffects)
            {
                BUFF_UNIQUE_KEYWORD keyword = kvp.Key;
                var cachedAbility = kvp.Value;
                if (cachedAbility == null || cachedAbility.effectObj == null) continue;

                BuffVfxEntry vfxEntry = FindVfxEntry(view, keyword);
                string buffString = vfxEntry?.Keyword;
                int currentStack = FindStack(model, buffString, keyword);
                int currentTurn = FindTurnCount(model, buffString, keyword);

                bool shouldHaveAura;
                if (vfxEntry != null)
                    shouldHaveAura = vfxEntry.ActiveOrNot
                        && currentStack >= vfxEntry.StackThreshold
                        && currentTurn >= vfxEntry.TurnThreshold;
                else
                    shouldHaveAura = currentStack > 0;

                Effect_Ability existing = null;
                if (effects != null)
                {
                    foreach (var effect in effects)
                    {
                        if (effect != null && effect.keyword == keyword)
                        { existing = effect; break; }
                    }
                }

                if (shouldHaveAura && existing == null)
                    CreateOrRefreshAura(view, keyword, cachedAbility, vfxEntry);
                else if (!shouldHaveAura && existing != null)
                    DestroyAura(view, existing);
                else if (shouldHaveAura && existing != null)
                {
                    if (existing.effectObj != null && !existing.effectObj.activeSelf)
                        existing.effectObj.SetActive(true);
                    foreach (var ps in existing.effectObj.GetComponentsInChildren<ParticleSystem>())
                    { ps.Clear(); ps.Play(); }
                }
            }
        }

        private static BuffVfxEntry FindVfxEntry(BattleUnitView view, BUFF_UNIQUE_KEYWORD keyword)
        {
            string appearanceID = view?._unitModel?.GetAppearanceID();
            if (string.IsNullOrEmpty(appearanceID))
                return null;
            if (!MotionData.BuffVfxEntries.TryGetValue(appearanceID, out var entries))
                return null;
            foreach (var e in entries)
                if (e.ParsedKeyword == keyword)
                    return e;
            return null;
        }

        private static void CreateOrRefreshAura(BattleUnitView view, BUFF_UNIQUE_KEYWORD keyword,
            Effect_Ability cachedAbility, BuffVfxEntry vfxEntry)
        {
            foreach (var effect in view._effects_ability)
            {
                if (effect != null && effect.keyword == keyword)
                {
                    if (effect.effectObj != null)
                    {
                        if (!effect.effectObj.activeSelf)
                            effect.effectObj.SetActive(true);
                        foreach (var ps in effect.effectObj.GetComponentsInChildren<ParticleSystem>())
                        { ps.Clear(); ps.Play(); }
                    }
                    return;
                }
            }

            GameObject instance = UnityEngine.Object.Instantiate(cachedAbility.effectObj);
            var ability = new Effect_Ability
            {
                keyword = cachedAbility.keyword,
                effectObj = instance,
                IsSetOverrideDie = cachedAbility.IsSetOverrideDie
            };

            bool isFront = vfxEntry?.IsFront
                ?? (MotionData.BuffAuraIsFront.TryGetValue(keyword, out var f) && f);

            instance.transform.SetParent(isFront
                ? view.viewEffectRootDirection
                : view.viewEffectRootBack);
            instance.transform.localPosition = Vector3.zero;
            instance.transform.localRotation = Quaternion.identity;
            instance.transform.localScale =
                Vector3.one * view.Appearance.charInfo.transform_Height.localPosition.y * 0.25f;

            view._effects_ability.Add(ability);
            instance.SetActive(false);
            instance.SetActive(true);
        }

        private static void DestroyAura(BattleUnitView view, Effect_Ability effect)
        {
            if (effect == null) return;
            if (effect.effectObj != null)
            {
                effect.effectObj.SetActive(false);
                UnityEngine.Object.Destroy(effect.effectObj);
            }
            view._effects_ability.Remove(effect);
        }

        private static int FindStack(BattleUnitModel model, string buffStringName, BUFF_UNIQUE_KEYWORD fallbackKeyword)
        {
            if (model == null) return 0;

            var allBuffs = model.GetBuffAll();
            if (allBuffs == null || allBuffs.Count == 0)
                return 0;

            for (int i = 0; i < allBuffs.Count; i++)
            {
                var buff = allBuffs[i];
                if (buff == null) continue;
                if (buff._activeRound != 0) continue;
                string buffName = buff._mainKeyword.ToString();
                if (!string.IsNullOrEmpty(buffStringName) && buffName == buffStringName)
                    return buff._stack;
            }

            for (int i = 0; i < allBuffs.Count; i++)
            {
                var buff = allBuffs[i];
                if (buff == null) continue;
                if (buff._activeRound != 0) continue;
                if (buff._mainKeyword == fallbackKeyword)
                    return buff._stack;
            }

            return 0;
        }

        private static int FindTurnCount(BattleUnitModel model, string buffStringName, BUFF_UNIQUE_KEYWORD fallbackKeyword)
        {
            if (model == null) return 0;

            var allBuffs = model.GetBuffAll();
            if (allBuffs == null || allBuffs.Count == 0)
                return 0;

            for (int i = 0; i < allBuffs.Count; i++)
            {
                var buff = allBuffs[i];
                if (buff == null) continue;
                if (buff._activeRound != 0) continue;
                string buffName = buff._mainKeyword.ToString();
                if (!string.IsNullOrEmpty(buffStringName) && buffName == buffStringName)
                    return buff._turn;
            }

            for (int i = 0; i < allBuffs.Count; i++)
            {
                var buff = allBuffs[i];
                if (buff == null) continue;
                if (buff._activeRound != 0) continue;
                if (buff._mainKeyword == fallbackKeyword)
                    return buff._turn;
            }

            return 0;
        }
    }
}
