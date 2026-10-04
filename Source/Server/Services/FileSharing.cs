using System;
using System.Collections.Generic;
using OCUnion.Transfer.Model;
using ServerOnlineCity.Model;
using Transfer;

namespace ServerOnlineCity.Services
{
    internal sealed class FileSharing : IGenerateResponseContainer
    {
        public int RequestTypePackage => (int)PackageType.Request49FileSharing;

        public int ResponseTypePackage => (int)PackageType.Response50FileSharing;

        public ModelContainer GenerateModelContainer(ModelContainer request, ServiceContext context)
        {
            if (context?.Player == null || request?.Packet == null) return null;

            object result;
            if (request.Packet is List<ModelFileSharing> list)
            {
                // Пакетна перевірка хешів без завантаження повних тіл файлів
                var res = new List<ModelFileSharing>(list.Count);
                for (int i = 0; i < list.Count; i++)
                {
                    var item = list[i];
                    if (item != null)
                    {
                        res.Add(GetFileSharing(item, context.Player, true));
                    }
                }
                result = res;
            }
            else if (request.Packet is ModelFileSharing fileSharing)
            {
                // Одиночний запит на передачу або отримання файлу
                result = GetFileSharing(fileSharing, context.Player, false);
            }
            else
            {
                return null;
            }

            return new ModelContainer
            {
                TypePacket = ResponseTypePackage,
                Packet = result
            };
        }

        private ModelFileSharing GetFileSharing(ModelFileSharing fileSharing, PlayerServer player, bool onlyCheck)
        {
            if (fileSharing == null) return null;

            if (!onlyCheck && fileSharing.Data != null)
            {
                // Запит на збереження файлу на сервері
                if (!Repository.GetFileSharing.SaveFileSharing(player, fileSharing))
                {
                    fileSharing.Hash = null; // Позначка помилки або відхилення запиту
                }
                fileSharing.Data = null;
            }
            else
            {
                // Запит на отримання файлу зі сховища сервера
                Repository.GetFileSharing.LoadFileSharing(player, fileSharing);
                if (onlyCheck) fileSharing.Data = null; // Отримуємо хеш для звірки та скидаємо важкий масив
            }
            return fileSharing;
        }
    }
}