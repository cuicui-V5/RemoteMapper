// Diag.cs - diagnose why decoded audio is inaudible (DC offset? clipping? gain?)
using System;
using System.IO;
using System.Collections.Generic;

class Diag {
    static readonly int[] stepTable = { 7,8,9,10,11,12,13,14,16,17,19,21,23,25,28,31,34,37,41,45,50,55,60,66,73,80,88,97,107,118,130,143,157,173,190,209,230,253,279,307,337,371,408,449,494,544,598,658,724,796,876,963,1060,1166,1282,1411,1552,1707,1878,2066,2272,2499,2749,3024,3327,3660,4026,4428,4871,5358,5894,6484,7132,7845,8630,9493,10442,11487,12635,13899,15289,16818,18500,20350,22385,24623,27086,29794,32767 };
    static readonly int[] indexTable = { -1,-1,-1,-1,2,4,6,8 };
    const int FRAME = 120;
    static byte[] data;

    static void Main() {
        data = File.ReadAllBytes(@"D:\Projects\RemoteMapper\audio.bin");
        Console.WriteLine("=== diagnose skip=4 hi-first (the 'best' candidate) ===");
        var pcm = DecodeSkip(4, true);
        int n = pcm.Length;

        // mean (DC offset)
        double sum = 0; for (int i = 0; i < n; i++) sum += pcm[i];
        double mean = sum / n;
        Console.WriteLine("mean (DC) = " + mean.ToString("F1"));

        // AC RMS (after removing DC)
        double ac = 0; for (int i = 0; i < n; i++) { double d = pcm[i] - mean; ac += d * d; }
        ac = Math.Sqrt(ac / n);
        Console.WriteLine("AC RMS = " + ac.ToString("F1"));

        // percentile analysis of AC
        var absAc = new List<double>();
        for (int i = 0; i < n; i++) absAc.Add(Math.Abs(pcm[i] - mean));
        absAc.Sort();
        Console.WriteLine("AC abs percentiles:  p50=" + absAc[n/2].ToString("F0") + "  p90=" + absAc[(int)(n*0.9)].ToString("F0") + "  p99=" + absAc[(int)(n*0.99)].ToString("F0") + "  max=" + absAc[n-1].ToString("F0"));

        // how many samples are saturated near -32768?
        int satNeg = 0, satPos = 0;
        for (int i = 0; i < n; i++) { if (pcm[i] < -32700) satNeg++; if (pcm[i] > 32700) satPos++; }
        Console.WriteLine("saturated: < -32700 = " + (satNeg*100.0/n).ToString("F1") + "%   > 32700 = " + (satPos*100.0/n).ToString("F1") + "%");

        // first 20 raw samples
        Console.Write("first 20 raw: ");
        for (int i = 0; i < 20; i++) Console.Write(pcm[i] + " ");
        Console.WriteLine();

        // sample at voice region (middle)
        int m = n / 2;
        Console.Write("mid 20 raw: ");
        for (int i = 0; i < 20; i++) Console.Write(pcm[m + i] + " ");
        Console.WriteLine();

        // ---- now produce properly normalized output: DC removal + percentile norm ----
        double target = 0.7 * 32767;
        double p98 = absAc[(int)(n * 0.98)];
        double gain = p98 > 1 ? target / p98 : 1;
        Console.WriteLine("p98=" + p98.ToString("F0") + "  gain=" + gain.ToString("F2"));

        var outPcm = new short[n];
        for (int i = 0; i < n; i++) {
            double v = (pcm[i] - mean) * gain;
            outPcm[i] = (short)Math.Max(-32768, Math.Min(32767, (int)Math.Round(v)));
        }
        WriteWav(@"D:\Projects\RemoteMapper\audio_diag4.wav", outPcm, 16000);

        // also try a fixed strong gain to brute-force listenability
        for (double g = 5; g <= 50; g += 5) {
            var o2 = new short[n];
            int clip2 = 0;
            for (int i = 0; i < n; i++) {
                double v = (pcm[i] - mean) * g;
                if (v > 32767) { v = 32767; clip2++; }
                if (v < -32768) { v = -32768; clip2++; }
                o2[i] = (short)v;
            }
            Console.WriteLine("fixed gain " + g + "x -> clip " + (clip2*100.0/n).ToString("F1") + "%");
        }

        // write with fixed gain 20 and DC removal
        {
            var o2 = new short[n];
            for (int i = 0; i < n; i++) {
                double v = (pcm[i] - mean) * 20;
                if (v > 32767) v = 32767;
                if (v < -32768) v = -32768;
                o2[i] = (short)v;
            }
            WriteWav(@"D:\Projects\RemoteMapper\audio_g20.wav", o2, 16000);
        }
        Console.WriteLine("wrote audio_diag4.wav (p98-norm) and audio_g20.wav (fixed 20x, DC-removed)");
    }

    static short[] DecodeSkip(int skip, bool hiFirst) {
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
