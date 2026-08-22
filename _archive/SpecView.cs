// SpecView.cs - compute spectrogram of all decode variants to find true voice pitch
using System;
using System.IO;
using System.Collections.Generic;

class SpecView {
    static readonly int[] STEP = { 7,8,9,10,11,12,13,14,16,17,19,21,23,25,28,31,34,37,41,45,50,55,60,66,73,80,88,97,107,118,130,143,157,173,190,209,230,253,279,307,337,371,408,449,494,544,598,658,724,796,876,963,1060,1166,1282,1411,1552,1707,1878,2066,2272,2499,2749,3024,3327,3660,4026,4428,4871,5358,5894,6484,7132,7845,8630,9493,10442,11487,12635,13899,15289,16818,18500,20350,22385,24623,27086,29794,32767 };
    static readonly int[] IDX = { -1,-1,-1,-1,2,4,6,8 };
    const int FRAME = 120, HDR = 6, SR = 16000;
    static byte[] data;

    static void Main() {
        data = File.ReadAllBytes(@"D:\Projects\RemoteMapper\audio.bin");

        // compute spectrum of the "fixed" decode (mode6 framed) over several windows
        // to see if there's a clear voice fundamental
        Console.WriteLine("=== Spectrum of audio_fixed (mode6 framed pred@3LE step@2) ===");
        Console.WriteLine("(looking for a strong peak in 80-400Hz = voice fundamental)");
        Console.WriteLine();

        var pcm = DecodeFramed(3, false, 2);
        // take a window in the middle (likely voice) and compute DFT
        int start = pcm.Length / 3, N = 1024;
        double[] win = new double[N];
        for (int i = 0; i < N; i++) win[i] = pcm[start + i];
        // remove DC
        double mean = 0; for (int i = 0; i < N; i++) mean += win[i]; mean /= N;
        for (int i = 0; i < N; i++) win[i] -= mean;
        // Hann window
        for (int i = 0; i < N; i++) win[i] *= 0.5 - 0.5 * Math.Cos(2*Math.PI*i/(N-1));

        Console.WriteLine("freq bins (50Hz steps up to 800Hz, with magnitude):");
        int maxBin = (int)(800.0 / SR * N);
        double maxMag = 0;
        double[] mags = new double[maxBin+1];
        for (int k = 1; k <= maxBin; k++) {
            double re = 0, im = 0;
            for (int i = 0; i < N; i++) { double a = -2*Math.PI*k*i/N; re += win[i]*Math.Cos(a); im += win[i]*Math.Sin(a); }
            mags[k] = Math.Sqrt(re*re+im*im);
            if (mags[k] > maxMag) maxMag = mags[k];
        }
        for (int freq = 50; freq <= 800; freq += 50) {
            int k = (int)(freq / (double)SR * N);
            // bar
            int barLen = (int)(mags[k] / maxMag * 50);
            Console.WriteLine(freq.ToString().PadLeft(4) + "Hz: " + new string('#', barLen) + " (" + (mags[k]/maxMag).ToString("F2") + ")");
        }
        Console.WriteLine();

        // Now: the key question. Is there voice pitch in the DECODED audio at all?
        // Compute autocorrelation and find the peak lag in 80-400Hz range
        Console.WriteLine("=== Autocorrelation peak (should show voice pitch in 80-400Hz) ===");
        int lagMin = (int)(SR / 400.0), lagMax = (int)(SR / 80.0);
        double best = 0; int bestLag = 0;
        double[] x = win; // already DC-removed but not Hann for autocorr; redo without Hann
        double[] xc = new double[N];
        for (int i = 0; i < N; i++) xc[i] = pcm[start+i] - mean;
        double energy = 0; for (int i = 0; i < N; i++) energy += xc[i]*xc[i];
        for (int lag = lagMin; lag <= lagMax; lag++) {
            double c = 0;
            for (int i = 0; i < N - lag; i++) c += xc[i]*xc[i+lag];
            c /= energy;
            if (c > best) { best = c; bestLag = lag; }
        }
        double peakFreq = SR / (double)bestLag;
        Console.WriteLine("best autocorr lag=" + bestLag + " -> freq=" + peakFreq.ToString("F1") + "Hz  strength=" + best.ToString("F3"));
        Console.WriteLine("(if strength > 0.5 and freq in 80-300Hz, voice IS present)");
        Console.WriteLine();

        // Save the window region as WAV for direct comparison
        int winLen = SR * 2; // 2 seconds
        var seg = new short[winLen];
        for (int i = 0; i < winLen; i++) seg[i] = pcm[start + i];
        WriteWav(@"D:\Projects\RemoteMapper\s_fixed_seg.wav", seg, SR);
        Console.WriteLine("saved 2s voice segment: s_fixed_seg.wav");
    }

    static short[] DecodeFramed(int pp, bool be, int sp) {
        var list = new List<short>();
        for (int off = 0; off + FRAME <= data.Length; off += FRAME) {
            int predictor = be ? (short)((data[off+pp]<<8)|data[off+pp+1]) : (short)((data[off+pp+1]<<8)|data[off+pp]);
            int si = Math.Min(88, (int)data[off+sp]);
            for (int i = off + HDR; i < off + FRAME; i++) {
                list.Add((short)Nibble(data[i] >> 4, ref predictor, ref si));
                list.Add((short)Nibble(data[i] & 0xF, ref predictor, ref si));
            }
        }
        return list.ToArray();
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
