using Model;
using OCUnion;
using OCUnion.Transfer;
using OCUnion.Transfer.Model;
using RimWorldOnlineCity.GameClasses;
using RimWorldOnlineCity.Services;
using RimWorldOnlineCity.UI;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using Verse;

namespace RimWorldOnlineCity
{
    public class PanelInfoPlayer : DialogControlBase
    {
        public PlayerClient player;
        private bool Loading = true;
        private WorldObjectsValues AllWorldObjects;
        private ModelPlayerInfoExtended info = null;

        public Vector2 ScrollPosition = new Vector2();
        public float Height = 0f;

        private static readonly Color WindowBGBorderColor = new ColorInt(97, 108, 122).ToColor;
        private static readonly Texture2D SkillBarFillTex = SolidColorMaterials.NewSolidColorTexture(new Color(1f, 1f, 1f, 0.1f));

        private float historyMax = 1f;
        private List<CaravanOnline> _sortedWObjects;
        private List<CaravanOnline> _lastSourceWObjects;

        // Кешовані рядки перекладів
        private static string CachedLastSaveTimePrefix;
        private static string CachedBaseCountPrefix;
        private static string CachedCaravanCountPrefix;
        private static string CachedMarketValuePrefix;
        private static string CachedMarketValuePawnPrefix;
        private static string CachedMarketValueTradingPrefix;
        private static string[] SkillNames;

        public PanelInfoPlayer(PlayerClient player)
        {
            Init(player);
        }

        private void Init(PlayerClient pl)
        {
            this.player = pl;
            Loading = true;
            info = null;
            _sortedWObjects = null;
            _lastSourceWObjects = null;

            Task.Run(() =>
            {
                try
                {
                    SessionClientController.Command((connect) =>
                    {
                        info = connect.GetPlayerInfoExtended(pl.Public.Login);
                    });
                }
                catch (Exception ex)
                {
                    Loger.Log("Exception PanelInfoPlayer " + ex.ToString());
                }
            });
        }

        private void Init2()
        {
            Loading = false;
            AllWorldObjects = player.CostWorldObjects();

            historyMax = AllWorldObjects?.MarketValueTotal ?? 0f;
            if (info?.MarketValueHistory != null && info.MarketValueHistory.Count > 0)
            {
                for (int i = 0; i < info.MarketValueHistory.Count; i++)
                {
                    if (info.MarketValueHistory[i] > historyMax) historyMax = info.MarketValueHistory[i];
                }
            }
            if (historyMax <= 0f) historyMax = 1f;

            InitStaticLabels();
        }

        private static void InitStaticLabels()
        {
            if (CachedLastSaveTimePrefix == null)
            {
                CachedLastSaveTimePrefix = "OCity_PlayerClient_LastSaveTime".Translate().Replace("{2}", "").ToString();
                CachedBaseCountPrefix = "OCity_PlayerClient_baseCount".Translate().Replace("{3}", "").ToString();
                CachedCaravanCountPrefix = "OCity_PlayerClient_caravanCount".Translate().Replace("{4}", "").ToString();
                CachedMarketValuePrefix = "OCity_PlayerClient_marketValue".Translate().Replace("{5}", "").ToString();
                CachedMarketValuePawnPrefix = "OCity_PlayerClient_marketValuePawn".Translate().Replace("{6}", "").ToString();
                CachedMarketValueTradingPrefix = "OCity_PlayerClient_marketValueTrading".Translate().Replace("{7}", "").ToString();

                SkillNames = new string[]
                {
                    "OC_Shooting".Translate(),
                    "OC_Melee".Translate(),
                    "OC_Construction".Translate(),
                    "OC_Mining".Translate(),
                    "OC_Cooking".Translate(),
                    "OC_Plants".Translate(),
                    "OC_Animals".Translate(),
                    "OC_Crafting".Translate(),
                    "OC_Artistic".Translate(),
                    "OC_Medical".Translate(),
                    "OC_Social".Translate(),
                    "OC_Intellectual".Translate()
                };
            }
        }

        private List<CaravanOnline> GetSortedWObjects()
        {
            if (player?.WObjects == null) return null;
            if (_sortedWObjects == null || _lastSourceWObjects != player.WObjects)
            {
                _lastSourceWObjects = player.WObjects;
                _sortedWObjects = new List<CaravanOnline>(player.WObjects);
                _sortedWObjects.Sort((a, b) =>
                {
                    long prioA = (a is BaseOnline ? 1000000L : 2000000L) + (a.OnlineWObject?.PlaceServerId ?? 0);
                    long prioB = (b is BaseOnline ? 1000000L : 2000000L) + (b.OnlineWObject?.PlaceServerId ?? 0);
                    return prioA.CompareTo(prioB);
                });
            }
            return _sortedWObjects;
        }

        public void Drow(Rect inRect)
        {
            if (player == null) return;
            player = player.Refrash();

            if (Loading && info != null) Init2();

            var chatAreaInner = new Rect(0, 0, inRect.width - ListBox<string>.WidthScrollLine, 0);
            if (chatAreaInner.width <= 0) return;
            chatAreaInner.height = Height;
            ScrollPosition = GUI.BeginScrollView(inRect, ScrollPosition, chatAreaInner);
            GUILayout.BeginArea(chatAreaInner);
            Text.Anchor = TextAnchor.MiddleLeft;

            var curHeight = 0f;
            float totalWidth = chatAreaInner.width;

            if (Loading || info == null)
            {
                DrawLoading(totalWidth, ref curHeight);
                Height = curHeight;
                Text.Anchor = TextAnchor.UpperLeft;
                GUILayout.EndArea();
                GUI.EndScrollView();
                return;
            }

            // 1. Секція профілю гравця
            DrawPlayerProfileSection(totalWidth, ref curHeight);

            // 2. Секція рейтингу та графіка
            DrawRatingAndGraphSection(totalWidth, ref curHeight);

            // 3. Секція командних навичок
            DrawTeamSkillsSection(totalWidth, ref curHeight);

            // 4. Секції поселень та караванів
            var wObjects = GetSortedWObjects();
            if (wObjects != null)
            {
                for (int i = 0; i < wObjects.Count; i++)
                {
                    DrawWorldObjectSection(wObjects[i], totalWidth, ref curHeight);
                }
            }

            Height = curHeight;
            Text.Anchor = TextAnchor.UpperLeft;
            GUILayout.EndArea();
            GUI.EndScrollView();
        }

        private void DrawLoading(float totalWidth, ref float curHeight)
        {
            const float iconBorder = 3f;
            var prect = new Rect(0, curHeight, 128f + iconBorder * 2f, 128f + iconBorder * 2f);
            var icon = GameInterfaceHelper.GetPlayerIcon(player.Public.Login);
            if (icon != null)
            {
                GUI.DrawTexture(prect, Command.BGTexShrunk);
                GUI.DrawTexture(prect.ContractedBy(iconBorder), icon);
            }

            var textRect = new Rect(128f + iconBorder * 2f + 16f, curHeight + 40f, totalWidth - 128f - 32f, 50f);
            Text.Font = GameFont.Medium;
            Widgets.Label(textRect, player.Public.Login + Environment.NewLine + "OC_Loading".Translate() + "...");
            Text.Font = GameFont.Small;

            curHeight += 128f + iconBorder * 2f + 12f;
        }

        private void DrawPlayerProfileSection(float totalWidth, ref float curHeight)
        {
            const float iconHeight = 128f;
            const float iconBorder = 3f;

            var prect = new Rect(0, curHeight, 128f + iconBorder * 2f, iconHeight + iconBorder * 2f);
            var icon = GameInterfaceHelper.GetPlayerIcon(player.Public.Login);
            if (icon != null)
            {
                GUI.DrawTexture(prect, Command.BGTexShrunk);
                GUI.DrawTexture(prect.ContractedBy(iconBorder), icon);
            }

            float col0 = (totalWidth - 128f - 8f) * 0.45f;
            float col1 = (totalWidth - 128f - 8f) - col0;
            const int rowCount = 4;
            float rowHeight = iconHeight / rowCount;
            float startX = 128f + iconBorder * 2f + 16f;

            // Рядок 0: Логін
            Rect r = new Rect(startX, curHeight + 1, col0, rowHeight);
            Text.Font = GameFont.Medium;
            Widgets.Label(r, player.Public.Login);
            Text.Font = GameFont.Small;

            // Рядок 1: Онлайн/Офлайн
            r = new Rect(startX, curHeight + rowHeight + 2, col0, rowHeight);
            if (player.Online)
            {
                Widgets.Label(r, "Online");
                var iconEmoji = GeneralTexture.Get.GetEmoji("green_circle");
                GUI.DrawTexture(new Rect(r.x + 48f, r.y + 4f, 12f, 12f), iconEmoji);
            }
            else
            {
                Widgets.Label(r, "Offline");
                var iconEmoji = GeneralTexture.Get.GetEmoji("red_circle");
                GUI.DrawTexture(new Rect(r.x + 48f, r.y + 4f, 12f, 12f), iconEmoji);
            }

            // Рядок 2: Час збереження
            r = new Rect(startX, curHeight + rowHeight * 2 + 3, col0, rowHeight);
            string saveTimeStr = (player.Public.LastSaveTime == DateTime.MinValue)
                ? "OCity_PlayerClient_LastSaveTimeNon".Translate().ToString()
                : player.Public.LastSaveTime.ToGoodUtcString();
            Widgets.Label(r, CachedLastSaveTimePrefix + saveTimeStr);

            // Рядок 3: Тіки
            r = new Rect(startX, curHeight + rowHeight * 3 + 4, col0, rowHeight);
            Widgets.Label(r, string.Format("OCity_PlayerClient_LastTick".Translate(), player.Public.LastTick / 3600000, player.Public.LastTick / 60000));

            // Колонка 1 (Бази, каравани, багатство)
            float startX1 = startX + col0;

            // Рядок 4: Бази та каравани
            r = new Rect(startX1, curHeight + 1, col1, rowHeight);
            GUI.DrawTexture(new Rect(r.x, r.y, 32f, 32f), GeneralTexture.HomeAreaOn);
            r.xMin += 34f;
            string txt = CachedBaseCountPrefix + AllWorldObjects.BaseCount;
            Widgets.Label(r, txt);
            r.xMin += Text.CalcSize(txt).x + 4f;

            GUI.DrawTexture(new Rect(r.x, r.y, 32f, 32f), GeneralTexture.Caravan);
            r.xMin += 34f;
            Widgets.Label(r, CachedCaravanCountPrefix + AllWorldObjects.CaravanCount);

            // Рядок 5: Вартість речей
            r = new Rect(startX1, curHeight + rowHeight + 2, col1, rowHeight);
            GUI.DrawTexture(new Rect(r.x, r.y, 32f, 32f), GeneralTexture.ItemStash);
            r.xMin += 34f;
            Widgets.Label(r, CachedMarketValuePrefix + AllWorldObjects.MarketValue.ToStringMoney());

            // Рядок 6: Вартість людей/тварин
            r = new Rect(startX1, curHeight + rowHeight * 2 + 3, col1, rowHeight);
            GUI.DrawTexture(new Rect(r.x, r.y, 32f, 32f), GeneralTexture.ItemStash);
            r.xMin += 34f;
            Widgets.Label(r, CachedMarketValuePawnPrefix + AllWorldObjects.MarketValuePawn.ToStringMoney());

            // Рядок 7: Торговий баланс
            r = new Rect(startX1, curHeight + rowHeight * 3 + 4, col1, rowHeight);
            GUI.DrawTexture(new Rect(r.x, r.y, 32f, 32f), GeneralTexture.OpenBox);
            r.xMin += 34f;
            Widgets.Label(r, CachedMarketValueTradingPrefix + (AllWorldObjects.MarketValueBalance + AllWorldObjects.MarketValueStorage).ToStringMoney());

            curHeight += iconHeight + iconBorder * 2f + 10f;
            DrawSeparator(totalWidth, ref curHeight);
        }

        private void DrawRatingAndGraphSection(float totalWidth, ref float curHeight)
        {
            const float iconHeight = 128f;
            const float iconBorder = 3f;
            float startX = 128f + iconBorder * 2f + 16f;

            // Ліва частина: Ачівки та Рейтинг
            var iconRect = new Rect(0, curHeight, 128f + iconBorder * 2f, iconHeight + iconBorder * 2f);
            var barRect = new Rect(iconRect) { height = (iconHeight + iconBorder * 2f) / 3f };

            if (info.Achievements != null && info.Achievements.Count > 0)
            {
                float w = 32f;
                int count = info.Achievements.Count;
                for (int i = 0; i < count; i++)
                {
                    float x = (barRect.width - w) / (count + 1) * (i + 1);
                    var texture = ContentFinder<Texture2D>.Get(info.Achievements[i], false) ?? GeneralTexture.Get.ByName(info.Achievements[i]);
                    var ar = new Rect(barRect.x + x, barRect.y, w, w);
                    if (texture != null) GUI.DrawTexture(ar, texture);
                    if (Mouse.IsOver(ar)) Widgets.DrawHighlight(ar);
                    TooltipHandler.TipRegion(ar, ("OC_Achievements_" + info.Achievements[i]).Translate());
                }
            }

            barRect.y += barRect.height;
            var anchor = Text.Anchor;
            Text.Anchor = TextAnchor.MiddleCenter;
            var barRect2 = new Rect(barRect);
            Text.Font = GameFont.Medium;
            if (info.MarketValueRanking > 0)
            {
                barRect2.xMax -= 16f;
                Widgets.Label(barRect2, "OC_PlayerClient_Rating".Translate() + " " + info.MarketValueRanking);
            }
            else
            {
                Widgets.Label(barRect2, "OC_PlayerClient_NoRating".Translate());
            }
            Text.Font = GameFont.Small;

            if (info.MarketValueRanking > 0)
            {
                barRect2 = new Rect(barRect.x + barRect.width - 16f, barRect.y + 16f, 16f, 16f);
                var tl = "OC_PlayerClient_PastRating".Translate() + " " + (info.MarketValueRankingLast == 0 ? "-" : info.MarketValueRankingLast.ToString());
                if (info.MarketValueRankingLast == 0 || info.MarketValueRankingLast > info.MarketValueRanking)
                {
                    GUI.DrawTexture(barRect2, GeneralTexture.RankingUp);
                    if (Mouse.IsOver(barRect2)) Widgets.DrawHighlight(barRect2);
                    TooltipHandler.TipRegion(barRect2, tl);
                }
                else if (info.MarketValueRankingLast < info.MarketValueRanking)
                {
                    GUI.DrawTexture(barRect2, GeneralTexture.RankingDown);
                    if (Mouse.IsOver(barRect2)) Widgets.DrawHighlight(barRect2);
                    TooltipHandler.TipRegion(barRect2, tl);
                }

                barRect.y += 32f;
                int precent = (info.RankingCount - 1) > 0 ? 100 * (info.RankingCount - info.MarketValueRanking) / (info.RankingCount - 1) : 100;
                Widgets.Label(barRect, string.Format("OC_PlayerClient_BetterPlayers".Translate(), precent));
            }
            Text.Anchor = anchor;

            // Центральна частина: Графік вартості
            const float widthCol = 4f;
            var graRect = new Rect(startX, curHeight, 8f + (widthCol + 1f) * 60, iconHeight + 4f);
            GUI.color = WindowBGBorderColor;
            Widgets.DrawBox(graRect);
            GUI.color = Color.white;

            var barColRect = graRect.ContractedBy(4);
            barColRect.width = widthCol;

            if (info.MarketValueHistory != null && historyMax > 0f)
            {
                int historyCount = Math.Min(info.MarketValueHistory.Count, 60);
                for (int i = 0; i < historyCount; i++)
                {
                    float val = Mathf.Clamp01(info.MarketValueHistory[i] / historyMax);
                    float pix = val * barColRect.height;
                    if (pix >= 1f)
                    {
                        GUI.DrawTexture(new Rect(barColRect.x, barColRect.y + barColRect.height - pix, barColRect.width, pix), Command.BGTexShrunk);
                    }
                    if (Mouse.IsOver(barColRect)) Widgets.DrawHighlight(barColRect);
                    TooltipHandler.TipRegion(barColRect, info.MarketValueHistory[i].ToStringMoney());
                    barColRect.x += widthCol + 1f;
                }
            }

            // Права частина: Цифри статистики
            float statsX = startX + graRect.width + 8f;
            float statsW = totalWidth - statsX;
            float statsRowH = (iconHeight + 4f) / 4f;

            // Вартість всього
            var itemRect = new Rect(statsX, curHeight, statsW, statsRowH);
            GUI.DrawTexture(new Rect(itemRect.x, itemRect.y, 32f, 32f), GeneralTexture.ItemStash);
            itemRect.xMin += 34f;
            Widgets.Label(itemRect, "OC_PlayerClient_TotalCost".Translate().ToString() + ": " + AllWorldObjects.MarketValueTotal.ToStringMoney());

            // Максимум вартості
            itemRect = new Rect(statsX, curHeight + statsRowH, statsW, statsRowH);
            GUI.DrawTexture(new Rect(itemRect.x, itemRect.y, 32f, 32f), GeneralTexture.ItemStash);
            itemRect.xMin += 34f;
            Widgets.Label(itemRect, "OC_PlayerClient_Maximum".Translate().ToString() + ": " + historyMax.ToStringMoney());

            // Пішаки та стан
            float pBlockW = statsW / 16f;
            itemRect = new Rect(statsX, curHeight + statsRowH * 2, pBlockW * 3.5f, statsRowH);
            if (Mouse.IsOver(itemRect)) Widgets.DrawHighlight(itemRect);
            TooltipHandler.TipRegion(itemRect, "OC_PlayerClient_TotalColonists".Translate());
            GUI.DrawTexture(new Rect(itemRect.x, itemRect.y, 32f, 32f), GeneralTexture.Pawns);
            itemRect.xMin += 34f;
            Widgets.Label(itemRect, info.ColonistsCount.ToString());

            itemRect = new Rect(statsX + pBlockW * 3.5f, curHeight + statsRowH * 2, pBlockW * 3.5f, statsRowH);
            if (Mouse.IsOver(itemRect)) Widgets.DrawHighlight(itemRect);
            TooltipHandler.TipRegion(itemRect, "OC_PlayerClient_ColonistsRequiringTreatment".Translate());
            GUI.DrawTexture(new Rect(itemRect.x, itemRect.y, 32f, 32f), GeneralTexture.PawnsNeedingTend);
            itemRect.xMin += 34f;
            Widgets.Label(itemRect, info.ColonistsNeedingTend.ToString());

            itemRect = new Rect(statsX + pBlockW * 7f, curHeight + statsRowH * 2, pBlockW * 3.5f, statsRowH);
            if (Mouse.IsOver(itemRect)) Widgets.DrawHighlight(itemRect);
            TooltipHandler.TipRegion(itemRect, "OC_PlayerClient_ColonistsUnconscious".Translate());
            GUI.DrawTexture(new Rect(itemRect.x, itemRect.y, 32f, 32f), GeneralTexture.PawnsDown);
            itemRect.xMin += 34f;
            Widgets.Label(itemRect, info.ColonistsDownCount.ToString());

            itemRect = new Rect(statsX + pBlockW * 10.5f, curHeight + statsRowH * 2, pBlockW * 5.5f, statsRowH);
            if (Mouse.IsOver(itemRect)) Widgets.DrawHighlight(itemRect);
            TooltipHandler.TipRegion(itemRect, "OC_PlayerClient_TotalTrainedAnimals".Translate());
            GUI.DrawTexture(new Rect(itemRect.x, itemRect.y, 32f, 32f), GeneralTexture.PawnsAnimal);
            itemRect.xMin += 34f;
            Widgets.Label(itemRect, info.AnimalObedienceCount.ToString());

            if (info.ExistsEnemyPawns)
            {
                itemRect = new Rect(statsX, curHeight + statsRowH * 3, statsW, statsRowH);
                GUI.DrawTexture(new Rect(itemRect.x, itemRect.y, 32f, 32f), GeneralTexture.AttackSettlement);
                itemRect.xMin += 34f;
                Widgets.Label(itemRect, "OC_PlayerClient_EnemieOnMap".Translate());
            }

            curHeight += iconHeight + iconBorder * 2f + 10f;
            DrawSeparator(totalWidth, ref curHeight);
        }

        private void DrawTeamSkillsSection(float totalWidth, ref float curHeight)
        {
            if (info.MaxSkills == null || info.MaxSkills.Count != 12) return;

            const float iconHeight = 120f;
            const float iconBorder = 3f;

            var iconRect = new Rect(0, curHeight, 128f + iconBorder * 2f, iconHeight + iconBorder * 2f);
            var anchor = Text.Anchor;
            Text.Anchor = TextAnchor.MiddleCenter;
            Text.Font = GameFont.Medium;
            Widgets.Label(iconRect, "TeamSkills".Translate());
            Text.Font = GameFont.Small;
            Text.Anchor = anchor;

            float col0 = (totalWidth - 128f - 8f) * 0.45f;
            float col1 = (totalWidth - 128f - 8f) - col0;
            const int rowCount = 6;
            float rowHeight = iconHeight / rowCount;
            float startX = 128f + iconBorder * 2f + 16f;

            // 1-ша колонка навичок (0..5)
            for (int i = 0; i < 6; i++)
            {
                var r = new Rect(startX, curHeight + rowHeight * i + i + 1, col0, rowHeight);
                DrawSkill(r, SkillNames[i], info.MaxSkills[i]);
            }

            // 2-га колонка навичок (6..11)
            float startX1 = startX + col0 + 16f;
            float col1W = col1 - 16f;
            for (int i = 6; i < 12; i++)
            {
                int rowIdx = i - 6;
                var r = new Rect(startX1, curHeight + rowHeight * rowIdx + rowIdx + 1, col1W, rowHeight);
                DrawSkill(r, SkillNames[i], info.MaxSkills[i]);
            }

            curHeight += iconHeight + iconBorder * 2f + 10f;
            DrawSeparator(totalWidth, ref curHeight);
        }

        private void DrawWorldObjectSection(CaravanOnline wo, float totalWidth, ref float curHeight)
        {
            const float iconHeight = 128f;
            const float iconBorder = 3f;

            var prect = new Rect(0, curHeight, 128f + iconBorder * 2f, iconHeight + iconBorder * 2f);
            var icon = ContentFinder<Texture2D>.Get(wo.ExpandingIconName, false);
            if (icon != null)
            {
                GUI.DrawTexture(prect, Command.BGTexShrunk);
                GUI.DrawTexture(prect.ContractedBy(iconBorder), icon);
            }

            float col0 = (totalWidth - 128f - 8f) * 0.45f;
            float col1 = (totalWidth - 128f - 8f) - col0;
            const int rowCount = 4;
            float rowHeight = iconHeight / rowCount;
            float startX = 128f + iconBorder * 2f + 16f;

            // Рядок 0: Назва об'єкта та кнопка камери
            var titleRect = new Rect(startX, curHeight + 1, totalWidth - startX, rowHeight);
            Text.Font = GameFont.Medium;
            Widgets.Label(titleRect, wo.LabelCap);
            Text.Font = GameFont.Small;

            var rectBut = new Rect(titleRect.x + titleRect.width - titleRect.height, titleRect.y, titleRect.height, titleRect.height);
            GUI.DrawTexture(rectBut, Command.BGTexShrunk);
            GUI.DrawTexture(rectBut.ContractedBy(2), ContentFinder<Texture2D>.Get("Waypoint", false));
            if (Mouse.IsOver(rectBut)) Widgets.DrawHighlight(rectBut);
            if (Widgets.ButtonInvisible(rectBut))
            {
                GameUtils.CameraJump(wo);
            }

            if (wo is BaseOnline wobase && wo.OnlineWObject.LoginOwner != SessionClientController.My.Login)
            {
                foreach (var giz in wobase.GetGizmos())
                {
                    if (giz is Command_Action cmd && cmd.icon != GeneralTexture.OCInfo)
                    {
                        rectBut.x -= rectBut.width + 4f;
                        GUI.DrawTexture(rectBut, Command.BGTexShrunk);
                        GUI.DrawTexture(rectBut.ContractedBy(2), cmd.icon);
                        if (Mouse.IsOver(rectBut)) Widgets.DrawHighlight(rectBut);
                        if (Widgets.ButtonInvisible(rectBut))
                        {
                            cmd.action();
                        }
                        TooltipHandler.TipRegion(rectBut, cmd.defaultDesc);
                    }
                }
            }

            // Рядок 1: Координати
            var r = new Rect(startX, curHeight + rowHeight + 2, col0, rowHeight);
            Vector2 vector = Find.WorldGrid.LongLatOf(wo.Tile);
            string coordText = "OCity_Coordinates".Translate() + " " + vector.y.ToStringLatitude() + " " + vector.x.ToStringLongitude();
            Widgets.Label(r, coordText);
            r.xMin += Text.CalcSize(coordText).x + 4f;
            var prevColor = GUI.color;
            GUI.color = Color.gray;
            Widgets.Label(r, $" sId: {wo.OnlineWObject.PlaceServerId}");
            GUI.color = prevColor;

            // Рядок 2: Вага або біом
            r = new Rect(startX, curHeight + rowHeight * 2 + 3, col0, rowHeight);
            if (!(wo is BaseOnline))
            {
                GUI.DrawTexture(new Rect(r.x, r.y, 32f, 32f), GeneralTexture.OCE_To);
                r.xMin += 34f;
                Widgets.Label(r, "OCity_Caravan_FreeWeight".Translate().ToString() + wo.OnlineWObject.FreeWeight.ToStringMass());
            }
            else
            {
                Widgets.Label(r, Find.WorldGrid[wo.Tile].biome.LabelCap);
            }

            // Рядок 3: Загальна вартість об'єкта
            r = new Rect(startX, curHeight + rowHeight * 3 + 4, col0, rowHeight);
            GUI.DrawTexture(new Rect(r.x, r.y, 32f, 32f), GeneralTexture.ItemStash);
            r.xMin += 34f;
            Widgets.Label(r, "OC_PlayerClient_TotalCost".Translate() + ": " + wo.OnlineWObject.MarketValueTotal.ToStringMoney());

            // Права колонка деталізації вартості
            float startX1 = startX + col0;

            // Рядок 5: Вартість речей
            r = new Rect(startX1, curHeight + rowHeight + 2, col1, rowHeight);
            GUI.DrawTexture(new Rect(r.x, r.y, 32f, 32f), GeneralTexture.ItemStash);
            r.xMin += 34f;
            Widgets.Label(r, CachedMarketValuePrefix + wo.OnlineWObject.MarketValue.ToStringMoney());

            // Рядок 6: Вартість пішаків
            r = new Rect(startX1, curHeight + rowHeight * 2 + 3, col1, rowHeight);
            GUI.DrawTexture(new Rect(r.x, r.y, 32f, 32f), GeneralTexture.ItemStash);
            r.xMin += 34f;
            Widgets.Label(r, CachedMarketValuePawnPrefix + wo.OnlineWObject.MarketValuePawn.ToStringMoney());

            // Рядок 7: Баланс
            r = new Rect(startX1, curHeight + rowHeight * 3 + 4, col1, rowHeight);
            GUI.DrawTexture(new Rect(r.x, r.y, 32f, 32f), GeneralTexture.OpenBox);
            r.xMin += 34f;
            Widgets.Label(r, CachedMarketValueTradingPrefix + (wo.OnlineWObject.MarketValueBalance + wo.OnlineWObject.MarketValueStorage).ToStringMoney());

            curHeight += iconHeight + iconBorder * 2f + 10f;
            DrawSeparator(totalWidth, ref curHeight);
        }

        private static void DrawSeparator(float totalWidth, ref float curHeight)
        {
            var lineRect = new Rect(0, curHeight, totalWidth, 2f);
            GUI.color = WindowBGBorderColor;
            Widgets.DrawBox(lineRect);
            GUI.color = Color.white;
            curHeight += 12f;
        }

        private void DrawSkill(Rect rect, string caption, int skill)
        {
            var anchor = Text.Anchor;
            Text.Anchor = TextAnchor.MiddleLeft;
            Widgets.Label(rect, caption);
            rect.xMin += rect.width - 50f;
            float fillPercent = Mathf.Max(0.01f, (float)skill / 20f);
            Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.FillableBar(rect.ContractedBy(2f), fillPercent, SkillBarFillTex, null, doBorder: false);
            Widgets.Label(rect, skill.ToString());
            Text.Anchor = anchor;
        }
    }
}