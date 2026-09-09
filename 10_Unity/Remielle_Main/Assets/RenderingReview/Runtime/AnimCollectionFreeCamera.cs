// zcode 2026-09-07, AnimCollection free camera: decoupled from the review
// follow rig (build clears RemielleReviewControls.follow). Press RMB to
// toggle look mode (press again to release), WASD for planar translation,
// Space/Shift for world-Z up/down. Uses the Input System package
// exclusively (activeInputHandler=1). Camera has no gravity. Wheel input
// removed at the user's request. Speeds and look sensitivity are halved
// twice from the initial revision per user feedback.
using UnityEngine;
using UnityEngine.InputSystem;

[DefaultExecutionOrder(210)]
public class AnimCollectionFreeCamera : MonoBehaviour
{
    public float lookSensitivityDegPerPixel = 0.0375f;
    public float moveSpeedMetersPerSecond = 3f;
    public float verticalSpeedMetersPerSecond = 3f;
    public float pitchMaxDeg = 85f;
    public float pitchMinDeg = -80f;

    bool looking;

    void Update()
    {
        var mouse = Mouse.current;
        var keyboard = Keyboard.current;
        if (mouse == null || keyboard == null) return;

        // Press RMB once to enter look mode, press again to release.
        // Esc also releases.
        if (mouse.rightButton.wasPressedThisFrame)
        {
            looking = !looking;
            Cursor.visible = !looking;
        }
        if (looking && keyboard.escapeKey.wasPressedThisFrame)
        {
            looking = false;
            Cursor.visible = true;
        }

        if (looking)
        {
            var delta = mouse.delta.ReadValue();
            if (delta != Vector2.zero)
            {
                float yaw = delta.x * lookSensitivityDegPerPixel;
                float pitch = delta.y * lookSensitivityDegPerPixel;
                var euler = transform.eulerAngles;
                euler.y += yaw;
                euler.x = Mathf.Clamp(NormalizePitch(euler.x) - pitch, pitchMinDeg, pitchMaxDeg);
                transform.eulerAngles = euler;
            }
        }

        var forward = Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized;
        var right = Vector3.ProjectOnPlane(transform.right, Vector3.up).normalized;

        var move = Vector3.zero;
        if (keyboard.wKey.isPressed) move += forward;
        if (keyboard.sKey.isPressed) move -= forward;
        if (keyboard.dKey.isPressed) move += right;
        if (keyboard.aKey.isPressed) move -= right;
        if (move.sqrMagnitude > 0f) transform.position += move.normalized * (moveSpeedMetersPerSecond * Time.deltaTime);

        // World-Z vertical traversal; the camera has no gravity and holds
        // height between inputs.
        float vertical = 0f;
        if (keyboard.spaceKey.isPressed) vertical += 1f;
        if (keyboard.leftShiftKey.isPressed) vertical -= 1f;
        if (!Mathf.Approximately(vertical, 0f))
            transform.position += Vector3.up * (vertical * verticalSpeedMetersPerSecond * Time.deltaTime);
    }

    static float NormalizePitch(float deg)
    {
        if (deg > 180f) deg -= 360f;
        return deg;
    }

    void OnDisable() { if (looking) { looking = false; Cursor.visible = true; } }
}
