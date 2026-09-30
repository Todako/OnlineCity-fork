using System;
using System.Collections.Generic;
using System.Reflection;

namespace RimWorldOnlineCity
{
    /// <summary>
    /// Службовий клас для налагодження та розробки інструментів тестування.
    /// </summary>
    public class DevelopTest
    {
        private static readonly HashSet<string> ExcludeTypes = new HashSet<string>()
        {
            "Faction",
            "Filth",
            "ThoughtHandler",
            "Pawn_WorkSettings",
            "WorldObjectDef",
            "ThingDef",
            "PawnKindDef",
            "NeedDef",
            "HediffDef",
            "ThinkTreeDef",
            "MentalStateDef",
            "SoundDef",
            "SkillDef",
            "HairDef",
            "TraitDef",
            "TimeAssignmentDef",
            "FactionDef",
            "ThingCategoryDef",
            "SpecialThingFilterDef",
            "PrisonerInteractionModeDef",
            "PawnGroupKindDef",
            "RaidStrategyDef",
            "WorkGiverDef",
        };

        public static string TextObj(object o, bool detalic = false)
        {
            return detalic
                ? Restricted.ToStringRestricted(o, ExcludeTypes)
                : Restricted.ToStringRestrictedShort(o, ExcludeTypes);
        }

        public bool Run()
        {
            return false;
        }
    }

    public static class ExtensionMethod
    {
        public static Component AddComponentExt(this GameObject obj, string scriptName)
        {
            Component cmpnt = null;

            for (int i = 0; i < 10; i++)
            {
                cmpnt = _AddComponentExt(obj, scriptName, i);
                if (cmpnt != null)
                {
                    break;
                }
            }

            if (cmpnt == null)
            {
                Debug.LogError("Failed to Add Component");
                return null;
            }
            return cmpnt;
        }

        private static Component _AddComponentExt(GameObject obj, string className, int trials)
        {
            const string userMadeScript = "Assembly-CSharp, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null";
            const string builtInScript = "UnityEngine, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null";
            const string builtInScriptUI = "UnityEngine.UI, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null";
            const string builtInScriptNetwork = "UnityEngine.Networking, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null";
            const string builtInScriptAnalytics = "UnityEngine.Analytics, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null";
            const string builtInScriptHoloLens = "UnityEngine.HoloLens, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null";

            Assembly asm = null;

            try
            {
                switch (trials)
                {
                    case 0:
                        asm = Assembly.Load(userMadeScript);
                        break;
                    case 1:
                        className = "UnityEngine." + className;
                        asm = Assembly.Load(builtInScript);
                        break;
                    case 2:
                        className = "UnityEngine.UI." + className;
                        asm = Assembly.Load(builtInScriptUI);
                        break;
                    case 3:
                        className = "UnityEngine.Video." + className;
                        asm = Assembly.Load(builtInScript);
                        break;
                    case 4:
                        className = "UnityEngine.Networking." + className;
                        asm = Assembly.Load(builtInScriptNetwork);
                        break;
                    case 5:
                        className = "UnityEngine.Analytics." + className;
                        asm = Assembly.Load(builtInScriptAnalytics);
                        break;
                    case 6:
                        className = "UnityEngine.EventSystems." + className;
                        asm = Assembly.Load(builtInScriptUI);
                        break;
                    case 7:
                        className = "UnityEngine.Audio." + className;
                        asm = Assembly.Load(builtInScriptHoloLens);
                        break;
                    case 8:
                        className = "UnityEngine.VR.WSA." + className;
                        asm = Assembly.Load(builtInScriptHoloLens);
                        break;
                    case 9:
                        className = "UnityEngine.AI." + className;
                        asm = Assembly.Load(builtInScript);
                        break;
                }
            }
            catch
            { }

            if (asm == null) return null;

            Type type = asm.GetType(className);
            if (type == null) return null;

            return obj.AddComponent(type);
        }
    }
}