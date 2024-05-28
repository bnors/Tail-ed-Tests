using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;

public class OrangeSpawner : NetworkBehaviour
{
    public GameObject orangePrefab;
    public Transform spawnPoint;
    private float spawnInterval = 5f;
    private bool canSpawn = true;  // Initially set to true to spawn the first orange

    private void Start()
    {
        if (IsServer)
        {
            StartCoroutine(SpawnOrange());
        }
    }

    private IEnumerator SpawnOrange()
    {
        while (true)
        {
            yield return new WaitUntil(() => canSpawn);  // Wait until it's allowed to spawn
            yield return new WaitForSeconds(spawnInterval);  // Then wait the spawn interval
            SpawnOrangeAtPosition();
            canSpawn = false;  // Prevent further spawning until the current orange is picked up
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

    public void OrangePickedUp()
    {
        StartCoroutine(SpawnNewOrangeAfterDelay(5f));  // Schedule next orange spawn
    }

    public IEnumerator SpawnNewOrangeAfterDelay(float delay)
    {
        yield return new WaitForSeconds(delay);
        canSpawn = true;  // Allow new orange to spawn after the delay
    }
}