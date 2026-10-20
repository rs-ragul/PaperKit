#!/usr/bin/env python3
"""
Generate realistic PDF-signature fixtures so the verifier can be tested
against a real CMS/PKCS#7 signature, not a mock.

Chain:  Test Root CA (self-signed)  ->  Test Intermediate CA  ->  Leaf DSC
Produces:
  valid.pdf              - signed, chain complete, root = "trusted" (pinned)
  foreign.pdf            - signed by a DIFFERENT root (not pinned) -> untrusted
  tampered_bytes.pdf     - one byte inserted after signing -> digest mismatch
  tampered_resave.pdf    - re-written by another tool after signing
  appended.pdf           - incremental update after signing (allowed changes)
"""
import datetime
import io
import os
import sys

from cryptography import x509
from cryptography.hazmat.primitives import hashes, serialization
from cryptography.hazmat.primitives.asymmetric import rsa
from cryptography.x509.oid import NameOID

from reportlab.lib.pagesizes import A4
from reportlab.pdfgen import canvas
from pyhanko.pdf_utils.incremental_writer import IncrementalPdfFileWriter
from pyhanko.sign import signers
from pyhanko.sign.fields import SigFieldSpec

OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "fixtures")
OUT = os.path.abspath(OUT)
os.makedirs(OUT, exist_ok=True)

NOW = datetime.datetime.now(datetime.timezone.utc)


def name(cn, org="VeriPDF Test PKI"):
    return x509.Name([
        x509.NameAttribute(NameOID.ORGANIZATION_NAME, org),
        x509.NameAttribute(NameOID.COMMON_NAME, cn),
        x509.NameAttribute(NameOID.COUNTRY_NAME, "IN"),
    ])


def make_root(cn):
    key = rsa.generate_private_key(public_exponent=65537, key_size=2048)
    cert = (
        x509.CertificateBuilder()
        .subject_name(name(cn))
        .issuer_name(name(cn))
        .public_key(key.public_key())
        .serial_number(x509.random_serial_number())
        .not_valid_before(NOW - datetime.timedelta(days=365))
        .not_valid_after(NOW + datetime.timedelta(days=3650))
        .add_extension(x509.BasicConstraints(ca=True, path_length=1), critical=True)
        .add_extension(
            x509.KeyUsage(
                digital_signature=False, content_commitment=False,
                key_encipherment=False, data_encipherment=False,
                key_agreement=False, key_cert_sign=True, crl_sign=True,
                encipher_only=False, decipher_only=False,
            ), critical=True,
        )
        .sign(key, hashes.SHA256())
    )
    return key, cert


def make_intermediate(root_key, root_cert, cn):
    key = rsa.generate_private_key(public_exponent=65537, key_size=2048)
    cert = (
        x509.CertificateBuilder()
        .subject_name(name(cn))
        .issuer_name(root_cert.subject)
        .public_key(key.public_key())
        .serial_number(x509.random_serial_number())
        .not_valid_before(NOW - datetime.timedelta(days=365))
        .not_valid_after(NOW + datetime.timedelta(days=1825))
        .add_extension(x509.BasicConstraints(ca=True, path_length=0), critical=True)
        .add_extension(
            x509.KeyUsage(
                digital_signature=False, content_commitment=False,
                key_encipherment=False, data_encipherment=False,
                key_agreement=False, key_cert_sign=True, crl_sign=True,
                encipher_only=False, decipher_only=False,
            ), critical=True,
        )
        .add_extension(
            x509.SubjectKeyIdentifier.from_public_key(key.public_key()), critical=False
        )
        .add_extension(
            x509.AuthorityKeyIdentifier.from_issuer_public_key(root_key.public_key()),
            critical=False,
        )
        .sign(root_key, hashes.SHA256())
    )
    return key, cert


def make_leaf(ca_key, ca_cert, cn):
    key = rsa.generate_private_key(public_exponent=65537, key_size=2048)
    cert = (
        x509.CertificateBuilder()
        .subject_name(name(cn, "VeriPDF Test Signer"))
        .issuer_name(ca_cert.subject)
        .public_key(key.public_key())
        .serial_number(x509.random_serial_number())
        .not_valid_before(NOW - datetime.timedelta(days=30))
        .not_valid_after(NOW + datetime.timedelta(days=825))
        .add_extension(x509.BasicConstraints(ca=False, path_length=None), critical=True)
        .add_extension(
            x509.KeyUsage(
                digital_signature=True, content_commitment=True,
                key_encipherment=False, data_encipherment=False,
                key_agreement=False, key_cert_sign=False, crl_sign=False,
                encipher_only=False, decipher_only=False,
            ), critical=True,
        )
        .add_extension(
            x509.ExtendedKeyUsage([
                x509.ObjectIdentifier("1.3.6.1.5.5.7.3.3"),  # id-kp-codeSigning
                x509.ObjectIdentifier("1.3.6.1.5.5.7.3.4"),  # id-kp-emailProtection
            ]), critical=False,
        )
        .add_extension(
            x509.SubjectKeyIdentifier.from_public_key(key.public_key()), critical=False
        )
        .add_extension(
            x509.AuthorityKeyIdentifier.from_issuer_public_key(ca_key.public_key()),
            critical=False,
        )
        .sign(ca_key, hashes.SHA256())
    )
    return key, cert


def make_signer(leaf_key, leaf_cert, chain_certs):
    """Build a pyHanko SimpleSigner straight from key/cert objects.

    (We avoid PKCS#12 here: modern `cryptography` writes PBES2/AES which
    pyHanko's asn1crypto-based reader does not accept.)
    """
    from asn1crypto import keys as a_keys, x509 as a_x509
    from pyhanko_certvalidator.registry import SimpleCertificateStore

    signing_key = a_keys.PrivateKeyInfo.load(
        leaf_key.private_bytes(
            serialization.Encoding.DER,
            serialization.PrivateFormat.PKCS8,
            serialization.NoEncryption(),
        )
    )
    signing_cert = a_x509.Certificate.load(
        leaf_cert.public_bytes(serialization.Encoding.DER)
    )
    registry = SimpleCertificateStore.from_certs(
        [
            a_x509.Certificate.load(c.public_bytes(serialization.Encoding.DER))
            for c in chain_certs
        ]
    )
    return signers.SimpleSigner(signing_cert, signing_key, registry)


def base_pdf(path, title):
    c = canvas.Canvas(path, pagesize=A4)
    c.setFont("Helvetica-Bold", 22)
    c.drawString(60, 760, title)
    c.setFont("Helvetica", 11)
    c.drawString(60, 720, "Signed by: Zonal Deputy Tahsildar (test fixture)")
    c.drawString(60, 700, "Certificate No: TN-720-2026-000123")
    c.drawString(60, 680, "Community: BC - Maravar")
    c.setFont("Helvetica-Oblique", 9)
    c.drawString(60, 640, "This is a synthetic fixture for offline signature verification testing.")
    c.showPage()
    c.save()


def sign_pdf(base, out, signer):
    with open(base, "rb") as src:
        w = IncrementalPdfFileWriter(src)
        meta = signers.PdfSignatureMetadata(
            field_name="Signature1",
            md_algorithm="sha256",
            reason="Issued by the revenue department",
            contact_info="veripdf test fixture",
        )
        field = SigFieldSpec(sig_field_name="Signature1", on_page=0, box=(60, 60, 320, 160))
        pdf_signer = signers.PdfSigner(
            meta,
            signer=signer,
            stamp_style=signers.DEFAULT_SIGNING_STAMP_STYLE,
            new_field_spec=field,
        )
        with open(out, "wb") as f:
            pdf_signer.sign_pdf(w, output=f)


def main():
    print("building PKI ...")
    root_key, root_cert = make_root("VeriPDF Test Root CA")
    int_key, int_cert = make_intermediate(root_key, root_cert, "VeriPDF Test Intermediate CA")
    leaf_key, leaf_cert = make_leaf(int_key, int_cert, "Zonal Deputy Tahsildar, Tambaram")
    trusted_signer = make_signer(leaf_key, leaf_cert, [int_cert, root_cert])

    foreign_root_key, foreign_root_cert = make_root("Foreign Root CA (not pinned)")
    f_int_key, f_int_cert = make_intermediate(foreign_root_key, foreign_root_cert, "Foreign Intermediate CA")
    f_leaf_key, f_leaf_cert = make_leaf(f_int_key, f_int_cert, "Some Other Signer")
    foreign_signer = make_signer(f_leaf_key, f_leaf_cert, [f_int_cert, foreign_root_cert])

    base = os.path.join(OUT, "base.pdf")
    base_pdf(base, "Community Certificate - Test Fixture")

    print("signing valid.pdf ...")
    sign_pdf(base, os.path.join(OUT, "valid.pdf"), trusted_signer)

    print("signing foreign.pdf ...")
    sign_pdf(base, os.path.join(OUT, "foreign.pdf"), foreign_signer)

    # --- tampered: one extra byte inserted inside the signed byte range ---
    with open(os.path.join(OUT, "valid.pdf"), "rb") as f:
        data = bytearray(f.read())
    idx = data.find(b"\n", 8) + 1
    data[idx:idx] = b"% innocuous comment added after signing\n"
    with open(os.path.join(OUT, "tampered_bytes.pdf"), "wb") as f:
        f.write(bytes(data))

    # --- tampered: re-saved by another tool (full rewrite) ---
    try:
        from pypdf import PdfReader, PdfWriter
        r = PdfReader(os.path.join(OUT, "valid.pdf"))
        w = PdfWriter()
        for page in r.pages:
            w.add_page(page)
        w.add_metadata({"/Producer": "SomeOtherEditor"})
        with open(os.path.join(OUT, "tampered_resave.pdf"), "wb") as f:
            w.write(f)
    except Exception as exc:  # pragma: no cover
        print("  (resave fixture skipped:", exc, ")")

    # --- appended: a GENUINE incremental update after the signature ---
    # The original bytes are left untouched; a new object + xref + trailer
    # (/Prev) are appended. This is exactly what Acrobat does when it adds a
    # comment or form value to an already-signed document.
    try:
        import re

        with open(os.path.join(OUT, "valid.pdf"), "rb") as f:
            original = f.read()
        n = len(original)

        prev_xref = int(re.findall(rb"startxref\s+(\d+)", original)[-1])
        root_obj = int(re.findall(rb"/Root\s+(\d+)\s+\d+\s+R", original)[-1])

        obj_num = 99
        extra = (
            f"\n{obj_num} 0 obj\n"
            "<< /Type /Annot /Subtype /Text /Rect [0 0 0 0] "
            "/Contents (comment added after signing) >>\nendobj\n"
        ).encode("latin1")
        new_xref_offset = n + len(extra)
        xref = (
            f"xref\n{obj_num} 1\n"
            f"{n + 1:010d} 00000 n \n"
        ).encode("latin1")
        trailer = (
            f"trailer\n<< /Size {obj_num + 1} /Root {root_obj} 0 R "
            f"/Prev {prev_xref} >>\nstartxref\n{new_xref_offset}\n%%EOF\n"
        ).encode("latin1")

        with open(os.path.join(OUT, "appended.pdf"), "wb") as f:
            f.write(original + extra + xref + trailer)
    except Exception as exc:  # pragma: no cover
        print("  (append fixture skipped:", exc, ")")

    # --- export the trust anchors (DER) the app would ship -------------
    with open(os.path.join(OUT, "trusted-root.der"), "wb") as f:
        f.write(root_cert.public_bytes(serialization.Encoding.DER))
    with open(os.path.join(OUT, "trusted-intermediate.der"), "wb") as f:
        f.write(int_cert.public_bytes(serialization.Encoding.DER))
    with open(os.path.join(OUT, "foreign-root.der"), "wb") as f:
        f.write(foreign_root_cert.public_bytes(serialization.Encoding.DER))

    print("fixtures written to", OUT)
    for fn in sorted(os.listdir(OUT)):
        print("  ", fn, os.path.getsize(os.path.join(OUT, fn)), "bytes")


if __name__ == "__main__":
    sys.exit(main())
