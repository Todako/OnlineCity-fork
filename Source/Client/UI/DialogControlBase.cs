using Verse;

namespace RimWorldOnlineCity.UI
{
    /// <summary>
    /// Базовий клас для елементів керування діалогових вікон OnlineCity.
    /// Забезпечує кешування стандартних розмірів шрифтів та ширини елементів інтерфейсу.
    /// </summary>
    public class DialogControlBase
    {
        public const float WidthScrollLine = 18f;

        private static float _cachedDefaultTextHeight = 0f;
        private float _textHeightValue = 0f;

        /// <summary>
        /// Висота рядка тексту для поточного елемента керування.
        /// ОПТИМІЗАЦІЯ: статичне кешування базового розміру шрифту (GameFont.Small)
        /// усуває повторні виклики Text.CalcSize() під час кожного кадру рендерингу GUI.
        /// </summary>
        public float TextHeight
        {
            get
            {
                if (_textHeightValue == 0f)
                {
                    if (_cachedDefaultTextHeight == 0f && Text.Font == GameFont.Small)
                    {
                        _cachedDefaultTextHeight = Text.CalcSize("HWDOA/|").y;
                        _textHeightValue = _cachedDefaultTextHeight;
                    }
                    else
                    {
                        _textHeightValue = Text.CalcSize("HWDOA/|").y;
                    }
                }
                return _textHeightValue;
            }
        }
    }
}