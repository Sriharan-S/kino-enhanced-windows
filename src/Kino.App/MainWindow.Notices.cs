using Kino.Core;
using Kino.Core.Protocol;
using Microsoft.UI.Xaml;

namespace Kino;

/// <summary>"Waiting for…" holds (§14.10) and ready checks (§14.14), shown in a windowed popup over the video.</summary>
public sealed partial class MainWindow
{
    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _noticeTimer;
    private DateTimeOffset _hideNoticeAt = DateTimeOffset.MaxValue;
    private long _answeredReady = -1;

    private void AttachNotices(ScreenSession s)
    {
        _answeredReady = -1;
        s.HoldChanged += _ => _ui.TryEnqueue(() => { if (_session == s) RenderNotice(); });
        s.ReadyChanged += r => _ui.TryEnqueue(() =>
        {
            if (_session != s) return;
            if (r is { Done: true }) _hideNoticeAt = DateTimeOffset.Now.AddSeconds(5);
            RenderNotice();
        });
        if (_noticeTimer is null)
        {
            _noticeTimer = _ui.CreateTimer();
            _noticeTimer.Interval = TimeSpan.FromSeconds(1);
            _noticeTimer.Tick += (_, _) => RenderNotice(); // countdown, and hiding finished checks
            _noticeTimer.Start();
        }
        RenderNotice();
    }

    private void RenderNotice()
    {
        if (_session is not { } s)
        {
            NoticePopup.IsOpen = false;
            return;
        }
        ReadyYesButton.Visibility = ReadyNoButton.Visibility = ResumeAnywayButton.Visibility = NoticeDetail.Visibility = Visibility.Collapsed;
        string? text = null;

        if (s.Hold is { Count: > 0 } hold)
        {
            text = $"Waiting for {string.Join(", ", hold)}…";
            if (s.Can(Permissions.PlaybackToggle)) ResumeAnywayButton.Visibility = Visibility.Visible;
        }
        else if (s.Ready is { } r && (!r.Done || DateTimeOffset.Now < _hideNoticeAt))
        {
            int present = s.Viewers.Count(v => v.Connected);
            int ready = r.Answers.Count(a => a.Value);
            double left = Math.Max(0, (r.Deadline - DateTimeOffset.Now.ToUnixTimeMilliseconds()) / 1000);
            if (r.Done)
            {
                text = ready >= present ? "✅ Everyone is ready" : $"Ready check: {ready} of {present} ready";
            }
            else
            {
                text = s.Self is { } me && r.Answers.ContainsKey(me.Id) ? $"Ready check by {r.By}" : $"{r.By} asks: ready?";
                NoticeDetail.Text = $"{ready} of {present} ready · {left:0} s";
                NoticeDetail.Visibility = Visibility.Visible;
                bool answered = _answeredReady == r.Id || (s.Self is { } self && r.Answers.ContainsKey(self.Id));
                ReadyYesButton.Visibility = ReadyNoButton.Visibility = answered ? Visibility.Collapsed : Visibility.Visible;
            }
        }

        NoticeText.Text = text ?? "";
        NoticePopup.IsOpen = text is not null && _page == Page.Video;
        if (NoticePopup.IsOpen) LayoutOverlays();
    }

    private async void OnReadyCheckClick(object sender, RoutedEventArgs e)
    {
        if (_session is { } s) await s.StartReadyCheckAsync();
    }

    private async void OnReadyYes(object sender, RoutedEventArgs e) => await AnswerReadyAsync(true);

    private async void OnReadyNo(object sender, RoutedEventArgs e) => await AnswerReadyAsync(false);

    private async Task AnswerReadyAsync(bool ready)
    {
        if (_session is not { Ready: { Done: false } r } s) return;
        _answeredReady = r.Id;
        RenderNotice();
        await s.AnswerReadyAsync(r.Id, ready);
    }

    private async void OnResumeAnyway(object sender, RoutedEventArgs e)
    {
        if (_session is { } s) await s.ReleaseHoldAsync();
    }
}
