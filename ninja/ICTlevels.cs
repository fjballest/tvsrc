// ICTlevels for NinjaTrader 8 (NinjaScript indicator, C#)
//
// Day/week/month levels, Asia/London/NY session ranges (killzones), NDOG/NWOG/RTHOG gaps with
// quadrants and standard deviations, premarket range, EQH/EQL, PO3 lines, TGIF levels, macros,
// silver bullet and a session/macro status text. Draws levels only: no plots, no signals, no alerts.
//
// Port of the TradingView Pine Script v6 indicator ICTlevels.txt (md5 539d117057b21ca3312a0943138d8bef),
// © franjballest, Mozilla Public License 2.0 (https://mozilla.org/MPL/2.0/).
//
// Install:
//   1. Copy this file to Documents\NinjaTrader 8\bin\Custom\Indicators\ICTlevels.cs
//   2. Open NinjaTrader > New > NinjaScript Editor, open ICTlevels and press F5 to compile.
//      NinjaTrader appends the "#region NinjaScript generated code" factory block itself on compile;
//      it is intentionally not written here.
//   3. Add ICTlevels to a minute (or second) chart of 1 hour or lower. Load at least ~120 days of data
//      so the daily (ATR, daily swings) and monthly (month levels) series have enough history.
//
// Times: bar times (platform time zone, Core.Globals.GeneralOptions.TimeZoneInfo) are converted to
// New York ("Eastern Standard Time") for NQ / US and to Madrid ("Romance Standard Time") for DAX / EU,
// so the session logic does not depend on the platform time zone setting.
//
// Differences from the Pine version that could not be avoided:
//   - No 500 object budget in NT8: the day/session/gap caps are dropped, the keep counts are used as
//     entered and "show object cap warning" has no effect (no cap row in the status text).
//   - "compute for historic data use" (rightmost visible bar mode) has no NT8 equivalent and is ignored;
//     objects are always computed up to the last bar.
//   - Calculate is forced to OnBarClose (in State.Configure): the RTHOG hint before the open and all
//     right-edge updates move once per bar instead of on every tick.
//   - Only Minute and Second bars are supported (Pine needs a time-based chart too); other bar types
//     draw nothing. Pine bar-open times are mapped to NT8 bar timestamps (bar close times).
//   - The status table is drawn with Draw.TextFixed (top right, one text per row).
//   - Pine label sizes map to fonts (tiny 8, small 10, normal 12, large 15, huge 20); colors with
//     transparency use area opacity (100 - transparency).
//   - Monthly and daily data come from AddDataSeries (completed bars only), so they need enough loaded
//     days on the chart; month lines start at the first loaded bar when the month starts earlier.
//   - Pivots (ta.pivothigh / ta.pivotlow) use strict comparisons on both sides.

#region Using declarations
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Xml.Serialization;
using NinjaTrader.Cbi;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Chart;
using NinjaTrader.Gui.Tools;
using NinjaTrader.Data;
using NinjaTrader.NinjaScript;
using NinjaTrader.Core.FloatingPoint;
using NinjaTrader.NinjaScript.DrawingTools;
#endregion

// enums at global scope so the NinjaScript generated code can reference them
public enum ICTlevelsZoneMode
{
	Auto,
	US,
	EU
}

public enum ICTlevelsTextSize
{
	Tiny,
	Small,
	Normal,
	Large,
	Huge
}

public enum ICTlevelsPmMode
{
	Auto,
	Manual
}

namespace NinjaTrader.NinjaScript.Indicators
{
	public class ICTlevels : Indicator
	{
		#region Types

		private class Sty
		{
			public DashStyleHelper sty;
			public int wid;
			public Brush col;
			public Sty(DashStyleHelper s, int w, Brush c) { sty = s; wid = w; col = c; }
		}

		// a recurring time window, start and end in seconds of day (target time zone)
		private class ZI
		{
			public string name;
			public int ss;
			public int es;
			public Sty sty;
			public bool Inside(int t)
			{
				return ss < es ? (t >= ss && t < es) : (t >= ss || t < es);
			}
		}

		// a horizontal line with an optional text label at its right end (Pine mkLL)
		private class LL
		{
			public string tag;
			public DateTime x1;
			public DateTime x2;
			public DateTime? lx;    // label x when it differs from x2 (month lines)
			public double y;
			public Brush c;
			public DashStyleHelper sty;
			public int w;
			public string txt;
			public Brush tc;
			public SimpleFont font;
			public bool ray;
			public int yoff;
		}

		private class IZone
		{
			public int id;
			public string name;
			public string dstr;     // start date ("23O", "23Jul") in the target time zone
			public DateTime tstart;
			public double op, hi, lo;
			public DateTime hit, lot;
			public DateTime? tend, hidonet, lodonet, extendt, tclose;
			public double pwhi = double.NaN, pwlo = double.NaN;
			public int hmode, lmode;
			public bool isday, isweek, dead, drawrect, drawvlines, drawlbl;
			public bool drawmaxmin = true;
			public bool drawnOnce, dirty;
			public ZI zi;
			public Sty lnsty, maxsty, minsty;
			public List<LL> qlines = new List<LL>();     // premarket quadrants and hi/lo lines
			public List<LL> sdlines = new List<LL>();    // premarket stddevs, order +1, -1, +2, -2...
			public List<string> sdnames = new List<string>();
			public double xhi = double.NaN, xlo = double.NaN;
		}

		private class IGap
		{
			public int id;
			public string name;
			public string dstr;
			public DateTime tstart;
			public double hi, lo;
			public bool isw;
			public int n = 1, nd = 0;
			public bool show = true;
			public double uhi, ulo;
			public DateTime ut;
		}

		private class IRthog
		{
			public int id;
			public DateTime? tstart, tend;
			public double cl = double.NaN, op = double.NaN;
			public LL cline, mline, cline2, opline, uq, dq;
			public bool rect;
			public DateTime rx0;
			public List<LL> po3 = new List<LL>();
			public List<string> po3names = new List<string>();
			public double xhi = double.NaN, xlo = double.NaN;
			public bool isold;
			public List<LL> octs = new List<LL>();  // octant lines (latest RTHOG, until noon)
			public DateTime? noon;                  // noon of the RTHOG day
		}

		private class IPiv
		{
			public int id;
			public double p;
			public DateTime t;
			public bool eq;
		}

		#endregion

		#region Fields

		private static readonly Brush PineGreen = Rgb(76, 175, 80);
		private static readonly Brush PineRed = Rgb(242, 54, 69);
		private static readonly Brush PineBlue = Rgb(41, 98, 255);
		private static readonly Brush PineBlack = Rgb(54, 58, 69);
		private static readonly Brush PineOrange = Rgb(255, 152, 0);
		private static readonly Brush PineMaroon = Rgb(136, 14, 79);

		private const string TagPfx = "ICTlv_";

		private TimeZoneInfo platformTz, targetTz;
		private bool useUS, tfok, rectOn, preOn;
		private int tfsecs, rdx;

		private ZI market, premarket;
		private List<ZI> zinfos, macros, sbs;
		private int gapStartSec, gapEndSec;

		private List<IZone> days, zones, premarkets, actz, monz;
		private List<IGap> dgaps, wgaps;
		private IGap ndog;
		private List<IRthog> rthogs;
		private List<IPiv> hpv, lpv, hpvD, lpvD;
		private List<double> sh3p, sl3p;
		private List<DateTime> sh3t, sl3t;
		private LL[] mls;
		private HashSet<string> drawn;
		private int nextId;

		private Sty daysty, daymaxsty, dayminsty, sesmaxsty, sesminsty, gapsty, wgapsty;
		private SimpleFont fDay, fSes, fTg, fPm, fEq, fNormal, fTable;

		// per bar
		private DateTime barTz;
		private int tsec, tsecPrev;
		private DayOfWeek dow, dowPrev;
		private bool newDay, newWeek, mktEndsPrev;
		private DateTime winStart;
		private bool winSet;

		// daily series (completed bars)
		private ATR atrC, atrD;
		private double datr = double.NaN, dph = double.NaN, dpl = double.NaN;
		private DateTime dpt;
		private int dailySeq, dailySeqSeen;

		// TGIF
		private double twO = double.NaN, twH = double.NaN, twL = double.NaN;
		private int tgDir;
		private bool tgMon, tgHit;
		private double tgX = double.NaN, tgR = double.NaN, tgV1 = double.NaN, tgV2 = double.NaN;
		private DateTime tgT0;

		#endregion

		#region State

		protected override void OnStateChange()
		{
			if (State == State.SetDefaults)
			{
				Description = "ICT levels: day/week/month levels, sessions, NDOG/NWOG/RTHOG gaps, premarket range, EQH/EQL, TGIF, macros (port of ICTlevels Pine v6)";
				Name = "ICTlevels";
				Calculate = Calculate.OnBarClose;
				IsOverlay = true;
				DisplayInDataBox = false;
				DrawOnPricePanel = true;
				DrawHorizontalGridLines = true;
				DrawVerticalGridLines = true;
				PaintPriceMarkers = false;
				ScaleJustification = NinjaTrader.Gui.Chart.ScaleJustification.Right;
				IsSuspendedWhileInactive = true;

				ZoneMode = ICTlevelsZoneMode.Auto;
				ComputeVisible = false;
				Xoff = 2;
				ShowCap = true;
				DoMonth = true;
				MonMaxColor = PineGreen;
				MonMinColor = PineRed;
				MonCloseColor = PineBlack;
				MonStyle = DashStyleHelper.Solid;
				MonWidth = 3;
				NDays = 60;
				DoLines = true;
				DayLineStyle = DashStyleHelper.Dot;
				DayWidth = 2;
				DayColor = PineBlue;
				WeekLineStyle = DashStyleHelper.Dash;
				WeekWidth = 2;
				DoDayMaxMin = true;
				DayMaxColor = PineGreen;
				DayMinColor = PineRed;
				DayMaxMinStyle = DashStyleHelper.Solid;
				DayMaxMinWidth = 2;
				DoDayLabel = true;
				DayLabelSize = ICTlevelsTextSize.Tiny;
				DoReh = true;
				RelTol = 0.05;
				DoEq = true;
				EqStyle = DashStyleHelper.Dash;
				EqWidth = 1;
				EqColor = PineBlue;
				DoOpen = true;
				OpenStyle = DashStyleHelper.Dash;
				OpenWidth = 1;
				OpenColor = PineBlack;
				DoTgif = true;
				TgP1 = 0.2;
				TgP2 = 0.3;
				TgNear = 0.4;
				TgUpColor = PineGreen;
				TgDownColor = PineRed;
				TgStyle = DashStyleHelper.Dash;
				TgWidth = 2;
				TgLabelSize = ICTlevelsTextSize.Normal;
				DoSessions = true;
				NSessions = 20;
				DoRect = true;
				RectTransparency = 75;
				RectBorderOnly = false;
				NoLowRects = true;
				RectMinTf = 5;
				DoSesMaxMin = true;
				SesMaxColor = PineGreen;
				SesMinColor = PineRed;
				SesStyle = DashStyleHelper.Solid;
				SesWidth = 1;
				DoSesLabel = true;
				SesLabelSize = ICTlevelsTextSize.Tiny;
				AsiaColor = PineRed;
				LondonColor = PineGreen;
				NYColor = PineBlue;
				DoGaps = true;
				NGaps = 10;
				DoNdog = true;
				DoNwog = true;
				DoRthog = true;
				DoRthogQ = true;
				DoRthogOct = true;
				RthogQMin = 25.0;
				DoRthogPo3 = true;
				RthogPo3Min = 25.0;
				RthogPo3Num = 4;
				RthogAuto = false;
				DoGapRect = false;
				GapRectTransparency = 90;
				CombGaps = true;
				CombTol = 0.05;
				NdogStyle = DashStyleHelper.Solid;
				NdogWidth = 1;
				NdogColor = Rgb(195, 46, 125);
				NwogStyle = DashStyleHelper.Solid;
				NwogWidth = 2;
				NwogColor = PineMaroon;
				RthogStyle = DashStyleHelper.Solid;
				RthogWidth = 2;
				RthogOldColor = Rgb(23, 225, 218);
				RthogColor = Rgb(107, 23, 225);
				RthogQuadStyle = DashStyleHelper.Solid;
				RthogPo3Style = DashStyleHelper.Dot;
				ExtendGaps = false;
				DoPremarket = true;
				PmKeep = 1;
				PmMode = ICTlevelsPmMode.Auto;
				PmSession = "0700-0900";
				DoPmQ = true;
				PmQMin = 20.0;
				DoPmPo3 = true;
				PmPo3Min = 20.0;
				PmPo3Num = 4;
				PmAuto = false;
				PmColor = PineRed;
				PmWidth = 2;
				PmQuadStyle = DashStyleHelper.Solid;
				PmCeStyle = DashStyleHelper.Solid;
				PmPo3Style = DashStyleHelper.Dot;
				PmLabelSize = ICTlevelsTextSize.Normal;
				PmCTol = 0.05;
				DoEqhl = true;
				DoEqDaily = true;
				EqLen = 3;
				EqLenD = 3;
				EqTol = 0.10;
				EqKeep = 5;
				DoEq3 = true;
				Eq3Pts = 3.0;
				EqhColor = PineGreen;
				EqlColor = PineRed;
				EqhlStyle = DashStyleHelper.Solid;
				EqhlWidth = 2;
				EqLabelSize = ICTlevelsTextSize.Normal;
				DoZones = true;
				ZoneColor = PineBlue;
				DoMacros = true;
				MacroColor = PineOrange;
				DoMacroBg = false;
				MacroBgColor = Rgb(54, 58, 69, 25);
				DoSilver = true;
				SilverColor = PineRed;
			}
			else if (State == State.Configure)
			{
				// the logic works on completed bars only
				Calculate = Calculate.OnBarClose;
				// higher timeframe data, completed bars only (no repainting)
				AddDataSeries(BarsPeriodType.Day, 1);    // BarsArray[1]: daily ATR(14) and daily swings
				AddDataSeries(BarsPeriodType.Month, 1);  // BarsArray[2]: month high / low / close
			}
			else if (State == State.DataLoaded)
			{
				Setup();
			}
		}

		public override string DisplayName
		{
			get { return Name; }
		}

		private void Setup()
		{
			FreezeBrushes();

			platformTz = Core.Globals.GeneralOptions.TimeZoneInfo ?? TimeZoneInfo.Local;
			string tkr = Instrument != null && Instrument.MasterInstrument != null && Instrument.MasterInstrument.Name != null
				? Instrument.MasterInstrument.Name.ToUpperInvariant() : string.Empty;
			string dsc = Instrument != null && Instrument.MasterInstrument != null && Instrument.MasterInstrument.Description != null
				? Instrument.MasterInstrument.Description.ToUpperInvariant() : string.Empty;
			bool autoEU = dsc.Contains("DAX") || tkr.Contains("DAX") || dsc.Contains("GERMANY") || tkr.Contains("FDX");
			useUS = ZoneMode == ICTlevelsZoneMode.US || (ZoneMode == ICTlevelsZoneMode.Auto && !autoEU);
			targetTz = FindTz(useUS ? "Eastern Standard Time" : "Romance Standard Time");

			BarsPeriod bp = BarsArray[0].BarsPeriod;
			tfsecs = 0;
			if (bp.BarsPeriodType == BarsPeriodType.Minute)
				tfsecs = bp.Value * 60;
			else if (bp.BarsPeriodType == BarsPeriodType.Second)
				tfsecs = bp.Value;
			tfok = tfsecs > 0 && tfsecs <= 3600;
			rectOn = DoRect && !(NoLowRects && tfsecs < 900 && tfsecs <= RectMinTf * 60);
			preOn = DoPremarket && tfsecs <= 300;
			rdx = tfsecs <= 60 ? 4 : 2;

			fDay = MkFont(DayLabelSize, false);
			fSes = MkFont(SesLabelSize, false);
			fTg = MkFont(TgLabelSize, false);
			fPm = MkFont(PmLabelSize, false);
			fEq = MkFont(EqLabelSize, true);
			fNormal = MkFont(ICTlevelsTextSize.Normal, false);
			fTable = MkFont(ICTlevelsTextSize.Large, false);

			daysty = new Sty(DayLineStyle, DayWidth, DayColor);
			daymaxsty = new Sty(DayMaxMinStyle, DayMaxMinWidth, DayMaxColor);
			dayminsty = new Sty(DayMaxMinStyle, DayMaxMinWidth, DayMinColor);
			sesmaxsty = new Sty(SesStyle, SesWidth, SesMaxColor);
			sesminsty = new Sty(SesStyle, SesWidth, SesMinColor);
			gapsty = new Sty(NdogStyle, NdogWidth, NdogColor);
			wgapsty = new Sty(NwogStyle, NwogWidth, NwogColor);
			Sty nysty = new Sty(DayLineStyle, 1, NYColor);
			Sty londonsty = new Sty(DayLineStyle, 1, LondonColor);
			Sty asiasty = new Sty(DayLineStyle, 1, AsiaColor);

			string mktstr = useUS ? "0930-1615" : "0900-1730";
			string gapstr = useUS ? "1700-1800" : "2300-0000";
			string asiastr = useUS ? "2000-0000" : "2300-0600";
			string londonstr = useUS ? "0200-0500" : "0800-1300";
			string nyamstr = useUS ? "0700-1130" : "1330-1800";
			string nypmstr = useUS ? "1330-1600" : "1900-2200";
			string pmauto = useUS ? "0700-0900" : "0630-0830";
			string premarketstr = PmMode == ICTlevelsPmMode.Manual ? PmSession : pmauto;

			market = MkZI("market", mktstr, null);
			zinfos = new List<ZI>();
			foreach (ZI zi in new[] { MkZI("Asia", asiastr, asiasty), MkZI("London", londonstr, londonsty),
				MkZI("NY AM", nyamstr, nysty), MkZI("NY PM", nypmstr, nysty) })
				if (zi != null)
					zinfos.Add(zi);
			premarket = MkZI("Pre-mkt", premarketstr, nysty) ?? MkZI("Pre-mkt", pmauto, nysty);

			string[] mNames = useUS
				? new[] { "NYpre", "NYAM.1", "NYAM.2", "NYAM.3", "NY.lunch", "NYPM", "NYLast", "MOC", "LDN.1", "LDN.2" }
				: new[] { "Dax7", "Dax8", "DaxOpen", "Dax10", "Dax11", "DaxTapas", "Dax17", "NYopen", "Dax19", "DaxLunch" };
			string[] mTimes = useUS
				? new[] { "0740-0810", "0850-0910", "0950-1010", "1050-1110", "1150-1210", "1310-1340", "1515-1530", "1545-1600", "0230-0300", "0403-0430" }
				: new[] { "0700-0710", "0750-0810", "0850-0910", "0955-1010", "1050-1110", "1245-1310", "1650-1710", "1515-1540", "1850-1910", "1350-1410" };
			string[] sbTimes = useUS ? new[] { "1000-1100", "1400-1500" } : new[] { "1000-1100" };
			macros = new List<ZI>();
			for (int i = 0; i < mNames.Length; i++)
			{
				ZI m = MkZI(mNames[i], mTimes[i], null);
				if (m != null)
					macros.Add(m);
			}
			sbs = new List<ZI>();
			foreach (string s in sbTimes)
			{
				ZI m = MkZI("S.Bullet", s, null);
				if (m != null)
					sbs.Add(m);
			}

			gapStartSec = ParseSecs(gapstr.Substring(0, 4));
			gapEndSec = ParseSecs(gapstr.Substring(5, 4));

			days = new List<IZone>();
			zones = new List<IZone>();
			premarkets = new List<IZone>();
			actz = new List<IZone>();
			monz = new List<IZone>();
			dgaps = new List<IGap>();
			wgaps = new List<IGap>();
			ndog = null;
			rthogs = new List<IRthog>();
			hpv = new List<IPiv>();
			lpv = new List<IPiv>();
			hpvD = new List<IPiv>();
			lpvD = new List<IPiv>();
			sh3p = new List<double>();
			sl3p = new List<double>();
			sh3t = new List<DateTime>();
			sl3t = new List<DateTime>();
			mls = new LL[9];
			drawn = new HashSet<string>();
			nextId = 0;
			winSet = false;
			dailySeq = 0;
			dailySeqSeen = 0;
			datr = double.NaN;
			dph = double.NaN;
			dpl = double.NaN;
			twO = twH = twL = double.NaN;
			tgDir = 0;
			tgMon = tgHit = false;

			atrC = ATR(BarsArray[0], 14);
			atrD = ATR(BarsArray[1], 14);
		}

		private void FreezeBrushes()
		{
			MonMaxColor = Fz(MonMaxColor);
			MonMinColor = Fz(MonMinColor);
			MonCloseColor = Fz(MonCloseColor);
			DayColor = Fz(DayColor);
			DayMaxColor = Fz(DayMaxColor);
			DayMinColor = Fz(DayMinColor);
			EqColor = Fz(EqColor);
			OpenColor = Fz(OpenColor);
			TgUpColor = Fz(TgUpColor);
			TgDownColor = Fz(TgDownColor);
			SesMaxColor = Fz(SesMaxColor);
			SesMinColor = Fz(SesMinColor);
			AsiaColor = Fz(AsiaColor);
			LondonColor = Fz(LondonColor);
			NYColor = Fz(NYColor);
			NdogColor = Fz(NdogColor);
			NwogColor = Fz(NwogColor);
			RthogOldColor = Fz(RthogOldColor);
			RthogColor = Fz(RthogColor);
			PmColor = Fz(PmColor);
			EqhColor = Fz(EqhColor);
			EqlColor = Fz(EqlColor);
			ZoneColor = Fz(ZoneColor);
			MacroColor = Fz(MacroColor);
			MacroBgColor = Fz(MacroBgColor);
			SilverColor = Fz(SilverColor);
		}

		#endregion

		#region Bar update

		protected override void OnBarUpdate()
		{
			if (BarsInProgress == 1)
			{
				OnDailyBar();
				return;
			}
			if (BarsInProgress != 0 || !tfok)
				return;

			barTz = ToTz(Time[0].AddSeconds(-tfsecs));   // bar open time in the target time zone
			tsec = (int)barTz.TimeOfDay.TotalSeconds;
			dow = barTz.DayOfWeek;

			if (CurrentBar < 1)
			{
				tsecPrev = tsec;
				dowPrev = dow;
				mktEndsPrev = Ends(market);
				return;
			}

			if (!winSet)
			{
				// processing window: history is limited for speed (as in Pine)
				DateTime last = BarsArray[0].GetTime(BarsArray[0].Count - 1);
				winStart = last.AddDays(-(Math.Max(NDays, NGaps) + 5) * 1.5);
				winSet = true;
			}
			bool inWin = Time[0] >= winStart;
			bool finalBar = inWin && (State == State.Realtime || CurrentBar >= Count - 2);

			newDay = dow != dowPrev;
			newWeek = (dow == DayOfWeek.Sunday && dowPrev != DayOfWeek.Sunday)
				|| (dow == DayOfWeek.Monday && dowPrev == DayOfWeek.Friday);
			bool newDBar = dailySeq != dailySeqSeen;
			dailySeqSeen = dailySeq;

			// ---------------------------------------------------- days, sessions, premarket
			if (inWin)
			{
				if (newDay)
				{
					IZone d = MkZone("D", null);
					d.isday = true;
					d.isweek = newWeek;
					d.lnsty = daysty;
					d.maxsty = daymaxsty;
					d.minsty = dayminsty;
					d.drawmaxmin = DoDayMaxMin;
					d.drawlbl = DoDayLabel;
					if (days.Count > 0)
					{
						IZone ld = days[days.Count - 1];
						if (!d.isweek)
						{
							d.pwhi = double.IsNaN(ld.pwhi) ? ld.hi : Math.Max(ld.pwhi, ld.hi);
							d.pwlo = double.IsNaN(ld.pwlo) ? ld.lo : Math.Min(ld.pwlo, ld.lo);
						}
						EndZone(ld);
						if (days.Count > 1)
						{
							IZone pd = days[days.Count - 2];
							pd.extendt = ld.tend;
							pd.dirty = true;
						}
					}
					days.Add(d);
					actz.Add(d);
					if (days.Count > NDays)
					{
						KillZ(days[0]);
						days.RemoveAt(0);
					}
				}
				if (DoSessions)
				{
					foreach (ZI zi in zinfos)
					{
						if (!Starts(zi))
							continue;
						IZone s = MkZone(zi.name, zi);
						s.drawmaxmin = DoSesMaxMin;
						s.drawrect = rectOn;
						s.drawlbl = DoSesLabel;
						s.lnsty = zi.sty;
						s.maxsty = sesmaxsty;
						s.minsty = sesminsty;
						zones.Add(s);
						actz.Add(s);
						if (zones.Count > NSessions)
						{
							KillZ(zones[0]);
							zones.RemoveAt(0);
						}
					}
				}
				if (preOn && premarket != null && Starts(premarket))
				{
					IZone p = MkZone(premarket.name, premarket);
					p.drawmaxmin = DoSesMaxMin;
					p.drawvlines = true;
					// lines stop at the market close
					p.tclose = Time[0].AddSeconds((market.es - tsec + 86400) % 86400);
					p.drawlbl = DoSesLabel;
					p.lnsty = daysty;
					p.maxsty = sesmaxsty;
					p.minsty = sesminsty;
					premarkets.Add(p);
					actz.Add(p);
					if (premarkets.Count > NSessions)
					{
						KillZ(premarkets[0]);
						premarkets.RemoveAt(0);
					}
				}
				RunActive();
				RunMon();
			}

			// ---------------------------------------------------- TGIF (state only, drawn on the final bar)
			if (inWin && DoTgif)
			{
				if (newWeek)
				{
					int d = double.IsNaN(twO) ? 0 : TgEval(twH, twL, twO, Close[1]);
					bool carry = d != 0 && !(tgDir == d && tgHit);
					tgDir = 0;
					tgMon = false;
					tgHit = false;
					if (carry)
					{
						tgDir = d;
						tgMon = true;
						tgX = d == 1 ? twH : twL;
						tgR = twH - twL;
						tgT0 = Time[0];
					}
					twO = Open[0];
					twH = High[0];
					twL = Low[0];
				}
				else if (!double.IsNaN(twO))
				{
					if (newDay && dow == DayOfWeek.Friday)
					{
						tgDir = TgEval(twH, twL, twO, Close[1]);
						tgMon = false;
						tgHit = false;
						tgT0 = Time[0];
					}
					else if (newDay && tgDir != 0 && !(tgMon && dow == DayOfWeek.Monday))
					{
						// Friday or Monday is over
						tgDir = 0;
						tgMon = false;
					}
					twH = Math.Max(twH, High[0]);
					twL = Math.Min(twL, Low[0]);
				}
				if (tgDir != 0)
				{
					if (!tgMon && !tgHit)
					{
						tgX = tgDir == 1 ? twH : twL;
						tgR = twH - twL;
					}
					tgV1 = tgX - tgDir * TgP1 * tgR;
					tgV2 = tgX - tgDir * TgP2 * tgR;
					if (!tgHit && (tgDir == 1 ? Low[0] <= tgV1 : High[0] >= tgV1))
						tgHit = true;
				}
			}

			// ---------------------------------------------------- NDOG / NWOG
			bool gapOpenBar = tsec == gapStartSec;
			bool gapCloseBar = (tsec + tfsecs) % 86400 == gapStartSec;
			bool gapEndBar = tsec == gapEndSec;
			bool isWeekGap = dow == DayOfWeek.Friday || dow == DayOfWeek.Sunday;
			if (inWin && DoGaps)
			{
				if (gapOpenBar || gapCloseBar)
				{
					int si = gapOpenBar ? 1 : 0;
					if ((isWeekGap && DoNwog) || (!isWeekGap && DoNdog))
						ndog = MkGap(isWeekGap, Time[si], Close[si]);
				}
				else if (dow == DayOfWeek.Sunday && dowPrev == DayOfWeek.Friday && DoNwog)
					ndog = MkGap(true, Time[1], Close[1]);
				if (ndog != null && gapEndBar)
				{
					GEnd(ndog, Open[0]);
					List<IGap> lst = ndog.isw ? wgaps : dgaps;
					lst.Add(ndog);
					if (lst.Count > NGaps)
					{
						KillG(lst[0]);
						lst.RemoveAt(0);
					}
					ndog = null;
				}
			}

			// ---------------------------------------------------- RTHOG
			bool mktEnds = Ends(market);
			bool mktClose = mktEnds && !mktEndsPrev;
			int dOpen = (market.ss - tsec + 86400) % 86400;
			bool rthStarting = dOpen < tfsecs + 900;   // from 15 min before the open up to the open bar
			bool rthOpenBar = dOpen < tfsecs;          // bar containing the open
			bool rthOk = inWin && DoGaps && DoRthog && tfsecs <= 900 && Count - 1 - CurrentBar <= 5000
				&& dow != DayOfWeek.Sunday;
			if (rthOk)
			{
				if (mktClose)
				{
					IRthog rt = new IRthog { id = nextId++ };
					RStarts(rt, Close[1], Time[1]);
					if (rthogs.Count > 0)
					{
						IRthog pv = rthogs[rthogs.Count - 1];
						KillQ(pv);
						KillPo3(pv);
						KillOct(pv);
					}
					if (rthogs.Count > 1)
						SetOld(rthogs[rthogs.Count - 2]);
					rthogs.Add(rt);
					if (rthogs.Count > NGaps)
					{
						KillR(rthogs[0]);
						rthogs.RemoveAt(0);
					}
				}
				else if (rthStarting && rthogs.Count > 0)
				{
					IRthog cg = rthogs[rthogs.Count - 1];
					if (!double.IsNaN(cg.cl) && (finalBar || rthOpenBar))
						REnds(cg, rthOpenBar ? Open[0] : Close[0]);
				}
				// automatic stddevs: extremes of the day from the open until the close
				if (RthogAuto && !mktClose && rthogs.Count > 0)
				{
					IRthog ag = rthogs[rthogs.Count - 1];
					if (ag.tend.HasValue && Time[0] > ag.tend.Value)
					{
						ag.xhi = Math.Max(ag.xhi, High[0]);
						ag.xlo = Math.Min(ag.xlo, Low[0]);
					}
				}
			}
			// automatic stddevs: extremes of the latest premarket range from its end until the market close
			if (preOn && PmAuto && premarkets.Count > 0)
			{
				IZone az = premarkets[premarkets.Count - 1];
				if (az.tend.HasValue && Time[0] > az.tend.Value && (!az.tclose.HasValue || Time[0] < az.tclose.Value))
				{
					az.xhi = double.IsNaN(az.xhi) ? High[0] : Math.Max(az.xhi, High[0]);
					az.xlo = double.IsNaN(az.xlo) ? Low[0] : Math.Min(az.xlo, Low[0]);
				}
			}

			// ---------------------------------------------------- EQH / EQL
			if (inWin && DoEqhl)
			{
				double tol = EqTol * Nz(atrC[0]);
				SweepPivs(hpv, true, tol);
				SweepPivs(lpv, false, tol);
				Sweep3(sh3p, sh3t, true);
				Sweep3(sl3p, sl3t, false);
				if (CurrentBar >= 2 * EqLen)
				{
					DateTime pivT = Time[EqLen];
					double ph = PivotHigh(EqLen);
					double pl = PivotLow(EqLen);
					if (!double.IsNaN(ph))
					{
						AddPiv(hpv, ph, pivT, true, tol);
						if (DoEq3)
							Eq3(hpv, sh3p, sh3t, ph, pivT, true);
					}
					if (!double.IsNaN(pl))
					{
						AddPiv(lpv, pl, pivT, false, tol);
						if (DoEq3)
							Eq3(lpv, sl3p, sl3t, pl, pivT, false);
					}
				}
				if (CurrentBar >= 2)
				{
					double ph3 = PivotHigh(1);
					double pl3 = PivotLow(1);
					if (!double.IsNaN(ph3))
						Add3(sh3p, sh3t, ph3, Time[1]);
					if (!double.IsNaN(pl3))
						Add3(sl3p, sl3t, pl3, Time[1]);
				}
			}
			// daily swings: previous completed daily bar only
			if (inWin && DoEqhl && DoEqDaily)
			{
				double tolD = EqTol * Nz(datr);
				SweepPivs(hpvD, true, tolD);
				SweepPivs(lpvD, false, tolD);
				if (newDBar)
				{
					if (!double.IsNaN(dph))
						AddPiv(hpvD, dph, dpt, true, tolD);
					if (!double.IsNaN(dpl))
						AddPiv(lpvD, dpl, dpt, false, tolD);
				}
			}

			// ---------------------------------------------------- macros background
			if (DoMacroBg)
			{
				foreach (ZI m in macros)
				{
					if (m.Inside(tsec))
					{
						BackBrush = MacroBgColor;
						break;
					}
				}
			}

			if (finalBar)
				FinalDraw();

			tsecPrev = tsec;
			dowPrev = dow;
			mktEndsPrev = mktEnds;
		}

		// daily series: ATR(14) and the swing of the daily bar that just completed
		private void OnDailyBar()
		{
			if (CurrentBars[1] < 0)
				return;
			datr = atrD[0];
			dph = double.NaN;
			dpl = double.NaN;
			int L = EqLenD;
			if (CurrentBars[1] >= 2 * L + 1)
			{
				dph = PivotHigh(L);
				dpl = PivotLow(L);
				dpt = Time[L + 1];   // start of the pivot day (close of the day before)
			}
			dailySeq++;
		}

		#endregion

		#region Final bar drawing

		private void FinalDraw()
		{
			if (DoReh && DoDayLabel && DoDayMaxMin)
				MarkRel(days, RelTol * Nz(datr));
			RefreshList(days);
			RefreshList(zones);
			RefreshList(premarkets);
			if (preOn)
			{
				int npm = premarkets.Count;
				for (int i = 0; i < npm; i++)
				{
					if (i >= npm - PmKeep)
						PmDraw(premarkets[i], PmAuto && i == npm - 1);
					else
						PmClear(premarkets[i]);
				}
			}
			DrawTgif();
			double gtol = CombTol * Nz(datr);
			if (CombGaps)
				CombineGaps(dgaps, wgaps, gtol);
			else
			{
				foreach (IGap g in dgaps)
					GDraw(g, g.tstart, g.hi, g.lo, gtol);
				foreach (IGap g in wgaps)
					GDraw(g, g.tstart, g.hi, g.lo, gtol);
			}
			if (RthogAuto && DoRthogPo3 && rthogs.Count > 0)
			{
				IRthog lg = rthogs[rthogs.Count - 1];
				if (lg.tend.HasValue && !double.IsNaN(lg.op) && !double.IsNaN(lg.cl) && !lg.isold)
					RPo3(lg);
			}
			for (int i = 0; i < rthogs.Count; i++)
			{
				if (i == rthogs.Count - 1)
				{
					RRight(rthogs[i], RthogColor);
					OctRight(rthogs[i], RthogColor);    // octants: latest RTHOG only (removed at the close, like the quadrants)
				}
				else if (i == rthogs.Count - 2)
					RRight(rthogs[i], RthogOldColor);
			}
			if (preOn && rthogs.Count > 0 && premarkets.Count > 0)
				CombinePo3(rthogs[rthogs.Count - 1], premarkets[premarkets.Count - 1], PmCTol * Nz(datr));
			if (DoEqhl)
			{
				DrawEq(hpv, "EQH", EqhColor);
				DrawEq(lpv, "EQL", EqlColor);
				if (DoEqDaily)
				{
					DrawEq(hpvD, "EQH D", EqhColor);
					DrawEq(lpvD, "EQL D", EqlColor);
				}
			}
			if (DoMonth)
				DrawMonths();
			DrawTable();
		}

		// zones are drawn here only: the first time, when their state changed, and while they are live
		private void RefreshList(List<IZone> arr)
		{
			foreach (IZone z in arr)
			{
				if (!z.drawnOnce || z.dirty || Live(z))
				{
					ZRefresh(z);
					z.drawnOnce = true;
					z.dirty = false;
				}
			}
		}

		private void DrawTgif()
		{
			if (DoTgif && tgDir != 0 && !double.IsNaN(tgV1))
			{
				Brush c = tgDir == 1 ? TgDownColor : TgUpColor;
				DateTime x1 = TRight(Xoff);
				DLine(TagPfx + "tg1", tgT0, tgV1, x1, tgV1, c, TgStyle, TgWidth);
				DText(TagPfx + "tg1L", "TGIF" + (TgP1 * 100).ToString("0.#", CultureInfo.InvariantCulture), x1, tgV1, c, fTg, 0);
				DLine(TagPfx + "tg2", tgT0, tgV2, x1, tgV2, c, TgStyle, TgWidth);
				DText(TagPfx + "tg2L", "TGIF" + (TgP2 * 100).ToString("0.#", CultureInfo.InvariantCulture), x1, tgV2, c, fTg, 0);
			}
			else
			{
				Rm(TagPfx + "tg1");
				Rm(TagPfx + "tg1L");
				Rm(TagPfx + "tg2");
				Rm(TagPfx + "tg2L");
			}
		}

		private void DrawMonths()
		{
			if (BarsArray.Length < 3 || CurrentBars[2] < 0)
				return;
			DateTime first = BarsArray[0].GetTime(0);
			for (int k = 0; k < 3; k++)
			{
				if (CurrentBars[2] < k)
					break;
				DateTime mt = Times[2][k];
				DateTime ms = new DateTime(mt.Year, mt.Month, 1);
				DateTime tc = ms.AddMonths(1);
				string suf = " " + ms.ToString("MMM", CultureInfo.InvariantCulture);
				GetM(k * 3, ms, Highs[2][k], MonMaxColor, "max" + suf, first);
				GetM(k * 3 + 1, ms, Lows[2][k], MonMinColor, "min" + suf, first);
				GetM(k * 3 + 2, tc, Closes[2][k], MonCloseColor, "close" + suf, first);
			}
		}

		private void GetM(int idx, DateTime t, double p, Brush c, string txt, DateTime first)
		{
			DateTime t1 = t < first ? first : t;
			LL m = mls[idx];
			if (m == null)
			{
				m = new LL { tag = TagPfx + "m" + idx };
				mls[idx] = m;
			}
			m.x1 = t1;
			m.x2 = t1.AddSeconds(tfsecs);
			m.lx = TRight(Xoff);
			m.y = p;
			m.c = c;
			m.tc = c;
			m.sty = MonStyle;
			m.w = MonWidth;
			m.txt = txt;
			m.font = fNormal;
			m.ray = true;
			m.yoff = 8;     // Pine puts the text above the line ("\n")
			DrawLL(m);
		}

		private void DrawTable()
		{
			string sname = string.Empty;
			if (DoZones)
				foreach (ZI zi in zinfos)
					if (zi.Inside(tsec)) { sname = zi.name; break; }
			string mname = string.Empty;
			if (DoMacros)
				foreach (ZI m in macros)
					if (m.Inside(tsec)) { mname = m.name; break; }
			string bname = string.Empty;
			if (DoSilver)
				foreach (ZI m in sbs)
					if (m.Inside(tsec)) { bname = m.name; break; }
			DFixed(TagPfx + "tb0", sname, ZoneColor);
			DFixed(TagPfx + "tb1", mname.Length > 0 ? "\n" + mname : string.Empty, MacroColor);
			DFixed(TagPfx + "tb2", bname.Length > 0 ? "\n\n" + bname : string.Empty, SilverColor);
		}

		#endregion

		#region Zones

		private IZone MkZone(string name, ZI zi)
		{
			return new IZone
			{
				id = nextId++,
				name = name,
				dstr = DateStr(),
				tstart = Time[0],
				op = Open[0],
				hi = High[0],
				lo = Low[0],
				hit = Time[0],
				lot = Time[0],
				zi = zi
			};
		}

		private string ZTag(IZone z)
		{
			return TagPfx + "z" + z.id + "_";
		}

		private void KillZ(IZone z)
		{
			z.dead = true;
			PmClear(z);
			string tg = ZTag(z);
			foreach (string s in new[] { "v", "v2", "hi", "lo", "hiL", "loL", "eq", "eqL", "op", "opL", "r" })
				Rm(tg + s);
		}

		// create or update every object of the zone from its current state
		private void ZRefresh(IZone z)
		{
			string tg = ZTag(z);
			DateTime re = TRight(2);
			if (z.tclose.HasValue && z.tclose.Value < re)
				re = z.tclose.Value;
			DateTime xe = z.tend ?? re;
			if (z.drawvlines || (z.isday && DoLines))
			{
				if (!drawn.Contains(tg + "v"))
					DVLine(tg + "v", z.tstart, z.lnsty.col, z.isweek ? WeekLineStyle : z.lnsty.sty,
						z.isweek ? WeekWidth : z.lnsty.wid);
				if (z.drawvlines && z.tend.HasValue && !drawn.Contains(tg + "v2"))
					DVLine(tg + "v2", z.tend.Value.AddSeconds(tfsecs), z.lnsty.col, z.lnsty.sty, z.lnsty.wid);
			}
			if (z.drawmaxmin)
			{
				DateTime hx = z.hidonet.HasValue ? Mn(z.hidonet.Value, re) : re;
				DateTime lx = z.lodonet.HasValue ? Mn(z.lodonet.Value, re) : re;
				DLine(tg + "hi", z.hit, z.hi, hx, z.hi, z.maxsty.col, z.maxsty.sty, z.maxsty.wid);
				DLine(tg + "lo", z.lot, z.lo, lx, z.lo, z.minsty.col, z.minsty.sty, z.minsty.wid);
				if (z.drawlbl)
				{
					SimpleFont f = z.isday ? fDay : fSes;
					string hn = z.name + " hi";
					string ln = z.name + " lo";
					if (z.isday)
					{
						hn = double.IsNaN(z.pwhi) || z.hi >= z.pwhi ? "week high" : "day high";
						ln = double.IsNaN(z.pwlo) || z.lo <= z.pwlo ? "week low" : "day low";
						if (z.hmode == 1)
							hn = "day REHs";
						if (z.lmode == 1)
							ln = "day RELs";
					}
					TinyLbl(tg + "hiL", !z.hidonet.HasValue && z.hmode != 2, re, z.hi, hn, z.maxsty.col, f);
					TinyLbl(tg + "loL", !z.lodonet.HasValue && z.lmode != 2, re, z.lo, ln, z.minsty.col, f);
				}
			}
			if (z.isday && DoEq)
			{
				double eq = (z.hi + z.lo) / 2.0;
				DateTime ex = z.extendt ?? re;
				DateTime x0 = Mn(z.hit, z.lot);
				DLine(tg + "eq", x0, eq, ex, eq, EqColor, EqStyle, EqWidth);
				DText(tg + "eqL", "eq", ex, eq, EqColor, fNormal, 0);
			}
			if (z.isday && DoOpen)
			{
				DLine(tg + "op", z.tstart, z.op, xe, z.op, OpenColor, OpenStyle, OpenWidth);
				DText(tg + "opL", "M.O.", xe, z.op, OpenColor, fNormal, 0);
			}
			if (z.drawrect && z.lnsty != null)
				DRect(tg + "r", z.tstart, z.hi, xe, z.lo, z.lnsty.col, 100 - RectTransparency, RectBorderOnly);
		}

		private void TinyLbl(string tag, bool show, DateTime x, double y, string txt, Brush c, SimpleFont f)
		{
			if (show)
				DText(tag, txt, x, y, c, f, 0);
			else
				Rm(tag);
		}

		// needs right-edge updates on the final bar
		private bool Live(IZone z)
		{
			return !z.tend.HasValue || (z.drawmaxmin && (!z.hidonet.HasValue || !z.lodonet.HasValue))
				|| (z.isday && !z.extendt.HasValue);
		}

		// only state, no drawing: cheap per bar
		private void Upd(IZone z)
		{
			if (High[0] >= z.hi)
			{
				z.hit = Time[0];
				z.hi = High[0];
			}
			if (Low[0] <= z.lo)
			{
				z.lot = Time[0];
				z.lo = Low[0];
			}
		}

		private void EndZone(IZone z)
		{
			if (!z.tend.HasValue)
			{
				z.tend = Time[1];
				z.dirty = true;
				if (z.drawmaxmin)
					monz.Add(z);
			}
		}

		private void RunActive()
		{
			for (int i = actz.Count - 1; i >= 0; i--)
			{
				IZone z = actz[i];
				if (z.dead || z.tend.HasValue)
					actz.RemoveAt(i);
				else if (z.zi != null && Ends(z.zi))
				{
					EndZone(z);
					actz.RemoveAt(i);
				}
				else
					Upd(z);
			}
		}

		// ended zones: only watch for the max/min being taken
		private void RunMon()
		{
			for (int i = monz.Count - 1; i >= 0; i--)
			{
				IZone z = monz[i];
				if (z.dead)
				{
					monz.RemoveAt(i);
					continue;
				}
				if (!z.hidonet.HasValue && High[0] > z.hi)
				{
					z.hidonet = Time[0];
					z.dirty = true;
				}
				if (!z.lodonet.HasValue && Low[0] < z.lo)
				{
					z.lodonet = Time[0];
					z.dirty = true;
				}
				if (z.hidonet.HasValue && z.lodonet.HasValue)
					monz.RemoveAt(i);
			}
		}

		// unbroken day highs (lows) close to each other: only the higher high (lower low) keeps a
		// label, which reads REHs (RELs)
		private void MarkRel(List<IZone> arr, double tol)
		{
			int n = arr.Count;
			for (int i = 0; i < n; i++)
			{
				IZone a = arr[i];
				a.hmode = 0;
				a.lmode = 0;
				for (int j = 0; j < n; j++)
				{
					if (j == i)
						continue;
					IZone b = arr[j];
					if (!a.hidonet.HasValue && !b.hidonet.HasValue && Math.Abs(a.hi - b.hi) <= tol)
					{
						if (b.hi > a.hi || (b.hi == a.hi && j > i))
							a.hmode = 2;
						else if (a.hmode == 0)
							a.hmode = 1;
					}
					if (!a.lodonet.HasValue && !b.lodonet.HasValue && Math.Abs(a.lo - b.lo) <= tol)
					{
						if (b.lo < a.lo || (b.lo == a.lo && j > i))
							a.lmode = 2;
						else if (a.lmode == 0)
							a.lmode = 1;
					}
				}
			}
		}

		// stddevs to draw on one side of a range: at least n; with auto, also every stddev reached by the
		// extreme ext (base: the range edge on that side, u: the step, sgn 1 above, -1 below) plus the next one
		// stddev name: distance from the range edge in whole ranges (step i of u, range r), for example "+0.25", "-1.5"
		private static string SdName(int i, double u, double r, int sgn)
		{
			return (sgn > 0 ? "+" : "-") + Math.Round(i * u / r, 3).ToString("0.###", CultureInfo.InvariantCulture);
		}

		private static int SdCount(int n, bool auto, double b, double u, double ext, int sgn)
		{
			int r = n;
			if (auto && !double.IsNaN(ext) && u > 0)
			{
				int reached = Math.Max(0, (int)Math.Floor((ext - b) * sgn / u));
				r = Math.Max(n, Math.Min(reached + 1, 40));
			}
			return r;
		}

		// step of the stddevs: octants, quadrants, half, whole range or none
		private static double StdUnit(double r, double min)
		{
			if (r / 8.0 > min)
				return r / 8.0;
			if (r / 4.0 > min)
				return r / 4.0;
			if (r / 2.0 > min)
				return r / 2.0;
			if (r > min)
				return r;
			return 0.0;
		}

		private void PmClear(IZone z)
		{
			foreach (LL l in z.qlines)
				RemoveLL(l);
			z.qlines.Clear();
			foreach (LL l in z.sdlines)
				RemoveLL(l);
			z.sdlines.Clear();
			z.sdnames.Clear();
		}

		// premarket quadrants (25%, CE, 75%), stddevs and the hi/lo lines
		private void PmDraw(IZone z, bool auto)
		{
			List<LL> oldQ = z.qlines;
			List<LL> oldS = z.sdlines;
			z.qlines = new List<LL>();
			z.sdlines = new List<LL>();
			z.sdnames.Clear();
			string tg = ZTag(z);
			double r = z.hi - z.lo;
			DateTime x1 = TRight(Xoff);
			if (z.tclose.HasValue && z.tclose.Value < x1)
				x1 = z.tclose.Value;
			DateTime x0 = (z.tend ?? Time[0]).AddSeconds(-(double)tfsecs * rdx);
			string nm = z.dstr;
			if (r > 0)
			{
				if (DoPmQ && r / 4.0 > PmQMin)
				{
					for (int k = 1; k <= 3; k++)
					{
						string pt = k == 1 ? "dq" : k == 2 ? "ce" : "uq";
						z.qlines.Add(MkLL(tg + "q" + k, x0, x1, z.lo + k * r / 4.0, PmColor, k == 2 ? PmCeStyle : PmQuadStyle,
							PmWidth, "PMKT " + pt + " " + nm, PmColor, fPm, false));
					}
				}
				if (DoPmPo3)
				{
					double u = StdUnit(r, PmPo3Min);
					if (u > 0)
					{
						int nu = SdCount(PmPo3Num, auto, z.hi, u, z.xhi, 1);
						int nd = SdCount(PmPo3Num, auto, z.lo, u, z.xlo, -1);
						int mx = Math.Max(nu, nd);
						for (int i = 1; i <= mx; i++)
						{
							if (i <= nu)
							{
								z.sdlines.Add(MkLL(tg + "su" + i, x0, x1, z.hi + i * u, PmColor, PmPo3Style, PmWidth,
									"PMKT" + SdName(i, u, r, 1), PmColor, fPm, false));
								z.sdnames.Add(SdName(i, u, r, 1));
							}
							if (i <= nd)
							{
								z.sdlines.Add(MkLL(tg + "sd" + i, x0, x1, z.lo - i * u, PmColor, PmPo3Style, PmWidth,
									"PMKT" + SdName(i, u, r, -1), PmColor, fPm, false));
								z.sdnames.Add(SdName(i, u, r, -1));
							}
						}
					}
				}
				// high and low of the range, solid lines, whenever quadrants or stddevs are drawn
				if (z.qlines.Count > 0 || z.sdlines.Count > 0)
				{
					z.qlines.Add(MkLL(tg + "qh", x0, x1, z.hi, PmColor, DashStyleHelper.Solid, PmWidth, "PMKT hi " + nm, PmColor, fPm, false));
					z.qlines.Add(MkLL(tg + "ql", x0, x1, z.lo, PmColor, DashStyleHelper.Solid, PmWidth, "PMKT lo " + nm, PmColor, fPm, false));
				}
			}
			RemoveStale(oldQ, z.qlines);
			RemoveStale(oldS, z.sdlines);
		}

		#endregion

		#region TGIF

		private int TgEval(double h, double l, double o, double c)
		{
			double r = h - l;
			int d = 0;
			if (r > 0)
			{
				if (h - c <= TgNear * r)
					d = 1;
				else if (c - l <= TgNear * r)
					d = -1;
			}
			return d;
		}

		#endregion

		#region NDOG / NWOG

		private IGap MkGap(bool isw, DateTime t, double c)
		{
			string ds = DateStr();
			return new IGap
			{
				id = nextId++,
				isw = isw,
				dstr = ds,
				name = (isw ? "NWOG " : "NDOG ") + ds,
				tstart = t,
				hi = c,
				lo = double.NaN
			};
		}

		private string GTag(IGap g)
		{
			return TagPfx + "g" + g.id + "_";
		}

		// closes the gap with the open price, orders the edges and names it
		private void GEnd(IGap g, double xe)
		{
			double s = g.hi;
			g.hi = Math.Max(s, xe);
			g.lo = Math.Min(s, xe);
			g.dstr = DateStr();
			g.name = (g.isw ? "NWOG " : "NDOG ") + g.dstr;
			g.uhi = g.hi;
			g.ulo = g.lo;
			g.ut = g.tstart;
		}

		// text of the gap without the date: plain, or combined (NDOGx3, NWOGx2, NWOG+NDOG)
		private string GName(IGap g)
		{
			string b = g.isw ? "NWOG" : "NDOG";
			if (g.isw && g.nd > 0)
				b = (g.n > 1 ? "NWOGx" + g.n : "NWOG") + "+NDOG" + (g.nd > 1 ? "x" + g.nd : string.Empty);
			else if (g.n > 1)
				b = (g.isw ? "NWOGx" : "NDOGx") + g.n;
			return b;
		}

		// lines, labels and optional rectangle of a gap at levels h / l from t0. Two labels "NDOGx3 hi 23O" /
		// "NDOGx3 lo 23O", or a single one in the middle ("NDOGx3 23O") when the levels are within tol of each other
		private void GDraw(IGap g, DateTime t0, double h, double l, double tol)
		{
			Sty st = g.isw ? wgapsty : gapsty;
			string tg = GTag(g);
			DateTime x = TRight(Xoff);
			DLine(tg + "h", t0, h, x, h, st.col, st.sty, st.wid);
			DLine(tg + "l", t0, l, x, l, st.col, st.sty, st.wid);
			bool small = Math.Abs(h - l) <= tol;
			string b = GName(g);
			string dt = " " + g.dstr;
			DText(tg + "hL", small ? b + dt : b + " hi" + dt, x, small ? (h + l) / 2.0 : h, st.col, fNormal, 0);
			if (small)
				Rm(tg + "lL");
			else
				DText(tg + "lL", b + " lo" + dt, x, l, st.col, fNormal, 0);
			if (DoGapRect)
				DRect(tg + "r", t0, h, x, l, st.col, 100 - GapRectTransparency);
		}

		private void KillG(IGap g)
		{
			string tg = GTag(g);
			foreach (string s in new[] { "h", "l", "hL", "lL", "r" })
				Rm(tg + s);
		}

		// gaps whose ranges overlap or are within tol are one group: the oldest draws the outer levels of
		// the group, the others are hidden
		private void GCluster(List<IGap> arr, double tol)
		{
			int n = arr.Count;
			if (n == 0)
				return;
			int[] cid = new int[n];
			for (int i = 0; i < n; i++)
			{
				IGap g = arr[i];
				g.n = 1;
				g.nd = 0;
				g.show = true;
				g.uhi = g.hi;
				g.ulo = g.lo;
				g.ut = g.tstart;
				cid[i] = i;
			}
			bool ch = n > 1;
			while (ch)
			{
				ch = false;
				for (int i = 0; i < n - 1; i++)
				{
					for (int j = i + 1; j < n; j++)
					{
						if (cid[i] == cid[j])
							continue;
						IGap a = arr[i];
						IGap b = arr[j];
						if (a.hi + tol >= b.lo && b.hi + tol >= a.lo)
						{
							int from = cid[j];
							int xto = cid[i];
							for (int k = 0; k < n; k++)
								if (cid[k] == from)
									cid[k] = xto;
							ch = true;
						}
					}
				}
			}
			int[] first = Enumerable.Repeat(-1, n).ToArray();
			for (int i = 0; i < n; i++)
			{
				int c = cid[i];
				int r = first[c];
				if (r < 0)
					first[c] = i;
				else
				{
					IGap rep = arr[r];
					IGap g = arr[i];
					rep.n += 1;
					rep.uhi = Math.Max(rep.uhi, g.hi);
					rep.ulo = Math.Min(rep.ulo, g.lo);
					g.show = false;
				}
			}
		}

		private void CombineGaps(List<IGap> dg, List<IGap> wg, double tol)
		{
			GCluster(dg, tol);
			GCluster(wg, tol);
			foreach (IGap w in wg)
			{
				if (!w.show)
					continue;
				foreach (IGap d in dg)
				{
					if (d.show && Math.Abs(d.uhi - w.uhi) <= tol && Math.Abs(d.ulo - w.ulo) <= tol)
					{
						w.nd += d.n;
						d.show = false;
					}
				}
			}
			foreach (IGap g in dg)
				GComb(g, tol);
			foreach (IGap g in wg)
				GComb(g, tol);
		}

		private void GComb(IGap g, double tol)
		{
			if (!g.show)
				KillG(g);
			else
				GDraw(g, g.ut, g.uhi, g.ulo, tol);
		}

		#endregion

		#region RTHOG

		private string RTag(IRthog g)
		{
			return TagPfx + "r" + g.id + "_";
		}

		private void KillQ(IRthog g)
		{
			RemoveLL(g.uq);
			RemoveLL(g.dq);
			g.uq = null;
			g.dq = null;
		}

		private void KillPo3(IRthog g)
		{
			foreach (LL l in g.po3)
				RemoveLL(l);
			g.po3.Clear();
		}

		private void KillOct(IRthog g)
		{
			foreach (LL l in g.octs)
				RemoveLL(l);
			g.octs.Clear();
		}

		// octants: 1/8, 3/8, 5/8, 7/8 of the gap, from the open to noon (or the right edge before noon)
		private void ROct(IRthog g)
		{
			KillOct(g);
			if (!g.tend.HasValue)
				return;
			double ody = Math.Abs(g.op - g.cl) / 8.0;
			DateTime te = g.tend.Value;
			DateTime teTz = ToTz(te);
			g.noon = te + (teTz.Date.AddHours(12) - teTz);
			DateTime x0 = te.AddSeconds(-(double)tfsecs * rdx);
			DateTime x1 = Mn(TRight(Xoff), g.noon.Value);
			if (ody > RthogQMin && x1 > x0)
			{
				string tg = RTag(g);
				double p = Math.Min(g.cl, g.op);
				string nm = DateStr();
				foreach (int k in new[] { 1, 3, 5, 7 })
					g.octs.Add(MkLL(tg + "oc" + k, x0, x1, p + k * ody, RthogColor, RthogQuadStyle, 1,
						"RTHOG " + k + "/8 " + nm, RthogColor, fNormal, false));
			}
		}

		// final bar: the octants follow the right edge until noon
		private void OctRight(IRthog g, Brush c)
		{
			if (g.octs.Count == 0 || !g.noon.HasValue)
				return;
			DateTime x = Mn(TRight(Xoff), g.noon.Value);
			foreach (LL l in g.octs)
				SetR(l, x, c);
		}

		private void KillR(IRthog g)
		{
			KillOct(g);
			RemoveLL(g.cline);
			RemoveLL(g.mline);
			RemoveLL(g.cline2);
			RemoveLL(g.opline);
			g.cline = null;
			g.mline = null;
			g.cline2 = null;
			g.opline = null;
			Rm(RTag(g) + "rect");
			g.rect = false;
			KillQ(g);
			KillPo3(g);
		}

		// market close: settlement price line (value and bar time of the last bar of the session)
		private void RStarts(IRthog g, double c, DateTime t)
		{
			g.cl = c;
			g.tstart = t;
			g.cline = MkLL(RTag(g) + "cl", t, TRight(Xoff), c, RthogColor, RthogStyle, RthogWidth,
				"new RTHOG settlement", RthogColor, fNormal, false);
		}

		private void RQuads(IRthog g)
		{
			double qdy = Math.Abs(g.op - g.cl) / 4.0;
			if (qdy > RthogQMin && g.tend.HasValue)
			{
				string tg = RTag(g);
				double p = Math.Min(g.cl, g.op);
				DateTime x0 = g.tend.Value.AddSeconds(-(double)tfsecs * rdx);
				DateTime x1 = TRight(Xoff);
				string nm = DateStr();
				g.dq = MkLL(tg + "dq", x0, x1, p + qdy, RthogColor, RthogQuadStyle, RthogWidth, "RTHOG dq " + nm, RthogColor, fNormal, false);
				g.uq = MkLL(tg + "uq", x0, x1, p + 3 * qdy, RthogColor, RthogQuadStyle, RthogWidth, "RTHOG uq " + nm, RthogColor, fNormal, false);
			}
			else
				KillQ(g);
		}

		private void RPo3(IRthog g)
		{
			List<LL> old = g.po3;
			g.po3 = new List<LL>();
			g.po3names.Clear();
			double gap = Math.Abs(g.op - g.cl);
			double u = StdUnit(gap, RthogPo3Min);
			if (u > 0 && g.tend.HasValue)
			{
				string tg = RTag(g);
				DateTime x0 = g.tend.Value.AddSeconds(-(double)tfsecs * rdx);
				DateTime x1 = TRight(Xoff);
				double pu0 = Math.Max(g.cl, g.op);
				double pd0 = Math.Min(g.cl, g.op);
				int nu = SdCount(RthogPo3Num, RthogAuto, pu0, u, g.xhi, 1);
				int nd = SdCount(RthogPo3Num, RthogAuto, pd0, u, g.xlo, -1);
				int mx = Math.Max(nu, nd);
				for (int i = 1; i <= mx; i++)
				{
					if (i <= nu)
					{
						g.po3.Add(MkLL(tg + "su" + i, x0, x1, pu0 + i * u, RthogColor, RthogPo3Style, RthogWidth,
							"RTHOG" + SdName(i, u, gap, 1), RthogColor, fNormal, false));
						g.po3names.Add(SdName(i, u, gap, 1));
					}
					if (i <= nd)
					{
						g.po3.Add(MkLL(tg + "sd" + i, x0, x1, pd0 - i * u, RthogColor, RthogPo3Style, RthogWidth,
							"RTHOG" + SdName(i, u, gap, -1), RthogColor, fNormal, false));
						g.po3names.Add(SdName(i, u, gap, -1));
					}
				}
			}
			RemoveStale(old, g.po3);
		}

		// opening gap: called from 15 minutes before the open (hint, price = current close)
		// and on the open bar (final, price = open)
		private void REnds(IRthog g, double opv)
		{
			g.op = opv;
			g.tend = Time[0];
			g.xhi = High[0];
			g.xlo = Low[0];
			string tg = RTag(g);
			DateTime x1 = TRight(Xoff);
			DateTime x0 = Time[0].AddSeconds(-(double)tfsecs * rdx);
			string nm = DateStr();
			double mid = (g.cl + g.op) / 2.0;
			bool up = g.op > g.cl;
			g.mline = MkLL(tg + "ce", x0, x1, mid, RthogColor, RthogStyle, RthogWidth, "RTHOG ce " + nm, RthogColor, fNormal, false);
			g.opline = MkLL(tg + "op", Time[0], x1, g.op, RthogColor, RthogStyle, RthogWidth,
				(up ? "RTHOG hi " : "RTHOG lo ") + nm, RthogColor, fNormal, false);
			if (g.cline != null)
			{
				g.cline.txt = (up ? "RTHOG lo " : "RTHOG hi ") + nm;
				DrawLabel(g.cline);
			}
			if (DoGapRect)
			{
				g.rect = true;
				g.rx0 = x0;
				DRect(tg + "rect", x0, g.op, x1, g.cl, RthogColor, 100 - GapRectTransparency);
			}
			else
			{
				Rm(tg + "rect");
				g.rect = false;
			}
			if (DoRthogQ)
				RQuads(g);
			else
				KillQ(g);
			if (DoRthogOct)
				ROct(g);
			else
				KillOct(g);
			if (DoRthogPo3)
				RPo3(g);
			else
				KillPo3(g);
		}

		// compact version for gaps older than the previous one
		private void SetOld(IRthog g)
		{
			g.isold = true;
			bool complete = g.tend.HasValue && g.tstart.HasValue && !double.IsNaN(g.op) && !double.IsNaN(g.cl);
			KillR(g);
			if (!complete)
				return;
			string tg = RTag(g);
			double d = (double)tfsecs * rdx;
			double mid = (g.cl + g.op) / 2.0;
			DateTime ts = g.tstart.Value;
			DateTime te = g.tend.Value;
			g.cline = MkLL(tg + "ocl", ts.AddSeconds(-d), ts.AddSeconds(d), g.cl, RthogOldColor, RthogStyle, RthogWidth,
				"rthog.cl", PineBlack, fNormal, false);
			g.cline2 = MkLL(tg + "ocl2", te.AddSeconds(-d), te.AddSeconds(d), g.cl, RthogOldColor, RthogStyle, RthogWidth,
				"rthog.cl", PineBlack, fNormal, ExtendGaps);
			g.mline = MkLL(tg + "oce", te.AddSeconds(-d), te.AddSeconds(d), mid, RthogOldColor, RthogStyle, RthogWidth,
				"rthog.ce", PineBlack, fNormal, ExtendGaps);
			g.opline = MkLL(tg + "oop", te.AddSeconds(-d), te.AddSeconds(d), g.op, RthogOldColor, RthogStyle, RthogWidth,
				"rthog.op", PineBlack, fNormal, ExtendGaps);
		}

		// right-edge update, final bar only
		private void RRight(IRthog g, Brush c)
		{
			if (g.isold)
				return;
			DateTime x = TRight(Xoff);
			SetR(g.cline, x, c);
			SetR(g.mline, x, c);
			SetR(g.opline, x, c);
			SetR(g.uq, x, c);
			SetR(g.dq, x, c);
			if (g.rect)
				DRect(RTag(g) + "rect", g.rx0, g.op, x, g.cl, RthogColor, 100 - GapRectTransparency);
			foreach (LL l in g.po3)
				SetR(l, x, c);
		}

		// stddev lines of the RTHOG and the premarket range at almost the same price share one label:
		// "RTHOG+0.5 PMKT+1". The other label is emptied.
		private void CombinePo3(IRthog g, IZone z, double tol)
		{
			if (g.po3.Count == 0 && z.sdlines.Count == 0)
				return;
			for (int k = 0; k < g.po3.Count; k++)
				g.po3[k].txt = "RTHOG" + (k < g.po3names.Count ? g.po3names[k] : string.Empty);
			for (int k = 0; k < z.sdlines.Count; k++)
				z.sdlines[k].txt = "PMKT" + (k < z.sdnames.Count ? z.sdnames[k] : string.Empty);
			bool[] used = new bool[z.sdlines.Count];
			for (int k = 0; k < g.po3.Count; k++)
			{
				for (int m = 0; m < z.sdlines.Count; m++)
				{
					if (!used[m] && Math.Abs(g.po3[k].y - z.sdlines[m].y) <= tol)
					{
						g.po3[k].txt = "RTHOG" + (k < g.po3names.Count ? g.po3names[k] : string.Empty)
							+ " PMKT" + (m < z.sdnames.Count ? z.sdnames[m] : string.Empty);
						z.sdlines[m].txt = string.Empty;
						used[m] = true;
						break;
					}
				}
			}
			foreach (LL l in g.po3)
				DrawLabel(l);
			foreach (LL l in z.sdlines)
				DrawLabel(l);
		}

		#endregion

		#region EQH / EQL

		// ta.pivothigh / ta.pivotlow on the series in progress: the bar len bars ago is a strict extreme of
		// the len bars on each side
		private double PivotHigh(int len)
		{
			double c = High[len];
			for (int i = 1; i <= len; i++)
				if (High[len + i] >= c || High[len - i] >= c)
					return double.NaN;
			return c;
		}

		private double PivotLow(int len)
		{
			double c = Low[len];
			for (int i = 1; i <= len; i++)
				if (Low[len + i] <= c || Low[len - i] <= c)
					return double.NaN;
			return c;
		}

		private string PTag(IPiv v)
		{
			return TagPfx + "p" + v.id + "_";
		}

		private void RemovePiv(IPiv v)
		{
			Rm(PTag(v) + "ln");
			Rm(PTag(v) + "lb");
		}

		private void DropFirst(List<IPiv> arr, bool wantEq)
		{
			for (int i = 0; i < arr.Count; i++)
			{
				if (arr[i].eq == wantEq)
				{
					RemovePiv(arr[i]);
					arr.RemoveAt(i);
					return;
				}
			}
		}

		private void TrimPivs(List<IPiv> arr)
		{
			int ne = arr.Count(v => v.eq);
			while (ne > EqKeep)
			{
				DropFirst(arr, true);
				ne--;
			}
			while (arr.Count - ne > 30)
				DropFirst(arr, false);
		}

		private void SweepPivs(List<IPiv> arr, bool isHigh, double tol)
		{
			for (int i = arr.Count - 1; i >= 0; i--)
			{
				IPiv v = arr[i];
				double slack = v.eq ? 0.0 : tol;
				bool swept = isHigh ? High[0] > v.p + slack : Low[0] < v.p - slack;
				if (swept)
				{
					RemovePiv(v);
					arr.RemoveAt(i);
				}
			}
		}

		private void AddPiv(List<IPiv> arr, double p, DateTime t, bool isHigh, double tol)
		{
			IPiv m = null;
			foreach (IPiv v in arr)
			{
				if (Math.Abs(v.p - p) <= tol)
				{
					m = v;
					break;
				}
			}
			if (m != null)
			{
				m.eq = true;
				if (isHigh ? p > m.p : p < m.p)
					m.p = p;
			}
			else
				arr.Add(new IPiv { id = nextId++, p = p, t = t });
			TrimPivs(arr);
		}

		// 3-candle swings (one candle on each side), kept until price takes them
		private void Sweep3(List<double> ps, List<DateTime> ts, bool isHigh)
		{
			for (int i = ps.Count - 1; i >= 0; i--)
			{
				if (isHigh ? High[0] > ps[i] : Low[0] < ps[i])
				{
					ps.RemoveAt(i);
					ts.RemoveAt(i);
				}
			}
		}

		private void Add3(List<double> ps, List<DateTime> ts, double p, DateTime t)
		{
			ps.Add(p);
			ts.Add(t);
			if (ps.Count > 60)
			{
				ps.RemoveAt(0);
				ts.RemoveAt(0);
			}
		}

		// the pivot just added at time t (price p) becomes an EQH / EQL when an earlier unswept 3-candle
		// swing has the same price or is at most Eq3Pts beyond it. The level is the swing price, from the swing.
		private void Eq3(List<IPiv> arr, List<double> ps, List<DateTime> ts, double p, DateTime t, bool isHigh)
		{
			if (arr.Count == 0)
				return;
			IPiv v = arr[arr.Count - 1];
			if (v.t != t || v.eq)
				return;
			for (int i = ps.Count - 1; i >= 0; i--)
			{
				double q = ps[i];
				DateTime qt = ts[i];
				double d = isHigh ? q - p : p - q;
				if (qt < t && d >= 0 && d <= Eq3Pts)
				{
					v.eq = true;
					v.p = q;
					v.t = qt;
					TrimPivs(arr);
					break;
				}
			}
		}

		private void DrawEq(List<IPiv> arr, string txt, Brush c)
		{
			DateTime x = TRight(Xoff);
			foreach (IPiv v in arr)
			{
				if (!v.eq)
					continue;
				string tg = PTag(v);
				DLine(tg + "ln", v.t, v.p, x, v.p, c, EqhlStyle, EqhlWidth);
				DText(tg + "lb", txt, x, v.p, c, fEq, 0);
			}
		}

		#endregion

		#region Helpers

		private static Brush Rgb(byte r, byte g, byte b, byte a = 255)
		{
			SolidColorBrush br = new SolidColorBrush(Color.FromArgb(a, r, g, b));
			br.Freeze();
			return br;
		}

		private static Brush Fz(Brush b)
		{
			if (b == null)
				return Brushes.Transparent;
			if (b.IsFrozen)
				return b;
			Brush c = b.Clone();
			c.Freeze();
			return c;
		}

		private static SimpleFont MkFont(ICTlevelsTextSize s, bool bold)
		{
			double px;
			switch (s)
			{
				case ICTlevelsTextSize.Tiny: px = 8; break;
				case ICTlevelsTextSize.Small: px = 10; break;
				case ICTlevelsTextSize.Large: px = 15; break;
				case ICTlevelsTextSize.Huge: px = 20; break;
				default: px = 12; break;
			}
			return new SimpleFont("Arial", px) { Bold = bold };
		}

		private static TimeZoneInfo FindTz(string id)
		{
			try
			{
				return TimeZoneInfo.FindSystemTimeZoneById(id);
			}
			catch (Exception)
			{
				return null;
			}
		}

		// platform time zone -> target time zone (New York or Madrid)
		private DateTime ToTz(DateTime t)
		{
			if (targetTz == null || platformTz == null || targetTz.Id == platformTz.Id)
				return t;
			DateTime u = DateTime.SpecifyKind(t, DateTimeKind.Unspecified);
			try
			{
				return TimeZoneInfo.ConvertTime(u, platformTz, targetTz);
			}
			catch (ArgumentException)
			{
				// invalid local time (daylight saving gap of the platform zone)
				try
				{
					return TimeZoneInfo.ConvertTime(u.AddHours(1), platformTz, targetTz);
				}
				catch (ArgumentException)
				{
					return t;
				}
			}
		}

		private static int ParseSecs(string s)
		{
			int h, m;
			if (s == null || s.Length < 4 || !int.TryParse(s.Substring(0, 2), out h) || !int.TryParse(s.Substring(2, 2), out m))
				return -1;
			if (h < 0 || h > 24 || m < 0 || m > 59)
				return -1;
			return (60 * (60 * h + m)) % 86400;
		}

		private static ZI MkZI(string name, string ival, Sty sty)
		{
			if (string.IsNullOrEmpty(name) || ival == null)
				return null;
			ival = ival.Trim();
			if (ival.Length < 9)
				return null;
			int ss = ParseSecs(ival.Substring(0, 4));
			int es = ParseSecs(ival.Substring(5, 4));
			if (ss < 0 || es < 0)
				return null;
			return new ZI { name = name, ss = ss, es = es, sty = sty };
		}

		// the bar contains the start instant
		private bool Starts(ZI z)
		{
			return (z.ss - tsec + 86400) % 86400 < tfsecs;
		}

		// the bar contains the end instant, or the window was left between two bars (data gaps)
		private bool Ends(ZI z)
		{
			return (z.es - tsec + 86400) % 86400 < tfsecs || (z.Inside(tsecPrev) && !z.Inside(tsec));
		}

		private DateTime TRight(int n)
		{
			return Time[0].AddSeconds((double)n * tfsecs);
		}

		// short date for labels: day of the month and the month, one letter when no other month starts with it
		// (F, S, O, N, D), else three letters (Jan, Mar, Apr, May, Jun, Jul, Aug): 23O, 23Jul
		private string DateStr()
		{
			int m = barTz.Month;
			string mm = m == 2 ? "F" : m == 9 ? "S" : m == 10 ? "O" : m == 11 ? "N" : m == 12 ? "D"
				: barTz.ToString("MMM", CultureInfo.InvariantCulture);
			return barTz.Day.ToString(CultureInfo.InvariantCulture) + mm;
		}

		private static DateTime Mn(DateTime a, DateTime b)
		{
			return a < b ? a : b;
		}

		private static double Nz(double v)
		{
			return double.IsNaN(v) ? 0.0 : v;
		}

		#endregion

		#region Drawing

		private void Rm(string tag)
		{
			if (drawn.Remove(tag))
				RemoveDrawObject(tag);
		}

		private void DLine(string tag, DateTime t1, double y1, DateTime t2, double y2, Brush c, DashStyleHelper sty, int w)
		{
			Draw.Line(this, tag, false, t1, y1, t2, y2, c, sty, w);
			drawn.Add(tag);
		}

		private void DRay(string tag, DateTime t1, double y1, DateTime t2, double y2, Brush c, DashStyleHelper sty, int w)
		{
			Draw.Ray(this, tag, false, t1, y1, t2, y2, c, sty, w);
			drawn.Add(tag);
		}

		private void DVLine(string tag, DateTime t, Brush c, DashStyleHelper sty, int w)
		{
			Draw.VerticalLine(this, tag, t, c, sty, w);
			drawn.Add(tag);
		}

		private void DRect(string tag, DateTime t1, double y1, DateTime t2, double y2, Brush area, int opacity,
			bool borderOnly = false)
		{
			int op = Math.Max(0, Math.Min(100, opacity));
			if (borderOnly)
			{
				// outline only: the area color, with the rectangle opacity, as the border; no fill
				Brush b = area.Clone();
				b.Opacity = op / 100.0;
				b.Freeze();
				Draw.Rectangle(this, tag, false, t1, y1, t2, y2, b, Brushes.Transparent, 0);
			}
			else
				Draw.Rectangle(this, tag, false, t1, y1, t2, y2, Brushes.Transparent, area, op);
			drawn.Add(tag);
		}

		// left-aligned text at (t, y), like a Pine label.style_label_left with a transparent background
		private void DText(string tag, string txt, DateTime t, double y, Brush c, SimpleFont f, int yoff)
		{
			Draw.Text(this, tag, false, txt, t, y, yoff, c, f, TextAlignment.Left, Brushes.Transparent, Brushes.Transparent, 0);
			drawn.Add(tag);
		}

		private void DFixed(string tag, string txt, Brush c)
		{
			if (string.IsNullOrEmpty(txt))
			{
				Rm(tag);
				return;
			}
			Draw.TextFixed(this, tag, txt, TextPosition.TopRight, c, fTable, Brushes.Transparent, Brushes.Transparent, 0);
			drawn.Add(tag);
		}

		private LL MkLL(string tag, DateTime x1, DateTime x2, double y, Brush c, DashStyleHelper sty, int w, string txt,
			Brush tc, SimpleFont f, bool ray)
		{
			LL l = new LL { tag = tag, x1 = x1, x2 = x2, y = y, c = c, sty = sty, w = w, txt = txt, tc = tc, font = f, ray = ray };
			DrawLL(l);
			return l;
		}

		private void DrawLL(LL l)
		{
			if (l.ray)
				DRay(l.tag, l.x1, l.y, l.x2, l.y, l.c, l.sty, l.w);
			else
				DLine(l.tag, l.x1, l.y, l.x2, l.y, l.c, l.sty, l.w);
			DrawLabel(l);
		}

		private void DrawLabel(LL l)
		{
			if (string.IsNullOrEmpty(l.txt))
				Rm(l.tag + "L");
			else
				DText(l.tag + "L", l.txt, l.lx ?? l.x2, l.y, l.tc, l.font, l.yoff);
		}

		private void RemoveLL(LL l)
		{
			if (l == null)
				return;
			Rm(l.tag);
			Rm(l.tag + "L");
		}

		// removes the objects of the old list that were not drawn again with the same tag
		private void RemoveStale(List<LL> oldList, List<LL> newList)
		{
			HashSet<string> keep = new HashSet<string>();
			foreach (LL l in newList)
				keep.Add(l.tag);
			foreach (LL l in oldList)
				if (!keep.Contains(l.tag))
					RemoveLL(l);
		}

		// right end of a line and its label moved to x and recolored (Pine setR)
		private void SetR(LL l, DateTime x, Brush c)
		{
			if (l == null)
				return;
			l.x2 = x;
			l.c = c;
			l.tc = c;
			DrawLL(l);
		}

		#endregion

		#region Properties

		[NinjaScriptProperty]
		[Display(Name = "Zone: US / EU / Auto", GroupName = "Time & Settings", Order = 101)]
		public ICTlevelsZoneMode ZoneMode { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "compute for historic data use (no effect in NT8)", GroupName = "Time & Settings", Order = 102)]
		public bool ComputeVisible { get; set; }

		[NinjaScriptProperty]
		[Range(1, 500)]
		[Display(Name = "offset for margin labels (bars)", GroupName = "Time & Settings", Order = 103)]
		public int Xoff { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "show object cap warning (no effect in NT8)", GroupName = "Time & Settings", Order = 104)]
		public bool ShowCap { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Draw max/min/close for last 3 months", GroupName = "Month Settings", Order = 201)]
		public bool DoMonth { get; set; }

		[NinjaScriptProperty]
		[XmlIgnore]
		[Display(Name = "max", GroupName = "Month Settings", Order = 202)]
		public Brush MonMaxColor { get; set; }

		[Browsable(false)]
		public string MonMaxColorSerializable
		{
			get { return Serialize.BrushToString(MonMaxColor); }
			set { MonMaxColor = Serialize.StringToBrush(value); }
		}

		[NinjaScriptProperty]
		[XmlIgnore]
		[Display(Name = "min", GroupName = "Month Settings", Order = 203)]
		public Brush MonMinColor { get; set; }

		[Browsable(false)]
		public string MonMinColorSerializable
		{
			get { return Serialize.BrushToString(MonMinColor); }
			set { MonMinColor = Serialize.StringToBrush(value); }
		}

		[NinjaScriptProperty]
		[XmlIgnore]
		[Display(Name = "close", GroupName = "Month Settings", Order = 204)]
		public Brush MonCloseColor { get; set; }

		[Browsable(false)]
		public string MonCloseColorSerializable
		{
			get { return Serialize.BrushToString(MonCloseColor); }
			set { MonCloseColor = Serialize.StringToBrush(value); }
		}

		[NinjaScriptProperty]
		[Display(Name = "line style", GroupName = "Month Settings", Order = 205)]
		public DashStyleHelper MonStyle { get; set; }

		[NinjaScriptProperty]
		[Range(1, 3)]
		[Display(Name = "line width", GroupName = "Month Settings", Order = 206)]
		public int MonWidth { get; set; }

		[NinjaScriptProperty]
		[Range(1, 1000)]
		[Display(Name = "Number of days", GroupName = "Day Settings", Order = 301)]
		public int NDays { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "day vertical lines", GroupName = "Day Settings", Order = 302)]
		public bool DoLines { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "day vertical lines style", GroupName = "Day Settings", Order = 303)]
		public DashStyleHelper DayLineStyle { get; set; }

		[NinjaScriptProperty]
		[Range(1, 3)]
		[Display(Name = "day vertical lines width", GroupName = "Day Settings", Order = 304)]
		public int DayWidth { get; set; }

		[NinjaScriptProperty]
		[XmlIgnore]
		[Display(Name = "day vertical lines color", GroupName = "Day Settings", Order = 305)]
		public Brush DayColor { get; set; }

		[Browsable(false)]
		public string DayColorSerializable
		{
			get { return Serialize.BrushToString(DayColor); }
			set { DayColor = Serialize.StringToBrush(value); }
		}

		[NinjaScriptProperty]
		[Display(Name = "week vertical lines style", GroupName = "Day Settings", Order = 306)]
		public DashStyleHelper WeekLineStyle { get; set; }

		[NinjaScriptProperty]
		[Range(1, 3)]
		[Display(Name = "week vertical lines width", GroupName = "Day Settings", Order = 307)]
		public int WeekWidth { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "day max/min", GroupName = "Day Settings", Order = 308)]
		public bool DoDayMaxMin { get; set; }

		[NinjaScriptProperty]
		[XmlIgnore]
		[Display(Name = "day max color", GroupName = "Day Settings", Order = 309)]
		public Brush DayMaxColor { get; set; }

		[Browsable(false)]
		public string DayMaxColorSerializable
		{
			get { return Serialize.BrushToString(DayMaxColor); }
			set { DayMaxColor = Serialize.StringToBrush(value); }
		}

		[NinjaScriptProperty]
		[XmlIgnore]
		[Display(Name = "day min color", GroupName = "Day Settings", Order = 310)]
		public Brush DayMinColor { get; set; }

		[Browsable(false)]
		public string DayMinColorSerializable
		{
			get { return Serialize.BrushToString(DayMinColor); }
			set { DayMinColor = Serialize.StringToBrush(value); }
		}

		[NinjaScriptProperty]
		[Display(Name = "day max/min style", GroupName = "Day Settings", Order = 311)]
		public DashStyleHelper DayMaxMinStyle { get; set; }

		[NinjaScriptProperty]
		[Range(1, 3)]
		[Display(Name = "day max/min width", GroupName = "Day Settings", Order = 312)]
		public int DayMaxMinWidth { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "label unbroken day max/min", GroupName = "Day Settings", Order = 313)]
		public bool DoDayLabel { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "day label size", GroupName = "Day Settings", Order = 314)]
		public ICTlevelsTextSize DayLabelSize { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "REH/REL labels", GroupName = "Day Settings", Order = 315)]
		public bool DoReh { get; set; }

		[NinjaScriptProperty]
		[Range(0.0, double.MaxValue)]
		[Display(Name = "REH/REL tolerance x daily ATR", GroupName = "Day Settings", Order = 316)]
		public double RelTol { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "equilibrium", GroupName = "Day Settings", Order = 317)]
		public bool DoEq { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "equilibrium style", GroupName = "Day Settings", Order = 318)]
		public DashStyleHelper EqStyle { get; set; }

		[NinjaScriptProperty]
		[Range(1, 3)]
		[Display(Name = "equilibrium width", GroupName = "Day Settings", Order = 319)]
		public int EqWidth { get; set; }

		[NinjaScriptProperty]
		[XmlIgnore]
		[Display(Name = "equilibrium color", GroupName = "Day Settings", Order = 320)]
		public Brush EqColor { get; set; }

		[Browsable(false)]
		public string EqColorSerializable
		{
			get { return Serialize.BrushToString(EqColor); }
			set { EqColor = Serialize.StringToBrush(value); }
		}

		[NinjaScriptProperty]
		[Display(Name = "Day Open", GroupName = "Day Settings", Order = 321)]
		public bool DoOpen { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Day Open style", GroupName = "Day Settings", Order = 322)]
		public DashStyleHelper OpenStyle { get; set; }

		[NinjaScriptProperty]
		[Range(1, 3)]
		[Display(Name = "Day Open width", GroupName = "Day Settings", Order = 323)]
		public int OpenWidth { get; set; }

		[NinjaScriptProperty]
		[XmlIgnore]
		[Display(Name = "Day Open color", GroupName = "Day Settings", Order = 324)]
		public Brush OpenColor { get; set; }

		[Browsable(false)]
		public string OpenColorSerializable
		{
			get { return Serialize.BrushToString(OpenColor); }
			set { OpenColor = Serialize.StringToBrush(value); }
		}

		[NinjaScriptProperty]
		[Display(Name = "TGIF levels %", GroupName = "TGIF", Order = 401)]
		public bool DoTgif { get; set; }

		[NinjaScriptProperty]
		[Range(0.01, 1.0)]
		[Display(Name = "TGIF level 1 (fraction)", GroupName = "TGIF", Order = 402)]
		public double TgP1 { get; set; }

		[NinjaScriptProperty]
		[Range(0.01, 1.0)]
		[Display(Name = "TGIF level 2 (fraction)", GroupName = "TGIF", Order = 403)]
		public double TgP2 { get; set; }

		[NinjaScriptProperty]
		[Range(0.05, 0.5)]
		[Display(Name = "TGIF displacement: price within x range of the high / low", Description = "The week is a displacement up (down) when price is between the week high (low) and this fraction of the week range below (above) it, whatever its body.", GroupName = "TGIF", Order = 404)]
		public double TgNear { get; set; }

		[NinjaScriptProperty]
		[XmlIgnore]
		[Display(Name = "colors: up", GroupName = "TGIF", Order = 405)]
		public Brush TgUpColor { get; set; }

		[Browsable(false)]
		public string TgUpColorSerializable
		{
			get { return Serialize.BrushToString(TgUpColor); }
			set { TgUpColor = Serialize.StringToBrush(value); }
		}

		[NinjaScriptProperty]
		[XmlIgnore]
		[Display(Name = "down", GroupName = "TGIF", Order = 406)]
		public Brush TgDownColor { get; set; }

		[Browsable(false)]
		public string TgDownColorSerializable
		{
			get { return Serialize.BrushToString(TgDownColor); }
			set { TgDownColor = Serialize.StringToBrush(value); }
		}

		[NinjaScriptProperty]
		[Display(Name = "line style", GroupName = "TGIF", Order = 407)]
		public DashStyleHelper TgStyle { get; set; }

		[NinjaScriptProperty]
		[Range(1, 3)]
		[Display(Name = "line width", GroupName = "TGIF", Order = 408)]
		public int TgWidth { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "text size", GroupName = "TGIF", Order = 409)]
		public ICTlevelsTextSize TgLabelSize { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Draw sessions", GroupName = "Session Settings", Order = 501)]
		public bool DoSessions { get; set; }

		[NinjaScriptProperty]
		[Range(3, 1000)]
		[Display(Name = "sessions to keep", GroupName = "Session Settings", Order = 502)]
		public int NSessions { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Draw session rectangles", GroupName = "Session Settings", Order = 503)]
		public bool DoRect { get; set; }

		[NinjaScriptProperty]
		[Range(0, 100)]
		[Display(Name = "rectangle transparency", GroupName = "Session Settings", Order = 504)]
		public int RectTransparency { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Session rectangles: border only", Description = "Draw only the outline of the session rectangles (session color, with the transparency above), no fill", GroupName = "Session Settings", Order = 504)]
		public bool RectBorderOnly { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "No session rectangles at or under", GroupName = "Session Settings", Order = 505)]
		public bool NoLowRects { get; set; }

		[NinjaScriptProperty]
		[Range(1, 1440)]
		[Display(Name = "timeframe (minutes)", GroupName = "Session Settings", Order = 506)]
		public int RectMinTf { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "session max/min", GroupName = "Session Settings", Order = 507)]
		public bool DoSesMaxMin { get; set; }

		[NinjaScriptProperty]
		[XmlIgnore]
		[Display(Name = "session max color", GroupName = "Session Settings", Order = 508)]
		public Brush SesMaxColor { get; set; }

		[Browsable(false)]
		public string SesMaxColorSerializable
		{
			get { return Serialize.BrushToString(SesMaxColor); }
			set { SesMaxColor = Serialize.StringToBrush(value); }
		}

		[NinjaScriptProperty]
		[XmlIgnore]
		[Display(Name = "session min color", GroupName = "Session Settings", Order = 509)]
		public Brush SesMinColor { get; set; }

		[Browsable(false)]
		public string SesMinColorSerializable
		{
			get { return Serialize.BrushToString(SesMinColor); }
			set { SesMinColor = Serialize.StringToBrush(value); }
		}

		[NinjaScriptProperty]
		[Display(Name = "session max/min style", GroupName = "Session Settings", Order = 510)]
		public DashStyleHelper SesStyle { get; set; }

		[NinjaScriptProperty]
		[Range(1, 3)]
		[Display(Name = "session max/min width", GroupName = "Session Settings", Order = 511)]
		public int SesWidth { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Label unbroken session max/min", GroupName = "Session Settings", Order = 512)]
		public bool DoSesLabel { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "session label size", GroupName = "Session Settings", Order = 513)]
		public ICTlevelsTextSize SesLabelSize { get; set; }

		[NinjaScriptProperty]
		[XmlIgnore]
		[Display(Name = "colors: asia", GroupName = "Session Settings", Order = 514)]
		public Brush AsiaColor { get; set; }

		[Browsable(false)]
		public string AsiaColorSerializable
		{
			get { return Serialize.BrushToString(AsiaColor); }
			set { AsiaColor = Serialize.StringToBrush(value); }
		}

		[NinjaScriptProperty]
		[XmlIgnore]
		[Display(Name = "london", GroupName = "Session Settings", Order = 515)]
		public Brush LondonColor { get; set; }

		[Browsable(false)]
		public string LondonColorSerializable
		{
			get { return Serialize.BrushToString(LondonColor); }
			set { LondonColor = Serialize.StringToBrush(value); }
		}

		[NinjaScriptProperty]
		[XmlIgnore]
		[Display(Name = "NY", GroupName = "Session Settings", Order = 516)]
		public Brush NYColor { get; set; }

		[Browsable(false)]
		public string NYColorSerializable
		{
			get { return Serialize.BrushToString(NYColor); }
			set { NYColor = Serialize.StringToBrush(value); }
		}

		[NinjaScriptProperty]
		[Display(Name = "Draw NDOG/NWOG/RTHOGs", GroupName = "Gaps and Ranges", Order = 601)]
		public bool DoGaps { get; set; }

		[NinjaScriptProperty]
		[Range(1, 1000)]
		[Display(Name = "gaps to keep", GroupName = "Gaps and Ranges", Order = 602)]
		public int NGaps { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "NDOGs", GroupName = "Gaps and Ranges", Order = 603)]
		public bool DoNdog { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "NWOGs", GroupName = "Gaps and Ranges", Order = 604)]
		public bool DoNwog { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "RTHOGs (timeframes up to 15m)", GroupName = "Gaps and Ranges", Order = 605)]
		public bool DoRthog { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "RTHOG quadrants", GroupName = "Gaps and Ranges", Order = 606)]
		public bool DoRthogQ { get; set; }

		[NinjaScriptProperty]
		[Range(0.0, double.MaxValue)]
		[Display(Name = "RTHOG quadrants min size", GroupName = "Gaps and Ranges", Order = 607)]
		public double RthogQMin { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "RTHOG octants", Description = "Midpoints between the quadrants (1/8, 3/8, 5/8 and 7/8 of the gap) of the latest RTHOG, drawn when an eighth of the gap is above the quadrant min size, from the open until noon.", GroupName = "Gaps and Ranges", Order = 607)]
		public bool DoRthogOct { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "RTHOG stddevs", GroupName = "Gaps and Ranges", Order = 608)]
		public bool DoRthogPo3 { get; set; }

		[NinjaScriptProperty]
		[Range(0.0, double.MaxValue)]
		[Display(Name = "RTHOG stddevs min size", GroupName = "Gaps and Ranges", Order = 609)]
		public double RthogPo3Min { get; set; }

		[NinjaScriptProperty]
		[Range(1, 40)]
		[Display(Name = "number of stddevs to draw above/below", GroupName = "Gaps and Ranges", Order = 610)]
		public int RthogPo3Num { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "RTHOG stddevs auto", GroupName = "Gaps and Ranges", Order = 611)]
		public bool RthogAuto { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Draw gap rectangles", GroupName = "Gaps and Ranges", Order = 612)]
		public bool DoGapRect { get; set; }

		[NinjaScriptProperty]
		[Range(0, 100)]
		[Display(Name = "gap rectangle transparency", GroupName = "Gaps and Ranges", Order = 613)]
		public int GapRectTransparency { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Combine gaps at similar levels", GroupName = "Gaps and Ranges", Order = 614)]
		public bool CombGaps { get; set; }

		[NinjaScriptProperty]
		[Range(0.0, double.MaxValue)]
		[Display(Name = "combine tolerance x daily ATR", GroupName = "Gaps and Ranges", Order = 615)]
		public double CombTol { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "NDOG style", GroupName = "Gaps and Ranges", Order = 616)]
		public DashStyleHelper NdogStyle { get; set; }

		[NinjaScriptProperty]
		[Range(1, 3)]
		[Display(Name = "NDOG width", GroupName = "Gaps and Ranges", Order = 617)]
		public int NdogWidth { get; set; }

		[NinjaScriptProperty]
		[XmlIgnore]
		[Display(Name = "NDOG color", GroupName = "Gaps and Ranges", Order = 618)]
		public Brush NdogColor { get; set; }

		[Browsable(false)]
		public string NdogColorSerializable
		{
			get { return Serialize.BrushToString(NdogColor); }
			set { NdogColor = Serialize.StringToBrush(value); }
		}

		[NinjaScriptProperty]
		[Display(Name = "NWOG style", GroupName = "Gaps and Ranges", Order = 619)]
		public DashStyleHelper NwogStyle { get; set; }

		[NinjaScriptProperty]
		[Range(1, 3)]
		[Display(Name = "NWOG width", GroupName = "Gaps and Ranges", Order = 620)]
		public int NwogWidth { get; set; }

		[NinjaScriptProperty]
		[XmlIgnore]
		[Display(Name = "NWOG color", GroupName = "Gaps and Ranges", Order = 621)]
		public Brush NwogColor { get; set; }

		[Browsable(false)]
		public string NwogColorSerializable
		{
			get { return Serialize.BrushToString(NwogColor); }
			set { NwogColor = Serialize.StringToBrush(value); }
		}

		[NinjaScriptProperty]
		[Display(Name = "RTHOG style", GroupName = "Gaps and Ranges", Order = 622)]
		public DashStyleHelper RthogStyle { get; set; }

		[NinjaScriptProperty]
		[Range(1, 3)]
		[Display(Name = "RTHOG width", GroupName = "Gaps and Ranges", Order = 623)]
		public int RthogWidth { get; set; }

		[NinjaScriptProperty]
		[XmlIgnore]
		[Display(Name = "RTHOG color old", GroupName = "Gaps and Ranges", Order = 624)]
		public Brush RthogOldColor { get; set; }

		[Browsable(false)]
		public string RthogOldColorSerializable
		{
			get { return Serialize.BrushToString(RthogOldColor); }
			set { RthogOldColor = Serialize.StringToBrush(value); }
		}

		[NinjaScriptProperty]
		[XmlIgnore]
		[Display(Name = "RTHOG color this", GroupName = "Gaps and Ranges", Order = 625)]
		public Brush RthogColor { get; set; }

		[Browsable(false)]
		public string RthogColorSerializable
		{
			get { return Serialize.BrushToString(RthogColor); }
			set { RthogColor = Serialize.StringToBrush(value); }
		}

		[NinjaScriptProperty]
		[Display(Name = "RTHOG quadrant line", GroupName = "Gaps and Ranges", Order = 626)]
		public DashStyleHelper RthogQuadStyle { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "PO3 line", GroupName = "Gaps and Ranges", Order = 627)]
		public DashStyleHelper RthogPo3Style { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "extend old RTHOG lines", GroupName = "Gaps and Ranges", Order = 628)]
		public bool ExtendGaps { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Draw premarket range (m5 or less)", GroupName = "Premarket Range", Order = 701)]
		public bool DoPremarket { get; set; }

		[NinjaScriptProperty]
		[Range(1, 5)]
		[Display(Name = "quadrants/stddevs for the last", GroupName = "Premarket Range", Order = 702)]
		public int PmKeep { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "range", GroupName = "Premarket Range", Order = 703)]
		public ICTlevelsPmMode PmMode { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "manual range (HHMM-HHMM)", GroupName = "Premarket Range", Order = 704)]
		public string PmSession { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "PMKT quadrants", GroupName = "Premarket Range", Order = 705)]
		public bool DoPmQ { get; set; }

		[NinjaScriptProperty]
		[Range(0.0, double.MaxValue)]
		[Display(Name = "PMKT quadrants min size", GroupName = "Premarket Range", Order = 706)]
		public double PmQMin { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "PMKT stddevs", GroupName = "Premarket Range", Order = 707)]
		public bool DoPmPo3 { get; set; }

		[NinjaScriptProperty]
		[Range(0.0, double.MaxValue)]
		[Display(Name = "PMKT stddevs min size", GroupName = "Premarket Range", Order = 708)]
		public double PmPo3Min { get; set; }

		[NinjaScriptProperty]
		[Range(1, 40)]
		[Display(Name = "number of stddevs to draw above/below", GroupName = "Premarket Range", Order = 709)]
		public int PmPo3Num { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "PMKT stddevs auto", GroupName = "Premarket Range", Order = 710)]
		public bool PmAuto { get; set; }

		[NinjaScriptProperty]
		[XmlIgnore]
		[Display(Name = "line", GroupName = "Premarket Range", Order = 711)]
		public Brush PmColor { get; set; }

		[Browsable(false)]
		public string PmColorSerializable
		{
			get { return Serialize.BrushToString(PmColor); }
			set { PmColor = Serialize.StringToBrush(value); }
		}

		[NinjaScriptProperty]
		[Range(1, 3)]
		[Display(Name = "line width", GroupName = "Premarket Range", Order = 712)]
		public int PmWidth { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "quadrant", GroupName = "Premarket Range", Order = 713)]
		public DashStyleHelper PmQuadStyle { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "CE", GroupName = "Premarket Range", Order = 714)]
		public DashStyleHelper PmCeStyle { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "stddev", GroupName = "Premarket Range", Order = 715)]
		public DashStyleHelper PmPo3Style { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "label size", GroupName = "Premarket Range", Order = 716)]
		public ICTlevelsTextSize PmLabelSize { get; set; }

		[NinjaScriptProperty]
		[Range(0.0, double.MaxValue)]
		[Display(Name = "combine RTHOG labels, tolerance x daily ATR", GroupName = "Premarket Range", Order = 717)]
		public double PmCTol { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Draw EQH / EQL", GroupName = "Equal Highs / Lows", Order = 801)]
		public bool DoEqhl { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "also from the daily chart", GroupName = "Equal Highs / Lows", Order = 802)]
		public bool DoEqDaily { get; set; }

		[NinjaScriptProperty]
		[Range(1, 100)]
		[Display(Name = "pivot", GroupName = "Equal Highs / Lows", Order = 803)]
		public int EqLen { get; set; }

		[NinjaScriptProperty]
		[Range(1, 100)]
		[Display(Name = "daily pivot", GroupName = "Equal Highs / Lows", Order = 804)]
		public int EqLenD { get; set; }

		[NinjaScriptProperty]
		[Range(0.0, double.MaxValue)]
		[Display(Name = "tolerance x ATR", GroupName = "Equal Highs / Lows", Order = 805)]
		public double EqTol { get; set; }

		[NinjaScriptProperty]
		[Range(1, 20)]
		[Display(Name = "keep per side", GroupName = "Equal Highs / Lows", Order = 806)]
		public int EqKeep { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "also with 3-candle swings, max points beyond", GroupName = "Equal Highs / Lows", Order = 807)]
		public bool DoEq3 { get; set; }

		[NinjaScriptProperty]
		[Range(0.0, double.MaxValue)]
		[Display(Name = "3-candle swing max points beyond", GroupName = "Equal Highs / Lows", Order = 808)]
		public double Eq3Pts { get; set; }

		[NinjaScriptProperty]
		[XmlIgnore]
		[Display(Name = "EQH", GroupName = "Equal Highs / Lows", Order = 809)]
		public Brush EqhColor { get; set; }

		[Browsable(false)]
		public string EqhColorSerializable
		{
			get { return Serialize.BrushToString(EqhColor); }
			set { EqhColor = Serialize.StringToBrush(value); }
		}

		[NinjaScriptProperty]
		[XmlIgnore]
		[Display(Name = "EQL", GroupName = "Equal Highs / Lows", Order = 810)]
		public Brush EqlColor { get; set; }

		[Browsable(false)]
		public string EqlColorSerializable
		{
			get { return Serialize.BrushToString(EqlColor); }
			set { EqlColor = Serialize.StringToBrush(value); }
		}

		[NinjaScriptProperty]
		[Display(Name = "line", GroupName = "Equal Highs / Lows", Order = 811)]
		public DashStyleHelper EqhlStyle { get; set; }

		[NinjaScriptProperty]
		[Range(1, 3)]
		[Display(Name = "line width", GroupName = "Equal Highs / Lows", Order = 812)]
		public int EqhlWidth { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "label size", GroupName = "Equal Highs / Lows", Order = 813)]
		public ICTlevelsTextSize EqLabelSize { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Show current session name", GroupName = "Macros and Events", Order = 901)]
		public bool DoZones { get; set; }

		[NinjaScriptProperty]
		[XmlIgnore]
		[Display(Name = "session name color", GroupName = "Macros and Events", Order = 902)]
		public Brush ZoneColor { get; set; }

		[Browsable(false)]
		public string ZoneColorSerializable
		{
			get { return Serialize.BrushToString(ZoneColor); }
			set { ZoneColor = Serialize.StringToBrush(value); }
		}

		[NinjaScriptProperty]
		[Display(Name = "Show current macro name", GroupName = "Macros and Events", Order = 903)]
		public bool DoMacros { get; set; }

		[NinjaScriptProperty]
		[XmlIgnore]
		[Display(Name = "macro name color", GroupName = "Macros and Events", Order = 904)]
		public Brush MacroColor { get; set; }

		[Browsable(false)]
		public string MacroColorSerializable
		{
			get { return Serialize.BrushToString(MacroColor); }
			set { MacroColor = Serialize.StringToBrush(value); }
		}

		[NinjaScriptProperty]
		[Display(Name = "flag macros with background color", GroupName = "Macros and Events", Order = 905)]
		public bool DoMacroBg { get; set; }

		[NinjaScriptProperty]
		[XmlIgnore]
		[Display(Name = "macro background color", GroupName = "Macros and Events", Order = 906)]
		public Brush MacroBgColor { get; set; }

		[Browsable(false)]
		public string MacroBgColorSerializable
		{
			get { return Serialize.BrushToString(MacroBgColor); }
			set { MacroBgColor = Serialize.StringToBrush(value); }
		}

		[NinjaScriptProperty]
		[Display(Name = "Show active silver bullet", GroupName = "Macros and Events", Order = 907)]
		public bool DoSilver { get; set; }

		[NinjaScriptProperty]
		[XmlIgnore]
		[Display(Name = "silver bullet color", GroupName = "Macros and Events", Order = 908)]
		public Brush SilverColor { get; set; }

		[Browsable(false)]
		public string SilverColorSerializable
		{
			get { return Serialize.BrushToString(SilverColor); }
			set { SilverColor = Serialize.StringToBrush(value); }
		}

		#endregion
	}
}
