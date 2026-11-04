using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using HydayTalk.Core;
using static HydayTalk.Native.Win32;

namespace HydayTalk;

/// The floating status panel. It never takes focus, so the app the user is typing in stays
/// in front and receives the paste.
public sealed class VoicePanel : Window
{
    private readonly AppController controller;
#pragma warning disable CS0649 // assigned from the XAML by Xaml.Load
    private Ellipse Dot = null!;
    private TextBlock Status = null!, Elapsed = null!, ActionLabel = null!, ResultText = null!, NoticeText = null!, Hint = null!;
    private Border Meter = null!, MeterFill = null!;
    private ScrollViewer ResultScroll = null!;
    private StackPanel Buttons = null!;
    private Button CopyButton = null!, RetryButton = null!;
#pragma warning restore CS0649

    internal VoicePanel(AppController controller)
    {
        this.controller = controller;
        // An opaque borderless window sized to its content; Windows 11 rounds its corners. A per-pixel
        // transparent window would need re-shaping whenever the card grows.
        Title = "Hyday Talk"; Width = 420; SizeToContent = SizeToContent.Height;
        WindowStyle = WindowStyle.None; Background = (Brush)FindResource("VoiceSurface");
        ResizeMode = ResizeMode.NoResize; ShowInTaskbar = false; Topmost = true; ShowActivated = false; Focusable = false;
        Content = Xaml.Load<FrameworkElement>(this, "VoicePanel.xaml");
        controller.PropertyChanged += (_, e) =>
        {
            if (IsVisible && e.PropertyName is nameof(AppController.Level) or nameof(AppController.Elapsed) or nameof(AppController.Transcript)
                or nameof(AppController.Result) or nameof(AppController.Notice) or nameof(AppController.FailedRecordId)) Update();
        };
        SizeChanged += (_, _) => { if (IsVisible) Anchor(); };
        SourceInitialized += (_, _) =>
        {
            var handle = new WindowInteropHelper(this).Handle;
            SetWindowLong(handle, GWL_EXSTYLE, GetWindowLong(handle, GWL_EXSTYLE) | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW);
            var round = DWMWCP_ROUND;
            DwmSetWindowAttribute(handle, DWMWA_WINDOW_CORNER_PREFERENCE, ref round, sizeof(int));
        };
    }

    internal void Refresh()
    {
        if (controller.Phase == CapturePhase.Idle) { Hide(); return; }
        Update();
        if (!IsVisible) { Show(); Anchor(); }
    }

    /// Bottom centre of the primary work area, above the taskbar.
    private void Anchor()
    {
        var area = SystemParameters.WorkArea;
        Left = area.Left + (area.Width - ActualWidth) / 2;
        Top = area.Bottom - ActualHeight - 24;
    }

    private void Update()
    {
        var phase = controller.Phase;
        Status.Text = phase.Title();
        ActionLabel.Text = $"{controller.Action.Title()} · 輸出到 {controller.Destination}";
        Dot.Fill = (Brush)FindResource(phase switch
        {
            CapturePhase.Recording => "Accent",
            CapturePhase.Done => "Success",
            CapturePhase.Failed => "Danger",
            _ => "Action",
        });
        var recording = phase == CapturePhase.Recording;
        Meter.Visibility = recording ? Visibility.Visible : Visibility.Collapsed;
        MeterFill.Width = Math.Max(0, Meter.ActualWidth * controller.Level);
        Elapsed.Text = phase is CapturePhase.Recording or CapturePhase.Transcribing or CapturePhase.Processing
            ? TimeSpan.FromSeconds(controller.Elapsed).ToString(@"m\:ss") : "";
        var text = controller.Result.Length > 0 ? controller.Result : controller.Transcript;
        ResultText.Text = text;
        ResultScroll.Visibility = text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        NoticeText.Text = controller.Notice;
        NoticeText.Visibility = controller.Notice.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        Buttons.Visibility = phase is CapturePhase.Done or CapturePhase.Failed ? Visibility.Visible : Visibility.Collapsed;
        CopyButton.Visibility = controller.Result.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        RetryButton.Visibility = phase == CapturePhase.Failed && controller.FailedRecordId != null ? Visibility.Visible : Visibility.Collapsed;
        Hint.Visibility = recording ? Visibility.Visible : Visibility.Collapsed;
    }

    private void Copy_Click(object sender, RoutedEventArgs e) => controller.CopyResult();
    private void Retry_Click(object sender, RoutedEventArgs e) => controller.RetryFailed();
    private void Close_Click(object sender, RoutedEventArgs e) => controller.DismissPanel();
}
