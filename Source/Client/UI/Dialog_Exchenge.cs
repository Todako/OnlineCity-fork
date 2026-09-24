using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Model;
using OCUnion;
using RimWorld;
using RimWorld.Planet;
using RimWorldOnlineCity.GameClasses.Harmony;
using RimWorldOnlineCity.Services;
using Transfer;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace RimWorldOnlineCity.UI
{
    /// <summary>
    /// Вікно онлайн-біржі та управління вантажами: перегляд та виставлення ордерів,
    /// переміщення предметів між поселеннями, караванами та торговими складами.
    /// </summary>
    public class Dialog_Exchenge : Window
    {
        public Action PostCloseAction;

        private Place PlaceCurrent;
        private WorldObject WorldObjectCurrent;
        private WorldObject WorldObjectStorageCurrentTile = null;
        private List<WorldObject> WorldObjectsTile;
        private List<CaravanOnline> WorldObjectCaravanOnlinesTile;

        public List<Thing> AllThings;
        public List<ThingTrade> AllThingsOriginalEntry;
        public List<TradeOrder> Orders;

        private string StatusLoadOrders = null;
        private AnyLoad LoaderOrders = null;

        private int FilterTileLength = 0;
        private string FilterTileLengthBuffer = "";

        private string FilterSell = "";
        private ThingDef FilterSellThing = null;
        private string FilterBuy = "";
        private ThingDef FilterBuyThing = null;

        private bool ActiveElementBlock = false;

        public TradeOrder EditOrder { get; private set; }
        public TradeOrder EditOrderOnStart { get; private set; }

        private int TabIndex;
        private GridBox<TransferableOneWay> AddThingGrid;
        private GridBox<TradeOrder> OrdersGrid;

        private string EditOrderTitle = "";
        private Vector2 ScrollPositionEditOrder;

        private bool EditOrderIsMy => EditOrder != null && EditOrder.Owner.Login == SessionClientController.My.Login;

        private bool EditOrderToTrade;
        private int EditOrderCountReady;

        private static bool ShowedMessageExistsEnemyPawns = false;
        private static string CachedYouSuffix;
        private static float CachedHitPointsMeasureWidth = 0f;
        private static float CachedPercentMeasureWidth = 0f;
        private string CachedPlaneTitle = "";

        private class OrderEditBuffer
        {
            private readonly Dialog_Exchenge That;
            public ThingTrade Thing;
            public TextFieldNumericBox TextField1;
            public TextFieldNumericBox TextField2;
            public int TotalCount => Thing.Count * That.EditOrderCountReady;

            public OrderEditBuffer(Dialog_Exchenge that, ThingTrade thing)
            {
                That = that;
                Thing = thing;
                TextField1 = new TextFieldNumericBox(
                    () => Thing.Count,
                    (cnt) =>
                    {
                        Thing.Count = cnt;
                        that.EditOrderChange();
                    },
                    () => !that.ActiveElementBlock || !that.EditOrderIsMy)
                {
                    ShowButton = false,
                    Min = 1
                };

                TextField2 = new TextFieldNumericBox(
                    () => TotalCount,
                    (cnt) =>
                    {
                        if (that.EditOrderCountReady <= 0) that.EditOrderCountReady = 1;
                        if (TotalCount > 0) that.EditOrderCountReady = that.EditOrderCountReady * cnt / TotalCount;
                        if (that.EditOrderCountReady <= 0) that.EditOrderCountReady = 1;

                        that.EditOrderChange();
                    },
                    () => !that.ActiveElementBlock)
                {
                    ShowButton = false,
                    Min = 1
                };
            }
        }

        // ОПТИМІЗАЦІЯ: об'єднання всіх даних рядка речей у єдиний контейнер (замість 4 окремих словників)
        private class ThingRowData
        {
            public string MaxCountStr;
            public string MarketValueStr;
            public TextFieldNumericBox Component;
        }

        private Dictionary<ThingTrade, OrderEditBuffer> EditOrderEditBuffer;
        private TextFieldNumericBox EditOrderCountReadyBuffer = null;

        public override Vector2 InitialSize => new Vector2(1024f, (float)Verse.UI.screenHeight);

        private Dialog_Exchenge()
        {
            closeOnCancel = false;
            closeOnAccept = false;

            // ОПТИМІЗАЦІЯ: безпечний виклик діалогу через чергу головного потоку замість сирого Thread
            if (!ShowedMessageExistsEnemyPawns && UpdateWorldController.ExistsEnemyPawns)
            {
                ShowedMessageExistsEnemyPawns = true;
                Task.Delay(500).ContinueWith(_ =>
                {
                    ModBaseData.RunMainThread(() =>
                    {
                        Find.WindowStack.Add(new Dialog_MessageBox("OC_PlayerClient_EnemiesOnMap".Translate()));
                    });
                });
            }
        }

        public Dialog_Exchenge(Caravan caravan) : this()
        {
            SetPlaceCurrent(caravan);
            Init();
        }

        public Dialog_Exchenge(MapParent settlement) : this()
        {
            SetPlaceCurrent(settlement);
            Init();
        }

        public Dialog_Exchenge(WorldObjectBaseOnline onlyPlace) : this()
        {
            SetPlaceCurrent(onlyPlace);
            Init();
        }

        public Dialog_Exchenge(TradeThingsOnline tradeThings) : this()
        {
            SetPlaceCurrent(tradeThings);
            Init();
        }

        public Dialog_Exchenge(WorldObject worldObject) : this()
        {
            SetPlaceCurrent(worldObject);
            Init();
        }

        /// <summary>
        /// Ініціалізація та оновлення контексту поточної локації.
        /// ОПТИМІЗАЦІЯ: об'єднано обхід об'єктів тайла в один прохід, кешовано заголовок координат.
        /// </summary>
        private void SetPlaceCurrent(WorldObject placeWorldObject)
        {
            try
            {
                Loger.Log("Client ExchengeEdit SetPlaceCurrent " + placeWorldObject.GetType().Name, Loger.LogLevel.EXCHANGE);
                WorldObjectCurrent = placeWorldObject;

                PlaceCurrent = new Place
                {
                    Name = placeWorldObject.LabelCap,
                    PlaceServerId = UpdateWorldController.GetServerInfo(placeWorldObject)?.PlaceServerId ?? 0,
                    ServerName = SessionClientController.My?.ServerName ?? string.Empty,
                    Tile = placeWorldObject.Tile,
                    DayPath = 0
                };

                Vector2 vector = Find.WorldGrid.LongLatOf(placeWorldObject.Tile);
                CachedPlaneTitle = vector.y.ToStringLatitude() + " " + vector.x.ToStringLongitude();

                var rawTileObjects = ExchengeUtils.WorldObjectsByTile(placeWorldObject.Tile);
                WorldObjectsTile = new List<WorldObject>(rawTileObjects.Count);
                WorldObjectCaravanOnlinesTile = new List<CaravanOnline>(rawTileObjects.Count);

                for (int i = 0; i < rawTileObjects.Count; i++)
                {
                    var o = rawTileObjects[i];
                    if (o is CaravanOnline co)
                    {
                        WorldObjectCaravanOnlinesTile.Add(co);
                    }
                    if (o is TradeThingsOnline || ((o.Faction?.IsPlayer ?? false) && (o is Settlement || o is Caravan)))
                    {
                        WorldObjectsTile.Add(o);
                    }
                }

                if (placeWorldObject is TradeThingsOnline)
                {
                    WorldObjectStorageCurrentTile = placeWorldObject;
                }
                else
                {
                    WorldObjectStorageCurrentTile = null;
                    for (int i = 0; i < WorldObjectsTile.Count; i++)
                    {
                        if (WorldObjectsTile[i] is TradeThingsOnline)
                        {
                            WorldObjectStorageCurrentTile = WorldObjectsTile[i];
                            break;
                        }
                    }
                }

                if (WorldObjectStorageCurrentTile == null)
                {
                    WorldObjectStorageCurrentTile = new TradeThingsOnline { Tile = placeWorldObject.Tile };
                }

                if (placeWorldObject is Settlement settlement)
                {
                    AllThings = GameUtils.GetAllThings(settlement.Map);
                    AllThingsOriginalEntry = null;
                }
                else if (placeWorldObject is Caravan caravan)
                {
                    AllThings = GameUtils.GetAllThings(caravan);
                    AllThingsOriginalEntry = null;
                }
                else if (placeWorldObject is TradeThingsOnline tto)
                {
                    AllThings = GameUtils.GetAllThings(tto);
                    if (SessionClientController.Data != null && SessionClientController.Data.CashlessBalance > 0)
                    {
                        AllThings.Add(GameUtils.GetCashlessBalanceThing(SessionClientController.Data.CashlessBalance));
                    }
                    AllThingsOriginalEntry = tto.TradeThings.Things;
                }
                else
                {
                    AllThings = new List<Thing>(0);
                    AllThingsOriginalEntry = null;
                }
            }
            catch (Exception exp)
            {
                ExceptionUtil.ExceptionLog(exp, "Dialog_Exchenge SetPlaceCurrent Exception");
            }
        }

        private void Init()
        {
            UpdateOrdersList();
        }

        private void SetEditOrder(TradeOrder order)
        {
            EditOrder = order;
            EditOrderOnStart = order?.Clone();
            EditOrderEditBuffer = new Dictionary<ThingTrade, OrderEditBuffer>();
            EditOrderCountReady = EditOrder?.CountReady ?? 0;
            EditOrderCountReadyBuffer = null;
            EditOrderChange();
        }

        private List<Thing> GetAvailableThingsInEditOrder()
        {
            if (!EditOrderIsMy || EditOrderOnStart == null || EditOrderOnStart.Id <= 0)
            {
                return new List<Thing>(0);
            }

            using (GameUtils.NormalGameError())
            {
                var sellThings = EditOrderOnStart.SellThings;
                var res = new List<Thing>(sellThings.Count);
                for (int i = 0; i < sellThings.Count; i++)
                {
                    var th = sellThings[i].CreateThing();
                    th.stackCount *= EditOrderOnStart.CountReady;
                    res.Add(th);
                }
                return res;
            }
        }

        /// <summary>
        /// Повертає список доступних речей для угоди без важких LINQ Concat.
        /// </summary>
        private List<Thing> GetAvailableThings(bool withoutInEditOrder = false, bool withoutCashless = false)
        {
            var result = new List<Thing>((AllThings?.Count ?? 0) + 16);
            if (AllThings != null && AllThings.Count > 0)
            {
                result.AddRange(AllThings);
            }

            if (!withoutInEditOrder)
            {
                var inEditOrder = GetAvailableThingsInEditOrder();
                if (inEditOrder.Count > 0)
                {
                    result.AddRange(inEditOrder);
                }
            }

            if (!withoutCashless && !(WorldObjectCurrent is TradeThingsOnline) && SessionClientController.Data != null && SessionClientController.Data.CashlessBalance > 0)
            {
                result.Add(GameUtils.GetCashlessBalanceThing(SessionClientController.Data.CashlessBalance));
            }

            return result;
        }

        private void EditOrderChange()
        {
            if (EditOrder == null) return;

            EditOrderToTrade = GameUtils.ChechToTrade(
                EditOrderIsMy ? EditOrder.SellThings : EditOrder.BuyThings,
                GetAvailableThings(),
                null,
                out _) != null
                && (EditOrder.SellThings?.Count ?? 0) > 0
                && (EditOrder.BuyThings?.Count ?? 0) > 0;

            if (EditOrderIsMy)
            {
                if (EditOrderCountReady == 0) EditOrderCountReady = 1;
            }
        }

        private void LoaderOrdersCancel()
        {
            if (LoaderOrders == null) return;
            try
            {
                LoaderOrders.TaskFinish = null;
                LoaderOrders.TaskError = null;
                LoaderOrders.Cancel();
            }
            catch { }
            Orders = new List<TradeOrder>(0);
            StatusLoadOrders = null;
            LoaderOrders = null;
            OrdersGrid = null;
        }

        public override void PostClose()
        {
            LoaderOrdersCancel();
            base.PostClose();
            PostCloseAction?.Invoke();
        }

        public override void DoWindowContents(Rect inRect)
        {
            try
            {
                float margin = 5f;
                var btnSize = new Vector2(140f, 35f);
                Text.Font = GameFont.Small;

                if (Widgets.ButtonText(new Rect(inRect.width - btnSize.x, 0, btnSize.x, btnSize.y), "OCity_Dialog_Exchenge_Close".TranslateCache()))
                {
                    Close();
                }

                Rect rect = new Rect(0f, 0f, inRect.width, btnSize.y);
                Text.Font = GameFont.Medium;
                Text.Anchor = TextAnchor.MiddleCenter;

                Widgets.Label(rect,
                    (StatusLoadOrders != null) ? "OCity_Dialog_Exchenge_Trade_OrdersLoad".TranslateCache() + " " + StatusLoadOrders
                    : (Orders == null || Orders.Count == 0) ? "OCity_Dialog_Exchenge_No_Warrants".TranslateCache()
                    : "OCity_Dialog_Exchenge_Active_Orders".TranslateCache(Orders.Count.ToString()));

                Text.Font = GameFont.Tiny;
                Text.Anchor = TextAnchor.UpperLeft;

                var regionRectOut = new Rect(inRect.x, inRect.y + btnSize.y + margin, inRect.width, InitialSize.y - 455f);

                GUI.BeginGroup(regionRectOut);
                DoWindowOrders(regionRectOut.AtZero());
                GUI.EndGroup();

                regionRectOut = new Rect(regionRectOut.x, regionRectOut.yMax + margin, regionRectOut.width, inRect.height - (regionRectOut.yMax + margin));

                var screenRect = new Rect(regionRectOut.x, regionRectOut.y + 31f, 400f, 0);
                var tabRect = new Rect(regionRectOut.x, regionRectOut.y + 31f, regionRectOut.width, regionRectOut.height - 31f);

                List<TabRecord> list = new List<TabRecord>(2)
                {
                    new TabRecord("OCity_DialogExchenge_Things".Translate(), () => { TabIndex = 0; }, TabIndex == 0),
                    new TabRecord("OCity_DialogExchenge_Deal".Translate(), () => { TabIndex = 1; }, TabIndex == 1)
                };

                Widgets.DrawMenuSection(tabRect);
                if (TabIndex == 0)
                {
                    var rt0 = new Rect(tabRect);
                    rt0.xMin += 251f;
                    Widgets.DrawMenuSection(rt0);
                }
                TabDrawer.DrawTabs(screenRect, list);

                var regionRect = tabRect.ContractedBy(margin * 2f);
                GUI.BeginGroup(regionRect);
                var regionRectIn = regionRect.AtZero();
                if (TabIndex == 0) DoTab0ThingList(regionRectIn);
                else if (TabIndex == 1) DoTab1EditOrder(regionRectIn);
                GUI.EndGroup();

                Text.Anchor = TextAnchor.UpperLeft;
            }
            catch (Exception e)
            {
                ExceptionUtil.ExceptionLog(e, "Dialog_Exchenge DoWindowContents");
            }
        }

        private static string FormatPrivatPlayers(List<Player> players)
        {
            if (players == null || players.Count == 0) return string.Empty;
            var sb = new StringBuilder(players.Count * 16);
            for (int i = 0; i < players.Count; i++)
            {
                if (i > 0) sb.Append(", ");
                sb.Append(players[i].Login);
            }
            return sb.ToString();
        }

        /// <summary>
        /// Відмальовка таблиці ордерів.
        /// ОПТИМІЗАЦІЯ: підказки TooltipHandler.TipRegion формуються ЛИШЕ коли курсор миші знаходиться над елементом (Mouse.IsOver).
        /// </summary>
        public void DoWindowOrders(Rect inRect)
        {
            if (OrdersGrid == null && Orders != null && Orders.Count > 0)
            {
                OrdersGrid = new GridBox<TradeOrder>
                {
                    DataSource = Orders,
                    LineHeight = 24f,
                    ShowSelected = true,
                    Tooltip = null
                };
                OrdersGrid.OnClick += (int line, TradeOrder item) =>
                {
                    SetEditOrder(item);
                    if (EditOrderIsMy)
                        EditOrderTitle = "OCity_Dialog_Exchenge_Edit".TranslateCache();
                    else
                        EditOrderTitle = "OCity_Dialog_Exchenge_Viewing_Orders".TranslateCache() + " " + item.Owner.Login;
                };

                if (CachedYouSuffix == null)
                {
                    CachedYouSuffix = " (" + "OCity_Dialog_Exchenge_You".TranslateCache() + ")";
                }

                OrdersGrid.OnDrawLine = (int line, TradeOrder item, Rect rectLine) =>
                {
                    Text.WordWrap = false;
                    Text.Anchor = TextAnchor.MiddleLeft;
                    try
                    {
                        float currentWidth = rectLine.width;

                        // Галочка приватного ордера
                        var rect2 = new Rect(rectLine.x + rectLine.width - 24f, rectLine.y, 24f, rectLine.height);
                        currentWidth -= 24f;
                        var flag = item.PrivatPlayers == null || item.PrivatPlayers.Count == 0;
                        if (Mouse.IsOver(rect2))
                        {
                            TooltipHandler.TipRegion(rect2, flag
                                ? "OCity_Dialog_Exchenge_Deal_Open_Everyone".TranslateCache()
                                : "OCity_Dialog_Exchenge_Deal_Open_Specific".TranslateCache(FormatPrivatPlayers(item.PrivatPlayers)));
                        }
                        Widgets.Checkbox(rect2.position, ref flag, 24f, false);

                        // Нік продавця
                        rect2 = new Rect(rectLine.x + currentWidth - 200f, rectLine.y, 200f, rectLine.height);
                        currentWidth -= 200f;
                        var rect2p = new Rect(rect2.x, 0f, 24f, 24f);
                        if (Widgets.ButtonImage(rect2p, GeneralTexture.Get.ByName("pl_" + item.Owner.Login)))
                        {
                            Dialog_InfoPlayer.ShowInfo(item.Owner.Login);
                        }
                        rect2p = new Rect(rect2.x + 24f, 0f, rect2.width - 24f, rect2.height);
                        if (Mouse.IsOver(rect2p))
                        {
                            TooltipHandler.TipRegion(rect2p, item.Owner.Login + Environment.NewLine + "OCity_Dialog_Exchenge_BeenOnline".TranslateCache() + item.Owner.LastSaveTime.ToGoodUtcString());
                        }
                        Widgets.Label(rect2p, item.Owner.Login + (item.Owner.Login == SessionClientController.My?.Login ? CachedYouSuffix : ""));

                        // Відстань та локація
                        rect2 = new Rect(rectLine.x + currentWidth - 200f, rectLine.y, 200f, rectLine.height);
                        currentWidth -= 200f;

                        rect2p = new Rect(rect2.x, 0f, 24f, rect2.height);
                        if (ActiveElementBlock) GUI.color = Color.gray;
                        if (Widgets.ButtonImage(rect2p, GeneralTexture.Waypoint))
                        {
                            GUI.color = Color.white;
                            if (!ActiveElementBlock && GameUtils.CameraJumpWorldObject(item.Tile))
                            {
                                Close();
                            }
                        }
                        GUI.color = Color.white;

                        string text = "";
                        if (item.Place.DayPath > 0)
                        {
                            text = "OCity_Dialog_Exchenge_Tile".TranslateCache() + " " + ((int)item.Place.DayPath).ToString() + " "
                                + "OCity_Dialog_Exchenge_To".TranslateCache() + " ";
                        }
                        text += item.Place.Name;
                        rect2p = new Rect(rect2.x + 24f, 0f, rect2.width - 24f, rect2.height);
                        if (Mouse.IsOver(rect2p))
                        {
                            TooltipHandler.TipRegion(rect2p, "OCity_Dialog_Exchenge_Location_Goods".TranslateCache() + Environment.NewLine + text);
                        }
                        Widgets.Label(rect2p, text);

                        // Кількість повторів
                        rect2 = new Rect(rectLine.x + currentWidth - 60f, rectLine.y, 60f, rectLine.height);
                        currentWidth -= 60f;
                        text = item.CountReady.ToString();
                        if (Mouse.IsOver(rect2))
                        {
                            TooltipHandler.TipRegion(rect2, "OCity_Dialog_Exchenge_Max_Repetition_Transaction".TranslateCache() + Environment.NewLine + text);
                        }
                        Widgets.Label(rect2, text);

                        // Іконки продажу
                        rect2 = new Rect(rectLine.x, rectLine.y, currentWidth / 2f, rectLine.height);
                        var rect3 = new Rect(rect2.x, rect2.y, rectLine.height, rectLine.height);
                        for (int i = 0; i < item.SellThings.Count; i++)
                        {
                            var th = item.SellThings[i];
                            GameUtils.DravLineThing(rect3, th, false);
                            var textCnt = item.SellThings[i].Count.ToString();
                            var textCntW = Text.CalcSize(textCnt).x;
                            var labelRect = new Rect(rect3.xMax, rect3.y, textCntW, rect3.height);
                            Widgets.Label(labelRect, textCnt);

                            var totalItemRect = new Rect(rect3.x, rect3.y, rect3.width + textCntW, rect3.height);
                            if (Mouse.IsOver(totalItemRect))
                            {
                                TooltipHandler.TipRegion(totalItemRect, th.LabelText);
                            }
                            rect3.x += rectLine.height + textCntW + 2f;
                        }

                        // Іконки запитуваного (купівлі)
                        rect2 = new Rect(rectLine.x + rect2.width, rectLine.y, currentWidth - rect2.width, rectLine.height);
                        rect3 = new Rect(rect2.x, rect2.y, rectLine.height, rectLine.height);
                        for (int i = 0; i < item.BuyThings.Count; i++)
                        {
                            var th = item.BuyThings[i];
                            GameUtils.DravLineThing(rect3, th, false);
                            var textCnt = item.BuyThings[i].Count.ToString();
                            var textCntW = Text.CalcSize(textCnt).x;
                            var labelRect = new Rect(rect3.xMax, rect3.y, textCntW, rect3.height);
                            Widgets.Label(labelRect, textCnt);

                            var totalItemRect = new Rect(rect3.x, rect3.y, rect3.width + textCntW, rect3.height);
                            if (Mouse.IsOver(totalItemRect))
                            {
                                TooltipHandler.TipRegion(totalItemRect, th.LabelText);
                            }
                            rect3.x += rectLine.height + textCntW + 2f;
                        }
                    }
                    catch (Exception e)
                    {
                        Log.Error(e.ToString());
                    }
                    Text.WordWrap = true;
                };
            }

            // Фільтри
            Rect rect = new Rect(inRect.x, inRect.y, inRect.width, 24f);
            inRect.yMin += rect.height;
            Text.Font = GameFont.Tiny;

            var rectFilter = new Rect(rect);
            Text.Anchor = TextAnchor.MiddleLeft;
            Widgets.Label(rectFilter, "OCity_Dialog_Exchenge_Trade_FilterDistance".TranslateCache());
            rectFilter.xMin += 115f;
            rectFilter.width = 60f;
            Widgets.TextFieldNumeric<int>(rectFilter.ContractedBy(2f), ref FilterTileLength, ref FilterTileLengthBuffer, 0f, 9999f);
            rectFilter.x += rectFilter.width + 10f;

            rectFilter.width = 50f;
            Widgets.Label(rectFilter, "OCity_Dialog_Exchenge_Trade_FilterSell".TranslateCache());
            rectFilter.x += rectFilter.width + 5f;
            ShowSelectThingDef(ref rectFilter, FilterSellThing, (name, th) => { FilterSell = name; FilterSellThing = th; });

            rectFilter.width = 50f;
            Widgets.Label(rectFilter, "OCity_Dialog_Exchenge_Trade_FilterBuy".TranslateCache());
            rectFilter.x += rectFilter.width + 5f;
            ShowSelectThingDef(ref rectFilter, FilterBuyThing, (name, th) => { FilterBuy = name; FilterBuyThing = th; });

            rect.xMin += inRect.width - 140f;
            Text.Anchor = TextAnchor.MiddleCenter;
            if (LoaderOrders != null)
            {
                if (ActiveElementBlock) GUI.color = Color.gray;
                if (Widgets.ButtonText(rect.ContractedBy(1f), "OCity_Dialog_CreateWorld_BtnCancel".TranslateCache(), true, false, true))
                {
                    GUI.color = Color.white;
                    if (!ActiveElementBlock)
                    {
                        SoundDefOf.Tick_High.PlayOneShotOnCamera(null);
                        Loger.Log("Client ExchengeLoad LoaderOrders.Cancel", Loger.LogLevel.EXCHANGE);
                        LoaderOrdersCancel();
                        return;
                    }
                }
                GUI.color = Color.white;
            }
            else
            {
                if (ActiveElementBlock) GUI.color = Color.gray;
                if (Widgets.ButtonText(rect.ContractedBy(1f), "OCity_Dialog_Exchenge_Update".TranslateCache(), true, false, true))
                {
                    GUI.color = Color.white;
                    if (!ActiveElementBlock)
                    {
                        SoundDefOf.Tick_High.PlayOneShotOnCamera(null);
                        ActiveElementBlock = true;
                        UpdateWorldAndOrdersList();
                        return;
                    }
                }
                GUI.color = Color.white;
            }

            // Прямий рендеринг заголовків колонок таблиці
            var rectTop = new Rect(inRect.x, inRect.y, inRect.width, 18f);
            inRect.yMin += rectTop.height;

            float lineGridW = inRect.width - 10f;
            lineGridW -= 24f; // чекбокс
            float sellerX = lineGridW - 200f; lineGridW -= 200f;
            float locX = lineGridW - 200f; lineGridW -= 200f;
            float countX = lineGridW - 60f; lineGridW -= 60f;
            float acquireW = lineGridW / 2f;
            float giveToW = lineGridW - acquireW;

            Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.Label(new Rect(5f, rectTop.y, acquireW, rectTop.height), "OCity_Dialog_Exchenge_Acquire".TranslateCache());
            Widgets.Label(new Rect(5f + acquireW, rectTop.y, giveToW, rectTop.height), "OCity_Dialog_Exchenge_GiveTo".TranslateCache());
            Widgets.Label(new Rect(countX, rectTop.y, 60f, rectTop.height), "OCity_Dialog_Exchenge_Number".TranslateCache());
            Widgets.Label(new Rect(locX, rectTop.y, 200f, rectTop.height), "OCity_Dialog_Exchenge_Location".TranslateCache());
            Widgets.Label(new Rect(sellerX, rectTop.y, 200f, rectTop.height), "OCity_Dialog_Exchenge_Seller".TranslateCache());

            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleLeft;
            Widgets.DrawMenuSection(inRect);

            if (OrdersGrid == null) return;

            OrdersGrid.Area = inRect.ContractedBy(5f);
            OrdersGrid.Drow();
        }

        private void UpdateWorldAndOrdersList(bool useEditOrderChange = false)
        {
            Loger.Log("Client UpdateWorldSafelyRun set");
            SessionClientController.UpdateWorldSafelyRun = () =>
            {
                try
                {
                    Loger.Log("Client UpdateWorldSafelyRun 1");
                    UpdateOrdersList();
                    SetPlaceCurrent(WorldObjectCurrent);
                    if (useEditOrderChange) EditOrderChange();
                    AddThingGrid = null;
                }
                catch (Exception e)
                {
                    Log.Error("UpdateWorldAndOrdersList " + e.ToString());
                }
                ActiveElementBlock = false;
            };
        }

        private class ThingPlace
        {
            public WorldObject WO;
            public string Text => WO.LabelCap;
            public override string ToString()
            {
                if (WO is Caravan)
                    return "<img CaravanOnExpanding> " + Text;
                if (WO is Settlement)
                    return "<img HomeAreaOn> " + Text;
                if (WO is TradeThingsOnline)
                    return "<img AppleF> " + Text;
                return "<img Waypoint> " + Text;
            }
        }

        private ListBox<ThingPlace> lbThingListPlaces = null;

        private void DoTab0ThingList(Rect inRect)
        {
            if (lbThingListPlaces == null)
            {
                lbThingListPlaces = new ListBox<ThingPlace>
                {
                    Area = new Rect(inRect.x, inRect.y + 30f, 250f, inRect.height - 30f),
                    LineHeight = 28f,
                    Tooltip = (item) => item.Text
                };
                lbThingListPlaces.OnClick += (index, item) =>
                {
                    try
                    {
                        SoundDefOf.Tick_High.PlayOneShotOnCamera(null);
                        SetPlaceCurrent(item.WO);
                        SetEditOrder(null);
                        AddThingGrid = null;
                    }
                    catch (Exception exp)
                    {
                        ExceptionUtil.ExceptionLog(exp, "DoWindowAddThingList button ...");
                    }
                };

                var listWO = new List<WorldObject>(WorldObjectsTile.Count + 1);
                listWO.AddRange(WorldObjectsTile);

                bool hasStorage = false;
                for (int i = 0; i < listWO.Count; i++)
                {
                    if (listWO[i] is TradeThingsOnline) { hasStorage = true; break; }
                }
                if (!hasStorage) listWO.Add(WorldObjectStorageCurrentTile);

                var ds = new List<ThingPlace>(listWO.Count);
                for (int i = 0; i < listWO.Count; i++)
                {
                    ds.Add(new ThingPlace { WO = listWO[i] });
                }
                lbThingListPlaces.DataSource = ds;
                lbThingListPlaces.UsePanelText = true;
                lbThingListPlaces.SelectedIndex = 0;

                if (ds.Count > 0)
                {
                    SetPlaceCurrent(ds[0].WO);
                    SetEditOrder(null);
                    AddThingGrid = null;
                }
            }
            lbThingListPlaces.Drow();

            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.UpperCenter;
            Widgets.Label(new Rect(inRect.x, inRect.y, 250f, 30f), CachedPlaneTitle);
            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.UpperLeft;

            // ОПТИМІЗАЦІЯ: кешування рядків через єдиний словник ThingRowData (1 пошук замість 4)
            if (AddThingGrid == null)
            {
                AddThingGrid = new GridBox<TransferableOneWay>();
                var transferables = AllThings.DistinctToTransferableOneWays();

                var rowDataDic = new Dictionary<TransferableOneWay, ThingRowData>(transferables.Count);
                for (int i = 0; i < transferables.Count; i++)
                {
                    var t = transferables[i];
                    int maxCnt = t.MaxCount;
                    rowDataDic[t] = new ThingRowData
                    {
                        MaxCountStr = maxCnt.ToString(),
                        MarketValueStr = t.AnyThing.MarketValue.ToStringMoney(),
                        Component = new TextFieldNumericBox(t, () => !ActiveElementBlock, AddThingGridValueChanged) { Max = maxCnt }
                    };
                }

                AddThingGrid.DataSource = transferables;
                AddThingGrid.LineHeight = 24f;
                AddThingGrid.Tooltip = null;

                AddThingGrid.OnDrawLine = (int line, TransferableOneWay item, Rect rectLine) =>
                {
                    try
                    {
                        if (!item.HasAnyThing || !rowDataDic.TryGetValue(item, out var row)) return;
                        float currentWidth = rectLine.width;

                        var componentWidth = 60f + rectLine.height * 2f;
                        currentWidth -= componentWidth;
                        row.Component.Drow(new Rect(rectLine.width - componentWidth, 0f, componentWidth, rectLine.height));

                        var rect3 = new Rect(currentWidth - 60f, 0f, 60f, rectLine.height);
                        Text.WordWrap = false;
                        Text.Anchor = TextAnchor.MiddleRight;
                        Widgets.Label(rect3, row.MaxCountStr);
                        currentWidth -= 60f;

                        rect3 = new Rect(currentWidth - 60f, 0f, 80f, rectLine.height);
                        Text.Anchor = TextAnchor.MiddleLeft;
                        Widgets.Label(rect3, row.MarketValueStr);
                        Text.WordWrap = true;
                        currentWidth -= 60f;

                        GameUtils.DravLineThing(new Rect(0f, 0f, currentWidth, rectLine.height), item.AnyThing, Color.white);
                    }
                    catch (Exception e)
                    {
                        Log.Error(e.ToString());
                    }
                };
            }

            var buttonWidth = 100f;
            var buttonInner = 64f;
            var buttonArea = new Rect(inRect.width - buttonWidth, 0, buttonWidth, inRect.height);

            var btnRect = new Rect(buttonArea.x + (buttonWidth - buttonInner) / 2, buttonArea.y, buttonInner, buttonInner);
            var rectText = new Rect(buttonArea.x, buttonArea.y, buttonWidth, 18);

            // Кнопка "Продати"
            GUI.color = Color.white;
            Text.Anchor = TextAnchor.MiddleCenter;
            if (ActiveElementBlock || !AddThingListOK || EditOrder?.SellThings?.Count >= 6) GUI.color = Color.gray;
            if (Widgets.ButtonImage(btnRect, GeneralTexture.OCE_Sell))
            {
                GUI.color = Color.white;
                if (!ActiveElementBlock && AddThingListOK && EditOrder?.SellThings?.Count < 6)
                {
                    SoundDefOf.Tick_High.PlayOneShotOnCamera(null);
                    AddThingListApply();
                    return;
                }
            }
            btnRect.y += btnRect.height;
            rectText.y += btnRect.height;
            Text.Anchor = TextAnchor.MiddleCenter;
            if (ActiveElementBlock || !AddThingListOK || EditOrder?.SellThings?.Count >= 6) GUI.color = Color.red;
            Widgets.Label(rectText, "OCity_DialogExchenge_Sell".Translate());
            btnRect.y += rectText.height;
            rectText.y += rectText.height;
            Widgets.Label(rectText, "OCity_DialogExchenge_OnExchange".Translate());
            GUI.color = Color.white;
            btnRect.y += rectText.height + 10f;
            rectText.y += rectText.height + 10f;

            // Кнопка "Перемістити"
            Text.Anchor = TextAnchor.MiddleCenter;
            GUI.color = Color.white;
            if (ActiveElementBlock) GUI.color = Color.gray;
            if (Widgets.ButtonImage(btnRect, GeneralTexture.OCE_Swap))
            {
                GUI.color = Color.white;
                if (!ActiveElementBlock)
                {
                    SoundDefOf.Tick_High.PlayOneShotOnCamera(null);
                    FloatMenuPlaceSelect((wo) =>
                    {
                        ActiveElementBlock = true;
                        MoveSelectThings(wo);
                    }, true, true);
                    return;
                }
            }
            btnRect.y += btnRect.height;
            rectText.y += btnRect.height;
            Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.Label(rectText, "OCity_DialogExchenge_Move".Translate());
            btnRect.y += rectText.height;
            rectText.y += rectText.height;
            Widgets.Label(rectText, "OCity_DialogExchenge_InThisPosition".Translate());
            btnRect.y += rectText.height + 10f;
            rectText.y += rectText.height + 10f;

            // Кнопка "Доставка"
            if (WorldObjectCurrent is TradeThingsOnline)
            {
                GUI.color = Color.white;
                Text.Anchor = TextAnchor.MiddleCenter;
                if (ActiveElementBlock) GUI.color = Color.gray;
                if (Widgets.ButtonImage(btnRect, GeneralTexture.OCE_Trans))
                {
                    GUI.color = Color.white;
                    if (!ActiveElementBlock)
                    {
                        SoundDefOf.Tick_High.PlayOneShotOnCamera(null);
                        TransferThingList();
                        return;
                    }
                }
                GUI.color = Color.white;
                btnRect.y += btnRect.height;
                rectText.y += btnRect.height;
                Text.Anchor = TextAnchor.MiddleCenter;
                Widgets.Label(rectText, "OCity_DialogExchenge_Delivery".Translate());
                btnRect.y += rectText.height;
                rectText.y += rectText.height;
                Widgets.Label(rectText, "OCity_DialogExchenge_ToARemotePoint".Translate());
                btnRect.y += rectText.height + 10f;
                rectText.y += rectText.height + 10f;
            }

            if (!(WorldObjectCurrent is TradeThingsOnline))
            {
                GUI.color = Color.white;
                Text.Anchor = TextAnchor.MiddleCenter;
                if (ActiveElementBlock) GUI.color = Color.gray;
                if (Widgets.ButtonImage(btnRect, GeneralTexture.OCE_Del))
                {
                    GUI.color = Color.white;
                    if (!ActiveElementBlock)
                    {
                        SoundDefOf.Tick_High.PlayOneShotOnCamera(null);
                        ThrowThingList();
                        return;
                    }
                }
                GUI.color = Color.white;
                btnRect.y += btnRect.height;
                rectText.y += btnRect.height;
                Text.Anchor = TextAnchor.MiddleCenter;
                Widgets.Label(rectText, "OCity_DialogExchenge_Destroy".Translate());
                btnRect.y += rectText.height;
                rectText.y += rectText.height;
                Widgets.Label(rectText, "OCity_DialogExchenge_ForTenPercentThePrice".Translate());
                btnRect.y += rectText.height + 10f;
                rectText.y += rectText.height + 10f;
            }

            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleLeft;
            AddThingGrid.Area = new Rect(lbThingListPlaces.Area.width + 4f, 0f, inRect.width - lbThingListPlaces.Area.width - buttonWidth - 8f, inRect.height);
            AddThingGrid.Drow();
        }

        private bool AddThingListOK = true;

        private void AddThingGridValueChanged(TextFieldNumericBox editBox, int value)
        {
            AddThingListOK = true;
            if (AddThingGrid.DataSource == null) return;

            int selectedCount = 0;
            var ds = AddThingGrid.DataSource;
            for (int i = 0; i < ds.Count; i++)
            {
                if (ds[i].CountToTransfer > 0)
                {
                    selectedCount++;
                    if (selectedCount > 1)
                    {
                        AddThingListOK = false;
                        return;
                    }
                }
            }

            if (!EditOrderIsMy) return;

            for (int i = 0; i < ds.Count; i++)
            {
                var item = ds[i];
                if (item.CountToTransfer == 0) continue;
                var th = ThingTrade.CreateTrade(item.AnyThing, item.CountToTransfer);
                var sellThings = EditOrder.SellThings;
                for (int s = 0; s < sellThings.Count; s++)
                {
                    if (sellThings[s].MatchesThingTrade(th))
                    {
                        AddThingListOK = false;
                        return;
                    }
                }
            }
        }

        private void FloatMenuPlaceSelect(Action<WorldObject> action, bool insertTradeThingsOnline, bool insertCaravanOnlinesOnTile = false)
        {
            var listWO = new List<WorldObject>(WorldObjectsTile.Count + 4);
            for (int i = 0; i < WorldObjectsTile.Count; i++)
            {
                if (WorldObjectsTile[i] != WorldObjectCurrent) listWO.Add(WorldObjectsTile[i]);
            }

            if (insertTradeThingsOnline && !(WorldObjectCurrent is TradeThingsOnline))
            {
                bool hasStorage = false;
                for (int i = 0; i < listWO.Count; i++)
                {
                    if (listWO[i] is TradeThingsOnline) { hasStorage = true; break; }
                }
                if (!hasStorage) listWO.Add(WorldObjectStorageCurrentTile);
            }

            if (insertCaravanOnlinesOnTile)
            {
                listWO.AddRange(WorldObjectCaravanOnlinesTile);
            }

            var listFO = new List<FloatMenuOption>(listWO.Count);
            for (int i = 0; i < listWO.Count; i++)
            {
                var wo = listWO[i];
                listFO.Add(wo is CaravanOnline co
                    ? ExchengeUtils.ExchangeOfGoods_GetFloatMenu(co, () =>
                    {
                        SoundDefOf.Tick_High.PlayOneShotOnCamera(null);
                        action(wo);
                    })
                    : new FloatMenuOption(wo.LabelCap, () =>
                    {
                        SoundDefOf.Tick_High.PlayOneShotOnCamera(null);
                        action(wo);
                    }));
            }

            if (listFO.Count == 0) return;
            Find.WindowStack.Add(new FloatMenu(listFO));
        }

        private void AddThingListApply()
        {
            if (!EditOrderIsMy)
            {
                CreateMyOrder();
            }

            if (!EditOrderIsMy || AddThingGrid.DataSource == null) return;

            bool newCountReady = true;
            for (int i = 0; i < EditOrder.SellThings.Count; i++)
            {
                if (EditOrder.SellThings[i].IsPawn || EditOrder.SellThings[i].IsCorpse) { newCountReady = false; break; }
            }
            if (newCountReady)
            {
                for (int i = 0; i < EditOrder.BuyThings.Count; i++)
                {
                    if (EditOrder.BuyThings[i].IsPawn || EditOrder.BuyThings[i].IsCorpse) { newCountReady = false; break; }
                }
            }

            int newEditOrderCountReady = EditOrderCountReady;
            ThingTrade newThingTrade = null;
            var ds = AddThingGrid.DataSource;
            for (int i = 0; i < ds.Count; i++)
            {
                var item = ds[i];
                if (item.CountToTransfer == 0) continue;
                newEditOrderCountReady = item.CountToTransfer;
                newThingTrade = ThingTrade.CreateTrade(item.AnyThing, newCountReady ? 1 : item.CountToTransfer);
                EditOrder.SellThings.Add(newThingTrade);
                break;
            }

            if (newCountReady)
            {
                for (int i = 0; i < EditOrder.SellThings.Count; i++)
                {
                    var item = EditOrder.SellThings[i];
                    if (item == newThingTrade) continue;
                    item.Count = item.Count * EditOrderCountReady / newEditOrderCountReady;
                    if (item.Count <= 0) item.Count = 1;
                }
                for (int i = 0; i < EditOrder.BuyThings.Count; i++)
                {
                    var item = EditOrder.BuyThings[i];
                    item.Count = item.Count * EditOrderCountReady / newEditOrderCountReady;
                    if (item.Count <= 0) item.Count = 1;
                }
            }

            EditOrderChange();
            AddThingGrid = null;
            TabIndex = 1;
            if (newCountReady)
            {
                EditOrderCountReady = newEditOrderCountReady;
            }
        }

        private void ThrowThingList()
        {
            if (AddThingGrid.DataSource == null) return;

            float dropCost = 0f;
            var ds = AddThingGrid.DataSource;
            for (int i = 0; i < ds.Count; i++)
            {
                var item = ds[i];
                if (item.CountToTransfer == 0) continue;
                var th = ThingTrade.CreateTrade(item.AnyThing, item.CountToTransfer);
                if (th.IsCorpse || th.IsPawn) return;
                if (th.GameCost < 1) continue;

                dropCost += th.GameCost * item.CountToTransfer;
            }

            if (dropCost == 0f) return;

            if (ExchengeUtils.MoveSelectThings(WorldObjectCurrent, null, AddThingGrid.DataSource, () =>
            {
                SetPlaceCurrent(WorldObjectCurrent);
                AddThingGrid = null;
            }))
            {
                int summ = (int)(dropCost * 10f / 100f);
                if (WorldObjectCurrent is TradeThingsOnline)
                {
                    Loger.Log($"Client ThrowThingList +{summ} server", Loger.LogLevel.EXCHANGE);
                    SessionClientController.CommandSafely((connect) =>
                        connect.ExchengeStorage(
                            new List<ThingTrade> { ThingTrade.CreateTrade(MainHelper.CashlessThingDef, 0f, QualityCategory.Awful, summ) },
                            null,
                            WorldObjectCurrent.Tile));
                }
                else
                {
                    var ths = GameUtils.GetCashlessBalanceThingList(summ);
                    if (WorldObjectCurrent is Caravan c)
                    {
                        Loger.Log($"Client ThrowThingList +{summ} game caravan", Loger.LogLevel.EXCHANGE);
                        ExchengeUtils.SpawnThings(ths, c);
                    }
                    else if (WorldObjectCurrent is Settlement s)
                    {
                        Loger.Log($"Client ThrowThingList +{summ} game map", Loger.LogLevel.EXCHANGE);
                        ExchengeUtils.SpawnThings(ths, s.Map);
                    }
                }
            }

            SetPlaceCurrent(WorldObjectCurrent);
            AddThingGrid = null;
        }

        private void TransferThingList()
        {
            if (AddThingGrid.DataSource == null || !(WorldObjectCurrent is TradeThingsOnline)) return;

            var select = AddThingGrid.DataSource.TransferableOneWaysToDictionary();
            var toTargetEntry = new List<ThingTrade>(select.Count);
            foreach (var pair in select)
            {
                if (pair.Key.stackCount != pair.Value) pair.Key.stackCount = pair.Value;
                toTargetEntry.Add(ThingTrade.CreateTrade(pair.Key, pair.Value));
            }

            var playerObjects = ExchengeUtils.WorldObjectsPlayer();
            var listFO = new List<FloatMenuOption>(playerObjects.Count);
            for (int i = 0; i < playerObjects.Count; i++)
            {
                var wo = playerObjects[i];
                if (wo.Tile == WorldObjectCurrent.Tile) continue;

                ExchengeUtils.CargoDeliveryCalc(WorldObjectCurrent, wo, toTargetEntry, out int cost, out int dist);

                listFO.Add(new FloatMenuOption(wo.LabelCap + " " + cost + "$ (✗" + dist + ")", () =>
                {
                    SoundDefOf.Tick_High.PlayOneShotOnCamera(null);
                    ActiveElementBlock = true;
                    ExchengeUtils.CargoDelivery(WorldObjectCurrent, wo, toTargetEntry, () =>
                    {
                        ActiveElementBlock = false;
                        UpdateWorldAndOrdersList();
                    });
                }));
            }

            if (listFO.Count == 0) return;
            Find.WindowStack.Add(new FloatMenu(listFO));
        }

        private void CreateMyOrder()
        {
            EditOrderTitle = "OCity_Dialog_Exchenge_Order_Create".TranslateCache();
            var newOrder = new TradeOrder
            {
                Owner = SessionClientController.My,
                Place = PlaceCurrent,
                Tile = PlaceCurrent.Tile,
                PlaceServerId = PlaceCurrent.PlaceServerId,
                CountReady = 0,
                SellThings = new List<ThingTrade>(4),
                BuyThings = new List<ThingTrade>(4) { ThingTrade.CreateTrade(MainHelper.CashlessThingDef, 0f, QualityCategory.Awful, 1) },
                PrivatPlayers = new List<Player>(0)
            };

            SetEditOrder(newOrder);
        }

        private void DoTab1EditOrder(Rect inRect)
        {
            if (EditOrder == null)
            {
                CreateMyOrder();
            }

            bool existInServer = EditOrder.Id != 0;

            Rect rect = new Rect(0f, 0f, inRect.width, 18f);
            inRect.yMin += rect.height;
            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.Label(rect, EditOrderTitle);

            GUI.DrawTexture(new Rect((inRect.width - 64f) / 2f, inRect.y, 64f, 64f), GeneralTexture.OCE_To);

            int loopWidth = 70;
            if (EditOrderIsMy)
            {
                Rect rectLoop = new Rect(inRect.width / 2f - 50f - loopWidth, inRect.y, loopWidth, 200f);
                EditOrderShowSellThings(rectLoop, -loopWidth);

                rectLoop = new Rect(inRect.width / 2f + 50f, inRect.y, loopWidth, 200f);
                EditOrderShowBuyThings(rectLoop, loopWidth);
            }
            else
            {
                Rect rectLoop = new Rect(inRect.width / 2f - 50f - loopWidth, inRect.y, loopWidth, 200f);
                EditOrderShowBuyThings(rectLoop, -loopWidth);

                rectLoop = new Rect(inRect.width / 2f + 50f, inRect.y, loopWidth, 200f);
                EditOrderShowSellThings(rectLoop, loopWidth);
            }

            if (EditOrderCountReadyBuffer == null)
            {
                EditOrderCountReadyBuffer = new TextFieldNumericBox(() => EditOrderCountReady, (val) =>
                {
                    EditOrderCountReady = val;
                    EditOrderChange();
                }, () => !ActiveElementBlock)
                {
                    Min = 1,
                    ShowButton = false
                };
                if (!EditOrderIsMy) EditOrderCountReadyBuffer.Max = EditOrder.CountReady;
            }

            Text.Font = GameFont.Medium;
            float widthtextin = 80f;
            float widthtextx = 24f;
            var rect3 = new Rect((inRect.width - widthtextin) / 2f - widthtextx, inRect.y + 64f + 18f * 3f + 24f, 24f, 24f);
            Widgets.Label(rect3, "*");
            Text.Font = GameFont.Tiny;
            rect3.x += widthtextx;
            rect3.width = widthtextin;
            EditOrderCountReadyBuffer.Drow(rect3);
            rect3.y += rect3.height;

            if (!EditOrderIsMy)
            {
                if (EditOrderCountReady > EditOrder.CountReady) GUI.color = Color.red;
                Widgets.Label(rect3, "(max " + EditOrder.CountReady + ")");
                GUI.color = Color.white;
            }

            inRect.yMin += 230f;

            float buttonwidth = (inRect.width / 2f - 20f) / 3f;
            if (buttonwidth < 160f) buttonwidth = 160f;

            rect = new Rect(inRect.x + inRect.width - buttonwidth, inRect.y + 20f, buttonwidth, 24f);
            if (!EditOrderToTrade) GUI.color = Color.red;
            if (ActiveElementBlock) GUI.color = Color.gray;
            if (Widgets.ButtonText(rect.ContractedBy(1f),
                EditOrderIsMy
                    ? (existInServer ? "OCity_Dialog_Exchenge_Save".TranslateCache() : "OCity_Dialog_Exchenge_Create".TranslateCache())
                    : "OCity_Dialog_Exchenge_Trade".TranslateCache(),
                true, false, true))
            {
                GUI.color = Color.white;
                if (!ActiveElementBlock && EditOrderToTrade)
                {
                    SoundDefOf.Tick_High.PlayOneShotOnCamera(null);
                    if (EditOrderIsMy)
                    {
                        ApplyEditMyOrder();
                    }
                    else
                    {
                        ApplyEditOtherOrder();
                        return;
                    }

                    EditOrderChange();
                    return;
                }
            }
            GUI.color = Color.white;

            rect = new Rect(inRect.x + inRect.width - buttonwidth * 2f - 10f, inRect.y + 20f, buttonwidth, 24f);
            if (!EditOrderIsMy)
            {
                if (ActiveElementBlock) GUI.color = Color.gray;
                if (Widgets.ButtonText(rect.ContractedBy(1f), "OCity_Dialog_Exchenge_Counterproposal".TranslateCache(), true, false, true))
                {
                    GUI.color = Color.white;
                    if (!ActiveElementBlock)
                    {
                        SoundDefOf.Tick_High.PlayOneShotOnCamera(null);
                        CreateCounterproposal();
                        return;
                    }
                }
                GUI.color = Color.white;
            }
            if (EditOrderIsMy && existInServer && EditOrder.Id != 0)
            {
                if (ActiveElementBlock) GUI.color = Color.gray;
                if (Widgets.ButtonText(rect.ContractedBy(1f), "OCity_Dialog_Exchenge_Delete".TranslateCache(), true, false, true))
                {
                    GUI.color = Color.white;
                    if (!ActiveElementBlock)
                    {
                        SoundDefOf.Tick_High.PlayOneShotOnCamera(null);
                        ApplyDeleteMyOrder();
                        return;
                    }
                }
                GUI.color = Color.white;
            }

            rect = new Rect(inRect.x + inRect.width - buttonwidth * 3f - 20f, inRect.y + 20f, buttonwidth, 24f);
            if (ActiveElementBlock) GUI.color = Color.gray;
            if (Widgets.ButtonText(rect.ContractedBy(1f), "OCity_Dialog_Exchenge_Order_New".TranslateCache(), true, false, true))
            {
                GUI.color = Color.white;
                if (!ActiveElementBlock)
                {
                    SoundDefOf.Tick_High.PlayOneShotOnCamera(null);
                    SetEditOrder(null);
                    return;
                }
            }
            GUI.color = Color.white;

            Text.Anchor = TextAnchor.UpperLeft;

            rect = new Rect(inRect.x, inRect.y + 20f, inRect.x + inRect.width - 52f - buttonwidth * 4f - 40f, 24f);
            if (EditOrder.PrivatPlayers == null || EditOrder.PrivatPlayers.Count == 0)
            {
                Widgets.Label(rect, "OCity_Dialog_Exchenge_No_User_Restrictions".TranslateCache());
            }
            else
            {
                float buttonheight = 20f;
                rect = new Rect(inRect.x, inRect.y + 10f - (EditOrder.PrivatPlayers.Count - 2) * buttonheight, inRect.x + inRect.width - 52f - buttonwidth * 4f - 40f, buttonheight);
                Widgets.Label(rect, "OCity_Dialog_Exchenge_User_Restrictions".TranslateCache());
                rect.y += buttonheight;
                for (int i = 0; i < EditOrder.PrivatPlayers.Count; i++)
                {
                    rect3 = new Rect(rect.x, rect.y, rect.width - 25f, 24f);
                    Widgets.Label(rect3, EditOrder.PrivatPlayers[i].Login);

                    rect3 = new Rect(rect.xMax - 24f, rect.y, 24f, 24f);
                    if (ActiveElementBlock) GUI.color = Color.gray;
                    if (EditOrderIsMy && Widgets.ButtonImage(rect3, GeneralTexture.IconDelTex))
                    {
                        GUI.color = Color.white;
                        if (!ActiveElementBlock)
                        {
                            EditOrder.PrivatPlayers.RemoveAt(i--);
                        }
                    }
                    GUI.color = Color.white;
                    rect.y += buttonheight;
                }
            }

            if (EditOrderIsMy)
            {
                rect = new Rect(inRect.x + inRect.width - 52f - buttonwidth * 4f - 30f, inRect.y + 20f, buttonwidth, 24f);
                if (ActiveElementBlock) GUI.color = Color.gray;
                if (Widgets.ButtonText(rect.ContractedBy(1f), "OCity_Dialog_Exchenge_Add_User".TranslateCache(), true, false, true))
                {
                    GUI.color = Color.white;
                    if (!ActiveElementBlock)
                    {
                        SoundDefOf.Tick_High.PlayOneShotOnCamera(null);
                        var order = EditOrder;
                        var players = SessionClientController.Data.Players;
                        var list = new List<FloatMenuOption>(players.Count);
                        foreach (var kvp in players)
                        {
                            string pLogin = kvp.Key;
                            if (pLogin == "system") continue;
                            bool alreadyIn = false;
                            for (int pi = 0; pi < order.PrivatPlayers.Count; pi++)
                            {
                                if (order.PrivatPlayers[pi].Login == pLogin) { alreadyIn = true; break; }
                            }
                            if (!alreadyIn)
                            {
                                list.Add(new FloatMenuOption(pLogin, () =>
                                {
                                    for (int pi = 0; pi < order.PrivatPlayers.Count; pi++)
                                    {
                                        if (order.PrivatPlayers[pi].Login == pLogin) return;
                                    }
                                    order.PrivatPlayers.Add(players[pLogin].Public);
                                }));
                            }
                        }

                        if (list.Count > 0)
                        {
                            list.Sort((a, b) => string.CompareOrdinal(a.Label, b.Label));
                            Find.WindowStack.Add(new FloatMenu(list));
                        }
                    }
                }
                GUI.color = Color.white;
            }
        }

        private void EditOrderShowSellThings(Rect rect, int loopWidth)
        {
            for (int i = 0; i < EditOrder.SellThings.Count; i++)
            {
                var th = EditOrder.SellThings[i];
                var rect3 = new Rect(rect.x + (Math.Abs(loopWidth) - 64f) / 2f, rect.y, 64f, 64f);
                GameUtils.DravLineThing(rect3, th.DataThing, true, 40f, 40f);

                rect3 = new Rect(rect.x + 4f, rect.y + rect3.height + 2f, rect.width - 8f, 18f);
                rect3.height = EditOrderShowHitAndQ(rect3, th);
                rect3.y += rect3.height;

                if (!EditOrderEditBuffer.TryGetValue(th, out var editComponent))
                {
                    editComponent = new OrderEditBuffer(this, th);
                    EditOrderEditBuffer.Add(th, editComponent);
                }

                rect3.height = 24f;
                editComponent.TextField1.Drow(rect3);
                rect3.y += rect3.height * 2f; // пропуск рядка

                rect3.height = 24f;
                editComponent.TextField2.Drow(rect3);
                rect3.y += rect3.height;

                rect3.height = 18f;
                if (EditOrderIsMy)
                {
                    if (editComponent.TotalCount > th.TradeCount) GUI.color = Color.red;
                    Widgets.Label(rect3, "(max " + th.TradeCount + ")");
                    GUI.color = Color.white;
                }
                rect3.y += rect3.height;

                rect3.height = 24f;
                if (ActiveElementBlock) GUI.color = Color.gray;
                if (EditOrderIsMy && Widgets.ButtonImage(new Rect(rect3.x + (rect3.width - rect3.height) / 2f, rect3.y, rect3.height, rect3.height), GeneralTexture.IconDelTex))
                {
                    GUI.color = Color.white;
                    if (!ActiveElementBlock)
                    {
                        EditOrder.SellThings.RemoveAt(i--);
                        EditOrderChange();
                    }
                }
                GUI.color = Color.white;
                rect.x += loopWidth;
            }

            if (EditOrderIsMy && EditOrder.SellThings.Count < 6)
            {
                var rect3 = new Rect(rect.x + (Math.Abs(loopWidth) - 64f) / 2f, rect.y, 64f, 64f);
                if (ActiveElementBlock) GUI.color = Color.gray;
                if (Widgets.ButtonImage(rect3, GeneralTexture.OCE_Add))
                {
                    GUI.color = Color.white;
                    if (!ActiveElementBlock) TabIndex = 0;
                }
                GUI.color = Color.white;
            }
        }

        private void ShowSelectThingDef(ref Rect rectInLine, ThingDef selectThing, Action<string, ThingDef> setSelect)
        {
            if (selectThing != null)
            {
                rectInLine.width = 24f;
                GameUtils.DravLineThing(rectInLine, selectThing, false);
                rectInLine.x += rectInLine.width + 3f;
                rectInLine.width = Text.CalcSize(selectThing.LabelCap).x;
                Widgets.Label(rectInLine, selectThing.LabelCap);
                rectInLine.x += rectInLine.width + 3f;
                rectInLine.width = 100f;
                if (ActiveElementBlock) GUI.color = Color.gray;
                if (Widgets.ButtonText(rectInLine.ContractedBy(1f), "OCity_Dialog_CreateWorld_BtnCancel".TranslateCache(), true, false, true))
                {
                    GUI.color = Color.white;
                    if (!ActiveElementBlock)
                    {
                        SoundDefOf.Tick_High.PlayOneShotOnCamera(null);
                        setSelect("", null);
                    }
                }
                GUI.color = Color.white;
            }
            else
            {
                rectInLine.width = 100f;
                if (ActiveElementBlock) GUI.color = Color.gray;
                if (Widgets.ButtonText(rectInLine.ContractedBy(1f), "OCity_Dialog_Exchenge_Choose".TranslateCache(), true, false, true))
                {
                    GUI.color = Color.white;
                    if (!ActiveElementBlock)
                    {
                        SoundDefOf.Tick_High.PlayOneShotOnCamera(null);
                        var formm = new Dialog_SelectThingDef();
                        formm.ClearFilter();
                        formm.PostCloseAction = () =>
                        {
                            if (formm.SelectThingDef == null)
                            {
                                setSelect("", null);
                                return;
                            }
                            setSelect(formm.SelectThingDef.defName, formm.SelectThingDef);
                        };
                        Find.WindowStack.Add(formm);
                    }
                }
                GUI.color = Color.white;
            }
            rectInLine.x += rectInLine.width + 10f;
        }

        private void EditOrderShowBuyThings(Rect rect, int loopWidth)
        {
            for (int i = 0; i < EditOrder.BuyThings.Count; i++)
            {
                var th = EditOrder.BuyThings[i];
                var rect3 = new Rect(rect.x + (Math.Abs(loopWidth) - 64f) / 2f, rect.y, 64f, 64f);
                if (th.Concrete)
                    GameUtils.DravLineThing(rect3, th.DataThing, true, 40f, 40f);
                else
                    GameUtils.DravLineThing(rect3, th, true, Color.gray, 40f, 40f);

                rect3 = new Rect(rect.x + 4f, rect.y + rect3.height + 2f, rect.width - 8f, 18f);
                rect3.height = EditOrderShowHitAndQ(rect3, th);
                rect3.y += rect3.height;

                if (!EditOrderEditBuffer.TryGetValue(th, out var editComponent))
                {
                    editComponent = new OrderEditBuffer(this, th);
                    EditOrderEditBuffer.Add(th, editComponent);
                }

                rect3.height = 24f;
                editComponent.TextField1.Drow(rect3);
                rect3.y += rect3.height * 2f; // пропуск рядка

                rect3.height = 24f;
                editComponent.TextField2.Drow(rect3);
                rect3.y += rect3.height;

                rect3.height = 18f;
                if (!EditOrderIsMy)
                {
                    if (editComponent.TotalCount > th.TradeCount) GUI.color = Color.red;
                    Widgets.Label(rect3, "(max " + th.TradeCount + ")");
                    GUI.color = Color.white;
                }
                rect3.y += rect3.height;

                rect3.height = 24f;
                if (ActiveElementBlock) GUI.color = Color.gray;
                if (EditOrderIsMy && Widgets.ButtonImage(new Rect(rect3.x + (rect3.width - rect3.height) / 2f, rect3.y, rect3.height, rect3.height), GeneralTexture.IconDelTex))
                {
                    GUI.color = Color.white;
                    if (!ActiveElementBlock)
                    {
                        EditOrder.BuyThings.RemoveAt(i--);
                        EditOrderChange();
                    }
                }
                GUI.color = Color.white;
                rect.x += loopWidth;
            }

            if (EditOrderIsMy && EditOrder.BuyThings.Count < 6)
            {
                var rect3 = new Rect(rect.x + (Math.Abs(loopWidth) - 64f) / 2f, rect.y, 64f, 64f);
                if (ActiveElementBlock) GUI.color = Color.gray;
                if (Widgets.ButtonImage(rect3, GeneralTexture.OCE_Add))
                {
                    GUI.color = Color.white;
                    if (!ActiveElementBlock)
                    {
                        SoundDefOf.Tick_High.PlayOneShotOnCamera(null);
                        var formm = new Dialog_SelectThingDef();
                        formm.ClearFilter();
                        formm.PostCloseAction = () =>
                        {
                            if (formm.SelectThingDef == null) return;
                            var th = ThingTrade.CreateTrade(formm.SelectThingDef, formm.SelectHitPointsPercents.min, formm.SelectQualities.min, 1);
                            EditOrder.BuyThings.Add(th);
                            EditOrderChange();
                        };
                        Find.WindowStack.Add(formm);
                        return;
                    }
                }
                GUI.color = Color.white;
            }
        }

        private float EditOrderShowHitAndQ(Rect rect, ThingTrade th)
        {
            var outHeight = rect.height;
            Text.Anchor = TextAnchor.UpperCenter;

            var rect1 = new Rect(rect.x, rect.y, rect.width, rect.height + (th.Quality < 0 ? rect.height : 0f));
            if (th.NotTrade) GUI.color = Color.red;
            if (Mouse.IsOver(rect1))
            {
                TooltipHandler.TipRegion(rect1, th.Name);
            }
            Widgets.Label(rect1, th.Name);
            GUI.color = Color.white;
            rect.y += rect.height;
            outHeight += rect.height;

            var rect2 = new Rect(rect.x, rect.y, rect.width, rect.height);
            if (th.Quality >= 0)
            {
                if (th.Concrete)
                {
                    if (Mouse.IsOver(rect2))
                    {
                        TooltipHandler.TipRegion(rect2, "OCity_Dialog_Exchenge_Quality".TranslateCache() + ((QualityCategory)th.Quality).GetLabel());
                    }
                    Widgets.Label(rect2, ((QualityCategory)th.Quality).GetLabelShort());
                }
                else
                {
                    if (Mouse.IsOver(rect2))
                    {
                        TooltipHandler.TipRegion(rect2, "OCity_Dialog_Exchenge_QualityNo_Less_Than".TranslateCache() + ((QualityCategory)th.Quality).GetLabel());
                    }
                    Widgets.Label(rect2, th.Quality == 0 ? "OCity_Dialog_Exchenge_All".TranslateCache() : ((QualityCategory)th.Quality).GetLabelShort() + "+");
                }
            }
            rect.y += rect.height;
            outHeight += rect.height;

            var rect3 = new Rect(rect.xMax - rect.height, rect.y, rect.height, rect.height);
            if (th.HitPoints > 0)
            {
                if (th.WornByCorpse)
                {
                    if (Mouse.IsOver(rect3))
                    {
                        TooltipHandler.TipRegion(rect3, (th.Concrete ? "OCity_Dialog_Exchenge_FromCorpse" : "OCity_Dialog_Exchenge_FromCorpseD").TranslateCache());
                    }
                    if (EditOrderIsMy && !th.Concrete)
                    {
                        if (ActiveElementBlock) GUI.color = Color.gray;
                        if (Widgets.ButtonImage(rect3, GeneralTexture.IconSkull, Color.green))
                        {
                            GUI.color = Color.white;
                            if (ActiveElementBlock) return outHeight;
                            th.WornByCorpse = false;
                        }
                        GUI.color = Color.white;
                    }
                    else
                        Widgets.DrawTextureFitted(rect3, GeneralTexture.IconSkull, 1f);
                }
                else
                {
                    if (Mouse.IsOver(rect3))
                    {
                        TooltipHandler.TipRegion(rect3, (th.Concrete ? "OCity_Dialog_Exchenge_FromNotCorpse" : "OCity_Dialog_Exchenge_FromNotCorpseD").TranslateCache());
                    }
                    if (EditOrderIsMy && !th.Concrete)
                    {
                        if (ActiveElementBlock) GUI.color = Color.gray;
                        if (Widgets.ButtonImage(rect3, GeneralTexture.IconSkull, Color.gray))
                        {
                            GUI.color = Color.white;
                            if (ActiveElementBlock) return outHeight;
                            th.WornByCorpse = true;
                        }
                        GUI.color = Color.white;
                    }
                }
            }
            GUI.color = Color.white;

            // ОПТИМІЗАЦІЯ: розрахунок рядка підказки лише під час наведення миші
            if (CachedHitPointsMeasureWidth == 0f)
            {
                CachedHitPointsMeasureWidth = Text.CalcSize("888/888 ").x;
                CachedPercentMeasureWidth = Text.CalcSize("188% ").x;
            }

            int maxHp = th.MaxHitPoints > 0 ? th.MaxHitPoints : 1;
            if (th.Concrete)
            {
                if (th.HitPoints >= 0)
                {
                    rect3 = new Rect(rect.xMax - rect3.width - CachedHitPointsMeasureWidth, rect.y, CachedHitPointsMeasureWidth, rect.height);
                    if (Mouse.IsOver(rect3))
                    {
                        TooltipHandler.TipRegion(rect3, "OCity_Dialog_Exchenge_Whole_On".TranslateCache(th.HitPoints, th.MaxHitPoints, (th.HitPoints * 100 / maxHp).ToString()));
                    }
                    Widgets.Label(rect3, th.HitPoints + "/" + th.MaxHitPoints);
                }
            }
            else
            {
                if (th.HitPoints > 0)
                {
                    rect3 = new Rect(rect.xMax - rect3.width - CachedPercentMeasureWidth, rect.y, CachedPercentMeasureWidth, rect.height);
                    if (Mouse.IsOver(rect3))
                    {
                        TooltipHandler.TipRegion(rect3, "OCity_Dialog_Exchenge_Whole_Less_Than".TranslateCache() + (th.HitPoints * 100 / maxHp).ToString() + "%");
                    }
                    Widgets.Label(rect3, (th.HitPoints * 100 / maxHp).ToString() + "%");
                }
            }
            return outHeight;
        }

        private void UpdateOrdersList()
        {
            try
            {
                OrdersGrid = null;

                SessionClientController.Command((connect) =>
                {
                    connect.ErrorMessage = null;
                    StatusLoadOrders = ".";

                    List<int> tiles = null;
                    if (FilterTileLength > 0)
                    {
                        var rawTradeObjects = ExchengeUtils.GetWorldObjectsForTrade();
                        if (rawTradeObjects != null)
                        {
                            var distinctTiles = new HashSet<int>();
                            foreach (var wo in rawTradeObjects)
                            {
                                if (distinctTiles.Add(wo.Tile))
                                {
                                    if (GameUtils.DistanceBetweenTile(PlaceCurrent.Tile, wo.Tile) <= FilterTileLength)
                                    {
                                        if (tiles == null) tiles = new List<int>();
                                        tiles.Add(wo.Tile);
                                    }
                                }
                            }
                        }
                    }

                    StatusLoadOrders = "...";
                    var orders = connect.ExchengeLoad(tiles, FilterBuy, FilterSell);
                    StatusLoadOrders = $"({orders.Count})...";

                    Loger.Log("LoaderOrdersStart " + (orders?.Count ?? 0));

                    var tasks = new List<AnyLoadTask>(orders.Count * 2);
                    for (int i = 0; i < orders.Count; i++)
                    {
                        var sts = orders[i].SellThings;
                        if (sts != null)
                        {
                            for (int j = 0; j < sts.Count; j++)
                            {
                                tasks.Add(new AnyLoadTask { Hash = sts[j].DataHash });
                            }
                        }
                    }

                    LoaderOrders = new AnyLoad(tasks, (loader) =>
                    {
                        using (GameUtils.NormalGameError())
                        {
                            var dicHash = new Dictionary<long, string>(loader.ListLoad.Count);
                            for (int i = 0; i < loader.ListLoad.Count; i++)
                            {
                                var item = loader.ListLoad[i];
                                if (!dicHash.ContainsKey(item.Hash))
                                {
                                    dicHash[item.Hash] = item.Data;
                                }
                            }

                            for (int i = 0; i < orders.Count; i++)
                            {
                                var order = orders[i];
                                var sts = order.SellThings;
                                if (sts != null)
                                {
                                    for (int s = 0; s < sts.Count; s++)
                                    {
                                        if (dicHash.TryGetValue(sts[s].DataHash, out var data))
                                        {
                                            sts[s].Data = data;
                                        }
                                    }
                                }
                                order.Place.DayPath = GameUtils.DistanceBetweenTile(PlaceCurrent.Tile, order.Tile);
                            }

                            orders.Sort((a, b) => a.Place.DayPath.CompareTo(b.Place.DayPath));
                            Orders = orders;

                            StatusLoadOrders = null;
                            LoaderOrders = null;
                            OrdersGrid = null;
                        }
                    },
                    (loader, precent) =>
                    {
                        StatusLoadOrders = $"({orders.Count}) {precent}%";
                    },
                    (loader, error) =>
                    {
                        LoaderOrdersCancel();
                    });

                    if (!string.IsNullOrEmpty(connect.ErrorMessage))
                    {
                        Loger.Log("Client ExchengeLoad error: " + connect.ErrorMessage?.ServerTranslate(), Loger.LogLevel.ERROR);
                    }
                });
            }
            catch (Exception exp)
            {
                ExceptionUtil.ExceptionLog(exp, "Dialog_Exchenge UpdateOrdersList Exception");
            }
        }

        private void ApplyDeleteMyOrder()
        {
            SessionClientController.Command((connect) =>
            {
                SoundDefOf.Tick_High.PlayOneShotOnCamera(null);
                ActiveElementBlock = true;
                EditOrder.Id = -EditOrder.Id;
                if (!connect.ExchengeEdit(EditOrder))
                {
                    EditOrder.Id = -EditOrder.Id;
                    Loger.Log("Client ExchengeEdit error: " + connect.ErrorMessage?.ServerTranslate(), Loger.LogLevel.ERROR);
                    Find.WindowStack.Add(new Dialog_Input("OCity_Dialog_Exchenge_Action_Not_CarriedOut".TranslateCache(), connect.ErrorMessage?.ServerTranslate(), true));
                }
                else
                {
                    SetEditOrder(null);
                }
                UpdateWorldAndOrdersList();
            });
        }

        private void ApplyEditMyOrder()
        {
            if (!EditOrderToTrade) return;

            if (SessionClientController.Data?.BackgroundSaveGameOff == true)
            {
                Loger.Log("Client ApplyEditMyOrder Cancel BackgroundSaveGameOff", Loger.LogLevel.EXCHANGE);
                return;
            }

            Loger.Log($"Client ApplyEditMyOrder EditOrderCountReady={EditOrderCountReady}", Loger.LogLevel.EXCHANGE);

            List<TransferableOneWay> thingsForAdd = null;
            if (!(WorldObjectCurrent is TradeThingsOnline))
            {
                var availInOrder = GetAvailableThingsInEditOrder();
                if (SessionClientController.Data != null && SessionClientController.Data.CashlessBalance > 0)
                {
                    availInOrder.Add(GameUtils.GetCashlessBalanceThing(SessionClientController.Data.CashlessBalance));
                }

                thingsForAdd = GameUtils.ChechToTrade(
                    EditOrder.SellThings,
                    availInOrder,
                    GetAvailableThings(true, true),
                    out _,
                    EditOrderCountReady);
            }

            SoundDefOf.Tick_High.PlayOneShotOnCamera(null);
            EditOrder.CountReady = EditOrderCountReady;

            Action exchengeEdit = () =>
            {
                SessionClientController.Command((connect) =>
                {
                    if (!connect.ExchengeEdit(EditOrder))
                    {
                        Loger.Log("Client ExchengeEdit error: " + connect.ErrorMessage?.ServerTranslate());
                        Find.WindowStack.Add(new Dialog_Input("OCity_Dialog_CreateWorld_Err".TranslateCache(), connect.ErrorMessage?.ServerTranslate(), true));
                    }
                    else
                    {
                        SetEditOrder(null);
                    }
                });
            };

            ActiveElementBlock = true;
            if (thingsForAdd != null && thingsForAdd.Count > 0)
            {
                MoveSelectThings(WorldObjectStorageCurrentTile, thingsForAdd, exchengeEdit);
            }
            else
            {
                exchengeEdit();
                UpdateWorldAndOrdersList();
            }
        }

        private void ApplyEditOtherOrder()
        {
            if (!EditOrderToTrade) return;

            if (SessionClientController.Data?.BackgroundSaveGameOff == true)
            {
                Loger.Log("Client ApplyEditOtherOrder Cancel BackgroundSaveGameOff", Loger.LogLevel.EXCHANGE);
                return;
            }

            List<TransferableOneWay> thingsForBuy = null;
            if (!(WorldObjectCurrent is TradeThingsOnline))
            {
                var cashless = GameUtils.GetCashlessBalanceThingList(SessionClientController.Data?.CashlessBalance ?? 0f);
                thingsForBuy = GameUtils.ChechToTrade(
                    EditOrder.BuyThings,
                    cashless,
                    GetAvailableThings(true, true),
                    out _,
                    EditOrderCountReady);
            }

            SoundDefOf.Tick_High.PlayOneShotOnCamera(null);

            Action exchengeEdit = () =>
            {
                SessionClientController.Command((connect) =>
                {
                    if (!connect.ExchengeBuy(EditOrder.Id, EditOrderCountReady))
                    {
                        Loger.Log("Client ExchengeEdit error: " + connect.ErrorMessage?.ServerTranslate(), Loger.LogLevel.ERROR);
                        Find.WindowStack.Add(new Dialog_Input("OCity_Dialog_Exchenge_Action_Not_CarriedOut".TranslateCache(), connect.ErrorMessage?.ServerTranslate(), true));
                    }
                    else
                    {
                        SetEditOrder(null);
                    }
                });
            };

            ActiveElementBlock = true;
            if (thingsForBuy != null && thingsForBuy.Count > 0)
            {
                MoveSelectThings(WorldObjectStorageCurrentTile, thingsForBuy, exchengeEdit);
            }
            else
            {
                exchengeEdit();
                UpdateWorldAndOrdersList();
            }
        }

        private void MoveSelectThings(WorldObject toWorldObject, List<TransferableOneWay> selectTow = null, Action finish = null)
        {
            if (selectTow == null) selectTow = AddThingGrid.DataSource;

            if (WorldObjectCurrent is TradeThingsOnline
                && !(toWorldObject is TradeThingsOnline)
                && SessionClientController.Data != null
                && SessionClientController.Data.CashlessBalance < 0)
            {
                Loger.Log("Client MoveSelectThings cancel: Impossible with negative balance", Loger.LogLevel.ERROR);
                Find.WindowStack.Add(new Dialog_Input("OCity_DialogExchenge_NegativeBalance".Translate(), "OCity_DialogExchenge_NegativeBalance".Translate() + " " + SessionClientController.Data.CashlessBalance, true));
                return;
            }

            if (!ExchengeUtils.MoveSelectThings(WorldObjectCurrent, toWorldObject, selectTow, () =>
            {
                finish?.Invoke();
                UpdateWorldAndOrdersList(true);
                AddThingGrid = null;
            }))
            {
                ActiveElementBlock = false;
            }
        }

        private void CreateCounterproposal()
        {
            if (EditOrderIsMy) return;

            var newOrderThings = EditOrder.Clone();
            var buyThings = newOrderThings.BuyThings;
            var sellThings = newOrderThings.SellThings;

            EditOrderTitle = "OCity_Dialog_Exchenge_Counterproposal".TranslateCache();
            var newOrder = new TradeOrder
            {
                Owner = SessionClientController.My,
                Place = PlaceCurrent,
                Tile = PlaceCurrent.Tile,
                PlaceServerId = PlaceCurrent.PlaceServerId,
                CountReady = 0,
                SellThings = new List<ThingTrade>(),
                BuyThings = new List<ThingTrade>(),
                PrivatPlayers = new List<Player> { EditOrder.Owner }
            };

            SetEditOrder(newOrder);

            var ts = GameUtils.ChechToTrade(buyThings, GetAvailableThings(), null, out int rate);
            newOrder.BuyThings = sellThings;
            newOrder.SellThings = new List<ThingTrade>();

            if (ts != null && rate > 0)
            {
                EditOrderCountReady = rate;
                for (int i = 0; i < ts.Count; i++)
                {
                    var item = ts[i];
                    if (item.CountToTransfer == 0) continue;
                    if (MainHelper.DebugMode) Loger.Log($"CreateCounterproposal rate={rate} CountToTransfer={item.CountToTransfer}", Loger.LogLevel.EXCHANGE);
                    var th = ThingTrade.CreateTrade(item.AnyThing, item.CountToTransfer / rate);
                    newOrder.SellThings.Add(th);
                }
            }

            if (EditOrderCountReady < 1) EditOrderCountReady = 1;
            EditOrderChange();
        }
    }
}