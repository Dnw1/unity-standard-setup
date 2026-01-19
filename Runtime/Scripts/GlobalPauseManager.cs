using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Video;
using UnityEngine.Audio;

/// <summary>
/// Global pause manager that pauses the entire experience when headset is removed.
/// Pauses videos, audio, timers, and all time-based systems across all scenes.
/// Automatically pauses new components when scenes load while paused.
/// </summary>
public class GlobalPauseManager : MonoBehaviour
{
    public static GlobalPauseManager Instance { get; private set; }

    [Header("Pause Settings")]
    [Tooltip("Pause everything when headset is removed")]
    [SerializeField] private bool pauseOnHeadsetRemoval = true;
    
    [Tooltip("Pause Time.timeScale (affects timers, animations, physics)")]
    [SerializeField] private bool pauseTimeScale = true;

    private HeadsetPresenceDetector _headsetDetector;
    private bool _isPaused = false;
    private float _savedTimeScale = 1f;

    // Track paused components
    private List<VideoPlayer> _pausedVideoPlayers = new List<VideoPlayer>();
    private List<AudioSource> _pausedAudioSources = new List<AudioSource>();
    private List<VRVideoPlayer> _pausedVRVideoPlayers = new List<VRVideoPlayer>();

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
    }

    private void Start()
    {
        // Find or create headset presence detector
        _headsetDetector = FindFirstObjectByType<HeadsetPresenceDetector>();
        if (_headsetDetector == null && pauseOnHeadsetRemoval)
        {
            GameObject detectorObj = new GameObject("HeadsetPresenceDetector");
            _headsetDetector = detectorObj.AddComponent<HeadsetPresenceDetector>();
            DontDestroyOnLoad(detectorObj);
            Debug.Log("[GlobalPauseManager]: Created HeadsetPresenceDetector");
        }

        // Subscribe to headset events
        if (_headsetDetector != null && pauseOnHeadsetRemoval)
        {
            _headsetDetector.OnHeadsetRemoved += PauseEverything;
            _headsetDetector.OnHeadsetPutOn += ResumeEverything;
            Debug.Log("[GlobalPauseManager]: Subscribed to headset presence events");
        }

        // Subscribe to scene load events to pause new components in new scenes
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private void OnDestroy()
    {
        // Unsubscribe from events
        if (_headsetDetector != null)
        {
            _headsetDetector.OnHeadsetRemoved -= PauseEverything;
            _headsetDetector.OnHeadsetPutOn -= ResumeEverything;
        }

        SceneManager.sceneLoaded -= OnSceneLoaded;

        // Restore time scale if we're destroyed while paused
        if (_isPaused && pauseTimeScale)
        {
            Time.timeScale = _savedTimeScale;
        }
    }

    /// <summary>
    /// Called when a new scene is loaded. If we're paused, pause any new components.
    /// </summary>
    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (_isPaused)
        {
            // Wait a frame for components to initialize, then pause them
            StartCoroutine(PauseNewSceneComponents());
        }
    }

    /// <summary>
    /// Waits a frame then pauses all components in the newly loaded scene.
    /// </summary>
    private IEnumerator PauseNewSceneComponents()
    {
        yield return null; // Wait one frame for components to initialize

        if (_isPaused)
        {
            Debug.Log("[GlobalPauseManager]: New scene loaded while paused - pausing new components");
            
            // Pause any new VideoPlayers
            VideoPlayer[] videoPlayers = FindObjectsByType<VideoPlayer>(FindObjectsSortMode.None);
            foreach (VideoPlayer vp in videoPlayers)
            {
                if (vp != null && vp.isPlaying && !_pausedVideoPlayers.Contains(vp))
                {
                    vp.Pause();
                    _pausedVideoPlayers.Add(vp);
                    Debug.Log($"[GlobalPauseManager]: Paused new VideoPlayer: {vp.gameObject.name}");
                }
            }

            // Pause any new AudioSources
            AudioSource[] audioSources = FindObjectsByType<AudioSource>(FindObjectsSortMode.None);
            foreach (AudioSource audio in audioSources)
            {
                if (audio != null && audio.isPlaying && !_pausedAudioSources.Contains(audio))
                {
                    audio.Pause();
                    _pausedAudioSources.Add(audio);
                    Debug.Log($"[GlobalPauseManager]: Paused new AudioSource: {audio.gameObject.name}");
                }
            }

            // Pause any new VRVideoPlayers
            VRVideoPlayer[] vrVideoPlayers = FindObjectsByType<VRVideoPlayer>(FindObjectsSortMode.None);
            foreach (VRVideoPlayer vrvp in vrVideoPlayers)
            {
                if (vrvp != null && vrvp.IsPlaying && !_pausedVRVideoPlayers.Contains(vrvp))
                {
                    vrvp.Pause();
                    _pausedVRVideoPlayers.Add(vrvp);
                    Debug.Log($"[GlobalPauseManager]: Paused new VRVideoPlayer: {vrvp.gameObject.name}");
                }
            }
        }
    }

    /// <summary>
    /// Pauses everything: videos, audio, timers, and time-based systems.
    /// </summary>
    public void PauseEverything()
    {
        if (_isPaused) return;

        _isPaused = true;
        Debug.Log("[GlobalPauseManager]: Pausing entire experience");

        // Save current time scale
        if (pauseTimeScale)
        {
            _savedTimeScale = Time.timeScale;
            Time.timeScale = 0f;
            Debug.Log("[GlobalPauseManager]: Time.timeScale set to 0");
        }

        // Pause all VideoPlayers
        PauseAllVideoPlayers();

        // Pause all AudioSources
        PauseAllAudioSources();

        // Pause all VRVideoPlayers
        PauseAllVRVideoPlayers();
    }

    /// <summary>
    /// Resumes everything: videos, audio, timers, and time-based systems.
    /// </summary>
    public void ResumeEverything()
    {
        if (!_isPaused) return;

        _isPaused = false;
        Debug.Log("[GlobalPauseManager]: Resuming entire experience");

        // Restore time scale
        if (pauseTimeScale)
        {
            Time.timeScale = _savedTimeScale;
            Debug.Log($"[GlobalPauseManager]: Time.timeScale restored to {_savedTimeScale}");
        }

        // Resume all VideoPlayers
        ResumeAllVideoPlayers();

        // Resume all AudioSources
        ResumeAllAudioSources();

        // Resume all VRVideoPlayers
        ResumeAllVRVideoPlayers();
    }

    /// <summary>
    /// Finds and pauses all VideoPlayer components in the scene.
    /// </summary>
    private void PauseAllVideoPlayers()
    {
        _pausedVideoPlayers.Clear();
        VideoPlayer[] videoPlayers = FindObjectsByType<VideoPlayer>(FindObjectsSortMode.None);
        
        foreach (VideoPlayer vp in videoPlayers)
        {
            if (vp != null && vp.isPlaying)
            {
                vp.Pause();
                _pausedVideoPlayers.Add(vp);
                Debug.Log($"[GlobalPauseManager]: Paused VideoPlayer: {vp.gameObject.name}");
            }
        }
    }

    /// <summary>
    /// Resumes all previously paused VideoPlayer components.
    /// </summary>
    private void ResumeAllVideoPlayers()
    {
        foreach (VideoPlayer vp in _pausedVideoPlayers)
        {
            if (vp != null)
            {
                vp.Play();
                Debug.Log($"[GlobalPauseManager]: Resumed VideoPlayer: {vp.gameObject.name}");
            }
        }
        _pausedVideoPlayers.Clear();
    }

    /// <summary>
    /// Finds and pauses all AudioSource components in the scene.
    /// </summary>
    private void PauseAllAudioSources()
    {
        _pausedAudioSources.Clear();
        AudioSource[] audioSources = FindObjectsByType<AudioSource>(FindObjectsSortMode.None);
        
        foreach (AudioSource audio in audioSources)
        {
            if (audio != null && audio.isPlaying)
            {
                audio.Pause();
                _pausedAudioSources.Add(audio);
                Debug.Log($"[GlobalPauseManager]: Paused AudioSource: {audio.gameObject.name}");
            }
        }
    }

    /// <summary>
    /// Resumes all previously paused AudioSource components.
    /// </summary>
    private void ResumeAllAudioSources()
    {
        foreach (AudioSource audio in _pausedAudioSources)
        {
            if (audio != null)
            {
                audio.UnPause();
                Debug.Log($"[GlobalPauseManager]: Resumed AudioSource: {audio.gameObject.name}");
            }
        }
        _pausedAudioSources.Clear();
    }

    /// <summary>
    /// Finds and pauses all VRVideoPlayer components in the scene.
    /// </summary>
    private void PauseAllVRVideoPlayers()
    {
        _pausedVRVideoPlayers.Clear();
        VRVideoPlayer[] vrVideoPlayers = FindObjectsByType<VRVideoPlayer>(FindObjectsSortMode.None);
        
        foreach (VRVideoPlayer vrvp in vrVideoPlayers)
        {
            if (vrvp != null && vrvp.IsPlaying)
            {
                vrvp.Pause();
                _pausedVRVideoPlayers.Add(vrvp);
                Debug.Log($"[GlobalPauseManager]: Paused VRVideoPlayer: {vrvp.gameObject.name}");
            }
        }
    }

    /// <summary>
    /// Resumes all previously paused VRVideoPlayer components.
    /// </summary>
    private void ResumeAllVRVideoPlayers()
    {
        foreach (VRVideoPlayer vrvp in _pausedVRVideoPlayers)
        {
            if (vrvp != null)
            {
                vrvp.Play();
                Debug.Log($"[GlobalPauseManager]: Resumed VRVideoPlayer: {vrvp.gameObject.name}");
            }
        }
        _pausedVRVideoPlayers.Clear();
    }

    /// <summary>
    /// Returns true if the experience is currently paused.
    /// </summary>
    public bool IsPaused => _isPaused;

    /// <summary>
    /// Manually pause (for testing or other purposes).
    /// </summary>
    public void Pause()
    {
        PauseEverything();
    }

    /// <summary>
    /// Manually resume (for testing or other purposes).
    /// </summary>
    public void Resume()
    {
        ResumeEverything();
    }
}

