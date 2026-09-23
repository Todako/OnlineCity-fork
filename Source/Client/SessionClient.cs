using Model;
using OCUnion;
using OCUnion.Transfer;
using OCUnion.Transfer.Model;
using System.Collections.Generic;
using Transfer;
using Transfer.ModelMails;

namespace RimWorldOnlineCity
{
    /// <summary>
    /// Специфічний для гри клієнт сесії, що виступає фасадом над базовим транспортним сокетом.
    /// Забезпечує передачу ігрових пакетів (світ, каравани, біржа, PvP-битви).
    /// </summary>
    public class SessionClient : Transfer.SessionClient
    {
        private static readonly object SyncLock = new object();
        private static volatile SessionClient Single = new SessionClient();

        /// <summary>
        /// Поточний активний екземпляр сесії клієнта.
        /// </summary>
        public static SessionClient Get => Single;

        /// <summary>
        /// Безпечне перестворення екземпляра сесії із закриттям попереднього з'єднання.
        /// </summary>
        public static void Recreate(SessionClient newClient)
        {
            lock (SyncLock)
            {
                Single?.Disconnect();
                Single = newClient ?? new SessionClient();
            }
        }

        /// <summary>
        /// Запит на завантаження збереження світу з сервера.
        /// </summary>
        public ModelInfo WorldLoad()
        {
            if (Loger.Enable) Loger.Log("Client WorldLoad (GetInfo 3)");

            var packet = new ModelInt { Value = (long)ServerInfoType.SendSave };
            return TransObject<ModelInfo>(packet, (int)PackageType.Request5UserInfo, (int)PackageType.Response6UserInfo);
        }

        /// <summary>
        /// Надсилання запиту на створення нового ігрового світу на сервері.
        /// </summary>
        public bool CreateWorld(ModelCreateWorld packet)
        {
            if (Loger.Enable) Loger.Log("Client CreateWorld");

            var stat = TransObject<ModelStatus>(packet, (int)PackageType.Request7CreateWorld, (int)PackageType.Response8WorldCreated);

            if (stat != null && stat.Status != 0)
            {
                ErrorCode = stat.Status;
                ErrorMessage = stat.Message;
                return false;
            }

            return stat != null;
        }

        /// <summary>
        /// Передача предметів каравану або поселенню іншого онлайн-гравця.
        /// </summary>
        public bool SendThings(List<ThingEntry> sendThings, string myLogin, string onlinePlayerLogin, long serverId, int tile)
        {
            // Ранній вихід без виклику важких описів списку та логування
            if (sendThings == null || sendThings.Count == 0)
            {
                return false;
            }

            if (Loger.Enable && !MainHelper.OffAllLog)
            {
                Loger.Log("Client SendThings " + sendThings.ToStringLabel());
            }

            var packet = new ModelMailTrade
            {
                From = new Player { Login = myLogin },
                To = new Player { Login = onlinePlayerLogin },
                Tile = tile,
                PlaceServerId = serverId,
                Things = sendThings
            };

            var stat = TransObject<ModelStatus>(packet, (int)PackageType.Request15, (int)PackageType.Response16);

            if (stat != null && stat.Status != 0)
            {
                ErrorCode = stat.Status;
                ErrorMessage = stat.Message;
                return false;
            }

            return stat != null;
        }

        /// <summary>
        /// Редагування існуючого ордера на біржі.
        /// </summary>
        public bool ExchengeEdit(TradeOrder order)
        {
            if (Loger.Enable) Loger.Log("Client ExchengeEdit " + order, Loger.LogLevel.EXCHANGE);

            var stat = TransObject<ModelStatus>(order, (int)PackageType.Request21, (int)PackageType.Response22);

            if (stat != null && stat.Status != 0)
            {
                ErrorCode = stat.Status;
                ErrorMessage = stat.Message;
                return false;
            }

            return stat != null;
        }

        /// <summary>
        /// Купівля товарів за ордером на біржі.
        /// </summary>
        public bool ExchengeBuy(long orderId, int count)
        {
            if (Loger.Enable)
            {
                Loger.Log($"Client ExchengeBuy id={orderId} count={count}", Loger.LogLevel.EXCHANGE);
            }

            var packet = new ModelOrderBuy { OrderId = orderId, Count = count };
            var stat = TransObject<ModelStatus>(packet, (int)PackageType.Request23, (int)PackageType.Response24);

            if (stat != null && stat.Status != 0)
            {
                ErrorCode = stat.Status;
                ErrorMessage = stat.Message;
                return false;
            }

            return stat != null;
        }

        /// <summary>
        /// Завантаження списку ордерів біржі для заданих тайлів і фільтрів.
        /// </summary>
        public List<TradeOrder> ExchengeLoad(List<int> tiles, string filterBuy, string filterSell)
        {
            if (Loger.Enable) Loger.Log("Client ExchengeLoad", Loger.LogLevel.EXCHANGE);

            var packet = new ModelOrderLoadRequest
            {
                Tiles = tiles,
                FilterBuy = filterBuy,
                FilterSell = filterSell
            };

            var stat = TransObject<ModelOrderLoad>(packet, (int)PackageType.Request25, (int)PackageType.Response26);

            if (stat != null && stat.Status != 0)
            {
                ErrorCode = stat.Status;
                ErrorMessage = stat.Message;
                return null;
            }

            return stat?.Orders;
        }

        /// <summary>
        /// Передача та прийом пакетів ініціатора PvP-бою (гарячий мережевий цикл 50 мс).
        /// </summary>
        public AttackInitiatorFromSrv AttackOnlineInitiator(AttackInitiatorToSrv fromClient)
        {
            return TransObject<AttackInitiatorFromSrv>(fromClient, (int)PackageType.Request27, (int)PackageType.Response28);
        }

        /// <summary>
        /// Передача та прийом пакетів хоста/захисника PvP-бою (гарячий мережевий цикл 50 мс).
        /// </summary>
        public AttackHostFromSrv AttackOnlineHost(AttackHostToSrv fromClient)
        {
            return TransObject<AttackHostFromSrv>(fromClient, (int)PackageType.Request29, (int)PackageType.Response30);
        }

        /// <summary>
        /// Оновлення складу онлайн-біржі (додавання/вилучення предметів).
        /// </summary>
        public bool ExchengeStorage(List<ThingTrade> addThings, List<ThingTrade> deleteThings, int tile, int tileTo = 0, int cost = 0, int dist = 0)
        {
            if (Loger.Enable) Loger.Log("Client ExchengeStorage", Loger.LogLevel.EXCHANGE);

            var packet = new ModelExchengeStorage
            {
                AddThings = addThings,
                DeleteThings = deleteThings,
                Tile = tile,
                TileTo = tileTo,
                Cost = cost,
                Dist = dist
            };

            var stat = TransObject<ModelStatus>(packet, (int)PackageType.Request47Storage, (int)PackageType.Response48Storage);

            if (stat != null && stat.Status != 0)
            {
                ErrorCode = stat.Status;
                ErrorMessage = stat.Message;
                return false;
            }

            return stat != null;
        }

        /// <summary>
        /// Запит кількості доступного товару на складі біржі.
        /// </summary>
        public int ExchengeInfo_GetCountThing(ThingTrade thing)
        {
            var packet = new ModelExchengeInfo
            {
                Request = ModelExchengeInfoRequest.GetCountThing,
                Thing = thing
            };

            var stat = TransObject<ModelExchengeInfo>(packet, (int)PackageType.Request53ExchengeInfo, (int)PackageType.Response54ExchengeInfo);

            if (stat != null && stat.Status != 0)
            {
                ErrorCode = stat.Status;
                ErrorMessage = stat.Message;
                return -1;
            }

            return stat?.Result ?? -1;
        }
    }
}