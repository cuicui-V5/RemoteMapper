// DefDev.cs - standalone test for IPolicyConfig default-device switching
// Usage:  DefDev list        -> list capture devices + current default
//         DefDev set <id>    -> set default capture to <id>
using System;
using System.Runtime.InteropServices;

class DefDev {
    enum EDataFlow { eRender=0, eCapture=1, eAll=2 }
    enum ERole { eConsole=0, eMultimedia=1, eCommunications=2 }
    [Flags] enum DSTATE : uint { ACTIVE=1, DISABLED=2, NOTPRESENT=4, UNPLUGGED=8, MASK_ALL=15 }

    [StructLayout(LayoutKind.Sequential)]
    struct PK { public Guid fmtid; public uint pid; }

    // IMMDeviceEnumerator
    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IEnum {
        void EnumAudioEndpoints(EDataFlow f, DSTATE s, out IColl c);
        void GetDefaultAudioEndpoint(EDataFlow f, ERole r, out IDev d);
        void GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IDev d);
        void RegisterEndpointNotificationCallback(IntPtr p);
        void UnregisterEndpointNotificationCallback(IntPtr p);
    }
    // IMMDeviceCollection
    [ComImport, Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IColl {
        void GetCount(out uint n);
        void Item(uint i, out IDev d);
    }
    // IMMDevice
    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IDev {
        int Activate();                          // slot0 placeholder (not called)
        void OpenPropertyStore(uint stgm, out IPropStore ps);
        void GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
        void GetState(out DSTATE st);
    }
    // IPropertyStore
    [ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IPropStore {
        void GetCount(out uint n);
        void GetAt(uint i, out PK k);
        void GetValue([In] ref PK k, IntPtr pv);
        int SetValue();                          // slot3 placeholder (not called)
        void Commit();
    }
    // IPolicyConfig — 10 placeholders then SetDefaultEndpoint at slot 10
    [ComImport, Guid("870af99c-171d-4f9e-af0d-e63df40c2bc9")] internal class CPolicyConfigClient { }
    [Guid("f8679f50-850a-41cf-9c72-430f290290c8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IPolicy {
        int M0();int M1();int M2();int M3();int M4();int M5();int M6();int M7();int M8();int M9();
        [PreserveSig] int SetDefaultEndpoint([MarshalAs(UnmanagedType.LPWStr)] string id, ERole r);
    }

    [DllImport("ole32.dll")] static extern int PropVariantClear(IntPtr pv);

    static readonly Guid CLSID_ENUM = new Guid("BCDE0395-E52F-467C-8E3D-C4579291692E");
    static readonly Guid CLSID_POLICY = new Guid("870af99c-171d-4f9e-af0d-e63df40c2bc9");
    static readonly Guid PKEY_NAME = new Guid("a45c254e-df1c-4efd-8020-67d146a850e0");

    static IEnum NewEnum() { return (IEnum)Activator.CreateInstance(Type.GetTypeFromCLSID(CLSID_ENUM)); }
    static IPolicy NewPolicy() { return (IPolicy)new CPolicyConfigClient(); }

    static string ReadName(IPropStore ps) {
        var key = new PK { fmtid = PKEY_NAME, pid = 2 };
        IntPtr pv = Marshal.AllocCoTaskMem(40);
        for (int i = 0; i < 40; i++) Marshal.WriteByte(pv, i, 0);
        try {
            ps.GetValue(ref key, pv);
            short vt = Marshal.ReadInt16(pv);
            if (vt == 31) {                                   // VT_LPWSTR
                IntPtr pwsz = Marshal.ReadIntPtr(pv, 8);      // union starts at offset 8
                return Marshal.PtrToStringUni(pwsz);
            }
            return "(vt=" + vt + ")";
        } finally { PropVariantClear(pv); Marshal.FreeCoTaskMem(pv); }
    }

    static string DefaultId() {
        var e = NewEnum();
        IDev d; e.GetDefaultAudioEndpoint(EDataFlow.eCapture, ERole.eConsole, out d);
        string id; d.GetId(out id);
        return id;
    }

    static void List() {
        var e = NewEnum();
        IColl c; e.EnumAudioEndpoints(EDataFlow.eCapture, DSTATE.ACTIVE, out c);
        uint n; c.GetCount(out n);
        Console.WriteLine("=== Capture devices (ACTIVE): " + n + " ===");
        for (uint i = 0; i < n; i++) {
            IDev d; c.Item(i, out d);
            string id; d.GetId(out id);
            IPropStore ps; d.OpenPropertyStore(0, out ps);
            Console.WriteLine("  [" + i + "] " + id + "\n        = " + ReadName(ps));
        }
        Console.WriteLine("\n=== Current DEFAULT capture (console role): " + DefaultId());
    }

    static void Set(string id) {
        var p = NewPolicy();
        int r1 = p.SetDefaultEndpoint(id, ERole.eConsole);
        int r2 = p.SetDefaultEndpoint(id, ERole.eMultimedia);
        int r3 = p.SetDefaultEndpoint(id, ERole.eCommunications);
        Console.WriteLine("SetDefaultEndpoint(" + id + ")");
        Console.WriteLine("  console=" + r1 + " multimedia=" + r2 + " communications=" + r3 + " (0=S_OK)");
        Console.WriteLine("New default: " + DefaultId());
    }

    static void Main(string[] args) {
        if (args.Length == 0 || args[0] == "list") { List(); return; }
        if (args[0] == "set" && args.Length >= 2) { Set(args[1]); return; }
        Console.WriteLine("Usage: DefDev list | DefDev set <deviceId>");
    }
}
