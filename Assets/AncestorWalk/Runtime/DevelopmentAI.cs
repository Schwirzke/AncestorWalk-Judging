using UnityEngine;

namespace AncestorWalk
{
    // Existing app-private helper connection only. Never bundle credentials in assets or builds.
    public static class DevelopmentAI
    {
        public const string Transport="mac-helper-v1";
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        public const bool Enabled=true;
#else
        public const bool Enabled=false;
#endif
        static SceneServiceConnection cached;
        static float checkAfter;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Reset(){cached=null;checkAfter=0;}
        public static bool TryConnection(out SceneServiceConnection connection)
        {
            // Headset sessions require the user's tested key. The local helper remains an Editor tool.
            connection=null;if(!Enabled||!Application.isEditor||MetaApi.Configured)return false;
            if(cached!=null){connection=cached;return true;}
            if(Time.realtimeSinceStartup<checkAfter)return false;checkAfter=Time.realtimeSinceStartup+2;
            try{cached=SceneGeneration.ReadConnection();connection=cached;return true;}
            catch{return false;}
        }
        public static SceneServiceConnection Resolve()
        {
            if(TryConnection(out var connection))return connection;
            MetaApi.RequireKey();return null;
        }
    }
}
