using OCUnion;
using ServerOnlineCity.Model;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Transfer.ModelMails;

namespace ServerOnlineCity.Mechanics
{
    /// <summary>
    /// Інструкція з додавання інцидентів:
    /// Додати новий тип у CallIncident (ParseIncidentTypes і обробку коду, якщо є новий параметр).
    /// Якщо додається новий параметр командного рядка, додати його в CallIncidentCmd, ModelMailStartIncident, функцію MailProcessStartIncident() та поле в OCIncident.
    /// Додати тип у FMailIncident. Він задається в конструкторі (черга) та у функціях наприкінці класу (тексти, затримка до початку інциденту й затримка черги після нього).
    /// Додати його в інтерфейс Dialog_BaseOnlineButton.
    /// Додати реалізацію на клієнті в новий клас Incident*.
    /// Додати виклик класу в Incidents.
    /// Додати в OCIncident розрахунок вартості CalculateRaidCost() і розрахунок інтенсивності на основі вартості поселення (якщо використовується) CalculatePoints().
    /// </summary>
    public static class CallIncident
    {
        public static string CreateIncident(PlayerServer player
            , PlayerServer targetPlayer
            , long serverId
            , IncidentTypes type
            , int mult
            , List<string> parameters
            //, string arrivalMode
            //, string faction
            //, string param
            , bool checkMode)
        {
            Loger.Log("IncidentLod CallIncident.CreateIncident 1");

            if (!ServerManager.ServerSettings.GeneralSettings.IncidentEnable) return "OC_incidents_IncidentsTurnedOFF";

            //if (type == null) return "OC_Incidents_CallIncidents_TypeErr";

            if (targetPlayer == player) return "OC_Incidents_CallIncidebts_selfErr";

            if (player.Public.LastTick / 3600000 < 2) return "OC_Incidents_CallIncidebts_YearErr1";

            if (targetPlayer.Public.LastTick / 3600000 < 2) return "OC_Incidents_CallIncidebts_YearErr2";


            var costAllPlayer = player.AllCostWorldObjects();
            if (costAllPlayer < 100000f) return "OC_Incidents_CallIncidebts_CostErr1";

            var costAllTargetPlayer = targetPlayer.AllCostWorldObjects();
            if (costAllTargetPlayer < 100000f) return "OC_Incidents_CallIncidebts_CostErr2";

            Loger.Log("IncidentLod CallIncident.CreateIncident 2");

            mult = mult > ServerManager.ServerSettings.GeneralSettings.IncidentMaxMult ? ServerManager.ServerSettings.GeneralSettings.IncidentMaxMult : mult;

            // Формуємо пакет.
            var packet = new ModelMailStartIncident()
            {
                From = player.Public,
                To = targetPlayer.Public,
                PlaceServerId = serverId,
                NeedSaveGame = true,
                IncidentType = type,
                IncidentMult = mult,
                //IncidentArrivalMode = arrivalMode,
                //IncidentFaction = faction,
                //IncidentParam = param,
                IncidentParams = parameters,
            };
            var fPacket = new FMailIncident(packet);

            Loger.Log("Server test call " + type + " " + targetPlayer.Public.Login);

            // Перевіряємо допустимість і додаємо інцидент.
            var ownLogin = player.Public.Login;
            lock (targetPlayer)
            {
                var list = targetPlayer.FunctionMails
                    .Where(m => m is FMailIncident)
                    .Cast<FMailIncident>()
                    .Where(m => m.NumberOrder == fPacket.NumberOrder);

                if (list.Count() > ServerManager.ServerSettings.GeneralSettings.IncidentCountInOffline)
                    return "OC_Incidents_CallIncidents_MaxIncidentsCnt";

                //if (list.Count(m => m.Mail.From.Login == ownLogin) > 1)
                //    return "OC_Incidents_CallIncidents_NotShooted";

                // Перевірку завершено.
                // Якщо це тестовий запуск, виходимо.
                if (checkMode)
                {
                    Loger.Log("IncidentLod CallIncident.CreateIncident 3 checkMode OK");
                    return null;
                }

                //targetPlayer.Mails.Add(packet);
                // Замість негайного надсилання використовуємо обробник відкладеного надсилання, щоб витримати паузу між рейдами.
                targetPlayer.FunctionMails.Add(fPacket);
            }

            player.AttacksInitiatorCount++;   // Не збільшувати для позитивних інцидентів!

            // Додаємо до спеціального журналу.
            IncidentLogAppend("NewIncident", packet, "", (int)costAllPlayer, (int)costAllTargetPlayer);

            Loger.Log("IncidentLod CallIncident.CreateIncident 3");

            return null;
        }

        public static void IncidentLogAppend(string record, ModelMailStartIncident mail, string data, int fromWorth = 0, int toWorth = 0)
        {
            Func<DateTime, string> dateTimeToStr = dt => dt == DateTime.MinValue ? "" : dt.ToString("yyyy-MM-dd hh:mm:ss", CultureInfo.InvariantCulture);

            var fileName = Path.Combine(Path.GetDirectoryName(Repository.Get.SaveFileName)
                , $"Incidents_{DateTime.Now.ToString("yyyy-MM")}.csv");
            if (!File.Exists(fileName))
            {
                File.WriteAllText(fileName, $"time;record" +
                    $";fromLogin;toLogin;fromDay;toDay;fromWorth;toWorth;paramIncident;serverId" +
                    // Структура data:
                    $";worthTarget;delayAfterMail;numberOrder;countInOrder" +
                    Environment.NewLine, Encoding.UTF8);
            }

            if (fromWorth == 0) fromWorth = (int)Repository.GetPlayerByLogin(mail.From.Login).AllCostWorldObjects();
            if (toWorth == 0) toWorth = (int)Repository.GetPlayerByLogin(mail.To.Login).AllCostWorldObjects();

            var param = $"{mail.IncidentType} lvl:{mail.IncidentMult}"
                + $" mode:" + (mail.IncidentParams != null && mail.IncidentParams.Count > 0 ? mail.IncidentParams[0] : null)  //arrivalMode / anyParam
                + $" who:" + (mail.IncidentParams != null && mail.IncidentParams.Count > 1 ? mail.IncidentParams[1] : null) //faction
                + (mail.IncidentParams != null && mail.IncidentParams.Count > 2 ? $" alt:" + mail.IncidentParams[2] : null); //not use

            var contentLog = dateTimeToStr(DateTime.Now) + ";" + record
                + $";{mail.From.Login};{mail.To.Login};{mail.From.LastTick / 60000};{mail.To.LastTick / 60000};{fromWorth};{toWorth};{param};{mail.PlaceServerId}"
                + ";" + data + Environment.NewLine;

            Loger.Log("IncidentLogAppend. " + contentLog);

            try
            {
                File.AppendAllText(fileName, contentLog, Encoding.UTF8);
            }
            catch
            {
                try
                {
                    File.AppendAllText(fileName + "add", contentLog, Encoding.UTF8);
                }
                catch
                {
                    Loger.Log("IncidentLogAppend. Error write file " + fileName);
                }
            }
        }

    }
}
