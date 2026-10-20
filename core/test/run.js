/**
 * End-to-end tests for the offline signature verifier.
 * Run: node test/run.js
 */
import { readFileSync, existsSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import path from 'node:path';
import { verifyPdf, fingerprint } from '../src/index.js';

const here = path.dirname(fileURLToPath(import.meta.url));
const FIX = path.resolve(here, '../../fixtures');

const trustedRoots = [
  {
    der: readFileSync(path.join(FIX, 'trusted-root.der')),
    name: 'VeriPDF Test Root CA',
    source: 'test fixture',
  },
];

let pass = 0;
let fail = 0;

function check(label, actual, expected) {
  const ok = JSON.stringify(actual) === JSON.stringify(expected);
  if (ok) {
    pass++;
    console.log(`  \x1b[32mPASS\x1b[0m  ${label}`);
  } else {
    fail++;
    console.log(`  \x1b[31mFAIL\x1b[0m  ${label}  (expected ${expected}, got ${actual})`);
  }
}

async function run(file, opts = {}) {
  const buf = readFileSync(path.join(FIX, file));
  return verifyPdf(buf, { trustedRoots, ...opts });
}

console.log('\n\x1b[1mVeriPDF signature verification core - test suite\x1b[0m\n');

/* 1. A correctly signed document, root pinned */
{
  console.log('valid.pdf  (root pinned in trust store)');
  const r = await run('valid.pdf');
  check('verdict', r.verdict, 'VALID');
  check('integrity', r.integrity, 'valid');
  check('trust', r.trust, 'trusted');
  check('one signature found', r.signatures.length, 1);
  const s = r.signatures[0];
  check('signer subject', s.signer.subject.includes('Zonal Deputy Tahsildar'), true);
  check('chain length (leaf+int+root)', s.chain.length, 3);
  check('root is self-signed', s.chain[2].selfSigned, true);
  check('trust anchor name', s.trustAnchor, 'VeriPDF Test Root CA');
  check('signedAt present', !!s.signedAt, true);
  // These two are the important ones: they prove the CRYPTOGRAPHIC checks
  // actually ran, rather than the result being inferred from the digest
  // alone. A silent WebCrypto failure must never look like success.
  check('signature cryptographically verified', s.signatureVerified, true);
  check('every chain link cryptographically verified', s.chainVerified, true);
  check('no error-level issues', s.issues.filter((i) => i.severity === 'error').length, 0);
  check('no SIGNATURE_CHECK_FAILED', s.issues.some((i) => i.code === 'SIGNATURE_CHECK_FAILED'), false);
  console.log('');
}

/* 2. Same structure, but the root is NOT the one we trust */
{
  console.log('foreign.pdf  (intact, but root not pinned)');
  const r = await run('foreign.pdf');
  check('verdict', r.verdict, 'INTACT_UNTRUSTED');
  check('integrity still valid', r.integrity, 'valid');
  check('trust', r.trust, 'untrusted');
  check('ROOT_NOT_TRUSTED raised', r.signatures[0].issues.some((i) => i.code === 'ROOT_NOT_TRUSTED'), true);
  console.log('');
}

/* 3. A single byte changed after signing */
{
  console.log('tampered_bytes.pdf  (one byte inserted after signing)');
  const r = await run('tampered_bytes.pdf');
  check('verdict', r.verdict, 'MODIFIED');
  check('integrity', r.integrity, 'invalid');
  check('DIGEST_MISMATCH raised', r.signatures[0].issues.some((i) => i.code === 'DIGEST_MISMATCH'), true);
  console.log('');
}

/* 4. Re-saved by a different tool (full rewrite) */
{
  console.log('tampered_resave.pdf  (re-written by another tool)');
  const r = await run('tampered_resave.pdf');
  const okVerdict = ['MODIFIED', 'UNSIGNED', 'INDETERMINATE'].includes(r.verdict);
  check('re-save is NOT reported as VALID', okVerdict, true);
  console.log(`        (verdict: ${r.verdict})`);
  console.log('');
}

/* 5. Genuine incremental update appended after the signature */
{
  console.log('appended.pdf  (incremental update after signing)');
  const r = await run('appended.pdf');
  check('verdict', r.verdict, 'VALID_WITH_CHANGES');
  check('signed content still intact', r.integrity, 'valid');
  check('post-signature data detected', r.signatures[0].hasPostSignatureData, true);
  check('CHANGES_AFTER_SIGNING raised', r.signatures[0].issues.some((i) => i.code === 'CHANGES_AFTER_SIGNING'), true);
  console.log('');
}

/* 6. A PDF with no signature at all */
{
  console.log('base.pdf  (unsigned)');
  const r = await run('base.pdf');
  check('verdict', r.verdict, 'UNSIGNED');
  check('no signatures', r.signatures.length, 0);
  console.log('');
}

/* 7. Root fingerprint is stable and matches the pinned anchor */
{
  console.log('trust anchor fingerprint');
  const fp = await fingerprint(readFileSync(path.join(FIX, 'trusted-root.der')));
  check('fingerprint format (colon separated hex)', /^(?:[0-9A-F]{2}:){31}[0-9A-F]{2}$/.test(fp), true);
  console.log(`        ${fp}`);
  console.log('');
}

/* 8. Garbage input must not crash the verifier */
{
  console.log('robustness');
  const r = await verifyPdf(Buffer.from('not a pdf at all'), { trustedRoots });
  check('garbage input -> UNSIGNED', r.verdict, 'UNSIGNED');
  const r2 = await verifyPdf(readFileSync(path.join(FIX, 'valid.pdf')).subarray(0, 500), { trustedRoots });
  check('truncated file handled without throwing', typeof r2.verdict, 'string');
  console.log('');
}

console.log(`\n\x1b[1m${pass} passed, ${fail} failed\x1b[0m\n`);
process.exit(fail ? 1 : 0);
