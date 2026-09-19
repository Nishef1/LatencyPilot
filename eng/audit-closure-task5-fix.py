from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]

store_path = ROOT / "src/LatencyPilot.Platform.Windows/Devices/DeviceInterruptConfigurationStore.cs"
store = store_path.read_text(encoding="utf-8")
anchor = "using System.Buffers.Binary;\nusing LatencyPilot.Platform.Windows.Interop;\n"
replacement = "using System.Buffers.Binary;\nusing LatencyPilot.Core.Devices;\nusing LatencyPilot.Platform.Windows.Interop;\n"
if store.count(anchor) != 1:
    raise RuntimeError(f"expected one import anchor, found {store.count(anchor)}")
store_path.write_text(store.replace(anchor, replacement, 1), encoding="utf-8", newline="\n")

transaction_path = ROOT / "src/LatencyPilot.Service/DeviceInterruptMutationTransaction.cs"
transaction = transaction_path.read_text(encoding="utf-8")
lookup = "        var entry = journal.GetRequired(id);\n"
lookup_replacement = "        var entry = journal.TryGet(id)\n            ?? throw new InvalidOperationException($\"Mutation journal entry {id:D} was not found.\");\n"
if transaction.count(lookup) != 1:
    raise RuntimeError(f"expected one journal lookup anchor, found {transaction.count(lookup)}")
transaction_path.write_text(transaction.replace(lookup, lookup_replacement, 1), encoding="utf-8", newline="\n")
