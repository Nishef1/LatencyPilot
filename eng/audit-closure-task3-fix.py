from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
path = ROOT / "tools/LatencyPilot.GateAValidation/GpuAutoAffinityGateABackend.cs"
text = path.read_text(encoding="utf-8")

old_filter = '''        catch (Exception exception) when (\n            exception is FileNotFoundException or\n            DirectoryNotFoundException or\n            System.Net.Http.HttpRequestException or\n            InvalidOperationException or\n            System.ComponentModel.Win32Exception ||\n            exception is IOException and not InvalidDataException)\n'''
new_filter = '''        catch (Exception exception) when (\n            exception is FileNotFoundException or\n            DirectoryNotFoundException or\n            System.Net.Http.HttpRequestException or\n            InvalidOperationException or\n            System.ComponentModel.Win32Exception or\n            IOException)\n'''
if text.count(old_filter) != 1:
    raise RuntimeError("Expected one generated optional PresentMon catch filter.")
text = text.replace(old_filter, new_filter, 1)

old_snapshot = '''            ApiVersion: null,\n            SwapChains: [],\n            UnavailableMetrics: [],\n            ApiPath: null,\n'''
new_snapshot = '''            ApiVersion: null,\n            Frames: [],\n            UnavailableOptionalMetrics: [],\n            ApiPath: null,\n'''
if text.count(old_snapshot) != 1:
    raise RuntimeError("Expected one generated unavailable PresentMon snapshot.")
text = text.replace(old_snapshot, new_snapshot, 1)

path.write_text(text, encoding="utf-8", newline="\n")
