using Model;
using ServerOnlineCity.Model;
using ServerOnlineCity.Services;
using System;
using System.Collections.Generic;
using System.Linq;

namespace ServerOnlineCity.Common
{
    internal static class StaticHelper
    {
        private static HashSet<string> _cachedPublicChatPartyLogin;
        private static DateTime _lastPublicChatChanged = DateTime.MinValue;
        private static int _lastPartyLoginCount = -1;
        private static readonly object _syncLock = new object();
        private static readonly HashSet<string> EmptySet = new HashSet<string>();

        /// <summary>
        /// Повертає логіни гравців, яких бачить цей користувач.
        /// </summary>
        public static HashSet<string> PartyLoginSee(PlayerServer player)
        {
            if (player == null) return EmptySet;

            if (player.IsAdmin)
            {
                var allLogins = Repository.GetData?.GetPlayerLoginsAll;
                if (allLogins is HashSet<string> set)
                {
                    return set;
                }
                return allLogins != null ? allLogins.ToHashSet() : EmptySet;
            }

            var publicChat = ChatManager.Instance?.PublicChat;
            if (publicChat == null || publicChat.PartyLogin == null)
            {
                return EmptySet;
            }

            // Перевіряємо необхідність оновлення кешу публічного чату
            if (_cachedPublicChatPartyLogin == null
                || _lastPublicChatChanged != publicChat.LastChanged
                || _lastPartyLoginCount != publicChat.PartyLogin.Count)
            {
                lock (_syncLock)
                {
                    if (_cachedPublicChatPartyLogin == null
                        || _lastPublicChatChanged != publicChat.LastChanged
                        || _lastPartyLoginCount != publicChat.PartyLogin.Count)
                    {
                        _cachedPublicChatPartyLogin = new HashSet<string>(publicChat.PartyLogin);
                        _lastPublicChatChanged = publicChat.LastChanged;
                        _lastPartyLoginCount = publicChat.PartyLogin.Count;
                    }
                }
            }

            return _cachedPublicChatPartyLogin;
        }

        /// <summary>
        /// Отримує об'єкт серверного гравця за моделлю клієнтського гравця.
        /// </summary>
        public static PlayerServer GetPlayerServer(this Player player)
        {
            if (player?.Login == null) return null;
            return Repository.GetPlayerByLogin(player.Login);
        }
    }
}