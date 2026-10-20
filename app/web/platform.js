/**
 * Platform abstraction.
 *
 * The same UI runs in three places and each one can do files differently:
 *
 *   Electron (Windows)  - native open/save dialogs, native print-to-PDF
 *   Android (Capacitor) - Storage Access Framework via the WebView file
 *                         input, Filesystem + Share for saving a report
 *   Browser             - <input type=file> + a download link
 *
 * Everything degrades gracefully, and nothing ever touches the network.
 */

/* eslint-disable no-undef */
const native = typeof window !== 'undefined' ? window.veripdfNative : null;

/**
 * Capacitor injects its plugins into the WebView as
 * `window.Capacitor.Plugins.*` at runtime. Using that global means the
 * shared bundle does not have to import any Capacitor package - it stays
 * clean and runs unchanged in a plain browser.
 */
let capacitor = null;
function cap() {
  if (capacitor) return capacitor;
  const C = typeof window !== 'undefined' ? window.Capacitor : null;
  if (C && C.Plugins && C.Plugins.Filesystem) {
    capacitor = { Filesystem: C.Plugins.Filesystem, Share: C.Plugins.Share };
  } else {
    capacitor = false;
  }
  return capacitor;
}

export const isElectron = !!native;
export const isAndroid = /Android/i.test(
  typeof navigator !== 'undefined' ? navigator.userAgent : '',
);

/** Open a PDF. Returns {name, data: Uint8Array} or null if cancelled. */
export async function openFile() {
  if (native) {
    const res = await native.openFile();
    if (!res) return null;
    return { name: res.name, data: new Uint8Array(res.data) };
  }
  return new Promise((resolve) => {
    const input = document.createElement('input');
    input.type = 'file';
    input.accept = 'application/pdf,.pdf';
    input.style.display = 'none';
    document.body.appendChild(input);
    let settled = false;
    const finish = (value) => {
      if (settled) return;
      settled = true;
      input.remove();
      resolve(value);
    };
    input.addEventListener('change', async () => {
      const f = input.files && input.files[0];
      if (!f) return finish(null);
      finish({ name: f.name, data: new Uint8Array(await f.arrayBuffer()) });
    });
    // A cancelled picker fires no 'change' event in some engines; 'cancel'
    // is supported in modern Chromium/WebView.
    input.addEventListener('cancel', () => finish(null));
    window.addEventListener('focus', () => {
      setTimeout(() => {
        if (!input.files || !input.files.length) finish(null);
      }, 800);
    });
    input.click();
  });
}

/** Save text to a user-chosen location. Returns a path/URI or null. */
export async function saveText(name, text, mime = 'application/json') {
  if (native) {
    return native.saveFile(name, Array.from(new TextEncoder().encode(text)), mime);
  }
  const c = cap();
  if (c) {
    try {
      const res = await c.Filesystem.writeFile({
        path: name,
        data: text,
        directory: 'DOCUMENTS',
        encoding: 'utf8',
        recursive: true,
      });
      try {
        if (c.Share) {
          await c.Share.share({ title: name, text: name, url: res.uri, dialogTitle: 'Share report' });
        }
      } catch {
        /* user dismissed the share sheet - the file is still saved */
      }
      return res.uri;
    } catch {
      /* fall through to the browser download */
    }
  }
  downloadBlob(name, new Blob([text], { type: mime }));
  return name;
}

/** Save an HTML document as a PDF. */
export async function savePdf(name, html) {
  if (native) {
    return native.printPdf(html, name);
  }
  const c = cap();
  if (c) {
    try {
      const res = await c.Filesystem.writeFile({
        path: name,
        data: html,
        directory: 'DOCUMENTS',
        encoding: 'utf8',
        recursive: true,
      });
      if (c.Share) {
        try {
          await c.Share.share({ title: name, text: name, url: res.uri, dialogTitle: 'Share report' });
        } catch {
          /* dismissed */
        }
      }
      return res.uri;
    } catch {
      /* fall through */
    }
  }
  // Browser: open a print window the user can "Save as PDF".
  const w = window.open('', '_blank');
  if (w) {
    w.document.write(html);
    w.document.close();
    w.focus();
    setTimeout(() => w.print(), 300);
    return null;
  }
  downloadBlob(name.replace(/\.pdf$/, '.html'), new Blob([html], { type: 'text/html' }));
  return null;
}

function downloadBlob(name, blob) {
  const url = URL.createObjectURL(blob);
  const a = document.createElement('a');
  a.href = url;
  a.download = name;
  document.body.appendChild(a);
  a.click();
  a.remove();
  setTimeout(() => URL.revokeObjectURL(url), 4000);
}

export function openExternal(url) {
  if (native) return native.openExternal(url);
  window.open(url, '_blank', 'noopener');
}
