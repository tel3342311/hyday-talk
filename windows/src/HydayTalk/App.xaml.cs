using System.Windows;
using HydayTalk.Native;
using Forms = System.Windows.Forms;

namespace HydayTalk;

/// Runs in the notification area: the keyboard hook listens for the shortcut, the voice panel
/// floats while dictating, and the settings window opens from the tray icon.
public partial class App : Application
{
    private Mutex? single;
    private AppController? controller;
    private KeyboardHook? hook;
    private VoicePanel? panel;
    private MainWindow? window;
    private Forms.NotifyIcon? tray;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        single = new Mutex(true, @"Local\tw.muki.hyday-talk", out var first);
        if (!first) { MessageBox.Show("Hyday Talk 已在執行，請從系統匣圖示開啟。", "Hyday Talk"); Shutdown(); return; }

        controller = new AppController(Dispatcher);
        panel = new VoicePanel(controller);
        controller.PanelChanged += () => panel.Refresh();

        hook = new KeyboardHook { IsRecording = () => controller.Phase == CapturePhase.Recording };
        // Leave the hook callback quickly; Windows drops hooks that take too long.
        hook.Action += action => Dispatcher.BeginInvoke(() => controller.Handle(action));
        controller.ShortcutReady = hook.Start();

        tray = new Forms.NotifyIcon
        {
            Icon = System.Drawing.SystemIcons.Application,
            Text = "Hyday Talk：按右 Ctrl 開始聽寫",
            Visible = true,
            ContextMenuStrip = new Forms.ContextMenuStrip(),
        };
        tray.ContextMenuStrip.Items.Add("開啟 Hyday Talk", null, (_, _) => ShowSettings());
        tray.ContextMenuStrip.Items.Add("開始聽寫", null, (_, _) => controller.Begin(Core.VoiceAction.Dictate));
        tray.ContextMenuStrip.Items.Add("-");
        tray.ContextMenuStrip.Items.Add("結束", null, (_, _) => Shutdown());
        tray.DoubleClick += (_, _) => ShowSettings();

        if (!controller.ShortcutReady) ShowSettings();
        else tray.ShowBalloonTip(3000, "Hyday Talk", "已在背景執行。按一下右 Ctrl 開始聽寫，再按一次完成。", Forms.ToolTipIcon.None);
    }

    private void ShowSettings()
    {
        if (controller == null) return;
        if (window is not { IsLoaded: true }) window = new MainWindow(controller);
        window.Show();
        if (window.WindowState == WindowState.Minimized) window.WindowState = WindowState.Normal;
        window.Activate();
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
