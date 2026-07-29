using System.Text.Json;
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

	[WasmBridgeExport]
	public int AddTodo(string text, TodoPriority priority)
	{
		var item = new TodoItem
		{
			Id = _nextId++,
			Text = text,
			Priority = priority,
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

	[WasmBridgeExport]
	public string GetTodos()
	{
		return JsonSerializer.Serialize(_items, TodoJsonContext.Default.ListTodoItem);
	}

	[WasmBridgeExport]
	public string GetStats()
	{
		var stats = new TodoStats
		{
			Total = _items.Count,
			Completed = _items.Count(i => i.Completed),
			CountByPriority = _items
				.GroupBy(i => i.Priority.ToString())
				.ToDictionary(g => g.Key, g => g.Count())
		};
		return JsonSerializer.Serialize(stats, TodoJsonContext.Default.TodoStats);
	}
}
