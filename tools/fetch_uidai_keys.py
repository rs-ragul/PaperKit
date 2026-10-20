#!/usr/bin/env python3
"""Fetch the UIDAI public key certificates.

UIDAI moved their certificate downloads to a CDN that blocks some networks
(and this build sandbox). Run this once on your own machine, then rebuild the
bundled trust store:

    python3 tools/fetch_uidai_keys.py
    python3 tools/make_roots_json.py
    npm run build

What the keys are for
---------------------
* `uidai_offline_publickey_*.cer`  - verifies the digital signature on an
  Aadhaar **Paperless Offline e-KYC** XML and on the **Secure QR code**.
* `uidai_auth_sign_*.cer`          - the certificate UIDAI uses to sign
  e-Aadhaar PDFs (this is what appears in the PDF's certificate chain).
"""
import base64
import pathlib
import re
import ssl
import sys
import urllib.request

HERE = pathlib.Path(__file__).resolve().parent
DEST = HERE.parent / "trust-roots"

BASE = "https://backend.uidai.gov.in/get/files/media/document/2026-07/"
NAMES = [
    "uidai_offline_publickey_2026",
    "uidai_offline_publickey_17022026",
    "uidai_offline_publickey_26022021",
    "uidai_offline_publickey_26022019",
    "uidai_offline_publickey_29032019",
    "uidai_auth_sign_prod",
    "uidai_auth_sign_Prod_2026",
    "uidai_auth_sign_prod_2023",
]

UA = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 Chrome/124 Safari/537.36"


def to_der(raw: bytes) -> bytes | None:
    if b"-----BEGIN" in raw:
        return base64.b64decode(b"".join(l for l in raw.splitlines() if not l.startswith(b"---")))
    try:
        from cryptography import x509

        x509.load_der_x509_certificate(raw)
        return raw
    except Exception:
        pass
    try:
        return base64.b64decode(re.sub(rb"\s+", b"", raw), validate=True)
    except Exception:
        return None


def main() -> int:
    DEST.mkdir(parents=True, exist_ok=True)
    ctx = ssl.create_default_context()
    ok = fail = 0

    for name in NAMES:
        url = BASE + name + ".cer"
        try:
            req = urllib.request.Request(url, headers={"User-Agent": UA})
            raw = urllib.request.urlopen(req, timeout=45, context=ctx).read()
            der = to_der(raw)
            if not der:
                print(f"  ! {name}: downloaded but not a certificate")
                fail += 1
                continue
            (DEST / f"{name}.der").write_bytes(der)
            print(f"  + {name}.der ({len(der)} bytes)")
            ok += 1
        except Exception as exc:
            print(f"  ! {name}: {type(exc).__name__}: {exc}")
            fail += 1

    print(f"\n{ok} downloaded, {fail} failed -> {DEST}")
    print("now run:  python3 tools/make_roots_json.py && npm run build")
    return 0 if ok else 1


if __name__ == "__main__":
    sys.exit(main())
