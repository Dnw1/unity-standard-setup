    using System.Collections;
    using UnityEngine;
    using UnityEngine.SceneManagement;

namespace com.dnw.standardpackage
{

    /// <summary>
    /// Handles resetting the experience, including auto-reset after completion and manual reset via button.
    /// Pauses auto-reset when headset is removed to prevent new players from missing content.
    /// </summary>
    public class RestartExperience : MonoBehaviour
    {
        [Header("Scene Settings")]
        [Tooltip("Name or index of the start scene")]
        [SerializeField] private string _startSceneName = GameConstants.SceneFiles.WordShooter;

        /// <summary>
        /// Name or index of the start scene to load when resetting the experience.
        /// </summary>
        public string startSceneName => _startSceneName;

        [Header("Auto-Reset Settings")]
        [Tooltip("Delay in seconds before auto-reset (5-10 seconds recommended)")]
        [SerializeField] private float autoResetDelay = 7.5f;

        [Tooltip("Enable automatic reset after delay")]
        [SerializeField] private bool enableAutoReset = true;

        [Tooltip("Pause auto-reset when headset is removed")]
        [SerializeField] private bool pauseOnHeadsetRemoval = true;

        private Coroutine autoResetCoroutine;
        private HeadsetPresenceDetector _headsetDetector;
        private float _remainingDelay;
        private bool _isPaused;

        private bool _wasPausedBeforeEndScene = false;
        private bool _restartButtonPressed = false;
        private bool _isRestarting = false; // Debounce flag to prevent double press

            [Header("Teardown Settings")]
            [Tooltip("Seconds to wait after tearing down persistent objects before loading the start scene. Increase if overlays/unregistering need more time.")]
            [SerializeField]
            private float postTeardownDelay = 1.0f;

        private void Start()
        {
            Debug.Log("[RestartExperience]: EndScene loaded");

            // CRITICAL: Wait a moment to ensure VAKANTIE video has completely finished
            // and scene transition is complete before showing restart button
            StartCoroutine(WaitForVideoToFinishThenShowRestart());

            // Disable auto-reset - wait for existing RESTART button click instead
            enableAutoReset = false;
            Debug.Log("[RestartExperience]: Auto-reset disabled - waiting for RESTART button click");
        }

        /// <summary>
        /// Waits for any playing videos to finish, then enables passthrough and shows restart button.
        /// </summary>
        private IEnumerator WaitForVideoToFinishThenShowRestart()
        {
            // CRITICAL: Wait for full 20 seconds to ensure VAKANTIE video has finished
            // The VAKANTIE video is limited to 20 seconds in DiplomaSceneHandler
            Debug.Log("[RestartExperience]: Waiting for VAKANTIE video to finish (20 seconds)...");
            yield return new WaitForSeconds(20f);

            // Wait a moment for scene transition to complete
            yield return new WaitForSeconds(0.5f);

            // CRITICAL: FULLY KILL any remaining videos
            VideoManager videoManager = FindFirstObjectByType<VideoManager>();
            if (videoManager != null)
            {
                Debug.Log("[RestartExperience]: FULLY KILLING any remaining videos...");
                videoManager.KillVideoCompletely();
            }

            // Wait a moment to ensure video is fully stopped and hidden
            yield return new WaitForSeconds(0.5f);

            // Now it's safe to enable passthrough and pause
            Debug.Log("[RestartExperience]: All videos finished and hidden, enabling passthrough and showing START button");
            EnablePassthroughAndPause();
        }

        /// <summary>
        /// Enables passthrough and pauses everything when EndScene loads.
        /// This creates a landing screen where the user sees the real world.
        /// </summary>
        private void EnablePassthroughAndPause()
        {
            // Pause everything (videos, audio, timers, etc.)
            if (GlobalPauseManager.Instance != null)
            {
                _wasPausedBeforeEndScene = GlobalPauseManager.Instance.IsPaused;
                if (!_wasPausedBeforeEndScene)
                {
                    GlobalPauseManager.Instance.Pause();
                    Debug.Log("[RestartExperience]: Paused everything for RESTART button landing screen");
                }
            }

            // Enable passthrough so user can see the real world
            VideoManager videoManager = FindFirstObjectByType<VideoManager>();
            if (videoManager != null)
            {
                videoManager.PassthroughCall(true); // Enable passthrough (see real world)
                Debug.Log("[RestartExperience]: Enabled passthrough for RESTART button landing screen");
            }
        }

        private void OnDestroy()
        {
            // Unsubscribe from events
            if (_headsetDetector != null)
            {
                _headsetDetector.OnHeadsetRemoved -= PauseAutoReset;
                _headsetDetector.OnHeadsetPutOn -= ResumeAutoReset;
            }
        }

        /// <summary>
        /// Starts the auto-reset coroutine that will reset after the delay.
        /// </summary>
        private void StartAutoReset()
        {
            if (autoResetCoroutine != null)
            {
                StopCoroutine(autoResetCoroutine);
            }
            _remainingDelay = autoResetDelay;
            _isPaused = false;
            autoResetCoroutine = StartCoroutine(AutoResetCoroutine());
        }

        /// <summary>
        /// Coroutine that waits for the delay then resets the experience.
        /// Pauses when headset is removed and resumes when put back on.
        /// </summary>
        private IEnumerator AutoResetCoroutine()
        {
            Debug.Log($"[RestartExperience]: Auto-reset will occur in {autoResetDelay} seconds (pauses when headset removed)");

            while (_remainingDelay > 0f)
            {
                // Only count down if not paused and headset is worn
                if (!_isPaused && (_headsetDetector == null || _headsetDetector.IsHeadsetWorn))
                {
                    float deltaTime = Time.unscaledDeltaTime;
                    _remainingDelay -= deltaTime;
                }

                yield return null; // Wait one frame
            }

            // Only reset if headset is currently worn (or no detector)
            if (_headsetDetector == null || _headsetDetector.IsHeadsetWorn)
            {
                Debug.Log("[RestartExperience]: Auto-reset triggered (headset is worn)");
                RestartByReload();
            }
            else
            {
                // Headset was removed during countdown - wait for it to be put back on
                Debug.Log("[RestartExperience]: Auto-reset delayed - waiting for headset to be put back on");
                yield return new WaitUntil(() => _headsetDetector == null || _headsetDetector.IsHeadsetWorn);
                Debug.Log("[RestartExperience]: Headset detected, triggering reset");
                RestartByReload();
            }
        }

        /// <summary>
        /// Pauses the auto-reset countdown when headset is removed.
        /// </summary>
        private void PauseAutoReset()
        {
            if (!pauseOnHeadsetRemoval) return;

            _isPaused = true;
            Debug.Log($"[RestartExperience]: Auto-reset paused (headset removed). {_remainingDelay:F1} seconds remaining.");
        }

        /// <summary>
        /// Resumes the auto-reset countdown when headset is put back on.
        /// </summary>
        private void ResumeAutoReset()
        {
            if (!pauseOnHeadsetRemoval) return;

            _isPaused = false;
            Debug.Log($"[RestartExperience]: Auto-reset resumed (headset put back on). {_remainingDelay:F1} seconds remaining.");
        }

        /// <summary>
        /// Called by UI Button to immediately reset the experience.
        /// Also stops auto-reset if it's still running.
        /// Disables passthrough and resumes everything before restarting.
        /// </summary>
        public void RestartByReload()
        {
            // Debounce and prevent double activation at the very start
            if (_isRestarting)
            {
                Debug.LogWarning("[RestartExperience]: Restart already in progress, ignoring duplicate button press");
                return;
            }
            _isRestarting = true;
            Debug.Log("[RestartExperience]: START button clicked (restart initiated)");

            // CRITICAL: Resume everything if we were the ones who paused it
            if (GlobalPauseManager.Instance != null && !_wasPausedBeforeEndScene)
            {
                GlobalPauseManager.Instance.Resume();
                Debug.Log("[RestartExperience]: Resumed everything before restart");
            }

            // Stop auto-reset if it's still running
            if (autoResetCoroutine != null)
            {
                StopCoroutine(autoResetCoroutine);
                autoResetCoroutine = null;
            }

            // Begin destruction and reload flow
            StartCoroutine(DestroyAndReload());
        }

        // private IEnumerator DestroyAndReload()
        // {
        //     DestroySpecificDontDestroyOnLoadObjects(new string[] {
        //         "CDNDownloadManager",
        //         "VideoManager",
        //         "VRUIManager",
        //         "SceneManagement",
        //         "PerformanceMonitor",
        //         "MoviePlayer",
        //         "VideoSkip",
        //         "ScoreManagerNM"
        //     });

        //     // Wait one frame for destruction to complete
        //     // Wait two frames to give OVROverlay a chance to unregister from render callbacks
        //     yield return null;
        //     yield return null;
        
        //     // Disable any remaining OVROverlay components app-wide to prevent render callback NREs
        //     OVROverlay[] allOverlays = Object.FindObjectsByType<OVROverlay>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        //     foreach (var ov in allOverlays)
        //     {
        //         if (ov == null) continue;
        //         try { ov.enabled = false; } catch { }
        //     }
        
        //     // Try to free up native resources before loading the new scene
        //     yield return Resources.UnloadUnusedAssets();
        //     System.GC.Collect();
        //     // Small extra delay to allow unregistering to complete
        //     // Use a realtime delay (configurable) so this works even if timeScale is changed.
        //     yield return new WaitForSecondsRealtime(postTeardownDelay);

        //     // Load the start scene only after we've attempted to destroy the persistent objects
        //     SceneManager.LoadSceneAsync(startSceneName);
        // }

private IEnumerator DestroyAndReload()
{
    // First disable ALL OVROverlays in the scene
    OVROverlay[] allOverlays = Object.FindObjectsByType<OVROverlay>(FindObjectsInactive.Include, FindObjectsSortMode.None);
    foreach (var ov in allOverlays)
    {
        if (ov == null) continue;
        try 
        { 
            ov.enabled = false;
            ov.overridePerLayerColorScaleAndOffset = false;
        } 
        catch { }
    }
    
    // Wait for one frame to let rendering system unregister
    yield return null;
    
    // Now destroy persistent objects
    DestroySpecificDontDestroyOnLoadObjects(new string[] {
        "CDNDownloadManager",
        "VideoManager",
        "VRUIManager",
        "SceneManagement",
        "PerformanceMonitor",
        "MoviePlayer",
        "VideoSkip",
        "ScoreManagerNM"
    });

    // Wait for one more frame for destruction to complete
    yield return null;
    
    // Disable any remaining OVROverlay components app-wide
    allOverlays = Object.FindObjectsByType<OVROverlay>(FindObjectsInactive.Include, FindObjectsSortMode.None);
    foreach (var ov in allOverlays)
    {
        if (ov == null) continue;
        try { ov.enabled = false; } catch { }
    }
    
    // Try to free up native resources before loading the new scene
    yield return Resources.UnloadUnusedAssets();
    System.GC.Collect();
    
    // Small extra delay to allow unregistering to complete
    yield return new WaitForSecondsRealtime(postTeardownDelay);

    // Load the start scene only after we've attempted to destroy the persistent objects
    SceneManager.LoadSceneAsync(startSceneName);
}

        // /// <summary>
        // /// Destroys only the specified DontDestroyOnLoad GameObjects by name.
        // /// Special-cases MoviePlayer to disable OVROverlay before destroy.
        // /// </summary>
        // /// <param name="targetNames">Names of GameObjects to destroy</param>
        // private void DestroySpecificDontDestroyOnLoadObjects(string[] targetNames)
        // {
        //     // Create a temporary object to access the DDOL scene
        //     var temp = new GameObject("_TempDDOLFinder");
        //     DontDestroyOnLoad(temp);
        //     var ddolScene = temp.scene;

        //     // Find all root objects in DDOL
        //     var allRootObjects = ddolScene.GetRootGameObjects();

        //     // First, deactivate known problematic persistent roots so they stop rendering/interacting
        //     string[] deactivateNames = new string[] { "OVROverlayCanvasManager", "MoviePlayer" };
        //     foreach (var root in allRootObjects)
        //     {
        //         if (root == temp) continue;
        //         foreach (var dname in deactivateNames)
        //         {
        //             if (root.name == dname)
        //             {
        //                 try { root.SetActive(false); } catch { }
        //                 break;
        //             }
        //         }
        //     }

        //     // Disable any OVROverlay instances found in the persistent scene to ensure they unregister
        //     foreach (var root in allRootObjects)
        //     {
        //         if (root == temp) continue;
        //         var overlays = root.GetComponentsInChildren<OVROverlay>(true);
        //         foreach (var ov in overlays)
        //         {
        //             if (ov == null) continue;
        //             try { ov.enabled = false; } catch { }
        //         }
        //     }
        //     foreach (var go in allRootObjects)
        //     {
        //         if (go == temp) continue;
        //         foreach (var name in targetNames)
        //         {
        //             if (go.name == name)
        //             {
        //                 // Special handling for MoviePlayer (disable OVROverlay before destroy)
        //                 if (name == "MoviePlayer")
        //                 {
        //                     var overlay = go.GetComponent<OVROverlay>();
        //                     if (overlay != null)
        //                     {
        //                         try { overlay.enabled = false; } catch { }
        //                     }
        //                 }

        //                 // If manager exposes a cleanup method (KillVideoCompletely etc.), call it safely
        //                 var videoMgr = go.GetComponent<VideoManager>();
        //                 if (videoMgr != null)
        //                 {
        //                     try { videoMgr.KillVideoCompletely(); } catch { }
        //                 }

        //                 Destroy(go);
        //                 break;
        //             }
        //         }
        //     }

        //     // Log remaining DDOL root GameObjects one frame later
        //     StartCoroutine(LogRemainingDDOLObjectsAfterFrame(temp));
        // }

private void DestroySpecificDontDestroyOnLoadObjects(string[] targetNames)
{
    // Create a temporary object to access the DDOL scene
    var temp = new GameObject("_TempDDOLFinder");
    DontDestroyOnLoad(temp);
    var ddolScene = temp.scene;

    // Find all root objects in DDOL
    var allRootObjects = ddolScene.GetRootGameObjects();

    // FIRST: Disable OVROverlay components BEFORE deactivating GameObjects
    foreach (var root in allRootObjects)
    {
        if (root == temp) continue;
        var overlays = root.GetComponentsInChildren<OVROverlay>(true);
        foreach (var ov in overlays)
        {
            if (ov == null) continue;
            try 
            { 
                // Disable first, then set overlay to none
                ov.enabled = false;
                ov.overridePerLayerColorScaleAndOffset = false;
                ov.isExternalSurface = false;
            } 
            catch (System.Exception e) 
            { 
                Debug.LogWarning($"[RestartExperience]: Error disabling OVROverlay on {ov.name}: {e.Message}");
            }
        }
    }

    // SECOND: Deactivate problematic persistent roots
    string[] deactivateNames = new string[] { "OVROverlayCanvasManager", "MoviePlayer" };
    foreach (var root in allRootObjects)
    {
        if (root == temp) continue;
        foreach (var dname in deactivateNames)
        {
            if (root.name == dname)
            {
                try { root.SetActive(false); } catch { }
                break;
            }
        }
    }

    // THIRD: Now destroy the target GameObjects
    foreach (var go in allRootObjects)
    {
        if (go == temp) continue;
        foreach (var name in targetNames)
        {
            if (go.name == name)
            {
                // Special handling for MoviePlayer
                if (name == "MoviePlayer")
                {
                    var overlay = go.GetComponent<OVROverlay>();
                    if (overlay != null)
                    {
                        try 
                        { 
                            overlay.enabled = false;
                            overlay.overridePerLayerColorScaleAndOffset = false;
                        } 
                        catch { }
                    }
                }

                // Call cleanup methods
                var videoMgr = go.GetComponent<VideoManager>();
                if (videoMgr != null)
                {
                    try { videoMgr.KillVideoCompletely(); } catch { }
                }

                Destroy(go);
                break;
            }
        }
    }

    // Log remaining DDOL root GameObjects one frame later
    StartCoroutine(LogRemainingDDOLObjectsAfterFrame(temp));
}

        private System.Collections.IEnumerator LogRemainingDDOLObjectsAfterFrame(GameObject temp)
        {
            yield return null; // Wait one frame for destruction
            var ddolScene = temp.scene;
            var remaining = ddolScene.GetRootGameObjects();
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            sb.Append("[RestartExperience] Remaining DontDestroyOnLoad root GameObjects: ");
            foreach (var go in remaining)
            {
                if (go != temp)
                {
                    sb.Append(go.name).Append(", ");
                }
            }
            Debug.Log(sb.ToString().TrimEnd(',', ' '));
            Destroy(temp);
        }
    }
}