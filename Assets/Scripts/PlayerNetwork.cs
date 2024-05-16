using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Rendering;
using TMPro;

public class PlayerNetwork : NetworkBehaviour
{
    private Vector2 moveDir;
    private float moveSpeed = 3f;
    private Animator animator;
    private SpriteRenderer spriteRenderer;

    // Network variables for running state, move direction, and client ID
    private NetworkVariable<bool> isRunning = new NetworkVariable<bool>(default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private NetworkVariable<Vector2> networkMoveDir = new NetworkVariable<Vector2>(default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private NetworkVariable<ulong> clientId = new NetworkVariable<ulong>(default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    // Reference to the TextMeshProUGUI component for displaying the client ID
    public TextMeshProUGUI clientIDText;

    private void Start()
    {
        animator = GetComponent<Animator>();
        spriteRenderer = GetComponent<SpriteRenderer>();

        // If the player is the owner, request the server to update the client ID
        if (IsOwner)
        {
            ulong localClientId = NetworkManager.Singleton.LocalClientId;
            RequestUpdateClientIDServerRpc(localClientId);
        }

        // Update the client ID text
        UpdateClientIDText();
    }

    private void Update()
    {
        if (IsOwner)
        {
            HandleInput();
            MovePlayer();
            // Request the server to update the running state and move direction
            RequestUpdateServerRpc(moveDir != Vector2.zero, moveDir);
        }

        // Handle animation for all instances
        HandleAnimation();
    }

    private void HandleInput()
    {
        moveDir = Vector2.zero;  // Reset moveDir each frame

        // Update moveDir based on input keys
        if (Input.GetKey(KeyCode.W)) moveDir.y = 1f;
        if (Input.GetKey(KeyCode.S)) moveDir.y = -1f;
        if (Input.GetKey(KeyCode.A)) moveDir.x = -1f;
        if (Input.GetKey(KeyCode.D)) moveDir.x = 1f;

        // Normalize to ensure consistent speed in all directions
        moveDir = moveDir.normalized;
    }

    private void MovePlayer()
    {
        // Update the position of the player
        transform.position += (Vector3)moveDir * moveSpeed * Time.deltaTime;
    }

    [ServerRpc]
    private void RequestUpdateServerRpc(bool running, Vector2 moveDirection)
    {
        // Server updates the NetworkVariables
        isRunning.Value = running;
        networkMoveDir.Value = moveDirection;
    }

    [ServerRpc]
    private void RequestUpdateClientIDServerRpc(ulong clientID)
    {
        clientId.Value = clientID;
    }

    private void HandleAnimation()
    {
        // Update the Animator parameter
        animator.SetBool("isRunning", isRunning.Value);

        if (isRunning.Value)
        {
            // Use the synchronized move direction for flipping the sprite
            if (networkMoveDir.Value.x < 0)
            {
                spriteRenderer.flipX = true;
            }
            else if (networkMoveDir.Value.x > 0)
            {
                spriteRenderer.flipX = false;
            }
        }
    }

    private void UpdateClientIDText()
    {
        clientIDText.text = $"Client {clientId.Value}";
    }

    // Ensure the client ID text is updated whenever the clientId value changes
    private void OnEnable()
    {
        clientId.OnValueChanged += OnClientIDChanged;
    }

    private void OnDisable()
    {
        clientId.OnValueChanged -= OnClientIDChanged;
    }

    private void OnClientIDChanged(ulong oldClientId, ulong newClientId)
    {
        UpdateClientIDText();
    }
}