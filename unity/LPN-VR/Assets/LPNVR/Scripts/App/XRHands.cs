// Quest kontrolcüleri (OpenXR, Input System) — kullanım (usage) tabanlı bağlamalar, Touch / Touch Plus profilleriyle çalışır.
using UnityEngine;
using UnityEngine.InputSystem;

namespace LPNVR
{
    public class XRHand
    {
        public readonly string hand;
        readonly InputAction pos, rot, aimPos, aimRot, trigger, triggerBtn, triggerPath, grip, primary, secondary, stick, stickClick, haptic;
        public Vector3 position, aimPosition; public Quaternion rotation = Quaternion.identity, aimRotation = Quaternion.identity;
        public float triggerValue; public bool gripPressed, primaryPressed, secondaryPressed, stickPressed; public Vector2 stickValue;
        public bool connected;
        public bool simulated; // test: alanlar dışarıdan doldurulur, Poll dokunmaz
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
            // yedek bağlamalar: bazı profillerde/çalışma zamanlarında kullanım eşlemesi eksik olabilir
            triggerBtn = A("triggerBtn", "{TriggerButton}", InputActionType.Button);
            triggerPath = A("triggerPath", "trigger", ctrl: "Axis");
            grip = A("grip", "{GripButton}", InputActionType.Button);
            primary = A("primary", "{PrimaryButton}", InputActionType.Button);
            secondary = A("secondary", "{SecondaryButton}", InputActionType.Button);
            stick = A("stick", "{Primary2DAxis}", ctrl: "Vector2");
            stickClick = A("stickClick", "{Primary2DAxisClick}", InputActionType.Button);
            haptic = A("haptic", "{Haptic}", InputActionType.PassThrough);
        }

        // Kaynak durumu (tanı paneli için)
        public string source = "-";

        // rig: izleme uzayından dünyaya. İki kaynak birlikte okunur: Input System eylemleri ve
        // UnityEngine.XR.InputDevices (XR alt sistemi). Biri veri vermezse diğeri kullanılır.
        public void Poll(Transform rig)
        {
            if (simulated) return;
            bool isOk = pos.controls.Count > 0;
            var dev = UnityEngine.XR.InputDevices.GetDeviceAtXRNode(hand == "left" ? UnityEngine.XR.XRNode.LeftHand : UnityEngine.XR.XRNode.RightHand);
            bool xrOk = dev.isValid;
            connected = isOk || xrOk;
            if (!connected) { source = "yok"; triggerValue = 0; return; }

            Vector3 lp = Vector3.zero; Quaternion lq = Quaternion.identity; bool havePose = false;
            if (isOk) { lp = pos.ReadValue<Vector3>(); lq = rot.ReadValue<Quaternion>(); havePose = lq != new Quaternion(0, 0, 0, 0); }
            if (!havePose && xrOk && dev.TryGetFeatureValue(UnityEngine.XR.CommonUsages.devicePosition, out var xp) && dev.TryGetFeatureValue(UnityEngine.XR.CommonUsages.deviceRotation, out var xq))
            { lp = xp; lq = xq; havePose = true; }
            if (lq == new Quaternion(0, 0, 0, 0)) lq = Quaternion.identity;
            position = rig.TransformPoint(lp);
            rotation = rig.rotation * lq;
            if (isOk && aimPos.controls.Count > 0)
            {
                aimPosition = rig.TransformPoint(aimPos.ReadValue<Vector3>());
                var aq = aimRot.ReadValue<Quaternion>(); if (aq == new Quaternion(0, 0, 0, 0)) aq = Quaternion.identity;
                aimRotation = rig.rotation * aq;
            }
            else { aimPosition = position; aimRotation = rotation * Quaternion.Euler(60, 0, 0); }

            float tIS = isOk ? Mathf.Max(trigger.ReadValue<float>(), triggerPath.ReadValue<float>(), triggerBtn.IsPressed() ? 1 : 0) : 0;
            float tXR = 0; bool gXR = false, aXR = false, bXR = false, sXR = false; Vector2 stXR = Vector2.zero;
            if (xrOk)
            {
                dev.TryGetFeatureValue(UnityEngine.XR.CommonUsages.trigger, out tXR);
                if (dev.TryGetFeatureValue(UnityEngine.XR.CommonUsages.triggerButton, out bool tb) && tb) tXR = Mathf.Max(tXR, 1);
                dev.TryGetFeatureValue(UnityEngine.XR.CommonUsages.gripButton, out gXR);
                dev.TryGetFeatureValue(UnityEngine.XR.CommonUsages.primaryButton, out aXR);
                dev.TryGetFeatureValue(UnityEngine.XR.CommonUsages.secondaryButton, out bXR);
                dev.TryGetFeatureValue(UnityEngine.XR.CommonUsages.primary2DAxisClick, out sXR);
                dev.TryGetFeatureValue(UnityEngine.XR.CommonUsages.primary2DAxis, out stXR);
            }
            triggerValue = Mathf.Max(tIS, tXR);
            gripPressed = (isOk && grip.IsPressed()) || gXR;
            primaryPressed = (isOk && primary.IsPressed()) || aXR;
            secondaryPressed = (isOk && secondary.IsPressed()) || bXR;
            stickPressed = (isOk && stickClick.IsPressed()) || sXR;
            var stIS = isOk ? stick.ReadValue<Vector2>() : Vector2.zero;
            stickValue = stIS.sqrMagnitude >= stXR.sqrMagnitude ? stIS : stXR;
            source = $"IS={(isOk ? tIS.ToString("F2") : "yok")} XR={(xrOk ? tXR.ToString("F2") : "yok")}";
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
