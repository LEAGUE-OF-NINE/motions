using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Motions
{
    public static class CustomBattleSkillView_er
    {
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
    }
}
