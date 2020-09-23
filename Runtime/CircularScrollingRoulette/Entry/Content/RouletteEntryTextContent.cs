using TMPro;
using UnityEngine;

namespace CircularScrollingRoulette.Entry.Content
{
    /// <summary>
    /// Roulette entry TMPro content
    /// </summary>
    [RequireComponent(typeof(TextMeshProUGUI))]
    public class RouletteEntryTextContent : RouletteEntryContent
    {
        private TextMeshProUGUI _text;

        private void Awake()
        {
            _text = GetComponent<TextMeshProUGUI>();
        }

        public override void SetContent(object content)
        {
            _text.text = (string)content;
        }

        public override object GetContent() => _text.text;
    }
}
