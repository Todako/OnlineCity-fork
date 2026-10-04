using OCUnion;
using OCUnion.Transfer.Model;
using ServerOnlineCity.Model;
using System;
using Transfer;
using Transfer.ModelMails;

namespace ServerOnlineCity.Services
{
    internal sealed class LoginByPassword : IGenerateResponseContainer
    {
        public int RequestTypePackage => (int)PackageType.Request3Login;

        public int ResponseTypePackage => (int)PackageType.Response4Login;

        private static readonly ModelStatus StatusOk = new ModelStatus { Status = 0, Message = null };
        private static readonly ModelStatus StatusIncorrect = new ModelStatus { Status = 1, Message = "User or password incorrect" };
        private static readonly ModelStatus StatusNotApproved = new ModelStatus { Status = 1, Message = "User not approve" };

        public ModelContainer GenerateModelContainer(ModelContainer request, ServiceContext context)
        {
            if (context.Player != null) return null;
            var result = new ModelContainer() { TypePacket = ResponseTypePackage };
            result.Packet = login((ModelLogin)request.Packet, context);
            return result;
        }

        private ModelStatus login(ModelLogin packet, ServiceContext context)
        {
            if (packet == null || string.IsNullOrEmpty(packet.Login))
            {
                return StatusIncorrect;
            }

            Loger.Log($"Player {packet.Login} start login on server.", Loger.LogLevel.LOGIN);
            Loger.Log($"Player {packet.Login} client version {packet.Version}.", Loger.LogLevel.LOGIN);
            packet.Email = Repository.CheckIsIntruder(context, packet.Email, packet.Login);

            if (string.Equals(packet.Login, "system", StringComparison.OrdinalIgnoreCase))
            {
                return StatusIncorrect;
            }

            var player = Repository.GetPlayerByLogin(packet.Login, true);

            if (player != null)
            {
                if (!string.IsNullOrEmpty(packet.KeyReconnect))
                {
                    if (!player.KeyReconnectVerification(packet.KeyReconnect))
                    {
                        Loger.Log("Reconnect " + player.Public.Login + " Fail", Loger.LogLevel.ERROR);
                        player = null;
                    }
                    else
                    {
                        Loger.Log("Reconnect " + player.Public.Login + " OK", Loger.LogLevel.WARNING);
                    }
                }
                else if (player.Pass != packet.Pass)
                {
                    player = null;
                }
            }

            if (player == null)
            {
                return StatusIncorrect;
            }

            if (ServerManager.ServerSettings.PlayerNeedApprove && !player.Approve)
            {
                return StatusNotApproved;
            }

            // Перед входом закриваємо всі попередні підключення цього ж гравця
            if (context.AllSessionAction != null)
            {
                context.AllSessionAction(session =>
                {
                    var sc = session.GetContext();
                    if (sc == null
                        || sc.Player?.Public?.Login != player.Public.Login
                        || sc == context) return;

                    Loger.Log("Disconnect old session at relogin " + player.Public.Login, Loger.LogLevel.LOGIN);
                    session.Dispose();
                });
            }

            lock (player)
            {
                // Дії перед входом
                player.ExitReason = OCUnion.Transfer.DisconnectReason.AllGood;
                player.ApproveLoadWorldReason = true;

                // При відновленні підключення (Reconnect) сесії нічого не скидаємо
                if (string.IsNullOrEmpty(packet.KeyReconnect))
                {
                    // Якщо зайшли за паролем, скидаємо ключ для передачі клієнту нового
                    player.KeyReconnect1 = null;

                    // Скасування бою, якщо обидва учасники були відключені одночасно
                    if (player.AttackData != null) player.AttackData.Finish();

                    // Видалення всіх листів з командою на перезавантаження
                    if (player.Mails != null)
                    {
                        for (int i = 0; i < player.Mails.Count; i++)
                        {
                            if (player.Mails[i] is ModelMailAttackCancel)
                            {
                                player.Mails.RemoveAt(i--);
                            }
                        }
                    }

                    // Оновлюємо покажчики прочитаних повідомлень чату
                    if (packet.Login != "discord" && player.Chats != null)
                    {
                        foreach (var v in player.Chats.Values)
                        {
                            if (v != null)
                            {
                                v.Value = -1;
                                v.Time = DateTime.MinValue;
                            }
                        }
                    }
                }
            }

            context.Player = player;
            context.Logined();
            Loger.Log($"Player {packet.Login} was logged in to the server.", Loger.LogLevel.LOGIN);

            return StatusOk;
        }
    }
}