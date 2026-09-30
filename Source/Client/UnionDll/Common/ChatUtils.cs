using System.Collections.Generic;

namespace OCUnion.Common
{
    public static class ChatUtils
    {
        /// <summary>
        /// Розбирає введений у чат рядок на назву команди та список аргументів.
        /// </summary>
        public static void ParceCommand(string chatLine, out string command, out List<string> argsM)
        {
            if (string.IsNullOrEmpty(chatLine))
            {
                command = string.Empty;
                argsM = new List<string>(0);
                return;
            }

            var s = chatLine.Split(new char[] { ' ' }, 2);
            command = s[0].Trim().ToLower();
            var args = s.Length == 1 ? "" : s[1];

            // Розбираємо аргументи в лапках '. Подвійна лапка '' вказує на символ лапки всередині рядка.
            argsM = SplitBySpace(args);
        }

        /// <summary>
        /// Розділяє рядок аргументів за пропусками з урахуванням екранування лапками.
        /// </summary>
        public static List<string> SplitBySpace(string args)
        {
            int i = 0;
            var argsM = new List<string>();

            while (i + 1 < args.Length)
            {
                if (args[i] == '\'')
                {
                    int endK = i;
                    bool exit;
                    do
                    {
                        // Шукаємо закриваючу лапку; якщо після знайденої йде ще одна — це екранування
                        exit = true;
                        endK = args.IndexOf('\'', endK + 1);
                        if (endK >= 0 && endK + 1 < args.Length && args[endK + 1] == '\'')
                        {
                            endK++;
                            exit = false;
                        }
                    }
                    while (!exit);

                    if (endK >= 0)
                    {
                        argsM.Add(args.Substring(i + 1, endK - i - 1).Replace("''", "'"));
                        i = endK + 1;
                        continue;
                    }
                }

                var ni = args.IndexOf(' ', i);
                if (ni >= 0)
                {
                    // Ігноруємо подвійні або множинні пропуски
                    if (ni > i)
                    {
                        argsM.Add(args.Substring(i, ni - i));
                    }
                    i = ni + 1;
                    continue;
                }
                else
                {
                    break;
                }
            }

            if (i < args.Length)
            {
                argsM.Add(args.Substring(i));
            }

            return argsM;
        }
    }
}