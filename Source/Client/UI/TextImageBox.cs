using UnityEngine;
using Verse;

namespace RimWorldOnlineCity.UI
{
    /// <summary>
    /// Контейнер відображення форматованого тексту з зображеннями та підтримкою прокручування.
    /// ОПТИМІЗАЦІЯ: кешування висоти та ширини виключає повторні виклики перерахунку розмірів щокадру.
    /// </summary>
    public class TextImageBox : DialogControlBase
    {
        public string Text
        {
            get => Panel.PrintText;
            set => Panel.PrintText = value;
        }

        public Vector2 ScrollPosition = new Vector2();
        private readonly PanelText Panel;

        private string _textLastDrow;
        private float _widthLastDrow;
        private float _heightLastDrow;
        private bool _scrollToDownLastDrow;

        public TextImageBox()
        {
            Panel = new PanelText();
            Text = string.Empty;
        }

        /// <summary>
        /// Відмальовує текстову панель із підтримкою плавного скролу.
        /// </summary>
        public void Drow(Rect chatAreaOuter, bool scrollToDown = false)
        {
            var chatAreaInner = new Rect(0, 0, chatAreaOuter.width - WidthScrollLine, 0);
            if (chatAreaInner.width <= 0) return;

            float calcHeight = 0f;
            string currentText = Panel.PrintText;

            // Швидка перевірка: чи збігається текст за посиланням або значенням, і чи не змінилася ширина
            bool isSameText = ReferenceEquals(_textLastDrow, currentText) || _textLastDrow == currentText;
            bool isSameWidth = Mathf.Abs(_widthLastDrow - chatAreaInner.width) < 0.1f;

            if (isSameText && isSameWidth && _heightLastDrow > 0)
            {
                // Якщо розмір уже розраховано для цього тексту та ширини — використовуємо його без перерахунку
                chatAreaInner.height = _heightLastDrow;
                if (_scrollToDownLastDrow)
                {
                    scrollToDown = true;
                    _scrollToDownLastDrow = false;
                }
            }
            else
            {
                // Якщо розмір невідомий — розраховуємо з запасом і плануємо автоскрол на наступний кадр
                calcHeight = 10000f;
                chatAreaInner.height = chatAreaOuter.height;
                _scrollToDownLastDrow = scrollToDown;
            }

            if (scrollToDown && chatAreaInner.height > chatAreaOuter.height)
            {
                ScrollPosition.y = chatAreaInner.height - chatAreaOuter.height;
            }

            ScrollPosition = GUI.BeginScrollView(chatAreaOuter, ScrollPosition, chatAreaInner);
            GUILayout.BeginArea(chatAreaInner);

            _heightLastDrow = Panel.Drow(chatAreaInner, calcHeight);
            _widthLastDrow = chatAreaInner.width;
            _textLastDrow = currentText;

            GUILayout.EndArea();
            GUI.EndScrollView();
        }
    }
}