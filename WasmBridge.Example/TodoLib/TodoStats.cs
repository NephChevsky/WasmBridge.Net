using WasmBridge.Attributes;

namespace TodoLib;

[WasmBridgeTsInterface]
public class TodoStats
{
	public int Total { get; set; }
	public int Completed { get; set; }
	public required IReadOnlyDictionary<string, int> CountByPriority { get; set; }
}
