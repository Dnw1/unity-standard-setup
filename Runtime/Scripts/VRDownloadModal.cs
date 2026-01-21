using System.IO;
using TMPro;
using UnityEngine;

namespace com.dnw.standardpackage
{

    /// <summary>
    /// VR Modal component for displaying download progress.
    /// Shows current file being downloaded, progress percentage, and file count.
    /// Uses VRModal-style positioning (30cm in front of headset).
    /// </summary>
    public class VRDownloadModal : MonoBehaviour
    {
        [Header("UI References")]
        [SerializeField] private GameObject modalPanel;
        [SerializeField] private TextMeshProUGUI titleText; // "DOWNLOAD" title
        [SerializeField] private TextMeshProUGUI messageText;
        [SerializeField] private TextMeshProUGUI progressText;
        [SerializeField] private TextMeshProUGUI fileCountText;
        [SerializeField] private Canvas modalCanvas;
        [SerializeField] private UnityEngine.UI.Image progressBarFill; // Optional visual progress bar

        [Header("Settings")]
        [Tooltip("Distance from headset in meters (30cm = 0.3m)")]
        [SerializeField] private float distanceFromHeadset = 0.3f;
        
        [Header("Floor Alignment")]
        [Tooltip("Use floor-based height calculation")]
        [SerializeField] private bool useFloorAlignment = true;
        
        [Tooltip("Height above floor in meters (if useFloorAlignment is true, 1.3m = comfortable reading height)")]
        [SerializeField] private float heightAboveFloor = 1.3f;

        [Tooltip("Update frequency in seconds (how often to refresh display)")]
        [SerializeField] private float updateInterval = 0.1f; // Update 10 times per second

        private Camera _headsetCamera;
        private CDNDownloadManager _downloadManager;
        private string _currentDownloadingFile = "";
        private float _currentProgress = 0f;
        private int _totalFiles = 0;
        private int _completedFiles = 0;
        private float _lastUpdateTime = 0f;
        private bool _isVisible = false;
        
        // Cached reference to OVRCameraRig (replacing GameObject.Find() calls)
        private GameObject _ovrCameraRig;

        private void Awake()
        {
            // Ensure FloorAlignment exists if using floor alignment
            // if (useFloorAlignment && FloorAlignment.Instance == null)
            // {
            //     GameObject floorAlignmentObj = new GameObject("FloorAlignment");
            //     floorAlignmentObj.AddComponent<FloorAlignment>();
            // }
            
            // Cache OVRCameraRig reference
            CacheOVRCameraRig();
            
            // Find headset camera - try multiple methods for VR compatibility
            FindVRCamera();
            
            // Get download manager
            CDNDownloadManager.EnsureInstanceExists();
            _downloadManager = CDNDownloadManager.Instance;

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
                    // CanvasScaler for proper scaling in VR
                    if (GetComponent<UnityEngine.UI.CanvasScaler>() == null)
                    {
                        var scaler = gameObject.AddComponent<UnityEngine.UI.CanvasScaler>();
                        scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ConstantPixelSize;
                        scaler.scaleFactor = 0.001f; // Scale down for VR (1mm per pixel)
                    }
                    // GraphicRaycaster for interaction
                    if (GetComponent<UnityEngine.UI.GraphicRaycaster>() == null)
                    {
                        gameObject.AddComponent<UnityEngine.UI.GraphicRaycaster>();
                    }
                }
            }
            
            // Update canvas camera reference
            if (modalCanvas != null && _headsetCamera != null)
            {
                modalCanvas.worldCamera = _headsetCamera;
            }

            // Initially hidden
            if (modalPanel != null)
            {
                modalPanel.SetActive(false);
            }
        }
        
        /// <summary>
        /// Finds the VR camera using multiple methods for compatibility.
        /// </summary>
        private void FindVRCamera()
        {
            // Method 1: Try OVRCameraRig first (Meta Quest)
            if (_ovrCameraRig != null)
            {
                // Look for CenterEyeAnchor or Camera in children
                Transform centerEye = _ovrCameraRig.transform.Find("CenterEyeAnchor");
                if (centerEye != null)
                {
                    _headsetCamera = centerEye.GetComponent<Camera>();
                }
                
                // If not found, search all children
                if (_headsetCamera == null)
                {
                    _headsetCamera = _ovrCameraRig.GetComponentInChildren<Camera>();
                }
            }
            
            // Method 2: Try Camera.main (works in Game view)
            if (_headsetCamera == null)
            {
                _headsetCamera = Camera.main;
            }
            
            // Method 3: Find any active camera tagged as MainCamera
            if (_headsetCamera == null)
            {
                GameObject mainCamObj = GameObject.FindGameObjectWithTag("MainCamera");
                if (mainCamObj != null)
                {
                    _headsetCamera = mainCamObj.GetComponent<Camera>();
                }
            }
            
            // Method 4: Find any active camera in scene
            if (_headsetCamera == null)
            {
                Camera[] cameras = FindObjectsByType<Camera>(FindObjectsSortMode.None);
                foreach (Camera cam in cameras)
                {
                    if (cam.enabled && cam.gameObject.activeInHierarchy)
                    {
                        _headsetCamera = cam;
                        break;
                    }
                }
            }
            
            if (_headsetCamera == null)
            {
                Debug.LogWarning("[VRDownloadModal]: No camera found! Modal may not display correctly.");
            }
            else
            {
                Debug.Log($"[VRDownloadModal]: Using camera: {_headsetCamera.name}");
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
            // Subscribe to download events
            if (_downloadManager != null)
            {
                _downloadManager.OnDownloadProgress += OnDownloadProgress;
                _downloadManager.OnDownloadComplete += OnDownloadComplete;
                _downloadManager.OnBatchProgress += OnBatchProgress;
            }

            // Apply De Dijk styling
            // ApplyDeDijkStyling();
        }

        /// <summary>
        /// Applies De Dijk design system styling to the download modal.
        /// </summary>
        // private void ApplyDeDijkStyling()
        // {
        //     // Title text: De Dijk Dark Blue, heading size
        //     // if (titleText != null)
        //     // {
        //     //     titleText.color = DeDijkUIDesignSystem.DeDijkDarkBlue;
        //     //     titleText.fontSize = DeDijkUIDesignSystem.HeadingSize * 1000f; // Convert meters to font size units
        //     // }

        //     // // Message text: De Dijk Dark Blue, body size
        //     // if (messageText != null)
        //     // {
        //     //     messageText.color = DeDijkUIDesignSystem.DeDijkDarkBlue;
        //     //     messageText.fontSize = DeDijkUIDesignSystem.BodySize * 1000f;
        //     // }

        //     // // Progress text: De Dijk Orange for progress indicators
        //     // if (progressText != null)
        //     // {
        //     //     progressText.color = DeDijkUIDesignSystem.DeDijkOrange;
        //     //     progressText.fontSize = DeDijkUIDesignSystem.BodySize * 1000f;
        //     // }

        //     // // File count text: De Dijk Dark Blue, small size
        //     // if (fileCountText != null)
        //     // {
        //     //     fileCountText.color = DeDijkUIDesignSystem.DeDijkDarkBlue;
        //     //     fileCountText.fontSize = DeDijkUIDesignSystem.SmallSize * 1000f;
        //     // }

        //     // // Progress bar: De Dijk Orange color
        //     // if (progressBarFill != null)
        //     // {
        //     //     progressBarFill.color = DeDijkUIDesignSystem.DeDijkOrange;
        //     // }
        // }

        private void Update()
        {
            // Re-find camera if lost (for VR simulator compatibility)
            if (_headsetCamera == null || !_headsetCamera.gameObject.activeInHierarchy)
            {
                FindVRCamera();
                if (modalCanvas != null && _headsetCamera != null)
                {
                    modalCanvas.worldCamera = _headsetCamera;
                }
            }
            
            // Update position to always be in front of headset
            if (_headsetCamera != null && _isVisible)
            {
                UpdatePosition();
            }

            // Throttle UI updates for performance
            if (_isVisible && Time.time - _lastUpdateTime >= updateInterval)
            {
                UpdateDisplay();
                _lastUpdateTime = Time.time;
            }
        }

        private void OnDestroy()
        {
            if (_downloadManager != null)
            {
                _downloadManager.OnDownloadProgress -= OnDownloadProgress;
                _downloadManager.OnDownloadComplete -= OnDownloadComplete;
                _downloadManager.OnBatchProgress -= OnBatchProgress;
            }
        }

        private void OnDownloadProgress(string fileName, float progress)
        {
            _currentDownloadingFile = fileName;
            _currentProgress = progress;
            
            // Show modal if not already visible
            if (!_isVisible)
            {
                Show();
            }
        }

        private void OnDownloadComplete(string fileName)
        {
            _completedFiles++;
            
            // Update display
            UpdateDisplay();
            
            // Hide if all downloads complete
            if (_totalFiles > 0 && _completedFiles >= _totalFiles)
            {
                // Wait a moment before hiding
                StartCoroutine(HideAfterDelay(2f));
            }
        }

        private void OnBatchProgress(int downloaded, int total)
        {
            _completedFiles = downloaded;
            _totalFiles = total;
            
            // Show modal if downloads are starting
            if (total > 0 && !_isVisible)
            {
                Show();
            }
        }

        private void Show()
        {
            if (modalPanel != null)
            {
                modalPanel.SetActive(true);
                _isVisible = true;
                UpdatePosition();
                UpdateDisplay();
            }
        }

        private void Hide()
        {
            if (modalPanel != null)
            {
                modalPanel.SetActive(false);
                _isVisible = false;
            }
        }

        private System.Collections.IEnumerator HideAfterDelay(float delay)
        {
            yield return new WaitForSeconds(delay);
            Hide();
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

        private void UpdateDisplay()
        {
            // Update title text (always show "DOWNLOAD")
            // if (titleText != null)
            // {
            //     titleText.text = "DOWNLOAD";
            // }
            
            // Update message text with current file name
            // if (messageText != null)
            // {
            //     if (!string.IsNullOrEmpty(_currentDownloadingFile))
            //     {
            //         // Show just the filename (not full path) for cleaner display
            //         // Clean up filename - remove path separators and special characters that might cause display issues
            //         string fileName = Path.GetFileName(_currentDownloadingFile);
            //         // Remove any "(current)" or similar markers that might be in the path
            //         fileName = fileName.Replace("(current)", "").Trim();
            //         // Remove extra spaces
            //         while (fileName.Contains("  "))
            //         {
            //             fileName = fileName.Replace("  ", " ");
            //         }
            //         messageText.text = string.IsNullOrEmpty(fileName) ? "Downloading..." : $"Downloading: {fileName}";
            //     }
            //     else
            //     {
            //         messageText.text = "Preparing download...";
            //     }
            // }

            // // Update progress text (percentage)
            // if (progressText != null)
            // {
            //     progressText.text = $"{(_currentProgress * 100f):F0}%";
            // }

            // // Update file count text
            // if (fileCountText != null && _totalFiles > 0)
            // {
            //     fileCountText.text = $"{_completedFiles}/{_totalFiles} files";
            // }
            // else if (fileCountText != null)
            // {
            //     fileCountText.text = "";
            // }

            // Update progress bar fill (if assigned)
            if (progressBarFill != null)
            {
                RectTransform fillRect = progressBarFill.GetComponent<RectTransform>();
                if (fillRect != null)
                {
                    // Scale fill based on progress
                    fillRect.anchorMax = new Vector2(_currentProgress, 1f);
                }
            }
        }
    }
}