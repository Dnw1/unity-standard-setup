using System.Collections;
using TMPro;
using UnityEngine;

namespace com.dnw.standardpackage
{

    /// <summary>
    /// VR Modal component for displaying critical information to users.
    /// Always positioned in front of headset, auto-dismisses after display time.
    /// </summary>
    public class VRModal : MonoBehaviour
    {
        [Header("UI References")]
        [SerializeField] private GameObject modalPanel;
        [SerializeField] private TextMeshProUGUI messageText;
        [SerializeField] private Canvas modalCanvas;

        [Header("Settings")]
        [Tooltip("Default display duration in seconds (0 = use default)")]
        [SerializeField] private float defaultDisplayDuration = 3f;
        
        [Tooltip("Distance from headset in meters (30cm = 0.3m)")]
        [SerializeField] private float distanceFromHeadset = 0.3f;

        [Tooltip("Fade in/out duration in seconds")]
        [SerializeField] private float fadeDuration = 0.3f;
        
        [Header("Floor Alignment")]
        [Tooltip("Use floor-based height calculation")]
        [SerializeField] private bool useFloorAlignment = true;
        
        [Tooltip("Height above floor in meters (if useFloorAlignment is true, 1.3m = comfortable reading height)")]
        [SerializeField] private float heightAboveFloor = 1.3f;

        private Camera _headsetCamera;
        private Coroutine _showCoroutine;
        private Coroutine _fadeCoroutine;
        private CanvasGroup _canvasGroup;
        
        // Cached reference to OVRCameraRig (replacing GameObject.Find() calls)
        private GameObject _ovrCameraRig;

        private void Awake()
        {
            // Remove OVROverlayCanvas components (incompatible with URP, causes flickering)
            RemoveOVROverlayCanvasComponents();
            
            // Cache OVRCameraRig reference
            CacheOVRCameraRig();
            
            // Find headset camera (cache in Awake, not Update)
            _headsetCamera = Camera.main;
            if (_headsetCamera == null && _ovrCameraRig != null)
            {
                _headsetCamera = _ovrCameraRig.GetComponentInChildren<Camera>();
            }
            
            // Auto-find Canvas if not assigned
            if (modalCanvas == null)
            {
                modalCanvas = GetComponent<Canvas>();
                if (modalCanvas == null)
                {
                    modalCanvas = GetComponentInChildren<Canvas>();
                }
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
        
        /// <summary>
        /// Removes OVROverlayCanvas components and optimizes Canvas for URP + Quest 3.
        /// OVROverlayCanvas is recommended for Built-in Render Pipeline but causes flickering with URP.
        /// Best practice for URP: Use WorldSpace Canvas with proper optimization settings.
        /// </summary>
        private void RemoveOVROverlayCanvasComponents()
        {
            // Use reflection to find and remove OVROverlayCanvas components
            System.Type overlayCanvasType = System.Type.GetType("OVROverlayCanvas, Assembly-CSharp");
            if (overlayCanvasType == null)
            {
                overlayCanvasType = System.Type.GetType("OVROverlayCanvas, com.meta.xr.sdk.core");
            }
            
            if (overlayCanvasType != null)
            {
                // Remove from this GameObject
                var component = GetComponent(overlayCanvasType);
                if (component != null)
                {
                    DestroyImmediate(component);
                    Debug.Log("[VRModal] Removed OVROverlayCanvas (incompatible with URP - using optimized WorldSpace Canvas instead)");
                }
                
                // Remove from children
                var components = GetComponentsInChildren(overlayCanvasType, true);
                foreach (var comp in components)
                {
                    if (comp != null)
                    {
                        DestroyImmediate(comp);
                    }
                }
            }
            
            // Optimize Canvas for URP + Quest 3
            OptimizeCanvasForURP();
        }
        
        /// <summary>
        /// Optimizes Canvas settings for URP + Quest 3 VR performance.
        /// </summary>
        private void OptimizeCanvasForURP()
        {
            if (modalCanvas != null)
            {
                // Set to WorldSpace (required for VR, works with URP)
                modalCanvas.renderMode = RenderMode.WorldSpace;
                
                // Assign camera for proper rendering
                if (modalCanvas.worldCamera == null && _headsetCamera != null)
                {
                    modalCanvas.worldCamera = _headsetCamera;
                }
                
                // Optimize Canvas settings for performance
                modalCanvas.pixelPerfect = false; // Disable for better performance
                modalCanvas.sortingOrder = 0; // Keep default sorting
                
                // Add CanvasScaler for proper VR scaling (if not present)
                var scaler = modalCanvas.GetComponent<UnityEngine.UI.CanvasScaler>();
                if (scaler == null)
                {
                    scaler = modalCanvas.gameObject.AddComponent<UnityEngine.UI.CanvasScaler>();
                    scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ConstantPixelSize;
                    scaler.scaleFactor = 0.001f; // Scale for VR (1mm per pixel)
                }
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
            if (modalPanel == null)
            {
                modalPanel = gameObject;
            }

            if (modalCanvas == null)
            {
                modalCanvas = GetComponent<Canvas>();
                if (modalCanvas == null)
                {
                    modalCanvas = gameObject.AddComponent<Canvas>();
                    modalCanvas.renderMode = RenderMode.WorldSpace;
                    modalCanvas.worldCamera = _headsetCamera;
                }
            }

            // Get or add CanvasGroup for fading
            _canvasGroup = GetComponent<CanvasGroup>();
            if (_canvasGroup == null)
            {
                _canvasGroup = gameObject.AddComponent<CanvasGroup>();
            }

            // Apply De Dijk styling to text
            // ApplyDeDijkStyling();

            // Initially hidden
            if (modalPanel != null)
            {
                modalPanel.SetActive(false);
            }
            _canvasGroup.alpha = 0f;
        }

        /// <summary>
        /// Applies De Dijk design system styling to the modal.
        /// </summary>
        // private void ApplyDeDijkStyling()
        // {
        //     if (messageText != null)
        //     {
        //         // Use De Dijk dark blue for text color
        //         messageText.color = DeDijkUIDesignSystem.DeDijkDarkBlue;
        //         // Use De Dijk body size for modal text with accessibility multiplier
        //         // TextMeshProUGUI uses point size, so we multiply by 1000 to convert from meters
        //         float baseSize = DeDijkUIDesignSystem.BodySize * 1000f;
        //         messageText.fontSize = DeDijkUIDesignSystem.GetAccessibleTextSize(DeDijkUIDesignSystem.BodySize) * 1000f;
                
        //         // Store original size for accessibility settings
        //         if (messageText.gameObject.GetComponent<TextSizeData>() == null)
        //         {
        //             TextSizeData sizeData = messageText.gameObject.AddComponent<TextSizeData>();
        //             sizeData.originalFontSize = baseSize;
        //         }
        //     }
        // }

        private void Update()
        {
            // Update position to always be in front of headset
            if (_headsetCamera != null && modalPanel != null && modalPanel.activeSelf)
            {
                UpdatePosition();
            }
        }

        /// <summary>
        /// Shows the modal with a message. Auto-dismisses after duration.
        /// </summary>
        /// <param name="message">Message to display</param>
        /// <param name="duration">Display duration in seconds (0 = use default)</param>
        public void Show(string message, float duration = 0f)
        {
            if (string.IsNullOrEmpty(message))
            {
                Debug.LogWarning("[VRModal]: Cannot show modal with empty message");
                return;
            }

            // Stop any existing show coroutine
            if (_showCoroutine != null)
            {
                StopCoroutine(_showCoroutine);
            }

            _showCoroutine = StartCoroutine(ShowCoroutine(message, duration > 0 ? duration : defaultDisplayDuration));
        }

        /// <summary>
        /// Hides the modal immediately.
        /// </summary>
        public void Hide()
        {
            if (_showCoroutine != null)
            {
                StopCoroutine(_showCoroutine);
                _showCoroutine = null;
            }

            if (_fadeCoroutine != null)
            {
                StopCoroutine(_fadeCoroutine);
                _fadeCoroutine = null;
            }

            if (gameObject.activeInHierarchy && enabled)
            {
                StartCoroutine(FadeOutCoroutine());
            }
        }

        private IEnumerator ShowCoroutine(string message, float duration)
        {
            // Set message text
            // if (messageText != null)
            // {
            //     messageText.text = message;
            // }

            // Show panel
            if (modalPanel != null)
            {
                modalPanel.SetActive(true);
            }

            // Update position
            UpdatePosition();

            // Fade in
            yield return StartCoroutine(FadeInCoroutine());

            // Wait for display duration
            yield return new WaitForSeconds(duration);

            // Fade out and hide
            yield return StartCoroutine(FadeOutCoroutine());
        }

        private IEnumerator FadeInCoroutine()
        {
            if (_fadeCoroutine != null)
            {
                StopCoroutine(_fadeCoroutine);
            }

            _fadeCoroutine = StartCoroutine(FadeCoroutine(0f, 1f, fadeDuration));
            yield return _fadeCoroutine;
            _fadeCoroutine = null;
        }

        private IEnumerator FadeOutCoroutine()
        {
            if (_fadeCoroutine != null)
            {
                StopCoroutine(_fadeCoroutine);
            }

            _fadeCoroutine = StartCoroutine(FadeCoroutine(_canvasGroup.alpha, 0f, fadeDuration));
            yield return _fadeCoroutine;
            _fadeCoroutine = null;

            // Hide panel after fade
            if (modalPanel != null)
            {
                modalPanel.SetActive(false);
            }
        }

        private IEnumerator FadeCoroutine(float startAlpha, float endAlpha, float duration)
        {
            // Ensure _canvasGroup is initialized (in case Start() hasn't run yet)
            if (_canvasGroup == null)
            {
                _canvasGroup = GetComponent<CanvasGroup>();
                if (_canvasGroup == null)
                {
                    _canvasGroup = gameObject.AddComponent<CanvasGroup>();
                }
            }
            
            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                if (_canvasGroup != null)
                {
                    _canvasGroup.alpha = Mathf.Lerp(startAlpha, endAlpha, t);
                }
                yield return null;
            }
            if (_canvasGroup != null)
            {
                _canvasGroup.alpha = endAlpha;
            }
        }

        private void UpdatePosition()
        {
            if (_headsetCamera == null) return;

            // Position 30cm in front of headset
            Vector3 forward = _headsetCamera.transform.forward;
            Vector3 position = _headsetCamera.transform.position + (forward * distanceFromHeadset);
            
            // Apply floor alignment if enabled
            // if (useFloorAlignment && FloorAlignment.Instance != null)
            // {
            //     position = FloorAlignment.Instance.GetPositionAtHeight(position, heightAboveFloor);
            // }

            transform.position = position;
            
            // Face the camera
            Vector3 directionToCamera = _headsetCamera.transform.position - transform.position;
            if (directionToCamera != Vector3.zero)
            {
                transform.rotation = Quaternion.LookRotation(-directionToCamera, _headsetCamera.transform.up);
            }
        }

        private void OnDestroy()
        {
            if (_showCoroutine != null)
            {
                StopCoroutine(_showCoroutine);
            }
            if (_fadeCoroutine != null)
            {
                StopCoroutine(_fadeCoroutine);
            }
        }
    }
}