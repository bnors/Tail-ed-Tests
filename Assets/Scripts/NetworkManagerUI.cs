using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

public class NetworkManagerUI : MonoBehaviour
{
    [SerializeField] private Button serverBtn;
    [SerializeField] private Button hostBtn;
    [SerializeField] private Button clientBtn;

    private void Awake()
    {
        serverBtn.onClick.AddListener(() =>
        {
            NetworkManager.Singleton.StartServer();
        });

        hostBtn.onClick.AddListener(() =>
        {
            NetworkManager.Singleton.StartHost();
            DespawnHostPlayer();
        });

        clientBtn.onClick.AddListener(() =>
        {
            NetworkManager.Singleton.StartClient();
        });
    }

    private void DespawnHostPlayer()
    {
        // Accessing ServerClientId statically
        var serverClientId = NetworkManager.ServerClientId;
        if (NetworkManager.Singleton.ConnectedClients.TryGetValue(serverClientId, out var networkClient))
        {
            if (networkClient.PlayerObject != null)
            {
                networkClient.PlayerObject.Despawn();
            }
        }
    }
}