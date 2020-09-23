using UnityEngine;

namespace CircularScrollingRoulette.Bank
{
	/// <summary>
	/// The base class of the roulette content container.
	/// Create the individual RouletteBank by inheriting this class
	/// </summary>
	public abstract class RouletteBank: MonoBehaviour
	{
		public abstract object GetRouletteContent(int index);
		public abstract int GetRouletteLength();
	}
}
