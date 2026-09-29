using HarmonyLib;
using OCUnion;
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Xml;
using Verse;

namespace RimWorldOnlineCity.GameClasses.Harmony
{
    [HarmonyPatch(typeof(ScribeLoader))]
    [HarmonyPatch("InitLoading")]
    internal class ScribeLoader_InitLoading_Patch
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
    internal class ScribeLoader_InitLoadingMetaHeaderOnly_Patch
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
    internal class ScribeSaver_InitSaving_Patch
    {
        public static MemoryStream SaveData;
        public static bool Enable = false;

        // Кешований розмір останнього збереження для запобігання фрагментації Large Object Heap (LOH)
        public static int LastSaveSize = 1024 * 1024 * 4; // 4 МБ за замовчуванням

        private static readonly FieldInfo CurPathField = AccessTools.Field(typeof(ScribeSaver), "curPath");
        private static readonly FieldInfo SavedNodesField = AccessTools.Field(typeof(ScribeSaver), "savedNodes");
        private static readonly FieldInfo NextListElementTemporaryIdField = AccessTools.Field(typeof(ScribeSaver), "nextListElementTemporaryId");
        private static readonly FieldInfo SaveStreamField = AccessTools.Field(typeof(ScribeSaver), "saveStream");
        private static readonly FieldInfo WriterField = AccessTools.Field(typeof(ScribeSaver), "writer");

        private static int GetInitialSaveCapacity()
        {
            if (LastSaveSize <= 0) return 1024 * 1024 * 4;
            // Виділяємо з запасом +15% для уникнення будь-яких повторних алокацій масиву в LOH
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

            var curPath = (string)CurPathField?.GetValue(__instance);
            if (curPath != null)
            {
                Log.Error("Current path is not null in InitSaving");
                CurPathField?.SetValue(__instance, null);
                var savedNodes = (HashSet<string>)SavedNodesField?.GetValue(__instance);
                savedNodes?.Clear();
                NextListElementTemporaryIdField?.SetValue(__instance, 0);
            }

            try
            {
                Scribe.mode = LoadSaveMode.Saving;

                // ОПТИМІЗАЦІЯ: початкова ємність задається заздалегідь, запобігаючи множинним алокаціям у LOH
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

                SaveStreamField?.SetValue(__instance, saveStream);

                XmlWriterSettings xmlWriterSettings = new XmlWriterSettings
                {
                    Indent = true,
                    IndentChars = "\t",
                    CloseOutput = false
                };

                var writer = XmlWriter.Create(saveStream, xmlWriterSettings);
                WriterField?.SetValue(__instance, writer);

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
}