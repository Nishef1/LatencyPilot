from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
PATH = ROOT / "src/LatencyPilot.Benchmarking/Optimization/GpuAutoAffinitySession.cs"
text = PATH.read_text(encoding="utf-8")
start = text.find("    private async Task<IReadOnlyList<CandidateEvaluation>> RescreenTopCandidatesAsync(\n")
end = text.find("    private async Task<GpuAutoAffinitySessionResult> VerifyAndKeepFinalistAsync(\n", start)
if start < 0 or end < 0:
    raise SystemExit("adaptive finalist method anchors not found")
replacement = '''    private async Task<IReadOnlyList<CandidateEvaluation>> RescreenTopCandidatesAsync(
        GpuAutoAffinitySessionRequest request,
        IReadOnlyList<CandidateEvaluation> screeningEvaluations,
        GpuBenchmarkEvidence reference,
        List<GpuAutoAffinityCandidateReport> candidateReports,
        List<GpuAutoAffinityTrialReport> trialReports,
        Func<int> nextRunNumber,
        CancellationToken cancellationToken)
    {
        var shortlist = CreateAdaptiveShortlist(screeningEvaluations);
        if (shortlist.Length == 0)
        {
            return [];
        }

        // The screen contributes observation #1. Every finalist always receives
        // two independent fresh apply/restart/warm-up/score/rollback rounds first.
        // Only finalists that still lack a stable 3-run cluster receive at most
        // two more adaptive replacement rounds.
        var freshByProcessor = shortlist.ToDictionary(
            static item => item.Candidate.Processor,
            static _ => new List<GpuAutoAffinityTrialObservation>(capacity: 4));
        var stableByProcessor = new Dictionary<LogicalProcessorId, CandidateEvaluation>();
        var failureByProcessor = new Dictionary<LogicalProcessorId, string>();

        for (var round = 0; round < 2; round++)
        {
            var roundCandidates = shortlist
                .Where(item => !failureByProcessor.ContainsKey(item.Candidate.Processor))
                .Select(static item => item.Candidate)
                .ToArray();
            var roundSeed = unchecked(request.ShuffleSeed ^ (int)(0x9E3779B9u * (uint)(round + 1)));
            ShuffleDeterministically(roundCandidates, roundSeed);

            foreach (var candidate in roundCandidates)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    var fresh = await MeasureCandidateAsync(
                        candidate,
                        FinalistPhaseName,
                        repetitions: 1,
                        request.ScreeningDuration,
                        reference,
                        nextRunNumber,
                        trialReports,
                        cancellationToken).ConfigureAwait(false);
                    freshByProcessor[candidate.Processor].AddRange(fresh);
                }
                catch (SessionAbortException abort)
                {
                    failureByProcessor[candidate.Processor] = abort.Message;
                }
            }
        }

        foreach (var screened in shortlist)
        {
            if (failureByProcessor.ContainsKey(screened.Candidate.Processor))
            {
                continue;
            }

            var combined = screened.Observations
                .Concat(freshByProcessor[screened.Candidate.Processor])
                .ToArray();
            var evaluation = CreateEvaluation(screened.Candidate, combined);
            if (evaluation.IsRankable)
            {
                stableByProcessor[screened.Candidate.Processor] = evaluation;
            }
        }

        for (var replacementRound = 0; replacementRound < 2; replacementRound++)
        {
            var roundCandidates = shortlist
                .Where(item =>
                    !failureByProcessor.ContainsKey(item.Candidate.Processor) &&
                    !stableByProcessor.ContainsKey(item.Candidate.Processor))
                .Select(static item => item.Candidate)
                .ToArray();
            if (roundCandidates.Length == 0)
            {
                break;
            }

            var roundSeed = unchecked(request.ShuffleSeed ^ (int)(0x9E3779B9u * (uint)(replacementRound + 3)));
            ShuffleDeterministically(roundCandidates, roundSeed);
            foreach (var candidate in roundCandidates)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    var fresh = await MeasureCandidateAsync(
                        candidate,
                        FinalistPhaseName,
                        repetitions: 1,
                        request.ScreeningDuration,
                        reference,
                        nextRunNumber,
                        trialReports,
                        cancellationToken).ConfigureAwait(false);
                    freshByProcessor[candidate.Processor].AddRange(fresh);

                    var screened = shortlist.Single(item => item.Candidate.Processor == candidate.Processor);
                    var combined = screened.Observations
                        .Concat(freshByProcessor[candidate.Processor])
                        .ToArray();
                    var evaluation = CreateEvaluation(candidate, combined);
                    if (evaluation.IsRankable)
                    {
                        stableByProcessor[candidate.Processor] = evaluation;
                    }
                }
                catch (SessionAbortException abort)
                {
                    failureByProcessor[candidate.Processor] = abort.Message;
                }
            }
        }

        var finalists = new List<CandidateEvaluation>(shortlist.Length);
        foreach (var screened in shortlist)
        {
            CandidateEvaluation evaluation;
            if (failureByProcessor.TryGetValue(screened.Candidate.Processor, out var failure))
            {
                evaluation = CandidateEvaluation.Unrankable(
                    screened.Candidate,
                    failure,
                    1 + freshByProcessor[screened.Candidate.Processor].Count);
            }
            else if (stableByProcessor.TryGetValue(screened.Candidate.Processor, out var stable))
            {
                evaluation = stable;
            }
            else
            {
                evaluation = CreateEvaluation(
                    screened.Candidate,
                    screened.Observations.Concat(freshByProcessor[screened.Candidate.Processor]).ToArray());
            }

            var report = ToReport(
                FinalistPhaseName,
                evaluation,
                freshByProcessor[screened.Candidate.Processor].Count);
            candidateReports.Add(report);
            await PublishCandidateReportAsync(report).ConfigureAwait(false);
            finalists.Add(evaluation);
        }

        return finalists;
    }

'''
PATH.write_text(text[:start] + replacement + text[end:], encoding="utf-8", newline="\n")
