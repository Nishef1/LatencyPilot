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
            (_, _) => PreserveMonotonicBaselineProgress());
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

        var completedSequence = _baselineProgressStopwatch.Elapsed >= GetNominalBaselineDuration() - TimeSpan.FromSeconds(8);
        StopBaselineProgressExperience(completedSequence);
    }

    private void StartBaselineProgressExperience()
    {
        _baselineProgressStopwatch.Restart();
        _baselineProgressPercent = 0;
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

        var nominalDuration = GetNominalBaselineDuration();
        var elapsed = _baselineProgressStopwatch.Elapsed;
        var fraction = nominalDuration.TotalMilliseconds <= 0
            ? 0d
            : elapsed.TotalMilliseconds / nominalDuration.TotalMilliseconds;
        var percent = Math.Clamp(Math.Floor(fraction * 100d), 0d, 99d);

        if (percent > _baselineProgressPercent)
        {
            _baselineProgressPercent = percent;
            SetBaselineProgressValue(percent);
        }

        var remaining = nominalDuration - elapsed;
        if (remaining < TimeSpan.Zero)
        {
            remaining = TimeSpan.Zero;
        }

        var phase = GetBaselineProgressPhase(elapsed);
        BaselineStatusText.Text =
            $"{phase} · {_baselineProgressPercent:F0}% · {FormatApproximateRemainingTime(remaining)} remaining.";
    }

    private static TimeSpan GetNominalBaselineDuration() =>
        BaselinePreSequenceSettleDelay +
        TimeSpan.FromTicks(BaselineObservationDuration.Ticks * BaselineWindowCount) +
        TimeSpan.FromTicks(BaselineInterWindowDelay.Ticks * (BaselineWindowCount - 1));

    private static string GetBaselineProgressPhase(TimeSpan elapsed)
    {
        if (elapsed < BaselinePreSequenceSettleDelay)
        {
            return "Settling LatencyPilot/service";
        }

        var remaining = elapsed - BaselinePreSequenceSettleDelay;
        for (var index = 1; index <= BaselineWindowCount; index++)
        {
            if (remaining < BaselineObservationDuration)
            {
                return $"Capturing window {index} of {BaselineWindowCount}";
            }

            remaining -= BaselineObservationDuration;
            if (index < BaselineWindowCount)
            {
                if (remaining < BaselineInterWindowDelay)
                {
                    return $"Window {index} complete · settling for window {index + 1}";
                }

                remaining -= BaselineInterWindowDelay;
            }
        }

        return "Finalizing baseline quality";
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

    private void PreserveMonotonicBaselineProgress()
    {
        if (!_baselineProgressActive || _baselineProgressInternalUpdate)
        {
            return;
        }

        // The capture loop historically writes completed-window ordinals (1..5) into this bar.
        // Keep those writes from making the new time-based 0..100 progress indicator jump backwards.
        if (BaselineProgressBar.Value < _baselineProgressPercent)
        {
            SetBaselineProgressValue(_baselineProgressPercent);
        }
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
