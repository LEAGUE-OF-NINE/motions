using HarmonyLib;
using Il2CppInterop.Runtime;
using System;
using System.Collections.Generic;
using UnityEngine;
using View;

namespace Motions
{
    public static class CustomBattleSkillView_er
    {
        public static Dictionary<string, string> skillIds_duelViewers = new();
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
    }
}
