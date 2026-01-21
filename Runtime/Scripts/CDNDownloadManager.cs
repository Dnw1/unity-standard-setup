using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;

namespace com.dnw.standardpackage
{

    /// <summary>
    /// Manages downloading files from CDN when they don't exist locally.
    /// Downloads continue even when headset is removed (uses unscaled time).
    /// </summary>
    public class CDNDownloadManager : MonoBehaviour
    {
        public static CDNDownloadManager Instance { get; private set; }

        [Header("CDN Configuration")]
        [Tooltip("Base URL for CDN (direct file URLs only for security)")]
        [SerializeField] private string cdnBaseUrl = "https://cdn.pack.house/de-dijk/files/";
        
        [Tooltip("Enable CDN downloads (set to false to use local files only)")]
        [SerializeField] private bool enableCDNDownloads = true;

        [Header("Download Settings")]
        [Tooltip("Timeout in seconds for each download (large video files may need 30+ minutes on slow connections)")]
        [SerializeField] private int downloadTimeout = 1800; // 30 minutes for large video files on slow connections
        
        [Tooltip("Maximum concurrent downloads")]
        [SerializeField] private int maxConcurrentDownloads = 3;
        
        [Tooltip("Maximum number of retry attempts for failed downloads")]
        [SerializeField] private int maxRetries = 3;
        
        [Tooltip("Delay between retries in seconds (exponential backoff)")]
        [SerializeField] private float retryDelayBase = 2f;

        // Events
        public event Action<string, float> OnDownloadProgress; // fileName, progress (0-1)
        public event Action<string> OnDownloadComplete; // fileName
        public event Action<string, string> OnDownloadError; // fileName, error message
        public event Action<int, int> OnBatchProgress; // downloaded, total

        // Download state
        private Dictionary<string, Coroutine> _activeDownloads = new Dictionary<string, Coroutine>();
        private HashSet<string> _downloadingFiles = new HashSet<string>();
        private Queue<DownloadRequest> _downloadQueue = new Queue<DownloadRequest>();
        private int _activeDownloadCount = 0;

        private class DownloadRequest
        {
            public string fileName;
            public string localPath;
            public string cdnUrl;
            public Action<string> onComplete;
            public Action<string> onError;
            public int retryCount = 0;
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);
            
            // Clean up any leftover .temp files from interrupted downloads
            CleanupTempFiles();
            
            // Auto-add DownloadProgressUI if not present (for backward compatibility)
            // Note: VRDownloadModal can be added manually or via VRUIManager for VR-optimized display
            // We don't auto-add VRDownloadModal here to avoid conflicts - it should be added via VRUIManager or manually
            if (GetComponent<DownloadProgressUI>() == null && GetComponent<VRDownloadModal>() == null)
            {
                gameObject.AddComponent<DownloadProgressUI>();
                Debug.Log("[CDNDownloadManager]: Auto-added DownloadProgressUI component. For VR, add VRDownloadModal via VRUIManager.");
            }
            
            Debug.Log($"[CDNDownloadManager]: Initialized - CDN URL: {cdnBaseUrl}, Enabled: {enableCDNDownloads}");
        }

        /// <summary>
        /// Cleans up any leftover .temp files from interrupted downloads.
        /// Note: Temp files are kept during active downloads to enable resume capability.
        /// This cleanup only runs on startup to remove old abandoned temp files.
        /// </summary>
        private void CleanupTempFiles()
        {
            try
            {
                string basePath;
                #if UNITY_EDITOR
                string testVideosPath = Path.Combine(Application.dataPath, "..", "test-videos");
                basePath = Path.GetFullPath(testVideosPath);
                #else
                basePath = Application.persistentDataPath;
                #endif

                if (!Directory.Exists(basePath))
                    return;

                // Find all .temp files
                string[] tempFiles = Directory.GetFiles(basePath, "*.temp", SearchOption.AllDirectories);
                int cleanedCount = 0;
                
                foreach (string tempFile in tempFiles)
                {
                    try
                    {
                        // Only delete temp files that are older than 1 hour (likely abandoned)
                        FileInfo fileInfo = new FileInfo(tempFile);
                        if (fileInfo.Exists && (DateTime.UtcNow - fileInfo.LastWriteTimeUtc).TotalHours > 1)
                        {
                            File.Delete(tempFile);
                            cleanedCount++;
                            Debug.Log($"[CDNDownloadManager]: Cleaned up old temp file: {Path.GetFileName(tempFile)}");
                        }
                        else
                        {
                            Debug.Log($"[CDNDownloadManager]: Keeping recent temp file for resume: {Path.GetFileName(tempFile)}");
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning($"[CDNDownloadManager]: Failed to process temp file {tempFile}: {ex.Message}");
                    }
                }

                if (cleanedCount > 0)
                {
                    Debug.Log($"[CDNDownloadManager]: Cleaned up {cleanedCount} old temp file(s) from interrupted downloads");
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[CDNDownloadManager]: Error during temp file cleanup: {ex.Message}");
            }
        }

        /// <summary>
        /// Checks if network connectivity is available.
        /// Uses Android-specific API on Quest3, falls back to Unity's internetReachability on other platforms.
        /// </summary>
        /// <returns>True if network is available, false otherwise</returns>
        public bool IsNetworkAvailable()
        {
            #if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                AndroidJavaClass unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
                AndroidJavaObject currentActivity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");
                AndroidJavaObject connectivityManager = currentActivity.Call<AndroidJavaObject>("getSystemService", "connectivity");
                AndroidJavaObject networkInfo = connectivityManager.Call<AndroidJavaObject>("getActiveNetworkInfo");
                bool isConnected = networkInfo != null && networkInfo.Call<bool>("isConnected");
                return isConnected;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[CDNDownloadManager]: Failed to check Android network status: {ex.Message}");
                // Fallback to Unity's internetReachability
                return Application.internetReachability != NetworkReachability.NotReachable;
            }
            #else
            return Application.internetReachability != NetworkReachability.NotReachable;
            #endif
        }
        
        /// <summary>
        /// Ensures the CDNDownloadManager instance exists, creating it if needed.
        /// </summary>
        public static void EnsureInstanceExists()
        {
            if (Instance == null)
            {
                GameObject managerObj = new GameObject("CDNDownloadManager");
                Instance = managerObj.AddComponent<CDNDownloadManager>();
                Debug.Log("[CDNDownloadManager]: Auto-created instance (configure CDN URL in Inspector)");
            }
        }

        /// <summary>
        /// Ensures a file exists locally, downloading from CDN if needed.
        /// In Editor: downloads to test-videos folder for testing.
        /// On Quest3: downloads to Application.persistentDataPath.
        /// Checks CDN file size/date and replaces local file if CDN is newer.
        /// </summary>
        /// <param name="fileName">Relative file path (e.g., "Videos/intro.mp4")</param>
        /// <param name="onComplete">Callback when file is ready (path provided)</param>
        /// <param name="onError">Callback if download fails</param>
        /// <returns>True if file already exists and is up-to-date, false if download started</returns>
        public bool EnsureFileExists(string fileName, Action<string> onComplete = null, Action<string> onError = null)
        {
            // In Editor: use test-videos folder for testing
            // On Quest3: use persistentDataPath
            string localPath;
            #if UNITY_EDITOR
            string testVideosPath = Path.Combine(Application.dataPath, "..", "test-videos");
            localPath = Path.Combine(testVideosPath, fileName);
            localPath = Path.GetFullPath(localPath); // Normalize path
            #else
            localPath = Path.Combine(Application.persistentDataPath, fileName);
            #endif
            
            // Check if file already exists and compare with CDN
            if (File.Exists(localPath))
            {
                // Check if CDN has a newer version (compare file size and date)
                if (enableCDNDownloads)
                {
                    StartCoroutine(CheckAndUpdateFileIfNeeded(fileName, localPath, onComplete, onError));
                    return false; // Will download if needed
                }
                else
                {
                    Debug.Log($"[CDNDownloadManager] ✓ File EXISTS: {fileName} at {localPath}");
                    onComplete?.Invoke(localPath);
                    return true;
                }
            }
            
            Debug.LogWarning($"[CDNDownloadManager] ✗ File NOT FOUND: {fileName} at {localPath}");

            // Check if CDN downloads are disabled
            if (!enableCDNDownloads)
            {
                string error = $"File not found locally and CDN downloads are disabled: {fileName}";
                Debug.LogWarning($"[CDNDownloadManager]: {error}");
                Debug.LogError(Path.GetFileName(fileName) + error);
                onError?.Invoke(error);
                return false;
            }
            
            // Check network connectivity before attempting download
            if (!IsNetworkAvailable())
            {
                string error = $"File not found locally and no internet connection available: {fileName}";
                Debug.LogWarning($"[CDNDownloadManager]: {error}");
                Debug.LogError(
                    "No internet connection. Using local files only.\nSome content may be missing." +
                    $"Network unavailable - cannot download {fileName}"
                );
                onError?.Invoke(error);
                return false;
            }

            // Check if already downloading
            if (_downloadingFiles.Contains(fileName))
            {
                Debug.Log($"[CDNDownloadManager]: File already downloading: {fileName}");
                // Queue the callback to be called when download completes
                // Note: This is a simplified approach - you might want a more sophisticated callback system
                return false;
            }

            // Create download request - URL encode the file path to handle spaces and special characters
            string encodedFileName = fileName.Replace('\\', '/');
            // URL encode each path segment separately to preserve slashes
            string[] pathSegments = encodedFileName.Split('/');
            for (int i = 0; i < pathSegments.Length; i++)
            {
                pathSegments[i] = Uri.EscapeDataString(pathSegments[i]);
            }
            string encodedPath = string.Join("/", pathSegments);
            string cdnUrl = cdnBaseUrl.TrimEnd('/') + "/" + encodedPath;
            DownloadRequest request = new DownloadRequest
            {
                fileName = fileName,
                localPath = localPath,
                cdnUrl = cdnUrl,
                onComplete = onComplete,
                onError = onError
            };

            // Start download or queue it
            if (_activeDownloadCount < maxConcurrentDownloads)
            {
                StartDownload(request);
            }
            else
            {
                _downloadQueue.Enqueue(request);
            }

            return false;
        }

        /// <summary>
        /// Starts a download request.
        /// </summary>
        private void StartDownload(DownloadRequest request)
        {
            _activeDownloadCount++;
            _downloadingFiles.Add(request.fileName);
            
            Coroutine downloadCoroutine = StartCoroutine(DownloadFileCoroutine(request));
            _activeDownloads[request.fileName] = downloadCoroutine;
        }

        /// <summary>
        /// Checks if CDN file is newer than local file (by size or date) and downloads if needed.
        /// </summary>
        private IEnumerator CheckAndUpdateFileIfNeeded(string fileName, string localPath, Action<string> onComplete, Action<string> onError)
        {
            // Check network connectivity before checking CDN
            if (!IsNetworkAvailable())
            {
                Debug.LogWarning($"[CDNDownloadManager]: No network connection - using local file: {fileName}");
                Debug.LogError(
                    "No internet connection. Using local files only.\nSome content may be outdated." +
                    $"Network unavailable - cannot check CDN for {fileName}"
                );
                onComplete?.Invoke(localPath);
                yield break;
            }
            
            // URL encode the file path to handle spaces and special characters
            string encodedFileName = fileName.Replace('\\', '/');
            string[] pathSegments = encodedFileName.Split('/');
            for (int i = 0; i < pathSegments.Length; i++)
            {
                pathSegments[i] = Uri.EscapeDataString(pathSegments[i]);
            }
            string encodedPath = string.Join("/", pathSegments);
            string cdnUrl = cdnBaseUrl.TrimEnd('/') + "/" + encodedPath;
            
            // Get local file info
            FileInfo localFileInfo = new FileInfo(localPath);
            long localSize = localFileInfo.Exists ? localFileInfo.Length : 0;
            DateTime localDate = localFileInfo.Exists ? localFileInfo.LastWriteTimeUtc : DateTime.MinValue;

            // Check CDN file info using HEAD request (lighter than full download)
            using (UnityWebRequest headRequest = UnityWebRequest.Head(cdnUrl))
            {
                headRequest.timeout = 10; // Short timeout for HEAD request
                yield return headRequest.SendWebRequest();

                if (headRequest.result == UnityWebRequest.Result.Success)
                {
                    // Get CDN file size from Content-Length header
                    string contentLengthHeader = headRequest.GetResponseHeader("Content-Length");
                    long cdnSize = 0;
                    if (!string.IsNullOrEmpty(contentLengthHeader) && long.TryParse(contentLengthHeader, out cdnSize))
                    {
                        // Get CDN file date from Last-Modified header
                        string lastModifiedHeader = headRequest.GetResponseHeader("Last-Modified");
                        DateTime cdnDate = DateTime.MinValue;
                        if (!string.IsNullOrEmpty(lastModifiedHeader))
                        {
                            if (DateTime.TryParse(lastModifiedHeader, out DateTime parsedDate))
                            {
                                cdnDate = parsedDate.ToUniversalTime();
                            }
                        }

                        // Compare: Download if CDN size is different OR CDN date is newer
                        bool needsUpdate = (cdnSize != localSize) || (cdnDate > localDate && cdnDate != DateTime.MinValue);
                        
                        if (needsUpdate)
                        {
                            Debug.Log($"[CDNDownloadManager]: CDN file is newer/different - Local: {localSize} bytes, {localDate:yyyy-MM-dd HH:mm:ss} UTC | CDN: {cdnSize} bytes, {cdnDate:yyyy-MM-dd HH:mm:ss} UTC");
                            Debug.Log($"[CDNDownloadManager]: Replacing local file with CDN version: {fileName}");
                            
                            // Delete local file and download new version
                            try
                            {
                                File.Delete(localPath);
                            }
                            catch (Exception ex)
                            {
                                Debug.LogWarning($"[CDNDownloadManager]: Failed to delete old file: {ex.Message}");
                            }
                            
                            // Start download
                            DownloadRequest request = new DownloadRequest
                            {
                                fileName = fileName,
                                localPath = localPath,
                                cdnUrl = cdnUrl,
                                onComplete = onComplete,
                                onError = onError
                            };
                            
                            if (_activeDownloadCount < maxConcurrentDownloads)
                            {
                                StartDownload(request);
                            }
                            else
                            {
                                _downloadQueue.Enqueue(request);
                            }
                            yield break;
                        }
                        else
                        {
                            Debug.Log($"[CDNDownloadManager] ✓ File is up-to-date: {fileName} (Local: {localSize} bytes, CDN: {cdnSize} bytes)");
                            onComplete?.Invoke(localPath);
                            yield break;
                        }
                    }
                    else
                    {
                        // Content-Length header not available, assume file is up-to-date
                        Debug.Log($"[CDNDownloadManager]: CDN doesn't provide Content-Length, assuming local file is valid: {fileName}");
                        onComplete?.Invoke(localPath);
                        yield break;
                    }
                }
                else
                {
                    // HEAD request failed - check if it's a network error
                    bool isNetworkError = headRequest.result == UnityWebRequest.Result.ConnectionError;
                    if (isNetworkError)
                    {
                        Debug.LogWarning($"[CDNDownloadManager]: Network error checking CDN for {fileName}, using local file: {headRequest.error}");
                        Debug.LogError(
                            "Network error checking for updates. Using local files only.\nSome content may be outdated." +
                            $"Network error checking CDN: {headRequest.error}"
                        );
                    }
                    else
                    {
                        // HEAD request failed for other reasons (CDN might not support HEAD)
                        Debug.LogWarning($"[CDNDownloadManager]: HEAD request failed for {fileName}, using local file: {headRequest.error}");
                    }
                    onComplete?.Invoke(localPath);
                    yield break;
                }
            }
        }

        /// <summary>
        /// Downloads a file from CDN. Uses unscaled time to continue even when headset is off.
        /// Downloads to .temp file first, then renames to final file on success.
        /// </summary>
        private IEnumerator DownloadFileCoroutine(DownloadRequest request)
        {
            string fileName = request.fileName;
            string localPath = request.localPath;
            string cdnUrl = request.cdnUrl;
            
            // Check network connectivity before starting download
            if (!IsNetworkAvailable())
            {
                string error = $"No internet connection available for download: {fileName}";
                Debug.LogWarning($"[CDNDownloadManager]: {error}");
                Debug.LogError(
                    "No internet connection. Using local files only.\nSome content may be missing." +
                    $"Network unavailable - cannot download {fileName}"
                );
                
                _downloadingFiles.Remove(fileName);
                _activeDownloads.Remove(fileName);
                _activeDownloadCount--;
                
                request.onError?.Invoke(error);
                ProcessNextInQueue();
                yield break;
            }
            
            // Use .temp file for download to prevent corruption if app shuts down
            string tempPath = localPath + ".temp";

            Debug.Log($"[CDNDownloadManager]: Starting download: {cdnUrl} -> {tempPath}");

            // Create directory if needed
            string directory = Path.GetDirectoryName(localPath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            // Check if we have a partial download (.temp file) to resume from
            long resumeFrom = 0;
            bool hasPartialDownload = File.Exists(tempPath);
            if (hasPartialDownload)
            {
                try
                {
                    FileInfo tempFileInfo = new FileInfo(tempPath);
                    resumeFrom = tempFileInfo.Length;
                    Debug.Log($"[CDNDownloadManager]: Resuming download from byte {resumeFrom:N0} (partial file exists: {tempPath})");
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[CDNDownloadManager]: Failed to get temp file size, starting fresh: {ex.Message}");
                    resumeFrom = 0;
                    hasPartialDownload = false;
                }
            }

            // Create UnityWebRequest
            UnityWebRequest www;
            if (hasPartialDownload && resumeFrom > 0)
            {
                // Use Range request to resume download
                www = UnityWebRequest.Get(cdnUrl);
                www.SetRequestHeader("Range", $"bytes={resumeFrom}-");
                Debug.Log($"[CDNDownloadManager]: Resuming download with Range header: bytes={resumeFrom}-");
            }
            else
            {
                // Start fresh download
                www = UnityWebRequest.Get(cdnUrl);
            }
            
            using (www)
            {
                // Set timeout (very long for slow connections)
                www.timeout = downloadTimeout;
                
                // Send request
                var operation = www.SendWebRequest();

                // Track progress using unscaled time (continues even when headset is off)
                float lastProgressUpdate = 0f;
                while (!operation.isDone)
                {
                    // Update progress (throttle updates to avoid spam)
                    float progress = operation.progress;
                    if (Mathf.Abs(progress - lastProgressUpdate) > 0.01f || progress >= 1f)
                    {
                        OnDownloadProgress?.Invoke(fileName, progress);
                        lastProgressUpdate = progress;
                    }

                    // Use unscaled time to continue even when game is paused
                    yield return null; // This continues even during pause
                }

                // Check for errors
                if (www.result != UnityWebRequest.Result.Success)
                {
                    bool isTimeout = www.error != null && (www.error.Contains("timeout") || www.error.Contains("Timeout"));
                    bool isNetworkError = www.result == UnityWebRequest.Result.ConnectionError;
                    bool canRetry = request.retryCount < maxRetries && (isTimeout || isNetworkError);
                    
                    string error = isTimeout
                        ? $"Download timeout after {downloadTimeout}s (slow connection - will retry)"
                        : $"Download failed: {www.error} (Status: {www.responseCode})";
                    
                    Debug.LogWarning($"[CDNDownloadManager] ⚠ Download FAILED (attempt {request.retryCount + 1}/{maxRetries + 1}): {fileName} - {error}");
                    
                    // Don't delete temp file if we can retry (keep partial download for resume)
                    if (!canRetry)
                    {
                        // Clean up temp file on final failure
                        try
                        {
                            if (File.Exists(tempPath))
                            {
                                File.Delete(tempPath);
                            }
                        }
                        catch (Exception ex)
                        {
                            Debug.LogWarning($"[CDNDownloadManager]: Failed to clean up temp file after error: {ex.Message}");
                        }
                        
                        _downloadingFiles.Remove(fileName);
                        _activeDownloads.Remove(fileName);
                        _activeDownloadCount--;
                        
                        // Show user-friendly error message
                        string userMessage = $"Download failed: {Path.GetFileName(fileName)}\n{error}";
                        Debug.LogError(Path.GetFileName(fileName) + error);
                        
                        OnDownloadError?.Invoke(fileName, error);
                        request.onError?.Invoke(error);
                        
                        // Process next in queue
                        ProcessNextInQueue();
                        yield break;
                    }
                    else
                    {
                        // Retry with exponential backoff
                        request.retryCount++;
                        float retryDelay = retryDelayBase * Mathf.Pow(2f, request.retryCount - 1); // Exponential backoff: 2s, 4s, 8s
                        Debug.Log($"[CDNDownloadManager]: Retrying download in {retryDelay:F1} seconds... (attempt {request.retryCount + 1}/{maxRetries + 1}): {fileName}");
                        
                        // Show retry notification to user (non-blocking)
                        if (request.retryCount == 1)
                        {
                            // Only show on first retry to avoid spam
                            Debug.LogError(
                                $"Download failed, retrying: {Path.GetFileName(fileName)}" +
                                $"Download retry attempt {request.retryCount + 1}/{maxRetries + 1}: {error}"
                            );
                        }
                        
                        // Keep temp file for resume - don't remove from downloading state
                        // Wait before retry
                        yield return new WaitForSeconds(retryDelay);
                        
                        // Update the coroutine reference and restart download (will resume from temp file)
                        _activeDownloads[fileName] = StartCoroutine(DownloadFileCoroutine(request));
                        yield break;
                    }
                }

                // Check if server supports Range requests (206 = Partial Content)
                bool isPartialContent = www.responseCode == 206;
                bool isFullDownload = www.responseCode == 200;
                
                if (!isPartialContent && !isFullDownload)
                {
                    string error = $"Unexpected response code: {www.responseCode}";
                    Debug.LogError($"[CDNDownloadManager] ✗ Download FAILED: {fileName} - {error}");
                    
                    // Clean up temp file
                    try
                    {
                        if (File.Exists(tempPath))
                        {
                            File.Delete(tempPath);
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning($"[CDNDownloadManager]: Failed to clean up temp file: {ex.Message}");
                    }
                    
                    _downloadingFiles.Remove(fileName);
                    _activeDownloads.Remove(fileName);
                    _activeDownloadCount--;
                    
                    // Show user-friendly error message
                    Debug.LogError(Path.GetFileName(fileName) + error);
                    
                    OnDownloadError?.Invoke(fileName, error);
                    request.onError?.Invoke(error);
                    
                    ProcessNextInQueue();
                    yield break;
                }

                // Save to temp file (append if resuming, overwrite if fresh)
                try
                {
                    byte[] downloadedData = www.downloadHandler.data;
                    
                    if (hasPartialDownload && isPartialContent && resumeFrom > 0)
                    {
                        // Server supports Range requests and sent partial content - append to existing temp file
                        using (FileStream fs = new FileStream(tempPath, FileMode.Append, FileAccess.Write))
                        {
                            fs.Write(downloadedData, 0, downloadedData.Length);
                        }
                        FileInfo finalInfo = new FileInfo(tempPath);
                        Debug.Log($"[CDNDownloadManager]: Resumed download - appended {downloadedData.Length:N0} bytes, total: {finalInfo.Length:N0} bytes");
                    }
                    else
                    {
                        // Write fresh file (either no partial download, or server doesn't support Range and sent full file)
                        // If we had a partial download but server doesn't support Range, delete old temp first
                        if (hasPartialDownload && !isPartialContent)
                        {
                            Debug.Log($"[CDNDownloadManager]: Server doesn't support Range requests, restarting download from beginning");
                            try
                            {
                                if (File.Exists(tempPath))
                                {
                                    File.Delete(tempPath);
                                }
                            }
                            catch (Exception ex)
                            {
                                Debug.LogWarning($"[CDNDownloadManager]: Failed to delete old temp file: {ex.Message}");
                            }
                        }
                        
                        File.WriteAllBytes(tempPath, downloadedData);
                        Debug.Log($"[CDNDownloadManager]: Downloaded to temp file: {tempPath} ({downloadedData.Length:N0} bytes)");
                    }
                    
                    // Check if download is complete (for Range requests, we need to verify total size)
                    // For now, if we got 200 or the Range request completed, assume it's done
                    // In a more sophisticated implementation, we'd check Content-Range header
                    
                    // Delete old file if it exists
                    if (File.Exists(localPath))
                    {
                        File.Delete(localPath);
                    }
                    
                    // Rename temp file to final file (atomic operation)
                    File.Move(tempPath, localPath);
                    FileInfo finalFileInfo = new FileInfo(localPath);
                    Debug.Log($"[CDNDownloadManager] ✓ Download SUCCESS: {fileName} ({finalFileInfo.Length:N0} bytes)");
                }
                catch (Exception ex)
                {
                    string error = $"Failed to save file: {ex.Message}";
                    Debug.LogError($"[CDNDownloadManager] ✗ Save FAILED: {fileName} - {error}");
                    
                    // Clean up temp file on error
                    try
                    {
                        if (File.Exists(tempPath))
                        {
                            File.Delete(tempPath);
                        }
                    }
                    catch (Exception cleanupEx)
                    {
                        Debug.LogWarning($"[CDNDownloadManager]: Failed to clean up temp file: {cleanupEx.Message}");
                    }
                    
                    _downloadingFiles.Remove(fileName);
                    _activeDownloads.Remove(fileName);
                    _activeDownloadCount--;
                    
                    // Show user-friendly error message
                    Debug.LogError(Path.GetFileName(fileName) + error);
                    
                    OnDownloadError?.Invoke(fileName, error);
                    request.onError?.Invoke(error);
                    
                    ProcessNextInQueue();
                    yield break;
                }
            }

            // Success
            _downloadingFiles.Remove(fileName);
            _activeDownloads.Remove(fileName);
            _activeDownloadCount--;
            
            OnDownloadComplete?.Invoke(fileName);
            OnDownloadProgress?.Invoke(fileName, 1f);
            request.onComplete?.Invoke(localPath);

            // Process next in queue
            ProcessNextInQueue();
        }

        /// <summary>
        /// Processes the next download in the queue.
        /// </summary>
        private void ProcessNextInQueue()
        {
            if (_downloadQueue.Count > 0 && _activeDownloadCount < maxConcurrentDownloads)
            {
                DownloadRequest nextRequest = _downloadQueue.Dequeue();
                StartDownload(nextRequest);
            }
        }

        /// <summary>
        /// Ensures multiple files exist, downloading from CDN if needed.
        /// </summary>
        /// <param name="fileNames">List of file paths to check/download</param>
        /// <param name="onAllComplete">Callback when all files are ready</param>
        /// <param name="onError">Callback if any download fails</param>
        public void EnsureFilesExist(List<string> fileNames, Action onAllComplete = null, Action<string> onError = null)
        {
            StartCoroutine(EnsureFilesExistCoroutine(fileNames, onAllComplete, onError));
        }

        private IEnumerator EnsureFilesExistCoroutine(List<string> fileNames, Action onAllComplete, Action<string> onError)
        {
            int total = fileNames.Count;
            if (total == 0)
            {
                onAllComplete?.Invoke();
                yield break;
            }

            // Track completion status per file to avoid race conditions
            Dictionary<string, bool> fileCompletionStatus = new Dictionary<string, bool>();
            foreach (string fileName in fileNames)
            {
                fileCompletionStatus[fileName] = false;
            }

            bool hasError = false;
            string errorMessage = null;
            int completedCount = 0;

            // Start all downloads
            foreach (string fileName in fileNames)
            {
                if (hasError) break;

                bool fileExists = EnsureFileExists(
                    fileName,
                    onComplete: (path) =>
                    {
                        if (!fileCompletionStatus.ContainsKey(fileName))
                            return; // File was removed from tracking
                        
                        if (!fileCompletionStatus[fileName])
                        {
                            fileCompletionStatus[fileName] = true;
                            completedCount++;
                            OnBatchProgress?.Invoke(completedCount, total);
                        }
                    },
                    onError: (error) =>
                    {
                        if (!hasError)
                        {
                            hasError = true;
                            errorMessage = error;
                            onError?.Invoke(error);
                        }
                    }
                );

                // If file already exists, mark as complete immediately
                if (fileExists)
                {
                    if (!fileCompletionStatus[fileName])
                    {
                        fileCompletionStatus[fileName] = true;
                        completedCount++;
                        OnBatchProgress?.Invoke(completedCount, total);
                    }
                }

                // Small delay to avoid overwhelming the system
                yield return new WaitForSeconds(0.1f);
            }

            // Wait for all downloads to complete
            while (completedCount < total && !hasError)
            {
                yield return new WaitForSeconds(0.5f);
            }

            if (!hasError && completedCount == total)
            {
                Debug.Log($"[CDNDownloadManager] ✓ All {total} files are ready");
                onAllComplete?.Invoke();
            }
            else if (hasError)
            {
                Debug.LogError($"[CDNDownloadManager] ✗ Batch download failed: {errorMessage}");
                
                // Show user-friendly error message for batch failures
                int failedCount = total - completedCount;
                string batchErrorMessage = $"{failedCount} file(s) failed to download. The experience may be incomplete.";
                Debug.LogError(
                    batchErrorMessage +
                    $"Batch download error: {errorMessage}"
                );
            }
        }

        /// <summary>
        /// Cancels a download if in progress.
        /// </summary>
        public void CancelDownload(string fileName)
        {
            if (_activeDownloads.TryGetValue(fileName, out Coroutine coroutine))
            {
                StopCoroutine(coroutine);
                _activeDownloads.Remove(fileName);
                _downloadingFiles.Remove(fileName);
                _activeDownloadCount--;
                ProcessNextInQueue();
            }
        }

        /// <summary>
        /// Gets download progress for a file (0-1, or -1 if not downloading).
        /// </summary>
        public float GetDownloadProgress(string fileName)
        {
            // This is a simplified version - you'd need to track progress per file
            if (_downloadingFiles.Contains(fileName))
            {
                return 0.5f; // Placeholder - would need to track actual progress
            }
            return -1f; // Not downloading
        }

        /// <summary>
        /// Checks if a file is currently being downloaded.
        /// </summary>
        public bool IsDownloading(string fileName)
        {
            return _downloadingFiles.Contains(fileName);
        }
    }

}