using Unity.Netcode;
using UnityEngine;

[RequireComponent(typeof(NetworkManager))]
public class AutoNetworkStart : MonoBehaviour
{
    public bool autoHost = true;

    void Start()
    {
        if (autoHost)
            NetworkManager.Singleton.StartHost();
    }
}
