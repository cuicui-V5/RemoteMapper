// RemoteMic.cs - real-time pipeline: BLE ATVV -> decode -> VB-Cable + hotkey linkage
// Hold voice button on remote -> streams decoded mic audio to CABLE + holds [RAlt+Comma] for WeChat IME
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Windows.Foundation;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Devices.Enumeration;
using Windows.Storage.Streams;

class RemoteMic {
    // ===== ATVV service/characteristics =====
    static readonly Guid SVC   = Guid.Parse("ab5e0001-5a21-4f05-bc7d-af01f617b664");
    static readonly Guid C_CMD = Guid.Parse("ab5e0002-5a21-4f05-bc7d-af01f617b664");
    static readonly Guid C_AUD = Guid.Parse("ab5e0003-5a21-4f05-bc7d-af01f617b664");
    static readonly Guid C_CTL = Guid.Parse("ab5e0004-5a21-4f05-bc7d-af01f617b664");

    // ===== audio params (locked in Phase 2) =====
    const int SR = 16000;
    const int FRAME = 120;        // default bytes per BLE audio frame (actual from CAPS → frameSize)
    const int FRAME_SAMPLES = 240; // default samples per frame (FRAME * 2)
    static readonly int[] STEP = { 7,8,9,10,11,12,13,14,16,17,19,21,23,25,28,31,34,37,41,45,50,55,60,66,73,80,88,97,107,118,130,143,157,173,190,209,230,253,279,307,337,371,408,449,494,544,598,658,724,796,876,963,1060,1166,1282,1411,1552,1707,1878,2066,2272,2499,2749,3024,3327,3660,4026,4428,4871,5358,5894,6484,7132,7845,8630,9493,10442,11487,12635,13899,15289,16818,18500,20350,22385,24623,27086,29794,32767 };
    static readonly int[] IDX = { -1,-1,-1,-1,2,4,6,8 };
    const double GAIN = 8.0;  // legacy fixed gain (unused when AGC on)
    // AGC state
    static double agcPeak = 1000;
    const double AGC_TARGET = 28000;
    const double AGC_DECAY = 0.9997;   // halves ~144ms (slow release)
    const double AGC_MAX_GAIN = 30;
    const double AGC_FLOOR = 200;

    // ===== state machine =====
    enum State { Idle, Talking }
    static volatile State state = State.Idle;
    static int predictor = 0, stepIndex = 0;
    static short lastSample = 0;     // for cross-frame lowpass continuity
    static short prevDecoded = 0;    // for cross-frame declip continuity
    static DateTime lastAudioFrameUtc = DateTime.MinValue;
    static Thread audioWatchdogThread;

    static void StartAudioWatchdog() {
        audioWatchdogThread = new Thread(() => {
            while (true) {
                Thread.Sleep(80);
                if (state == State.Talking) {
                    double elapsed = (DateTime.UtcNow - lastAudioFrameUtc).TotalMilliseconds;
                    if (elapsed > 400) {
                        StopTalking("stream silence (" + (int)elapsed + "ms)");
                    }
                }
            }
        }) { IsBackground = true, Name = "audiowatchdog" };
        audioWatchdogThread.Start();
    }

    static void StopTalking(string reason) {
        if (state != State.Talking) return;
        Console.WriteLine("[VOICE] <<< button RELEASED (" + reason + ") -> hotkey UP, stop streaming" +
            "  (" + totalFramesPlayed + " frames, " + (DateTime.Now - sessionStart).TotalSeconds.ToString("0.0") + "s)");
        state = State.Idle;
        audioPending.Clear();
        if (dumpEnabled && dumpSamples != null && dumpSamples.Count > 0) {
            string path = "rt_dump_" + DateTime.Now.ToString("HHmmss") + ".wav";
            WriteWav(path, dumpSamples.ToArray(), SR);
            Console.WriteLine("[DUMP] saved " + dumpSamples.Count + " samples -> " + path);
            dumpSamples = null;
        }
        if (hotkeyEnabled) keyQueue.Add(new KeyAction(KeyAction.VoiceRelease, null));
        // Re-arm remote HTT standby so next button press is guaranteed to respond
        Task.Run(async () => {
            try {
                await Task.Delay(60);
                if (isConnected && chCmd != null && state == State.Idle) {
                    await WriteCmd(new byte[] { 0x0C, 0x00 });
                }
            } catch { }
        });
    }

    // ===== ATVV negotiated parameters (updated from CAPS / AUDIO_START) =====
    static int frameSize = 120;        // bytes per audio frame (from CAPS response, default 120)
    static byte sessionId = 0;         // ATVV session ID (from AUDIO_START, used in MIC_EXTEND)
    // AUDIO_SYNC: pending decoder reset applied before next decoded frame
    static bool syncPending = false;
    static int syncPredictor = 0, syncStepIndex = 0;
    // Frame accumulator: handles fragmented or merged BLE audio notifications
    static readonly List<byte> audioPending = new List<byte>(256);

    // ===== key injection worker thread (avoids doing SendInput inside hook/WinRT callbacks) =====
    sealed class KeyAction {
        public const int VoiceHold = 1, VoiceRelease = 2, MapDown = 3, MapUp = 4, MapTap = 5, TaskView = 6, Launch = 7, Cmd = 8, Code = 9;
        public int Kind;
        public ushort[] Combo;
        public string Command;
        public KeyAction(int kind, ushort[] combo) : this(kind, combo, null) { }
        public KeyAction(int kind, ushort[] combo, string command) { Kind = kind; Combo = combo; Command = command; }
    }
    static System.Collections.Concurrent.BlockingCollection<KeyAction> keyQueue =
        new System.Collections.Concurrent.BlockingCollection<KeyAction>();
    static Thread keyThread;
    static void StartKeyWorker() {
        keyThread = new Thread(() => {
            foreach (var act in keyQueue.GetConsumingEnumerable()) {
                try {
                    if (act.Kind == KeyAction.VoiceHold) {
                        KeySim.HoldCombo();
                    } else if (act.Kind == KeyAction.VoiceRelease) {
                        KeySim.ReleaseCombo();
                    } else if (act.Kind == KeyAction.MapDown) {
                        KeySim.PressMappedCombo(act.Combo);
                    } else if (act.Kind == KeyAction.MapUp) {
                        KeySim.ReleaseMappedCombo(act.Combo);
                    } else if (act.Kind == KeyAction.MapTap) {
                        KeySim.TapMappedCombo(act.Combo);
                    } else if (act.Kind == KeyAction.TaskView) {
                        KeySim.OpenTaskView();
                    } else if (act.Kind == KeyAction.Launch) {
                        KeySim.RunLaunch(act.Command);
                    } else if (act.Kind == KeyAction.Cmd) {
                        KeySim.RunCmd(act.Command);
                    } else if (act.Kind == KeyAction.Code) {
                        KeySim.RunCode(act.Command);
                    }
                } catch (Exception ex) { Console.WriteLine("[KEY] worker err: " + ex.Message); }
            }
        }) { IsBackground = true, Name = "keyworker" };
        keyThread.Start();
    }

    public static void QueueMappedKey(MappedKeyEvent action) {
        if (action.Action == KeyActionKind.Launch) {
            keyQueue.Add(new KeyAction(KeyAction.Launch, null, action.Command));
            return;
        }
        if (action.Action == KeyActionKind.Cmd) {
            keyQueue.Add(new KeyAction(KeyAction.Cmd, null, action.Command));
            return;
        }
        if (action.Action == KeyActionKind.Code) {
            keyQueue.Add(new KeyAction(KeyAction.Code, null, action.Command));
            return;
        }
        int kind = action.Action == KeyActionKind.TaskView ? KeyAction.TaskView :
            (action.IsTap ? KeyAction.MapTap : (action.IsDown ? KeyAction.MapDown : KeyAction.MapUp));
        keyQueue.Add(new KeyAction(kind, action.Combo));
    }

    // ===== playback =====
    static WaveStreamer streamer;
    static long totalFramesPlayed = 0;
    static DateTime sessionStart;
    static System.Collections.Generic.List<short> dumpSamples; // for REMOTEMIC_DUMP

    // ===== BLE =====
    static BluetoothLEDevice device;
    static GattDeviceService currentSvc;
    static GattCharacteristic chCmd;
    static volatile bool isConnected = false;
    static volatile bool reconnectRequested = false;
    static bool hotkeyEnabled = Environment.GetEnvironmentVariable("REMOTEMIC_HOTKEY") != "0";
    static bool dumpEnabled = Environment.GetEnvironmentVariable("REMOTEMIC_DUMP") == "1";

    public static void TriggerReconnect() {
        reconnectRequested = true;
    }

    static async Task Run() {
        Console.Title = "RemoteMic - Xiaomi Remote -> VB-Cable";
        Console.WriteLine("== RemoteMic: remote mic -> CABLE + Voice IME hotkey ==");

        KeyMapper.Load("keymap.json");
        StartKeyWorker();
        StartAudioWatchdog();
        KeyMapUi.Start();
        VoiceKeyBlocker.Start();

        // Listen for system sleep / modern standby / hibernate resume events
        try {
            Microsoft.Win32.SystemEvents.PowerModeChanged += (s, e) => {
                if (e.Mode == Microsoft.Win32.PowerModes.Resume) {
                    Console.WriteLine("\n[POWER] System resumed from sleep/standby -> refreshing hook and triggering reconnect");
                    VoiceKeyBlocker.EnsureHook();
                    TriggerReconnect();
                }
            };
        } catch (Exception ex) { Console.WriteLine("[POWER] event hook err: " + ex.Message); }

        // Start VB-Cable audio streamer
        streamer = new WaveStreamer();
        if (!streamer.Start(SR)) {
            Console.WriteLine("[AUDIO] CABLE not found at startup, will retry when audio streams");
        }

        Console.WriteLine("===========================================================");
        Console.WriteLine(">> HOLD the voice button to talk. Release to stop.");
        Console.WriteLine(">> Configured hotkey will be held for your voice IME.");
        Console.WriteLine(">> Background connection watchdog & sleep recovery ACTIVE.");
        Console.WriteLine(">> Ctrl+C to exit.");
        Console.WriteLine("===========================================================\n");

        // Main lifecycle loop: auto-connects, keeps alive, and auto-reconnects on sleep/drop
        while (true) {
            bool ok = false;
            try {
                ok = await TryConnectBle();
            } catch (Exception ex) {
                Console.WriteLine("[BLE] Connection attempt error: " + ex.Message);
            }

            if (ok) {
                // Connected! Monitor connection status
                while (isConnected && !reconnectRequested) {
                    await Task.Delay(2000);
                    if (device == null || device.ConnectionStatus == BluetoothConnectionStatus.Disconnected) {
                        Console.WriteLine("\n[BLE] Connection lost (status Disconnected) -> reconnecting...");
                        break;
                    }
                    if (state == State.Talking) {
                        try {
                            await WriteCmd(new byte[] { 0x0E, sessionId }); // extend active speech session
                        } catch { }
                    }
                }
            }

            CleanupBle();
            await Task.Delay(2500); // Backoff before next connection attempt
        }
    }

    static async Task<bool> TryConnectBle() {
        reconnectRequested = false;
        var sel = BluetoothLEDevice.GetDeviceSelector();
        var devs = await AsT(DeviceInformation.FindAllAsync(sel));
        var di = devs.FirstOrDefault(d => d.Name.IndexOf("MI RC", StringComparison.OrdinalIgnoreCase) >= 0)
               ?? devs.FirstOrDefault(d => d.Id.IndexOf("c0:5d:39", StringComparison.OrdinalIgnoreCase) >= 0);
        if (di == null) {
            return false; // Remote is likely sleeping or out of range, will silently retry
        }

        Console.Write("[BLE] Found remote (" + di.Name + "), connecting...");
        var dev = await AsT(BluetoothLEDevice.FromIdAsync(di.Id));
        if (dev == null) {
            Console.WriteLine(" FromIdAsync failed");
            return false;
        }

        HookEventNamed(dev, "add_ConnectionStatusChanged", new TypedEventHandler<BluetoothLEDevice, object>((s, e) => {
            if (s.ConnectionStatus == BluetoothConnectionStatus.Disconnected) {
                Console.WriteLine("\n[BLE] Event: device disconnected");
                TriggerReconnect();
            } else if (s.ConnectionStatus == BluetoothConnectionStatus.Connected) {
                Console.WriteLine("\n[BLE] Event: device connected");
            }
        }));

        GattDeviceService svc = null;
        GattCharacteristic cmd = null, chAud = null, chCtl = null;
        for (int attempt = 0; attempt < 5; attempt++) {
            var svcRes = await AsT(dev.GetGattServicesAsync(BluetoothCacheMode.Uncached));
            svc = svcRes.Services.FirstOrDefault(s => s.Uuid == SVC);
            if (svc != null) {
                var chRes = await AsT(svc.GetCharacteristicsAsync(BluetoothCacheMode.Uncached));
                cmd = chRes.Characteristics.FirstOrDefault(c => c.Uuid == C_CMD);
                chAud = chRes.Characteristics.FirstOrDefault(c => c.Uuid == C_AUD);
                chCtl = chRes.Characteristics.FirstOrDefault(c => c.Uuid == C_CTL);
                if (cmd != null && chAud != null && chCtl != null) break;
            }
            await Task.Delay(800);
        }

        if (svc == null || cmd == null || chAud == null || chCtl == null) {
            Console.WriteLine(" ATVV characteristics not ready");
            try { if (svc != null) svc.Dispose(); dev.Dispose(); } catch { }
            return false;
        }

        device = dev;
        currentSvc = svc;
        chCmd = cmd;
        HookEvent(chCtl, MakeCtlHandler());
        HookEvent(chAud, MakeAudioHandler());
        await AsT(chCtl.WriteClientCharacteristicConfigurationDescriptorAsync(GattClientCharacteristicConfigurationDescriptorValue.Notify));
        await AsT(chAud.WriteClientCharacteristicConfigurationDescriptorAsync(GattClientCharacteristicConfigurationDescriptorValue.Notify));

        // Ensure audio streamer is alive
        if (streamer == null || !streamer.IsAlive) {
            if (streamer != null) streamer.Stop();
            streamer = new WaveStreamer();
            streamer.Start(SR);
        }

        // ATVV Handshake
        await WriteCmd(new byte[] { 0x0A, 0x01, 0x00, 0x00, 0x03, 0x03 }); // GET_CAPS
        await Task.Delay(300);
        await WriteCmd(new byte[] { 0x0C, 0x00 }); // MIC_OPEN
        await Task.Delay(200);

        Console.WriteLine(" OK (ATVV ready)");
        isConnected = true;
        return true;
    }

    static void CleanupBle() {
        StopTalking("BLE cleanup");
        isConnected = false;
        chCmd = null;
        try {
            if (currentSvc != null) {
                currentSvc.Dispose();
                currentSvc = null;
            }
        } catch { }
        try {
            if (device != null) {
                device.Dispose();
                device = null;
            }
        } catch { }
    }

    static TypedEventHandler<GattCharacteristic, GattValueChangedEventArgs> MakeCtlHandler() {
        return (s, e) => {
            var b = ToB(e.CharacteristicValue);
            if (b.Length < 1) return;
            byte op = b[0];
            // AUDIO_START with HTT reason: byte1 == 0x03
            if (op == 0x04 && b.Length >= 2 && b[1] == 0x03) {
                // Voice button PRESSED (AUDIO_START with HTT reason)
                if (state == State.Talking) {
                    StopTalking("restart");
                }
                sessionId = (b.Length >= 4) ? b[3] : (byte)0;
                Console.WriteLine("\n[VOICE] >>> button PRESSED (HTT) -> hotkey DOWN, start streaming");
                predictor = 0; stepIndex = 0; lastSample = 0; prevDecoded = 0; agcPeak = 1000;
                syncPending = false; audioPending.Clear();
                totalFramesPlayed = 0; sessionStart = DateTime.Now;
                lastAudioFrameUtc = DateTime.UtcNow;
                if (dumpEnabled) dumpSamples = new System.Collections.Generic.List<short>();
                state = State.Talking;
                if (hotkeyEnabled) keyQueue.Add(new KeyAction(KeyAction.VoiceHold, null));
            }
            // Any AUDIO_STOP / MIC_CLOSED / release op:
            else if (op == 0x00) {
                string reason = (b.Length >= 2 && b[1] == 0x02) ? "HTT" :
                                (b.Length >= 2 && b[1] == 0x00) ? "MIC_CLOSED" :
                                ("0x" + (b.Length >= 2 ? b[1].ToString("X2") : "00"));
                StopTalking(reason);
            }
            else if (op == 0x0B && b.Length >= 7) {
                // CAPS_RESP: parse version, codec, frame size
                int ver = (b[1] << 8) | b[2];
                int fs = (b[5] << 8) | b[6];
                if (fs > 0) frameSize = fs;
                int codec = (b.Length >= 4) ? b[3] : 0;
                Console.WriteLine("[ATVV] CAPS v" + ver + " codec=0x" + codec.ToString("X2") + " frame=" + frameSize);
            }
            else if (op == 0x0A && b.Length >= 7) {
                // AUDIO_SYNC: decoder resets predictor/stepIndex before next frame
                int pred = (b[4] << 8) | b[5];
                if (pred >= 32768) pred -= 65536;
                syncPredictor = pred;
                syncStepIndex = b[6];
                syncPending = true;
            }
        };
    }

    static TypedEventHandler<GattCharacteristic, GattValueChangedEventArgs> MakeAudioHandler() {
        return (s, e) => {
            if (state != State.Talking) return;
            lastAudioFrameUtc = DateTime.UtcNow;
            var b = ToB(e.CharacteristicValue);
            int fs = frameSize;
            // Fast path: exact frame, no pending fragments, no sync — identical to original behavior
            if (audioPending.Count == 0 && b.Length == fs && !syncPending) {
                DecodeFrame(b, fs);
                return;
            }
            // Accumulate fragmented/merged notifications, decode complete frames
            audioPending.AddRange(b);
            while (audioPending.Count >= fs) {
                byte[] frame = new byte[fs];
                audioPending.CopyTo(0, frame, 0, fs);
                audioPending.RemoveRange(0, fs);
                DecodeFrame(frame, fs);
            }
        };
    }

    // Decode one complete ADPCM frame + post-process (declip, lowpass, AGC) + enqueue to VB-Cable
    static void DecodeFrame(byte[] data, int fs) {
        // Apply pending AUDIO_SYNC reset before first nibble of this frame
        if (syncPending) {
            predictor = syncPredictor;
            stepIndex = syncStepIndex;
            syncPending = false;
        }
        var samples = new short[fs * 2];
        int pred = predictor, si = stepIndex;
        int n = Math.Min(data.Length, fs);
        int k = 0;
        for (int i = 0; i < n; i++) {
            samples[k++] = (short)Nibble(data[i] >> 4, ref pred, ref si);
            samples[k++] = (short)Nibble(data[i] & 0xF, ref pred, ref si);
        }
        predictor = pred; stepIndex = si;
        // post-process: declip (within frame), lowpass (cross-frame), AGC
        Declip(samples);
        Lowpass(samples);
        for (int i = 0; i < samples.Length; i++) {
            double v = samples[i];
            double a = v < 0 ? -v : v;
            if (a > agcPeak) agcPeak = a; else agcPeak *= AGC_DECAY;
            double g = Math.Min(AGC_MAX_GAIN, AGC_TARGET / Math.Max(agcPeak, AGC_FLOOR));
            v *= g;
            // soft clip (tanh-like)
            if (v > 32767) v = 32767; else if (v < -32768) v = -32768;
            samples[i] = (short)v;
        }
        streamer.Enqueue(samples);
        if (dumpEnabled) dumpSamples.AddRange(samples);
        Interlocked.Increment(ref totalFramesPlayed);
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

    static void Declip(short[] s) {
        const int TH = 1000;
        int len = s.Length;
        int prev = prevDecoded;
        for (int i = 0; i < len; i++) {
            int p = i == 0 ? prev : s[i - 1];
            int nx = i == len - 1 ? s[i] : s[i + 1];
            int cur = s[i];
            int dp = Math.Abs(cur - p), dn = Math.Abs(cur - nx);
            int nd = Math.Abs(nx - p);
            if (dp > TH && dn > TH && Math.Min(dp, dn) > nd * 2)
                s[i] = (short)((p + nx) / 2);
        }
        prevDecoded = s[len - 1];
    }
    static void Lowpass(short[] s) {
        if (s.Length == 0) return;
        short prev = lastSample;
        for (int i = 0; i < s.Length - 1; i++) {
            short cur = s[i];
            s[i] = (short)((prev + 2 * cur + s[i + 1]) >> 2);
            prev = cur;
        }
        lastSample = s[s.Length - 1];
    }

    // ===== BLE helpers =====
    static async Task WriteCmd(byte[] data) {
        var w = new DataWriter(); w.WriteBytes(data);
        await AsT(chCmd.WriteValueAsync(w.DetachBuffer()));
    }
    static void HookEvent(object instance, Delegate handler) {
        HookEventNamed(instance, "add_ValueChanged", handler);
    }
    static void HookEventNamed(object instance, string methodName, Delegate handler) {
        if (instance == null) return;
        var mi = instance.GetType().GetMethod(methodName,
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
        if (mi != null) mi.Invoke(instance, new object[] { handler });
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
    static void WriteWav(string path, short[] pcm, int sr) {
        int ds = pcm.Length * 2;
        using (var fs = new System.IO.FileStream(path, System.IO.FileMode.Create))
        using (var w = new System.IO.BinaryWriter(fs)) {
            var a = System.Text.Encoding.ASCII;
            w.Write(a.GetBytes("RIFF")); w.Write(36 + ds); w.Write(a.GetBytes("WAVE"));
            w.Write(a.GetBytes("fmt ")); w.Write(16); w.Write((short)1); w.Write((short)1);
            w.Write(sr); w.Write(sr * 2); w.Write((short)2); w.Write((short)16);
            w.Write(a.GetBytes("data")); w.Write(ds);
            byte[] b = new byte[ds]; System.Buffer.BlockCopy(pcm, 0, b, 0, ds); w.Write(b);
        }
    }

    static void Main() {
        // tee all console output to RemoteMic.log for diagnosing double-click launches
        try {
            var logfile = new System.IO.StreamWriter("RemoteMic.log", false) { AutoFlush = true };
            Console.SetOut(new TeeWriter(Console.Out, logfile));
            Console.WriteLine("=== RemoteMic log " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " ===");
            Console.WriteLine("CWD: " + System.IO.Directory.GetCurrentDirectory());
            Console.WriteLine("SessionId: " + System.Diagnostics.Process.GetCurrentProcess().SessionId);
            Console.WriteLine("Interactive: " + System.Environment.UserInteractive);
        } catch (Exception ex) { Console.WriteLine("[LOG] init failed: " + ex.Message); }

        Console.CancelKeyPress += (o, ea) => {
            ea.Cancel = true;
            RequestExit();
        };
        try { Run().GetAwaiter().GetResult(); }
        catch (Exception ex) { Console.WriteLine("FATAL: " + ex); Console.ReadLine(); }
    }

    public static void RequestExit() {
        Console.WriteLine("\nExiting...");
        try { KeySim.ReleaseCombo(); } catch { }
        if (streamer != null) streamer.Stop();
        try { VoiceKeyBlocker.Stop(); } catch { }
        try { KeyMapUi.Stop(); } catch { }
        Environment.Exit(0);
    }
}

// ===== TeeWriter: write to console + file simultaneously =====
class TeeWriter : System.IO.TextWriter {
    System.IO.TextWriter _c; System.IO.StreamWriter _f;
    public TeeWriter(System.IO.TextWriter c, System.IO.StreamWriter f) { _c = c; _f = f; }
    public override System.Text.Encoding Encoding { get { return _c.Encoding; } }
    public override void Write(string v) { _c.Write(v); try { _f.Write(v); _f.Flush(); } catch { } }
    public override void WriteLine(string v) { _c.WriteLine(v); try { _f.WriteLine(v); _f.Flush(); } catch { } }
    public override void Write(char v) { _c.Write(v); try { _f.Write(v); _f.Flush(); } catch { } }
}

// ===== VoiceKeyBlocker: swallows the remote voice button's F20 spam =====
// The remote voice button used to arrive as F5 (HID), which polluted apps and
// the injected hotkey combo; the driver now remaps it to F20 (0x83) so it can
// never collide with the user's physical F5. F20 is reserved as the remote-only
// voice key in this setup, so swallowing ALL F20 events is intentional. Trigger
// still works via the BLE CTL channel.
class VoiceKeyBlocker {
    delegate IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] static extern IntPtr SetWindowsHookEx(int idHook, HookProc lpfn, IntPtr hMod, uint dwThreadId);
    [DllImport("user32.dll")] static extern bool UnhookWindowsHookEx(IntPtr hhk);
    [DllImport("user32.dll")] static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] static extern int GetMessage(out MSG msg, IntPtr hwnd, uint min, uint max);
    [DllImport("user32.dll")] static extern UIntPtr SetTimer(IntPtr hwnd, UIntPtr id, uint interval, IntPtr callback);
    [DllImport("kernel32.dll")] static extern uint GetTickCount();
    [DllImport("kernel32.dll")] static extern IntPtr GetModuleHandle(string name);
    [StructLayout(LayoutKind.Sequential)] struct MSG { public IntPtr hwnd; public uint message; public IntPtr wParam; public IntPtr lParam; public uint time; public int px, py; }
    const int WH_KEYBOARD_LL = 13;
    const int WM_KEYDOWN = 0x0100, WM_KEYUP = 0x0101, WM_SYSKEYDOWN = 0x0104, WM_SYSKEYUP = 0x0105;
    const uint LLKHF_INJECTED = 0x10, LLKHF_LOWER_IL_INJECTED = 0x02;
    const ushort VK_F5 = 0x74;  // remote voice button emits HID F5 natively
    const ushort VK_F20 = 0x83; // compatibility if driver was ever installed
    [StructLayout(LayoutKind.Sequential)]
    struct KBDLLHOOKSTRUCT { public uint vkCode; public uint scanCode; public uint flags; public uint time; public IntPtr extra; }
    static HookProc proc = HookCb;
    static volatile IntPtr hhk = IntPtr.Zero;
    static Thread pump;
    static uint pumpTid;
    const uint WM_TIMER = 0x0113, WM_APP_REHOOK = 0x8000;
    static UIntPtr keymapTimerId = UIntPtr.Zero;
    static volatile int f5Count = 0;
    [DllImport("kernel32.dll")] static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")] static extern bool PostThreadMessage(uint tid, uint msg, IntPtr w, IntPtr l);

    static IntPtr HookCb(int nCode, IntPtr wParam, IntPtr lParam) {
        try {
            if (nCode >= 0) {
                var k = (KBDLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(KBDLLHOOKSTRUCT));
                if (k.vkCode == VK_F5 || k.vkCode == VK_F20) { f5Count++; return (IntPtr)1; }   // swallow remote voice-button spam

                int message = wParam.ToInt32();
                bool down = message == WM_KEYDOWN || message == WM_SYSKEYDOWN;
                bool up = message == WM_KEYUP || message == WM_SYSKEYUP;
                if (down || up) {
                    bool injected = (k.flags & (LLKHF_INJECTED | LLKHF_LOWER_IL_INJECTED)) != 0;
                    MappedKeyEvent[] actions;
                    if (KeyMapper.Handle((ushort)k.vkCode, down, injected, k.time, out actions)) {
                        foreach (MappedKeyEvent action in actions) RemoteMic.QueueMappedKey(action);
                        return (IntPtr)1;
                    }
                }
            }
        } catch (Exception ex) { Console.WriteLine("[KEYMAP] hook err: " + ex.Message); }
        return CallNextHookEx(hhk, nCode, wParam, lParam);
    }

    public static void Start() {
        pump = new Thread(() => {
            Thread.CurrentThread.Priority = ThreadPriority.Highest;
            pumpTid = GetCurrentThreadId();
            try {
                hhk = SetWindowsHookEx(WH_KEYBOARD_LL, proc, GetModuleHandle(null), 0);
                keymapTimerId = SetTimer(IntPtr.Zero, (UIntPtr)1, 25, IntPtr.Zero);
                if (keymapTimerId == UIntPtr.Zero) Console.WriteLine("[KEYMAP] timer install failed");
                Console.WriteLine("[F5] voice-key blocker + key mapper hook installed");
                MSG m;
                while (GetMessage(out m, IntPtr.Zero, 0, 0) > 0) {
                    if (m.message == WM_APP_REHOOK) {
                        if (hhk != IntPtr.Zero) {
                            try { UnhookWindowsHookEx(hhk); } catch { }
                            hhk = IntPtr.Zero;
                        }
                        hhk = SetWindowsHookEx(WH_KEYBOARD_LL, proc, GetModuleHandle(null), 0);
                        Console.WriteLine("[F5] hook refreshed by watchdog");
                    }
                    if (m.message == WM_TIMER && unchecked((ulong)m.wParam.ToInt64()) == keymapTimerId.ToUInt64()) {
                        if (hhk == IntPtr.Zero)
                            hhk = SetWindowsHookEx(WH_KEYBOARD_LL, proc, GetModuleHandle(null), 0);
                        foreach (MappedKeyEvent action in KeyMapper.TakeDueActions(GetTickCount()))
                            RemoteMic.QueueMappedKey(action);
                    }
                    TranslateMessage(ref m);
                    DispatchMessage(ref m);
                }
            } catch (Exception ex) { Console.WriteLine("[VOICEKEY] pump err: " + ex.Message); }
        }) { IsBackground = true, Name = "voicekeypump" };
        pump.Start();
    }
    [DllImport("user32.dll")] static extern bool TranslateMessage(ref MSG m);
    [DllImport("user32.dll")] static extern IntPtr DispatchMessage(ref MSG m);
    public static void EnsureHook() {
        if (pumpTid != 0) {
            PostThreadMessage(pumpTid, WM_APP_REHOOK, IntPtr.Zero, IntPtr.Zero);
        }
    }
    public static void Stop() {
        if (hhk != IntPtr.Zero) { UnhookWindowsHookEx(hhk); hhk = IntPtr.Zero; }
    }
}

// ===== waveOut streamer to VB-Cable =====
class WaveStreamer {
    [DllImport("winmm.dll", CharSet = CharSet.Unicode)]
    static extern int waveOutGetDevCaps(uint id, ref WAVEOUTCAPS pwoc, int cbwoc);
    [DllImport("winmm.dll")]
    static extern int waveOutOpen(out IntPtr phwi, uint id, ref WAVEFORMATEX pwfx, IntPtr cb, IntPtr inst, uint fdo);
    [DllImport("winmm.dll")]
    static extern int waveOutPrepareHeader(IntPtr hwo, IntPtr pwh, int cbwh);
    [DllImport("winmm.dll")]
    static extern int waveOutWrite(IntPtr hwo, IntPtr pwh, int cbwh);
    [DllImport("winmm.dll")]
    static extern int waveOutReset(IntPtr hwo);
    [DllImport("winmm.dll")]
    static extern int waveOutClose(IntPtr hwo);
    [DllImport("winmm.dll")]
    static extern uint waveOutGetNumDevs();

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct WAVEOUTCAPS { public ushort wMid, wPid; public uint v; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string name; public uint df; public ushort ch, r1; public uint sup; }
    [StructLayout(LayoutKind.Sequential)]
    struct WAVEFORMATEX { public ushort tag; public ushort ch; public uint sr; public uint avg; public ushort blk; public ushort bits; public ushort cb; }

    const int NB = 8;
    const int BUF_BYTES = 240 * 2;  // one audio frame
    const int HDR_SIZE = 48;        // x64 WAVEHDR size
    const int OFF_FLAGS = 24;
    const int WHDR_DONE = 1;

    IntPtr hWave = IntPtr.Zero;
    IntPtr[] hdr = new IntPtr[NB];
    IntPtr[] data = new IntPtr[NB];
    bool[] used = new bool[NB];
    Queue<short[]> q = new Queue<short[]>();
    object qlock = new object();
    volatile bool running;
    Thread thr;

    public bool IsAlive {
        get { return running && hWave != IntPtr.Zero; }
    }

    public bool Start(int sr) {
        // find device: look for VB-Audio Virtual Cable / CABLE Input
        uint n = waveOutGetNumDevs();
        uint bestIdx = 0xFFFFFFFF;
        int bestScore = -1;
        for (uint i = 0; i < n; i++) {
            var c = new WAVEOUTCAPS();
            waveOutGetDevCaps(i, ref c, Marshal.SizeOf(c));
            string nm = c.name ?? "";
            Console.WriteLine("    out[" + i + "] = " + nm);
            int score = 0;
            if (nm.IndexOf("CABLE Input", StringComparison.OrdinalIgnoreCase) >= 0) {
                score = 100;
            } else if (nm.IndexOf("VB-Audio", StringComparison.OrdinalIgnoreCase) >= 0 || nm.IndexOf("VB-Cable", StringComparison.OrdinalIgnoreCase) >= 0) {
                score = (nm.IndexOf("16", StringComparison.OrdinalIgnoreCase) >= 0) ? 50 : 90;
            } else if (nm.IndexOf("CABLE", StringComparison.OrdinalIgnoreCase) >= 0) {
                score = 60;
            }
            if (score > bestScore) {
                bestScore = score;
                bestIdx = i;
            }
        }
        if (bestScore <= 0 || bestIdx == 0xFFFFFFFF) return false;
        uint idx = bestIdx;

        var wfx = new WAVEFORMATEX { tag = 1, ch = 1, sr = (uint)sr, avg = (uint)(sr * 2), blk = 2, bits = 16, cb = 0 };
        int hr = waveOutOpen(out hWave, idx, ref wfx, IntPtr.Zero, IntPtr.Zero, 0);
        if (hr != 0) return false;

        for (int i = 0; i < NB; i++) {
            data[i] = Marshal.AllocHGlobal(BUF_BYTES);
            hdr[i] = Marshal.AllocHGlobal(HDR_SIZE);
            // zero the header
            for (int o = 0; o < HDR_SIZE; o++) Marshal.WriteByte(hdr[i], o, 0);
            Marshal.WriteIntPtr(hdr[i], 0, data[i]);          // lpData
            Marshal.WriteInt32(hdr[i], 8, BUF_BYTES);          // dwBufferLength
            waveOutPrepareHeader(hWave, hdr[i], HDR_SIZE);
        }
        running = true;
        thr = new Thread(Pump) { IsBackground = true, Name = "wavout" };
        thr.Start();
        return true;
    }

    public void Enqueue(short[] samples) {
        lock (qlock) {
            q.Enqueue(samples);
            if (q.Count > 40) { q.Dequeue(); } // drop old if backed up (>600ms)
        }
    }

    public void Stop() {
        running = false;
        if (thr != null) thr.Join(500);
        if (hWave != IntPtr.Zero) {
            waveOutReset(hWave);
            for (int i = 0; i < NB; i++) {
                if (hdr[i] != IntPtr.Zero) { Marshal.FreeHGlobal(hdr[i]); hdr[i] = IntPtr.Zero; }
                if (data[i] != IntPtr.Zero) { Marshal.FreeHGlobal(data[i]); data[i] = IntPtr.Zero; }
            }
            waveOutClose(hWave);
            hWave = IntPtr.Zero;
        }
        lock (qlock) { q.Clear(); }
        Array.Clear(used, 0, used.Length);
    }

    void Pump() {
        while (running) {
            int freeIdx = -1;
            for (int i = 0; i < NB; i++) {
                if (used[i] && (Marshal.ReadInt32(hdr[i], OFF_FLAGS) & WHDR_DONE) != 0)
                    used[i] = false;
                if (!used[i]) { freeIdx = i; break; }
            }
            short[] samples = null;
            if (freeIdx >= 0) {
                lock (qlock) { if (q.Count > 0) samples = q.Dequeue(); }
            }
            if (freeIdx >= 0 && samples != null) {
                int n = Math.Min(samples.Length, 240);
                Marshal.Copy(samples, 0, data[freeIdx], n);
                Marshal.WriteInt32(hdr[freeIdx], 8, n * 2);  // dwBufferLength
                waveOutWrite(hWave, hdr[freeIdx], HDR_SIZE);
                used[freeIdx] = true;
            } else {
                Thread.Sleep(3);
            }
        }
    }
}

// ===== keyboard simulation: hold/release [Right Alt + Comma] (scan-code + dual API) =====
class KeySim {
    [DllImport("user32.dll")]
    static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern int GetWindowText(IntPtr h, System.Text.StringBuilder s, int n);
    [DllImport("user32.dll")]
    static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);

    public static void PressMappedCombo(ushort[] combo) {
        if (!KeyComboSender.Press(combo)) Console.WriteLine("[KEYMAP] SendInput down failed");
    }
    public static void ReleaseMappedCombo(ushort[] combo) {
        if (!KeyComboSender.Release(combo)) Console.WriteLine("[KEYMAP] SendInput up failed");
    }
    public static void TapMappedCombo(ushort[] combo) {
        if (!KeyComboSender.Tap(combo)) Console.WriteLine("[KEYMAP] SendInput tap failed");
    }
    public static void OpenTaskView() {
        System.Diagnostics.Process.Start("explorer.exe", "shell:::{3080F90E-D7AD-11D9-BD98-0000947B0257}");
    }
    public static void RunCode(string source) {
        string text = KeySnippet.Run(source);
        if (String.IsNullOrEmpty(text)) return;
        if (!KeyComboSender.TypeText(text)) Console.WriteLine("[KEYMAP] type snippet failed");
        else Console.WriteLine("[KEY] CODE -> " + text);
    }
    public static void RunLaunch(string command) {
        if (String.IsNullOrWhiteSpace(command)) return;
        string path, args;
        KeyMapConfig.SplitCommand(command, out path, out args);
        var psi = new System.Diagnostics.ProcessStartInfo();
        psi.FileName = path;
        psi.Arguments = args ?? "";
        psi.UseShellExecute = true;
        System.Diagnostics.Process.Start(psi);
        Console.WriteLine("[KEY] LAUNCH " + command);
    }
    public static void RunCmd(string command) {
        if (String.IsNullOrWhiteSpace(command)) return;
        var psi = new System.Diagnostics.ProcessStartInfo();
        psi.FileName = "cmd.exe";
        psi.Arguments = "/c " + command;
        psi.UseShellExecute = false;
        psi.CreateNoWindow = true;
        System.Diagnostics.Process.Start(psi);
        Console.WriteLine("[KEY] CMD " + command);
    }
    public static void DiagFg(string tag) {
        // kept for manual debugging; disabled by default
        if (Environment.GetEnvironmentVariable("REMOTEMIC_KEYDIAG") == "1") {
            IntPtr fg = GetForegroundWindow();
            var sb = new System.Text.StringBuilder(256);
            GetWindowText(fg, sb, 256);
            uint pid; GetWindowThreadProcessId(fg, out pid);
            string proc = "?";
            try { proc = System.Diagnostics.Process.GetProcessById((int)pid).ProcessName; } catch { }
            Console.WriteLine("[KEY] "+tag+" | fgproc="+proc+" title="+(sb.ToString()==""?"(none)":sb.ToString()));
        }
    }
    public static void HoldCombo() {
        ushort[] combo = KeyMapper.VoiceHotkey;
        if (combo != null && combo.Length > 0) {
            KeyComboSender.Release(combo);
            Thread.Sleep(20);
            KeyComboSender.Press(combo);
        }
        Console.WriteLine("[KEY] HOLD done (" + (combo != null ? KeyMapConfig.FormatCombo(combo) : "") + ")");
    }
    public static void ReleaseCombo() {
        ushort[] combo = KeyMapper.VoiceHotkey;
        if (combo != null && combo.Length > 0) {
            KeyComboSender.Release(combo);
        }
        Console.WriteLine("[KEY] RELEASE done");
    }
}
