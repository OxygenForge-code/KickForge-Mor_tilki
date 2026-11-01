using System;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using KickForgeDesktop.Settings;

namespace KickForgeDesktop.Pages
{
    public partial class AboutPage : UserControl
    {
        private readonly MainWindow _shell;

        public AboutPage(MainWindow shell)
        {
            _shell = shell;
            InitializeComponent();

            var version = Assembly.GetExecutingAssembly().GetName().Version;
            VersionLine.Text = "sürüm " + (version == null ? "2.0.0" : version.ToString(3)) + " · win-x64 · tek dosya";
            DataLine.Text = "ayarlar ve geçmiş: " + SettingsStore.Folder;
        }

        private void Site_Click(object sender, RoutedEventArgs e) => _shell.OpenLink(_shell.Settings.ApiBase);

        private void Github_Click(object sender, RoutedEventArgs e) => _shell.OpenGithub();

        private void Exe_Click(object sender, RoutedEventArgs e) =>
            _shell.OpenLink("https://github.com/OxygenForge-code/KickForge-Mor_tilki/releases/latest/download/KickForge.exe");
    }
}
