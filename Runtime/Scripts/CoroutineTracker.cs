using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Tracks and manages coroutines to prevent memory leaks and ensure proper cleanup.
/// Provides named coroutine tracking so coroutines can be stopped by name.
/// </summary>
public class CoroutineTracker : MonoBehaviour
{
    private Dictionary<string, Coroutine> _activeCoroutines = new Dictionary<string, Coroutine>();

    /// <summary>
    /// Starts a coroutine with a unique name. If a coroutine with the same name is already running,
    /// it will be stopped before starting the new one.
    /// </summary>
    /// <param name="name">Unique name for this coroutine (used for tracking and stopping)</param>
    /// <param name="routine">The coroutine to start</param>
    /// <returns>The started coroutine, or null if routine is null</returns>
    public Coroutine StartTrackedCoroutine(string name, IEnumerator routine)
    {
        if (routine == null)
        {
            Debug.LogWarning($"[CoroutineTracker] Cannot start coroutine '{name}': routine is null");
            return null;
        }

        if (string.IsNullOrEmpty(name))
        {
            Debug.LogWarning("[CoroutineTracker] Cannot start coroutine with empty name");
            return null;
        }

        // Stop existing coroutine with same name if it exists
        if (_activeCoroutines.TryGetValue(name, out var existing))
        {
            if (existing != null)
            {
                StopCoroutine(existing);
                Debug.Log($"[CoroutineTracker] Stopped existing coroutine: {name}");
            }
            _activeCoroutines.Remove(name);
        }

        // Start new coroutine
        var coroutine = StartCoroutine(routine);
        _activeCoroutines[name] = coroutine;
        Debug.Log($"[CoroutineTracker] Started tracked coroutine: {name}");
        
        return coroutine;
    }

    /// <summary>
    /// Stops a tracked coroutine by name.
    /// </summary>
    /// <param name="name">Name of the coroutine to stop</param>
    public void StopTrackedCoroutine(string name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return;
        }

        if (_activeCoroutines.TryGetValue(name, out var coroutine))
        {
            if (coroutine != null)
            {
                StopCoroutine(coroutine);
                Debug.Log($"[CoroutineTracker] Stopped tracked coroutine: {name}");
            }
            _activeCoroutines.Remove(name);
        }
    }

    /// <summary>
    /// Stops all tracked coroutines and clears the tracking dictionary.
    /// </summary>
    public void StopAllTrackedCoroutines()
    {
        foreach (var kvp in _activeCoroutines)
        {
            if (kvp.Value != null)
            {
                StopCoroutine(kvp.Value);
            }
        }
        _activeCoroutines.Clear();
        Debug.Log("[CoroutineTracker] Stopped all tracked coroutines");
    }

    /// <summary>
    /// Checks if a coroutine with the given name is currently running.
    /// </summary>
    /// <param name="name">Name of the coroutine to check</param>
    /// <returns>True if the coroutine is tracked and running, false otherwise</returns>
    public bool IsCoroutineRunning(string name)
    {
        return _activeCoroutines.ContainsKey(name) && _activeCoroutines[name] != null;
    }

    /// <summary>
    /// Gets the number of currently tracked coroutines.
    /// </summary>
    /// <returns>Number of active tracked coroutines</returns>
    public int GetActiveCoroutineCount()
    {
        return _activeCoroutines.Count;
    }

    /// <summary>
    /// Cleans up all tracked coroutines when the component is destroyed.
    /// </summary>
    private void OnDestroy()
    {
        StopAllTrackedCoroutines();
    }
}

