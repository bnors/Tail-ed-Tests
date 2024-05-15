using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Rendering;

public class PlayerNetwork : NetworkBehaviour
{
    // Movement direction and speed
    private Vector2 moveDir;
    private float moveSpeed = 3f;
    private Animator animator;
    private SpriteRenderer spriteRenderer;

    // Network variables for running state and movement direction
    private NetworkVariable<bool> isRunning = new NetworkVariable<bool>(default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private NetworkVariable<Vector2> networkMoveDir = new NetworkVariable<Vector2>(default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private void Start()
    {
        // Get references to the Animator and SpriteRenderer components
        animator = GetComponent<Animator>();
        spriteRenderer = GetComponent<SpriteRenderer>();
    }

    private void Update()
    {
        // Only the owner of the object handles input and movement
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

    private void HandleAnimation()
    {
        // Update the Animator parameter
        animator.SetBool("isRunning", isRunning.Value);

        if (isRunning.Value)
        {
            // Flip the sprite based on the synchronized move direction
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
}