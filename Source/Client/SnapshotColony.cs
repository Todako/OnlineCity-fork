using Model;
using OCUnion;
using OCUnion.Transfer;
using OCUnion.Transfer.Model;
using RimWorldOnlineCity.Services;
using RimWorldOnlineCity.UI;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using Verse;
using MapRenderer;
using RimWorld.Planet;

namespace RimWorldOnlineCity
{
    internal class SnapshotColony
    {
        public bool HighQuality = false;
        public bool Background = false;

        public void Exec(Settlement settlement)
        {
            if (settlement == null || settlement.Map == null) return;

            var serverId = UpdateWorldController.GetMyByLocalId(settlement.ID)?.PlaceServerId ?? 0;
            Loger.Log($"SnapshotColony serverId={serverId}");

            if (serverId == 0) return;

            var renderMap = new RenderMap();

            if (HighQuality)
            {
                renderMap.SettingsPixelOnCell = 30;
                renderMap.SettingsQuality = 86;
            }
            else
            {
                renderMap.SettingsPixelOnCell = 16;
                renderMap.SettingsQuality = 80;
            }

            renderMap.ImageReady = (image) => SendToServer(image, serverId, Background);

            try
            {
                renderMap.Initialize(settlement.Map);
                renderMap.Render();
            }
            catch (Exception ex)
            {
                Loger.Log($"SnapshotColony Render Exception: {ex}");
            }
        }

        private static void SendToServer(Func<byte[]> getImage, long serverId, bool background)
        {
            Task.Run(() =>
            {
                try
                {
                    Loger.Log("SnapshotColony EncodeToJPG start");
                    var data = getImage?.Invoke();

                    if (data == null || data.Length == 0)
                    {
                        Loger.Log("SnapshotColony: data is empty, aborting upload.");
                        return;
                    }

                    Loger.Log($"SnapshotColony Send serverId={serverId} data.Len={data.Length}");

                    SessionClientController.Command((connect) =>
                    {
                        try
                        {
                            // Відправляємо скріншот на сервер
                            connect.FileSharingUpload(FileSharingCategory.ColonyScreen, SessionClientController.My.Login + "@" + serverId, data);

                            // Без повторного зворотного завантаження для економії мережі та пам'яті
                            if (!background)
                            {
                                ModBaseData.RunMainThread(() =>
                                {
                                    GeneralTexture.Clear();
                                    Find.WindowStack.Add(new Dialog_MessageBox("OCity_Successfully".Translate()));
                                });
                            }
                        }
                        catch (Exception ex)
                        {
                            Loger.Log($"SnapshotColony FileSharingUpload error: {ex.Message}");
                            if (!background)
                            {
                                ModBaseData.RunMainThread(() =>
                                {
                                    Find.WindowStack.Add(new Dialog_MessageBox("OCity_Error".Translate()));
                                });
                            }
                        }
                    });
                }
                catch (Exception ex)
                {
                    Loger.Log($"SnapshotColony task error: {ex}");
                }

                Loger.Log("SnapshotColony Send end");
            });
        }
    }
}
