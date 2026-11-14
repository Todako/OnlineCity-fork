using Model;
using System;

namespace OCUnion
{
    public class AttackUtils
    {
        public static float MaxCostAttackerCaravan(float costTarget, bool isSettlement)
        {
            if (costTarget <= 0f) costTarget = 1f;

            if (isSettlement)
            {
                return 4f / (25f / 1000000f + 10f / costTarget) + 2000f;
            }
            else
            {
                return costTarget * 1.15f + 2000f;
            }
        }

        private static bool IsPlayerOnlineSafe(IPlayerEx player)
        {
            if (player == null) return false;
            if (player.Online) return true;
            if (player.Public != null && player.Public.LastOnlineTime > DateTime.MinValue)
            {
                // Fallback against timezone or clock skew between client and server
                double diffSec = Math.Abs((DateTime.UtcNow - player.Public.LastOnlineTime).TotalSeconds);
                if (diffSec < 45.0) return true;
            }
            return false;
        }

        public static string CheckPossibilityAttack(IPlayerEx attacker, IPlayerEx host, long attackerWOServerId, long hostWOServerId,
            bool protectingNovice, bool isSettlement = true)
        {
            try
            {
                var res =
                    !IsPlayerOnlineSafe(attacker) ? "Attacker not online"
                    : !IsPlayerOnlineSafe(host) ? "Host not online"
                    : !attacker.Public.EnablePVP ? "Attacker not EnablePVP"
                    : !host.Public.EnablePVP ? "Host not EnablePVP"
                    : null;

                if (res != null) return res;
                if (!protectingNovice) return null;

                var hostCosts = host.CostWorldObjects(hostWOServerId);
                var hostCost = MaxCostAttackerCaravan(hostCosts.MarketValueTotal, isSettlement);

                var attCosts = attacker.CostWorldObjects(attackerWOServerId);
                var attCost = attCosts.MarketValueTotal;

                res =
                    attCost > hostCost
                    ? "The cost of the attacker must be less than " + ((long)hostCost).ToString()
                    : (DateTime.UtcNow - host.Public.LastPVPTime).TotalMinutes < host.MinutesIntervalBetweenPVP
                    ? "It was recently attacked. Wait to " + host.Public.LastPVPTime.ToGoodUtcString()
                    : null;

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