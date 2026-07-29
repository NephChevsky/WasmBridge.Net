export interface TodoBridge {
  IsValidText: (text: string) => boolean
  AddTodo: (text: string, priority: number) => number
  CompleteTodo: (id: number) => boolean
  RemoveTodo: (id: number) => boolean
  GetTodos: () => string
  GetStats: () => string
}

declare global {
  interface Window {
    __todoBridge?: TodoBridge
  }
}

// Module-level singleton so double invocation (e.g. Vite HMR) doesn't inject
// the loader <script> twice and load the wasm runtime twice.
let todoBridgePromise: Promise<TodoBridge> | null = null

export function loadTodoBridge(): Promise<TodoBridge> {
  if (!todoBridgePromise) {
    todoBridgePromise = new Promise<TodoBridge>((resolve) => {
      const onReady = () => {
        window.removeEventListener('todobridge-ready', onReady)
        resolve(window.__todoBridge!)
      }
      window.addEventListener('todobridge-ready', onReady)

      // Vite serves files under `public/` as static assets only, so the
      // published dotnet.js module must be loaded via a real <script type="module">
      // tag (an HTML reference) rather than a JS `import()` call.
      const script = document.createElement('script')
      const debugLevel = import.meta.env.DEV ? 1 : 0
      script.type = 'module'
      script.textContent = `
        import { dotnet } from '/wasm-app/_framework/dotnet.js';
        const { getAssemblyExports, getConfig } = await dotnet
          .withDebugging(${debugLevel})
          .create();
        const config = getConfig();
        const exports = await getAssemblyExports(config.mainAssemblyName);
        window.__todoBridge = exports.TodoServiceBridge;
        window.dispatchEvent(new Event('todobridge-ready'));
      `
      document.head.appendChild(script)
    })
  }
  return todoBridgePromise
}
