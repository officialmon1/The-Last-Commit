using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// First-person movement: WASD walk, Shift sprint, Space jump, mouse look, Esc releases the cursor, left click locks it again.
/// Parents the Main Camera at eye height automatically. Set <see cref="inputLocked"/> to true while a UI is open to freeze input.
/// </summary>
public class FirstPersonController : MonoBehaviour
{
    public float moveSpeed = 4f;
    public float sprintMultiplier = 1.7f;
    public float jumpHeight = 1.0f;
    public float gravity = -18f;
    public float mouseSensitivity = 0.08f;
    [Tooltip("Look speed with the arrow keys (degrees/second)")] public float keyLookSpeed = 90f;
    public float eyeHeight = 1.62f;
    [Tooltip("Empty = Camera.main")] public Camera cam;

    /// <summary>When true, movement and look input are ignored (used while a UI is open).</summary>
    public bool inputLocked;

    CharacterController _cc;
    float _yaw, _pitch, _vy;
    bool _wasLocked;
    int _skipMouseFrames;
    float _lockedAt = -1f;

    public Camera Cam => cam;
    /// <summary>Pitch (degrees, positive = down). External code (e.g. the death sequence) may drive it while input is locked.</summary>
    public float Pitch { get => _pitch; set => _pitch = value; }
    public float Yaw { get => _yaw; set => _yaw = value; }

    void Awake()
    {
        _cc = GetComponent<CharacterController>();
        if (_cc == null)
        {
            _cc = gameObject.AddComponent<CharacterController>();
            _cc.height = 1.8f;
            _cc.radius = 0.35f;
            _cc.center = new Vector3(0f, 0.9f, 0f);
            _cc.stepOffset = 0.3f;
        }
        if (cam == null) cam = Camera.main;
        cam.transform.SetParent(transform, false);
        cam.transform.localPosition = new Vector3(0f, eyeHeight, 0.1f);
        cam.transform.localRotation = Quaternion.identity;
        _yaw = transform.eulerAngles.y;
        Cursor.lockState = CursorLockMode.Locked;
    }

    void Update()
    {
        var kb = Keyboard.current;
        var mouse = Mouse.current;
        if (!inputLocked && kb != null && kb.escapeKey.wasPressedThisFrame) Cursor.lockState = CursorLockMode.None;
        if (!inputLocked && mouse != null && mouse.leftButton.wasPressedThisFrame && Cursor.lockState != CursorLockMode.Locked)
            Cursor.lockState = CursorLockMode.Locked;

        // Skip the first frames after locking: the OS warping the cursor to the center produces a huge fake delta
        bool locked = Cursor.lockState == CursorLockMode.Locked;
        if (locked && !_wasLocked) _lockedAt = Time.unscaledTime;
        _wasLocked = locked;
        _skipMouseFrames = Time.unscaledTime - _lockedAt < 0.3f ? 1 : 0;

        Vector3 move = Vector3.zero;
        bool jump = false, sprint = false;
        if (!inputLocked && locked)
        {
            if (mouse != null && _skipMouseFrames == 0)
            {
                var d = mouse.delta.ReadValue() * mouseSensitivity;
                _yaw += d.x;
                _pitch = Mathf.Clamp(_pitch - d.y, -85f, 85f);
            }
            if (kb != null)
            {
                // arrow keys also turn the view (for players without a mouse)
                float ky = (kb.rightArrowKey.isPressed ? 1f : 0f) - (kb.leftArrowKey.isPressed ? 1f : 0f);
                float kp = (kb.downArrowKey.isPressed ? 1f : 0f) - (kb.upArrowKey.isPressed ? 1f : 0f);
                _yaw += ky * keyLookSpeed * Time.deltaTime;
                _pitch = Mathf.Clamp(_pitch + kp * keyLookSpeed * Time.deltaTime, -85f, 85f);

                float x = (kb.dKey.isPressed ? 1f : 0f) - (kb.aKey.isPressed ? 1f : 0f);
                float z = (kb.wKey.isPressed ? 1f : 0f) - (kb.sKey.isPressed ? 1f : 0f);
                move = Vector3.ClampMagnitude(new Vector3(x, 0f, z), 1f);
                jump = kb.spaceKey.wasPressedThisFrame;
                sprint = kb.leftShiftKey.isPressed;
            }
        }

        transform.rotation = Quaternion.Euler(0f, _yaw, 0f);
        cam.transform.localRotation = Quaternion.Euler(_pitch, 0f, 0f);

        if (_cc.isGrounded)
        {
            if (_vy < 0f) _vy = -2f;
            if (jump) _vy = Mathf.Sqrt(jumpHeight * -2f * gravity);
        }
        _vy += gravity * Time.deltaTime;
        var vel = transform.TransformDirection(move) * (moveSpeed * (sprint ? sprintMultiplier : 1f)) + Vector3.up * _vy;
        _cc.Move(vel * Time.deltaTime);
    }

    /// <summary>Call when a UI opens/closes: freezes input and shows/hides the cursor.</summary>
    public void SetInputLocked(bool locked)
    {
        inputLocked = locked;
        Cursor.lockState = locked ? CursorLockMode.None : CursorLockMode.Locked;
        Cursor.visible = locked;
    }
}
