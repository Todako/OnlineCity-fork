using MapRenderer;
using OCUnion;
using OCUnion.Transfer.Model;
using RimWorld.Planet;
using System;
using System.Threading;
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

        // Атомарний прапорець фонового завантаження для запобігання перевантаженню мережевого сокета
        private static int _isUploading = 0;

        /// <summary>
        /// Повертає true, якщо наразі триває створення знімка або його відправка на сервер.
        /// </summary>
        public static bool IsBusy => RenderMap.IsRendering || _isUploading != 0;

        /// <summary>
        /// Запуск процесу автоматичного рендерингу карти поселення.
        /// </summary>
        public void Exec(Settlement settlement)
        {
            ExecStatic(settlement, HighQuality, Background);
        }

        /// <summary>
        /// Статична точка входу для рендерингу карти колонії.
        /// Гарантує виконання графічних викликів у головному потоці Unity.
        /// </summary>
        public static void ExecStatic(Settlement settlement, bool highQuality = false, bool background = true)
        {
            if (settlement == null || settlement.Map == null)
            {
                return;
            }

            // Якщо виклик прийшов із фонового таймера — безпечно перенаправляємо в головний потік Unity
            if (!UnityData.IsInMainThread)
            {
                ModBaseData.RunMainThread(() => ExecStatic(settlement, highQuality, background));
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
        /// Отримання готових байтів кадру та асинхронна передача файлу на сервер у фоновому завданні.
        /// </summary>
        private static void SendToServer(Func<byte[]> getImage, long serverId)
        {
            if (getImage == null) return;
            if (Interlocked.CompareExchange(ref _isUploading, 1, 0) != 0) return;

            try
            {
                // Отримуємо байти в контексті ImageReady до переходу у фоновий потік
                byte[] data;
                try
                {
                    data = getImage();
                }
                catch (Exception ex)
                {
                    Loger.Log($"SnapshotColony getImage Exception: {ex.Message}", Loger.LogLevel.WARNING);
                    Interlocked.Exchange(ref _isUploading, 0);
                    return;
                }

                if (data == null || data.Length == 0)
                {
                    Loger.Log("SnapshotColony: отримано порожній масив зображення", Loger.LogLevel.WARNING);
                    Interlocked.Exchange(ref _isUploading, 0);
                    return;
                }

                var myLogin = SessionClientController.My?.Login;
                if (string.IsNullOrEmpty(myLogin))
                {
                    Interlocked.Exchange(ref _isUploading, 0);
                    return;
                }

                var fileKey = myLogin + "@" + serverId;

                Task.Run(() =>
                {
                    try
                    {
                        if (Loger.Enable && !MainHelper.OffAllLog)
                        {
                            Loger.Log($"SnapshotColony Send serverId={serverId} bytes={data.Length}");
                        }

                        SessionClientController.Command((connect) =>
                        {
                            try
                            {
                                connect.FileSharingUpload(FileSharingCategory.ColonyScreen, fileKey, data);
                                Loger.Log($"SnapshotColony: успішно завантажено на сервер: {fileKey}", Loger.LogLevel.INFO);
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
                        Interlocked.Exchange(ref _isUploading, 0);
                    }
                });
            }
            catch
            {
                Interlocked.Exchange(ref _isUploading, 0);
            }
        }
    }
}