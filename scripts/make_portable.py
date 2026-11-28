"""Build a location-independent APPLOAD entry point with embedded host modules."""
import base64
import hashlib
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
BUNDLE = ROOT / "dist" / "v1.1.0" / "CS16.bundle"
FAMILIES = ("2020", "2021-2024", "2025-2026", "2027")

def build():
    digest = hashlib.sha256()
    clauses = []
    count = 0
    for family in FAMILIES:
        entries = []
        for path in sorted((BUNDLE / "Contents" / family).glob("*.dll")):
            if path.name.lower().startswith(("acmgd", "acdbmgd", "accoremgd", "autodesk.")):
                raise RuntimeError(f"Host assembly must not be embedded: {path}")
            data = path.read_bytes()
            digest.update(family.encode() + path.name.encode() + hashlib.sha256(data).digest())
            # LOAD's reader has a much smaller literal/line limit than runtime
            # strings. Keep each Base64 literal below 255 characters as well.
            chunks = [base64.b64encode(data[i:i + 180]).decode("ascii") for i in range(0, len(data), 180)]
            assert b"".join(base64.b64decode(s, validate=True) for s in chunks) == data
            lines = "\n".join('"' + s + '"' for s in chunks)
            entries.append(f'(list "{path.name}" {len(data)} \'(\n{lines}\n))')
            count += 1
        if not any('"AutoCADCS.dll"' in entry for entry in entries):
            raise RuntimeError(f"Missing game module: {family}")
        clauses.append(f'((equal family "{family}") (list\n' + "\n".join(entries) + "))")
    payloads = "(defun cs16:payload (family) (cond\n" + "\n".join(clauses) + "\n(T nil)))"
    source = (ROOT / "distribution" / "CS16-portable.lsp").read_text(encoding="utf-8")
    source = source.replace("@@BUILD_ID@@", "v1.1.0-" + digest.hexdigest()[:16]).replace("@@PAYLOADS@@", payloads)
    notices = (ROOT / "distribution" / "THIRD-PARTY.txt").read_text(encoding="utf-8")
    source = "\n".join(";; " + line for line in notices.splitlines()) + "\n" + source
    result = BUNDLE.parent / "CS16.lsp"
    result.write_text(source, encoding="ascii")
    # Verify the written payload too, not only the intermediate chunk encoder.
    import re
    written = result.read_text(encoding="ascii")
    entries = re.findall(r'\(list "([^"\n]+\.dll)" (\d+) \'\(\n((?:"[A-Za-z0-9+/=]+"\n)+)\)\)', written)
    expected = [p for f in FAMILIES for p in sorted((BUNDLE / "Contents" / f).glob("*.dll"))]
    assert len(entries) == len(expected)
    for (name, size, encoded), path in zip(entries, expected):
        raw = b"".join(base64.b64decode(line[1:-1], validate=True) for line in encoded.splitlines())
        assert name == path.name and len(raw) == int(size) and raw == path.read_bytes()
    print(f"Portable APPLOAD file: {result}; {count} verified embedded DLLs; {result.stat().st_size} bytes")

if __name__ == "__main__":
    build()
