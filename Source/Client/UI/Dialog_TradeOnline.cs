using OCUnion;
using RimWorld;
using RimWorld.Planet;
using System;
using System.Collections.Generic;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace RimWorldOnlineCity
{
    public class Dialog_TradeOnline : Window
    {
        public const float MassLabelYOffset = 32f;

        private readonly Action onClosed;
        private bool thisWindowInstanceEverOpened;

        private List<TransferableOneWay> transferables;
        private TransferableOneWayWidget itemsTransfer;

        private bool massUsageDirty = true;
        private float cachedMassUsage;

        private readonly Vector2 BottomButtonSize = new Vector2(160f, 40f);

        private readonly string WhoName;
        private readonly float FreeWeight;
        public IEnumerable<Thing> AllItem;
        public bool IsCancel = false;

        private readonly string _cachedTitle;

        private static string CachedAcceptButton;
        private static string CachedResetButton;
        private static string CachedCancelButton;

        public override Vector2 InitialSize => new Vector2(1024f, (float)Verse.UI.screenHeight);

        private float MassUsage
        {
            get
            {
                if (this.massUsageDirty)
                {
                    this.massUsageDirty = false;
                    this.cachedMassUsage = CollectionsMassCalculator.MassUsageTransferables(
                        this.transferables,
                        IgnorePawnsInventoryMode.IgnoreIfAssignedToUnload,
                        false,
                        true);
                }
                return this.cachedMassUsage;
            }
        }

        private float MassCapacity => FreeWeight > 0 ? FreeWeight : 9999999f;

        public Dictionary<Thing, int> GetSelect()
        {
            if (IsCancel || transferables == null) return new Dictionary<Thing, int>(0);
            return transferables.TransferableOneWaysToDictionary();
        }

        public Dialog_TradeOnline(IEnumerable<Thing> allItem, string who, float freeWeight, Action onClosed = null, bool showEstTimeToDestinationButton = true)
        {
            AllItem = allItem;
            WhoName = who;
            FreeWeight = freeWeight;
            closeOnCancel = false;
            closeOnAccept = false;
            this.onClosed = onClosed;
            this.forcePause = true;
            this.absorbInputAroundWindow = true;

            _cachedTitle = "OCity_Dialog_TradeOnline_Trade".Translate()
                + " " + WorldObjectDefOf.Caravan.LabelCap
                + " ⇨ " + (WhoName ?? string.Empty);

            if (CachedAcceptButton == null)
            {
                CachedAcceptButton = "AcceptButton".Translate().ToString();
                CachedResetButton = "ResetButton".Translate().ToString();
                CachedCancelButton = "CancelButton".Translate().ToString();
            }
        }

        public override void PostOpen()
        {
            base.PostOpen();
            _lastUsedMass = -99999f;
            _lastAvailableMass = -99999f;
            this.massUsageDirty = true;

            if (!this.thisWindowInstanceEverOpened)
            {
                this.thisWindowInstanceEverOpened = true;
                this.CalculateAndRecacheTransferables();
            }
        }

        public override void PostClose()
        {
            base.PostClose();
            this.onClosed?.Invoke();
        }

        public override void DoWindowContents(Rect inRect)
        {
            Rect rect = new Rect(0f, 0f, inRect.width, 40f);
            Text.Font = GameFont.Medium;
            Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.Label(rect, _cachedTitle);
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.UpperLeft;

            inRect.yMin += 72f;
            Widgets.DrawMenuSection(inRect);

            inRect = inRect.ContractedBy(17f);
            GUI.BeginGroup(inRect);
            Rect rect2 = inRect.AtZero();

            Rect rect3 = rect2;
            rect3.y += 32f;
            rect3.xMin += rect2.width - 515f;
            this.DrawMassAndFoodInfo(rect3);

            this.DoBottomButtons(rect2);
            Rect inRect2 = rect2;
            inRect2.yMax -= 59f;

            if (this.itemsTransfer != null)
            {
                this.itemsTransfer.OnGUI(inRect2, out bool flag);
                if (flag)
                {
                    this.CountToTransferChanged();
                }
            }

            GUI.EndGroup();
        }

        private void DrawMassAndFoodInfo(Rect rect)
        {
            DrawMassInfo(rect, this.MassUsage, MassCapacity, -9999f, true);
        }

        private static float _lastUsedMass = -99999f;
        private static float _lastAvailableMass = -99999f;
        private static string _cachedMassText;
        private static Vector2 _cachedMassVector;

        public static void DrawMassInfo(Rect rect, float usedMass, float availableMass, float lastMassFlashTime = -9999f, bool alignRight = false)
        {
            GUI.color = usedMass > availableMass ? Color.red : Color.gray;

            if (Math.Abs(usedMass - _lastUsedMass) > 0.01f || Math.Abs(availableMass - _lastAvailableMass) > 0.01f || _cachedMassText == null)
            {
                _lastUsedMass = usedMass;
                _lastAvailableMass = availableMass;
                _cachedMassText = $" {usedMass:0.##} / {availableMass:0.##} kg";
                _cachedMassVector = Text.CalcSize(_cachedMassText);
            }

            Rect rect2 = alignRight
                ? new Rect(rect.xMax - _cachedMassVector.x, rect.y, _cachedMassVector.x, _cachedMassVector.y)
                : new Rect(rect.x, rect.y, _cachedMassVector.x, _cachedMassVector.y);

            if (Time.time - lastMassFlashTime < 1f)
            {
                GUI.DrawTexture(rect2, TransferableUIUtility.FlashTex);
            }

            Widgets.Label(rect2, _cachedMassText);
            GUI.color = Color.white;
        }

        private void DoBottomButtons(Rect rect)
        {
            Rect rect2 = new Rect(rect.width / 2f - this.BottomButtonSize.x / 2f, rect.height - 55f, this.BottomButtonSize.x, this.BottomButtonSize.y);
            if (Widgets.ButtonText(rect2, CachedAcceptButton, true, false, true))
            {
                SoundDefOf.Tick_High.PlayOneShotOnCamera(null);
                IsCancel = false;
                this.Close(false);
            }
            Rect rect3 = new Rect(rect2.x - 10f - this.BottomButtonSize.x, rect2.y, this.BottomButtonSize.x, this.BottomButtonSize.y);
            if (Widgets.ButtonText(rect3, CachedResetButton, true, false, true))
            {
                SoundDefOf.Tick_Low.PlayOneShotOnCamera(null);
                this.CalculateAndRecacheTransferables();
            }
            Rect rect4 = new Rect(rect2.xMax + 10f, rect2.y, this.BottomButtonSize.x, this.BottomButtonSize.y);
            if (Widgets.ButtonText(rect4, CachedCancelButton, true, false, true))
            {
                IsCancel = true;
                this.Close(true);
            }
        }

        private void CalculateAndRecacheTransferables()
        {
            this.transferables = AllItem != null ? AllItem.DistinctToTransferableOneWays() : new List<TransferableOneWay>(0);
            CreateCaravanTransferableWidgets(
                this.transferables,
                out this.itemsTransfer,
                null,
                null,
                "FormCaravanColonyThingCountTip".Translate(),
                IgnorePawnsInventoryMode.IgnoreIfAssignedToUnload,
                () => this.MassCapacity - this.MassUsage,
                false);
            this.CountToTransferChanged();
        }

        public static void CreateCaravanTransferableWidgets(
            List<TransferableOneWay> transferables,
            out TransferableOneWayWidget itemsTransfer,
            string sourceLabel,
            string destLabel,
            string thingCountTip,
            IgnorePawnsInventoryMode ignorePawnInventoryMass,
            Func<float> availableMassGetter,
            bool ignoreCorpsesGearAndInventoryMass)
        {
            itemsTransfer = new TransferableOneWayWidget(
                transferables,
                sourceLabel,
                destLabel,
                thingCountTip,
                true,
                ignorePawnInventoryMass,
                true, // ВИПРАВЛЕННЯ: передаємо true замість false для includePawns!
                availableMassGetter,
                24f,
                ignoreCorpsesGearAndInventoryMass);
        }

        private void CountToTransferChanged()
        {
            this.massUsageDirty = true;
        }
    }
}