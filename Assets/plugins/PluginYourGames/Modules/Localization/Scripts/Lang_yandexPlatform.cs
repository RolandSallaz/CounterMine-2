#if YandexGamesPlatform_yg
using System.Runtime.InteropServices;

namespace YG
{
    public partial class PlatformYG2 : IPlatformsYG2
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")]
        private static extern string LangRequest_js();
#endif

        public string GetLanguage()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            return LangRequest_js();
#elif UNITY_EDITOR
            return YG2.infoYG.Simulation.language;
#else
            return UnityEngine.Application.systemLanguage == UnityEngine.SystemLanguage.Russian ? "ru" : "en";
#endif
        }
    }
}
#endif
