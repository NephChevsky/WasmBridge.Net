using WasmBridge.Attributes;

namespace TodoLib;

[WasmBridge]
public class TodoService
{
	private readonly List<TodoItem> _items = [];
	private int _nextId = 1;

	[WasmBridgeExport]
	public static bool IsValidText(string text)
	{
		return !string.IsNullOrWhiteSpace(text);
	}

	// JSExport only marshals primitives directly, so the enum crosses the bridge as its
	// underlying int value and gets cast back to TodoPriority on this side.
	[WasmBridgeExport]
	public int AddTodo(string text, int priority)
	{
		var item = new TodoItem
		{
			Id = _nextId++,
			Text = text,
			Priority = (TodoPriority)priority,
			Completed = false
		};
		_items.Add(item);
		return item.Id;
	}

	[WasmBridgeExport]
	public bool CompleteTodo(int id)
	{
		TodoItem? item = _items.FirstOrDefault(i => i.Id == id);
		if (item is null)
		{
			return false;
		}

		item.Completed = true;
		return true;
	}

	[WasmBridgeExport]
	public bool RemoveTodo(int id)
	{
		return _items.RemoveAll(i => i.Id == id) > 0;
	}

	// The bridge generator sees TodoItem is [WasmBridgeTsInterface]-rooted and auto-wraps this
	// in JsonSerializer.Serialize using the SDK-generated WasmBridgeJsonContext, exposing it
	// as a JSON string on the JS side - no hand-written serialization needed here.
	[WasmBridgeExport]
	public List<TodoItem> GetTodos()
	{
		return _items;
	}

	[WasmBridgeExport]
	public TodoStats GetStats()
	{
		return new TodoStats
		{
			Total = _items.Count,
			Completed = _items.Count(i => i.Completed),
			CountByPriority = _items
				.GroupBy(i => i.Priority.ToString())
				.ToDictionary(g => g.Key, g => g.Count())
		};
	}
}
