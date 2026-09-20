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

        /// <summary>
        /// Запуск процесу автоматичного рендерингу карти поселення.
        /// </summary>
        public void Exec(Settlement settlement)
        {
            // Перевірка наявності поселення та завантаженої активної карти
            if (settlement == null || settlement.Map == null)
            {
                Loger.Log("SnapshotColony: поселення або його карта відсутні, скасування знімка", Loger.LogLevel.WARNING);
                return;
            }

            // Захист від накладання: якщо попередній рендер ще триває, новий не запускаємо
            if (RenderMap.IsRendering)
            {
                Loger.Log("SnapshotColony: RenderMap уже виконує рендеринг, пропуск запиту", Loger.LogLevel.INFO);
                return;
            }

            var serverId = UpdateWorldController.GetMyByLocalId(settlement.ID)?.PlaceServerId ?? 0;
            Loger.Log($"SnapshotColony serverId={serverId}");

            if (serverId == 0) return;

            var renderMap = new RenderMap();

            // Логіка вибору якості:
            // HighQuality = true: максимальна чіткість (30 px/клітинка, 86% якість) для красивих знімків на сервері.
            // HighQuality = false: збалансований режим (16 px/клітинка, 75% якість) для відсутності фризів та економії місця.
            if (HighQuality)
            {
                renderMap.SettingsPixelOnCell = 30;
                renderMap.SettingsQuality = 86;
            }
            else
            {
                renderMap.SettingsPixelOnCell = 16;
                renderMap.SettingsQuality = 75;
            }

            renderMap.ImageReady = (image) => SendToServer(image, serverId, Background);

            renderMap.Initialize(settlement.Map);
            renderMap.Render();
        }

        /// <summary>
        /// Асинхронне кодування кадру в JPG та передача файлу на сервер у фоновому потоці.
        /// </summary>
        private static void SendToServer(Func<byte[]> getImage, long serverId, bool background)
        {
            Task.Run(() =>
            {
                try
                {
                    Loger.Log("SnapshotColony EncodeToJPG start");
                    var data = getImage();

                    if (data == null || data.Length == 0)
                    {
                        Loger.Log("SnapshotColony: отримано порожній масив зображення", Loger.LogLevel.WARNING);
                        return;
                    }

                    Loger.Log($"SnapshotColony Send serverId={serverId} data.Len={data.Length}");

                    SessionClientController.Command((connect) =>
                    {
                        try
                        {
                            var fileKey = SessionClientController.My.Login + "@" + serverId;
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
                    Loger.Log($"SnapshotColony Encode/Send Exception: {ex.Message}", Loger.LogLevel.WARNING);
                }

                Loger.Log("SnapshotColony Send end");
            });
        }
    }
}