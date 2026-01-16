using System.Collections;
using UnityEngine;
// using NativeWebSocket;
using Meta.Net.NativeWebSocket;
using UnityEngine.SceneManagement;
using System;
using System.Dynamic;
using UnityEngine.Video;
using System.Text;
using System.Collections.Generic;
using OVRSimpleJSON;

[Serializable]
public class StateData {
    public string scene;
    public int time;
    public string role;
}

// [Serializable]
// public class JSONResponse {
//     public string id;
//     public ConfigData config;
//     //public StateData state;
//     public string action;
//     public Args args;
// }

[Serializable] public class Experience {
    public int id;
    public string createdAt;
    public string updatedAt;
    public string configJson;
    public string name;
}

[Serializable]
public class ExperiencesResponse {
    public int id;
    public Experience[] data;
}
[Serializable]
public class Args {
    public string name;
    public string text;
    public bool enable;
    public string asset;
    public float[] position;
}

// [Serializable]
// public class ConfigData {
//     public ConfigScene[] scenes;
//     public ObjectConfig[] objects;
//     public DefaulSettings defaulSettings;
//     public AssetInfo[] assets;
//     //public AddressableConfig[] addressableConfigs; // Removed and moved to a seperate local json.
// }

[Serializable]
public class ConfigScene {
    public string name;
    public int index;
    public ButtonList[] buttons;
    public string nextScene;
    public SceneVideo video;
    public string buttonLink;
    public Timer timers;
    public string text;
    public string control_room_text;
    public Questionnaire[] questionnaire;
    public List<Bundle> bundles;
}

[Serializable]
public class ButtonList {
    public int visible;
    public string name;
}

[Serializable]
public class SceneVideo {
    public string name;
    public string sceneName;
    public bool button;
    public string buttonText;
}

[Serializable]
public class DefaulSettings {
    public VideoSet video;
    public ButtonSet button;
}

[Serializable]
public class VideoSet {
    public string stereo;
    public int shape;
}

[Serializable]
public class ButtonSet {
    public TransformSet transform;
}

[Serializable]
public class TransformSet
{
    public Vector3 position;
    public Vector3 rotation;
    public Vector3 scale;
}

[Serializable]
public class Timer {
    public int seconds;
    public TimerAction action;
}

[Serializable]
public class TimerAction {
    public string type;
    public TimerArgs args;
}

[Serializable]
public class TimerArgs {
    public string name;
}

[Serializable]
public class Questionnaire {
    public string name;
    // public Settings settings;
    public Questions[] questions;
}

// [Serializable]
// public class Settings {
//     public ButtonArray[] buttons;
// }

// [Serializable]
// public class ButtonArray {
//     public string name;
// }

[Serializable]
public class Questions {
    public string text;
    public Options[] options;
    public int answer;
}

[Serializable]
public class Options {
    public string text;
}

[Serializable]
public class ObjectConfig {
    public string type;
    public string id;
    public string name;
    public string parent;
    public string text;
    public ObjectStates states;
    public TransformData transform;
    public ActionConfig action;
}

[Serializable]
public class ObjectStates {
    public StateConfig defaultState;
    public StateConfig active;
}

[Serializable]
public class StateConfig {
    public string background;
}

[Serializable]
public class TransformData {
    public Vector3 position;
    public Vector3 rotation;
    public Vector2 size;
    public Vector3 scale;
}

[Serializable]
public class ActionConfig {
    public string type;
    public ActionArgs args;
}

[Serializable]
public class ActionArgs {
    public string name;
    public string setting;
}

[Serializable]
public class VideoSettings {
    public string name;
    public string stereo;
    public int shape;
    public bool passthrough;
}

[Serializable]
public class AssetInfo {
    public VideoSettings[] video_settings;
    public string name;
    public string path;
    public string isLoaded;
    public int defaultIndex;
}
// [Serializable]
// public class VideoSettings {
//     public string name;
//     public string stereo;
//     public int Shape;
//     public bool passthrough;
// }

[Serializable]
public class Bundle {
    public string name;
}

[Serializable]
public class Actions {
    public string type;
    public struct args {
        public string name;
        public string setting;
    }
}

enum SocketTasks {
    REGISTER,
    JOIN_SESSION,
    GET_CONFIG,
    GET_STATE,
    GET_EXPERIENCES
}

public class WebSocketManager : MonoBehaviour {

    WebSocket ws;
    // [SerializeField] private VideoManager vidManager;
    // [SerializeField] private SceneController sceneController;
    // [SerializeField] private LoadSceneManager experienceManager;
    // [SerializeField] private ManageScenes manageScenes;
    // [SerializeField] private ObjectManager objectManager;
    // [SerializeField] private LocalJsonManager localJsonManager;
    
    private bool cooldown;
    private AudioAsset audioAsset;


    private async void Awake()
    {
        await OpenWebsocket();
    }

    private async System.Threading.Tasks.Task OpenWebsocket()
    {
        Debug.Log("Opening websocket...");

        // create Meta socket
        ws = new WebSocket("wss://p3036.office.pack.house/ws");
        // ws = new MetaWS.WebSocket("wss://api.xrtraining.nl/ws");

        // Attach the correct delegate signatures from the Meta wrapper
        ws.OnOpen += new WebSocketOpenEventHandler(OnOpen);
        ws.OnMessage += new WebSocketMessageEventHandler(OnMessage);
        ws.OnError += new WebSocketErrorEventHandler(OnError);
        ws.OnClose += new WebSocketCloseEventHandler(OnClose);

        try
        {
            await ws.Connect();
        }
        catch (Exception ex)
        {
            Debug.LogError("WebSocket connect error: " + ex);
        }
    }

    private void OnOpen()
    {
        Debug.Log("WebSocket Opened");
        Register();
    }

    // Meta's OnMessage signature: (byte[] data, int offset, int length)
    private void OnMessage(byte[] data, int offset, int length)
    {
        // copy relevant bytes out of the buffer
        var payload = new byte[length];
        Array.Copy(data, offset, payload, 0, length);

        // Marshal processing to main thread because JsonUtility / UnityEngine calls must run on main thread.
        Meta.Net.NativeWebSocket.MainThreadUtil.Run(ProcessIncomingMessageOnMainThread(payload));
    }

    // Coroutine executed on main thread
    private IEnumerator ProcessIncomingMessageOnMainThread(byte[] payload)
    {
        var message = Encoding.UTF8.GetString(payload);
        Debug.Log("OnMessage! " + message);

        JSONResponse json = null;
        try
        {
            json = JsonUtility.FromJson<JSONResponse>(message);
        }
        catch (Exception ex)
        {
            Debug.LogError("Failed to parse JSON: " + ex + " | raw: " + message);
        }

        if (json != null)
        {
            if (json.id == SocketTasks.REGISTER.ToString())
            {
                JoinSession();
                Debug.Log("Register");
            }
            else if (json.id == SocketTasks.JOIN_SESSION.ToString())
            {
                GetTheConfig();
                Debug.Log("JOIN_SESSION");
            }
            else if (json.id == SocketTasks.GET_CONFIG.ToString())
            {
                GetState();
                Debug.Log("GET_CONFIG");
                // quizManager.ProcessTrainingConfig(json.config);
            }
            else if (json.id == SocketTasks.GET_STATE.ToString())
            {
                Debug.Log("GET_STATE");
            }

            if (json.action == "restart")
            {
                // Close and reopen: run asynchronously but do not block the coroutine.
                _ = ReconnectAsync();
            }
        }

        yield break;
    }

    private async System.Threading.Tasks.Task ReconnectAsync()
    {
        try
        {
            if (ws != null)
            {
                await ws.Close();
            }
        }
        catch (Exception ex)
        {
            Debug.LogError("Error closing websocket for restart: " + ex);
        }

        // small delay to avoid tight restart loops
        await System.Threading.Tasks.Task.Delay(250);

        await OpenWebsocket();
    }

    private void OnError(string errorMsg)
    {
        Debug.LogError("WebSocket Error: " + errorMsg);
    }

    private void OnClose(WebSocketCloseCode code)
    {
        Debug.Log("WebSocket Closed: " + code);
    }

    // private async void Awake() {
    //     this.OpenWebsocket();
    // }

    // private void Start() {
    //     // sceneController = GameObject.Find("SceneController").GetComponent<SceneController>();
    //     // experienceManager = GameObject.Find("LoadSceneManager").GetComponent<LoadSceneManager>();
    // }

    // private async void OpenWebsocket() {
    //     Debug.Log("I am Awake. >>>>");
    //     //ws = new WebSocket("wss://p3036.office.pack.house/ws");
    //     ws = new WebSocket("wss://api.projectxr.io/ws");
    //     //ws = new WebSocket("wss://api.xrtraining.nl/ws");

    //     ws.OnOpen += () => {
    //         Register();
    //     };
    //     ws.OnMessage += async (bytes) => {
    //         // getting the message as a string
    //         Debug.Log("Set!");
    //         var message = Encoding.UTF8.GetString(bytes);
    //         Debug.Log("OnMessage! " + message);
    //         var json = JsonUtility.FromJson<JSONResponse>(message);

    //         if (json.id == SocketTasks.REGISTER.ToString()) {
    //             GetExperiences();
    //             Debug.Log("Register");
    //         }
    //         if (json.id == SocketTasks.GET_EXPERIENCES.ToString()) {
    //             var experiences = JsonUtility.FromJson<ExperiencesResponse>(message);
                
    //             experienceManager.ProcessConfigInfo(experiences.data);
    //             Debug.Log($"Json Experience response: {json}");
    //         }
    //         if (json.id == SocketTasks.JOIN_SESSION.ToString()) {
    //             GetTheConfig();
    //             Debug.Log("JOIN_SESSION");
    //         }
    //         if (json.id == SocketTasks.GET_CONFIG.ToString()) {
    //             GetState();
    //             Debug.Log("GET_CONFIG");
    //             sceneController.ProcessTrainingConfig(json.config);
    //             //manageScenes.ProcessTrainingConfig(json.config);
    //             //vidManager.ProcessTrainingConfig(json.config);
    //             //objectManager.ProcessTrainingConfig(json.config);
    //             //localJsonManager.ProcessTrainingConfig(json.config);
    //             //handMenuManager.ProcessTrainingConfig(json.config);
    //         }
    //         if (json.id == SocketTasks.GET_STATE.ToString()) {
    //             Debug.Log("GET_STATE");
    //             // if (json.state.role != null) sceneManager.SetRole(json.state.role);
    //             // if (json.state.time > 0) sceneManager.SetTime(json.state.time);
                
    //             //!!
    //             // if (!String.IsNullOrEmpty(json.state.scene)) {
    //             //     Debug.Log("GOTO SCENE CALLED (STATE): " + json.state.scene);
    //             //     manageScenes.GoToScene(json.state.scene);
    //             // }

    //             // if (json.state.marker != null) sceneManager.MarkersInput(json.state.marker);
    //         }
    //         if (json.action == "goToScene" && !cooldown) {
    //             cooldown = true;
    //             // manageScenes.GoToScene(json.args.name);
    //             StartCoroutine(Cooldown());
    //         }
    //         // if (json.action == "playAudio") {
    //         //     vidManager.AudioPlayer(audioAsset);
    //         // }
    //         if(json.action == "playVideo" && !cooldown) {
    //             cooldown = true; 
    //             //vidManager.FindVideo(json.args.name);
    //             StartCoroutine(Cooldown());
    //         }
    //         // if(json.action == "enablePassThrough") {
    //         //     if(json.args.enable){
    //         //         vidManager.PassthroughCall(true);
    //         //         Debug.Log("<>Enable" + json.args.enable);
    //         //     } else if(!json.args.enable) {
    //         //         vidManager.PassthroughCall(false);
    //         //         Debug.Log("<>Disable" + json.args.enable);
    //         //     }
    //         // }
    //         // if(json.action == "ending") {
    //         //     SceneManager.LoadScene(json.args.text);
    //         // }
    //         // if (json.action == "restart") {
    //         //     await ws.Close();
    //         //     this.OpenWebsocket();
    //         //     SceneManager.LoadScene(json.args.text);
    //         // }
    //     };
    //     ws.OnError += (e) => {
    //         Debug.Log("Error! " + e);
    //     };
    //     ws.OnClose += (e) => {
    //         Debug.Log("Connection closed!");
    //     };

    //     await ws.Connect();
    // }

    private IEnumerator Cooldown()
    {
        cooldown = true;
        yield return new WaitForSeconds(2.0f);
        cooldown = false;
    }

    // private void Update() {
    //     #if !UNITY_WEBGL || UNITY_EDITOR || UNITY_ANDROID
    //     ws.DispatchMessageQueue();
    //     #endif
    // }

    private async void OnApplicationQuit() {
        await ws.Close();
    }
    public void Register() {
        var deviceId = SystemInfo.deviceUniqueIdentifier;
        Debug.Log("DEVICE:" + deviceId);
        ws.SendText("{\"id\": \""+SocketTasks.REGISTER+"\",\"action\": \"register\", \"args\": { \"deviceId\": \""+deviceId+"\"}}");
    }

    public void GetExperiences() {
        Debug.Log("GetExperience");
        ws.SendText("{\"id\": \""+SocketTasks.GET_EXPERIENCES+"\",\"action\": \"getExperiences\" }");
    }
    public void JoinSession() {
        Debug.Log("JoinSession");
        ws.SendText("{\"id\": \""+SocketTasks.JOIN_SESSION+"\",\"action\": \"joinSession\", \"args\": { \"id\": \"fakeSessionId\"}}");
    }
    public void GetTheConfig() {
        Debug.Log("GetConfig");
        ws.SendText("{\"id\": \""+SocketTasks.GET_CONFIG+"\",\"action\": \"getConfig\"}");
    }
    public void GetState() {
        Debug.Log("GetState");
        ws.SendText("{\"id\": \""+SocketTasks.GET_STATE+"\",\"action\": \"getState\"}");
    }
    public void SendDeviceID() {
        ws.SendText($"{SystemInfo.deviceUniqueIdentifier}");
    }

}
