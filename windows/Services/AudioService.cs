using System;
using System.Collections.Generic;
using System.Linq;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace KickForgeDesktop.Services
{
    public class OutputDevice
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public int Index { get; set; }
        public bool IsDefault { get; set; }

        public override string ToString() => Name + (IsDefault ? "  · varsayılan" : "");

        public static OutputDevice SystemDefault => new OutputDevice { Id = "", Name = "sistem varsayılanı", Index = -1, IsDefault = false };
    }

    /// <summary>
    /// ses çıkışları (kulaklık dahil) ve kısa test/uyarı sesleri.
    /// </summary>
    public class AudioService : IDisposable
    {
        private WaveOutEvent? _output;

        public IReadOnlyList<OutputDevice> Devices()
        {
            var list = new List<OutputDevice>();
            try
            {
                using var enumerator = new MMDeviceEnumerator();
                var defaultId = "";
                try
                {
                    using var def = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
                    defaultId = def.ID;
                }
                catch
                {
                    // varsayılan aygıt yoksa da listeyi verelim
                }

                var endpoints = enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active).ToList();
                for (var i = 0; i < endpoints.Count; i++)
                {
                    var device = endpoints[i];
                    list.Add(new OutputDevice
                    {
                        Id = device.ID,
                        Name = device.FriendlyName,
                        Index = i,
                        IsDefault = device.ID == defaultId
                    });
                    device.Dispose();
                }
            }
            catch (Exception ex)
            {
                Log.Write("ses cihazları okunamadı: " + ex.Message);
            }

            return list;
        }

        public string DefaultDeviceId()
        {
            try
            {
                using var enumerator = new MMDeviceEnumerator();
                using var def = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
                return def.ID;
            }
            catch
            {
                return "";
            }
        }

        public void PlayTone(string deviceId, double frequency, int milliseconds, int volumePercent)
        {
            var devices = Devices();
            var device = ResolveDevice(devices, deviceId);
            var gain = Math.Max(0, Math.Min(100, volumePercent)) / 100.0 * 0.35;

            var generator = new SignalGenerator(44100, 1)
            {
                Gain = gain,
                Frequency = frequency,
                Type = SignalGeneratorType.Sin
            };
            var tone = generator.Take(TimeSpan.FromMilliseconds(Math.Max(60, milliseconds)));
            Play(device, tone);
        }

        public void PlayNotification(NotifySoundKind kind, string deviceId, int volumePercent)
        {
            if (kind == NotifySoundKind.None) return;

            var device = ResolveDevice(Devices(), deviceId);
            var gain = Math.Max(0, Math.Min(100, volumePercent)) / 100.0 * 0.4;
            var parts = new List<ISampleProvider>();

            void Add(double frequency, int ms, double level)
            {
                var generator = new SignalGenerator(44100, 1)
                {
                    Gain = gain * level,
                    Frequency = frequency,
                    Type = SignalGeneratorType.Sin
                };
                parts.Add(generator.Take(TimeSpan.FromMilliseconds(ms)));
            }

            switch (kind)
            {
                case NotifySoundKind.Pulse:
                    Add(660, 110, 1);
                    Add(990, 150, 0.9);
                    break;
                case NotifySoundKind.Blip:
                    Add(1320, 70, 0.8);
                    break;
                case NotifySoundKind.Chime:
                    Add(523.25, 220, 0.75);
                    Add(659.25, 240, 0.7);
                    Add(783.99, 320, 0.65);
                    break;
            }

            if (parts.Count == 0) return;
            var joined = parts.Count == 1 ? parts[0] : new ConcatenatingSampleProvider(parts);
            Play(device, joined);
        }

        private OutputDevice ResolveDevice(IReadOnlyList<OutputDevice> devices, string deviceId)
        {
            if (string.IsNullOrEmpty(deviceId)) return OutputDevice.SystemDefault;
            return devices.FirstOrDefault(d => d.Id == deviceId) ?? OutputDevice.SystemDefault;
        }

        private void Play(OutputDevice device, ISampleProvider provider)
        {
            Stop();
            try
            {
                _output = new WaveOutEvent { DeviceNumber = device.Index };
                _output.Init(provider);
                _output.PlaybackStopped += (_, _) => Stop();
                _output.Play();
            }
            catch (Exception ex)
            {
                Log.Write("ses çalınamadı: " + ex.Message);
                Stop();
            }
        }

        private void Stop()
        {
            try
            {
                _output?.Stop();
                _output?.Dispose();
            }
            catch
            {
                // kapanırken sorun çıkarsa yut
            }
            finally
            {
                _output = null;
            }
        }

        public void Dispose() => Stop();
    }

    public enum NotifySoundKind
    {
        None,
        Pulse,
        Blip,
        Chime
    }
}
