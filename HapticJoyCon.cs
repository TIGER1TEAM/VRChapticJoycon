/**
 *
 * Listens for VRChat OSC parameter broadcasts and maps them to
 * HD Rumble on up to 6 Nintendo Joy-Cons using the JoyCon.NET library.
 *
 * Setup:
 *   1. Create a new .NET Console App project in Visual Studio 2022
 *   2. Add NuGet package: JoyCon.NET
 *   3. Add this file to the project
 *   4. Click Build Solution 
 *	5. Add contact receivers to your avatar and link them to parameters 
 *	6. Add those parameter names to the code file
 *	7. Turn on OSC in-game
 *	8. Build again
 *
 * Running it:
 *	1. Connect all your JoyCons via Bluetooth by holding the pair button and find them in windows settings
 *	2. Find the .exe and run it (it will show a list of parameters with the corresponding controller)
 *	3. Launch VRChat and read the CMD logs to make sure the parameters are received in the program
 *	4. Thats it. Just close the CMD window when closing the game.
 *
 * Notes:
 *	You can change the amplitude and frequency by changing the lines with freq or amp. (It will look like Freq = 160f)
 *	I do urge you to change and customize the presets for yourself. They are by default: Strong, Low, Bendy, and Default. They are functional and the ones I use but if you are bored, mess with them numbers. (It will involve a lot of simple math) Hopefully you took algebra and geometry…
 *	This is built for 6 controllers but if you are insane and have like 10 or 20, you can just duplicate some lines and adjust some limits to add more. I dont know if there is a limit in Bluetooth but the code will basically allow infinite. (idk bc i only own 4 and possibly 6)
 *	Also, controller modes are only set per device. So if you want them all to be the same mode, you have to go one by one.
 *	Try not to restart the .exe because when you do, the exe will reassign all the controllers every launch. You will need to relaunch it if: you need to build a new version or edits, you want to add or lose a controller, or if it crashes but I haven’t had that happen yet…
 *	If you see it start printing “Error: Not started” don’t panic. This means one of your controllers isn’t replying back ie got disconnected or died or entered pairing process. Just close the CMD window and find the one that died or got disconnected, and pair it again.
 *	Also, the reason for high and low frequency systems is that the haptic engine for the JoyCons has 2 separate rumble motors. One is built for the lower frequencies, and the other for median and high.
 *	The most forceful frequency is 175 Hz at full amplitude
 *
 */

using HidSharp;
using System.Net;
using System.Net.Sockets;
using wtf.cluster.JoyCon;
using wtf.cluster.JoyCon.InputReports;
using wtf.cluster.JoyCon.Rumble;

class VRChatJoyConHaptics
{
    // -----------------------------------------------------------------------
    // Configuration - edit these to match your avatar parameters
    // -----------------------------------------------------------------------

    struct ParamMapping
    {
        public string ParamName;
        public int JoyConIndex;  // 0-5, in order they're found
    }

    static readonly ParamMapping[] PARAM_MAP = new[]
    {
        new ParamMapping { ParamName = "haptic_1", JoyConIndex = 0 },
        new ParamMapping { ParamName = "haptic_2", JoyConIndex = 1 },
        new ParamMapping { ParamName = "haptic_3", JoyConIndex = 2 },
        new ParamMapping { ParamName = "haptic_4", JoyConIndex = 3 },
        new ParamMapping { ParamName = "haptic_5", JoyConIndex = 4 },
        new ParamMapping { ParamName = "haptic_6", JoyConIndex = 5 },
    };

    const int OSC_PORT = 9001;
    const float HIGH_FREQ_HZ = 180f; // adjust these to the max frequency you have
    const float LOW_FREQ_HZ = 160f;

    // -----------------------------------------------------------------------
    // Joy-Con management
    // -----------------------------------------------------------------------

    enum HapticMode { Strong, Low, Bendy, Default }

    class JoyConState
    {
        public JoyCon? Controller;
        public float CurrentAmplitude = 0f;
        public float LastSentAmplitude = -1f;
        public HapticMode Mode = HapticMode.Strong;
        public readonly object Lock = new();
        public DateTime LastModeChange = DateTime.MinValue;
    }

    static readonly List<JoyConState> joycons = new();
    static readonly Dictionary<string, int> oscAddressMap = new();

    // -----------------------------------------------------------------------
    // OSC parsing (minimal - handles float32 only)
    // -----------------------------------------------------------------------

    struct OscMessage
    {
        public string Address;
        public float FloatValue;
        public bool Valid;
    }

    static int GetOscStringLength(byte[] buf, int offset)
    {
        int start = offset;
        while (offset < buf.Length && buf[offset] != 0) offset++;
        offset++; // include null terminator
        // pad to 4-byte boundary
        while (offset % 4 != 0) offset++;
        return offset - start;
    }

    static OscMessage ParseOsc(byte[] buf, int length)
    {
        var msg = new OscMessage { Valid = false };
        if (length < 4) return msg;

        // Address string
        int addrLen = GetOscStringLength(buf, 0);
        if (addrLen <= 0 || addrLen >= length) return msg;
        msg.Address = System.Text.Encoding.UTF8.GetString(buf, 0, addrLen).TrimEnd('\0');

        int offset = addrLen;
        if (offset >= length || buf[offset] != (byte)',') return msg;

        // Type tag string (starts with ',')
        int tagLen = GetOscStringLength(buf, offset);
        if (tagLen < 2) return msg;

        char type = (char)buf[offset + 1];
        if (type != 'f') return msg; // only handle floats

        offset += tagLen;
        if (offset + 4 > length) return msg;

        // Float: 4 bytes big-endian IEEE 754
        uint raw = (uint)((buf[offset] << 24) | (buf[offset + 1] << 16) |
                          (buf[offset + 2] << 8) | buf[offset + 3]);
        msg.FloatValue = BitConverter.ToSingle(BitConverter.GetBytes(raw), 0);
        msg.Valid = true;

        return msg;
    }

    static (float, float, float, float) GetRumbleParams(float amp, HapticMode mode)
    {
        return mode switch
        {
            HapticMode.Strong => (

// Math explanation: a + (amp * b) this is the same as y=mx+b. a is the lowest you want the frequency to go, and b is the multiplier in witch it would increase. (It is helpful to visualize it in desmos) Another rule of thumb is that a + b should equal the max frequency since the input is a bool (0 through 1) so for this example, 1 * 50 then + 120. Just make sure to change the max frequency at the top of the file and at the bottom where the caps are.

                120f + (amp * 50f),  
                140f + (amp * 35f),   

// this is the exact same but now you have to start from 0. (so no y intercept) This is literally just the multiplier. IF THIS IS GREATER THAN 1, YOU MUST HAVE THE CAP AT THE BOTTOM OF THIS FILE OR IT WILL ERROR OUT WHEN TESTING.

                amp * 3f,
                amp * 2f
            ),
            HapticMode.Low => (
                80f + (amp * 50f),   
                100f + (amp * 50f),
                amp * 3f,
                amp * 2f
            ),
            HapticMode.Bendy => (
                40f + (amp * 200f),  
                160f + (amp * 400f),  
                Math.Min(amp * 4f, 1f),
                Math.Min(amp * 3f, 1f)
            ),
            HapticMode.Default => (
                40f + (amp * 120f),  
                60f + (amp * 115f),  
                amp * 0.6f,
                amp * 0.8f
            ),
            _ => (120f, 140f, 0f, 0f)
        };
    }

    // -----------------------------------------------------------------------
    // Main
    // -----------------------------------------------------------------------

    static async Task Main()
    {
        Console.WriteLine("[joycon_osc] Starting VRChat Joy-Con OSC Haptics");
        Console.WriteLine("[joycon_osc] Searching for Joy-Cons...");

        // Find all Joy-Cons
        var deviceList = DeviceList.Local;
        var nintendoDevices = deviceList.GetHidDevices(0x057e).ToList();

        if (!nintendoDevices.Any())
        {
            Console.WriteLine("[joycon_osc] No Joy-Cons found. Pair them via Bluetooth first.");
            return;
        }

        // Connect to each Joy-Con
        foreach (var device in nintendoDevices)
        {
            try
            {
                var joycon = new JoyCon(device);
                var state = new JoyConState { Controller = joycon };
                joycons.Add(state);

                // Initialize controller
                joycon.Start();
                await joycon.SetInputReportModeAsync(JoyCon.InputReportType.Full);
                await joycon.EnableRumbleAsync(true);

                var info = await joycon.GetDeviceInfoAsync();
                Console.WriteLine($"[joycon_osc] Found Joy-Con #{joycons.Count - 1} " +
                                  $"({info.ControllerType})");

                joycon.ReportReceived += (sender, input) =>
                {
                    if (input is InputFull j)
                    {
                        var b = j.Buttons;
                        var now = DateTime.UtcNow;

                        lock (state.Lock)
                        {
                            if ((now - state.LastModeChange).TotalMilliseconds < 500) // 500ms debounce
                                return Task.CompletedTask;

                            if (b.SL)
                            {
                                state.Mode = state.Mode == HapticMode.Strong ? HapticMode.Default : state.Mode - 1;
                                state.LastModeChange = now;
                                Console.WriteLine($"[mode] Joy-Con #{joycons.Count} -> {state.Mode}");
                            }
                            else if (b.SR)
                            {
                                state.Mode = state.Mode == HapticMode.Default ? HapticMode.Strong : state.Mode + 1;
                                state.LastModeChange = now;
                                Console.WriteLine($"[mode] Joy-Con #{joycons.Count} -> {state.Mode}");
                            }
                        }
                    }
                    return Task.CompletedTask;
                };
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[joycon_osc] Failed to connect to device: {ex.Message}");
            }
        }

        if (joycons.Count == 0)
        {
            Console.WriteLine("[joycon_osc] No Joy-Cons could be initialized.");
            return;
        }

        if (joycons.Count < 6)
        {
            Console.WriteLine($"[joycon_osc] Warning: found {joycons.Count} Joy-Con(s), " +
                              "expected 6. Some parameters will have no device.");
        }

        // Build OSC address map
        Console.WriteLine("\n[joycon_osc] Parameter -> Joy-Con mapping:");
        foreach (var mapping in PARAM_MAP)
        {
            string addr = $"/avatar/parameters/{mapping.ParamName}";
            oscAddressMap[addr] = mapping.JoyConIndex;

            string deviceInfo = mapping.JoyConIndex < joycons.Count
                ? "connected"
                : "NOT FOUND";
            Console.WriteLine($"  {addr,-40} -> Joy-Con #{mapping.JoyConIndex} ({deviceInfo})");
        }
        Console.WriteLine();

        // Display body part assignments with LEDs for 3 seconds
        Console.WriteLine("[joycon_osc] Displaying body part assignments on LEDs...");
        Console.WriteLine("MAP: You can add where each JoyCon goes here");

        // Helper to set LED pattern from byte
        async Task SetLedPattern(JoyCon jc, byte pattern)
        {
            var led1 = (pattern & 0b0001) != 0 ? JoyCon.LedState.On : JoyCon.LedState.Off;
            var led2 = (pattern & 0b0010) != 0 ? JoyCon.LedState.On : JoyCon.LedState.Off;
            var led3 = (pattern & 0b0100) != 0 ? JoyCon.LedState.On : JoyCon.LedState.Off;
            var led4 = (pattern & 0b1000) != 0 ? JoyCon.LedState.On : JoyCon.LedState.Off;
            await jc.SetPlayerLedsAsync(led1, led2, led3, led4);
        }

        var assignmentPatterns = new byte[]
        {
            0b0001,  // Joy-Con #0 = 1 LED
            0b0011,  // Joy-Con #1 = 1 LED  
            0b0111,  // Joy-Con #2 = 1 LED
            0b1111,  // Joy-Con #3 = 1 LED
            0b1110,  // Joy-Con #4 = 2 LEDs
            0b1101   // Joy-Con #5 = 2 LEDs
        };

        for (int i = 0; i < joycons.Count && i < assignmentPatterns.Length; i++)
        {
            try
            {
                await SetLedPattern(joycons[i].Controller!, assignmentPatterns[i]);
            }
            catch { }
        }

        await Task.Delay(3000);
        Console.WriteLine("[joycon_osc] Switching to intensity mode...\n");

        // Start rumble update thread
        var cts = new CancellationTokenSource();
        var rumbleTask = Task.Run(() => RumbleUpdateLoop(cts.Token));

        // Start OSC listener
        using var udpClient = new UdpClient(OSC_PORT);
        Console.WriteLine($"[joycon_osc] Listening on UDP port {OSC_PORT}...");
        Console.WriteLine("Press Ctrl+C to exit.\n");

        Console.CancelKeyPress += (s, e) =>
        {
            e.Cancel = true;
            cts.Cancel();
        };

        try
        {
            while (!cts.Token.IsCancellationRequested)
            {
                var result = await udpClient.ReceiveAsync();
                var msg = ParseOsc(result.Buffer, result.Buffer.Length);

                if (!msg.Valid || !oscAddressMap.TryGetValue(msg.Address, out int jcIndex))
                    continue;

                if (jcIndex < 0 || jcIndex >= joycons.Count)
                    continue;

                float amp = Math.Clamp(msg.FloatValue, 0f, 1f);

                lock (joycons[jcIndex].Lock)
                {
                    joycons[jcIndex].CurrentAmplitude = amp;
                }

                Console.WriteLine($"[osc] {msg.Address,-40} = {amp:F3} -> Joy-Con #{jcIndex}");
            }
        }
        catch (SocketException)
        {
            // Expected on shutdown
        }

        // Cleanup
        Console.WriteLine("\n[joycon_osc] Shutting down...");
        cts.Cancel();
        await rumbleTask;

        foreach (var state in joycons)
        {
            try
            {
                if (state.Controller != null)
                {
                    await state.Controller.WriteRumble(new RumbleSet(
                        new RumbleData(LOW_FREQ_HZ, 0f),
                        new RumbleData(HIGH_FREQ_HZ, 0f)
                    ));
                    state.Controller.Dispose();
                }
            }
            catch { }
        }

        Console.WriteLine("[joycon_osc] Shutdown complete.");
    }

    static async Task RumbleUpdateLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            foreach (var state in joycons)
            {
                if (state.Controller == null) continue;

                float amp;
                lock (state.Lock)
                {
                    amp = state.CurrentAmplitude;
                    //amp = 0.5f;
                }

                // Only send if amplitude changed meaningfully
                float diff = Math.Abs(amp - state.LastSentAmplitude);
                if (diff > 0.01f)
                {
                    try
                    {


                        HapticMode mode;
                        lock (state.Lock) { mode = state.Mode; }

                        var (lowFreq, highFreq, lowAmp, highAmp) = GetRumbleParams(amp, mode);

				// DONT MESS WITH THE FIRST 2, THEY ARE IN CASE A INSANE VALUE GETS SENT

                        if (highAmp > 1f)
                        {
                            highAmp = 1f;
                        }

                        if (lowAmp > 1f)
                        {
                            lowAmp = 1f;
                        }

                        if (highFreq > 180f) // Adjust these to the max frequency you have
                        {
                            highFreq = 180f;
                        }

                        if (lowFreq > 170f)
                        {
                            lowFreq = 170f;
                        }

                        await state.Controller.WriteRumble(new RumbleSet(
                            new RumbleData(lowFreq, lowAmp),
                            new RumbleData(highFreq, highAmp)
                        ));
                        state.LastSentAmplitude = amp;
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[rumble] Error: {ex.Message}");
                    }
                }
            }

            await Task.Delay(16, ct); // ~60Hz
        }
    }
}
