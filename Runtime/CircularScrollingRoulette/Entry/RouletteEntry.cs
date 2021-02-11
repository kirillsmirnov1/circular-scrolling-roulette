using CircularScrollingRoulette.Bank;
using CircularScrollingRoulette.Entry.Content;
using UnityEngine;
using UnityEngine.UI;

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
		public int rouletteEntryId;   // The same as the order in the `rouletteEntries`
		[HideInInspector]
		public RouletteEntry lastRouletteEntry;
		[HideInInspector]
		public RouletteEntry nextRouletteEntry;

		private Roulette.Roulette _roulette;
		protected RouletteBank _rouletteBank;
		[SerializeField] protected int contentId;
		protected int _prevContentId = -1;

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
		public int GetContentId() => contentId;

		/* Notice: RouletteEntry will initialize its variables from Roulette.
	 * Make sure that the execution order of script Roulette is prior to
	 * RouletteEntry.
	 */
		protected virtual void Start()
		{
			_content = GetComponentInChildren<RouletteEntryContent>();
			if(_content == null) Debug.LogWarning($"{gameObject.name}: couldn't get content");
			_roulette = transform.GetComponentInParent<Roulette.Roulette>();
			_rouletteBank = _roulette.rouletteBank;

			_maxCurvePos = _roulette.CanvasMaxPosL * _roulette.rouletteCurvature;
			_unitPos = _roulette.UnitPosL;
			_lowerBoundPos = _roulette.LowerBoundPosL;
			_upperBoundPos = _roulette.UpperBoundPosL;
			_changeSideLowerBoundPos = _lowerBoundPos + _unitPos * 0.3f;
			_changeSideUpperBoundPos = _upperBoundPos - _unitPos * 0.3f;
			_cosValueAdjust = _roulette.positionAdjust;

			_initialLocalScale = transform.localScale;

			InitialPosition();
			InitContent();
			CheckAnchor();
			AddClickEvent();
		}

		private void AddClickEvent()
		{
			GetComponentInChildren<Button>()
				?.onClick
				.AddListener(() => _roulette.onEntryClick.Invoke(contentId));
		}

		/// <summary>
		/// Initialize the content of RouletteEntry. 
		/// </summary>
		public void InitContent()
		{
			// Get the content ID of the centered entry
			contentId = rouletteEntryId;
			UpdateDisplayContent();
		}
	
		/// <summary>
		/// Update the displaying content on the RouletteEntry. 
		/// </summary>
		protected virtual void UpdateDisplayContent()
		{
			// Update the content according to its contentID.
			_content.SetContent(_rouletteBank.GetRouletteContent(contentId));
			_roulette.EntryChangedContent(this, _prevContentId, contentId);
			_prevContentId = contentId;
		}

		/* Initialize the local position of the RouletteEntry according to its ID
	 */
		private void InitialPosition()
		{
			// If there are even number of RouletteEntries, adjust the initial position by an half unitPos.
			var evenNumberOfEntries = (_roulette.rouletteEntries.Length & 0x1) == 0;
			
			switch (_roulette.direction) {
				case Roulette.Roulette.Direction.Vertical:
					transform.localPosition = new Vector3(0.0f,
						_unitPos.y * (rouletteEntryId * -1 + _roulette.rouletteEntries.Length / 2) - (evenNumberOfEntries ? _unitPos.y / 2 : 0),
						0.0f);
					UpdateXPosition();
					break;
				case Roulette.Roulette.Direction.Horizontal:
					transform.localPosition = new Vector3(
						_unitPos.x * (rouletteEntryId - _roulette.rouletteEntries.Length / 2) + (evenNumberOfEntries ? _unitPos.x / 2 : 0),
						0.0f, 0.0f);
					UpdateYPosition();
					break;
				case Roulette.Roulette.Direction.Radial:
					InitAngularPos();
					UpdateAngularPosition();
					break;
			}
		}

		private void InitAngularPos()
		{
			radPos = rouletteEntryId * _roulette.radPerEntry + _roulette.RadStart;
		}

		/// <summary>
		/// Update the local position of RouletteEntry according to the delta position at each frame.
		/// Note that the deltaPosition must be in local space. 
		/// </summary>
		public void UpdatePosition(Vector3 deltaPositionL)
		{
			switch (_roulette.direction) {
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
				_roulette.radius * Mathf.Cos(radPos),
				_roulette.radius * Mathf.Sin(radPos));
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
			_roulette.CheckAnchor(rouletteEntryId, transform.localPosition);
		}
	
		/// <summary>
		/// If entry is moving to borderline positions, it scales to 0
		/// </summary>
		public void UpdateScale()
		{
			if (!_roulette.scaleEdgeObjects) return;

			switch (_roulette.direction)
			{
				case Roulette.Roulette.Direction.Vertical:
					ScaleByY();
					break;
				case Roulette.Roulette.Direction.Horizontal:
					ScaleByX();
					break;
			}
		}

		private void ScaleByY()
		{
			var y = transform.localPosition.y;

			if (y > _roulette.anchors[1].y)
			{
				var scale = Mathf.InverseLerp(_roulette.anchors[0].y, _roulette.anchors[1].y, y);
				transform.localScale = scale * Vector3.one;
			}
			else if (y < _roulette.anchors[2].y)
			{
				var scale = Mathf.InverseLerp(_roulette.anchors[3].y, _roulette.anchors[2].y, y);
				transform.localScale = scale * Vector3.one;
			}
			else
			{
				transform.localScale = Vector3.one;
			}
		}
		
		private void ScaleByX()
		{
			var x = transform.localPosition.x;

			if (x < _roulette.anchors[1].x)
			{
				var scale = Mathf.InverseLerp(_roulette.anchors[0].x, _roulette.anchors[1].x, x);
				transform.localScale = scale * Vector3.one;
			}
			else if (x > _roulette.anchors[2].x)
			{
				var scale = Mathf.InverseLerp(_roulette.anchors[3].x, _roulette.anchors[2].x, x);
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
			                       (1.0f + _roulette.centerEntryScaleRatio *
				                       Mathf.InverseLerp(smallest_at, 0.0f, Mathf.Abs(target_value)));
		}

		/// <summary>
		/// Update the content to the last content of the next RouletteEntry
		/// </summary>
		void UpdateToLastContent()
		{
			contentId = nextRouletteEntry.GetContentId() - 1;
			contentId = (contentId < 0) ? _rouletteBank.GetRouletteLength() - 1 : contentId;

			if (_roulette.rouletteType == Roulette.Roulette.RouletteType.Linear) {
				if (contentId == _rouletteBank.GetRouletteLength() - 1 ||
				    !nextRouletteEntry.isActiveAndEnabled) {
					// If the entry has been disabled at the other side,
					// decrease the counter of the other side.
					if (!isActiveAndEnabled)
						--_roulette.numOfLowerDisabledEntries;

					// In linear mode, don't display the content of the other end
					gameObject.SetActive(false);
					++_roulette.numOfUpperDisabledEntries;
				} else if (!isActiveAndEnabled) {
					// The disabled entry from the other end will be enabled again,
					// if the next entry is enabled.
					gameObject.SetActive(true);
					--_roulette.numOfLowerDisabledEntries;
				}
			}

			UpdateDisplayContent();
		}
	
		/// <summary>
		/// Update the content to the next content of the last RouletteEntry
		/// </summary>
		void UpdateToNextContent()
		{
			contentId = lastRouletteEntry.GetContentId() + 1;
			contentId = (contentId == _rouletteBank.GetRouletteLength()) ? 0 : contentId;

			if (_roulette.rouletteType == Roulette.Roulette.RouletteType.Linear) {
				if (contentId == 0 || !lastRouletteEntry.isActiveAndEnabled) {
					if (!isActiveAndEnabled)
						--_roulette.numOfUpperDisabledEntries;

					// In linear mode, don't display the content of the other end
					gameObject.SetActive(false);
					++_roulette.numOfLowerDisabledEntries;
				} else if (!isActiveAndEnabled) {
					gameObject.SetActive(true);
					--_roulette.numOfUpperDisabledEntries;
				}
			}

			UpdateDisplayContent();
		}

		public object GetContent() => _content.GetContent();
	}
}
