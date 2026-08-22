// ResampleTest.cs - take decoded PCM, output at different sample rates + lowpass variants
using System;
using System.IO;
using System.Collections.Generic;

class ResampleTest {
    static readonly int[] stepTable = { 7,8,9,10,11,12,13,14,16,17,19,21,23,25,28,31,34,37,41,45,50,55,60,66,73,80,88,97,107,118,130,143,157,173,190,209,230,253,279,307,337,371,408,449,494,544,598,658,724,796,876,963,1060,1166,1282,1411,1552,1707,1878,2066,2272,2499,2749,3024,3327,3660,4026,4428,4871,5358,5894,6484,7132,7845,8630,9493,10442,11487,12635,13899,15289,16818,18500,20350,22385,24623,27086,29794,32767 };
    static readonly int[] indexTable = { -1,-1,-1,-1,2,4,6,8 };
    const int FRAME = 120;
    static byte[] data;

    static void Main() {
        data = File.ReadAllBytes(@"D:\Projects\RemoteMapper\audio.bin");
        // best decode: cont skip5 hi
        short[] raw = DecodeSkipCont(5, true);

        Console.WriteLine("=== generating sample-rate + filter variants for skip5 hi ===");
        // variants: (sr, label, lowpassCutoff or 0)
        Emit("r_16k_raw",    raw, 16000, 0);
        Emit("r_8k_raw",     raw, 8000, 0);       // hypothesis: data is really 8kHz
        Emit("r_16k_lp3k",   raw, 16000, 3000);    // 16kHz + lowpass 3kHz (phone quality)
        Emit("r_16k_lp4k",   raw, 16000, 4000);
        Emit("r_8k_lp3k",    raw, 8000, 3000);
        // also lo-nibble at 8k
        short[] rawLo = DecodeSkipCont(5, false);
        Emit("r_8k_lo",      rawLo, 8000, 0);
        Emit("r_16k_lo",     rawLo, 16000, 0);

        Console.WriteLine();
        Console.WriteLine("Listen: if r_8k_raw is clearer than r_16k_raw, data is 8kHz.");
        Console.WriteLine("If lowpass helps, high-freq quantization noise is the issue.");
    }

    static void Emit(string tag, short[] pcm, int sr, double lpCutoff) {
        var p = DcRemovePctNorm(pcm);
        if (lpCutoff > 0) p = Lowpass(p, sr, lpCutoff);
        string path = @"D:\Projects\RemoteMapper\" + tag + ".wav";
        WriteWav(path, p, sr);
        // report pitch strength at this sample rate (recompute lag window)
        double pitch = PitchStrength(p, sr);
        Console.WriteLine(tag + "  sr=" + sr + (lpCutoff>0?" lp="+lpCutoff:"") + "  pitch=" + pitch.ToString("F3") + "  -> " + Path.GetFileName(path));
    }

    // simple one-pole lowpass
    static short[] Lowpass(short[] pcm, int sr, double cutoff) {
        double rc = 1.0 / (2 * Math.PI * cutoff);
        double dt = 1.0 / sr;
        double a = dt / (rc + dt);
        double[] y = new double[pcm.Length];
        y[0] = pcm[0];
        for (int i = 1; i < pcm.Length; i++) y[i] = y[i-1] + a * (pcm[i] - y[i-1]);
        // normalize back
        double peak = 0; for (int i = 0; i < y.Length; i++) { double v = Math.Abs(y[i]); if (v > peak) peak = v; }
        double g = peak > 0 ? (0.7 * 32767) / peak : 1;
        short[] o = new short[pcm.Length];
        for (int i = 0; i < pcm.Length; i++) o[i] = (short)Math.Max(-32768, Math.Min(32767, (int)Math.Round(y[i] * g)));
        return o;
    }

    static double PitchStrength(short[] pcm, int sr) {
        // voice pitch 80-400Hz -> lag = sr/freq
        int lagMin = (int)(sr / 400.0), lagMax = (int)(sr / 80.0);
        if (lagMin < 2) lagMin = 2;
        int start = pcm.Length / 4, len = Math.Min(sr, pcm.Length - start);
        double mean = 0; for (int i = start; i < start + len; i++) mean += pcm[i]; mean /= len;
        double[] x = new double[len];
        for (int i = 0; i < len; i++) x[i] = pcm[start + i] - mean;
        double energy = 0; for (int i = 0; i < len; i++) energy += x[i] * x[i];
        if (energy < 1) return 0;
        double best = 0;
        for (int lag = lagMin; lag <= lagMax && lag < len; lag++) {
            double c = 0;
            for (int i = 0; i < len - lag; i++) c += x[i] * x[i + lag];
            c /= energy;
            if (c > best) best = c;
        }
        return best;
    }

    static short[] DecodeSkipCont(int skip, bool hi) {
        var list = new List<short>();
        int p = 0, si = 0;
        for (int off = 0; off + FRAME <= data.Length; off += FRAME) {
            for (int i = off + skip; i < off + FRAME; i++) {
                list.Add((short)Nibble(hi ? data[i] >> 4 : data[i] & 0xF, ref p, ref si));
                list.Add((short)Nibble(hi ? data[i] & 0xF : data[i] >> 4, ref p, ref si));
            }
        }
        return list.ToArray();
    }

    static short[] DcRemovePctNorm(short[] pcm) {
        double mean = 0; for (int i = 0; i < pcm.Length; i++) mean += pcm[i]; mean /= pcm.Length;
        var abs = new List<double>(); for (int i = 0; i < pcm.Length; i++) abs.Add(Math.Abs(pcm[i] - mean));
        abs.Sort();
        double p98 = abs[(int)(abs.Count * 0.98)];
        double gain = p98 > 1 ? (0.7 * 32767) / p98 : 1;
        var o = new short[pcm.Length];
        for (int i = 0; i < pcm.Length; i++) o[i] = (short)Math.Max(-32768, Math.Min(32767, (int)Math.Round((pcm[i] - mean) * gain)));
        return o;
    }

    static int Nibble(int nibble, ref int predictor, ref int stepIndex) {
        int step = stepTable[stepIndex];
        int diff = step >> 3;
        if ((nibble & 1) != 0) diff += step >> 2;
        if ((nibble & 2) != 0) diff += step >> 1;
        if ((nibble & 4) != 0) diff += step;
        if ((nibble & 8) != 0) predictor -= diff; else predictor += diff;
        if (predictor > 32767) predictor = 32767;
        if (predictor < -32768) predictor = -32768;
        stepIndex += indexTable[nibble & 7];
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
