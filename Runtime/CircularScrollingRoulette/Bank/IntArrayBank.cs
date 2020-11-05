using UnityEngine;

namespace CircularScrollingRoulette.Bank
{
    public class IntArrayBank : RouletteBank
    {
#pragma warning disable 0649
        [SerializeField] private int[] data;
#pragma warning restore 0649
        
        public override object GetRouletteContent(int index) => data[index];

        public override int GetRouletteLength() => data.Length;
    }
}