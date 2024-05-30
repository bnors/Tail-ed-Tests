using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;

// This class handles the spawning of oranges in the game
public class OrangeSpawner : NetworkBehaviour
{
    public GameObject orangePrefab; // Prefab of the orange to spawn
    public Transform spawnPoint; // Location where oranges will be spawned
    private float spawnInterval = 5f; // Interval between spawns
    private bool canSpawn = true; // Control flag for spawning oranges

    // Called when the object is spawned on the network
    public override void OnNetworkSpawn()
    {
        if (IsServer) // Only the server can spawn oranges
        {
            Debug.Log("Server started, beginning to spawn the first orange.");
            StartCoroutine(SpawnOrange()); // Start the spawning coroutine
        }
    }

    // Method to spawn an orange at the designated spawn point
    public void SpawnOrangeAtPosition()
    {
        GameObject orange = Instantiate(orangePrefab, spawnPoint.position, Quaternion.identity); // Instantiate the orange prefab
        NetworkObject networkObject = orange.GetComponent<NetworkObject>();
        if (networkObject != null)
        {
            networkObject.Spawn(); // Spawn the orange on the network
            Debug.Log($"Orange spawned successfully at {spawnPoint.position}");
        }
        else
        {
            Debug.LogError("Failed to spawn orange, NetworkObject component missing."); // Error if the NetworkObject component is missing
        }
    }

    // Coroutine to handle continuous spawning of oranges
    private IEnumerator SpawnOrange()
    {
        while (true)
        {
            yield return new WaitUntil(() => canSpawn); // Wait until spawning is allowed
            Debug.Log("Spawning orange now after interval.");
            SpawnOrangeAtPosition(); // Spawn the orange
            canSpawn = false;  // Ensure no new oranges spawn until explicitly allowed
        }
    }

    // Called when an orange is picked up by a player
    public void OrangePickedUp()
    {
        Debug.Log("Orange picked up, scheduling new spawn.");
        StartCoroutine(SpawnNewOrangeAfterDelay(5f)); // Schedule a new orange to spawn after a delay
    }

    // Coroutine to handle the delay before spawning a new orange
    private IEnumerator SpawnNewOrangeAfterDelay(float delay)
    {
        yield return new WaitForSeconds(delay); // Wait for the specified delay
        canSpawn = true;  // Allow spawning of new orange
        Debug.Log("Can spawn new orange now.");
    }
}