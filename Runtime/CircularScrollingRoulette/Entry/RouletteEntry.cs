﻿using CircularScrollingRoulette.Bank;
using CircularScrollingRoulette.Entry.Content;
using UnityEngine;

namespace CircularScrollingRoulette.Entry
{
	/// <summary>
	/// The basic component of the scrolling roulette.
	/// <para>Control the position and update the content of the roulette element.</para>
	/// </summary>
	public class RouletteEntry : MonoBehaviour
	{
		[Tooltip("The display text for the content of the roulette entry")]
		private RouletteEntryContent _content;

		// public EntryType type;
	
		// These public variables will be initialized
		// in Roulette.InitializeEntriesDependency().
		[HideInInspector]
		public int rouletteEntryId;   // The same as the order in the `rouletteEntries`
		[HideInInspector]
		public RouletteEntry lastRouletteEntry;
		[HideInInspector]
		public RouletteEntry nextRouletteEntry;

		private Roulette.Roulette _positionControl;
		protected RouletteBank _rouletteBank;
		protected int _contentId;

		/* ====== Position variables ====== */
		// Position calculated here is in the local space of the roulette
		private Vector2 _maxCurvePos;     // The maximum outer position
		private Vector2 _unitPos;         // The distance between entries
		private Vector2 _lowerBoundPos;   // The left/down-most position of the entry
		private Vector2 _upperBoundPos;   // The right/up-most position of the entry
		// _changeSide(Lower/Upper)BoundPos is the boundary for checking that
		// whether to move the entry to the other end or not
		private Vector2 _changeSideLowerBoundPos;
		private Vector2 _changeSideUpperBoundPos;
		private float _cosValueAdjust;

		private Vector3 _initialLocalScale;
		public float radPos;
		public float AnglePos => radPos * Mathf.Rad2Deg;

		/// <summary>
		/// Get the content ID of the entry
		/// </summary>
		public int GetContentId()
		{
			return _contentId;
		}

		/* Notice: RouletteEntry will initialize its variables from Roulette.
	 * Make sure that the execution order of script Roulette is prior to
	 * RouletteEntry.
	 */
		protected virtual void Start()
		{
			_content = GetComponentInChildren<RouletteEntryContent>();
			if(_content == null) Debug.LogWarning($"{gameObject.name}: couldn't get content");
			_positionControl = transform.GetComponentInParent<Roulette.Roulette>();
			_rouletteBank = _positionControl.rouletteBank;

			_maxCurvePos = _positionControl.CanvasMaxPosL * _positionControl.rouletteCurvature;
			_unitPos = _positionControl.UnitPosL;
			_lowerBoundPos = _positionControl.LowerBoundPosL;
			_upperBoundPos = _positionControl.UpperBoundPosL;
			_changeSideLowerBoundPos = _lowerBoundPos + _unitPos * 0.3f;
			_changeSideUpperBoundPos = _upperBoundPos - _unitPos * 0.3f;
			_cosValueAdjust = _positionControl.positionAdjust;

			_initialLocalScale = transform.localScale;

			InitialPosition();
			InitContent();
			CheckAnchor();
		}
	
		/// <summary>
		/// Initialize the content of RouletteEntry. 
		/// </summary>
		public void InitContent()
		{
			// Get the content ID of the centered entry
			_contentId = _positionControl.centeredContentId;

			// Adjust the contentID according to its initial order.
			_contentId += rouletteEntryId - _positionControl.rouletteEntries.Length / 2;

			// In the linear mode, disable the entry if needed
			if (_positionControl.rouletteType == Roulette.Roulette.RouletteType.Linear) {
				// Disable the entries at the upper half of the roulette
				// which will hold the item at the tail of the contents.
				if (_contentId < 0) {
					_positionControl.numOfUpperDisabledEntries += 1;
					gameObject.SetActive(false);
				}
				// Disable the entry at the lower half of the roulette
				// which will hold the repeated item.
				else if (_contentId >= _rouletteBank.GetRouletteLength()) {
					_positionControl.numOfLowerDisabledEntries += 1;
					gameObject.SetActive(false);
				}
			}

			// Round the content id
			while (_contentId < 0)
				_contentId += _rouletteBank.GetRouletteLength();
			_contentId = _contentId % _rouletteBank.GetRouletteLength();

			UpdateDisplayContent();
		}
	
		/// <summary>
		/// Update the displaying content on the RouletteEntry. 
		/// </summary>
		protected virtual void UpdateDisplayContent()
		{
			// Update the content according to its contentID.
			_content.SetContent(_rouletteBank.GetRouletteContent(_contentId));
		}

		/* Initialize the local position of the RouletteEntry according to its ID
	 */
		void InitialPosition()
		{
			// If there are even number of RouletteEntries, adjust the initial position by an half unitPos.
			if ((_positionControl.rouletteEntries.Length & 0x1) == 0) {
				switch (_positionControl.direction) {
					case Roulette.Roulette.Direction.Vertical:
						transform.localPosition = new Vector3(0.0f,
							_unitPos.y * (rouletteEntryId * -1 + _positionControl.rouletteEntries.Length / 2) - _unitPos.y / 2,
							0.0f);
						UpdateXPosition();
						break;
					case Roulette.Roulette.Direction.Horizontal:
						transform.localPosition = new Vector3(
							_unitPos.x * (rouletteEntryId - _positionControl.rouletteEntries.Length / 2) - _unitPos.x / 2,
							0.0f, 0.0f);
						UpdateYPosition();
						break;
					case Roulette.Roulette.Direction.Radial:
						InitAngularPos();
						UpdateAngularPosition();
						break;
				}
			} else {
				switch (_positionControl.direction) {
					case Roulette.Roulette.Direction.Vertical:
						transform.localPosition = new Vector3(0.0f,
							_unitPos.y * (rouletteEntryId * -1 + _positionControl.rouletteEntries.Length / 2),
							0.0f);
						UpdateXPosition();
						break;
					case Roulette.Roulette.Direction.Horizontal:
						transform.localPosition = new Vector3(
							_unitPos.x * (rouletteEntryId - _positionControl.rouletteEntries.Length / 2),
							0.0f, 0.0f);
						UpdateYPosition();
						break;
					case Roulette.Roulette.Direction.Radial:
						InitAngularPos();
						UpdateAngularPosition();
						break;
				}
			}
		}

		private void InitAngularPos()
		{
			radPos = rouletteEntryId * _positionControl.radPerEntry + _positionControl.RadStart;
		}

		/// <summary>
		/// Update the local position of RouletteEntry according to the delta position at each frame.
		/// Note that the deltaPosition must be in local space. 
		/// </summary>
		public void UpdatePosition(Vector3 deltaPositionL)
		{
			switch (_positionControl.direction) {
				case Roulette.Roulette.Direction.Vertical:
					transform.localPosition += new Vector3(0.0f, deltaPositionL.y, 0.0f);
					CheckBoundaryY();
					UpdateXPosition();
					break;
				case Roulette.Roulette.Direction.Horizontal:
					transform.localPosition += new Vector3(deltaPositionL.x, 0.0f, 0.0f);
					CheckBoundaryX();
					UpdateYPosition();
					break;
				case Roulette.Roulette.Direction.Radial:
					radPos += deltaPositionL.y;
					radPos %= 2f * Mathf.PI;
					UpdateAngularPosition();
					break;
			}
		}

		private void UpdateAngularPosition()
		{
			transform.localPosition = new Vector2(
				_positionControl.radius * Mathf.Cos(radPos),
				_positionControl.radius * Mathf.Sin(radPos));
		}
	
		/// <summary>
		/// Calculate the x position according to the y position.
		/// </summary>
		void UpdateXPosition()
		{
			// Formula: x = maxCurvePos_x * (cos(r) + cosValueAdjust),
			// where r = (y / upper_y) * pi / 2, then r is in range [- pi / 2, pi / 2],
			// and corresponding cosine value is from 0 to 1 to 0.
			transform.localPosition = new Vector3(
				_maxCurvePos.x * (_cosValueAdjust +
				                  Mathf.Cos(transform.localPosition.y / _upperBoundPos.y * Mathf.PI / 2.0f)),
				transform.localPosition.y, transform.localPosition.z);
			// UpdateSize(_upperBoundPos.y, transform.localPosition.y); 
			UpdateScale();
		}
	
		/// <summary>
		/// Calculate the y position according to the x position. 
		/// </summary>
		void UpdateYPosition()
		{
			transform.localPosition = new Vector3(
				transform.localPosition.x,
				_maxCurvePos.y * (_cosValueAdjust +
				                  Mathf.Cos(transform.localPosition.x / _upperBoundPos.x * Mathf.PI / 2.0f)),
				transform.localPosition.z);
			// UpdateSize(_upperBoundPos.x, transform.localPosition.x);
			UpdateScale();
		}
	
		/// <summary>
		/// Check if the RouletteEntry is beyond the checking boundary or not
		/// If it does, move the RouletteEntry to the other end of the roulette
		/// and update the content.
		/// </summary>
		void CheckBoundaryY()
		{
			float beyondPosYL = 0.0f;

			if (transform.localPosition.y < _changeSideLowerBoundPos.y) {
				beyondPosYL = transform.localPosition.y - _lowerBoundPos.y;
				transform.localPosition = new Vector3(
					transform.localPosition.x,
					_upperBoundPos.y - _unitPos.y + beyondPosYL,
					transform.localPosition.z);
				UpdateToLastContent();
			} else if (transform.localPosition.y > _changeSideUpperBoundPos.y) {
				beyondPosYL = transform.localPosition.y - _upperBoundPos.y;
				transform.localPosition = new Vector3(
					transform.localPosition.x,
					_lowerBoundPos.y + _unitPos.y + beyondPosYL,
					transform.localPosition.z);
				UpdateToNextContent();
			}
		}

		void CheckBoundaryX()
		{
			float beyondPosXL = 0.0f;

			if (transform.localPosition.x < _changeSideLowerBoundPos.x) {
				beyondPosXL = transform.localPosition.x - _lowerBoundPos.x;
				transform.localPosition = new Vector3(
					_upperBoundPos.x - _unitPos.x + beyondPosXL,
					transform.localPosition.y,
					transform.localPosition.z);
				UpdateToNextContent();
			} else if (transform.localPosition.x > _changeSideUpperBoundPos.x) {
				beyondPosXL = transform.localPosition.x - _upperBoundPos.x;
				transform.localPosition = new Vector3(
					_lowerBoundPos.x + _unitPos.x + beyondPosXL,
					transform.localPosition.y,
					transform.localPosition.z);
				UpdateToLastContent();
			}
		}

		/// <summary>
		/// Check if this entry position might be used as anchor
		/// </summary>
		private void CheckAnchor()
		{
			_positionControl.CheckAnchor(rouletteEntryId, transform.localPosition.y);
		}
	
		/// <summary>
		/// If entry is moving to borderline positions, it scales to 0
		/// </summary>
		public void UpdateScale()
		{
			if (!_positionControl.scaleEdgeObjects) return;
		
			var y = transform.localPosition.y;
		
			if (y > _positionControl.anchorsY[1])
			{
				var scale = Mathf.InverseLerp(_positionControl.anchorsY[0], _positionControl.anchorsY[1], y);
				transform.localScale = scale * Vector3.one;
			}
			else if (y < _positionControl.anchorsY[2])
			{
				var scale = Mathf.InverseLerp(_positionControl.anchorsY[3], _positionControl.anchorsY[2], y);
				transform.localScale = scale * Vector3.one;
			}
			else
			{
				transform.localScale = Vector3.one;
			}
		}
	
		/// <summary>
		/// Scale the RouletteEntry according to its position
		/// </summary>
		/// <param name="smallest_at">The position at where the smallest RouletteEntry will be</param>
		/// <param name="target_value">The position of the target RouletteEntry</param>
		void UpdateSize(float smallest_at, float target_value)
		{
			// The scale of the entry at the either end is initialLocalScale.
			// The scale of the entry at the center is initialLocalScale * (1 + centerEntryScaleRatio).
			transform.localScale = _initialLocalScale *
			                       (1.0f + _positionControl.centerEntryScaleRatio *
				                       Mathf.InverseLerp(smallest_at, 0.0f, Mathf.Abs(target_value)));
		}

		private int GetCurrentContentId()
		{
			return _contentId;
		}
	
		/// <summary>
		/// Update the content to the last content of the next RouletteEntry
		/// </summary>
		void UpdateToLastContent()
		{
			_contentId = nextRouletteEntry.GetCurrentContentId() - 1;
			_contentId = (_contentId < 0) ? _rouletteBank.GetRouletteLength() - 1 : _contentId;

			if (_positionControl.rouletteType == Roulette.Roulette.RouletteType.Linear) {
				if (_contentId == _rouletteBank.GetRouletteLength() - 1 ||
				    !nextRouletteEntry.isActiveAndEnabled) {
					// If the entry has been disabled at the other side,
					// decrease the counter of the other side.
					if (!isActiveAndEnabled)
						--_positionControl.numOfLowerDisabledEntries;

					// In linear mode, don't display the content of the other end
					gameObject.SetActive(false);
					++_positionControl.numOfUpperDisabledEntries;
				} else if (!isActiveAndEnabled) {
					// The disabled entry from the other end will be enabled again,
					// if the next entry is enabled.
					gameObject.SetActive(true);
					--_positionControl.numOfLowerDisabledEntries;
				}
			}

			UpdateDisplayContent();
		}
	
		/// <summary>
		/// Update the content to the next content of the last RouletteEntry
		/// </summary>
		void UpdateToNextContent()
		{
			_contentId = lastRouletteEntry.GetCurrentContentId() + 1;
			_contentId = (_contentId == _rouletteBank.GetRouletteLength()) ? 0 : _contentId;

			if (_positionControl.rouletteType == Roulette.Roulette.RouletteType.Linear) {
				if (_contentId == 0 || !lastRouletteEntry.isActiveAndEnabled) {
					if (!isActiveAndEnabled)
						--_positionControl.numOfUpperDisabledEntries;

					// In linear mode, don't display the content of the other end
					gameObject.SetActive(false);
					++_positionControl.numOfLowerDisabledEntries;
				} else if (!isActiveAndEnabled) {
					gameObject.SetActive(true);
					--_positionControl.numOfUpperDisabledEntries;
				}
			}

			UpdateDisplayContent();
		}

		public object GetContent() => _content.GetContent();
	}
}
