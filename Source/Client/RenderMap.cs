using HarmonyLib;
using OCUnion;
using RimWorld.Planet;
using RimWorldOnlineCity;
using System;
using System.Collections;
using System.Reflection;
using UnityEngine;
using Verse;

namespace MapRenderer
{
    // Автор: AaronCRobinson https://github.com/AaronCRobinson/MapRenderer
    public class RenderMap : MonoBehaviour
    {
        private const int defaultPixelOnCell = 15;
        private const int defaultQuality = 80;

        public int SettingsPixelOnCell = defaultPixelOnCell;
        public int SettingsQuality = defaultQuality;
        public bool SettingsShowWeather = true;
        public Action<Func<byte[]>> ImageReady;

        private static bool isRendering;

        private Camera camera;
        private Map map;

        private Map rememberedMap;
        private bool switchedMap;
        private Vector3 rememberedRootPos;
        private float rememberedRootSize;
        private bool rememberedWorldRendered;
        private bool rememberedShowZones;
        private bool rememberedShowRoofOverlay;
        private bool rememberedShowFertilityOverlay;
        private bool rememberedShowTerrainAffordanceOverlay;
        private bool rememberedShowPollutionOverlay;
        private bool rememberedShowTemperatureOverlay;

        private int viewWidth;
        private int viewHeight;

        private RenderTexture rt;
        private Texture2D tempTexture;

        // ВИПРАВЛЕННЯ: поля lastViewRect та lastViewRectGetFrame є статичними у CameraDriver.
        // Використовуємо безпечний FieldInfo замість FieldRefAccess.
        private static readonly FieldInfo LastViewRectField =
            AccessTools.Field(typeof(CameraDriver), "lastViewRect");
        private static readonly FieldInfo LastViewRectGetFrameField =
            AccessTools.Field(typeof(CameraDriver), "lastViewRectGetFrame");

        public static bool IsRendering { get => isRendering; set => isRendering = value; }

        public RenderMap() { }

        public void Initialize(Map bymap)
        {
            if (bymap == null) bymap = Find.CurrentMap;
            map = bymap;
            viewWidth = map.Size.x * SettingsPixelOnCell;
            viewHeight = map.Size.z * SettingsPixelOnCell;
        }

        public void Render() => Find.CameraDriver.StartCoroutine(Renderer());

        private IEnumerator Renderer()
        {
            yield return new WaitForFixedUpdate();

            IsRendering = true;

            switchedMap = false;
            rememberedMap = Find.CurrentMap;
            if (map != rememberedMap)
            {
                switchedMap = true;
                Current.Game.CurrentMap = map;
            }
            rememberedWorldRendered = WorldRendererUtility.WorldRenderedNow;
            if (rememberedWorldRendered)
            {
                CameraJumper.TryHideWorld();
            }

            var settings = Find.PlaySettings;
            rememberedShowZones = settings.showZones;
            rememberedShowRoofOverlay = settings.showRoofOverlay;
            rememberedShowFertilityOverlay = settings.showFertilityOverlay;
            rememberedShowTerrainAffordanceOverlay = settings.showTerrainAffordanceOverlay;
            rememberedShowPollutionOverlay = settings.showPollutionOverlay;
            rememberedShowTemperatureOverlay = settings.showTemperatureOverlay;

            settings.showZones = false;
            settings.showRoofOverlay = false;
            settings.showFertilityOverlay = false;
            settings.showTerrainAffordanceOverlay = false;
            settings.showPollutionOverlay = false;
            settings.showTemperatureOverlay = false;

            rememberedRootPos = map.rememberedCameraPos.rootPos;
            rememberedRootSize = map.rememberedCameraPos.rootSize;

            rt = RenderTexture.GetTemporary(viewWidth, viewHeight, 24);
            tempTexture = new Texture2D(viewWidth, viewHeight, TextureFormat.RGB24, false);
            camera = Find.Camera;
            var camDriver = camera.GetComponent<CameraDriver>();
            camDriver.enabled = false;
            var rememberedFarClipPlane = camera.farClipPlane;

            var camViewRect = camDriver.CurrentViewRect;
            var camRectMinX = Math.Min(0, camViewRect.minX);
            var camRectMinZ = Math.Min(0, camViewRect.minZ);
            var camRectMaxX = Math.Max(map.Size.x, camViewRect.maxX);
            var camRectMaxZ = Math.Max(map.Size.z, camViewRect.maxZ);

            // Прямий запис статичних полів CameraDriver без Traverse
            LastViewRectField?.SetValue(null, CellRect.FromLimits(camRectMinX, camRectMinZ, camRectMaxX, camRectMaxZ));
            LastViewRectGetFrameField?.SetValue(null, Time.frameCount);

            yield return RenderCurrentView();

            camera.farClipPlane = rememberedFarClipPlane;
            camDriver.SetRootPosAndSize(rememberedRootPos, rememberedRootSize);
            camDriver.enabled = true;

            // Повертаємо RenderTexture у пул без виклику помилкового Destroy(rt)
            RenderTexture.ReleaseTemporary(rt);
            rt = null;

            Find.PlaySettings.showZones = rememberedShowZones;
            Find.PlaySettings.showRoofOverlay = rememberedShowRoofOverlay;
            Find.PlaySettings.showFertilityOverlay = rememberedShowFertilityOverlay;
            Find.PlaySettings.showTerrainAffordanceOverlay = rememberedShowTerrainAffordanceOverlay;
            Find.PlaySettings.showPollutionOverlay = rememberedShowPollutionOverlay;
            Find.PlaySettings.showTemperatureOverlay = rememberedShowTemperatureOverlay;

            if (rememberedWorldRendered)
            {
                CameraJumper.TryShowWorld();
            }
            if (switchedMap)
            {
                Current.Game.CurrentMap = rememberedMap;
            }

            Func<byte[]> getImage = () =>
            {
                byte[] encodedImage = null;
                // Гарантуємо виконання кодування та видалення текстури в головному потоці Unity
                ModBaseData.RunMainThreadSync(() =>
                {
                    try
                    {
                        if (tempTexture != null)
                        {
                            encodedImage = tempTexture.EncodeToJPG(SettingsQuality);
                            UnityEngine.Object.Destroy(tempTexture);
                            tempTexture = null;
                        }
                    }
                    catch (Exception ex)
                    {
                        Loger.Log("RenderMap EncodeToJPG Exception: " + ex.Message, Loger.LogLevel.WARNING);
                    }
                });
                return encodedImage;
            };

            if (ImageReady != null)
            {
                ImageReady(getImage);
            }
            else if (tempTexture != null)
            {
                UnityEngine.Object.Destroy(this.tempTexture);
                tempTexture = null;
            }

            IsRendering = false;

            yield return null;
        }

        private void RestoreCamera() => RenderTexture.active = this.camera.targetTexture = null;

        private void SetCamera() => RenderTexture.active = this.camera.targetTexture = this.rt;

        private IEnumerator RenderCurrentView()
        {
#if DEBUG
            Log.Message("Start of RenderCurrentView");
#endif
            yield return new WaitForEndOfFrame();
#if DEBUG
            Log.Message("After WaitForEndOfFrame");
#endif
            try
            {
                var cameraPosX = map.Size.x / 2;
                var cameraPosZ = map.Size.z / 2;
                var orthographicSize = cameraPosZ;
                var cameraBasePos = new Vector3(cameraPosX, 15f + (orthographicSize - 11f) / 49f * 50f, cameraPosZ);
                camera.orthographicSize = orthographicSize;
                camera.farClipPlane = cameraBasePos.y + 6.5f;
                SetCamera();
                RenderTexture.active = rt;
                if (SettingsShowWeather)
                {
                    map.weatherManager.DrawAllWeather();
                }
                camera.transform.position = new Vector3(cameraBasePos.x, cameraBasePos.y, cameraBasePos.z);
                camera.Render();
#if DEBUG
                Log.Message("After Render");
#endif
                tempTexture.ReadPixels(new Rect(0, 0, viewWidth, viewHeight), 0, 0, false);

                RenderTexture.active = null;
                RestoreCamera();
#if DEBUG
                Log.Message("End of RenderCurrentView");
#endif
            }
            catch (Exception exp)
            {
                Log.Error(exp.Message);
            }
        }
    }
}