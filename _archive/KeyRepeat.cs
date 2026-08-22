// KeyRepeat.cs - inject the same combo multiple times with pauses, to test WeType debounce/dedup
using System;
using System.Runtime.InteropServices;
using System.Threading;

class KeyRepeat {
    [DllImport("user32.dll")]
    static extern uint SendInput(uint n, INPUT[] pInputs, int cbSize);
    [DllImport("user32.dll")]
    static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);
    [DllImport("user32.dll")]
    static extern short MapVirtualKey(ushort uCode, uint uMapType);
    [DllImport("user32.dll")]
    static extern short GetAsyncKeyState(int vKey);
    [DllImport("user32.dll")]
    static extern short GetKeyState(int vKey);

    const int INPUT_KEYBOARD = 1;
    const uint KEYEVENTF_EXTENDEDKEY = 0x0001;
    const uint KEYEVENTF_KEYUP = 0x0002;
    const uint KEYEVENTF_SCANCODE = 0x0008;
    const ushort VK_RMENU = 0xA5;
    const ushort VK_OEM_COMMA = 0xBC;
    const uint MAPVK_VK_TO_VSC = 0;

    [StructLayout(LayoutKind.Sequential)]
    struct INPUT {
        public uint type;
        public KEYBDINPUT ki;
        public uint pad1, pad2, pad3, pad4;
    }
    [StructLayout(LayoutKind.Sequential)]
    struct KEYBDINPUT {
        public ushort wVk; public ushort wScan; public uint dwFlags; public uint time; public UIntPtr dwExtraInfo;
    }

    static void Send(ushort vk, bool down) {
        byte sc = (byte)MapVirtualKey(vk, MAPVK_VK_TO_VSC);
        bool ext = (vk == VK_RMENU);
        uint flags = 0;
        if (ext) flags |= KEYEVENTF_EXTENDEDKEY;
        flags |= KEYEVENTF_SCANCODE;
        if (!down) flags |= KEYEVENTF_KEYUP;
        var inp = new INPUT[1];
        inp[0].type = INPUT_KEYBOARD;
        inp[0].ki.wVk = vk; inp[0].ki.wScan = sc; inp[0].ki.dwFlags = flags;
        SendInput(1, inp, Marshal.SizeOf(typeof(INPUT)));
        keybd_event((byte)vk, sc, flags, UIntPtr.Zero);
    }

    static void Hold() {
        // force release first
        keybd_event((byte)VK_RMENU, (byte)MapVirtualKey(VK_RMENU, MAPVK_VK_TO_VSC), KEYEVENTF_EXTENDEDKEY | KEYEVENTF_KEYUP, UIntPtr.Zero);
        keybd_event((byte)VK_OEM_COMMA, (byte)MapVirtualKey(VK_OEM_COMMA, MAPVK_VK_TO_VSC), KEYEVENTF_KEYUP, UIntPtr.Zero);
        Thread.Sleep(50);
        Send(VK_RMENU, true);
        Thread.Sleep(40);
        Send(VK_OEM_COMMA, true);
    }
    static void Release() {
        Send(VK_OEM_COMMA, false);
        Thread.Sleep(20);
        Send(VK_RMENU, false);
    }

    static string KState() {
        return "RAlt(async=" + (GetAsyncKeyState(VK_RMENU) & 0x8000).ToString("X4") +
               ",sync=" + (GetKeyState(VK_RMENU) & 0x8000).ToString("X4") + ")" +
               " Comma(async=" + (GetAsyncKeyState(VK_OEM_COMMA) & 0x8000).ToString("X4") + ")";
    }

    static void Main() {
        Console.WriteLine("KeyRepeat: inject combo 3 times, 4s apart. Watch WeType each time.");
        Console.WriteLine("Initial state: " + KState());
        for (int i = 1; i <= 3; i++) {
            Console.WriteLine("\n--- Test " + i + " ---");
            Console.Write("HOLD... "); Hold();
            Console.WriteLine("after HOLD: " + KState());
            Console.WriteLine("(holding 3s - watch WeType)");
            Thread.Sleep(3000);
            Release();
            Console.WriteLine("after RELEASE: " + KState());
            Thread.Sleep(1500);
        }
        Console.WriteLine("\nDone. Did WeType pop up 3 times?");
        Console.ReadLine();
    }
}
