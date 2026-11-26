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
3. Select each of the other five panes and load the ICTmany template (its
   shortcut button in the top toolbar, or the templates menu), so they all get
   the new versions. Then save the layout.

Do not click the template shortcuts in the top toolbar by mistake: they apply a
template to whichever pane is selected.

Publishing an update is public: the release notes are shown to the user and
published only with the user's OK, as an update of the existing publication
without a new chart image. With the script open in the editor: the publish
button, "Update existing script", pick the publication, tick "Don't update
the chart", write the notes, Continue, tick the House Rules box, "Publish new
version". TradingView only publishes a script that is on the selected chart:
if it says "Script is not on the chart", add it, publish, then remove that
copy ("Undo insert" in the top toolbar, or from the pane legend).

## ICTScalp

ICTScalp is not in the ICTmany template. In the ICT6win layout it is on the
m1, m3 and m5 panes, which use the "ICTmanyscalp" indicator template (ICTmany
plus ICTScalp). To update it:

1. Save the new source of ICTScalp in the Pine Editor (as above) and check
   the editor console: a save with errors still says "saved" but the copies
   on the charts stay on the old version.
2. On the m1 pane, update ICTScalp on the chart. Copies that came from a
   template are pinned to their version (the legend shows "ICTScalp · 44.0")
   and do not follow a save. Then: open its settings and record the inputs
   (the dialog's comboboxes and inputs, in order), remove it, select the m1
   pane, open ICTScalp in the editor (Recently used) and press "Add to chart".
   Compare the new copy's inputs with the recorded ones (a new input shifts
   the order) and set back any that differ.
3. With the m1 pane selected, save its indicators as the "ICTmanyscalp"
   template (templates menu, "Save indicator template...", replace).
4. Select the m5 pane and then the m3 pane and load the ICTmanyscalp template
   on each. Then save the layout.
5. Keep the strategy in sync: run `python3 tools/mkstrategy.py ICTScalp.txt
   ICTScalpStrategy.txt`, commit, and save ICTScalpStrategy.txt as the
   "ICTScalp strategy" script (Open script..., paste, save, check the console).

The "ICTScalp test" layout (one 1m chart) only needs the saved script; reset
its inputs to the defaults and turn on Show trades, Simulate trades, Results
table and Explore combinations when the defaults change.
