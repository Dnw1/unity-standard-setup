using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Central manager for all VR UI components.
/// Coordinates modals, buttons, and other UI elements.
/// </summary>
public class VRUIManager : MonoBehaviour
{
    public static VRUIManager Instance { get; private set; }

    [Header("UI Component References")]
    [SerializeField] private VRModal modalPrefab;
    [SerializeField] private VRSkipButton skipButtonPrefab;
    [SerializeField] private VRDownloadModal downloadModalPrefab; // DEPRECATED: Use LoadingScreen for downloads instead
    // [SerializeField] private DeDijkInstructionDisplay instructionDisplayPrefab;
    [SerializeField] private LoadingScreen loadingScreenPrefab; // Handles both general loading and download progress

    private Camera _headsetCamera;
    private VRModal _currentModal;
    private VRSkipButton _currentSkipButton;
    private VRDownloadModal _currentDownloadModal;
    // private DeDijkInstructionDisplay _currentInstructionDisplay;
    private LoadingScreen _currentLoadingScreen;
    private List<VR3DButton> _activeButtons = new List<VR3DButton>();
    
    // Object pooling for performance optimization
    private ObjectPool<VRModal> _modalPool;
    private ObjectPool<VRSkipButton> _skipButtonPool;
    private ObjectPool<VRDownloadModal> _downloadModalPool;
    
    // Cached reference to OVRCameraRig (replacing GameObject.Find() calls)
    private GameObject _ovrCameraRig;

    private void Awake()
    {
        // Singleton pattern
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);

        // Initialize object pools for UI elements
        InitializeObjectPools();

        // Cache OVRCameraRig reference
        CacheOVRCameraRig();
        
        // Find headset camera (cache in Awake)
        _headsetCamera = Camera.main;
        if (_headsetCamera == null && _ovrCameraRig != null)
        {
            _headsetCamera = _ovrCameraRig.GetComponentInChildren<Camera>();
        }
    }
    
    /// <summary>
    /// Initializes object pools for UI elements to reduce GC spikes.
    /// </summary>
    private void InitializeObjectPools()
    {
        // Initialize modal pool
        if (modalPrefab != null)
        {
            _modalPool = new ObjectPool<VRModal>(modalPrefab, transform, initialSize: 2, maxSize: 5);
            Debug.Log("[VRUIManager] Initialized modal object pool");
        }
        
        // Initialize skip button pool
        if (skipButtonPrefab != null)
        {
            _skipButtonPool = new ObjectPool<VRSkipButton>(skipButtonPrefab, transform, initialSize: 1, maxSize: 3);
            Debug.Log("[VRUIManager] Initialized skip button object pool");
        }
        
        // Initialize download modal pool
        if (downloadModalPrefab != null)
        {
            _downloadModalPool = new ObjectPool<VRDownloadModal>(downloadModalPrefab, transform, initialSize: 1, maxSize: 2);
            Debug.Log("[VRUIManager] Initialized download modal object pool");
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
    /// Show a modal with a message. Auto-dismisses after duration.
    /// </summary>
    /// <param name="message">Message to display</param>
    /// <param name="duration">Display duration in seconds (0 = use default)</param>
    public void ShowModal(string message, float duration = 0f)
    {
        // Hide existing modal if any and return to pool
        if (_currentModal != null)
        {
            ReturnModalToPool(_currentModal);
            _currentModal = null;
        }

        // Get modal from pool or create new
        if (_modalPool != null)
        {
            _currentModal = _modalPool.Get();
            _currentModal.transform.SetParent(transform);
            Debug.Log($"[VRUIManager] Got modal from pool, showing message: '{message}'");
        }
        else if (modalPrefab != null)
        {
            // Fallback to instantiate if pool not initialized
            _currentModal = Instantiate(modalPrefab);
            _currentModal.transform.SetParent(transform);
            Debug.LogWarning("[VRUIManager] Modal pool not initialized, using Instantiate (fallback)");
            Debug.Log($"[VRUIManager] Instantiated modal from prefab, showing message: '{message}'");
        }
        else
        {
            // ERROR: Cannot create modal without prefab!
            Debug.LogError("[VRUIManager] Cannot show modal - modalPrefab is null and modalPool is null! Please assign modalPrefab in Inspector.");
            return;
        }

        if (_currentModal != null)
        {
            _currentModal.Show(message, duration);
            Debug.Log($"[VRUIManager] Called Show() on modal with message: '{message}', duration: {duration}s");
        }
        else
        {
            Debug.LogError("[VRUIManager] _currentModal is null after creation attempt!");
        }
    }
    
    /// <summary>
    /// Returns a modal to the pool instead of destroying it.
    /// </summary>
    private void ReturnModalToPool(VRModal modal)
    {
        if (modal == null) return;
        
        modal.Hide();
        
        if (_modalPool != null)
        {
            _modalPool.Return(modal);
        }
        else
        {
            // Fallback to destroy if pool not initialized
            Destroy(modal.gameObject);
        }
    }

    /// <summary>
    /// Hide the current modal immediately and return it to the pool.
    /// </summary>
    public void HideModal()
    {
        if (_currentModal != null)
        {
            ReturnModalToPool(_currentModal);
            _currentModal = null;
        }
    }

    /// <summary>
    /// Show skip button for video playback.
    /// </summary>
    /// <param name="onSkipAction">Action to call when skip is pressed</param>
    public void ShowSkipButton(UnityEngine.Events.UnityAction onSkipAction)
    {
        // Hide existing skip button if any and return to pool
        if (_currentSkipButton != null)
        {
            ReturnSkipButtonToPool(_currentSkipButton);
            _currentSkipButton = null;
        }

        // Get skip button from pool or create new
        if (_skipButtonPool != null)
        {
            _currentSkipButton = _skipButtonPool.Get();
            _currentSkipButton.transform.SetParent(transform);
        }
        else if (skipButtonPrefab != null)
        {
            // Fallback to instantiate if pool not initialized
            _currentSkipButton = Instantiate(skipButtonPrefab);
            _currentSkipButton.transform.SetParent(transform);
            Debug.LogWarning("[VRUIManager] Skip button pool not initialized, using Instantiate (fallback)");
        }
        else
        {
            // Create skip button from scratch if no prefab
            GameObject skipObj = new GameObject("VRSkipButton");
            skipObj.transform.SetParent(transform);
            _currentSkipButton = skipObj.AddComponent<VRSkipButton>();
        }

        _currentSkipButton.SetOnClick(onSkipAction);
        _currentSkipButton.Show();
    }
    
    /// <summary>
    /// Returns a skip button to the pool instead of destroying it.
    /// </summary>
    private void ReturnSkipButtonToPool(VRSkipButton skipButton)
    {
        if (skipButton == null) return;
        
        skipButton.Hide();
        
        if (_skipButtonPool != null)
        {
            _skipButtonPool.Return(skipButton);
        }
        else
        {
            // Fallback to destroy if pool not initialized
            Destroy(skipButton.gameObject);
        }
    }

    /// <summary>
    /// Hide the skip button and return it to the pool.
    /// </summary>
    public void HideSkipButton()
    {
        if (_currentSkipButton != null)
        {
            ReturnSkipButtonToPool(_currentSkipButton);
            _currentSkipButton = null;
        }
    }

    /// <summary>
    /// Register a 3D button for management.
    /// </summary>
    public void RegisterButton(VR3DButton button)
    {
        if (button != null && !_activeButtons.Contains(button))
        {
            _activeButtons.Add(button);
        }
    }

    /// <summary>
    /// Unregister a 3D button.
    /// </summary>
    public void UnregisterButton(VR3DButton button)
    {
        _activeButtons.Remove(button);
    }

    /// <summary>
    /// Get or create the download modal for showing download progress.
    /// DEPRECATED: Use ShowLoadingScreen() instead - LoadingScreen automatically handles download progress.
    /// </summary>
    [System.Obsolete("Use ShowLoadingScreen() instead - LoadingScreen automatically handles download progress from CDNDownloadManager")]
    public VRDownloadModal GetDownloadModal()
    {
        Debug.LogWarning("[VRUIManager] GetDownloadModal() is deprecated. Use ShowLoadingScreen() instead - LoadingScreen automatically handles download progress.");
        
        if (_currentDownloadModal == null)
        {
            // Get download modal from pool or create new
            if (_downloadModalPool != null)
            {
                _currentDownloadModal = _downloadModalPool.Get();
                _currentDownloadModal.transform.SetParent(transform);
            }
            else if (downloadModalPrefab != null)
            {
                // Fallback to instantiate if pool not initialized
                _currentDownloadModal = Instantiate(downloadModalPrefab);
                _currentDownloadModal.transform.SetParent(transform);
                Debug.LogWarning("[VRUIManager] Download modal pool not initialized, using Instantiate (fallback)");
            }
            else
            {
                // Create download modal from scratch if no prefab
                GameObject downloadModalObj = new GameObject("VRDownloadModal");
                downloadModalObj.transform.SetParent(transform);
                _currentDownloadModal = downloadModalObj.AddComponent<VRDownloadModal>();
            }
        }
        return _currentDownloadModal;
    }
    
    /// <summary>
    /// Returns the download modal to the pool.
    /// </summary>
    public void ReturnDownloadModal()
    {
        if (_currentDownloadModal != null)
        {
            if (_downloadModalPool != null)
            {
                _currentDownloadModal.gameObject.SetActive(false);
                _downloadModalPool.Return(_currentDownloadModal);
            }
            else
            {
                // Fallback to destroy if pool not initialized
                Destroy(_currentDownloadModal.gameObject);
            }
            _currentDownloadModal = null;
        }
    }

    /// <summary>
    /// Shows an instruction with De Dijk branding.
    /// </summary>
    /// <param name="instruction">Instruction text to display</param>
    /// <param name="duration">How long to display (0 = use default or don't auto-hide)</param>
    // public void ShowInstruction(string instruction, float duration = 0f)
    // {
    //     // Hide existing instruction if any
    //     if (_currentInstructionDisplay != null)
    //     {
    //         _currentInstructionDisplay.Hide();
    //         _currentInstructionDisplay = null;
    //     }

    //     // Get or create instruction display
    //     if (instructionDisplayPrefab != null)
    //     {
    //         _currentInstructionDisplay = Instantiate(instructionDisplayPrefab);
    //         _currentInstructionDisplay.transform.SetParent(transform);
    //     }
    //     else
    //     {
    //         // Create instruction display from scratch if no prefab
    //         GameObject instructionObj = new GameObject("DeDijkInstructionDisplay");
    //         instructionObj.transform.SetParent(transform);
    //         _currentInstructionDisplay = instructionObj.AddComponent<DeDijkInstructionDisplay>();
    //     }

    //     _currentInstructionDisplay.ShowInstruction(instruction, duration);
    // }

    /// <summary>
    /// Hides the current instruction display.
    /// </summary>
    // public void HideInstruction()
    // {
    //     if (_currentInstructionDisplay != null)
    //     {
    //         _currentInstructionDisplay.Hide();
    //         _currentInstructionDisplay = null;
    //     }
    // }

    /// <summary>
    /// Shows a loading screen with optional progress tracking.
    /// LoadingScreen automatically subscribes to CDNDownloadManager events and will display
    /// download progress when downloads are active (file name, progress bar, file count).
    /// </summary>
    /// <param name="message">Loading message to display</param>
    /// <param name="showProgress">Whether to show progress bar</param>
    /// <param name="progress">Initial progress (0-1)</param>
    public void ShowLoadingScreen(string message = null, bool showProgress = true, float progress = 0f)
    {
        // Hide existing loading screen if any
        if (_currentLoadingScreen != null)
        {
            _currentLoadingScreen.Hide();
            Destroy(_currentLoadingScreen.gameObject);
            _currentLoadingScreen = null;
        }

        // Get or create loading screen
        if (loadingScreenPrefab != null)
        {
            _currentLoadingScreen = Instantiate(loadingScreenPrefab);
            _currentLoadingScreen.transform.SetParent(transform);
        }
        else
        {
            // Create loading screen from scratch if no prefab
            GameObject loadingObj = new GameObject("LoadingScreen");
            loadingObj.transform.SetParent(transform);
            _currentLoadingScreen = loadingObj.AddComponent<LoadingScreen>();
        }

        _currentLoadingScreen.ShowLoading(message, showProgress, progress);
    }

    /// <summary>
    /// Updates the progress of the current loading screen.
    /// </summary>
    /// <param name="progress">Progress value (0-1)</param>
    /// <param name="message">Optional message to update</param>
    public void UpdateLoadingProgress(float progress, string message = null)
    {
        if (_currentLoadingScreen != null)
        {
            _currentLoadingScreen.UpdateProgress(progress, message);
        }
    }

    /// <summary>
    /// Hides the loading screen.
    /// </summary>
    /// <param name="fadeOut">Whether to fade out (future enhancement)</param>
    public void HideLoadingScreen(bool fadeOut = false)
    {
        if (_currentLoadingScreen != null)
        {
            _currentLoadingScreen.Hide(fadeOut);
            if (!fadeOut)
            {
                Destroy(_currentLoadingScreen.gameObject);
                _currentLoadingScreen = null;
            }
        }
    }

    /// <summary>
    /// Loads a scene with a loading screen showing progress.
    /// </summary>
    /// <param name="sceneName">Name of the scene to load</param>
    /// <param name="loadingMessage">Message to show during loading</param>
    public void LoadSceneWithLoadingScreen(string sceneName, string loadingMessage = "Scene laden...")
    {
        StartCoroutine(LoadSceneWithProgressCoroutine(sceneName, loadingMessage));
    }

    /// <summary>
    /// Coroutine that loads a scene and updates loading screen progress.
    /// </summary>
    private IEnumerator LoadSceneWithProgressCoroutine(string sceneName, string loadingMessage)
    {
        // Show loading screen
        ShowLoadingScreen(loadingMessage, showProgress: true, progress: 0f);

        // Load scene asynchronously
        AsyncOperation asyncLoad = SceneManager.LoadSceneAsync(sceneName);
        asyncLoad.allowSceneActivation = false;

        // Update progress while loading
        while (asyncLoad.progress < 0.9f)
        {
            UpdateLoadingProgress(asyncLoad.progress, loadingMessage);
            yield return null;
        }

        // Ensure 100% before activating
        UpdateLoadingProgress(1f, "Bijna klaar...");
        yield return new WaitForSeconds(0.2f);

        // Activate scene
        asyncLoad.allowSceneActivation = true;

        // Wait for scene to fully load
        while (!asyncLoad.isDone)
        {
            yield return null;
        }

        // Hide loading screen
        HideLoadingScreen();
    }

    private bool _startButtonPressed = false;
    private bool _wasPausedBeforeStartButton = false;

    /// <summary>
    /// Shows a START button and returns a coroutine that waits for the button to be pressed.
    /// Used for landing screens before game starts (e.g., after VAKANTIE video).
    /// Pauses everything and enables passthrough so user can see the real world.
    /// Uses the skip button system which already works well in VR.
    /// </summary>
    /// <param name="buttonText">Text to display on the button (default: "START")</param>
    /// <returns>Coroutine that completes when the button is pressed</returns>
    public IEnumerator ShowStartButtonAndWait(string buttonText = "START")
    {
        _startButtonPressed = false;
        
        // CRITICAL: Pause everything (videos, audio, timers, etc.)
        if (GlobalPauseManager.Instance != null)
        {
            _wasPausedBeforeStartButton = GlobalPauseManager.Instance.IsPaused;
            if (!_wasPausedBeforeStartButton)
            {
                GlobalPauseManager.Instance.Pause();
                Debug.Log("[VRUIManager]: Paused everything for START button landing screen");
            }
        }
        
        // CRITICAL: Enable passthrough so user can see the real world
        VideoManager videoManager = FindFirstObjectByType<VideoManager>();
        if (videoManager != null)
        {
            videoManager.PassthroughCall(true); // Enable passthrough (see real world)
            Debug.Log("[VRUIManager]: Enabled passthrough for START button landing screen");
        }
        
        // Show modal message
        ShowModal("Klaar om opnieuw te beginnen?", duration: 0f); // No auto-dismiss
        
        // Show skip button as START button (reuse existing system)
        ShowSkipButton(() => {
            _startButtonPressed = true;
            Debug.Log("[VRUIManager]: START button pressed");
        });
        
        // Wait for button to be pressed
        yield return new WaitUntil(() => _startButtonPressed);
        
        // CRITICAL: Disable passthrough (back to VR)
        if (videoManager != null)
        {
            videoManager.PassthroughCall(false); // Disable passthrough (back to VR)
            Debug.Log("[VRUIManager]: Disabled passthrough after START button");
        }
        
        // CRITICAL: Resume everything if we were the ones who paused it
        if (GlobalPauseManager.Instance != null && !_wasPausedBeforeStartButton)
        {
            GlobalPauseManager.Instance.Resume();
            Debug.Log("[VRUIManager]: Resumed everything after START button");
        }
        
        // Clean up UI
        HideModal();
        HideSkipButton();
        
        Debug.Log("[VRUIManager]: START button pressed, continuing flow");
    }

    /// <summary>
    /// Clear all active UI elements and return them to pools.
    /// </summary>
    public void ClearAll()
    {
        HideModal();
        HideSkipButton();
        ReturnDownloadModal();
        // HideInstruction();
        HideLoadingScreen();
        
        foreach (var button in _activeButtons)
        {
            if (button != null)
            {
                Destroy(button.gameObject);
            }
        }
        _activeButtons.Clear();
    }
    
    /// <summary>
    /// Cleanup when component is destroyed.
    /// </summary>
    private void OnDestroy() {
        // Clear all UI elements
        ClearAll();
        
        // Clear object pools
        if (_modalPool != null) {
            _modalPool.Clear();
        }
        if (_skipButtonPool != null) {
            _skipButtonPool.Clear();
        }
        if (_downloadModalPool != null) {
            _downloadModalPool.Clear();
        }
    }
}

