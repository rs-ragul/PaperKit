import { Buffer } from 'buffer';
/**
 * Minimal DER parser that preserves exact byte offsets.
 *
 * Why not use forge's ASN.1 for everything? Because CMS signedAttrs must be
 * verified over their ORIGINAL DER bytes with the SET tag (0x31) substituted
 * for the IMPLICIT [0] tag (0xA0) found in the SignedData structure. If we
 * decode and re-encode, a lenient encoder can change the bytes and break the
 * signature. So we keep raw slices.
 */

export const TAG = {
  BOOLEAN: 0x01,
  INTEGER: 0x02,
  BIT_STRING: 0x03,
  OCTET_STRING: 0x04,
  NULL: 0x05,
  OID: 0x06,
  UTF8_STRING: 0x0c,
  SEQUENCE: 0x30,
  SET: 0x31,
  PRINTABLE_STRING: 0x13,
  T61_STRING: 0x14,
  IA5_STRING: 0x16,
  UTC_TIME: 0x17,
  GENERALIZED_TIME: 0x18,
  BMP_STRING: 0x1e,
  CONTEXT_0: 0xa0,
  CONTEXT_1: 0xa1,
  CONTEXT_2: 0xa2,
  CONTEXT_3: 0xa3,
};

/** Parse one TLV at `offset`. Returns null on malformed input. */
export function readTlv(buf, offset) {
  if (offset + 2 > buf.length) return null;
  const tag = buf[offset];
  let p = offset + 1;
  let length = buf[p++];
  if (length & 0x80) {
    const n = length & 0x7f;
    if (n === 0 || n > 4 || p + n > buf.length) return null;
    length = 0;
    for (let i = 0; i < n; i++) length = length * 256 + buf[p++];
  }
  const contentStart = p;
  const contentEnd = contentStart + length;
  if (contentEnd > buf.length) return null;
  return { tag, headerStart: offset, contentStart, contentEnd, end: contentEnd, length };
}

/** Iterate the direct children of a constructed TLV. */
export function children(buf, tlv) {
  const out = [];
  let p = tlv.contentStart;
  while (p < tlv.contentEnd) {
    const child = readTlv(buf, p);
    if (!child) break;
    out.push(child);
    p = child.end;
  }
  return out;
}

/** Raw bytes of a TLV, header included. */
export function rawOf(buf, tlv) {
  return buf.subarray(tlv.headerStart, tlv.end);
}

/** Raw content bytes of a TLV. */
export function contentOf(buf, tlv) {
  return buf.subarray(tlv.contentStart, tlv.contentEnd);
}

/** Re-tag a TLV in place (same content, different tag) -> new Buffer. */
export function retag(buf, tlv, newTag) {
  const lenBytes = [];
  let n = tlv.length;
  if (n < 0x80) {
    lenBytes.push(n);
  } else {
    const raw = [];
    let x = n;
    while (x > 0) {
      raw.unshift(x & 0xff);
      x >>>= 8;
    }
    lenBytes.push(0x80 | raw.length, ...raw);
  }
  return Buffer.concat([Buffer.from([newTag, ...lenBytes]), contentOf(buf, tlv)]);
}

/** Decode an OID TLV to dotted notation. */
export function decodeOid(buf, tlv) {
  const c = contentOf(buf, tlv);
  if (!c.length) return '';
  const first = c[0];
  const parts = [Math.floor(first / 40), first % 40];
  let value = 0;
  for (let i = 1; i < c.length; i++) {
    value = value * 128 + (c[i] & 0x7f);
    if (!(c[i] & 0x80)) {
      parts.push(value);
      value = 0;
    }
  }
  return parts.join('.');
}

/** Decode UTCTime / GeneralizedTime to a JS Date (UTC). */
export function decodeTime(buf, tlv) {
  const s = contentOf(buf, tlv).toString('latin1').trim();
  if (tlv.tag === TAG.UTC_TIME) {
    // YYMMDDHHMMSSZ
    const yy = parseInt(s.slice(0, 2), 10);
    const year = yy >= 50 ? 1900 + yy : 2000 + yy;
    return new Date(Date.UTC(
      year,
      parseInt(s.slice(2, 4), 10) - 1,
      parseInt(s.slice(4, 6), 10),
      parseInt(s.slice(6, 8), 10),
      parseInt(s.slice(8, 10), 10),
      parseInt(s.slice(10, 12), 10),
    ));
  }
  // GeneralizedTime: YYYYMMDDHHMMSSZ
  return new Date(Date.UTC(
    parseInt(s.slice(0, 4), 10),
    parseInt(s.slice(4, 6), 10) - 1,
    parseInt(s.slice(6, 8), 10),
    parseInt(s.slice(8, 10), 10),
    parseInt(s.slice(10, 12), 10),
    parseInt(s.slice(12, 14), 10),
  ));
}

/** First child with the given tag. */
export function firstChild(buf, tlv, tag) {
  return children(buf, tlv).find((c) => c.tag === tag) || null;
}
