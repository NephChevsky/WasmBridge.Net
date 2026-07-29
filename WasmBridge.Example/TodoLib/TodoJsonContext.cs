using System.Text.Json.Serialization;

namespace TodoLib;

[JsonSerializable(typeof(TodoItem))]
[JsonSerializable(typeof(List<TodoItem>))]
[JsonSerializable(typeof(TodoStats))]
internal partial class TodoJsonContext : JsonSerializerContext
{
}
