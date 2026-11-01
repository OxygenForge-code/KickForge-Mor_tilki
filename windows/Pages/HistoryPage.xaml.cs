using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using KickForgeDesktop.Models;

namespace KickForgeDesktop.Pages
{
    public partial class HistoryPage : UserControl
    {
        private readonly MainWindow _shell;

        public HistoryPage(MainWindow shell)
        {
            _shell = shell;
            InitializeComponent();
            shell.FeedAdded += (_, _) => RefreshEvents();
            shell.StatsUpdated += (_, _) => RefreshLog();
            Loaded += (_, _) =>
            {
                RefreshEvents();
                RefreshLog();
            };
        }

        private void RefreshEvents()
        {
            EventList.ItemsSource = null;
            EventList.ItemsSource = _shell.Events.ToList();
        }

        private void RefreshLog()
        {
            LogList.ItemsSource = null;
            LogList.ItemsSource = Log.Lines.Reverse().Take(120).ToList();
        }

        private void Clear_Click(object sender, RoutedEventArgs e)
        {
            EventList.ItemsSource = null;
        }

        private void OpenLog_Click(object sender, RoutedEventArgs e) => _shell.OpenLogFile();
    }
}
