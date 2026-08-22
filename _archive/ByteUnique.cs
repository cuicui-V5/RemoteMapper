// ByteUnique.cs - deterministic header discovery via per-byte uniqueness analysis
using System;
using System.IO;
using System.Collections.Generic;

class ByteUnique {
    static void Main() {
        byte[] data = File.ReadAllBytes(@"D:\Projects\RemoteMapper\audio.bin");
        int FRAME = 120;
        int nFrames = data.Length / FRAME;
        Console.WriteLine("frames: " + nFrames + ", frame size: " + FRAME);
        Console.WriteLine();
        Console.WriteLine("=== per-byte uniqueness across all " + nFrames + " frames ===");
        Console.WriteLine("(header bytes: FEW unique values; ADPCM data: ~256 unique, near-uniform distribution)");
        Console.WriteLine();
        Console.WriteLine("{0,4} {1,7} {2,7} {3,10} {4,10} {5}", "pos", "unique", "%uniq", "min", "max", "note");
        for (int pos = 0; pos < FRAME; pos++) {
            var seen = new HashSet<int>();
            int mn = 256, mx = -1;
            // also track value distribution
            var dist = new int[256];
            for (int f = 0; f < nFrames; f++) {
                int v = data[f * FRAME + pos];
                seen.Add(v);
                dist[v]++;
                if (v < mn) mn = v;
                if (v > mx) mx = v;
            }
            double pct = seen.Count / 256.0 * 100;
            string note = "";
            if (seen.Count == 1) note = "<<< CONSTANT (metadata!)";
            else if (seen.Count <= 8) note = "<<< FEW values (metadata?)";
            else if (pct < 50) note = "  (low diversity)";
            Console.WriteLine("{0,4} {1,7} {2,6:F1}% {3,10} {4,10} {5}", pos, seen.Count, pct, mn, mx, note);
        }
        Console.WriteLine();

        // also dump the actual unique values for suspicious low-diversity positions
        Console.WriteLine("=== detailed unique values for low-diversity positions (first 20) ===");
        for (int pos = 0; pos < 20; pos++) {
            var seen = new SortedSet<int>();
            for (int f = 0; f < nFrames; f++) seen.Add(data[f * FRAME + pos]);
            if (seen.Count <= 16) {
                var vals = new List<int>(seen);
                Console.WriteLine("pos " + pos.ToString().PadLeft(2) + " (" + seen.Count + " unique): " + string.Join(" ", vals));
            }
        }
    }
}
