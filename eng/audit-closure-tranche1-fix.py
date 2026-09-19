from pathlib import Path

session_path = Path("src/LatencyPilot.Benchmarking/Optimization/GpuAutoAffinitySession.cs")
session = session_path.read_text(encoding="utf-8")
replacements = {
    "private static IReadOnlyList<CandidateEvaluation> SelectNearBestHigher(":
        "private static CandidateEvaluation[] SelectNearBestHigher(",
    "private static IReadOnlyList<CandidateEvaluation> SelectNearBestLower(":
        "private static CandidateEvaluation[] SelectNearBestLower(",
}
for old, new in replacements.items():
    if session.count(old) != 1:
        raise RuntimeError(f"Expected one generated signature for {old!r}")
    session = session.replace(old, new, 1)
session_path.write_text(session, encoding="utf-8", newline="\n")

backend_path = Path("tools/LatencyPilot.GateAValidation/GpuAutoAffinityGateABackend.cs")
backend = backend_path.read_text(encoding="utf-8")
old = "runtimePlacement.RequestedProcessorNumber != candidate.Processor.Number"
new = "runtimePlacement.TargetProcessorNumber != candidate.Processor.Number"
if backend.count(old) != 1:
    raise RuntimeError("Expected one generated placement target field reference.")
backend_path.write_text(backend.replace(old, new, 1), encoding="utf-8", newline="\n")
