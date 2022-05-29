using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Threading;
using System.Windows.Controls;
using Hardcodet.Wpf.TaskbarNotification;

namespace Edoc
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        private TaskbarIcon? notifyIcon = null;

        private InvisibleWindow invisibleWindow = new InvisibleWindow();
        Mutex? process_lock = null;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            process_lock = new Mutex(true, "EdocClass", out bool process_lock_created);
            // Prevent duplicate apps
            if (!process_lock_created)
            {
                Shutdown();
                MessageBox.Show("Edoc instance already running!\nPlease right click the icon on the system tray and exit", "Instance running", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            else
            {
                //create the notifyicon (it's a resource declared in NotifyIconResources.xaml
                notifyIcon = (TaskbarIcon)FindResource("NotifyIcon");
                Application.Current.MainWindow = new MainWindow();
            }
        }

        protected override void OnExit(ExitEventArgs e)
        {
            process_lock?.Close();
            notifyIcon?.Dispose(); //the icon would clean up automatically, but this is cleaner
            base.OnExit(e);
        }
    }
}
