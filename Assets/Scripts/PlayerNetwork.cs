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
        // Update client-side variables if not the owner (server has already updated the owner)
        if (!IsOwner)
        {
            isRunning.Value = running;
            networkMoveDir.Value = moveDirection;
        }
        HandleAnimation();  // Ensure this updates the animation based on the latest state
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

    void HandleAnimation()
    {
        animator.SetBool("isRunning", isRunning.Value);
        spriteRenderer.flipX = networkMoveDir.Value.x < 0;
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
            if (orangeNetworkObject.OwnerClientId == rpcParams.Receive.SenderClientId || orangeNetworkObject.OwnerClientId == 0)
            {
                GameObject orange = orangeNetworkObject.gameObject;
                if (heldOrange == null)
                {
                    orange.transform.SetParent(transform);
                    orange.transform.localPosition = Vector3.zero;
                    heldOrange = orange;
                    orangeNetworkObject.ChangeOwnership(rpcParams.Receive.SenderClientId);

                    OrangeSpawner spawner = FindObjectOfType<OrangeSpawner>(); // Find the spawner
                    if (spawner != null)
                    {
                        StartCoroutine(spawner.SpawnNewOrangeAfterDelay(5)); // Use the spawner's method
                    }
                }
            }
            else
            {
                Debug.LogError($"Ownership mismatch: client {rpcParams.Receive.SenderClientId} tried to pick up an orange owned by {orangeNetworkObject.OwnerClientId}");
            }
        }
    }



    [ClientRpc]
    void UpdateOrangeStateClientRpc(bool pickedUp, ulong orangeNetworkObjectId)
    {
        if (NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(orangeNetworkObjectId, out NetworkObject orangeNetworkObject))
        {
            GameObject orange = orangeNetworkObject.gameObject;
            // You can disable the renderer or collider here to visually indicate that the orange has been picked up
            if (pickedUp)
            {
                orange.GetComponent<Renderer>().enabled = false;
                orange.GetComponent<Collider2D>().enabled = false;
            }
            else
            {
                orange.GetComponent<Renderer>().enabled = true;
                orange.GetComponent<Collider2D>().enabled = true;
            }
        }
    }

    [ServerRpc(RequireOwnership = false)]
    void RequestDropOrangeServerRpc(ServerRpcParams rpcParams = default)
    {
        if (heldOrange != null)
        {
            NetworkObject orangeNetworkObject = heldOrange.GetComponent<NetworkObject>();
            if (orangeNetworkObject.OwnerClientId == rpcParams.Receive.SenderClientId)
            {
                heldOrange.transform.SetParent(null);
                heldOrange = null;
                orangeNetworkObject.ChangeOwnership(0); // Reset ownership to no one
                orangeNetworkObject.Despawn();
            }
            else
            {
                Debug.LogError($"Drop mismatch: client {rpcParams.Receive.SenderClientId} tried to drop an orange they do not own.");
            }
        }
    }

    void TryPickupOrange()
    {
        Collider2D[] hitColliders = Physics2D.OverlapCircleAll(transform.position, 1f);
        foreach (Collider2D hit in hitColliders)
        {
            if (hit.CompareTag("Orange"))
            {
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
        if (collision.CompareTag("Orange") && heldOrange == null)
        {
            RequestPickupOrangeServerRpc(collision.gameObject.GetComponent<NetworkObject>().NetworkObjectId);
        }

        if (collision.CompareTag("Basket") && heldOrange != null)
        {
            RequestDropOrangeServerRpc();
        }
    }
}