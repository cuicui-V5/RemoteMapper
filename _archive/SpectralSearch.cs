// SpectralSearch.cs - find correct decode by voice pitch autocorrelation strength
using System;
using System.IO;
using System.Collections.Generic;

class SpectralSearch {
    static readonly int[] stepTable = { 7,8,9,10,11,12,13,14,16,17,19,21,23,25,28,31,34,37,41,45,50,55,60,66,73,80,88,97,107,118,130,143,157,173,190,209,230,253,279,307,337,371,408,449,494,544,598,658,724,796,876,963,1060,1166,1282,1411,1552,1707,1878,2066,2272,2499,2749,3024,3327,3660,4026,4428,4871,5358,5894,6484,7132,7845,8630,9493,10442,11487,12635,13899,15289,16818,18500,20350,22385,24623,27086,29794,32767 };
    static readonly int[] indexTable = { -1,-1,-1,-1,2,4,6,8 };
    const int SR = 16000, FRAME = 120;
    static byte[] data;

    struct R { public string name; public double pitch; public double flatness; public short[] pcm; }

    static void Main() {
        data = File.ReadAllBytes(@"D:\Projects\RemoteMapper\audio.bin");
        var results = new List<R>();

        // continuous decode: skip 0-8, hi/lo nibble order
        for (int skip = 0; skip <= 8; skip++) {
            results.Add(Mk("cont skip" + skip + " hi", DecodeSkipCont(skip, true)));
            results.Add(Mk("cont skip" + skip + " lo", DecodeSkipCont(skip, false)));
        }
        // framed reset: standard ATVV hdr6, both nibble orders
        results.Add(Mk("frame hdr6 ATVV hi", DecodeFramed(6, 3, true, 5, true)));
        results.Add(Mk("frame hdr6 ATVV lo", DecodeFramed(6, 3, true, 5, false)));

        // sort by pitch strength (descending) - strongest voice wins
        results.Sort((a, b) => b.pitch.CompareTo(a.pitch));
        Console.WriteLine("=== ranked by pitch autocorrelation strength (higher = more voice-like) ===");
        Console.WriteLine("{0,-28} {1,8} {2,10}", "config", "pitch", "flatness");
        foreach (var r in results)
            Console.WriteLine("{0,-28} {1,8:F3} {2,10:F3}", r.name, r.pitch, r.flatness);

        // write top 4 (DC-removed, percentile-normalized)
        Console.WriteLine();
        for (int i = 0; i < Math.Min(4, results.Count); i++) {
            var norm = DcRemovePctNorm(results[i].pcm);
            WriteWav(string.Format(@"D:\Projects\RemoteMapper\audio_sp{0}.wav", i), norm, SR);
            Console.WriteLine("audio_sp" + i + ".wav = " + results[i].name + " (pitch=" + results[i].pitch.ToString("F3") + ")");
        }
    }

    static R Mk(string name, short[] pcm) {
        double pitch = PitchStrength(pcm);
        double flat = SpectralFlatness(pcm);
        return new R { name = name, pitch = pitch, flatness = flat, pcm = pcm };
    }

    // pitch strength: max normalized autocorrelation in lag 40..200 (80..400Hz @16kHz)
    static double PitchStrength(short[] pcm) {
        // use a window in the middle of the signal (likely voice)
        int start = pcm.Length / 4, len = Math.Min(SR, pcm.Length - start); // 1s window
        double mean = 0; for (int i = start; i < start + len; i++) mean += pcm[i]; mean /= len;
        double[] x = new double[len];
        for (int i = 0; i < len; i++) x[i] = pcm[start + i] - mean;
        double energy = 0; for (int i = 0; i < len; i++) energy += x[i] * x[i];
        if (energy < 1) return 0;
        double best = 0;
        for (int lag = 40; lag <= 200; lag++) {
            double c = 0;
            for (int i = 0; i < len - lag; i++) c += x[i] * x[i + lag];
            c /= energy;
            if (c > best) best = c;
        }
        return best;
    }

    // spectral flatness via DFT of a window: 0=pure tone(speech-ish), 1=white noise
    static double SpectralFlatness(short[] pcm) {
        int N = 512, start = pcm.Length / 4;
        if (start + N > pcm.Length) return 1;
        double[] x = new double[N];
        double mean = 0; for (int i = 0; i < N; i++) mean += pcm[start + i]; mean /= N;
        for (int i = 0; i < N; i++) x[i] = (pcm[start + i] - mean) * (0.5 - 0.5 * Math.Cos(2 * Math.PI * i / (N - 1))); // Hann
        // naive DFT magnitude at bins 1..N/2
        double[] mag = new double[N / 2];
        for (int k = 1; k < N / 2; k++) {
            double re = 0, im = 0;
            for (int i = 0; i < N; i++) { double a = -2 * Math.PI * k * i / N; re += x[i] * Math.Cos(a); im += x[i] * Math.Sin(a); }
            mag[k] = Math.Sqrt(re * re + im * im);
        }
        double geo = 0, arith = 0; int cnt = 0;
        for (int k = 1; k < N / 2; k++) { if (mag[k] > 1e-6) { geo += Math.Log(mag[k]); arith += mag[k]; cnt++; } }
        if (cnt == 0 || arith == 0) return 1;
        geo = Math.Exp(geo / cnt); arith /= cnt;
        return geo / arith;
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

    static short[] DecodeFramed(int hdr, int po, bool be, int so, bool hi) {
        var list = new List<short>();
        for (int off = 0; off + FRAME <= data.Length; off += FRAME) {
            int pred = be ? (short)((data[off + po] << 8) | data[off + po + 1]) : (short)((data[off + po + 1] << 8) | data[off + po]);
            int si = data[off + so]; if (si > 88) si = 88; if (si < 0) si = 0;
            for (int i = off + hdr; i < off + FRAME; i++) {
                list.Add((short)Nibble(hi ? data[i] >> 4 : data[i] & 0xF, ref pred, ref si));
                list.Add((short)Nibble(hi ? data[i] & 0xF : data[i] >> 4, ref pred, ref si));
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
