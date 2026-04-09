using Microsoft.UI.Xaml;
using System;
using System.Threading;

namespace MonkeModCleaner
{
    public partial class App : Application
    {
        private Window? _window;
        private static Mutex? _mutex;

        public App() => InitializeComponent();

        protected override void OnLaunched(LaunchActivatedEventArgs args)
        {
            _mutex = new Mutex(true, "MonkeModCleaner_SingleInstance", out bool isNew);
            if (!isNew)
            {
                Environment.Exit(0);
                return;
            }

            _window = new MainWindow();
            _window.Activate();
        }
    }
}