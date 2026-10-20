/**
 * VeriPDF - Windows (Electron) shell.
 *
 * The renderer is the exact same app/dist bundle that the Android app and
 * the browser use. This process only provides native services:
 *   - a real file-open dialog
 *   - "save as" for verification reports
 *   - printing a report to PDF
 *
 * No network access is performed by the app; the renderer never fetches
 * anything outside the bundle.
 */
const { app, BrowserWindow, ipcMain, dialog, shell, nativeTheme, session } = require('electron');
const path = require('node:path');
const fs = require('node:fs');
const { pathToFileURL } = require('node:url');

const isDev = process.env.VERIPDF_DEV === '1';
const DIST = path.resolve(__dirname, '../app/dist');

let win = null;

function createWindow() {
  win = new BrowserWindow({
    width: 1180,
    height: 860,
    minWidth: 420,
    minHeight: 560,
    backgroundColor: '#0b0e1a',
    show: false,
    autoHideMenuBar: true,
    webPreferences: {
      preload: path.join(__dirname, 'preload.js'),
      contextIsolation: true,
      nodeIntegration: false,
      sandbox: false,
      spellcheck: false,
    },
  });

  win.once('ready-to-show', () => win.show());

  // External links open in the real browser, never inside the app.
  win.webContents.setWindowOpenHandler(({ url }) => {
    if (/^https?:/i.test(url)) shell.openExternal(url);
    return { action: 'deny' };
  });

  if (isDev) {
    win.loadURL('http://127.0.0.1:7878/');
  } else {
    win.loadURL(pathToFileURL(path.join(DIST, 'index.html')).href);
  }
}

/* ------------------------------------------------------------------ */
/* native services                                                     */
/* ------------------------------------------------------------------ */

ipcMain.handle('veripdf:open-file', async () => {
  const res = await dialog.showOpenDialog(win, {
    title: 'Open a document to verify',
    properties: ['openFile'],
    filters: [{ name: 'PDF documents', extensions: ['pdf'] }],
  });
  if (res.canceled || !res.filePaths.length) return null;
  const filePath = res.filePaths[0];
  const buf = await fs.promises.readFile(filePath);
  return {
    name: path.basename(filePath),
    path: filePath,
    data: buf.buffer.slice(buf.byteOffset, buf.byteOffset + buf.byteLength),
  };
});

ipcMain.handle('veripdf:save-file', async (_e, { name, data, mime }) => {
  const res = await dialog.showSaveDialog(win, {
    title: 'Save',
    defaultPath: name,
  });
  if (res.canceled || !res.filePath) return null;
  await fs.promises.writeFile(res.filePath, Buffer.from(data));
  return res.filePath;
});

ipcMain.handle('veripdf:print-pdf', async (_e, { html, name }) => {
  const res = await dialog.showSaveDialog(win, {
    title: 'Save verification report as PDF',
    defaultPath: name,
    filters: [{ name: 'PDF', extensions: ['pdf'] }],
  });
  if (res.canceled || !res.filePath) return null;

  const hidden = new BrowserWindow({ show: false });
  await hidden.loadURL(
    `data:text/html;charset=utf-8,${encodeURIComponent(html)}`,
  );
  const pdf = await hidden.webContents.printToPDF({
    printBackground: true,
    pageSize: 'A4',
    margins: { marginType: 'custom', top: 0.4, bottom: 0.4, left: 0.4, right: 0.4 },
  });
  await fs.promises.writeFile(res.filePath, pdf);
  hidden.destroy();
  return res.filePath;
});

ipcMain.handle('veripdf:open-external', async (_e, url) => {
  if (/^https?:/i.test(url)) await shell.openExternal(url);
  return true;
});

/* ------------------------------------------------------------------ */

app.whenReady().then(() => {
  nativeTheme.themeSource = 'dark';

  /* ------------------------------------------------------------------
   * Hard offline guarantee.
   * The renderer may only load local files (the bundled app/dist folder)
   * and inline data: URLs (the printable report). Every http(s) request
   * is cancelled, so no document, fingerprint or report can ever leave
   * this machine - not even by accident.
   * ------------------------------------------------------------------ */
  session.defaultSession.webRequest.onBeforeRequest((details, callback) => {
    const url = details.url || '';
    if (/^(file|data|blob|about|devtools|chrome):/i.test(url)) {
      return callback({ cancel: false });
    }
    return callback({ cancel: true });
  });

  createWindow();

  app.on('activate', () => {
    if (BrowserWindow.getAllWindows().length === 0) createWindow();
  });
});

app.on('window-all-closed', () => {
  if (process.platform !== 'darwin') app.quit();
});
