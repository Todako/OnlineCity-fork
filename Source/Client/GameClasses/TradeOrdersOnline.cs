using Model;
using OCUnion;
using RimWorld.Planet;
using RimWorldOnlineCity.UI;
using System;
using System.Collections.Generic;
using System.Text;
using Transfer;
using UnityEngine;
using Verse;

namespace RimWorldOnlineCity
{
    /// <summary>
    /// Зелене яблуко – список активних торгових угод на біржі.
    /// </summary>
    [StaticConstructorOnStartup]
    public class TradeOrdersOnline : WorldObjectBaseOnline
    {
        public override IModelPlace Place => TradeOrders != null && TradeOrders.Count > 0 ? TradeOrders[0] : null;

        /// <summary>
        /// Список ордерів для біржі.
        /// </summary>
        public List<TradeOrderShort> TradeOrders { get; set; } = new List<TradeOrderShort>();

        private static string _cachedLabel;
        public override string Label
        {
            get
            {
                if (_cachedLabel == null)
                {
                    _cachedLabel = "OC_TradeOrdersOnline_Orders".Translate().ToString();
                }
                return _cachedLabel;
            }
        }

        // Кеш інспектора для усунення перерахунку щокадру OnGUI
        private int _lastInspectOrderCount = -1;
        private string _cachedInspectString;

        public override string GetInspectString()
        {
            int currentCount = TradeOrders?.Count ?? 0;
            if (_lastInspectOrderCount != currentCount || _cachedInspectString == null)
            {
                _lastInspectOrderCount = currentCount;
                _cachedInspectString = Label + " " + _lastInspectOrderCount;
            }
            return _cachedInspectString;
        }

        /// <summary>
        /// Формування повного опису біржі.
        /// ОПТИМІЗАЦІЯ: ліквідовано O(N^2) LINQ Aggregate на користь StringBuilder.
        /// </summary>
        public override string GetDescription()
        {
            var sb = new StringBuilder(256);
            sb.Append(base.GetDescription());
            sb.AppendLine();
            sb.AppendLine();
            sb.Append(Label);

            if (TradeOrders != null && TradeOrders.Count > 0)
            {
                if (TradeOrders[0] is TradeOrderShort)
                {
                    sb.AppendLine();
                    sb.Append("OC_TradeOrdersOnline_OpenForInfo".Translate());
                }
                else
                {
                    for (int i = 0; i < TradeOrders.Count; i++)
                    {
                        sb.AppendLine();
                        sb.Append(TradeOrders[i]);
                    }
                }
            }

            return sb.ToString();
        }

        public override string ToString()
        {
            if (TradeOrders == null || TradeOrders.Count == 0) return string.Empty;
            return string.Join(Environment.NewLine, TradeOrders);
        }

        private static string _cachedCommandLabel;
        public override IEnumerable<Gizmo> GetGizmos()
        {
            foreach (Gizmo gizmo in base.GetGizmos())
            {
                yield return gizmo;
            }

            if (_cachedCommandLabel == null)
            {
                _cachedCommandLabel = "OCity_Dialog_Exchenge_Trade_Orders".Translate().ToString();
            }

            var command_Action = new Command_Action
            {
                defaultLabel = _cachedCommandLabel,
                defaultDesc = _cachedCommandLabel,
                icon = GeneralTexture.TradeButtonIcon,
                action = delegate
                {
                    Find.WindowStack.Add(new Dialog_Exchenge(this));
                }
            };
            yield return command_Action;
        }

        #region Icons
        private static Material MatTradeOnlineIcon;
        private static readonly Texture2D TradeOnlineIcon = ContentFinder<Texture2D>.Get("GreenApple");

        public override Material Material
        {
            get
            {
                if (MatTradeOnlineIcon == null)
                {
                    MatTradeOnlineIcon = MaterialPool.MatFrom(
                        TradeOnlineIcon,
                        ShaderDatabase.WorldOverlayTransparentLit,
                        Color.white,
                        WorldMaterials.WorldObjectRenderQueue);
                }
                return MatTradeOnlineIcon;
            }
        }

        public override Texture2D ExpandingIcon => TradeOnlineIcon;

        public override string ExpandingIconName => "GreenApple";
        #endregion
    }
}