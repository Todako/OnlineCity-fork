using OCUnion;
using RimWorld;
using Transfer;
using Transfer.ModelMails;
using Verse;

namespace RimWorldOnlineCity
{
    public class OCIncidentFactory
    {
        public OCIncident GetIncident(IncidentTypes type)
        {
            switch (type)
            {
                case IncidentTypes.Raid:
                    return new IncidentRaid();
                case IncidentTypes.Caravan:
                    return new IncidentCaravan();
                case IncidentTypes.ChunkDrop:
                    return new IncidentChunkDrop();
                case IncidentTypes.Infistation:
                    return new IncidentInfistation();
                case IncidentTypes.Quest:
                    return new IncidentQuest();
                case IncidentTypes.Bombing:
                    return new IncidentBombing();
                case IncidentTypes.Acid:
                    return new IncidentAcid();
                case IncidentTypes.EMP:
                    return new IncidentEMP();
                case IncidentTypes.Pack:
                    return new IncidentPack();
                case IncidentTypes.Eclipse:
                    return new IncidentEclipse();
                case IncidentTypes.Storm:
                    return new IncidentStorm();
                case IncidentTypes.Plague:
                    return new IncidentPlague();
                case IncidentTypes.Def:
                    return new IncidentByDef();
                default:
                    return null;
            }
        }
    }
}