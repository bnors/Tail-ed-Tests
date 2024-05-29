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
        if (IsClient && !IsHost && IsOwner)
        {
            HandleInput();
            MovePlayer();

            // Only send updates if there's a change
            bool currentlyRunning = moveDir.sqrMagnitude > 0;
            if (isRunning.Value != currentlyRunning || (networkMoveDir.Value != moveDir && currentlyRunning))
            {
                RequestUpdateServerRpc(currentlyRunning, moveDir);
            }

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

    private void OnDestroy()
    {
        if (NetworkManager.Singleton)
        {
            NetworkManager.Singleton.OnClientConnectedCallback -= AssignClientID;
        }
    }

    private void AssignClientID(ulong newClientID)
    {
        if (IsServer)
        {
            // Assign the connected client's ID as its clientId variable
            var player = NetworkManager.Singleton.ConnectedClients[newClientID].PlayerObject.GetComponent<PlayerNetwork>();
            player.clientId.Value = newClientID; // This ensures each client gets and keeps its unique client ID
        }
    }

    private void HandleClientIDChanged(ulong previousValue, ulong newValue)
    {
        UpdateClientIDText();
    }

    void HandleInput()
    {
        Vector2 previousMoveDir = moveDir;
        moveDir = new Vector2(Input.GetAxis("Horizontal"), Input.GetAxis("Vertical")).normalized;

        bool isNowRunning = moveDir != Vector2.zero;
        if (moveDir != previousMoveDir && IsOwner)
        {
            // Client requests the server to update movement state
            RequestUpdateServerRpc(isNowRunning, moveDir);
        }
    }

    void MovePlayer()
    {
        transform.position += (Vector3)moveDir * moveSpeed * Time.deltaTime;
        if (heldOrange != null)
        {
            heldOrange.transform.position = transform.position;
        }
    }

    [ServerRpc]
    void RequestUpdateServerRpc(bool running, Vector2 moveDirection)
    {
        UpdateMovementState(running, moveDirection);
        UpdateAnimationStateClientRpc(running, moveDirection);
    }

    [ClientRpc]
    void UpdateAnimationStateClientRpc(bool running, Vector2 moveDirection)
    {
        // Directly pass the running state and direction to the animation handler
        HandleAnimation(running, moveDirection);
    }

    void UpdateMovementState(bool running, Vector2 moveDirection)
    {
        isRunning.Value = running;
        networkMoveDir.Value = moveDirection;
    }

    [ServerRpc(RequireOwnership = false)]
    void RequestUpdateClientIDServerRpc(ulong clientID, ServerRpcParams rpcParams = default)
    {
        clientId.Value = clientID;
        UpdateClientIDTextClientRpc(clientID);
    }

    [ClientRpc]
    void UpdateClientIDTextClientRpc(ulong id)
    {
        if (clientIDText != null)
            clientIDText.text = $"Client {id}";
    }

    void HandleAnimation(bool isRunning, Vector2 moveDir)
    {
        // Update the animator with the running state
        animator.SetBool("isRunning", isRunning);
        // Determine the sprite flip based on the x component of the move direction
        spriteRenderer.flipX = moveDir.x < 0;
    }

    private void UpdateClientIDText()
    {
        if (clientIDText != null)
        {
            clientIDText.text = $"Client {clientId.Value}";
        }
    }

    void UpdateIndividualScoreText()
    {
        if (individualScoreText != null)
            individualScoreText.text = $"Score: {individualScore.Value}";
    }

    void UpdateHighestScoreText()
    {
        if (highestScoreText != null)
            highestScoreText.text = $"Highest Score: {highestScore.Value} (Client {highestScoreClientId.Value})";
    }

    [ClientRpc]
    void UpdateScoreTextsClientRpc(ulong clientId, int newIndividualScore, int newHighestScore, ulong newHighestScoreClientId)
    {
        Debug.Log($"SERVER {clientId} CLIENT {this.clientId.Value} Client: Received score update. New Individual Score: {newIndividualScore}, New Highest Score: {newHighestScore}");
        if (individualScoreText != null && IsClient && IsOwner)
            individualScoreText.text = $"Score: {newIndividualScore}";
        if (highestScoreText != null)
            highestScoreText.text = $"Highest Score: {newHighestScore} (Client {newHighestScoreClientId})";
    }

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
                    // Only adjust local properties, do not reparent
                    orange.transform.position = transform.position;
                    heldOrange = orange; // Only assign to a local variable, don't change the parent
                }
            }
            else
            {
                if (NetworkManager.Singleton.IsServer)
                {
                    // Deactivation and reparenting should only be done by the server
                    orange.transform.SetParent(null);
                    orange.SetActive(false);
                }
                heldOrange = null;
            }
        }
        else
        {
            Debug.LogError("Client: Failed to find orange in network manager's spawned objects.");
        }
    }

    [ServerRpc(RequireOwnership = true)]
    void RequestDropOrangeServerRpc(ulong orangeNetworkObjectId, ServerRpcParams rpcParams = default)
    {
        Debug.Log($"Server: Received drop request for orange {orangeNetworkObjectId}");
        if (NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(orangeNetworkObjectId, out NetworkObject orangeNetworkObject))
        {
            if (orangeNetworkObject.OwnerClientId == rpcParams.Receive.SenderClientId)
            {
                Debug.Log("Server: Despawning orange and updating score.");
                orangeNetworkObject.Despawn();
                AddScore(5, rpcParams.Receive.SenderClientId);
                FindObjectOfType<OrangeSpawner>().OrangePickedUp();
            }
            else
            {
                Debug.LogError("Server: Ownership mismatch or orange not found.");
            }
        }
    }

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
                    Debug.Log("Basket detected, attempting to drop orange.");
                    RequestDropOrangeServerRpc(heldOrange.GetComponent<NetworkObject>().NetworkObjectId);
                    break;
                }
            }
        }
    }

    public void AddScore(int points, ulong clientId)
    {
        Debug.Log($"Server: Adding score {points} to client {clientId} {this.clientId} ");
        if (!IsServer) return;


        var playerNetwork = NetworkManager.Singleton.ConnectedClients[clientId].PlayerObject.GetComponent<PlayerNetwork>();
        //playerNetwork.individualScore.Value += points;
        individualScore.Value += points;
        Debug.Log($"Server: New score for client {clientId} is {playerNetwork.individualScore.Value}");

        if (playerNetwork.individualScore.Value > highestScore.Value)
        {
            highestScore.Value = playerNetwork.individualScore.Value;
            highestScoreClientId.Value = clientId;
            Debug.Log("Server: New highest score updated.");
        }

        UpdateScoreTextsClientRpc(clientId, playerNetwork.individualScore.Value, highestScore.Value, highestScoreClientId.Value);
    }

    void OnEnable()
    {
        if (IsServer)
        {
            NetworkManager.Singleton.OnClientConnectedCallback += HandleClientConnected;
        }

        //individualScore.OnValueChanged += OnIndividualScoreChanged;
        highestScore.OnValueChanged += OnHighestScoreChanged;
        highestScoreClientId.OnValueChanged += OnHighestScoreClientIdChanged;
    }

    void OnDisable()
    {
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientConnectedCallback -= HandleClientConnected;
        }
        //individualScore.OnValueChanged -= OnIndividualScoreChanged;
        highestScore.OnValueChanged -= OnHighestScoreChanged;
        highestScoreClientId.OnValueChanged -= OnHighestScoreClientIdChanged;
    }

    private void HandleClientConnected(ulong newClientId)
    {
        if (IsServer)
        {
            UpdateClientIDClientRpc(newClientId);
        }
    }

    [ClientRpc]
    private void UpdateClientIDClientRpc(ulong newClientId)
    {
        clientId.Value = newClientId;
        UpdateClientIDText();
    }

    void OnIndividualScoreChanged(int oldScore, int newScore)
    {
        Debug.Log($"OnIndividualScoreChanged : {clientId.Value}, {oldScore}, {newScore}");
        UpdateIndividualScoreText();
    }

    void OnHighestScoreChanged(int oldScore, int newScore)
    {
        UpdateHighestScoreText();
    }

    void OnHighestScoreClientIdChanged(ulong oldClientId, ulong newClientId)
    {
        UpdateHighestScoreText();
    }

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