using System.ComponentModel;
using System.Globalization;
using System.Security;
using System.Security.Principal;
using LatencyPilot.Core.Devices;
using LatencyPilot.Core.System;
using LatencyPilot.Persistence;
using LatencyPilot.Platform.Windows.Devices;
using LatencyPilot.Platform.Windows.Etw;
using LatencyPilot.Platform.Windows.System;
using LatencyPilot.Protocol;
using LatencyPilot.Service;

return PhysicalValidationProgram.Run(args);

internal static class PhysicalValidationProgram
{
    private static readonly Guid DisplayDeviceClass = new("4D36E968-E325-11CE-BFC1-08002BE10318");
    private static readonly TimeSpan RuntimePlacementCaptureDuration = TimeSpan.FromSeconds(20);
    private const string MutationConfirmationFlag = "--confirm-physical-mutation";
    private const string GpuAffinityMutationKind = "gpu-interrupt-affinity";

    internal static int Run(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);

        try
        {
            if (args.Length == 0)
            {
                PrintUsage();
                return 2;
            }

            return args[0] switch
            {
                "inspect" => Inspect(args),
                "list-gpus" => ListGpus(args),
                "plan-gpu-affinity" => PlanGpuAffinity(args),
                "verify-gpu-placement" => VerifyGpuPlacement(args),
                "prepare-gpu-affinity" => PrepareGpuAffinity(args),
                "apply" => Apply(args),
                "rollback" => Rollback(args),
                "recover" => Recover(args),
                "--help" or "-h" or "help" => Help(args),
                _ => throw new ArgumentException($"Unknown command '{args[0]}'."),
            };
        }
        catch (Exception exception) when (exception is
            ArgumentException or
            InvalidOperationException or
            InvalidDataException or
            IOException or
            UnauthorizedAccessException or
            SecurityException or
            Win32Exception or
            NotSupportedException or
            OverflowException or
            PlatformNotSupportedException)
        {
            Console.Error.WriteLine($"ERROR: {exception.Message}");
            return 1;
        }
    }

    private static int Help(string[] args)
    {
        EnsureExactArgumentCount(args, 1);
        PrintUsage();
        return 0;
    }

    private static int Inspect(string[] args)
    {
        EnsureExactArgumentCount(args, 1);
        EnsureWindows();

        var unresolved = MutationJournalReadOnlyInspector.GetUnresolved(
            MutationJournal.GetDefaultDatabasePath());
        Console.WriteLine($"Mutation journal: READY; unresolved={unresolved.Count.ToString(CultureInfo.InvariantCulture)}");

        foreach (var entry in unresolved)
        {
            var inspection = MutationRecoveryAssessment.Inspect(entry);
            Console.WriteLine(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"experiment={entry.ExperimentId:D} state={entry.State} target={entry.TargetId} relation={inspection.StoredStateRelation} recovery={inspection.RecoveryPlan.Action}"));
            if (!string.IsNullOrWhiteSpace(inspection.Error))
            {
                Console.WriteLine($"  assessment-error={inspection.Error}");
            }

            Console.WriteLine($"  recovery-reason={inspection.RecoveryPlan.Reason}");
        }

        return 0;
    }

    private static int ListGpus(string[] args)
    {
        EnsureExactArgumentCount(args, 1);
        EnsureWindows();

        var devices = DeviceInventoryReader.CapturePresentDevices().Devices
            .Where(static device => device.ClassGuid == DisplayDeviceClass)
            .OrderBy(static device => device.InstanceId, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        Console.WriteLine($"Present display adapters: {devices.Length.ToString(CultureInfo.InvariantCulture)}");
        foreach (var device in devices)
        {
            Console.WriteLine($"device={device.InstanceId}");
            Console.WriteLine($"  name={device.DisplayName}");
            Console.WriteLine($"  driver={device.Driver.Version ?? "unavailable"}");
            Console.WriteLine(
                $"  allocated-interrupt-status={device.InterruptResources.ReadStatus} count={device.InterruptResources.Resources.Count.ToString(CultureInfo.InvariantCulture)}");
            foreach (var resource in device.InterruptResources.Resources)
            {
                Console.WriteLine(
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"  irq={resource.Irq} group={resource.ProcessorGroup} affinity=0x{resource.AffinityMask:X} flags=0x{resource.RawFlags:X4}"));
            }
        }

        return 0;
    }

    private static int PlanGpuAffinity(string[] args)
    {
        var options = ParseOptions(args, "--evidence");
        EnsureWindows();

        var plan = BaselineEvidenceCandidatePlan.Create(options.GetRequiredValue("--evidence"));
        Console.WriteLine("GPU affinity candidate plan: READ-ONLY");
        Console.WriteLine($"evidence-revision={plan.SourceRevisionId ?? "unavailable"}");
        Console.WriteLine($"validated-windows={plan.WindowCount.ToString(CultureInfo.InvariantCulture)}");
        Console.WriteLine($"cpu-set-metadata={(plan.CpuSetMetadataAvailable ? "available" : "unavailable")}");
        Console.WriteLine($"candidate-count={plan.Candidates.Count.ToString(CultureInfo.InvariantCulture)}");

        for (var index = 0; index < plan.Candidates.Count; index++)
        {
            var candidate = plan.Candidates[index];
            Console.WriteLine(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"rank={index + 1} core={candidate.PhysicalCoreIndex} group={candidate.Processor.Group} cpu={candidate.Processor.Number} pressure={candidate.ObservedPressureScore:P4} efficiency-class={candidate.EfficiencyClass} smt={candidate.CoreUsesSmt}"));
        }

        Console.WriteLine("No journal or device state was changed. Use one listed processor explicitly with prepare-gpu-affinity only after reviewing the plan.");
        return 0;
    }

    private static int VerifyGpuPlacement(string[] args)
    {
        var options = ParseOptions(args, "--experiment");
        RequireKernelCaptureAuthority();
        var experimentId = ParseExperimentId(options.GetRequiredValue("--experiment"));

        var entry = MutationJournalReadOnlyInspector.TryGet(
                MutationJournal.GetDefaultDatabasePath(),
                experimentId)
            ?? throw new InvalidOperationException(
                $"Mutation journal entry {experimentId:D} was not found.");
        if (!string.Equals(entry.Kind, GpuAffinityMutationKind, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Mutation journal entry {experimentId:D} is kind '{entry.Kind}', not GPU interrupt affinity.");
        }

        if (entry.State is not MutationJournalState.Applied and
            not MutationJournalState.Measuring and
            not MutationJournalState.AwaitingDecision)
        {
            throw new InvalidOperationException(
                $"Runtime placement verification requires an active experiment; journal state is {entry.State}.");
        }

        var original = GpuInterruptAffinityJournalCodec.DeserializeOriginal(entry.OriginalStateJson);
        var candidate = GpuInterruptAffinityJournalCodec.DeserializeCandidate(entry.CandidateStateJson);
        if (!string.Equals(original.DeviceInstanceId, entry.TargetId, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "GPU affinity journal target does not match its captured original-state device instance ID.");
        }

        var target = DeviceInventoryReader.CapturePresentDevices().Devices.FirstOrDefault(device =>
            device.ClassGuid == DisplayDeviceClass &&
            string.Equals(device.InstanceId, original.DeviceInstanceId, StringComparison.OrdinalIgnoreCase));
        if (target is null)
        {
            throw new InvalidOperationException("The journaled GPU is no longer a present display adapter.");
        }

        if (!string.Equals(target.Driver.Version, original.DriverVersion, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "The display-driver version changed after the experiment was prepared; runtime placement is not comparable.");
        }

        Console.WriteLine($"experiment={entry.ExperimentId:D}");
        Console.WriteLine($"state={entry.State}");
        Console.WriteLine($"target={target.InstanceId}");
        Console.WriteLine($"driver={target.Driver.Version ?? "unavailable"}");
        Console.WriteLine(
            string.Create(
                CultureInfo.InvariantCulture,
                $"candidate=group {candidate.ProcessorGroup}, CPU {candidate.ProcessorNumber}, mask 0x{candidate.AffinityMask:X}"));

        var resources = target.InterruptResources;
        Console.WriteLine(
            $"allocated-interrupt-status={resources.ReadStatus} count={resources.Resources.Count.ToString(CultureInfo.InvariantCulture)}");
        if (resources.ReadStatus != InterruptResourceReadStatus.Available || resources.Resources.Count == 0)
        {
            Console.WriteLine("allocated-affinity-match=false");
            Console.WriteLine("placement-proof=unavailable; exact-target allocated interrupt resources could not be established.");
            return 3;
        }

        foreach (var resource in resources.Resources)
        {
            Console.WriteLine(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"allocated-irq={resource.Irq} group={resource.ProcessorGroup} affinity=0x{resource.AffinityMask:X} flags=0x{resource.RawFlags:X4}"));
        }

        var allocatedMatchesCandidate = resources.Resources.All(resource =>
            resource.ProcessorGroup == candidate.ProcessorGroup &&
            resource.AffinityMask == candidate.AffinityMask);
        Console.WriteLine($"allocated-affinity-match={allocatedMatchesCandidate}");
        if (!allocatedMatchesCandidate)
        {
            Console.WriteLine("placement-proof=failed; stored policy is not independently confirmed by the exact target's allocated interrupt affinity.");
            return 3;
        }

        Console.WriteLine(
            $"Capturing {RuntimePlacementCaptureDuration.TotalSeconds:F0}s of raw kernel DPC/ISR evidence. Keep the representative workload active and repeatable.");
        var capture = KernelLatencyCapture.Capture(
            new KernelLatencyCaptureOptions(
                RuntimePlacementCaptureDuration,
                ObservationProtocol.MaximumCaptureEvents));
        Console.WriteLine($"capture-valid={capture.IsValid}");
        Console.WriteLine($"events-lost={capture.EventsLost.ToString(CultureInfo.InvariantCulture)}");
        Console.WriteLine($"invalid-events={capture.InvalidEventCount.ToString(CultureInfo.InvariantCulture)}");
        Console.WriteLine($"invalid-image-events={capture.InvalidImageEventCount.ToString(CultureInfo.InvariantCulture)}");
        Console.WriteLine($"event-limit-reached={capture.EventLimitReached}");
        Console.WriteLine($"dpc-events={capture.DpcCount.ToString(CultureInfo.InvariantCulture)}");
        Console.WriteLine($"isr-events={capture.IsrCount.ToString(CultureInfo.InvariantCulture)}");
        Console.WriteLine($"resolved-module-events={capture.ResolvedModuleEventCount.ToString(CultureInfo.InvariantCulture)}");
        Console.WriteLine($"unresolved-module-events={capture.UnresolvedModuleEventCount.ToString(CultureInfo.InvariantCulture)}");

        try
        {
            var placement = GpuInterruptRuntimePlacementVerifier.Analyze(
                capture,
                original.DeviceInstanceId,
                candidate);
            Console.WriteLine($"service-module={placement.DriverServiceName}");
            Console.WriteLine($"service-module-isr-events={placement.MatchingResolvedIsrEventCount.ToString(CultureInfo.InvariantCulture)}");
            Console.WriteLine($"service-module-target-isr-events={placement.TargetProcessorIsrEventCount.ToString(CultureInfo.InvariantCulture)}");
            Console.WriteLine($"service-module-off-target-isr-events={placement.OffTargetIsrEventCount.ToString(CultureInfo.InvariantCulture)}");
            Console.WriteLine($"unresolved-isr-events={placement.UnresolvedIsrEventCount.ToString(CultureInfo.InvariantCulture)}");
            foreach (var processor in placement.ObservedProcessors)
            {
                Console.WriteLine(
                    $"service-module-isr-cpu={processor.ProcessorNumber.ToString(CultureInfo.InvariantCulture)} count={processor.IsrEventCount.ToString(CultureInfo.InvariantCulture)}");
            }

            Console.WriteLine(
                $"service-module-correlation={(placement.HasRuntimeEvidence ? "observed" : "not-observed")}");
        }
        catch (Exception exception) when (exception is
            InvalidOperationException or
            Win32Exception or
            InvalidDataException)
        {
            Console.WriteLine("service-module-correlation=unavailable");
            Console.WriteLine($"service-module-correlation-reason={exception.Message}");
        }

        Console.WriteLine(
            capture.IsValid
                ? "placement-proof=exact-target allocated affinity confirmed; clean ETW runtime observation captured. Service-module ISR correlation is supplementary evidence only."
                : "placement-proof=incomplete; allocated affinity matched, but ETW capture integrity was not clean.");
        return capture.IsValid ? 0 : 3;
    }

    private static int PrepareGpuAffinity(string[] args)
    {
        var options = ParseOptions(args, "--device", "--processor");
        RequirePhysicalMutationAuthority(options);

        var deviceInstanceId = options.GetRequiredValue("--device");
        var processorText = options.GetRequiredValue("--processor");
        if (!byte.TryParse(processorText, NumberStyles.None, CultureInfo.InvariantCulture, out var processorNumber) ||
            processorNumber >= 64)
        {
            throw new ArgumentException(
                "--processor must identify an existing group-0 logical processor from 0 through 63.");
        }

        var topology = ProcessorTopologyReader.Capture();
        var candidate = GpuInterruptAffinityCandidate.Create(
            topology,
            new LogicalProcessorId(0, processorNumber));

        var journal = OpenJournal();
        var transaction = new GpuInterruptAffinityMutationTransaction(journal);
        var prepared = transaction.Prepare(deviceInstanceId, candidate);

        Console.WriteLine("Prepared GPU interrupt-affinity experiment; no device policy write was attempted.");
        PrintJournalEntry(prepared);
        Console.WriteLine(
            string.Create(
                CultureInfo.InvariantCulture,
                $"candidate=group {candidate.ProcessorGroup}, CPU {candidate.ProcessorNumber}, mask 0x{candidate.AffinityMask:X}"));
        return 0;
    }

    private static int Apply(string[] args)
    {
        var options = ParseOptions(args, "--experiment");
        RequirePhysicalMutationAuthority(options);
        var experimentId = ParseExperimentId(options.GetRequiredValue("--experiment"));

        var transaction = new GpuInterruptAffinityMutationTransaction(OpenJournal());
        var result = transaction.ApplyAndActivate(experimentId);
        PrintMutationStep(result);
        return result.JournalEntry.State == MutationJournalState.Applied ? 0 : 3;
    }

    private static int Rollback(string[] args)
    {
        var options = ParseOptions(args, "--experiment");
        RequirePhysicalMutationAuthority(options);
        var experimentId = ParseExperimentId(options.GetRequiredValue("--experiment"));

        var transaction = new GpuInterruptAffinityMutationTransaction(OpenJournal());
        var result = transaction.RollbackAndActivate(experimentId);
        PrintMutationStep(result);
        return result.JournalEntry.State == MutationJournalState.Reverted ? 0 : 3;
    }

    private static int Recover(string[] args)
    {
        var options = ParseOptions(args, "--experiment");
        RequirePhysicalMutationAuthority(options);
        var experimentId = ParseExperimentId(options.GetRequiredValue("--experiment"));

        var executor = new MutationRecoveryExecutor(OpenJournal());
        var result = executor.Execute(experimentId);
        Console.WriteLine(
            $"recovery-plan={result.Inspection.RecoveryPlan.Action} relation={result.Inspection.StoredStateRelation}");
        PrintJournalEntry(result.JournalEntry);
        PrintRestart(result.Restart);
        Console.WriteLine($"original-state-restored={result.OriginalStateRestored}");

        return result.JournalEntry.IsTerminal ? 0 : 3;
    }

    private static MutationJournal OpenJournal()
    {
        var journal = new MutationJournal(MutationJournal.GetDefaultDatabasePath());
        journal.Initialize();
        return journal;
    }

    private static void RequireKernelCaptureAuthority()
    {
        EnsureWindows();
        if (!IsAdministrator())
        {
            throw new UnauthorizedAccessException(
                "Raw kernel runtime-placement capture requires an elevated interactive owner terminal.");
        }
    }

    private static void RequirePhysicalMutationAuthority(ParsedOptions options)
    {
        EnsureWindows();
        if (!IsAdministrator())
        {
            throw new UnauthorizedAccessException(
                "Physical mutation validation commands require an elevated interactive owner terminal.");
        }

        if (!options.HasFlag(MutationConfirmationFlag))
        {
            throw new InvalidOperationException(
                $"Physical mutation validation requires the explicit {MutationConfirmationFlag} acknowledgement.");
        }
    }

    private static bool IsAdministrator()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);
        return principal.IsInRole(WindowsBuiltInRole.Administrator);
    }

    private static void EnsureWindows()
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("LatencyPilot physical validation is supported only on Windows.");
        }
    }

    private static Guid ParseExperimentId(string value)
    {
        if (!Guid.TryParseExact(value, "D", out var experimentId) || experimentId == Guid.Empty)
        {
            throw new ArgumentException("--experiment must be a non-empty GUID in D format.");
        }

        return experimentId;
    }

    private static ParsedOptions ParseOptions(string[] args, params string[] valueOptions)
    {
        if (args.Length < 2)
        {
            throw new ArgumentException($"Command '{args[0]}' is missing required options.");
        }

        var allowedValueOptions = new HashSet<string>(valueOptions, StringComparer.Ordinal);
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        var flags = new HashSet<string>(StringComparer.Ordinal);

        for (var index = 1; index < args.Length; index++)
        {
            var token = args[index];
            if (string.Equals(token, MutationConfirmationFlag, StringComparison.Ordinal))
            {
                if (!flags.Add(token))
                {
                    throw new ArgumentException($"Option '{token}' was specified more than once.");
                }

                continue;
            }

            if (!allowedValueOptions.Contains(token))
            {
                throw new ArgumentException($"Option '{token}' is not allowed for command '{args[0]}'.");
            }

            if (values.ContainsKey(token))
            {
                throw new ArgumentException($"Option '{token}' was specified more than once.");
            }

            if (index + 1 >= args.Length || args[index + 1].StartsWith("--", StringComparison.Ordinal))
            {
                throw new ArgumentException($"Option '{token}' requires a value.");
            }

            values.Add(token, args[++index]);
        }

        foreach (var requiredOption in allowedValueOptions)
        {
            if (!values.ContainsKey(requiredOption))
            {
                throw new ArgumentException($"Required option '{requiredOption}' is missing.");
            }
        }

        return new ParsedOptions(values, flags);
    }

    private static void PrintMutationStep(GpuInterruptAffinityMutationStepResult result)
    {
        PrintJournalEntry(result.JournalEntry);
        PrintRestart(result.Restart);
        Console.WriteLine($"original-state-restored={result.OriginalStateRestored}");
    }

    private static void PrintJournalEntry(MutationJournalEntry entry)
    {
        Console.WriteLine($"experiment={entry.ExperimentId:D}");
        Console.WriteLine($"state={entry.State}");
        Console.WriteLine($"target={entry.TargetId}");
        Console.WriteLine($"revision={entry.Revision.ToString(CultureInfo.InvariantCulture)}");
        if (!string.IsNullOrWhiteSpace(entry.FailureReason))
        {
            Console.WriteLine($"failure-reason={entry.FailureReason}");
        }
    }

    private static void PrintRestart(GpuDeviceRestartResult? restart)
    {
        if (restart is null)
        {
            Console.WriteLine("restart=not-attempted");
            return;
        }

        Console.WriteLine($"restart-in-place={restart.RestartedInPlace}");
        Console.WriteLine($"system-restart-required={restart.SystemRestartRequired}");
        Console.WriteLine($"device-started={restart.DeviceStarted}");
        Console.WriteLine($"device-has-problem={restart.DeviceHasProblem}");
        var deviceProblemCode = restart.ProblemCode is uint problemCode
            ? problemCode.ToString(CultureInfo.InvariantCulture)
            : "none";
        Console.WriteLine($"device-problem-code={deviceProblemCode}");
        Console.WriteLine($"device-node-status=0x{restart.DeviceNodeStatusFlags:X8}");
        Console.WriteLine($"device-install-flags=0x{restart.DeviceInstallFlags:X8}");
    }

    private static void EnsureExactArgumentCount(string[] args, int count)
    {
        if (args.Length != count)
        {
            throw new ArgumentException($"Command '{args[0]}' does not accept additional arguments.");
        }
    }

    private static void PrintUsage()
    {
        Console.WriteLine("LatencyPilot owner-only Phase 3 physical validation harness");
        Console.WriteLine();
        Console.WriteLine("Read-only:");
        Console.WriteLine("  inspect");
        Console.WriteLine("  list-gpus");
        Console.WriteLine("  plan-gpu-affinity --evidence <valid-realworld-baseline.json>");
        Console.WriteLine();
        Console.WriteLine("Elevated read-only kernel capture:");
        Console.WriteLine("  verify-gpu-placement --experiment <guid>");
        Console.WriteLine();
        Console.WriteLine("State-changing / physical validation only (elevated owner terminal + explicit acknowledgement):");
        Console.WriteLine("  prepare-gpu-affinity --device <instance-id> --processor <group-0-cpu> --confirm-physical-mutation");
        Console.WriteLine("  apply --experiment <guid> --confirm-physical-mutation");
        Console.WriteLine("  rollback --experiment <guid> --confirm-physical-mutation");
        Console.WriteLine("  recover --experiment <guid> --confirm-physical-mutation");
    }

    private sealed record ParsedOptions(
        IReadOnlyDictionary<string, string> Values,
        IReadOnlySet<string> Flags)
    {
        internal string GetRequiredValue(string name) =>
            Values.TryGetValue(name, out var value)
                ? value
                : throw new ArgumentException($"Required option '{name}' is missing.");

        internal bool HasFlag(string name) => Flags.Contains(name);
    }
}
