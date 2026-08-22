// SkipDecode.cs - skip first K bytes of each 120-byte frame, continuous predictor
using System;
using System.IO;
using System.Collections.Generic;

class SkipDecode {
    static readonly int[] stepTable = { 7,8,9,10,11,12,13,14,16,17,19,21,23,25,28,31,34,37,41,45,50,55,60,66,73,80,88,97,107,118,130,143,157,173,190,209,230,253,279,307,337,371,408,449,494,544,598,658,724,796,876,963,1060,1166,1282,1411,1552,1707,1878,2066,2272,2499,2749,3024,3327,3660,4026,4428,4871,5358,5894,6484,7132,7845,8630,9493,10442,11487,12635,13899,15289,16818,18500,20350,22385,24623,27086,29794,32767 };
    static readonly int[] indexTable = { -1,-1,-1,-1,2,4,6,8 };
    const int SR = 16000, FRAME = 120;
    static byte[] data;

    static void Main() {
        data = File.ReadAllBytes(@"D:\Projects\RemoteMapper\audio.bin");
        Console.WriteLine("=== skip-continuous decode: skip first K bytes of each 120B frame, predictor continuous ===");
        Console.WriteLine("{0,3} {1,8} {2,8} {3,8} {4,7} {5,7}", "skip", "samples", "rms", "peak", "clip%", "ratio");
        Console.WriteLine("(ratio = rms_voice / rms_silence ; higher = cleaner voice)");
        for (int skip = 0; skip <= 8; skip++) {
            short[] pcm = DecodeSkipContinuous(skip, true);
            double rms = 0, peak = 0; int clip = 0;
            // silence = first 0.3s of samples, voice = middle
            int n = pcm.Length;
            for (int i = 0; i < n; i++) {
                rms += (long)pcm[i] * pcm[i];
                double a = Math.Abs((double)pcm[i]); if (a > peak) peak = a;
                if (pcm[i] >= 32700 || pcm[i] <= -32700) clip++;
            }
            rms = Math.Sqrt(rms / n);
            // voice/silence ratio
            int silEnd = Math.Min(SR * 3 / 10, n);
            double rs = 0; for (int i = 0; i < silEnd; i++) rs += (long)pcm[i] * pcm[i];
            rs = Math.Sqrt(rs / silEnd);
            int vStart = n / 3, vEnd = 2 * n / 3;
            double rv = 0; for (int i = vStart; i < vEnd; i++) rv += (long)pcm[i] * pcm[i];
            rv = Math.Sqrt(rv / (vEnd - vStart));
            double ratio = rs > 1 ? rv / rs : rv;
            Console.WriteLine("{0,3} {1,8} {2,8:F0} {3,8:F0} {4,6:F1}% {5,7:F2}", skip, n, rms, peak, clip * 100.0 / n, ratio);
            WriteWav(@"D:\Projects\RemoteMapper\audio_skip" + skip + ".wav", NormalizeRms(pcm), SR);
        }
        Console.WriteLine();
        Console.WriteLine("wrote audio_skip0.wav .. audio_skip8.wav (RMS-normalized)");
    }

    static short[] DecodeSkipContinuous(int skip, bool hiFirst) {
        var list = new List<short>();
        int predictor = 0, stepIndex = 0;
        for (int off = 0; off + FRAME <= data.Length; off += FRAME) {
            for (int i = off + skip; i < off + FRAME; i++) {
                list.Add((short)Nibble(hiFirst ? data[i] >> 4 : data[i] & 0xF, ref predictor, ref stepIndex));
                list.Add((short)Nibble(hiFirst ? data[i] & 0xF : data[i] >> 4, ref predictor, ref stepIndex));
            }
        }
        return list.ToArray();
    }

    // normalize so RMS = 0.15 * 32767 (~ -16dBFS), with hard clip protection
    static short[] NormalizeRms(short[] pcm) {
        double rms = 0; for (int i = 0; i < pcm.Length; i++) rms += (long)pcm[i] * pcm[i];
        rms = Math.Sqrt(rms / pcm.Length);
        double gain = rms > 1 ? (0.15 * 32767.0) / rms : 1;
        if (gain > 30) gain = 30; // cap gain to avoid exploding silence
        short[] o = new short[pcm.Length];
        for (int i = 0; i < pcm.Length; i++) {
            double v = pcm[i] * gain;
            o[i] = (short)Math.Max(-32768, Math.Min(32767, (int)Math.Round(v)));
        }
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
