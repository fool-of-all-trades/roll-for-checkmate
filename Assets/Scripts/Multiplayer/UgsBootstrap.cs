//using System.Threading.Tasks;
//using Unity.Services.Core;
//using Unity.Services.Authentication;
//using UnityEngine;

//public class UgsBootstrap : MonoBehaviour
//{
//    public static bool IsReady { get; private set; }

//    async void Awake()
//    {
//        if (IsReady) return;
//        await Init();
//    }

//    public static async Task Init()
//    {
//        if (IsReady) return;
//        await UnityServices.InitializeAsync();
//        if (!AuthenticationService.Instance.IsSignedIn)
//        {
//            await AuthenticationService.Instance.SignInAnonymouslyAsync();
//            Debug.Log($"[UGS] Signed in anon: {AuthenticationService.Instance.PlayerId}");
//        }
//        IsReady = true;
//    }
//}


using System.Threading.Tasks;
using Unity.Services.Core;
using Unity.Services.Core.Environments;
using Unity.Services.Authentication;
using UnityEngine;

public class UgsBootstrap : MonoBehaviour
{
    [SerializeField] string profile = "default"; // set "editorA" / "editorB" per instance
    [SerializeField] string environmentName = "production"; // or "development" if you use environments

    public static bool IsReady { get; private set; }

    async void Awake()
    {
        if (IsReady) return;
        await Init(profile, environmentName);
    }

    public static async Task Init(string profileName = "default", string environmentName = "production")
    {
        if (!IsReady)
        {
            var init = new InitializationOptions()
                .SetEnvironmentName(environmentName)
                .SetProfile(profileName);

            await UnityServices.InitializeAsync(init);
        }

        // If you ever want to switch at runtime (rare), you can:
        // AuthenticationService.Instance.SwitchProfile("someOtherProfile");

        if (!AuthenticationService.Instance.IsSignedIn)
        {
            await AuthenticationService.Instance.SignInAnonymouslyAsync();
            Debug.Log($"[UGS] Signed in as {AuthenticationService.Instance.PlayerId} (profile={profileName})");
        }

        IsReady = true;
    }
}
