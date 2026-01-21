using System.Collections;
using UnityEngine;

namespace com.dnw.standardpackage
{

    public class VideoSkipCall : MonoBehaviour
    {
        private VideoManager _videoManager;
        // public bool delay;
        private bool _armed;
        public static VideoSkipCall Instance;

        private void Start() {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);   // Kill the duplicate
                return;
            }

            Instance = this;
            DontDestroyOnLoad(gameObject);
            _videoManager = FindFirstObjectByType<VideoManager>();
            gameObject.SetActive(false);
        }

        public void ResetState() {
            _armed = false;
            StopAllCoroutines();
        }

        public void Arm() {
            _armed = true;
        }

        public void Disarm() {
            _armed = false;
        }

        public void FindAndCall() {
            if (!_armed) {
                return;
            }
            _armed = false;

            _videoManager?.SkipVideo();
        }

        // private void Start() {
        //     // Make this button persist across scenes
        //     DontDestroyOnLoad(gameObject);
            
        //     // Find the VideoManager (which is also DontDestroyOnLoad)
        //     GameObject videoManagerObj = GameObject.Find("VideoManager");
        //     if (videoManagerObj != null) {
        //         _videoManager = videoManagerObj.GetComponent<VideoManager>();
        //     }
            
        //     // Hide initially
        //     gameObject.SetActive(false);
        // }
        
        // public void FindAndCall()  {
        //     if (delay) return;
        //     delay = true;

        //     if(_videoManager != null) {
        //         _videoManager.SkipVideo();
        //     } else {
        //         Debug.Log("VideoSkipCall]: Fallback function");
        //         GameObject.Find("VideoManager").GetComponent<VideoManager>().SkipVideo();
        //     }
        // }

    }
}