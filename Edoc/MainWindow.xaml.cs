﻿using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Diagnostics;
using Microsoft.WindowsAPICodePack.Shell;
using System.ComponentModel;
using System.Windows.Media.Animation;

namespace Edoc
{
    internal enum AccentState
    {
        ACCENT_DISABLED = 1,
        ACCENT_ENABLE_GRADIENT = 0,
        ACCENT_ENABLE_TRANSPARENTGRADIENT = 2,
        ACCENT_ENABLE_BLURBEHIND = 3,
        ACCENT_INVALID_STATE = 4
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct AccentPolicy
    {
        public AccentState AccentState;
        public int AccentFlags;
        public int GradientColor;
        public int AnimationId;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct WindowCompositionAttributeData
    {
        public WindowCompositionAttribute Attribute;
        public IntPtr Data;
        public int SizeOfData;
    }

    internal enum WindowCompositionAttribute
    {
        // ...
        WCA_ACCENT_POLICY = 19
        // ...
    }

    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        [DllImport("user32.dll")]
        internal static extern int SetWindowCompositionAttribute(IntPtr hwnd, ref WindowCompositionAttributeData data);

        Storyboard spinnerAnimation;
        BackgroundWorker process_tokens_worker = new BackgroundWorker();

        public MainWindow()
        {
            InitializeComponent();
            Directory.SetCurrentDirectory(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
            Left = (SystemParameters.WorkArea.Width - Width) / 2;
            Top = SystemParameters.WorkArea.Height / 4 - Height / 2;
            process_tokens_worker.DoWork += ProcessTokens;
            process_tokens_worker.RunWorkerCompleted += ProcessTokensCompleted;
            process_tokens_worker.WorkerReportsProgress = false;
            process_tokens_worker.WorkerSupportsCancellation = true;

            /*
            var closeButton = (Button)FindName("closeButton");
            var closeButtonImage = (closeButton.Content as Image);

            if (closeButtonImage != null)
            {
                closeButtonImage.RenderTransformOrigin = new Point(0.5, 0.5);
                closeButtonImage.RenderTransform = new RotateTransform(30);
            }
            */
            spinnerAnimation = (Storyboard)FindResource("spinnerAnimation");
            Storyboard.SetTarget(spinnerAnimation, spinnerImage);
        }

        private void ProcessTokensCompleted(object? sender, RunWorkerCompletedEventArgs e)
        {
            spinnerAnimation.Remove();
            spinnerImage.Visibility = Visibility.Hidden;
        }

        private void ProcessTokens(object? sender, DoWorkEventArgs e)
        {
            var bg_worker = sender as BackgroundWorker;
            var tokens = e.Argument as string[];
            if (tokens == null || bg_worker == null) return;

            Debug.WriteLine("Processing tokens...");
            if (tokens[0] == "restart" && tokens.Length == 2)
            {
                KillProcess(tokens[1]);
                StartProcess(tokens[1], bg_worker);
            }
            else if (tokens[0] == "kill" && tokens.Length == 2)
            {
                KillProcess(tokens[1]);
            }
            else if (tokens[0] == "start" && tokens.Length == 2)
            {
                StartProcess(tokens[1], bg_worker);
            }
        }

        internal void EnableBlur()
        {
            var windowHelper = new WindowInteropHelper(this);

            var accent = new AccentPolicy();
            accent.AccentState = AccentState.ACCENT_ENABLE_BLURBEHIND;

            var accentStructSize = Marshal.SizeOf(accent);

            var accentPtr = Marshal.AllocHGlobal(accentStructSize);
            Marshal.StructureToPtr(accent, accentPtr, false);

            var data = new WindowCompositionAttributeData();
            data.Attribute = WindowCompositionAttribute.WCA_ACCENT_POLICY;
            data.SizeOfData = accentStructSize;
            data.Data = accentPtr;

            SetWindowCompositionAttribute(windowHelper.Handle, ref data);

            Marshal.FreeHGlobal(accentPtr);
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            EnableBlur();
        }

        private void Window_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
                DragMove();
        }

        private void KillProcess(string process_name)
        {
            foreach (var process in Process.GetProcessesByName(process_name))
            {
                Debug.Write($"Killing '{process.ProcessName}'");
                if (process.MainWindowHandle != IntPtr.Zero)
                {
                    Debug.WriteLine($" with window called '{process.MainWindowTitle}'");
                }
                else
                { 
                    Debug.Write("\n");
                }
                process.Kill();
            }
        }

        private void StartFromAppsFolder(string model_id)
        { 
            Process.Start("explorer.exe", $"shell:appsFolder\\{model_id}");
        }

        private static string? SearchProcessInPath(string process_name, CancellationToken ct)
        { 
            var enviromentPath = Environment.GetEnvironmentVariable("PATH");

            if (enviromentPath == null) return null;

            var paths = enviromentPath.Split(';');
            foreach (var path in paths)
            {
                //Debug.WriteLine(path);
                if (Directory.Exists(path))
                {
                    foreach (var entry in Directory.GetFiles(path))
                    {
                        if (ct.IsCancellationRequested) return null;

                        //Debug.WriteLine($"\t{entry}");
                        var last_part = entry.Substring(entry.LastIndexOf('\\') + 1).ToLower();
                        if (process_name.Contains('.'))
                        {
                            if (process_name == last_part)
                            {
                                return entry;
                            }
                        }
                        else
                        {
                            var first_dot = last_part.IndexOf('.');
                            if (first_dot > 0)
                            {
                                last_part = last_part.Remove(first_dot);
                            }
                            if (process_name == last_part)
                            {
                                return entry;
                            }
                        }
                    }
                }
            }
            return null;
        }

        private void StartProcess(string process_name, BackgroundWorker bg_worker)
        {
            var tokenSource = new CancellationTokenSource();
            CancellationToken ct2 = tokenSource.Token;
            var search_in_path = Task.Run(() => SearchProcessInPath(process_name, ct2), tokenSource.Token);

            // GUID taken from https://docs.microsoft.com/en-us/windows/win32/shell/knownfolderid
            var FOLDERID_AppsFolder = new Guid("{1e87508d-89c2-42f0-8a7e-645a0f50ca58}");
            ShellObject appsFolder = (ShellObject)KnownFolderHelper.FromKnownFolderId(FOLDERID_AppsFolder);

            foreach (var app in (IKnownFolder)appsFolder)
            {
                if (search_in_path.IsCompletedSuccessfully && search_in_path.Result != null)
                {
                    Process.Start(search_in_path.Result);
                    return;
                }

                if (bg_worker.CancellationPending) {
                    Debug.WriteLine("Cancelation requested for start process");
                    break;
                }

                // The friendly app name
                string name = app.Name;
                // The ParsingName property is the AppUserModelID
                string appUserModelID = app.ParsingName; // or app.Properties.System.AppUserModel.ID
                                                         // You can even get the Jumbo icon in one shot

                //Debug.WriteLine($"{name}, {appUserModelID}");
                var last_part = appUserModelID.Substring(appUserModelID.LastIndexOfAny(new char[] { '!', '\\' }) + 1).ToLower();
                if (process_name.Contains('.'))
                {
                    if (process_name == last_part)
                    {
                        StartFromAppsFolder(appUserModelID);
                        break;
                    }
                }
                else
                {
                    var first_dot = last_part.IndexOf('.');
                    if (first_dot > 0)
                    {
                        last_part = last_part.Remove(first_dot);
                    }
                    if (process_name == last_part || name.ToLower() == process_name)
                    {
                        StartFromAppsFolder(appUserModelID);
                        break;
                    }
                }

                ImageSource icon = app.Thumbnail.ExtraLargeBitmapSource;
            }

            Debug.WriteLine("Cancelling starting process from path");
            tokenSource.Cancel();
        }

        private void TextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Return)
            {
                var tokens = textBox.Text.Trim().ToLower().Split(" ");
                spinnerAnimation.Begin();
                spinnerImage.Visibility = Visibility.Visible;

                process_tokens_worker.RunWorkerAsync(tokens);
            }
        }

        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape && !is_closing) Close();
        }

        private void NiceClose()
        { 
            if (!is_closing) Close();
        }

        private bool is_closing = false;

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            is_closing = true;
        }

        private void Window_Deactivated(object sender, EventArgs e)
        {
            NiceClose();
        }

        private void closeButton_Click(object sender, RoutedEventArgs e)
        {
            NiceClose();
        }

        private void textBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            process_tokens_worker.CancelAsync();
            spinnerAnimation.Remove();
            spinnerImage.Visibility = Visibility.Hidden;
        }
    }
}
