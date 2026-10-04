using OCUnion.Transfer.Model;
using ServerOnlineCity.Model;
using System;
using System.Collections.Generic;
using Transfer;
using Transfer.ModelMails;

namespace ServerOnlineCity.Services
{
    internal sealed class GetPlayerInfoExtended : IGenerateResponseContainer
    {
        public int RequestTypePackage => (int)PackageType.Request55PlayerInfoExtended;

        public int ResponseTypePackage => (int)PackageType.Response56PlayerInfoExtended;

        private readonly struct IncidentSortEntry
        {
            public readonly long Sort;
            public readonly FMailIncident Mail;

            public IncidentSortEntry(long sort, FMailIncident mail)
            {
                Sort = sort;
                Mail = mail;
            }
        }

        private static readonly Comparison<IncidentSortEntry> IncidentComparer = (a, b) => a.Sort.CompareTo(b.Sort);

        public ModelContainer GenerateModelContainer(ModelContainer request, ServiceContext context)
        {
            if (context.Player == null) return null;
            var result = new ModelContainer() { TypePacket = ResponseTypePackage };
            result.Packet = getPlayerInfoExtended((ModelName)request.Packet, context);
            return result;
        }

        private ModelPlayerInfoExtended getPlayerInfoExtended(ModelName packet, ServiceContext context)
        {
            if (string.IsNullOrEmpty(packet?.Value)) return new ModelPlayerInfoExtended();

            var player = Repository.GetPlayerByLogin(packet.Value);
            if (player == null) return new ModelPlayerInfoExtended();

            var result = new ModelPlayerInfoExtended();
            lock (player)
            {
                var gameProgress = player.GameProgressLast;
                result.ColonistsCount = gameProgress?.ColonistsCount ?? 0;
                result.ColonistsNeedingTend = gameProgress?.ColonistsNeedingTend ?? 0;
                result.ColonistsDownCount = gameProgress?.ColonistsDownCount ?? 0;
                result.AnimalObedienceCount = gameProgress?.AnimalObedienceCount ?? 0;
                result.ExistsEnemyPawns = gameProgress?.ExistsEnemyPawns ?? false;

                // ОПТИМІЗАЦІЯ: швидкий розрахунок пікових навичок без LINQ
                var pawns = gameProgress?.Pawns;
                if (pawns != null && pawns.Count > 0 && pawns[0]?.Skills != null)
                {
                    var maxSkills = new List<int>(pawns[0].Skills);
                    for (int pIdx = 1; pIdx < pawns.Count; pIdx++)
                    {
                        var skills = pawns[pIdx]?.Skills;
                        if (skills == null) continue;

                        int count = Math.Min(maxSkills.Count, skills.Count);
                        for (int i = 0; i < count; i++)
                        {
                            if (maxSkills[i] < skills[i]) maxSkills[i] = skills[i];
                        }
                    }
                    result.MaxSkills = maxSkills;
                }

                result.MarketValueHistory = player.MarketValueHistory;
                result.RankingCount = Repository.GetData?.PlayersRanking?.Count ?? 0;
                result.MarketValueRanking = player.MarketValueRanking;
                result.MarketValueRankingLast = player.MarketValueRankingLast;

                // ОПТИМІЗАЦІЯ: сортування інцидентів без виділення анонімних класів та ітераторів LINQ
                var fMails = player.FunctionMails;
                if (fMails != null && fMails.Count > 0)
                {
                    var incidentList = new List<IncidentSortEntry>(fMails.Count);
                    int incidentIndex = 0;

                    for (int i = 0; i < fMails.Count; i++)
                    {
                        if (fMails[i] is FMailIncident mi && mi.Mail != null)
                        {
                            long sortKey = (long)mi.NumberOrder * 1000000L + incidentIndex++;
                            incidentList.Add(new IncidentSortEntry(sortKey, mi));
                        }
                    }

                    if (incidentList.Count > 0)
                    {
                        incidentList.Sort(IncidentComparer);

                        var mailsView = new List<ModelMailStartIncident>(incidentList.Count);
                        for (int i = 0; i < incidentList.Count; i++)
                        {
                            var mi = incidentList[i].Mail;
                            mailsView.Add(new ModelMailStartIncident
                            {
                                AlreadyStart = mi.AlreadyStart,
                                IncidentType = mi.Mail.IncidentType,
                                IncidentMult = mi.Mail.IncidentMult,
                                PlaceServerId = mi.Mail.PlaceServerId,
                            });
                        }
                        result.FunctionMailsView = mailsView;
                    }
                    else
                    {
                        result.FunctionMailsView = new List<ModelMailStartIncident>(0);
                    }
                }
                else
                {
                    result.FunctionMailsView = new List<ModelMailStartIncident>(0);
                }
            }
            return result;
        }
    }
}