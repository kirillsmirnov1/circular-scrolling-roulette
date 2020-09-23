using UnityEngine;

namespace CircularScrollingRoulette.Entry.Content
{
    /// <summary>
    /// Base class for content of roulette entry 
    /// </summary>
    public abstract class RouletteEntryContent : MonoBehaviour
    {
        public abstract void SetContent(object content);
        public abstract object GetContent();
    }
}