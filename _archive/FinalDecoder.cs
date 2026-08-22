// FinalDecoder.cs - faithful reimplementation of ATVVoice decode pipeline
// mode6 framed: seq@0-1, pad@2, predictor@3-4 BE, step@5, ADPCM@6.., hi-nibble-first
// post: predictor as first sample + declip + 3-tap lowpass + gain
using System;
using System.IO;
using System.Collections.Generic;

class FinalDecoder {
    static readonly int[] STEP = { 7,8,9,10,11,12,13,14,16,17,19,21,23,25,28,31,34,37,41,45,50,55,60,66,73,80,88,97,107,118,130,143,157,173,190,209,230,253,279,307,337,371,408,449,494,544,598,658,724,796,876,963,1060,1166,1282,1411,1552,1707,1878,2066,2272,2499,2749,3024,3327,3660,4026,4428,4871,5358,5894,6484,7132,7845,8630,9493,10442,11487,12635,13899,15289,16818,18500,20350,22385,24623,27086,29794,32767 };
    static readonly int[] IDX = { -1,-1,-1,-1,2,4,6,8 };
    const int SR = 16000, FRAME = 120, HDR = 6;

    static void Main(string[] args) {
        byte[] data = File.ReadAllBytes(@"D:\Projects\RemoteMapper\audio.bin");
        int gainDb = args.Length > 0 ? int.Parse(args[0]) : 20;

        Console.WriteLine("=== ATVVoice-faithful decode: hdr6 pred@3-4BE step@5 hi-first ===");
        var all = new List<short>();
        int nFrames = data.Length / FRAME;
        for (int f = 0; f < nFrames; f++) {
            int off = f * FRAME;
            int predictor = (short)((data[off + 3] << 8) | data[off + 4]);
            int stepIndex = Math.Min(88, (int)data[off + 5]);
            // predictor is the first sample of the frame (ATVVoice behavior)
            all.Add((short)predictor);
            // decode ADPCM payload, hi-nibble first
            for (int i = off + HDR; i < off + FRAME; i++) {
                all.Add((short)Nibble(data[i] >> 4, ref predictor, ref stepIndex));
                all.Add((short)Nibble(data[i] & 0xF, ref predictor, ref stepIndex));
            }
        }
        short[] pcm = all.ToArray();
        int declipCount = CountSpikes(pcm);
        Console.WriteLine("decoded: " + pcm.Length + " samples (" + (pcm.Length/(double)SR).ToString("0.000") + "s), pre-declip spikes=" + declipCount);

        // variant A: raw (no post) -- reproduce audio_fixed
        WriteWav(@"D:\Projects\RemoteMapper\f_raw.wav", pcm, SR);

        // variant B: declip only
        var b = (short[])pcm.Clone(); Declip(b);
        WriteWav(@"D:\Projects\RemoteMapper\f_declip.wav", b, SR);

        // variant C: declip + lowpass (ATVVoice pipeline minus gain)
        var c = (short[])pcm.Clone(); Declip(c); Lowpass(c);
        WriteWav(@"D:\Projects\RemoteMapper\f_pipe.wav", c, SR);

        // variant D: full pipeline declip + lowpass + gain
        var d = (short[])pcm.Clone(); Declip(d); Lowpass(d); ApplyGain(d, gainDb);
        WriteWav(@"D:\Projects\RemoteMapper\f_full.wav", d, SR);

        // variant E: full pipeline + RMS normalize (instead of fixed gain)
        var e = (short[])pcm.Clone(); Declip(e); Lowpass(e); var en = RmsNorm(e);
        WriteWav(@"D:\Projects\RemoteMapper\f_norm.wav", en, SR);

        Console.WriteLine("spikes after declip: " + CountSpikes(b));
        Console.WriteLine("wrote: f_raw, f_declip, f_pipe, f_full, f_norm (.wav)");
    }

    // ATVVoice declip: single-sample spike interpolator, threshold 1000
    static void Declip(short[] s) {
        const int TH = 1000;
        for (int i = 1; i < s.Length - 1; i++) {
            int prev = s[i-1], cur = s[i], nxt = s[i+1];
            int dp = Math.Abs(cur - prev), dn = Math.Abs(cur - nxt);
            int nd = Math.Abs(nxt - prev);
            if (dp > TH && dn > TH && Math.Min(dp, dn) > nd * 2)
                s[i] = (short)((prev + nxt) / 2);
        }
    }
    static int CountSpikes(short[] s) {
        const int TH = 1000; int c = 0;
        for (int i = 1; i < s.Length - 1; i++) {
            int dp = Math.Abs(s[i]-s[i-1]), dn = Math.Abs(s[i]-s[i+1]), nd = Math.Abs(s[i+1]-s[i-1]);
            if (dp > TH && dn > TH && Math.Min(dp,dn) > nd*2) c++;
        }
        return c;
    }

    // ATVVoice lowpass: 3-tap triangle [0.25, 0.5, 0.25], in-place preserving prev
    static void Lowpass(short[] s) {
        if (s.Length < 3) return;
        short prev = s[0];
        for (int i = 1; i < s.Length - 1; i++) {
            short cur = s[i];
            s[i] = (short)((prev + 2 * cur + s[i + 1]) >> 2);
            prev = cur;
        }
    }

    static void ApplyGain(short[] s, double db) {
        double g = Math.Pow(10, db / 20.0);
        for (int i = 0; i < s.Length; i++) {
            double v = s[i] * g;
            s[i] = (short)Math.Max(-32768, Math.Min(32767, (int)Math.Round(v)));
        }
    }

    static short[] RmsNorm(short[] s) {
        double rms = 0; for (int i = 0; i < s.Length; i++) rms += (long)s[i]*s[i];
        rms = Math.Sqrt(rms / s.Length);
        double g = rms > 1 ? (0.15 * 32767) / rms : 1;
        var o = new short[s.Length];
        for (int i = 0; i < s.Length; i++) {
            double v = s[i] * g;
            o[i] = (short)Math.Max(-32768, Math.Min(32767, (int)Math.Round(v)));
        }
        return o;
    }

    static int Nibble(int nibble, ref int predictor, ref int stepIndex) {
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
