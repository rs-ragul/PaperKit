/**
 * Trust evaluation for PDF signatures.
 *
 * Three independent questions are answered, and they must NOT be conflated:
 *
 *   1. INTEGRITY  - do the signed bytes still hash to the signed digest?
 *                   (pure cryptography; "modified" or "intact")
 *   2. IDENTITY   - does the signer's certificate chain to a root we trust?
 *                   (policy; "trusted" / "untrusted" / "incomplete")
 *   3. FRESHNESS  - was the cert valid at signing time, and has it been
 *                   revoked since? (time + revocation)
 *
 * A document can be intact and untrusted. That is NOT a failure.
 */
import { Buffer } from 'buffer';
import forge from './forge.js';
import { parseSignedData, OID, digestName } from './cms.js';
import { findSignatures, pdfMeta, signatureSubFilter } from './pdf.js';
import { readTlv, children, rawOf, decodeOid, TAG } from './der.js';

/* ------------------------------------------------------------------ */
/* helpers                                                             */
/* ------------------------------------------------------------------ */

const subtle = globalThis.crypto?.subtle;

async function digest(algo, bytes) {
  const h = await subtle.digest(algo, bytes);
  return new Uint8Array(h);
}

/**
 * Hex-encode bytes. Accepts a Buffer/Uint8Array or a forge-style "binary
 * string" (each char code is one byte) - getting this wrong silently
 * produces a serial number made of ASCII garbage.
 */
export function toHex(value) {
  if (value == null) return '';
  if (Buffer.isBuffer(value)) return value.toString('hex').toUpperCase();
  if (value instanceof Uint8Array) return Buffer.from(value).toString('hex').toUpperCase();
  if (typeof value === 'string') {
    let out = '';
    for (let i = 0; i < value.length; i++) {
      out += value.charCodeAt(i).toString(16).padStart(2, '0');
    }
    return out.toUpperCase();
  }
  return '';
}

/** SHA-256 fingerprint of a DER blob, colon separated (thumbprint style). */
export async function fingerprint(der) {
  const d = await digest('SHA-256', new Uint8Array(der));
  const hex = toHex(d);
  return hex.match(/.{2}/g).join(':');
}

function pemToDer(pem) {
  const body = pem
    .replace(/-----BEGIN CERTIFICATE-----/g, '')
    .replace(/-----END CERTIFICATE-----/g, '')
    .replace(/\s+/g, '');
  return Buffer.from(body, 'base64');
}

function dnToString(dn) {
  if (!dn || !dn.attributes) return '';
  return dn.attributes
    .map((a) => `${a.type}=${a.value}`)
    .sort()
    .join(',');
}

function attrValue(cert, oidShortName) {
  if (!cert || !cert.attributes) return null;
  const hit = cert.attributes.find((a) => a.type === oidShortName);
  return hit ? hit.value : null;
}

const OID_NAMES = {
  '2.5.4.3': 'CN',
  '2.5.4.6': 'C',
  '2.5.4.7': 'L',
  '2.5.4.8': 'ST',
  '2.5.4.10': 'O',
  '2.5.4.11': 'OU',
  '2.5.4.5': 'serialNumber',
  '1.2.840.113549.1.9.1': 'emailAddress',
  '2.5.4.15': 'businessCategory',
};

/** Parse a DER certificate into a normalized descriptor. */
export function parseCertificate(der) {
  const raw = Buffer.from(der);
  const asn1 = forge.asn1.fromDer(raw.toString('binary'));
  const cert = forge.pki.certificateFromAsn1(asn1);
  const pem = forge.pki.certificateToPem(cert);

  const notBefore = cert.validity.notBefore;
  const notAfter = cert.validity.notAfter;

  // Exact TBSCertificate bytes + the certificate's own signature algorithm.
  // We need the ORIGINAL bytes to re-verify the issuer's signature.
  const tbsBytes = extractTbs(raw);
  const spkiDer = extractSpki(raw);
  const signatureAlgorithmOid = extractCertSignatureAlgorithm(raw);

  const ext = {};
  for (const e of cert.extensions || []) {
    if (e.name === 'basicConstraints') ext.basicConstraints = e;
    if (e.name === 'keyUsage') ext.keyUsage = e;
    if (e.name === 'extKeyUsage') ext.extKeyUsage = e;
    if (e.name === 'subjectKeyIdentifier') ext.ski = e.value;
    if (e.name === 'authorityKeyIdentifier') ext.aki = e.value;
    if (e.name === 'crlDistributionPoints') ext.crlDp = e;
    if (e.name === 'authorityInfoAccess') ext.aia = e;
  }

  return {
    der: raw,
    pem,
    spkiDer,
    tbsBytes,
    signature: cert.signature,
    signatureAlgorithmOid,
    subject: dnToString(cert.subject),
    issuer: dnToString(cert.issuer),
    subjectPretty: prettyDn(cert.subject),
    issuerPretty: prettyDn(cert.issuer),
    // node-forge already exposes serialNumber as a hex string.
    serialNumber: String(cert.serialNumber).toUpperCase().replace(/^(00)+/, ''),
    notBefore,
    notAfter,
    publicKey: cert.publicKey,
    isSelfSigned:
      cert.subject.attributes.length === cert.issuer.attributes.length &&
      dnToString(cert.subject) === dnToString(cert.issuer),
    isCA: !!(ext.basicConstraints && ext.basicConstraints.cA),
    keyUsage: ext.keyUsage ? ext.keyUsage : null,
    extKeyUsage: ext.extKeyUsage || null,
    ski: ext.ski ? Buffer.from(ext.ski).toString('hex').toLowerCase() : null,
    aki: ext.aki || null,
    hasCrlDp: !!ext.crlDp,
    hasAia: !!ext.aia,
    sha256Fingerprint: null, // filled lazily
  };
}

/** Raw TBSCertificate bytes from a Certificate SEQUENCE. */
function extractTbs(raw) {
  const outer = readTlv(raw, 0);
  if (!outer) return null;
  const kids = children(raw, outer);
  if (!kids.length) return null;
  return Buffer.from(rawOf(raw, kids[0]));
}

/**
 * SubjectPublicKeyInfo DER from a Certificate.
 *
 * This must be the SPKI, NOT the certificate itself - WebCrypto's
 * importKey('spki') will happily accept a certificate DER and then fail
 * later with a useless DataError.
 */
function extractSpki(raw) {
  const outer = readTlv(raw, 0);
  if (!outer) return null;
  const tbs = children(raw, outer)[0];
  if (!tbs) return null;
  for (const k of children(raw, tbs)) {
    if (k.tag !== TAG.SEQUENCE) continue;
    const sub = children(raw, k);
    if (sub.length === 2 && sub[0].tag === TAG.SEQUENCE && sub[1].tag === TAG.BIT_STRING) {
      return Buffer.from(rawOf(raw, k));
    }
  }
  return null;
}

/** OID of the certificate's own signature AlgorithmIdentifier. */
function extractCertSignatureAlgorithm(raw) {
  const outer = readTlv(raw, 0);
  if (!outer) return null;
  const tbs = children(raw, outer)[0];
  if (!tbs) return null;
  const tbsKids = children(raw, tbs);
  // [0] version, serialNumber, signature AlgorithmIdentifier, ...
  const algId = tbsKids[2];
  if (!algId) return null;
  const algKids = children(raw, algId);
  if (!algKids.length) return null;
  return decodeOid(raw, algKids[0]);
}

function prettyDn(dn) {
  if (!dn || !dn.attributes) return '';
  return dn.attributes
    .map((a) => `${OID_NAMES[a.type] || a.type}=${a.value}`)
    .join(', ');
}

/* ------------------------------------------------------------------ */
/* signature algorithm mapping                                         */
/* ------------------------------------------------------------------ */

function algorithmFor(sigAlgOid, digestAlgOid) {
  const hash = digestName(digestAlgOid) || 'SHA-256';
  switch (sigAlgOid) {
    case OID.sha1Rsa: return { name: 'RSASSA-PKCS1-v1_5', hash: 'SHA-1' };
    case OID.sha256Rsa: return { name: 'RSASSA-PKCS1-v1_5', hash: 'SHA-256' };
    case OID.sha384Rsa: return { name: 'RSASSA-PKCS1-v1_5', hash: 'SHA-384' };
    case OID.sha512Rsa: return { name: 'RSASSA-PKCS1-v1_5', hash: 'SHA-512' };
    case OID.rsassaPss: return { name: 'RSA-PSS', hash, saltLength: hash === 'SHA-1' ? 20 : parseInt(hash.slice(4), 10) };
    case OID.ecdsaWithSha1: return { name: 'ECDSA', hash: 'SHA-1' };
    case OID.ecdsaWithSha256: return { name: 'ECDSA', hash: 'SHA-256' };
    case OID.ed25519: return { name: 'Ed25519' };
    default: return null;
  }
}

/* ------------------------------------------------------------------ */
/* chain building                                                      */
/* ------------------------------------------------------------------ */

/**
 * Build a candidate path from `leaf` upward using the certificates we hold.
 * @returns {{chain: Array, missingIssuer: boolean, brokenAt: number|null}}
 */
export function buildChain(leaf, pool) {
  const chain = [leaf];
  let current = leaf;
  let missingIssuer = false;

  for (let guard = 0; guard < 12; guard++) {
    if (current.isSelfSigned) break;
    const parent = pool.find(
      (c) =>
        c.subject === current.issuer &&
        c !== current &&
        (!current.aki || !c.ski || c.ski === current.aki || true),
    );
    if (!parent) {
      missingIssuer = true;
      break;
    }
    chain.push(parent);
    current = parent;
  }
  return { chain, missingIssuer };
}

/** Algorithm used by a CA to sign a certificate. */
function certSigAlgorithm(oid) {
  switch (oid) {
    case '1.2.840.113549.1.1.5': return { name: 'RSASSA-PKCS1-v1_5', hash: 'SHA-1' };
    case '1.2.840.113549.1.1.11': return { name: 'RSASSA-PKCS1-v1_5', hash: 'SHA-256' };
    case '1.2.840.113549.1.1.12': return { name: 'RSASSA-PKCS1-v1_5', hash: 'SHA-384' };
    case '1.2.840.113549.1.1.13': return { name: 'RSASSA-PKCS1-v1_5', hash: 'SHA-512' };
    case '1.2.840.10045.4.1': return { name: 'ECDSA', hash: 'SHA-1' };
    case '1.2.840.10045.4.3.2': return { name: 'ECDSA', hash: 'SHA-256' };
    case '1.2.840.10045.4.3.3': return { name: 'ECDSA', hash: 'SHA-384' };
    case '1.2.840.10045.4.3.4': return { name: 'ECDSA', hash: 'SHA-512' };
    case '1.3.101.112': return { name: 'Ed25519' };
    default: return null;
  }
}

/**
 * Cryptographically verify that `child` was issued by `parent`.
 * Returns true / false / null (could not determine).
 */
async function issuedBy(child, parent) {
  const alg = certSigAlgorithm(child.signatureAlgorithmOid);
  if (!alg || !child.tbsBytes) return null;
  try {
    const key = await subtle.importKey(
      'spki',
      new Uint8Array(parent.spkiDer),
      alg,
      true,
      ['verify'],
    );
    const sig = new Uint8Array(
      child.signature instanceof Uint8Array
        ? child.signature
        : Buffer.from(child.signature, 'binary'),
    );
    return await subtle.verify(alg.name, key, sig, new Uint8Array(child.tbsBytes));
  } catch {
    return null;
  }
}

function validAt(cert, when) {
  return when >= cert.notBefore && when <= cert.notAfter;
}

/* ------------------------------------------------------------------ */
/* main verification                                                   */
/* ------------------------------------------------------------------ */

/**
 * @param {Buffer} buf raw PDF bytes
 * @param {object} opts
 *   opts.trustedRoots: Array<{der: Buffer, name?: string, source?: string}>
 *   opts.extraCerts:  Array<Buffer> additional intermediate certs (DER)
 *   opts.at:          Date to evaluate cert validity against (default: now)
 */
export async function verifyPdf(input, opts = {}) {
  // Accept a Buffer, a Uint8Array or an ArrayBuffer. Everything downstream
  // assumes Buffer semantics (notably `.toString('latin1')`).
  const buf = Buffer.isBuffer(input)
    ? input
    : Buffer.from(input instanceof ArrayBuffer ? new Uint8Array(input) : input);
  const trustedRoots = opts.trustedRoots || [];
  const extraCerts = opts.extraCerts || [];
  const at = opts.at || new Date();
  const issues = [];

  const meta = pdfMeta(buf);
  const sigs = findSignatures(buf);

  if (!sigs.length) {
    return {
      verdict: 'UNSIGNED',
      integrity: 'n/a',
      trust: 'n/a',
      meta,
      signatures: [],
      issues: [
        {
          code: 'NO_SIGNATURE',
          severity: 'info',
          message: 'No PDF digital signature found in this document.',
        },
      ],
    };
  }

  const rootByFingerprint = new Map();
  for (const r of trustedRoots) {
    const fp = await fingerprint(r.der);
    rootByFingerprint.set(fp, { ...r, fingerprint: fp });
  }

  const results = [];

  for (const sig of sigs) {
    const sigIssues = [];
    let integrity = 'indeterminate';
    let trust = 'unknown';
    let cms = null;
    let signerInfo = null;
    let signatureVerified = null;
    let chainVerified = null;

    try {
      cms = parseSignedData(sig.contents);
    } catch (err) {
      sigIssues.push({
        code: 'CMS_PARSE_FAILED',
        severity: 'error',
        message: `Signature blob could not be parsed as CMS SignedData: ${err.message}`,
      });
    }

    try {
      if (cms) {
        // ---- 1. byte range + digest -----------------------------------
      const [a, b, c, d] = sig.byteRange;
      const covered = Buffer.concat(
        sig.segments.map(([start, end]) => buf.subarray(start, end)),
      );
      const algo = signerInfoAlgo(cms);
      const hashName = digestName(algo) || 'SHA-256';
      const computed = await digest(hashName, new Uint8Array(covered));

      // ---- 2. the signed attributes carry the authoritative digest -----
      if (cms.signers.length) {
        signerInfo = cms.signers[0];
        const mdAttr = signerInfo.messageDigest;
        if (mdAttr) {
          const expected = new Uint8Array(mdAttr);
          if (Buffer.compare(Buffer.from(computed), Buffer.from(expected)) === 0) {
            integrity = 'valid';
          } else {
            integrity = 'invalid';
            sigIssues.push({
              code: 'DIGEST_MISMATCH',
              severity: 'error',
              message:
                'The signed content no longer matches the digest recorded at signing time. The document was modified after it was signed.',
            });
          }
        }

        // ---- 3. verify the cryptographic signature -------------------
        const sigAlg = algorithmFor(signerInfo.signatureAlgorithm, signerInfo.digestAlgorithm);
        {
          const pool = [
            ...cms.certificates.map(parseCertificate),
            ...extraCerts.map(parseCertificate),
          ];
          const leafCert =
            pool.find(
              (c) =>
                signerInfo.sid &&
                signerInfo.sid.type === 'issuerAndSerial' &&
                c.serialNumber === signerInfo.sid.serial,
            ) || pool[0];

          if (leafCert) {
            if (sigAlg && integrity !== 'invalid') {
            const dataToVerify = signerInfo.signedAttrsBytes
              ? new Uint8Array(signerInfo.signedAttrsBytes)
              : new Uint8Array(covered);
            try {
              const key = await subtle.importKey(
                'spki',
                new Uint8Array(leafCert.spkiDer),
                sigAlg,
                true,
                ['verify'],
              );
              const ok = await subtle.verify(
                sigAlg.name,
                key,
                new Uint8Array(signerInfo.signature),
                dataToVerify,
              );
              signatureVerified = ok;
              if (ok) {
                if (integrity === 'indeterminate') integrity = 'valid';
              } else {
                integrity = 'invalid';
                sigIssues.push({
                  code: 'SIGNATURE_MISMATCH',
                  severity: 'error',
                  message: 'The cryptographic signature does not verify against the signer certificate.',
                });
              }
            } catch (err) {
              sigIssues.push({
                code: 'SIGNATURE_CHECK_FAILED',
                severity: 'warning',
                message: `Could not complete the signature check: ${
                  err && (err.message || err.name) ? err.message || err.name : String(err)
                }`,
              });
            }
            }

            // ---- 4. trust chain -------------------------------------
            const { chain, missingIssuer } = buildChain(leafCert, pool);
            const root = chain[chain.length - 1];
            const rootFp = await fingerprint(root.der);
            const anchored = rootByFingerprint.get(rootFp);

            // Every link must verify cryptographically - matching DN strings
            // is not evidence of anything.
            let chainValid = true;
            let linksOk = 0;
            let linksChecked = 0;
            for (let i = 0; i < chain.length - 1; i++) {
              const ok = await issuedBy(chain[i], chain[i + 1]);
              linksChecked++;
              if (ok === true) linksOk++;
              if (ok === null) {
                sigIssues.push({
                  code: 'CHAIN_LINK_UNVERIFIED',
                  severity: 'warning',
                  message: `Could not cryptographically verify the link from "${chain[i].subjectPretty}" to "${chain[i + 1].subjectPretty}".`,
                });
              }
              if (ok === false) {
                chainValid = false;
                sigIssues.push({
                  code: 'CHAIN_LINK_INVALID',
                  severity: 'error',
                  message: `The certificate for "${chain[i].subjectPretty}" was NOT actually issued by "${chain[i + 1].subjectPretty}". The chain is forged.`,
                });
              }
            }

            chainVerified =
              linksChecked === 0 ? null : linksOk === linksChecked ? true : false;

            if (!chainValid) {
              trust = 'invalid-chain';
            } else if (missingIssuer && !root.isSelfSigned) {
              trust = 'incomplete';
              sigIssues.push({
                code: 'CHAIN_INCOMPLETE',
                severity: 'warning',
                message: `The issuing certificate for "${chain[chain.length - 1].issuerPretty}" is not embedded in the document and is not available offline.`,
              });
            } else if (anchored && chainVerified === true && signatureVerified === true) {
              trust = 'trusted';
            } else if (anchored) {
              trust = 'unverified';
              sigIssues.push({
                code: 'CHAIN_NOT_VERIFIED',
                severity: 'warning',
                message:
                  'The chain reaches a trusted root, but not every link could be cryptographically verified in this environment, so the result is reported as unverified.',
              });
            } else {
              trust = 'untrusted';
              sigIssues.push({
                code: 'ROOT_NOT_TRUSTED',
                severity: 'warning',
                message: `The signature chains to a root certificate that is not in this app's trust store: "${root.subjectPretty}" (SHA-256 ${rootFp}). The document is intact, but this app cannot vouch for the issuer.`,
              });
            }

            // ---- 5. validity window ----------------------------------
            const signedAt = signerInfo.signingTime || at;
            for (const cert of chain) {
              if (!validAt(cert, signedAt)) {
                sigIssues.push({
                  code: 'CERT_NOT_YET_VALID_OR_EXPIRED',
                  severity: 'error',
                  message: `Certificate "${cert.subjectPretty}" was not valid at the signing time (${signedAt.toISOString()}).`,
                });
                integrity = integrity === 'valid' ? 'invalid' : integrity;
              }
            }

            // ---- 6. content appended after the signature ---------------
            let status = buildStatus(integrity, trust);
            if (sig.hasPostSignatureData) {
              sigIssues.push({
                code: 'CHANGES_AFTER_SIGNING',
                severity: 'warning',
                message:
                  'The file contains an incremental update that was added after the signature was applied. The original signed content is intact, but the document was modified afterwards.',
              });
              if (status === 'valid') status = 'valid-with-changes';
            }

            results.push({
              index: sig.index,
              status,
              integrity,
              trust,
              signatureVerified,
              chainVerified,
              subFilter: signatureSubFilter(buf),
              digestAlgorithm: signerInfo.digestAlgorithm,
              signatureAlgorithm: signerInfo.signatureAlgorithm,
              signedAt: signerInfo.signingTime ? signerInfo.signingTime.toISOString() : null,
              hasTimestamp: cms.signers.some((s) =>
                s.unsignedAttrs.length > 0,
              ),
              signer: {
                subject: leafCert.subjectPretty,
                issuer: leafCert.issuerPretty,
                serialNumber: leafCert.serialNumber,
                notBefore: leafCert.notBefore.toISOString(),
                notAfter: leafCert.notAfter.toISOString(),
                isCA: leafCert.isCA,
                sha256Fingerprint: await fingerprint(leafCert.der),
              },
              chain: await Promise.all(
                chain.map(async (cert) => ({
                  subject: cert.subjectPretty,
                  issuer: cert.issuerPretty,
                  serialNumber: cert.serialNumber,
                  notBefore: cert.notBefore.toISOString(),
                  notAfter: cert.notAfter.toISOString(),
                  isCA: cert.isCA,
                  selfSigned: cert.isSelfSigned,
                  sha256Fingerprint: await fingerprint(cert.der),
                })),
              ),
              trustAnchor: anchored ? anchored.name : null,
              rootFingerprint: rootFp,
              coversWholeFile: sig.coversWholeFile,
              hasPostSignatureData: sig.hasPostSignatureData,
              issues: sigIssues,
            });
            continue;
          }
        }
      }
      }
    } catch (err) {
      sigIssues.push({
        code: 'VERIFICATION_ERROR',
        severity: 'error',
        message: err.message,
      });
    }

    results.push({
      index: sig.index,
      status: 'error',
      integrity,
      trust,
      issues: sigIssues,
    });
  }

  const primary = results[0] || {};
  const verdict = overallVerdict(results);

  return {
    verdict,
    integrity: primary.integrity || 'indeterminate',
    trust: primary.trust || 'unknown',
    meta,
    signatures: results,
    issues,
  };
}

function signerInfoAlgo(cms) {
  if (!cms || !cms.signers.length) return OID.sha256;
  return cms.signers[0].digestAlgorithm;
}

function buildStatus(integrity, trust) {
  if (integrity === 'invalid') return 'modified';
  if (integrity === 'valid' && trust === 'trusted') return 'valid';
  if (integrity === 'valid' && trust === 'unverified') return 'unverified';
  if (integrity === 'valid') return 'intact-untrusted';
  return 'indeterminate';
}

function overallVerdict(results) {
  if (!results.length) return 'UNSIGNED';
  if (results.some((r) => r.status === 'modified')) return 'MODIFIED';
  if (results.some((r) => r.status === 'valid')) return 'VALID';
  if (results.some((r) => r.status === 'valid-with-changes')) return 'VALID_WITH_CHANGES';
  if (results.some((r) => r.status === 'intact-untrusted')) return 'INTACT_UNTRUSTED';
  if (results.some((r) => r.status === 'unverified')) return 'UNVERIFIED';
  if (results.some((r) => r.status === 'indeterminate')) return 'INDETERMINATE';
  return 'ERROR';
}
