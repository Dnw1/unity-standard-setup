using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Networking;
using UnityEngine.Video;
using Oculus.Interaction;

[Serializable]
public class AudioAsset {
    public string name;
    public float volume;
    public Vector3 location;
}
[Serializable]
public class VideoAsset {
    public string name;
    public string stereo;
    public int shape;
}

public class VideoManager : MonoBehaviour {
    
    [SerializeField] private GameObject vidHolder;
    [SerializeField] private VideoPlayer vidPlayer;
    [SerializeField] private VRVideoPlayer vrVidPlayer;

    [SerializeField] private AudioSource setAudio;
    [SerializeField] private AudioSource backgroundMusicAudio; // Separate AudioSource for background music during videos
    [SerializeField] private OVRPassthroughLayer passthrough;
    
    // Cached reference to OVRCameraRig (replacing GameObject.Find() calls)
    private GameObject _ovrCameraRig;

    [SerializeField] private VideoSkipCall skipButton;
    
    
    // Video audio volume control
    private Dictionary<string, float> _videoVolumes = new Dictionary<string, float>();

    private AudioClip audioC;
    private ConfigData data;

    public event Action OnVideoFinished;
    public event Action OnAudioFinished;
    public event Action OnVideoStarted;
    public event Action OnVideoSkipped;

    private int _playId = 0;
    private int _activePlayId = -1;
    private string _currentVideoFileName = null;
    private bool _completed;

    // Coroutine tracking for performance optimization
    private CoroutineTracker _coroutineTracker;
    
    // Video performance monitoring
    // private VideoPerformanceMonitor _videoPerformanceMonitor;
    
    // Video error handling constants
    private const float PREPARE_TIMEOUT = 30f; // 30 seconds for video preparation
    private const float PLAYBACK_TIMEOUT_BUFFER = 5f; // 5 seconds buffer beyond video duration
    
    // Store error handler delegate so we can unsubscribe properly
    private VideoPlayer.ErrorEventHandler _currentErrorHandler = null;
    
    // Video fade overlay for transitions
    // private GameObject _fadeOverlayCanvas;
    // private UnityEngine.UI.Image _fadeOverlayImage;
    // private const float DEFAULT_FADE_DURATION = 1f;

    public static VideoManager Instance;

    private void Awake() {
        // if (Instance != null && Instance != this)
        // {
        //     Destroy(gameObject);   // Kill the duplicate
        //     return;
        // }

        // Instance = this;
        // DontDestroyOnLoad(gameObject);
        

        vrVidPlayer.Play();
        
        // Initialize CoroutineTracker for performance optimization
        _coroutineTracker = GetComponent<CoroutineTracker>();
        if (_coroutineTracker == null)
        {
            _coroutineTracker = gameObject.AddComponent<CoroutineTracker>();
        }
        
        // Initialize VideoPerformanceMonitor for adaptive render scale
        // _videoPerformanceMonitor = GetComponent<VideoPerformanceMonitor>();
        // if (_videoPerformanceMonitor == null)
        // {
        //     _videoPerformanceMonitor = gameObject.AddComponent<VideoPerformanceMonitor>();
        // }
        
        // Cache OVRCameraRig reference
        CacheOVRCameraRig();
        
        // Ensure VRUIManager exists (it will persist across scenes)
        EnsureVRUIManagerExists();
    }

    public void ProcessTrainingConfig(ConfigData data) {
        Debug.Log(data);
        this.data = data;
        
        // Initialize video volumes from config (much easier to adjust!)
        Debug.Log($"[VideoManager]: Initializing video volumes from config JSON");
        InitializeVideoVolumes();
    }
    
    /// <summary>
    /// Initializes video volumes from config JSON (reads volume from each video entry).
    /// </summary>
    private void InitializeVideoVolumes()
    {
        if (data == null || data.videos == null)
        {
            Debug.LogWarning("[VideoManager]: ConfigData or videos array is null, cannot initialize volumes");
            return;
        }
        
        // Read volume from each video in config
        foreach (var video in data.videos)
        {
            if (!string.IsNullOrEmpty(video.name))
            {
                float volume = video.volume; // Defaults to 1.0f if not specified in JSON
                SetVideoVolume(video.name, volume);
                Debug.Log($"[VideoManager]: ✓ Loaded volume {volume:F2} ({volume * 100:F0}%) for video: {video.name}");
            }
        }
        
        // Debug: Log all loaded volumes
        Debug.Log($"[VideoManager]: Total video volumes loaded: {_videoVolumes.Count}");
        foreach (var kvp in _videoVolumes)
        {
            Debug.Log($"[VideoManager]:   - {kvp.Key}: {kvp.Value:F2} ({kvp.Value * 100:F0}%)");
        }
    }
    
    /// <summary>
    /// Sets the audio volume for a specific video.
    /// </summary>
    /// <param name="videoName">Video name (e.g., "SchaatsBaan")</param>
    /// <param name="volume">Volume (0-1)</param>
    public void SetVideoVolume(string videoName, float volume)
    {
        volume = Mathf.Clamp01(volume);
        _videoVolumes[videoName] = volume;
        
        // If video is currently playing, update its audio volume immediately
        if (vidPlayer != null && vidPlayer.isPlaying)
        {
            // Apply volume to currently playing video using SetDirectAudioVolume
            vidPlayer.SetDirectAudioVolume(0, volume);
            Debug.Log($"[VideoManager] Updated volume for currently playing video: {videoName} = {volume}");
        }
        
        Debug.Log($"[VideoManager] Set video volume: {videoName} = {volume}");
    }
    
    /// <summary>
    /// Gets the audio volume for a specific video.
    /// </summary>
    public float GetVideoVolume(string videoName)
    {
        if (string.IsNullOrEmpty(videoName))
        {
            Debug.LogWarning("[VideoManager]: GetVideoVolume called with null or empty videoName, returning default 1.0");
            return 1.0f;
        }
        
        // First check if volume was set from config
        if (_videoVolumes.TryGetValue(videoName, out float volume))
        {
            Debug.Log($"[VideoManager]: Found volume in _videoVolumes dictionary: {videoName} = {volume:F2}");
            return volume;
        }
        
        // Fallback: Try to get from config data if available
        if (data != null && data.videos != null)
        {
            foreach (var video in data.videos)
            {
                if (video.name == videoName)
                {
                    float vol = video.volume; // Defaults to 1.0f if not specified
                    Debug.Log($"[VideoManager]: Found volume in config data: {videoName} = {vol:F2}");
                    // Cache it for next time
                    _videoVolumes[videoName] = vol;
                    return vol;
                }
            }
            
            // Build list of available video names for debugging
            var videoNames = new List<string>();
            foreach (var v in data.videos)
            {
                if (!string.IsNullOrEmpty(v.name))
                    videoNames.Add(v.name);
            }
            Debug.LogWarning($"[VideoManager]: Video '{videoName}' not found in config data. Available videos: {string.Join(", ", videoNames)}");
        }
        else
        {
            Debug.LogWarning($"[VideoManager]: Config data is null or videos array is null. Cannot get volume for: {videoName}");
        }
        
        // Final fallback: default volume
        Debug.LogWarning($"[VideoManager]: Using default volume 1.0 for video: {videoName}");
        return 1.0f;
    }

    public void ManualSceneInit() {

        // Initialize CoroutineTracker for performance optimization
        _coroutineTracker = GetComponent<CoroutineTracker>();
        if (_coroutineTracker == null)
        {
            _coroutineTracker = gameObject.AddComponent<CoroutineTracker>();
        }
        
        // Initialize VideoPerformanceMonitor for adaptive render scale
        // _videoPerformanceMonitor = GetComponent<VideoPerformanceMonitor>();
        // if (_videoPerformanceMonitor == null)
        // {
        //     _videoPerformanceMonitor = gameObject.AddComponent<VideoPerformanceMonitor>();
        // }

        // Cache OVRCameraRig reference
        CacheOVRCameraRig();
        
        // Ensure VRUIManager exists (it will persist across scenes)
        EnsureVRUIManagerExists();
    }

    private void OnEnable()
    {
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private void OnDisable()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    /// <summary>
    /// Called when a new scene is loaded. Refreshes runtime references that may belong to scene objects.
    /// This protects the persistent VideoManager from holding stale references to destroyed scene objects
    /// after a Restart/Reload cycle.
    /// </summary>
    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        Debug.Log("[VideoManager]: Scene loaded - refreshing runtime references");
        // Re-cache camera rig / passthrough references
        RefreshOVRCameraRig();

        // CRITICAL: Disable passthrough IMMEDIATELY on scene load to prevent flash
        // Only EndScene should have passthrough enabled at start (for restart button visibility)
        // All other scenes start with videos or need explicit passthrough enable for games
        bool isEndScene = scene.name == GameConstants.SceneFiles.EndScene || scene.name.Contains("End");
        passthrough.enabled = false;

        // Attempt to recover any scene-local references that might have been destroyed
        TryReassignRuntimeReferences();
    }

    /// <summary>
    /// Attempts to (safely) reassign runtime references that commonly become null after scene reloads.
    /// Uses conservative Find calls so we don't overwrite valid inspector-set references unless null.
    /// </summary>
    private void TryReassignRuntimeReferences()
    {
        // vidHolder may be scene-local; only search if currently null
        if (vidHolder == null)
        {
            var foundHolder = GameObject.Find("VidHolder") ?? GameObject.Find("VideoHolder");
            if (foundHolder != null)
            {
                vidHolder = foundHolder;
                Debug.Log("[VideoManager]: Reassigned vidHolder from scene");
            }
        }

        // Prefer to find the VRVideoPlayer first and derive the VideoPlayer from it
        if (vrVidPlayer == null)
        {
            var foundVr = FindFirstObjectByType<VRVideoPlayer>();
            if (foundVr != null)
            {
                vrVidPlayer = foundVr;
                Debug.Log("[VideoManager]: Reassigned vrVidPlayer from scene");
            }
        }

        // If we found a VRVideoPlayer, ensure vidPlayer references the same underlying VideoPlayer
        if (vrVidPlayer != null)
        {
            try
            {
                var vp = vrVidPlayer.GetComponent<VideoPlayer>();
                if (vp != null)
                {
                    vidPlayer = vp;
                    Debug.Log("[VideoManager]: Bound vidPlayer to VRVideoPlayer's VideoPlayer");
                }

                // Reset VRVideoPlayer runtime state to force overlay reconfiguration
                vrVidPlayer.ResetState();

                // Try to re-enable overlay component if present
                var overlayComp = vrVidPlayer.GetComponent<OVROverlay>();
                if (overlayComp != null && !overlayComp.enabled)
                {
                    overlayComp.enabled = true;
                    Debug.Log("[VideoManager]: Re-enabled OVROverlay on VRVideoPlayer");
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("[VideoManager]: Error binding VRVideoPlayer -> VideoPlayer: " + e.Message);
            }
        }

        // Try to find an AudioSource for setAudio if missing (prefer non-background sources)
        if (setAudio == null)
        {
            var audioSources = FindObjectsByType<AudioSource>(FindObjectsSortMode.None);
            foreach (var a in audioSources)
            {
                if (a != backgroundMusicAudio && a.gameObject.activeInHierarchy)
                {
                    setAudio = a;
                    Debug.Log("[VideoManager]: Reassigned setAudio from scene");
                    break;
                }
            }
        }

        // If VRVideoPlayer was found and has an overlay, ensure it's enabled to register with render pipeline
        // If we still have a VRVideoPlayer, it's already been handled above. Otherwise, try to locate an OVROverlay
        if (vrVidPlayer == null)
        {
            var overlayCandidate = FindFirstObjectByType<OVROverlay>();
            if (overlayCandidate != null && !overlayCandidate.enabled)
            {
                overlayCandidate.enabled = true;
                Debug.Log("[VideoManager]: Re-enabled standalone OVROverlay on scene load");
            }
        }
    }
    
    /// <summary>
    /// Caches the OVRCameraRig reference to avoid GameObject.Find() calls.
    /// </summary>
    private void CacheOVRCameraRig() {
        if (_ovrCameraRig == null) {
            _ovrCameraRig = GameObject.Find("OVRCameraRig");
            if (_ovrCameraRig != null) {
                // Also cache the passthrough component if not already assigned
                if (passthrough == null) {
                    passthrough = _ovrCameraRig.GetComponent<OVRPassthroughLayer>();
                }
            }
        }
    }
    
    /// <summary>
    /// Refreshes the OVRCameraRig reference (useful when scenes change).
    /// </summary>
    private void RefreshOVRCameraRig() {
        _ovrCameraRig = null; // Clear cache
        passthrough = null; // Also clear passthrough cache to ensure fresh reference for new scene
        CacheOVRCameraRig();
    }
    
    /// <summary>
    /// Ensures VRUIManager exists. Creates it if missing.
    /// </summary>
    private void EnsureVRUIManagerExists()
    {
        if (VRUIManager.Instance == null)
        {
            GameObject uiManagerObj = new GameObject("VRUIManager");
            uiManagerObj.AddComponent<VRUIManager>();
            DontDestroyOnLoad(uiManagerObj);
            Debug.Log("[VideoManager]: Created VRUIManager (was missing)");
        }
    }

    public void ResetState() {
        _playId = 0;
        _activePlayId = -1;
        _completed = false;
        _currentVideoFileName = null;
        StopAllCoroutines();
        StopBackgroundMusic(); // Stop background music on reset
        
        // Clean up error event handler
        if (vidPlayer != null && _currentErrorHandler != null) {
            vidPlayer.errorReceived -= _currentErrorHandler;
            _currentErrorHandler = null;
        }
    }
    
    /// <summary>
    /// Cleanup when component is destroyed.
    /// </summary>
    private void OnDestroy() {
        // Stop all tracked coroutines
        if (_coroutineTracker != null) {
            _coroutineTracker.StopAllTrackedCoroutines();
        }
        
        // Stop video performance monitoring
        // if (_videoPerformanceMonitor != null) {
        //     _videoPerformanceMonitor.StopMonitoring();
        // }
        
        // Clean up error event handler
        if (vidPlayer != null && _currentErrorHandler != null) {
            vidPlayer.errorReceived -= _currentErrorHandler;
            _currentErrorHandler = null;
        }
        
        // Unsubscribe from events
        OnVideoFinished = null;
        OnAudioFinished = null;
        OnVideoStarted = null;
        OnVideoSkipped = null;
    }

    public bool canSendVideo = true;

    public void PlayVideo(string fileName) {
        // Null check for fileName first
        if (string.IsNullOrEmpty(fileName)) {
            Debug.LogError("[VideoManager] Cannot play video: fileName is null or empty");
            Debug.LogError(
                "Video file name is missing. Please check your configuration file." +
                "PlayVideo called with null or empty fileName"
            );
            return;
        }
        
        // Null check for required components using NullCheck utility
        // if (!NullCheck.ValidateReference(vidPlayer, "VideoPlayer", "VideoManager")) {
        //     return;
        // }
        // if (!NullCheck.ValidateReference(vrVidPlayer, "VRVideoPlayer", "VideoManager")) {
        //     return;
        // }
        
        _playId++;
        _activePlayId = _playId;
        _completed = false;
        _currentVideoFileName = fileName;

        // Use CoroutineTracker to manage video playback coroutine
        _coroutineTracker.StartTrackedCoroutine("videoPlayback", LocalPlayer(fileName, _activePlayId));
    }

    private void PassthroughManage(bool setState) {
        // Refresh reference in case scene changed
        RefreshOVRCameraRig();

        string currentScene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
        bool inBeatSaberScene = currentScene.Contains("BeatSaber") || currentScene == "BeatSaber";

        // CRITICAL FIX: Always allow passthrough to be DISABLED (setState=false), even in BeatSaber scenes
        // This ensures screen is black during scene transitions, preventing passthrough from showing
        // Only skip passthrough ENABLE (setState=true) in BeatSaber scenes (to prevent accidental passthrough)
        if (!inBeatSaberScene || !setState) {
            if (_ovrCameraRig != null) {
                // Get passthrough component if not already cached
                if (passthrough == null) {
                    passthrough = _ovrCameraRig.GetComponent<OVRPassthroughLayer>();
                }
                
                if (passthrough != null) {
                    passthrough.enabled = setState;
                    Debug.Log($"[VideoManager]: Passthrough set to: {setState} (scene: {currentScene})");
                } else {
                    Debug.LogWarning("[VideoManager]: No OVRPassthroughLayer found on the rig.");
                }
            } else {
                Debug.LogWarning("[VideoManager]: OVRCameraRig not found.");
            }
        } else {
            Debug.Log($"[VideoManager]: Skipping passthrough enable in BeatSaber scene (scene: {currentScene})");
        }
    }

    /// <summary>
    /// Checks if a file is likely an image file based on its extension.
    /// Handles both single extensions (.jpg) and double extensions (.mp3.mpeg where .mpeg might be misleading).
    /// </summary>
    private bool IsImageFile(string filePath) {
        string fileName = Path.GetFileName(filePath).ToLowerInvariant();
        string extension = Path.GetExtension(filePath).ToLowerInvariant();
        
        // Check for common image extensions
        string[] imageExtensions = { ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".tga", ".tiff", ".tif", ".exr", ".hdr", ".dds", ".psd" };
        if (System.Array.IndexOf(imageExtensions, extension) >= 0) {
            return true;
        }
        
        // Check for double extensions that might indicate an image (e.g., "file.mp3.mpeg" where it's actually a jpg)
        // Look for image extensions in the filename before the last extension
        foreach (string imgExt in imageExtensions) {
            if (fileName.Contains(imgExt) && fileName.IndexOf(imgExt) < fileName.LastIndexOf('.')) {
                return true;
            }
        }
        
        // Check for known problematic filenames (album covers with wrong extensions)
        // This specific file is known to be an image
        if (fileName.Contains("album") || fileName.Contains("cover") || 
            fileName.Contains("KPop_Demon_Hunters") && fileName.Contains("mp3.mpeg")) {
            return true;
        }
        
        return false;
    }

    private IEnumerator LocalPlayer(string fileName, int playId) {
        // Null checks for required components
        if (vidPlayer == null) {
            Debug.LogError("[VideoManager] VideoPlayer is null! Cannot play video.");
            yield break;
        }
        if (vrVidPlayer == null) {
            Debug.LogError("[VideoManager] VRVideoPlayer is null! Cannot play video.");
            yield break;
        }
        if(data == null) {
            data = GameObject.Find("JsonStructure").GetComponent<JsonStructure>().data;
        }
        
        // yield return new WaitForSeconds(0.5f);
        
        vidPlayer.isLooping = false;
        if (setAudio != null && setAudio.isPlaying) {
            setAudio.Stop();
            Debug.Log($"[VideoManager]: pausing {setAudio}");
        }

        if (vidHolder != null) {
            vidHolder.SetActive(true);
        }
        if(vidPlayer.isPlaying) {
            vidPlayer.Stop();
            vidPlayer.url = null;
            Debug.Log("[VideoManager]: Video player was playing stopping it.");
        }

        PassthroughManage(false);

        // Set default shape to 360° (will be configured after we detect resolution)
        vrVidPlayer.SetShape(VRVideoPlayer.VideoShape._360);
        
        // Note: Stereo/mono detection will happen after video is prepared (when we can check resolution)

        // Determine video path: Editor uses test-videos folder, Quest3 uses persistentDataPath
        string videoPath = GetVideoPath(fileName);

        // Check if file is actually an image (album covers sometimes have wrong extensions)
        if (File.Exists(videoPath) && IsImageFile(videoPath)) {
            Debug.LogWarning($"[VideoManager]: Skipping image file (not video): {Path.GetFileName(videoPath)}");
            // Complete the video "playback" immediately since there's no video to play
            CompleteVideo(playId);
            yield break;
        }

        // Check if file exists, download from CDN if needed
        if (File.Exists(videoPath)) {
            Debug.Log($"[VideoManager] ✓ Video FOUND: {fileName} at {videoPath}");
        } else {
            Debug.LogWarning($"[VideoManager] ✗ Video NOT FOUND: {fileName} at {videoPath}");
            
            // Try to download from CDN (works in Editor and on Quest3)
            CDNDownloadManager.EnsureInstanceExists();
            if (CDNDownloadManager.Instance != null) {
                Debug.Log($"[VideoManager] → Attempting CDN download: {fileName}");
                bool fileReady = false;
                string errorMessage = null;
                
                CDNDownloadManager.Instance.EnsureFileExists(
                    fileName,
                    onComplete: (path) => {
                        videoPath = path;
                        fileReady = true;
                    },
                    onError: (error) => {
                        errorMessage = error;
                        fileReady = true; // Set to true to exit wait loop
                    }
                );
                
                // Wait for download to complete (or fail)
                yield return new WaitUntil(() => fileReady);
                
                if (!string.IsNullOrEmpty(errorMessage) || !File.Exists(videoPath)) {
                    Debug.LogError(fileName + errorMessage + " Unknown error");
                    yield break;
                } else {
                    Debug.Log($"[VideoManager] ✓ CDN download SUCCESS: {fileName} → {videoPath}");
                }
            } else {
                Debug.LogError(fileName + " Video " + videoPath);
                yield break;
            }
        }

        // Double-check after download that it's still not an image
        if (IsImageFile(videoPath)) {
            Debug.LogWarning($"[VideoManager]: Skipping image file (not video): {Path.GetFileName(videoPath)}");
            CompleteVideo(playId);
            yield break;
        }

        yield return new WaitForSeconds(0.2f);

        vidPlayer.url = videoPath;
        Debug.Log($"[VideoManager]: Video's file: " + fileName + " video path: " + videoPath);

        // Set up error handling for VideoPlayer
        bool videoErrorOccurred = false;
        string videoErrorMessage = null;
        
        // Clear any previous error handlers
        if (_currentErrorHandler != null && vidPlayer != null) {
            vidPlayer.errorReceived -= _currentErrorHandler;
            _currentErrorHandler = null;
        }
        
        // Add error handler for this video playback
        _currentErrorHandler = (source, message) => {
            videoErrorOccurred = true;
            videoErrorMessage = message;
            Debug.LogError($"[VideoManager] Video playback error: {message}");
            Debug.LogError(
                $"Video playback error: {Path.GetFileName(fileName)}\n{message} " +
                $" Video error for {fileName}: {message}"
            );
        };
        vidPlayer.errorReceived += _currentErrorHandler;

        vidPlayer.Prepare();
        
        // Wait for preparation with timeout (30 seconds as per design spec)
        float elapsed = 0f;
        while (!vidPlayer.isPrepared && elapsed < PREPARE_TIMEOUT && !videoErrorOccurred)
        {
            elapsed += Time.deltaTime;
            if (vidPlayer == null) {
                Debug.LogWarning("[VideoManager]: Video player was destroyed during preparation");
                CompleteVideo(playId);
                yield break;
            }
            yield return null;
        }
        
        // Check if preparation succeeded
        if (videoErrorOccurred)
        {
            Debug.LogError($"[VideoManager]: Video error during preparation: {videoErrorMessage}");
            CompleteVideo(playId);
            yield break;
        }
        
        if (vidPlayer == null || !vidPlayer.isPrepared)
        {
            if (elapsed >= PREPARE_TIMEOUT)
            {
                Debug.LogError($"[VideoManager]: Video preparation timeout: {fileName}");
                Debug.LogError(
                    $"Video took too long to load: {Path.GetFileName(fileName)} " +
                    $" Video preparation timeout after {PREPARE_TIMEOUT}s: {fileName}"
                );
            }
            else
            {
                Debug.LogWarning($"[VideoManager]: Video player was destroyed during preparation");
            }
            CompleteVideo(playId);
            yield break;
        }
        
        // CRITICAL FIX: Detect mono vs stereo based on video resolution
        // Mono 360° videos: aspect ratio ≈ 2:1 (width/height ≈ 2.0), e.g., 3840x1920
        // Stereo TopBottom videos: aspect ratio ≈ 1:1 (width/height ≈ 1.0), e.g., 5760x5760
        uint videoWidth = vidPlayer.width;
        uint videoHeight = vidPlayer.height;
        float aspectRatio = videoHeight > 0 ? (float)videoWidth / (float)videoHeight : 1.0f;
        
        // If aspect ratio is close to 2:1 (mono), use Mono format
        // If aspect ratio is close to 1:1 (stereo TopBottom), use TopBottom format
        // Threshold: 1.5 (between 1:1 and 2:1)
        bool isMonoVideo = aspectRatio >= 1.5f;
        
        if (isMonoVideo)
        {
            // Mono 360° video: Set to Mono stereo format and enable displayMono for proper rendering
            vrVidPlayer.SetStereo(VRVideoPlayer.VideoStereo.Mono);
            vrVidPlayer.SetDisplayMono(true); // Both eyes see same image (proper mono rendering)
            Debug.Log($"[VideoManager]: Detected MONO 360° video - Resolution: {videoWidth}x{videoHeight}, Aspect Ratio: {aspectRatio:F2}:1 - Using Mono format with displayMono=true");
        }
        else
        {
            // Stereo 360° video: Use TopBottom format (default for 5.7k stereo videos)
            vrVidPlayer.SetStereo(VRVideoPlayer.VideoStereo.TopBottom);
            vrVidPlayer.SetDisplayMono(false); // Each eye sees different image (stereo)
            Debug.Log($"[VideoManager]: Detected STEREO 360° video - Resolution: {videoWidth}x{videoHeight}, Aspect Ratio: {aspectRatio:F2}:1 - Using TopBottom format");
        }

        Debug.Log("[VideoManager]: video stereo: " + vrVidPlayer.CurrentStereo);
        Debug.Log("[VideoManager]: video shape: " + vrVidPlayer.CurrentShape);
        Debug.Log("[VideoManager]: displayMono: " + (isMonoVideo ? "true (mono video)" : "false (stereo video)"));
        
        // Apply video audio volume if configured
        string videoName = ExtractVideoName(fileName);
        float videoVolume = GetVideoVolume(videoName);
        
        // Always apply volume (defaults to 1.0 if not configured)
        // Use Unity VideoPlayer API to set direct audio volume
        // Track 0 is the main audio track
        // CRITICAL: Set volume BEFORE playing to ensure it's applied
        vidPlayer.SetDirectAudioVolume(0, videoVolume);
        Debug.Log($"[VideoManager]: Set volume {videoVolume:F2} ({videoVolume * 100:F0}%) for video: {videoName ?? fileName} (before play)");
        
        // Try to play the video
        try
        {
            vidPlayer.Play();
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[VideoManager]: Exception while playing video: {e.Message}");
            CompleteVideo(playId);
            yield break;
        }
        
        // CRITICAL: Apply volume again AFTER play starts to ensure it sticks
        // Sometimes SetDirectAudioVolume needs to be called after Play() for it to take effect
        yield return null; // Wait one frame for video to start
        if (vidPlayer != null && vidPlayer.isPlaying)
        {
            vidPlayer.SetDirectAudioVolume(0, videoVolume);
            Debug.Log($"[VideoManager]: Applied volume {videoVolume:F2} ({videoVolume * 100:F0}%) to video: {videoName ?? fileName} (after play)");
        }
        // vidPlayer.isLooping = true;

        // Notify that video has started (null-safe)
        // NullCheck.SafeInvoke(OnVideoStarted, "OnVideoStarted", "VideoManager");
        
        // // Start video performance monitoring for adaptive render scale
        // if (_videoPerformanceMonitor != null)
        // {
        //     _videoPerformanceMonitor.StartMonitoring();
        // }
        
        // Ensure VRUIManager exists before showing skip button
        EnsureVRUIManagerExists();
        
        // Show skip button using VRUIManager (new UI system)
        if (VRUIManager.Instance != null)
        {
            VRUIManager.Instance.ShowSkipButton(() => SkipVideo());
            Debug.Log("[VideoManager]: Skip button shown via VRUIManager");
        }
        else
        {
            Debug.LogError("[VideoManager]: Failed to create VRUIManager - skip button will not be shown.");
        }

        // Wait for video to finish with timeout (duration + buffer)
        float videoDuration = 0f;
        if (vidPlayer.clip != null)
        {
            videoDuration = (float)vidPlayer.clip.length;
        }
        else
        {
            // Fallback: use a reasonable default timeout if clip info not available
            videoDuration = 60f; // 1 minute default
            Debug.LogWarning($"[VideoManager]: Video clip length not available, using default timeout of {videoDuration}s");
        }
        
        float playbackTimeout = videoDuration + PLAYBACK_TIMEOUT_BUFFER;
        elapsed = 0f;
        skipButton.Arm();
        
        // Wait for video to finish with timeout and null checks
        yield return new WaitUntil(() =>
        {
            // Check if video player still exists
            if (vidPlayer == null)
            {
                Debug.LogWarning("[VideoManager]: VideoPlayer destroyed during playback");
                return true; // Exit wait loop
            }
            
            // Check if this play request is still active
            if (playId != _activePlayId)
            {
                Debug.LogWarning($"[VideoManager]: Play ID mismatch ({playId} != {_activePlayId}), video was replaced");
                return true; // Exit wait loop
            }
            
            // Check for video errors
            if (videoErrorOccurred)
            {
                Debug.LogError($"[VideoManager]: Video error during playback: {videoErrorMessage}");
                return true; // Exit wait loop
            }
            
            // Check timeout
            elapsed += Time.deltaTime;
            if (elapsed >= playbackTimeout)
            {
                Debug.LogWarning($"[VideoManager]: Video playback timeout after {playbackTimeout}s: {fileName}");
                Debug.LogError(
                    $"Video playback timed out: {Path.GetFileName(fileName)} " +
                    $" Video playback timeout after {playbackTimeout}s: {fileName}"
                );
                return true; // Exit wait loop
            }
            
            return _completed || !vidPlayer.isPlaying;
        });
        
        // Stop video if it's still playing (timeout or error)
        if (vidPlayer != null && vidPlayer.isPlaying && (elapsed >= playbackTimeout || videoErrorOccurred))
        {
            Debug.LogWarning($"[VideoManager]: Stopping video due to timeout or error: {fileName}");
            vidPlayer.Stop();
        }

        // Clean up error handler
        if (vidPlayer != null && _currentErrorHandler != null) {
            vidPlayer.errorReceived -= _currentErrorHandler;
            _currentErrorHandler = null;
        }
        
        // Only complete if this is still the active play request
        if (playId == _activePlayId && !_completed)
        {
            CompleteVideo(playId);
        }

        // yield return new WaitForSeconds(cooldown);
        // canSendVideo = true;

        // StartCoroutine(videoEnded());
    }

    private void CompleteVideo(int playId) {
        if (_completed) {
            return;
        } 
        if (playId != _activePlayId) {
            Debug.LogWarning($"[VideoManager]: CompleteVideo called with mismatched playId ({playId} != {_activePlayId})");
            return;
        }

        _completed = true;

        // Stop tracked video playback coroutine
        _coroutineTracker.StopTrackedCoroutine("videoPlayback");

        // Null checks before stopping video
        if (vidPlayer != null)
        {
            try
            {
                vidPlayer.Stop();
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[VideoManager]: Error stopping video player: {e.Message}");
            }
        }
        else
        {
            Debug.LogWarning("[VideoManager]: VideoPlayer is null when trying to stop");
        }

        // Passthrough management: Keep passthrough disabled during scene swaps
        // (Removed passthrough enable after videos - user preference: no passthrough during scene transitions)
        // Passthrough is disabled when videos start (line 411) and stays disabled after videos complete

        // Hide skip button using VRUIManager (new UI system)
        if (VRUIManager.Instance != null)
        {
            VRUIManager.Instance.HideSkipButton();
            Debug.Log("[VideoManager]: Skip button hidden via VRUIManager");
        }
        
        // Safe event invocation
        // try
        // {
        //     NullCheck.SafeInvoke(OnVideoFinished, "OnVideoFinished", "VideoManager");
        // }
        // catch (System.Exception e)
        // {
        //     Debug.LogError($"[VideoManager]: Error in CompleteVideo: {e.Message}");
        // }
    }

    public void SkipVideo() {
        // Stop video performance monitoring when video is skipped
        // if (_videoPerformanceMonitor != null)
        // {
        //     Debug.Log("[VideoManager]: VideoSKip - stopMonitoring");
        //     _videoPerformanceMonitor.StopMonitoring();
        // }
        
        // Hide skip button immediately when skip is pressed
        if (VRUIManager.Instance != null)
        {
            Debug.Log("[VideoManager]: VideoSKip - hideSkip");
            VRUIManager.Instance.HideSkipButton();
        }
        
        if (vidPlayer != null && !_completed && vidPlayer.isPlaying) {
            Debug.Log("[VideoManager]: Video skipped by user.");
            CompleteVideo(_activePlayId);
            // NullCheck.SafeInvoke(OnVideoSkipped, "OnVideoSkipped", "VideoManager");
        }

        if (setAudio != null && setAudio.isPlaying) {
            setAudio.Stop();
            // NullCheck.SafeInvoke(OnAudioFinished, "OnAudioFinished", "VideoManager");
        }
    }

    public void AudioPlayer(string fileName) {
        if (setAudio == null) {
            Debug.LogError("[VideoManager] AudioSource (setAudio) is null! Cannot play audio: " + fileName);
            return;
        }
        StartCoroutine(PlayAudio(fileName));
    }

    /// <summary>
    /// Play background music simultaneously with video (doesn't wait for video to finish).
    /// Use this for music that should play behind videos (e.g., "HAPPY GO LUCKY").
    /// </summary>
    /// <param name="fileName">Audio file name (e.g., "Audios/happy-go-lucky.mp3")</param>
    /// <param name="loop">Whether to loop the music</param>
    public void PlayBackgroundMusic(string fileName, bool loop = false) {
        // Stop any existing background music
        StopBackgroundMusic();
        
        // Start background music coroutine using CoroutineTracker
        _coroutineTracker.StartTrackedCoroutine("backgroundMusic", PlayBackgroundMusicCoroutine(fileName, loop));
    }

    /// <summary>
    /// Stop background music playback.
    /// </summary>
    /// <summary>
    /// FULLY KILLS and cleans up the current video completely.
    /// Stops all playback, resets state, clears resources, and hides video.
    /// Used to ensure video is completely gone before scene transitions or restarts.
    /// </summary>
    public void KillVideoCompletely()
    {
        Debug.Log("[VideoManager]: FULLY KILLING video - complete cleanup");
        
        // 1. Stop all tracked coroutines (video playback, background music, etc.)
        if (_coroutineTracker != null)
        {
            _coroutineTracker.StopAllTrackedCoroutines();
            Debug.Log("[VideoManager]: Stopped all tracked coroutines");
        }
        
        // 2. Stop video performance monitoring
        // if (_videoPerformanceMonitor != null)
        // {
        //     _videoPerformanceMonitor.StopMonitoring();
        //     Debug.Log("[VideoManager]: Stopped video performance monitoring");
        // }
        
        // 3. Clean up error event handler
        if (vidPlayer != null && _currentErrorHandler != null)
        {
            vidPlayer.errorReceived -= _currentErrorHandler;
            _currentErrorHandler = null;
            Debug.Log("[VideoManager]: Cleared error event handler");
        }
        
        // 4. Stop VRVideoPlayer
        if (vrVidPlayer != null)
        {
            vrVidPlayer.Stop();
            Debug.Log("[VideoManager]: Stopped VRVideoPlayer");
        }
        
        // 5. Stop and clear underlying VideoPlayer
        if (vidPlayer != null)
        {
            try
            {
                if (vidPlayer.isPlaying)
                {
                    vidPlayer.Stop();
                }
                vidPlayer.url = null; // Clear URL to prevent video from showing
                vidPlayer.clip = null; // Clear clip reference
                Debug.Log("[VideoManager]: Stopped and cleared VideoPlayer (URL and clip cleared)");
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[VideoManager]: Error stopping VideoPlayer: {e.Message}");
            }
        }
        
        // 6. Stop setAudio if playing
        if (setAudio != null && setAudio.isPlaying)
        {
            setAudio.Stop();
            setAudio.clip = null; // Clear clip reference
            Debug.Log("[VideoManager]: Stopped and cleared setAudio");
        }
        
        // 7. Stop background music
        StopBackgroundMusic();
        
        // 8. Hide video holder GameObject
        if (vidHolder != null && vidHolder.activeSelf)
        {
            vidHolder.SetActive(false);
            Debug.Log("[VideoManager]: Hid video holder GameObject");
        }
        
        // 9. Reset all internal state
        ResetState();
        
        Debug.Log("[VideoManager]: Video FULLY KILLED - all state reset, resources cleared");
    }
    
    public void StopBackgroundMusic() {
        // Stop tracked background music coroutine
        _coroutineTracker.StopTrackedCoroutine("backgroundMusic");
        
        // CRITICAL FIX: Ensure AudioSource exists before stopping
        EnsureBackgroundMusicAudioExists();
        
        if (backgroundMusicAudio != null && backgroundMusicAudio.isPlaying) {
            backgroundMusicAudio.Stop();
            backgroundMusicAudio.clip = null;
        }
    }

    public void PassthroughCall(bool setPass) {
        if(setPass) {
            PassthroughManage(true);
        } else {
            PassthroughManage(false);
        }
    }

    /// <summary>
    /// Gets the full path to a video file, checking test-videos folder in Editor first,
    /// then falling back to persistentDataPath. On Quest3, always uses persistentDataPath.
    /// Handles "Videos/" prefix from JSON config: keeps subfolder structure for test-videos to match Quest3.
    /// </summary>
    private string GetVideoPath(string fileName) {
        // fileName may contain "Videos/" prefix from JSON config (e.g., "Videos/leerjaar-1-intro-1-1.mp4")
        // Keep the full path structure for test-videos to match Quest3 /files/ structure
        
        #if UNITY_EDITOR
        // In Editor: Check test-videos folder with full path structure (Videos/subfolder/file.mp4)
        // This matches the Quest3 /files/Videos/ structure
        string testVideosPath = Path.Combine(Application.dataPath, "..", "test-videos", fileName);
        testVideosPath = Path.GetFullPath(testVideosPath); // Normalize path
        
        if (File.Exists(testVideosPath)) {
            Debug.Log($"[VideoManager] ✓ Video FOUND in test-videos: {fileName}");
            return testVideosPath;
        }
        
        Debug.Log($"[VideoManager] ✗ Video NOT FOUND in test-videos: {fileName}, trying persistentDataPath");
        #endif
        
        // Fall back to persistentDataPath (or use it directly on Quest3)
        // Keep the original fileName (with "Videos/" prefix if present) for Quest3 structure
        string persistentPath = Path.Combine(Application.persistentDataPath, fileName);
        
        #if !UNITY_EDITOR
        // On Quest3, log if file exists at persistentDataPath
        if (File.Exists(persistentPath)) {
            Debug.Log($"[VideoManager] ✓ Video FOUND in persistentDataPath: {fileName}");
        } else {
            Debug.LogWarning($"[VideoManager] ✗ Video NOT FOUND in persistentDataPath: {fileName}");
        }
        #endif
        
        return persistentPath;
    }

    /// <summary>
    /// Extracts video name from file path for volume lookup.
    /// </summary>
    private string ExtractVideoName(string fileName)
    {
        // Remove path and extension
        string name = Path.GetFileNameWithoutExtension(fileName);
        
        // Try to match against known video names in GameConstants
        // This is a simple lookup - could be improved with better matching
        if (name.Contains("Schaats", System.StringComparison.OrdinalIgnoreCase) || 
            name.Contains("SchaatsBaan", System.StringComparison.OrdinalIgnoreCase))
        {
            return GameConstants.Videos.SchaatsBaan;
        }
        if (name.Contains("Strand", System.StringComparison.OrdinalIgnoreCase))
        {
            return GameConstants.Videos.Strand;
        }
        
        // Try direct match with GameConstants
        // This is a fallback - ideally video names should match exactly
        return name;
    }
    
    /// <summary>
    /// Gets the full path to an audio file, checking test-videos folder in Editor first,
    /// then falling back to persistentDataPath. On Quest3, always uses persistentDataPath.
    /// Handles both "audios/" and "Videos/" prefixes from JSON config: keeps subfolder structure for test-videos to match Quest3.
    /// </summary>
    private string GetAudioPath(string fileName) {
        // fileName may contain "audios/" or "Videos/" prefix from JSON config
        // Examples: "Audios/leerjaar-1-intro-1-1-voice.mp3" or "Audios/happy-go-lucky.mp3"
        // Keep the full path structure for test-videos to match Quest3 /files/ structure
        
        #if UNITY_EDITOR
        // In Editor: Check test-videos folder with full path structure
        // This matches the Quest3 /files/ structure
        string testVideosPath = Path.Combine(Application.dataPath, "..", "test-videos", fileName);
        testVideosPath = Path.GetFullPath(testVideosPath); // Normalize path
        
        if (File.Exists(testVideosPath)) {
            Debug.Log($"[VideoManager] ✓ Audio FOUND in test-videos: {fileName} at {testVideosPath}");
            return testVideosPath;
        }
        
        Debug.Log($"[VideoManager] ✗ Audio NOT FOUND in test-videos: {fileName} at {testVideosPath}, trying persistentDataPath");
        #endif
        
        // Fall back to persistentDataPath (or use it directly on Quest3)
        // Keep the original fileName (with prefix if present) for Quest3 structure
        string persistentPath = Path.Combine(Application.persistentDataPath, fileName);
        Debug.Log($"[VideoManager]: Audio path resolved: {fileName} -> {persistentPath}");
        return persistentPath;
    }

    private IEnumerator PlayAudio(string fileName) {
        if(setAudio == null) {
            Debug.LogError("[VideoManager] AudioSource (setAudio) is null! Cannot play audio.");
            yield break;
        }
        if(setAudio.isPlaying) {
            setAudio.Stop();
            setAudio.clip = null;
            Debug.Log("[VideoManager]: Stopped previous audio");
        }
        Debug.Log($"[VideoManager] Requesting audio: {fileName}");
        string audioPath = GetAudioPath(fileName);
        Debug.Log($"[VideoManager] Resolved audio path: {audioPath} (exists: {File.Exists(audioPath)})");

        // Check if file exists, download from CDN if needed
        if (File.Exists(audioPath)) {
            Debug.Log($"[VideoManager] ✓ Audio FOUND: {fileName} at {audioPath}");
        } else {
            Debug.LogWarning($"[VideoManager] ✗ Audio NOT FOUND: {fileName} at {audioPath}");
            Debug.LogWarning($"[VideoManager] File check details - Path: '{audioPath}', Directory exists: {Directory.Exists(Path.GetDirectoryName(audioPath))}");
            
            // Try to download from CDN (works in Editor and on Quest3)
            CDNDownloadManager.EnsureInstanceExists();
            if (CDNDownloadManager.Instance != null) {
                Debug.Log($"[VideoManager] → Attempting CDN download: {fileName}");
                bool fileReady = false;
                string errorMessage = null;
                
                CDNDownloadManager.Instance.EnsureFileExists(
                    fileName,
                    onComplete: (path) => {
                        audioPath = path;
                        fileReady = true;
                    },
                    onError: (error) => {
                        errorMessage = error;
                        fileReady = true; // Set to true to exit wait loop
                    }
                );
                
                // Wait for download to complete (or fail)
                yield return new WaitUntil(() => fileReady);
                
                if (!string.IsNullOrEmpty(errorMessage) || !File.Exists(audioPath)) {
                    Debug.LogError(fileName + errorMessage + "Unknown error");
                    yield break;
                } else {
                    Debug.Log($"[VideoManager] ✓ CDN download SUCCESS: {fileName} → {audioPath}");
                }
            } else {
                Debug.LogError(fileName + "Audio" + audioPath);
                yield break;
            }
        }

        // UnityWebRequest requires "file://" prefix for local files
        // On Windows, need "file:///" (three slashes), on other platforms "file://" (two slashes)
        string uri = audioPath.Replace("\\", "/"); // Normalize path separators
        if (!uri.StartsWith("file://")) {
            if (Application.platform == RuntimePlatform.WindowsEditor || Application.platform == RuntimePlatform.WindowsPlayer) {
                uri = "file:///" + uri;
            } else {
                uri = "file://" + uri;
            }
        }
        
        using(UnityWebRequest www = UnityWebRequestMultimedia.GetAudioClip(uri, AudioType.MPEG)) {
            Debug.Log($"[VideoManager]: Loading audio from: {uri}");
            yield return www.SendWebRequest();
            if (www.result == UnityWebRequest.Result.ConnectionError) {
                Debug.LogError("Error while loading audio: " + www.error);
                yield break;
            } else {
                audioC = DownloadHandlerAudioClip.GetContent(www);
                if (setAudio != null && audioC != null) {
                    setAudio.clip = audioC;
                    setAudio.Play();

                    yield return new WaitUntil(() => setAudio == null || setAudio.isPlaying == false);

                    // NullCheck.SafeInvoke(OnAudioFinished, "OnAudioFinished", "VideoManager");
                } else {
                    Debug.LogError("[VideoManager] AudioSource or AudioClip is null! Cannot play audio.");
                }
            }
        }
    }

    /// <summary>
    /// CRITICAL FIX: Ensures backgroundMusicAudio AudioSource exists, creates it if missing.
    /// Also configures it properly for background music playback.
    /// </summary>
    private void EnsureBackgroundMusicAudioExists() {
        if (backgroundMusicAudio == null) {
            // Try to find existing AudioSource on this GameObject
            backgroundMusicAudio = GetComponent<AudioSource>();
            
            // If still null, create a new AudioSource
            if (backgroundMusicAudio == null) {
                backgroundMusicAudio = gameObject.AddComponent<AudioSource>();
                Debug.Log("[VideoManager]: Created backgroundMusicAudio AudioSource component (was missing)");
            } else {
                Debug.Log("[VideoManager]: Found existing AudioSource component, using it for background music");
            }
        }
        
        // CRITICAL FIX: Ensure AudioSource is properly configured for playback
        if (backgroundMusicAudio != null) {
            // Ensure GameObject is active (AudioSource won't play on inactive GameObjects)
            if (!gameObject.activeInHierarchy) {
                Debug.LogWarning("[VideoManager]: VideoManager GameObject is inactive! AudioSource may not play. Activating GameObject...");
                gameObject.SetActive(true);
            }
            
            // Configure AudioSource settings
            backgroundMusicAudio.playOnAwake = false;
            backgroundMusicAudio.loop = false; // Will be set per-clip
            backgroundMusicAudio.volume = 1.0f; // Full volume by default
            backgroundMusicAudio.mute = false; // Ensure not muted
            backgroundMusicAudio.enabled = true; // Ensure component is enabled
            
            Debug.Log($"[VideoManager]: AudioSource configured - Volume: {backgroundMusicAudio.volume}, Mute: {backgroundMusicAudio.mute}, Enabled: {backgroundMusicAudio.enabled}, GameObject Active: {gameObject.activeInHierarchy}");
        }
    }
    
    /// <summary>
    /// Coroutine to play background music simultaneously with video.
    /// CRITICAL FIX: Ensure music plays during videos (not suppressed).
    /// </summary>
    private IEnumerator PlayBackgroundMusicCoroutine(string fileName, bool loop) {
        // CRITICAL FIX: Ensure AudioSource exists before using it
        EnsureBackgroundMusicAudioExists();
        
        if (backgroundMusicAudio == null) {
            Debug.LogError("[VideoManager]: BackgroundMusicAudio is null after creation attempt! Cannot play background music.");
            yield break;
        }

        // Stop any currently playing background music
        if (backgroundMusicAudio.isPlaying) {
            backgroundMusicAudio.Stop();
            backgroundMusicAudio.clip = null;
        }
        
        // CRITICAL FIX: Ensure AudioSource is not muted and volume is set correctly
        // Videos have their own audio, but background music should play simultaneously
        backgroundMusicAudio.mute = false;
        backgroundMusicAudio.volume = 1.0f; // Full volume for background music
        Debug.Log($"[VideoManager]: Background music AudioSource configured - mute: {backgroundMusicAudio.mute}, volume: {backgroundMusicAudio.volume}");

        Debug.Log($"[VideoManager] Requesting background music: {fileName}");
        string audioPath = GetAudioPath(fileName);
        Debug.Log($"[VideoManager] Resolved background music path: {audioPath} (exists: {File.Exists(audioPath)})");

        // Check if file exists, download from CDN if needed
        if (!File.Exists(audioPath)) {
            Debug.LogWarning($"[VideoManager] ✗ Background music NOT FOUND: {fileName} at {audioPath}");
            
            // Try to download from CDN
            CDNDownloadManager.EnsureInstanceExists();
            if (CDNDownloadManager.Instance != null) {
                Debug.Log($"[VideoManager] → Attempting CDN download: {fileName}");
                bool fileReady = false;
                string errorMessage = null;
                
                CDNDownloadManager.Instance.EnsureFileExists(
                    fileName,
                    onComplete: (path) => {
                        audioPath = path;
                        fileReady = true;
                    },
                    onError: (error) => {
                        errorMessage = error;
                        fileReady = true;
                    }
                );
                
                yield return new WaitUntil(() => fileReady);
                
                if (!string.IsNullOrEmpty(errorMessage) || !File.Exists(audioPath)) {
                    Debug.LogError($"[VideoManager] ✗ CDN download FAILED: {fileName} - {errorMessage ?? "Unknown error"}");
                    yield break;
                } else {
                    Debug.Log($"[VideoManager] ✓ CDN download SUCCESS: {fileName} → {audioPath}");
                }
            } else {
                Debug.LogError($"[VideoManager]: Background music file not found: {audioPath} and CDNDownloadManager not available");
                yield break;
            }
        }

        // UnityWebRequest requires "file://" prefix for local files
        string uri = audioPath.Replace("\\", "/");
        if (!uri.StartsWith("file://")) {
            if (Application.platform == RuntimePlatform.WindowsEditor || Application.platform == RuntimePlatform.WindowsPlayer) {
                uri = "file:///" + uri;
            } else {
                uri = "file://" + uri;
            }
        }
        
        using(UnityWebRequest www = UnityWebRequestMultimedia.GetAudioClip(uri, AudioType.MPEG)) {
            Debug.Log($"[VideoManager]: Loading background music from: {uri}");
            yield return www.SendWebRequest();
            if (www.result == UnityWebRequest.Result.ConnectionError) {
                Debug.LogError($"[VideoManager]: Error loading background music: {www.error}");
                yield break;
            } else {
                AudioClip clip = DownloadHandlerAudioClip.GetContent(www);
                
                // CRITICAL FIX: Validate clip was loaded correctly
                if (clip == null) {
                    Debug.LogError($"[VideoManager]: Failed to load audio clip from {uri}. DownloadHandlerAudioClip.GetContent returned null.");
                    yield break;
                }
                
                // CRITICAL FIX: Ensure AudioSource is still configured before playing
                EnsureBackgroundMusicAudioExists();
                
                backgroundMusicAudio.clip = clip;
                backgroundMusicAudio.loop = loop;
                
                // CRITICAL FIX: Additional validation before playing
                if (!gameObject.activeInHierarchy) {
                    Debug.LogError("[VideoManager]: Cannot play background music - VideoManager GameObject is inactive!");
                    yield break;
                }
                
                if (!backgroundMusicAudio.enabled) {
                    Debug.LogWarning("[VideoManager]: AudioSource component is disabled! Enabling it...");
                    backgroundMusicAudio.enabled = true;
                }
                
                // CRITICAL FIX: Additional validation before playing
                Debug.Log($"[VideoManager]: Pre-play check - clip: {(clip != null ? $"loaded ({clip.length:F2}s, {clip.channels} channels, {clip.frequency}Hz)" : "NULL")}, " +
                         $"AudioSource enabled: {backgroundMusicAudio.enabled}, GameObject active: {gameObject.activeInHierarchy}, " +
                         $"volume: {backgroundMusicAudio.volume}, mute: {backgroundMusicAudio.mute}");
                
                // Check for AudioListener (required for 3D audio, but 2D should work without)
                AudioListener listener = FindFirstObjectByType<AudioListener>();
                if (listener == null) {
                    Debug.LogWarning("[VideoManager]: No AudioListener found in scene! Audio may not be audible. AudioListener is usually on the Main Camera.");
                } else {
                    Debug.Log($"[VideoManager]: AudioListener found on: {listener.gameObject.name}");
                }
                
                // CRITICAL FIX: Configure AudioSource for 2D playback (not spatial)
                backgroundMusicAudio.spatialBlend = 0f; // 0 = 2D (full 2D), 1 = 3D (spatial)
                backgroundMusicAudio.rolloffMode = AudioRolloffMode.Logarithmic;
                
                // Play the audio
                backgroundMusicAudio.Play();
                
                // CRITICAL FIX: Force immediate play check (sometimes Play() needs a frame)
                int frameCount = 0;
                while (!backgroundMusicAudio.isPlaying && frameCount < 10) {
                    yield return null; // Wait one frame
                    frameCount++;
                }
                
                // Additional attempt: Try PlayOneShot if regular Play() didn't work
                if (!backgroundMusicAudio.isPlaying && clip != null) {
                    Debug.LogWarning("[VideoManager]: AudioSource.Play() didn't start. Trying PlayOneShot as fallback...");
                    backgroundMusicAudio.PlayOneShot(clip, backgroundMusicAudio.volume);
                    yield return null; // Wait one frame
                }
                
                // Final verification
                if (backgroundMusicAudio.isPlaying) {
                    Debug.Log($"[VideoManager]: ✓ Background music playing: {fileName} (loop: {loop}, clip length: {clip.length:F2}s, volume: {backgroundMusicAudio.volume}, spatialBlend: {backgroundMusicAudio.spatialBlend})");
                } else {
                    Debug.LogError($"[VideoManager]: ✗ Background music FAILED to start playing: {fileName}.\n" +
                                 $"  AudioSource.isPlaying = {backgroundMusicAudio.isPlaying}\n" +
                                 $"  clip = {(clip != null ? $"loaded ({clip.length:F2}s, {clip.channels} channels)" : "NULL")}\n" +
                                 $"  enabled = {backgroundMusicAudio.enabled}\n" +
                                 $"  GameObject active = {gameObject.activeInHierarchy}\n" +
                                 $"  volume = {backgroundMusicAudio.volume}\n" +
                                 $"  mute = {backgroundMusicAudio.mute}\n" +
                                 $"  spatialBlend = {backgroundMusicAudio.spatialBlend}\n" +
                                 $"  AudioListener = {(listener != null ? $"found on {listener.gameObject.name}" : "NOT FOUND")}\n" +
                                 $"  TROUBLESHOOTING: Check Unity Editor audio settings, ensure AudioListener exists, verify audio file format is supported.");
                }
                
                // Wait for music to finish (if not looping)
                if (!loop) {
                    yield return new WaitUntil(() => !backgroundMusicAudio.isPlaying);
                    Debug.Log($"[VideoManager]: Background music finished: {fileName}");
                }
            }
        }
    }
    
    // #region Video Fade Transitions
    
    /// <summary>
    /// Ensures the fade overlay Canvas and Image exist for video fade transitions.
    /// Creates them if they don't exist.
    /// </summary>
    // private void EnsureFadeOverlayExists()
    // {
    //     if (_fadeOverlayCanvas != null && _fadeOverlayImage != null)
    //     {
    //         return; // Already exists
    //     }
        
    //     // Find or create OVRCameraRig
    //     RefreshOVRCameraRig();
    //     if (_ovrCameraRig == null)
    //     {
    //         Debug.LogWarning("[VideoManager]: Cannot create fade overlay - OVRCameraRig not found");
    //         return;
    //     }
        
    //     // Find the center eye camera for overlay
    //     Camera centerEyeCamera = _ovrCameraRig.GetComponentInChildren<Camera>();
    //     if (centerEyeCamera == null)
    //     {
    //         centerEyeCamera = Camera.main;
    //     }
        
    //     if (centerEyeCamera == null)
    //     {
    //         Debug.LogWarning("[VideoManager]: Cannot create fade overlay - no camera found");
    //         return;
    //     }
        
    //     // Create Canvas for fade overlay as child of camera
    //     _fadeOverlayCanvas = new GameObject("VideoFadeOverlayCanvas");
    //     _fadeOverlayCanvas.transform.SetParent(centerEyeCamera.transform, false);
        
    //     UnityEngine.Canvas canvas = _fadeOverlayCanvas.AddComponent<UnityEngine.Canvas>();
    //     canvas.renderMode = RenderMode.ScreenSpaceCamera;
    //     canvas.worldCamera = centerEyeCamera;
    //     canvas.planeDistance = 0.1f; // Close to camera for overlay effect
    //     canvas.sortingOrder = 9999; // High sorting order to be on top
        
    //     UnityEngine.UI.CanvasScaler scaler = _fadeOverlayCanvas.AddComponent<UnityEngine.UI.CanvasScaler>();
    //     scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
    //     scaler.referenceResolution = new Vector2(1920, 1080);
        
    //     _fadeOverlayCanvas.AddComponent<UnityEngine.UI.GraphicRaycaster>();
        
    //     // Create Image for fade overlay
    //     GameObject imageObj = new GameObject("FadeOverlayImage");
    //     imageObj.transform.SetParent(_fadeOverlayCanvas.transform, false);
        
    //     _fadeOverlayImage = imageObj.AddComponent<UnityEngine.UI.Image>();
    //     _fadeOverlayImage.color = new Color(0f, 0f, 0f, 0f); // Start transparent (black with 0 alpha)
        
    //     RectTransform rectTransform = imageObj.GetComponent<RectTransform>();
    //     rectTransform.anchorMin = Vector2.zero;
    //     rectTransform.anchorMax = Vector2.one;
    //     rectTransform.sizeDelta = Vector2.zero;
    //     rectTransform.anchoredPosition = Vector2.zero;
        
    //     // Make sure it's initially hidden
    //     _fadeOverlayImage.gameObject.SetActive(false);
        
    //     Debug.Log("[VideoManager]: Created fade overlay Canvas and Image");
    // }
    
    /// <summary>
    /// Fades out the current video (fades to black).
    /// </summary>
    /// <param name="duration">Fade duration in seconds (default: 1s)</param>
    /// <returns>Coroutine</returns>
    // public IEnumerator FadeOutVideo(float duration = DEFAULT_FADE_DURATION)
    // {
    //     EnsureFadeOverlayExists();
        
    //     if (_fadeOverlayImage == null)
    //     {
    //         Debug.LogWarning("[VideoManager]: Cannot fade out - fade overlay not available");
    //         yield break;
    //     }
        
    //     // Enable the overlay image
    //     _fadeOverlayImage.gameObject.SetActive(true);
        
    //     // Fade from transparent (alpha 0) to opaque (alpha 1)
    //     float elapsed = 0f;
    //     Color color = _fadeOverlayImage.color;
        
    //     while (elapsed < duration)
    //     {
    //         elapsed += Time.deltaTime;
    //         float alpha = Mathf.Clamp01(elapsed / duration);
    //         color.a = alpha;
    //         _fadeOverlayImage.color = color;
    //         yield return null;
    //     }
        
    //     // Ensure fully opaque
    //     color.a = 1f;
    //     _fadeOverlayImage.color = color;
        
    //     Debug.Log($"[VideoManager]: Fade out complete ({duration:F2}s)");
    // }
    
    /// <summary>
    /// Fades in the next video (fades from black to visible).
    /// </summary>
    /// <param name="duration">Fade duration in seconds (default: 1s)</param>
    /// <returns>Coroutine</returns>
    // public IEnumerator FadeInVideo(float duration = DEFAULT_FADE_DURATION)
    // {
    //     EnsureFadeOverlayExists();
        
    //     if (_fadeOverlayImage == null)
    //     {
    //         Debug.LogWarning("[VideoManager]: Cannot fade in - fade overlay not available");
    //         yield break;
    //     }
        
    //     // Fade from opaque (alpha 1) to transparent (alpha 0)
    //     float elapsed = 0f;
    //     Color color = _fadeOverlayImage.color;
        
    //     while (elapsed < duration)
    //     {
    //         elapsed += Time.deltaTime;
    //         float alpha = Mathf.Clamp01(1f - (elapsed / duration));
    //         color.a = alpha;
    //         _fadeOverlayImage.color = color;
    //         yield return null;
    //     }
        
    //     // Ensure fully transparent
    //     color.a = 0f;
    //     _fadeOverlayImage.color = color;
        
    //     // Hide the overlay image when fade is complete
    //     _fadeOverlayImage.gameObject.SetActive(false);
        
    //     Debug.Log($"[VideoManager]: Fade in complete ({duration:F2}s)");
    // }
    
    // /// <summary>
    // /// Plays a video with fade transitions (fade out current, fade in new).
    // /// If no video is currently playing, only fades in the new video.
    // /// </summary>
    // /// <param name="fileName">Video file name to play</param>
    // /// <param name="fadeDuration">Fade duration in seconds (default: 1s)</param>
    // public void PlayVideoWithFade(string fileName, float fadeDuration = DEFAULT_FADE_DURATION)
    // {
    //     // Null check for fileName
    //     if (string.IsNullOrEmpty(fileName))
    //     {
    //         Debug.LogError("[VideoManager] Cannot play video with fade: fileName is null or empty");
    //         Debug.LogError(
    //             "Video file name is missing. Please check your configuration file." +
    //             "PlayVideoWithFade called with null or empty fileName"
    //         );
    //         return;
    //     }
        
    //     // Start fade transition coroutine
    //     _coroutineTracker.StartTrackedCoroutine("videoFadeTransition", PlayVideoWithFadeCoroutine(fileName, fadeDuration));
    // }
    
    // /// <summary>
    // /// Coroutine that handles video playback with fade transitions.
    // /// </summary>
    // private IEnumerator PlayVideoWithFadeCoroutine(string fileName, float fadeDuration)
    // {
    //     // Fade out current video if one is playing
    //     if (vidPlayer != null && vidPlayer.isPlaying)
    //     {
    //         Debug.Log("[VideoManager]: Fading out current video before playing new video");
    //         yield return StartCoroutine(FadeOutVideo(fadeDuration));
            
    //         // Stop the current video
    //         if (vidPlayer != null && vidPlayer.isPlaying)
    //         {
    //             vidPlayer.Stop();
    //         }
    //     }
        
    //     // Play the new video (it will start behind the fade overlay)
    //     PlayVideo(fileName);
        
    //     // Wait for video to start playing (with timeout)
    //     float waitTimeout = 5f;
    //     float elapsed = 0f;
    //     while ((vidPlayer == null || !vidPlayer.isPlaying) && elapsed < waitTimeout)
    //     {
    //         elapsed += Time.deltaTime;
    //         yield return null;
    //     }
        
    //     if (vidPlayer == null || !vidPlayer.isPlaying)
    //     {
    //         Debug.LogWarning($"[VideoManager]: Video did not start playing within {waitTimeout}s, proceeding with fade in anyway");
    //     }
        
    //     // Fade in the new video
    //     yield return StartCoroutine(FadeInVideo(fadeDuration));
        
    //     Debug.Log($"[VideoManager]: Video playback with fade complete: {fileName}");
    // }
    
    // #endregion

}

