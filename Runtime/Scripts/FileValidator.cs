using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace com.dnw.standardpackage
{

    /// <summary>
    /// Utility class for validating file existence and structure before use.
    /// Provides comprehensive file validation to prevent crashes from missing files.
    /// </summary>
    public static class FileValidator
    {
        /// <summary>
        /// Validates that a file exists at the given path.
        /// Shows user-friendly error message if file is missing.
        /// </summary>
        /// <param name="filePath">Path to the file to validate (can be relative to persistentDataPath or absolute)</param>
        /// <param name="fileType">Type of file (e.g., "Video", "Audio", "JSON") for error messages</param>
        /// <param name="fileName">Display name of the file (for user-friendly messages)</param>
        /// <param name="showError">Whether to show error message to user (default: true)</param>
        /// <returns>True if file exists, false otherwise</returns>
        public static bool ValidateFile(string filePath, string fileType, string fileName = null, bool showError = true)
        {
            if (string.IsNullOrEmpty(filePath))
            {
                if (showError)
                {
                    string displayName = fileName ?? "Unknown";
                    Debug.LogError(
                        $"{fileType} file path is empty for '{displayName}'. Please check your configuration." +
                        $"{fileType} file path is null or empty"
                    );
                }
                return false;
            }
            
            // Resolve full path (handle both relative and absolute paths)
            string fullPath = filePath;
            if (!Path.IsPathRooted(filePath))
            {
                #if UNITY_EDITOR
                // In Editor: Check test-videos folder first (matches GetVideoPath behavior)
                string testVideosPath = Path.Combine(Application.dataPath, "..", "test-videos", filePath);
                testVideosPath = Path.GetFullPath(testVideosPath); // Normalize path
                
                if (File.Exists(testVideosPath))
                {
                    return true; // File found in test-videos
                }
                #endif
                
                // Fall back to persistentDataPath (or use it directly on Quest3)
                fullPath = Path.Combine(Application.persistentDataPath, filePath);
            }
            
            if (!File.Exists(fullPath))
            {
                if (showError)
                {
                    string displayName = fileName ?? Path.GetFileName(filePath) ?? "Unknown";
                    Debug.LogError(
                        $"{fileType} file '{displayName}' not found. Please check your files." +
                        $"{fileType} file not found: {fullPath}"
                    );
                }
                return false;
            }
            
            return true;
        }
        
        /// <summary>
        /// Validates all required files from a ConfigData object.
        /// Checks all videos and audio files referenced in the configuration.
        /// </summary>
        /// <param name="config">Configuration data to validate</param>
        /// <param name="showErrors">Whether to show error messages for missing files (default: true)</param>
        /// <returns>Coroutine that validates all files</returns>
        public static IEnumerator ValidateRequiredFiles(ConfigData config, bool showErrors = true)
        {
            if (config == null)
            {
                if (showErrors)
                {
                    Debug.LogError(
                        "Configuration data is missing. Cannot validate files." +
                        "ConfigData is null"
                    );
                }
                yield break;
            }
            
            List<string> missingFiles = new List<string>();
            
            // Check all videos
            if (config.videos != null)
            {
                foreach (var video in config.videos)
                {
                    if (video == null || string.IsNullOrEmpty(video.subFolder))
                    {
                        continue; // Skip invalid entries
                    }
                    
                    string videoPath = video.subFolder;
                    if (!Path.IsPathRooted(videoPath))
                    {
                        videoPath = Path.Combine(Application.persistentDataPath, videoPath);
                    }
                    
                    if (!File.Exists(videoPath))
                    {
                        string displayName = !string.IsNullOrEmpty(video.name) ? video.name : Path.GetFileName(video.subFolder);
                        missingFiles.Add($"Video: {displayName} ({video.subFolder})");
                    }
                }
            }
            
            // Check all audio files
            if (config.audios != null)
            {
                foreach (var audio in config.audios)
                {
                    if (audio == null || string.IsNullOrEmpty(audio.subFolder))
                    {
                        continue; // Skip invalid entries
                    }
                    
                    string audioPath = audio.subFolder;
                    if (!Path.IsPathRooted(audioPath))
                    {
                        audioPath = Path.Combine(Application.persistentDataPath, audioPath);
                    }
                    
                    if (!File.Exists(audioPath))
                    {
                        string displayName = !string.IsNullOrEmpty(audio.name) ? audio.name : Path.GetFileName(audio.subFolder);
                        missingFiles.Add($"Audio: {displayName} ({audio.subFolder})");
                    }
                }
            }
            
            // Report missing files
            if (missingFiles.Count > 0 && showErrors)
            {
                string message = $"Missing {missingFiles.Count} file(s) detected:\n";
                message += string.Join("\n", missingFiles.Take(5)); // Show first 5
                if (missingFiles.Count > 5)
                {
                    message += $"\n... and {missingFiles.Count - 5} more";
                }
                message += "\n\nThe experience may be incomplete.";
                
                Debug.LogError(
                    message +
                    $"Missing {missingFiles.Count} files from configuration"
                );
            }
            
            yield return null;
        }
        
        /// <summary>
        /// Validates that a JSON configuration file exists and can be read.
        /// </summary>
        /// <param name="jsonFilePath">Path to the JSON file</param>
        /// <param name="showError">Whether to show error message if file is missing</param>
        /// <returns>True if file exists, false otherwise</returns>
        public static bool ValidateJsonFile(string jsonFilePath, bool showError = true)
        {
            return ValidateFile(jsonFilePath, "JSON", "LocalConfig.json", showError);
        }
    }
}
