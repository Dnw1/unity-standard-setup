using UnityEngine;

namespace com.dnw.standardpackage
{

    /// <summary>
    /// Skip button component that floats on the left side of headset, pointing at user.
    /// Only visible during videos, turns with headset.
    /// </summary>
    public class VRSkipButton : MonoBehaviour
    {
        [Header("Position Settings")]
        [Tooltip("Offset from headset center (bottom-left position: negative X = left, negative Y = down)")]
        [SerializeField] private Vector3 offset = new Vector3(-0.2f, -0.3f, 0.2f);
        
        [Tooltip("Distance from headset in meters (20cm = 0.2m)")]
        [SerializeField] private float distanceFromHeadset = 0.2f;
        
        [Header("Floor Alignment")]
        [Tooltip("Use floor-based height calculation")]
        [SerializeField] private bool useFloorAlignment = true;
        
        [Tooltip("Height above floor in meters (if useFloorAlignment is true, 1.2m = comfortable button height)")]
        [SerializeField] private float heightAboveFloor = 1.2f;

        [Header("Size Settings")]
        [Tooltip("Scale factor to make button smaller (0.3 = 30% of original size)")]
        [SerializeField] private float scaleFactor = 0.3f; // Very small button

        [Header("References")]
        [SerializeField] private GameObject buttonObject;
        [SerializeField] private UnityEngine.UI.Button buttonComponent;

        private Camera _headsetCamera;
        private Vector3 _originalScale;
        private bool _isVisible = false;
        
        // Cached reference to OVRCameraRig (replacing GameObject.Find() calls)
        private GameObject _ovrCameraRig;

        private void Awake()
        {
            // Cache OVRCameraRig reference
            CacheOVRCameraRig();
            
            // Find headset camera (cache in Awake, not Update)
            _headsetCamera = Camera.main;
            if (_headsetCamera == null && _ovrCameraRig != null)
            {
                _headsetCamera = _ovrCameraRig.GetComponentInChildren<Camera>();
            }
        }
        
        /// <summary>
        /// Caches the OVRCameraRig reference to avoid GameObject.Find() calls.
        /// </summary>
        private void CacheOVRCameraRig() {
            if (_ovrCameraRig == null) {
                _ovrCameraRig = GameObject.Find("OVRCameraRig");
            }
        }

        private void Start()
        {
            // Ensure FloorAlignment exists if using floor alignment
            // if (useFloorAlignment && FloorAlignment.Instance == null)
            // {
            //     GameObject floorAlignmentObj = new GameObject("FloorAlignment");
            //     floorAlignmentObj.AddComponent<FloorAlignment>();
            // }
            
            // Auto-setup if not assigned
            if (buttonObject == null)
            {
                buttonObject = gameObject;
            }

            if (buttonComponent == null)
            {
                buttonComponent = GetComponent<UnityEngine.UI.Button>();
            }

            // Store original scale
            _originalScale = transform.localScale;
            transform.localScale = _originalScale * scaleFactor;

            // Initially hidden
            SetVisible(false);
        }

        private void Update()
        {
            if (_isVisible && _headsetCamera != null)
            {
                UpdatePosition();
            }
        }

        private void UpdatePosition()
        {
            if (_headsetCamera == null) return;

            // Calculate position on left side, pointing at user
            Vector3 forward = _headsetCamera.transform.forward;
            Vector3 right = _headsetCamera.transform.right;
            Vector3 up = _headsetCamera.transform.up;

            // Base position: slightly in front and to the left
            Vector3 basePosition = _headsetCamera.transform.position + (forward * distanceFromHeadset);
            
            // Apply offset (left side, centered vertically)
            Vector3 finalPosition = basePosition + (right * offset.x) + (up * offset.y) + (forward * offset.z);
            
            // Apply floor alignment if enabled (only adjust Y height, preserve X/Z offset)
            // if (useFloorAlignment && FloorAlignment.Instance != null)
            // {
            //     // Get floor-aligned height for the X/Z position
            //     Vector3 floorAlignedPosition = FloorAlignment.Instance.GetPositionAtHeight(finalPosition, heightAboveFloor);
            //     // Preserve horizontal offset but use floor-aligned height
            //     finalPosition.y = floorAlignedPosition.y;
            // }

            transform.position = finalPosition;
            
            // Point directly at the camera (user)
            Vector3 directionToCamera = _headsetCamera.transform.position - transform.position;
            if (directionToCamera != Vector3.zero)
            {
                transform.rotation = Quaternion.LookRotation(directionToCamera, up);
            }
        }

        /// <summary>
        /// Show the skip button (call when video starts)
        /// </summary>
        public void Show()
        {
            SetVisible(true);
        }

        /// <summary>
        /// Hide the skip button (call when video ends)
        /// </summary>
        public void Hide()
        {
            SetVisible(false);
        }

        private void SetVisible(bool visible)
        {
            _isVisible = visible;
            if (buttonObject != null)
            {
                buttonObject.SetActive(visible);
            }
        }

        /// <summary>
        /// Set the button's onClick action
        /// </summary>
        public void SetOnClick(UnityEngine.Events.UnityAction action)
        {
            if (buttonComponent != null)
            {
                buttonComponent.onClick.RemoveAllListeners();
                buttonComponent.onClick.AddListener(action);
            }
        }
    }
}
