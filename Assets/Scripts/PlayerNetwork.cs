using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Rendering;
using TMPro;
using static Unity.Burst.Intrinsics.X86.Avx;

// Defines the player's network behavior and interactions within a multiplayer setting.
public class PlayerNetwork : NetworkBehaviour
{
    // Player movement variables
    private Vector2 moveDir;
    private float moveSpeed = 3f;
    private Animator animator;
    private SpriteRenderer spriteRenderer;
    private GameObject heldOrange;  // Reference to the orange game object this player might be holding

    // Network variables to synchronize across clients and server
    private NetworkVariable<bool> isRunning = new NetworkVariable<bool>(default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private NetworkVariable<Vector2> networkMoveDir = new NetworkVariable<Vector2>(default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private NetworkVariable<ulong> clientId = new NetworkVariable<ulong>(default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private NetworkVariable<int> individualScore = new NetworkVariable<int>(default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private static NetworkVariable<int> highestScore = new NetworkVariable<int>(default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private static NetworkVariable<ulong> highestScoreClientId = new NetworkVariable<ulong>(default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    // UI components to display player and score information
    private TextMeshProUGUI clientIDText;
    private TextMeshProUGUI individualScoreText;
    private TextMeshProUGUI highestScoreText;

    private void Start()
    {
        // Initialize components
        animator = GetComponent<Animator>();
        spriteRenderer = GetComponent<SpriteRenderer>();

        // Finding and setting up UI elements
        clientIDText = GetComponentInChildren<TextMeshProUGUI>();
        individualScoreText = GameObject.Find("IndividualScoreText").GetComponent<TextMeshProUGUI>();
        highestScoreText = GameObject.Find("HighestScoreText").GetComponent<TextMeshProUGUI>();

        // Setup network event handlers
        clientId.OnValueChanged += HandleClientIDChanged;
        if (IsServer)
        {
            NetworkManager.Singleton.OnClientConnectedCallback += AssignClientID;
        }

        // Initial update of UI
        UpdateClientIDText();
        UpdateIndividualScoreText();
        UpdateHighestScoreText();
    }

    private void Update()
    {
        // This block only runs for the client that owns this player object
        if (IsClient && !IsHost && IsOwner)
        {
            HandleInput();
            MovePlayer();
            if (heldOrange != null)
            {
                UpdateHeldOrangePosition();
            }

            // Update running state and movement direction if there are changes
            bool currentlyRunning = moveDir.sqrMagnitude > 0;
            if (isRunning.Value != currentlyRunning || (networkMoveDir.Value != moveDir && currentlyRunning))
            {
                RequestUpdateServerRpc(currentlyRunning, moveDir);
            }

            // Input handling for picking up or dropping oranges
            if (Input.GetKeyDown(KeyCode.E))
            {
                if (heldOrange == null)
                {
                    TryPickupOrange();
                }
                else
                {
                    TryDropOrange();
                }
            }
        }
    }

    // Moves the held orange to follow the player's current position
    void UpdateHeldOrangePosition()
    {
        heldOrange.transform.position = transform.position;
    }

    private void OnDestroy()
    {
        // Clean up network event handlers when the object is destroyed
        if (NetworkManager.Singleton)
        {
            NetworkManager.Singleton.OnClientConnectedCallback -= AssignClientID;
        }
    }

    // Assigns a new client ID when a client connects
    private void AssignClientID(ulong newClientID)
    {
        if (IsServer)
        {
            var player = NetworkManager.Singleton.ConnectedClients[newClientID].PlayerObject.GetComponent<PlayerNetwork>();
            player.clientId.Value = newClientID;
        }
    }

    // Updates the client ID display when it changes
    private void HandleClientIDChanged(ulong previousValue, ulong newValue)
    {
        UpdateClientIDText();
    }

    // Handles input for moving the player
    void HandleInput()
    {
        Vector2 previousMoveDir = moveDir;
        moveDir = new Vector2(Input.GetAxis("Horizontal"), Input.GetAxis("Vertical")).normalized;
        bool isNowRunning = moveDir != Vector2.zero;
        if (moveDir != previousMoveDir && IsOwner)
        {
            RequestUpdateServerRpc(isNowRunning, moveDir);
        }
    }

    // Applies the movement to the player
    void MovePlayer()
    {
        transform.position += (Vector3)moveDir * moveSpeed * Time.deltaTime;
    }

    // Server RPC to update movement state
    [ServerRpc]
    void RequestUpdateServerRpc(bool running, Vector2 moveDirection)
    {
        UpdateMovementState(running, moveDirection);
        UpdateAnimationStateClientRpc(running, moveDirection);
    }

    // Client RPC to update animation state based on movement
    [ClientRpc]
    void UpdateAnimationStateClientRpc(bool running, Vector2 moveDirection)
    {
        HandleAnimation(running, moveDirection);
    }

    // Updates the movement state variables
    void UpdateMovementState(bool running, Vector2 moveDirection)
    {
        isRunning.Value = running;
        networkMoveDir.Value = moveDirection;
    }

    // Server RPC to update client ID
    [ServerRpc(RequireOwnership = false)]
    void RequestUpdateClientIDServerRpc(ulong clientID, ServerRpcParams rpcParams = default)
    {
        clientId.Value = clientID;
        UpdateClientIDTextClientRpc(clientID);
    }

    // Client RPC to update client ID text
    [ClientRpc]
    void UpdateClientIDTextClientRpc(ulong id)
    {
        if (clientIDText != null)
            clientIDText.text = $"Client {id}";
    }

    // Handles player animation based on movement state
    void HandleAnimation(bool isRunning, Vector2 moveDir)
    {
        animator.SetBool("isRunning", isRunning);
        spriteRenderer.flipX = moveDir.x < 0;
    }

    // Updates the client ID text display
    private void UpdateClientIDText()
    {
        if (clientIDText != null)
        {
            clientIDText.text = $"Client {clientId.Value}";
        }
    }

    // Updates the individual score text
    void UpdateIndividualScoreText()
    {
        if (individualScoreText != null)
            individualScoreText.text = $"Score: {individualScore.Value}";
    }

    // Updates the highest score text
    void UpdateHighestScoreText()
    {
        if (highestScoreText != null)
            highestScoreText.text = $"Highest Score: {highestScore.Value} (Client {highestScoreClientId.Value})";
    }

    // Client RPC to update score texts across all clients
    [ClientRpc]
    void UpdateScoreTextsClientRpc(int newIndividualScore, int newHighestScore, ulong newHighestScoreClientId)
    {
        Debug.Log($"Client: Updating score texts. New scores - Individual: {newIndividualScore}, Highest: {newHighestScore} by Client {newHighestScoreClientId}");
        if (individualScoreText != null)
            individualScoreText.text = $"Score: {newIndividualScore}";
        if (highestScoreText != null)
            highestScoreText.text = $"Highest Score: {newHighestScore} (Client {newHighestScoreClientId})";
    }

    // Server RPC to handle orange pickup requests
    [ServerRpc(RequireOwnership = false)]
    void RequestPickupOrangeServerRpc(ulong orangeNetworkObjectId, ServerRpcParams rpcParams = default)
    {
        Debug.Log($"Server: Received pickup request for orange {orangeNetworkObjectId} from client {rpcParams.Receive.SenderClientId}");
        if (NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(orangeNetworkObjectId, out NetworkObject orangeNetworkObject))
        {
            Debug.Log($"Server: Found orange in SpawnManager. Current owner: {orangeNetworkObject.OwnerClientId}");
            if (orangeNetworkObject.IsSpawned && orangeNetworkObject.OwnerClientId == 0)
            {
                orangeNetworkObject.ChangeOwnership(rpcParams.Receive.SenderClientId);
                Debug.Log($"Server: Changing ownership of orange {orangeNetworkObjectId} and sending update to clients.");
                Vector3 playerPosition = NetworkManager.Singleton.ConnectedClients[rpcParams.Receive.SenderClientId].PlayerObject.transform.position;
                UpdateOrangeStateClientRpc(true, orangeNetworkObjectId, rpcParams.Receive.SenderClientId);
            }
            else
            {
                Debug.Log("Server: Orange is already owned or not spawned.");
            }
        }
        else
        {
            Debug.LogError("Server: Orange not found in SpawnManager.");
        }
    }


    // Client RPC to handle updates on orange state
    [ClientRpc]
    void UpdateOrangeStateClientRpc(bool pickedUp, ulong orangeNetworkObjectId, ulong clientId)
    {
        Debug.Log($"Client: Received update to change orange state {pickedUp} for orange {orangeNetworkObjectId}");

        if (NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(orangeNetworkObjectId, out NetworkObject orangeNetworkObject))
        {
            GameObject orange = orangeNetworkObject.gameObject;
            if (pickedUp)
            {
                if (NetworkManager.Singleton.LocalClientId == clientId)
                {
                    // Position the orange near the player without changing the parent
                    orange.transform.position = NetworkManager.Singleton.ConnectedClients[clientId].PlayerObject.transform.position;
                    heldOrange = orange;  // Just reference the held orange
                }
            }
            else
            {
                if (NetworkManager.Singleton.IsServer)
                {
                    // Server should handle deactivation and clearing of the parent
                    orange.transform.SetParent(null);
                    orange.SetActive(false);
                }
                if (heldOrange == orange)
                {
                    heldOrange = null;  // Clear the reference if the orange is dropped
                }
            }
        }
        else
        {
            Debug.LogError($"Client: Failed to find orange with ID {orangeNetworkObjectId} in the network manager's spawned objects.");
        }
    }

    // Server RPC to handle orange drop requests
    [ServerRpc(RequireOwnership = true)]
    void RequestDropOrangeServerRpc(ulong orangeNetworkObjectId)
    {
        Debug.Log($"Server: Received drop request for orange {orangeNetworkObjectId}");
        if (NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(orangeNetworkObjectId, out NetworkObject orangeNetworkObject))
        {
            if (orangeNetworkObject.OwnerClientId == NetworkManager.Singleton.LocalClientId)
            {
                Debug.Log("Server: Despawning orange and updating score.");
                orangeNetworkObject.Despawn();
                AddScore(5, NetworkManager.Singleton.LocalClientId);
            }
            else
            {
                Debug.LogError("Server: Owner mismatch or orange not found.");
            }
        }
    }

    // Attempt to pick up an orange
    public void TryPickupOrange()
    {
        Debug.Log("Attempting to pick up an orange.");
        if (heldOrange != null)
        {
            Debug.Log("Already holding an orange.");
            return;  // Exit if already holding an orange
        }

        Collider2D[] hitColliders = Physics2D.OverlapCircleAll(transform.position, 1f);
        foreach (Collider2D hit in hitColliders)
        {
            if (hit.CompareTag("Orange"))
            {
                Debug.Log($"Trying to pick up orange with ID: {hit.GetComponent<NetworkObject>().NetworkObjectId}");
                RequestPickupOrangeServerRpc(hit.GetComponent<NetworkObject>().NetworkObjectId);
                break;
            }
        }
    }

    // Attempt to drop an orange
    void TryDropOrange()
    {
        if (heldOrange != null && IsOwner)
        {
            Debug.Log("Checking for basket collision to drop orange.");
            Collider2D[] hitColliders = Physics2D.OverlapCircleAll(transform.position, 0.5f);
            foreach (Collider2D hit in hitColliders)
            {
                if (hit.CompareTag("Basket"))
                {
                    RequestDropOrangeServerRpc(heldOrange.GetComponent<NetworkObject>().NetworkObjectId);
                    break;
                }
            }
        }
    }

    // Update the score for a specific client
    public void AddScore(int points, ulong clientId)
    {
        Debug.Log($"Server: Adding score {points} to client {clientId}");
        if (!IsServer) return;

        var playerNetwork = NetworkManager.Singleton.ConnectedClients[clientId].PlayerObject.GetComponent<PlayerNetwork>();
        playerNetwork.individualScore.Value += points;
        Debug.Log($"Server: New score for client {clientId} is {playerNetwork.individualScore.Value}");

        if (playerNetwork.individualScore.Value > highestScore.Value)
        {
            highestScore.Value = playerNetwork.individualScore.Value;
            highestScoreClientId.Value = clientId;
            Debug.Log("Server: New highest score updated.");
        }

        UpdateScoreTextsClientRpc(playerNetwork.individualScore.Value, highestScore.Value, highestScoreClientId.Value);
    }

    // Handles network events when enabled
    void OnEnable()
    {
        if (IsServer)
        {
            NetworkManager.Singleton.OnClientConnectedCallback += HandleClientConnected;
        }

        individualScore.OnValueChanged += OnIndividualScoreChanged;
        highestScore.OnValueChanged += OnHighestScoreChanged;
        highestScoreClientId.OnValueChanged += OnHighestScoreClientIdChanged;
    }

    // Cleans up network events when disabled
    void OnDisable()
    {
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientConnectedCallback -= HandleClientConnected;
        }
        individualScore.OnValueChanged -= OnIndividualScoreChanged;
        highestScore.OnValueChanged -= OnHighestScoreChanged;
        highestScoreClientId.OnValueChanged -= OnHighestScoreClientIdChanged;
    }

    // Handles new client connections by assigning client IDs
    private void HandleClientConnected(ulong newClientId)
    {
        if (IsServer)
        {
            UpdateClientIDClientRpc(newClientId);
        }
    }

    // Client RPC to update client ID information
    [ClientRpc]
    private void UpdateClientIDClientRpc(ulong newClientId)
    {
        clientId.Value = newClientId;
        UpdateClientIDText();
    }

    // Updates the individual score text when the score changes
    void OnIndividualScoreChanged(int oldScore, int newScore)
    {
        UpdateIndividualScoreText();
    }

    // Updates the highest score text when the highest score changes
    void OnHighestScoreChanged(int oldScore, int newScore)
    {
        UpdateHighestScoreText();
    }

    // Updates the highest score client ID text when it changes
    void OnHighestScoreClientIdChanged(ulong oldClientId, ulong newClientId)
    {
        UpdateHighestScoreText();
    }

    // Handles collisions with the basket to process score updates and orange drops
    void OnTriggerEnter2D(Collider2D collision)
    {
        if (collision.CompareTag("Basket") && heldOrange != null)
        {
            if (IsOwner)  // Ensure only the owner can score
            {
                ulong ownerId = heldOrange.GetComponent<NetworkObject>().OwnerClientId;
                AddScore(5, ownerId);  // Add score to the client who owns the orange
                RequestDropOrangeServerRpc(heldOrange.GetComponent<NetworkObject>().NetworkObjectId);
            }
        }
    }
}