using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// Audio category for volume control (Music or SFX).
/// </summary>
public enum AudioCategory
{
    Music,
    SFX
}

/// <summary>
/// Manages audio clip caching and AudioSource pooling for performance optimization.
/// Prevents reloading audio clips and reduces memory usage from multiple AudioSource components.
/// </summary>
public class AudioManager : MonoBehaviour
{
    [Header("Pool Settings")]
    [Tooltip("Initial number of AudioSource components to pool")]
    [SerializeField] private int _initialPoolSize = 5;
    
    [Tooltip("Maximum number of AudioSource components in pool")]
    [SerializeField] private int _maxPoolSize = 20;
    
    [Tooltip("Maximum number of concurrent audio sources playing")]
    [SerializeField] private int _maxConcurrentSources = 10;
    
    [Header("Cache Settings")]
    [Tooltip("Maximum number of audio clips to cache (0 = unlimited)")]
    [SerializeField] private int _maxCachedClips = 50;
    
    [Tooltip("Clear cache when scene unloads")]
    [SerializeField] private bool _clearCacheOnSceneUnload = false;
    
    public static AudioManager Instance { get; private set; }
    
    // Audio clip cache (file path -> AudioClip)
    private Dictionary<string, AudioClip> _audioClipCache = new Dictionary<string, AudioClip>();
    
    // AudioSource pool
    private Queue<AudioSource> _audioSourcePool = new Queue<AudioSource>();
    private List<AudioSource> _activeAudioSources = new List<AudioSource>();
    private List<AudioSource> _allAudioSources = new List<AudioSource>();
    
    // Parent object for pooled AudioSources
    private Transform _poolParent;
    
    private CoroutineTracker _coroutineTracker;
    
    // Volume control system
    private Dictionary<string, float> _sceneVolumes = new Dictionary<string, float>();
    private Dictionary<string, float> _videoVolumes = new Dictionary<string, float>();
    private Dictionary<string, float> _audioVolumes = new Dictionary<string, float>(); // Audio-specific volumes from config
    private float _musicVolume = 1f;
    private float _sfxVolume = 1f;
    
    // Config data for volume lookup
    private ConfigData _configData;
    
    // Track active audio sources by category for volume updates
    private Dictionary<AudioSource, string> _sourceToScene = new Dictionary<AudioSource, string>();
    private Dictionary<AudioSource, string> _sourceToVideo = new Dictionary<AudioSource, string>();
    private List<AudioSource> _musicSources = new List<AudioSource>();
    private List<AudioSource> _sfxSources = new List<AudioSource>();
    
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
        
        // Initialize CoroutineTracker
        _coroutineTracker = GetComponent<CoroutineTracker>();
        if (_coroutineTracker == null)
        {
            _coroutineTracker = gameObject.AddComponent<CoroutineTracker>();
        }
        
        // Create pool parent
        GameObject poolParentObj = new GameObject("AudioSourcePool");
        poolParentObj.transform.SetParent(transform);
        _poolParent = poolParentObj.transform;
        
        // Initialize pool
        InitializePool();
    }
    
    /// <summary>
    /// Initializes the AudioSource pool.
    /// </summary>
    private void InitializePool()
    {
        for (int i = 0; i < _initialPoolSize; i++)
        {
            AudioSource source = CreateAudioSource();
            _audioSourcePool.Enqueue(source);
        }
        
        Debug.Log($"[AudioManager] Initialized pool with {_initialPoolSize} AudioSources");
    }
    
    /// <summary>
    /// Creates a new AudioSource component for the pool.
    /// </summary>
    private AudioSource CreateAudioSource()
    {
        GameObject sourceObj = new GameObject($"AudioSource_{_allAudioSources.Count}");
        sourceObj.transform.SetParent(_poolParent);
        sourceObj.SetActive(false);
        
        AudioSource source = sourceObj.AddComponent<AudioSource>();
        source.playOnAwake = false;
        source.loop = false;
        
        _allAudioSources.Add(source);
        return source;
    }
    
    /// <summary>
    /// Gets an AudioSource from the pool.
    /// </summary>
    private AudioSource GetAudioSource()
    {
        AudioSource source;
        
        if (_audioSourcePool.Count > 0)
        {
            source = _audioSourcePool.Dequeue();
        }
        else if (_allAudioSources.Count < _maxPoolSize)
        {
            source = CreateAudioSource();
        }
        else
        {
            // Pool is full, reuse oldest active source
            if (_activeAudioSources.Count > 0)
            {
                source = _activeAudioSources[0];
                _activeAudioSources.RemoveAt(0);
                source.Stop();
            }
            else
            {
                Debug.LogWarning("[AudioManager] Pool exhausted, creating temporary AudioSource");
                source = CreateAudioSource();
            }
        }
        
        source.gameObject.SetActive(true);
        _activeAudioSources.Add(source);
        
        // Limit concurrent sources
        if (_activeAudioSources.Count > _maxConcurrentSources)
        {
            AudioSource oldest = _activeAudioSources[0];
            _activeAudioSources.RemoveAt(0);
            ReturnAudioSource(oldest);
        }
        
        return source;
    }
    
    /// <summary>
    /// Returns an AudioSource to the pool.
    /// </summary>
    private void ReturnAudioSource(AudioSource source)
    {
        if (source == null) return;
        
        // Clean up tracking before returning to pool
        CleanupAudioSource(source);
        
        source.Stop();
        source.clip = null;
        source.gameObject.SetActive(false);
        
        _activeAudioSources.Remove(source);
        
        if (_audioSourcePool.Count < _maxPoolSize)
        {
            _audioSourcePool.Enqueue(source);
        }
    }
    
    /// <summary>
    /// Loads an audio clip from file path, using cache if available.
    /// </summary>
    /// <param name="filePath">Path to audio file</param>
    /// <returns>Coroutine that loads the audio clip</returns>
    public IEnumerator LoadAudioClip(string filePath)
    {
        if (string.IsNullOrEmpty(filePath))
        {
            Debug.LogError("[AudioManager] Cannot load audio: filePath is null or empty");
            yield break;
        }
        
        // Normalize path for cache key
        string cacheKey = Path.GetFullPath(filePath).Replace('\\', '/');
        
        // Check cache first
        if (_audioClipCache.ContainsKey(cacheKey))
        {
            Debug.Log($"[AudioManager] Using cached audio clip: {Path.GetFileName(filePath)}");
            yield break;
        }
        
        // Check cache size limit
        if (_maxCachedClips > 0 && _audioClipCache.Count >= _maxCachedClips)
        {
            // Remove oldest entry (simple FIFO)
            var firstKey = new List<string>(_audioClipCache.Keys)[0];
            AudioClip oldClip = _audioClipCache[firstKey];
            _audioClipCache.Remove(firstKey);
            Destroy(oldClip);
            Debug.Log($"[AudioManager] Cache full, removed: {Path.GetFileName(firstKey)}");
        }
        
        // Load audio clip
        string uri = filePath;
        if (!uri.StartsWith("file://"))
        {
            if (Application.platform == RuntimePlatform.WindowsEditor || 
                Application.platform == RuntimePlatform.WindowsPlayer)
            {
                uri = "file:///" + uri;
            }
            else
            {
                uri = "file://" + uri;
            }
        }
        
        using (UnityWebRequest www = UnityWebRequestMultimedia.GetAudioClip(uri, AudioType.MPEG))
        {
            yield return www.SendWebRequest();
            
            if (www.result == UnityWebRequest.Result.Success)
            {
                AudioClip clip = DownloadHandlerAudioClip.GetContent(www);
                if (clip != null)
                {
                    _audioClipCache[cacheKey] = clip;
                    Debug.Log($"[AudioManager] Cached audio clip: {Path.GetFileName(filePath)} ({clip.length:F1}s)");
                }
                else
                {
                    Debug.LogError($"[AudioManager] Failed to create AudioClip from: {filePath}");
                }
            }
            else
            {
                Debug.LogError($"[AudioManager] Error loading audio: {www.error} (path: {filePath})");
            }
        }
    }
    
    /// <summary>
    /// Gets a cached audio clip, or null if not cached.
    /// </summary>
    public AudioClip GetCachedClip(string filePath)
    {
        if (string.IsNullOrEmpty(filePath)) return null;
        
        string cacheKey = Path.GetFullPath(filePath).Replace('\\', '/');
        _audioClipCache.TryGetValue(cacheKey, out AudioClip clip);
        return clip;
    }
    
    /// <summary>
    /// Plays an audio clip using a pooled AudioSource.
    /// </summary>
    /// <param name="filePath">Path to audio file</param>
    /// <param name="volume">Volume (0-1)</param>
    /// <param name="loop">Whether to loop</param>
    /// <param name="category">Audio category (Music or SFX) for volume control</param>
    /// <param name="sceneName">Optional scene name for scene-specific volume</param>
    /// <param name="videoName">Optional video name for video-specific volume</param>
    /// <returns>Coroutine that plays the audio</returns>
    public IEnumerator PlayAudio(string filePath, float volume = 1f, bool loop = false, 
        AudioCategory category = AudioCategory.SFX, string sceneName = null, string videoName = null)
    {
        // Load clip if not cached
        yield return StartCoroutine(LoadAudioClip(filePath));
        
        AudioClip clip = GetCachedClip(filePath);
        if (clip == null)
        {
            Debug.LogError($"[AudioManager] Audio clip not found: {filePath}");
            yield break;
        }
        
        // Get AudioSource from pool
        AudioSource source = GetAudioSource();
        source.clip = clip;
        source.loop = loop;
        
        // Apply volume multipliers
        float finalVolume = volume;
        if (category == AudioCategory.Music)
        {
            finalVolume *= _musicVolume;
            _musicSources.Add(source);
        }
        else
        {
            finalVolume *= _sfxVolume;
            _sfxSources.Add(source);
        }
        
        // Apply scene volume if specified
        if (!string.IsNullOrEmpty(sceneName) && _sceneVolumes.ContainsKey(sceneName))
        {
            finalVolume *= _sceneVolumes[sceneName];
            _sourceToScene[source] = sceneName;
        }
        
        // Apply video volume if specified
        if (!string.IsNullOrEmpty(videoName) && _videoVolumes.ContainsKey(videoName))
        {
            finalVolume *= _videoVolumes[videoName];
            _sourceToVideo[source] = videoName;
        }
        
        source.volume = Mathf.Clamp01(finalVolume);
        source.Play();
        
        // Wait for audio to finish (if not looping)
        if (!loop)
        {
            yield return new WaitWhile(() => source.isPlaying);
            CleanupAudioSource(source);
            ReturnAudioSource(source);
        }
        else
        {
            // For looping audio, return to pool when stopped manually
            // Store reference for manual cleanup
        }
    }
    
    /// <summary>
    /// Cleans up audio source tracking when audio finishes.
    /// </summary>
    private void CleanupAudioSource(AudioSource source)
    {
        if (source == null) return;
        
        _musicSources.Remove(source);
        _sfxSources.Remove(source);
        _sourceToScene.Remove(source);
        _sourceToVideo.Remove(source);
    }
    
    /// <summary>
    /// Stops all playing audio sources and returns them to pool.
    /// </summary>
    public void StopAllAudio()
    {
        for (int i = _activeAudioSources.Count - 1; i >= 0; i--)
        {
            AudioSource source = _activeAudioSources[i];
            if (source != null)
            {
                CleanupAudioSource(source);
                source.Stop();
                source.clip = null;
                source.gameObject.SetActive(false);
                _activeAudioSources.RemoveAt(i);
                
                if (_audioSourcePool.Count < _maxPoolSize)
                {
                    _audioSourcePool.Enqueue(source);
                }
            }
        }
    }
    
    /// <summary>
    /// Clears the audio clip cache.
    /// </summary>
    public void ClearCache()
    {
        foreach (var clip in _audioClipCache.Values)
        {
            if (clip != null)
            {
                Destroy(clip);
            }
        }
        _audioClipCache.Clear();
        Debug.Log("[AudioManager] Audio clip cache cleared");
    }
    
    /// <summary>
    /// Gets the number of cached audio clips.
    /// </summary>
    public int CachedClipCount => _audioClipCache.Count;
    
    /// <summary>
    /// Gets the number of active audio sources.
    /// </summary>
    public int ActiveSourceCount => _activeAudioSources.Count;
    
    /// <summary>
    /// Gets the pool size.
    /// </summary>
    public int PoolSize => _audioSourcePool.Count;
    
    #region Volume Control
    
    /// <summary>
    /// Sets the volume multiplier for a specific scene.
    /// </summary>
    /// <param name="sceneName">Scene name (e.g., "1-Word", "2-Quiz")</param>
    /// <param name="volume">Volume (0-1)</param>
    public void SetSceneVolume(string sceneName, float volume)
    {
        volume = Mathf.Clamp01(volume);
        _sceneVolumes[sceneName] = volume;
        
        // Update all active audio sources for this scene
        foreach (var kvp in _sourceToScene)
        {
            if (kvp.Value == sceneName && kvp.Key != null)
            {
                kvp.Key.volume = volume;
            }
        }
        
        Debug.Log($"[AudioManager] Set scene volume: {sceneName} = {volume}");
    }
    
    /// <summary>
    /// Sets the volume multiplier for a specific video.
    /// </summary>
    /// <param name="videoName">Video name (e.g., "SchaatsBaan", "Strand")</param>
    /// <param name="volume">Volume (0-1)</param>
    public void SetVideoVolume(string videoName, float volume)
    {
        volume = Mathf.Clamp01(volume);
        _videoVolumes[videoName] = volume;
        
        // Update all active audio sources for this video
        foreach (var kvp in _sourceToVideo)
        {
            if (kvp.Value == videoName && kvp.Key != null)
            {
                kvp.Key.volume = volume;
            }
        }
        
        Debug.Log($"[AudioManager] Set video volume: {videoName} = {volume}");
    }
    
    /// <summary>
    /// Sets the global music volume multiplier.
    /// </summary>
    /// <param name="volume">Volume (0-1)</param>
    public void SetMusicVolume(float volume)
    {
        volume = Mathf.Clamp01(volume);
        _musicVolume = volume;
        
        // Update all active music sources
        foreach (var source in _musicSources)
        {
            if (source != null)
            {
                source.volume = volume;
            }
        }
        
        Debug.Log($"[AudioManager] Set music volume: {volume}");
    }
    
    /// <summary>
    /// Sets the global SFX volume multiplier.
    /// </summary>
    /// <param name="volume">Volume (0-1)</param>
    public void SetSFXVolume(float volume)
    {
        volume = Mathf.Clamp01(volume);
        _sfxVolume = volume;
        
        // Update all active SFX sources
        foreach (var source in _sfxSources)
        {
            if (source != null)
            {
                source.volume = volume;
            }
        }
        
        Debug.Log($"[AudioManager] Set SFX volume: {volume}");
    }
    
    /// <summary>
    /// Gets the volume multiplier for a specific scene.
    /// </summary>
    public float GetSceneVolume(string sceneName)
    {
        return _sceneVolumes.TryGetValue(sceneName, out float volume) ? volume : 1f;
    }
    
    /// <summary>
    /// Gets the volume multiplier for a specific video.
    /// </summary>
    public float GetVideoVolume(string videoName)
    {
        return _videoVolumes.TryGetValue(videoName, out float volume) ? volume : 1f;
    }
    
    /// <summary>
    /// Gets the global music volume multiplier.
    /// </summary>
    public float GetMusicVolume() => _musicVolume;
    
    /// <summary>
    /// Gets the global SFX volume multiplier.
    /// </summary>
    public float GetSFXVolume() => _sfxVolume;
    
    /// <summary>
    /// Processes training configuration data and initializes audio volumes from config.
    /// </summary>
    public void ProcessTrainingConfig(ConfigData configData)
    {
        _configData = configData;
        InitializeAudioVolumes();
    }
    
    /// <summary>
    /// Initializes audio volumes from config JSON (reads volume from each audio entry).
    /// </summary>
    private void InitializeAudioVolumes()
    {
        if (_configData == null || _configData.audios == null)
        {
            Debug.LogWarning("[AudioManager]: ConfigData or audios array is null, cannot initialize volumes");
            return;
        }
        
        // Read volume from each audio in config
        foreach (var audio in _configData.audios)
        {
            if (!string.IsNullOrEmpty(audio.name))
            {
                float volume = audio.volume; // Defaults to 1.0f if not specified in JSON
                _audioVolumes[audio.name] = volume;
                Debug.Log($"[AudioManager]: ✓ Loaded volume {volume:F2} ({volume * 100:F0}%) for audio: {audio.name}");
            }
        }
        
        // Debug: Log all loaded volumes
        Debug.Log($"[AudioManager]: Total audio volumes loaded: {_audioVolumes.Count}");
        foreach (var kvp in _audioVolumes)
        {
            Debug.Log($"[AudioManager]:   - {kvp.Key}: {kvp.Value:F2} ({kvp.Value * 100:F0}%)");
        }
    }
    
    /// <summary>
    /// Gets the audio volume for a specific audio file by name.
    /// </summary>
    /// <param name="audioName">Audio name (e.g., "laser-gun")</param>
    /// <returns>Volume (0-1), defaults to 1.0 if not found</returns>
    public float GetAudioVolume(string audioName)
    {
        if (string.IsNullOrEmpty(audioName))
        {
            return 1.0f;
        }
        
        // First check if volume was set from config
        if (_audioVolumes.TryGetValue(audioName, out float volume))
        {
            return volume;
        }
        
        // Fallback: Try to get from config data if available
        if (_configData != null && _configData.audios != null)
        {
            foreach (var audio in _configData.audios)
            {
                if (audio.name == audioName)
                {
                    float vol = audio.volume; // Defaults to 1.0f if not specified
                    // Cache it for next time
                    _audioVolumes[audioName] = vol;
                    return vol;
                }
            }
        }
        
        // Final fallback: default volume
        return 1.0f;
    }
    
    #endregion
    
    private void OnDestroy()
    {
        // Cleanup
        StopAllAudio();
        ClearCache();
        
        if (_coroutineTracker != null)
        {
            _coroutineTracker.StopAllTrackedCoroutines();
        }
    }
    
    private void OnApplicationQuit()
    {
        ClearCache();
    }
}

