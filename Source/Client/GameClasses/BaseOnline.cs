using OCUnion;
using OCUnion.Transfer.Model;
using RimWorld.Planet;
using RimWorldOnlineCity.UI;
using System;
using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace RimWorldOnlineCity
{
    [StaticConstructorOnStartup]
    public class BaseOnline : CaravanOnline
    {
        #region Icons
        // ОПТИМІЗАЦІЯ: спільні статичні матеріали для всіх баз гравців на карті світу
        private static Material MatColonyOn;
        private static Material MatColonyOff;

        private WealthTexture ColonyOnExpandingTexture;
        private WealthTexture ColonyOffExpandingTexture;
        private float _lastWealthLevel = -1f;

        private static readonly Texture2D ColonyOn = ContentFinder<Texture2D>.Get("ColonyOn");
        private static readonly Texture2D ColonyOff = ContentFinder<Texture2D>.Get("ColonyOff");
        private static readonly Texture2D ColonyOnExpanding = ContentFinder<Texture2D>.Get("ColonyOnExpanding");
        private static readonly Texture2D ColonyOffExpanding = ContentFinder<Texture2D>.Get("ColonyOffExpanding");

        private static readonly WealthTexture[] WealthTexturesOn;
        private static readonly WealthTexture[] WealthTexturesOff;
        private static readonly int[] WealthLevels = new int[] { 0, 25_000, 50_000, 100_000, 200_000, 300_000, 500_000, 1_000_000, 2_000_000 };

        static BaseOnline()
        {
            WealthTexturesOn = new WealthTexture[WealthLevels.Length];
            WealthTexturesOff = new WealthTexture[WealthLevels.Length];

            for (int i = 0; i < WealthLevels.Length; i++)
            {
                WealthTexturesOn[i] = new WealthTexture { Wealth = WealthLevels[i], TextureName = "ColonyOnExpanding" + i.ToString() };
                WealthTexturesOn[i].Texture = ContentFinder<Texture2D>.Get(WealthTexturesOn[i].TextureName);
                WealthTexturesOff[i] = new WealthTexture { Wealth = WealthLevels[i], TextureName = "ColonyOffExpanding" + i.ToString() };
                WealthTexturesOff[i].Texture = ContentFinder<Texture2D>.Get(WealthTexturesOff[i].TextureName);
            }
        }

        public override Material Material
        {
            get
            {
                if (IsOnline)
                {
                    if (MatColonyOn == null)
                    {
                        MatColonyOn = MaterialPool.MatFrom(
                            ColonyOn,
                            ShaderDatabase.WorldOverlayTransparentLit,
                            Color.white,
                            WorldMaterials.WorldObjectRenderQueue);
                    }
                    return MatColonyOn;
                }

                if (MatColonyOff == null)
                {
                    MatColonyOff = MaterialPool.MatFrom(
                        ColonyOff,
                        ShaderDatabase.WorldOverlayTransparentLit,
                        Color.white,
                        WorldMaterials.WorldObjectRenderQueue);
                }
                return MatColonyOff;
            }
        }

        // ОПТИМІЗАЦІЯ: кешування іконки багатства з автоматичним оновленням при зміні майна
        private void UpdateWealthTexturesIfNeeded()
        {
            float currentWealth = OnlineWObject?.MarketValueTotal ?? 0f;
            if (_lastWealthLevel != currentWealth || ColonyOnExpandingTexture == null)
            {
                _lastWealthLevel = currentWealth;
                ColonyOnExpandingTexture = GetMaterialByWealth(WealthTexturesOn, currentWealth);
                ColonyOffExpandingTexture = GetMaterialByWealth(WealthTexturesOff, currentWealth);
            }
        }

        public override Texture2D ExpandingIcon
        {
            get
            {
                UpdateWealthTexturesIfNeeded();
                return IsOnline ? ColonyOnExpandingTexture.Texture : ColonyOffExpandingTexture.Texture;
            }
        }

        public override string ExpandingIconName
        {
            get
            {
                UpdateWealthTexturesIfNeeded();
                return IsOnline ? ColonyOnExpandingTexture.TextureName : ColonyOffExpandingTexture.TextureName;
            }
        }

        private static WealthTexture GetMaterialByWealth(WealthTexture[] wealthTextures, float wealth)
        {
            for (int i = 1; i < WealthLevels.Length - 1; i++)
            {
                if (wealth < WealthLevels[i])
                {
                    return wealthTextures[i];
                }
            }
            return wealthTextures[WealthLevels.Length - 1];
        }
        #endregion

        public static readonly Texture2D BaseOnlineButtonIcon = ContentFinder<Texture2D>.Get("ShowLearningHelper");

        public Texture2D ImageBaseWhenOwnerOffline = null;

        public override IEnumerable<Gizmo> GetGizmos()
        {
            foreach (Gizmo gizmo in base.GetGizmos())
            {
                yield return gizmo;
            }

            var command_Action = new Command_Action
            {
                defaultLabel = "OC_Base_Interact".Translate(OnlinePlayerLogin),
                defaultDesc = "OC_Base_InteractWith".Translate(OnlineName, OnlinePlayerLogin),
                icon = BaseOnlineButtonIcon,
                action = delegate
                {
                    Find.WindowStack.Add(new Dialog_BaseOnlineButton(this));
                }
            };
            yield return command_Action;

            command_Action = GameUtils.CommandShowMap(this);
            if (command_Action != null) yield return command_Action;
        }
    }

    public class WealthTexture
    {
        public int Wealth;
        public Texture2D Texture;
        public string TextureName;
    }
}