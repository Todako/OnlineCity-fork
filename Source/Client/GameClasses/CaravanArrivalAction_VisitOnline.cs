using Model;
using OCUnion;
using RimWorld;
using RimWorld.Planet;
using System;
using Verse;

namespace RimWorldOnlineCity
{
    public class CaravanArrivalAction_VisitOnline : CaravanArrivalAction
    {
        private CaravanOnline сaravanOnline;
        private string mode;

        public CaravanArrivalAction_VisitOnline()
        {
        }

        public CaravanArrivalAction_VisitOnline(CaravanOnline сaravanOnline, string mode)
        {
            this.сaravanOnline = сaravanOnline;
            this.mode = mode;
        }

        public override string Label
        {
            get
            {
                if (сaravanOnline == null) return "";
                return string.Format(mode == "exchangeOfGoods" ? "OCity_Caravan_GoTrade".Translate()
                        : mode == "attack" ? "OCity_Caravan_Go_Attack_Target".Translate()
                        : "OCity_Caravan_GoTrade2".Translate()
                    , сaravanOnline.Label);
            }
        }

        public override string ReportString => Label;

        public override void Arrived(Caravan caravan)
        {
            if (mode == "exchangeOfGoods")
            {
                ExchengeUtils.ExchangeOfGoods_DoAction(сaravanOnline, caravan);
            }
            else if (mode == "attack")
            {
                attack(caravan);
            }
        }

        private void attack(Caravan caravan)
        {
            if (сaravanOnline == null || caravan == null) return;

            Find.TickManager.Pause();

            // Залишаємо лише перше речення з питанням підтвердження атаки (без згадки про тренувальний бій)
            string rawConfirmText = "OCity_Caravan_Confirm_Attack_TestBattle_Possible".Translate().ToString();
            int qIdx = rawConfirmText.IndexOf('?');
            string confirmText = qIdx >= 0
                ? rawConfirmText.Substring(0, qIdx + 1)
                : ("OCity_Caravan_Go_Attack_Target".Translate(сaravanOnline.Label).ToString() + "?");

            GameUtils.ShowDialodOKCancel(
                "OCity_Caravan_Go_Attack_Target".Translate(сaravanOnline.Label),
                confirmText,
                () =>
                {
                    if (GameAttacker.Create())
                    {
                        GameAttacker.Get.Start(caravan, сaravanOnline, false);
                    }
                },
                () => { }
            );
        }
    }
}