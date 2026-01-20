using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;

[System.Serializable]
public class SceneFlag
{
    public string sceneName;
    public bool isActive;
}

public class SceneHandler : MonoBehaviour
{
    [Header("Managers")]
    [SerializeField] private VideoManager vidManager;
    [SerializeField] internal List<SceneFlag> sceneFlags = new List<SceneFlag>();

    [Header("Optional helpers")]
    [SerializeField] internal GameObject skipButton; // optional; used to find VideoSkipCall if needed
    [SerializeField] internal ButtonHandler _buttonHandler;

    // Runtime / config
    private ConfigData data;
    private HashSet<string> startedFlags = new HashSet<string>();
    private bool _isDownloadingFiles = false;

    // runtime signals (reset per flag start)
    internal bool videoFinished;
    internal bool audioFinished;
    internal bool buttonPressed;
    internal bool finishedScene;

    // utilities
    private CoroutineTracker _coroutineTracker;
    public static SceneHandler Instance { get; private set; }
    /// <summary>
    /// Simple global trigger other scripts can call to request advancing to the next scene.
    /// Call from other scripts with: SceneHandler.NextSceneRequested?.Invoke();
    /// </summary>
    public static Action NextSceneRequested;

    // Events
    public event Action<string> OnSceneFlagActivated;
    public event Action OnSceneReset;

    private VideoSkipCall vidSkipCall;

    private const float SCENE_LOAD_TIMEOUT = 30f;

    private void Awake() {
        // Singleton pattern
        if (Instance != null && Instance != this) {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);

        _coroutineTracker = GetComponent<CoroutineTracker>();
        if (_coroutineTracker == null) {
            _coroutineTracker = gameObject.AddComponent<CoroutineTracker>();
        }

        NextSceneRequested += HandleExternalNextSceneRequest;
    }

    private void HandleExternalNextSceneRequest() {
        Debug.Log("[SceneHandler] External NextSceneRequested invoked. Proceeding.");
        // Mirror RequestProceed behavior so existing logic continues to work
        RequestProceed();
    }

    private void Start() {
        if (skipButton != null) {
            vidSkipCall = skipButton.GetComponent<VideoSkipCall>();
            if (vidSkipCall == null) {
                Debug.LogWarning("[SceneHandler] skipButton provided but no VideoSkipCall component found.");
            }
        }
    }

    #region Public API - called by other managers / scenes

    /// <summary>
    /// Call this to tell the SceneHandler to process the training config (after JSON loaded).
    /// </summary>
    public void ProcessTrainingConfig(ConfigData configData)
    {
        Debug.Log("[SceneHandler] ProcessTrainingConfig called.");
        data = configData ?? throw new ArgumentNullException(nameof(configData));

        // subscribe to vidManager events if available
        if (vidManager != null)
        {
            vidManager.OnVideoFinished -= HandleVideoFinished;
            vidManager.OnAudioFinished -= HandleAudioFinished;

            vidManager.OnVideoFinished += HandleVideoFinished;
            vidManager.OnAudioFinished += HandleAudioFinished;
        }
        else
        {
            Debug.LogWarning("[SceneHandler] VideoManager is null. Video/audio events won't be handled.");
        }

        // build startedFlags with current sceneFlags to avoid duplicate starts across scene loads
        startedFlags.Clear();

        // start download coroutine (use CoroutineTracker if available)
        if (_coroutineTracker != null)
        {
            _coroutineTracker.StartTrackedCoroutine("downloadAllFiles", DownloadAllFilesAndStartScenes());
        }
        else
        {
            StartCoroutine(DownloadAllFilesAndStartScenes());
        }
    }

    /// <summary>
    /// External callers should call RequestProceed() (or RequestProceed(currentSceneName)) to tell SceneHandler to advance.
    /// This sets the per-flag finishedScene signal so the SceneLogic coroutine will move on.
    /// </summary>
    public void RequestProceed(string sceneName = null)
    {
        Debug.Log($"[SceneHandler] RequestProceed called for scene '{sceneName ?? "<any>"}'");
        finishedScene = true;
    }

    /// <summary>
    /// External callers can mark the button pressed state (helper).
    /// </summary>
    public void MarkButtonPressed() => buttonPressed = true;

    /// <summary>
    /// Full reset of this SceneHandler (used by restart logic).
    /// </summary>
    public void ResetAllManagers()
    {
        Debug.Log("[SceneHandler] ResetAllManagers()");
        // Stop coroutines, clear flags, tell subscribers to reset
        StopAllCoroutines();
        startedFlags.Clear();

        videoFinished = audioFinished = buttonPressed = finishedScene = false;

        foreach (var f in sceneFlags)
            f.isActive = false;

        if (sceneFlags.Count > 0)
            sceneFlags[0].isActive = true;

        OnSceneReset?.Invoke();

        // reinitialize video manager if present
        if (vidManager != null)
        {
            vidManager.KillVideoCompletely();
            vidManager.ManualSceneInit();
        }

        // re-run downloads + start scenes if we have config
        if (data != null)
            ProcessTrainingConfig(data);
    }

    #endregion

    #region Download & startup flow

    private IEnumerator DownloadAllFilesAndStartScenes()
    {
        _isDownloadingFiles = true;

        List<string> filesToDownload = new List<string>();

        // Videos
        if (data?.videos != null)
        {
            foreach (var v in data.videos)
                if (!string.IsNullOrEmpty(v.subFolder))
                    filesToDownload.Add(v.subFolder);
        }

        // Audios
        if (data?.audios != null)
        {
            foreach (var a in data.audios)
                if (!string.IsNullOrEmpty(a.subFolder))
                    filesToDownload.Add(a.subFolder);
        }

        // Music (groups + items)
        // if (data?.music != null)
        // {
        //     foreach (var mg in data.music)
        //     {
        //         if (mg?.numbers == null || string.IsNullOrEmpty(mg.folder))
        //             continue;

        //         foreach (var mi in mg.numbers)
        //         {
        //             if (!string.IsNullOrEmpty(mi.song) && !string.IsNullOrEmpty(mi.subFolder))
        //                 filesToDownload.Add(Path.Combine(mg.folder, mi.subFolder, mi.song));
        //             if (!string.IsNullOrEmpty(mi.beatmap) && !string.IsNullOrEmpty(mi.subFolder))
        //                 filesToDownload.Add(Path.Combine(mg.folder, mi.subFolder, mi.beatmap));
        //         }
        //     }
        // }

        if (filesToDownload.Count == 0)
        {
            Debug.Log("[SceneHandler] No files to download. Starting scenes immediately.");
            _isDownloadingFiles = false;
            StartScenes();
            yield break;
        }

        Debug.Log($"[SceneHandler] Downloading {filesToDownload.Count} files...");

        // show loading screen if available
        if (VRUIManager.Instance != null)
            VRUIManager.Instance.ShowLoadingScreen("Laden...", showProgress: true, progress: 0f);

        // ensure CDNDownloadManager exists
        CDNDownloadManager.EnsureInstanceExists();
        if (CDNDownloadManager.Instance == null)
        {
            Debug.LogWarning("[SceneHandler] CDNDownloadManager not available - skipping downloads.");
            if (VRUIManager.Instance != null)
                VRUIManager.Instance.HideLoadingScreen();
            _isDownloadingFiles = false;
            StartScenes();
            yield break;
        }

        // ensure base path exists
        string basePath = Application.persistentDataPath;
        if (!Directory.Exists(basePath))
        {
            try
            {
                Directory.CreateDirectory(basePath);
                Debug.Log($"[SceneHandler] Created persistent data path: {basePath}");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[SceneHandler] Failed to ensure persistentDataPath: {ex}");
            }
        }

        bool downloadsComplete = false;
        string downloadError = null;

        CDNDownloadManager.Instance.EnsureFilesExist(
            filesToDownload,
            onAllComplete: () =>
            {
                downloadsComplete = true;
                Debug.Log("[SceneHandler] All downloads complete.");
            },
            onError: (err) =>
            {
                downloadError = err;
                downloadsComplete = true;
                Debug.LogError($"[SceneHandler] Download error: {err}");
            }
        );

        yield return new WaitUntil(() => downloadsComplete);

        if (VRUIManager.Instance != null)
            VRUIManager.Instance.HideLoadingScreen(fadeOut: true);

        if (!string.IsNullOrEmpty(downloadError))
            Debug.LogWarning($"[SceneHandler] Downloads ended with errors: {downloadError}");

        startedFlags.Clear(); // allow flags to start fresh after downloads
        _isDownloadingFiles = false;

        StartScenes();
    }

    private void StartScenes()
    {
        // start any flags marked active
        foreach (var flag in sceneFlags)
        {
            if (flag.isActive)
                TryStartFlag(flag);
        }
    }

    private void TryStartFlag(SceneFlag flag)
    {
        if (flag == null || string.IsNullOrEmpty(flag.sceneName))
            return;

        if (startedFlags.Contains(flag.sceneName))
        {
            Debug.Log($"[SceneHandler] Flag '{flag.sceneName}' already started - skipping.");
            return;
        }

        startedFlags.Add(flag.sceneName);

        // start the scene logic coroutine (uses CoroutineTracker if available)
        string coroName = $"sceneLogic_{flag.sceneName}";
        IEnumerator coro = SceneLogic(flag);
        if (_coroutineTracker != null)
            _coroutineTracker.StartTrackedCoroutine(coroName, coro);
        else
            StartCoroutine(coro);
    }

    #endregion

    #region Scene logic & loading

    private IEnumerator SceneLogic(SceneFlag flag)
    {
        if (flag == null)
            yield break;

        // Reset per-flag runtime signals
        videoFinished = audioFinished = buttonPressed = finishedScene = false;

        // Load the target scene
        if (!string.IsNullOrEmpty(flag.sceneName))
        {
            yield return StartCoroutine(LoadSceneSafely(flag.sceneName));
        }

        // Fire activation event so other managers in the newly loaded scene can set up
        Debug.Log($"[SceneHandler] Activating SceneFlag -> {flag.sceneName}");
        OnSceneFlagActivated?.Invoke(flag.sceneName);

        // Wait until either:
        // - other script calls RequestProceed(), or
        // - a combination of signals you care about (videoFinished/audioFinished/buttonPressed)
        // Timeout is intentionally not enforced here; external logic decides when to proceed.
        yield return new WaitUntil(() => finishedScene || buttonPressed || videoFinished || audioFinished);

        Debug.Log($"[SceneHandler] Proceeding from flag '{flag.sceneName}'");

        // Clear finished signal for next use
        finishedScene = false;

        // Deactivate current flag and activate the next in the list (if any)
        AdvanceToNextFlag(flag.sceneName);
    }

    /// <summary>
    /// Advances to the next flag in sceneFlags after currentSceneName. If none left, logs completion.
    /// </summary>
    private void AdvanceToNextFlag(string currentSceneName)
    {
        int idx = sceneFlags.FindIndex(f => f.sceneName == currentSceneName);
        if (idx < 0)
        {
            Debug.LogWarning($"[SceneHandler] AdvanceToNextFlag: current flag '{currentSceneName}' not found in list.");
            return;
        }

        // deactivate current
        sceneFlags[idx].isActive = false;

        // activate next if exists
        int nextIdx = idx + 1;
        if (nextIdx < sceneFlags.Count)
        {
            sceneFlags[nextIdx].isActive = true;
            Debug.Log($"[SceneHandler] Activating next SceneFlag: {sceneFlags[nextIdx].sceneName}");
            TryStartFlag(sceneFlags[nextIdx]);
        }
        else
        {
            Debug.Log("[SceneHandler] No more scene flags to advance to. Experience complete.");
        }
    }

    /// <summary>
    /// Safely loads a scene with a timeout and fallback behavior.
    /// </summary>
    internal IEnumerator LoadSceneSafely(string sceneName, string fallbackSceneName = null)
    {
        if (string.IsNullOrEmpty(sceneName))
            yield break;

        if (string.IsNullOrEmpty(fallbackSceneName))
            fallbackSceneName = GameConstants.SceneFiles.WordShooter;

        // Check if scene is in build settings
        int sceneIndex = SceneUtility.GetBuildIndexByScenePath($"Assets/Scenes/{sceneName}.unity");
        if (sceneIndex < 0)
        {
            Debug.LogError($"[SceneHandler] Scene not found in build settings: {sceneName}. Trying fallback {fallbackSceneName}.");
            if (fallbackSceneName != sceneName)
            {
                yield return new WaitForSeconds(0.5f);
                yield return StartCoroutine(LoadSceneSafely(fallbackSceneName, null));
            }
            yield break;
        }

        Debug.Log($"[SceneHandler] Loading scene: {sceneName}");
        AsyncOperation asyncLoad = SceneManager.LoadSceneAsync(sceneName);
        if (asyncLoad == null)
        {
            Debug.LogError($"[SceneHandler] LoadSceneAsync returned null for {sceneName}");
            yield break;
        }

        asyncLoad.allowSceneActivation = false;

        float elapsed = 0f;
        while (asyncLoad.progress < 0.9f && elapsed < SCENE_LOAD_TIMEOUT)
        {
            elapsed += Time.deltaTime;
            yield return null;
        }

        if (asyncLoad.progress < 0.9f)
        {
            Debug.LogError($"[SceneHandler] Scene load timeout for {sceneName} after {elapsed:F1}s");
            if (fallbackSceneName != sceneName)
            {
                yield return new WaitForSeconds(0.5f);
                yield return StartCoroutine(LoadSceneSafely(fallbackSceneName, null));
            }
            yield break;
        }

        asyncLoad.allowSceneActivation = true;

        elapsed = 0f;
        while (!asyncLoad.isDone && elapsed < SCENE_LOAD_TIMEOUT)
        {
            elapsed += Time.deltaTime;
            yield return null;
        }

        if (!asyncLoad.isDone)
        {
            Debug.LogError($"[SceneHandler] Scene activation timeout for {sceneName}");
            if (fallbackSceneName != sceneName)
            {
                yield return new WaitForSeconds(0.5f);
                yield return StartCoroutine(LoadSceneSafely(fallbackSceneName, null));
            }
        }
        else
        {
            Debug.Log($"[SceneHandler] Scene loaded: {sceneName}");
        }
    }

    #endregion

    #region Event handlers & helpers

    private void HandleVideoFinished()
    {
        videoFinished = true;
        Debug.Log("[SceneHandler] HandleVideoFinished()");
    }

    private void HandleAudioFinished()
    {
        audioFinished = true;
        Debug.Log("[SceneHandler] HandleAudioFinished()");
    }

    internal string FindVideoUrl(string name)
    {
        if (data == null || data.videos == null)
        {
            Debug.LogError("[SceneHandler] FindVideoUrl: video configuration missing.");
            return null;
        }

        foreach (var video in data.videos)
        {
            if (video.name == name)
            {
                if (!string.IsNullOrEmpty(video.subFolder))
                    return video.subFolder;
                Debug.LogError($"[SceneHandler] Video '{name}' subFolder empty.");
                return null;
            }
        }

        Debug.LogError($"[SceneHandler] Video '{name}' not found in config.");
        return null;
    }

    internal string FindAudioUrl(string name)
    {
        if (data == null || data.audios == null)
        {
            Debug.LogError("[SceneHandler] FindAudioUrl: audio configuration missing.");
            return null;
        }

        foreach (var audio in data.audios)
        {
            if (audio.name == name)
            {
                if (!string.IsNullOrEmpty(audio.subFolder))
                    return audio.subFolder;
                Debug.LogError($"[SceneHandler] Audio '{name}' subFolder empty.");
                return null;
            }
        }

        Debug.LogError($"[SceneHandler] Audio '{name}' not found in config.");
        return null;
    }

    #endregion

    private void OnEnable()
    {
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private void OnDisable()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        Debug.Log($"[SceneHandler] OnSceneLoaded: {scene.name}");

        if (_isDownloadingFiles)
        {
            Debug.Log("[SceneHandler] Still downloading files; deferring flag starts.");
            return;
        }

        // try starting any active flags (useful if scene load happened independently)
        foreach (var flag in sceneFlags)
        {
            if (flag.isActive)
                TryStartFlag(flag);
        }
    }

    private void OnDestroy()
    {
        if (_coroutineTracker != null)
            _coroutineTracker.StopAllTrackedCoroutines();

        if (vidManager != null)
        {
            vidManager.OnVideoFinished -= HandleVideoFinished;
            vidManager.OnAudioFinished -= HandleAudioFinished;
        }

        if (Instance == this)
            Instance = null;
    }
}
