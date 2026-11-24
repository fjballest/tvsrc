# Updating the TradingView scripts from this repo

Claude in Chrome does this on the TradingView chart, with the user signed in.
The Pine Editor is a Monaco editor without a global `monaco`, so the source
goes in as a synthetic paste:

1. In the page, fetch the file from GitHub at the pushed commit:
   `https://raw.githubusercontent.com/fjballest/tvsrc/<commit>/<encodeURIComponent(file)>`,
   turn CRLF into LF.
2. Open the script (script-name menu, "Open script...", search the name, first row).
3. Click in the editor, cmd+a, then dispatch on the editor textarea a
   `ClipboardEvent("paste")` whose `clipboardData` holds the source.
4. If the status bar says "Unsaved version" the source changed: "Save script"
   from the same menu. Nothing to do otherwise.
5. Read the editor console: a save with an `Error at` line keeps the old
   compiled script on the chart, so fix it in the repo and repeat.

| TradingView script   | Repo file              |
|----------------------|------------------------|
| FVGs+IFVG+OTE5       | FVGS+IFVG+OTE.txt      |
| FVGs+IFVG+OTE5 PL    | FVGS+IFVG+OTE+PL.txt   |
| FVGs+IFVG+OTE4       | FVGS+IFVG+OTE4.txt     |
| ICTblocks            | ICTblocks.txt          |
| ICTcotas             | ICTcotas.txt           |
| OTEFibWick           | OTEFibWick.txt         |
| ICTlevels            | ICTlevels.txt          |
| ICTgaps              | ICTgaps.txt            |
| ICTday               | ICTday.txt             |
| NWOGs                | NWOGs.txt              |
| NDOGs                | NDOGs.txt              |
| RTHOGs               | RTHOGs.txt             |

Publishing an update is public: the release notes are shown to the user and
published only with the user's OK, as an update of the existing publication
without a new chart image.
