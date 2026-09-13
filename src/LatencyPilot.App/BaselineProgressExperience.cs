using System.Diagnostics;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;

namespace LatencyPilot.App;

public sealed partial class MainWindow
{
    private readonly Stopwatch _baselineProgressStopwatch = new();
    private DispatcherQueueTimer? _baselineProgressTimer;
    private bool _baselineProgressActive;
    private bool _baselineProgressInternalUpdate;
    private double _baselineProgressPercent;
    private int _baselineCompletedWindows;
    private TimeSpan _baselineLastWindowCompletedAt;
    private TimeSpan _baselineEstimatedTotalDuration;

    private void InitializeBaselineProgressExperience()
    {
        BaselineProgressBar.Minimum = 0;
        BaselineProgressBar.Maximum = 100;
        BaselineProgressBar.Value = 0;
        AutomationProperties.SetName(BaselineProgressBar, "Decision baseline progress");
        AutomationProperties.SetHelpText(
            BaselineProgressBar,
            "Shows overall progress across the service settle period and all five 20-second baseline windows.");

        _baselineProgressTimer = DispatcherQueue.CreateTimer();
        _baselineProgressTimer.Interval = TimeSpan.FromSeconds(1);
        _baselineProgressTimer.IsRepeating = true;
        _baselineProgressTimer.Tick += (_, _) => UpdateBaselineProgressExperience();

        BaselineVerdictText.RegisterPropertyChangedCallback(
            TextBlock.TextProperty,
            (_, _) => HandleBaselineVerdictChanged());

        BaselineProgressBar.RegisterPropertyChangedCallback(
            RangeBase.ValueProperty,
            (_, _) => HandleBaselineProgressValueChanged());
    }

    private void HandleBaselineVerdictChanged()
    {
        if (string.Equals(BaselineVerdictText.Text, "Capturing", StringComparison.OrdinalIgnoreCase))
        {
            StartBaselineProgressExperience();
            return;
        }

        if (!_baselineProgressActive)
        {
            return;
        }

        var completedSequence = _baselineCompletedWindows >= BaselineWindowCount ||
            BaselineStatusText.Text.Contains(
                $"Window {BaselineWindowCount} of {BaselineWindowCount} complete",
                StringComparison.OrdinalIgnoreCase);
        StopBaselineProgressExperience(completedSequence);
    }

    private void StartBaselineProgressExperience()
    {
        _baselineProgressStopwatch.Restart();
        _baselineProgressPercent = 0;
        _baselineCompletedWindows = 0;
        _baselineLastWindowCompletedAt = TimeSpan.Zero;
        _baselineEstimatedTotalDuration = GetNominalBaselineDuration();
        _baselineProgressActive = true;
        SetBaselineProgressValue(0);
        UpdateBaselineProgressExperience();
        _baselineProgressTimer?.Start();
    }

    private void StopBaselineProgressExperience(bool completedSequence)
    {
        _baselineProgressTimer?.Stop();
        _baselineProgressStopwatch.Stop();
        _baselineProgressActive = false;

        if (completedSequence)
        {
            _baselineProgressPercent = 100;
            SetBaselineProgressValue(100);
        }
    }

    private void UpdateBaselineProgressExperience()
    {
        if (!_baselineProgressActive)
        {
            return;
        }

        var elapsed = _baselineProgressStopwatch.Elapsed;
        var fraction = _baselineEstimatedTotalDuration.TotalMilliseconds <= 0
            ? 0d
            : elapsed.TotalMilliseconds / _baselineEstimatedTotalDuration.TotalMilliseconds;
        var percent = Math.Clamp(Math.Floor(fraction * 100d), 0d, 99d);

        if (percent > _baselineProgressPercent)
        {
            _baselineProgressPercent = percent;
            SetBaselineProgressValue(percent);
        }

        var remaining = _baselineEstimatedTotalDuration - elapsed;
        if (remaining < TimeSpan.Zero)
        {
            remaining = TimeSpan.Zero;
        }

        BaselineStatusText.Text =
            $"{GetBaselineProgressPhase(elapsed)} · {_baselineProgressPercent:F0}% · {FormatApproximateRemainingTime(remaining)} remaining.";
    }

    private static TimeSpan GetNominalBaselineDuration() =>
        BaselinePreSequenceSettleDelay +
        TimeSpan.FromTicks(BaselineObservationDuration.Ticks * BaselineWindowCount) +
        TimeSpan.FromTicks(BaselineInterWindowDelay.Ticks * (BaselineWindowCount - 1));

    private string GetBaselineProgressPhase(TimeSpan elapsed)
    {
        if (_baselineCompletedWindows == 0)
        {
            return elapsed < BaselinePreSequenceSettleDelay
                ? "Settling LatencyPilot/service"
                : $"Capturing window 1 of {BaselineWindowCount}";
        }

        if (_baselineCompletedWindows >= BaselineWindowCount)
        {
            return "Finalizing baseline quality";
        }

        var sinceLastWindow = elapsed - _baselineLastWindowCompletedAt;
        return sinceLastWindow < BaselineInterWindowDelay
            ? $"Window {_baselineCompletedWindows} complete · settling for window {_baselineCompletedWindows + 1}"
            : $"Capturing window {_baselineCompletedWindows + 1} of {BaselineWindowCount}";
    }

    private static string FormatApproximateRemainingTime(TimeSpan remaining)
    {
        if (remaining <= TimeSpan.FromSeconds(5))
        {
            return "≈ <5 s";
        }

        var roundedSeconds = (int)Math.Ceiling(remaining.TotalSeconds / 5d) * 5;
        var minutes = roundedSeconds / 60;
        var seconds = roundedSeconds % 60;

        return minutes > 0
            ? seconds > 0
                ? $"≈ {minutes} min {seconds} s"
                : $"≈ {minutes} min"
            : $"≈ {seconds} s";
    }

    private void HandleBaselineProgressValueChanged()
    {
        if (!_baselineProgressActive || _baselineProgressInternalUpdate)
        {
            return;
        }

        var externallyWrittenValue = BaselineProgressBar.Value;
        var completedWindowOrdinal = (int)Math.Round(externallyWrittenValue);
        if (completedWindowOrdinal >= 1 &&
            completedWindowOrdinal <= BaselineWindowCount &&
            Math.Abs(externallyWrittenValue - completedWindowOrdinal) < 0.001)
        {
            RecordCompletedBaselineWindow(completedWindowOrdinal);
        }

        // The capture loop historically writes completed-window ordinals (1..5) into this bar.
        // Restore the time-based 0..100 value immediately so progress never jumps backwards.
        if (BaselineProgressBar.Value < _baselineProgressPercent)
        {
            SetBaselineProgressValue(_baselineProgressPercent);
        }
    }

    private void RecordCompletedBaselineWindow(int windowNumber)
    {
        if (windowNumber <= _baselineCompletedWindows)
        {
            return;
        }

        _baselineCompletedWindows = windowNumber;
        _baselineLastWindowCompletedAt = _baselineProgressStopwatch.Elapsed;

        var interWindowTime = TimeSpan.FromTicks(
            BaselineInterWindowDelay.Ticks * Math.Max(0, windowNumber - 1));
        var measuredCaptureTime = _baselineLastWindowCompletedAt -
            BaselinePreSequenceSettleDelay -
            interWindowTime;
        if (measuredCaptureTime <= TimeSpan.Zero)
        {
            return;
        }

        var averageCaptureTicks = measuredCaptureTime.Ticks / windowNumber;
        var projectedCaptureTime = TimeSpan.FromTicks(averageCaptureTicks * BaselineWindowCount);
        var projectedTotal = BaselinePreSequenceSettleDelay +
            projectedCaptureTime +
            TimeSpan.FromTicks(BaselineInterWindowDelay.Ticks * (BaselineWindowCount - 1));

        // Completed-window timing includes real Service/ETW overhead, so use it to improve ETA.
        // Never project a total shorter than elapsed time or move the displayed percentage backwards.
        if (projectedTotal > _baselineLastWindowCompletedAt)
        {
            _baselineEstimatedTotalDuration = projectedTotal;
        }

        UpdateBaselineProgressExperience();
    }

    private void SetBaselineProgressValue(double value)
    {
        _baselineProgressInternalUpdate = true;
        try
        {
            BaselineProgressBar.Value = value;
        }
        finally
        {
            _baselineProgressInternalUpdate = false;
        }
    }
}
