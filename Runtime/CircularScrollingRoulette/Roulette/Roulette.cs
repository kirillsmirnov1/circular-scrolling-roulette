using System;
using CircularScrollingRoulette.Bank;
using CircularScrollingRoulette.Entry;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace CircularScrollingRoulette.Roulette
{
	public interface IControlEventHandler:
		IBeginDragHandler, IDragHandler, IEndDragHandler, IScrollHandler
	{}

	[Serializable]
	public class RouletteEntryClickEvent : UnityEvent<int>
	{}

	/// <summary>
	/// Handle the controlling event and send the moving information to the entries it has
	/// </summary>
	public class Roulette : MonoBehaviour, IControlEventHandler
	{
		public enum RouletteType
		{
			Circular,
			Linear
		}

		public enum ControlMode
		{
			Drag,       // By the mouse pointer or finger
			Button,     // By the up/down button
			MouseWheel,  // By the mouse wheel
			DragAndButton 
		}

		public enum Direction
		{
			Vertical,
			Horizontal
		}

		/*========== Settings ==========*/
		[Header("Roulette mode")]
		public RouletteType rouletteType = RouletteType.Circular;
		public ControlMode controlMode = ControlMode.DragAndButton;
		public bool alignMiddle;
		public Direction direction = Direction.Vertical;

		[Header("Containers")]
		public RouletteBank rouletteBank;
		[Tooltip("Specify the centered content ID")]
		public int centeredContentId;
		[Tooltip("Prefab used to generate elements of roulette")]
		public GameObject entryPrefab;
		[Tooltip("Number of entries to be generated")]
		public int numberOfEntries;
		[HideInInspector]
		public RouletteEntry[] rouletteEntries;
		[Tooltip("The event handler for the onClick event of the roulette entry (containing Button component)" +
		         "\nThe handler function must have 1 int parameter for passing the content ID of the clicked entry.")]
		public RouletteEntryClickEvent onEntryClick;
		public Button[] controlButtons;

		[Header("Parameters")]
		[Tooltip("Set the distance between each RouletteEntry. The larger, the closer.")]
		public float entryGapFactor = 2.0f;
		[Tooltip("Set the sliding duration in frames. The larger, the longer.")]
		public int entrySlidingFrames = 35;
		[Tooltip(" Set the sliding speed. The larger, the quicker.")]
		[Range(0.0f, 1.0f)]
		public float entrySlidingSpeedFactor = 0.2f;
		[Tooltip("Set the scrolling roulette curving to left/right, or up/down in HORIZONTAL mode." +
		         "\nPositive: Curve to right (up); Negative: Curve to left (down).")]
		[Range(-1.0f, 1.0f)]
		public float rouletteCurvature = 0.3f;
		[Tooltip("Set this value to make the whole roulette not to sway to one side." +
		         "\nAdjust the horizontal position in the Vertical mode or the vertical position in the Horizontal mode." +
		         "\nThis value will be used in RouletteEntry.update[X/Y]Position().")]
		[Range(-1.0f, 1.0f)]
		public float positionAdjust = -0.7f;
		[Tooltip("Set the scale ratio of the center entry.")]
		public float centerEntryScaleRatio = 0.32f;
		/*===============================*/

		// The canvas plane which the scrolling roulette is at.
		protected Canvas _parentCanvas;

		// The constrains of position in the local space of the canvas plane.
		public Vector2 CanvasMaxPosL { get; private set; }
		public Vector2 UnitPosL { get; private set; }
		public Vector2 LowerBoundPosL { get; private set; }
		public Vector2 UpperBoundPosL { get; private set; }

		// Delegate functions
		private delegate void InputPositionHandlerDelegate(
			PointerEventData pointer, TouchPhase state);
		private InputPositionHandlerDelegate _inputPositionHandler;
		private delegate void ScrollHandlerDelegate(Vector2 scrollDelta);
		private ScrollHandlerDelegate _scrollHandler;

		// Input mouse/finger position in the local space of the roulette.
		private Vector3 _startInputPosL;
		private Vector3 _endInputPosL;
		private Vector3 _curFrameInputPosL;
		private Vector3 _deltaInputPosL;
		private int _numOfInputFrames;

		// Variables for moving rouletteEntries
		private int _slidingFramesLeft;
		private Vector3 _slidingDistance;     // The sliding distance for each frame
		private Vector3 _slidingDistanceLeft;
		/// <summary>
		/// The flag indicating that one of the entries need to be centered after the sliding
		/// </summary>
		private bool _needToAlignToCenter;

		// Variables for linear mode
		[HideInInspector]
		public int numOfUpperDisabledEntries;
		[HideInInspector]
		public int numOfLowerDisabledEntries;
		private int _maxNumOfDisabledEntries;
	
		// Variables for scaling
		[Header("Scaling")]
		[Tooltip("Make objects look like they disappear near the edges")]
		public bool scaleEdgeObjects = true;
		[Range(0f, 1f)]
		[Tooltip("At which percentage of path to anchor entry scale equals zero")]
		public float scaleShift = 0.8f;
		[HideInInspector]
		public float[] anchorsY = new float[4];
		private int _entriesCheckedForAnchor;
		private bool _rouletteSliding;

		[Header("Finish boost")]
		[SerializeField] private float minSlidingDistanceSqrMagnitude = 60000f;
		[SerializeField] private float baselineSlidingFactor = 0.02f;
		private bool _finishBoostActivated;
		
#pragma warning disable 0649
		[Header("Debug")] 
		[SerializeField] private bool logSlidingDistance;
#pragma warning restore 0649
		
		protected Action OnSlidingFinishedCallback;

		/// <summary>
		/// Notice: RouletteEntry will initialize its variables from here, so Roulette
		/// must be executed before RouletteEntry. You have to set the execution order in the inspector.
		/// </summary>
		protected virtual void Start()
		{
			InstantiateEntries();
			InitializePositionVars();
			InitializeInputFunction();
			InitializeEntryDependency();
			InitCallbacks();
			_maxNumOfDisabledEntries = rouletteEntries.Length / 2;
		}

		private void InitCallbacks()
		{
			OnSlidingFinishedCallback += () => centeredContentId = GetCenteredContentId();
		}

		private void InstantiateEntries()
		{
			if (gameObject == null)
			{
				Debug.LogError($"{gameObject.name}: No entry attached");
				return;
			}
		
			rouletteEntries = new RouletteEntry[numberOfEntries];
			for (int i = 0; i < numberOfEntries; i++)
			{
				rouletteEntries[i] = InstantiateEntry(entryPrefab, transform);
				rouletteEntries[i].transform.SetSiblingIndex(i);
			}
		}

		protected virtual RouletteEntry InstantiateEntry(GameObject prefab, Transform t)
		{
			return Instantiate(prefab, t).GetComponent<RouletteEntry>();
		}

		void InitializePositionVars()
		{
			/* The the reference of canvas plane */
			_parentCanvas = GetComponentInParent<Canvas>();

			/* Get the max position of canvas plane in the canvas space.
		 * Assume that the origin of the canvas space is at the center of canvas plane. */

			CanvasMaxPosL = GenerateCanvasMaxPosL();

			UnitPosL = CanvasMaxPosL / entryGapFactor;
			LowerBoundPosL = UnitPosL * (-1 * rouletteEntries.Length / 2 - 1);
			UpperBoundPosL = UnitPosL * (rouletteEntries.Length / 2 + 1);

			// If there are even number of RouletteEntries, narrow the boundary for 1 unitPos.
			if ((rouletteEntries.Length & 0x1) == 0) {
				LowerBoundPosL += UnitPosL / 2;
				UpperBoundPosL -= UnitPosL / 2;
			}
		}

		protected virtual Vector2 GenerateCanvasMaxPosL()
		{
			RectTransform rectTransform = _parentCanvas.GetComponent<RectTransform>();
			return new Vector2(rectTransform.rect.width / 2, rectTransform.rect.height / 2);
		}

		void InitializeEntryDependency()
		{
			// Set the entry ID according to the order in the container `rouletteEntries`
			for (int i = 0; i < rouletteEntries.Length; ++i)
				rouletteEntries[i].rouletteEntryId = i;

			// Set the neighbor entries
			for (int i = 0; i < rouletteEntries.Length; ++i) {
				rouletteEntries[i].lastRouletteEntry = rouletteEntries[(i - 1 >= 0) ? i - 1 : rouletteEntries.Length - 1];
				rouletteEntries[i].nextRouletteEntry = rouletteEntries[(i + 1 < rouletteEntries.Length) ? i + 1 : 0];
			}
		}
	
		/// <summary>
		/// Initialize the corresponding handlers for the selected controlling mode
		///
		/// The unused handler will be assigned a dummy function to
		/// prevent the handling of the event.
		/// </summary>
		void InitializeInputFunction()
		{
			switch (controlMode) {
				case ControlMode.Drag:
					_inputPositionHandler = DragPositionHandler;

					_scrollHandler = delegate { };
					foreach (Button button in controlButtons)
						button.gameObject.SetActive(false);
					break;

				case ControlMode.Button:
					_inputPositionHandler =
						delegate { };
					_scrollHandler = delegate { };
					break;
			
				case ControlMode.DragAndButton:
					_inputPositionHandler = DragPositionHandler;
					_scrollHandler = delegate { };
					break;

				case ControlMode.MouseWheel:
					_scrollHandler = ScrollDeltaHandler;

					_inputPositionHandler =
						delegate { };
					foreach (Button button in controlButtons)
						button.gameObject.SetActive(false);
					break;
			}
		}

		/* ====== Callback functions for the unity event system ====== */
		public void OnBeginDrag(PointerEventData pointer)
		{
			_inputPositionHandler(pointer, TouchPhase.Began);
		}

		public void OnDrag(PointerEventData pointer)
		{
			_inputPositionHandler(pointer, TouchPhase.Moved);
		}

		public void OnEndDrag(PointerEventData pointer)
		{
			_inputPositionHandler(pointer, TouchPhase.Ended);
		}

		public void OnScroll(PointerEventData pointer)
		{
			_scrollHandler(pointer.scrollDelta);
		}
	
		/// <summary>
		/// Move the roulette according to the dragging position and the dragging state
		/// </summary>
		void DragPositionHandler(PointerEventData pointer, TouchPhase state)
		{
			switch (state) {
				case TouchPhase.Began:
					_numOfInputFrames = 0;
					_startInputPosL = ScreenToCanvasSpace(pointer.position);
					_slidingFramesLeft = 0; // Make the roulette stop sliding
					break;

				case TouchPhase.Moved:
					++_numOfInputFrames;
					_deltaInputPosL = ScreenToCanvasSpace(pointer.delta);
					// Slide the roulette as long as the moving distance of the pointer
					_slidingDistanceLeft = _deltaInputPosL;
					_slidingFramesLeft = 1;
					break;

				case TouchPhase.Ended:
					_endInputPosL = ScreenToCanvasSpace(pointer.position);
					SetSlidingEffect();
					break;
			}
		}
	
		/// <summary>
		/// Scroll the roulette according to the delta of the mouse scrolling
		/// </summary>
		void ScrollDeltaHandler(Vector2 mouseScrollDelta)
		{
			switch (direction) {
				case Direction.Vertical:
					if (mouseScrollDelta.y > 0)
						MoveOneUnitUp();
					else if (mouseScrollDelta.y < 0)
						MoveOneUnitDown();
					break;

				case Direction.Horizontal:
					if (mouseScrollDelta.y > 0)
						MoveOneUnitDown();
					else if (mouseScrollDelta.y < 0)
						MoveOneUnitUp();
					break;
			}
		}
	
		/// <summary>
		/// Transform the coordinate from the screen space to the canvas space
		/// </summary>
		Vector3 ScreenToCanvasSpace(Vector3 position)
		{
			return position / _parentCanvas.scaleFactor;
		}


		/* ====== Movement functions ====== */
		/* Control the movement of rouletteEntries
	 */
		void Update()
		{
			if (_slidingFramesLeft > 0)
			{
				_rouletteSliding = true;
				if (rouletteType == RouletteType.Linear) {
					StopRouletteWhenReachEnd();
				}

				--_slidingFramesLeft;

				// Set sliding distance for this frame
				if (_slidingFramesLeft == 0) {
					if (_needToAlignToCenter) {
						_needToAlignToCenter = false;
						SetSlidingToCenter();
					} else {
						_slidingDistance = _slidingDistanceLeft;
					}
				}
				else
				{
					// Boost end of scrolling 
					if (_slidingDistanceLeft.sqrMagnitude <= minSlidingDistanceSqrMagnitude && !_finishBoostActivated)
					{
						_finishBoostActivated = true;

						_slidingFramesLeft = (int) (_slidingFramesLeft * entrySlidingSpeedFactor / baselineSlidingFactor);
						
						entrySlidingSpeedFactor = baselineSlidingFactor;
						if(logSlidingDistance) Debug.Log("Finish boost activated");
					}
					_slidingDistance = Vector3.Lerp(Vector3.zero, _slidingDistanceLeft,
						entrySlidingSpeedFactor);

					if (logSlidingDistance)
					{
						Debug.Log(_slidingDistanceLeft.sqrMagnitude);
						// Debug.Log($"x: {_slidingDistanceLeft.x} y: {_slidingDistanceLeft.y}");
						// Debug.Log($"x: {_slidingDistance.x} y: {_slidingDistance.y}");
					}
				}

				foreach (RouletteEntry rouletteEntry in rouletteEntries)
					rouletteEntry.UpdatePosition(_slidingDistance);

				_slidingDistanceLeft -= _slidingDistance;
			}
			else
			{
				// Roulette movement is finished here
				if (_rouletteSliding)
				{
					_rouletteSliding = false;
					_finishBoostActivated = false;
					OnSlidingFinishedCallback?.Invoke();
				}
			}
		}
	
		/// <summary>
		/// Calculate the sliding distance and sliding frames
		/// </summary>
		void SetSlidingEffect()
		{
			Vector3 deltaPos = _deltaInputPosL;
			Vector3 slideDistance = _endInputPosL - _startInputPosL;
			bool fastSliding = IsFastSliding(_numOfInputFrames, slideDistance);

			if (fastSliding)
				deltaPos *= 5.0f;   // Slide more longer!

			_slidingDistanceLeft = deltaPos;

			if (alignMiddle) {
				_slidingFramesLeft = fastSliding ? entrySlidingFrames >> 1 : entrySlidingFrames >> 2;
				_needToAlignToCenter = true;
			} else {
				_slidingFramesLeft = fastSliding ? entrySlidingFrames * 2 : entrySlidingFrames;
			}
		}

		/// <summary>
		/// Determine if the finger or mouse sliding is the fast sliding.
		/// If the duration of a slide is within 15 frames and the distance is
		/// longer than the 1/3 of the distance of the roulette, the slide is the fast sliding.
		/// </summary>
		bool IsFastSliding(int frames, Vector3 distance)
		{
			if (frames < 15) {
				switch (direction) {
					case Direction.Horizontal:
						if (Mathf.Abs(distance.x) > CanvasMaxPosL.x * 2.0f / 3.0f)
							return true;
						else
							return false;
					case Direction.Vertical:
						if (Mathf.Abs(distance.y) > CanvasMaxPosL.y * 2.0f / 3.0f)
							return true;
						else
							return false;
				}
			}
			return false;
		}
	
		/// <summary>
		/// Set the sliding effect to make one of entries align to center
		/// </summary>
		private void SetSlidingToCenter()
		{
			_slidingDistanceLeft = FindDeltaPositionToCenter();
			_slidingFramesLeft = entrySlidingFrames;
		}
	
		/// <summary>
		/// Find entry which is the closest to the center position,
		/// and calculate the delta x or y position between it and the center position.
		/// </summary>
		private Vector3 FindDeltaPositionToCenter()
		{
			float minDeltaPos = Mathf.Infinity;
			float deltaPos;
			Vector3 alignToCenterDistance;

			switch (direction) {
				case Direction.Vertical:
					foreach (RouletteEntry rouletteEntry in rouletteEntries) {
						// Skip the disabled entry in linear mode
						if (!rouletteEntry.isActiveAndEnabled)
							continue;

						deltaPos = -rouletteEntry.transform.localPosition.y;
						if (Mathf.Abs(deltaPos) < Mathf.Abs(minDeltaPos))
							minDeltaPos = deltaPos;
					}

					alignToCenterDistance = new Vector3(0.0f, minDeltaPos, 0.0f);
					break;

				case Direction.Horizontal:
					foreach (RouletteEntry rouletteEntry in rouletteEntries) {
						// Skip the disabled entry in linear mode
						if (!rouletteEntry.isActiveAndEnabled)
							continue;

						deltaPos = -rouletteEntry.transform.localPosition.x;
						if (Mathf.Abs(deltaPos) < Mathf.Abs(minDeltaPos))
							minDeltaPos = deltaPos;
					}

					alignToCenterDistance = new Vector3(minDeltaPos, 0.0f, 0.0f);
					break;

				default:
					alignToCenterDistance = Vector3.zero;
					break;
			}

			return alignToCenterDistance;
		}
	
		/// <summary>
		/// Move the roulette for the distance of times of unit position
		/// </summary>
		protected void SetUnitMove(int unit)
		{
			Vector2 deltaPos = UnitPosL * unit;

			if (_slidingFramesLeft != 0)
				deltaPos += (Vector2)_slidingDistanceLeft;

			_slidingDistanceLeft = deltaPos;
			_slidingFramesLeft = entrySlidingFrames;
		}
	
		/// <summary>
		/// Move all rouletteEntries 1 unit up.
		/// </summary>
		public void MoveOneUnitUp()
		{
			SetUnitMove(1);
		}
	
		/// <summary>
		/// Move all rouletteEntries 1 unit down. 
		/// </summary>
		public void MoveOneUnitDown()
		{
			SetUnitMove(-1);
		}

		/// <summary>
		/// Make roulette can't go further, when the it reaches the end.
		/// This method is used for the linear mode.
		/// </summary>
		private void StopRouletteWhenReachEnd()
		{
			switch (direction) {
				case Direction.Vertical:
					// If the roulette reaches the head and it keeps going down, or
					// the roulette reaches the tail and it keeps going up,
					// make the roulette end be stopped at the center.
					if ((numOfUpperDisabledEntries >= _maxNumOfDisabledEntries && _slidingDistanceLeft.y < 0) ||
					    (numOfLowerDisabledEntries >= _maxNumOfDisabledEntries && _slidingDistanceLeft.y > 0)) {
						Vector3 remainDistance = FindDeltaPositionToCenter();
						_slidingDistanceLeft.y = remainDistance.y;

						if (_slidingFramesLeft == 1)
							_slidingFramesLeft = entrySlidingFrames;
					}

					break;

				case Direction.Horizontal:
					// If the roulette reaches the head and it keeps going left, or
					// the roulette reaches the tail and it keeps going right,
					// make the roulette end be stopped at the center.
					if ((numOfUpperDisabledEntries >= _maxNumOfDisabledEntries && _slidingDistanceLeft.x > 0) ||
					    (numOfLowerDisabledEntries >= _maxNumOfDisabledEntries && _slidingDistanceLeft.x < 0)) {
						Vector3 remainDistance = FindDeltaPositionToCenter();
						_slidingDistanceLeft.x = remainDistance.x;
					}

					break;
				default:
					throw new ArgumentOutOfRangeException();
			}
		}


		/// <summary>
		/// Get the object of the centered RouletteEntry.
		/// The centered RouletteEntry is found by comparing which one is the closest
		/// to the center.
		/// </summary>
		public RouletteEntry GetCenteredEntry()
		{
			var minPosition = Mathf.Infinity;
			float position;
			RouletteEntry candidateEntry = null;

			switch (direction) {
				case Direction.Vertical:
					foreach (RouletteEntry rouletteEntry in rouletteEntries) {
						position = Mathf.Abs(rouletteEntry.transform.localPosition.y);
						if (position < minPosition) {
							minPosition = position;
							candidateEntry = rouletteEntry;
						}
					}
					break;
				case Direction.Horizontal:
					foreach (RouletteEntry rouletteEntry in rouletteEntries) {
						position = Mathf.Abs(rouletteEntry.transform.localPosition.x);
						if (position < minPosition) {
							minPosition = position;
							candidateEntry = rouletteEntry;
						}
					}
					break;
			}

			return candidateEntry;
		}

		/// <summary>
		/// Get the content ID of the centered entry
		/// </summary>
		public int GetCenteredContentId()
		{
			return GetCenteredEntry().GetContentId();
		}

		/// <summary>
		/// Divide each component of vector a by vector b.
		/// </summary>
		private Vector3 DivideComponent(Vector3 a, Vector3 b)
		{
			return new Vector3(a.x / b.x, a.y / b.y, a.z / b.z);
		}

		/// <summary>
		/// Checks if entry position can be used as an anchor for scaling
		/// </summary>
		/// <param name="i">entry index in array</param>
		/// <param name="entryYPos">entry position</param>
		public void CheckAnchor(int i, float entryYPos)
		{
			if (i <= 1) // if it takes one of first two positions
			{
				anchorsY[i] = entryYPos;
			} 
			else if (i >= rouletteEntries.Length - 2) // or one of the last two positions
			{
				anchorsY[i - (rouletteEntries.Length - 4)] = entryYPos;
			}

			_entriesCheckedForAnchor++;

			if (_entriesCheckedForAnchor == rouletteEntries.Length)
			{
				// Move edge anchors closer to center
				anchorsY[0] = Mathf.Lerp(anchorsY[1], anchorsY[0], scaleShift);
				anchorsY[3] = Mathf.Lerp(anchorsY[2], anchorsY[3], scaleShift);
				ScaleEntries();
			}
		}

		/// <summary>
		/// Explicitly check entries scale
		/// <para>On first init, anchorsY is not filled fully, so their scales would be broken and we need to recheck them manually</para>
		/// </summary>
		private void ScaleEntries()
		{
			foreach (var entry in rouletteEntries)
			{
				entry.UpdateScale();
			}
		}
	}
}