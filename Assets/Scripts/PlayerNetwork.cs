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
            if (orangeNetworkObject.OwnerClientId == 0)  // If not owned
            {
                orangeNetworkObject.ChangeOwnership(rpcParams.Receive.SenderClientId);
                UpdateOrangeStateClientRpc(true, orangeNetworkObjectId, rpcParams.Receive.SenderClientId);
            }
        }
    }


    [ClientRpc]
    void UpdateOrangeStateClientRpc(bool pickedUp, ulong orangeNetworkObjectId, ulong clientId)
    {
        if (NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(orangeNetworkObjectId, out NetworkObject orangeNetworkObject))
        {
            GameObject orange = orangeNetworkObject.gameObject;

            // Only adjust the transform if this client is the owner of the orange
            if (NetworkManager.Singleton.LocalClientId == clientId)
            {
                if (pickedUp)
                {
                    // Position the orange at the player's central position (0,0,0 relative to the player)
                    var player = NetworkManager.Singleton.LocalClient.PlayerObject;
                    if (player != null)
                    {
                        orange.transform.position = player.transform.position;
                        orange.transform.SetParent(player.transform);  // Optional: Attach to follow player
                    }
                }
                else
                {
                    // Reset parent to null and reactivate the orange for pickup
                    orange.transform.SetParent(null);
                    orange.SetActive(true);
                }
            }
        }
    }

    [ServerRpc(RequireOwnership = false)]
    void RequestDropOrangeServerRpc(ulong orangeNetworkObjectId)
    {
        if (NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(orangeNetworkObjectId, out NetworkObject orangeNetworkObject))
        {
            if (orangeNetworkObject.OwnerClientId == NetworkManager.Singleton.LocalClientId)
            {
                // Logic to handle the orange drop
                GameObject orange = orangeNetworkObject.gameObject;
                orange.transform.SetParent(null);  // Detach orange from player
                orangeNetworkObject.ChangeOwnership(0); // Reset ownership to no one
                orangeNetworkObject.Despawn();
            }
            else
            {
                Debug.LogError("Drop mismatch: client tried to drop an orange they do not own.");
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
        if (heldOrange != null)
        {
            Collider2D[] hitColliders = Physics2D.OverlapCircleAll(transform.position, 0.5f);
            foreach (Collider2D hit in hitColliders)
            {
                if (hit.CompareTag("Basket"))
                {
                    AddScore(5);
                    FindObjectOfType<OrangeSpawner>().OrangePickedUp();
                    Destroy(heldOrange);
                    heldOrange = null;
                    break;
                }
            }
        }
    }

    public void AddScore(int points)
    {
        if (!IsServer) return;

        individualScore.Value += points;
        Debug.Log($"Adding score: {points} to client {clientId.Value}, Total score: {individualScore.Value}");
        if (individualScore.Value > highestScore.Value)
        {
            highestScore.Value = individualScore.Value;
            highestScoreClientId.Value = NetworkManager.Singleton.LocalClientId;
        }
        UpdateScoreTextsClientRpc(individualScore.Value, highestScore.Value, highestScoreClientId.Value);
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
                AddScore(5);  // Add score to the client
                NetworkObject heldOrangeNetworkObject = heldOrange.GetComponent<NetworkObject>();
                RequestDropOrangeServerRpc(heldOrangeNetworkObject.NetworkObjectId);
            }
        }
    }
}