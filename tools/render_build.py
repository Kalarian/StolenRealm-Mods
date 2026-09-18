"""Render a build guide: HTML -> PDF (same basename) + preview.png of page 1, using Edge headless.

Usage:  python C:/Claude/General/stolen-realm/tools/render_build.py <path/to/BuildN-Name.html>

Writes <same folder>/<basename>.pdf and <same folder>/preview.png. If the PDF is locked by an open viewer the
file is written as <basename>-v2.pdf (then -v3 ...) instead. Prints the page count when pypdf is installed.
"""
import os, sys, subprocess

EDGE = r"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe"
if not os.path.exists(EDGE):
    EDGE = r"C:\Program Files\Microsoft\Edge\Application\msedge.exe"


def run(args, timeout=120):
    subprocess.run([EDGE, "--headless=new", "--disable-gpu", "--hide-scrollbars"] + args, check=True, timeout=timeout,
                   stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)


def main():
    if len(sys.argv) < 2 or not sys.argv[1].lower().endswith(".html"):
        sys.exit(__doc__)
    src = os.path.abspath(sys.argv[1])
    folder, base = os.path.split(src)
    stem = os.path.splitext(base)[0]
    url = "file:///" + src.replace("\\", "/")

    tmp = os.path.join(folder, stem + ".tmp.pdf")
    run(["--no-pdf-header-footer", "--print-to-pdf=" + tmp, url])
    out = os.path.join(folder, stem + ".pdf")
    n = 2
    while True:
        try:
            os.replace(tmp, out)
            break
        except PermissionError:
            out = os.path.join(folder, f"{stem}-v{n}.pdf")
            n += 1
    print("PDF:", out)
    try:
        import pypdf
        print("pages:", len(pypdf.PdfReader(out).pages))
    except Exception as e:
        print("page count skipped:", e)

    png = os.path.join(folder, "preview.png")
    try:
        run(["--window-size=900,1200", "--screenshot=" + png, url])
        print("preview:", png)
    except Exception as e:
        print("preview skipped:", e)


if __name__ == "__main__":
    main()
