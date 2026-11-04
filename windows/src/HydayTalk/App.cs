using System.Windows;
using HydayTalk.Native;
using Forms = System.Windows.Forms;

namespace HydayTalk;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        var app = new App { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.Run();
    }
}

/// Runs in the notification area: the keyboard hook listens for the shortcut, the voice panel
/// floats while dictating, and the settings window opens from the tray icon.
public sealed class App : Application
{
    private Mutex? single;
    private AppController? controller;
    private KeyboardHook? hook;
    private VoicePanel? panel;
    private MainWindow? window;
    private Forms.NotifyIcon? tray;

    public static System.Drawing.Icon AppIcon
    {
        get
        {
            using var stream = typeof(App).Assembly.GetManifestResourceStream("HydayTalk.ico")!;
            return new System.Drawing.Icon(stream);
        }
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        Resources.MergedDictionaries.Add(Xaml.Parse<ResourceDictionary>("Theme.xaml"));
        DispatcherUnhandledException += (_, args) =>
        {
            // Keep the tray app alive; the panel shows what went wrong.
            if (controller != null) controller.Notice = "發生錯誤：" + args.Exception.Message;
            args.Handled = true;
        };
        single = new Mutex(true, @"Local\tw.muki.hyday-talk", out var first);
        if (!first) { MessageBox.Show("Hyday Talk 已在執行，請從系統匣圖示開啟。", "Hyday Talk"); Shutdown(); return; }

        controller = new AppController(Dispatcher);
        panel = new VoicePanel(controller);
        controller.PanelChanged += () => panel.Refresh();
        controller.ShowSettingsRequested += page => ShowSettings(page);

        hook = new KeyboardHook { IsRecording = () => controller.Phase == CapturePhase.Recording };
        // Leave the hook callback quickly; Windows drops hooks that take too long.
        hook.Action += action => Dispatcher.BeginInvoke(() => controller.Handle(action));
        hook.ReturnPressed += () => Dispatcher.BeginInvoke(controller.ReturnPressed);
        controller.ShortcutReady = hook.Start();
        controller.Start();

        tray = new Forms.NotifyIcon
        {
            Icon = AppIcon,
            Text = "Hyday Talk：按右 Ctrl 開始聽寫",
            Visible = true,
            ContextMenuStrip = new Forms.ContextMenuStrip(),
        };
        tray.ContextMenuStrip.Items.Add("開啟 Hyday Talk", null, (_, _) => ShowSettings());
        tray.ContextMenuStrip.Items.Add("開始聽寫", null, (_, _) => controller.Begin(Core.VoiceAction.Dictate));
        tray.ContextMenuStrip.Items.Add("-");
        tray.ContextMenuStrip.Items.Add("結束", null, (_, _) => Shutdown());
        tray.DoubleClick += (_, _) => ShowSettings();

        var firstRun = !File.Exists(Path.Combine(controller.DataDirectory, "settings.json"));
        controller.SaveSettings();
        if (firstRun || !controller.ShortcutReady || !Permissions.MicrophoneAllowed() || e.Args.Contains("--show")) ShowSettings();
        else tray.ShowBalloonTip(3000, "Hyday Talk", "已在背景執行。按一下右 Ctrl 開始聽寫，再按一次完成。", Forms.ToolTipIcon.None);
    }

    private void ShowSettings(string? page = null)
    {
        if (controller == null) return;
        if (window is not { IsLoaded: true }) window = new MainWindow(controller, Shutdown);
        window.Show();
        if (window.WindowState == WindowState.Minimized) window.WindowState = WindowState.Normal;
        window.Activate();
        if (page != null) window.ShowPage(page);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        hook?.Dispose();
        controller?.Dispose();
        if (tray != null) { tray.Visible = false; tray.Dispose(); }
        single?.Dispose();
        base.OnExit(e);
    }
}
