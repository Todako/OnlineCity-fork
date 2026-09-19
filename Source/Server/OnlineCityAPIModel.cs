using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ServerOnlineCity
{
    public class APIRequest
    {
        /// <summary>
        /// Тип запиту (регістр не має значення):
        /// "s" результат APIResponseStatus - загальна інформація щодо онлайну
        /// "p" результат APIResponsePlayers - інформація щодо гравця з ніком у Login
        /// "a" результат APIResponsePlayers - інформація про всіх зареєстрованих гравців
        /// "PlayerStat" результат APIResponseRawData, заповнити Key для доступу - звіт про всіх гравців, який можна отримати, натиснувши в консолі сервера S
        /// "BlockKey" результат APIResponseRawData, заповнити Key для доступу - передає вміст blockkey.txt із сервера
        /// "cc" заповнити N та Key для доступу - виконує однолітерну команду в консолі, наче її було натиснуто на сервері
        /// "cp" заповнити N та Key для доступу - пише N у загальний чат гри, як виконання command.txt
        /// "pa" результат APIResponseStatus, заповнити Key для доступу - передати підтвердження реєстрації користувача (у Login списком через *) та відмову в реєстрації (ніки гравців на видалення в N через *)
        /// "li" результат APIResponseRawData, заповнити N та Key для доступу - отримати зображення
        /// "si" заповнити N, Data та Key для доступу - зберегти зображення
        /// </summary>
        public string Q { get; set; }
        public int T { get; set; }
        public string Login { get; set; }
        public string N { get; set; }
        public string Key { get; set; }
        public byte[] Data { get; set; }
    }

    public abstract class APIResponse
    {
    }

    public class APIResponseRawData : APIResponse
    {
        public byte[] Data { get; set; }
    }

    public class APIResponseError : APIResponse
    {
        public string Error { get; set; }
    }

    public class APIResponseStatus : APIResponse
    {
        /// <summary>
        /// Кількість гравців онлайн
        /// </summary>
        public int OnlineCount { get; set; }
        /// <summary>
        /// Кількість зареєстрованих гравців
        /// </summary>
        public int PlayerCount { get; set; }
        /// <summary>
        /// Список ніків гравців онлайн
        /// </summary>
        public List<string> Onlines { get; set; }
        /// <summary>
        /// Гравці, які потребують підтвердження після реєстрації. Тут логін*discordІм'я
        /// </summary>
        public List<string> NeedApprove { get; set; }
    }

    public class APIResponsePlayers : APIResponse
    {
        public List<APIPlayer> Players { get; set; }
    }

    public class APIPlayer
    {
        public string Login { get; set; }

        public string DiscordUserName { get; set; }

        /// <summary>
        /// Час останньої активності в грі за Гринвічем
        /// </summary>
        public DateTime LastOnlineTime { get; set; }

        /// <summary>
        /// Кількість ігрових днів. Days / 60 = років
        /// </summary>
        public long Days { get; set; }

        /// <summary>
        /// Кількість поселень гравця
        /// </summary>
        public int BaseCount { get; set; }

        /// <summary>
        /// Кількість караванів гравця
        /// </summary>
        public int CaravanCount { get; set; }

        /// <summary>
        /// Загальна ігрова вартість
        /// </summary>
        public float MarketValueTotal { get; set; }

        /// <summary>
        /// Id поселень гравця, розділені комою
        /// </summary>
        public string BaseServerIds { get; set; }
    }
}
