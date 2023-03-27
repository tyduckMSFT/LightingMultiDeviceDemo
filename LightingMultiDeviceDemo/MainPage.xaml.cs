using Microsoft.Graphics.Canvas;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading;
using System.Threading.Tasks;
using Windows.Devices.Enumeration;
using Windows.Devices.Lights;
using Windows.Devices.Lights.Effects;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.Streams;
using Windows.System;
using Windows.UI;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media.Imaging;

namespace LightingMultiDeviceDemo
{
    public sealed partial class MainPage : Page
    {
        public class LightingDevice
        {
            public string m_deviceId;
            public string m_displayName;
            public LampArray m_lampArray;
            public LampArrayEffectPlaylist m_playlist;

            public LampArrayBitmapEffect m_bitmapEffect;
            public LampArrayCustomEffect m_snakeEffect;

            // For Snake Effect
            public int m_snakeHead = 0;
            public Color m_snakeColor = Colors.HotPink;

            // For generated bitmap effect
            public int m_squareXCoordinate = 0;
            public int m_squareYCoordinate = 25;
            public int m_boundWidth = 0;
            public int m_boundHeight = 0;
            public CanvasDevice m_canvasDevice = null;
            public CanvasRenderTarget m_offscreenCanvas = null;
            public CanvasDrawingSession m_offscreenDrawingSession = null;
            public SoftwareBitmap m_squareBitmap = null;
            public SoftwareBitmap m_displayBitmap = null;
            public Windows.Storage.Streams.Buffer m_squareBuffer = null;
        }

        private int m_sldBrightnessLevel = 100;

        // For Snake Effect
        private readonly int m_snakeLength = 15;

        private readonly DeviceWatcher m_deviceWatcher;
        private readonly Dictionary<string, LightingDevice> m_lightingDevices;
        private readonly CoreDispatcher m_dispatcher;
        private bool m_isRunning = false;

        public MainPage()
        {
            this.InitializeComponent();

            m_lightingDevices = new Dictionary<string, LightingDevice>();

            m_dispatcher = Window.Current.Dispatcher;

            UpdateConnectedDevicesSummary();

            m_deviceWatcher = DeviceInformation.CreateWatcher(LampArray.GetDeviceSelector());
            m_deviceWatcher.Added += Watcher_Added;
            m_deviceWatcher.Removed += Watcher_Removed;
            m_deviceWatcher.Start();
        }

        private async void Watcher_Added(DeviceWatcher sender, DeviceInformation args)
        {
            LightingDevice device = new LightingDevice
            {
                m_deviceId = args.Id,
                m_displayName = args.Name,
                m_lampArray = await LampArray.FromIdAsync(args.Id)
            };

            device.m_lampArray.BrightnessLevel = m_sldBrightnessLevel / 100; ;

            device.m_playlist = new LampArrayEffectPlaylist
            {
                EffectStartMode = LampArrayEffectStartMode.Simultaneous
            };

            lock (m_lightingDevices)
            {
                m_lightingDevices.Add(args.Id, device);
                UpdateConnectedDevicesSummary();
            }
        }

        private void Watcher_Removed(DeviceWatcher sender, DeviceInformationUpdate args)
        {
            lock (m_lightingDevices)
            {
                m_lightingDevices.Remove(args.Id);
                UpdateConnectedDevicesSummary();
            }
        }

        private async void UpdateConnectedDevicesSummary()
        {
            await m_dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
            {
                devicesSummary.Text = "Connected Devices (" + m_lightingDevices.Count + ")";
                if (m_lightingDevices.Count > 0)
                {
                    devicesSummary.Text += ":";
                }

                devicesSummary.Text += "\n";
                foreach (var device in m_lightingDevices)
                {
                    devicesSummary.Text += "\t" + device.Value.m_displayName + "\n";
                }
            });
        }

        private async Task CleanupFromPreviousEffect()
        {
            lock (m_lightingDevices)
            {
                // Must have a least 1 LampArray plugged-in
                if (m_lightingDevices.Count == 0)
                {
                    return;
                }

                List<LampArrayEffectPlaylist> playlists = new List<LampArrayEffectPlaylist>();
                foreach (LightingDevice device in m_lightingDevices.Values)
                {
                    if (device.m_playlist != null)
                    {
                        playlists.Add(device.m_playlist);
                    }

                    device.m_playlist = new LampArrayEffectPlaylist
                    {
                        EffectStartMode = LampArrayEffectStartMode.Simultaneous
                    };

                    device.m_snakeHead = 0;
                    device.m_bitmapEffect = null;
                    device.m_snakeEffect = null;
                }

                LampArrayEffectPlaylist.StopAll(playlists);

                imgBitmap.Source = null;

                m_isRunning = false;
            }
        }

        private void sldBrightness_ValueChanged(object sender, Windows.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
        {
            m_sldBrightnessLevel = (int)e.NewValue;

            if (m_lightingDevices != null)
            {
                foreach (LightingDevice device in m_lightingDevices.Values)
                {
                    if (device.m_lampArray != null)
                    {
                        device.m_lampArray.BrightnessLevel = m_sldBrightnessLevel / 100.0;
                    }
                }
            }
        }

        private void btnDisplayBitmapImage1_Click(object sender, RoutedEventArgs e)
        {
            DisplayBitmap(new Uri(this.BaseUri, "/Assets/LightingImage1.bmp"));
        }

        private void btnDisplayBitmapImage2_Click(object sender, RoutedEventArgs e)
        {
            DisplayBitmap(new Uri(this.BaseUri, "/Assets/LightingImage2.jpg"));
        }

        private async void DisplayBitmap(Uri bitmapPath)
        {
            await CleanupFromPreviousEffect();

            List<LampArrayEffectPlaylist> playlists = new List<LampArrayEffectPlaylist>();
            foreach (LightingDevice device in m_lightingDevices.Values)
            {
                LampArrayBitmapEffect bitmapEffect = new LampArrayBitmapEffect(device.m_lampArray, Enumerable.Range(0, device.m_lampArray.LampCount).ToArray())
                {
                    Duration = TimeSpan.MaxValue,
                    UpdateInterval = TimeSpan.FromMilliseconds(33)
                };
                bitmapEffect.BitmapRequested += DisplayBitmapEffect_BitmapRequested;
                device.m_bitmapEffect = bitmapEffect;
                device.m_playlist.Append(bitmapEffect);
                playlists.Add(device.m_playlist);

                StorageFile sf = await StorageFile.GetFileFromApplicationUriAsync(bitmapPath);
                using (IRandomAccessStream stream = await sf.OpenAsync(FileAccessMode.Read))
                {
                    BitmapDecoder decoder = await BitmapDecoder.CreateAsync(stream);

                    device.m_displayBitmap = await decoder.GetSoftwareBitmapAsync();

                    BitmapImage displayImage = new BitmapImage();
                    await displayImage.SetSourceAsync(stream);

                    imgBitmap.Source = displayImage;
                }
            }

            LampArrayEffectPlaylist.StartAll(playlists);
            m_isRunning = true;
        }

        private void DisplayBitmapEffect_BitmapRequested(LampArrayBitmapEffect sender, LampArrayBitmapRequestedEventArgs args)
        {
            lock (m_lightingDevices)
            { 
                // Can change the bitmap here, but for demo, just showing the same image.
                foreach (LightingDevice device in m_lightingDevices.Values)
                {
                    if (device.m_bitmapEffect == sender)
                    {
                        args.UpdateBitmap(device.m_displayBitmap);
                    }
                }
            }
        }

        private async void btnDisplayFadeInOut_Click(object sender, RoutedEventArgs e)
        {
            await CleanupFromPreviousEffect();

            List<LampArrayEffectPlaylist> playlists = new List<LampArrayEffectPlaylist>();
            foreach (LightingDevice device in m_lightingDevices.Values)
            {
                for (int i = 0; i < device.m_lampArray.LampCount; i++)
                {
                    Random r = new Random();
                    Color color = Color.FromArgb(
                        (byte)(0xFF),
                        (byte)(r.Next(255)),
                        (byte)(r.Next(255)),
                        (byte)(r.Next(255)));

                    LampArrayBlinkEffect blinkEffect = new Windows.Devices.Lights.Effects.LampArrayBlinkEffect(device.m_lampArray, new[] { i })
                    {
                        Color = color,
                        ZIndex = 0,
                        AttackDuration = TimeSpan.FromMilliseconds(300),
                        SustainDuration = TimeSpan.FromMilliseconds(500),
                        DecayDuration = TimeSpan.FromMilliseconds(800),
                        RepetitionDelay = TimeSpan.FromMilliseconds(100),
                        RepetitionMode = LampArrayRepetitionMode.Forever
                    };
                    device.m_playlist.Append(blinkEffect);
                }

                playlists.Add(device.m_playlist);
            }

            LampArrayEffectPlaylist.StartAll(playlists);
            m_isRunning = true;
        }

        private async void btnDisplaySnakeEffect_Click(object sender, RoutedEventArgs e)
        {
            await CleanupFromPreviousEffect();

            List<LampArrayEffectPlaylist> playlists = new List<LampArrayEffectPlaylist>();
            foreach (var device in m_lightingDevices.Values)
            {
                device.m_playlist.RepetitionMode = LampArrayRepetitionMode.Forever;

                var snakeEffect = new Windows.Devices.Lights.Effects.LampArrayCustomEffect(
                    device.m_lampArray, Enumerable.Range(0, device.m_lampArray.LampCount).ToArray());

                snakeEffect.UpdateInterval = TimeSpan.FromMilliseconds(35);
                snakeEffect.UpdateRequested += SnakeEffect_Update;
                snakeEffect.ZIndex = 0;
                snakeEffect.Duration = TimeSpan.MaxValue;
                device.m_playlist.Append(snakeEffect);
                device.m_snakeEffect = snakeEffect;

                playlists.Add(device.m_playlist);
            }

            LampArrayEffectPlaylist.StartAll(playlists);
            m_isRunning = true;
        }

        private void SnakeEffect_Update(LampArrayCustomEffect sender, LampArrayUpdateRequestedEventArgs args)
        {
            lock (m_lightingDevices)
            {
                foreach (LightingDevice device in m_lightingDevices.Values)
                {
                    if (device.m_snakeEffect == sender)
                    {
                        if (device.m_lampArray.LampCount > m_snakeLength)
                        {
                            Color[] colors = GetScaledSnakeColors(device);
                            int[] positions = GetPositionsBehindHead(device);
                            args.SetColor(Colors.Black); // cheap way to clear out whatever was previously set.
                            args.SetColorsForIndices(colors, positions);

                            device.m_snakeHead++;
                            if (device.m_snakeHead == device.m_lampArray.LampCount)
                            {
                                device.m_snakeHead = 0;
                            }
                        }
                        else
                        {
                            // Device doesn't have enough lamps for a good snake effect. Set a solid color instead.
                            args.SetColor(device.m_snakeColor);
                        }
                    }
                }
            }
        }

        private int[] GetPositionsBehindHead(LightingDevice device)
        {
            List<int> positions = new List<int>();

            IEnumerable<int> pos1 = Enumerable.Range(0, device.m_snakeHead).Reverse();
            IEnumerable<int> pos2 = Enumerable.Range(device.m_snakeHead, device.m_lampArray.LampCount - device.m_snakeHead).Reverse();

            positions.AddRange(pos1);
            positions.AddRange(pos2);

            positions = positions.GetRange(0, m_snakeLength);

            return positions.ToArray();
        }

        private Color[] GetScaledSnakeColors(LightingDevice device)
        {
            List<Color> colors = new List<Color>();

            for (int i = 0; i < m_snakeLength; i++)
            {
                Color c;
                float factor = (float)(m_snakeLength - i) / (float)m_snakeLength;
                c.A = 0xFF;
                c.R = (byte)(device.m_snakeColor.R * factor);
                c.G = (byte)(device.m_snakeColor.G * factor);
                c.B = (byte)(device.m_snakeColor.B * factor);

                colors.Add(c);
            }

            return colors.ToArray();
        }

        private async void btnDisplayGeneratedBitmap_Click(object sender, RoutedEventArgs e)
        {
            await CleanupFromPreviousEffect();

            List<LampArrayEffectPlaylist> playlists = new List<LampArrayEffectPlaylist>();
            foreach (LightingDevice device in m_lightingDevices.Values)
            {
                LampArrayBitmapEffect generatedBitmapEffect = new LampArrayBitmapEffect(
                    device.m_lampArray, Enumerable.Range(0, device.m_lampArray.LampCount).ToArray())
                {
                    Duration = TimeSpan.MaxValue,
                    UpdateInterval = TimeSpan.FromMilliseconds(33)
                };

                generatedBitmapEffect.BitmapRequested += GeneratedBitmap_UpdateRequest;
                device.m_playlist.Append(generatedBitmapEffect);

                device.m_boundWidth = (int)generatedBitmapEffect.SuggestedBitmapSize.Width;
                device.m_boundHeight = (int)generatedBitmapEffect.SuggestedBitmapSize.Height;

                device.m_canvasDevice = CanvasDevice.GetSharedDevice();

                // 96 works for the specific devices I've tried; might need to be tweaked for others.
                device.m_offscreenCanvas = new CanvasRenderTarget(device.m_canvasDevice, device.m_boundWidth, device.m_boundHeight, 96);

                device.m_offscreenDrawingSession = device.m_offscreenCanvas.CreateDrawingSession();

                device.m_squareBitmap = new SoftwareBitmap(BitmapPixelFormat.Bgra8, device.m_boundWidth, device.m_boundHeight, BitmapAlphaMode.Premultiplied);

                device.m_squareBuffer = new Windows.Storage.Streams.Buffer((uint)(device.m_boundWidth * device.m_boundHeight * 4));

                device.m_bitmapEffect = generatedBitmapEffect;

                playlists.Add(device.m_playlist);
            }

            LampArrayEffectPlaylist.StartAll(playlists);
            m_isRunning = true;
        }

        private void GeneratedBitmap_UpdateRequest(LampArrayBitmapEffect sender, LampArrayBitmapRequestedEventArgs args)
        {
            lock (m_lightingDevices)
            {
                foreach (var device in m_lightingDevices.Values)
                {
                    if (device.m_bitmapEffect == sender)
                    {
                        // Draw a red rectangle on a blue background.
                        device.m_offscreenDrawingSession.Clear(Colors.Blue);
                        device.m_offscreenDrawingSession.FillRectangle(device.m_squareXCoordinate, device.m_squareYCoordinate, 100, 100, Colors.Red);

                        device.m_offscreenDrawingSession.Flush();

                        device.m_squareXCoordinate += 5;

                        if (device.m_squareYCoordinate > sender.SuggestedBitmapSize.Height)
                        {
                            device.m_squareYCoordinate = 0;
                        }

                        if (device.m_squareXCoordinate > sender.SuggestedBitmapSize.Width)
                        {
                            device.m_squareXCoordinate = 0;
                        }

                        device.m_offscreenCanvas.GetPixelBytes(device.m_squareBuffer);
                        device.m_squareBitmap.CopyFromBuffer(device.m_squareBuffer);

                        args.UpdateBitmap(device.m_squareBitmap);
                    }
                }
            }
        }

        private async void btnSendReceiveVendorMessage_Click(object sender, RoutedEventArgs e)
        {
            await CleanupFromPreviousEffect();
            try
            {
                if (m_lightingDevices.Count > 0)
                {
                    // Send vendor message.
                    var devices = m_lightingDevices.Values.ToArray();

                    var vendorSendMessage = new Windows.Storage.Streams.DataWriter();
                    vendorSendMessage.WriteBytes(new byte[] { 0x01, 0x02 });

                    await devices[0].m_lampArray.SendMessageAsync(0x7, vendorSendMessage.DetachBuffer());

                    // Message was successfully sent.
                    System.Diagnostics.Debug.WriteLine("message sent");

                    // Receive vendor message.
                    IBuffer receivedMessage = await devices[0].m_lampArray.RequestMessageAsync(0x7);
                    var vendorReceivedMessage = Windows.Storage.Streams.DataReader.FromBuffer(receivedMessage);

                    // Call Read* to parse the message.
                    byte[] b = new byte[2];
                    vendorReceivedMessage.ReadBytes(b);
                    System.Diagnostics.Debug.WriteLine("message received");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(ex.Message);
            }
        }

        private async void btnCyclePrimaryColors_Click(object sender, RoutedEventArgs e)
        {
            await CleanupFromPreviousEffect();

            List<LampArrayEffectPlaylist> playlists = new List<LampArrayEffectPlaylist>();
            foreach (LightingDevice device in m_lightingDevices.Values)
            {
                device.m_playlist.RepetitionMode = LampArrayRepetitionMode.Forever;
                device.m_playlist.EffectStartMode = LampArrayEffectStartMode.Sequential;

                LampArrayColorRampEffect redColorRampEffect = new LampArrayColorRampEffect(device.m_lampArray, Enumerable.Range(0, device.m_lampArray.LampCount).ToArray())
                {
                    Color = Colors.Red,
                    RampDuration = TimeSpan.FromMilliseconds(500),
                    CompletionBehavior = LampArrayEffectCompletionBehavior.KeepState
                };
                device.m_playlist.Append(redColorRampEffect);

                LampArrayColorRampEffect yellowColorRampEffect = new LampArrayColorRampEffect(device.m_lampArray, Enumerable.Range(0, device.m_lampArray.LampCount).ToArray())
                {
                    Color = Colors.Yellow,
                    RampDuration = TimeSpan.FromMilliseconds(500),
                    CompletionBehavior = LampArrayEffectCompletionBehavior.KeepState
                };
                device.m_playlist.Append(yellowColorRampEffect);

                LampArrayColorRampEffect greenColorRampEffect = new LampArrayColorRampEffect(device.m_lampArray, Enumerable.Range(0, device.m_lampArray.LampCount).ToArray())
                {
                    Color = Colors.Green,
                    RampDuration = TimeSpan.FromMilliseconds(500),
                    CompletionBehavior = LampArrayEffectCompletionBehavior.KeepState
                };
                device.m_playlist.Append(greenColorRampEffect);

                LampArrayColorRampEffect blueColorRampEffect = new LampArrayColorRampEffect(device.m_lampArray, Enumerable.Range(0, device.m_lampArray.LampCount).ToArray())
                {
                    Color = Colors.Blue,
                    RampDuration = TimeSpan.FromMilliseconds(500),
                    CompletionBehavior = LampArrayEffectCompletionBehavior.KeepState
                };
                device.m_playlist.Append(blueColorRampEffect);

                playlists.Add(device.m_playlist);
            }

            LampArrayEffectPlaylist.StartAll(playlists);
            m_isRunning = true;
        }

        private void btnStartStopAll_Click(object sender, RoutedEventArgs e)
        {
            List<LampArrayEffectPlaylist> playlists = new List<LampArrayEffectPlaylist>();
            foreach (LightingDevice device in m_lightingDevices.Values)
            {
                playlists.Add(device.m_playlist);
            }

            if (m_isRunning)
            {
                LampArrayEffectPlaylist.StopAll(playlists);
                m_isRunning = false;
            }
            else
            {
                LampArrayEffectPlaylist.StartAll(playlists);
                m_isRunning = true;
            }
        }
    }
}
