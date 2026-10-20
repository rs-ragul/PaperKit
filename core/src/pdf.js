/**
 * Locate and extract PDF digital signature dictionaries.
 *
 * A PDF signature is a signature field whose /V value is a CMS blob. The
 * /ByteRange array tells us which bytes of the file the signature covers:
 *
 *   /ByteRange [ 0  A  B  C ]   ->  bytes [0, A) and [B, C) are signed
 *
 * The gap between A and B is where the hex string holding the CMS blob
 * itself lives (it cannot be covered by its own signature). If C < file
 * length there are further incremental updates after the signature, which
 * means the document was modified post-signing.
 */
import { Buffer } from 'buffer';
import { readTlv } from './der.js';

const BR = Buffer.from('/ByteRange');
const CONTENTS = Buffer.from('/Contents');
const SIG = Buffer.from('/Sig');

/**
 * @param {Buffer} buf raw PDF bytes
 * @returns {Array<{index:number, byteRange:number[], contents:Buffer, coversWholeFile:boolean, hasPostSignatureData:boolean}>}
 */
export function findSignatures(input) {
  const buf = Buffer.isBuffer(input) ? input : Buffer.from(input);
  // Step 1: every /ByteRange array in the file.
  const byteRanges = [];
  let cursor = 0;
  while (cursor < buf.length) {
    const i = buf.indexOf(BR, cursor);
    if (i === -1) break;
    cursor = i + BR.length;

    // Guard against /ByteRangeX or a mention inside a stream.
    const open = buf.indexOf(Buffer.from('['), i);
    if (open === -1 || open - i > 24) continue;
    const close = buf.indexOf(Buffer.from(']'), open);
    if (close === -1) continue;

    const nums = buf
      .subarray(open + 1, close)
      .toString('latin1')
      .split(/[\s\r\n]+/)
      .filter(Boolean)
      .map(Number);
    if (nums.length !== 4 || nums.some((n) => !Number.isFinite(n) || n < 0)) continue;
    byteRanges.push({ pos: i, nums });
  }
  if (!byteRanges.length) return [];

  // Step 2: every /Contents hex blob in the file.
  const contents = [];
  cursor = 0;
  while (cursor < buf.length) {
    const i = buf.indexOf(CONTENTS, cursor);
    if (i === -1) break;
    cursor = i + CONTENTS.length;

    const lt = buf.indexOf(Buffer.from('<'), i);
    if (lt === -1 || lt - i > 24) continue;
    const gt = buf.indexOf(Buffer.from('>'), lt + 1);
    if (gt === -1) continue;

    const hex = buf.subarray(lt + 1, gt).toString('latin1').replace(/[^0-9a-fA-F]/g, '');
    if (hex.length < 64) continue;
    contents.push({ pos: i, hexStart: lt, hexEnd: gt, der: Buffer.from(hex, 'hex') });
  }

  // Step 3: pair them up. The order inside the signature dictionary is not
  // fixed - some producers emit /Contents before /ByteRange, others after.
  const out = [];
  for (const br of byteRanges) {
    let best = null;
    for (const c of contents) {
      const lo = Math.min(br.pos, c.hexStart);
      const hi = Math.max(br.pos, c.hexStart);
      const between = buf.subarray(lo, hi);
      // Must live in the same indirect object.
      if (between.includes(Buffer.from('endobj'))) continue;
      if (between.includes(Buffer.from('stream'))) continue;
      const dist = Math.abs(br.pos - c.hexStart);
      if (!best || dist < best.dist) best = { c, dist };
    }
    if (!best) continue;

    const nums = br.nums;
    const interp = interpretByteRange(nums, best.c.hexStart, buf.length);
    out.push({
      index: out.length,
      byteRange: nums,
      segments: interp.segments,
      interpretation: interp.kind,
      contents: best.c.der,
      contentsOffset: best.c.hexStart,
      coversWholeFile: interp.segments[0][0] === 0 && interp.segments[1][1] === buf.length,
      hasPostSignatureData: interp.segments[1][1] < buf.length,
      fileLength: buf.length,
    });
  }
  return out;
}

/**
 * /ByteRange holds TWO PAIRS of (offset, length) - not (start, end) pairs.
 * ISO 32000-1 12.8.1. A few producers get this wrong, so we pick the
 * interpretation that is self-consistent: the gap between the two signed
 * segments must be exactly where the /Contents hex blob lives.
 */
export function interpretByteRange(nums, contentsOffset, fileLength) {
  const [a, b, c, d] = nums;

  // Spec-compliant: (offset, length) pairs.
  const spec = {
    kind: 'offset-length',
    segments: [[a, a + b], [c, c + d]],
  };
  const specGap = [spec.segments[0][1], spec.segments[1][0]];

  // Naive: (start, end) pairs.
  const naive = {
    kind: 'start-end',
    segments: [[a, b], [c, d]],
  };
  const naiveGap = [naive.segments[0][1], naive.segments[1][0]];

  const inGap = (gap) =>
    contentsOffset >= gap[0] && contentsOffset < gap[1] && gap[1] > gap[0];

  if (inGap(specGap) && spec.segments[1][1] <= fileLength) return spec;
  if (inGap(naiveGap) && naive.segments[1][1] <= fileLength) return naive;

  // Neither is self-consistent - fall back to the spec reading.
  return spec;
}

/** Is this PDF encrypted? Encrypted PDFs hide the signature structure. */
export function isEncrypted(buf) {
  return buf.includes(Buffer.from('/Encrypt')) && buf.includes(Buffer.from('/Filter'));
}

/** Heuristic: does the file carry a /DSS (Document Security Store)? */
export function hasDocumentSecurityStore(buf) {
  return buf.includes(Buffer.from('/DSS')) || buf.includes(Buffer.from('/DocSecurityStore'));
}

/** Extract a small metadata summary for UI display. */
export function pdfMeta(input) {
  const buf = Buffer.isBuffer(input) ? input : Buffer.from(input);
  const version = buf.subarray(0, 9).toString('latin1');
  const m = /%PDF-(\d\.\d)/.exec(version);
  return {
    pdfVersion: m ? m[1] : '?',
    encrypted: isEncrypted(buf),
    hasDSS: hasDocumentSecurityStore(buf),
    sizeBytes: buf.length,
  };
}

/** Locate the /Annots /SubFilter hint for the signature type (adbe.pkcs7 vs ETSI.CAdES.detached). */
export function signatureSubFilter(buf) {
  const idx = buf.indexOf(Buffer.from('/SubFilter'));
  if (idx === -1) return null;
  const m = /\/SubFilter\s*\/([A-Za-z0-9.]+)/.exec(
    buf.subarray(idx, idx + 80).toString('latin1'),
  );
  return m ? m[1] : null;
}

export { readTlv };
