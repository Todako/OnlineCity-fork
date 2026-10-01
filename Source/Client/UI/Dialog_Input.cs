using System;
using UnityEngine;
using Verse;

namespace RimWorldOnlineCity
{
    public class Dialog_Input : Window
    {
        private bool ModeOkCancel = false;
        private bool ModeOkOnly = false;
        private string TitleText;
        private string PrintText;
        private bool NeedFockus = true;
        private bool isClosed = false;

        public string InputText = "";
        public bool ResultOK = false;
        public Action PostCloseAction;

        public override Vector2 InitialSize => new Vector2(400f, 300f);

        public Dialog_Input()
        {
            closeOnCancel = false;
            closeOnAccept = false;
            doCloseButton = false;
            doCloseX = true;
            forcePause = true;
            absorbInputAroundWindow = true;
        }

        public Dialog_Input(string title, string printText, string inputTextStart)
            : this()
        {
            TitleText = title;
            PrintText = printText;
            InputText = inputTextStart ?? "";
        }

        public Dialog_Input(string title, string printText, bool modeOkOnly = false)
            : this()
        {
            TitleText = title;
            PrintText = printText;
            if (modeOkOnly) ModeOkOnly = true;
            else ModeOkCancel = true;
        }

        public override void PreOpen()
        {
            base.PreOpen();
            isClosed = false;
            ResultOK = false;
        }

        public override void PostClose()
        {
            base.PostClose();
            isClosed = true;
            if (!ResultOK) InputText = null;
            if (PostCloseAction != null) PostCloseAction();
        }

        public override void DoWindowContents(Rect inRect)
        {
            // ЗАХИСТ: якщо вікно закрито в цьому ж кадрі, не малюємо вміст
            if (isClosed) return;

            const float mainListingSpacing = 6f;

            var btnSize = new Vector2(140f, 40f);
            var buttonYStart = inRect.height - btnSize.y;

            var ev = Event.current;
            if (Widgets.ButtonText(new Rect(0, buttonYStart, btnSize.x, btnSize.y), "OCity_DialogInput_Ok".Translate())
                || (ev.isKey && ev.type == EventType.KeyDown && ev.keyCode == KeyCode.Return))
            {
                ResultOK = true;
                Close();
                return;
            }

            if (!ModeOkOnly && Widgets.ButtonText(new Rect(inRect.width - btnSize.x, buttonYStart, btnSize.x, btnSize.y), "OCity_DialogInput_Cancele".Translate()))
            {
                ResultOK = false;
                Close();
                return;
            }

            var mainListing = new Listing_Standard();
            mainListing.verticalSpacing = mainListingSpacing;
            mainListing.Begin(inRect);
            Text.Font = GameFont.Medium;
            mainListing.Label(TitleText);

            Text.Font = GameFont.Small;
            mainListing.GapLine();
            mainListing.Gap();

            Widgets.Label(new Rect(0, 70f, inRect.width, 120f), PrintText);

            if (!ModeOkCancel && !ModeOkOnly)
            {
                GUI.SetNextControlName("StartTextField");
                // ЗАХИСТ: InputText ?? "" гарантує, що Unity не викине NullReferenceException
                InputText = GUI.TextField(new Rect(0, 70f + 90f, 300f, 25f), InputText ?? "", 1000);
            }

            if (NeedFockus)
            {
                NeedFockus = false;
                GUI.FocusControl("StartTextField");
            }

            mainListing.End();
        }
    }
}