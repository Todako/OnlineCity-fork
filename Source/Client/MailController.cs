using Model;
using OCUnion;
using RimWorld;
using RimWorld.Planet;
using RimWorldOnlineCity.GameClasses;
using System;
using System.Collections.Generic;
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
        /// <summary>
        /// Точка входу при отриманні нового листа від сервера.
        /// </summary>
        public static void MailArrived(ModelMail mail)
        {
            try
            {
                if (mail?.To == null) return;
                var myLogin = SessionClientController.My?.Login;
                if (myLogin == null || !string.Equals(mail.To.Login, myLogin, StringComparison.OrdinalIgnoreCase)) return;

                if (Loger.Enable && !MainHelper.OffAllLog)
                {
                    string hash = null;
                    try { hash = mail.GetHash(); } catch { }
                    Loger.Log($"Mail {mail.GetType().Name} {(mail.From?.Login ?? "-")}->{(mail.To?.Login ?? "-")}: hash={hash ?? "-"}");
                }

                // ОПТИМІЗАЦІЯ: швидкий pattern matching замість словника делегатів Dictionary<Type, Action<ModelMail>>
                switch (mail)
                {
                    case ModelMailTrade trade:
                        MailProcessCreateThings(trade);
                        break;
                    case ModelMailDeleteWO deleteWO:
                        MailProcessDeleteByServerId(deleteWO);
                        break;
                    case ModelMailAttackCancel attackCancel:
                        MailProcessAttackCancel(attackCancel);
                        break;
                    case ModelMailAttackTechnicalVictory techVictory:
                        MailProcessAttackTechnicalVictory(techVictory);
                        break;
                    case ModelMailStartIncident incident:
                        MailProcessStartIncident(incident);
                        break;
                    case ModelMailMessadge message:
                        MailProcessMessadge(message);
                        break;
                    default:
                        Loger.Log("Mail fail: unknown type " + mail.GetType().Name, Loger.LogLevel.ERROR);
                        break;
                }
            }
            catch (Exception e)
            {
                Loger.Log("Mail Exception: " + e, Loger.LogLevel.ERROR);
            }
        }

        #region MailProcessMessadge
        public static void MailProcessMessadge(ModelMailMessadge msg)
        {
            LetterDef def = GetLetterDef(msg.type);

            ModBaseData.RunMainThread(() =>
            {
                if (Find.LetterStack == null) return;

                var place = ExchengeUtils.GetPlace(msg);
                GlobalTargetInfo? targetInfo = null;
                if (place != null)
                    targetInfo = new GlobalTargetInfo(place);
                else if (msg.Tile != 0)
                    targetInfo = new GlobalTargetInfo(msg.Tile);

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

        private static LetterDef GetLetterDef(ModelMailMessadge.MessadgeTypes type)
        {
            switch (type)
            {
                case ModelMailMessadge.MessadgeTypes.ThreatBig:
                    return LetterDefOf.ThreatBig;
                case ModelMailMessadge.MessadgeTypes.ThreatSmall:
                    return LetterDefOf.ThreatSmall;
                case ModelMailMessadge.MessadgeTypes.Death:
                    return LetterDefOf.Death;
                case ModelMailMessadge.MessadgeTypes.Negative:
                    return LetterDefOf.NegativeEvent;
                case ModelMailMessadge.MessadgeTypes.Neutral:
                    return LetterDefOf.NeutralEvent;
                case ModelMailMessadge.MessadgeTypes.Positive:
                    return LetterDefOf.PositiveEvent;
                case ModelMailMessadge.MessadgeTypes.Visitor:
                    return LetterDefOf.AcceptVisitors;
                case ModelMailMessadge.MessadgeTypes.GoldenLetter:
                    return OC_LetterDefOf.GoldenLetter ?? LetterDefOf.PositiveEvent;
                case ModelMailMessadge.MessadgeTypes.GreyGoldenLetter:
                    return OC_LetterDefOf.GreyGoldenLetter ?? LetterDefOf.NeutralEvent;
                default:
                    return LetterDefOf.NeutralEvent;
            }
        }
        #endregion

        #region MailProcessStartIncident
        public static void MailProcessStartIncident(ModelMailStartIncident mail)
        {
            if (Loger.Enable && !MainHelper.OffAllLog)
            {
                Loger.Log("IncidentLog MailController.MailProcessStartIncident 1");
            }

            ModBaseData.RunMainThread(() =>
            {
                if (Find.TickManager != null)
                {
                    Find.TickManager.Pause();
                }

                var incident = new OCIncidentFactory().GetIncident(mail.IncidentType);
                incident.mult = mail.IncidentMult;
                incident.incidentParams = mail.IncidentParams;
                incident.attacker = mail.From?.Login;
                incident.place = ExchengeUtils.GetPlace(mail);
                incident.TryExecuteEvent();

                if (SessionClientController.Data != null && !SessionClientController.Data.BackgroundSaveGameOff)
                {
                    SessionClientController.SaveGameNow(true);
                }

                if (Loger.Enable && !MainHelper.OffAllLog)
                {
                    Loger.Log("IncidentLog MailController.MailProcessStartIncident 2");
                }
            });
        }
        #endregion

        #region CreateThings
        public static void MailProcessCreateThings(ModelMailTrade mail)
        {
            if (mail.Things == null || mail.Things.Count == 0 || mail.PlaceServerId <= 0)
            {
                Loger.Log("Mail fail: no data", Loger.LogLevel.WARNING);
                return;
            }

            string fromLogin = mail.From?.Login ?? "-";

            ModBaseData.RunMainThread(() =>
            {
                var place = ExchengeUtils.GetPlace(mail);
                if (place != null)
                {
                    DropToWorldObject(place, mail.Things, fromLogin);
                }
                else
                {
                    Loger.Log($"Mail trade fail: place not found for serverId={mail.PlaceServerId}", Loger.LogLevel.WARNING);
                }
            });
        }

        private static void DropToWorldObject(WorldObject place, List<ThingEntry> things, string from)
        {
            string text = "OCity_UpdateWorld_TradeDetails".Translate(from, place.LabelCap, things.ToStringLabel()).ToString();

            if (Find.TickManager != null)
            {
                Find.TickManager.Pause();
            }

            GameUtils.ShowDialodOKCancel(
                "OCity_UpdateWorld_Trade".Translate(),
                text,
                () =>
                {
                    if (place != null)
                    {
                        ExchengeUtils.SpawnToWorldObject(place, things, text);
                    }
                },
                () => Loger.Log("Drop Mail canceled from " + from + ": " + text)
            );
        }
        #endregion

        #region DeleteByServerId
        public static void MailProcessDeleteByServerId(ModelMailDeleteWO mail)
        {
            if (Loger.Enable && !MainHelper.OffAllLog)
            {
                Loger.Log("Client MailProcessDeleteByServerId " + mail.PlaceServerId);
            }

            if (mail.PlaceServerId <= 0)
            {
                Loger.Log("Mail fail: no data", Loger.LogLevel.WARNING);
                return;
            }

            ModBaseData.RunMainThread(() =>
            {
                var place = ExchengeUtils.GetPlace(mail, false, true);
                if (place != null && Find.WorldObjects != null)
                {
                    Find.WorldObjects.Remove(place);
                    SessionClientController.SaveGameNow(true);
                }
            });
        }
        #endregion

        #region AttackCancel
        public static void MailProcessAttackCancel(ModelMailAttackCancel mail)
        {
            if (Loger.Enable && !MainHelper.OffAllLog)
            {
                Loger.Log("Client MailProcessAttackCancel");
            }

            ModBaseData.RunMainThread(() =>
            {
                GameAttackTrigger_Patch.ForceSpeed = -1f;
                if (SessionClientController.Data != null)
                {
                    SessionClientController.Data.AttackModule?.Clear();
                    SessionClientController.Data.AttackUsModule?.Clear();
                }

                SessionClientController.Disconnected("OCity_GameAttacker_Dialog_ErrorMessage".Translate());
            });
        }
        #endregion

        #region TechnicalVictory
        public static void MailProcessAttackTechnicalVictory(ModelMailAttackTechnicalVictory mail)
        {
            if (Loger.Enable && !MainHelper.OffAllLog)
            {
                Loger.Log("Client MailProcessAttackTechnicalVictory");
            }

            ModBaseData.RunMainThread(() =>
            {
                if (SessionClientController.Data != null)
                {
                    SessionClientController.Data.AttackModule?.Finish(true);
                    SessionClientController.Data.AttackUsModule?.Finish(false);
                }
            });
        }
        #endregion
    }
}