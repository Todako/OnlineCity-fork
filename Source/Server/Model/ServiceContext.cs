using OCUnion;
using System;
using System.Collections.Generic;

namespace ServerOnlineCity.Model
{
    public class ServiceContext
    {
        public PlayerServer Player;

        public Action<Action<SessionServer>> AllSessionAction;

        public string AddrIP;

        /// <summary>
        /// Якщо під час автентифікації не було надано ключа.
        /// З цим статусом можна оновлюватися, але при спробі завантажити світ для гри відбудеться відключення.
        /// </summary>
        public bool PossiblyIntruder;

        /// <summary>
        /// Тимчасове поле для зберігання ключів перевірки Intruder.
        /// </summary>
        public string IntruderKeys;

        /// <summary>
        /// Оновлення ключів перевірки без алокацій LINQ (.Where, .Union, .Aggregate).
        /// Знижує складність з O(N*M) до лінійної O(N+M).
        /// </summary>
        public void Logined()
        {
            if (IntruderKeys == null || Player == null) return;

            if (Player.IntruderKeys == null)
            {
                Player.IntruderKeys = "";
            }

            // Збираємо наявні ключі гравця
            var existingKeys = Player.IntruderKeys.Split(new[] { "@@@" }, StringSplitOptions.RemoveEmptyEntries);
            var keySet = new HashSet<string>(StringComparer.Ordinal);
            var keyList = new List<string>(existingKeys.Length + 8);

            for (int i = 0; i < existingKeys.Length; i++)
            {
                var k = existingKeys[i];
                if (k.Length > 3 && keySet.Add(k))
                {
                    keyList.Add(k);
                }
            }

            // Додаємо нові валідні ключі
            var newKeys = IntruderKeys.Split(new[] { "@@@" }, StringSplitOptions.RemoveEmptyEntries);
            bool addedAny = false;

            for (int i = 0; i < newKeys.Length; i++)
            {
                var k = newKeys[i];
                if (k.Length > 3 && keySet.Add(k))
                {
                    keyList.Add(k);
                    addedAny = true;
                }
            }

            if (addedAny && keyList.Count > 0)
            {
                Player.IntruderKeys = string.Join("@@@", keyList);
            }
        }

        public void Disconnect(string logMsg)
        {
            if (AllSessionAction == null) return;

            AllSessionAction(session =>
            {
                var sc = session.GetContext();
                if (sc != this) return;

                Loger.Log("Disconnect " + logMsg + " " + Player?.Public?.Login, Loger.LogLevel.LOGIN);
                session.Dispose();
            });
        }

        public void DisconnectLogin(string login, string logMsg)
        {
            if (AllSessionAction == null) return;

            AllSessionAction(session =>
            {
                var sc = session.GetContext();
                if (sc?.Player?.Public?.Login == null
                    || sc.Player.Public.Login != login) return;

                Loger.Log("DisconnectLogin " + logMsg + " " + login + " by " + Player?.Public?.Login, Loger.LogLevel.LOGIN);
                session.Dispose();
            });
        }
    }
}