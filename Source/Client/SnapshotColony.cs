using MapRenderer;
using Model;
using OCUnion;
using OCUnion.Transfer;
using OCUnion.Transfer.Model;
using RimWorld.Planet;
using RimWorldOnlineCity.Services;
using System;
using System.Threading.Tasks;
using Verse;

namespace RimWorldOnlineCity
{
    /// <summary>
    /// Відповідає за автоматичне створення графічного знімка карти колонії опівдні
    /// та його фонову передачу на сервер для перегляду іншими гравцями.
    /// </summary>
    internal class SnapshotColony
    {
        public bool HighQuality = false;
        public bool Background = true;

        // Прапорець фонового завантаження для запобігання перевантаженню мережевого сокета
        private static volatile bool isUploading = false;

        /// <summary>
        /// Повертає true, якщо наразі триває створення знімка або його відправка на сервер.
        /// </summary>
        public static bool IsBusy => RenderMap.IsRendering || isUploading;

        /// <summary>
        /// Запуск процесу автоматичного рендерингу карти поселення.
        /// </summary>
        public void Exec(Settlement settlement)
        {
            ExecStatic(settlement, HighQuality, Background);
        }

        /// <summary>
        /// Статична точка входу для рендерингу карти колонії без зайвих алокацій об'єкта.
        /// </summary>
        public static void ExecStatic(Settlement settlement, bool highQuality = false, bool background = true)
        {
            if (settlement == null || settlement.Map == null)
            {
                return;
            }

            // Захист від накладання: якщо рендер або передача попереднього знімка ще тривають — пропускаємо
            if (IsBusy)
            {
                Loger.Log("SnapshotColony: рендеринг або передача файлу вже триває, пропуск запиту", Loger.LogLevel.INFO);
                return;
            }

            var myWO = UpdateWorldController.GetMyByLocalId(settlement.ID);
            var serverId = myWO?.PlaceServerId ?? 0;
            if (serverId == 0) return;

            var renderMap = new RenderMap();

            // Налаштування якості рендерингу
            if (highQuality)
            {
                renderMap.SettingsPixelOnCell = 30;
                renderMap.SettingsQuality = 86;
            }
            else
            {
                renderMap.SettingsPixelOnCell = 16;
                renderMap.SettingsQuality = 75;
            }

            renderMap.ImageReady = (imageFunc) => SendToServer(imageFunc, serverId);

            renderMap.Initialize(settlement.Map);
            renderMap.Render();
        }

        /// <summary>
        /// Асинхронне отримання байтів кадру та передача файлу на сервер у фоновому потоці.
        /// ОПТИМІЗАЦІЯ: безпечне отримання даних із головного потоку Unity у разі потреби та захист від NRE.
        /// </summary>
        private static void SendToServer(Func<byte[]> getImage, long serverId)
        {
            if (getImage == null || isUploading) return;
            isUploading = true;

            Task.Run(() =>
            {
                try
                {
                    byte[] data = null;
                    try
                    {
                        data = getImage();
                    }
                    catch (Exception ex)
                    {
                        // Якщо виклик методів Unity (EncodeToJPG/Destroy) обмежений для фонових потоків, викликаємо синхронно в Unity
                        Loger.Log($"SnapshotColony: фонове кодування викликало помилку ({ex.Message}), перемикання на головний потік", Loger.LogLevel.DEBUG);
                        ModBaseData.RunMainThreadSync(() =>
                        {
                            try
                            {
                                data = getImage();
                            }
                            catch (Exception innerEx)
                            {
                                Loger.Log($"SnapshotColony MainThread getImage Exception: {innerEx.Message}", Loger.LogLevel.WARNING);
                            }
                        });
                    }

                    if (data == null || data.Length == 0)
                    {
                        Loger.Log("SnapshotColony: отримано порожній масив зображення", Loger.LogLevel.WARNING);
                        return;
                    }

                    var myLogin = SessionClientController.My?.Login;
                    if (string.IsNullOrEmpty(myLogin)) return;

                    var fileKey = myLogin + "@" + serverId;

                    if (Loger.Enable && !MainHelper.OffAllLog)
                    {
                        Loger.Log($"SnapshotColony Send serverId={serverId} bytes={data.Length}");
                    }

                    SessionClientController.Command((connect) =>
                    {
                        try
                        {
                            connect.FileSharingUpload(FileSharingCategory.ColonyScreen, fileKey, data);
                        }
                        catch (Exception ex)
                        {
                            Loger.Log($"SnapshotColony FileSharingUpload Exception: {ex.Message}", Loger.LogLevel.WARNING);
                        }
                    });
                }
                catch (Exception ex)
                {
                    Loger.Log($"SnapshotColony Send Exception: {ex.Message}", Loger.LogLevel.WARNING);
                }
                finally
                {
                    isUploading = false;
                }
            });
        }
    }
}