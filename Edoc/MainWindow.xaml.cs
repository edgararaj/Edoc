using System;
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
        BackgroundWorker process_text_worker = new BackgroundWorker();

        public MainWindow()
        {
            InitializeComponent();
            Directory.SetCurrentDirectory(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
            Left = (SystemParameters.WorkArea.Width - Width) / 2;
            Top = SystemParameters.WorkArea.Height / 4 - Height / 2;

            process_text_worker.DoWork += ProcessText;
            process_text_worker.RunWorkerCompleted += ProcessTextCompleted;
            process_text_worker.WorkerReportsProgress = false;
            process_text_worker.WorkerSupportsCancellation = true;

            spinnerAnimation = (Storyboard)FindResource("spinnerAnimation");
            Storyboard.SetTarget(spinnerAnimation, spinnerImage);
        }

        private void ProcessTextCompleted(object? sender, RunWorkerCompletedEventArgs e)
        {
            spinnerImage.Visibility = Visibility.Hidden;
            spinnerAnimation.Remove();
        }

        private void ConsumeCommand(Token.Type type, IEnumerable<Token> token_list, BackgroundWorker bg_worker)
        {
            if (token_list.Count() == 0) return;

            var process = string.Join(" ", token_list.Select(x => x.text));

            switch (type)
            {
                case Token.Type.CommandRestart:
                    KillProcess(process);
                    StartProcess(process, bg_worker);
                    break;
                case Token.Type.CommandKill:
                    KillProcess(process);
                    break;
                case Token.Type.CommandStart:
                    StartProcess(process, bg_worker);
                    break;
                case Token.Type.CommandShellStart:
                    StartShellProcess(process);
                    break;
            }

        }

        private void ProcessTokens(List<Token> token_list, BackgroundWorker bg_worker)
        {
            for (int token_list_index = 0; token_list_index < token_list.Count; token_list_index++)
            {
                var token = token_list[token_list_index];
                switch (token.type)
                {
                    case Token.Type.CommandRestart:
                    case Token.Type.CommandShellStart:
                    case Token.Type.CommandStart:
                    case Token.Type.CommandKill:
                        ConsumeCommand(token.type, token_list.Skip(token_list_index + 1), bg_worker);
                        return;
                    case Token.Type.Text:
                        ConsumeCommand(Token.Type.CommandStart, token_list, bg_worker);
                        return;
                }
            }
        }

        public struct Token {
            public enum Type { 
                CommandRestart, CommandKill, CommandStart, CommandShellStart, Text
            }

            public Type type;
            public int start, length;
            public string text;
        }

        private void ProcessText(object? sender, DoWorkEventArgs e)
        {
            var bg_worker = sender as BackgroundWorker;
            var textbox = e.Argument as string;
            if (textbox == null || bg_worker == null) return;

            Debug.WriteLine("Processing text...");
            var token_list = new List<Token>();
            var tokens = textbox.Trim().ToLower().Split(" ");
            foreach (var token in tokens)
            {
                var start = textbox.IndexOf(token);
                if (token[0] == '!')
                {
                    token_list.Add(new Token { type = Token.Type.CommandShellStart, start = start, length = token.Length });
                    if (token.Count() > 1)
                    {
                        token_list.Add(new Token { type = Token.Type.Text, start = start, length = token.Length, text = token.Substring(1) });
                    }
                    continue;
                }

                switch (token)
                {
                    case "restart":
                        token_list.Add(new Token { type = Token.Type.CommandRestart, start = start, length = token.Length });
                        break;
                    case "kill":
                        token_list.Add(new Token { type = Token.Type.CommandKill, start = start, length = token.Length });
                        break;
                    case "start":
                        token_list.Add(new Token { type = Token.Type.CommandStart, start = start, length = token.Length });
                        break;
                    default:
                        token_list.Add(new Token { type = Token.Type.Text, start = start, length = token.Length, text = token});
                        break;
                }

            }

            ProcessTokens(token_list, bg_worker);

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
            //EnableBlur();
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

        private void StartShellProcess(string process_name)
        {
            Process.Start("cmd.exe", $"/k {process_name}");
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
                spinnerAnimation.Begin();
                spinnerImage.Visibility = Visibility.Visible;

                process_text_worker.RunWorkerAsync(textBox.Text);
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
            process_text_worker.CancelAsync();
            spinnerAnimation.Remove();
            spinnerImage.Visibility = Visibility.Hidden;
        }
    }
}
