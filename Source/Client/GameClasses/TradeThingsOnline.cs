using Model;
using OCUnion;
using RimWorld.Planet;
using RimWorldOnlineCity.UI;
using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using Verse;

namespace RimWorldOnlineCity
{
    /// <summary>
    /// Червоне яблуко – торговий склад речей гравця на карті планети.
    /// </summary>
    [StaticConstructorOnStartup]
    public class TradeThingsOnline : WorldObjectBaseOnline
    {
        public override IModelPlace Place => TradeThings;

        public TradeThingStorage TradeThings { get; set; } = new TradeThingStorage();

        private static string _cachedLabel;
        public override string Label
        {
            get
            {
                if (_cachedLabel == null)
                {
                    _cachedLabel = "OC_TradeThingsOnline_Storage".Translate().ToString();
                }
                return _cachedLabel;
            }
        }

        // ОПТИМІЗАЦІЯ: кешування рядка інспектора (усуває переклад 4 тегів та конкатенацію щокадру OnGUI)
        private int _lastInspectThingCount = -1;
        private string _cachedInspectString;

        public override string GetInspectString()
        {
            int currentCount = TradeThings?.Things?.Count ?? 0;
            if (_lastInspectThingCount != currentCount || _cachedInspectString == null)
            {
                _lastInspectThingCount = currentCount;
                _cachedInspectString = Label + Environment.NewLine
                    + "OC_TradeThingsOnline_ThingTypes".Translate(currentCount) + Environment.NewLine
                    + "OC_TradeThingsOnline_Info".Translate()
                    + " " + "OCity_Dialog_Exchenge_Move".Translate();
            }
            return _cachedInspectString;
        }

        public override string GetDescription()
        {
            var sb = new StringBuilder(256);
            sb.Append(base.GetDescription());
            sb.AppendLine();
            sb.AppendLine();
            sb.Append(Label);
            if (TradeThings?.Things != null && TradeThings.Things.Count > 0)
            {
                sb.Append(TradeThings.Things.ToStringLabel());
            }
            return sb.ToString();
        }

        public override string ToString()
        {
            return TradeThings?.Things?.ToStringLabel() ?? string.Empty;
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
        private static Material MatTradeThingsOnlineIcon;
        private static readonly Texture2D TradeThingsOnlineIcon = ContentFinder<Texture2D>.Get("Apple");

        public override Material Material
        {
            get
            {
                if (MatTradeThingsOnlineIcon == null)
                {
                    MatTradeThingsOnlineIcon = MaterialPool.MatFrom(
                        TradeThingsOnlineIcon,
                        ShaderDatabase.WorldOverlayTransparentLit,
                        Color.white,
                        WorldMaterials.WorldObjectRenderQueue);
                }
                return MatTradeThingsOnlineIcon;
            }
        }

        public override Texture2D ExpandingIcon => TradeThingsOnlineIcon;

        public override string ExpandingIconName => "Apple";
        #endregion
    }
}