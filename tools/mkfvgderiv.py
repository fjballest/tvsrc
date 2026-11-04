#!/usr/bin/env python3
# Derive ICTblocks.txt and FVGS+IFVG+OTE4.txt from FVGS+IFVG+OTE.txt (FVGs+IFVG+OTE5).
#
#   python3 tools/mkfvgderiv.py            (from the tvsrc folder)
#
# Rerun it after every change to FVGS+IFVG+OTE.txt: the two files are generated, never edited by hand.
#
# Markers in FVGS+IFVG+OTE.txt (comments, ignored by TradingView):
#   //#cut T ... //#end   lines removed in derivative T: ote4 (FVGS+IFVG+OTE4), blk (ICTblocks), sw (both)
#   //#T code             line used only in derivative T (the "//#T " prefix is removed there)
# Inputs that a derivative does not show become constants with their default value (or the value given in
# its table below).
import re, sys, os

SRC = "FVGS+IFVG+OTE.txt"

# FVGS+IFVG+OTE4: everything but breakers, mitigation blocks, the Delivery Engine and swing-mode IFVGs
OTE4_KEEP = {"deConf", "deInv", "deMaxInv", "deDead", "deDeadCol"}  # IFVG confirmation and inversion rules
OTE4_DROP = re.compile(r"^(brk\w*|mb\w*|de\w*|ifvgswings|useSwingFilter|showSwing|lblAp)$")
OTE4_SET = {"brkOn": "false", "mbOn": "false", "lblAp": "false"}

# ICTblocks: breakers, mitigation blocks and the Delivery Engine (FVGs / IFVGs computed, not drawn)
BLK_KEEP = re.compile(r"^(brk\w*|mb\w*|deDisp|deDispType|deStdLen|deStdX|deDispBar|deDispCol|deConf|deInv|"
                      r"deMaxInv|plMode|minfvg|maxfvg)$")
BLK_SET = {"dofp": "false", "dowick": "false", "useOTE": "false", "oteFvg": "false", "showOTEband": "false",
           "showSignals": "false", "showOTEsig": "false", "doifvgalert": "false", "dofvgalert": "false",
           "dowickalert": "false", "fp1m": "false", "oteHtf1": "false", "oteHtf2": "false",
           "deAplus": "false", "deFlag": "false", "showSwing": "false", "ifvgswings": "false"}
BLK_SUBST = [  # (old, new): defaults of the inputs it keeps
    ('brkBox = input.bool(false, "Rectangle"', 'brkBox = input.bool(true, "Rectangle"'),
    ('mbBox = input.bool(false, "Rectangle"', 'mbBox = input.bool(true, "Rectangle"'),
    ('"Breakers at FVG / IFVG pullbacks"', '"Breakers"'),
    ('"Mitigation blocks at FVG / IFVG pullbacks"', '"Mitigation blocks"'),
    ('G_DE = "Delivery Engine (Nuno)"', 'G_DE = "Delivery Engine (Nuno): FVGs / IFVGs used"'),
]


def calls(s):
    """yield (start, end) of each input.xxx(...) call in s (balanced parentheses, strings skipped)"""
    for m in re.finditer(r"input\.(\w+)\(", s):
        i = m.end()
        depth = 1
        args = []
        a0 = i
        q = None
        while depth:
            c = s[i]
            if q:
                if c == "\\":
                    i += 1
                elif c == q:
                    q = None
            elif c in "\"'":
                q = c
            elif c in "([":
                depth += 1
            elif c in ")]":
                depth -= 1
            elif c == "," and depth == 1:
                args.append(s[a0:i])
                a0 = i + 1
            i += 1
        args.append(s[a0:i - 1])
        yield m.start(), i, m.group(1), args[0].strip()


def constant(stmt, value):
    """input statement -> constant statement (one line)"""
    out = ""
    last = 0
    for a, b, kind, dflt in calls(stmt):
        v = dflt if value is None else value
        if kind == "float" and value is None:
            v = "float(%s)" % v
        out += stmt[last:a] + v
        last = b
    return out + stmt[last:]


def derive(lines, tag, keep, setv, title, header, srctitle="FVGs+IFVG+OTE5"):
    # 1. cuts and derivative-only lines
    res = []
    stack = []  # cut regions can nest: a line is removed when any open region applies
    for ln in lines:
        cut = any(stack)
        m = re.match(r"^//#cut (\w+)\s*$", ln)
        if m:
            stack.append(m.group(1) in (tag, "sw"))
            continue
        if re.match(r"^//#end\s*$", ln):
            stack.pop()
            continue
        m = re.match(r"^//#(\w+) ?(.*)$", ln)
        if m and m.group(1) in ("ote4", "blk", "sw"):
            if (m.group(1) == tag or m.group(1) == "sw") and not cut:
                res.append(m.group(2))
            continue
        if not cut:
            res.append(ln)
    assert not stack, "unclosed cut"
    # 2. inputs: statements at column 0 with input.xxx( and their indented continuation lines
    out = []
    i = 0
    while i < len(res):
        ln = res[i]
        m = re.match(r"^(?:var\s+)?(?:\w+\s+)?(\w+)\s*=.*\binput\.\w+\(", ln)
        if not m:
            out.append(ln)
            i += 1
            continue
        j = i + 1
        while j < len(res) and res[j].startswith(" ") and res[j].strip():
            j += 1
        name = m.group(1)
        if keep(name) and name not in setv:
            out.extend(res[i:j])
        else:
            stmt = " ".join(x.strip() for x in res[i:j])
            out.append(constant(stmt, setv.get(name)))
        i = j
    txt = "\n".join(out)
    # 3. title and header
    a = 'indicator("%s",' % srctitle
    assert txt.count(a) == 1, a
    txt = txt.replace(a, 'indicator("%s",' % title, 1)
    k = txt.index("//@version=6")
    txt = header + txt[k:]
    return txt


def fold(txt):
    """both derivatives have deFlag and deAplus as constants false: drop bw() (always 1) and deAp() (always
    false), whose arguments would be unused"""
    txt, n = re.subn(r"(?m)^// \[DE\] border width[^\n]*\nmethod bw\(FVG f\) =>\n(?: [^\n]*\n)+", "", txt)
    assert n == 1, "bw"
    txt = txt.replace("f.bw()", "1")
    txt, n = re.subn(r"(?m)^(?:// A\+ for an IFVG[^\n]*\n)?deAp\(int d\) =>\n(?: [^\n]*\n)+", "", txt)
    assert n == 1, "deAp"
    txt = re.sub(r"\bdeAp\([^()]*\)", "false", txt)
    # A+ MSS: never true, so the MSS bars are never set (left in, the dead assignments confuse the compiler)
    txt = re.sub(r"(?m)^    if isMss and false\n(?:        [^\n]*\n)+", "", txt)
    txt = re.sub(r"f\.aplus := deAplus and not na\(deMss(?:Up|Dn)Bar\)[^\n]*", "f.aplus := false // [DE] A+ off", txt)
    txt = re.sub(r"(?m)^var int deMss(?:Up|Dn)Bar = na[^\n]*\n", "", txt)
    assert "bw(" not in txt and not re.search(r"\bdeAp\b|deMss(Up|Dn)Bar", txt)
    return txt


def main():
    os.chdir(os.path.dirname(os.path.abspath(__file__)) + "/..")
    src = open(SRC, newline="").read()
    assert "\r" not in src
    lines = src.split("\n")
    note = ("// GENERATED from FVGS+IFVG+OTE.txt by tools/mkfvgderiv.py: do not edit, change FVGS+IFVG+OTE.txt and\n"
            "// rerun the script.\n")
    lic = "\n".join(lines[:2]) + "\n"
    ote4 = derive(lines, "ote4", lambda n: n in OTE4_KEEP or not OTE4_DROP.match(n), OTE4_SET, "FVGs+IFVG+OTE4",
                  lic + note +
                  "// FVGs+IFVG+OTE4: the FVGs+IFVG+OTE5 indicator without the breakers, mitigation blocks and Delivery\n"
                  "// Engine (see ICTblocks; its IFVG confirmation and inversion rules are kept here) and without the\n"
                  "// swing-mode IFVGs (IFVGs are created on a close through the FVG).\n"
                  "// The rest (FVGs, 1PFVGs, wick C.E., filters, IFVGs, OTE, poor liquidity) is the same.\n\n")
    s = derive(lines, "blk", lambda n: bool(BLK_KEEP.match(n)), BLK_SET, "ICTblocks",
               lic + note +
               "// ICTblocks: the breakers, mitigation blocks and Delivery Engine of FVGs+IFVG+OTE5, alone. The FVGs and\n"
               "// close-mode IFVGs are computed as in FVGs+IFVG+OTE5 (they decide where the blocks are drawn) but not\n"
               "// drawn: use it next to FVGs+IFVG+OTE4. Delivery Engine: displacement filter of those FVGs, colored\n"
               "// displacement candles, IFVG confirmation and inversion rules.\n\n")
    ote4 = fold(ote4)
    s = fold(s)
    for a, b in BLK_SUBST:
        assert s.count(a) == 1, a
        s = s.replace(a, b)
    open("FVGS+IFVG+OTE4.txt", "w", newline="").write(ote4)
    open("ICTblocks.txt", "w", newline="").write(s)
    print("FVGS+IFVG+OTE4.txt", ote4.count("\n"), "lines; ICTblocks.txt", s.count("\n"), "lines")


if __name__ == "__main__":
    main()
