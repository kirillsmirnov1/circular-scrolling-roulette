using UnityEngine;

namespace CircularScrollingRoulette.Bank
{
    public class SpriteArrayBank : RouletteBank
    {
#pragma warning disable 0649
        [SerializeField] private Sprite[] sprites;
#pragma warning restore 0649
        
        public override object GetRouletteContent(int index) 
            => sprites[index];

        public override int GetRouletteLength() 
            => sprites.Length;
    }
}