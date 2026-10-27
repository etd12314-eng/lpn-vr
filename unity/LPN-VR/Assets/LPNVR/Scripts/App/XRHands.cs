// Quest kontrolcüleri (OpenXR, Input System) — kullanım (usage) tabanlı bağlamalar, Touch / Touch Plus profilleriyle çalışır.
using UnityEngine;
using UnityEngine.InputSystem;

namespace LPNVR
{
    public class XRHand
    {
        public readonly string hand;
        readonly InputAction pos, rot, aimPos, aimRot, trigger, grip, primary, secondary, stick, stickClick, haptic;
        public Vector3 position, aimPosition; public Quaternion rotation = Quaternion.identity, aimRotation = Quaternion.identity;
        public float triggerValue; public bool gripPressed, primaryPressed, secondaryPressed, stickPressed; public Vector2 stickValue;
        public bool connected;
        readonly System.Collections.Generic.Dictionary<string, bool> prev = new System.Collections.Generic.Dictionary<string, bool>();

        public XRHand(string hand)
        {
            this.hand = hand;
            string h = hand == "left" ? "{LeftHand}" : "{RightHand}";
            InputAction A(string name, string path, InputActionType type = InputActionType.Value, string ctrl = null)
            {
                var a = new InputAction(name + "-" + hand, type, "<XRController>" + h + "/" + path, expectedControlType: ctrl);
                a.Enable(); return a;
            }
            pos = A("pos", "devicePosition", ctrl: "Vector3");
            rot = A("rot", "deviceRotation", ctrl: "Quaternion");
            aimPos = A("aimPos", "pointerPosition", ctrl: "Vector3");
            aimRot = A("aimRot", "pointerRotation", ctrl: "Quaternion");
            trigger = A("trigger", "{Trigger}", ctrl: "Axis");
            grip = A("grip", "{GripButton}", InputActionType.Button);
            primary = A("primary", "{PrimaryButton}", InputActionType.Button);
            secondary = A("secondary", "{SecondaryButton}", InputActionType.Button);
            stick = A("stick", "{Primary2DAxis}", ctrl: "Vector2");
            stickClick = A("stickClick", "{Primary2DAxisClick}", InputActionType.Button);
            haptic = A("haptic", "{Haptic}", InputActionType.PassThrough);
        }

        // rig: izleme uzayından dünyaya
        public void Poll(Transform rig)
        {
            connected = pos.controls.Count > 0;
            if (!connected) return;
            position = rig.TransformPoint(pos.ReadValue<Vector3>());
            rotation = rig.rotation * rot.ReadValue<Quaternion>();
            if (aimPos.controls.Count > 0)
            {
                aimPosition = rig.TransformPoint(aimPos.ReadValue<Vector3>());
                aimRotation = rig.rotation * aimRot.ReadValue<Quaternion>();
            }
            else { aimPosition = position; aimRotation = rotation * Quaternion.Euler(60, 0, 0); }
            triggerValue = trigger.ReadValue<float>();
            gripPressed = grip.IsPressed();
            primaryPressed = primary.IsPressed();
            secondaryPressed = secondary.IsPressed();
            stickPressed = stickClick.IsPressed();
            stickValue = stick.ReadValue<Vector2>();
        }

        // Yükselen kenar
        public bool Edge(string key, bool v)
        {
            prev.TryGetValue(key, out bool was);
            prev[key] = v;
            return v && !was;
        }

        public void Haptic(float amp, float dur)
        {
            if (!connected || amp <= 0.01f) return;
            try { UnityEngine.XR.OpenXR.Input.OpenXRInput.SendHapticImpulse(haptic, Mathf.Clamp01(amp), dur, haptic.controls.Count > 0 ? haptic.controls[0].device : null); }
            catch (System.Exception) { }
        }
    }
}
