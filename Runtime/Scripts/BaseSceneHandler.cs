using System.Collections;
using UnityEngine;

namespace com.dnw.standardpackage
{

    /// <summary>
    /// Abstract base class for scene-specific handlers.
    /// Each scene (Word Shooter, Quiz, Beat Saber, Diploma) has its own handler
    /// that implements the scene-specific logic.
    /// </summary>
    public abstract class BaseSceneHandler : MonoBehaviour
    {
        /// <summary>
        /// Reference to the main SceneHandler for accessing shared resources.
        /// </summary>
        protected SceneHandler sceneHandler;
        
        /// <summary>
        /// Tracks if ExecuteScene is currently running to prevent duplicate execution.
        /// </summary>
        private bool _isExecuting = false;

        /// <summary>
        /// Initializes the scene handler with a reference to the main SceneHandler.
        /// </summary>
        public virtual void Initialize(SceneHandler handler)
        {
            sceneHandler = handler;
        }

        /// <summary>
        /// Executes the scene-specific logic. Each scene handler implements this
        /// to define the sequence of events for that scene.
        /// </summary>
        public abstract IEnumerator ExecuteScene();
        
        /// <summary>
        /// Safely executes the scene, preventing duplicate execution.
        /// </summary>
        public IEnumerator ExecuteSceneSafely()
        {
            if (_isExecuting)
            {
                Debug.LogWarning($"[{GetType().Name}]: ExecuteScene already running, skipping duplicate call");
                yield break;
            }
            
            _isExecuting = true;
            try
            {
                yield return StartCoroutine(ExecuteScene());
            }
            finally
            {
                _isExecuting = false;
            }
        }

        /// <summary>
        /// Resets the scene to its initial state. Called when the experience restarts.
        /// </summary>
        public virtual void ResetScene()
        {
            // Default implementation does nothing
            // Override in derived classes if needed
        }
    }
}