using System.Collections;
using System.IO;
using UnityEngine;
using TMPro;
using UnityEngine.UI;

/// <summary>
/// Loading screen component with progress bar, spinner, and De Dijk branding.
/// Displays loading messages and progress during async operations.
/// Also handles download progress from CDNDownloadManager.
/// </summary>
public class LoadingScreen : MonoBehaviour
{
    [Header("UI References")]
    [SerializeField] private GameObject loadingPanel;
    [SerializeField] private TMP_Text loadingText;
    [SerializeField] private Image progressBarFill;
    [SerializeField] private GameObject progressBarContainer;
    [SerializeField] private GameObject spinner;
    [SerializeField] private TMP_Text taglineText;
    [SerializeField] private TMP_Text fileCountText; // Optional: Shows "X/Y files" during downloads
    
    [Header("Settings")]
    [Tooltip("Default message when no specific message is provided")]
    [SerializeField] private string defaultLoadingMessage = "Laden...";
    
    [Tooltip("Distance from camera in meters")]
    [SerializeField] private float distanceFromCamera = 2f;
    
    [Tooltip("Height offset from camera center in meters")]
    [SerializeField] private float heightOffset = 0f;
    
    [Header("Floor Alignment")]
    [Tooltip("Use floor-based height calculation")]
    [SerializeField] private bool useFloorAlignment = true;
    
    [Tooltip("Height above floor in meters (if useFloorAlignment is true, 1.3m = comfortable reading height)")]
    [SerializeField] private float heightAboveFloor = 1.3f;
    
    [Tooltip("Update frequency in seconds (how often to refresh display during downloads)")]
    [SerializeField] private float updateInterval = 0.1f; // Update 10 times per second
    
    private Camera _headsetCamera;
    private CoroutineTracker _coroutineTracker;
    private bool _isVisible = false;
    
    // Download state
    private CDNDownloadManager _downloadManager;
    private string _currentDownloadingFile = "";
    private float _currentProgress = 0f;
    private int _totalFiles = 0;
    private int _completedFiles = 0;
    private float _lastUpdateTime = 0f;
    private bool _isDownloadMode = false;
    
    private void Awake()
    {
        // Ensure FloorAlignment exists if using floor alignment
        // if (useFloorAlignment && FloorAlignment.Instance == null)
        // {
        //     GameObject floorAlignmentObj = new GameObject("FloorAlignment");
        //     floorAlignmentObj.AddComponent<FloorAlignment>();
        // }
        
        // Remove OVROverlayCanvas components (incompatible with URP, causes flickering)
        RemoveOVROverlayCanvasComponents();
        
        // Get or add CoroutineTracker for performance optimization
        _coroutineTracker = GetComponent<CoroutineTracker>();
        if (_coroutineTracker == null)
        {
            _coroutineTracker = gameObject.AddComponent<CoroutineTracker>();
        }
        
        // Find headset camera
        _headsetCamera = Camera.main;
        if (_headsetCamera == null)
        {
            GameObject ovrCameraRig = GameObject.Find("OVRCameraRig");
            if (ovrCameraRig != null)
            {
                _headsetCamera = ovrCameraRig.GetComponentInChildren<Camera>();
            }
        }
        
        // Get download manager reference
        CDNDownloadManager.EnsureInstanceExists();
        _downloadManager = CDNDownloadManager.Instance;
        
        // Setup progress bar fill type if assigned
        if (progressBarFill != null)
        {
            progressBarFill.type = Image.Type.Filled;
            progressBarFill.fillMethod = Image.FillMethod.Horizontal;
            progressBarFill.fillOrigin = 0; // Left
            progressBarFill.fillAmount = 0f; // Start empty
        }
        
        // Apply De Dijk styling
        ApplyDeDijkStyling();
        
        // Initially hide
        if (loadingPanel != null)
        {
            loadingPanel.SetActive(false);
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
    }
    
    /// <summary>
    /// Applies De Dijk design system styling to all UI elements.
    /// </summary>
    private void ApplyDeDijkStyling()
    {
        // Style tagline text
        // if (taglineText != null)
        // {
        //     taglineText.text = DeDijkUIDesignSystem.SchoolTagline;
        //     taglineText.color = DeDijkUIDesignSystem.DeDijkOrange;
        //     taglineText.fontSize = DeDijkUIDesignSystem.SmallSize * 1000f;
        // }
        
        // // Style loading text
        // if (loadingText != null)
        // {
        //     loadingText.color = DeDijkUIDesignSystem.DeDijkDarkBlue;
        //     loadingText.fontSize = DeDijkUIDesignSystem.BodySize * 1000f;
        // }
        
        // // Style file count text (for downloads)
        // if (fileCountText != null)
        // {
        //     fileCountText.color = DeDijkUIDesignSystem.DeDijkDarkBlue;
        //     fileCountText.fontSize = DeDijkUIDesignSystem.SmallSize * 1000f;
        // }
        
        // // Style progress bar
        // if (progressBarFill != null)
        // {
        //     progressBarFill.color = DeDijkUIDesignSystem.DeDijkOrange;
        // }
    }
    
    private void Update()
    {
        // Update position to always be in front of headset
        if (_headsetCamera != null && loadingPanel != null && _isVisible)
        {
            UpdatePosition();
        }
        
        // Throttle UI updates for performance during downloads
        if (_isVisible && _isDownloadMode && Time.time - _lastUpdateTime >= updateInterval)
        {
            UpdateDownloadDisplay();
            _lastUpdateTime = Time.time;
        }
    }
    
    /// <summary>
    /// Updates the loading screen position to be in front of the camera.
    /// </summary>
    private void UpdatePosition()
    {
        if (_headsetCamera == null || loadingPanel == null) return;
        
        Vector3 cameraPosition = _headsetCamera.transform.position;
        Vector3 cameraForward = _headsetCamera.transform.forward;
        Vector3 cameraUp = _headsetCamera.transform.up;
        
        // Position in front of camera
        Vector3 targetPosition = cameraPosition + cameraForward * distanceFromCamera;
        
        // Apply floor alignment if enabled, otherwise use height offset
        // if (useFloorAlignment && FloorAlignment.Instance != null)
        // {
        //     targetPosition = FloorAlignment.Instance.GetPositionAtHeight(targetPosition, heightAboveFloor);
        // }
        // else
        // {
        //     // Fallback: use height offset
        //     targetPosition += cameraUp * heightOffset;
        // }
        
        // Face the camera
        loadingPanel.transform.position = targetPosition;
        loadingPanel.transform.LookAt(cameraPosition);
        loadingPanel.transform.Rotate(0, 180, 0); // Flip to face camera
    }
    
    /// <summary>
    /// Shows the loading screen with a message and optional progress.
    /// </summary>
    /// <param name="message">Loading message to display</param>
    /// <param name="showProgress">Whether to show the progress bar</param>
    /// <param name="progress">Initial progress value (0-1)</param>
    public void ShowLoading(string message = null, bool showProgress = true, float progress = 0f)
    {
        if (loadingPanel == null)
        {
            Debug.LogWarning("[LoadingScreen] Loading panel not assigned");
            return;
        }
        
        // Set message
        // if (loadingText != null)
        // {
        //     loadingText.text = string.IsNullOrEmpty(message) ? defaultLoadingMessage : message;
        // }
        
        // Show/hide progress bar
        if (progressBarContainer != null)
        {
            progressBarContainer.SetActive(showProgress);
        }
        
        // Set initial progress
        if (showProgress && progressBarFill != null)
        {
            progressBarFill.fillAmount = Mathf.Clamp01(progress);
        }
        
        // Show spinner
        if (spinner != null)
        {
            spinner.SetActive(true);
        }
        
        // Activate panel
        loadingPanel.SetActive(true);
        _isVisible = true;
        
        // Update position immediately
        UpdatePosition();
    }
    
    /// <summary>
    /// Updates the progress bar value.
    /// </summary>
    /// <param name="progress">Progress value (0-1)</param>
    /// <param name="message">Optional message to update</param>
    public void UpdateProgress(float progress, string message = null)
    {
        if (!_isVisible || loadingPanel == null || !loadingPanel.activeSelf)
        {
            return;
        }
        
        // Update progress bar
        if (progressBarFill != null)
        {
            progressBarFill.fillAmount = Mathf.Clamp01(progress);
        }
        
        // Update message if provided
        // if (!string.IsNullOrEmpty(message) && loadingText != null)
        // {
        //     loadingText.text = message;
        // }
    }
    
    /// <summary>
    /// Hides the loading screen with optional fade-out animation.
    /// </summary>
    /// <param name="fadeOut">Whether to fade out (future enhancement)</param>
    public void Hide(bool fadeOut = false)
    {
        if (loadingPanel == null) return;
        
        if (fadeOut)
        {
            // Future: Add fade-out coroutine
            _coroutineTracker.StartTrackedCoroutine("fadeOut", FadeOutCoroutine());
        }
        else
        {
            loadingPanel.SetActive(false);
            _isVisible = false;
        }
    }
    
    /// <summary>
    /// Coroutine for fade-out animation (future enhancement).
    /// </summary>
    private IEnumerator FadeOutCoroutine()
    {
        // TODO: Implement fade-out animation
        yield return new WaitForSeconds(0.3f);
        loadingPanel.SetActive(false);
        _isVisible = false;
    }
    
    /// <summary>
    /// Shows loading screen and automatically updates progress based on async operation.
    /// </summary>
    /// <param name="message">Loading message</param>
    /// <param name="operation">Async operation to track (0-1 progress)</param>
    public void ShowLoadingWithProgress(string message, AsyncOperation operation)
    {
        ShowLoading(message, showProgress: true, progress: 0f);
        
        _coroutineTracker.StartTrackedCoroutine("progressUpdate", UpdateProgressCoroutine(operation));
    }
    
    /// <summary>
    /// Coroutine that updates progress based on AsyncOperation.
    /// </summary>
    private IEnumerator UpdateProgressCoroutine(AsyncOperation operation)
    {
        while (!operation.isDone)
        {
            UpdateProgress(operation.progress);
            yield return null;
        }
        
        // Ensure 100% when done
        UpdateProgress(1f);
        yield return new WaitForSeconds(0.2f);
        Hide();
    }
    
    /// <summary>
    /// Handles download progress events from CDNDownloadManager.
    /// </summary>
    private void OnDownloadProgress(string fileName, float progress)
    {
        _currentDownloadingFile = fileName;
        _currentProgress = progress;
        _isDownloadMode = true;
        
        // Show loading screen if not already visible
        if (!_isVisible)
        {
            ShowLoading("Downloading files...", showProgress: true, progress: 0f);
        }
        
        UpdateDownloadDisplay();
    }
    
    /// <summary>
    /// Handles download complete events from CDNDownloadManager.
    /// </summary>
    private void OnDownloadComplete(string fileName)
    {
        _completedFiles++;
        UpdateDownloadDisplay();
        
        // Hide if all downloads complete
        if (_totalFiles > 0 && _completedFiles >= _totalFiles)
        {
            // Wait a moment before hiding
            _coroutineTracker.StartTrackedCoroutine("hideAfterDownload", HideAfterDelay(1f));
        }
    }
    
    /// <summary>
    /// Handles batch progress events from CDNDownloadManager.
    /// </summary>
    private void OnBatchProgress(int downloaded, int total)
    {
        _completedFiles = downloaded;
        _totalFiles = total;
        _isDownloadMode = true;
        
        // Show loading screen if downloads are starting
        if (total > 0 && !_isVisible)
        {
            ShowLoading("Preparing download...", showProgress: true, progress: 0f);
        }
        
        UpdateDownloadDisplay();
    }
    
    /// <summary>
    /// Updates the display with current download information.
    /// </summary>
    private void UpdateDownloadDisplay()
    {
        if (!_isVisible || loadingPanel == null || !loadingPanel.activeSelf)
        {
            return;
        }
        
        // Update loading text with current file name
        // if (loadingText != null)
        // {
        //     if (!string.IsNullOrEmpty(_currentDownloadingFile))
        //     {
        //         // Show just the filename (not full path) for cleaner display
        //         string fileName = Path.GetFileName(_currentDownloadingFile);
        //         // Clean up filename - remove path separators and special characters
        //         fileName = fileName.Replace("(current)", "").Trim();
        //         // Remove extra spaces
        //         while (fileName.Contains("  "))
        //         {
        //             fileName = fileName.Replace("  ", " ");
        //         }
        //         loadingText.text = string.IsNullOrEmpty(fileName) ? "Downloading..." : $"Downloading: {fileName}";
        //     }
        //     else if (_totalFiles > 0)
        //     {
        //         loadingText.text = "Preparing download...";
        //     }
        // }
        
        // Update progress bar
        if (progressBarFill != null)
        {
            progressBarFill.fillAmount = Mathf.Clamp01(_currentProgress);
        }
        
        // Update file count text
        // if (fileCountText != null)
        // {
        //     if (_totalFiles > 0)
        //     {
        //         fileCountText.text = $"{_completedFiles}/{_totalFiles} files";
        //         fileCountText.gameObject.SetActive(true);
        //     }
        //     else
        //     {
        //         fileCountText.gameObject.SetActive(false);
        //     }
        // }
    }
    
    /// <summary>
    /// Coroutine to hide loading screen after a delay.
    /// </summary>
    private IEnumerator HideAfterDelay(float delay)
    {
        yield return new WaitForSeconds(delay);
        _isDownloadMode = false;
        Hide();
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
                Debug.Log("[LoadingScreen] Removed OVROverlayCanvas (incompatible with URP - using optimized WorldSpace Canvas instead)");
            }
            
            // Remove from children (including loadingPanel)
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
        Canvas canvas = GetComponent<Canvas>();
        if (canvas == null)
        {
            canvas = GetComponentInChildren<Canvas>();
        }
        
        if (canvas != null)
        {
            // Set to WorldSpace (required for VR, works with URP)
            canvas.renderMode = RenderMode.WorldSpace;
            
            // Assign camera for proper rendering
            if (canvas.worldCamera == null && _headsetCamera != null)
            {
                canvas.worldCamera = _headsetCamera;
            }
            
            // Optimize Canvas settings for performance
            canvas.pixelPerfect = false; // Disable for better performance
            canvas.sortingOrder = 0; // Keep default sorting
            
            // Add CanvasScaler for proper VR scaling (if not present)
            var scaler = canvas.GetComponent<UnityEngine.UI.CanvasScaler>();
            if (scaler == null)
            {
                scaler = canvas.gameObject.AddComponent<UnityEngine.UI.CanvasScaler>();
                scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ConstantPixelSize;
                scaler.scaleFactor = 0.001f; // Scale for VR (1mm per pixel)
            }
        }
    }
    
    private void OnDestroy()
    {
        // Unsubscribe from download events
        if (_downloadManager != null)
        {
            _downloadManager.OnDownloadProgress -= OnDownloadProgress;
            _downloadManager.OnDownloadComplete -= OnDownloadComplete;
            _downloadManager.OnBatchProgress -= OnBatchProgress;
        }
        
        // Clean up tracked coroutines
        if (_coroutineTracker != null)
        {
            _coroutineTracker.StopAllTrackedCoroutines();
        }
    }
}
