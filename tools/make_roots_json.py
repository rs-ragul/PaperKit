#!/usr/bin/env python3
"""Build the bundled trust store for the app.

Reads the real trust anchors from trust-roots/*.der and writes
app/web/bundled-roots.json, which the app ships with. Nothing is fetched at
runtime - the app is offline by design.

Source of the CCA India roots: https://cca.gov.in/root_certificate.html
"""
import base64
import hashlib
import json
import os

from cryptography import x509

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT_DIR = os.path.abspath(os.path.join(HERE, ".."))
TRUST_DIR = os.path.join(ROOT_DIR, "trust-roots")
OUT = os.path.join(ROOT_DIR, "app", "web", "bundled-roots.json")

# display name -> file stem
ANCHORS = [
    ("CCA India 2022 (Root CA)", "CCAIndia2022"),
    ("CCA India 2022 SPL (Root CA)", "CCAIndia2022SPL"),
    ("CCA India 2015 SPL (Root CA)", "CCAIndia2015"),
    ("CCA India 2014 (Root CA)", "CCAIndia2014"),
    ("CCA India 2011 (Root CA)", "CCAIndia2011"),
    ("CCA India 2007 (Root CA)", "CCAIndia2007"),
    ("CCA India (Root CA, 2002)", "CCAIndia2002"),
    ("UIDAI offline e-KYC public key", "uidai_offline_publickey"),
]

out = []
for name, stem in ANCHORS:
    path = os.path.join(TRUST_DIR, f"{stem}.der")
    if not os.path.exists(path):
        print(f"  skip (not present): {name}")
        continue
    with open(path, "rb") as f:
        der = f.read()
    cert = x509.load_der_x509_certificate(der)
    fp = hashlib.sha256(der).hexdigest().upper()
    out.append(
        {
            "name": name,
            "der": list(der),
            "sha256": fp,
            "subject": cert.subject.rfc4514_string(),
            "notBefore": cert.not_valid_before_utc.isoformat(),
            "notAfter": cert.not_valid_after_utc.isoformat(),
        }
    )
    print(f"  + {name}")
    print(f"      {cert.subject.rfc4514_string()}")
    print(f"      valid {cert.not_valid_before_utc.date()} -> {cert.not_valid_after_utc.date()}")
    print(f"      sha256 {fp}")

os.makedirs(os.path.dirname(OUT), exist_ok=True)
with open(OUT, "w") as f:
    json.dump(out, f, indent=1)

print(f"\nwrote {OUT} ({len(out)} anchor(s))")
