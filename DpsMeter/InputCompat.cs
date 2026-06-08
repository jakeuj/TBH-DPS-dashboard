using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using UnityEngine;

namespace TbhDpsMeter
{
    /// <summary>
    /// OS-level input via Win32. This game's Unity input (both legacy UnityEngine.Input
    /// and the new Input System) does not deliver live mouse data to plugin code:
    /// Mouse.current.position is frozen and IMGUI events never fire. So we read the
    /// cursor and buttons straight from user32 and map desktop coords into the game
    /// window's client area, which equals IMGUI/GUI coordinates (top-left origin).
    /// Call Poll() once per frame; the accessors return the snapshot/edges.
    /// </summary>
    internal static class InputCompat
    {
        [StructLayout(LayoutKind.Sequential)] private struct POINT { public int X; public int Y; }
        [StructLayout(LayoutKind.Sequential)] private struct RECT { public int Left, Top, Right, Bottom; }
        [DllImport("user32.dll")] private static extern bool GetCursorPos(out POINT p);
        [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] private static extern IntPtr GetActiveWindow();
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr FindWindow(string lpClassName, string lpWindowName);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);
        [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);
        [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr hWnd);
        [DllImport("user32.dll")] private static extern bool ScreenToClient(IntPtr hWnd, ref POINT p);
        [DllImport("user32.dll")] private static extern bool GetClientRect(IntPtr hWnd, out RECT r);
        [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int vKey);
        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        // Low-level mouse hook: lets us SWALLOW a left-click when the cursor is over one of our
        // panels, so the click no longer falls through to the game (or, in windowed mode, the
        // desktop / other apps). Polling can detect a click but cannot consume it; only a hook can.
        [StructLayout(LayoutKind.Sequential)]
        private struct MSLLHOOKSTRUCT { public POINT pt; public uint mouseData; public uint flags; public uint time; public IntPtr dwExtraInfo; }
        private delegate IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr SetWindowsHookEx(int idHook, HookProc lpfn, IntPtr hMod, uint dwThreadId);
        [DllImport("user32.dll", SetLastError = true)] private static extern bool UnhookWindowsHookEx(IntPtr hhk);
        [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern IntPtr GetModuleHandle(string lpModuleName);

        private const int WH_MOUSE_LL = 14;
        private const int HC_ACTION = 0;
        private const int WM_LBUTTONDOWN = 0x0201, WM_LBUTTONUP = 0x0202, WM_LBUTTONDBLCLK = 0x0203;

        private const string UnityWindowClass = "UnityWndClass";

        private const int VK_LBUTTON = 0x01;
        private const int VK_F9 = 0x78;
        private const int VK_PRIOR = 0x21;  // PageUp
        private const int VK_NEXT = 0x22;   // PageDown

        private static int _cw, _ch, _rawX, _rawY;
        private static float _sx = 1f, _sy = 1f;
        private static IntPtr _window;
        private static string _windowSource = "none";

        private static Vector2 _pos;
        private static bool _down, _pressed, _released;
        private static bool _lb, _f9, _pgUp, _pgDn;            // previous key states
        private static int _seenDownSeq, _seenUpSeq;           // last hook edge counts consumed by Poll
        private static bool _f9Edge, _pgUpEdge, _pgDnEdge;

        private static bool Key(int vk) => (GetAsyncKeyState(vk) & 0x8000) != 0;

        // Panel rectangles (GUI coords) the hook should treat as solid. One slot per overlay;
        // each overlay re-registers its rect + visibility every frame from Update().
        private const int MaxPanels = 4;
        private static readonly Rect[] _panelRect = new Rect[MaxPanels];
        private static readonly bool[] _panelOn = new bool[MaxPanels];

        /// <summary>Register an overlay's panel so left-clicks over it are swallowed (not passed
        /// through). slot is a stable per-overlay id (0 DPS / 1 taken / 2 compare).</summary>
        public static void SetPanel(int slot, bool visible, Rect rectGui)
        {
            if (slot < 0 || slot >= MaxPanels) return;
            _panelOn[slot] = visible;
            _panelRect[slot] = rectGui;
        }

        // Drag arbitration: when panels overlap, only one may grab a drag. The id doubles as the
        // z-order (panels are drawn in id order, so a higher id is on top and wins the press).
        private static int _dragOwner = -1, _dragFrame = -1;

        /// <summary>Request drag ownership for this panel on the current press. Returns true only
        /// for the topmost (highest-id) panel that asks this frame; a later higher-id caller steals
        /// it, so lower panels must re-check OwnsDrag() before continuing a drag.</summary>
        public static bool ClaimDrag(int id)
        {
            if (_pressed && _dragFrame != _polledFrame) { _dragFrame = _polledFrame; _dragOwner = -1; }
            if (_dragOwner == id) return true;
            if (_dragOwner == -1 || id > _dragOwner) { _dragOwner = id; return true; }
            return false;
        }

        public static bool OwnsDrag(int id) => _dragOwner == id;
        public static void ReleaseDrag(int id) { if (_dragOwner == id) _dragOwner = -1; }

        private static IntPtr _hookHandle = IntPtr.Zero;
        private static HookProc _hookProc;   // keep a managed ref alive so the GC can't collect the thunk
        private static bool _hookTried;

        private static void EnsureMouseHook()
        {
            if (_hookTried) return;
            _hookTried = true;
            try
            {
                _hookProc = MouseHookCallback;
                _hookHandle = SetWindowsHookEx(WH_MOUSE_LL, _hookProc, GetModuleHandle(null), 0);
                Plugin.Logger?.LogInfo(_hookHandle != IntPtr.Zero
                    ? "Mouse click-through guard installed (WH_MOUSE_LL)."
                    : "WH_MOUSE_LL install failed; panels will still pass clicks through.");
            }
            catch (Exception e) { Plugin.Logger?.LogWarning("mouse hook ex: " + e.Message); }
        }

        /// <summary>Remove the hook (call on shutdown so we don't leave a global hook dangling).</summary>
        public static void UninstallMouseHook()
        {
            try { if (_hookHandle != IntPtr.Zero) { UnhookWindowsHookEx(_hookHandle); _hookHandle = IntPtr.Zero; } }
            catch { }
        }

        // Swallow left-clicks that land on a visible panel so they don't fall through to the game
        // or desktop. Set false to make the hook log-only (diagnostics).
        private static bool _swallowEnabled = true;

        // Left-button state sourced FROM the hook. Swallowing a click stops GetAsyncKeyState from
        // reporting it, so once the hook is installed it becomes our source of truth for the button.
        // Sequence counters latch press/release edges so a down+up inside one frame isn't missed.
        private static volatile bool _hookLbDown;
        private static volatile int _hookDownSeq, _hookUpSeq;

        private static IntPtr MouseHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            try
            {
                if (nCode == HC_ACTION)
                {
                    int msg = wParam.ToInt32();
                    // CRITICAL: this is a SYSTEM-WIDE hook. Only act on clicks when the GAME is the
                    // foreground window — otherwise we'd swallow clicks meant for other apps (e.g. a
                    // browser) just because they land where a panel sits in game coords.
                    if ((msg == WM_LBUTTONDOWN || msg == WM_LBUTTONUP || msg == WM_LBUTTONDBLCLK) && GameIsForeground())
                    {
                        if (msg == WM_LBUTTONUP) { _hookLbDown = false; _hookUpSeq++; }
                        else { _hookLbDown = true; _hookDownSeq++; }

                        var data = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
                        if (OverAnyPanel(data.pt.X, data.pt.Y, out var g, out int slot, out _))
                        {
                            if (Plugin.DebugDamage != null && Plugin.DebugDamage.Value)
                                Plugin.Logger?.LogInfo($"[hook] {(msg == WM_LBUTTONUP ? "up" : "down")} gui=({g.x:0},{g.y:0}) slot={slot} swallow={_swallowEnabled}");
                            if (_swallowEnabled) return (IntPtr)1;   // don't forward to the game or desktop
                        }
                    }
                }
            }
            catch (Exception e) { Plugin.Logger?.LogWarning("[hook] cb ex: " + e.Message); }
            return CallNextHookEx(_hookHandle, nCode, wParam, lParam);
        }

        private static bool OverAnyPanel(int screenX, int screenY, out Vector2 gui, out int slot, out Rect rect)
        {
            slot = -1; rect = default;
            if (!ScreenToGuiPoint(screenX, screenY, out gui)) return false;
            for (int i = 0; i < MaxPanels; i++)
                if (_panelOn[i] && _panelRect[i].Contains(gui)) { slot = i; rect = _panelRect[i]; return true; }
            return false;
        }

        /// <summary>Map a desktop point to IMGUI/GUI coords (same basis as the cursor in Poll()).</summary>
        private static bool ScreenToGuiPoint(int screenX, int screenY, out Vector2 gui)
        {
            gui = default;
            IntPtr h = ResolveGameWindow();
            if (h == IntPtr.Zero) return false;
            var p = new POINT { X = screenX, Y = screenY };
            ScreenToClient(h, ref p);
            int cw = _cw, ch = _ch;
            if (GetClientRect(h, out var rc)) { cw = rc.Right - rc.Left; ch = rc.Bottom - rc.Top; }
            float fx = cw > 0 ? (float)Screen.width / cw : 1f;
            float fy = ch > 0 ? (float)Screen.height / ch : 1f;
            gui = new Vector2(p.X * fx, p.Y * fy);
            return true;
        }

        private static int _polledFrame = -1;
        public static void Poll()
        {
            // run at most once per frame even if several components call it
            int frame = Time.frameCount;
            if (frame == _polledFrame) return;
            _polledFrame = frame;
            EnsureMouseHook();
            try
            {
                if (GetCursorPos(out var p))
                {
                    _rawX = p.X; _rawY = p.Y;
                    IntPtr h = ResolveGameWindow();
                    var cp = p;
                    if (h != IntPtr.Zero)
                    {
                        ScreenToClient(h, ref cp);
                        if (GetClientRect(h, out var rc))
                        {
                            _cw = rc.Right - rc.Left;
                            _ch = rc.Bottom - rc.Top;
                        }
                    }
                    // Scale physical client pixels -> Unity GUI (logical) pixels so the
                    // cursor lines up with the IMGUI rects regardless of DPI/resolution.
                    _sx = _cw > 0 ? (float)Screen.width / _cw : 1f;
                    _sy = _ch > 0 ? (float)Screen.height / _ch : 1f;
                    _pos = new Vector2(cp.X * _sx, cp.Y * _sy);
                }

                if (_hookHandle != IntPtr.Zero)
                {
                    // Hook is the source of truth (GetAsyncKeyState goes blind once we swallow).
                    // Latch edges via the sequence counters so single-frame clicks aren't lost.
                    _pressed = _hookDownSeq != _seenDownSeq; _seenDownSeq = _hookDownSeq;
                    _released = _hookUpSeq != _seenUpSeq; _seenUpSeq = _hookUpSeq;
                    _down = _hookLbDown; _lb = _down;
                }
                else
                {
                    bool lb = Key(VK_LBUTTON);
                    _pressed = lb && !_lb;
                    _released = !lb && _lb;
                    _down = lb; _lb = lb;
                }

                bool f9 = Key(VK_F9); _f9Edge = f9 && !_f9; _f9 = f9;
                bool pu = Key(VK_PRIOR); _pgUpEdge = pu && !_pgUp; _pgUp = pu;
                bool pd = Key(VK_NEXT); _pgDnEdge = pd && !_pgDn; _pgDn = pd;
            }
            catch { }
        }

        /// <summary>True only when the game's own window is the foreground window. Gates the global
        /// mouse hook so it never swallows clicks destined for other applications.</summary>
        private static bool GameIsForeground()
        {
            try
            {
                IntPtr fg = GetForegroundWindow();
                if (fg == IntPtr.Zero) return false;
                IntPtr game = ResolveGameWindow();
                return game != IntPtr.Zero && fg == game;
            }
            catch { return false; }
        }

        private static IntPtr ResolveGameWindow()
        {
            if (_window != IntPtr.Zero && IsWindow(_window))
            {
                return _window;
            }

            _window = FindUnityWindowForCurrentProcess();
            if (_window != IntPtr.Zero)
            {
                _windowSource = "UnityWndClass/pid";
                return _window;
            }

            _window = FindWindow(UnityWindowClass, null);
            if (_window != IntPtr.Zero)
            {
                _windowSource = "UnityWndClass";
                return _window;
            }

            _window = GetActiveWindow();
            if (_window != IntPtr.Zero)
            {
                _windowSource = "active";
                return _window;
            }

            _window = GetForegroundWindow();
            _windowSource = _window != IntPtr.Zero ? "foreground" : "none";
            return _window;
        }

        private static IntPtr FindUnityWindowForCurrentProcess()
        {
            uint currentPid = (uint)Process.GetCurrentProcess().Id;
            IntPtr found = IntPtr.Zero;

            EnumWindows((hWnd, _) =>
            {
                GetWindowThreadProcessId(hWnd, out uint pid);
                if (pid != currentPid) return true;

                var className = new StringBuilder(128);
                if (GetClassName(hWnd, className, className.Capacity) <= 0) return true;
                if (!string.Equals(className.ToString(), UnityWindowClass, StringComparison.Ordinal)) return true;

                found = hWnd;
                return false;
            }, IntPtr.Zero);

            return found;
        }

        public static Vector2 MouseGuiPos() => _pos;
        public static bool MousePressed() => _pressed;
        public static bool MouseHeld() => _down;
        public static bool MouseReleased() => _released;
        public static bool TogglePressed() => _f9Edge;
        public static bool OpacityUpPressed() => _pgUpEdge;
        public static bool OpacityDownPressed() => _pgDnEdge;

        // General per-key edge detection for arbitrary hotkeys (e.g. the damage-taken
        // panel's F10). Call at most once per frame per key.
        private static readonly Dictionary<int, bool> _keyPrev = new Dictionary<int, bool>();
        public static bool TogglePressed(KeyCode key) => KeyPressed(key);
        public static bool KeyPressed(KeyCode key)
        {
            int vk = Vk(key);
            if (vk == 0) return false;
            bool down = Key(vk);
            _keyPrev.TryGetValue(vk, out bool prev);
            _keyPrev[vk] = down;
            return down && !prev;
        }

        private static int Vk(KeyCode k)
        {
            if (k >= KeyCode.F1 && k <= KeyCode.F12) return 0x70 + (int)(k - KeyCode.F1); // F1=0x70
            switch (k)
            {
                case KeyCode.PageUp: return 0x21;
                case KeyCode.PageDown: return 0x22;
                case KeyCode.Home: return 0x24;
                case KeyCode.End: return 0x23;
                case KeyCode.Insert: return 0x2D;
                case KeyCode.Delete: return 0x2E;
                case KeyCode.Tab: return 0x09;
                case KeyCode.Backslash: return 0xDC;
            }
            if (k >= KeyCode.A && k <= KeyCode.Z) return 0x41 + (int)(k - KeyCode.A);
            if (k >= KeyCode.Alpha0 && k <= KeyCode.Alpha9) return 0x30 + (int)(k - KeyCode.Alpha0);
            return 0;
        }

        public static string Probe()
            => $"cursorGui={_pos} raw=({_rawX},{_rawY}) hwnd=0x{_window.ToInt64():X} source={_windowSource} client={_cw}x{_ch} screen={Screen.width}x{Screen.height} scale=({_sx:0.###},{_sy:0.###}) down={_down}";
    }
}
