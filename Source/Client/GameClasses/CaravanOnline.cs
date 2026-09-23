using Model;
using OCUnion;
using RimWorld;
using RimWorld.Planet;
using RimWorldOnlineCity.UI;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using UnityEngine;
using Verse;

namespace RimWorldOnlineCity
{
    [StaticConstructorOnStartup]
    public class CaravanOnline : WorldObjectBaseOnline
    {
        public override IModelPlace Place => OnlineWObject;

        public string OnlinePlayerLogin => OnlineWObject?.LoginOwner;
        public string OnlineName => OnlineWObject == null ? string.Empty : OnlineWObject.Name;

        public PlayerClient Player => SessionClientController.Data?.Players != null && !string.IsNullOrEmpty(OnlinePlayerLogin) && SessionClientController.Data.Players.TryGetValue(OnlinePlayerLogin, out var pl) ? pl : null;
        public bool IsOnline => Player != null && Player.Online;

        public WorldObjectEntry OnlineWObject;

        // Кешування назви каравану для карти світу
        private string _cachedLabel;
        private string _lastLabelName;
        private string _lastLabelLogin;

        public override string Label
        {
            get
            {
                var name = OnlineName;
                var login = OnlinePlayerLogin;
                if (_cachedLabel == null || name != _lastLabelName || login != _lastLabelLogin)
                {
                    _lastLabelName = name;
                    _lastLabelLogin = login;
                    _cachedLabel = "OCity_Caravan_Player".Translate(name, login);
                }
                return _cachedLabel;
            }
        }

        #region Кешування InspectString

        private float _lastInspectStringTime = -999f;
        private string _cachedInspectString;
        private bool _lastInspectIsOnline;
        private static string _cachedInspectTemplate;

        public override string GetInspectString()
        {
            float now = Time.realtimeSinceStartup;
            bool online = IsOnline;

            if (now - _lastInspectStringTime < 1.0f && _cachedInspectString != null && online == _lastInspectIsOnline)
            {
                return _cachedInspectString;
            }

            _lastInspectStringTime = now;
            _lastInspectIsOnline = online;
            _cachedInspectString = CalculateInspectString(online);
            return _cachedInspectString;
        }

        private string CalculateInspectString(bool online)
        {
            if (OnlineWObject == null)
            {
                return "OCity_Caravan_Player".Translate(OnlineName, OnlinePlayerLogin) + Environment.NewLine;
            }

            if (_cachedInspectTemplate == null)
            {
                _cachedInspectTemplate = "OCity_Caravan_Player".Translate() + Environment.NewLine + "OCity_Caravan_Other".Translate();
            }

            var statusOnline = online ? " Online!" : "";
            var totalTrading = (OnlineWObject.MarketValueBalance + OnlineWObject.MarketValueStorage).ToStringMoney();

            var sb = new StringBuilder(256);
            sb.AppendFormat(
                _cachedInspectTemplate,
                OnlineName,
                OnlinePlayerLogin + statusOnline + " (sId:" + OnlineWObject.PlaceServerId + ")",
                OnlineWObject.MarketValue.ToStringMoney(),
                OnlineWObject.MarketValuePawn.ToStringMoney(),
                totalTrading
            );

            if ((this is BaseOnline) && (SessionClientController.My?.EnablePVP ?? false))
            {
                sb.AppendLine();
                sb.Append("OCity_Caravan_PlayerAttackCost".Translate(
                    AttackUtils.MaxCostAttackerCaravan(OnlineWObject.MarketValueTotal, this is BaseOnline).ToStringMoney()));
            }

            if (OnlineWObject.FreeWeight > 0 && OnlineWObject.FreeWeight < 999999)
            {
                sb.AppendLine();
                sb.Append("OCity_Caravan_FreeWeight".Translate());
                sb.Append(OnlineWObject.FreeWeight.ToStringMass());
            }

            return sb.ToString();
        }

        private float _lastInspectExtendedTime = -999f;
        private string _cachedInspectExtendedString;
        private bool _lastInspectExtendedIsOnline;
        private static string _cachedInspectExtendedTemplate;

        public virtual string GetInspectExtendedString()
        {
            float now = Time.realtimeSinceStartup;
            bool online = IsOnline;

            if (now - _lastInspectExtendedTime < 1.0f && _cachedInspectExtendedString != null && online == _lastInspectExtendedIsOnline)
            {
                return _cachedInspectExtendedString;
            }

            _lastInspectExtendedTime = now;
            _lastInspectExtendedIsOnline = online;
            _cachedInspectExtendedString = CalculateInspectExtendedString(online);
            return _cachedInspectExtendedString;
        }

        private string CalculateInspectExtendedString(bool online)
        {
            if (OnlineWObject == null)
            {
                return "OCity_Caravan_Player".Translate(OnlineName, OnlinePlayerLogin) + Environment.NewLine;
            }

            if (_cachedInspectExtendedTemplate == null)
            {
                _cachedInspectExtendedTemplate = "OCity_Caravan_Player".Translate() + Environment.NewLine + "OCity_Caravan_Other".Translate();
            }

            var statusOnline = online ? "<:check_mark_button height=16:> Online!" : "<:zzz height=16:> ";
            var totalTrading = (OnlineWObject.MarketValueBalance + OnlineWObject.MarketValueStorage).ToStringMoney();

            var sb = new StringBuilder(256);
            sb.AppendFormat(
                _cachedInspectExtendedTemplate,
                $"<&{OnlineWObject.PlaceServerId}> ",
                statusOnline + " (sId:" + OnlineWObject.PlaceServerId + ")",
                "<:money_bag height=16:> " + OnlineWObject.MarketValue.ToStringMoney(),
                "<:busts_in_silhouette height=16:> " + OnlineWObject.MarketValuePawn.ToStringMoney(),
                "<:chart_increasing height=16:> " + totalTrading
            );

            if ((this is BaseOnline) && (SessionClientController.My?.EnablePVP ?? false))
            {
                sb.AppendLine();
                sb.Append("OCity_Caravan_PlayerAttackCost".Translate(
                    AttackUtils.MaxCostAttackerCaravan(OnlineWObject.MarketValueTotal, this is BaseOnline).ToStringMoney()));
            }

            if (OnlineWObject.FreeWeight > 0 && OnlineWObject.FreeWeight < 999999)
            {
                sb.AppendLine();
                sb.Append("OCity_Caravan_FreeWeight".Translate());
                sb.Append(" <:handbag height=16:> ");
                sb.Append(OnlineWObject.FreeWeight.ToStringMass());
            }

            return sb.ToString();
        }

        #endregion

        public override string GetDescription()
        {
            var sb = new StringBuilder(512);
            sb.Append(base.GetDescription());
            sb.AppendLine();
            sb.AppendLine();
            sb.Append(GetInspectString());

            var player = Player;
            if (player != null)
            {
                sb.AppendLine();
                sb.AppendLine();
                sb.Append(player.GetTextInfo());
            }
            return sb.ToString();
        }

        [DebuggerHidden]
        public override IEnumerable<FloatMenuOption> GetFloatMenuOptions(Caravan caravan)
        {
            foreach (FloatMenuOption o in base.GetFloatMenuOptions(caravan))
            {
                yield return o;
            }

            if (SessionClientController.My == null || this.Player == null) yield break;

            var player = this.Player;
            FloatMenuOption fmoTrade;
            try
            {
                fmoTrade = ExchengeUtils.ExchangeOfGoods_GetFloatMenu(this, () =>
                {
                    caravan.pather.StartPath(this.Tile, new CaravanArrivalAction_VisitOnline(this, "exchangeOfGoods"), true);
                });
            }
            catch
            {
                yield break;
            }
            yield return fmoTrade;

            if (SessionClientController.My.EnablePVP
                && this is BaseOnline
                && GameAttacker.CanStart)
            {
                FloatMenuOption fmo;
                try
                {
                    var dis = AttackUtils.CheckPossibilityAttack(
                        SessionClientController.Data.MyEx,
                        player,
                        UpdateWorldController.GetMyByLocalId(caravan.ID).PlaceServerId,
                        this.OnlineWObject.PlaceServerId,
                        SessionClientController.Data.ProtectingNovice
                    );

                    fmo = new FloatMenuOption("OCity_Caravan_Attack".Translate(OnlinePlayerLogin + " " + OnlineName)
                        + (dis != null ? " (" + dis + ")" : ""),
                        delegate
                        {
                            caravan.pather.StartPath(this.Tile, new CaravanArrivalAction_VisitOnline(this, "attack"), true);
                        },
                        MenuOptionPriority.Default, null, null, 0f, null, this);

                    if (dis != null)
                    {
                        fmo.Disabled = true;
                    }
                }
                catch
                {
                    yield break;
                }
                yield return fmo;
            }
        }

        public override IEnumerable<Gizmo> GetGizmos()
        {
            foreach (Gizmo gizmo in base.GetGizmos())
            {
                yield return gizmo;
            }

            if (Player != null)
            {
                yield return new Command_Action
                {
                    defaultLabel = "OCity_Dialog_ChennelPlayerInfo".Translate(),
                    defaultDesc = "OCity_Dialog_ChennelPlayerInfoTitle".Translate() + Player.Public.Login,
                    icon = GeneralTexture.OCInfo,
                    action = delegate
                    {
                        Dialog_InfoPlayer.ShowInfoPlayer(Player);
                    }
                };
            }

            yield return new Command_Action
            {
                defaultLabel = "OCity_Dialog_Exchenge_Trade_Orders".Translate(),
                defaultDesc = "OCity_Dialog_Exchenge_Trade_Orders".Translate(),
                icon = GeneralTexture.TradeButtonIcon,
                action = delegate
                {
                    Find.WindowStack.Add(new Dialog_Exchenge(this));
                }
            };
        }

        #region Icons
        // ОПТИМІЗАЦІЯ: спільні статичні матеріали для всіх караванів на планеті
        private static Material MatCaravanOn;
        private static Material MatCaravanOff;

        private static readonly Texture2D CaravanOn = ContentFinder<Texture2D>.Get("CaravanOn");
        private static readonly Texture2D CaravanOff = ContentFinder<Texture2D>.Get("CaravanOff");
        private static readonly Texture2D CaravanOnExpanding = ContentFinder<Texture2D>.Get("CaravanOnExpanding");
        private static readonly Texture2D CaravanOffExpanding = ContentFinder<Texture2D>.Get("CaravanOffExpanding");

        public override Material Material
        {
            get
            {
                if (IsOnline)
                {
                    if (MatCaravanOn == null)
                    {
                        MatCaravanOn = MaterialPool.MatFrom(
                            CaravanOn,
                            ShaderDatabase.WorldOverlayTransparentLit,
                            Color.white,
                            WorldMaterials.WorldObjectRenderQueue);
                    }
                    return MatCaravanOn;
                }
                else
                {
                    if (MatCaravanOff == null)
                    {
                        MatCaravanOff = MaterialPool.MatFrom(
                            CaravanOff,
                            ShaderDatabase.WorldOverlayTransparentLit,
                            Color.white,
                            WorldMaterials.WorldObjectRenderQueue);
                    }
                    return MatCaravanOff;
                }
            }
        }

        public override Texture2D ExpandingIcon => IsOnline ? CaravanOnExpanding : CaravanOffExpanding;

        public override string ExpandingIconName => IsOnline ? "CaravanOnExpanding" : "CaravanOffExpanding";
        #endregion
    }
}