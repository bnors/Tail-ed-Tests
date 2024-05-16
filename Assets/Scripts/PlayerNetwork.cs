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
    private GameObject heldOrange;

    // Network variables for running state, move direction, client ID, and scores
    private NetworkVariable<bool> isRunning = new NetworkVariable<bool>(default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private NetworkVariable<Vector2> networkMoveDir = new NetworkVariable<Vector2>(default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private NetworkVariable<ulong> clientId = new NetworkVariable<ulong>(default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private NetworkVariable<int> individualScore = new NetworkVariable<int>(default, NetworkVariableReadPermission.Owner, NetworkVariableWritePermission.Server);
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

        // Find and assign the TextMeshPro components
        clientIDText = GetComponentInChildren<TextMeshProUGUI>();

        if (IsOwner)
        {
            // Find UI elements in the scene
            individualScoreText = GameObject.Find("IndividualScoreText").GetComponent<TextMeshProUGUI>();
            highestScoreText = GameObject.Find("HighestScoreText").GetComponent<TextMeshProUGUI>();

            ulong localClientId = NetworkManager.Singleton.LocalClientId;
            RequestUpdateClientIDServerRpc(localClientId);
        }

        // Update the client ID text
        UpdateClientIDText();
        UpdateIndividualScoreText();
        UpdateHighestScoreText();
    }

    private void Update()
    {
        if (IsOwner)
        {
            HandleInput();
            MovePlayer();
            // Request the server to update the running state and move direction
            RequestUpdateServerRpc(moveDir != Vector2.zero, moveDir);

            // Handle interaction input
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
        if (clientIDText != null)
        {
            clientIDText.text = $"Client {clientId.Value}";
        }
    }

    private void UpdateIndividualScoreText()
    {
        if (individualScoreText != null)
        {
            individualScoreText.text = $"Score: {individualScore.Value}";
        }
    }

    private void UpdateHighestScoreText()
    {
        if (highestScoreText != null)
        {
            highestScoreText.text = $"Highest Score: {highestScore.Value} (Client {highestScoreClientId.Value})";
        }
    }

    private void TryPickupOrange()
    {
        // Check for nearby oranges to pick up
        Collider2D[] hitColliders = Physics2D.OverlapCircleAll(transform.position, 1f);
        foreach (var hitCollider in hitColliders)
        {
            if (hitCollider.CompareTag("Orange"))
            {
                heldOrange = hitCollider.gameObject;
                heldOrange.transform.SetParent(transform);
                heldOrange.transform.localPosition = new Vector3(0, 0.01f, 0); // Position in the mouth
                break;
            }
        }
    }

    private void TryDropOrange()
    {
        // Check for the basket
        Collider2D[] hitColliders = Physics2D.OverlapCircleAll(transform.position, 1f);
        foreach (var hitCollider in hitColliders)
        {
            if (hitCollider.CompareTag("Basket"))
            {
                Destroy(heldOrange);
                heldOrange = null;
                AddScoreServerRpc(5);
                break;
            }
        }
    }

    [ServerRpc]
    private void AddScoreServerRpc(int points)
    {
        individualScore.Value += points;
        if (individualScore.Value > highestScore.Value)
        {
            highestScore.Value = individualScore.Value;
            highestScoreClientId.Value = clientId.Value;
        }

        UpdateScoreTextsClientRpc(individualScore.Value, highestScore.Value, highestScoreClientId.Value);
    }

    [ClientRpc]
    private void UpdateScoreTextsClientRpc(int newIndividualScore, int newHighestScore, ulong newHighestScoreClientId)
    {
        if (individualScoreText != null)
        {
            individualScoreText.text = $"Score: {newIndividualScore}";
        }
        if (highestScoreText != null)
        {
            highestScoreText.text = $"Highest Score: {newHighestScore} (Client {newHighestScoreClientId})";
        }
    }

    // Ensure the client ID text is updated whenever the clientId value changes
    private void OnEnable()
    {
        clientId.OnValueChanged += OnClientIDChanged;
        individualScore.OnValueChanged += OnIndividualScoreChanged;
        highestScore.OnValueChanged += OnHighestScoreChanged;
        highestScoreClientId.OnValueChanged += OnHighestScoreClientIdChanged;
    }

    private void OnDisable()
    {
        clientId.OnValueChanged -= OnClientIDChanged;
        individualScore.OnValueChanged -= OnIndividualScoreChanged;
        highestScore.OnValueChanged -= OnHighestScoreChanged;
        highestScoreClientId.OnValueChanged -= OnHighestScoreClientIdChanged;
    }

    private void OnClientIDChanged(ulong oldClientId, ulong newClientId)
    {
        UpdateClientIDText();
    }

    private void OnIndividualScoreChanged(int oldScore, int newScore)
    {
        UpdateIndividualScoreText();
    }

    private void OnHighestScoreChanged(int oldScore, int newScore)
    {
        UpdateHighestScoreText();
    }

    private void OnHighestScoreClientIdChanged(ulong oldClientId, ulong newClientId)
    {
        UpdateHighestScoreText();
    }

    private void OnTriggerEnter2D(Collider2D collision)
    {
        if (collision.CompareTag("Orange") && heldOrange == null)
        {
            heldOrange = collision.gameObject;
            heldOrange.transform.SetParent(transform);
            heldOrange.transform.localPosition = new Vector3(0, 0.01f, 0); // Adjust this position to fit the player
        }

        if (collision.CompareTag("Basket") && heldOrange != null)
        {
            Destroy(heldOrange);
            heldOrange = null;
            AddScoreServerRpc(5);
        }
    }
}