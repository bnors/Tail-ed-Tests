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

    public override void OnNetworkSpawn()
    {
        if (IsServer)
        {
            Debug.Log("Server started, beginning to spawn the first orange.");
            StartCoroutine(SpawnOrange());
        }
    }

    private IEnumerator SpawnOrange()
    {
        while (true)
        {
            yield return new WaitUntil(() => canSpawn);
            Debug.Log("Spawning orange now after interval.");
            SpawnOrangeAtPosition();
            canSpawn = false;  // Ensure no new oranges spawn until explicitly allowed
        }
    }

    public void SpawnOrangeAtPosition()
    {
        GameObject orange = Instantiate(orangePrefab, spawnPoint.position, Quaternion.identity);
        NetworkObject networkObject = orange.GetComponent<NetworkObject>();
        if (networkObject != null)
        {
            networkObject.Spawn();
            Debug.Log($"Orange spawned successfully at {spawnPoint.position}");
        }
        else
        {
            Debug.LogError("Failed to spawn orange, NetworkObject component missing.");
        }
    }

    public void OrangePickedUp()
    {
        Debug.Log("Orange picked up, scheduling new spawn.");
        StartCoroutine(SpawnNewOrangeAfterDelay(5f));
    }

    private IEnumerator SpawnNewOrangeAfterDelay(float delay)
    {
        yield return new WaitForSeconds(delay);
        canSpawn = true;  // Allow spawning new orange
        Debug.Log("Can spawn new orange now.");
    }
}