// CheckHdr.cs - inspect predictor/step values each candidate extracts from frames
using System;
using System.IO;

class CheckHdr {
    static byte[] data;
    static void Main() {
        data = File.ReadAllBytes(@"D:\Projects\RemoteMapper\audio.bin");
        int nFrames = data.Length / 120;
        Console.WriteLine("frames: " + nFrames);
        Console.WriteLine();
        // check all top candidates from ContinuitySearch
        Check("hdr6 p@3LE s@2 hi", 6, 3, false, 2);
        Check("hdr4 p@2LE s@1 lo", 4, 2, false, 1);
        Check("hdr4 p@0BE s@2 hi", 4, 0, true, 2);
        Check("hdr3 p@0BE s@2 hi", 3, 0, true, 2);
        Check("hdr5 p@2BE s@0 hi", 5, 2, true, 0);
    }
    static void Check(string name, int hdr, int po, bool be, int so) {
        Console.WriteLine("=== " + name + " ===");
        int stepClamped = 0;
        for (int f = 0; f < Math.Min(nFrames(), 20); f++) {
            int off = f * 120;
            int b0 = data[off + po], b1 = data[off + po + 1];
            int pred = be ? (short)((b0 << 8) | b1) : (short)((b1 << 8) | b0);
            int si = data[off + so];
            if (si > 88) { stepClamped++; }
            if (f < 8) Console.WriteLine("  f" + f + ": pred=" + pred + " step=" + si + (si > 88 ? " (CLAMP!)" : ""));
        }
        // count clamped over all frames
        int total = nFrames(), clamped = 0;
        for (int f = 0; f < total; f++) { if (data[f * 120 + so] > 88) clamped++; }
        Console.WriteLine("  step clamped ( >88 ): " + clamped + "/" + total + " = " + (clamped * 100.0 / total).ToString("F1") + "%");
        Console.WriteLine();
    }
    static int nFrames() { return data.Length / 120; }
}
