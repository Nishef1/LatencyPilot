from pathlib import Path
import sys

ROOT = Path(__file__).resolve().parents[1]


def fix_tests() -> None:
    path = ROOT / "tests/LatencyPilot.CriticalTests/SourceRevisionIdentityTests.cs"
    text = path.read_text(encoding="utf-8")
    old = '''        var benchmarkControlServerSource = File.ReadAllText(Path.Combine(\n            repositoryRoot,\n            "src",\n            "LatencyPilot.GpuBenchmark",\n            "BenchmarkControlServer.cs"));\n        StringAssert.Contains(benchmarkControlServerSource, "BenchmarkRendererOwner rendererOwner");\n        Assert.IsFalse(\n            benchmarkControlServerSource.Contains("D3D12BenchmarkRenderer renderer", StringComparison.Ordinal),\n            "The async pipe server must not directly own or dispose the renderer/window.");\n'''
    new = '''        var benchmarkOwnerControlServerSource = File.ReadAllText(Path.Combine(\n            repositoryRoot,\n            "src",\n            "LatencyPilot.GpuBenchmark",\n            "BenchmarkControlServer.cs"));\n        StringAssert.Contains(benchmarkOwnerControlServerSource, "BenchmarkRendererOwner rendererOwner");\n        Assert.IsFalse(\n            benchmarkOwnerControlServerSource.Contains("D3D12BenchmarkRenderer renderer", StringComparison.Ordinal),\n            "The async pipe server must not directly own or dispose the renderer/window.");\n'''
    if text.count(old) != 1:
        raise RuntimeError("Expected one F1 owner-server assertion block.")
    path.write_text(text.replace(old, new, 1), encoding="utf-8", newline="\n")


def fix_implementation() -> None:
    path = ROOT / "src/LatencyPilot.GpuBenchmark/BenchmarkRendererOwner.cs"
    text = path.read_text(encoding="utf-8")
    old = "using System.Collections.Concurrent;\n\nnamespace LatencyPilot.GpuBenchmark;\n"
    new = "using System.Collections.Concurrent;\nusing LatencyPilot.Core.Benchmarking;\n\nnamespace LatencyPilot.GpuBenchmark;\n"
    if text.count(old) != 1:
        raise RuntimeError("Expected one BenchmarkRendererOwner using header.")
    path.write_text(text.replace(old, new, 1), encoding="utf-8", newline="\n")


if __name__ == "__main__":
    if len(sys.argv) != 2 or sys.argv[1] not in {"tests", "implementation"}:
        raise SystemExit("usage: audit-closure-f1-fix.py tests|implementation")
    (fix_tests if sys.argv[1] == "tests" else fix_implementation)()
