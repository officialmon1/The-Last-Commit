using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

// A small first-person controller for exploring the generated environment.
// Uses the project's enabled input backend; no input actions asset is required.
[DisallowMultipleComponent]
[RequireComponent(typeof(CharacterController))]
public sealed class AmberRuinsLitePlayer : MonoBehaviour
{
    public Transform view;
    [Min(.1f)] public float walkSpeed = 4f;
    [Min(.1f)] public float sprintSpeed = 6.5f;
    [Min(0)] public float jumpHeight = .9f;
    [Min(1)] public float gravity = 20f;
    [Range(.02f, .5f)] public float lookSensitivity = .12f;
    public bool showControls = true;

    CharacterController controller;
    Vector3 spawnPosition;
    Quaternion spawnRotation;
    float pitch, verticalSpeed;

    void Awake()
    {
        controller = GetComponent<CharacterController>();
        if (view == null)
        {
            var camera = GetComponentInChildren<Camera>();
            if (camera != null) view = camera.transform;
        }
        if (view == null)
        {
            Debug.LogError("Amber Ruins test player needs a child camera assigned to View.", this);
            enabled = false; return;
        }
        spawnPosition = transform.position;
        spawnRotation = transform.rotation;
    }
    void Start() { SetCursor(true); }
    void OnDisable() { SetCursor(false); }
    void OnApplicationFocus(bool focused) { if (!focused) SetCursor(false); }
    static void SetCursor(bool locked)
    {
        Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
        Cursor.visible = !locked;
    }
    void Update()
    {
        Vector2 move, look; bool sprint, jump, escape, click, reset;
        ReadInput(out move, out look, out sprint, out jump, out escape, out click, out reset);
        bool capturedThisFrame = false;
        if (escape) SetCursor(false);
        else if (click && Cursor.lockState != CursorLockMode.Locked)
        {
            SetCursor(true); capturedThisFrame = true;
        }
        bool captured = Cursor.lockState == CursorLockMode.Locked && Application.isFocused;
        if (captured && reset) { Respawn(); return; }
        if (captured && !capturedThisFrame)
        {
            transform.Rotate(0, look.x * lookSensitivity, 0, Space.Self);
            pitch = Mathf.Clamp(pitch - look.y * lookSensitivity, -80f, 80f);
            view.localRotation = Quaternion.Euler(pitch, 0, 0);
        }
        else { move = Vector2.zero; jump = false; }

        float dt = Time.deltaTime;
        if (dt <= 0 || !controller.enabled) return;
        bool grounded = controller.isGrounded;
        if (grounded && verticalSpeed < 0) verticalSpeed = -2f;
        if (grounded && jump && jumpHeight > 0) verticalSpeed = Mathf.Sqrt(2f * gravity * jumpHeight);
        verticalSpeed = Mathf.Max(verticalSpeed - gravity * dt, -40f);
        Vector3 horizontal = transform.right * move.x + transform.forward * move.y;
        horizontal = Vector3.ClampMagnitude(horizontal, 1f);
        float speed = sprint ? sprintSpeed : walkSpeed;
        CollisionFlags collisions = controller.Move((horizontal * speed + Vector3.up * verticalSpeed) * dt);
        if ((collisions & CollisionFlags.Above) != 0 && verticalSpeed > 0) verticalSpeed = 0;
        if ((collisions & CollisionFlags.Below) != 0 && verticalSpeed < 0) verticalSpeed = -2f;
        if (transform.position.y < spawnPosition.y - 25f) Respawn();
    }
    void Respawn()
    {
        controller.enabled = false;
        transform.SetPositionAndRotation(spawnPosition, spawnRotation);
        verticalSpeed = pitch = 0;
        view.localRotation = Quaternion.identity;
        controller.enabled = true;
    }
    static void ReadInput(out Vector2 move, out Vector2 look, out bool sprint, out bool jump,
        out bool escape, out bool click, out bool reset)
    {
        move = look = Vector2.zero;
        sprint = jump = escape = click = reset = false;
#if ENABLE_INPUT_SYSTEM
        var keys = Keyboard.current;
        if (keys != null)
        {
            move.x = ((keys.dKey.isPressed || keys.rightArrowKey.isPressed) ? 1 : 0) -
                     ((keys.aKey.isPressed || keys.leftArrowKey.isPressed) ? 1 : 0);
            move.y = ((keys.wKey.isPressed || keys.upArrowKey.isPressed) ? 1 : 0) -
                     ((keys.sKey.isPressed || keys.downArrowKey.isPressed) ? 1 : 0);
            sprint = keys.leftShiftKey.isPressed || keys.rightShiftKey.isPressed;
            jump = keys.spaceKey.wasPressedThisFrame;
            escape = keys.escapeKey.wasPressedThisFrame;
            reset = keys.rKey.wasPressedThisFrame;
        }
        var mouse = Mouse.current;
        if (mouse != null)
        {
            look = mouse.delta.ReadValue();
            click = mouse.leftButton.wasPressedThisFrame;
        }
#elif ENABLE_LEGACY_INPUT_MANAGER
        move.x = ((Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow)) ? 1 : 0) -
                 ((Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow)) ? 1 : 0);
        move.y = ((Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow)) ? 1 : 0) -
                 ((Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow)) ? 1 : 0);
        // Legacy mouse axes already include axis sensitivity, unlike raw Input System pixels.
        look = new Vector2(Input.GetAxisRaw("Mouse X"), Input.GetAxisRaw("Mouse Y")) * 15f;
        sprint = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
        jump = Input.GetKeyDown(KeyCode.Space);
        escape = Input.GetKeyDown(KeyCode.Escape);
        click = Input.GetMouseButtonDown(0);
        reset = Input.GetKeyDown(KeyCode.R);
#endif
    }
    void OnGUI()
    {
        if (!showControls) return;
        GUI.Box(new Rect(12, 12, 385, 65), "Amber Ruins — test player");
        GUI.Label(new Rect(22, 35, 365, 36), Cursor.lockState == CursorLockMode.Locked ?
            "WASD: walk   Shift: sprint   Space: jump\nMouse: look   Esc: release mouse   R: return to start" :
            "Click the Game view to capture the mouse and walk.");
    }
}
