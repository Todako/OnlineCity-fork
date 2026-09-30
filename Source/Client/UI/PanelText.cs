using OCUnion;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text;

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

        // Швидкий безблокувальний кеш вимірювання слів для усунення нативних викликів Unity Text.CalcSize
        private static readonly ConcurrentDictionary<string, Vector2> WordSizeCache =
            new ConcurrentDictionary<string, Vector2>(StringComparer.Ordinal);

        private static Vector2 GetWordSizeCached(string word)
        {
            if (string.IsNullOrEmpty(word)) return Vector2.zero;

            if (WordSizeCache.TryGetValue(word, out var size))
            {
                return size;
            }

            var calculated = Text.CalcSize(word);

            if (WordSizeCache.Count > 2000)
            {
                WordSizeCache.Clear();
            }

            WordSizeCache[word] = calculated;
            return calculated;
        }

        /// <summary>
        /// Структура ключа кешу без виділення пам'яті в купі (Zero GC Allocation Key).
        /// </summary>
        private readonly struct PanelCacheKey : IEquatable<PanelCacheKey>
        {
            public readonly int X;
            public readonly int Y;
            public readonly int Width;
            public readonly int Height;
            public readonly int DynamicHeight;
            public readonly int TextHash;
            public readonly string Text;

            public PanelCacheKey(Rect rect, float dynamicHeight, string text)
            {
                X = (int)rect.x;
                Y = (int)rect.y;
                Width = (int)rect.width;
                Height = (int)rect.height;
                DynamicHeight = (int)dynamicHeight;
                Text = text ?? string.Empty;
                TextHash = text != null ? StringComparer.Ordinal.GetHashCode(text) : 0;
            }

            public bool Equals(PanelCacheKey other)
            {
                return X == other.X
                    && Y == other.Y
                    && Width == other.Width
                    && Height == other.Height
                    && DynamicHeight == other.DynamicHeight
                    && TextHash == other.TextHash
                    && string.Equals(Text, other.Text, StringComparison.Ordinal);
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

        private DateTime FirstCalcDrow = DateTime.MinValue;

        [ThreadStatic]
        private static List<Pair<string, string>> t_attrBuffer;

        private static List<Pair<string, string>> GetAttrBuffer()
        {
            if (t_attrBuffer == null) t_attrBuffer = new List<Pair<string, string>>(8);
            else t_attrBuffer.Clear();
            return t_attrBuffer;
        }

        /// <summary>
        /// Головна функція відмальовки компонента.
        /// </summary>
        public float Drow(Rect inRect, float dynamicHeight = 0)
        {
            if (string.IsNullOrEmpty(PrintText) || inRect.width < 1f || inRect.height < 1f)
                return 0f;

            // Очищення кешу лише за умови суттєвого переповнення (запобігає щохвилинним просіданням FPS)
            if (Optimization.Count > 300)
            {
                Optimization.Clear();
            }

            var key = new PanelCacheKey(inRect, dynamicHeight, PrintText);
            if (!Optimization.TryGetValue(key, out var res))
            {
                res = CalcDrow(inRect, dynamicHeight);
                Optimization[key] = res;
            }

            // Швидке послідовне виконання скомпільованого дерева команд
            var act = res.Tree;
            while (act != null)
            {
                act.Act();
                act = act.Next;
            }

            return res.Height;
        }

        private static string ResolveLanguageInjection(string k)
        {
            var activeLang = LanguageDatabase.activeLanguage;
            if (activeLang?.defInjections == null) return k;

            var packages = activeLang.defInjections;
            for (int i = 0; i < packages.Count; i++)
            {
                var pkg = packages[i];
                if (pkg?.injections != null && pkg.injections.TryGetValue(k, out var inj) && !string.IsNullOrEmpty(inj?.injection))
                {
                    return inj.injection;
                }
            }

            for (int i = 0; i < packages.Count; i++)
            {
                var pkg = packages[i];
                if (pkg?.injections == null) continue;
                foreach (var kvp in pkg.injections)
                {
                    if (string.Equals(kvp.Key, k, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(kvp.Value?.injection))
                    {
                        return kvp.Value.injection;
                    }
                }
            }

            return k;
        }

        private PanelCacheValue CalcDrow(Rect inRect, float dynamicHeight = 0)
        {
            if (FirstCalcDrow == DateTime.MinValue) FirstCalcDrow = DateTime.UtcNow;

            var prevFont = Text.Font;
            Text.Font = GameFont.Small;

            try
            {
                ActionTree startAction = new ATStart();
                ActionTree currentAction = startAction;

                float iconHeightDefault = TextHeight;

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

                // Локалізація тегів <l>
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
                            tr = LanguageInjections.GetOrAdd(tr, ResolveLanguageInjection) ?? tr;
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

                                var attrs = ParseAttributes(word);
                                for (int a = 0; a < attrs.Count; a++)
                                {
                                    var arg = attrs[a];
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

                                if (!string.IsNullOrEmpty(name))
                                {
                                    if (!Btns.TryGetValue(name, out tagBtnAct) && !GlobalBtns.TryGetValue(name, out tagBtnAct))
                                    {
                                        if (!string.IsNullOrEmpty(className))
                                        {
                                            tagBtnAct = TagBtn.GetByClass(className, d, tagBtnArg);
                                            Btns[name] = tagBtnAct;
                                        }
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

                                var attrs = ParseAttributes(word);
                                for (int a = 0; a < attrs.Count; a++)
                                {
                                    var agr = attrs[a];
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
                                    staticImage = getIcon == null ? icon : null,
                                    dynamicImage = getIcon
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

                        string testWord = currentWord.Length > 0 ? (currentWord + word) : word;
                        if (testWord.IndexOf('\n') >= 0)
                        {
                            testWord = testWord.Replace("\n", "");
                        }
                        var size = GetWordSizeCached(testWord);

                        bool concat = curX + size.x <= width || (currentWord.Length == 0 && curX == 0);
                        if (concat)
                        {
                            currentWord += word;
                            currentWordSize = size;
                        }

                        bool hasTrailingNewLine = currentWord.Length > 0 && currentWord[currentWord.Length - 1] == '\n';
                        bool newLine = curX + size.x > width || hasTrailingNewLine || lastLoop;
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
                            currentWord = word;
                            string cleanWord = word.IndexOf('\n') >= 0 ? word.Replace("\n", "") : word;
                            currentWordSize = GetWordSizeCached(cleanWord);

                            newLine = currentWord.Length > 0 && currentWord[currentWord.Length - 1] == '\n';
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
            finally
            {
                Text.Font = prevFont;
            }
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
                if (tagBtnAct == null) return;

                bool isOver = Mouse.IsOver(tagRect);
                if (isOver)
                {
                    if (tagBtnAct.HighlightIsOver) Widgets.DrawHighlight(tagRect);
                    tagBtnAct.ActionIsOver?.Invoke(tagBtnArg);

                    if (!string.IsNullOrEmpty(tagBtnAct.Tooltip))
                    {
                        TooltipHandler.TipRegion(tagRect, tagBtnAct.Tooltip);
                    }
                }
                if (Widgets.ButtonInvisible(tagRect))
                {
                    tagBtnAct.ActionClick?.Invoke(tagBtnArg);
                }
            }
        }

        private class ATDrawTexture : ActionTree
        {
            public Rect position;
            public Texture2D staticImage;
            public Func<Texture2D> dynamicImage;

            public override void Act()
            {
                var tex = staticImage ?? dynamicImage?.Invoke();
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

        private static List<Pair<string, string>> ParseAttributes(string fullTag)
        {
            var result = GetAttrBuffer();
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

                int eqIndex = fullTag.IndexOf('=', tokenStart);
                if (eqIndex < 0 || eqIndex >= i)
                {
                    string key = fullTag.Substring(tokenStart, tokenLen);
                    result.Add(new Pair<string, string>(key, string.Empty));
                }
                else
                {
                    string key = fullTag.Substring(tokenStart, eqIndex - tokenStart);
                    int valStart = eqIndex + 1;
                    int valLen = i - valStart;
                    string val = fullTag.Substring(valStart, valLen);

                    if (val.Length >= 2 && ((val[0] == '"' && val[val.Length - 1] == '"') || (val[0] == '\'' && val[val.Length - 1] == '\'')))
                    {
                        val = val.Substring(1, val.Length - 2);
                    }
                    result.Add(new Pair<string, string>(key, val));
                }
            }

            return result;
        }

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