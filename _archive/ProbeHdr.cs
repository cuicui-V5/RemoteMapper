// ProbeHdr.cs - find true predictor/step position by predicting next frame's predictor
using System;
using System.IO;
using System.Collections.Generic;

class ProbeHdr {
    static readonly int[] STEP = { 7,8,9,10,11,12,13,14,16,17,19,21,23,25,28,31,34,37,41,45,50,55,60,66,73,80,88,97,107,118,130,143,157,173,190,209,230,253,279,307,337,371,408,449,494,544,598,658,724,796,876,963,1060,1166,1282,1411,1552,1707,1878,2066,2272,2499,2749,3024,3327,3660,4026,4428,4871,5358,5894,6484,7132,7845,8630,9493,10442,11487,12635,13899,15289,16818,18500,20350,22385,24623,27086,29794,32767 };
    static readonly int[] IDX = { -1,-1,-1,-1,2,4,6,8 };
    const int FRAME = 120, HDR = 6;
    static byte[] data;

    static void Main() {
        data = File.ReadAllBytes(@"D:\Projects\RemoteMapper\audio.bin");
        int nFrames = data.Length / FRAME;

        Console.WriteLine("=== Probe: which (predPos, predBE/LE, stepPos) gives frame-end matching next-predictor? ===");
        Console.WriteLine("(correct header -> last sample of frame N ≈ predictor of frame N+1)");
        Console.WriteLine();

        // try predictor at positions [0..4], BE/LE; step at positions [0..5]
        var results = new List<dynamic>();
        for (int pp = 0; pp <= 4; pp++) {
            foreach (bool be in new[] { true, false }) {
                for (int sp = 0; sp <= 5; sp++) {
                    if (sp == pp || sp == pp + 1) continue;
                    double err = EvalPredMatch(pp, be, sp);
                    results.Add(new { pp, be, sp, err });
                }
            }
        }
        results.Sort((a, b) => ((double)a.err).CompareTo((double)b.err));
        Console.WriteLine("{0,5} {1,6} {2,5} {3,10}", "predP", "endian", "stepP", "matchErr");
        foreach (var r in results.GetRange(0, 12))
            Console.WriteLine("{0,5} {1,6} {2,5} {3,10:F0}", r.pp, r.be ? "BE" : "LE", r.sp, r.err);
        Console.WriteLine();

        // Second probe: which step position yields step_index that stays in range AND evolves smoothly?
        // Correct step position -> decoded step_index per frame stays small (0..40) for speech
        Console.WriteLine("=== Probe 2: step position -> average step_index magnitude (speech: 10..40) ===");
        for (int sp = 0; sp <= 5; sp++) {
            double avgSi = 0; int cnt = 0;
            for (int f = 0; f < nFrames; f++) {
                int si = data[f * FRAME + sp];
                if (si <= 88) { avgSi += si; cnt++; }
            }
            if (cnt > 0) Console.WriteLine("  step@" + sp + ": avg=" + (avgSi/cnt).ToString("F1") + " (valid " + cnt + "/" + nFrames + ")");
        }
    }

    // decode frame fully, return last sample (the predictor state at end of frame)
    static double EvalPredMatch(int pp, bool be, int sp) {
        int nFrames = data.Length / FRAME;
        double totalErr = 0; int count = 0;
        int prevLast = 0;
        for (int f = 0; f < nFrames; f++) {
            int off = f * FRAME;
            int predictor = be ? (short)((data[off+pp] << 8) | data[off+pp+1]) : (short)((data[off+pp+1] << 8) | data[off+pp]);
            int si = Math.Min(88, (int)data[off+sp]);
            int curPred = predictor;
            for (int i = off + HDR; i < off + FRAME; i++) {
                curPred = NibbleVal(data[i] >> 4, curPred, ref si);
                curPred = NibbleVal(data[i] & 0xF, curPred, ref si);
            }
            if (f > 0) {
                // compare curPred (end of this frame) is meaningless; instead:
                // the predictor field of THIS frame should ≈ last decoded sample of PREVIOUS frame
                totalErr += Math.Abs(predictor - prevLast);
                count++;
            }
            prevLast = curPred;
        }
        return count > 0 ? totalErr / count : 1e9;
    }

    static int NibbleVal(int nibble, int predictor, ref int stepIndex) {
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
}
