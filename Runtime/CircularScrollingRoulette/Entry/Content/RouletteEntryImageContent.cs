using UnityEngine;
using UnityEngine.UI;

namespace CircularScrollingRoulette.Entry.Content
{
    [RequireComponent(typeof(Image))]
    public class RouletteEntryImageContent : RouletteEntryContent
    {
        protected Image Image;

        protected virtual void Awake() => Image = GetComponent<Image>();

        public override void SetContent(object content) 
            => Image.sprite = (Sprite) content;

        public override object GetContent() 
            => Image.sprite;
    }
}