using Model;
using System;

namespace OCUnion
{
    public class AttackUtils
    {
        public static float MaxCostAttackerCaravan(float costTarget, bool isSettlement)
        {
            if (isSettlement)
            {
                // Дуже приблизна спрощена середня формула обчислення вартості атакувальних рейдів із вихідного коду гри.
                //  4 / (25/1000000 + 10/багатство колонії) + 2000 = багатство нападників (4 — складність і коефіцієнт часу, + 2000 — їжа для каравану).

                return 4f / (25f / 1000000f + 10f / costTarget) + 2000f;
            }
            else
            {
                // Якщо атакують каравани, атакувані можуть бути сильнішими на 15%.
                return costTarget * 1.15f;
            }
        }

        public static string CheckPossibilityAttack(IPlayerEx attacker, IPlayerEx host, long attackerWOServerId, long hostWOServerId
            , bool protectingNovice)
        {
            try
            {
                var res =
                    !attacker.Online ? "Attacker not online"
                    : !host.Online ? "Host not online"
                    : !attacker.Public.EnablePVP ? "Attacker not EnablePVP"
                    : !host.Public.EnablePVP ? "Host not EnablePVP"
                    : null
                    ;
                if (res != null) return res;
                if (!protectingNovice) return null;

                var hostCosts = host.CostWorldObjects(hostWOServerId);
                var hostCost = MaxCostAttackerCaravan(hostCosts.MarketValueTotal, true);

                var attCosts = attacker.CostWorldObjects(attackerWOServerId);
                var attCost = attCosts.MarketValueTotal;

                res =
                    // Вартість колонії більша за вартість каравану.
                    attCost > hostCost
                    ? //"The cost of the attackers is higher than the cost of the colony, this is not fair"
                    "The cost of the attacker must be less than " + ((long)hostCost).ToString()
                    // Колонії більше одного року.
                    //to do  : host.Public.LastTick < 3600000 ? "You must not attack the game for less than a year"

                    // На колонію нещодавно нападали.
                    : (DateTime.UtcNow - host.Public.LastPVPTime).TotalMinutes < host.MinutesIntervalBetweenPVP
                    ? "It was recently attacked. Wait to " + host.Public.LastPVPTime.ToGoodUtcString()
                    : null;

                /*
                if (res != null) Loger.Log("CheckPossibilityAttack: " + res
                    + " LastOnlineTime=" + attacker.Public.LastOnlineTime.ToString("o")
                    + " UtcNow=" + DateTime.UtcNow.ToString("o")
                    );
                */
                return res;
            }
            catch (Exception exp)
            {
                Loger.Log("CheckPossibilityAttack Exception" + exp.ToString(), Loger.LogLevel.ERROR);
                return "Error calc";
            }
        }
    }
}
