/**
 * Preload bridge for the Electron shell.
 *
 * Exposes a tiny, explicit API - the renderer gets no Node access and no
 * arbitrary IPC channel.
 */
const { contextBridge, ipcRenderer } = require('electron');

contextBridge.exposeInMainWorld('veripdfNative', {
  available: true,
  platform: process.platform,

  /** Open a PDF with the native dialog. Returns {name, path, data} or null. */
  openFile: () => ipcRenderer.invoke('veripdf:open-file'),

  /** Save bytes to a user-chosen path. */
  saveFile: (name, data, mime) =>
    ipcRenderer.invoke('veripdf:save-file', { name, data, mime }),

  /** Render an HTML string to a PDF via the native print pipeline. */
  printPdf: (html, name) => ipcRenderer.invoke('veripdf:print-pdf', { html, name }),

  /** Open a URL in the user's real browser. */
  openExternal: (url) => ipcRenderer.invoke('veripdf:open-external', url),
});
