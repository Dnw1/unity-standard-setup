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
public class JSONResponse { public string id; public ConfigData config; public string action; }
[Serializable]
public class ConfigData { public Videos[] videos; public VideoSettings videoSettings; public Audios[] audios; public Lighting[] lighting; public PostProcessing[] postProcessing; }
[Serializable]
public class Videos  { public string name; public string subFolder; public float volume = 1.0f; public float? videoException; public string stereoException; }
[Serializable]
public class VideoSettings { public float videoShape; public string videoStereo; }
[Serializable]
public class Audios  { public string name; public string subFolder; public float volume = 1.0f; }
// [Serializable]
// public class Music { public string folder; public DestinationOrder destinationOrder; public Numbers[] numbers; }
[Serializable]
public class DestinationOrder { public List<string> order; public int timer = 30; }
[Serializable]
public class Numbers { public string subFolder; public string song; public int bpm; public string beatmap; }
[Serializable]
public class Note { public float time; public int lineIndex; public int lineLayer; public int colorIndex; public int cutDirection; }
[Serializable]
public class Lighting { public string color = "#FFFFFF"; public float intensity = 1f; }
// [Serializable]
// public class WordList { public List<string> words; }
// [Serializable]
// public class PointAward { public float correctWord; public float maxTimeAward; public float minTimeAward; }
// [Serializable]
// public class NoteRaw { public float b; public int r; public int i; }
// [Serializable]
// public class NoteDataRaw { public int x; public int y; public int c; public int d; public int a; }
// [Serializable]
// public class BeatSaberMapRaw { public List<NoteRaw> colorNotes; public List<NoteDataRaw> colorNotesData; }
[Serializable]
public class PostProcessing { public bool overrideBloom = false; public float bloomIntensity = 1f; }

namespace com.dnw.standardpackage
{

    public class JsonStructure : MonoBehaviour {
        // public string mapFilePath;
        public float bpm = 123f;

        // [HideInInspector] public List<Note> notes;
        public ConfigData data;

        // private TMP_Text songName;
        public static JsonStructure Instance { get; private set; }

        private int pendingSongIndex = -1;
        public event Action OnMusicReady;
        // [SerializeField] public AudioSource audioSource;

        // private int startIndex;
        private string localPath;
        [SerializeField] private SceneHandler sceneH;
        [SerializeField] private VideoManager videoM;

        private static readonly HashSet<string> ImageExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
            ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".tga", ".tiff", ".tif", ".exr", ".hdr", ".dds", ".psd"
        };

        private void Awake() {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            DontDestroyOnLoad(gameObject);

            // audioSource = GetComponent<AudioSource>() ?? gameObject.AddComponent<AudioSource>();

            // start loading config
            StartCoroutine(LoadConfigAsync());
        }

        private IEnumerator LoadConfigAsync() {
            // 1) Try bundled resource
            TryLoadConfigFromResources();

            // 2) If still null, try persistentDataPath / CDN
            localPath = Path.Combine(Application.persistentDataPath, "localConfig.json");

            if (data == null) {
                if (!File.Exists(localPath)) {
                    // Try CDN for either lowercase or uppercase filename (single helper)
                    string[] candidates = new[] { "localConfig.json", "LocalConfig.json" };
                    yield return TryDownloadCandidatesFromCdn(candidates, (downloaded) => localPath = downloaded);
                }

                if (File.Exists(localPath)) {
                    UseLocalJson();
                }
            }

            // 3) fallback to default
            if (data == null) {
                LoadDefaultConfig();
            }
        }

        private void TryLoadConfigFromResources() {
            try {
                TextAsset configAsset = Resources.Load<TextAsset>("localConfig");
                if (configAsset == null) return;
                LoadConfigFromJsonString(configAsset.text, "Resources");
            } catch (Exception ex) {
                Debug.LogWarning("[Json] Resources load failed: " + ex.Message);
            }
        }

        // Attempts downloading candidate filenames using CDNDownloadManager; returns after first success or all fail
        private IEnumerator TryDownloadCandidatesFromCdn(string[] candidates, Action<string> onDownloaded) {
            if (CDNDownloadManager.Instance == null) {
                CDNDownloadManager.EnsureInstanceExists();
                yield return null; // allow Ensure to initialize
            }

            if (CDNDownloadManager.Instance == null) yield break;

            foreach (var candidate in candidates) {
                bool done = false;
                string resultPath = null;
                string error = null;

                CDNDownloadManager.Instance.EnsureFileExists(candidate, onComplete: (path) => { resultPath = path; done = true; }, onError: (err) => { error = err; done = true; });
                yield return new WaitUntil(() => done);

                if (!string.IsNullOrEmpty(resultPath) && File.Exists(resultPath)) {
                    // Normalize: ensure lowercase filename in persistentDataPath when possible
                    try {
                        string dest = Path.Combine(Application.persistentDataPath, "localConfig.json");
                        if (!string.Equals(Path.GetFullPath(resultPath), Path.GetFullPath(dest), StringComparison.OrdinalIgnoreCase)) {
                            if (File.Exists(dest)) File.Delete(dest);
                            File.Move(resultPath, dest);
                            resultPath = dest;
                        }
                    } catch (Exception ex) {
                        Debug.LogWarning("[Json] Could not normalize downloaded config filename: " + ex.Message);
                    }

                    onDownloaded?.Invoke(resultPath);
                    yield break;
                }
                // else continue to next candidate
            }

            Debug.LogWarning("[Json] CDN did not supply any of the candidate config files.");
        }

        private void LoadConfigFromJsonString(string jsonString, string source) {
            if (string.IsNullOrWhiteSpace(jsonString)) { LoadDefaultConfig(); return; }

            try {
                var root = JObject.Parse(jsonString);

                // lightweight warnings if common sections are missing
                if (root["videos"] == null) Debug.LogWarning("[Json] 'videos' missing");
                if (root["audios"] == null) Debug.LogWarning("[Json] 'audios' missing");

                var settings = new JsonSerializerSettings { MissingMemberHandling = MissingMemberHandling.Ignore, NullValueHandling = NullValueHandling.Ignore, Error = (s, e) => { Debug.LogWarning("[Json] parse warning: " + e.ErrorContext.Error.Message); e.ErrorContext.Handled = true; } };
                var cfg = root.ToObject<ConfigData>(JsonSerializer.Create(settings));

                if (cfg == null) { Debug.LogError("[Json] config deserialized null"); LoadDefaultConfig(); return; }

                // Ensure wordlist fallback
                // if (cfg.wordList == null || cfg.wordList.words == null || cfg.wordList.words.Count == 0) cfg.wordList = cfg.wordList ?? new WordList() { words = new List<string>() };
                // if (cfg.wordList.words.Count == 0) cfg.wordList.words.Add("GROOTS");

                BindManagersIfNeeded();
                ProcessTrainingConfig(cfg);
                videoM?.ProcessTrainingConfig(cfg);
                sceneH?.ProcessTrainingConfig(cfg);
                if (AudioManager.Instance != null) AudioManager.Instance.ProcessTrainingConfig(cfg);

            } catch (JsonException jex) {
                Debug.LogError("[Json] parse error: " + jex.Message);
                LoadDefaultConfig();
            } catch (Exception ex) {
                Debug.LogError("[Json] unexpected error: " + ex);
                LoadDefaultConfig();
            }
        }

        private void BindManagersIfNeeded() {
            if (videoM == null) videoM = FindFirstObjectByType<VideoManager>();
            if (sceneH == null) sceneH = FindFirstObjectByType<SceneHandler>();
        }

        private void LoadDefaultConfig() {
            Debug.LogWarning("[Json] Loading default config");
            var defaultConfig = new ConfigData {
                videos = new Videos[0], audios = new Audios[0]
            };
            BindManagersIfNeeded();
            ProcessTrainingConfig(defaultConfig);
        }

        private void UseLocalJson() {
            if (!File.Exists(localPath)) { LoadDefaultConfig(); return; }
            try {
                var jsonString = File.ReadAllText(localPath);
                LoadConfigFromJsonString(jsonString, "persistentDataPath");
            } catch (Exception ex) { Debug.LogError("[Json] could not read local config: " + ex.Message); LoadDefaultConfig(); }
        }

        public void ProcessTrainingConfig(ConfigData configData) {
            data = configData;
            InitializeFromConfig(data, 0);
        }

        public void InitializeFromConfig(ConfigData cfg, int index) {
            data = cfg;
            // if (data?.music != null && data.music.Length > 0 && data.music[0].numbers != null && data.music[0].numbers.Length > index) {
            //     bpm = data.music[0].numbers[index].bpm;
            //     startIndex = index;
            // } else {
            //     Debug.LogWarning("[Json] InitializeFromConfig: music metadata missing or malformed");
            // }
        }

        // public void LoadSong(int numberId) {
        //     if (data == null || data.music == null || data.music.Length == 0 || data.music[0].numbers == null) { Debug.LogError("[Json] LoadSong: data/music missing"); return; }

        //     var info = data.music[0].numbers.ElementAtOrDefault(numberId);
        //     if (info == null) { Debug.LogError($"[Json] LoadSong: invalid index {numberId}"); return; }

        //     bpm = info.bpm;
        //     string baseFolder = Path.Combine(Application.persistentDataPath, data.music[0].folder);
        //     // string beatmapPath = Path.Combine(baseFolder, info.subFolder, info.beatmap);
        //     string songPath    = Path.Combine(baseFolder, info.subFolder, info.song);

        //     // LoadMap(beatmapPath);
        //     StartCoroutine(LoadAudioClip(songPath));
        // }

        public void MusicSetup() {
            if (pendingSongIndex < 0) pendingSongIndex = 0;
            // StartCoroutine(DelayedLoad(pendingSongIndex, 4f));
        }

        // private IEnumerator DelayedLoad(int number, float seconds) { yield return new WaitForSeconds(seconds); LoadSong(number); }

        // private string FormatSongNameForDisplay(string songFileName) {
        //     if (string.IsNullOrEmpty(songFileName)) return string.Empty;
        //     var formatted = Path.GetFileNameWithoutExtension(songFileName).Replace("_", " ").Trim();
        //     return formatted;
        // }

        private bool IsImageFile(string filePath) {
            var fileName = Path.GetFileName(filePath);
            var ext = Path.GetExtension(fileName);
            if (!string.IsNullOrEmpty(ext) && ImageExtensions.Contains(ext)) return true;

            // Check for double extension patterns like "name.jpg.mp3" or embedded image name
            foreach (var imgExt in ImageExtensions) if (fileName.IndexOf(imgExt, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            return false;
        }

        // private IEnumerator LoadAudioClip(string path) {
        //     string finalPath = GetPathWithTestVideos(path);

        //     if (IsImageFile(finalPath)) { Debug.LogWarning("[Json] audio path points to image, skipping: " + finalPath); OnMusicReady?.Invoke(); yield break; }

        //     if (!File.Exists(finalPath)) {
        //         string fileName = Path.GetFileName(finalPath);
        //         if (CDNDownloadManager.Instance == null) CDNDownloadManager.EnsureInstanceExists();
        //         if (CDNDownloadManager.Instance != null) {
        //             bool done = false; string downloaded = null;
        //             CDNDownloadManager.Instance.EnsureFileExists(fileName, onComplete: (p) => { downloaded = p; done = true; }, onError: (e) => { Debug.LogError("[Json] CDN audio error: " + e); done = true; });
        //             yield return new WaitUntil(() => done);
        //             if (string.IsNullOrEmpty(downloaded) || !File.Exists(downloaded)) { Debug.LogError("[Json] Failed to retrieve audio from CDN: " + fileName); yield break; }
        //             finalPath = downloaded;
        //         } else { Debug.LogError("[Json] Audio not found locally and CDN unavailable: " + finalPath); yield break; }
        //     }

        //     if (IsImageFile(finalPath)) { Debug.LogWarning("[Json] final audio is actually image, skipping: " + finalPath); OnMusicReady?.Invoke(); yield break; }

        //     using var uwr = UnityWebRequestMultimedia.GetAudioClip("file://" + finalPath, AudioType.MPEG);
        //     yield return uwr.SendWebRequest();

        //     if (uwr.result != UnityWebRequest.Result.Success) { Debug.LogWarning("[Json] audio load failed: " + uwr.error); OnMusicReady?.Invoke(); yield break; }

        //     var clip = DownloadHandlerAudioClip.GetContent(uwr);
        //     if (clip == null) { Debug.LogWarning("[Json] AudioClip null after load: " + finalPath); OnMusicReady?.Invoke(); yield break; }

        //     audioSource = audioSource ?? GetComponent<AudioSource>() ?? gameObject.AddComponent<AudioSource>();
        //     audioSource.clip = clip;
        //     Debug.Log($"[Json] Loaded AudioClip: {clip.name} ({clip.length:F1}s)");
        //     OnMusicReady?.Invoke();
        // }

        // private void LoadMap(string path) {
        //     string finalPath = GetPathWithTestVideos(path);
        //     if (!File.Exists(finalPath)) {
        //         string fileName = Path.GetFileName(finalPath);
        //         StartCoroutine(WaitForFileAndLoadMap(finalPath, fileName));
        //         return;
        //     }

        //     try {
        //         string json = File.ReadAllText(finalPath);
        //         var rawMap = JsonUtility.FromJson<BeatSaberMapRaw>(json);
        //         if (rawMap?.colorNotes == null || rawMap.colorNotesData == null) { Debug.LogError("[Json] Beatmap parse failed"); return; }

        //         float secPerBeat = 60f / bpm;
        //         notes = new List<Note>(rawMap.colorNotes.Count);
        //         for (int i = 0; i < rawMap.colorNotes.Count; i++) {
        //             var h = rawMap.colorNotes[i];
        //             int bodyIdx = h.i;
        //             if (bodyIdx < 0 || bodyIdx >= rawMap.colorNotesData.Count) { Debug.LogWarning("[Json] malformed note index"); continue; }
        //             var b = rawMap.colorNotesData[bodyIdx];
        //             notes.Add(new Note { time = h.b * secPerBeat, lineIndex = b.x, lineLayer = b.y, colorIndex = b.c, cutDirection = b.d });
        //         }
        //         notes.Sort((a, b) => a.time.CompareTo(b.time));
        //     } catch (Exception ex) { Debug.LogError("[Json] LoadMap failed: " + ex.Message); }
        // }

        private string GetPathWithTestVideos(string originalPath) {
            #if UNITY_EDITOR
            string persistentPath = Application.persistentDataPath;
            string relative = originalPath.StartsWith(persistentPath) ? originalPath.Substring(persistentPath.Length).TrimStart('/', '\\') : originalPath;
            string testVideosPath = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "test-videos", relative));
            if (File.Exists(testVideosPath)) return testVideosPath;
            #endif
            return originalPath;
        }

        private IEnumerator WaitForFileAndLoadMap(string originalPath, string fileName) {
            while (CDNDownloadManager.Instance != null && CDNDownloadManager.Instance.IsDownloading(fileName)) yield return new WaitForSeconds(0.1f);

            #if UNITY_EDITOR
            string testVideosPath = Path.Combine(Path.GetFullPath(Path.Combine(Application.dataPath, "..", "test-videos")), fileName);
            string downloadedPath = File.Exists(testVideosPath) ? testVideosPath : originalPath;
            #else
            string downloadedPath = File.Exists(Path.Combine(Application.persistentDataPath, fileName)) ? Path.Combine(Application.persistentDataPath, fileName) : originalPath;
            #endif

            // if (File.Exists(downloadedPath)) LoadMap(downloadedPath); else Debug.LogError("[Json] Map still missing after CDN attempt: " + fileName);
        }

        private void OnDestroy() { if (Instance == this) Instance = null; }
    }
}
