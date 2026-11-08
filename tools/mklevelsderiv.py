#!/usr/bin/env python3
# Derive ICTgaps.txt and ICTday.txt from ICTlevels.txt.
#
#   python3 tools/mklevelsderiv.py            (from the tvsrc folder)
#
# Rerun it after every change to ICTlevels.txt: the two files are generated, never edited by hand.
# Both keep all the ICTlevels code; the inputs of the features a derivative does not have become constants
# that turn those features off (other inputs it does not show become constants with their default value).
import re, os, sys
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from mkfvgderiv import derive

SRC = "ICTlevels.txt"

# inputs of the "Time & Settings" group: in both
COMMON = {"TZ", "visible", "xoff", "showcap", "attLvl", "badatr"}
# ICTgaps: NDOG / NWOG / RTHOG ("Gaps and Ranges") and the premarket range
GAPS = re.compile(r"^(dogaps|ngaps|dondog|donwog|dorthog\w*|rthog\w*|dogrect|grtransp|combgaps|combtol|"
                  r"gaplnsty|gapwid|gapcolor|wgap\w*|rgap\w*|lrgapcolor|extendgaps|"
                  r"dopremarket|pm\w*|dopmq|dopmpo3)$")
# feature switches of the other derivative's features
GAPS_OFF = ["dondog", "donwog", "dorthog", "dopremarket"]
# (dosmaxmin / doseslbl stay at their defaults in ICTgaps: they also draw the premarket high / low and labels,
# with the session line styles and keep count; sessions themselves are off with doses)
DAY_OFF = ["domonth", "dolines", "dodmaxmin", "dodaylbl", "dobody", "doreh", "doeq", "doopen", "dotgif", "doses",
           "dorect", "doeqhl", "doeqd", "dozones", "domacros", "domacrobg", "dosilver"]


def main():
    os.chdir(os.path.dirname(os.path.abspath(__file__)) + "/..")
    src = open(SRC, newline="").read()
    assert "\r" not in src
    lines = src.split("\n")
    lic = "\n".join(lines[:3]) + "\n"
    note = ("// GENERATED from ICTlevels.txt by tools/mklevelsderiv.py: do not edit, change ICTlevels.txt and rerun\n"
            "// the script.\n")
    gaps = derive(lines, "none", lambda n: n in COMMON or bool(GAPS.match(n)), {k: "false" for k in DAY_OFF},
                  "ICTgaps",
                  lic + note +
                  "// ICTgaps: the gaps and ranges of ICTlevels (NDOG / NWOG / RTHOG with quadrants, octants and\n"
                  "// stddevs, and the premarket range), nothing else. Use it next to ICTday.\n\n",
                  srctitle="ICTlevels")
    day = derive(lines, "none", lambda n: n in COMMON or not GAPS.match(n), {k: "false" for k in GAPS_OFF},
                 "ICTday",
                 lic + note +
                 "// ICTday: ICTlevels without the gaps and ranges (see ICTgaps): months, days (vertical lines,\n"
                 "// max / min, REH / REL, equilibrium, open), TGIF, sessions, EQH / EQL, macros and events.\n\n",
                 srctitle="ICTlevels")
    open("ICTgaps.txt", "w", newline="").write(gaps)
    open("ICTday.txt", "w", newline="").write(day)
    print("ICTgaps.txt", gaps.count("\n"), "lines; ICTday.txt", day.count("\n"), "lines")


if __name__ == "__main__":
    main()
