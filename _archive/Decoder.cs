// Decoder.cs - IMA ADPCM decoder for ATVV audio.bin -> WAV
using System;
using System.IO;

class Decoder {
    static readonly int[] stepTable = { 7,8,9,10,11,12,13,14,16,17,19,21,23,25,28,31,34,37,41,45,50,55,60,66,73,80,88,97,107,118,130,143,157,173,190,209,230,253,279,307,337,371,408,449,494,544,598,658,724,796,876,963,1060,1166,1282,1411,1552,1707,1878,2066,2272,2499,2749,3024,3327,3660,4026,4428,4871,5358,5894,6484,7132,7845,8630,9493,10442,11487,12635,13899,15289,16818,18500,20350,22385,24623,27086,29794,32767 };
    static readonly int[] indexTable = { -1,-1,-1,-1,2,4,6,8 };
    const int SR = 16000, FRAME = 120;

    static void Main(string[] args) {
        int mode = args.Length > 0 ? int.Parse(args[0]) : 6;
        bool post = args.Length > 1 && args[1] == "post";
        string bin = @"D:\Projects\RemoteMapper\audio.bin";
        string outp = @"D:\Projects\RemoteMapper\audio_out.wav";
        byte[] data = File.ReadAllBytes(bin);
        short[] pcm = (mode == 0) ? DecodeContinuous(data) : DecodeFramed(data, FRAME, mode);
        if (post) pcm = PostProcess(pcm);
        WriteWav(outp, pcm, SR);

        Console.WriteLine("mode=" + mode + " post=" + post + "  samples=" + pcm.Length + "  (" + (pcm.Length / (double)SR).ToString("0.000") + "s)");
        Console.Write("first 32: ");
        for (int i = 0; i < 32 && i < pcm.Length; i++) Console.Write(pcm[i] + " ");
        Console.WriteLine();
        long sum = 0; for (int i = 0; i < pcm.Length; i++) sum += (long)pcm[i] * pcm[i];
        Console.WriteLine("RMS=" + Math.Sqrt(sum / (double)pcm.Length).ToString("0.0"));
        Console.WriteLine("wrote " + outp);
    }

    // low-pass (3-tap) + peak normalization to -3dBFS
    static short[] PostProcess(short[] pcm) {
        double[] d = new double[pcm.Length];
        for (int i = 0; i < pcm.Length; i++) d[i] = pcm[i];
        // 3-tap low-pass [0.25,0.5,0.25]
        double[] lp = new double[pcm.Length];
        lp[0] = d[0]; lp[pcm.Length-1] = d[pcm.Length-1];
        for (int i = 1; i < pcm.Length-1; i++) lp[i] = 0.25*d[i-1] + 0.5*d[i] + 0.25*d[i+1];
        // peak normalize to -3dBFS (0.707 * 32767)
        double peak = 0; for (int i = 0; i < lp.Length; i++) { double a = Math.Abs(lp[i]); if (a > peak) peak = a; }
        double gain = peak > 0 ? (0.707 * 32767.0) / peak : 1;
        short[] outpcm = new short[pcm.Length];
        for (int i = 0; i < pcm.Length; i++) { double v = lp[i] * gain; outpcm[i] = (short)Math.Max(-32768, Math.Min(32767, (int)Math.Round(v))); }
        return outpcm;
    }

    static short[] DecodeContinuous(byte[] data) {
        short[] pcm = new short[data.Length * 2];
        int predictor = 0, stepIndex = 0;
        for (int i = 0; i < data.Length; i++) {
            pcm[i * 2]     = (short)Nibble(data[i] >> 4, ref predictor, ref stepIndex);
            pcm[i * 2 + 1] = (short)Nibble(data[i] & 0xF, ref predictor, ref stepIndex);
        }
        return pcm;
    }

    static short[] DecodeFramed(byte[] data, int frameSize, int hdrLen) {
        var list = new System.Collections.Generic.List<short>();
        int frame = 0;
        for (int off = 0; off + frameSize <= data.Length; off += frameSize, frame++) {
            int predictor = 0, stepIndex = 0;
            if (hdrLen >= 6) {
                // v0.4 header: [seq(2)] [0x00 pad] [predictor BE i16] [step_index u8]
                predictor = (short)((data[off + 3] << 8) | data[off + 4]);
                stepIndex = data[off + 5];
                if (stepIndex < 0) stepIndex = 0; if (stepIndex > 88) stepIndex = 88;
            }
            for (int i = off + hdrLen; i < off + frameSize; i++) {
                list.Add((short)Nibble(data[i] >> 4, ref predictor, ref stepIndex));
                list.Add((short)Nibble(data[i] & 0xF, ref predictor, ref stepIndex));
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

    static void WriteWav(string path, short[] pcm, int sampleRate) {
        int dataSize = pcm.Length * 2;
        using (var fs = new FileStream(path, FileMode.Create))
        using (var w = new BinaryWriter(fs)) {
            var a = System.Text.Encoding.ASCII;
            w.Write(a.GetBytes("RIFF")); w.Write(36 + dataSize); w.Write(a.GetBytes("WAVE"));
            w.Write(a.GetBytes("fmt ")); w.Write(16); w.Write((short)1); w.Write((short)1);
            w.Write(sampleRate); w.Write(sampleRate * 2); w.Write((short)2); w.Write((short)16);
            w.Write(a.GetBytes("data")); w.Write(dataSize);
            byte[] b = new byte[dataSize]; Buffer.BlockCopy(pcm, 0, b, 0, dataSize);
            w.Write(b);
        }
    }
}
