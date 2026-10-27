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

        // Потокобезпечний спільний буфер для уникнення алокацій StringBuilder у купі
        [ThreadStatic]
        private static StringBuilder _sharedSb;

        private static StringBuilder GetSharedStringBuilder()
        {
            var sb = _sharedSb;
            if (sb == null)
            {
                sb = new StringBuilder(512);
                _sharedSb = sb;
            }
            else
            {
                sb.Clear();
            }
            return sb;
        }

        // Однокадрове кешування гравця для усунення зайвих пошуків у словнику Players
        private PlayerClient _cachedPlayer;
        private string _cachedPlayerLogin;
        private int _lastPlayerLookupFrame = -1;

        public PlayerClient Player
        {
            get
            {
                var login = OnlinePlayerLogin;
                if (string.IsNullOrEmpty(login)) return null;

                int frame = Time.frameCount;
                if (_lastPlayerLookupFrame == frame && _cachedPlayerLogin == login)
                {
                    return _cachedPlayer;
                }

                _lastPlayerLookupFrame = frame;
                _cachedPlayerLogin = login;

                if (SessionClientController.Data?.Players != null && SessionClientController.Data.Players.TryGetValue(login, out var pl))
                {
                    _cachedPlayer = pl;
                }
                else
                {
                    _cachedPlayer = null;
                }
                return _cachedPlayer;
            }
        }

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

        #region Кешування InspectString з перевіркою фактичних змін (Dirty Check)

        private float _lastInspectStringTime = -999f;
        private string _cachedInspectString;
        private bool _lastInspectIsOnline;
        private float _lastMarketValue = -1f;
        private float _lastMarketValuePawn = -1f;
        private float _lastMarketValueTrading = -1f;
        private float _lastFreeWeight = -1f;
        private long _lastServerId = -1;
        private bool _lastPvpEnabled;

        private static string _cachedInspectTemplate;
        private static string _cachedFreeWeightLabel;

        private bool IsDataChanged(bool online, bool pvpEnabled, float mv, float mvp, float mvt, float fw, long sid)
        {
            return online != _lastInspectIsOnline
                || pvpEnabled != _lastPvpEnabled
                || mv != _lastMarketValue
                || mvp != _lastMarketValuePawn
                || mvt != _lastMarketValueTrading
                || fw != _lastFreeWeight
                || sid != _lastServerId;
        }

        public override string GetInspectString()
        {
            float now = Time.realtimeSinceStartup;
            if (now - _lastInspectStringTime < 1.0f && _cachedInspectString != null)
            {
                return _cachedInspectString;
            }

            _lastInspectStringTime = now;
            bool online = IsOnline;
            bool pvp = (this is BaseOnline) && (SessionClientController.My?.EnablePVP ?? false);
            float mv = OnlineWObject?.MarketValue ?? 0f;
            float mvp = OnlineWObject?.MarketValuePawn ?? 0f;
            float mvt = (OnlineWObject?.MarketValueBalance ?? 0f) + (OnlineWObject?.MarketValueStorage ?? 0f);
            float fw = OnlineWObject?.FreeWeight ?? 0f;
            long sid = OnlineWObject?.PlaceServerId ?? -1;

            if (_cachedInspectString != null && !IsDataChanged(online, pvp, mv, mvp, mvt, fw, sid))
            {
                return _cachedInspectString;
            }

            _lastInspectIsOnline = online;
            _lastPvpEnabled = pvp;
            _lastMarketValue = mv;
            _lastMarketValuePawn = mvp;
            _lastMarketValueTrading = mvt;
            _lastFreeWeight = fw;
            _lastServerId = sid;

            _cachedInspectString = CalculateInspectString(online, pvp, mv, mvp, mvt, fw, sid);
            return _cachedInspectString;
        }

        private string CalculateInspectString(bool online, bool pvp, float mv, float mvp, float mvt, float fw, long sid)
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
            var sb = GetSharedStringBuilder();

            sb.AppendFormat(
                _cachedInspectTemplate,
                OnlineName,
                OnlinePlayerLogin + statusOnline + " (sId:" + sid + ")",
                mv.ToStringMoney(),
                mvp.ToStringMoney(),
                mvt.ToStringMoney()
            );

            if (pvp)
            {
                sb.AppendLine();
                sb.Append("OCity_Caravan_PlayerAttackCost".Translate(
                    AttackUtils.MaxCostAttackerCaravan(OnlineWObject.MarketValueTotal, true).ToStringMoney()));
            }

            if (fw > 0 && fw < 999999)
            {
                if (_cachedFreeWeightLabel == null)
                {
                    _cachedFreeWeightLabel = "OCity_Caravan_FreeWeight".Translate().ToString();
                }
                sb.AppendLine();
                sb.Append(_cachedFreeWeightLabel);
                sb.Append(fw.ToStringMass());
            }

            return sb.ToString();
        }

        private float _lastInspectExtendedTime = -999f;
        private string _cachedInspectExtendedString;
        private bool _lastExtOnline;
        private float _lastExtMv = -1f;
        private float _lastExtMvp = -1f;
        private float _lastExtMvt = -1f;
        private float _lastExtFw = -1f;
        private long _lastExtSid = -1;
        private bool _lastExtPvp;
        private static string _cachedInspectExtendedTemplate;

        public virtual string GetInspectExtendedString()
        {
            float now = Time.realtimeSinceStartup;
            if (now - _lastInspectExtendedTime < 1.0f && _cachedInspectExtendedString != null)
            {
                return _cachedInspectExtendedString;
            }

            _lastInspectExtendedTime = now;
            bool online = IsOnline;
            bool pvp = (this is BaseOnline) && (SessionClientController.My?.EnablePVP ?? false);
            float mv = OnlineWObject?.MarketValue ?? 0f;
            float mvp = OnlineWObject?.MarketValuePawn ?? 0f;
            float mvt = (OnlineWObject?.MarketValueBalance ?? 0f) + (OnlineWObject?.MarketValueStorage ?? 0f);
            float fw = OnlineWObject?.FreeWeight ?? 0f;
            long sid = OnlineWObject?.PlaceServerId ?? -1;

            if (_cachedInspectExtendedString != null
                && online == _lastExtOnline
                && pvp == _lastExtPvp
                && mv == _lastExtMv
                && mvp == _lastExtMvp
                && mvt == _lastExtMvt
                && fw == _lastExtFw
                && sid == _lastExtSid)
            {
                return _cachedInspectExtendedString;
            }

            _lastExtOnline = online;
            _lastExtPvp = pvp;
            _lastExtMv = mv;
            _lastExtMvp = mvp;
            _lastExtMvt = mvt;
            _lastExtFw = fw;
            _lastExtSid = sid;

            _cachedInspectExtendedString = CalculateInspectExtendedString(online, pvp, mv, mvp, mvt, fw, sid);
            return _cachedInspectExtendedString;
        }

        private string CalculateInspectExtendedString(bool online, bool pvp, float mv, float mvp, float mvt, float fw, long sid)
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
            var sb = GetSharedStringBuilder();

            sb.AppendFormat(
                _cachedInspectExtendedTemplate,
                "<&" + sid + "> ",
                statusOnline + " (sId:" + sid + ")",
                "<:money_bag height=16:> " + mv.ToStringMoney(),
                "<:busts_in_silhouette height=16:> " + mvp.ToStringMoney(),
                "<:chart_increasing height=16:> " + mvt.ToStringMoney()
            );

            if (pvp)
            {
                sb.AppendLine();
                sb.Append("OCity_Caravan_PlayerAttackCost".Translate(
                    AttackUtils.MaxCostAttackerCaravan(OnlineWObject.MarketValueTotal, true).ToStringMoney()));
            }

            if (fw > 0 && fw < 999999)
            {
                if (_cachedFreeWeightLabel == null)
                {
                    _cachedFreeWeightLabel = "OCity_Caravan_FreeWeight".Translate().ToString();
                }
                sb.AppendLine();
                sb.Append(_cachedFreeWeightLabel);
                sb.Append(" <:handbag height=16:> ");
                sb.Append(fw.ToStringMass());
            }

            return sb.ToString();
        }

        #endregion

        public override string GetDescription()
        {
            string baseDesc = base.GetDescription();
            string inspectStr = GetInspectString();
            var player = Player;
            string playerInfo = player?.GetTextInfo();

            var sb = GetSharedStringBuilder();
            sb.Append(baseDesc);
            sb.Append("\n\n");
            sb.Append(inspectStr);

            if (!string.IsNullOrEmpty(playerInfo))
            {
                sb.Append("\n\n");
                sb.Append(playerInfo);
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
            FloatMenuOption fmoTrade = null;
            try
            {
                fmoTrade = ExchengeUtils.ExchangeOfGoods_GetFloatMenu(this, () =>
                {
                    if (caravan.Tile == this.Tile)
                    {
                        ExchengeUtils.ExchangeOfGoods_DoAction(this, caravan);
                    }
                    else
                    {
                        caravan.pather.StartPath(this.Tile, new CaravanArrivalAction_VisitOnline(this, "exchangeOfGoods"), true);
                    }
                });
            }
            catch
            {
                yield break;
            }

            if (fmoTrade != null)
            {
                yield return fmoTrade;
            }

            if (SessionClientController.My.EnablePVP
                && this is BaseOnline
                && GameAttacker.CanStart)
            {
                FloatMenuOption fmo = null;
                try
                {
                    string dis = null;
                    var myEntry = UpdateWorldController.GetMyByLocalId(caravan.ID);

                    if (myEntry == null || myEntry.PlaceServerId == 0 || this.OnlineWObject == null || this.OnlineWObject.PlaceServerId == 0)
                    {
                        dis = "OCity_SessionCC_Synchronization".TranslateCache();
                    }
                    else
                    {
                        dis = AttackUtils.CheckPossibilityAttack(
                            SessionClientController.Data.MyEx,
                            player,
                            myEntry.PlaceServerId,
                            this.OnlineWObject.PlaceServerId,
                            SessionClientController.Data.ProtectingNovice
                        );
                    }

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

                if (fmo != null)
                {
                    yield return fmo;
                }
            }
        }

        private Command_Action _gizmoPlayerInfo;
        private Command_Action _gizmoTrade;
        private string _lastGizmoPlayerLogin;
        private static string _cachedPlayerInfoTitlePrefix;

        public override IEnumerable<Gizmo> GetGizmos()
        {
            foreach (Gizmo gizmo in base.GetGizmos())
            {
                yield return gizmo;
            }

            var player = Player;
            if (player != null)
            {
                var login = player.Public?.Login ?? string.Empty;
                if (_gizmoPlayerInfo == null)
                {
                    _gizmoPlayerInfo = new Command_Action
                    {
                        defaultLabel = "OCity_Dialog_ChennelPlayerInfo".Translate(),
                        icon = GeneralTexture.OCInfo,
                        action = delegate
                        {
                            var p = Player;
                            if (p != null) Dialog_InfoPlayer.ShowInfoPlayer(p);
                        }
                    };
                }

                if (_lastGizmoPlayerLogin != login || _gizmoPlayerInfo.defaultDesc == null)
                {
                    _lastGizmoPlayerLogin = login;
                    if (_cachedPlayerInfoTitlePrefix == null)
                    {
                        _cachedPlayerInfoTitlePrefix = "OCity_Dialog_ChennelPlayerInfoTitle".Translate().ToString();
                    }
                    _gizmoPlayerInfo.defaultDesc = _cachedPlayerInfoTitlePrefix + login;
                }

                yield return _gizmoPlayerInfo;
            }

            if (_gizmoTrade == null)
            {
                var tradeText = "OCity_Dialog_Exchenge_Trade_Orders".Translate();
                _gizmoTrade = new Command_Action
                {
                    defaultLabel = tradeText,
                    defaultDesc = tradeText,
                    icon = GeneralTexture.TradeButtonIcon,
                    action = delegate
                    {
                        Find.WindowStack.Add(new Dialog_Exchenge(this));
                    }
                };
            }
            yield return _gizmoTrade;
        }

        #region Icons
        private static Material MatCaravanOn;
        private static Material MatCaravanOff;

        private static readonly Texture2D CaravanOn = ContentFinder<Texture2D>.Get("CaravanOn");
        private static readonly Texture2D CaravanOff = ContentFinder<Texture2D>.Get("CaravanOff");
        private static readonly Texture2D CaravanOnExpanding = ContentFinder<Texture2D>.Get("CaravanOnExpanding");
        private static readonly Texture2D CaravanOffExpanding = ContentFinder<Texture2D>.Get("CaravanOffExpanding");

        static CaravanOnline()
        {
            InitMaterials();
        }

        private static void InitMaterials()
        {
            if (MatCaravanOn == null && CaravanOn != null)
            {
                MatCaravanOn = MaterialPool.MatFrom(
                    CaravanOn,
                    ShaderDatabase.WorldOverlayTransparentLit,
                    Color.white,
                    WorldMaterials.WorldObjectRenderQueue);
            }
            if (MatCaravanOff == null && CaravanOff != null)
            {
                MatCaravanOff = MaterialPool.MatFrom(
                    CaravanOff,
                    ShaderDatabase.WorldOverlayTransparentLit,
                    Color.white,
                    WorldMaterials.WorldObjectRenderQueue);
            }
        }

        public override Material Material
        {
            get
            {
                if (MatCaravanOn == null) InitMaterials();
                return IsOnline ? MatCaravanOn : MatCaravanOff;
            }
        }

        public override Texture2D ExpandingIcon => IsOnline ? CaravanOnExpanding : CaravanOffExpanding;

        public override string ExpandingIconName => IsOnline ? "CaravanOnExpanding" : "CaravanOffExpanding";
        #endregion
    }
}