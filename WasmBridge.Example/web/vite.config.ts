import { defineConfig } from 'vite'

// https://vite.dev/config/
export default defineConfig({
  server: {
    proxy: {
      // Forwards the .NET WASM debugging proxy's websocket handshake to the
      // TodoLib.Wasm dev host (started via `dotnet run`/F5 debugging) so
      // Chrome/Edge can find `/_framework/debug/ws-proxy` from this app's own
      // origin, even though the WASM assets themselves are served as static
      // files copied into public/wasm-app rather than by that host.
      '/_framework/debug': {
        target: 'http://localhost:5159',
        ws: true,
        changeOrigin: true,
      },
    },
  },
})
