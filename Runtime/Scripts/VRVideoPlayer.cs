using System.Collections;
using UnityEngine;
using UnityEngine.Video;
using UnityEngine.Rendering;

/// <summary>
/// Production-ready VR video player for 180-degree and 360-degree videos.
/// Handles OVROverlay setup for proper VR video rendering.
/// </summary>
public class VRVideoPlayer : MonoBehaviour
{
    [SerializeField] private VideoPlayer videoPlayer;
    [SerializeField] private OVROverlay overlay;
    [SerializeField] private Renderer mediaRenderer;
    
    [Header("Video Settings")]
    [SerializeField] private VideoShape shape = VideoShape._180;
    [SerializeField] private VideoStereo stereo = VideoStereo.Mono;
    [SerializeField] private bool displayMono = false;
    [SerializeField] private bool loopVideo = false;

    public VideoShape CurrentShape => shape;
    public VideoStereo CurrentStereo => stereo;
    
    public enum VideoShape
    {
        _360,
        _180,
        Quad
    }
    
    public enum VideoStereo
    {
        Mono,
        TopBottom,
        LeftRight,
        BottomTop
    }
    
    // Track last state to avoid unnecessary updates
    private VideoShape _lastShape = (VideoShape)(-1);
    private VideoStereo _lastStereo = (VideoStereo)(-1);
    private bool _lastDisplayMono = false;
    
    public bool IsPlaying { get; private set; }
    public static VRVideoPlayer Instance;
    
    // Flag to prevent operations during shutdown
    private bool _isShuttingDown = false;
    
    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            // Disable overlay before destroying duplicate to prevent render pipeline errors
            if (overlay != null)
            {
                try
                {
                    overlay.enabled = false;
                }
                catch (System.Exception)
                {
                    // Ignore errors - overlay may already be destroyed
                }
            }
            Destroy(gameObject);   // Kill the duplicate
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);
        // Get or add required components (auto-assign if not set in Inspector)
        if (videoPlayer == null)
        {
            videoPlayer = GetComponent<VideoPlayer>();
            if (videoPlayer == null)
            {
                videoPlayer = gameObject.AddComponent<VideoPlayer>();
            }
        }
        
        if (overlay == null)
        {
            overlay = GetComponent<OVROverlay>();
            if (overlay == null)
            {
                overlay = gameObject.AddComponent<OVROverlay>();
            }
        }
        
        if (mediaRenderer == null)
        {
            mediaRenderer = GetComponent<Renderer>();
        }
        
        // Initialize overlay
        overlay.enabled = false;
        overlay.isExternalSurface = false; // Use Unity VideoPlayer, not native plugin
        
        // Enable overlay for VR (Equirect shape works on Android)
        overlay.enabled = (overlay.currentOverlayShape != OVROverlay.OverlayShape.Equirect ||
                          Application.platform == RuntimePlatform.Android);
        
        #if UNITY_EDITOR
        overlay.currentOverlayShape = OVROverlay.OverlayShape.Quad;
        overlay.enabled = true;
        #endif
        
        videoPlayer.isLooping = loopVideo;
    }

    public void ResetState() {
        _lastDisplayMono = false;
        displayMono = false;
        loopVideo = false;
        StopAllCoroutines();
        
        if (videoPlayer == null)
        {
            videoPlayer = GetComponent<VideoPlayer>();
            if (videoPlayer == null)
            {
                videoPlayer = gameObject.AddComponent<VideoPlayer>();
            }
        }
        
        if (overlay == null)
        {
            overlay = GetComponent<OVROverlay>();
            if (overlay == null)
            {
                overlay = gameObject.AddComponent<OVROverlay>();
            }
        }
        
        if (mediaRenderer == null)
        {
            mediaRenderer = GetComponent<Renderer>();
        }
        
        // Initialize overlay
        overlay.enabled = false;
        overlay.isExternalSurface = false; // Use Unity VideoPlayer, not native plugin
        
        // Enable overlay for VR (Equirect shape works on Android)
        overlay.enabled = (overlay.currentOverlayShape != OVROverlay.OverlayShape.Equirect ||
                          Application.platform == RuntimePlatform.Android);
        
        #if UNITY_EDITOR
        overlay.currentOverlayShape = OVROverlay.OverlayShape.Quad;
        overlay.enabled = true;
        #endif
        
        videoPlayer.isLooping = loopVideo;

    }
    
    private void Update()
    {
        // Early exit if shutting down or objects are null
        if (_isShuttingDown || overlay == null || videoPlayer == null)
        {
            return;
        }
        
        // Additional safety check - verify overlay hasn't been destroyed
        if (overlay == null || overlay.Equals(null))
        {
            _isShuttingDown = true;
            return;
        }
        
        try
        {
            UpdateShapeAndStereo();
            
            if (!overlay.isExternalSurface && videoPlayer != null)
            {
                var displayTexture = videoPlayer.texture != null ? videoPlayer.texture : Texture2D.blackTexture;
                
                if (overlay.enabled)
                {
                    if (overlay.textures[0] != displayTexture)
                    {
                        // OVROverlay won't check if the texture changed, so disable to clear old texture
                        overlay.enabled = false;
                        overlay.textures[0] = displayTexture;
                        overlay.enabled = true;
                    }
                }
                else if (mediaRenderer != null && mediaRenderer.material != null)
                {
                    mediaRenderer.material.mainTexture = displayTexture;
                    // Set source rect vectors if shader supports them (convert Rect to Vector4: x, y, width, height)
                    if (mediaRenderer.material.HasProperty("_SrcRectLeft"))
                    {
                        Rect rect = overlay.srcRectLeft;
                        mediaRenderer.material.SetVector("_SrcRectLeft", new Vector4(rect.x, rect.y, rect.width, rect.height));
                    }
                    if (mediaRenderer.material.HasProperty("_SrcRectRight"))
                    {
                        Rect rect = overlay.srcRectRight;
                        mediaRenderer.material.SetVector("_SrcRectRight", new Vector4(rect.x, rect.y, rect.width, rect.height));
                    }
                }
                
                IsPlaying = videoPlayer.isPlaying;
            }
        }
        catch (MissingReferenceException)
        {
            // Overlay was destroyed - set shutdown flag and exit
            _isShuttingDown = true;
            overlay = null;
        }
        catch (System.Exception ex)
        {
            // Log other errors but don't spam
            if (!_isShuttingDown)
            {
                Debug.LogWarning($"[VRVideoPlayer] Error in Update: {ex.Message}");
            }
        }
    }
    
    private void UpdateShapeAndStereo()
    {
        // Null check to prevent errors during shutdown
        if (_isShuttingDown || overlay == null || overlay.Equals(null))
        {
            return;
        }
        
        try
        {
            if (shape != _lastShape || stereo != _lastStereo || displayMono != _lastDisplayMono)
            {
                Rect destRect = new Rect(0, 0, 1, 1);
                
                switch (shape)
                {
                    case VideoShape._360:
                        overlay.currentOverlayShape = OVROverlay.OverlayShape.Equirect;
                        break;
                    case VideoShape._180:
                        overlay.currentOverlayShape = OVROverlay.OverlayShape.Equirect;
                        destRect = new Rect(0.25f, 0, 0.5f, 1.0f); // Show center 50% (180 degrees)
                        break;
                    case VideoShape.Quad:
                    default:
                        overlay.currentOverlayShape = OVROverlay.OverlayShape.Quad;
                        break;
                }
                
                overlay.overrideTextureRectMatrix = true;
                overlay.invertTextureRects = false;
                
                Rect sourceLeft = new Rect(0, 0, 1, 1);
                Rect sourceRight = new Rect(0, 0, 1, 1);
                
                switch (stereo)
                {
                    case VideoStereo.LeftRight:
                        sourceLeft = new Rect(0.0f, 0.0f, 0.5f, 1.0f);
                        sourceRight = new Rect(0.5f, 0.0f, 0.5f, 1.0f);
                        break;
                    case VideoStereo.TopBottom:
                        sourceLeft = new Rect(0.0f, 0.5f, 1.0f, 0.5f);
                        sourceRight = new Rect(0.0f, 0.0f, 1.0f, 0.5f);
                        break;
                    case VideoStereo.BottomTop:
                        sourceLeft = new Rect(0.0f, 0.0f, 1.0f, 0.5f);
                        sourceRight = new Rect(0.0f, 0.5f, 1.0f, 0.5f);
                        break;
                }
                
                overlay.SetSrcDestRects(sourceLeft, displayMono ? sourceLeft : sourceRight, destRect, destRect);
                
                _lastDisplayMono = displayMono;
                _lastStereo = stereo;
                _lastShape = shape;
            }
        }
        catch (MissingReferenceException)
        {
            _isShuttingDown = true;
            overlay = null;
        }
    }
    
    /// <summary>
    /// Checks if a file is likely an image file based on its extension.
    /// Handles both single extensions (.jpg) and double extensions (.mp3.mpeg where .mpeg might be misleading).
    /// </summary>
    private bool IsImageFile(string filePath)
    {
        string fileName = System.IO.Path.GetFileName(filePath).ToLowerInvariant();
        string extension = System.IO.Path.GetExtension(filePath).ToLowerInvariant();
        
        // Check for common image extensions
        string[] imageExtensions = { ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".tga", ".tiff", ".tif", ".exr", ".hdr", ".dds", ".psd" };
        if (System.Array.IndexOf(imageExtensions, extension) >= 0)
        {
            return true;
        }
        
        // Check for double extensions that might indicate an image (e.g., "file.mp3.mpeg" where it's actually a jpg)
        // Look for image extensions in the filename before the last extension
        foreach (string imgExt in imageExtensions)
        {
            if (fileName.Contains(imgExt) && fileName.IndexOf(imgExt) < fileName.LastIndexOf('.'))
            {
                return true;
            }
        }
        
        // Check for known problematic filenames (album covers with wrong extensions)
        // This specific file is known to be an image
        if (fileName.Contains("album") || fileName.Contains("cover") || 
            (fileName.Contains("KPop_Demon_Hunters") && fileName.Contains("mp3.mpeg")))
        {
            return true;
        }
        
        return false;
    }

    /// <summary>
    /// Play a video from a file path
    /// </summary>
    public void PlayVideo(string filePath)
    {
        if (string.IsNullOrEmpty(filePath))
        {
            Debug.LogError("[VRVideoPlayer] No file path provided!");
            return;
        }
        
        // Check if file is actually an image (album covers sometimes have wrong extensions)
        if (System.IO.File.Exists(filePath) && IsImageFile(filePath))
        {
            Debug.LogWarning($"[VRVideoPlayer]: Skipping image file (not video): {System.IO.Path.GetFileName(filePath)}");
            return;
        }
        
        Debug.Log($"[VRVideoPlayer] Playing: {System.IO.Path.GetFileName(filePath)}");
        
        try
        {
            videoPlayer.url = filePath;
            videoPlayer.Prepare();
            StartCoroutine(WaitForPrepareAndPlay());
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[VRVideoPlayer]: Exception while preparing video: {e.Message}");
        }
    }
    
    private IEnumerator WaitForPrepareAndPlay()
    {
        // Wait for preparation with timeout to detect failures (e.g., image files)
        float timeout = 5f; // 5 second timeout
        float elapsed = 0f;
        
        yield return new WaitUntil(() => 
        {
            elapsed += Time.deltaTime;
            if (videoPlayer == null) return true; // Exit if destroyed
            return videoPlayer.isPrepared || elapsed >= timeout;
        });
        
        // Check if still valid before playing
        if (videoPlayer != null && this != null)
        {
            // Check if preparation succeeded
            if (!videoPlayer.isPrepared)
            {
                if (elapsed >= timeout)
                {
                    Debug.LogWarning($"[VRVideoPlayer]: Video preparation timed out - file may be an image or unsupported format: {System.IO.Path.GetFileName(videoPlayer.url)}");
                }
                IsPlaying = false;
                yield break;
            }
            
            try
            {
                videoPlayer.Play();
                IsPlaying = true;
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[VRVideoPlayer]: Error playing video: {e.Message}");
                IsPlaying = false;
            }
        }
    }
    
    /// <summary>
    /// Play the current video
    /// </summary>
    public void Play()
    {
        if (videoPlayer != null)
        {
            videoPlayer.Play();
            IsPlaying = true;
        }
    }
    
    /// <summary>
    /// Pause the current video
    /// </summary>
    public void Pause()
    {
        if (videoPlayer != null)
        {
            videoPlayer.Pause();
            IsPlaying = false;
        }
    }
    
    /// <summary>
    /// Stop the current video
    /// </summary>
    public void Stop()
    {
        if (videoPlayer != null)
        {
            videoPlayer.Stop();
            IsPlaying = false;
        }
    }
    
    /// <summary>
    /// Set video shape (180, 360, or Quad)
    /// </summary>
    public void SetShape(VideoShape newShape)
    {
        shape = newShape;
        _lastShape = (VideoShape)(-1); // Force update
    }
    
    /// <summary>
    /// Set stereo format
    /// </summary>
    public void SetStereo(VideoStereo newStereo)
    {
        stereo = newStereo;
        _lastStereo = (VideoStereo)(-1); // Force update
    }
    
    /// <summary>
    /// Set display mono mode. When true, both eyes see the same image (for mono 360° videos).
    /// This improves the visual quality of mono videos in VR by ensuring proper rendering.
    /// </summary>
    public void SetDisplayMono(bool mono)
    {
        displayMono = mono;
        _lastDisplayMono = !mono; // Force update by setting to opposite value
    }
    
    /// <summary>
    /// Seek to a specific time in the video (in seconds)
    /// </summary>
    public void SeekTo(double timeInSeconds)
    {
        if (videoPlayer != null && videoPlayer.canSetTime)
        {
            videoPlayer.time = timeInSeconds;
            Debug.Log($"[VRVideoPlayer] Seeking to {timeInSeconds}s");
        }
        else
        {
            Debug.LogWarning("[VRVideoPlayer] Cannot seek - video not ready or seeking not supported");
        }
    }
    
    /// <summary>
    /// Seek to a specific time in the video (in milliseconds)
    /// </summary>
    public void SeekToMilliseconds(long timeInMs)
    {
        SeekTo(timeInMs / 1000.0);
    }
    
    /// <summary>
    /// Cleanup on disable - disable overlay before destruction to prevent render pipeline errors
    /// </summary>
    private void OnDisable()
    {
        _isShuttingDown = true;
        
        // CRITICAL: Disable overlay BEFORE destruction
        // This prevents render pipeline from calling destroyed overlay
        // Setting enabled = false should unregister OVROverlay from render pipeline events
        DisableOverlaySafely();
    }
    
    /// <summary>
    /// Safely disables the overlay component to prevent render pipeline errors
    /// </summary>
    private void DisableOverlaySafely()
    {
        if (overlay == null) return;
        
        // Check if overlay is already destroyed
        try
        {
            // Test if overlay is still valid by accessing a property
            var test = overlay.enabled;
        }
        catch (MissingReferenceException)
        {
            // Overlay already destroyed - clear reference and exit
            overlay = null;
            return;
        }
        catch (System.Exception)
        {
            // Overlay is invalid - clear reference and exit
            overlay = null;
            return;
        }
        
        // Overlay is still valid - disable it to unregister from render pipeline
        // Setting enabled = false should cause OVROverlay to unregister from RenderPipelineManager.beginCameraRendering
        try
        {
            overlay.enabled = false;
        }
        catch (MissingReferenceException)
        {
            // Overlay destroyed during disable - clear reference
            overlay = null;
        }
        catch (System.Exception)
        {
            // Ignore other errors during shutdown
        }
    }
    
    
    /// <summary>
    /// Cleanup on destroy - disable overlay to unsubscribe from render events
    /// </summary>
    private void OnDestroy()
    {
        _isShuttingDown = true;
        
        // Disable overlay safely (should already be disabled in OnDisable, but double-check)
        DisableOverlaySafely();
        
        // Clear overlay reference
        overlay = null;
        
        if (videoPlayer != null && !videoPlayer.Equals(null))
        {
            try
            {
                videoPlayer.Stop();
            }
            catch (System.Exception)
            {
                // Ignore errors during shutdown
            }
        }
        
        StopAllCoroutines();
    }
    
    /// <summary>
    /// Cleanup on application quit - ensure overlay is disabled
    /// </summary>
    private void OnApplicationQuit()
    {
        _isShuttingDown = true;
        
        // Deactivate GameObject first to prevent render callbacks
        if (gameObject != null && gameObject.activeSelf)
        {
            gameObject.SetActive(false);
        }
        
        // Disable overlay safely
        DisableOverlaySafely();
        overlay = null;
    }
}