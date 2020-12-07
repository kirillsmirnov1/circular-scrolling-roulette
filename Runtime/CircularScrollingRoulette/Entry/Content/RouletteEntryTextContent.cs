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
        protected TextMeshProUGUI Text;

        protected virtual void Awake()
        {
            Text = GetComponent<TextMeshProUGUI>();
        }

        public override void SetContent(object content)
        {
            Text.text = content.ToString();
        }

        public override object GetContent() => Text.text;
    }
}
