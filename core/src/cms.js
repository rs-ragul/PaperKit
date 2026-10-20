/**
 * CMS (RFC 5652) SignedData parser, scoped to what PDF signatures need.
 * Produces plain JS structures plus the exact raw byte slices required for
 * cryptographic verification.
 */
import { Buffer } from 'buffer';
import {
  TAG, readTlv, children, rawOf, contentOf, retag, decodeOid, decodeTime,
} from './der.js';

/* OIDs we care about */
export const OID = {
  signedData: '1.2.840.113549.1.7.2',
  data: '1.2.840.113549.1.7.1',
  rsaEncryption: '1.2.840.113549.1.1.1',
  sha256: '2.16.840.1.101.3.4.2.1',
  sha1: '1.3.14.3.2.26',
  sha384: '2.16.840.1.101.3.4.2.2',
  sha512: '2.16.840.1.101.3.4.2.3',
  sha256Rsa: '1.2.840.113549.1.1.11',
  sha1Rsa: '1.2.840.113549.1.1.5',
  sha384Rsa: '1.2.840.113549.1.1.12',
  sha512Rsa: '1.2.840.113549.1.1.13',
  rsassaPss: '1.2.840.113549.1.1.10',
  ecPublicKey: '1.2.840.10045.2.1',
  ecdsaWithSha256: '1.2.840.10045.4.3.2',
  ecdsaWithSha1: '1.2.840.10045.4.1',
  ed25519: '1.3.101.112',
  signingTime: '1.2.840.113549.1.9.5',
  messageDigest: '1.2.840.113549.1.9.4',
  contentType: '1.2.840.113549.1.9.3',
};

/** Map a digest OID to a WebCrypto / Node digest name. */
export function digestName(oid) {
  switch (oid) {
    case OID.sha1: return 'SHA-1';
    case OID.sha256: return 'SHA-256';
    case OID.sha384: return 'SHA-384';
    case OID.sha512: return 'SHA-512';
    default: return null;
  }
}

/**
 * Parse a CMS ContentInfo/SignedData blob.
 * @param {Buffer} der
 */
export function parseSignedData(der) {
  const buf = Buffer.from(der);
  const contentInfo = readTlv(buf, 0);
  if (!contentInfo || contentInfo.tag !== TAG.SEQUENCE) {
    throw new Error('Not a DER SEQUENCE (CMS ContentInfo)');
  }
  const ciKids = children(buf, contentInfo);
  const oidTlv = ciKids[0];
  const contentOid = decodeOid(buf, oidTlv);
  if (contentOid !== OID.signedData) {
    throw new Error(`CMS contentType is ${contentOid}, expected signedData`);
  }
  // [0] EXPLICIT content
  const explicit = ciKids[1];
  if (!explicit) throw new Error('Missing SignedData content');
  const signedData = children(buf, explicit)[0];
  if (!signedData) throw new Error('Malformed SignedData');

  const sd = children(buf, signedData);
  // 0 version, 1 digestAlgorithms, 2 encapContentInfo, 3 [0] certs,
  // 4 [1] crls, 5 signerInfos
  const certsTlv = sd.find((c) => c.tag === TAG.CONTEXT_0) || null;
  // SignedData has TWO SETs: digestAlgorithms (early) and signerInfos (last).
  // Taking the first one silently parses the digest algorithm as a SignerInfo.
  const sets = sd.filter((c) => c.tag === TAG.SET);
  const signerInfosTlv = sets.length ? sets[sets.length - 1] : null;
  if (!signerInfosTlv) throw new Error('No signerInfos');

  const certificates = [];
  if (certsTlv) {
    for (const certTlv of children(buf, certsTlv)) {
      if (certTlv.tag === TAG.SEQUENCE) {
        // The FULL TLV, not just its content - X.509 parsers want the
        // complete Certificate SEQUENCE including its header.
        certificates.push(rawOf(buf, certTlv));
      }
    }
  }

  const signers = [];
  for (const siTlv of children(buf, signerInfosTlv)) {
    const si = children(buf, siTlv);
    const version = decodeInt(buf, si[0]);
    const sidTlv = si[1];
    const digestAlg = si[2];
    const signedAttrsTlv = si.find((c) => c.tag === TAG.CONTEXT_0) || null;
    // The signature algorithm is the AlgorithmIdentifier that FOLLOWS the
    // signed attributes (or the digest algorithm when there are none).
    let sigAlgTlv = null;
    if (signedAttrsTlv) {
      const idx = si.indexOf(signedAttrsTlv);
      sigAlgTlv = si[idx + 1] || null;
    } else {
      sigAlgTlv = si[3] || null;
    }
    const signatureTlv = si.find((c) => c.tag === TAG.OCTET_STRING);
    // unsignedAttrs is [1]
    const unsignedAttrsTlv = si.find((c) => c.tag === TAG.CONTEXT_1) || null;

    const digestAlgorithmOid = decodeOid(buf, firstOid(buf, digestAlg));
    const sigAlgOid = decodeOid(buf, firstOid(buf, sigAlgTlv));

    // Signed attributes: raw bytes with SET tag substituted for [0].
    let signedAttrsBytes = null;
    const attrs = {};
    if (signedAttrsTlv) {
      signedAttrsBytes = retag(buf, signedAttrsTlv, TAG.SET);
      for (const attr of children(buf, signedAttrsTlv)) {
        const kids = children(buf, attr);
        const attrOid = decodeOid(buf, kids[0]);
        const values = kids[1] ? children(buf, kids[1]) : [];
        attrs[attrOid] = values;
      }
    }

    const sid = parseSid(buf, sidTlv);

    signers.push({
      version,
      sid,
      digestAlgorithm: digestAlgorithmOid,
      signatureAlgorithm: sigAlgOid,
      signature: signatureTlv ? Buffer.from(contentOf(buf, signatureTlv)) : null,
      signedAttrsBytes,
      attrs,
      signingTime: extractSigningTime(buf, attrs),
      messageDigest: extractMessageDigest(buf, attrs),
      contentType: extractContentType(buf, attrs),
      unsignedAttrs: unsignedAttrsTlv ? children(buf, unsignedAttrsTlv) : [],
      raw: rawOf(buf, siTlv),
    });
  }

  return { contentType: contentOid, certificates, signers };
}

function firstOid(buf, tlv) {
  if (!tlv) return null;
  const kids = children(buf, tlv);
  return kids[0] || null;
}

function decodeInt(buf, tlv) {
  if (!tlv) return null;
  const c = contentOf(buf, tlv);
  let n = 0;
  for (const b of c) n = n * 256 + b;
  return n;
}

/** Big-endian integer as a normalised uppercase hex string (no leading 00). */
function decodeIntHex(buf, tlv) {
  if (!tlv) return null;
  let hex = contentOf(buf, tlv).toString('hex').toUpperCase();
  hex = hex.replace(/^(00)+/, '');
  return hex;
}

function parseSid(buf, tlv) {
  if (!tlv) return null;
  // SignerIdentifier is a CHOICE, encoded as the chosen alternative itself.
  if (tlv.tag === TAG.SEQUENCE) {
    // IssuerAndSerialNumber ::= SEQUENCE { issuer Name, serialNumber INTEGER }
    const kids = children(buf, tlv);
    const issuerTlv = kids[0];
    const serialTlv = kids[1];
    return {
      type: 'issuerAndSerial',
      issuerDer: rawOf(buf, issuerTlv),
      serial: decodeIntHex(buf, serialTlv),
    };
  }
  if (tlv.tag === TAG.CONTEXT_0) {
    // subjectKeyIdentifier [0]
    return { type: 'ski', ski: Buffer.from(contentOf(buf, tlv)) };
  }
  return null;
}

function extractSigningTime(buf, attrs) {
  const set = attrs[OID.signingTime];
  if (!set || !set.length) return null;
  const t = set[0];
  if (t.tag === TAG.UTC_TIME || t.tag === TAG.GENERALIZED_TIME) {
    return decodeTime(buf, t);
  }
  return null;
}

function extractMessageDigest(buf, attrs) {
  const set = attrs[OID.messageDigest];
  if (!set || !set.length) return null;
  return Buffer.from(contentOf(buf, set[0]));
}

function extractContentType(buf, attrs) {
  const set = attrs[OID.contentType];
  if (!set || !set.length) return null;
  return decodeOid(buf, set[0]);
}
