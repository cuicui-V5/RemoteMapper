// CleanDecode.cs - headerless continuous decode (the correct model per ByteUnique)
// ALL 120 bytes per frame are ADPCM data. No header. State continuous across frames.
using System;
using System.IO;
using System.Collections.Generic;

class CleanDecode {
    static readonly int[] STEP = { 7,8,9,10,11,12,13,14,16,17,19,21,23,25,28,31,34,37,41,45,50,55,60,66,73,80,88,97,107,118,130,143,157,173,190,209,230,253,279,307,337,371,408,449,494,544,598,658,724,796,876,963,1060,1166,1282,1411,1552,1707,1878,2066,2272,2499,2749,3024,3327,3660,4026,4428,4871,5358,5894,6484,7132,7845,8630,9493,10442,11487,12635,13899,15289,16818,18500,20350,22385,24623,27086,29794,32767 };
    static readonly int[] IDX = { -1,-1,-1,-1,2,4,6,8 };
    const int FRAME = 120;
    static byte[] data;

    static void Main() {
        data = File.ReadAllBytes(@"D:\Projects\RemoteMapper\audio.bin");
        int nFrames = data.Length / FRAME;
        int totalBytes = nFrames * FRAME;
        Console.WriteLine("=== HEADERLESS continuous decode: all " + totalBytes + " bytes, state continuous ===");
        Console.WriteLine("frames=" + nFrames + " (" + (totalBytes*2) + " samples = " + (totalBytes*2/16000.0).ToString("F2") + "s @16k / " + (totalBytes*2/8000.0).ToString("F2") + "s @8k)");
        Console.WriteLine();

        // hi-nibble first
        var hi = DecodeCont(true);
        // lo-nibble first
        var lo = DecodeCont(false);

        Report("hi", hi);
        Report("lo", lo);

        // Emit variants. Key comparison: 8k vs 16k playback tempo.
        // hi-first
        Emit("c_hi_16k_raw",  hi, 16000, false);
        Emit("c_hi_16k_pipe", hi, 16000, true);
        Emit("c_hi_8k_raw",   hi, 8000,  false);
        Emit("c_hi_8k_pipe",  hi, 8000,  true);
        // lo-first
        Emit("c_lo_16k_pipe", lo, 16000, true);
        Emit("c_lo_8k_pipe",  lo, 8000,  true);

        Console.WriteLine();
        Console.WriteLine("wrote: c_hi_16k_raw, c_hi_16k_pipe, c_hi_8k_raw, c_hi_8k_pipe, c_lo_16k_pipe, c_lo_8k_pipe");
        Console.WriteLine("KEY: if 8k versions sound right-tempo and 16k sound slow/deep -> data is 8kHz.");
        Console.WriteLine("     if 16k sound right -> data is 16kHz.");
    }

    static void Report(string label, short[] pcm) {
        double rms = 0; int clip = 0;
        for (int i = 0; i < pcm.Length; i++) { double v = pcm[i]; rms += v*v; if (Math.Abs(v) > 30000) clip++; }
        rms = Math.Sqrt(rms/pcm.Length);
        // step index at end of decode = indicator of divergence
        Console.WriteLine(label + ": samples=" + pcm.Length + " rms=" + rms.ToString("F0") + " clip%=" + (100.0*clip/pcm.Length).ToString("F1"));
    }

    // headerless continuous decode
    static short[] DecodeCont(bool hiFirst) {
        int nFrames = data.Length / FRAME;
        var list = new List<short>(nFrames * FRAME * 2);
        int predictor = 0, stepIndex = 0;
        for (int f = 0; f < nFrames; f++) {
            int off = f * FRAME;
            for (int i = off; i < off + FRAME; i++) {
                if (hiFirst) {
                    list.Add((short)Nibble(data[i] >> 4, ref predictor, ref stepIndex));
                    list.Add((short)Nibble(data[i] & 0xF, ref predictor, ref stepIndex));
                } else {
                    list.Add((short)Nibble(data[i] & 0xF, ref predictor, ref stepIndex));
                    list.Add((short)Nibble(data[i] >> 4, ref predictor, ref stepIndex));
                }
            }
        }
        return list.ToArray();
    }

    static void Emit(string name, short[] pcm, int sr, bool pipeline) {
        short[] work = (short[])pcm.Clone();
        if (pipeline) { Declip(work); Lowpass(work); }
        // RMS normalize (ATVVoice uses spike-resistant percentile)
        work = RmsNorm(work);
        WriteWav(@"D:\Projects\RemoteMapper\" + name + ".wav", work, sr);
        Console.WriteLine("  " + name + ".wav  (" + sr + "Hz" + (pipeline? " +declip+lp" : " raw") + ")");
    }

    static void Declip(short[] s) {
        const int TH = 1000;
        for (int i = 1; i < s.Length - 1; i++) {
            int prev = s[i-1], cur = s[i], nxt = s[i+1];
            int dp = Math.Abs(cur - prev), dn = Math.Abs(cur - nxt);
            int nd = Math.Abs(nxt - prev);
            if (dp > TH && dn > TH && Math.Min(dp, dn) > nd * 2)
                s[i] = (short)((prev + nxt) / 2);
        }
    }
    static void Lowpass(short[] s) {
        if (s.Length < 3) return;
        short prev = s[0];
        for (int i = 1; i < s.Length - 1; i++) {
            short cur = s[i];
            s[i] = (short)((prev + 2 * cur + s[i + 1]) >> 2);
            prev = cur;
        }
    }
    static short[] RmsNorm(short[] s) {
        // use 95th percentile of abs for spike resistance (ATVVoice approach)
        var abs = new int[s.Length];
        for (int i = 0; i < s.Length; i++) abs[i] = Math.Abs((int)s[i]);
        var sorted = (int[])abs.Clone(); Array.Sort(sorted);
        int p95 = sorted[(int)(sorted.Length * 0.95)];
        double g = p95 > 1 ? (0.5 * 32767) / p95 : 1;
        var o = new short[s.Length];
        for (int i = 0; i < s.Length; i++) {
            double v = s[i] * g;
            o[i] = (short)Math.Max(-32768, Math.Min(32767, (int)Math.Round(v)));
        }
        return o;
    }
    static int Nibble(int nibble, ref int predictor, ref int stepIndex) {
        int step = STEP[stepIndex];
        int diff = step >> 3;
        if ((nibble & 1) != 0) diff += step >> 2;
        if ((nibble & 2) != 0) diff += step >> 1;
        if ((nibble & 4) != 0) diff += step;
        if ((nibble & 8) != 0) predictor -= diff; else predictor += diff;
        if (predictor > 32767) predictor = 32767;
        if (predictor < -32768) predictor = -32768;
        stepIndex += IDX[nibble & 7];
        if (stepIndex < 0) stepIndex = 0;
        if (stepIndex > 88) stepIndex = 88;
        return predictor;
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
