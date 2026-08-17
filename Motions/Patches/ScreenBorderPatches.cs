using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;

namespace Motions
{
    internal class ScreenBorderPatches
    {
        public static bool init = false;
        public static string bundlename = "hi";
        private static Canvas overlayCanvas;
        private static Image overlayImage;
        private static Material loadedMaterial;

        [HarmonyPatch(typeof(StageModel), nameof(StageModel.Init))]
        [HarmonyPostfix]
        public static void FindScriptName(StageStaticData stageinfo, StageModel __instance)
        {
            var scripts = stageinfo.stageScriptList;
            string bundleName = "hi";
            foreach (string script in scripts)  
            {
                if (script.StartsWith("Screenborder_", StringComparison.OrdinalIgnoreCase))
                {
                    bundleName = script.Remove(0, 13);
                    bundlename = bundleName;
                }
            }
        }

        [HarmonyPatch(typeof(BattleObjectManager), nameof(BattleObjectManager.OnRoundStart_Model))]
        [HarmonyPostfix]
        public static void TriggerScreen(BattleObjectManager __instance)
        {
            if (MotionData.screenBorderAssets.ContainsKey(bundlename) && init == false)
            {
                AssetBundle bundle = MotionData.screenBorderAssets[bundlename];
                foreach (var assetName in bundle.AllAssetNames())
                {
                    if (!assetName.EndsWith(".mat", StringComparison.OrdinalIgnoreCase))
                        continue;
                    loadedMaterial = bundle.LoadAsset<Material>($"{assetName}");
                }
                CreateOverlayCanvas();
                SetIntensity(1f);
            }
        }

        private static void CreateOverlayCanvas()
        {
            Logger.LogWarning("Creating overlay image under PerspectiveUI.");

            // 1. Try to find PerspectiveUI directly in the scene hierarchy
            GameObject targetUIObj = GameObject.Find("PerspectiveUI") ?? GameObject.Find("SafeArea");

            GameObject imageObj = new GameObject("Motions_ScreenBorder");

            if (targetUIObj != null)
            {
                Logger.LogInfo($"Found target UI: {targetUIObj.name}. Parenting border image.");
                imageObj.transform.SetParent(targetUIObj.transform, false);

                // Renders behind all other UI components inside PerspectiveUI
                imageObj.transform.SetAsFirstSibling();
            }
            else
            {
                Logger.LogWarning("PerspectiveUI not found! Creating fallback canvas.");
                GameObject canvasObj = new GameObject("Motions_ScreenBorder");
                UnityEngine.Object.DontDestroyOnLoad(canvasObj);

                overlayCanvas = canvasObj.AddComponent<Canvas>();
                overlayCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
                overlayCanvas.sortingOrder = -1; // Sit behind standard overlay canvasses

                CanvasScaler scaler = canvasObj.AddComponent<CanvasScaler>();
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;

                imageObj.transform.SetParent(canvasObj.transform, false);
            }

            overlayImage = imageObj.AddComponent<Image>();
            overlayImage.material = loadedMaterial;

            overlayImage.raycastTarget = false; // don't take inputs

            RectTransform rect = overlayImage.rectTransform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.sizeDelta = Vector2.zero;

            init = true;
        }
        public static void SetIntensity(float intensity)
        {
            if (loadedMaterial != null && loadedMaterial.HasFloat("_Intensity"))
            {
                loadedMaterial.SetFloat("_Intensity", intensity);
            }
        }

        public static void Unload()
        {
            Logger.LogInfo("Unloading screen border.");

            if (overlayCanvas != null)
            {
                UnityEngine.Object.Destroy(overlayCanvas.gameObject);
                overlayCanvas = null;
            }
            init = false;
            overlayImage = null;
            loadedMaterial = null;
            bundlename = "hi";
        }

    }
}
