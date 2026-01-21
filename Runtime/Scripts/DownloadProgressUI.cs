using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace com.dnw.standardpackage
{

    /// <summary>
    /// UI component to display download progress to the user.
    /// Shows current file being downloaded and overall progress.
    /// </summary>
    public class DownloadProgressUI : MonoBehaviour
    {
        [Header("UI References")]
        [SerializeField] private GameObject downloadPanel;
        [SerializeField] private TextMeshProUGUI statusText;
        [SerializeField] private TextMeshProUGUI progressText;
        [SerializeField] private Slider progressBar;
        [SerializeField] private TextMeshProUGUI fileCountText;

        [Header("Settings")]
        [Tooltip("Show download UI when downloads are active")]
        [SerializeField] private bool showOnDownload = true;
        
        [Tooltip("Hide download UI when all downloads complete")]
        [SerializeField] private bool hideOnComplete = true;

        private CDNDownloadManager _downloadManager;
        private string _currentDownloadingFile = "";
        private float _currentProgress = 0f;
        private int _totalFiles = 0;
        private int _completedFiles = 0;

        private void Start()
        {
            CDNDownloadManager.EnsureInstanceExists();
            _downloadManager = CDNDownloadManager.Instance;

            // Auto-create UI if not assigned
            if (downloadPanel == null)
            {
                CreateDownloadUI();
            }

            // Subscribe to download events
            _downloadManager.OnDownloadProgress += OnDownloadProgress;
            _downloadManager.OnDownloadComplete += OnDownloadComplete;
            _downloadManager.OnBatchProgress += OnBatchProgress;

            // Hide panel initially
            if (downloadPanel != null)
            {
                downloadPanel.SetActive(false);
            }
        }

        /// <summary>
        /// Auto-creates the download progress UI if not manually set up.
        /// </summary>
        private void CreateDownloadUI()
        {
            // Find or create Canvas
            Canvas canvas = FindFirstObjectByType<Canvas>();
            if (canvas == null)
            {
                GameObject canvasObj = new GameObject("DownloadCanvas");
                canvas = canvasObj.AddComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvasObj.AddComponent<CanvasScaler>();
                canvasObj.AddComponent<GraphicRaycaster>();
            }

            // Create Download Panel
            GameObject panelObj = new GameObject("DownloadPanel");
            panelObj.transform.SetParent(canvas.transform, false);
            downloadPanel = panelObj;
            
            RectTransform panelRect = panelObj.AddComponent<RectTransform>();
            panelRect.anchorMin = new Vector2(0.5f, 0.5f);
            panelRect.anchorMax = new Vector2(0.5f, 0.5f);
            panelRect.sizeDelta = new Vector2(600, 200);
            panelRect.anchoredPosition = Vector2.zero;

            // Add background image
            UnityEngine.UI.Image bgImage = panelObj.AddComponent<UnityEngine.UI.Image>();
            bgImage.color = new Color(0, 0, 0, 0.8f);

            // Create Status Text
            GameObject statusObj = new GameObject("StatusText");
            statusObj.transform.SetParent(panelObj.transform, false);
            statusText = statusObj.AddComponent<TextMeshProUGUI>();
            RectTransform statusRect = statusText.rectTransform;
            statusRect.anchorMin = new Vector2(0, 0.7f);
            statusRect.anchorMax = new Vector2(1, 1);
            statusRect.offsetMin = new Vector2(20, 0);
            statusRect.offsetMax = new Vector2(-20, -10);
            statusText.text = "Downloading...";
            statusText.fontSize = 24;
            statusText.alignment = TextAlignmentOptions.Center;

            // Create Progress Bar
            GameObject progressBarObj = new GameObject("ProgressBar");
            progressBarObj.transform.SetParent(panelObj.transform, false);
            progressBar = progressBarObj.AddComponent<Slider>();
            RectTransform barRect = progressBarObj.GetComponent<RectTransform>();
            barRect.anchorMin = new Vector2(0.1f, 0.4f);
            barRect.anchorMax = new Vector2(0.9f, 0.6f);
            barRect.offsetMin = Vector2.zero;
            barRect.offsetMax = Vector2.zero;
            progressBar.minValue = 0;
            progressBar.maxValue = 1;
            progressBar.value = 0;

            // Add fill area and fill for progress bar
            GameObject fillArea = new GameObject("Fill Area");
            fillArea.transform.SetParent(progressBarObj.transform, false);
            RectTransform fillAreaRect = fillArea.AddComponent<RectTransform>();
            fillAreaRect.anchorMin = Vector2.zero;
            fillAreaRect.anchorMax = Vector2.one;
            fillAreaRect.offsetMin = new Vector2(10, 0);
            fillAreaRect.offsetMax = new Vector2(-10, 0);

            GameObject fill = new GameObject("Fill");
            fill.transform.SetParent(fillArea.transform, false);
            RectTransform fillRect = fill.AddComponent<RectTransform>();
            fillRect.anchorMin = Vector2.zero;
            fillRect.anchorMax = new Vector2(0, 1);
            fillRect.sizeDelta = Vector2.zero;
            UnityEngine.UI.Image fillImage = fill.AddComponent<UnityEngine.UI.Image>();
            fillImage.color = new Color(0.2f, 0.6f, 1f); // Blue
            progressBar.fillRect = fillRect;

            // Create Progress Text (percentage)
            GameObject progressTextObj = new GameObject("ProgressText");
            progressTextObj.transform.SetParent(panelObj.transform, false);
            progressText = progressTextObj.AddComponent<TextMeshProUGUI>();
            RectTransform progressTextRect = progressText.rectTransform;
            progressTextRect.anchorMin = new Vector2(0.1f, 0.1f);
            progressTextRect.anchorMax = new Vector2(0.5f, 0.35f);
            progressTextRect.offsetMin = Vector2.zero;
            progressTextRect.offsetMax = Vector2.zero;
            progressText.text = "0%";
            progressText.fontSize = 20;
            progressText.alignment = TextAlignmentOptions.Left;

            // Create File Count Text
            GameObject fileCountObj = new GameObject("FileCountText");
            fileCountObj.transform.SetParent(panelObj.transform, false);
            fileCountText = fileCountObj.AddComponent<TextMeshProUGUI>();
            RectTransform fileCountRect = fileCountText.rectTransform;
            fileCountRect.anchorMin = new Vector2(0.5f, 0.1f);
            fileCountRect.anchorMax = new Vector2(0.9f, 0.35f);
            fileCountRect.offsetMin = Vector2.zero;
            fileCountRect.offsetMax = Vector2.zero;
            fileCountText.text = "";
            fileCountText.fontSize = 18;
            fileCountText.alignment = TextAlignmentOptions.Right;

            Debug.Log("[DownloadProgressUI]: Auto-created download progress UI");
        }

        private void OnDestroy()
        {
            if (_downloadManager != null)
            {
                _downloadManager.OnDownloadProgress -= OnDownloadProgress;
                _downloadManager.OnDownloadComplete -= OnDownloadComplete;
                _downloadManager.OnBatchProgress -= OnBatchProgress;
            }
        }

        private void OnDownloadProgress(string fileName, float progress)
        {
            _currentDownloadingFile = fileName;
            _currentProgress = progress;
            UpdateUI();
        }

        private void OnDownloadComplete(string fileName)
        {
            _completedFiles++;
            UpdateUI();
        }

        private void OnBatchProgress(int downloaded, int total)
        {
            _completedFiles = downloaded;
            _totalFiles = total;
            UpdateUI();
        }

        private void UpdateUI()
        {
            // Auto-create UI if still null
            if (downloadPanel == null)
            {
                CreateDownloadUI();
            }
            
            if (downloadPanel == null) return;

            // Show/hide panel - show if downloading OR if we have progress to show
            bool isDownloading = _downloadManager != null && !string.IsNullOrEmpty(_currentDownloadingFile);
            bool shouldShow = showOnDownload && (isDownloading || _currentProgress > 0f);
            
            if (downloadPanel.activeSelf != shouldShow)
            {
                downloadPanel.SetActive(shouldShow);
            }

            if (!shouldShow) return;

            // Update all UI elements
            UpdateProgressDisplay();

            // Hide if complete
            if (hideOnComplete && _completedFiles >= _totalFiles && _totalFiles > 0)
            {
                // Wait a moment before hiding
                StartCoroutine(HideAfterDelay(2f));
            }
        }

        private void UpdateProgressDisplay()
        {
            // Update status text with current file name
            if (statusText != null)
            {
                if (!string.IsNullOrEmpty(_currentDownloadingFile))
                {
                    // Show just the filename (not full path) for cleaner display
                    string fileName = Path.GetFileName(_currentDownloadingFile);
                    statusText.text = $"Downloading: {fileName}";
                }
                else
                {
                    statusText.text = "Preparing download...";
                }
            }

            // Update progress bar
            if (progressBar != null)
            {
                progressBar.value = _currentProgress;
            }

            // Update progress text (percentage)
            if (progressText != null)
            {
                progressText.text = $"{(_currentProgress * 100f):F0}%";
            }

            // Update file count text
            if (fileCountText != null && _totalFiles > 0)
            {
                fileCountText.text = $"{_completedFiles}/{_totalFiles} files";
            }
            else if (fileCountText != null)
            {
                fileCountText.text = "";
            }
        }

        private System.Collections.IEnumerator HideAfterDelay(float delay)
        {
            yield return new WaitForSeconds(delay);
            if (downloadPanel != null)
            {
                downloadPanel.SetActive(false);
            }
        }

        /// <summary>
        /// Manually show the download UI.
        /// </summary>
        public void Show()
        {
            if (downloadPanel != null)
            {
                downloadPanel.SetActive(true);
            }
        }

        /// <summary>
        /// Manually hide the download UI.
        /// </summary>
        public void Hide()
        {
            if (downloadPanel != null)
            {
                downloadPanel.SetActive(false);
            }
        }
    }
}
