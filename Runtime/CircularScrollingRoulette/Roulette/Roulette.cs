﻿using System;
using System.Collections.Generic;
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
		public event Action<int> OnCenteredContentIdUpdate;
		public event Action<int> OnCenteredContentUpdate;
		public event Action OnRouletteBeginDrag;
		
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
			DragAndButton,
			DragAndWheel,
		}

		public enum Direction
		{
			Vertical,
			Horizontal,
			Radial,
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
		[Tooltip(" Set the sliding speed. The larger, the quicker.")]
		[Range(0.0f, 1.0f)]
		public float entrySlidingSpeedFactor = 0.2f;
		[Tooltip("Affects the speed")]
		[SerializeField] private float dTMod = 200;
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
		
		[Header("Radial settings")]
		public float radius = 100f;
		[Tooltip("Sets centered position for radial mode. Counting counter-clock-wise from (1,0)")]
		[Range(0, 360)]
		public float angleStart;
		public float RadStart => angleStart * Mathf.Deg2Rad;
		[HideInInspector] public float radPerEntry;
		/*===============================*/

		// The canvas plane which the scrolling roulette is at.
		protected Canvas ParentCanvas;

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
		private Vector3 _deltaInputPosL;
		
		private Vector3 _slidingDistance;     
		private Vector3 _slidingDistanceLeft;
		/// <summary>
		/// The flag indicating that one of the entries need to be centered after the sliding
		/// </summary>
		private bool _needToAlignToCenter;

		// Variables for linear mode
		public bool blockInputOnLimitedData;
		[HideInInspector]
		public int numOfUpperDisabledEntries;
		[HideInInspector]
		public int numOfLowerDisabledEntries;
		private int _maxNumOfDisabledEntries;
		private bool _limitedData;
		
		protected Dictionary<int, RouletteEntry> ContentInEntries;
		protected int LastContentId;
		private bool _allEntriesInitiated;
	
		// Variables for scaling
		[Header("Scaling")]
		[Tooltip("Make objects look like they disappear near the edges")]
		public bool scaleEdgeObjects = true;
		[Range(0f, 1f)]
		[Tooltip("At which percentage of path to anchor entry scale equals zero")]
		public float scaleShift = 0.8f;
		[HideInInspector] public Vector2[] anchors = new Vector2[4];
		private int _entriesCheckedForAnchor;
		private bool _rouletteSliding;

		[Header("Finish boost")]
		[SerializeField] private float minSlidingDistanceSqrMagnitude = 60000f;
		[SerializeField] private float baselineSlidingFactor = 0.02f;
		private bool _finishBoostActivated;
		
#pragma warning disable 0649
		[Header("Debug")] 
		[SerializeField] private bool logLogic;
		[SerializeField] private bool logSlidingDistance;
#pragma warning restore 0649
		
		public event Action OnSlidingFinishedCallback;

		/// <summary>
		/// Notice: RouletteEntry will initialize its variables from here, so Roulette
		/// must be executed before RouletteEntry. You have to set the execution order in the inspector.
		/// </summary>
		protected virtual void Start()
		{
			InitHelperData();
			InstantiateEntries();
			InitializePositionVars();
			InitializeInputFunction();
			InitializeEntryDependency();
			InitCallbacks();
			_maxNumOfDisabledEntries = rouletteEntries.Length / 2;
		}

		protected virtual void InitHelperData()
		{
			if (rouletteType == RouletteType.Linear)
			{
				_limitedData = numberOfEntries >= rouletteBank.GetRouletteLength();
			
				numberOfEntries = numberOfEntries > rouletteBank.GetRouletteLength()
					? rouletteBank.GetRouletteLength()
					: numberOfEntries; 
				ContentInEntries = new Dictionary<int, RouletteEntry>();
				LastContentId = rouletteBank.GetRouletteLength() - 1;
			}
		}

		private void InitCallbacks()
		{
			OnSlidingFinishedCallback += CheckCenteredContentId;
		}

		private void CheckCenteredContentId()
		{
			if(logLogic) Debug.Log("CheckCenteredContentId()");
			var id = GetCenteredContentId();
			OnCenteredContentUpdate?.Invoke(id);
			if (id == centeredContentId) return;
			
			centeredContentId = id;
			OnCenteredContentIdUpdate?.Invoke(id);
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

		protected virtual RouletteEntry InstantiateEntry(GameObject prefab, Transform parent)
		{
			return Instantiate(prefab, parent).GetComponent<RouletteEntry>();
		}

		void InitializePositionVars()
		{
			/* The the reference of canvas plane */
			ParentCanvas = GetComponentInParent<Canvas>();

			/* Get the max position of canvas plane in the canvas space.
		 * Assume that the origin of the canvas space is at the center of canvas plane. */

			CanvasMaxPosL = GenerateCanvasMaxPosL();
			
			radPerEntry = Mathf.PI * 2f / rouletteEntries.Length;
			
			UnitPosL = (direction != Direction.Radial) ? (CanvasMaxPosL / entryGapFactor) : radPerEntry * Vector2.up;
			
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
			RectTransform rectTransform = ParentCanvas.GetComponent<RectTransform>();
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
			if (_limitedData && blockInputOnLimitedData)
			{
				_inputPositionHandler = delegate {  };
				_scrollHandler = delegate {  };
				foreach (Button button in controlButtons)
					button.gameObject.SetActive(false);
				
				return;
			}
			
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
				
				case ControlMode.DragAndWheel:
					_inputPositionHandler = DragPositionHandler;

					_scrollHandler = ScrollDeltaHandler;
					
					foreach (Button button in controlButtons)
						button.gameObject.SetActive(false);
					break;
			}
		}

		/* ====== Callback functions for the unity event system ====== */
		public void OnBeginDrag(PointerEventData pointer)
		{
			OnRouletteBeginDrag?.Invoke();
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
					_startInputPosL = ScreenToCanvasSpace(pointer.position);
					break;

				case TouchPhase.Moved:
					_deltaInputPosL = ScreenToCanvasSpace(pointer.delta);
					// Slide the roulette as long as the moving distance of the pointer
					_slidingDistanceLeft += _deltaInputPosL;
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
				case Direction.Radial: 
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
			return position / ParentCanvas.scaleFactor;
		}


		/* ====== Movement functions ====== */
		/* Control the movement of rouletteEntries
	 */
		private void Update()
		{
			if(!_allEntriesInitiated) return;
			
			if (_slidingDistanceLeft.sqrMagnitude > 1)
			{
				if(logSlidingDistance) Debug.Log(_slidingDistanceLeft.sqrMagnitude);
				
				_rouletteSliding = true;
				if (rouletteType == RouletteType.Linear) {
					StopRouletteWhenReachEnd();
				}

				var startFinishBoost = _slidingDistanceLeft.sqrMagnitude <= minSlidingDistanceSqrMagnitude && !_finishBoostActivated;
				// Boost end of scrolling 
				if (startFinishBoost)
				{
					_finishBoostActivated = true;
					if(logSlidingDistance) Debug.Log("Finish boost activated");
				}
				
				var slideSpeed = _finishBoostActivated ? baselineSlidingFactor : entrySlidingSpeedFactor;

				_slidingDistance = Vector3.Lerp(Vector3.zero, _slidingDistanceLeft, 
					slideSpeed) * (Time.deltaTime * dTMod);
				
				foreach (RouletteEntry rouletteEntry in rouletteEntries)
					rouletteEntry.UpdatePosition(_slidingDistance);

				_slidingDistanceLeft -= _slidingDistance;
			}
			else
			{
				if (_needToAlignToCenter) {
					_needToAlignToCenter = false;
					SetSlidingToCenter();
				} else {
					_slidingDistance = _slidingDistanceLeft;
				
				// Roulette movement is finished here
				if (_rouletteSliding)
				{
					_rouletteSliding = false;
					_finishBoostActivated = false;
					OnSlidingFinishedCallback?.Invoke();
				}
				
				}
			}
		}
	
		/// <summary>
		/// Calculate the sliding distance 
		/// </summary>
		void SetSlidingEffect()
		{
			Vector3 deltaPos = _deltaInputPosL;
			Vector3 slideDistance = _endInputPosL - _startInputPosL;
			bool fastSliding = IsFastSliding(slideDistance);

			if (fastSliding)
				deltaPos *= 5.0f;   // Slide more longer!

			_slidingDistanceLeft = deltaPos;

			if (alignMiddle) {
				_needToAlignToCenter = true;
			} 
		}

		/// <summary>
		/// Determine if the finger or mouse sliding is the fast sliding.
		/// If the distance is
		/// longer than the 1/3 of the distance of the roulette, the slide is the fast sliding.
		/// </summary>
		bool IsFastSliding(Vector3 distance)
		{
			{
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
			if(logLogic) Debug.Log("Sliding to center");
			_slidingDistanceLeft = FindDeltaPositionToCenter();
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
				case Direction.Radial:
					foreach (RouletteEntry rouletteEntry in rouletteEntries)
					{
						deltaPos = -rouletteEntry.radPos;
						if (Mathf.Abs(deltaPos) < Mathf.Abs(minDeltaPos))
							minDeltaPos = deltaPos;
					}

					alignToCenterDistance = new Vector3(minDeltaPos, 0, 0);
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
		protected virtual void SetUnitMove(int unit)
		{
			Vector2 deltaPos = UnitPosL * unit;

			if (_slidingDistanceLeft.sqrMagnitude > 0)
				deltaPos += (Vector2)_slidingDistanceLeft;

			_slidingDistanceLeft = deltaPos;
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
				{
					if (ReachedEnd())
					{
						Vector3 remainDistance = FindDeltaPositionToCenter();
						_slidingDistanceLeft.y = remainDistance.y;

					}

					break;
				}

				case Direction.Horizontal:
				{
					if (ReachedEnd())
					{
						Vector3 remainDistance = FindDeltaPositionToCenter();
						_slidingDistanceLeft.x = remainDistance.x;
					}

					break;
				}
				default:
					throw new ArgumentOutOfRangeException();
			}
		}

		protected virtual bool ReachedEnd()
		{
			(bool slidingUp, bool slidingDown) = direction switch
			{
				Direction.Vertical => (_slidingDistanceLeft.y < 0, _slidingDistanceLeft.y > 0),
				Direction.Horizontal => (_slidingDistanceLeft.x > 0, _slidingDistanceLeft.x < 0),
				Direction.Radial => throw new ArgumentException("Radial direction not supported by Linear type"),
				_ => throw new ArgumentOutOfRangeException()
			};

			return numOfUpperDisabledEntries >= _maxNumOfDisabledEntries && slidingUp ||
			       numOfLowerDisabledEntries >= _maxNumOfDisabledEntries && slidingDown || 
			       ContentIsActive(0) && slidingUp || 
			       ContentIsActive(LastContentId) && slidingDown;
		}

		private bool ContentIsActive(int contentId) 
			=> ContentInEntries.ContainsKey(contentId) && 
			   ContentInEntries[contentId].isActiveAndEnabled;

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
				case Direction.Radial:
					foreach (var rouletteEntry in rouletteEntries)
					{
						position = Mathf.Abs(Mathf.DeltaAngle(angleStart, rouletteEntry.AnglePos));
						
						if (position < minPosition)
						{
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
		/// Checks if entry position can be used as an anchor for scaling
		/// </summary>
		/// <param name="i">entry index in array</param>
		/// <param name="entryPos">entry position</param>
		public void CheckAnchor(int i, Vector2 entryPos)
		{
			if (i <= 1) // if it takes one of first two positions
			{
				anchors[i] = entryPos;
			} 
			else if (i >= rouletteEntries.Length - 2) // or one of the last two positions
			{
				anchors[i - (rouletteEntries.Length - 4)] = entryPos;
			}

			_entriesCheckedForAnchor++;

			if (_entriesCheckedForAnchor == rouletteEntries.Length)
			{
				_allEntriesInitiated = true;
				// Move edge anchors closer to center
				anchors[0] = Vector2.Lerp(anchors[1], anchors[0], scaleShift);
				anchors[3] = Vector2.Lerp(anchors[2], anchors[3], scaleShift);
				ScaleEntries();
			}
		}

		/// <summary>
		/// Explicitly check entries scale
		/// <para>On first init, anchors is not filled fully, so their scales would be broken and we need to recheck them manually</para>
		/// </summary>
		private void ScaleEntries()
		{
			foreach (var entry in rouletteEntries)
			{
				entry.UpdateScale();
			}
		}

		public void EntryChangedContent(RouletteEntry entry, int prevContentId, int contentId)
		{
			if(rouletteType != RouletteType.Linear) return;
			ContentInEntries.Remove(prevContentId);
			ContentInEntries.Add(contentId, entry);
		}
	}
}