using System.Collections;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace com.dnw.standardpackage
{

    /// <summary>
    /// 3D button component that feels like a physical object.
    /// Supports press animation, haptic feedback, and smart positioning for quiz buttons.
    /// </summary>
    public class VR3DButton : XRSimpleInteractable
    {
        [Header("Button Settings")]
        [Tooltip("Scale factor when pressed (0.9 = 90% size)")]
        [SerializeField] private float pressScale = 0.9f;
        
        [Tooltip("Distance from headset in meters (30cm = 0.3m)")]
        [SerializeField] private float distanceFromHeadset = 0.3f;
        
        [Header("Floor Alignment")]
        [Tooltip("Use floor-based height calculation")]
        [SerializeField] private bool useFloorAlignment = true;
        
        [Tooltip("Height above floor in meters (if useFloorAlignment is true, 1.2m = comfortable button height)")]
        [SerializeField] private float heightAboveFloor = 1.2f;

        [Header("Smart Positioning (for Quiz Buttons)")]
        [Tooltip("Enable smart positioning - locks when user looks at button")]
        [SerializeField] private bool smartPositioning = false;
        
        [Tooltip("Gaze angle threshold for smart lock (degrees)")]
        [SerializeField] private float gazeAngleThreshold = 30f;

        [Header("Visual Feedback")]
        [SerializeField] private Color normalColor = Color.white;
        [SerializeField] private Color pressedColor = new Color(0.8f, 0.8f, 0.8f);
        [SerializeField] private Renderer buttonRenderer;
        [SerializeField] private Material buttonMaterial;
        
        [Header("3D Button Appearance")]
        [Tooltip("Automatically create 3D mesh if button doesn't have a Renderer")]
        [SerializeField] private bool autoCreate3DMesh = true;
        [Tooltip("Depth of 3D button in meters (how thick it appears)")]
        [SerializeField] private float buttonDepth = 0.02f;
        [Tooltip("Button width in meters (default: 0.2m = 20cm)")]
        [SerializeField] private float buttonWidth = 0.2f;
        [Tooltip("Button height in meters (default: 0.1m = 10cm)")]
        [SerializeField] private float buttonHeight = 0.1f;
        
        [Header("Animation Settings")]
        [Tooltip("Duration of press/release scale animation in seconds")]
        [SerializeField] private float pressAnimationDuration = 0.1f;
        [Tooltip("Duration of color transition in seconds")]
        [SerializeField] private float colorAnimationDuration = 0.15f;

        private Camera _headsetCamera;
        private Vector3 _originalScale;
        private bool _isPressed = false;
        private bool _isLocked = false; // For smart positioning
        private Vector3 _lockedPosition;
        private Quaternion _lockedRotation;
        
        // Animation coroutines
        private Coroutine _scaleAnimationCoroutine;
        private Coroutine _colorAnimationCoroutine;
        
        // Cached reference to OVRCameraRig (replacing GameObject.Find() calls)
        private GameObject _ovrCameraRig;

        protected override void Awake()
        {
            base.Awake();
            
            // Cache OVRCameraRig reference
            CacheOVRCameraRig();
            
            // Find headset camera
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
            
            // Store original scale
            _originalScale = transform.localScale;

            // Get renderer and material
            if (buttonRenderer == null)
            {
                buttonRenderer = GetComponent<Renderer>();
            }

            // If no renderer found and auto-create is enabled, create a 3D mesh
            if (buttonRenderer == null && autoCreate3DMesh)
            {
                Create3DButtonMesh();
            }

            if (buttonRenderer != null && buttonMaterial == null)
            {
                buttonMaterial = buttonRenderer.material;
            }

            // Apply De Dijk styling
            // ApplyDeDijkStyling();
        }
        
        /// <summary>
        /// Creates a 3D cube mesh for the button to give it depth and a 3D appearance.
        /// </summary>
        private void Create3DButtonMesh()
        {
            // Create a cube primitive as the button mesh
            GameObject cubeMesh = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cubeMesh.name = "Button3DMesh";
            cubeMesh.transform.SetParent(transform);
            cubeMesh.transform.localPosition = Vector3.zero;
            cubeMesh.transform.localRotation = Quaternion.identity;
            
            // Set button dimensions
            cubeMesh.transform.localScale = new Vector3(buttonWidth, buttonHeight, buttonDepth);
            
            // Get the renderer from the cube
            buttonRenderer = cubeMesh.GetComponent<Renderer>();
            
            // Enable shadows for 3D appearance
            if (buttonRenderer != null)
            {
                buttonRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
                buttonRenderer.receiveShadows = true;
            }
            
            // Remove the collider from the cube (XR Interaction Toolkit handles interaction)
            Collider cubeCollider = cubeMesh.GetComponent<Collider>();
            if (cubeCollider != null)
            {
                Destroy(cubeCollider);
            }
            
            // Ensure this GameObject has a collider for XR Interaction Toolkit
            if (GetComponent<Collider>() == null)
            {
                BoxCollider boxCollider = gameObject.AddComponent<BoxCollider>();
                boxCollider.size = new Vector3(buttonWidth, buttonHeight, buttonDepth);
            }
            
            Debug.Log($"[VR3DButton]: Created 3D mesh for button {gameObject.name} (Size: {buttonWidth}x{buttonHeight}x{buttonDepth}m)");
        }

        /// <summary>
        /// Applies De Dijk design system styling to the button.
        /// </summary>
        // private void ApplyDeDijkStyling()
        // {
        //     // Use De Dijk colors for button states
        //     if (buttonMaterial != null)
        //     {
        //         // Primary buttons use De Dijk Dark Blue, secondary use De Dijk Yellow
        //         // normalColor = DeDijkUIDesignSystem.DeDijkDarkBlue;
        //         // Pressed state uses De Dijk Orange for visual feedback
        //         // pressedColor = DeDijkUIDesignSystem.DeDijkOrange;
        //         buttonMaterial.color = normalColor;
        //     }
        // }

        protected override void OnEnable()
        {
            base.OnEnable();
            transform.localScale = _originalScale;
            _isPressed = false;
            _isLocked = false;
        }

        private void Update()
        {
            // Update position if not locked and smart positioning enabled
            if (!_isLocked && _headsetCamera != null)
            {
                if (smartPositioning)
                {
                    UpdateSmartPosition();
                }
                else
                {
                    UpdateHeadsetRelativePosition();
                }
            }
        }

        protected override void OnSelectEntered(SelectEnterEventArgs args)
        {
            base.OnSelectEntered(args);
            PressButton(args);
        }

        protected override void OnSelectExited(SelectExitEventArgs args)
        {
            base.OnSelectExited(args);
            ReleaseButton();
        }

        private void PressButton(SelectEnterEventArgs args)
        {
            if (_isPressed) return;

            _isPressed = true;
            
            // Stop any existing animations
            if (_scaleAnimationCoroutine != null)
            {
                StopCoroutine(_scaleAnimationCoroutine);
            }
            if (_colorAnimationCoroutine != null)
            {
                StopCoroutine(_colorAnimationCoroutine);
            }
            
            // Smooth scale down animation
            _scaleAnimationCoroutine = StartCoroutine(AnimateScale(_originalScale * pressScale, pressAnimationDuration));
            
            // Smooth color transition
            if (buttonMaterial != null)
            {
                _colorAnimationCoroutine = StartCoroutine(AnimateColor(pressedColor, colorAnimationDuration));
            }

            // Haptic feedback
            if (args.interactorObject is XRDirectInteractor directInteractor)
            {
                // Trigger haptic feedback on Quest 3
                // Note: Haptic feedback implementation depends on XR Interaction Toolkit version
                // This is a placeholder - may need adjustment based on your XR ITK version
            }
        }

        private void ReleaseButton()
        {
            if (!_isPressed) return;

            _isPressed = false;
            
            // Stop any existing animations
            if (_scaleAnimationCoroutine != null)
            {
                StopCoroutine(_scaleAnimationCoroutine);
            }
            if (_colorAnimationCoroutine != null)
            {
                StopCoroutine(_colorAnimationCoroutine);
            }
            
            // Smooth scale back up (slightly longer for satisfying feel)
            _scaleAnimationCoroutine = StartCoroutine(AnimateScale(_originalScale, pressAnimationDuration * 1.5f));
            
            // Smooth color transition back to normal
            if (buttonMaterial != null)
            {
                _colorAnimationCoroutine = StartCoroutine(AnimateColor(normalColor, colorAnimationDuration));
            }
        }

        private void UpdateHeadsetRelativePosition()
        {
            if (_headsetCamera == null) return;

            Vector3 forward = _headsetCamera.transform.forward;
            Vector3 position = _headsetCamera.transform.position + (forward * distanceFromHeadset);
            
            // Apply floor alignment if enabled
            // if (useFloorAlignment && FloorAlignment.Instance != null)
            // {
            //     position = FloorAlignment.Instance.GetPositionAtHeight(position, heightAboveFloor);
            // }

            transform.position = position;
            transform.LookAt(_headsetCamera.transform);
        }

        private void UpdateSmartPosition()
        {
            if (_headsetCamera == null) return;

            // Check if user is looking at button
            Vector3 toButton = transform.position - _headsetCamera.transform.position;
            Vector3 forward = _headsetCamera.transform.forward;
            
            float angle = Vector3.Angle(forward, toButton);

            if (angle < gazeAngleThreshold && !_isLocked)
            {
                // User is looking at button - lock position
                _isLocked = true;
                _lockedPosition = transform.position;
                _lockedRotation = transform.rotation;
            }
            else if (angle >= gazeAngleThreshold && _isLocked)
            {
                // User looked away - unlock and follow headset again
                _isLocked = false;
                UpdateHeadsetRelativePosition();
            }
            else if (!_isLocked)
            {
                // Not locked - follow headset
                UpdateHeadsetRelativePosition();
            }
            // If locked, position stays at _lockedPosition (don't update)
        }

        /// <summary>
        /// Manually set button position (for fixed positioning like quiz board)
        /// </summary>
        public void SetFixedPosition(Vector3 position, Quaternion rotation)
        {
            smartPositioning = false;
            _isLocked = false;
            transform.position = position;
            transform.rotation = rotation;
        }

        /// <summary>
        /// Smoothly animates button scale with easing.
        /// </summary>
        private IEnumerator AnimateScale(Vector3 targetScale, float duration)
        {
            Vector3 startScale = transform.localScale;
            float elapsed = 0f;
            
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = elapsed / duration;
                // Use smooth easing curve for professional feel
                t = Mathf.SmoothStep(0f, 1f, t);
                transform.localScale = Vector3.Lerp(startScale, targetScale, t);
                yield return null;
            }
            
            transform.localScale = targetScale;
            _scaleAnimationCoroutine = null;
        }
        
        /// <summary>
        /// Smoothly animates button color with easing.
        /// </summary>
        private IEnumerator AnimateColor(Color targetColor, float duration)
        {
            if (buttonMaterial == null) yield break;
            
            Color startColor = buttonMaterial.color;
            float elapsed = 0f;
            
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = elapsed / duration;
                // Use smooth easing curve for professional feel
                t = Mathf.SmoothStep(0f, 1f, t);
                buttonMaterial.color = Color.Lerp(startColor, targetColor, t);
                yield return null;
            }
            
            buttonMaterial.color = targetColor;
            _colorAnimationCoroutine = null;
        }
    }
}