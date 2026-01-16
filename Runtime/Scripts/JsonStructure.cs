using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.Video;

[Serializable]
public class JSONResponse {
    public string id;
    public ConfigData config;
    public string action;
}

[Serializable]
public class ConfigData {
    public WordList wordList;
    public PointAward pointAward;
    public Videos[] videos;
    public Audios[] audios;
    public Lighting[] lighting;
    public PostProcessing[] postProcessing;
    public Music[] music;
}

[Serializable]
public class Videos  {
    public string name;
    public string subFolder;
    public float volume = 1.0f; // Optional: Audio volume for this video (0-1). Defaults to 1.0 (100%)
}

[Serializable]
public class Audios  {
    public string name;
    public string subFolder;
    public float volume = 1.0f; // Optional: Volume for this audio (0-1). Defaults to 1.0 (100%)
}


[Serializable]
public class Music {
    public string folder;
    public DestinationOrder destinationOrder;
    public Numbers[] numbers;
}

[Serializable]
public class DestinationOrder {
    public List<string> order;
    public int timer = 30;
}

[Serializable]
public class Numbers {
    public string subFolder;
    public string song;
    public int bpm;
    public string beatmap;
}


[Serializable]
public class Note {
    public float time;        // seconds (converted from beats)
    public int lineIndex;     // x
    public int lineLayer;     // y
    public int colorIndex;    // c (0-3)
    public int cutDirection;  // d
}

[Serializable]
public class Lighting {
    public string color = "#FFFFFF"; // html color string (eg #FF8A00)
    public float intensity = 1f;
}

[Serializable]
public class WordList {
    public List<string> words;
}

[Serializable]
public class PointAward {
    public float correctWord;
    public float maxTimeAward;
    public float minTimeAward;
}

// Raw classes matching Beat Saber v4.1.0 keys
[Serializable]
public class NoteRaw {
    public float b; // Beat time
    public int r;   // Rotation lane (unused here)
    public int i;   // Metadata index
}

[Serializable]
public class NoteDataRaw {
    public int x; // Line index (0-3)
    public int y; // Line layer (0-2)
    public int c; // Color (0-3)
    public int d; // Cut direction
    public int a; // Angle offset (unused here)
}

[Serializable]
public class BeatSaberMapRaw {
    public List<NoteRaw> colorNotes;
    public List<NoteDataRaw> colorNotesData;
}

[Serializable]
public class PostProcessing
{
    public bool overrideBloom = false;
    public float bloomIntensity = 1f;
}

public class JsonStructure : MonoBehaviour {
    [Tooltip("Path to .beatmap.dat file")] 
    public string mapFilePath;

    [Tooltip("Song BPM (used to convert beat to seconds)")]
    public float bpm = 123f;

    // private List<(int, int)> combos = new List<(int, int)> {  (1,3), (1,2), (0,3), (0,2), (1,3), (1,2), (0,3) };

    //= new List<(int, int)> { (0,2), (0,3), (1,2), (1,3) };

    [HideInInspector]
    public List<Note> notes;
    public ConfigData data;

    private TMP_Text songName;

    public static JsonStructure Instance { get; private set; }   // singleton instance

    private int pendingSongIndex = -1;

    public event Action OnMusicReady;

    [SerializeField] public AudioSource audioSource;

    private int startIndex;
    private string localPath;
    [SerializeField] private SceneHandler sceneH;
    [SerializeField] private VideoManager videoM;

    /// <summary>
    /// Loads and parses JSON config from a string, then distributes it to all managers.
    /// Includes comprehensive error handling with fallback to default config.
    /// </summary>
    private void LoadConfigFromJsonString(string jsonString, string source) {
        // Check for null or empty JSON string
        if (string.IsNullOrEmpty(jsonString)) {
            Debug.LogError($"[JsonStructure] JSON string is null or empty from {source}");
            Debug.LogError(
                "Configuration file is empty. Using default settings." +
                $"Empty JSON from {source}"
            );
            LoadDefaultConfig();
            return;
        }
        
        try {
            Debug.Log($"[JsonStructure] Loading config from {source}");

            // Validate JSON structure before parsing
            JObject root;
            try {
                root = JObject.Parse(jsonString);
            } catch (JsonException jsonEx) {
                Debug.LogError($"[JsonStructure] Invalid JSON structure from {source}: {jsonEx.Message}");
                Debug.LogError("localConfig.json" + jsonEx.Message);
                LoadDefaultConfig();
                return;
            }
            
            // Check required fields (warn if missing, but continue)
            if (root["videos"] == null) {
                Debug.LogWarning("[JsonStructure] 'videos' field missing in JSON - will use empty array");
            }
            if (root["audios"] == null) {
                Debug.LogWarning("[JsonStructure] 'audios' field missing in JSON - will use empty array");
            }
            if (root["wordList"] == null) {
                Debug.LogWarning("[JsonStructure] 'wordList' field missing in JSON - will use default words");
            }

            // Settings for safe deserialization with error handling
            var settings = new JsonSerializerSettings {
                MissingMemberHandling = MissingMemberHandling.Ignore,
                NullValueHandling = NullValueHandling.Ignore,
                ReferenceLoopHandling = ReferenceLoopHandling.Ignore,
                TypeNameHandling = TypeNameHandling.None,
                Error = (sender, args) => {
                    Debug.LogWarning($"[JsonStructure] JSON parsing warning: {args.ErrorContext.Error.Message}");
                    args.ErrorContext.Handled = true; // Continue parsing despite errors
                }
            };

            // Deserialize into ConfigData
            ConfigData cfg = root.ToObject<ConfigData>(JsonSerializer.Create(settings));
            if (cfg == null) {
                throw new Exception("Deserialized config is null after parsing");
            }
            
            // Validate essential data exists
            if (cfg.wordList == null || cfg.wordList.words == null || cfg.wordList.words.Count == 0) {
                Debug.LogWarning("[JsonStructure] Word list is empty or null - will use default words");
                if (cfg.wordList == null) {
                    cfg.wordList = new WordList { words = new List<string> { "GROOTS" } };
                } else if (cfg.wordList.words == null || cfg.wordList.words.Count == 0) {
                    cfg.wordList.words = new List<string> { "GROOTS" };
                }
            }

            GetScripts();

            // Hand off to systems on main thread
            ProcessTrainingConfig(cfg);
            videoM?.ProcessTrainingConfig(cfg);
            sceneH?.ProcessTrainingConfig(cfg);
            
            // Pass config to AudioManager if it exists (singleton, may not exist yet)
            if (AudioManager.Instance != null)
            {
                AudioManager.Instance.ProcessTrainingConfig(cfg);
            }

        } catch (JsonException jsonEx) {
            Debug.LogError($"[JsonStructure] JSON parsing error from {source}: {jsonEx.Message}");
            Debug.LogError(
                $"Configuration file is invalid. Using default settings.\nError: {jsonEx.Message}" +
                $"JSON parsing error from {source}: {jsonEx.Message}"
            );
            LoadDefaultConfig();
        } catch (Exception ex) {
            Debug.LogError($"[JsonStructure] Unexpected error loading config from {source}: {ex}");
            Debug.LogError(
                "Failed to load configuration. Using default settings." +
                $"Unexpected error from {source}: {ex}"
            );
            LoadDefaultConfig();
        }
    }

    private void GetScripts() {
        videoM = FindFirstObjectByType<VideoManager>();
        sceneH = FindFirstObjectByType<SceneHandler>();
    }
    
    /// <summary>
    /// Loads a default configuration when JSON parsing fails or file is missing.
    /// Provides minimal working configuration to allow app to continue.
    /// </summary>
    private void LoadDefaultConfig() {
        Debug.LogWarning("[JsonStructure] Loading default configuration");
        
        ConfigData defaultConfig = new ConfigData {
            videos = new Videos[0],
            audios = new Audios[0],
            wordList = new WordList { words = new List<string> { "GROOTS" } },
            pointAward = new PointAward {
                correctWord = 100f,
                maxTimeAward = 120f,
                minTimeAward = 5f
            }
        };

        GetScripts();
        
        // Distribute default config to all managers
        ProcessTrainingConfig(defaultConfig);
        sceneH?.ProcessTrainingConfig(defaultConfig);
        
        Debug.Log("[JsonStructure] Default configuration loaded and distributed");
    }

    /// <summary>
    /// Loads config from persistentDataPath (runtime file system).
    /// Kept for backwards compatibility or runtime updates.
    /// </summary>
    private void UseLocalJson() {
        // Validate file exists before reading using FileValidator
        if (!FileValidator.ValidateJsonFile(localPath, showError: true)) {
            // File doesn't exist - load default config
            LoadDefaultConfig();
            return;
        }
        
        try {
            string jsonString = File.ReadAllText(localPath);
            LoadConfigFromJsonString(jsonString, "persistentDataPath");
        } catch (Exception ex) {
            Debug.LogError($"[JsonStructure] Error reading JSON file from persistentDataPath: {ex.Message}");
            Debug.LogError("localConfig.json " + ex.Message);
            // Load default config on file read error
            LoadDefaultConfig();
        }
    }

    /// <summary>
    /// Loads config from Resources folder (embedded in build).
    /// This is the primary method - config is bundled with the APK.
    /// </summary>
    private void LoadConfigFromResources() {
        try {
            TextAsset configAsset = Resources.Load<TextAsset>("localConfig");
            if (configAsset == null) {
                Debug.LogWarning("[JsonStructure] Config not found in Resources folder (localConfig). Will try persistentDataPath if available.");
                return;
            }

            LoadConfigFromJsonString(configAsset.text, "Resources (embedded in build)");
        } catch (Exception ex) {
            Debug.LogError("[JsonStructure] Failed to load config from Resources: " + ex);
        }
    }

    public void ProcessTrainingConfig(ConfigData configData) {
        Debug.Log("Config data: " + configData);
        data = configData;
        InitializeFromConfig(data, 0);
    }

    public void InitializeFromConfig(ConfigData cfg, int index) {
        data = cfg;
        bpm = data.music[0].numbers[index].bpm;
        startIndex = index;
    }

    [SerializeField] private VideoPlayer vid;

    private void Awake() {
        // singleton pattern (preserve existing instance if present)
        Instance = this;

        // ensure audio source
        audioSource = GetComponent<AudioSource>();
        if (audioSource == null) {
            audioSource = gameObject.AddComponent<AudioSource>();
            Debug.Log("JsonStructure: Added AudioSource at runtime.");
        }

        // Load config asynchronously (supports CDN downloads)
        StartCoroutine(LoadConfigAsync());

        // subscribe to sceneLoaded so we can bind UI/manager when scene arrives
        // UnityEngine.SceneManagement.SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private IEnumerator LoadConfigAsync() {
        // Try to load config from Resources first (embedded in build - no file copy needed)
        LoadConfigFromResources();

        // Fallback to persistentDataPath if Resources didn't load (backwards compatibility or runtime updates)
        localPath = Path.Combine(Application.persistentDataPath, "localConfig.json");
        
        if (data == null) {
            // Check if file exists, download from CDN if needed
            if (!File.Exists(localPath)) {
                // Try to download from CDN (works in Editor and on Quest3)
                // Try both "localConfig.json" and "LocalConfig.json" to handle case differences
                CDNDownloadManager.EnsureInstanceExists();
                if (CDNDownloadManager.Instance != null) {
                    Debug.Log("[JsonStructure]: Config not found locally, attempting CDN download: localConfig.json");
                    bool fileReady = false;
                    string errorMessage = null;
                    
                    // Try lowercase first (standard)
                    CDNDownloadManager.Instance.EnsureFileExists(
                        "localConfig.json",
                        onComplete: (path) => {
                            // File downloaded - ensure it's saved as lowercase "localConfig.json"
                            try {
                                string correctPath = Path.Combine(Application.persistentDataPath, "localConfig.json");
                                if (path != correctPath && File.Exists(path)) {
                                    if (File.Exists(correctPath)) {
                                        try {
                                            File.Delete(correctPath);
                                        } catch (Exception ex) {
                                            Debug.LogWarning($"[JsonStructure] Could not delete existing config: {ex.Message}");
                                        }
                                    }
                                    try {
                                        File.Move(path, correctPath);
                                    } catch (Exception ex) {
                                        Debug.LogWarning($"[JsonStructure] Could not move config file: {ex.Message}");
                                    }
                                    localPath = correctPath;
                                    Debug.Log("[JsonStructure]: Renamed downloaded config to localConfig.json (standard case)");
                                } else {
                                    localPath = path;
                                }
                            } catch (System.Exception ex) {
                                Debug.LogWarning($"[JsonStructure]: Failed to rename config file: {ex.Message}, using downloaded path");
                                localPath = path;
                            }
                            fileReady = true;
                        },
                        onError: (error) => {
                            // If lowercase fails, try uppercase (some CDNs use different case)
                            Debug.Log($"[JsonStructure]: Lowercase failed ({error}), trying LocalConfig.json (uppercase)");
                            StartCoroutine(TryAlternateCaseConfig(error, () => { fileReady = true; }, (err) => { errorMessage = err; fileReady = true; }));
                        }
                    );
                    
                    // Wait for download to complete (or fail)
                    yield return new WaitUntil(() => fileReady);
                    
                    if (!string.IsNullOrEmpty(errorMessage) || !File.Exists(localPath)) {
                        Debug.LogError($"[JsonStructure]: Failed to download config: {errorMessage ?? "Unknown error"}");
                    }
                }
            }
            
            // Try to load from persistentDataPath if file exists
            if (File.Exists(localPath)) {
                Debug.Log("[JsonStructure] Config not found in Resources, trying persistentDataPath: " + localPath);
                UseLocalJson();
            }
        }

        // Fallback to default config if no config was loaded at all
        if (data == null) {
            Debug.LogError("[JsonStructure] Failed to load config from both Resources and persistentDataPath. Loading default config.");
            Debug.LogError(
                "Failed to load configuration from all sources. Using default settings." +
                "No config loaded from Resources or persistentDataPath"
            );
            LoadDefaultConfig();
        }
    }

    /// <summary>
    /// Tries to download config with uppercase name (LocalConfig.json) if lowercase failed.
    /// Renames to lowercase after download to ensure consistent naming.
    /// </summary>
    private IEnumerator TryAlternateCaseConfig(string firstError, System.Action onSuccess, System.Action<string> onFailure) {
        bool altFileReady = false;
        string altErrorMessage = firstError;
        
        CDNDownloadManager.Instance.EnsureFileExists(
            "LocalConfig.json",
            onComplete: (altPath) => {
                // Rename to standard lowercase
                try {
                    string correctPath = Path.Combine(Application.persistentDataPath, "localConfig.json");
                    if (File.Exists(correctPath)) {
                        File.Delete(correctPath);
                    }
                    if (File.Exists(altPath)) {
                        File.Move(altPath, correctPath);
                        localPath = correctPath;
                        Debug.Log("[JsonStructure]: Downloaded LocalConfig.json and renamed to localConfig.json");
                    } else {
                        localPath = altPath;
                    }
                } catch (System.Exception ex) {
                    Debug.LogWarning($"[JsonStructure]: Failed to rename config: {ex.Message}");
                    localPath = altPath;
                }
                altFileReady = true;
                onSuccess?.Invoke();
            },
            onError: (altError) => {
                altErrorMessage = $"Both localConfig.json and LocalConfig.json failed: {firstError}, {altError}";
                altFileReady = true;
                onFailure?.Invoke(altErrorMessage);
            }
        );
        
        yield return new WaitUntil(() => altFileReady);
        
        if (!string.IsNullOrEmpty(altErrorMessage) && altErrorMessage != firstError) {
            Debug.LogError($"[JsonStructure]: Failed to download config with alternate case: {altErrorMessage}");
        }
    }

    private void OnDestroy() {
        if (Instance == this) Instance = null;
        // UnityEngine.SceneManagement.SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    private int numList;

    public void LoadSong(int numberId) {
        foreach(var num in data.music[0].numbers) {
            Debug.Log($"[JsonStructure]: Music 0 numbers, Song: {num.song}");
        }
        foreach(var num in data.music) {
            Debug.Log($"[JsonStructure]: Music List numbers, Song: {num.numbers[numList].song}");
            numList++;
        }
        // clamp into valid range
        Debug.Log($"[JsonStructure]: LoadSong({numberId}) called. data==null? {data==null}");
        Debug.Log($"[JsonStructure]: Number: {data.music[0].numbers[0].subFolder} {data.music[0].numbers[0].song} {data.music[0].numbers[0].beatmap}");

        if (data == null) {
            Debug.LogError("[JsonStructure]: LoadSong: data is null. Aborting.");
            return;
        }
        if (data.music == null || data.music.Length == 0) {
            Debug.LogError("[JsonStructure]: LoadSong: data.music missing or empty.");
            return;
        }
        if (data.music[0].numbers == null) {
            Debug.LogError("[JsonStructure]: LoadSong: numbers array missing.");
            return;
        }

        var musicArray = data.music[0].numbers;
        if (numberId < 0 || numberId >= musicArray.Length) {
            Debug.LogError($"[JsonStructure]: LoadSong, invalid index {numberId}");
            return;
        }

        var info = musicArray[numberId];

        // update BPM before converting beats → seconds
        bpm = info.bpm;

        // compose full paths
        string baseFolder = Path.Combine(Application.persistentDataPath, data.music[0].folder);
        string beatmapPath = Path.Combine(baseFolder, info.subFolder, info.beatmap);
        string songPath    = Path.Combine(baseFolder, info.subFolder, info.song);
        Debug.Log("[JsonStructure]: songpath: " + songPath + " beatmap: " + beatmapPath);

        // if (songName == null) {
        //     songName = CacheSongTextComponent();
        // }
        // if (songName != null) {
        //     // Format song name for display (remove extension, replace underscores with spaces)
        //     songName.text = FormatSongNameForDisplay(info.song);
        // } else {
        //     Debug.LogWarning("[JsonStructure]: LoadSong, songName TMP_Text not found. Skipping UI label update.");
        // }

        // 1) load the beatmap right away
        LoadMap(beatmapPath);
        if (audioSource == null) {
            audioSource = GetComponent<AudioSource>();
            if (audioSource == null) {
                audioSource = gameObject.AddComponent<AudioSource>();
                Debug.Log("[JsonStructure]: Added AudioSource before audio load.");
            }
        }
        // 2) load the audio file asynchronously as an AudioClip
        StartCoroutine(LoadAudioClip(songPath));
    }

    public void MusicSetup() {
        Debug.Log("[JsonStructure]: MusicSetup() called. data==null? " + (data == null));
        if (pendingSongIndex < 0) pendingSongIndex = 0;
        StartCoroutine(WaitAndGo());
        // if the scene is already ready, perform setup immediately
        // if (IsSceneReadyForMusic()) {
        //     StartCoroutine(PerformSetupAndLoadIfRequested());
        // }
    }

    IEnumerator WaitAndGo() {
        yield return new WaitForSeconds(4);
        // if (songName == null) {
        //     songName = CacheSongTextComponent();
        // }
        LoadSong(0);
    }
    
    /// <summary>
    /// Caches the Song GameObject's TMP_Text component to avoid GameObject.Find() calls.
    /// </summary>
    /// <summary>
    /// Formats a song filename for display by removing extension and replacing underscores with spaces.
    /// Example: "KPop_Demon_Hunters_Golden.mp3" → "KPop Demon Hunters Golden"
    /// </summary>
    /// <param name="songFileName">Song filename (e.g., "KPop_Demon_Hunters_Golden.mp3")</param>
    /// <returns>Formatted song name for display</returns>
    private string FormatSongNameForDisplay(string songFileName) {
        if (string.IsNullOrEmpty(songFileName)) {
            return "";
        }
        
        // Remove file extension (.mp3, .mp3.mpeg, etc.)
        string formatted = songFileName;
        
        // Remove common audio extensions
        if (formatted.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase)) {
            formatted = formatted.Substring(0, formatted.Length - 4);
        } else if (formatted.EndsWith(".mp3.mpeg", StringComparison.OrdinalIgnoreCase)) {
            formatted = formatted.Substring(0, formatted.Length - 9);
        } else if (formatted.EndsWith(".mpeg", StringComparison.OrdinalIgnoreCase)) {
            formatted = formatted.Substring(0, formatted.Length - 5);
        }
        
        // Replace underscores with spaces
        formatted = formatted.Replace("_", " ");
        
        // Trim any leading/trailing whitespace
        formatted = formatted.Trim();
        
        return formatted;
    }
    
    private TMP_Text CacheSongTextComponent() {
        GameObject songObj = GameObject.Find("Song");
        if (songObj != null) {
            return songObj.GetComponent<TMP_Text>();
        }
        return null;
    }
    
    /// <summary>
    /// Hides the song name text during choices phase.
    /// </summary>
    public void HideSongName() {
        // if (songName == null) {
        //     songName = CacheSongTextComponent();
        // }
        // if (songName != null) {
        //     songName.gameObject.SetActive(false);
        //     Debug.Log("[JsonStructure]: Song name hidden during choices");
        // }
    }
    
    /// <summary>
    /// Shows the song name text again after choices phase.
    /// </summary>
    public void ShowSongName() {
        // if (songName == null) {
        //     songName = CacheSongTextComponent();
        // }
        // if (songName != null) {
        //     songName.gameObject.SetActive(true);
        //     Debug.Log("[JsonStructure]: Song name shown again");
        // }
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

    private IEnumerator LoadAudioClip(string path) {
        // First, check test-videos folder in Editor (like VideoManager does)
        string finalPath = GetAudioPath(path);
        
        // Check if file is actually an image (album covers sometimes have wrong extensions)
        if (IsImageFile(finalPath)) {
            Debug.LogWarning($"[JsonStructure]: Skipping image file (not audio): {Path.GetFileName(finalPath)}");
            // Still invoke OnMusicReady to allow the game to continue
            // The audio source will remain null/empty, which should be handled by the game logic
            OnMusicReady?.Invoke();
            yield break;
        }

        // Check if file exists, download from CDN if needed
        if (!File.Exists(finalPath)) {
            // Extract fileName from path (relative to download folder)
            string fileName = Path.GetFileName(finalPath);
            
            // Try to get relative path for CDN download
            #if UNITY_EDITOR
            string testVideosPath = Path.Combine(Application.dataPath, "..", "test-videos");
            testVideosPath = Path.GetFullPath(testVideosPath);
            if (finalPath.StartsWith(testVideosPath)) {
                fileName = finalPath.Substring(testVideosPath.Length).TrimStart('\\', '/');
            }
            #else
            string persistentPath = Application.persistentDataPath;
            if (finalPath.StartsWith(persistentPath)) {
                fileName = finalPath.Substring(persistentPath.Length).TrimStart('\\', '/');
            }
            #endif
            
            // Try to download from CDN (works in Editor and on Quest3)
            CDNDownloadManager.EnsureInstanceExists();
            if (CDNDownloadManager.Instance != null) {
                Debug.Log($"[JsonStructure]: Audio not found locally, attempting CDN download: {fileName}");
                bool fileReady = false;
                string errorMessage = null;
                
                CDNDownloadManager.Instance.EnsureFileExists(
                    fileName,
                    onComplete: (downloadedPath) => {
                        path = downloadedPath;
                        fileReady = true;
                    },
                    onError: (error) => {
                        errorMessage = error;
                        fileReady = true; // Set to true to exit wait loop
                    }
                );
                
                // Wait for download to complete (or fail)
                yield return new WaitUntil(() => fileReady);
                
                if (!string.IsNullOrEmpty(errorMessage) || !File.Exists(finalPath)) {
                    Debug.LogError($"[JsonStructure]: Failed to download audio: {errorMessage ?? "Unknown error"}");
                    yield break;
                }
                finalPath = path; // Use downloaded path
            } else {
                Debug.LogError($"[JsonStructure]: Audio file not found: {finalPath} and CDNDownloadManager not available");
                yield break;
            }
        }

        path = finalPath; // Use the found path

        // Double-check after download that it's still not an image
        if (IsImageFile(path)) {
            Debug.LogWarning($"[JsonStructure]: Skipping image file (not audio): {Path.GetFileName(path)}");
            OnMusicReady?.Invoke();
            yield break;
        }

        // note the file:// prefix
        using var uwr = UnityWebRequestMultimedia.GetAudioClip("file:///" + path, AudioType.MPEG);
        yield return uwr.SendWebRequest();

        if (uwr.result != UnityWebRequest.Result.Success) {
            // Check if error is related to unsupported file type (e.g., image file with wrong extension)
            if (uwr.error.Contains("0xc00d36c4") || uwr.error.Contains("unsupported") || uwr.error.Contains("byte stream type")) {
                Debug.LogWarning($"[JsonStructure]: File appears to be an image or unsupported format (not audio): {Path.GetFileName(path)}. Error: {uwr.error}");
            } else {
                Debug.LogError($"[JsonStructure]: Error loading audio: {uwr.error}");
            }
            // Still invoke OnMusicReady to allow the game to continue
            OnMusicReady?.Invoke();
            yield break;
        }

        AudioClip clip = DownloadHandlerAudioClip.GetContent(uwr);
        if (clip == null) {
            Debug.LogWarning($"[JsonStructure]: AudioClip is null after loading. File may be an image: {Path.GetFileName(path)}");
            OnMusicReady?.Invoke();
            yield break;
        }

        if (audioSource == null) {
            audioSource = GetComponent<AudioSource>();
            if (audioSource == null) {
                audioSource = gameObject.AddComponent<AudioSource>();
                Debug.Log("[JsonStructure]: Added AudioSource after audio load (safeguard).");
            }
        }

        audioSource.clip = clip;
        Debug.Log($"[JsonStructure]: Loaded AudioClip: {clip.name} ({clip.length:F1}s)");
        OnMusicReady?.Invoke();
    }

    private void LoadMap(string path) {
        // First, check test-videos folder in Editor (like VideoManager does)
        string finalPath = GetBeatmapPath(path);
        
        // Check if file exists, download from CDN if needed
        if (!File.Exists(finalPath)) {
            // Extract fileName from path (relative to download folder)
            string fileName = Path.GetFileName(finalPath);
            
            // Try to get relative path for CDN download
            #if UNITY_EDITOR
            string testVideosPath = Path.Combine(Application.dataPath, "..", "test-videos");
            testVideosPath = Path.GetFullPath(testVideosPath);
            if (finalPath.StartsWith(testVideosPath)) {
                fileName = finalPath.Substring(testVideosPath.Length).TrimStart('\\', '/');
            }
            #else
            string persistentPath = Application.persistentDataPath;
            if (finalPath.StartsWith(persistentPath)) {
                fileName = finalPath.Substring(persistentPath.Length).TrimStart('\\', '/');
            }
            #endif
            
            // Try to download from CDN (works in Editor and on Quest3)
            CDNDownloadManager.EnsureInstanceExists();
            if (CDNDownloadManager.Instance != null) {
                Debug.Log($"[JsonStructure]: Map not found locally, attempting CDN download: {fileName}");
                // Start async download and wait - LoadMap is called from coroutine context (LoadSong)
                StartCoroutine(WaitForFileAndLoadMap(finalPath, fileName));
                return;
            } else {
                Debug.LogError($"[JsonStructure]: Map file not found: {finalPath} and CDNDownloadManager not available");
                return;
            }
        }
        
        path = finalPath; // Use the found path

        Debug.Log("[JsonStructure]: Path exists: " + path);
        string json = File.ReadAllText(path);
        BeatSaberMapRaw rawMap = JsonUtility.FromJson<BeatSaberMapRaw>(json);
        if (rawMap?.colorNotes == null || rawMap.colorNotesData == null) {
            Debug.LogError("[JsonStructure]: Failed to parse Beat Saber map data.");
            return;
        }

        var rawNotes   = rawMap.colorNotes;
        var rawBodies  = rawMap.colorNotesData;
        float secPerBeat = 60f / bpm;

        notes = new List<Note>(rawNotes.Count);

        for (int idx = 0; idx < rawNotes.Count; idx++) {
            var header = rawNotes[idx];
            int bodyIndex = header.i;

            // guard in case of malformed map
            if (bodyIndex < 0 || bodyIndex >= rawBodies.Count) {
                Debug.LogWarning($"[JsonStructure]: Note #{idx} has invalid metadata index {bodyIndex}");
                continue;
            }

            var body = rawBodies[bodyIndex];
            notes.Add(new Note {
                time         = header.b * secPerBeat,
                lineIndex    = body.x,
                lineLayer    = body.y,
                colorIndex   = body.c,
                cutDirection = body.d
            });

            // Debug.Log($"Note[{idx}] beat={header.b} → grid X={body.x}, Y={body.y}");
        }

        notes.Sort((a, b) => a.time.CompareTo(b.time));
    }
    
    /// <summary>
    /// Gets the full path to a beatmap file, checking test-videos folder in Editor first,
    /// then falling back to persistentDataPath. On Quest3, always uses persistentDataPath.
    /// </summary>
    private string GetBeatmapPath(string originalPath) {
        #if UNITY_EDITOR
        // In Editor: Check test-videos folder first
        // Extract relative path from persistentDataPath structure
        string relativePath = originalPath;
        
        // Try to extract relative path (e.g., "Music/Kpop/HardStandard.beatmap.dat")
        string persistentPath = Application.persistentDataPath;
        if (originalPath.StartsWith(persistentPath)) {
            relativePath = originalPath.Substring(persistentPath.Length).TrimStart('\\', '/');
        }
        
        string testVideosPath = Path.Combine(Application.dataPath, "..", "test-videos", relativePath);
        testVideosPath = Path.GetFullPath(testVideosPath);
        
        if (File.Exists(testVideosPath)) {
            Debug.Log($"[JsonStructure] ✓ Beatmap FOUND in test-videos: {relativePath}");
            return testVideosPath;
        }
        
        Debug.Log($"[JsonStructure] ✗ Beatmap NOT FOUND in test-videos: {relativePath}, trying persistentDataPath");
        #endif
        
        return originalPath;
    }
    
    /// <summary>
    /// Gets the full path to an audio file, checking test-videos folder in Editor first,
    /// then falling back to persistentDataPath. On Quest3, always uses persistentDataPath.
    /// </summary>
    private string GetAudioPath(string originalPath) {
        #if UNITY_EDITOR
        // In Editor: Check test-videos folder first
        // Extract relative path from persistentDataPath structure
        string relativePath = originalPath;
        
        // Try to extract relative path (e.g., "Music/Kpop/song.mp3")
        string persistentPath = Application.persistentDataPath;
        if (originalPath.StartsWith(persistentPath)) {
            relativePath = originalPath.Substring(persistentPath.Length).TrimStart('\\', '/');
        }
        
        string testVideosPath = Path.Combine(Application.dataPath, "..", "test-videos", relativePath);
        testVideosPath = Path.GetFullPath(testVideosPath);
        
        if (File.Exists(testVideosPath)) {
            Debug.Log($"[JsonStructure] ✓ Audio FOUND in test-videos: {relativePath}");
            return testVideosPath;
        }
        
        Debug.Log($"[JsonStructure] ✗ Audio NOT FOUND in test-videos: {relativePath}, trying persistentDataPath");
        #endif
        
        return originalPath;
    }

    /// <summary>
    /// Helper coroutine to wait for file download and then load map.
    /// </summary>
    private IEnumerator WaitForFileAndLoadMap(string originalPath, string fileName) {
        // Wait for download to complete
        while (CDNDownloadManager.Instance != null && CDNDownloadManager.Instance.IsDownloading(fileName)) {
            yield return new WaitForSeconds(0.1f);
        }
        
        // Check if file now exists (in Editor: test-videos, on Quest3: persistentDataPath)
        string downloadedPath;
        #if UNITY_EDITOR
        string testVideosPath = Path.Combine(Application.dataPath, "..", "test-videos");
        downloadedPath = Path.Combine(Path.GetFullPath(testVideosPath), fileName);
        #else
        string persistentPath = Application.persistentDataPath;
        downloadedPath = Path.Combine(persistentPath, fileName);
        #endif
        string finalPath = File.Exists(downloadedPath) ? downloadedPath : originalPath;
        
        if (File.Exists(finalPath)) {
            LoadMap(finalPath);
        } else {
            Debug.LogError($"[JsonStructure]: Map file still not found after download attempt: {finalPath}");
        }
    }

}
