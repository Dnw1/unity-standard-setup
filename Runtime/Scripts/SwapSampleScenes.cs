using UnityEngine;
using UnityEngine.SceneManagement;

namespace com.dnw.standardpackage
{
    public class SwapSampleScenes : MonoBehaviour
    {
        
        public void SwapScene(string sceneName)
        {
            Debug.Log($"[SwapSampleScenes]: Swapping to scene {sceneName}");
            SceneManager.LoadSceneAsync(sceneName);
        }

    }
}
