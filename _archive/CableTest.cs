// CableTest.cs - inject a 440Hz test tone into VB-CABLE via winmm waveOut
using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

class CableTest {
    [DllImport("winmm.dll")]
    static extern uint waveOutGetNumDevs();
    [DllImport("winmm.dll", CharSet = CharSet.Unicode)]
    static extern int waveOutGetDevCaps(uint uDeviceID, ref WAVEOUTCAPS pwoc, int cbwoc);
    [DllImport("winmm.dll")]
    static extern int waveOutOpen(out IntPtr phwi, uint uDeviceID, ref WAVEFORMATEX pwfx, IntPtr dwCallback, IntPtr dwInstance, uint fdwOpen);
    [DllImport("winmm.dll")]
    static extern int waveOutPrepareHeader(IntPtr hwi, ref WAVEHDR pwh, int cbwh);
    [DllImport("winmm.dll")]
    static extern int waveOutWrite(IntPtr hwi, ref WAVEHDR pwh, int cbwh);
    [DllImport("winmm.dll")]
    static extern int waveOutUnprepareHeader(IntPtr hwi, ref WAVEHDR pwh, int cbwh);
    [DllImport("winmm.dll")]
    static extern int waveOutReset(IntPtr hwi);
    [DllImport("winmm.dll")]
    static extern int waveOutClose(IntPtr hwi);
    [DllImport("winmm.dll")]
    static extern int waveOutGetErrorText(int mmrErr, StringBuilder pszText, int cchText);

    const uint WAVE_MAPPER = 0xFFFFFFFF;
    const int WHDR_DONE = 0x00000001;
    const int WHDR_PREPARED = 0x00000002;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct WAVEOUTCAPS {
        public ushort wMid, wPid;
        public uint vDriverVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string szPname;
        public uint dwFormats;
        public ushort wChannels, wReserved1;
        public uint dwSupport;
    }
    [StructLayout(LayoutKind.Sequential)]
    struct WAVEFORMATEX {
        public ushort wFormatTag; public ushort nChannels; public uint nSamplesPerSec;
        public uint nAvgBytesPerSec; public ushort nBlockAlign; public ushort wBitsPerSample; public ushort cbSize;
    }
    [StructLayout(LayoutKind.Sequential)]
    struct WAVEHDR {
        public IntPtr lpData; public uint dwBufferLength; public uint dwBytesRecorded;
        public IntPtr dwUser; public uint dwFlags; public uint dwLoops;
        public IntPtr lpNext; public IntPtr reserved;
    }

    static string MmErr(int e) {
        var sb = new StringBuilder(256); waveOutGetErrorText(e, sb, 256); return sb.ToString();
    }

    static void Main(string[] args) {
        // 1. Enumerate output devices, find CABLE Input
        uint n = waveOutGetNumDevs();
        Console.WriteLine("Output devices (" + n + "):");
        uint cableIdx = 0xFFFFFFFF; bool found = false;
        for (uint i = 0; i < n; i++) {
            var caps = new WAVEOUTCAPS();
            waveOutGetDevCaps(i, ref caps, Marshal.SizeOf(caps));
            string line = "  [" + i + "] " + caps.szPname;
            if (caps.szPname.Contains("CABLE Input")) { line += "  <== TARGET"; cableIdx = i; found = true; }
            Console.WriteLine(line);
        }
        if (!found) { Console.WriteLine("CABLE Input not found!"); return; }
        Console.WriteLine("\nUsing device " + cableIdx + "\n");

        // 2. Open device mono 16kHz 16bit
        var wfx = new WAVEFORMATEX {
            wFormatTag = 1, nChannels = 1, nSamplesPerSec = 16000,
            nAvgBytesPerSec = 32000, nBlockAlign = 2, wBitsPerSample = 16, cbSize = 0
        };
        IntPtr hWave;
        int hr = waveOutOpen(out hWave, cableIdx, ref wfx, IntPtr.Zero, IntPtr.Zero, 0);
        if (hr != 0) { Console.WriteLine("waveOutOpen failed: " + MmErr(hr)); return; }
        Console.WriteLine("Device opened. Playing 440Hz tone for 4 seconds...");
        Console.WriteLine(">>> Open '声音设置 > 录制 > CABLE Output' or run a recorder to hear it <<<\n");

        // 3. Generate tone, play in a loop using 4 buffers
        int sr = 16000;
        int bufSamples = 1600; // 0.1s per buffer
        int bufBytes = bufSamples * 2;
        // pre-generate 4s of 440Hz
        int totalSamples = sr * 4;
        var fullPcm = new short[totalSamples];
        double phase = 0;
        for (int i = 0; i < totalSamples; i++) {
            fullPcm[i] = (short)(Math.Sin(phase) * 20000);
            phase += 2 * Math.PI * 440.0 / sr;
            if (phase > 2 * Math.PI) phase -= 2 * Math.PI;
        }

        // double-buffer: 2 WAVEHDRs
        var hdrs = new WAVEHDR[2];
        var buffers = new short[2][];
        var pBuffers = new IntPtr[2];
        for (int b = 0; b < 2; b++) {
            buffers[b] = new short[bufSamples];
            pBuffers[b] = Marshal.AllocHGlobal(bufBytes);
            hdrs[b] = new WAVEHDR {
                lpData = pBuffers[b], dwBufferLength = (uint)bufBytes,
                dwFlags = 0
            };
            waveOutPrepareHeader(hWave, ref hdrs[b], Marshal.SizeOf(hdrs[b]));
        }

        int pos = 0;
        // prime both buffers
        for (int b = 0; b < 2; b++) {
            Array.Copy(fullPcm, pos, buffers[b], 0, Math.Min(bufSamples, totalSamples - pos));
            Marshal.Copy(buffers[b], 0, pBuffers[b], bufSamples);
            hdrs[b].dwBufferLength = (uint)(Math.Min(bufSamples, totalSamples - pos) * 2);
            waveOutWrite(hWave, ref hdrs[b], Marshal.SizeOf(hdrs[b]));
            pos += bufSamples;
        }

        // feed until done
        while (pos < totalSamples) {
            // find a free buffer
            int b = -1;
            for (int i = 0; i < 2; i++) {
                // re-read header flags by marshalling (struct is a copy, need to query)
                // Use a trick: waveOutUnprepareHeader returns WHDR_DONE error if still playing
            }
            // Simpler: poll using a fresh approach - sleep half a buffer then try to reuse
            Thread.Sleep(bufSamples * 1000 / sr / 2);
            for (int i = 0; i < 2; i++) {
                if (pos >= totalSamples) break;
                // try unprepare; if succeeds, buffer is done
                int ur = waveOutUnprepareHeader(hWave, ref hdrs[i], Marshal.SizeOf(hdrs[i]));
                if (ur == 0) {
                    int cnt = Math.Min(bufSamples, totalSamples - pos);
                    Array.Copy(fullPcm, pos, buffers[i], 0, cnt);
                    Marshal.Copy(buffers[i], 0, pBuffers[i], cnt);
                    hdrs[i].dwBufferLength = (uint)(cnt * 2);
                    waveOutPrepareHeader(hWave, ref hdrs[i], Marshal.SizeOf(hdrs[i]));
                    waveOutWrite(hWave, ref hdrs[i], Marshal.SizeOf(hdrs[i]));
                    pos += cnt;
                }
            }
        }

        // wait for last buffers to finish
        Thread.Sleep(300);
        waveOutReset(hWave);
        for (int b = 0; b < 2; b++) {
            waveOutUnprepareHeader(hWave, ref hdrs[b], Marshal.SizeOf(hdrs[b]));
            Marshal.FreeHGlobal(pBuffers[b]);
        }
        waveOutClose(hWave);
        Console.WriteLine("\nDone. 440Hz tone written to CABLE Input.");
    }
}
