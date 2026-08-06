// ContinuitySearch.cs - find correct frame header by minimizing inter-frame discontinuity
using System;
using System.IO;
using System.Collections.Generic;

class ContinuitySearch {
    static readonly int[] stepTable = { 7,8,9,10,11,12,13,14,16,17,19,21,23,25,28,31,34,37,41,45,50,55,60,66,73,80,88,97,107,118,130,143,157,173,190,209,230,253,279,307,337,371,408,449,494,544,598,658,724,796,876,963,1060,1166,1282,1411,1552,1707,1878,2066,2272,2499,2749,3024,3327,3660,4026,4428,4871,5358,5894,6484,7132,7845,8630,9493,10442,11487,12635,13899,15289,16818,18500,20350,22385,24623,27086,29794,32767 };
    static readonly int[] indexTable = { -1,-1,-1,-1,2,4,6,8 };
    const int FRAME = 120;
    static byte[] data;

    struct Cand { public string desc; public double edgeRatio; public double rms; public int hdrLen; public int po; public bool be; public int so; public bool hi; }

    static void Main() {
        data = File.ReadAllBytes(@"D:\Projects\RemoteMapper\audio.bin");
        var cands = new List<Cand>();
        int nFrames = data.Length / FRAME;

        foreach (int hdr in new[] { 3, 4, 5, 6 }) {
            for (int po = 0; po + 1 < hdr; po++) {
                foreach (bool be in new[] { true, false }) {
                    for (int so = 0; so < hdr; so++) {
                        if (so == po || so == po + 1) continue;
                        foreach (bool hi in new[] { true, false }) {
                            var pcm = DecodeFramed(hdr, po, be, so, hi);
                            if (pcm.Length == 0) continue;
                            double edge = 0, total = 0;
                            int perFrame = (FRAME - hdr) * 2; // samples per frame
                            // edge energy: last sample of frame vs first sample of next frame
                            for (int f = 0; f < nFrames - 1; f++) {
                                int last = (f + 1) * perFrame - 1;
                                int firstNext = (f + 1) * perFrame;
                                if (firstNext >= pcm.Length) break;
                                double d = pcm[firstNext] - pcm[last];
                                edge += d * d;
                            }
                            for (int i = 0; i < pcm.Length; i++) total += (double)pcm[i] * pcm[i];
                            double edgeRatio = total > 0 ? edge / total : 1e9;
                            double rms = Math.Sqrt(total / pcm.Length);
                            cands.Add(new Cand { desc = string.Format("hdr{0} p@{1}{2} s@{3} {4}", hdr, po, be ? "BE" : "LE", so, hi ? "hi" : "lo"), edgeRatio = edgeRatio, rms = rms, hdrLen = hdr, po = po, be = be, so = so, hi = hi });
                        }
                    }
                }
            }
        }

        // rank by edge ratio (lower = smoother = more likely correct)
        cands.Sort((a, b) => a.edgeRatio.CompareTo(b.edgeRatio));
        Console.WriteLine("=== TOP 15 by lowest inter-frame edge ratio (smoothest = correct) ===");
        Console.WriteLine("{0,-26} {1,10} {2,10}", "config", "edgeRatio", "rms");
        foreach (var c in cands.GetRange(0, Math.Min(15, cands.Count)))
            Console.WriteLine("{0,-26} {1,10:F5} {2,10:F0}", c.desc, c.edgeRatio, c.rms);

        // write top 3 candidates (DC-removed + percentile normalized)
        Console.WriteLine();
        for (int i = 0; i < Math.Min(5, cands.Count); i++) {
            var c = cands[i];
            var pcm = DecodeFramed(c.hdrLen, c.po, c.be, c.so, c.hi);
            var norm = DcRemovePctNorm(pcm);
            WriteWav(string.Format(@"D:\Projects\RemoteMapper\audio_cont{0}.wav", i), norm, 16000);
            Console.WriteLine("audio_cont" + i + ".wav = " + c.desc);
        }
    }

    static short[] DecodeFramed(int hdr, int po, bool be, int so, bool hi) {
        var list = new List<short>();
        for (int off = 0; off + FRAME <= data.Length; off += FRAME) {
            int b0 = data[off + po], b1 = data[off + po + 1];
            int pred = be ? (short)((b0 << 8) | b1) : (short)((b1 << 8) | b0);
            int si = data[off + so]; if (si > 88) si = 88; if (si < 0) si = 0;
            for (int i = off + hdr; i < off + FRAME; i++) {
                list.Add((short)Nibble(hi ? data[i] >> 4 : data[i] & 0xF, ref pred, ref si));
                list.Add((short)Nibble(hi ? data[i] & 0xF : data[i] >> 4, ref pred, ref si));
            }
        }
        return list.ToArray();
    }

    static short[] DcRemovePctNorm(short[] pcm) {
        double mean = 0; for (int i = 0; i < pcm.Length; i++) mean += pcm[i];
        mean /= pcm.Length;
        var ac = new double[pcm.Length];
        for (int i = 0; i < pcm.Length; i++) ac[i] = pcm[i] - mean;
        var abs = new List<double>(); for (int i = 0; i < ac.Length; i++) abs.Add(Math.Abs(ac[i]));
        abs.Sort();
        double p98 = abs[(int)(abs.Count * 0.98)];
        double gain = p98 > 1 ? (0.7 * 32767) / p98 : 1;
        var o = new short[pcm.Length];
        for (int i = 0; i < pcm.Length; i++) {
            double v = ac[i] * gain;
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
