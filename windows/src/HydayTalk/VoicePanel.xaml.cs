using System.Windows;
using System.Windows.Interop;
using HydayTalk.Core;
using static HydayTalk.Native.Win32;

namespace HydayTalk;

/// The floating status panel. It never takes focus, so the app the user is typing in stays
/// in front and receives the paste.
public partial class VoicePanel : Window
{
    private readonly AppController controller;

    internal VoicePanel(AppController controller)
    {
        InitializeComponent();
        this.controller = controller;
        controller.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(AppController.Level) or nameof(AppController.Elapsed)
                or nameof(AppController.Transcript) or nameof(AppController.Result) or nameof(AppController.Notice)) Update();
        };
        SizeChanged += (_, _) => { if (IsVisible) Anchor(); };
        SourceInitialized += (_, _) =>
        {
            var handle = new WindowInteropHelper(this).Handle;
            SetWindowLong(handle, GWL_EXSTYLE, GetWindowLong(handle, GWL_EXSTYLE) | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW);
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
        Dot.Fill = (System.Windows.Media.Brush)FindResource(phase switch
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
        var finished = phase is CapturePhase.Done or CapturePhase.Failed;
        Buttons.Visibility = finished ? Visibility.Visible : Visibility.Collapsed;
        CopyButton.Visibility = controller.Result.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        Hint.Visibility = recording ? Visibility.Visible : Visibility.Collapsed;
    }

    private void Copy_Click(object sender, RoutedEventArgs e) => controller.CopyResult();
    private void Close_Click(object sender, RoutedEventArgs e) => controller.DismissPanel();
}
