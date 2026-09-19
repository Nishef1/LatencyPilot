from pathlib import Path

path = Path("src/LatencyPilot.Benchmarking/Optimization/GpuAutoAffinitySession.cs")
text = path.read_text(encoding="utf-8")
replacements = {
    "private static IReadOnlyList<CandidateEvaluation> SelectNearBestHigher(":
        "private static CandidateEvaluation[] SelectNearBestHigher(",
    "private static IReadOnlyList<CandidateEvaluation> SelectNearBestLower(":
        "private static CandidateEvaluation[] SelectNearBestLower(",
}
for old, new in replacements.items():
    if text.count(old) != 1:
        raise RuntimeError(f"Expected one generated signature for {old!r}")
    text = text.replace(old, new, 1)
path.write_text(text, encoding="utf-8", newline="\n")
