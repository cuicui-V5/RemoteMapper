// KeyTest.cs - standalone hotkey injection test for WeType
// Run it, switch to a text box within 3s, it will inject [RAlt+Comma] down, hold 3s, then release.
// Watch whether WeType's voice input pops up.
using System;
using System.Runtime.InteropServices;
using System.Threading;

class KeyTest {
    [DllImport("user32.dll")]
    static extern uint SendInput(uint n, INPUT[] pInputs, int cbSize);
    [DllImport("user32.dll")]
    static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);
    [DllImport("user32.dll")]
    static extern short MapVirtualKey(ushort uCode, uint uMapType);

    const int INPUT_KEYBOARD = 1;
    const uint KEYEVENTF_EXTENDEDKEY = 0x0001;
    const uint KEYEVENTF_KEYUP = 0x0002;
    const uint KEYEVENTF_SCANCODE = 0x0008;
    const ushort VK_LMENU = 0xA4;   // Left Alt
    const ushort VK_RMENU = 0xA5;    // Right Alt
    const ushort VK_OEM_COMMA = 0xBC; // ','
    const uint MAPVK_VK_TO_VSC = 0;

    [StructLayout(LayoutKind.Sequential)]
    struct INPUT {
        public uint type;
        public KEYBDINPUT ki;
        public uint pad1, pad2, pad3, pad4;  // pad to full union size
    }
    [StructLayout(LayoutKind.Sequential)]
    struct KEYBDINPUT {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public UIntPtr dwExtraInfo;
    }

    static void SendFull(ushort vk, bool down) {
        byte sc = (byte)MapVirtualKey(vk, MAPVK_VK_TO_VSC);
        bool ext = (vk == VK_RMENU || vk == VK_LMENU);
        uint flags = 0;
        if (ext) flags |= KEYEVENTF_EXTENDEDKEY;
        flags |= KEYEVENTF_SCANCODE;  // send scan code too (more reliable for hooks)
        if (!down) flags |= KEYEVENTF_KEYUP;

        var inp = new INPUT[1];
        inp[0].type = INPUT_KEYBOARD;
        inp[0].ki.wVk = vk;          // also include VK
        inp[0].ki.wScan = sc;
        inp[0].ki.dwFlags = flags;
        SendInput(1, inp, Marshal.SizeOf(typeof(INPUT)));

        // also fire via keybd_event as belt-and-suspenders
        uint keFlags = flags;
        keybd_event((byte)vk, sc, keFlags, UIntPtr.Zero);
    }

    static void TestCombo(string name, ushort modifier, ushort key) {
        Console.WriteLine("\n>>> Pressing " + name + " DOWN ...");
        SendFull(modifier, true);
        Thread.Sleep(30);
        SendFull(key, true);
        Console.WriteLine(">>> Holding 3 seconds (watch WeType now!) ...");
        Thread.Sleep(3000);
        Console.WriteLine(">>> Releasing " + name + " ...");
        SendFull(key, false);
        Thread.Sleep(10);
        SendFull(modifier, false);
    }

    static void Main(string[] args) {
        Console.WriteLine("KeyTest: hotkey injection diagnostic");
        Console.WriteLine("Switch to a text box / WeType NOW (3 seconds)...");
        Thread.Sleep(3000);

        // Test 1: Right Alt + Comma (the target)
        TestCombo("RightAlt+Comma", VK_RMENU, VK_OEM_COMMA);
        Thread.Sleep(2000);

        // Test 2: Left Alt + Comma
        TestCombo("LeftAlt+Comma", VK_LMENU, VK_OEM_COMMA);
        Thread.Sleep(2000);

        Console.WriteLine("\n=== Done. Did WeType pop up during any test? ===");
        Console.WriteLine("Press Enter to exit.");
        Console.ReadLine();
    }
}
