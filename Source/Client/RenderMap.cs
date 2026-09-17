using HarmonyLib;
using OCUnion;
using RimWorld.Planet;
using RimWorldOnlineCity;
using System;
using System.Collections;
using UnityEngine;
using Verse;

namespace MapRenderer
{
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

        public static bool IsRendering
        {
            get => isRendering;
            set => isRendering = value;
        }

        public RenderMap() { }

        public void Initialize(Map bymap)
        {
            if (bymap == null)
            {
                bymap = Find.CurrentMap;
            }

            map = bymap;

            if (map == null)
            {
                viewWidth = 0;
                viewHeight = 0;
                return;
            }

            viewWidth = map.Size.x * SettingsPixelOnCell;
            viewHeight = map.Size.z * SettingsPixelOnCell;
        }

        public void Render()
        {
            if (map == null)
            {
                Log.Warning("RenderMap: map is null.");
                return;
            }

            if (viewWidth <= 0 || viewHeight <= 0)
            {
                Log.Warning("RenderMap: недійсний розмір рендеру " + viewWidth + "x" + viewHeight);
                return;
            }

            if (IsRendering)
            {
                Log.Warning("RenderMap: інший процес рендерингу вже виконується.");
                return;
            }

            Find.CameraDriver.StartCoroutine(Renderer());
        }

        private IEnumerator Renderer()
        {
            yield return new WaitForFixedUpdate();

            IsRendering = true;

            Camera localCamera = null;
            CameraDriver camDriver = null;
            float rememberedFarClipPlane = 0f;

            // Запам'ятовуємо стан гри та камери
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

            localCamera = Find.Camera;
            camera = localCamera;

            if (localCamera != null)
            {
                camDriver = localCamera.GetComponent<CameraDriver>();
                if (camDriver != null)
                {
                    camDriver.enabled = false;
                    rememberedFarClipPlane = localCamera.farClipPlane;

                    var camViewRect = camDriver.CurrentViewRect;
                    var camRectMinX = Math.Min(0, camViewRect.minX);
                    var camRectMinZ = Math.Min(0, camViewRect.minZ);
                    var camRectMaxX = Math.Max(map.Size.x, camViewRect.maxX);
                    var camRectMaxZ = Math.Max(map.Size.z, camViewRect.maxZ);

                    var camDriverTraverse = Traverse.Create(camDriver);
                    camDriverTraverse.Field("lastViewRect")
                        .SetValue(CellRect.FromLimits(camRectMinX, camRectMinZ, camRectMaxX, camRectMaxZ));
                    camDriverTraverse.Field("lastViewRectGetFrame")
                        .SetValue(Time.frameCount);
                }
            }

            // try-finally без секції catch дозволяє використання yield return у C#
            try
            {
                yield return RenderCurrentView();
            }
            finally
            {
                // Відновлення стану гри та налаштувань камери
                try
                {
                    if (localCamera != null)
                    {
                        localCamera.farClipPlane = rememberedFarClipPlane;
                    }

                    if (camDriver != null)
                    {
                        camDriver.SetRootPosAndSize(rememberedRootPos, rememberedRootSize);
                        camDriver.enabled = true;
                    }
                }
                catch (Exception ex)
                {
                    Loger.Log("RenderMap: помилка відновлення камери: " + ex);
                }

                try
                {
                    var playSettings = Find.PlaySettings;
                    if (playSettings != null)
                    {
                        playSettings.showZones = rememberedShowZones;
                        playSettings.showRoofOverlay = rememberedShowRoofOverlay;
                        playSettings.showFertilityOverlay = rememberedShowFertilityOverlay;
                        playSettings.showTerrainAffordanceOverlay = rememberedShowTerrainAffordanceOverlay;
                        playSettings.showPollutionOverlay = rememberedShowPollutionOverlay;
                        playSettings.showTemperatureOverlay = rememberedShowTemperatureOverlay;
                    }
                }
                catch (Exception ex)
                {
                    Loger.Log("RenderMap: помилка відновлення PlaySettings: " + ex);
                }

                try
                {
                    if (rememberedWorldRendered)
                    {
                        CameraJumper.TryShowWorld();
                    }
                }
                catch (Exception ex)
                {
                    Loger.Log("RenderMap: помилка відновлення світу: " + ex);
                }

                try
                {
                    if (switchedMap && rememberedMap != null)
                    {
                        Current.Game.CurrentMap = rememberedMap;
                    }
                }
                catch (Exception ex)
                {
                    Loger.Log("RenderMap: помилка відновлення карти: " + ex);
                }

                ReleaseRenderTexture();
                IsRendering = false;
            }

            // Формування зворотного виклику зображення
            if (tempTexture != null)
            {
                Texture2D textureForEncoding = tempTexture;
                tempTexture = null;

                Func<byte[]> getImage = () =>
                {
                    if (textureForEncoding == null)
                    {
                        Loger.Log("RenderMap: текстура null під час JPG-кодування.");
                        return null;
                    }

                    try
                    {
                        var encodeStart = DateTime.UtcNow;
                        byte[] encodedImage = textureForEncoding.EncodeToJPG(SettingsQuality);
                        var encodeTime = (DateTime.UtcNow - encodeStart).TotalMilliseconds;

                        Loger.Log(
                            "RenderMap: EncodeToJPG " +
                            viewWidth + "x" + viewHeight +
                            ", quality=" + SettingsQuality +
                            ", " + encodeTime.ToString("F2") + " ms, " +
                            (encodedImage != null ? encodedImage.Length : 0) + " bytes"
                        );

                        return encodedImage;
                    }
                    catch (Exception ex)
                    {
                        Loger.Log("RenderMap: помилка EncodeToJPG: " + ex);
                        return null;
                    }
                    finally
                    {
                        var tex = textureForEncoding;
                        textureForEncoding = null;
                        ModBaseData.RunMainThread(() => DestroyTexture(tex));
                    }
                };

                try
                {
                    if (ImageReady != null)
                    {
                        ImageReady(getImage);
                    }
                    else
                    {
                        DestroyTexture(textureForEncoding);
                    }
                }
                catch (Exception ex)
                {
                    Loger.Log("RenderMap: помилка ImageReady: " + ex);
                    DestroyTexture(textureForEncoding);
                }
            }

            yield return null;
        }

        private void RestoreCamera()
        {
            RenderTexture.active = null;
            if (camera != null)
            {
                camera.targetTexture = null;
            }
        }

        private void SetCamera()
        {
            if (camera != null)
            {
                camera.targetTexture = rt;
            }
            RenderTexture.active = rt;
        }

        private IEnumerator RenderCurrentView()
        {
            yield return new WaitForEndOfFrame();

            try
            {
                var cameraPosX = map.Size.x / 2;
                var cameraPosZ = map.Size.z / 2;
                var orthographicSize = cameraPosZ;

                var cameraBasePos = new Vector3(
                    cameraPosX,
                    15f + (orthographicSize - 11f) / 49f * 50f,
                    cameraPosZ
                );

                camera.orthographicSize = orthographicSize;
                camera.farClipPlane = cameraBasePos.y + 6.5f;

                SetCamera();

                if (SettingsShowWeather)
                {
                    map.weatherManager.DrawAllWeather();
                }

                camera.transform.position = new Vector3(
                    cameraBasePos.x,
                    cameraBasePos.y,
                    cameraBasePos.z
                );

                var renderStart = DateTime.UtcNow;
                camera.Render();
                var renderTime = (DateTime.UtcNow - renderStart).TotalMilliseconds;

                var readPixelsStart = DateTime.UtcNow;
                tempTexture.ReadPixels(new Rect(0, 0, viewWidth, viewHeight), 0, 0, false);
                var readPixelsTime = (DateTime.UtcNow - readPixelsStart).TotalMilliseconds;

                Loger.Log(
                    "RenderMap: " + viewWidth + "x" + viewHeight +
                    ", Camera.Render=" + renderTime.ToString("F2") +
                    " ms, ReadPixels=" + readPixelsTime.ToString("F2") + " ms"
                );
            }
            catch (Exception exp)
            {
                Loger.Log("RenderMap: помилка RenderCurrentView: " + exp);
            }
            finally
            {
                RestoreCamera();
            }

            yield return null;
        }

        private void ReleaseRenderTexture()
        {
            if (rt == null) return;

            try
            {
                RenderTexture.active = null;
                RenderTexture.ReleaseTemporary(rt);
            }
            catch (Exception ex)
            {
                Loger.Log("RenderMap: помилка ReleaseRenderTexture: " + ex);
            }
            finally
            {
                rt = null;
            }
        }

        private static void DestroyTexture(Texture2D texture)
        {
            if (texture == null) return;

            try
            {
                UnityEngine.Object.Destroy(texture);
            }
            catch (Exception ex)
            {
                Loger.Log("RenderMap: помилка знищення текстури: " + ex);
            }
        }
    }
}
