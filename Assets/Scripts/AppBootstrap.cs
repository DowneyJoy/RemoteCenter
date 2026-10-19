using UnityEngine;
using UnityEngine.SceneManagement;

namespace RemoteCenter
{
    /// 按当前场景名自动装配服务端/客户端逻辑，无需手工往场景里挂脚本。
    public static class AppBootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void OnLoaded()
        {
            SceneManager.sceneLoaded += OnSceneLoaded;
            Init(SceneManager.GetActiveScene());
        }

        static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (mode == LoadSceneMode.Single) Init(scene);
        }

        static void Init(Scene scene)
        {
            string sceneName = scene.name;
            if (sceneName == "Server")
            {
                if (GameObject.Find("[ServerApp]") == null)
                    new GameObject("[ServerApp]").AddComponent<ServerApp>();
            }
            else if (sceneName == "Client")
            {
                if (GameObject.Find("[ClientApp]") == null)
                    new GameObject("[ClientApp]").AddComponent<ClientApp>();
            }
        }
    }
}
