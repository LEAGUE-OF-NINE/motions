using CustomAssetLoader.Managers;
using HarmonyLib;
using Il2CppInterop.Runtime;
using Lethe;
using ModularSkillScripts;
using SD;
using System;
using System.Collections.Generic;
using TerraFX.Interop.Windows;
using UnityEngine;
using UnityEngine.Timeline;
using View;

namespace Motions
{
    public static class CustomBattleSkillView_er
    {
        public static Dictionary<string, string> skillIds_duelViewers = new();
        //public static Dictionary<string, Il2CppSystem.Collections.Generic.List<BattleUnitView>> skillids_and_followers = new();
        public static Dictionary<string, Il2CppSystem.Collections.Generic.List<string>> skillids_and_followers = new();
        [HarmonyPatch(typeof(BattleUnitView), nameof(BattleUnitView.InitializeViewAsync))]
        [HarmonyPostfix]
        public static void Init_SkillViewer_POSTFIX(BattleUnitView __instance)
        {
            List<string> keysFromDict = [.. __instance._battleSkillViewers.Keys];
            foreach (var skillIdString in keysFromDict.ToArray())
            {
                Int32.TryParse(skillIdString, out int skillIdInt);
                var skillModel = __instance._battleSkillViewers[skillIdString].GetSkillModel();
                foreach (var abilityData in StaticDataManager.Instance._skillList.GetData(skillIdInt).GetAbilityScript(skillModel.GetGaksungLevel()))
                {
                    if (abilityData.scriptName.StartsWith("CustomBattleSkillView"))
                    {
                        var splitName = abilityData.scriptName.Split(':');
                        var battleSkillViewerTypeName = splitName[1];
                        var battleSkillViewTypeName = splitName[2];
                        var battleDuelViewerTypeName = splitName[3];
                        skillIds_duelViewers.TryAdd(skillIdString, battleDuelViewerTypeName);
                        var collectedTypeViewer = Il2CppSystem.Activator.CreateInstance(Util.GetTypeFromClassName($"{battleSkillViewerTypeName}"), [__instance, skillIdString, skillModel, null, skillModel.GetSkillActionScript()]);
                        var collectedTypeView = Activator.CreateInstance("Assembly-CSharp", $"{battleSkillViewTypeName}");
                        BattleSkillViewBase saved = new();
                        saved = __instance._battleSkillViewers[skillIdString]._skillViewBase;
                        saved = collectedTypeView.Unwrap() as BattleSkillViewBase;
                        saved.SetViewType(collectedTypeViewer.TryCast<BattleSkillViewer>());
                        __instance._battleSkillViewers[skillIdString] = collectedTypeViewer.TryCast<BattleSkillViewer>();
                        __instance._battleSkillViewers[skillIdString]._skillViewBase = saved;
                        break;
                    }
                }
            }
        }

        // updates the skill uis position
        [HarmonyPatch(typeof(CharacterAppearance), nameof(CharacterAppearance.OnAnimEvent))]
        [HarmonyPrefix]
        public static void OnAnimEvent_Postfix(CharacterAppearance __instance, int i)
        {
            switch (i)
            {
                case 6001:
                    __instance._battleUnitView._uiManager.SetUISize(-1);
                    return;
            }
        }

        [HarmonyPatch(typeof(BattleActionViewManager), nameof(BattleActionViewManager.AddDuelAction))]
        [HarmonyPrefix]
        public static bool BattleActionView_DuelAdd(BattleActionViewManager __instance, BattleActionLog log, int actorInstanceID)
        {
            int trueInstanceId = 0;
            if (BattleObjectManager.Instance.GetModel(actorInstanceID)._faction == UNIT_FACTION.PLAYER)
            {
                trueInstanceId = actorInstanceID;
            }
            else
            {
                trueInstanceId = log.GetOpponentCharacterInstanceID(actorInstanceID);
            }

            int skillID2 = log.GetSystemLog().GetOpponentBehaviour_Before(actorInstanceID).GetSkillID();

            if (!skillIds_duelViewers.ContainsKey(skillID2.ToString()))
                return true;

            GameObject gameObject = new GameObject();
            gameObject.name = "DuelViewerCUSTOM";

            int skillID = log.GetSystemLog().GetBehaviourByInstanceID_Before(actorInstanceID).GetSkillID();

            //we don't care about viewtypes
            VIEW_TYPE viewType = SingletonBehavior<BattleObjectManager>.Instance.GetView(actorInstanceID).GetSkillViewer(skillID).GetViewType();
            VIEW_TYPE viewType2 = SingletonBehavior<BattleObjectManager>.Instance.GetView(log.GetSystemLog().GetOpponentCharacterInfo(actorInstanceID).instanceID).GetSkillViewer(skillID2).GetViewType();
            Il2CppSystem.Collections.Generic.List<VIEW_TYPE> listTemp = new();
            listTemp.Add(viewType);
            listTemp.Add(viewType2);

            VIEW_TYPE view = Singleton<BattleActionViewManager>.Instance.GetView(listTemp, isDuel: true);


            SkillStaticData selfSkillData = StaticDataManager.Instance.SkillList.GetData(skillID2);

            BattleDuelViewer battleDuelViewer = null;
            skillIds_duelViewers.TryGetValue(skillID2.ToString(), out string typeName);
            Type componentType = AccessTools.TypeByName(typeName);
            if (componentType != null)
            {
                Il2CppSystem.Type il2cppType = Il2CppType.From(componentType);
                int selfSpecialDuelIndex = selfSkillData?.GetSpecialDuelIndex() ?? -1;
                if (selfSpecialDuelIndex < 0)
                {
                    gameObject.AddComponent(il2cppType);
                    battleDuelViewer = gameObject.GetComponent<BattleDuelViewer>();
                }
                else
                {
                    var specialViewer = gameObject.AddComponent<BattleDuelViewer_Special_AB>();
                    battleDuelViewer = specialViewer;
                }
            }
            battleDuelViewer.Init(log, trueInstanceId, viewType, viewType2);
            battleDuelViewer.SetFocusDuel();
            UnityEngine.Debug.LogError($"DuelTest: {skillID} : {skillID2} : {viewType.ToString()} : {viewType2.ToString()} : {view}");

            __instance._duelViewList.Add(new DuelViewerInfo
            {
                dv = battleDuelViewer,
                viewType = view
            });
            return false;
        }


        [HarmonyPatch(typeof(BattleSkillViewBase), nameof(BattleSkillViewBase.Init))]
        [HarmonyPostfix]
        private static void Postfix_SkillModel_OnBattleStart(BattleSkillViewBase __instance, BattleActionLog actionLog, BattleSkillViewer viewer)
        {
            string skillIdtrue = viewer._currentSkillModel.GetID().ToString();

            MOTION_DETAIL backupMotion = __instance._skillViewer._currentSkillModel.GetSkillMotion();

            if (!skillids_and_followers.ContainsKey(skillIdtrue))
            {
                __instance._curSkillMotion = backupMotion;
                return;
            }

            // list of followers
            skillids_and_followers.TryGetValue(skillIdtrue, out Il2CppSystem.Collections.Generic.List<string> listOfStringFollowViews);

            // clearing the followviews just in case
            if (__instance._followViews != null)
                __instance._followViews.Clear();
            else
                __instance._followViews = new();


            // adding followers in another list instead of directly in __instance.followViews, we don't want to see other units in our skill that failed the check
            Il2CppSystem.Collections.Generic.List<BattleUnitView> forSkillFollows = new();
            foreach (var lmao in listOfStringFollowViews)
            {
                int unitId = int.Parse(lmao.Substring(2));
                BattleUnitView activeUnit = SingletonBehavior<BattleObjectManager>.Instance.GetViewByUnitID(unitId);
                if (!activeUnit.BattleCharacterState.IsDead)
                {
                    forSkillFollows.Add(activeUnit);
                }
                else
                {
                    // if check is failed we're clearing the list and returning to the default skill motion we see in json 
                    forSkillFollows.Clear();
                    __instance._curSkillMotion = backupMotion;
                    return;
                }
            }

            // replacing the list of it
            __instance._followViews = forSkillFollows;

            // empty tween for some reason [FROM DECOMP]
            __instance.followMoveTween = new();
            __instance.followMoveTween.Add(null);

            // getting timelines from the CustomAssetLoader, I'll try to PR the public changes later
            CustomAssetLoader.Managers.BundleManager.KeyMap.TryGetValue($"Combo_{skillIdtrue}", out var bundleKey);
            CustomAssetLoader.Managers.BundleManager.TryLoadBundleForEntry(bundleKey, out var bundleAsset);
            CharacterFollowTimelineAssets comboObject = bundleAsset.LoadAsset<CharacterFollowTimelineAssets>(bundleKey.assetPath);

            // checking existance of S21 and adding obj [OTHERWISE FOR SOME REASON IT FAILS OTHER COINS] and then clearing them
            if (__instance._attackerView._curAppearance.GetMotion(MOTION_DETAIL.S21) == null)
            {
                __instance._attackerView._curAppearance.AddMotion(MOTION_DETAIL.S21);
                __instance._attackerView._curAppearance.GetMotion(MOTION_DETAIL.S21).damageObj = new();
                __instance._attackerView._curAppearance.GetMotion(MOTION_DETAIL.S21).motionObjects = new();
            }
            // replacing the timelines of S21, hoping that no enemy or id have them
            __instance._attackerView._curAppearance.GetMotion(MOTION_DETAIL.S21).timelineAssets = comboObject.timelines;

            // forcing the S21 and focus (ENCOUNTER VIEW), I don't want to see a lot of different combos during the BATTLE VIEW
            __instance._curSkillMotion = MOTION_DETAIL.S21;
            __instance._skillViewType = VIEW_TYPE.ENCOUNTER;

            // adding|gettin crucial component that autobinds tracks on the timelines 
            // YOUR COMMANDER UNIT SHOULD HAVE THIS COMPONENT BEFORE COMBAT STARTS
            var controller = __instance._attackerView._curAppearance.GetOrAddComponent<CharacterAppearanceFollowController>();
            controller.Initialize(__instance._attackerView._curAppearance);
            controller.ResetFollowAppearance();
            __instance._attackerView._curAppearance.Initialize(__instance._attackerView._curAppearance._battleUnitView);
            controller.SetFollowAppearance(__instance._followViews);
            //var resiver = __instance._attackerView._curAppearance.GetOrAddComponent<CharacterApperacneResiver>();
            //resiver.followController = controller;
        }

        [HarmonyPatch(typeof(CharacterAppearanceFollowController), nameof(CharacterAppearanceFollowController.AutoBindByClipName))]
        [HarmonyPostfix]
        private static void AutoBindByClipName_Prefix(CharacterAppearanceFollowController __instance)
        {
            if (__instance.gameObject.GetComponent<CharacterApperacneResiver>().followController == null)
            {
                UnityEngine.Debug.LogError($"FollowController is null for {__instance.gameObject.name}");
                __instance.gameObject.GetComponent<CharacterApperacneResiver>().followController = __instance;
            }
        }

        // COMBO: clearing the list on round start clear [new battle/new turn/whatever]
        [HarmonyPatch(typeof(PassiveDetail), nameof(PassiveDetail.OnRoundStart_After_Event))]
        [HarmonyPostfix]
        private static void Postfix_PassiveDetail_OnRoundStart_After_Event1(BATTLE_EVENT_TIMING timing, PassiveDetail __instance)
        {
            skillids_and_followers.Clear();
        }

        // COMBO: clearing the list on turn end ig just in case
        [HarmonyPatch(typeof(PassiveDetail), nameof(PassiveDetail.OnBattleEnd))]
        [HarmonyPostfix]
        private static void Postfix_PassiveDetail_OnBattleEnd1(BATTLE_EVENT_TIMING timing, PassiveDetail __instance)
        {
            skillids_and_followers.Clear();
        }

        // for some reason requires patching NOTHING for custom EGO
        [HarmonyPatch(typeof(BattleCutScene_EGO), nameof(BattleCutScene_EGO.SetCutSceneMaterial), new Type[] { typeof(BattleSkillViewer), typeof(EgoStaticData), typeof(BattleCutSceneStaticData) })]
        [HarmonyPostfix]
        private static void BattleCutScene_EGO_PostFix(BattleSkillViewBase __instance, BattleSkillViewer viewer, EgoStaticData egoData, BattleCutSceneStaticData cutsceneData) { }

        [HarmonyPatch(typeof(BattleCutScene_EGO), nameof(BattleCutScene_EGO.SetCutSceneMaterial), new Type[] { typeof(BattleSkillViewer), typeof(EgoStaticData), typeof(BattleCutSceneStaticData) })]
        [HarmonyPrefix]
        private static void BattleCutScene_EGO_PreFix(BattleSkillViewBase __instance, BattleSkillViewer viewer, EgoStaticData egoData, BattleCutSceneStaticData cutsceneData) { }

    }
}
