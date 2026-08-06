// CaptureCable.cs - record from "CABLE Output" capture device for N seconds -> WAV
// Use alongside RemoteMic (hotkey off) to verify real-time decode quality through CABLE.
using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

class CaptureCable {
    [DllImport("winmm.dll", CharSet = CharSet.Unicode)]
    static extern int waveInGetDevCaps(uint id, ref WAVEINCAPS pwic, int cbwic);
    [DllImport("winmm.dll")]
    static extern int waveInOpen(out IntPtr phwi, uint id, ref WAVEFORMATEX pwfx, IntPtr cb, IntPtr inst, uint fdo);
    [DllImport("winmm.dll")]
    static extern int waveInPrepareHeader(IntPtr hwi, IntPtr pwh, int cbwh);
    [DllImport("winmm.dll")]
    static extern int waveInAddBuffer(IntPtr hwi, IntPtr pwh, int cbwh);
    [DllImport("winmm.dll")]
    static extern int waveInStart(IntPtr hwi);
    [DllImport("winmm.dll")]
    static extern int waveInStop(IntPtr hwi);
    [DllImport("winmm.dll")]
    static extern int waveInReset(IntPtr hwi);
    [DllImport("winmm.dll")]
    static extern int waveInClose(IntPtr hwi);
    [DllImport("winmm.dll")]
    static extern uint waveInGetNumDevs();

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct WAVEINCAPS { public ushort wMid, wPid; public uint v; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string name; public uint df; public ushort ch, r1; public uint sup; }
    [StructLayout(LayoutKind.Sequential)]
    struct WAVEFORMATEX { public ushort tag; public ushort ch; public uint sr; public uint avg; public ushort blk; public ushort bits; public ushort cb; }

    const int HDR_SIZE = 48, OFF_FLAGS = 24, OFF_RECORDED = 12, WHDR_DONE = 1;
    static IntPtr hWave;
    static IntPtr[] hdr, data;
    static int sr = 16000, totalSec;

    static void Main(string[] args) {
        totalSec = args.Length > 0 ? int.Parse(args[0]) : 5;
        // find CABLE Output
        uint n = waveInGetNumDevs(); uint idx = 0xFFFFFFFF; bool found = false;
        Console.WriteLine("Capture devices:");
        for (uint i = 0; i < n; i++) {
            var c = new WAVEINCAPS();
            waveInGetDevCaps(i, ref c, Marshal.SizeOf(c));
            Console.WriteLine("  ["+i+"] "+c.name);
            if (c.name.Contains("CABLE Output")) { idx = i; found = true; }
        }
        if (!found) { Console.WriteLine("CABLE Output not found!"); return; }

        var wfx = new WAVEFORMATEX { tag = 1, ch = 1, sr = (uint)sr, avg = (uint)(sr * 2), blk = 2, bits = 16, cb = 0 };
        int hr = waveInOpen(out hWave, idx, ref wfx, IntPtr.Zero, IntPtr.Zero, 0);
        if (hr != 0) { Console.WriteLine("waveInOpen failed: " + hr); return; }

        int NB = 4; int bufSamples = sr; // 1s per buffer
        hdr = new IntPtr[NB]; data = new IntPtr[NB];
        var captured = new System.Collections.Concurrent.ConcurrentQueue<short[]>();
        for (int i = 0; i < NB; i++) {
            data[i] = Marshal.AllocHGlobal(bufSamples * 2);
            hdr[i] = Marshal.AllocHGlobal(HDR_SIZE);
            for (int o = 0; o < HDR_SIZE; o++) Marshal.WriteByte(hdr[i], o, 0);
            Marshal.WriteIntPtr(hdr[i], 0, data[i]);
            Marshal.WriteInt32(hdr[i], 8, bufSamples * 2);
            waveInPrepareHeader(hWave, hdr[i], HDR_SIZE);
            waveInAddBuffer(hWave, hdr[i], HDR_SIZE);
        }
        Console.WriteLine("\nRecording " + totalSec + "s from CABLE Output...");
        Console.WriteLine(">>> HOLD the voice button and SPEAK NOW <<<");
        waveInStart(hWave);

        long endTicks = DateTime.Now.AddSeconds(totalSec).Ticks;
        while (DateTime.Now.Ticks < endTicks) {
            for (int i = 0; i < NB; i++) {
                if ((Marshal.ReadInt32(hdr[i], OFF_FLAGS) & WHDR_DONE) != 0) {
                    int rec = Marshal.ReadInt32(hdr[i], OFF_RECORDED);
                    var copy = new short[rec / 2];
                    Marshal.Copy(data[i], copy, 0, rec / 2);
                    captured.Enqueue(copy);
                    // requeue
                    Marshal.WriteInt32(hdr[i], 8, bufSamples * 2);
                    Marshal.WriteInt32(hdr[i], OFF_FLAGS, 0);
                    waveInAddBuffer(hWave, hdr[i], HDR_SIZE);
                }
            }
            Thread.Sleep(50);
        }
        waveInStop(hWave); waveInReset(hWave); waveInClose(hWave);

        // assemble + write WAV
        var all = new System.Collections.Generic.List<short>();
        foreach (var a in captured) all.AddRange(a);
        string path = @"D:\Projects\RemoteMapper\cable_capture.wav";
        WriteWav(path, all.ToArray(), sr);
        Console.WriteLine("\nCaptured " + all.Count + " samples (" + (all.Count/(double)sr).ToString("0.00") + "s) -> " + path);
    }

    static void WriteWav(string path, short[] pcm, int sr) {
        int ds = pcm.Length * 2;
        using (var fs = new FileStream(path, FileMode.Create))
        using (var w = new BinaryWriter(fs)) {
            var a = System.Text.Encoding.ASCII;
            w.Write(a.GetBytes("RIFF")); w.Write(36 + ds); w.Write(a.GetBytes("WAVE"));
            w.Write(a.GetBytes("fmt ")); w.Write(16); w.Write((short)1); w.Write((short)1);
            w.Write(sr); w.Write(sr * 2); w.Write((short)2); w.Write((short)16);
            w.Write(a.GetBytes("data")); w.Write(ds);
            byte[] b = new byte[ds]; Buffer.BlockCopy(pcm, 0, b, 0, ds); w.Write(b);
        }
    }
}
