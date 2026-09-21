using Model;
using OCUnion;
using RimWorld;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using Verse;

namespace RimWorldOnlineCity.UI
{
    /// <summary>
    /// Високопродуктивний компонент рендерингу розміченого тексту (RichText),
    /// інтерактивних кнопок (<btn>), картинок (<img />) та локалізованих описів (<l>).
    /// </summary>
    public class PanelText : DialogControlBase
    {
        public string PrintText { get; set; }

        public static Dictionary<string, TagBtn> GlobalBtns { get; set; } = new Dictionary<string, TagBtn>();
        public Dictionary<string, TagBtn> Btns { get; set; } = new Dictionary<string, TagBtn>();
        public static Dictionary<string, Texture2D> GlobalImgs { get; set; } = new Dictionary<string, Texture2D>();
        public Dictionary<string, Texture2D> Imgs { get; set; } = new Dictionary<string, Texture2D>();

        public static ConcurrentDictionary<string, string> LanguageInjections { get; set; } = new ConcurrentDictionary<string, string>();

        /// <summary>
        /// Структура ключа кешу без виділення пам'яті в купі (Zero GC Allocation Key).
        /// Замінює важку конкатенацію рядків на кожному виклику OnGUI.
        /// </summary>
        private struct PanelCacheKey : IEquatable<PanelCacheKey>
        {
            public readonly int X;
            public readonly int Y;
            public readonly int Width;
            public readonly int Height;
            public readonly int DynamicHeight;
            public readonly int TextHash;

            public PanelCacheKey(Rect rect, float dynamicHeight, string text)
            {
                X = (int)rect.x;
                Y = (int)rect.y;
                Width = (int)rect.width;
                Height = (int)rect.height;
                DynamicHeight = (int)dynamicHeight;
                TextHash = text != null ? text.GetHashCode() : 0;
            }

            public bool Equals(PanelCacheKey other)
            {
                return X == other.X && Y == other.Y && Width == other.Width && Height == other.Height
                    && DynamicHeight == other.DynamicHeight && TextHash == other.TextHash;
            }

            public override bool Equals(object obj) => obj is PanelCacheKey other && Equals(other);

            public override int GetHashCode()
            {
                unchecked
                {
                    int hash = 17;
                    hash = hash * 31 + X;
                    hash = hash * 31 + Y;
                    hash = hash * 31 + Width;
                    hash = hash * 31 + Height;
                    hash = hash * 31 + DynamicHeight;
                    hash = hash * 31 + TextHash;
                    return hash;
                }
            }
        }

        private class PanelCacheValue
        {
            public ActionTree Tree;
            public float Height;
        }

        private static readonly ConcurrentDictionary<PanelCacheKey, PanelCacheValue> Optimization =
            new ConcurrentDictionary<PanelCacheKey, PanelCacheValue>();
        private static DateTime OptimizationTime;

        private DateTime FirstCalcDrow = DateTime.MinValue;

        /// <summary>
        /// Головна функція відмальовки компонента.
        /// ОПТИМІЗАЦІЯ: миттєве виконання дерева команд ActionTree без перерахунку переносу слів.
        /// </summary>
        public float Drow(Rect inRect, float dynamicHeight = 0)
        {
            if (string.IsNullOrEmpty(PrintText) || inRect.width < 1f || inRect.height < 1f)
                return 0f;

            // Періодичне очищення кешу раз на 60 секунд або при переповненні
            if ((DateTime.UtcNow - OptimizationTime).TotalSeconds > 60 || Optimization.Count > 150)
            {
                OptimizationTime = DateTime.UtcNow;
                Optimization.Clear();
            }

            var key = new PanelCacheKey(inRect, dynamicHeight, PrintText);
            if (!Optimization.TryGetValue(key, out var res))
            {
                res = CalcDrow(inRect, dynamicHeight);
                Optimization[key] = res;
            }

            // Швидке послідовне виконання команд рендерингу
            var act = res.Tree;
            while (act != null)
            {
                act.Act();
                act = act.Next;
            }

            return res.Height;
        }

        /// <summary>
        /// Обчислення розташування слів, картинок та формування дерева команд малювання.
        /// </summary>
        private PanelCacheValue CalcDrow(Rect inRect, float dynamicHeight = 0)
        {
            if (FirstCalcDrow == DateTime.MinValue) FirstCalcDrow = DateTime.UtcNow;

            ActionTree startAction = new ATStart();
            ActionTree currentAction = startAction;

            float iconHeightDefault = TextHeight;

            // Нормалізація переносу рядків лише за необхідності
            string text = PrintText;
            if (text.IndexOf('\r') >= 0)
            {
                text = text.Replace("\r", "");
            }

            float width = inRect.width;
            float height = dynamicHeight <= 0 ? inRect.height : dynamicHeight;

            TagBtn tagBtnAct = null;
            string tagBtnArg = null;
            float tagBtnStartX = 0f;

            int totalChars = 0;
            string currentWord = "";
            Vector2 currentWordSize = new Vector2();
            float curY = 0f;
            float curX = 0f;
            float curHeight = 0f;

            Action printCurrent = () =>
            {
                if (currentWord.Length > 0)
                {
                    currentAction.Next = new ATLabel
                    {
                        rect = new Rect(inRect.x + curX, inRect.y + curY, currentWordSize.x, currentWordSize.y),
                        label = currentWord.IndexOf('\n') >= 0 ? currentWord.Replace("\n", "") : currentWord
                    };
                    currentAction = currentAction.Next;

                    curX += currentWordSize.x;
                    if (curHeight < currentWordSize.y) curHeight = currentWordSize.y;
                    currentWord = "";
                    currentWordSize = new Vector2();
                }
            };

            Action printBtnAct = () =>
            {
                if (tagBtnAct != null && tagBtnStartX != curX && curHeight > 0)
                {
                    var tagRect = new Rect(inRect.x + tagBtnStartX, inRect.y + curY, curX - tagBtnStartX, curHeight);
                    currentAction.Next = new ATBtnAct
                    {
                        tagRect = tagRect,
                        tagBtnArg = tagBtnArg,
                        tagBtnAct = tagBtnAct
                    };
                    currentAction = currentAction.Next;
                }
            };

            // ОПТИМІЗАЦІЯ: обробка тегів локалізації <l> через StringBuilder лише при їхній наявності
            if (text.IndexOf("<l>", StringComparison.Ordinal) >= 0)
            {
                var sb = new StringBuilder(text.Length + 32);
                int lastIndex = 0;
                while (true)
                {
                    int posB = text.IndexOf("<l>", lastIndex, StringComparison.Ordinal);
                    if (posB < 0) break;
                    int posE = text.IndexOf("</l>", posB + 3, StringComparison.Ordinal);
                    if (posE < 0) break;

                    sb.Append(text, lastIndex, posB - lastIndex);
                    string sub = text.Substring(posB + 3, posE - posB - 3);
                    string tr = ChatController.ServerCharTranslate(sub, true);

                    if (tr.Contains("."))
                    {
                        tr = LanguageInjections.GetOrAdd(tr, k =>
                            LanguageDatabase.activeLanguage.defInjections
                                .Select(di => di.injections.FirstOrDefault(dii => dii.Value.path.Equals(k, StringComparison.OrdinalIgnoreCase)).Value?.injection)
                                .FirstOrDefault(dii => dii != null)
                        ) ?? tr;
                    }

                    sb.Append(tr.TranslateCache());
                    lastIndex = posE + 4;
                }

                if (lastIndex < text.Length)
                {
                    sb.Append(text, lastIndex, text.Length - lastIndex);
                }
                text = sb.ToString();
            }

            try
            {
                foreach (var word in ParceText(text))
                {
                    totalChars += word.Length;
                    bool lastLoop = totalChars == text.Length;

                    // Обробка тегів розмітки
                    if (word.Length > 1 && word[0] == '<')
                    {
                        if (word.StartsWith("<btn ", StringComparison.OrdinalIgnoreCase))
                        {
                            printCurrent();
                            tagBtnStartX = curX;
                            tagBtnAct = null;
                            tagBtnArg = null;
                            string name = null;
                            string className = "";
                            string d = "";

                            foreach (var arg in ParseAttributes(word))
                            {
                                if (string.IsNullOrEmpty(arg.Second))
                                {
                                    name = arg.First;
                                }
                                else if (arg.First.Equals("name", StringComparison.OrdinalIgnoreCase) || arg.First.Equals("act", StringComparison.OrdinalIgnoreCase))
                                {
                                    name = arg.Second;
                                }
                                else if (arg.First.Equals("arg", StringComparison.OrdinalIgnoreCase))
                                {
                                    tagBtnArg = arg.Second;
                                }
                                else if (arg.First.Equals("class", StringComparison.OrdinalIgnoreCase))
                                {
                                    className = arg.Second;
                                }
                                else if (arg.First.Equals("d", StringComparison.OrdinalIgnoreCase))
                                {
                                    d = arg.Second;
                                }
                            }

                            if (!Btns.TryGetValue(name, out tagBtnAct) && !GlobalBtns.TryGetValue(name, out tagBtnAct))
                            {
                                if (!string.IsNullOrEmpty(className))
                                {
                                    tagBtnAct = TagBtn.GetByClass(className, d, tagBtnArg);
                                    Btns[name] = tagBtnAct;
                                }
                            }

                            continue;
                        }
                        else if (word.StartsWith("</btn", StringComparison.OrdinalIgnoreCase))
                        {
                            printCurrent();
                            printBtnAct();
                            tagBtnAct = null;
                            tagBtnArg = null;
                            continue;
                        }
                        else if (word.StartsWith("<img ", StringComparison.OrdinalIgnoreCase))
                        {
                            printCurrent();

                            Func<Texture2D> getIcon = null;
                            Texture2D icon = null;
                            string name = "";
                            int h = 0;
                            int w = 0;

                            foreach (var agr in ParseAttributes(word))
                            {
                                if (string.IsNullOrEmpty(agr.Second))
                                {
                                    name = agr.First;
                                }
                                else if (agr.First.Equals("name", StringComparison.OrdinalIgnoreCase))
                                {
                                    name = agr.Second;
                                }
                                else if (agr.First.Equals("defName", StringComparison.OrdinalIgnoreCase))
                                {
                                    icon = GeneralTexture.Get.GetDefTexture(agr.Second);
                                }
                                else if (agr.First.Equals("height", StringComparison.OrdinalIgnoreCase))
                                {
                                    int.TryParse(agr.Second, out h);
                                }
                                else if (agr.First.Equals("width", StringComparison.OrdinalIgnoreCase))
                                {
                                    int.TryParse(agr.Second, out w);
                                }
                            }

                            if (icon == null)
                            {
                                if (string.IsNullOrWhiteSpace(name)) continue;
                                if (name.StartsWith("pl_") && name.Length > 4)
                                {
                                    getIcon = () => GeneralTexture.Get.ByName(name);
                                    icon = getIcon();
                                }
                                else if (!Imgs.TryGetValue(name, out icon) && !GlobalImgs.TryGetValue(name, out icon))
                                {
                                    try
                                    {
                                        icon = ContentFinder<Texture2D>.Get(name, false);
                                    }
                                    catch
                                    {
                                        icon = null;
                                    }
                                    if (icon != null) GlobalImgs[name] = icon;
                                }
                            }
                            if (icon == null) continue;

                            float iconHeight = h > 0 ? h : iconHeightDefault;
                            float iconWidth = w > 0 ? w : icon.width * iconHeight / icon.height;

                            // Перенос на новий рядок, якщо іконка не вміщується
                            if (curX > 0 && curX + iconWidth > width)
                            {
                                printBtnAct();

                                tagBtnStartX = 0;
                                curX = 0;
                                curY += curHeight;
                                curHeight = 0f;

                                if (curY >= height)
                                {
                                    return new PanelCacheValue { Tree = startAction, Height = curY };
                                }
                            }

                            currentAction.Next = new ATDrawTexture
                            {
                                position = new Rect(inRect.x + curX, inRect.y + curY, iconWidth, iconHeight),
                                image = getIcon ?? (() => icon)
                            };
                            currentAction = currentAction.Next;

                            curX += iconWidth;
                            if (curHeight < iconHeight) curHeight = iconHeight;

                            if (lastLoop)
                            {
                                printBtnAct();
                                tagBtnStartX = 0;
                                curX = 0;
                                curY += curHeight;
                                curHeight = 0f;
                            }
                            continue;
                        }
                    }

                    // Розрахунок розміру слова та обробка переносу
                    string testWord = (currentWord + word).Replace("\n", "");
                    var size = Text.CalcSize(testWord);

                    bool concat = curX + size.x <= width || (currentWord == "" && curX == 0);
                    if (concat)
                    {
                        currentWord += word;
                        currentWordSize = size;
                    }

                    bool newLine = curX + size.x > width || currentWord[currentWord.Length - 1] == '\n' || lastLoop;
                    if (newLine)
                    {
                        printCurrent();
                        printBtnAct();

                        tagBtnStartX = 0;
                        curX = 0;
                        curY += curHeight;
                        curHeight = 0f;

                        if (lastLoop)
                        {
                            printCurrent();
                            printBtnAct();
                        }

                        if (curY >= height)
                        {
                            return new PanelCacheValue { Tree = startAction, Height = curY };
                        }
                    }

                    if (!concat)
                    {
                        currentWord += word;
                        currentWordSize = Text.CalcSize(currentWord.Replace("\n", ""));

                        newLine = currentWord[currentWord.Length - 1] == '\n';
                        if (newLine) printCurrent();

                        if (newLine)
                        {
                            printBtnAct();

                            tagBtnStartX = 0;
                            curX = 0;
                            curY += curHeight;
                            curHeight = 0f;

                            if (lastLoop)
                            {
                                printCurrent();
                                printBtnAct();
                            }

                            if (curY >= height)
                            {
                                return new PanelCacheValue { Tree = startAction, Height = curY };
                            }
                        }
                    }
                }
            }
            finally
            {
                currentAction.Next = new ATFinish();
            }

            return new PanelCacheValue { Tree = startAction, Height = curY };
        }

        #region Дерево команд рендерингу (ActionTree)

        private abstract class ActionTree
        {
            public ActionTree Next;
            public abstract void Act();
        }

        private class ATStart : ActionTree
        {
            public override void Act()
            {
                Text.Font = GameFont.Small;
                GUI.skin.textField.wordWrap = false;
            }
        }

        private class ATLabel : ActionTree
        {
            public Rect rect;
            public string label;
            public override void Act()
            {
                Widgets.Label(rect, label);
            }
        }

        private class ATBtnAct : ActionTree
        {
            public Rect tagRect;
            public string tagBtnArg;
            public TagBtn tagBtnAct;

            public override void Act()
            {
                if (Mouse.IsOver(tagRect))
                {
                    if (tagBtnAct.HighlightIsOver) Widgets.DrawHighlight(tagRect);
                    tagBtnAct.ActionIsOver?.Invoke(tagBtnArg);
                }
                if (Widgets.ButtonInvisible(tagRect))
                {
                    tagBtnAct.ActionClick?.Invoke(tagBtnArg);
                }
                if (!string.IsNullOrEmpty(tagBtnAct.Tooltip))
                {
                    TooltipHandler.TipRegion(tagRect, tagBtnAct.Tooltip);
                }
            }
        }

        private class ATDrawTexture : ActionTree
        {
            public Rect position;
            public Func<Texture2D> image;

            public override void Act()
            {
                var tex = image?.Invoke();
                if (tex != null)
                {
                    GUI.DrawTexture(position, tex);
                }
            }
        }

        private class ATFinish : ActionTree
        {
            public override void Act()
            {
                Text.Font = GameFont.Tiny;
            }
        }

        #endregion

        /// <summary>
        /// Швидкий парсер атрибутів тегу без створення проміжних масивів від Split().
        /// </summary>
        private static List<Pair<string, string>> ParseAttributes(string fullTag)
        {
            var result = new List<Pair<string, string>>();
            int si = fullTag.IndexOf(' ');
            if (si < 0) return result;

            int end = fullTag.Length - 1;
            while (end > si && (fullTag[end] == '>' || fullTag[end] == '/' || char.IsWhiteSpace(fullTag[end])))
            {
                end--;
            }

            if (end <= si) return result;

            int i = si + 1;
            while (i <= end)
            {
                while (i <= end && char.IsWhiteSpace(fullTag[i])) i++;
                if (i > end) break;

                int tokenStart = i;
                while (i <= end && !char.IsWhiteSpace(fullTag[i])) i++;
                int tokenLen = i - tokenStart;

                string token = fullTag.Substring(tokenStart, tokenLen);
                int eqIndex = token.IndexOf('=');
                if (eqIndex < 0)
                {
                    result.Add(new Pair<string, string>(token, string.Empty));
                }
                else
                {
                    string key = token.Substring(0, eqIndex);
                    string val = token.Substring(eqIndex + 1);
                    if (val.Length >= 2 && ((val[0] == '"' && val[val.Length - 1] == '"') || (val[0] == '\'' && val[val.Length - 1] == '\'')))
                    {
                        val = val.Substring(1, val.Length - 2);
                    }
                    result.Add(new Pair<string, string>(key, val));
                }
            }

            return result;
        }

        /// <summary>
        /// Розбиває вхідний рядок на окремі токени та слова.
        /// </summary>
        private IEnumerable<string> ParceText(string text)
        {
            int index = 0;
            int iNewLine = -1;
            int iSpace = -1;
            int iTag = -1;

            while (index < text.Length)
            {
                if (text[index] == '<')
                {
                    int p1 = text.IndexOf('>', index);
                    if (p1 < 0)
                    {
                        yield return "<";
                        index++;
                        continue;
                    }

                    yield return text.Substring(index, p1 - index + 1);
                    index = p1 + 1;
                    continue;
                }

                if (iNewLine < index) iNewLine = text.IndexOf('\n', index);
                if (iSpace < index) iSpace = text.IndexOf(' ', index);
                if (iTag < index) iTag = text.IndexOf('<', index);

                int iNext;
                if (iNewLine >= 0 && (uint)iNewLine <= (uint)iSpace && (uint)iNewLine <= (uint)iTag)
                {
                    iNext = iNewLine + 1;
                }
                else if (iSpace >= 0 && (uint)iSpace <= (uint)iNewLine && (uint)iSpace <= (uint)iTag)
                {
                    iNext = iSpace + 1;
                }
                else if (iTag >= 0)
                {
                    iNext = iTag;
                }
                else
                {
                    iNext = text.Length;
                }

                yield return text.Substring(index, iNext - index);
                index = iNext;
            }
        }
    }
}