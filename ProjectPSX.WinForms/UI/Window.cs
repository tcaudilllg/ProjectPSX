﻿using ProjectPSX.Devices.Input;
using ProjectPSX.Util;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows.Forms;
using NAudio.Wave;
using System.Timers;
using System.Threading.Tasks;
using System.Diagnostics;
using System.Threading;

namespace ProjectPSX {




    public class Window : Form, IHostWindow {

        const int PSX_MHZ = 33868800;
        const int SYNC_CYCLES = 100;
        float MIPS_UNDERCLOCK = 2;

        private Size vramSize = new Size(1024, 512);
        private Size _640x480 = new Size(640, 480);
        private readonly DoubleBufferedPanel screen = new DoubleBufferedPanel();

        private Display display = new Display(640, 480);
        private Display vramViewer = new Display(1024, 512);

        private ProjectPSX psx;
        private int fps;
        private bool isVramViewer;

        private int horizontalRes;
        private int verticalRes;

        private int displayVRAMXStart;
        private int displayVRAMYStart;

        private bool is24BitDepth;

        private int displayX1;
        private int displayX2;
        private int displayY1;
        private int displayY2;

        private float emuPercentage = 0;

        private long cpuCyclesCounter;

        Dictionary<Keys, GamepadInputsEnum> _gamepadKeyMap;

        private WaveOut waveout = new WaveOut();
        private BufferedWaveProvider buffer = new BufferedWaveProvider(new WaveFormat());

        public Window() {
            Text = "ProjectPSX";
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            KeyUp += new KeyEventHandler(vramViewerToggle);

            screen.BackgroundImage = display.Bitmap;// TESTING
            screen.Size = _640x480;
            screen.Margin = new Padding(0);
            screen.MouseDoubleClick += new MouseEventHandler(toggleDebug);

            Controls.Add(screen);

            KeyDown += new KeyEventHandler(handleJoyPadDown);
            KeyUp += new KeyEventHandler(handleJoyPadUp);

            _gamepadKeyMap = new Dictionary<Keys, GamepadInputsEnum>() {
                { Keys.Space, GamepadInputsEnum.Space},
                { Keys.Z , GamepadInputsEnum.Z },
                { Keys.C , GamepadInputsEnum.C },
                { Keys.Enter , GamepadInputsEnum.Enter },
                { Keys.Up , GamepadInputsEnum.Up },
                { Keys.Right , GamepadInputsEnum.Right },
                { Keys.Down , GamepadInputsEnum.Down },
                { Keys.Left , GamepadInputsEnum.Left },
                { Keys.D1 , GamepadInputsEnum.D1 },
                { Keys.D3 , GamepadInputsEnum.D3 },
                { Keys.Q , GamepadInputsEnum.Q },
                { Keys.E , GamepadInputsEnum.E },
                { Keys.W , GamepadInputsEnum.W },
                { Keys.D , GamepadInputsEnum.D },
                { Keys.S , GamepadInputsEnum.S },
                { Keys.A , GamepadInputsEnum.A },
            };

            buffer.DiscardOnBufferOverflow = true;
            buffer.BufferDuration = new TimeSpan(0, 0, 0, 0, 300);

            string diskFilename = GetDiskFilename();
            psx = new ProjectPSX(this, diskFilename);
            Globals.GTEFile  = System.IO.File.Open("gte_out.obj", System.IO.FileMode.Open, System.IO.FileAccess.Write, System.IO.FileShare.ReadWrite);
            Globals.GPUVerticesFile = System.IO.File.Open("gpu_vertex.obj", System.IO.FileMode.Open, System.IO.FileAccess.Write, System.IO.FileShare.ReadWrite);
            Globals.GPUTexcoordsFile = System.IO.File.Open("gpu_texcoords.obj", System.IO.FileMode.Open, System.IO.FileAccess.Write, System.IO.FileShare.ReadWrite);


            Console.WriteLine("Welcome to ProjectPSXtract.");
            Console.WriteLine("Functions and keys:");
            Console.WriteLine("Emulation speed - F5");
            Console.WriteLine("Count of frames to trace - F6");
            Console.WriteLine("Trace GTE - F7");
            Console.WriteLine("Trace GPU - F8");
            Console.WriteLine("Suspend/resume emulation - F9");

            var timer = new System.Timers.Timer(1000);
            timer.Elapsed += OnTimedEvent;
            timer.Enabled = true;

            RunUncapped();
        }

        private string GetDiskFilename() {
            var cla = Environment.GetCommandLineArgs();
            if (cla.Any(s => s.EndsWith(".bin") || s.EndsWith(".cue"))) {
                String filename = cla.First(s => s.EndsWith(".bin") || s.EndsWith(".cue"));
                return filename;
            }
            else {
                //Show the user a dialog so they can pick the bin they want to load.
                var fileDialog = new OpenFileDialog();
                fileDialog.Filter = "BIN/CUE files (*.bin, *.cue)|*.bin;*.cue";
                fileDialog.ShowDialog();

                string file = fileDialog.FileName;
                return file;
            }
        }

        private void handleJoyPadUp(object sender, KeyEventArgs e) {

            if (e.KeyCode == Keys.F5) {
                Console.WriteLine("Input emulator speed as percent of max:");
                string input = Console.ReadLine();
                try {
                    MIPS_UNDERCLOCK = 1 / (float.Parse(input) * (float)0.01);
                }
                catch (FormatException) {
                    Console.WriteLine($"Unable to parse '{input}'");
                }
            }
            else
            if (e.KeyCode == Keys.F6) {
                Console.WriteLine("Specify number of frames to capture when logging GTE/GPU:");
                string input = Console.ReadLine();
                try {
                    
                    Globals.capframes = Int32.Parse(input);
                }
                catch (FormatException) {
                    Console.WriteLine($"Unable to parse '{input}'");
                }
            }
            else
            if (e.KeyCode == Keys.F7) {
                Console.WriteLine("GTE polygon calculating log scheduled for " + Globals.capframes + " frames");
                Globals.GTEprecapture = true;
            }
            else
            if (e.KeyCode == Keys.F8) {
                Console.WriteLine("GPU poly raster activity log scheduled for " + Globals.capframes + " frames");
                Globals.GPUprecapture = true;
            }
            else
            if (e.KeyCode == Keys.F9) {
                if (Globals.emuPaused == false) {
                    Console.WriteLine("Emulation suspended.");
                    Globals.emuPaused = true;
                }
                else {
                    Console.WriteLine("Emulation resumed.");
                    Globals.emuPaused = false;
                }
            }
            /*else
            if (e.KeyCode == Keys.F10) {

                Console.WriteLine("Specify folder path and file name for log output.");
                string input = Console.ReadLine();
                try {
                    Globals.currentFile.Close();
                    Globals.currentFile = System.IO.File.Open(input, System.IO.FileMode.Open, System.IO.FileAccess.Write, System.IO.FileShare.ReadWrite);
                }
                catch (FormatException) {
                    Console.WriteLine($"Unable to create file '{input}'");
                }
            }*/
                GamepadInputsEnum? button = GetGamepadButton(e.KeyCode);
            if(button != null)
                psx.JoyPadUp(button.Value);
        }

        private GamepadInputsEnum? GetGamepadButton(Keys keyCode) {
            if (_gamepadKeyMap.TryGetValue(keyCode, out GamepadInputsEnum gamepadButtonValue))
                return gamepadButtonValue;
            return null;
        }

        private void handleJoyPadDown(object sender, KeyEventArgs e) {

            GamepadInputsEnum? button = GetGamepadButton(e.KeyCode);
            if (button != null)
                psx.JoyPadDown(button.Value);
        }

        private void toggleDebug(object sender, MouseEventArgs e) {
            psx.toggleDebug();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Render(int[] vram) {

            if (isVramViewer) {
                Buffer.BlockCopy(vram, 0, display.Bits, 0, 0x200000);
            } else if (is24BitDepth) {
                blit24bpp(vram);
            } else {
                blit16bpp(vram);
            }

            fps++;
            screen.Invalidate();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void blit24bpp(int[] vramBits) {
            int range = (240 - (displayY2 - displayY1)) / 2;

            int yRangeOffset;
            if (range < 0) yRangeOffset = 0;
            else yRangeOffset = range;

            for (int y = yRangeOffset; y < verticalRes - yRangeOffset; y++) {
                int offset = 0;
                for (int x = 0; x < horizontalRes; x += 2) {
                    int p0rgb = vramBits[(offset++ + displayVRAMXStart) + ((y - yRangeOffset  + displayVRAMYStart) * 1024)];
                    int p1rgb = vramBits[(offset++ + displayVRAMXStart) + ((y - yRangeOffset  + displayVRAMYStart) * 1024)];
                    int p2rgb = vramBits[(offset++ + displayVRAMXStart) + ((y - yRangeOffset  + displayVRAMYStart) * 1024)];

                    ushort p0bgr555 = GetPixelBGR555(p0rgb);
                    ushort p1bgr555 = GetPixelBGR555(p1rgb);
                    ushort p2bgr555 = GetPixelBGR555(p2rgb);

                    //[(G0R0][R1)(B0][B1G1)]
                    //   RG    B - R   GB

                    int p0R = p0bgr555 & 0xFF;
                    int p0G = (p0bgr555 >> 8) & 0xFF;
                    int p0B = p1bgr555 & 0xFF;
                    int p1R = (p1bgr555 >> 8) & 0xFF;
                    int p1G = p2bgr555 & 0xFF;
                    int p1B = (p2bgr555 >> 8) & 0xFF;

                    int p0rgb24bpp = p0R << 16 | p0G << 8 | p0B;
                    int p1rgb24bpp = p1R << 16 | p1G << 8 | p1B;

                    display.Bits[x + (y  * horizontalRes)] = p0rgb24bpp;
                    display.Bits[x + 1 + (y * horizontalRes)] = p1rgb24bpp;
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void blit16bpp(int[] vramBits) {
            //Console.WriteLine($"x1 {displayX1} x2 {displayX2} y1 {displayY1} y2 {displayY2}");
            //Console.WriteLine($"Display Height {display.Height}  Width {display.Width}");
            int range = (240 - (displayY2 - displayY1)) / 2;

            int yRangeOffset;
            if (range < 0) yRangeOffset = 0;
            else yRangeOffset = range;

            for (int y = yRangeOffset; y < verticalRes - yRangeOffset; y++) {
                for (int x = 0; x < display.Width; x++) {
                    int pixel = vramBits[(x + displayVRAMXStart) + ((y - yRangeOffset + displayVRAMYStart) * 1024)];
                    display.Bits[x + (y * horizontalRes)] = pixel;
                    //Console.WriteLine(y + " " + x);
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static ushort GetPixelBGR555(int color) {
            byte m = (byte)((color & 0xFF000000) >> 24);
            byte r = (byte)((color & 0x00FF0000) >> 16 + 3);
            byte g = (byte)((color & 0x0000FF00) >> 8 + 3);
            byte b = (byte)((color & 0x000000FF) >> 3);

            return (ushort)(m << 15 | b << 10 | g << 5 | r);
        }

        public int GetVPS() {
            int currentFps = fps;
            fps = 0;
            return currentFps;
        }

        public void SetDisplayMode(int horizontalRes, int verticalRes, bool is24BitDepth) {
            this.is24BitDepth = is24BitDepth;

            if (horizontalRes != this.horizontalRes || verticalRes != this.verticalRes) {
                this.horizontalRes = horizontalRes;
                this.verticalRes = verticalRes;

                //Console.WriteLine($"setDisplayMode {horizontalRes} {verticalRes} {is24BitDepth}");

                if (!isVramViewer) {
                    display = new Display(horizontalRes, verticalRes);
                    screen.BackgroundImage = display.Bitmap;
                }
            }

        }

        public void SetVRAMStart(ushort displayVRAMXStart, ushort displayVRAMYStart) {
            //if (isVramViewer) return;

            this.displayVRAMXStart = displayVRAMXStart;
            this.displayVRAMYStart = displayVRAMYStart;

            //Console.WriteLine($"Vram Start {displayVRAMXStart} {displayVRAMYStart}");
        }

        public void SetVerticalRange(ushort displayY1, ushort displayY2) {
            //if (isVramViewer) return;

            this.displayY1 = displayY1;
            this.displayY2 = displayY2;

            //Console.WriteLine($"Vertical Range {displayY1} {displayY2}");
        }

        public void SetHorizontalRange(ushort displayX1, ushort displayX2) {
            //if (isVramViewer) return;

            this.displayX1 = displayX1;
            this.displayX2 = displayX2;

            //Console.WriteLine($"Horizontal Range {displayX1} {displayX2}");
        }

        private void vramViewerToggle(object sender, KeyEventArgs e) { //this is very buggy but its only for debug purposes maybe disable it when unneded?
            if(e.KeyCode == Keys.Tab) {
                if (!isVramViewer) {
                    display = vramViewer;
                    screen.Size = vramSize;
                } else {
                    display = new Display(horizontalRes, verticalRes);
                    screen.Size = _640x480;
                }
                isVramViewer = !isVramViewer;
                screen.BackgroundImage = display.Bitmap;
            }
        }

        public void Play(byte[] samples) {
            buffer.AddSamples(samples, 0, samples.Length);

            if (waveout.PlaybackState != PlaybackState.Playing) {
                waveout.Init(buffer);
                waveout.Play();
            }
        }

        private void OnTimedEvent(object sender, ElapsedEventArgs e) {
            Text = $"ProjectPSX | Cpu Speed {(int)((float)cpuCyclesCounter / (PSX_MHZ / MIPS_UNDERCLOCK) * SYNC_CYCLES)}% | Vps {GetVPS()}";
            cpuCyclesCounter = 0;
        }

        public void RunUncapped() {
            Task t = Task.Factory.StartNew(EXECUTE, TaskCreationOptions.LongRunning);
        }

        private void EXECUTE() {
            Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.High;
            Thread.CurrentThread.Priority = ThreadPriority.Highest;

            try {
                while (true) {
                    if (Globals.emuPaused) continue;
                    psx.RunFrame();
                    int cyclesPerFrame = PSX_MHZ / 60;
                    int syncLoops = (cyclesPerFrame / (SYNC_CYCLES * (int) MIPS_UNDERCLOCK)) + 1;
                    int cycles = syncLoops * SYNC_CYCLES;
                    cpuCyclesCounter += cycles;
                }
            } catch (Exception e) {
                Console.WriteLine(e.ToString());
            }
        }
    }
}
