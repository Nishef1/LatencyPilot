from pathlib import Path
import sys

ROOT = Path(__file__).resolve().parents[1]


def read(path: str) -> str:
    return (ROOT / path).read_text(encoding="utf-8")


def write(path: str, text: str) -> None:
    target = ROOT / path
    target.parent.mkdir(parents=True, exist_ok=True)
    target.write_text(text, encoding="utf-8", newline="\n")


def replace_once(path: str, old: str, new: str) -> None:
    text = read(path)
    count = text.count(old)
    if count != 1:
        raise RuntimeError(f"{path}: expected one anchor, found {count}: {old[:120]!r}")
    write(path, text.replace(old, new, 1))


def apply_tests() -> None:
    path = "tests/LatencyPilot.CriticalTests/SourceRevisionIdentityTests.cs"
    anchor = '''        Assert.IsFalse(\n            benchmarkOwnerControlServerSource.Contains("D3D12BenchmarkRenderer renderer", StringComparison.Ordinal),\n            "The async pipe server must not directly own or dispose the renderer/window.");\n\n        var benchmarkControlClientSource = File.ReadAllText(Path.Combine('''
    addition = '''        Assert.IsFalse(\n            benchmarkOwnerControlServerSource.Contains("D3D12BenchmarkRenderer renderer", StringComparison.Ordinal),\n            "The async pipe server must not directly own or dispose the renderer/window.");\n\n        var mutationLockPath = Path.Combine(\n            repositoryRoot,\n            "src",\n            "LatencyPilot.Service",\n            "MutationOperationLock.cs");\n        Assert.IsTrue(\n            File.Exists(mutationLockPath),\n            "F3 requires one shared crash-released named mutex for mutation/recovery/restore entry points.");\n        var mutationLockSource = File.ReadAllText(mutationLockPath);\n        StringAssert.Contains(mutationLockSource, "Global\\\\LatencyPilot.MutationOperation.v1");\n        StringAssert.Contains(mutationLockSource, "AbandonedMutexException");\n\n        var gpuMutationSource = File.ReadAllText(Path.Combine(\n            repositoryRoot, "src", "LatencyPilot.Service", "GpuInterruptAffinityMutationTransaction.cs"));\n        Assert.IsTrue(\n            CountOccurrences(gpuMutationSource, "MutationOperationLock.Acquire()") >= 3,\n            "Prepare/apply/rollback must all participate in the shared mutation lock.");\n\n        var recoveryExecutorSource = File.ReadAllText(Path.Combine(\n            repositoryRoot, "src", "LatencyPilot.Service", "MutationRecoveryExecutor.cs"));\n        StringAssert.Contains(recoveryExecutorSource, "MutationOperationLock.Acquire()");\n\n        var restoreSource = File.ReadAllText(Path.Combine(\n            repositoryRoot, "src", "LatencyPilot.Service", "GlobalRestoreBaseline.cs"));\n        StringAssert.Contains(restoreSource, "MutationOperationLock.Acquire()");\n\n        var physicalValidationSource = File.ReadAllText(Path.Combine(\n            repositoryRoot, "tools", "LatencyPilot.PhysicalValidation", "Program.cs"));\n        StringAssert.Contains(physicalValidationSource, "\\\"restore-original-settings\\\" => RestoreOriginalSettings(args)");\n        StringAssert.Contains(physicalValidationSource, "Restore original settings completed");\n\n        var benchmarkControlClientSource = File.ReadAllText(Path.Combine('''
    replace_once(path, anchor, addition)


def apply_implementation() -> None:
    lock_source = '''namespace LatencyPilot.Service;\n\n/// <summary>\n/// Serializes machine mutation, recovery and retained-restore operations across\n/// LatencyPilot processes. A Windows mutex is released by the kernel if its owner\n/// process/thread dies; SQLite CAS remains the second concurrency layer.\n/// </summary>\ninternal sealed class MutationOperationLock : IDisposable\n{\n    private const string MutexName = @"Global\\LatencyPilot.MutationOperation.v1";\n    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(30);\n\n    private readonly Mutex mutex;\n    private bool ownsMutex;\n    private bool disposed;\n\n    private MutationOperationLock(Mutex mutex)\n    {\n        this.mutex = mutex;\n        ownsMutex = true;\n    }\n\n    internal static MutationOperationLock Acquire() => Acquire(DefaultTimeout);\n\n    internal static MutationOperationLock Acquire(TimeSpan timeout)\n    {\n        if (timeout < TimeSpan.Zero || timeout > TimeSpan.FromMinutes(5))\n        {\n            throw new ArgumentOutOfRangeException(nameof(timeout));\n        }\n\n        var mutex = new Mutex(initiallyOwned: false, MutexName);\n        var acquired = false;\n        try\n        {\n            try\n            {\n                acquired = mutex.WaitOne(timeout);\n            }\n            catch (AbandonedMutexException)\n            {\n                // The previous owner died. Windows transfers ownership to this\n                // thread; actual journal + device state is still re-read by the\n                // mutation/recovery caller before any subsequent write.\n                acquired = true;\n            }\n\n            if (!acquired)\n            {\n                throw new TimeoutException(\n                    "Another LatencyPilot mutation/recovery operation owns the machine mutation lock. Retry only after that operation completes or recovery inspects its journal state.");\n            }\n\n            return new MutationOperationLock(mutex);\n        }\n        catch\n        {\n            if (!acquired)\n            {\n                mutex.Dispose();\n            }\n            throw;\n        }\n    }\n\n    public void Dispose()\n    {\n        if (disposed) return;\n        disposed = true;\n        if (ownsMutex)\n        {\n            ownsMutex = false;\n            mutex.ReleaseMutex();\n        }\n        mutex.Dispose();\n    }\n}\n'''
    write("src/LatencyPilot.Service/MutationOperationLock.cs", lock_source)

    transaction = "src/LatencyPilot.Service/GpuInterruptAffinityMutationTransaction.cs"
    replace_once(transaction, '''    {\n        ArgumentException.ThrowIfNullOrWhiteSpace(deviceInstanceId);\n        ArgumentNullException.ThrowIfNull(candidate);\n        ValidateCandidateAgainstCurrentTopology(candidate);\n''', '''    {\n        using var operationLock = MutationOperationLock.Acquire();\n        ArgumentException.ThrowIfNullOrWhiteSpace(deviceInstanceId);\n        ArgumentNullException.ThrowIfNull(candidate);\n        ValidateCandidateAgainstCurrentTopology(candidate);\n''')
    replace_once(transaction, '''    internal GpuInterruptAffinityMutationStepResult ApplyAndActivate(Guid experimentId)\n    {\n        var prepared = GetRequiredGpuEntry(experimentId, MutationJournalState.Prepared);\n''', '''    internal GpuInterruptAffinityMutationStepResult ApplyAndActivate(Guid experimentId)\n    {\n        using var operationLock = MutationOperationLock.Acquire();\n        var prepared = GetRequiredGpuEntry(experimentId, MutationJournalState.Prepared);\n''')
    replace_once(transaction, '''    internal GpuInterruptAffinityMutationStepResult RollbackAndActivate(Guid experimentId)\n    {\n        var entry = GetRequiredGpuEntry(experimentId);\n''', '''    internal GpuInterruptAffinityMutationStepResult RollbackAndActivate(Guid experimentId)\n    {\n        using var operationLock = MutationOperationLock.Acquire();\n        var entry = GetRequiredGpuEntry(experimentId);\n''')

    orchestrator = "src/LatencyPilot.Service/GpuOptimizationOrchestrator.cs"
    replace_once(orchestrator, '''    public void BeginMeasurement(Guid experimentId)\n    {\n        var entry = GetRequiredEntry(experimentId, MutationJournalState.Applied);\n''', '''    public void BeginMeasurement(Guid experimentId)\n    {\n        using var operationLock = MutationOperationLock.Acquire();\n        var entry = GetRequiredEntry(experimentId, MutationJournalState.Applied);\n''')
    replace_once(orchestrator, '''    public void AwaitDecision(Guid experimentId)\n    {\n        var entry = GetRequiredEntry(experimentId, MutationJournalState.Measuring);\n''', '''    public void AwaitDecision(Guid experimentId)\n    {\n        using var operationLock = MutationOperationLock.Acquire();\n        var entry = GetRequiredEntry(experimentId, MutationJournalState.Measuring);\n''')
    replace_once(orchestrator, '''    public void KeepCandidate(Guid experimentId)\n    {\n        var entry = GetRequiredEntry(experimentId, MutationJournalState.AwaitingDecision);\n''', '''    public void KeepCandidate(Guid experimentId)\n    {\n        using var operationLock = MutationOperationLock.Acquire();\n        var entry = GetRequiredEntry(experimentId, MutationJournalState.AwaitingDecision);\n''')

    recovery = "src/LatencyPilot.Service/MutationRecoveryExecutor.cs"
    replace_once(recovery, '''    internal MutationRecoveryExecutionResult Execute(Guid experimentId)\n    {\n        if (experimentId == Guid.Empty)\n''', '''    internal MutationRecoveryExecutionResult Execute(Guid experimentId)\n    {\n        using var operationLock = MutationOperationLock.Acquire();\n        if (experimentId == Guid.Empty)\n''')

    restore = "src/LatencyPilot.Service/GlobalRestoreBaseline.cs"
    replace_once(restore, '''    internal GlobalRestoreBaselineResult Restore()\n    {\n        var unresolved = journal.GetUnresolved();\n''', '''    internal GlobalRestoreBaselineResult Restore()\n    {\n        using var operationLock = MutationOperationLock.Acquire();\n        var unresolved = journal.GetUnresolved();\n''')

    physical = "tools/LatencyPilot.PhysicalValidation/Program.cs"
    replace_once(physical, '''                "recover" => Recover(args),\n                "--help" or "-h" or "help" => Help(args),\n''', '''                "recover" => Recover(args),\n                "restore-original-settings" => RestoreOriginalSettings(args),\n                "--help" or "-h" or "help" => Help(args),\n''')
    replace_once(physical, '''    private static MutationJournal OpenJournal()\n    {\n''', '''    private static int RestoreOriginalSettings(string[] args)\n    {\n        var options = ParseOptions(args);\n        RequirePhysicalMutationAuthority(options);\n\n        var result = new GlobalRestoreBaselineExecutor(OpenJournal()).Restore();\n        Console.WriteLine(\n            $"Restore original settings completed; restored={result.RestoredCount.ToString(CultureInfo.InvariantCulture)}");\n        foreach (var experimentId in result.RestoredExperimentIds)\n        {\n            Console.WriteLine($"restored-experiment={experimentId:D}");\n        }\n        return 0;\n    }\n\n    private static MutationJournal OpenJournal()\n    {\n''')
    replace_once(physical, '''        Console.WriteLine("  recover --experiment <guid> --confirm-physical-mutation");\n''', '''        Console.WriteLine("  recover --experiment <guid> --confirm-physical-mutation");\n        Console.WriteLine("  restore-original-settings --confirm-physical-mutation");\n''')


if __name__ == "__main__":
    if len(sys.argv) != 2 or sys.argv[1] not in {"tests", "implementation"}:
        raise SystemExit("usage: audit-closure-f3-lock-restore.py tests|implementation")
    if sys.argv[1] == "tests":
        apply_tests()
    else:
        apply_implementation()
