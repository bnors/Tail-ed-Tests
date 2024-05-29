using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Rendering;
using TMPro;
using static Unity.Burst.Intrinsics.X86.Avx;

public class PlayerNetwork : NetworkBehaviour
{
    private Vector2 moveDir;
    private float moveSpeed = 3f;
    private Animator animator;
    private SpriteRenderer spriteRenderer;
    private GameObject heldOrange;

    // Network variables for running state, move direction, client ID, and scores
    private NetworkVariable<bool> isRunning = new NetworkVariable<bool>(default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private NetworkVariable<Vector2> networkMoveDir = new NetworkVariable<Vector2>(default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private NetworkVariable<ulong> clientId = new NetworkVariable<ulong>(default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private NetworkVariable<int> individualScore = new NetworkVariable<int>(default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private static NetworkVariable<int> highestScore = new NetworkVariable<int>(default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private static NetworkVariable<ulong> highestScoreClientId = new NetworkVariable<ulong>(default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    // References to the TextMeshProUGUI components for displaying scores
    private TextMeshProUGUI clientIDText;
    private TextMeshProUGUI individualScoreText;
    private TextMeshProUGUI highestScoreText;

    private void Start()
    {
        animator = GetComponent<Animator>();
        spriteRenderer = GetComponent<SpriteRenderer>();

        clientIDText = GetComponentInChildren<TextMeshProUGUI>();
        individualScoreText = GameObject.Find("IndividualScoreText").GetComponent<TextMeshProUGUI>();
        highestScoreText = GameObject.Find("HighestScoreText").GetComponent<TextMeshProUGUI>();

        clientId.OnValueChanged += HandleClientIDChanged;

        if (IsServer)
        {
            // Triggered each time a client connects
            NetworkManager.Singleton.OnClientConnectedCallback += AssignClientID;
        }

        // Initialization of text fields should be done independently of whether it's a host or a client
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
    void UpdateScoreTextsClientRpc(int newIndividualScore, int newHighestScore, ulong newHighestScoreClientId)
    {
        if (individualScoreText != null)
            individualScoreText.text = $"Score: {newIndividualScore}";
        if (highestScoreText != null)
            highestScoreText.text = $"Highest Score: {newHighestScore} (Client {newHighestScoreClientId})";
    }

    [ServerRpc(RequireOwnership = false)]
    void RequestPickupOrangeServerRpc(ulong orangeNetworkObjectId, ServerRpcParams rpcParams = default)
    {
        if (NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(orangeNetworkObjectId, out NetworkObject orangeNetworkObject))
        {
            Debug.Log($"Server: Received pickup request for orange {orangeNetworkObjectId} from client {rpcParams.Receive.SenderClientId}");
            if (orangeNetworkObject.IsSpawned && orangeNetworkObject.OwnerClientId == 0)  // If not owned
            {
                orangeNetworkObject.ChangeOwnership(rpcParams.Receive.SenderClientId);
                var clientPlayer = NetworkManager.Singleton.ConnectedClients[rpcParams.Receive.SenderClientId].PlayerObject;
                Vector3 playerPosition = clientPlayer.transform.position;
                Debug.Log($"Server: Changing ownership of orange {orangeNetworkObjectId} and sending update to clients.");
                UpdateOrangeStateClientRpc(true, orangeNetworkObjectId, playerPosition, rpcParams.Receive.SenderClientId);
            }
        }
    }

    [ClientRpc]
    void UpdateOrangeStateClientRpc(bool pickedUp, ulong orangeNetworkObjectId, Vector3 newPosition, ulong clientId)
    {
        Debug.Log($"Client: Received update to change orange state {pickedUp} for orange {orangeNetworkObjectId}");
        var orangeNetworkObject = NetworkManager.Singleton.SpawnManager.SpawnedObjects[orangeNetworkObjectId];
        if (orangeNetworkObject != null)
        {
            GameObject orange = orangeNetworkObject.gameObject;

            if (pickedUp)
            {
                Debug.Log($"Client: Moving orange to new position at {newPosition}");
                // Directly setting position for debugging
                orange.transform.position = newPosition;
                // If this is the owner client, track the orange
                if (NetworkManager.Singleton.LocalClientId == clientId)
                {
                    heldOrange = orange;
                }
            }
            else
            {
                Debug.Log("Client: Resetting orange position and deactivating.");
                orange.transform.position = newPosition;  // You could adjust this to a drop-off point
                orange.SetActive(false);
                if (heldOrange == orange)
                {
                    heldOrange = null;
                }
            }
        }
        else
        {
            Debug.LogError("Client: Failed to find orange in network manager's spawned objects.");
        }
    }

    [ServerRpc(RequireOwnership = true)]
    void RequestDropOrangeServerRpc(ulong orangeNetworkObjectId)
    {
        if (NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(orangeNetworkObjectId, out NetworkObject orangeNetworkObject))
        {
            if (orangeNetworkObject.OwnerClientId == NetworkManager.Singleton.LocalClientId)
            {
                orangeNetworkObject.Despawn();  // Despawn the orange
                AddScore(5, orangeNetworkObject.OwnerClientId);  // Update score for the client who dropped the orange
            }
        }
    }

    public void TryPickupOrange()
    {
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
            Collider2D[] hitColliders = Physics2D.OverlapCircleAll(transform.position, 0.5f);
            foreach (Collider2D hit in hitColliders)
            {
                if (hit.CompareTag("Basket"))
                {
                    RequestDropOrangeServerRpc(heldOrange.GetComponent<NetworkObject>().NetworkObjectId);
                    // Call AddScore with correct clientId
                    AddScore(5, NetworkManager.Singleton.LocalClientId);
                    break;
                }
            }
        }
    }

    public void AddScore(int points, ulong clientId)
    {
        if (!IsServer) return;

        var playerNetwork = NetworkManager.Singleton.ConnectedClients[clientId].PlayerObject.GetComponent<PlayerNetwork>();
        playerNetwork.individualScore.Value += points;

        if (playerNetwork.individualScore.Value > highestScore.Value)
        {
            highestScore.Value = playerNetwork.individualScore.Value;
            highestScoreClientId.Value = clientId;
        }

        UpdateScoreTextsClientRpc(playerNetwork.individualScore.Value, highestScore.Value, highestScoreClientId.Value);
    }

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

    private void HandleClientConnected(ulong newClientId)
    {
        if (IsServer)
        {
            // Assign client ID
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
                // Get the client ID of the owner of the held orange
                ulong ownerId = heldOrange.GetComponent<NetworkObject>().OwnerClientId;
                AddScore(5, ownerId);  // Add score to the client who owns the orange
                RequestDropOrangeServerRpc(heldOrange.GetComponent<NetworkObject>().NetworkObjectId);
            }
        }
    }
}