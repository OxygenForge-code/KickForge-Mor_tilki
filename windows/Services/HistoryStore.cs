using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace KickForgeDesktop.Services
{
    public class Sample
    {
        public DateTime Time { get; set; }
        public int Viewers { get; set; }
        public long Followers { get; set; }
        public bool Live { get; set; }
    }

    /// <summary>
    /// izleyici/takipçi eğrisi. sitede olmayan şey: uygulama kapansa da
    /// örnekler durur, açılınca geçmişle birlikte çizilir.
    /// </summary>
    public class HistoryStore
    {
        private readonly object _sync = new object();
        private readonly List<Sample> _samples = new List<Sample>();

        public string FilePath => Path.Combine(Settings.SettingsStore.Folder, "history.json");

        public IReadOnlyList<Sample> Samples
        {
            get { lock (_sync) return _samples.ToArray(); }
        }

        public void Load()
        {
            try
            {
                if (!File.Exists(FilePath)) return;
                var json = File.ReadAllText(FilePath);
                var loaded = JsonSerializer.Deserialize<List<Sample>>(json);
                if (loaded == null) return;
                lock (_sync)
                {
                    _samples.Clear();
                    _samples.AddRange(loaded.OrderBy(s => s.Time));
                }
            }
            catch (Exception ex)
            {
                Log.Write("geçmiş okunamadı: " + ex.Message);
            }
        }

        public void Add(Sample sample, int keepMinutes)
        {
            lock (_sync)
            {
                _samples.Add(sample);
                var cutoff = DateTime.Now.AddMinutes(-Math.Max(30, keepMinutes));
                _samples.RemoveAll(s => s.Time < cutoff);
                while (_samples.Count > 3000) _samples.RemoveAt(0);
            }
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(Settings.SettingsStore.Folder);
                List<Sample> copy;
                lock (_sync) copy = _samples.ToList();
                File.WriteAllText(FilePath, JsonSerializer.Serialize(copy));
            }
            catch (Exception ex)
            {
                Log.Write("geçmiş yazılamadı: " + ex.Message);
            }
        }

        public IReadOnlyList<Sample> Window(int minutes)
        {
            var cutoff = DateTime.Now.AddMinutes(-Math.Max(1, minutes));
            lock (_sync) return _samples.Where(s => s.Time >= cutoff).ToList();
        }

        public double AverageViewers(int minutes)
        {
            var live = Window(minutes).Where(s => s.Live).ToList();
            return live.Count == 0 ? 0 : live.Average(s => (double)s.Viewers);
        }

        public int PeakViewers(int minutes)
        {
            var live = Window(minutes).Where(s => s.Live).ToList();
            return live.Count == 0 ? 0 : live.Max(s => s.Viewers);
        }
    }
}
