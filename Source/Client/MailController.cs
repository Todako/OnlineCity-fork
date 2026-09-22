using Model;
using OCUnion;
using RimWorld;
using RimWorld.Planet;
using RimWorldOnlineCity.GameClasses;
using System;
using System.Collections.Generic;
using Transfer;
using Transfer.ModelMails;
using Verse;

namespace RimWorldOnlineCity
{
    /// <summary>
    /// Контролер обробки вхідної ігрової пошти від сервера та інших гравців
    /// (посилки з речами, текстові листи, запуск інцидентів/рейдів, системні сповіщення).
    /// </summary>
    static class MailController
    {
        private static readonly Dictionary<Type, Action<ModelMail>> TypeMailProcessing = new Dictionary<Type, Action<ModelMail>>()
        {
            { typeof(ModelMailTrade), MailProcessCreateThings },
            { typeof(ModelMailDeleteWO), MailProcessDeleteByServerId },
            { typeof(ModelMailAttackCancel), MailProcessAttackCancel },
            { typeof(ModelMailAttackTechnicalVictory), MailProcessAttackTechnicalVictory },
            { typeof(ModelMailStartIncident), MailProcessStartIncident },
            { typeof(ModelMailMessadge), MailProcessMessadge }
        };

        /// <summary>
        /// Точка входу при отриманні нового листа від сервера.
        /// </summary>
        public static void MailArrived(ModelMail mail)
        {
            try
            {
                if (mail?.To == null || mail.To.Login != SessionClientController.My?.Login) return;

                if (TypeMailProcessing.TryGetValue(mail.GetType(), out var action))
                {
                    if (Loger.Enable && !MainHelper.OffAllLog)
                    {
                        Loger.Log($"Mail {mail.GetType().Name} {(mail.From?.Login ?? "-")}->{(mail.To?.Login ?? "-")}: hash={mail.GetHash()}");
                    }
                    action(mail);
                }
                else
                {
                    Loger.Log("Mail fail: unknown type " + mail.GetType().Name, Loger.LogLevel.ERROR);
                }
            }
            catch (Exception e)
            {
                Loger.Log("Mail Exception: " + e.ToString(), Loger.LogLevel.ERROR);
            }
        }

        #region MailProcessMessadge
        public static void MailProcessMessadge(ModelMail incoming)
        {
            var msg = (ModelMailMessadge)incoming;

            LetterDef def;
            switch (msg.type)
            {
                case ModelMailMessadge.MessadgeTypes.ThreatBig:
                    def = LetterDefOf.ThreatBig;
                    break;
                case ModelMailMessadge.MessadgeTypes.ThreatSmall:
                    def = LetterDefOf.ThreatSmall;
                    break;
                case ModelMailMessadge.MessadgeTypes.Death:
                    def = LetterDefOf.Death;
                    break;
                case ModelMailMessadge.MessadgeTypes.Negative:
                    def = LetterDefOf.NegativeEvent;
                    break;
                case ModelMailMessadge.MessadgeTypes.Neutral:
                    def = LetterDefOf.NeutralEvent;
                    break;
                case ModelMailMessadge.MessadgeTypes.Positive:
                    def = LetterDefOf.PositiveEvent;
                    break;
                case ModelMailMessadge.MessadgeTypes.Visitor:
                    def = LetterDefOf.AcceptVisitors;
                    break;
                case ModelMailMessadge.MessadgeTypes.GoldenLetter:
                    def = OC_LetterDefOf.GoldenLetter;
                    break;
                case ModelMailMessadge.MessadgeTypes.GreyGoldenLetter:
                    def = OC_LetterDefOf.GreyGoldenLetter;
                    break;
                default:
                    def = LetterDefOf.NeutralEvent;
                    break;
            }

            var place = ExchengeUtils.GetPlace(msg);
            GlobalTargetInfo? targetInfo = null;
            if (place != null)
                targetInfo = new GlobalTargetInfo(place);
            else if (msg.Tile != 0)
                targetInfo = new GlobalTargetInfo(msg.Tile);

            // ОПТИМІЗАЦІЯ: безпечне додавання листа до стеку в основному потоці Unity
            ModBaseData.RunMainThread(() =>
            {
                string label = ChatController.ServerCharTranslate(msg.label);
                string text = ChatController.ServerCharTranslate(msg.text);

                if (targetInfo == null)
                {
                    Find.LetterStack.ReceiveLetter(label, text, def);
                }
                else
                {
                    Find.LetterStack.ReceiveLetter(label, text, def, targetInfo.Value);
                }
            });
        }
        #endregion

        #region MailProcessStartIncident
        public static void MailProcessStartIncident(ModelMail incoming)
        {
            Loger.Log("IncidentLog MailController.MailProcessStartIncident 1");
            var mail = (ModelMailStartIncident)incoming;

            // ОПТИМІЗАЦІЯ: генерація та запуск події виконуються виключно в основному потоці
            ModBaseData.RunMainThread(() =>
            {
                Find.TickManager.Pause();

                var incident = new OCIncidentFactory().GetIncident(mail.IncidentType);
                incident.mult = mail.IncidentMult;
                incident.incidentParams = mail.IncidentParams;
                incident.attacker = mail.From?.Login;
                incident.place = ExchengeUtils.GetPlace(mail);
                incident.TryExecuteEvent();

                if (!SessionClientController.Data.BackgroundSaveGameOff)
                {
                    SessionClientController.SaveGameNow(true);
                }
                Loger.Log("IncidentLog MailController.MailProcessStartIncident 2");
            });
        }
        #endregion

        #region CreateThings
        public static void MailProcessCreateThings(ModelMail incoming)
        {
            var mail = (ModelMailTrade)incoming;

            if (mail.Things == null || mail.Things.Count == 0 || mail.PlaceServerId <= 0)
            {
                Loger.Log("Mail fail: no data");
                return;
            }

            var place = ExchengeUtils.GetPlace(mail);
            if (place != null)
            {
                DropToWorldObject(place, mail.Things, mail.From?.Login ?? "-");
            }
        }

        private static void DropToWorldObject(WorldObject place, List<ThingEntry> things, string from)
        {
            // ОПТИМІЗАЦІЯ: відкриття діалогового вікна підтвердження в основному потоці гри
            ModBaseData.RunMainThread(() =>
            {
                string text = "OCity_UpdateWorld_TradeDetails".Translate(from, place.LabelCap, things.ToStringLabel()).ToString();

                Find.TickManager.Pause();
                GameUtils.ShowDialodOKCancel(
                    "OCity_UpdateWorld_Trade".Translate(),
                    text,
                    () => ExchengeUtils.SpawnToWorldObject(place, things, text),
                    () => Loger.Log("Drop Mail canceled from " + from + ": " + text)
                );
            });
        }
        #endregion

        #region DeleteByServerId
        public static void MailProcessDeleteByServerId(ModelMail incoming)
        {
            var mail = (ModelMailDeleteWO)incoming;
            Loger.Log("Client MailProcessDeleteByServerId " + mail.PlaceServerId);

            if (mail.PlaceServerId <= 0)
            {
                Loger.Log("Mail fail: no data");
                return;
            }

            ModBaseData.RunMainThread(() =>
            {
                var place = ExchengeUtils.GetPlace(mail, false, true);
                if (place != null)
                {
                    Find.WorldObjects.Remove(place);
                    SessionClientController.SaveGameNow(true);
                }
            });
        }
        #endregion

        #region AttackCancel
        public static void MailProcessAttackCancel(ModelMail incoming)
        {
            Loger.Log("Client MailProcessAttackCancel");

            ModBaseData.RunMainThread(() =>
            {
                GameAttackTrigger_Patch.ForceSpeed = -1f;
                SessionClientController.Data.AttackModule?.Clear();
                SessionClientController.Data.AttackUsModule?.Clear();

                SessionClientController.Disconnected("OCity_GameAttacker_Dialog_ErrorMessage".Translate());
            });
        }
        #endregion

        #region TechnicalVictory
        public static void MailProcessAttackTechnicalVictory(ModelMail incoming)
        {
            Loger.Log("Client MailProcessAttackTechnicalVictory");

            ModBaseData.RunMainThread(() =>
            {
                SessionClientController.Data.AttackModule?.Finish(true);
                SessionClientController.Data.AttackUsModule?.Finish(false);
            });
        }
        #endregion
    }
}