/**
 * Verification report.
 *
 * The honest replacement for "make it green": instead of drawing a tick on
 * the page, produce a portable, auditable statement of what was verified -
 * the verdict, the chain, the fingerprints, the hash of the file and the
 * trust anchors that were in force at the time.
 */
import { fingerprint } from './verify.js';

export const REPORT_VERSION = '1.0';

const VERDICT_TEXT = {
  VALID: 'Signature valid',
  VALID_WITH_CHANGES: 'Signature valid, with later changes',
  INTACT_UNTRUSTED: 'Intact, but the issuer is not trusted',
  UNVERIFIED: 'Could not fully verify the certificate chain',
  MODIFIED: 'The document was modified after it was signed',
  UNSIGNED: 'No digital signature present',
  INDETERMINATE: 'Could not be determined',
  ERROR: 'Verification error',
};

/** SHA-256 of the whole file, so the report pins the exact bytes checked. */
export async function fileSha256(bytes) {
  const d = await crypto.subtle.digest('SHA-256', bytes);
  return Array.from(new Uint8Array(d))
    .map((b) => b.toString(16).padStart(2, '0'))
    .join('')
    .toUpperCase();
}

/**
 * Build the machine-readable report.
 * @param {object} report  the verifyPdf() result
 * @param {object} ctx     { fileName, fileSize, fileSha256, trustAnchors }
 */
export function buildReport(report, ctx) {
  const sig = (report.signatures && report.signatures[0]) || {};
  return {
    reportVersion: REPORT_VERSION,
    tool: 'VeriPDF',
    generatedAt: new Date().toISOString(),
    subject: {
      fileName: ctx.fileName,
      fileSizeBytes: ctx.fileSize,
      fileSha256: ctx.fileSha256,
      pdfVersion: report.meta && report.meta.pdfVersion,
      encrypted: !!(report.meta && report.meta.encrypted),
      bytes: ctx.fileSize,
    },
    verdict: {
      code: report.verdict,
      title: VERDICT_TEXT[report.verdict] || report.verdict,
      integrity: report.integrity,
      trust: report.trust,
    },
    signature: sig.signer
      ? {
          subject: sig.signer.subject,
          issuer: sig.signer.issuer,
          serialNumber: sig.signer.serialNumber,
          notBefore: sig.signer.notBefore,
          notAfter: sig.signer.notAfter,
          sha256Fingerprint: sig.signer.sha256Fingerprint,
          signedAt: sig.signedAt,
          digestAlgorithm: sig.digestAlgorithm,
          signatureAlgorithm: sig.signatureAlgorithm,
          subFilter: sig.subFilter,
          cryptographicallyVerified: sig.signatureVerified,
        }
      : null,
    certificateChain: (sig.chain || []).map((c) => ({
      subject: c.subject,
      issuer: c.issuer,
      serialNumber: c.serialNumber,
      notBefore: c.notBefore,
      notAfter: c.notAfter,
      isCA: c.isCA,
      selfSigned: c.selfSigned,
      sha256Fingerprint: c.sha256Fingerprint,
    })),
    trustAnchor: sig.trustAnchor || null,
    rootFingerprint: sig.rootFingerprint || null,
    trustAnchorsInForce: ctx.trustAnchors || [],
    findings: [...(report.issues || []), ...(sig.issues || [])].map((i) => ({
      code: i.code,
      severity: i.severity,
      message: i.message,
    })),
    notice:
      'A valid signature proves that the file has not changed since it was signed ' +
      'and that the signer chains to a trusted root. It does not prove that the ' +
      'data inside is correct, nor that the person presenting the document is the ' +
      'person named in it.',
  };
}

const esc = (s) =>
  String(s == null ? '' : s).replace(
    /[&<>"']/g,
    (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]),
  );

/** A printable HTML report. */
export function reportToHtml(r) {
  const rows = (obj) =>
    Object.entries(obj)
      .filter(([, v]) => v !== undefined && v !== null && v !== '')
      .map(([k, v]) => `<tr><th>${esc(k)}</th><td>${esc(v)}</td></tr>`)
      .join('');

  const chain = r.certificateChain
    .map(
      (c, i) => `<li>
        <strong>${esc(c.subject)}</strong>
        <span class="role">${esc(
          i === 0 ? 'Signer certificate' : c.selfSigned ? 'Root certificate (self-signed)' : 'Intermediate CA',
        )}</span>
        <code>${esc(c.sha256Fingerprint)}</code>
        <table>${rows({
          'Issued by': c.issuer,
          'Serial number': c.serialNumber,
          'Valid from': c.notBefore,
          'Valid until': c.notAfter,
        })}</table>
      </li>`,
    )
    .join('');

  const findings = r.findings.length
    ? `<table class="findings">${r.findings
        .map(
          (f) =>
            `<tr class="sev-${esc(f.severity)}"><td>${esc(f.code)}</td><td>${esc(f.message)}</td></tr>`,
        )
        .join('')}</table>`
    : '<p class="muted">No findings.</p>';

  return `<!DOCTYPE html>
<html lang="en"><head><meta charset="utf-8">
<title>VeriPDF verification report - ${esc(r.subject.fileName)}</title>
<style>
  body{font:14px/1.55 -apple-system,BlinkMacSystemFont,"Segoe UI",Roboto,Arial,sans-serif;color:#15182b;margin:0;padding:32px;background:#f6f7fb}
  .sheet{max-width:820px;margin:0 auto;background:#fff;border:1px solid #e2e5ee;border-radius:14px;padding:32px}
  h1{font-size:22px;margin:0 0 2px}
  h2{font-size:12px;letter-spacing:.09em;text-transform:uppercase;color:#8a90a8;margin:28px 0 10px}
  .sub{color:#5d6480;margin:0 0 24px;font-size:13px}
  .verdict{display:flex;gap:14px;align-items:flex-start;padding:16px;border-radius:12px;margin-bottom:6px}
  .v-valid{background:#e7f6f0;color:#059669}
  .v-warn{background:#fdf3e3;color:#b45309}
  .v-bad{background:#fdeceb;color:#dc2626}
  .v-idle{background:#eef1f6;color:#64748b}
  .verdict h3{margin:0;font-size:17px}
  .verdict p{margin:4px 0 0;font-size:13px;color:#3c4257}
  .badge{display:inline-block;padding:3px 9px;border-radius:999px;font-size:11px;font-weight:700;background:rgba(0,0,0,.08)}
  table{width:100%;border-collapse:collapse;margin:0}
  th,td{text-align:left;padding:6px 10px;border-bottom:1px solid #eceef4;vertical-align:top;font-size:12.5px}
  th{width:34%;color:#5d6480;font-weight:600}
  ol.chain{list-style:none;padding:0;margin:0;counter-reset:c}
  ol.chain li{position:relative;padding:0 0 18px 26px;border-left:2px solid #e2e5ee;margin-left:8px}
  ol.chain li:last-child{border-left-color:transparent;padding-bottom:0}
  ol.chain li:before{content:"";position:absolute;left:-8px;top:2px;width:12px;height:12px;border-radius:50%;background:#4f46e5;border:3px solid #fff}
  ol.chain .role{display:block;font-size:11.5px;color:#8a90a8;margin:2px 0 4px}
  code{font:11px ui-monospace,Menlo,Consolas,monospace;color:#5d6480;word-break:break-all}
  .findings td:first-child{width:22%;font-family:ui-monospace,Menlo,monospace;font-size:11px}
  .sev-error{background:#fdeceb}.sev-warning{background:#fdf3e3}.sev-info{background:#eef0ff}
  .notice{margin-top:26px;padding:14px 16px;background:#f6f7fb;border-radius:10px;font-size:12px;color:#5d6480}
  .muted{color:#8a90a8}
  .foot{margin-top:22px;font-size:11px;color:#8a90a8;border-top:1px solid #eceef4;padding-top:12px}
  @media print{body{background:#fff;padding:0}.sheet{border:0;padding:0}}
</style></head>
<body><div class="sheet">
  <h1>Document verification report</h1>
  <p class="sub">VeriPDF &middot; offline verification &middot; generated ${esc(r.generatedAt)}</p>

  <div class="verdict v-${esc(r.verdict.integrity === 'invalid' ? 'bad' : r.verdict.trust === 'trusted' ? 'valid' : r.verdict.code === 'UNSIGNED' ? 'idle' : 'warn')}">
    <div>
      <h3>${esc(r.verdict.title)}</h3>
      <p><span class="badge">${esc(r.verdict.code)}</span>
         &nbsp;Integrity: <strong>${esc(r.verdict.integrity)}</strong>
         &nbsp;&nbsp;Trust: <strong>${esc(r.verdict.trust)}</strong></p>
    </div>
  </div>

  <h2>Document</h2>
  <table>${rows({
    'File name': r.subject.fileName,
    'Size': `${r.subject.fileSizeBytes} bytes`,
    'PDF version': r.subject.pdfVersion,
    'Encrypted': r.subject.encrypted ? 'yes' : 'no',
    'SHA-256 of file': r.subject.fileSha256,
  })}</table>

  ${
    r.signature
      ? `<h2>Signature</h2><table>${rows({
          'Signed by': r.signature.subject,
          'Issued by': r.signature.issuer,
          'Signed at': r.signature.signedAt,
          'Serial number': r.signature.serialNumber,
          'Valid until': r.signature.notAfter,
          'Digest algorithm': r.signature.digestAlgorithm,
          'Signature algorithm': r.signature.signatureAlgorithm,
          'Sub-filter': r.signature.subFilter,
          'Cryptographically verified': r.signature.cryptographicallyVerified,
          'Certificate fingerprint': r.signature.sha256Fingerprint,
        })}</table>`
      : ''
  }

  <h2>Certificate chain</h2>
  <ol class="chain">${chain}</ol>

  <h2>Findings</h2>
  ${findings}

  <h2>Trust anchors in force</h2>
  <table>${rows({
    'Trust anchor used': r.trustAnchor,
    'Root fingerprint': r.rootFingerprint,
    'Anchors available': r.trustAnchorsInForce.length,
  })}</table>

  <div class="notice">${esc(r.notice)}</div>

  <div class="foot">
    This report was produced entirely on the device that generated it. No part of the
    document was transmitted anywhere. Report version ${esc(r.reportVersion)}.
  </div>
</div></body></html>`;
}
