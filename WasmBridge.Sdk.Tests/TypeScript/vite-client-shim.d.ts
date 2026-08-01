// Minimal ambient declaration matching the "vite/client" types the generated bridge loader
// file expects for `import.meta.env.DEV` (see GenerateBridgeTypeScriptTask's LoaderTemplate).
// Kept here instead of pulling in the real "vite" package as a devDependency just for this.
interface ImportMetaEnv {
  readonly DEV: boolean
}

interface ImportMeta {
  readonly env: ImportMetaEnv
}
