using ClientAncillary;
using OCUnion;
using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading;
using Verse;

namespace RimWorldOnlineCity
{
    public class AncillaryUtil
    {
        private static string ConsoleFileName;
        private static int _requestCounter = 1000;
        private const int ProcessTimeoutMs = 5000;

        public AncillaryUtil() { }

        /// <summary>
        /// Швидкий пошук шляху до ClientAncillary.exe.
        /// ОПТИМІЗАЦІЯ: миттєва перевірка локального каталогу моду замість рекурсивного сканування всієї папки Mods.
        /// </summary>
        private static string GetConsoleFileName()
        {
            if (ConsoleFileName != null)
            {
                return string.IsNullOrEmpty(ConsoleFileName) ? null : ConsoleFileName;
            }

            try
            {
                // 1. Перевіряємо теку поруч із самою бібліотекою мода
                var assemblyDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                if (!string.IsNullOrEmpty(assemblyDir))
                {
                    var directPath = Path.Combine(assemblyDir, "ClientAncillary.exe");
                    if (File.Exists(directPath))
                    {
                        return ConsoleFileName = directPath;
                    }

                    // Перевірка на рівень вище
                    var parentPath = Path.Combine(assemblyDir, "..", "ClientAncillary.exe");
                    if (File.Exists(parentPath))
                    {
                        return ConsoleFileName = Path.GetFullPath(parentPath);
                    }
                }

                // 2. Якщо не знайдено — шукаємо лише у папці нашого моду або Mods
                var fs = Directory.GetFiles(GenFilePaths.ModsFolderPath, "ClientAncillary.exe", SearchOption.AllDirectories);
                if (fs.Length > 0)
                {
                    return ConsoleFileName = fs[0];
                }
            }
            catch (Exception ex)
            {
                Loger.Log("AncillaryUtil GetConsoleFileName Exception: " + ex.Message, Loger.LogLevel.WARNING);
            }

            ConsoleFileName = string.Empty;
            return null;
        }

        /// <summary>
        /// Виконує запит до зовнішньої утиліти через Named Pipe.
        /// ОПТИМІЗАЦІЯ: захист від підвисання гри через тайм-аут процесу та атомарна генерація ID.
        /// </summary>
        private byte[] Request(string argument = "")
        {
            var exePath = GetConsoleFileName();
            if (string.IsNullOrEmpty(exePath)) return null;

            int code = Interlocked.Increment(ref _requestCounter);
            var com = new CommunicationConsole();

            byte[] data = null;
            try
            {
                data = com.ReceiveData(code, () =>
                {
                    Process proc = null;
                    try
                    {
                        var pi = new ProcessStartInfo(exePath, code.ToString() + " " + argument)
                        {
                            CreateNoWindow = true,
                            UseShellExecute = false
                        };

                        proc = Process.Start(pi);
                        if (proc != null)
                        {
                            // ОПТИМІЗАЦІЯ: жорсткий таймаут для запобігання вічному блокуванню потоку
                            if (!proc.WaitForExit(ProcessTimeoutMs))
                            {
                                Loger.Log("AncillaryUtil: ClientAncillary.exe timed out, killing process", Loger.LogLevel.WARNING);
                                proc.Kill();
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Loger.Log("AncillaryUtil Process Start Exception: " + ex.Message, Loger.LogLevel.WARNING);
                        if (proc != null && !proc.HasExited)
                        {
                            try { proc.Kill(); } catch { }
                        }
                    }
                    finally
                    {
                        proc?.Dispose();
                    }
                });
            }
            catch (Exception ex)
            {
                Loger.Log("AncillaryUtil Request Exception: " + ex.Message, Loger.LogLevel.WARNING);
            }

            return data;
        }

        public byte[] GetClipboardImageData()
        {
            return Request("0");
        }
    }
}