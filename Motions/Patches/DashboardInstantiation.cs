using BattleUI;
using ModularSkillScripts;
using UnityEngine;

namespace Motions
{
    public class DashboardInstantiation : IModularConsequence
    {
        private static GameObject GetVFX(Transform parent, string prefabName)
        {
            for (int i = 0; i < parent.childCount; i++)
            {
                Transform child = parent.GetChild(i);

                if (child.name == prefabName + "(Clone)")
                    return child.gameObject;
            }

            return null;
        }

        public void ExecuteConsequence(ModularSA modular, string section, string circledSection, string[] circles)
        {
            var targets = modular.GetTargetModelList(circles[0]);
            if (targets.Count == 0) return;

            int slotNum = modular.GetNumFromParamString(circles[1]);
            bool isActive = modular.GetBoolFromParamString(circles[2]);
            string vfxName = circles[3];

            bool topSlot = false;
            if (circles.Length > 4)
            {
                topSlot = circles[4] == "top";
            }

            int posX = 0;
            int posY = 0;
            int posZ = 0;

            if (circles.Length > 5)
            {
                posX = modular.GetNumFromParamString(circles[5]);
            }
            if (circles.Length > 6)
            {
                posY = modular.GetNumFromParamString(circles[6]);
            }
            if (circles.Length > 7)
            {
                posZ = modular.GetNumFromParamString(circles[7]);
            }
            if (!MotionData.createdDashboardAssets.TryGetValue(vfxName, out var cachedPrefab))
                return;

            foreach (var unit in targets)
            {
                var sinActionSlot = SingletonBehavior<BattleUIRoot>.Instance.NewOperationController.GetSinActionSlot(unit.GetSinActionList()[slotNum]);
                Transform parent = topSlot? sinActionSlot.FirstSinSlot.rect.transform: sinActionSlot.SecondSinSlot.rect.transform;
                GameObject instance = GetVFX(parent, cachedPrefab.name);

                if (instance == null)
                {
                    if (!isActive)
                        continue;

                    instance = UnityEngine.Object.Instantiate(cachedPrefab);
                    instance.transform.SetParent(parent, false);
                    instance.transform.localScale = Vector3.one;
                    instance.transform.position += new Vector3(posX, posY, posZ);
                }
                instance.SetActive(isActive);
            }
        }
    }
}