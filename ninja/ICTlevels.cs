// ICTlevels for NinjaTrader 8 (NinjaScript indicator, C#)
//
// Day/week/month levels, Asia/London/NY session ranges (killzones), NDOG/NWOG/RTHOG gaps with
// quadrants and standard deviations, premarket range, EQH/EQL, PO3 lines, TGIF levels, macros,
// silver bullet and a session/macro status text. Draws levels only: no plots, no signals, no alerts.
//
// Port of the TradingView Pine Script v6 indicator ICTlevels.txt (md5 d83834d3fbab817754763faa623ba49d),
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
			public bool islvl;      // [LVL] day or session (levels by importance, body levels)
			// [LVL] highest / lowest candle body around the extreme and its time; taken after it was decided
			public double bhi = double.NaN, blo = double.NaN;
			public DateTime bht, blt;
			public bool bhtk, bltk;
			// [LVL] candle (bucket) number of the high / low; swing: 1 yes, -1 no, 0 not known yet
			public int hbk = -1, lbk = -1, hsw, lsw;
			// [LVL] flat top / bottom on 15m candles (REH / REL) and on chart candles (reh / rel)
			public bool hrel, lrel, hrelc, lrelc;
			// [LVL] REH / REL with another day / session level, text of that combined label ("": shown by another)
			public bool hx, lx, bhx, blx;
			public string hxt = "", lxt = "", bhxt = "", blxt = "";
			// [LVL] the same level as the high / low (body level) of other zones: one level, not a REH / REL; its group
			// and the rank to use (the most important one of the group, -1 none)
			public bool hc, lc, bhc, blc;
			public int hcg = -1, lcg = -1, hfrk = -1, lfrk = -1;
			public bool bhOn, blOn;  // [LVL] body levels drawn (last final bar)
			public string sig = "";  // [LVL] state the levels were last drawn with
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
			public bool imp;    // [ATT] last NDOG (or the gap that shows it when combined)
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
			public double pl = double.NaN; // price of the left (earlier) swing of the EQH / EQL
			public double pr = double.NaN; // price of the right (later) swing
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
		// [ATT] session rectangles: fill transparency, outline only, outline transparency
		private int rtrF, rtrB;
		private bool rbord;
		private Dictionary<string, Brush> attCache = new Dictionary<string, Brush>();
		private Dictionary<ICTlevelsTextSize, SimpleFont> fontCache = new Dictionary<ICTlevelsTextSize, SimpleFont>();
		// [LVL] candles of max(15m, chart timeframe): number of the current one, the last six (high, low, open,
		// close, first bar time) and the average range of the completed ones (EMA)
		private const int BodyMax = 6;
		private int bkN;
		private long bkId = long.MinValue;
		private List<double> bkH = new List<double>(), bkL = new List<double>(), bkO = new List<double>(), bkC = new List<double>();
		private List<DateTime> bkT = new List<DateTime>();
		private double bkRng = double.NaN;
		// [MON] previous months (year * 12 + month) whose high / low was taken by a completed day candle that opened
		// and closed beyond it
		private HashSet<int> mTkH = new HashSet<int>(), mTkL = new HashSet<int>();
		private double dMo = double.NaN, dXo = double.NaN;  // lowest / highest 15m open of the current day
		private bool flatHi, flatLo;  // [LVL] flat top / bottom of this chart candle and the previous one
		private SimpleFont fTg, fPm, fEq, fNormal, fTable;

		// per bar
		private DateTime barTz;
		private int tsec, tsecPrev;
		private DayOfWeek dow, dowPrev;
		private bool newDay, newWeek, mktEndsPrev;
		private DateTime winStart;
		private bool winSet;

		// daily series (completed bars)
		private Series<double> cAtr;   // chart ATR(14), RMA of the cleaned true range

		// bad data: a wick longer than BadAtr daily ATRs is a bad print, not price; the high / low is taken at the
		// body. The daily ATR (datr) is built from daily candles cleaned with the ATR so far, so a bad print does not
		// inflate it. Hc / Lc read the series in progress.
		private double CH(int k, int i)
		{
			double h = Highs[k][i], b = Math.Max(Opens[k][i], Closes[k][i]);
			return BadAtr > 0 && !double.IsNaN(datr) && h - b > BadAtr * datr ? b : h;
		}

		private double CL(int k, int i)
		{
			double l = Lows[k][i], b = Math.Min(Opens[k][i], Closes[k][i]);
			return BadAtr > 0 && !double.IsNaN(datr) && b - l > BadAtr * datr ? b : l;
		}

		private double Hc(int i)
		{
			return CH(BarsInProgress, i);
		}

		private double Lc(int i)
		{
			return CL(BarsInProgress, i);
		}
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
				Attenuation = 2;
				BadAtr = 6;
				DoBody = true;
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
				NGaps = 10;
				DoNdog = true;
				DoNwog = true;
				DoRthog = true;
				DoRthogQ = true;
				DoRthogOct = true;
				RthogQMin = 50.0;
				DoRthogPo3 = true;
				RthogPo3Min = 50.0;
				RthogPo3Num = 4;
				RthogAuto = true;
				DoGapRect = true;
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
				PmQMin = 50.0;
				DoPmPo3 = true;
				PmPo3Min = 50.0;
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
				DoMacroBg = true;
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
				AddDataSeries(BarsPeriodType.Minute, 15); // BarsArray[3]: 15m opens for the taken month levels
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

			fTg = MkFont(TgLabelSize, false);
			fPm = MkFont(PmLabelSize, false);
			fEq = MkFont(EqLabelSize, true);
			fNormal = MkFont(ICTlevelsTextSize.Normal, false);
			fTable = MkFont(ICTlevelsTextSize.Large, false);

			attCache.Clear();
			fontCache.Clear();
			// [ATT] day lines minor, day / session max and min normal; session rectangles: filled and lighter at 1,
			// outline only from 2 on (fainter each level)
			rtrF = Attenuation == 1 ? AttT(RectTransparency, 1) : RectTransparency;
			rbord = RectBorderOnly || Attenuation >= 2;
			rtrB = Attenuation >= 2 ? (int)Math.Floor(Math.Min(100.0, RectTransparency / 2 + (100 - RectTransparency / 2) * (Attenuation - 1) * 0.2))
				: RectTransparency / 2;
			daysty = new Sty(DayLineStyle, AttW(DayWidth, 2), AttL(DayColor, 2));
			daymaxsty = new Sty(DayMaxMinStyle, AttW(DayMaxMinWidth, 1), AttL(DayMaxColor, 1));
			dayminsty = new Sty(DayMaxMinStyle, AttW(DayMaxMinWidth, 1), AttL(DayMinColor, 1));
			sesmaxsty = new Sty(SesStyle, AttW(SesWidth, 1), AttL(SesMaxColor, 1));
			sesminsty = new Sty(SesStyle, AttW(SesWidth, 1), AttL(SesMinColor, 1));
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
			bkN = 0;
			bkId = long.MinValue;
			bkH.Clear();
			bkL.Clear();
			bkO.Clear();
			bkC.Clear();
			bkT.Clear();
			bkRng = double.NaN;
			mTkH.Clear();
			mTkL.Clear();
			dMo = dXo = double.NaN;

			cAtr = new Series<double>(this);
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
			if (BarsInProgress == 3)
			{
				On15mBar();
				return;
			}
			if (BarsInProgress == 1)
			{
				OnDailyBar();
				return;
			}
			if (BarsInProgress != 0)
				return;
			{
				double ch = Hc(0), cl = Lc(0);
				double tr = CurrentBar < 1 ? ch - cl : Math.Max(ch, Close[1]) - Math.Min(cl, Close[1]);
				cAtr[0] = CurrentBar < 1 || double.IsNaN(cAtr[1]) ? tr : (cAtr[1] * 13 + tr) / 14;
			}
			if (!tfok)
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

			// ---------------------------------------------------- [LVL] candles of max(15m, chart timeframe)
			bool bkNew = BkUpdate();
			ComputeFlats();

			// ---------------------------------------------------- days, sessions, premarket
			if (inWin)
			{
				if (newDay)
				{
					IZone d = MkZone("D", null);
					d.isday = true;
					d.islvl = true;
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
						s.islvl = true;
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
				if (bkNew)
				{
					foreach (IZone z in days)
						SwTest(z);
					foreach (IZone z in zones)
						SwTest(z);
				}
				RunActive();
				RunMon();
			}

			// ---------------------------------------------------- TGIF (state only, drawn on the final bar)
			if (inWin && DoTgif)
			{
				if (newWeek)
				{
					int d = double.IsNaN(twO) ? 0 : TgEval(twH, twL, Close[1]);
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
					twH = Hc(0);
					twL = Lc(0);
				}
				else if (!double.IsNaN(twO))
				{
					if (newDay && dow == DayOfWeek.Friday)
					{
						tgDir = TgEval(twH, twL, Close[1]);
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
					twH = Math.Max(twH, Hc(0));
					twL = Math.Min(twL, Lc(0));
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
					if (!tgHit && (tgDir == 1 ? Lc(0) <= tgV1 : Hc(0) >= tgV1))
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
						ag.xhi = Math.Max(ag.xhi, Hc(0));
						ag.xlo = Math.Min(ag.xlo, Lc(0));
					}
				}
			}
			// automatic stddevs: extremes of the latest premarket range from its end until the market close
			if (preOn && PmAuto && premarkets.Count > 0)
			{
				IZone az = premarkets[premarkets.Count - 1];
				if (az.tend.HasValue && Time[0] > az.tend.Value && (!az.tclose.HasValue || Time[0] < az.tclose.Value))
				{
					az.xhi = double.IsNaN(az.xhi) ? Hc(0) : Math.Max(az.xhi, Hc(0));
					az.xlo = double.IsNaN(az.xlo) ? Lc(0) : Math.Min(az.xlo, Lc(0));
				}
			}

			// ---------------------------------------------------- EQH / EQL
			if (inWin && DoEqhl)
			{
				double tol = EqTol * Nz(cAtr[0]);
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
			if (DoMacroBg && tfsecs > 0 && tfsecs < 900) // not on 15m and above
			{
				foreach (ZI m in macros)
				{
					if (m.Inside(tsec))
					{
						BackBrush = AttL(MacroBgColor, 1);
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
			{
				double dh = Hc(0), dl = Lc(0);
				double tr = CurrentBars[1] < 1 ? dh - dl : Math.Max(dh, Closes[1][1]) - Math.Min(dl, Closes[1][1]);
				datr = double.IsNaN(datr) ? tr : (datr * 13 + tr) / 14;
			}
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

		// [MON] a previous month high (low) is taken once a completed day after that month closes above (below) it
		// and at least one of its 15m candles opens above (below) it. Checked on the first 15m bar of each session,
		// for the day that just ended (its last 15m close, its lowest / highest 15m open).
		private void On15mBar()
		{
			if (CurrentBars[3] < 1)
			{
				dMo = dXo = Opens[3][0];
				return;
			}
			if (BarsArray[3].IsFirstBarOfSession)
			{
				if (!double.IsNaN(dMo))
					MonTaken(Times[3][1], Closes[3][1], dMo, dXo);
				dMo = dXo = Opens[3][0];
			}
			else
			{
				dMo = Math.Min(dMo, Opens[3][0]);
				dXo = Math.Max(dXo, Opens[3][0]);
			}
		}

		private void MonTaken(DateTime dt, double dc, double mo, double xo)
		{
			if (BarsArray.Length < 4 || CurrentBars[2] < 0)
				return;
			for (int k = 0; k < 3 && k <= CurrentBars[2]; k++)
			{
				DateTime mt = Times[2][k];
				DateTime ms = new DateTime(mt.Year, mt.Month, 1);
				if (dt < ms.AddMonths(1))
					continue;
				int key = ms.Year * 12 + ms.Month;
				if (dc > CH(2, k) && xo > CH(2, k))
					mTkH.Add(key);
				if (dc < CL(2, k) && mo < CL(2, k))
					mTkL.Add(key);
			}
		}

		#endregion

		#region Final bar drawing

		private void FinalDraw()
		{
			if (DoReh && DoDayLabel && DoDayMaxMin)
				MarkRel(days, RelTol * Nz(datr));
			// [LVL] REH / REL between day and session levels (same tolerance as the flat tops / bottoms), body levels
			// shown (newest first, sessions then days, at most BodyMax per side); zones whose levels changed redraw
			foreach (IZone z in zones.Concat(days))
			{
				z.hx = false;
				z.lx = false;
				z.bhx = false;
				z.blx = false;
				z.hc = false;
				z.lc = false;
				z.bhc = false;
				z.blc = false;
				z.hcg = -1;
				z.lcg = -1;
			}
			double xtol = Math.Max(2 * TickSize, 0.1 * Nz(bkRng));
			XMark(true, xtol);
			XMark(false, xtol);
			int nbh = 0, nbl = 0;
			foreach (List<IZone> arr in new[] { zones, days })
			{
				for (int k = arr.Count - 1; k >= 0; k--)
				{
					IZone z = arr[k];
					z.bhOn = BodyOk(z, true) && nbh < BodyMax;
					z.blOn = BodyOk(z, false) && nbl < BodyMax;
					nbh += z.bhOn ? 1 : 0;
					nbl += z.blOn ? 1 : 0;
				}
			}
			// [LVL] a high / low shared with other zones takes the most important rank of them
			Dictionary<string, int> grk = new Dictionary<string, int>();
			foreach (IZone z in zones.Concat(days))
			{
				z.hfrk = -1;
				z.lfrk = -1;
				foreach (bool hi in new[] { true, false })
				{
					int g = hi ? z.hcg : z.lcg;
					if (g < 0 || !z.drawmaxmin)
						continue;
					Brush c; DashStyleHelper st; int w; ICTlevelsTextSize sz;
					int rk = ZSide(z, hi, hi ? z.bhOn : z.blOn, ICTlevelsTextSize.Tiny, -1, out c, out st, out w, out sz);
					string key = (hi ? "h" : "l") + g;
					int cur;
					grk[key] = grk.TryGetValue(key, out cur) ? Math.Min(cur, rk) : rk;
				}
			}
			foreach (IZone z in zones.Concat(days))
			{
				int r;
				if (z.hcg >= 0 && grk.TryGetValue("h" + z.hcg, out r))
					z.hfrk = r;
				if (z.lcg >= 0 && grk.TryGetValue("l" + z.lcg, out r))
					z.lfrk = r;
				string sig = string.Join("|", z.hsw, z.lsw, z.hrel, z.lrel, z.hrelc, z.lrelc, z.hx, z.lx, z.hxt, z.lxt,
					z.bhOn, z.blOn, z.bhx, z.blx, z.bhxt, z.blxt, z.bhi, z.blo, z.hmode, z.lmode, z.hc, z.lc, z.bhc, z.blc,
					z.hfrk, z.lfrk);
				if (sig != z.sig)
				{
					z.sig = sig;
					z.dirty = true;
				}
			}
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
			// [ATT] the last NDOG is important (and the gap that shows it when combined)
			for (int i = 0; i < dgaps.Count; i++)
				dgaps[i].imp = i == dgaps.Count - 1;
			foreach (IGap g in wgaps)
				g.imp = false;
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
					OctRight(rthogs[i], AttL(RthogColor, 1));    // octants: latest RTHOG only (removed at the close, like the quadrants)
				}
				else if (i == rthogs.Count - 2)
					RRight(rthogs[i], RthogOldColor);
			}
			if (preOn && rthogs.Count > 0 && premarkets.Count > 0)
				CombinePo3(rthogs[rthogs.Count - 1], premarkets[premarkets.Count - 1], PmCTol * Nz(datr));
			if (DoEqhl)
			{
				DrawEq(hpv, true, "", AttL(EqhColor, 1));
				DrawEq(lpv, false, "", AttL(EqlColor, 1));
				if (DoEqDaily)
				{
					DrawEq(hpvD, true, " D", AttL(EqhColor, 1));
					DrawEq(lpvD, false, " D", AttL(EqlColor, 1));
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
				Brush c = AttL(tgDir == 1 ? TgDownColor : TgUpColor, 0);
				DateTime x1 = TRight(Xoff);
				DLine(TagPfx + "tg1", tgT0, tgV1, x1, tgV1, c, TgStyle, AttW(TgWidth, 0));
				DText(TagPfx + "tg1L", "TGIF" + (TgP1 * 100).ToString("0.#", CultureInfo.InvariantCulture), x1, tgV1, c, fTg, 0);
				DLine(TagPfx + "tg2", tgT0, tgV2, x1, tgV2, c, TgStyle, AttW(TgWidth, 0));
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
				int key = ms.Year * 12 + ms.Month;
				GetM(k * 3, ms, CH(2, k), MonMaxColor, "max" + suf, first, mTkH.Contains(key));
				GetM(k * 3 + 1, ms, CL(2, k), MonMinColor, "min" + suf, first, mTkL.Contains(key));
				GetM(k * 3 + 2, tc, Closes[2][k], MonCloseColor, "close" + suf, first, false);
			}
		}

		// month line and label, normal rank; a taken high / low is drawn in the close color and minor
		private void GetM(int idx, DateTime t, double p, Brush c, string txt, DateTime first, bool tk)
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
			m.c = tk ? AttL(MonCloseColor, 2) : AttL(c, 1);      // [ATT] months: normal rank
			m.tc = m.c;
			m.sty = MonStyle;
			m.w = tk ? AttW(MonWidth, 2) : AttW(MonWidth, 1);
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
				hi = Hc(0),
				lo = Lc(0),
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
			foreach (string s in new[] { "v", "v2", "hi", "lo", "hiL", "loL", "eq", "eqL", "op", "opL", "r", "bh", "bhL", "bl", "blL" })
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
					DVLine(tg + "v", z.tstart, z.isweek ? AttL(DayColor, 1) : z.lnsty.col, z.isweek ? WeekLineStyle : z.lnsty.sty,
						z.isweek ? AttW(WeekWidth, 1) : z.lnsty.wid);
				if (z.drawvlines && z.tend.HasValue && !drawn.Contains(tg + "v2"))
					DVLine(tg + "v2", z.tend.Value.AddSeconds(tfsecs), z.lnsty.col, z.lnsty.sty, z.lnsty.wid);
			}
			if (z.drawmaxmin)
			{
				DateTime hx = z.hidonet.HasValue ? Mn(z.hidonet.Value, re) : re;
				DateTime lx = z.lodonet.HasValue ? Mn(z.lodonet.Value, re) : re;
				ICTlevelsTextSize sz0 = z.isday ? DayLabelSize : SesLabelSize;
				// [LVL] days and sessions: style by importance, and their body levels
				Brush hc = z.maxsty.col, lc = z.minsty.col;
				DashStyleHelper hs = z.maxsty.sty, ls = z.minsty.sty;
				int hw = z.maxsty.wid, lw = z.minsty.wid;
				ICTlevelsTextSize hsz = sz0, lsz = sz0;
				if (z.islvl)
				{
					ZSide(z, true, z.bhOn, sz0, z.hfrk, out hc, out hs, out hw, out hsz);
					ZSide(z, false, z.blOn, sz0, z.lfrk, out lc, out ls, out lw, out lsz);
				}
				DLine(tg + "hi", z.hit, z.hi, hx, z.hi, hc, hs, hw);
				DLine(tg + "lo", z.lot, z.lo, lx, z.lo, lc, ls, lw);
				ZBody(z, true, z.islvl && z.bhOn, re);
				ZBody(z, false, z.islvl && z.blOn, re);
				if (z.drawlbl)
				{
					// REH / REL first: "REH Asia", "reh day"; between levels, one combined label ("REH Asia body + day")
					string hb = ZName(z, true), lb0 = ZName(z, false);
					string hn = z.hrel ? "REH " + hb : z.hrelc ? "reh " + hb : hb + (z.isday ? " high" : " hi");
					string ln = z.lrel ? "REL " + lb0 : z.lrelc ? "rel " + lb0 : lb0 + (z.isday ? " low" : " lo");
					if (z.hx || z.hc)
						hn = z.hxt;
					if (z.lx || z.lc)
						ln = z.lxt;
					if (z.isday)
					{
						if (z.hmode == 1 && !(z.hx || z.hc))
							hn = "day REHs";
						if (z.lmode == 1 && !(z.lx || z.lc))
							ln = "day RELs";
					}
					TinyLbl(tg + "hiL", !z.hidonet.HasValue && (z.hmode != 2 || z.hx || z.hc) && hn.Length > 0, re, z.hi, hn, hc, FontOf(hsz));
					TinyLbl(tg + "loL", !z.lodonet.HasValue && (z.lmode != 2 || z.lx || z.lc) && ln.Length > 0, re, z.lo, ln, lc, FontOf(lsz));
				}
			}
			if (z.isday && DoEq)
			{
				double eq = (z.hi + z.lo) / 2.0;
				DateTime ex = z.extendt ?? re;
				DateTime x0 = Mn(z.hit, z.lot);
				DLine(tg + "eq", x0, eq, ex, eq, AttL(EqColor, 2), EqStyle, AttW(EqWidth, 2));
				DText(tg + "eqL", "eq", ex, eq, AttL(EqColor, 2), fNormal, 0);
			}
			if (z.isday && DoOpen)
			{
				// [ATT] the last midnight open is important, older ones minor
				int rk = days.Count > 0 && z == days[days.Count - 1] ? 0 : 2;
				DLine(tg + "op", z.tstart, z.op, xe, z.op, AttL(OpenColor, rk), OpenStyle, AttW(OpenWidth, rk));
				DText(tg + "opL", "M.O.", xe, z.op, AttL(OpenColor, rk), fNormal, 0);
			}
			if (z.drawrect && z.lnsty != null)
			{
				// [ATT] filled (lighter at attenuation 1), or outline only (rectangle border, or attenuation 2 and up)
				if (rbord)
					DRectB(tg + "r", z.tstart, z.hi, xe, z.lo, z.lnsty.col, rtrB, Attenuation < 3 ? 2 : 1);
				else
					DRect(tg + "r", z.tstart, z.hi, xe, z.lo, z.lnsty.col, 100 - rtrF);
			}
		}

		// [LVL] name of a day / session level for its label: Asia, London, ..., day or week
		private static string ZName(IZone z, bool hi)
		{
			if (!z.isday)
				return z.name;
			bool wk = hi ? double.IsNaN(z.pwhi) || z.hi >= z.pwhi : double.IsNaN(z.pwlo) || z.lo <= z.pwlo;
			return wk ? "week" : "day";
		}

		// [LVL] the body level of side hi is shown: high (low) untaken and beyond the body, body not taken later
		// (also for the current day / session, once its body level is decided)
		private bool BodyOk(IZone z, bool hi)
		{
			bool ok = z.drawmaxmin && DoBody;
			return hi ? ok && !z.hidonet.HasValue && !double.IsNaN(z.bhi) && z.bhi < z.hi && !z.bhtk
				: ok && !z.lodonet.HasValue && !double.IsNaN(z.blo) && z.blo > z.lo && !z.bltk;
		}

		// [LVL] style of the high (low) of a day / session: rank of an untaken high / low: swing or flat (REH / REL)
		// 0 (important: line 1 wider, label 1 size up; 2 and 2 for a REH / REL), not known yet 1, not a swing 1
		// (sessions) or 2 (days); a taken one is 1. A body level drawn below (above) it takes the importance: the
		// high (low) is dotted meanwhile.
		// frk: rank to use instead (the most important one of a level shared with other zones), -1 none; returns the rank
		private int ZSide(IZone z, bool hi, bool bodyShown, ICTlevelsTextSize sz0, int frk, out Brush c, out DashStyleHelper sty,
			out int w, out ICTlevelsTextSize sz)
		{
			bool isrel = hi ? z.hrel || z.hrelc || z.hx : z.lrel || z.lrelc || z.lx;
			int sw = hi ? z.hsw : z.lsw;
			bool taken = hi ? z.hidonet.HasValue : z.lodonet.HasValue;
			int rk = taken ? 1 : sw > 0 || isrel ? 0 : sw == 0 ? 1 : z.isday ? 2 : 1;
			if (bodyShown)
				rk = Math.Max(rk, 1);
			if (frk >= 0)
				rk = frk;
			Brush c0 = z.isday ? (hi ? DayMaxColor : DayMinColor) : (hi ? SesMaxColor : SesMinColor);
			int w0 = z.isday ? DayMaxMinWidth : SesWidth;
			bool rel = rk == 0 && isrel;
			c = AttL(c0, rk);
			sty = bodyShown ? DashStyleHelper.Dot : (z.isday ? DayMaxMinStyle : SesStyle);
			w = rk == 0 ? Math.Min(4, AttW(w0, 0) + (rel ? 2 : 1)) : AttW(w0, rk);
			sz = rk == 0 ? (rel ? SizeUp(SizeUp(sz0)) : SizeUp(sz0)) : sz0;
			return rk;
		}

		// [LVL] body level of side hi (want: draw it): important, solid (wider, label 1 size up; 2 and 2 when it is
		// a REH / REL with another level)
		private void ZBody(IZone z, bool hi, bool want, DateTime x)
		{
			string tg = ZTag(z) + (hi ? "bh" : "bl");
			if (!want)
			{
				Rm(tg);
				Rm(tg + "L");
				return;
			}
			double p = hi ? z.bhi : z.blo;
			bool bx = hi ? z.bhx : z.blx;
			Brush c = AttL(z.isday ? (hi ? DayMaxColor : DayMinColor) : (hi ? SesMaxColor : SesMinColor), 0);
			int w = Math.Min(4, AttW(2, 0) + (bx ? 1 : 0));
			ICTlevelsTextSize bsz = SizeUp(z.isday ? DayLabelSize : SesLabelSize);
			if (bx)
				bsz = SizeUp(bsz);
			bool bc = hi ? z.bhc : z.blc;
			string txt = bx || bc ? (hi ? z.bhxt : z.blxt) : (z.isday ? "day" : z.name) + (hi ? " body hi" : " body lo");
			DLine(tg, hi ? z.bht : z.blt, p, x, p, c, DashStyleHelper.Solid, w);
			if (txt.Length > 0)
				DText(tg + "L", txt, x, p, c, FontOf(bsz), 0);
			else
				Rm(tg + "L");
		}

		// [LVL] untaken highs (lows) of days and sessions, and their body levels, within tol of another one are REHs
		// (RELs): hx / lx, bhx / blx; the outermost level of a group carries the combined label, the others none.
		// Levels of different zones from the same candle at the same price (the Asia low that is also the week low)
		// are one level, not a REH / REL by themselves: hc / lc, bhc / blc, one label "Asia / week low".
		private void XMark(bool hi, double tol)
		{
			List<double> v = new List<double>();
			List<IZone> zs = new List<IZone>();
			List<bool> isb = new List<bool>();
			List<DateTime> ot = new List<DateTime>();
			foreach (List<IZone> arr in new[] { zones, days })
			{
				foreach (IZone z in arr)
				{
					if (z.drawmaxmin && !(hi ? z.hidonet.HasValue : z.lodonet.HasValue))
					{
						v.Add(hi ? z.hi : z.lo);
						zs.Add(z);
						isb.Add(false);
						ot.Add(hi ? z.hit : z.lot);
					}
					if (BodyOk(z, hi))
					{
						v.Add(hi ? z.bhi : z.blo);
						zs.Add(z);
						isb.Add(true);
						ot.Add(hi ? z.bht : z.blt);
					}
				}
			}
			int n = v.Count;
			if (n < 2)
				return;
			int[] cid = new int[n];
			for (int a = 0; a < n; a++)
				cid[a] = a;
			for (int a = 0; a < n - 1; a++)
			{
				for (int b = a + 1; b < n; b++)
				{
					if (cid[a] != cid[b] && Math.Abs(v[a] - v[b]) <= tol)
					{
						int from = cid[b], xto = cid[a];
						for (int k = 0; k < n; k++)
							if (cid[k] == from)
								cid[k] = xto;
					}
				}
			}
			// origin of each level: the first one of its group from the same candle at the same price
			int[] org = new int[n];
			for (int k = 0; k < n; k++)
			{
				int o = k;
				for (int j = 0; j < k && o == k; j++)
					if (cid[j] == cid[k] && isb[j] == isb[k] && ot[j] == ot[k] && v[j] == v[k])
						o = j;
				org[k] = o;
			}
			for (int c = 0; c < n; c++)
			{
				int lead = -1, no = 0;
				bool multi = false, anyday = false, grel = false, grelc = false;
				IZone z0 = null;
				for (int k = 0; k < n; k++)
				{
					if (cid[k] != c)
						continue;
					if (z0 == null)
						z0 = zs[k];
					else if (zs[k] != z0)
						multi = true;
					if (org[k] == k)
						no += 1;
					if (lead < 0 || (hi ? v[k] > v[lead] : v[k] < v[lead]))
						lead = k;
					anyday = anyday || zs[k].isday;
					if (!isb[k])
					{
						grel = grel || (hi ? zs[k].hrel : zs[k].lrel);
						grelc = grelc || (hi ? zs[k].hrelc : zs[k].lrelc);
					}
				}
				if (!multi)
					continue;
				// names of each origin joined with " / ", origins with " + "
				string parts = "";
				for (int o = 0; o < n; o++)
				{
					if (cid[o] != c || org[o] != o)
						continue;
					string nms = "";
					for (int k = 0; k < n; k++)
						if (cid[k] == c && org[k] == o)
							nms += (nms.Length == 0 ? "" : " / ") + ZName(zs[k], hi);
					parts += (parts.Length == 0 ? "" : " + ") + nms + (isb[o] ? " body" : "");
				}
				bool rel = no > 1;
				string txt;
				if (rel)
					txt = (hi ? "REH " : "REL ") + parts;
				else if (isb[lead])
					txt = parts + (hi ? " hi" : " lo");
				else
				{
					string pfx = grel ? (hi ? "REH " : "REL ") : grelc ? (hi ? "reh " : "rel ") : "";
					txt = pfx + parts + (pfx.Length > 0 ? "" : anyday ? (hi ? " high" : " low") : (hi ? " hi" : " lo"));
				}
				for (int k = 0; k < n; k++)
				{
					if (cid[k] != c)
						continue;
					IZone z = zs[k];
					string t = k == lead ? txt : "";
					if (isb[k])
					{
						if (hi) { z.bhx = rel; z.bhc = !rel; z.bhxt = t; }
						else { z.blx = rel; z.blc = !rel; z.blxt = t; }
					}
					else if (hi) { z.hx = rel; z.hc = !rel; z.hcg = rel ? -1 : c; z.hxt = t; }
					else { z.lx = rel; z.lc = !rel; z.lcg = rel ? -1 : c; z.lxt = t; }
				}
			}
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
			if (Hc(0) >= z.hi)
			{
				z.hit = Time[0];
				z.hi = Hc(0);
				z.hbk = bkN;
				z.hsw = 0;
				z.hrel = false;
				z.bhi = double.NaN;
				z.hrelc = flatHi && Time[1] >= z.tstart;
			}
			else if (z.hit == Time[1] && flatHi)
				z.hrelc = true;
			if (Lc(0) <= z.lo)
			{
				z.lot = Time[0];
				z.lo = Lc(0);
				z.lbk = bkN;
				z.lsw = 0;
				z.lrel = false;
				z.blo = double.NaN;
				z.lrelc = flatLo && Time[1] >= z.tstart;
			}
			else if (z.lot == Time[1] && flatLo)
				z.lrelc = true;
			if (!double.IsNaN(z.bhi) && Hc(0) > z.bhi)
				z.bhtk = true;
			if (!double.IsNaN(z.blo) && Lc(0) < z.blo)
				z.bltk = true;
		}

		// [LVL] bucket of the bar (candles of max(15m, chart timeframe), by bar open time); true on a new one
		private bool BkUpdate()
		{
			long bkTicks = (long)Math.Max(900, tfsecs) * TimeSpan.TicksPerSecond;
			long cur = Time[0].AddSeconds(-tfsecs).Ticks / bkTicks;
			if (cur != bkId)
			{
				if (bkH.Count > 0)
				{
					double r = bkH[bkH.Count - 1] - bkL[bkL.Count - 1];
					bkRng = double.IsNaN(bkRng) ? r : bkRng * 0.9 + r * 0.1;
				}
				bkId = cur;
				bkN += 1;
				bkH.Add(Hc(0));
				bkL.Add(Lc(0));
				bkO.Add(Open[0]);
				bkC.Add(Close[0]);
				bkT.Add(Time[0]);
				if (bkH.Count > 6)
				{
					bkH.RemoveAt(0);
					bkL.RemoveAt(0);
					bkO.RemoveAt(0);
					bkC.RemoveAt(0);
					bkT.RemoveAt(0);
				}
				return true;
			}
			int k = bkH.Count - 1;
			bkH[k] = Math.Max(bkH[k], Hc(0));
			bkL[k] = Math.Min(bkL[k], Lc(0));
			bkC[k] = Close[0];
			return false;
		}

		// [LVL] flat top / bottom of this chart candle and the previous one: highs (lows) within tolerance (2 ticks
		// or a tenth of the chart ATR) and both upper (lower) wicks within that tolerance or a fifth of the range
		private void ComputeFlats()
		{
			double tick = TickSize;
			double tol0 = Math.Max(2 * tick, 0.1 * Nz(cAtr[0]));
			double tol1 = Math.Max(2 * tick, 0.1 * Nz(cAtr[1]));
			double wm0 = Math.Max(tol0, 0.2 * (Hc(0) - Lc(0)));
			double wm1 = Math.Max(tol1, 0.2 * (Hc(1) - Lc(1)));
			double uw0 = Hc(0) - Math.Max(Open[0], Close[0]);
			double uw1 = Hc(1) - Math.Max(Open[1], Close[1]);
			double lw0 = Math.Min(Open[0], Close[0]) - Lc(0);
			double lw1 = Math.Min(Open[1], Close[1]) - Lc(1);
			flatHi = Math.Abs(Hc(0) - Hc(1)) <= tol0 && uw0 <= wm0 && uw1 <= wm1;
			flatLo = Math.Abs(Lc(0) - Lc(1)) <= tol0 && lw0 <= wm0 && lw1 <= wm1;
		}

		// [LVL] candles i and j (bucket indexes) make a flat top (bottom): highs (lows) within tolerance (2 ticks or
		// a tenth of the average candle range) and both upper (lower) wicks within that tolerance or a fifth of the
		// candle range
		private bool BkFlat(int i, int j, bool hi)
		{
			double tol = Math.Max(2 * TickSize, 0.1 * Nz(bkRng));
			double wi = hi ? bkH[i] - Math.Max(bkO[i], bkC[i]) : Math.Min(bkO[i], bkC[i]) - bkL[i];
			double wj = hi ? bkH[j] - Math.Max(bkO[j], bkC[j]) : Math.Min(bkO[j], bkC[j]) - bkL[j];
			double d = hi ? Math.Abs(bkH[i] - bkH[j]) : Math.Abs(bkL[i] - bkL[j]);
			return d <= tol && wi <= Math.Max(tol, 0.2 * (bkH[i] - bkL[i])) && wj <= Math.Max(tol, 0.2 * (bkH[j] - bkL[j]));
		}

		// [LVL] a high (low) is a swing when the candles before and after it have lower highs (higher lows); decided
		// when the candle after it completes. Same time: flat (REH / REL) with the candle before or after it. The
		// body level is the highest (lowest) body of its candle and two candles before and after it, decided when
		// the last of those completes.
		private void SwTest(IZone z)
		{
			int n = bkH.Count;
			if (z.hsw == 0 && z.hbk >= 0 && z.hbk == bkN - 2)
			{
				z.hsw = n >= 4 && bkH[n - 4] < z.hi && bkH[n - 2] < z.hi ? 1 : -1;
				z.hrel = n >= 4 && (BkFlat(n - 4, n - 3, true) || BkFlat(n - 3, n - 2, true));
			}
			if (z.lsw == 0 && z.lbk >= 0 && z.lbk == bkN - 2)
			{
				z.lsw = n >= 4 && bkL[n - 4] > z.lo && bkL[n - 2] > z.lo ? 1 : -1;
				z.lrel = n >= 4 && (BkFlat(n - 4, n - 3, false) || BkFlat(n - 3, n - 2, false));
			}
			const int k = 2;
			if (z.hbk >= 0 && z.hbk == bkN - 1 - k && n >= 2)
			{
				z.bhi = double.NaN;
				z.bhtk = false;
				for (int j = Math.Max(n - 2 - 2 * k, 0); j <= n - 2; j++)
				{
					double bt = Math.Max(bkO[j], bkC[j]);
					if (double.IsNaN(z.bhi) || bt > z.bhi)
					{
						z.bhi = bt;
						z.bht = bkT[j];
					}
				}
			}
			if (z.lbk >= 0 && z.lbk == bkN - 1 - k && n >= 2)
			{
				z.blo = double.NaN;
				z.bltk = false;
				for (int j = Math.Max(n - 2 - 2 * k, 0); j <= n - 2; j++)
				{
					double bb = Math.Min(bkO[j], bkC[j]);
					if (double.IsNaN(z.blo) || bb < z.blo)
					{
						z.blo = bb;
						z.blt = bkT[j];
					}
				}
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
				if (!z.hidonet.HasValue && Hc(0) > z.hi)
				{
					z.hidonet = Time[0];
					z.dirty = true;
				}
				if (!double.IsNaN(z.bhi) && Hc(0) > z.bhi)
					z.bhtk = true;
				if (!double.IsNaN(z.blo) && Lc(0) < z.blo)
					z.bltk = true;
				if (!z.lodonet.HasValue && Lc(0) < z.lo)
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
			// [ATT] quadrants and stddevs minor, PMKT high / low normal
			Brush c2 = AttL(PmColor, 2), c1 = AttL(PmColor, 1);
			int w2 = AttW(PmWidth, 2), w1 = AttW(PmWidth, 1);
			if (r > 0)
			{
				if (DoPmQ && r / 4.0 > PmQMin)
				{
					for (int k = 1; k <= 3; k++)
					{
						string pt = k == 1 ? "dq" : k == 2 ? "ce" : "uq";
						z.qlines.Add(MkLL(tg + "q" + k, x0, x1, z.lo + k * r / 4.0, c2, k == 2 ? PmCeStyle : PmQuadStyle,
							w2, "PMKT " + pt + " " + nm, c2, fPm, false));
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
								z.sdlines.Add(MkLL(tg + "su" + i, x0, x1, z.hi + i * u, c2, PmPo3Style, w2,
									"PMKT" + SdName(i, u, r, 1), c2, fPm, false));
								z.sdnames.Add(SdName(i, u, r, 1));
							}
							if (i <= nd)
							{
								z.sdlines.Add(MkLL(tg + "sd" + i, x0, x1, z.lo - i * u, c2, PmPo3Style, w2,
									"PMKT" + SdName(i, u, r, -1), c2, fPm, false));
								z.sdnames.Add(SdName(i, u, r, -1));
							}
						}
					}
				}
				// high and low of the range, solid lines, whenever quadrants or stddevs are drawn
				if (z.qlines.Count > 0 || z.sdlines.Count > 0)
				{
					z.qlines.Add(MkLL(tg + "qh", x0, x1, z.hi, c1, DashStyleHelper.Solid, w1, "PMKT hi " + nm, c1, fPm, false));
					z.qlines.Add(MkLL(tg + "ql", x0, x1, z.lo, c1, DashStyleHelper.Solid, w1, "PMKT lo " + nm, c1, fPm, false));
				}
			}
			RemoveStale(oldQ, z.qlines);
			RemoveStale(oldS, z.sdlines);
		}

		#endregion

		#region TGIF

		private int TgEval(double h, double l, double c)
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
			// [ATT] NWOGs and the last NDOG important, older NDOGs minor; NWOG: thinner lines and lighter fill at 3 and 4
			int rk = g.isw || g.imp ? 0 : 2;
			int rkw = g.isw && Attenuation >= 3 ? 1 : rk;
			Brush col = AttL(st.col, rk);
			int wid = AttW(st.wid, rkw);
			string tg = GTag(g);
			DateTime x = TRight(Xoff);
			DLine(tg + "h", t0, h, x, h, col, st.sty, wid);
			DLine(tg + "l", t0, l, x, l, col, st.sty, wid);
			bool small = Math.Abs(h - l) <= tol;
			string b = GName(g);
			string dt = " " + g.dstr;
			DText(tg + "hL", small ? b + dt : b + " hi" + dt, x, small ? (h + l) / 2.0 : h, col, fNormal, 0);
			if (small)
				Rm(tg + "lL");
			else
				DText(tg + "lL", b + " lo" + dt, x, l, col, fNormal, 0);
			if (DoGapRect)
				DRect(tg + "r", t0, h, x, l, st.col, 100 - AttT(GapRectTransparency, rkw));
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
					rep.imp = rep.imp || g.imp;
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
						w.imp = w.imp || d.imp;
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
					g.octs.Add(MkLL(tg + "oc" + k, x0, x1, p + k * ody, AttL(RthogColor, 1), RthogQuadStyle, 1,
						"RTHOG " + k + "/8 " + nm, AttL(RthogColor, 1), fNormal, false));
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
			g.cline = MkLL(RTag(g) + "cl", t, TRight(Xoff), c, AttL(RthogColor, 0), RthogStyle, AttW(RthogWidth, 0),
				"new RTHOG settlement", AttL(RthogColor, 0), fNormal, false);
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
				Brush cm = AttL(RthogColor, 1);
				int wm = AttW(RthogWidth, 1);
				g.dq = MkLL(tg + "dq", x0, x1, p + qdy, cm, RthogQuadStyle, wm, "RTHOG dq " + nm, cm, fNormal, false);
				g.uq = MkLL(tg + "uq", x0, x1, p + 3 * qdy, cm, RthogQuadStyle, wm, "RTHOG uq " + nm, cm, fNormal, false);
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
						g.po3.Add(MkLL(tg + "su" + i, x0, x1, pu0 + i * u, AttL(RthogColor, 2), RthogPo3Style, AttW(RthogWidth, 2),
							"RTHOG" + SdName(i, u, gap, 1), AttL(RthogColor, 2), fNormal, false));
						g.po3names.Add(SdName(i, u, gap, 1));
					}
					if (i <= nd)
					{
						g.po3.Add(MkLL(tg + "sd" + i, x0, x1, pd0 - i * u, AttL(RthogColor, 2), RthogPo3Style, AttW(RthogWidth, 2),
							"RTHOG" + SdName(i, u, gap, -1), AttL(RthogColor, 2), fNormal, false));
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
			g.xhi = Hc(0);
			g.xlo = Lc(0);
			string tg = RTag(g);
			DateTime x1 = TRight(Xoff);
			DateTime x0 = Time[0].AddSeconds(-(double)tfsecs * rdx);
			string nm = DateStr();
			double mid = (g.cl + g.op) / 2.0;
			bool up = g.op > g.cl;
			Brush c0 = AttL(RthogColor, 0);
			int w0 = AttW(RthogWidth, 0);
			g.mline = MkLL(tg + "ce", x0, x1, mid, c0, RthogStyle, w0, "RTHOG ce " + nm, c0, fNormal, false);
			g.opline = MkLL(tg + "op", Time[0], x1, g.op, c0, RthogStyle, w0,
				(up ? "RTHOG hi " : "RTHOG lo ") + nm, c0, fNormal, false);
			if (g.cline != null)
			{
				g.cline.txt = (up ? "RTHOG lo " : "RTHOG hi ") + nm;
				DrawLabel(g.cline);
			}
			if (DoGapRect && Attenuation < 2)   // [ATT] no RTHOG rectangle from attenuation 2 on
			{
				g.rect = true;
				g.rx0 = x0;
				DRect(tg + "rect", x0, g.op, x1, g.cl, RthogColor, 100 - AttT(GapRectTransparency, 0));
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
			// [ATT] old RTHOGs: minor
			Brush co = AttL(RthogOldColor, 2), to = AttL(PineBlack, 2);
			int wo = AttW(RthogWidth, 2);
			g.cline = MkLL(tg + "ocl", ts.AddSeconds(-d), ts.AddSeconds(d), g.cl, co, RthogStyle, wo,
				"rthog.cl", to, fNormal, false);
			g.cline2 = MkLL(tg + "ocl2", te.AddSeconds(-d), te.AddSeconds(d), g.cl, co, RthogStyle, wo,
				"rthog.cl", to, fNormal, ExtendGaps);
			g.mline = MkLL(tg + "oce", te.AddSeconds(-d), te.AddSeconds(d), mid, co, RthogStyle, wo,
				"rthog.ce", to, fNormal, ExtendGaps);
			g.opline = MkLL(tg + "oop", te.AddSeconds(-d), te.AddSeconds(d), g.op, co, RthogStyle, wo,
				"rthog.op", to, fNormal, ExtendGaps);
		}

		// right-edge update, final bar only
		// [ATT] current and previous RTHOG important, quadrants normal, stddevs minor
		private void RRight(IRthog g, Brush c0)
		{
			if (g.isold)
				return;
			DateTime x = TRight(Xoff);
			Brush c = AttL(c0, 0), cm = AttL(c0, 1), cs = AttL(c0, 2);
			SetR(g.cline, x, c);
			SetR(g.mline, x, c);
			SetR(g.opline, x, c);
			SetR(g.uq, x, cm);
			SetR(g.dq, x, cm);
			if (g.rect)
				DRect(RTag(g) + "rect", g.rx0, g.op, x, g.cl, RthogColor, 100 - AttT(GapRectTransparency, 0));
			foreach (LL l in g.po3)
				SetR(l, x, cs);
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
			double c = Hc(len);
			for (int i = 1; i <= len; i++)
				if (Hc(len + i) >= c || Hc(len - i) >= c)
					return double.NaN;
			return c;
		}

		private double PivotLow(int len)
		{
			double c = Lc(len);
			for (int i = 1; i <= len; i++)
				if (Lc(len + i) <= c || Lc(len - i) <= c)
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
				bool swept = isHigh ? Hc(0) > v.p + slack : Lc(0) < v.p - slack;
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
				if (!m.eq)
					m.pl = m.p;
				m.eq = true;
				m.pr = p;
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
				if (isHigh ? Hc(0) > ps[i] : Lc(0) < ps[i])
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
					v.pl = q;
					v.pr = v.p;
					v.p = q;
					v.t = qt;
					TrimPivs(arr);
					break;
				}
			}
		}

		// label: EQH, or eqH when the right swing is higher than the left one (EQL / eqL: lower, for lows)
		private void DrawEq(List<IPiv> arr, bool isHigh, string sfx, Brush c)
		{
			DateTime x = TRight(Xoff);
			foreach (IPiv v in arr)
			{
				if (!v.eq)
					continue;
				bool up = !double.IsNaN(v.pl) && !double.IsNaN(v.pr) && (isHigh ? v.pr > v.pl : v.pr < v.pl);
				string txt = (up ? (isHigh ? "eqH" : "eqL") : (isHigh ? "EQH" : "EQL")) + sfx;
				string tg = PTag(v);
				DLine(tg + "ln", v.t, v.p, x, v.p, c, EqhlStyle, AttW(EqhlWidth, 1));
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

		// [ATT] dimming steps of a rank (0 important, 1 normal, 2 minor) at the attenuation level
		private int AttE(int rank)
		{
			int a = Math.Max(0, Math.Min(4, Attenuation));
			return a == 0 ? 0 : rank == 0 ? (a >= 4 ? 2 : a >= 2 ? 1 : 0) : rank == 1 ? a : a + 1;
		}

		// line / text color of a rank: more transparent (Pine attL), cached
		private Brush AttL(Brush c, int rank)
		{
			int e = AttE(rank);
			if (e == 0 || c == null)
				return c;
			string key = c.ToString() + "|" + c.Opacity.ToString(CultureInfo.InvariantCulture) + "|" + e;
			Brush b;
			if (!attCache.TryGetValue(key, out b))
			{
				b = c.Clone();
				b.Opacity = c.Opacity * (1.0 - Math.Min(e * 0.18, 0.9));
				b.Freeze();
				attCache[key] = b;
			}
			return b;
		}

		private int AttW(int w, int rank)
		{
			return Math.Max(1, w - (AttE(rank) >= 2 ? 1 : 0));
		}

		// fill transparency of a rank
		private int AttT(int t, int rank)
		{
			return (int)Math.Floor(Math.Min(100.0, t + (100 - t) * AttE(rank) * 0.2));
		}

		// one label size up (important levels)
		private static ICTlevelsTextSize SizeUp(ICTlevelsTextSize s)
		{
			return s >= ICTlevelsTextSize.Huge ? ICTlevelsTextSize.Huge : s + 1;
		}

		private SimpleFont FontOf(ICTlevelsTextSize s)
		{
			SimpleFont f;
			if (!fontCache.TryGetValue(s, out f))
			{
				f = MkFont(s, false);
				fontCache[s] = f;
			}
			return f;
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
				// outline only, no fill: the area color with half the transparency, 2 px wide
				Brush b = area.Clone();
				b.Opacity = (100 - (100 - op) / 2) / 100.0;
				b.Freeze();
				Rectangle r = Draw.Rectangle(this, tag, false, t1, y1, t2, y2, b, Brushes.Transparent, 0);
				if (r != null && r.OutlineStroke != null)
					r.OutlineStroke.Width = 2;
			}
			else
				Draw.Rectangle(this, tag, false, t1, y1, t2, y2, Brushes.Transparent, area, op);
			drawn.Add(tag);
		}

		// [ATT] rectangle outline only: the color with transparency transp, width w
		private void DRectB(string tag, DateTime t1, double y1, DateTime t2, double y2, Brush c, int transp, int w)
		{
			Brush b = c.Clone();
			b.Opacity = c.Opacity * (100 - Math.Max(0, Math.Min(100, transp))) / 100.0;
			b.Freeze();
			Rectangle r = Draw.Rectangle(this, tag, false, t1, y1, t2, y2, b, Brushes.Transparent, 0);
			if (r != null && r.OutlineStroke != null)
				r.OutlineStroke.Width = w;
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
		[Display(Name = "Zone: US / EU / Auto", GroupName = "Time & Settings", Order = 102)]
		public ICTlevelsZoneMode ZoneMode { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "compute for historic data use (no effect in NT8)", GroupName = "Time & Settings", Order = 101)]
		public bool ComputeVisible { get; set; }

		[NinjaScriptProperty]
		[Range(1, 500)]
		[Display(Name = "offset for margin labels (bars)", GroupName = "Time & Settings", Order = 103)]
		public int Xoff { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "show object cap warning (no effect in NT8)", GroupName = "Time & Settings", Order = 104)]
		public bool ShowCap { get; set; }

		[NinjaScriptProperty]
		[Range(0, 4)]
		[Display(Name = "Attenuation", GroupName = "Enable / Filter", Order = 0, Description = "Dims the drawings, 0: as configured, 4: the most dimmed (2 by default). Levels have three ranks: important (the last M.O., the untaken high / low of the previous day and of the last two ended sessions, the current and previous RTHOG, TGIF, NWOGs, the last NDOG, swing highs / lows and body levels), normal (months, other day and session max/min, week lines, current RTHOG quadrants and octants, PMKT high / low, EQH / EQL, macro background) and minor (day lines, eq, older M.O.s, older NDOGs, old RTHOGs, stddevs, PMKT quadrants). Each level dims minor drawings the most and important ones the least; widths go down by 1 when dimmed enough. Session rectangles: filled and lighter at 1, outline only from 2 on. Gap rectangles fade with the rank of their gap.")]
		public int Attenuation { get; set; }

		[NinjaScriptProperty]
		[Range(0, double.MaxValue)]
		[Display(Name = "Ignore wicks longer than (daily ATRs)", GroupName = "Enable / Filter", Order = 10, Description = "Bad data: a wick longer than this many daily ATRs (chart, daily and monthly candles) is taken as a bad print, and the candle's high or low is taken at its body: day, session, week and month highs / lows, swings, EQH / EQL and ranges leave it out. 0: off.")]
		public double BadAtr { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Month max/min/close (last 3 months)", GroupName = "Enable / Filter", Order = 1)]
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
		[Display(Name = "body high / low levels (days and sessions)", GroupName = "Day Settings", Order = 325, Description = "While the high (low) of a day or session is not taken, also draw a line at the highest (lowest) 15m candle body around it (two candles before and after) when that is below (above) the wick: price often takes the body level but not the wick. Removed once price takes the body later, or takes the high (low). The most recent 6 per side are drawn.")]
		public bool DoBody { get; set; }

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
		[Display(Name = "TGIF levels", GroupName = "Enable / Filter", Order = 2)]
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
		[Display(Name = "Sessions", GroupName = "Enable / Filter", Order = 3)]
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
		[Display(Name = "Session rectangles: border only", Description = "Draw only the outline of the session rectangles, no fill: 2 px wide, in the session color with half the transparency above", GroupName = "Session Settings", Order = 504)]
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

		// some opening gap kind is on (no switch of its own)
		private bool DoGaps { get { return DoNdog || DoNwog || DoRthog; } }

		[NinjaScriptProperty]
		[Range(1, 1000)]
		[Display(Name = "gaps to keep", GroupName = "Gaps and Ranges", Order = 602)]
		public int NGaps { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "NDOGs", GroupName = "Enable / Filter", Order = 5)]
		public bool DoNdog { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "NWOGs", GroupName = "Enable / Filter", Order = 6)]
		public bool DoNwog { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "RTHOGs (timeframes up to 15m)", GroupName = "Enable / Filter", Order = 7)]
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
		[Display(Name = "PMKT range (m5 or less)", GroupName = "Enable / Filter", Order = 8)]
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
		[Display(Name = "EQH / EQL", GroupName = "Enable / Filter", Order = 4)]
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
		[Display(Name = "Flag macros", GroupName = "Enable / Filter", Order = 9)]
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
