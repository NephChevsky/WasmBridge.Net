import './style.css'
import { loadTodoService } from './wasm-interfaces/todoServiceBridge'
import type { TodoItem } from './wasm-interfaces/todoItem'
import { parseTodoStats } from './wasm-interfaces/todoStats'

const PRIORITY_NAMES = ['Low', 'Medium', 'High'] as const

const app = document.querySelector<HTMLDivElement>('#app')!
app.innerHTML = `
  <div class="app">
    <h1>Todo (WasmBridge.Example)</h1>
    <form class="add-form">
      <input type="text" name="text" placeholder="What needs doing?" required />
      <select name="priority">
        <option value="0">Low</option>
        <option value="1" selected>Medium</option>
        <option value="2">High</option>
      </select>
      <button type="submit">Add</button>
    </form>
    <ul class="todo-list"></ul>
    <div class="stats"></div>
  </div>
`

const form = app.querySelector<HTMLFormElement>('.add-form')!
const list = app.querySelector<HTMLUListElement>('.todo-list')!
const statsEl = app.querySelector<HTMLDivElement>('.stats')!

loadTodoService().then((bridge) => {
  function refresh() {
    // GetTodos returns a JSON array, so there's no single-object parseX
    // helper for it (parseX helpers are only generated for [WasmBridgeTsInterface]
    // roots, and the root here is the item, not the list) - parse it directly.
    const todos = JSON.parse(bridge.GetTodos()) as TodoItem[]

    list.innerHTML = ''
    for (const todo of todos) {
      const li = document.createElement('li')
      li.className = todo.Completed ? 'completed' : ''

      const checkbox = document.createElement('input')
      checkbox.type = 'checkbox'
      checkbox.checked = todo.Completed
      checkbox.disabled = todo.Completed
      checkbox.addEventListener('change', () => {
        bridge.CompleteTodo(todo.Id)
        refresh()
      })

      const text = document.createElement('span')
      text.className = `todo-text priority-${PRIORITY_NAMES[todo.Priority]}`
      text.textContent = todo.Text + (todo.Notes ? ` (${todo.Notes})` : '')

      const removeButton = document.createElement('button')
      removeButton.type = 'button'
      removeButton.textContent = 'Remove'
      removeButton.addEventListener('click', () => {
        bridge.RemoveTodo(todo.Id)
        refresh()
      })

      li.append(checkbox, text, removeButton)
      list.appendChild(li)
    }

    const stats = parseTodoStats(bridge.GetStats())
    statsEl.textContent = `${stats.Completed} / ${stats.Total} done`
  }

  form.addEventListener('submit', (event) => {
    event.preventDefault()
    const data = new FormData(form)
    const text = String(data.get('text') ?? '').trim()
    const priority = Number(data.get('priority'))

    if (!bridge.IsValidText(text)) {
      return
    }

    bridge.AddTodo(text, priority)
    form.reset()
    refresh()
  })

  refresh()
})
