// DecodeSearch.cs - brute-force search for correct ADPCM framing over audio.bin
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

class DecodeSearch {
    static readonly int[] stepTable = { 7,8,9,10,11,12,13,14,16,17,19,21,23,25,28,31,34,37,41,45,50,55,60,66,73,80,88,97,107,118,130,143,157,173,190,209,230,253,279,307,337,371,408,449,494,544,598,658,724,796,876,963,1060,1166,1282,1411,1552,1707,1878,2066,2272,2499,2749,3024,3327,3660,4026,4428,4871,5358,5894,6484,7132,7845,8630,9493,10442,11487,12635,13899,15289,16818,18500,20350,22385,24623,27086,29794,32767 };
    static readonly int[] indexTable = { -1,-1,-1,-1,2,4,6,8 };
    const int SR = 16000, FRAME = 120;
    static byte[] data;
    static int totalSamples;

    struct Result { public string desc; public double clip; public double rmsSil; public double rmsAll; public double ratio; public short[] pcm; }

    static void Main() {
        data = File.ReadAllBytes(@"D:\Projects\RemoteMapper\audio.bin");
        var results = new List<Result>();

        // 1. continuous (no header), both nibble orders
        results.Add(Eval("continuous hi-first", DecodeContinuous(true)));
        results.Add(Eval("continuous lo-first", DecodeContinuous(false)));

        // 2. framed with header lengths and predictor/step positions
        foreach (int hdr in new[] { 4, 5, 6 }) {
            for (int nibble = 0; nibble <= 1; nibble++) {
                bool hi = nibble == 0;
                // predictor 2-byte at various offsets, BE/LE
                for (int po = 0; po + 1 < hdr; po++) {
                    foreach (bool be in new[] { true, false }) {
                        for (int so = 0; so < hdr; so++) {
                            if (so == po || so == po + 1) continue;
                            string desc = string.Format("hdr{0} pred@{1}{2} step@{3} {4}",
                                hdr, po, be ? "BE" : "LE", so, hi ? "hi" : "lo");
                            results.Add(Eval(desc, DecodeFramed(hdr, po, be, so, hi)));
                        }
                    }
                }
                // predictor 1-byte at various offsets
                for (int po = 0; po < hdr; po++) {
                    for (int so = 0; so < hdr; so++) {
                        if (so == po) continue;
                        string desc = string.Format("hdr{0} pred1@{1} step@{2} {3}",
                            hdr, po, so, hi ? "hi" : "lo");
                        results.Add(Eval(desc, DecodeFramed1(hdr, po, so, hi)));
                    }
                }
            }
        }

        // rank: prefer low clip%, high ratio (voice/silence contrast)
        var ranked = results.OrderBy(r => r.clip * 5 - r.ratio).ToList();
        Console.WriteLine("=== TOP 12 (sorted by low-clip + high-ratio) ===");
        Console.WriteLine("{0,-42} {1,7} {2,8} {3,8} {4,7}", "config", "clip%", "rmsSil", "rmsAll", "ratio");
        foreach (var r in ranked.Take(12)) {
            Console.WriteLine("{0,-42} {1,7:F1} {2,8:F0} {3,8:F0} {4,7:F2}",
                r.desc, r.clip * 100, r.rmsSil, r.rmsAll, r.ratio);
        }

        // also show best by lowest clip with decent rms
        var byClip = results.Where(r => r.rmsAll > 1000).OrderBy(r => r.clip).Take(5);
        Console.WriteLine("\n=== lowest clip% with rmsAll>1000 ===");
        foreach (var r in byClip)
            Console.WriteLine("{0,-42} clip={1:F1}% rmsAll={2:F0} ratio={3:F2}",
                r.desc, r.clip * 100, r.rmsAll, r.ratio);

        // write multiple normalized candidates for A/B listening
        var cands = new Dictionary<string, short[]> {
            { "audio_c_lo.wav", Normalize(DecodeContinuous(false)) },   // continuous lo-first, clip 0%
            { "audio_c_hi.wav", Normalize(DecodeContinuous(true)) },    // continuous hi-first
            { "audio_f6.wav",   Normalize(DecodeFramed(6,3,false,2,true)) }, // hdr6 framed (has clip)
        };
        foreach (var kv in cands) WriteWav(@"D:\Projects\RemoteMapper\" + kv.Key, kv.Value, SR);
        Console.WriteLine("wrote: " + string.Join(", ", cands.Keys));
    }

    static short[] Normalize(short[] pcm) {
        double peak = 0; for (int i = 0; i < pcm.Length; i++) { double a = Math.Abs((double)pcm[i]); if (a > peak) peak = a; }
        double g = peak > 0 ? (0.7 * 32767.0) / peak : 1;
        short[] o = new short[pcm.Length];
        for (int i = 0; i < pcm.Length; i++) o[i] = (short)Math.Max(-32768, Math.Min(32767, (int)Math.Round(pcm[i] * g)));
        return o;
    }

    static Result Eval(string desc, short[] pcm) {
        double rmsAll = 0, rmsSil = 0;
        int silN = Math.Min(SR / 2, pcm.Length); // first 0.5s = silence estimate
        int clip = 0;
        for (int i = 0; i < pcm.Length; i++) {
            rmsAll += (long)pcm[i] * pcm[i];
            if (i < silN) rmsSil += (long)pcm[i] * pcm[i];
            if (pcm[i] >= 32700 || pcm[i] <= -32700) clip++;
        }
        rmsAll = Math.Sqrt(rmsAll / pcm.Length);
        rmsSil = rmsSil / Math.Max(1, silN); rmsSil = Math.Sqrt(rmsSil);
        double ratio = rmsSil > 1 ? rmsAll / rmsSil : rmsAll;
        return new Result { desc = desc, clip = clip / (double)pcm.Length, rmsSil = rmsSil, rmsAll = rmsAll, ratio = ratio, pcm = pcm };
    }

    static short[] DecodeContinuous(bool hiFirst) {
        short[] pcm = new short[data.Length * 2];
        int p = 0, si = 0;
        for (int i = 0; i < data.Length; i++) {
            pcm[i * 2] = (short)Nibble(hiFirst ? data[i] >> 4 : data[i] & 0xF, ref p, ref si);
            pcm[i * 2 + 1] = (short)Nibble(hiFirst ? data[i] & 0xF : data[i] >> 4, ref p, ref si);
        }
        return pcm;
    }

    static short[] DecodeFramed(int hdr, int predOff, bool predBE, int stepOff, bool hiFirst) {
        var list = new List<short>();
        for (int off = 0; off + FRAME <= data.Length; off += FRAME) {
            int lo = data[off + predOff], hi2 = data[off + predOff + 1];
            int pred = predBE ? (short)((lo << 8) | hi2) : (short)((hi2 << 8) | lo);
            int si = data[off + stepOff]; if (si > 88) si = 88; if (si < 0) si = 0;
            for (int i = off + hdr; i < off + FRAME; i++) {
                list.Add((short)Nibble(hiFirst ? data[i] >> 4 : data[i] & 0xF, ref pred, ref si));
                list.Add((short)Nibble(hiFirst ? data[i] & 0xF : data[i] >> 4, ref pred, ref si));
            }
        }
        return list.ToArray();
    }

    static short[] DecodeFramed1(int hdr, int predOff, int stepOff, bool hiFirst) {
        var list = new List<short>();
        for (int off = 0; off + FRAME <= data.Length; off += FRAME) {
            int pred = (sbyte)data[off + predOff] << 8;
            int si = data[off + stepOff]; if (si > 88) si = 88; if (si < 0) si = 0;
            for (int i = off + hdr; i < off + FRAME; i++) {
                list.Add((short)Nibble(hiFirst ? data[i] >> 4 : data[i] & 0xF, ref pred, ref si));
                list.Add((short)Nibble(hiFirst ? data[i] & 0xF : data[i] >> 4, ref pred, ref si));
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
