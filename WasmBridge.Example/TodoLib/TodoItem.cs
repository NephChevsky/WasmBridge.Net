using WasmBridge.Attributes;

namespace TodoLib;

[WasmBridgeTsInterface]
public class TodoItem : Entity
{
	public required string Text { get; set; }
	public TodoPriority Priority { get; set; }
	public bool Completed { get; set; }
	public string? Notes { get; set; }
}
