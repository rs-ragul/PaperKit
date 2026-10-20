/**
 * VeriPDF web shell.
 *
 * The whole verification happens in this browser context - the file is read
 * with the File API and never leaves the device. The same file is what the
 * Windows shell and the Android WebView load.
 */
import { verifyPdf, fingerprint } from '../../core/src/index.js';
import { buildReport, reportToHtml, fileSha256 } from '../../core/src/report.js';
import { openFile, savePdf, saveText, isElectron } from './platform.js';

const $ = (id) => document.getElementById(id);

const els = {
  dropzone: $('dropzone'),
  fileInput: $('fileInput'),
  result: $('result'),
  verdictCard: $('verdictCard'),
  verdictIcon: $('verdictIcon'),
  verdictTitle: $('verdictTitle'),
  verdictSub: $('verdictSub'),
  fileName: $('fileName'),
  fileMeta: $('fileMeta'),
  signerKv: $('signerKv'),
  docKv: $('docKv'),
  issues: $('issues'),
  chain: $('chain'),
  chainNote: $('chainNote'),
  btnReset: $('btnReset'),
  btnExportPdf: $('btnExportPdf'),
  btnExportJson: $('btnExportJson'),
  btnTrust: $('btnTrust'),
  trustCount: $('trustCount'),
  trustList: $('trustList'),
  trustDrawer: $('trustDrawer'),
  drawerScrim: $('drawerScrim'),
  btnCloseDrawer: $('btnCloseDrawer'),
  btnAddRoot: $('btnAddRoot'),
  rootInput: $('rootInput'),
  btnLoadDefaults: $('btnLoadDefaults'),
  toast: $('toast'),
};

const TRUST_KEY = 'veripdf.truststore.v1';
const TRUST_SEEDED_KEY = 'veripdf.truststore.seeded.v1';

let trustStore = loadTrustStore();

/** Everything needed to produce a report for the file just verified. */
let lastVerification = null;

/* ------------------------------------------------------------------ */
/* trust store                                                         */
/* ------------------------------------------------------------------ */

function loadTrustStore() {
  try {
    const raw = localStorage.getItem(TRUST_KEY);
    return raw ? JSON.parse(raw) : [];
  } catch {
    return [];
  }
}

function saveTrustStore() {
  localStorage.setItem(TRUST_KEY, JSON.stringify(trustStore));
  els.trustCount.textContent = String(trustStore.length);
  renderTrustList();
}

function renderTrustList() {
  els.trustList.innerHTML = '';
  if (!trustStore.length) {
    const li = document.createElement('li');
    li.className = 'trust-empty';
    li.textContent = 'No trust anchors yet — documents will verify as “intact but untrusted”.';
    els.trustList.appendChild(li);
    return;
  }
  trustStore.forEach((t, i) => {
    const li = document.createElement('li');
    const left = document.createElement('div');
    const name = document.createElement('div');
    name.className = 'tname';
    name.textContent = t.name || 'Unnamed root';
    const fp = document.createElement('div');
    fp.className = 'tfp';
    fp.textContent = t.fingerprint || '';
    left.append(name, fp);
    if (t.subject) {
      const sub = document.createElement('div');
      sub.className = 'tsub';
      sub.textContent = t.subject;
      left.appendChild(sub);
    }
    const del = document.createElement('button');
    del.className = 'tdel';
    del.type = 'button';
    del.title = 'Remove';
    del.innerHTML =
      '<svg viewBox="0 0 24 24" width="15" height="15" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round"><path d="M18 6 6 18M6 6l12 12"/></svg>';
    del.addEventListener('click', () => {
      trustStore.splice(i, 1);
      saveTrustStore();

/* Seed the bundled CCA India roots on the very first run, so a document is
   trusted out of the box instead of after a manual click. */
(async () => {
  try {
    if (localStorage.getItem(TRUST_SEEDED_KEY)) return;
    const added = await addBundledRoots();
    if (added === null) return;
    localStorage.setItem(TRUST_SEEDED_KEY, '1');
    if (added > 0) toast(`${added} CCA India root certificates loaded`);
  } catch {
    /* offline file:// sandbox - the manual button still works */
  }
})();
      toast('Trust anchor removed');
    });
    li.append(left, del);
    els.trustList.appendChild(li);
  });
}

/** Accept a .cer/.crt/.pem/.der file and add it as a trust anchor. */
async function addRootFromFile(file) {
  const buf = new Uint8Array(await file.arrayBuffer());
  let der = buf;
  const text = new TextDecoder('utf-8', { fatal: false }).decode(buf);
  if (text.includes('-----BEGIN CERTIFICATE-----')) {
    const b64 = text
      .replace(/-----BEGIN CERTIFICATE-----/g, '')
      .replace(/-----END CERTIFICATE-----/g, '')
      .replace(/\s+/g, '');
    der = Uint8Array.from(atob(b64), (c) => c.charCodeAt(0));
  } else {
    // Some .cer files are base64 without PEM headers.
    const stripped = text.replace(/\s+/g, '');
    if (/^[A-Za-z0-9+/=]+$/.test(stripped) && stripped.length > 100) {
      try {
        der = Uint8Array.from(atob(stripped), (c) => c.charCodeAt(0));
      } catch { /* keep raw bytes */ }
    }
  }
  const fp = await fingerprint(der);
  const name = file.name.replace(/\.(cer|crt|pem|der)$/i, '');
  let subject = null;
  try {
    const mod = await import('../../core/src/index.js');
    const parsed = mod.parseCertificate(der);
    subject = parsed.subjectPretty;
  } catch {
    /* the cert will still be pinned by fingerprint */
  }
  trustStore.push({ name, fingerprint: fp, subject, der: Array.from(der) });
  saveTrustStore();

/* Seed the bundled CCA India roots on the very first run, so a document is
   trusted out of the box instead of after a manual click. */
(async () => {
  try {
    if (localStorage.getItem(TRUST_SEEDED_KEY)) return;
    const added = await addBundledRoots();
    if (added === null) return;
    localStorage.setItem(TRUST_SEEDED_KEY, '1');
    if (added > 0) toast(`${added} CCA India root certificates loaded`);
  } catch {
    /* offline file:// sandbox - the manual button still works */
  }
})();
  toast(`Added trust anchor: ${name}`);
}

function trustedRoots() {
  return trustStore.map((t) => ({
    der: Uint8Array.from(t.der),
    name: t.name,
    source: 'user',
  }));
}

/* ------------------------------------------------------------------ */
/* verification                                                        */
/* ------------------------------------------------------------------ */

const ICONS = {
  valid:
    '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.2" stroke-linecap="round" stroke-linejoin="round"><path d="M20 6 9 17l-5-5"/></svg>',
  warn:
    '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.2" stroke-linecap="round" stroke-linejoin="round"><path d="M12 9v4M12 17h.01M10.3 3.9 1.8 18a2 2 0 0 0 1.7 3h17a2 2 0 0 0 1.7-3L13.7 3.9a2 2 0 0 0-3.4 0Z"/></svg>',
  bad:
    '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.2" stroke-linecap="round" stroke-linejoin="round"><path d="M18 6 6 18M6 6l12 12"/></svg>',
  idle:
    '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.2" stroke-linecap="round" stroke-linejoin="round"><circle cx="12" cy="12" r="9"/><path d="M9.1 9a3 3 0 0 1 5.8 1c0 2-3 3-3 3"/></svg>',
};

const VERDICTS = {
  VALID: {
    cls: 'v-valid',
    icon: 'valid',
    title: 'Signature valid',
    sub: 'The document is unchanged since it was signed, and the signer chains to a trusted root certificate.',
  },
  INTACT_UNTRUSTED: {
    cls: 'v-warn',
    icon: 'warn',
    title: 'Intact, but issuer not trusted',
    sub: 'The document has not been altered since signing — but the signing certificate does not chain to a root this app trusts, so it cannot vouch for who signed it.',
  },
  VALID_WITH_CHANGES: {
    cls: 'v-warn',
    icon: 'warn',
    title: 'Signature valid, with later changes',
    sub: 'The signed content is intact and the signer is trusted, but something was added to the file after it was signed (for example a comment or a form value).',
  },
  UNVERIFIED: {
    cls: 'v-warn',
    icon: 'warn',
    title: 'Could not fully verify the chain',
    sub: 'The document is intact and the chain reaches a trusted root, but not every link in the chain could be cryptographically verified here.',
  },
  MODIFIED: {
    cls: 'v-bad',
    icon: 'bad',
    title: 'Document modified after signing',
    sub: 'The content no longer matches the digest recorded at signing time. The file was changed after it was signed.',
  },
  UNSIGNED: {
    cls: 'v-idle',
    icon: 'idle',
    title: 'No digital signature',
    sub: 'This PDF does not carry a certificate-based digital signature. There is nothing to verify.',
  },
  INDETERMINATE: {
    cls: 'v-warn',
    icon: 'warn',
    title: 'Could not determine',
    sub: 'The signature exists but could not be fully evaluated. See the findings below.',
  },
  ERROR: {
    cls: 'v-bad',
    icon: 'bad',
    title: 'Verification error',
    sub: 'Something went wrong while reading this file. See the findings below.',
  },
};

function fmtDate(iso) {
  if (!iso) return '—';
  const d = new Date(iso);
  if (Number.isNaN(d.getTime())) return iso;
  return d.toLocaleString(undefined, {
    year: 'numeric', month: 'short', day: '2-digit',
    hour: '2-digit', minute: '2-digit',
  });
}

function fmtBytes(n) {
  if (!n && n !== 0) return '—';
  if (n < 1024) return `${n} B`;
  if (n < 1024 * 1024) return `${(n / 1024).toFixed(1)} KB`;
  return `${(n / 1024 / 1024).toFixed(2)} MB`;
}

function addKv(dl, label, value, mono = false) {
  const dt = document.createElement('dt');
  dt.textContent = label;
  const dd = document.createElement('dd');
  if (mono) dd.classList.add('mono');
  if (value instanceof Node) dd.appendChild(value);
  else dd.textContent = value == null || value === '' ? '—' : value;
  dl.append(dt, dd);
}

function renderResult(report, file) {
  const v = VERDICTS[report.verdict] || VERDICTS.INDETERMINATE;

  els.verdictIcon.className = `verdict-icon ${v.cls}`;
  els.verdictIcon.innerHTML = ICONS[v.icon];
  els.verdictTitle.textContent = v.title;
  els.verdictSub.textContent = v.sub;
  els.fileName.textContent = file.name;
  els.fileMeta.textContent = `${fmtBytes(file.size)} · PDF ${report.meta.pdfVersion}${
    report.meta.encrypted ? ' · encrypted' : ''
  }`;

  const sig = report.signatures[0] || {};

  // signer
  els.signerKv.innerHTML = '';
  if (sig.signer) {
    addKv(els.signerKv, 'Signed by', sig.signer.subject);
    addKv(els.signerKv, 'Issued by', sig.signer.issuer);
    addKv(els.signerKv, 'Signed at', fmtDate(sig.signedAt));
    addKv(els.signerKv, 'Valid until', fmtDate(sig.signer.notAfter));
    addKv(els.signerKv, 'Serial', sig.signer.serialNumber, true);
    addKv(els.signerKv, 'Fingerprint', sig.signer.sha256Fingerprint, true);
    addKv(els.signerKv, 'Algorithm', sig.signatureAlgorithm);
  } else {
    addKv(els.signerKv, 'Signed by', 'No signature found');
  }

  // document
  els.docKv.innerHTML = '';
  addKv(els.docKv, 'Integrity', {
    VALID: 'Intact — content matches the signing digest',
    INVALID: 'Modified — content does not match the signing digest',
    valid: 'Intact — content matches the signing digest',
    invalid: 'Modified — content does not match the signing digest',
    indeterminate: 'Could not be determined',
    'n/a': 'No signature',
  }[report.integrity] || report.integrity);
  addKv(
    els.docKv,
    'Trust',
    {
      trusted: 'Chains to a trusted root',
      untrusted: 'Root not in trust store',
      incomplete: 'Chain incomplete (missing intermediate)',
      'invalid-chain': 'Chain failed cryptographic verification',
      unknown: 'Unknown',
      'n/a': 'No signature',
    }[report.trust] || report.trust,
  );
  addKv(els.docKv, 'Trust anchor', sig.trustAnchor || '—');
  addKv(els.docKv, 'Timestamp', sig.hasTimestamp ? 'Present' : 'None embedded');
  addKv(els.docKv, 'Signed bytes', sig.coversWholeFile === false ? 'Partial (rest of file added later)' : 'Whole document');

  // issues
  els.issues.innerHTML = '';
  const allIssues = [...(report.issues || []), ...(sig.issues || [])];
  if (!allIssues.length) {
    const li = document.createElement('li');
    li.className = 'sev-info';
    li.innerHTML = '<span class="issue-code">OK</span><span>No issues found.</span>';
    els.issues.appendChild(li);
  } else {
    allIssues.forEach((iss) => {
      const li = document.createElement('li');
      li.className = `sev-${iss.severity || 'info'}`;
      const code = document.createElement('span');
      code.className = 'issue-code';
      code.textContent = iss.code;
      const msg = document.createElement('span');
      msg.textContent = iss.message;
      li.append(code, msg);
      els.issues.appendChild(li);
    });
  }

  // chain
  els.chain.innerHTML = '';
  els.chainNote.textContent = '';
  const chain = sig.chain || [];
  chain.forEach((c, i) => {
    const li = document.createElement('li');
    const node = document.createElement('div');
    node.className = 'node';
    const body = document.createElement('div');
    body.className = 'body';
    const cn = document.createElement('div');
    cn.className = 'cn';
    cn.textContent = c.subject;
    const role = document.createElement('div');
    role.className = 'role';
    role.textContent =
      i === 0 ? 'Signer certificate' :
      c.selfSigned ? 'Root certificate (self-signed)' : 'Intermediate CA';
    const fp = document.createElement('div');
    fp.className = 'fp';
    fp.textContent = c.sha256Fingerprint;
    body.append(cn, role, fp);
    li.append(node, body);
    els.chain.appendChild(li);
  });
  if (sig.rootFingerprint) {
    els.chainNote.textContent = `Root SHA-256 ${sig.rootFingerprint}`;
  }

  els.dropzone.classList.add('hidden');
  els.result.classList.remove('hidden');
  els.result.scrollIntoView({ behavior: 'smooth', block: 'start' });
}

async function handleFile(file) {
  if (!file) return;
  if (!/\.pdf$/i.test(file.name) && file.type !== 'application/pdf') {
    toast('That does not look like a PDF');
    return;
  }
  const buf = new Uint8Array(await file.arrayBuffer());
  const report = await verifyPdf(buf, { trustedRoots: trustedRoots() });
  lastVerification = { report, bytes: buf, name: file.name };
  renderResult(report, file);
}

/* ------------------------------------------------------------------ */
/* wiring                                                              */
/* ------------------------------------------------------------------ */

els.dropzone.addEventListener('click', async () => {
  if (isElectron) {
    const picked = await openFile();
    if (picked) await handleFile({ name: picked.name, size: picked.data.length, arrayBuffer: async () => picked.data.buffer });
    return;
  }
  els.fileInput.click();
});
els.dropzone.addEventListener('keydown', (e) => {
  if (e.key === 'Enter' || e.key === ' ') {
    e.preventDefault();
    els.fileInput.click();
  }
});
els.fileInput.addEventListener('change', (e) => handleFile(e.target.files[0]));

['dragenter', 'dragover'].forEach((ev) =>
  els.dropzone.addEventListener(ev, (e) => {
    e.preventDefault();
    els.dropzone.classList.add('dragging');
  }),
);
['dragleave', 'drop'].forEach((ev) =>
  els.dropzone.addEventListener(ev, (e) => {
    e.preventDefault();
    els.dropzone.classList.remove('dragging');
  }),
);
els.dropzone.addEventListener('drop', (e) => handleFile(e.dataTransfer.files[0]));

function reportBaseName() {
  const n = (lastVerification && lastVerification.name) || 'document';
  return n.replace(/\.[a-z0-9]+$/i, '') + '-verification-report';
}

els.btnExportPdf.addEventListener('click', async () => {
  if (!lastVerification) return;
  const { report, bytes, name } = lastVerification;
  const sha = await fileSha256(bytes);
  const full = buildReport(report, {
    fileName: name,
    fileSize: bytes.length,
    fileSha256: sha,
    trustAnchors: trustStore.map((t) => t.name),
  });
  const html = reportToHtml(full);
  const saved = await savePdf(reportBaseName() + '.pdf', html);
  toast(saved ? 'Report saved' : 'Report opened in a print window');
});

els.btnExportJson.addEventListener('click', async () => {
  if (!lastVerification) return;
  const { report, bytes, name } = lastVerification;
  const sha = await fileSha256(bytes);
  const full = buildReport(report, {
    fileName: name,
    fileSize: bytes.length,
    fileSha256: sha,
    trustAnchors: trustStore.map((t) => t.name),
  });
  const saved = await saveText(reportBaseName() + '.json', JSON.stringify(full, null, 2));
  toast(saved ? 'Report saved' : 'Report downloaded');
});

els.btnReset.addEventListener('click', () => {
  els.result.classList.add('hidden');
  els.dropzone.classList.remove('hidden');
  els.fileInput.value = '';
});

els.btnTrust.addEventListener('click', () => {
  els.trustDrawer.hidden = false;
});
els.btnCloseDrawer.addEventListener('click', () => {
  els.trustDrawer.hidden = true;
});
els.drawerScrim.addEventListener('click', () => {
  els.trustDrawer.hidden = true;
});
els.btnAddRoot.addEventListener('click', () => els.rootInput.click());
els.rootInput.addEventListener('change', async (e) => {
  if (e.target.files[0]) await addRootFromFile(e.target.files[0]);
  e.target.value = '';
});
/** Add every bundled CCA India root that is not in the store yet. */
async function addBundledRoots() {
  let roots;
  try {
    const res = await fetch('bundled-roots.json');
    if (!res.ok) throw new Error('not ok');
    roots = await res.json();
  } catch {
    return null;
  }
  let added = 0;
  for (const r of roots) {
    const der = Uint8Array.from(r.der);
    const fp = await fingerprint(der);
    if (!trustStore.some((t) => t.fingerprint === fp)) {
      trustStore.push({
        name: r.name,
        fingerprint: fp,
        subject: r.subject,
        notAfter: r.notAfter,
        der: Array.from(der),
      });
      added += 1;
    }
  }
  saveTrustStore();

/* Seed the bundled CCA India roots on the very first run, so a document is
   trusted out of the box instead of after a manual click. */
(async () => {
  try {
    if (localStorage.getItem(TRUST_SEEDED_KEY)) return;
    const added = await addBundledRoots();
    if (added === null) return;
    localStorage.setItem(TRUST_SEEDED_KEY, '1');
    if (added > 0) toast(`${added} CCA India root certificates loaded`);
  } catch {
    /* offline file:// sandbox - the manual button still works */
  }
})();
  return added;
}

els.btnLoadDefaults.addEventListener('click', async () => {
  const added = await addBundledRoots();
  if (added === null) return toast('No bundled roots available');
  toast(`Loaded ${added} bundled root(s)`);
});

let toastTimer;
function toast(msg) {
  els.toast.textContent = msg;
  els.toast.hidden = false;
  clearTimeout(toastTimer);
  toastTimer = setTimeout(() => {
    els.toast.hidden = true;
  }, 2600);
}

saveTrustStore();

/* Seed the bundled CCA India roots on the very first run, so a document is
   trusted out of the box instead of after a manual click. */
(async () => {
  try {
    if (localStorage.getItem(TRUST_SEEDED_KEY)) return;
    const added = await addBundledRoots();
    if (added === null) return;
    localStorage.setItem(TRUST_SEEDED_KEY, '1');
    if (added > 0) toast(`${added} CCA India root certificates loaded`);
  } catch {
    /* offline file:// sandbox - the manual button still works */
  }
})();
