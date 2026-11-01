using System;
using System.Threading;
using System.Windows;
using KickForgeDesktop.Settings;

namespace KickForgeDesktop
{
    public partial class App : Application
    {
        private Mutex? _singleInstance;

        public new static App Current => (App)Application.Current;

        public AppSettings Settings { get; private set; } = new AppSettings();

        public bool StartedWithWindows { get; private set; }

        protected override void OnStartup(StartupEventArgs e)
        {
            // iki kopya açılmasın: ikinci tıklama mevcut pencereyi öne getirsin
            _singleInstance = new Mutex(true, "KickForge.Desktop.SingleInstance", out var isFirst);
            if (!isFirst)
            {
                Shutdown();
                return;
            }

            base.OnStartup(e);

            foreach (var arg in e.Args)
            {
                if (string.Equals(arg, "--startup", StringComparison.OrdinalIgnoreCase)) StartedWithWindows = true;
            }

            Settings = SettingsStore.Load();
            Log.ToFile = Settings.LogToFile;

            // ilk açılışta dosya oluşsun: kullanıcı neyin nerede olduğunu görsün
            if (!System.IO.File.Exists(SettingsStore.FilePath)) SettingsStore.Save(Settings);
            ThemeManager.Apply(Settings.Theme);

            DispatcherUnhandledException += (_, args) =>
            {
                Log.Write("beklenmeyen hata: " + args.Exception.Message);
                MessageBox.Show("Beklenmeyen bir hata oldu:\n\n" + args.Exception.Message,
                    "KickForge", MessageBoxButton.OK, MessageBoxImage.Warning);
                args.Handled = true;
            };

            var window = new MainWindow();
            MainWindow = window;
            window.Show();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            try
            {
                ThemeManager.Apply(Settings.Theme);
            }
            catch
            {
                // kapanışta hata yutulur
            }

            _singleInstance?.Dispose();
            base.OnExit(e);
        }
    }
}
