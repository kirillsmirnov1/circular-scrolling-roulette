using UnityEngine;
using UnityEngine.UI;

namespace CircularScrollingRoulette.Entry.Content
{
    [RequireComponent(typeof(Image))]
    public class RouletteEntryImageContent : RouletteEntryContent
    {
        private Image _image;

        private void Awake() => _image = GetComponent<Image>();

        public override void SetContent(object content) 
            => _image.sprite = (Sprite) content;

        public override object GetContent() 
            => _image.sprite;
    }
}