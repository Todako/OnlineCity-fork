using Discord.Commands;
using OC.DiscordBotServer.Commands;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace OC.DiscordBotServer.Modules
{
    public sealed class UserModule : ModuleBase<SocketCommandContext>
    {
        private readonly RegmeCommand _regUserCmd;

        public UserModule(RegmeCommand regUserCmd)
        {
            _regUserCmd = regUserCmd;
        }

        [Description("Status server: where started, online player e.t.c ")]
        // Повідомляє стан сервера: чи запущений він, скільки гравців онлайн тощо.
        [Command("status")]
        public async Task StatusAsync()
        {
            // TO DO
            await ReplyAsync("in developing . . .");
        }

        [Command("regme")]
        [Description("{OCLogin}" +
            "\n Linking account discord user to RimWorld Online City"
            + "\n Attention: type it only in private chat! Bot self find where are registred and send link to a channel")]
        // Прив'язує обліковий запис користувача Discord до сервера RimworldOnlineCity: !regme Логін_у_RimworldonlineCity.
        public async Task RegmeAsync(string userToken)
        {
            await ReplyAsync(_regUserCmd.Execute(Context, userToken));
        }

        [Command("help")]
        [Description("List of all commands")]
        // Виводить список команд.
        public async Task Helpsync()
        {
            var sb = new StringBuilder();
            foreach (var type in Assembly.GetExecutingAssembly().GetTypes())
            {
                if (!(type.IsClass && type.IsSubclassOf(typeof(ModuleBase<SocketCommandContext>))))
                {
                    continue;
                }

                foreach (var method in type.GetMethods().Where(x => x.IsPublic && x.GetCustomAttribute<CommandAttribute>() != null && x.GetCustomAttribute<CommandAttribute>() != null))
                {
                    DescriptionAttribute desc = method.GetCustomAttribute<DescriptionAttribute>();
                    CommandAttribute cmd = method.GetCustomAttribute<CommandAttribute>();

                    if (!string.IsNullOrEmpty(desc.Description))
                    {
                        // !OC help: 
                        sb.Append(Program.PX + ' ');
                        sb.Append(cmd.Text);
                        sb.Append(": ");
                        sb.Append(desc.Description);
                        sb.AppendLine();
                    }
                }
            }

            await ReplyAsync(sb.ToString());
        }
    }
}
