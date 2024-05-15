using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Rendering;

public class PlayerNetwork : NetworkBehaviour
{
    private Vector2 moveDir;
    private float moveSpeed = 3f;

    private void Update()
    {
        if (!IsOwner) return;

        HandleInput();
        MovePlayer();
    }

    private void HandleInput()
    {
        moveDir = Vector2.zero;  // Reset moveDir each frame

        if (Input.GetKey(KeyCode.W)) moveDir.y = 1f;
        if (Input.GetKey(KeyCode.S)) moveDir.y = -1f;
        if (Input.GetKey(KeyCode.A)) moveDir.x = -1f;
        if (Input.GetKey(KeyCode.D)) moveDir.x = 1f;

        moveDir = moveDir.normalized;  // Normalize to ensure consistent speed in all directions
    }

    private void MovePlayer()
    {
        transform.position += (Vector3)moveDir * moveSpeed * Time.deltaTime;
    }
}
