from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
path = ROOT / "src/LatencyPilot.Platform.Windows/Devices/DeviceInterruptConfigurationStore.cs"
text = path.read_text(encoding="utf-8")
anchor = "using System.Buffers.Binary;\nusing LatencyPilot.Platform.Windows.Interop;\n"
replacement = "using System.Buffers.Binary;\nusing LatencyPilot.Core.Devices;\nusing LatencyPilot.Platform.Windows.Interop;\n"
if text.count(anchor) != 1:
    raise RuntimeError(f"expected one import anchor, found {text.count(anchor)}")
path.write_text(text.replace(anchor, replacement, 1), encoding="utf-8", newline="\n")
