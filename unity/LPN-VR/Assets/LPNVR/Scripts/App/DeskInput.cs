// Masaüstü (başlıksız test) giriş katmanı: Input System varsa onu, yoksa eski Input Manager'ı kullanır.
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace LPNVR
{
    public static class DeskInput
    {
#if ENABLE_INPUT_SYSTEM
        static UnityEngine.InputSystem.Key ToKey(string k)
        {
            switch (k)
            {
                case "tab": return UnityEngine.InputSystem.Key.Tab; case "space": return UnityEngine.InputSystem.Key.Space; case "escape": return UnityEngine.InputSystem.Key.Escape;
                case "up": return UnityEngine.InputSystem.Key.UpArrow; case "down": return UnityEngine.InputSystem.Key.DownArrow; case "left": return UnityEngine.InputSystem.Key.LeftArrow; case "right": return UnityEngine.InputSystem.Key.RightArrow;
                case "shift": return UnityEngine.InputSystem.Key.LeftShift; case "ctrl": return UnityEngine.InputSystem.Key.LeftCtrl;
            }
            if (k.Length == 1 && k[0] >= '0' && k[0] <= '9') return UnityEngine.InputSystem.Key.Digit0 + (k[0] - '0');
            if (k.Length == 1 && k[0] >= 'a' && k[0] <= 'z') return UnityEngine.InputSystem.Key.A + (k[0] - 'a');
            return UnityEngine.InputSystem.Key.None;
        }
        public static bool Key(string k) { var kb = Keyboard.current; var key = ToKey(k); return kb != null && key != UnityEngine.InputSystem.Key.None && kb[key].isPressed; }
        public static bool KeyDown(string k) { var kb = Keyboard.current; var key = ToKey(k); return kb != null && key != UnityEngine.InputSystem.Key.None && kb[key].wasPressedThisFrame; }
        public static Vector2 MousePos => Mouse.current != null ? Mouse.current.position.ReadValue() : Vector2.zero;
        public static Vector2 MouseDelta => Mouse.current != null ? Mouse.current.delta.ReadValue() : Vector2.zero;
        public static bool MouseHeld(int b) { var m = Mouse.current; if (m == null) return false; return b == 0 ? m.leftButton.isPressed : b == 1 ? m.rightButton.isPressed : m.middleButton.isPressed; }
        public static bool MouseDown(int b) { var m = Mouse.current; if (m == null) return false; return b == 0 ? m.leftButton.wasPressedThisFrame : b == 1 ? m.rightButton.wasPressedThisFrame : m.middleButton.wasPressedThisFrame; }
        public static bool MouseUp(int b) { var m = Mouse.current; if (m == null) return false; return b == 0 ? m.leftButton.wasReleasedThisFrame : b == 1 ? m.rightButton.wasReleasedThisFrame : m.middleButton.wasReleasedThisFrame; }
        public static float Wheel { get { float y = Mouse.current != null ? Mouse.current.scroll.ReadValue().y : 0; return y == 0 ? 0 : -Mathf.Sign(y) * 100; } } // DOM deltaY yönü, çentik başına ~100
#else
        static KeyCode ToKey(string k)
        {
            switch (k)
            {
                case "tab": return KeyCode.Tab; case "space": return KeyCode.Space; case "escape": return KeyCode.Escape;
                case "up": return KeyCode.UpArrow; case "down": return KeyCode.DownArrow; case "left": return KeyCode.LeftArrow; case "right": return KeyCode.RightArrow;
                case "shift": return KeyCode.LeftShift; case "ctrl": return KeyCode.LeftControl;
            }
            if (k.Length == 1 && k[0] >= '0' && k[0] <= '9') return KeyCode.Alpha0 + (k[0] - '0');
            if (k.Length == 1 && k[0] >= 'a' && k[0] <= 'z') return KeyCode.A + (k[0] - 'a');
            return KeyCode.None;
        }
        public static bool Key(string k) => Input.GetKey(ToKey(k));
        public static bool KeyDown(string k) => Input.GetKeyDown(ToKey(k));
        public static Vector2 MousePos => Input.mousePosition;
        public static Vector2 MouseDelta => new Vector2(Input.GetAxis("Mouse X"), Input.GetAxis("Mouse Y")) * 10;
        public static bool MouseHeld(int b) => Input.GetMouseButton(b);
        public static bool MouseDown(int b) => Input.GetMouseButtonDown(b);
        public static bool MouseUp(int b) => Input.GetMouseButtonUp(b);
        public static float Wheel => -Mathf.Sign(Input.mouseScrollDelta.y) * (Input.mouseScrollDelta.y != 0 ? 100 : 0);
#endif
    }
}
