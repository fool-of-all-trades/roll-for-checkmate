using Unity.Netcode;
using UnityEngine;

public class EnsureSingleNetworkManager : MonoBehaviour
{
    void Awake()
    {
        var thisNm = GetComponent<NetworkManager>();
        if (NetworkManager.Singleton != null && NetworkManager.Singleton != thisNm)
        {
            // There's already one alive; kill this duplicate scene copy.
            Destroy(gameObject);
            return;
        }
        DontDestroyOnLoad(gameObject); // keep the one true manager
    }
}
