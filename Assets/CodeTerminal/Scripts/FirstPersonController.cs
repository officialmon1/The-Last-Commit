using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 第一人称移动：WASD 走、Shift 跑、空格跳、鼠标看、Esc 释放鼠标、左键重新锁定。
/// 把 Main Camera 自动挂到自己下面的眼睛位置。UI 打开时把 <see cref="inputLocked"/> 设为 true 即可冻结输入。
/// </summary>
public class FirstPersonController : MonoBehaviour
{
    public float moveSpeed = 4f;
    public float sprintMultiplier = 1.7f;
    public float jumpHeight = 1.0f;
    public float gravity = -18f;
    public float mouseSensitivity = 0.08f;
    [Tooltip("方向键转视角的速度（度/秒）")] public float keyLookSpeed = 90f;
    public float eyeHeight = 1.62f;
    [Tooltip("留空则用 Camera.main")] public Camera cam;

    /// <summary>true 时忽略移动和视角输入（UI 打开时用）。</summary>
    public bool inputLocked;

    CharacterController _cc;
    float _yaw, _pitch, _vy;
    bool _wasLocked;
    int _skipMouseFrames;
    float _lockedAt = -1f;

    public Camera Cam => cam;
    /// <summary>俯仰角（度，向下为正）。输入锁住时外部（如死亡演出）可以直接驱动。</summary>
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

        // 刚锁定鼠标的那几帧，系统把光标挪到屏幕中央会产生一个巨大的假位移，跳过
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
                // 方向键也能转视角（没鼠标时用）
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

    /// <summary>UI 打开/关闭时调用：冻结输入并显示/隐藏鼠标。</summary>
    public void SetInputLocked(bool locked)
    {
        inputLocked = locked;
        Cursor.lockState = locked ? CursorLockMode.None : CursorLockMode.Locked;
        Cursor.visible = locked;
    }
}
