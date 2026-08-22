// FrameFind.cs - empirically discover the true frame alignment in audio.bin
using System;
using System.IO;

class FrameFind {
    static byte[] data;
    static void Main() {
        data = File.ReadAllBytes(@"D:\Projects\RemoteMapper\audio.bin");
        Console.WriteLine("data size: " + data.Length + " bytes");
        Console.WriteLine();

        // 6-byte header layout per research: [seq2 BE][pad=0][pred2 BE][step 0-88]
        // scan: framesize in 20..160, phase in 0..framesize-1
        // score = % of frames where byte[off+2]==0x00 AND byte[off+5] in [0,88]
        Console.WriteLine("=== scan: frame size + phase (6-byte hdr: pad@2, step@5) ===");
        Console.WriteLine("{0,5} {1,5} {2,6} {3,8}", "fsize", "phase", "frames", "valid%");
        var best = new { fs = 0, ph = 0, v = 0.0 };
        double bestScore = -1;
        for (int fs = 20; fs <= 160; fs++) {
            for (int ph = 0; ph < fs; ph++) {
                int count = 0, valid = 0;
                for (int off = ph; off + fs <= data.Length; off += fs) {
                    count++;
                    if (data[off + 2] == 0x00 && data[off + 5] <= 88) valid++;
                }
                if (count < 3) continue;
                double pct = valid / (double)count;
                if (pct > bestScore) { bestScore = pct; best = new { fs, ph, v = pct }; }
                if (pct >= 0.8 && count >= 3)
                    Console.WriteLine("{0,5} {1,5} {2,6} {3,7:F0}%", fs, ph, count, pct * 100);
            }
        }
        Console.WriteLine();
        Console.WriteLine("BEST: framesize=" + best.fs + " phase=" + best.ph + " valid=" + (best.v * 100).ToString("0.0") + "%");
        Console.WriteLine();

        // also try 3-byte header [pred2][step] at various positions
        Console.WriteLine("=== alt: 3-byte hdr, check byte at step-position in [0,88] ===");
        Console.WriteLine("{0,5} {1,5} {2,6} {3,8}", "fsize", "stepOff", "frames", "valid%");
        double best2 = -1; int bfs2 = 0, bso = 0;
        for (int fs = 20; fs <= 160; fs++) {
            for (int so = 0; so < 6; so++) {
                int count = 0, valid = 0;
                for (int off = 0; off + fs <= data.Length; off += fs) {
                    count++;
                    if (data[off + so] <= 88) valid++;
                }
                if (count < 3) continue;
                double pct = valid / (double)count;
                if (pct > best2) { best2 = pct; bfs2 = fs; bso = so; }
            }
        }
        Console.WriteLine("BEST (3-byte): framesize=" + bfs2 + " stepOff=" + bso + " valid=" + (best2 * 100).ToString("0.0") + "%");

        // dump header bytes at the best 6-byte alignment for inspection
        Console.WriteLine();
        Console.WriteLine("=== dump first 8 frames at best 6-byte alignment (fs=" + best.fs + " ph=" + best.ph + ") ===");
        int k = 0;
        for (int off = best.ph; off + best.fs <= data.Length && k < 8; off += best.fs, k++) {
            int seq = (data[off] << 8) | data[off + 1];
            int pad = data[off + 2];
            int pred = (short)((data[off + 3] << 8) | data[off + 4]);
            int step = data[off + 5];
            Console.Write("f" + k + " off" + off + ": seq=" + seq + " pad=" + pad + " pred=" + pred + " step=" + step + "  [");
            for (int i = 0; i < 12; i++) Console.Write(data[off + i].ToString("X2") + " ");
            Console.WriteLine("]");
        }
    }
}
