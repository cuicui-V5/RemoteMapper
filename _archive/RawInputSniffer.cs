// RawInputSniffer.cs - grab every keystroke/HID report from all input devices
// Prints: [time] type=... handle=... vk/scan or HID-report-hex  +  source device name
using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

public class Sniffer : NativeWindow {
    [StructLayout(LayoutKind.Sequential)] struct RAWINPUTHEADER { public uint dwType; public uint dwSize; public IntPtr hDevice; public IntPtr wParam; }
    [StructLayout(LayoutKind.Sequential)] struct RAWINPUTDEVICE { public ushort usUsagePage; public ushort usUsage; public uint dwFlags; public IntPtr hwndTarget; }

    [DllImport("user32.dll")] static extern uint GetRawInputData(IntPtr hRawInput, uint uiCommand, IntPtr pData, ref uint pcbSize, uint cbSizeHeader);
    [DllImport("user32.dll")] static extern bool RegisterRawInputDevices([In] RAWINPUTDEVICE[] pRawInputDevices, uint uiNumDevices, uint cbSize);
    [DllImport("user32.dll")] static extern uint GetRawInputDeviceInfo(IntPtr hDevice, uint uiCommand, IntPtr pData, ref uint pcbSize);
    [DllImport("user32.dll")] static extern bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] static extern void PostQuitMessage(int nExitCode);

    const uint RIDEV_INPUTSINK = 0x00000100;
    const uint RID_INPUT = 0x10000003;
    const uint WM_INPUT = 0x00FF;
    const uint WM_QUIT = 0x0012;

    int hdrSize;
    System.Threading.Timer timer;

    public void Run() {
        hdrSize = Marshal.SizeOf(typeof(RAWINPUTHEADER));
        var cp = new CreateParams();
        cp.Caption = "RawInputSniffer";
        this.CreateHandle(cp);

        var devs = new RAWINPUTDEVICE[] {
            new RAWINPUTDEVICE { usUsagePage = 0x01, usUsage = 0x06, dwFlags = RIDEV_INPUTSINK, hwndTarget = this.Handle }, // keyboard
            new RAWINPUTDEVICE { usUsagePage = 0x01, usUsage = 0x02, dwFlags = RIDEV_INPUTSINK, hwndTarget = this.Handle }, // mouse
            new RAWINPUTDEVICE { usUsagePage = 0x0C, usUsage = 0x01, dwFlags = RIDEV_INPUTSINK, hwndTarget = this.Handle }, // consumer
        };
        if (!RegisterRawInputDevices(devs, (uint)devs.Length, (uint)Marshal.SizeOf(typeof(RAWINPUTDEVICE)))) {
            Console.WriteLine("!! RegisterRawInputDevices failed: " + Marshal.GetLastWin32Error());
        }
        Console.WriteLine("== RawInputSniffer ready. Press keys on the remote. ESC to quit, auto-quit in 120s ==");
        Console.Out.Flush();

        timer = new System.Threading.Timer(_ => PostQuitMessage(0), null, 120000, -1);

        Application.Run();
    }

    protected override void WndProc(ref Message m) {
        if (m.Msg == WM_INPUT) {
            HandleRaw(m.LParam);
        }
        base.WndProc(ref m);
    }

    string NameOf(IntPtr hDevice) {
        if (hDevice == IntPtr.Zero) return "(none)";
        uint nsize = 0;
        GetRawInputDeviceInfo(hDevice, 0x20000007, IntPtr.Zero, ref nsize);
        if (nsize == 0) return "(?)";
        int bytes = (int)(nsize * 2);
        IntPtr nb = Marshal.AllocHGlobal(bytes);
        try {
            GetRawInputDeviceInfo(hDevice, 0x20000007, nb, ref nsize);
            byte[] b = new byte[bytes];
            Marshal.Copy(nb, b, 0, bytes);
            var sb = new StringBuilder();
            bool isUtf16 = (bytes >= 4 && b[1] == 0 && b[3] == 0);
            int step = isUtf16 ? 2 : 1;
            for (int i = 0; i + (step-1) < bytes; i += step) {
                char c = isUtf16 ? (char)(b[i] | (b[i+1] << 8)) : (char)b[i];
                if (c == 0) break;
                sb.Append(c < 32 || c > 126 ? '.' : c);
            }
            return sb.ToString();
        } finally { Marshal.FreeHGlobal(nb); }
    }

    bool IsRemote(string src) {
        return src.IndexOf("2717") >= 0 || src.IndexOf("32B8") >= 0;
    }

    void HandleRaw(IntPtr lParam) {
        uint size = 0;
        GetRawInputData(lParam, RID_INPUT, IntPtr.Zero, ref size, (uint)hdrSize);
        if (size == 0) return;
        IntPtr data = Marshal.AllocHGlobal((int)size);
        try {
            if (GetRawInputData(lParam, RID_INPUT, data, ref size, (uint)hdrSize) == 0) return;
            var header = (RAWINPUTHEADER)Marshal.PtrToStructure(data, typeof(RAWINPUTHEADER));
            string t = DateTime.Now.ToString("HH:mm:ss.fff");
            string src = NameOf(header.hDevice);

            if (header.dwType == 1) { // keyboard
                short makeCode = Marshal.ReadInt16(data, hdrSize + 0);
                ushort flags = (ushort)Marshal.ReadInt16(data, hdrSize + 2);
                ushort vkey = (ushort)Marshal.ReadInt16(data, hdrSize + 6);
                string dir = (flags & 1) != 0 ? "UP" : "DOWN";
                if (dir == "UP") return; // only key-down events
                string ext = (flags & 2) != 0 ? " E0" : ((flags & 4) != 0 ? " E1" : "");
                bool rem = IsRemote(src);
                Console.WriteLine("[{0}] {4}KEY vk=0x{1:X2}({3}) scan=0x{2:X2}{5}  dev={6}", t, vkey, makeCode & 0xFF, KeyName(vkey), rem ? "[REMOTE] " : "", ext, src);
                if (vkey == 0x1B) PostQuitMessage(0);
            } else if (header.dwType == 2) { // hid
                uint dwSizeHid = (uint)Marshal.ReadInt32(data, hdrSize + 0);
                uint dwCount = (uint)Marshal.ReadInt32(data, hdrSize + 4);
                int len = (int)(dwSizeHid * dwCount);
                if (len <= 0) return;
                byte[] b = new byte[len];
                Marshal.Copy(data + hdrSize + 8, b, 0, len);
                var sb = new StringBuilder();
                foreach (var by in b) sb.Append(by.ToString("X2") + " ");
                bool rem = IsRemote(src);
                Console.WriteLine("[{0}] {4}HID ({1}B) {2}  dev={3}", t, len, sb.ToString().TrimEnd(), src, rem ? "[REMOTE] " : "");
            } else if (header.dwType == 0) { // mouse
                short x = Marshal.ReadInt16(data, hdrSize + 0);
                short y = Marshal.ReadInt16(data, hdrSize + 2);
                uint btn = (uint)Marshal.ReadInt16(data, hdrSize + 4);
                bool rem = IsRemote(src);
                if (!rem && btn == 0 && x == 0 && y == 0) return; // skip empty mouse motion
                Console.WriteLine("[{0}] {5}MOUSE btn={1} dx={2} dy={3}  dev={4}", t, btn, x, y, src, rem ? "[REMOTE] " : "");
            }
            Console.Out.Flush();
        } finally { Marshal.FreeHGlobal(data); }
    }

    static string KeyName(ushort vk) {
        switch (vk) {
            case 0x08: return "BACK"; case 0x09: return "TAB"; case 0x0D: return "ENTER";
            case 0x1B: return "ESC"; case 0x20: return "SPACE";
            case 0x21: return "PGUP"; case 0x22: return "PGDN"; case 0x23: return "END"; case 0x24: return "HOME";
            case 0x25: return "LEFT"; case 0x26: return "UP"; case 0x27: return "RIGHT"; case 0x28: return "DOWN";
            case 0x2D: return "INS"; case 0x2E: return "DEL";
            case 0x70: case 0x71: case 0x72: case 0x73: case 0x74: case 0x75: case 0x76: case 0x77: case 0x78: case 0x79:
                return "F" + (vk - 0x6F);
            case 0xA0: return "LSHIFT"; case 0xA1: return "RSHIFT"; case 0xA2: return "LCTRL"; case 0xA3: return "RCTRL";
            case 0xA4: return "LWIN"; case 0xA5: return "RWIN"; case 0x5B: return "WINL"; case 0x5C: return "WINR";
            case 0xAD: return "VOL-MUTE"; case 0xAE: return "VOL-DOWN"; case 0xAF: return "VOL-UP";
            case 0xB0: return "MEDIA-NEXT"; case 0xB1: return "MEDIA-PREV"; case 0xB2: return "MEDIA-STOP";
            case 0xB3: return "MEDIA-PLAY"; case 0xB4: return "MAIL"; case 0xB5: return "MEDIASELECT";
            case 0xA6: return "BROWSER-BACK"; case 0xA7: return "BROWSER-FWD"; case 0xA8: return "BROWSER-REFRESH";
            case 0xA9: return "BROWSER-STOP"; case 0xAA: return "BROWSER-SEARCH"; case 0xAB: return "BROWSER-FAV"; case 0xAC: return "BROWSER-HOME";
            default: return ((System.Windows.Forms.Keys)vk).ToString();
        }
    }

    [STAThread]
    static void Main() {
        Console.OutputEncoding = Encoding.UTF8;
        new Sniffer().Run();
        Console.WriteLine("== done ==");
    }
}
