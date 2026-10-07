using OCUnion;
using OCUnion.Transfer.Model;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using Verse;

namespace RimWorldOnlineCity.Model
{
    internal class ClientHashCheckerResult
    {
        /// <summary>
        /// Відмінні файли, які було перезаписано або оновлено.
        /// </summary>
        public List<string> ReplaceFiles { get; set; } = new List<string>();

        /// <summary>
        /// Відмінні файли, які заборонено замінювати на клієнті.
        /// </summary>
        public List<string> DifferentFiles { get; set; } = new List<string>();

        private bool MarkExist { get; set; }
        private string MarkFileName => Loger.PathLog + "FileChecking.txt";
        private string ReportFileName => Loger.PathLog + "FileChecked.txt";
        private string ModsConfigByStart { get; }

        public ClientHashCheckerResult()
        {
            try
            {
                if (MarkExist = File.Exists(MarkFileName))
                {
                    File.Delete(MarkFileName);
                }
            }
            catch
            {
                MarkExist = false;
            }

            ModsConfigByStart = GetModsConfigContent();
        }

        private string GetModsConfigContent()
        {
            var configPath = Path.Combine(GenFilePaths.ConfigFolderPath, "ModsConfig.xml");
            return File.Exists(configPath) ? File.ReadAllText(configPath, Encoding.UTF8) : string.Empty;
        }

        private List<string> GetListLi(string text)
        {
            var result = new List<string>();
            if (string.IsNullOrEmpty(text)) return result;

            int pos = 0;
            while (true)
            {
                pos = text.IndexOf("<li>", pos, StringComparison.Ordinal);
                if (pos < 0) break;
                pos += 4;
                var e = text.IndexOf("</li>", pos, StringComparison.Ordinal);
                if (e < 0) break;

                var item = text.Substring(pos, e - pos).Trim();
                if (item.Length > 0)
                {
                    result.Add(item);
                }
            }
            return result;
        }

        /// <summary>
        /// Формує підсумковий звіт про перевірку файлів та відкриває його користувачеві у разі розбіжностей.
        /// </summary>
        public string ReportComplete()
        {
            if (DifferentFiles.Count > 0 || ReplaceFiles.Count > 0)
            {
                try
                {
                    File.CreateText(MarkFileName).Close();
                }
                catch { }
            }

            if (!MarkExist && DifferentFiles.Count == 0)
            {
                if (ReplaceFiles.Count == 0) return null;
                return "OCity_SessionCC_FilesUpdated".Translate();
            }

            var sb = new StringBuilder(1024);
            var modsConfigByServer = GetModsConfigContent();

            if (!string.Equals(ModsConfigByStart, modsConfigByServer, StringComparison.OrdinalIgnoreCase))
            {
                var verG = GameXMLUtils.GetByTag(ModsConfigByStart, "version");
                var verS = GameXMLUtils.GetByTag(modsConfigByServer, "version");
                if (!string.Equals(verG, verS, StringComparison.OrdinalIgnoreCase))
                {
                    sb.AppendLine();
                    sb.Append("OC_HashCheckerResult_VersionErr".Translate(verG, verS));
                }

                var listStart = GetListLi(ModsConfigByStart);
                var listServer = GetListLi(modsConfigByServer);

                var modsG = new HashSet<string>(listStart, StringComparer.OrdinalIgnoreCase);
                var modsS = new HashSet<string>(listServer, StringComparer.OrdinalIgnoreCase);

                var modsNeed = new HashSet<string>(modsS, StringComparer.OrdinalIgnoreCase);
                modsNeed.ExceptWith(modsG);

                var modsLeft = new HashSet<string>(modsG, StringComparer.OrdinalIgnoreCase);
                modsLeft.ExceptWith(modsS);

                bool hasDiff = false;
                if (modsNeed.Count > 0)
                {
                    hasDiff = true;
                    sb.AppendLine();
                    sb.Append("OC_HashCheckerResult_NeedMods".Translate()).Append(" ");
                    foreach (var item in modsNeed)
                    {
                        sb.AppendLine().Append(item);
                    }
                }

                if (modsLeft.Count > 0)
                {
                    hasDiff = true;
                    sb.AppendLine();
                    sb.Append("OC_HashCheckerResult_ExcessMods".Translate()).Append(" ");
                    foreach (var item in modsLeft)
                    {
                        sb.AppendLine().Append(item);
                    }
                }

                if (!hasDiff && sb.Length == 0)
                {
                    sb.AppendLine();
                    sb.Append("OC_HashCheckerResult_UnexpectedDiff".Translate());
                }
            }

            if (DifferentFiles.Count > 0)
            {
                sb.AppendLine();
                sb.Append("OC_HashCheckerResult_DiffFiles".Translate());
            }

            if (ReplaceFiles.Count > 0)
            {
                var distinctDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < ReplaceFiles.Count; i++)
                {
                    var fn = ReplaceFiles[i];
                    int slashIdx = fn.IndexOfAny(new[] { '\\', '/' });
                    if (slashIdx >= 0)
                    {
                        distinctDirs.Add(fn.Substring(0, slashIdx));
                    }
                }

                if (distinctDirs.Count > 0)
                {
                    sb.AppendLine();
                    sb.Append("OC_HashCheckerResult_ChangedDir".Translate()).Append(" ");
                    foreach (var dir in distinctDirs)
                    {
                        sb.AppendLine().Append(dir);
                    }
                }

                sb.AppendLine().AppendLine();
                sb.Append("OC_HashCheckerResult_ChangedFiles".Translate()).Append(" ");
                for (int i = 0; i < ReplaceFiles.Count; i++)
                {
                    sb.AppendLine().Append(ReplaceFiles[i]);
                }
            }

            if (DifferentFiles.Count > 0)
            {
                sb.AppendLine().AppendLine();
                sb.Append("OC_HashCheckerResult_CriticalDiff".Translate()).Append(" ");
                for (int i = 0; i < DifferentFiles.Count; i++)
                {
                    sb.AppendLine().Append(DifferentFiles[i]);
                }
            }

            string result = sb.Length > 0 ? sb.ToString() : null;

            if (!string.IsNullOrEmpty(result))
            {
                try
                {
                    File.WriteAllText(ReportFileName, result, Encoding.UTF8);
                    Process.Start("notepad", ReportFileName);
                }
                catch { }
            }

            return result;
        }

        internal void FileSynchronization(List<ModelFileInfo> files)
        {
            if (files == null) return;

            for (int i = 0; i < files.Count; i++)
            {
                var f = files[i];
                if (f == null) continue;

                if (f.NeedReplace)
                {
                    ReplaceFiles.Add(f.FileName);
                }
                else
                {
                    DifferentFiles.Add(f.FileName);
                }
            }
        }
    }
}