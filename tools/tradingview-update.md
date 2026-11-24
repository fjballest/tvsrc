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

Saving does not update the copies already on the charts. In the ICT6win layout
do it once, on the 15m pane, which has the full ICTmany set:

1. For each changed script, open its source from that pane's legend ("Source
   code" on the indicator), paste the new source, save, and press "Update on
   chart". It keeps the inputs.
2. With all of them updated, select the 15m pane and save its indicators as the
   "ICTmany" template (templates menu, "Save indicator template...", replace).
3. Select each of the other five panes and load the ICTmany template from the
   templates menu, so they all get the new versions. Then save the layout.

Do not click the template shortcuts in the top toolbar by mistake: they apply a
template to whichever pane is selected.

Publishing an update is public: the release notes are shown to the user and
published only with the user's OK, as an update of the existing publication
without a new chart image. With the script open in the editor: the publish
button, "Update existing script", pick the publication, tick "Don't update
the chart", write the notes, Continue, tick the House Rules box, "Publish new
version". TradingView only publishes a script that is on the selected chart:
if it says "Script is not on the chart", add it, publish, then remove that
copy from the pane legend.
