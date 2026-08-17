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
        private const string ScriptPrefix = "Screenborder_";

        /// <summary>The stage script's bundle name, or NoBundle when this stage asked for none.</summary>
        private const string NoBundle = "hi";

        private static bool _initialized;
        private static string _bundleName = NoBundle;
        private static Canvas _overlayCanvas;
        private static Image _overlayImage;
        private static Material _loadedMaterial;

        [HarmonyPatch(typeof(StageModel), nameof(StageModel.Init))]
        [HarmonyPostfix]
        public static void FindScriptName(StageStaticData stageinfo, StageModel __instance)
        {
            foreach (string script in stageinfo.stageScriptList)
            {
                if (script.StartsWith(ScriptPrefix, StringComparison.OrdinalIgnoreCase))
                    _bundleName = script.Remove(0, ScriptPrefix.Length);
            }
        }

        [HarmonyPatch(typeof(BattleObjectManager), nameof(BattleObjectManager.OnRoundStart_Model))]
        [HarmonyPostfix]
        public static void TriggerScreen(BattleObjectManager __instance)
        {
            if (MotionData.ScreenBorderAssets.ContainsKey(_bundleName) && !_initialized)
            {
                AssetBundle bundle = MotionData.ScreenBorderAssets[_bundleName];
                foreach (var assetName in bundle.AllAssetNames())
                {
                    if (!assetName.EndsWith(".mat", StringComparison.OrdinalIgnoreCase))
                        continue;
                    _loadedMaterial = bundle.LoadAsset<Material>($"{assetName}");
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

                _overlayCanvas = canvasObj.AddComponent<Canvas>();
                _overlayCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
                _overlayCanvas.sortingOrder = -1; // Sit behind standard overlay canvasses

                CanvasScaler scaler = canvasObj.AddComponent<CanvasScaler>();
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;

                imageObj.transform.SetParent(canvasObj.transform, false);
            }

            _overlayImage = imageObj.AddComponent<Image>();
            _overlayImage.material = _loadedMaterial;

            _overlayImage.raycastTarget = false; // don't take inputs

            RectTransform rect = _overlayImage.rectTransform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.sizeDelta = Vector2.zero;

            _initialized = true;
        }
        public static void SetIntensity(float intensity)
        {
            if (_loadedMaterial != null && _loadedMaterial.HasFloat("_Intensity"))
            {
                _loadedMaterial.SetFloat("_Intensity", intensity);
            }
        }

        public static void Unload()
        {
            Logger.LogInfo("Unloading screen border.");

            if (_overlayCanvas != null)
            {
                UnityEngine.Object.Destroy(_overlayCanvas.gameObject);
                _overlayCanvas = null;
            }
            _initialized = false;
            _overlayImage = null;
            _loadedMaterial = null;
            _bundleName = NoBundle;
        }

    }
}
