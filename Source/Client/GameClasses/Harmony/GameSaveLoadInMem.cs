using HarmonyLib;
using OCUnion;
using System;
using System.Collections.Generic;
using System.IO;
using System.Xml;
using Verse;

namespace RimWorldOnlineCity.GameClasses.Harmony
{
    [HarmonyPatch(typeof(ScribeLoader))]
    [HarmonyPatch("InitLoading")]
    internal static class ScribeLoader_InitLoading_Patch
    {
        public static byte[] LoadData = null;
        public static bool Enable = false;

        [HarmonyPrefix]
        public static bool Prefix(ScribeLoader __instance, string filePath)
        {
            if (!Enable) return true;
            Loger.Log("ScribeLoader_InitLoading_Patch Start");

            if (LoadData == null || LoadData.Length == 0)
            {
                Log.Error("ScribeLoader_InitLoading_Patch: LoadData є порожнім або відсутнім!");
                return true;
            }

            if (Scribe.mode != 0)
            {
                Log.Error("Called InitLoading() but current mode is " + Scribe.mode);
                Scribe.ForceStop();
            }
            if (__instance.curParent != null)
            {
                Log.Error("Current parent is not null in InitLoading");
                __instance.curParent = null;
            }
            if (__instance.curPathRelToParent != null)
            {
                Log.Error("Current path relative to parent is not null in InitLoading");
                __instance.curPathRelToParent = null;
            }
            try
            {
                using (var input = new MemoryStream(LoadData, 0, LoadData.Length, false, true))
                using (XmlTextReader reader = new XmlTextReader(input))
                {
                    XmlDocument xmlDocument = new XmlDocument();
                    xmlDocument.Load(reader);
                    __instance.curXmlParent = xmlDocument.DocumentElement;
                }
                Scribe.mode = LoadSaveMode.LoadingVars;
            }
            catch (Exception ex)
            {
                Log.Error("Exception while init loading file: " + filePath + "\n" + ex);
                __instance.ForceStop();
                throw;
            }
            Loger.Log("ScribeLoader_InitLoading_Patch End");
            return false;
        }
    }

    [HarmonyPatch(typeof(ScribeLoader))]
    [HarmonyPatch("InitLoadingMetaHeaderOnly")]
    internal static class ScribeLoader_InitLoadingMetaHeaderOnly_Patch
    {
        [HarmonyPrefix]
        public static bool Prefix(ScribeLoader __instance, string filePath)
        {
            if (!ScribeLoader_InitLoading_Patch.Enable) return true;
            Loger.Log("ScribeLoader_InitLoadingMetaHeaderOnly_Patch Start");

            var data = ScribeLoader_InitLoading_Patch.LoadData;
            if (data == null || data.Length == 0)
            {
                return true;
            }

            if (Scribe.mode != 0)
            {
                Log.Error("Called InitLoadingMetaHeaderOnly() but current mode is " + Scribe.mode);
                Scribe.ForceStop();
            }
            try
            {
                using (var input = new MemoryStream(data, 0, data.Length, false, true))
                using (XmlTextReader xmlTextReader = new XmlTextReader(input))
                {
                    if (!ScribeMetaHeaderUtility.ReadToMetaElement(xmlTextReader))
                    {
                        return false;
                    }
                    using (XmlReader reader = xmlTextReader.ReadSubtree())
                    {
                        XmlDocument xmlDocument = new XmlDocument();
                        xmlDocument.Load(reader);
                        XmlElement xmlElement = xmlDocument.CreateElement("root");
                        xmlElement.AppendChild(xmlDocument.DocumentElement);
                        __instance.curXmlParent = xmlElement;
                    }
                }
                Scribe.mode = LoadSaveMode.LoadingVars;
            }
            catch (Exception ex)
            {
                Log.Error("Exception while init loading meta header: " + filePath + "\n" + ex);
                __instance.ForceStop();
                throw;
            }

            Loger.Log("ScribeLoader_InitLoadingMetaHeaderOnly_Patch End");
            return false;
        }
    }

    [HarmonyPatch(typeof(ScribeSaver))]
    [HarmonyPatch("InitSaving")]
    internal static class ScribeSaver_InitSaving_Patch
    {
        public static MemoryStream SaveData;
        public static bool Enable = false;

        public static int LastSaveSize = 1024 * 1024 * 4;

        private static readonly AccessTools.FieldRef<ScribeSaver, string> CurPathRef =
            AccessTools.FieldRefAccess<ScribeSaver, string>("curPath");
        private static readonly AccessTools.FieldRef<ScribeSaver, HashSet<string>> SavedNodesRef =
            AccessTools.FieldRefAccess<ScribeSaver, HashSet<string>>("savedNodes");
        private static readonly AccessTools.FieldRef<ScribeSaver, int> NextListElementTemporaryIdRef =
            AccessTools.FieldRefAccess<ScribeSaver, int>("nextListElementTemporaryId");
        private static readonly AccessTools.FieldRef<ScribeSaver, Stream> SaveStreamRef =
            AccessTools.FieldRefAccess<ScribeSaver, Stream>("saveStream");
        private static readonly AccessTools.FieldRef<ScribeSaver, XmlWriter> WriterRef =
            AccessTools.FieldRefAccess<ScribeSaver, XmlWriter>("writer");

        private static int GetInitialSaveCapacity()
        {
            if (LastSaveSize <= 0) return 1024 * 1024 * 4;
            int capacity = (int)Math.Min((long)(LastSaveSize * 1.15), 64 * 1024 * 1024);
            return Math.Max(capacity, 1024 * 1024 * 2);
        }

        [HarmonyPrefix]
        public static bool Prefix(ScribeSaver __instance, string filePath, string documentElementName)
        {
            if (!Enable) return true;

            Loger.Log("ScribeSaver_InitSaving_Patch Start");

            if (Scribe.mode != 0)
            {
                Log.Error("Called InitSaving() but current mode is " + Scribe.mode);
                Scribe.ForceStop();
            }

            var curPath = CurPathRef(__instance);
            if (curPath != null)
            {
                Log.Error("Current path is not null in InitSaving");
                CurPathRef(__instance) = null;
            }

            // Гарантуємо ініціалізацію збережених вузлів
            var savedNodes = SavedNodesRef(__instance);
            if (savedNodes == null)
            {
                SavedNodesRef(__instance) = new HashSet<string>();
            }
            else
            {
                savedNodes.Clear();
            }
            NextListElementTemporaryIdRef(__instance) = 0;

            try
            {
                Scribe.mode = LoadSaveMode.Saving;

                var saveStream = SaveData = new MemoryStream(GetInitialSaveCapacity());

                try
                {
                    var dir = Path.GetDirectoryName(filePath);
                    if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    {
                        Directory.CreateDirectory(dir);
                    }
                    File.WriteAllText(filePath, "Online save");
                }
                catch { }

                SaveStreamRef(__instance) = saveStream;

                XmlWriterSettings xmlWriterSettings = new XmlWriterSettings
                {
                    Indent = true,
                    IndentChars = "\t",
                    CloseOutput = false
                };

                var writer = XmlWriter.Create(saveStream, xmlWriterSettings);
                WriterRef(__instance) = writer;

                writer.WriteStartDocument();
                __instance.EnterNode(documentElementName);
            }
            catch (Exception ex)
            {
                Log.Error("Exception while init saving file: " + filePath + "\n" + ex);
                __instance.ForceStop();
                throw;
            }

            Loger.Log("ScribeSaver_InitSaving_Patch End");
            return false;
        }
    }

    [HarmonyPatch(typeof(ScribeSaver))]
    [HarmonyPatch("FinalizeSaving")]
    internal static class ScribeSaver_FinalizeSaving_Patch
    {
        [HarmonyPostfix]
        public static void Postfix()
        {
            if (!ScribeSaver_InitSaving_Patch.Enable) return;
            if (ScribeSaver_InitSaving_Patch.SaveData != null)
            {
                ScribeSaver_InitSaving_Patch.LastSaveSize = (int)ScribeSaver_InitSaving_Patch.SaveData.Length;
            }
        }
    }
}