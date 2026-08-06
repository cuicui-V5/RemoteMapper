// VoiceProbe v2 - ATVV handshake: GET_CAPS -> MIC_OPEN -> capture audio -> MIC_CLOSE
using System;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Windows.Foundation;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Devices.Enumeration;
using Windows.Storage.Streams;

class VoiceProbe {
    static readonly Guid SVC   = Guid.Parse("ab5e0001-5a21-4f05-bc7d-af01f617b664");
    static readonly Guid C_CMD = Guid.Parse("ab5e0002-5a21-4f05-bc7d-af01f617b664");
    static readonly Guid C_AUD = Guid.Parse("ab5e0003-5a21-4f05-bc7d-af01f617b664");
    static readonly Guid C_CTL = Guid.Parse("ab5e0004-5a21-4f05-bc7d-af01f617b664");

    static int ctlN, audN;
    static DateTime t0;
    static volatile byte[] capsResp;
    static volatile bool gotAudioStart;
    static Func<double> el;

    static async Task Run() {
        Console.WriteLine("== VoiceProbe v2 ==");
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
        Console.WriteLine("chars: cmd=" + (chCmd!=null) + " audio=" + (chAud!=null) + " ctl=" + (chCtl!=null));

        t0 = DateTime.Now;
        el = () => (DateTime.Now - t0).TotalSeconds;

        var hCtl = new TypedEventHandler<GattCharacteristic, GattValueChangedEventArgs>((s, e) => {
            var b = ToB(e.CharacteristicValue);
            int n = Interlocked.Increment(ref ctlN);
            string op = b.Length > 0 ? OpName(b[0]) : "?";
            Console.WriteLine("[+" + el().ToString("0.00") + "s] CTL #" + n + " " + op + " " + Hex(b));
            Console.Out.Flush();
            if (b.Length > 0) {
                if (b[0] == 0x0B) capsResp = b;        // CAPS_RESP
                if (b[0] == 0x04) gotAudioStart = true; // AUDIO_START
            }
        });
        var hAud = new TypedEventHandler<GattCharacteristic, GattValueChangedEventArgs>((s, e) => {
            var b = ToB(e.CharacteristicValue);
            int n = Interlocked.Increment(ref audN);
            if (n <= 5 || n % 20 == 0) {
                Console.WriteLine("[+" + el().ToString("0.00") + "s] AUDIO #" + n + " [" + b.Length + "B] " + Hex(b, 24));
                Console.Out.Flush();
            }
        });
        HookEvent(chCtl, hCtl);
        HookEvent(chAud, hAud);

        Console.WriteLine("notify CTL=" + await AsT(chCtl.WriteClientCharacteristicConfigurationDescriptorAsync(GattClientCharacteristicConfigurationDescriptorValue.Notify)));
        Console.WriteLine("notify AUD=" + await AsT(chAud.WriteClientCharacteristicConfigurationDescriptorAsync(GattClientCharacteristicConfigurationDescriptorValue.Notify)));
        Console.Out.Flush();

        // ---- 1. GET_CAPS v1.0 ----
        Console.WriteLine(">> [1] GET_CAPS v1.0 = 0A 01 00 00 03 03");
        await WriteCmd(chCmd, new byte[] { 0x0A, 0x01, 0x00, 0x00, 0x03, 0x03 });
        for (int i = 0; i < 30 && capsResp == null; i++) await Task.Delay(100);
        if (capsResp != null) {
            Console.WriteLine(">> CAPS_RESP received: " + Hex(capsResp));
            if (capsResp.Length >= 3) {
                int verHi = capsResp[1], verLo = capsResp[2];
                Console.WriteLine("   version = " + verHi + "." + verLo);
            }
        } else {
            Console.WriteLine(">> no CAPS_RESP on v1.0. trying v0.4 = 0A 00 01 00 01");
            await WriteCmd(chCmd, new byte[] { 0x0A, 0x00, 0x01, 0x00, 0x01 });
            for (int i = 0; i < 20 && capsResp == null; i++) await Task.Delay(100);
            if (capsResp != null) Console.WriteLine(">> CAPS_RESP (v0.4): " + Hex(capsResp));
            else Console.WriteLine(">> still no CAPS_RESP.");
        }
        Console.Out.Flush();

        // ---- 2. MIC_OPEN v1.0 ----
        Console.WriteLine(">> [2] MIC_OPEN v1.0 = 0C 00");
        await WriteCmd(chCmd, new byte[] { 0x0C, 0x00 });
        await Task.Delay(2500);
        if (!gotAudioStart && audN == 0) {
            Console.WriteLine(">> [2b] MIC_OPEN v0.4 (8kHz) = 0C 00 01");
            await WriteCmd(chCmd, new byte[] { 0x0C, 0x00, 0x01 });
            await Task.Delay(2500);
        }
        Console.Out.Flush();

        // ---- 3. listen 15s ----
        Console.WriteLine(">> [3] listening 15s for audio... (host-initiated)");
        Console.Out.Flush();
        await Task.Delay(15000);

        // ---- 4. MIC_CLOSE ----
        Console.WriteLine(">> [4] MIC_CLOSE = 0D");
        await WriteCmd(chCmd, new byte[] { 0x0D });
        await Task.Delay(500);

        Console.WriteLine("==================================================");
        Console.WriteLine("== SUMMARY: CTL=" + ctlN + "  AUDIO=" + audN + "  audioStart=" + gotAudioStart + " ==");
        if (audN > 0) Console.WriteLine(">> AUDIO STREAM CONFIRMED. Next: decode ADPCM + virtual mic.");
        else Console.WriteLine(">> No audio. May need key press (PTT/HTT) or different MIC_OPEN params.");
        Console.Out.Flush();
    }

    static async Task<GattCommunicationStatus> WriteCmd(GattCharacteristic ch, byte[] data) {
        var w = new DataWriter();
        w.WriteBytes(data);
        var buf = w.DetachBuffer();
        var r = await AsT(ch.WriteValueAsync(buf));
        return r;
    }

    static void HookEvent(object instance, Delegate handler) {
        var mi = instance.GetType().GetMethod("add_ValueChanged",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
        mi.Invoke(instance, new object[] { handler });
    }

    static string OpName(byte b) {
        switch (b) {
            case 0x00: return "AUDIO_STOP";
            case 0x04: return "AUDIO_START";
            case 0x08: return "START_SEARCH";
            case 0x0A: return "AUDIO_SYNC";
            case 0x0B: return "CAPS_RESP";
            case 0x0C: return "MIC_OPEN_ERROR?";
            default: return "0x" + b.ToString("X2");
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

    static byte[] ToB(IBuffer buf) {
        var r = DataReader.FromBuffer(buf);
        var b = new byte[buf.Length]; r.ReadBytes(b); return b;
    }
    static string Hex(byte[] b, int max = 9999) {
        var sb = new StringBuilder();
        for (int i = 0; i < Math.Min(b.Length, max); i++) sb.Append(b[i].ToString("X2") + " ");
        if (b.Length > max) sb.Append("... (+" + (b.Length - max) + "B)");
        return sb.ToString();
    }

    static void Main() { Run().GetAwaiter().GetResult(); }
}
