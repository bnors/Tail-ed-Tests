using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;

public class OrangeSpawner : NetworkBehaviour
{
    public GameObject orangePrefab;
    public Transform spawnPoint;
    private float spawnInterval = 5f;
    private bool canSpawn = true;

    private void Start()
    {
        if (IsServer)
        {
            Debug.Log("Server starting, initializing orange spawning.");
            StartCoroutine(SpawnOrange());
        }
    }

    private IEnumerator SpawnOrange()
    {
        while (true)
        {
            yield return new WaitUntil(() => canSpawn);
            Debug.Log("Conditions met for spawning an orange.");
            yield return new WaitForSeconds(spawnInterval);
            SpawnOrangeAtPosition();
            canSpawn = false; // Prevent continuous spawning without control
        }
    }

    public void SpawnOrangeAtPosition()
    {
        GameObject orange = Instantiate(orangePrefab, spawnPoint.position, Quaternion.identity);
        NetworkObject networkObject = orange.GetComponent<NetworkObject>();
        if (networkObject != null)
        {
            networkObject.Spawn();
            Debug.Log("Orange Spawner: Orange spawned successfully.");
        }
        else
        {
            Debug.LogError("Failed to spawn orange, NetworkObject component missing.");
        }
    }

    public IEnumerator SpawnNewOrangeAfterDelay(float delay)
    {
        yield return new WaitForSeconds(delay);
        canSpawn = true; // Allow new orange to spawn
        Debug.Log("Spawn new orange after delay.");
        SpawnOrangeAtPosition(); // Call the method to spawn the orange
    }

    public void OrangePickedUp()
    {
        Debug.Log("Orange picked up, scheduling new spawn.");
        StartCoroutine(SpawnNewOrangeAfterDelay(5f));
    }
}