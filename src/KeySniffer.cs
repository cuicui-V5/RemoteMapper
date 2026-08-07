// KeySniffer.cs - global low-level keyboard hook to capture ALL key events
// Shows physical vs injected events. Run it, press remote voice button, see what HID events fire.
using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms; // need for ApplicationContext / message loop

class KeySniffer {
    delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);
    [DllImport("user32.dll")] static extern bool UnhookWindowsHookEx(IntPtr hhk);
    [DllImport("user32.dll")] static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);
    [DllImport("kernel32.dll")] static extern IntPtr GetModuleHandle(string lpModuleName);
    [DllImport("user32.dll")] static extern short MapVirtualKey(uint uCode, uint uMapType);

    const int WH_KEYBOARD_LL = 13;
    const int WM_KEYDOWN = 0x0100, WM_KEYUP = 0x0101, WM_SYSKEYDOWN = 0x0104, WM_SYSKEYUP = 0x0105;
    const uint LLKHF_EXTENDED = 0x01, LLKHF_INJECTED = 0x10, LLKHF_LOWER_IL_INJECTED = 0x02;

    [StructLayout(LayoutKind.Sequential)]
    struct KBDLLHOOKSTRUCT { public uint vkCode; public uint scanCode; public uint flags; public uint time; public IntPtr dwExtraInfo; }

    static IntPtr hhk;
    static LowLevelKeyboardProc proc = HookProc;
    static int eventCount = 0;

    static string VkName(uint vk) {
        switch (vk) {
            case 0xA0: return "LSHIFT"; case 0xA1: return "RSHIFT";
            case 0xA2: return "LCTRL"; case 0xA3: return "RCTRL";
            case 0xA4: return "LALT"; case 0xA5: return "RALT(AltGr)";
            case 0x5B: return "LWIN"; case 0x5C: return "RWIN";
            case 0xBC: return "OEM_COMMA";
            case 0x14: return "CAPSLOCK"; case 0x90: return "NUMLOCK";
            case 0x91: return "SCROLLLOCK";
            default: return vk >= 0x30 && vk <= 0x5A ? ((char)vk).ToString() : ("VK_0x" + vk.ToString("X2"));
        }
    }

    static IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam) {
        if (nCode >= 0) {
            var k = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
            int msg = wParam.ToInt32();
            string act = (msg == WM_KEYDOWN || msg == WM_SYSKEYDOWN) ? "DOWN" : "UP";
            bool isSys = (msg == WM_SYSKEYDOWN || msg == WM_SYSKEYUP);
            string flags = "";
            if ((k.flags & LLKHF_EXTENDED) != 0) flags += "EXT ";
            if ((k.flags & LLKHF_INJECTED) != 0) flags += "INJ ";
            if ((k.flags & LLKHF_LOWER_IL_INJECTED) != 0) flags += "LOWIL ";
            eventCount++;
            Console.WriteLine(String.Format("[{0,4}] {1,5} {2} sc={3,-3} {4}{5}",
                eventCount, act, VkName(k.vkCode).PadRight(12), k.scanCode,
                flags, isSys ? "(sys)" : ""));
        }
        return CallNextHookEx(hhk, nCode, wParam, lParam);
    }

    static void Main() {
        Console.WriteLine("KeySniffer: global keyboard hook");
        Console.WriteLine("Capturing ALL key events (physical + injected).");
        Console.WriteLine(">>> Now press the REMOTE VOICE BUTTON (hold 2s, release).");
        Console.WriteLine(">>> Also try physical RAlt+Comma to compare.");
        Console.WriteLine(">>> Press ESC in this window to stop.\n");

        using (var procHandle = Process.GetCurrentProcess())
        using (var mod = procHandle.MainModule)
            hhk = SetWindowsHookEx(WH_KEYBOARD_LL, proc, GetModuleHandle(mod.ModuleName), 0);

        // message pump via Application.Run
        var ctx = new ApplicationContext();
        Application.ApplicationExit += (s, e) => UnhookWindowsHookEx(hhk);
        Application.Run(ctx);
    }
}
