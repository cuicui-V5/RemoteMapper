// VoiceCapture - ATVV handshake + dump raw audio frames to audio.bin, CTL to ctl.txt
using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Windows.Foundation;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Devices.Enumeration;
using Windows.Storage.Streams;

class VoiceCapture {
    static readonly Guid SVC = Guid.Parse("ab5e0001-5a21-4f05-bc7d-af01f617b664");
    static readonly Guid C_CMD = Guid.Parse("ab5e0002-5a21-4f05-bc7d-af01f617b664");
    static readonly Guid C_AUD = Guid.Parse("ab5e0003-5a21-4f05-bc7d-af01f617b664");
    static readonly Guid C_CTL = Guid.Parse("ab5e0004-5a21-4f05-bc7d-af01f617b664");

    static readonly string BIN = @"D:\Projects\RemoteMapper\audio.bin";
    static readonly string CTL = @"D:\Projects\RemoteMapper\ctl.txt";

    static int ctlN, audN, totalAudioBytes;
    static DateTime t0;
    static Func<double> el;
    static FileStream fBin, fCtl;
    static object binLock = new object();

    static async Task Run() {
        Console.WriteLine("== VoiceCapture ==");
        fBin = new FileStream(BIN, FileMode.Create, FileAccess.Write, FileShare.Read);
        fCtl = new FileStream(CTL, FileMode.Create, FileAccess.Write, FileShare.Read);

        var sel = BluetoothLEDevice.GetDeviceSelector();
        var devs = await AsT(DeviceInformation.FindAllAsync(sel));
        var di = devs.FirstOrDefault(d => d.Id.IndexOf("c0:5d:39", StringComparison.OrdinalIgnoreCase) >= 0)
              ?? devs.FirstOrDefault();
        if (di == null) { Console.WriteLine("!! no device"); return; }
        Console.WriteLine("device: " + di.Name);
        var device = await AsT(BluetoothLEDevice.FromIdAsync(di.Id));
        if (device == null) { Console.WriteLine("!! FromIdAsync null"); return; }

        var svc = (await AsT(device.GetGattServicesAsync(BluetoothCacheMode.Uncached))).Services.First(s => s.Uuid == SVC);
        var chRes = await AsT(svc.GetCharacteristicsAsync(BluetoothCacheMode.Uncached));
        var chCmd = chRes.Characteristics.First(c => c.Uuid == C_CMD);
        var chAud = chRes.Characteristics.First(c => c.Uuid == C_AUD);
        var chCtl = chRes.Characteristics.First(c => c.Uuid == C_CTL);

        t0 = DateTime.Now;
        el = () => (DateTime.Now - t0).TotalSeconds;

        var hCtl = new TypedEventHandler<GattCharacteristic, GattValueChangedEventArgs>((s, e) => {
            var b = ToB(e.CharacteristicValue);
            int n = Interlocked.Increment(ref ctlN);
            string op = b.Length > 0 ? OpName(b[0]) : "?";
            string line = "[+" + el().ToString("0.00") + "s] CTL #" + n + " " + op + " " + Hex(b);
            Console.WriteLine(line);
            var bytes = Encoding.ASCII.GetBytes(line + "\r\n");
            lock (binLock) fCtl.Write(bytes, 0, bytes.Length);
        });
        var hAud = new TypedEventHandler<GattCharacteristic, GattValueChangedEventArgs>((s, e) => {
            var b = ToB(e.CharacteristicValue);
            int n = Interlocked.Increment(ref audN);
            int tot = Interlocked.Add(ref totalAudioBytes, b.Length);
            lock (binLock) fBin.Write(b, 0, b.Length);
            if (n <= 3 || n == 10 || n == 50 || n == 100 || n % 200 == 0)
                Console.WriteLine("[+" + el().ToString("0.00") + "s] AUDIO #" + n + " [" + b.Length + "B] total=" + tot + "B");
        });
        HookEvent(chCtl, hCtl);
        HookEvent(chAud, hAud);

        Console.WriteLine("notify CTL=" + await AsT(chCtl.WriteClientCharacteristicConfigurationDescriptorAsync(GattClientCharacteristicConfigurationDescriptorValue.Notify)));
        Console.WriteLine("notify AUD=" + await AsT(chAud.WriteClientCharacteristicConfigurationDescriptorAsync(GattClientCharacteristicConfigurationDescriptorValue.Notify)));

        Console.WriteLine(">> GET_CAPS");
        await WriteCmd(chCmd, new byte[] { 0x0A, 0x01, 0x00, 0x00, 0x03, 0x03 });
        await Task.Delay(1000);

        Console.WriteLine(">> MIC_OPEN (0C 00)");
        await WriteCmd(chCmd, new byte[] { 0x0C, 0x00 });

        Console.WriteLine("==================================================");
        Console.WriteLine(">>> HOLD the voice button & SPEAK a sentence (~8s)");
        Console.WriteLine(">>> e.g. \"hello hello one two three\"");
        Console.WriteLine(">>> capturing 20s...");
        Console.WriteLine("==================================================");
        Console.Out.Flush();

        await Task.Delay(20000);

        Console.WriteLine(">> MIC_CLOSE");
        await WriteCmd(chCmd, new byte[] { 0x0D });
        await Task.Delay(800);

        lock (binLock) { fBin.Flush(); fCtl.Flush(); fBin.Dispose(); fCtl.Dispose(); }
        Console.WriteLine("==================================================");
        Console.WriteLine("== SUMMARY: CTL=" + ctlN + "  AUDIO frames=" + audN + "  total bytes=" + totalAudioBytes + " ==");
        Console.WriteLine("== wrote " + BIN + " and " + CTL + " ==");
    }

    static async Task<GattCommunicationStatus> WriteCmd(GattCharacteristic ch, byte[] data) {
        var w = new DataWriter(); w.WriteBytes(data);
        return await AsT(ch.WriteValueAsync(w.DetachBuffer()));
    }
    static void HookEvent(object instance, Delegate handler) {
        var mi = instance.GetType().GetMethod("add_ValueChanged",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
        mi.Invoke(instance, new object[] { handler });
    }
    static string OpName(byte b) {
        switch (b) {
            case 0x00: return "AUDIO_STOP"; case 0x04: return "AUDIO_START";
            case 0x08: return "START_SEARCH"; case 0x0A: return "AUDIO_SYNC";
            case 0x0B: return "CAPS_RESP"; case 0x0C: return "MIC_OPEN_ERROR?"; default: return "0x" + b.ToString("X2");
        }
    }
    static Task<T> AsT<T>(IAsyncOperation<T> op) {
        var tcs = new TaskCompletionSource<T>();
        op.Completed = (o, s) => {
            try {
                if (s == AsyncStatus.Completed) tcs.TrySetResult(o.GetResults());
                else if (s == AsyncStatus.Error) tcs.TrySetException(o.ErrorCode);
                else tcs.TrySetCanceled();
            } catch (Exception ex) { tcs.TrySetException(ex); }
        };
        return tcs.Task;
    }
    static byte[] ToB(IBuffer buf) { var r = DataReader.FromBuffer(buf); var b = new byte[buf.Length]; r.ReadBytes(b); return b; }
    static string Hex(byte[] b, int max = 9999) {
        var sb = new StringBuilder();
        for (int i = 0; i < Math.Min(b.Length, max); i++) sb.Append(b[i].ToString("X2") + " ");
        if (b.Length > max) sb.Append("... (+" + (b.Length - max) + "B)");
        return sb.ToString();
    }
    static void Main() { Run().GetAwaiter().GetResult(); }
}
