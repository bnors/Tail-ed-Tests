using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

// This class manages the UI for starting the server, host, and client.
public class NetworkManagerUI : MonoBehaviour
{
    // References to the UI buttons for starting the server, host, and client.
    [SerializeField] private Button serverBtn;
    [SerializeField] private Button hostBtn;
    [SerializeField] private Button clientBtn;

    // This method is called when the script instance is being loaded.
    private void Awake()
    {
        // Adding listener to the server button to start the server.
        serverBtn.onClick.AddListener(() =>
        {
            NetworkManager.Singleton.StartServer();
        });

        // Adding listener to the host button to start the host and despawn the host player.
        hostBtn.onClick.AddListener(() =>
        {
            NetworkManager.Singleton.StartHost();
            DespawnHostPlayer();
        });

        // Adding listener to the client button to start the client.
        clientBtn.onClick.AddListener(() =>
        {
            NetworkManager.Singleton.StartClient();
        });
    }

    // Method to despawn the host player.
    private void DespawnHostPlayer()
    {
        // Accessing ServerClientId statically
        var serverClientId = NetworkManager.ServerClientId;
        // Checking if the server client is connected.
        if (NetworkManager.Singleton.ConnectedClients.TryGetValue(serverClientId, out var networkClient))
        {
            // If the server client has a player object, despawn it.
            if (networkClient.PlayerObject != null)
            {
                networkClient.PlayerObject.Despawn();
            }
        }
    }
}