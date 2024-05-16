using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;

public class OrangeSpawner : NetworkBehaviour
{
    public GameObject orangePrefab;  // Reference to the orange prefab
    public Transform spawnPoint;     // Point where the orange will spawn
    private float spawnInterval = 5f;  // Interval in seconds between spawns

    private void Start()
    {
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnServerStarted += OnServerStarted;
        }
    }

    private void OnDestroy()
    {
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnServerStarted -= OnServerStarted;
        }
    }

    private void OnServerStarted()
    {
        if (IsServer)
        {
            Debug.Log("Orange Spawner: Running on the server (host)");
            StartCoroutine(SpawnOrange());
        }
        else
        {
            Debug.Log("Orange Spawner: Not running on the server, spawner not active");
        }
    }

    private IEnumerator SpawnOrange()
    {
        while (true)
        {
            yield return new WaitForSeconds(spawnInterval);
            Debug.Log("Orange Spawner: Spawning orange...");
            SpawnOrangeAtPosition();
        }
    }

    private void SpawnOrangeAtPosition()
    {
        Debug.Log($"Orange Spawner: Instantiating orange at position {spawnPoint.position}");
        GameObject orange = Instantiate(orangePrefab, spawnPoint.position, Quaternion.identity);
        NetworkObject networkObject = orange.GetComponent<NetworkObject>();
        if (networkObject != null)
        {
            networkObject.Spawn();
            Debug.Log("Orange Spawner: Orange spawned successfully");
        }
        else
        {
            Debug.LogError("Orange Spawner: NetworkObject component missing from Orange prefab");
        }
    }
}