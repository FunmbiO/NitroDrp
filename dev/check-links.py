#!/usr/bin/env python3
"""
Checks every relative link and #anchor in the Markdown files, so a restructured README or docs folder can't
quietly break. External (http/https/mailto) links are not fetched.

    python3 dev/check-links.py              # whole repo
    python3 dev/check-links.py --self-test  # test the checker itself
"""
import os, re, subprocess, sys, tempfile

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), ".."))
LINK = re.compile(r"!?\[[^\]]*\]\(\s*<?([^)\s>]+)>?(?:\s+\"[^\"]*\")?\s*\)")
HEADING = re.compile(r"^(#{1,6})\s+(.*?)\s*#*\s*$")


def strip_code(text):
    """Remove fenced code blocks so example links inside them aren't checked."""
    return re.sub(r"^(```|~~~).*?^\1[^\n]*$", "", text, flags=re.S | re.M)


def slug(heading, seen):
    """GitHub's anchor for a heading: lowercase, drop punctuation, spaces to hyphens, -1/-2 for duplicates."""
    t = re.sub(r"!?\[([^\]]*)\]\([^)]*\)", r"\1", heading)  # [text](url) -> text
    t = re.sub(r"[`*_~]", "", t).strip().lower()
    t = re.sub(r"[^\w\- ]", "", t, flags=re.UNICODE).replace(" ", "-")
    n = seen.get(t, 0)
    seen[t] = n + 1
    return t if n == 0 else f"{t}-{n}"


def anchors(path):
    seen, out = {}, set()
    for line in strip_code(open(path, encoding="utf-8").read()).splitlines():
        m = HEADING.match(line)
        if m:
            out.add(slug(m.group(2), seen))
    return out


def markdown_files(root):
    try:
        files = subprocess.run(["git", "ls-files", "*.md"], cwd=root, capture_output=True, text=True, check=True).stdout.split()
        if files:
            return [os.path.join(root, f) for f in files]
    except (OSError, subprocess.CalledProcessError):
        pass
    found = []
    for d, dirs, names in os.walk(root):
        dirs[:] = [x for x in dirs if x not in {".git", "node_modules", "dist", "bin", "obj", ".terraform"}]
        found += [os.path.join(d, n) for n in names if n.endswith(".md")]
    return found


def check(root):
    problems, count = [], 0
    cache = {}
    for md in markdown_files(root):
        rel = os.path.relpath(md, root)
        for m in LINK.finditer(strip_code(open(md, encoding="utf-8").read())):
            target = m.group(1)
            if re.match(r"^(https?:|mailto:|tel:)", target):
                continue
            count += 1
            path, _, frag = target.partition("#")
            dest = md if not path else os.path.normpath(os.path.join(os.path.dirname(md), path))
            if not os.path.exists(dest):
                problems.append(f"{rel}: '{target}' -> missing file {os.path.relpath(dest, root)}")
            elif frag and dest.endswith(".md"):
                if dest not in cache:
                    cache[dest] = anchors(dest)
                if frag.lower() not in cache[dest]:
                    problems.append(f"{rel}: '{target}' -> no heading #{frag} in {os.path.relpath(dest, root)}")
    return problems, count


def self_test():
    with tempfile.TemporaryDirectory() as d:
        os.makedirs(os.path.join(d, "docs"))
        open(os.path.join(d, "docs", "b.md"), "w").write("# Title\n\n## Run it locally (no AWS account)\n\n## Dup\n\n## Dup\n")
        open(os.path.join(d, "README.md"), "w").write(
            "[ok](docs/b.md) [ok anchor](docs/b.md#run-it-locally-no-aws-account) [dup2](docs/b.md#dup-1) [self](#intro)\n"
            "[web](https://example.com/x#y) ![img](docs/b.md)\n\n## Intro\n\n```\n[inside code](nope.md)\n```\n"
        )
        problems, n = check(d)
        assert problems == [] and n == 5, (problems, n)
        open(os.path.join(d, "README.md"), "a").write("[bad file](docs/zzz.md) [bad anchor](docs/b.md#nope) [bad self](#nowhere)\n")
        problems, _ = check(d)
        assert len(problems) == 3, problems
    print("self-test passed")


if __name__ == "__main__":
    if "--self-test" in sys.argv:
        self_test()
        sys.exit(0)
    problems, count = check(ROOT)
    for p in problems:
        print("BROKEN", p)
    print(f"{count} relative links checked, {len(problems)} broken.")
    sys.exit(1 if problems else 0)
