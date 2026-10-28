// ICTcotas - NinjaTrader 8 indicator
//
// Daily, weekly and monthly Fair Value Gaps (FVG / IFVG), Volume Imbalances (VI) and wick CE
// levels, plus FVG boxes (and optional 1H / 4H wick CE lines) of 15m / 1H / 4H / D / W / M candles.
// Each FVG / VI is drawn with high, low and CE (midpoint). Wicks are grouped (consecutive candles
// with a qualifying wick on the same side) and the CE of the longest wick of the group is drawn.
// FVGs closed through become IFVGs. Mitigated levels are removed.
//
// Port of the TradingView Pine Script v6 indicator ICTcotas.txt by franjballest
// (source md5 4089f053e285078f810d1e95f07652e7). Mozilla Public License 2.0.
//
// Install:
//   1. Copy this file to  Documents\NinjaTrader 8\bin\Custom\Indicators\ICTcotas.cs
//   2. Open NinjaTrader 8 > New > NinjaScript Editor, open ICTcotas and press F5 to compile.
//   3. Add "ICTcotas" to a chart (Indicators window).
//   The "#region NinjaScript generated code" factory block is intentionally not included:
//   NinjaTrader regenerates and appends it automatically on compile.
//
// Differences from the Pine version that could not be avoided:
//   - All six higher timeframe series (D, W, M, 15m, 60m, 240m) are always loaded, because
//     AddDataSeries cannot depend on inputs; the inputs only gate the logic. The D / W / M series
//     are loaded with a fixed bar count (600 / 160 / 60) so the first candle triples exist even on
//     short intraday charts, like request.security history in Pine.
//   - A completed HTF candle triple is processed on the HTF series' own bar close
//     (BarsInProgress == that series) instead of on the first chart bar of the next HTF candle.
//     The triple is the same, and mitigation of new levels starts on the next chart bar, as in Pine.
//   - NinjaTrader stamps bars with their close time (D / W / M with the session date), so the left
//     edge of a level (Pine: open time of the candle) is approximated: the previous candle's close
//     stamp for minute series, the day after the previous candle's date for D / W / M. Left edges
//     are still clamped to the first chart bar.
//   - D / W / M candles follow the instrument's NinjaTrader trading hours template, which may
//     differ from TradingView's exchange candles.
//   - With Calculate.OnEachTick / OnPriceChange, HTF detection, inversion and mitigation are
//     evaluated once per closed bar (no intrabar mitigation, since NinjaScript state does not roll
//     back like Pine's). Tick Replay is not supported for this.
//   - Objects are drawn on the last historical bar and on realtime bars (like barstate.islast),
//     with the right edge 'offset' bars in the future. The by-timeframe CE box is drawn as a line.
//     Label sizes map to font sizes 8 / 10 / 12 / 16 / 20 pt. There is no 500-object limit.
//   - Non time based charts (tick, volume, range...) count as lower than 15m. A month counts as
//     2628003 s as in Pine, so monthly levels are off on a 1-month chart.

#region Using declarations
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
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

// Enums live outside any namespace so the generated factory code can resolve them everywhere.
public enum ICTcotasMitigation
{
	Never,
	CETouched,
	FullyFilledWick,
	FullyFilledClose
}

public enum ICTcotasLabelSize
{
	Tiny,
	Small,
	Normal,
	Large,
	Huge
}

public enum ICTcotasOverlap
{
	MostRecent,
	Enclosing,
	KeepAll
}

public enum ICTcotasLabelPosition
{
	RightMargin,
	InsideLeft,
	InsideRight,
	Center,
	LeftOfBox
}

public enum ICTcotasFill
{
	Never,
	AtMargin,
	Always
}

public enum ICTcotasExtendTo
{
	For,
	ToStart,
	ToCreation
}

public enum ICTcotasLabelAnchor
{
	High,
	CE,
	Low
}

namespace NinjaTrader.NinjaScript.Indicators
{
	public class ICTcotas : Indicator
	{
		#region Constants and state

		private const string GGen = "General";
		private const string GCol = "Colors (by timeframe)";
		private const string GLbl = "Labels";
		private const string GFvg = "Fair Value Gaps";
		private const string GVi = "Volume Imbalances";
		private const string GWk = "Wick CE";
		private const string GMf = "FVGs and wick CE by timeframe (15m / 1H / 4H / D / W / M)";

		// series indexes (order of AddDataSeries in State.Configure)
		private const int SerDay = 1;
		private const int SerWeek = 2;
		private const int SerMonth = 3;
		private const int Ser15 = 4;
		private const int Ser60 = 5;
		private const int Ser240 = 6;
		private const int SerCount = 7;

		// by-timeframe candidates: 15m, 1H, 4H, D, W, M (kind 3 levels use tfi = 3 + index)
		private static readonly int[] MfSecs = { 900, 3600, 14400, 86400, 604800, 2628003 };
		private static readonly string[] MfNames = { "15m", "1H", "4H", "D", "W", "M" };
		private static readonly int[] MfSeries = { Ser15, Ser60, Ser240, SerDay, SerWeek, SerMonth };

		private class Lvl
		{
			public int Id;
			public int Kind;      // 0 FVG / IFVG, 1 volume imbalance, 2 wick, 3 FVG by timeframe, 4 ULW CE
			public int Tfi;       // 0 daily, 1 weekly, 2 monthly, 3..7 by-timeframe 15m..W
			public int Dir;       // 1 bullish / support, -1 bearish / resistance
			public double Hi;
			public double Lo;
			public double Ce;
			public DateTime T;    // left edge
			public double WSize = double.NaN;
			public bool Inv;      // FVG inverted into an IFVG
			public bool Dead;
			public bool HasTfm;
			public DateTime Tfm;  // formation time
			public HashSet<string> Tags = new HashSet<string>();
		}

		private List<Lvl> lvls;
		private Lvl[] wcur;              // current wick group level: tfi * 2 + side
		private DateTime[] lastKey;      // last processed bar time per series
		private HashSet<Lvl> flippedNow = new HashSet<Lvl>();   // inverted since the last RunMit (not mitigated then)
		private DateTime lastPrimaryKey;
		private DateTime tfirst;
		private int nextId;
		private bool dirty;

		private int tfsecs;
		private bool dayOk;
		private bool wkOk;
		private bool monOk;
		private int mmode;
		private bool[] mfOn;
		private bool[] wkOn;
		private Brush[] bullB;
		private Brush[] bearB;
		private SimpleFont lblFont;
		private SimpleFont mfFont;

		#endregion

		public override string DisplayName
		{
			get { return "ICTcotas"; }
		}

		protected override void OnStateChange()
		{
			if (State == State.SetDefaults)
			{
				Description = "Daily, weekly and monthly Fair Value Gaps, Volume Imbalances and wick CE levels (port of the Pine Script ICTcotas).";
				Name = "ICTcotas";
				Calculate = Calculate.OnBarClose;
				IsOverlay = true;
				DisplayInDataBox = false;
				DrawOnPricePanel = true;
				PaintPriceMarkers = false;
				IsSuspendedWhileInactive = true;
				ScaleJustification = NinjaTrader.Gui.Chart.ScaleJustification.Right;
				MaximumBarsLookBack = MaximumBarsLookBack.Infinite;

				// General
				DoDaily = true;
				DoWeekly = true;
				DoMonthly = true;
				WidthExtra = 1;
				Mitigation = ICTcotasMitigation.FullyFilledWick;

				// Colors (Pine v6 palette)
				DailyBull = MakeBrush(0x4C, 0xAF, 0x50);   // green
				DailyBear = MakeBrush(0xF2, 0x36, 0x45);   // red
				WeeklyBull = MakeBrush(0x08, 0x99, 0x81);  // teal
				WeeklyBear = MakeBrush(0xFF, 0x98, 0x00);  // orange
				MonthlyBull = MakeBrush(0x29, 0x62, 0xFF); // blue
				MonthlyBear = MakeBrush(0x9C, 0x27, 0xB0); // purple

				// Labels
				ShowLabels = true;
				LabelBold = true;
				LabelDirection = false;
				LabelSize = ICTcotasLabelSize.Normal;
				LabelOffset = 2;
				TextDaily = "D";
				TextWeekly = "W";
				TextMonthly = "M";
				TextHigh = "hi";
				TextLow = "lo";
				TextCE = "CE";

				// Fair Value Gaps
				DrawFvg = true;
				FvgKeep = 8;
				FvgMinSize = 0.0;
				FvgStyle = DashStyleHelper.Solid;
				FvgWidth = 1;
				FvgCEStyle = DashStyleHelper.Dot;
				FvgShowHigh = true;
				FvgShowLow = true;
				FvgShowCE = true;
				FvgNoFill = false;
				FvgTransparency = 88;
				FvgText = "FVG";
				DoIfvg = true;
				IfvgText = "IFVG";
				FvgOverlap = ICTcotasOverlap.MostRecent;
				FvgExtendVI = true;

				// Volume Imbalances
				DrawVi = true;
				ViKeep = 8;
				ViMinSize = 0.0;
				ViStyle = DashStyleHelper.Solid;
				ViWidth = 1;
				ViCEStyle = DashStyleHelper.Dot;
				ViShowHigh = true;
				ViShowLow = true;
				ViShowCE = true;
				ViNoFill = false;
				ViTransparency = 88;
				ViText = "VI";

				// Wick CE
				DrawWick = true;
				WickKeep = 8;
				WickMinPct = 30.0;
				WickStyle = DashStyleHelper.Dash;
				WickWidth = 1;
				WickTextUpper = "UW";
				WickTextLower = "LW";
				DrawUlw = true;
				UlwText = "ULW";

				// FVGs and wick CE by timeframe
				DrawMf = true;
				MfNext = 0;
				Mf15 = false;
				Mf1H = true;
				Mf4H = true;
				MfD = false;
				MfW = false;
				MfM = false;
				MfKeep = 6;
				MfMinSize = 0.0;
				Mf15Bull = MakeBrush(0x00, 0xE6, 0x76);  // lime
				Mf15Bear = MakeBrush(0x88, 0x0E, 0x4F);  // maroon
				Mf1HBull = MakeBrush(0x4C, 0xAF, 0x50);  // green
				Mf1HBear = MakeBrush(0xF2, 0x36, 0x45);  // red
				Mf4HBull = MakeBrush(0x08, 0x99, 0x81);  // teal
				Mf4HBear = MakeBrush(0xFF, 0x98, 0x00);  // orange
				MfDBull = MakeBrush(0x29, 0x62, 0xFF);   // blue
				MfDBear = MakeBrush(0xE0, 0x40, 0xFB);   // fuchsia
				MfWBull = MakeBrush(0x31, 0x1B, 0x92);   // navy
				MfWBear = MakeBrush(0x9C, 0x27, 0xB0);   // purple
				MfMBull = MakeBrush(0x80, 0x80, 0x00);   // olive
				MfMBear = MakeBrush(0x78, 0x7B, 0x86);   // gray
				MfStyle = DashStyleHelper.Solid;
				MfWidth = 1;
				MfFill = ICTcotasFill.AtMargin;
				MfTransparency = 88;
				MfShowFormation = true;
				MfShowCE = true;
				MfCEStyle = DashStyleHelper.Dot;
				MfLabels = true;
				MfLabelShowTf = true;
				MfLabelShowType = true;
				MfLabelShowDir = false;
				MfLabelSize = ICTcotasLabelSize.Normal;
				MfLabelPos = ICTcotasLabelPosition.InsideRight;
				MfLabelAnchor = ICTcotasLabelAnchor.CE;
				Wick1H = false;
				Wick4H = false;
				MfMargin = false;
				MfMarginOffset = 4;
				MfMarginWidth = 4;
				MfExtend = false;
				MfExtendBars = 20;
				MfExtendTo = ICTcotasExtendTo.For;
			}
			else if (State == State.Configure)
			{
				// Added unconditionally (inputs cannot reliably drive AddDataSeries). Index order matters.
				AddDataSeries(null, new BarsPeriod { BarsPeriodType = BarsPeriodType.Day, Value = 1 }, 600, null, null);   // 1
				AddDataSeries(null, new BarsPeriod { BarsPeriodType = BarsPeriodType.Week, Value = 1 }, 160, null, null);  // 2
				AddDataSeries(null, new BarsPeriod { BarsPeriodType = BarsPeriodType.Month, Value = 1 }, 60, null, null);  // 3
				AddDataSeries(BarsPeriodType.Minute, 15);   // 4
				AddDataSeries(BarsPeriodType.Minute, 60);   // 5
				AddDataSeries(BarsPeriodType.Minute, 240);  // 6
			}
			else if (State == State.DataLoaded)
			{
				lvls = new List<Lvl>();
				wcur = new Lvl[16];
				lastKey = new DateTime[SerCount];
				for (int i = 0; i < SerCount; i++)
					lastKey[i] = DateTime.MinValue;
				lastPrimaryKey = DateTime.MinValue;
				tfirst = DateTime.MinValue;
				nextId = 0;
				dirty = false;

				tfsecs = PeriodSeconds(BarsArray[0].BarsPeriod);
				// D, W and M levels only on lower chart timeframes, never the chart's own
				dayOk = DoDaily && tfsecs < 86400;
				wkOk = DoWeekly && tfsecs < 604800;
				monOk = DoMonthly && tfsecs < 2592000;
				mmode = Mitigation == ICTcotasMitigation.CETouched ? 1
					: Mitigation == ICTcotasMitigation.FullyFilledWick ? 2
					: Mitigation == ICTcotasMitigation.FullyFilledClose ? 3 : 0;

				bool[] sel = { Mf15, Mf1H, Mf4H, MfD, MfW, MfM };
				mfOn = new bool[6];
				wkOn = new bool[6];
				for (int i = 0; i < 6; i++)
				{
					int s = MfSecs[i];
					bool on = DrawMf && MfOk(s) && sel[i];
					if (DrawMf && s > tfsecs && MfNext > 0)
					{
						int rank = 0;
						for (int j = 0; j < i; j++)
							if (MfSecs[j] > tfsecs)
								rank++;
						if (rank < MfNext)
							on = true;
					}
					mfOn[i] = on;
					wkOn[i] = DrawWick && MfOk(s) && ((i == 1 && Wick1H) || (i == 2 && Wick4H));
				}

				bullB = new Brush[9];
				bearB = new Brush[9];
				bullB[0] = Frz(DailyBull);   bearB[0] = Frz(DailyBear);
				bullB[1] = Frz(WeeklyBull);  bearB[1] = Frz(WeeklyBear);
				bullB[2] = Frz(MonthlyBull); bearB[2] = Frz(MonthlyBear);
				bullB[3] = Frz(Mf15Bull);    bearB[3] = Frz(Mf15Bear);
				bullB[4] = Frz(Mf1HBull);    bearB[4] = Frz(Mf1HBear);
				bullB[5] = Frz(Mf4HBull);    bearB[5] = Frz(Mf4HBear);
				bullB[6] = Frz(MfDBull);     bearB[6] = Frz(MfDBear);
				bullB[7] = Frz(MfWBull);     bearB[7] = Frz(MfWBear);
				bullB[8] = Frz(MfMBull);     bearB[8] = Frz(MfMBear);

				lblFont = new SimpleFont("Arial", FontSize(LabelSize)) { Bold = LabelBold };
				mfFont = new SimpleFont("Arial", FontSize(MfLabelSize)) { Bold = LabelBold };
			}
		}

		protected override void OnBarUpdate()
		{
			if (BarsInProgress == 0)
				OnPrimaryBar();
			else
				OnHtfBar(BarsInProgress);
		}

		#region Bar handlers

		// Returns false when nothing should be processed now. off = bars ago of the just closed bar.
		private bool ClosedOffset(out int off)
		{
			if (Calculate == Calculate.OnBarClose || State == State.Historical)
			{
				off = 0;
				return true;
			}
			off = 1;
			return IsFirstTickOfBar;
		}

		private void OnPrimaryBar()
		{
			if (CurrentBars[0] == 0)
			{
				tfirst = Times[0][0];
				if (!(dayOk || wkOk || monOk))
					Draw.TextFixed(this, "ICTc_note", "ICTcotas: use a chart timeframe lower than 1M",
						TextPosition.BottomRight, Brushes.Orange, new SimpleFont("Arial", 10),
						Brushes.Transparent, Brushes.Transparent, 0);
			}

			int off;
			if (ClosedOffset(out off) && CurrentBars[0] >= off)
			{
				DateTime key = Times[0][off];
				if (key != lastPrimaryKey)
				{
					lastPrimaryKey = key;
					RunMit(Closes[0][off], Highs[0][off], Lows[0][off]);
				}
			}

			// draw only at the end of history and on realtime bars (Pine: barstate.islast)
			bool atEnd = State == State.Realtime || CurrentBars[0] >= BarsArray[0].Count - 2;
			if (atEnd && (dirty || IsFirstTickOfBar))
			{
				DrawAll();
				dirty = false;
			}
		}

		// A higher timeframe bar closed: process the completed triple 1 = [off], 2 = [off+1], 3 = [off+2]
		private void OnHtfBar(int k)
		{
			if (CurrentBars[0] < 1)   // as in Pine, nothing is processed before the chart has bars
				return;
			int off;
			if (!ClosedOffset(out off))
				return;
			if (CurrentBars[k] < off + 2)
				return;
			DateTime key = Times[k][off];
			if (key == lastKey[k])
				return;
			lastKey[k] = key;

			if (k == SerDay && dayOk)
				ProcessTf(0, k, off);
			if (k == SerWeek && wkOk)
				ProcessTf(1, k, off);
			if (k == SerMonth && monOk)
				ProcessTf(2, k, off);

			for (int i = 0; i < 6; i++)
			{
				if (MfSeries[i] != k)
					continue;
				if (mfOn[i])
					AddFvgs(3, 3 + i, MfMinSize, k, off);
				if (wkOn[i])
					AddWicks(3 + i, k, off);
			}

			// FVG -> IFVG and the close mitigation rule only on a close of the level's own timeframe,
			// never on a chart close
			double c = Closes[k][off];
			if (k == SerDay && dayOk)
				CloseTf(0, c);
			if (k == SerWeek && wkOk)
				CloseTf(1, c);
			if (k == SerMonth && monOk)
				CloseTf(2, c);
			for (int i = 0; i < 6; i++)
				if (MfSeries[i] == k && (mfOn[i] || wkOn[i]))
					CloseTf(3 + i, c);
			dirty = true;
		}

		#endregion

		#region Helpers

		private static Brush MakeBrush(byte r, byte g, byte b)
		{
			SolidColorBrush br = new SolidColorBrush(Color.FromRgb(r, g, b));
			br.Freeze();
			return br;
		}

		private static Brush Frz(Brush b)
		{
			if (b == null)
				return Brushes.Gray;
			if (b.IsFrozen)
				return b;
			Brush c = b.Clone();
			c.Freeze();
			return c;
		}

		private static int FontSize(ICTcotasLabelSize s)
		{
			switch (s)
			{
				case ICTcotasLabelSize.Tiny: return 8;
				case ICTcotasLabelSize.Small: return 10;
				case ICTcotasLabelSize.Large: return 16;
				case ICTcotasLabelSize.Huge: return 20;
				default: return 12;
			}
		}

		// only timeframes above the chart are used, never the chart's own
		private bool MfOk(int s)
		{
			return s > tfsecs;
		}

		private static int PeriodSeconds(BarsPeriod bp)
		{
			switch (bp.BarsPeriodType)
			{
				case BarsPeriodType.Second: return bp.Value;
				case BarsPeriodType.Minute: return bp.Value * 60;
				case BarsPeriodType.Day: return bp.Value * 86400;
				case BarsPeriodType.Week: return bp.Value * 604800;
				case BarsPeriodType.Month: return bp.Value * 2628003;
				case BarsPeriodType.Year: return bp.Value * 31536000;
				default: return 0;   // tick, volume, range, renko...: lower than any listed timeframe
			}
		}

		// Approximate open time of the candle 'barsAgo' bars back on series k (needs barsAgo + 1 bars)
		private DateTime CandleStart(int k, int barsAgo)
		{
			DateTime prev = Times[k][barsAgo + 1];
			if (k == SerDay || k == SerWeek || k == SerMonth)
				return prev.Date.AddDays(1);
			return prev;
		}

		private Brush TCol(int tfi, int d)
		{
			return d == 1 ? bullB[tfi] : bearB[tfi];
		}

		private DashStyleHelper KSty(int k)
		{
			return k == 0 ? FvgStyle : k == 1 ? ViStyle : WickStyle;
		}

		private DashStyleHelper KCeSty(int k)
		{
			return k == 0 ? FvgCEStyle : k == 1 ? ViCEStyle : WickStyle;
		}

		private int KWid(int k, int tfi)
		{
			return (k == 0 ? FvgWidth : k == 1 ? ViWidth : WickWidth) + (tfi < 3 ? tfi * WidthExtra : 0);
		}

		// part: 0 high, 1 low, 2 CE. Wick levels only show the CE
		private bool KShow(int k, int part)
		{
			if (k == 0)
				return part == 0 ? FvgShowHigh : part == 1 ? FvgShowLow : FvgShowCE;
			if (k == 1)
				return part == 0 ? ViShowHigh : part == 1 ? ViShowLow : ViShowCE;
			return part == 2;
		}

		private bool KFill(int k)
		{
			return k == 0 ? !FvgNoFill : k == 1 ? !ViNoFill : false;
		}

		private string LblTxt(Lvl l, string part)
		{
			string nm = l.Kind == 0 ? (l.Inv ? IfvgText : FvgText) : l.Kind == 1 ? ViText : l.Kind == 4 ? UlwText
				: (l.Dir == -1 ? WickTextUpper : WickTextLower);
			string sg = LabelDirection && l.Kind < 2 ? (l.Dir == 1 ? "+" : "-") : "";
			string tf = l.Tfi >= 3 ? MfNames[l.Tfi - 3] : l.Tfi == 0 ? TextDaily : l.Tfi == 1 ? TextWeekly : TextMonthly;
			return tf + " " + nm + sg + " " + part;
		}

		private void Kill(Lvl l)
		{
			l.Dead = true;
			foreach (string tag in l.Tags)
				RemoveDrawObject(tag);
			l.Tags.Clear();
		}

		private string Tag(Lvl l, string part)
		{
			string tag = "ICTc_" + l.Id + "_" + part;
			l.Tags.Add(tag);
			return tag;
		}

		#endregion

		#region Levels

		// overlapping FVGs of the same timeframe and direction: returns false if the new one is discarded
		private bool FvgOk(int kind, int tfi, int dir, double lo, double hi)
		{
			bool ok = true;
			if (FvgOverlap != ICTcotasOverlap.KeepAll)
			{
				if (FvgOverlap == ICTcotasOverlap.Enclosing)
				{
					foreach (Lvl o in lvls)
						if (o.Kind == kind && o.Tfi == tfi && o.Dir == dir && !o.Inv && !o.Dead)
							if (lo <= o.Hi && hi >= o.Lo && o.Lo <= lo && o.Hi >= hi)
								ok = false;
				}
				if (ok)
				{
					for (int i = lvls.Count - 1; i >= 0; i--)
					{
						Lvl o = lvls[i];
						if (o.Kind == kind && o.Tfi == tfi && o.Dir == dir && !o.Inv && !o.Dead)
						{
							if (lo <= o.Hi && hi >= o.Lo)
							{
								Kill(o);
								lvls.RemoveAt(i);
							}
						}
					}
				}
			}
			return ok;
		}

		// returns null when an FVG is discarded
		private Lvl AddLvl(int kind, int tfi, int dir, double lo, double hi, DateTime t)
		{
			if ((kind == 0 || kind == 3) && !FvgOk(kind, tfi, dir, lo, hi))
				return null;

			Lvl l = new Lvl
			{
				Id = nextId++,
				Kind = kind,
				Tfi = tfi,
				Dir = dir,
				Hi = hi,
				Lo = lo,
				Ce = (hi + lo) / 2.0,
				T = t > tfirst ? t : tfirst
			};
			lvls.Add(l);

			int keep = kind == 0 ? FvgKeep : kind == 1 ? ViKeep : kind == 3 ? MfKeep : WickKeep;
			int cnt = 0;
			foreach (Lvl v in lvls)
				if (v.Kind == kind && v.Tfi == tfi && !v.Dead)
					cnt++;
			if (cnt > keep)
			{
				for (int i = 0; i < lvls.Count; i++)
				{
					Lvl o = lvls[i];
					if (o.Kind == kind && o.Tfi == tfi && !o.Dead)
					{
						Kill(o);
						lvls.RemoveAt(i);
						break;
					}
				}
			}
			return l;
		}

		// one step of the wick group state machine (side 0 = upper wick, 1 = lower wick)
		private void WickStep(int tfi, int side, bool q, double sz, double lo, double hi, DateTime t)
		{
			int idx = tfi * 2 + side;
			Lvl cur = wcur[idx];
			if (!q)
			{
				wcur[idx] = null;
			}
			else if (cur == null || (cur.Dead && sz > cur.WSize))
			{
				Lvl n = AddLvl(2, tfi, side == 0 ? -1 : 1, lo, hi, t);
				if (n != null)
					n.WSize = sz;
				wcur[idx] = n;
			}
			else if (sz > cur.WSize)
			{
				cur.Lo = lo;
				cur.Hi = hi;
				cur.Ce = (lo + hi) / 2.0;
				cur.T = t > tfirst ? t : tfirst;
				cur.WSize = sz;
			}
		}

		// FVGs of one completed candle triple on series k (1 newest = [off], 3 oldest = [off+2]),
		// with the VI extension: any gap between the body of candle 2 and the body of candle 1 or 3
		// widens the range, also when the wicks do not overlap (a gap)
		private void AddFvgs(int kind, int tfi, double minsz, int k, int off)
		{
			double o1 = Opens[k][off], h1 = Highs[k][off], l1 = Lows[k][off], c1 = Closes[k][off];
			double o2 = Opens[k][off + 1], c2 = Closes[k][off + 1];
			double o3 = Opens[k][off + 2], h3 = Highs[k][off + 2], l3 = Lows[k][off + 2], c3 = Closes[k][off + 2];
			DateTime t2 = CandleStart(k, off + 1);
			DateTime tform = Times[0][0];

			double topP = Math.Max(o2, c2);
			double botP = Math.Min(o2, c2);
			double topC = Math.Max(o1, c1);
			double botC = Math.Min(o1, c1);
			double top3 = Math.Max(o3, c3);
			double bot3 = Math.Min(o3, c3);

			if (l1 > h3 && l1 - h3 >= minsz)
			{
				double flo = h3;
				double fhi = l1;
				if (FvgExtendVI)
				{
					if (botP > top3 && botP - top3 >= ViMinSize)
					{
						flo = Math.Min(flo, top3);
						fhi = Math.Max(fhi, botP);
					}
					if (botC > topP && botC - topP >= ViMinSize)
					{
						flo = Math.Min(flo, topP);
						fhi = Math.Max(fhi, botC);
					}
				}
				Lvl nb = AddLvl(kind, tfi, 1, flo, fhi, t2);
				if (nb != null)
				{
					nb.Tfm = tform;
					nb.HasTfm = true;
				}
			}
			if (h1 < l3 && l3 - h1 >= minsz)
			{
				double flo = h1;
				double fhi = l3;
				if (FvgExtendVI)
				{
					if (topP < bot3 && bot3 - topP >= ViMinSize)
					{
						flo = Math.Min(flo, topP);
						fhi = Math.Max(fhi, bot3);
					}
					if (topC < botP && botP - topC >= ViMinSize)
					{
						flo = Math.Min(flo, topC);
						fhi = Math.Max(fhi, botP);
					}
				}
				Lvl ns = AddLvl(kind, tfi, -1, flo, fhi, t2);
				if (ns != null)
				{
					ns.Tfm = tform;
					ns.HasTfm = true;
				}
			}
		}

		// wick groups of the completed candle [off] of series k
		private void AddWicks(int tfi, int k, int off)
		{
			double o1 = Opens[k][off], h1 = Highs[k][off], l1 = Lows[k][off], c1 = Closes[k][off];
			DateTime t1 = CandleStart(k, off);
			double topC = Math.Max(o1, c1);
			double botC = Math.Min(o1, c1);
			double rng = h1 - l1;
			double uw = h1 - topC;
			double lw = botC - l1;
			bool uq = rng > 0 && uw >= WickMinPct / 100.0 * rng;
			bool lq = rng > 0 && lw >= WickMinPct / 100.0 * rng;
			WickStep(tfi, 0, uq, uw, topC, h1, t1);
			WickStep(tfi, 1, lq, lw, l1, botC, t1);
			// ULW CE: both wicks of a D / W / M candle are long, level halfway between their CEs
			if (DrawUlw && tfi < 3 && uq && lq)
			{
				double p = ((topC + h1) / 2 + (l1 + botC) / 2) / 2;
				AddLvl(4, tfi, c1 >= p ? 1 : -1, p, p, t1);
			}
		}

		// one completed daily, weekly or monthly candle: 1 = [off], 2 = [off+1], 3 = [off+2]
		private void ProcessTf(int tfi, int k, int off)
		{
			double o1 = Opens[k][off], h1 = Highs[k][off], l1 = Lows[k][off], c1 = Closes[k][off];
			double o2 = Opens[k][off + 1], h2 = Highs[k][off + 1], l2 = Lows[k][off + 1], c2 = Closes[k][off + 1];
			DateTime t1 = CandleStart(k, off);
			double topP = Math.Max(o2, c2);
			double botP = Math.Min(o2, c2);
			double topC = Math.Max(o1, c1);
			double botC = Math.Min(o1, c1);

			// no FVG / IFVG lines for D, W or M when the same timeframe is drawn as boxes (redundant)
			if (DrawFvg && !mfOn[tfi + 3])
				AddFvgs(0, tfi, FvgMinSize, k, off);
			if (DrawVi)
			{
				if (botC > topP && l1 <= h2 && botC - topP >= ViMinSize)
					AddLvl(1, tfi, 1, topP, botC, t1);
				if (topC < botP && h1 >= l2 && botP - topC >= ViMinSize)
					AddLvl(1, tfi, -1, topC, botP, t1);
			}
			if (DrawWick)
				AddWicks(tfi, k, off);
		}

		// a candle of timeframe tfi closed at c: live FVGs of that timeframe closed through become IFVGs,
		// and with the Fully filled (close) rule the other levels of that timeframe closed through are removed
		private void CloseTf(int tfi, double c)
		{
			for (int i = lvls.Count - 1; i >= 0; i--)
			{
				Lvl l = lvls[i];
				if (l.Dead || l.Tfi != tfi)
					continue;
				bool fvgLive = (l.Kind == 0 || l.Kind == 3) && DoIfvg && !l.Inv;
				bool through = (l.Dir == 1 && c < l.Lo) || (l.Dir == -1 && c > l.Hi);
				if (fvgLive)
				{
					if (through)
					{
						l.Inv = true;
						l.Dir = -l.Dir;
						flippedNow.Add(l);
						dirty = true;
					}
				}
				else if (mmode == 3 && through && !flippedNow.Contains(l))
				{
					Kill(l);
					lvls.RemoveAt(i);
					dirty = true;
				}
			}
		}

		// checked on every closed chart bar: CE / wick mitigation (inversion and the close rule are in CloseTf)
		private void RunMit(double close, double high, double low)
		{
			for (int i = lvls.Count - 1; i >= 0; i--)
			{
				Lvl l = lvls[i];
				if (l.Dead)
				{
					lvls.RemoveAt(i);
					continue;
				}
				bool fvgLive = (l.Kind == 0 || l.Kind == 3) && DoIfvg && !l.Inv;
				bool flipped = flippedNow.Contains(l);
				if (mmode > 0 && mmode < 3 && !fvgLive && !flipped)
				{
					bool m;
					if (l.Dir == 1)
						m = mmode == 1 ? low <= l.Ce : low <= l.Lo;
					else
						m = mmode == 1 ? high >= l.Ce : high >= l.Hi;
					if (m)
					{
						Kill(l);
						lvls.RemoveAt(i);
						dirty = true;
					}
				}
			}
			flippedNow.Clear();
		}

		#endregion

		#region Drawing (primary series only)

		// bars ago (on the primary series) of the first bar at or after t
		private int BarsAgoOf(DateTime t)
		{
			int idx = BarsArray[0].GetBar(t);
			if (idx < 0)
				idx = 0;
			if (idx > CurrentBars[0])
				idx = CurrentBars[0];
			return CurrentBars[0] - idx;
		}

		private void DrawAll()
		{
			foreach (Lvl l in lvls)
			{
				if (l.Dead)
					continue;
				if (l.Kind == 3)
				{
					if (MfMargin)
						DrawMM(l);
					else
						DrawM(l);
				}
				else
					DrawL(l);
			}
		}

		private void DrawLabel(string tag, int barsAgo, double y, string txt, Brush c, SimpleFont font, TextAlignment align)
		{
			Draw.Text(this, tag, false, txt, barsAgo, y, 0, c, font, align,
				Brushes.Transparent, Brushes.Transparent, 0);
		}

		// timeframe levels (kinds 0 to 2): optional fill, hi / lo / CE lines and labels
		private void DrawL(Lvl l)
		{
			int sba = BarsAgoOf(l.T);
			int eba = -LabelOffset;
			Brush c = TCol(l.Tfi, l.Dir);
			int w = KWid(l.Kind, l.Tfi);
			DashStyleHelper s1 = KSty(l.Kind);
			DashStyleHelper s2 = KCeSty(l.Kind);

			if (KFill(l.Kind))
			{
				int opacity = 100 - (l.Kind == 0 ? FvgTransparency : ViTransparency);
				Draw.Rectangle(this, Tag(l, "bx"), false, sba, l.Hi, eba, l.Lo, Brushes.Transparent, c, opacity);
			}
			if (KShow(l.Kind, 0))
			{
				Draw.Line(this, Tag(l, "hl"), false, sba, l.Hi, eba, l.Hi, c, s1, w);
				if (ShowLabels)
					DrawLabel(Tag(l, "hb"), eba, l.Hi, LblTxt(l, TextHigh), c, lblFont, TextAlignment.Left);
			}
			if (KShow(l.Kind, 1))
			{
				Draw.Line(this, Tag(l, "ll"), false, sba, l.Lo, eba, l.Lo, c, s1, w);
				if (ShowLabels)
					DrawLabel(Tag(l, "lb"), eba, l.Lo, LblTxt(l, TextLow), c, lblFont, TextAlignment.Left);
			}
			if (KShow(l.Kind, 2))
			{
				Draw.Line(this, Tag(l, "cl"), false, sba, l.Ce, eba, l.Ce, c, s2, w);
				if (ShowLabels)
					DrawLabel(Tag(l, "cb"), eba, l.Ce, LblTxt(l, TextCE), c, lblFont, TextAlignment.Left);
			}
		}

		// margin column of timeframe i: its rank among the enabled timeframes
		private int MfCol(int i)
		{
			int c = 0;
			for (int j = 0; j < i; j++)
				if (mfOn[j])
					c++;
			return c;
		}

		// FVG by timeframe in the right margin: box in its timeframe column, CE line, arrow label, extended levels
		private void DrawMM(Lvl l)
		{
			Brush c = TCol(l.Tfi, l.Dir);
			int x0 = -(MfMarginOffset + MfCol(l.Tfi - 3) * (MfMarginWidth + 1));
			int x1 = x0 - MfMarginWidth;

			NinjaTrader.NinjaScript.DrawingTools.Rectangle r = Draw.Rectangle(this, Tag(l, "bx"), false,
				x0, l.Hi, x1, l.Lo, c, MfFill == ICTcotasFill.Never ? Brushes.Transparent : c,
				MfFill == ICTcotasFill.Never ? 0 : 100 - MfTransparency);
			if (r != null)
				r.OutlineStroke = new Stroke(c, MfStyle, MfWidth);
			if (MfShowCE)
				Draw.Line(this, Tag(l, "cbx"), false, x0, l.Ce, x1, l.Ce, c, MfCEStyle, MfWidth);

			if (MfExtend)
			{
				int xs = MfExtendTo == ICTcotasExtendTo.ToStart ? BarsAgoOf(l.T)
					: MfExtendTo == ICTcotasExtendTo.ToCreation ? BarsAgoOf(l.HasTfm ? l.Tfm : l.T) : MfExtendBars;
				Draw.Line(this, Tag(l, "hl"), false, xs, l.Hi, x0, l.Hi, c, MfStyle, MfWidth);
				Draw.Line(this, Tag(l, "ll"), false, xs, l.Lo, x0, l.Lo, c, MfStyle, MfWidth);
				if (MfShowCE)
					Draw.Line(this, Tag(l, "cl"), false, xs, l.Ce, x0, l.Ce, c, MfCEStyle, MfWidth);
			}

			if (MfLabels)
			{
				double y = MfLabelAnchor == ICTcotasLabelAnchor.High ? l.Hi
					: MfLabelAnchor == ICTcotasLabelAnchor.Low ? l.Lo : l.Ce;
				// thin boxes: the timeframe on one line, the arrow on the next
				// TF with an "i" for an IFVG: "1Hi"
				string tfn = MfLabelShowTf ? MfNames[l.Tfi - 3] + (l.Inv ? "i" : "") + "\n" : "";
				string txt = tfn + (l.Dir == 1 ? "\u2191" : "\u2193");
				DrawLabel(Tag(l, "lbl"), (x0 + x1) / 2, y, txt, c, mfFont, TextAlignment.Center);
			}
		}

		// FVG by timeframe (kind 3): outline box (optionally filled), CE line, formation marker, label
		private void DrawM(Lvl l)
		{
			int sba = BarsAgoOf(l.T);
			int eba = -LabelOffset;
			Brush c = TCol(l.Tfi, l.Dir);

			NinjaTrader.NinjaScript.DrawingTools.Rectangle r = Draw.Rectangle(this, Tag(l, "bx"), false,
				sba, l.Hi, eba, l.Lo, c, MfFill == ICTcotasFill.Always ? c : Brushes.Transparent,
				MfFill == ICTcotasFill.Always ? 100 - MfTransparency : 0);
			if (r != null)
				r.OutlineStroke = new Stroke(c, MfStyle, MfWidth);

			if (MfShowCE)
				Draw.Line(this, Tag(l, "cbx"), false, sba, l.Ce, eba, l.Ce, c, MfCEStyle, MfWidth);

			if (MfShowFormation && l.HasTfm && l.Tfm >= l.T)
			{
				int fba = BarsAgoOf(l.Tfm);
				Draw.Line(this, Tag(l, "vl"), false, fba, l.Hi, fba, l.Lo, c, DashStyleHelper.Solid,
					MfWidth > 1 ? MfWidth - 1 : MfWidth);
			}

			if (MfLabels && (MfLabelShowTf || MfLabelShowType || MfLabelShowDir))
			{
				double y = MfLabelAnchor == ICTcotasLabelAnchor.High ? l.Hi
					: MfLabelAnchor == ICTcotasLabelAnchor.Low ? l.Lo : l.Ce;
				int lx = MfLabelPos == ICTcotasLabelPosition.InsideLeft || MfLabelPos == ICTcotasLabelPosition.LeftOfBox ? sba
					: MfLabelPos == ICTcotasLabelPosition.Center ? (sba + eba) / 2 : eba;
				TextAlignment al = MfLabelPos == ICTcotasLabelPosition.RightMargin || MfLabelPos == ICTcotasLabelPosition.InsideLeft
					? TextAlignment.Left
					: MfLabelPos == ICTcotasLabelPosition.Center ? TextAlignment.Center : TextAlignment.Right;
				string sg = MfLabelShowDir ? (l.Dir == 1 ? "+" : "-") : "";
				string ty = (MfLabelShowType ? (l.Inv ? IfvgText : FvgText) : "") + sg;
				string tfn = MfLabelShowTf ? MfNames[l.Tfi - 3] : "";
				string txt = tfn + (tfn != "" && ty != "" ? " " : "") + ty;
				DrawLabel(Tag(l, "lbl"), lx, y, txt, c, mfFont, al);
			}
		}

		#endregion

		#region Properties

		// ---------------------------------------------------------------- General
		[NinjaScriptProperty]
		[Display(Name = "Daily", Description = "Levels from daily candles (chart timeframe must be lower than 1D)", GroupName = GGen, Order = 1)]
		public bool DoDaily { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Weekly", Description = "Levels from weekly candles (chart timeframe must be lower than 1W)", GroupName = GGen, Order = 2)]
		public bool DoWeekly { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Monthly", Description = "Levels from monthly candles (chart timeframe must be lower than 1M)", GroupName = GGen, Order = 3)]
		public bool DoMonthly { get; set; }

		[NinjaScriptProperty]
		[Range(0, 2)]
		[Display(Name = "Extra line width per timeframe step", Description = "Weekly levels get this much extra width over daily ones, monthly levels twice as much", GroupName = GGen, Order = 4)]
		public int WidthExtra { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Remove when", Description = "When a level is considered mitigated and removed. Fully filled means price crossed the far edge (by wick, or by a close of the level's own timeframe). FVGs follow the IFVG rule instead when it is enabled.", GroupName = GGen, Order = 5)]
		public ICTcotasMitigation Mitigation { get; set; }

		// ---------------------------------------------------------------- Colors
		[NinjaScriptProperty]
		[XmlIgnore]
		[Display(Name = "Daily: bull", GroupName = GCol, Order = 1)]
		public Brush DailyBull { get; set; }

		[Browsable(false)]
		public string DailyBullSerializable
		{
			get { return Serialize.BrushToString(DailyBull); }
			set { DailyBull = Serialize.StringToBrush(value); }
		}

		[NinjaScriptProperty]
		[XmlIgnore]
		[Display(Name = "Daily: bear", GroupName = GCol, Order = 2)]
		public Brush DailyBear { get; set; }

		[Browsable(false)]
		public string DailyBearSerializable
		{
			get { return Serialize.BrushToString(DailyBear); }
			set { DailyBear = Serialize.StringToBrush(value); }
		}

		[NinjaScriptProperty]
		[XmlIgnore]
		[Display(Name = "Weekly: bull", GroupName = GCol, Order = 3)]
		public Brush WeeklyBull { get; set; }

		[Browsable(false)]
		public string WeeklyBullSerializable
		{
			get { return Serialize.BrushToString(WeeklyBull); }
			set { WeeklyBull = Serialize.StringToBrush(value); }
		}

		[NinjaScriptProperty]
		[XmlIgnore]
		[Display(Name = "Weekly: bear", GroupName = GCol, Order = 4)]
		public Brush WeeklyBear { get; set; }

		[Browsable(false)]
		public string WeeklyBearSerializable
		{
			get { return Serialize.BrushToString(WeeklyBear); }
			set { WeeklyBear = Serialize.StringToBrush(value); }
		}

		[NinjaScriptProperty]
		[XmlIgnore]
		[Display(Name = "Monthly: bull", GroupName = GCol, Order = 5)]
		public Brush MonthlyBull { get; set; }

		[Browsable(false)]
		public string MonthlyBullSerializable
		{
			get { return Serialize.BrushToString(MonthlyBull); }
			set { MonthlyBull = Serialize.StringToBrush(value); }
		}

		[NinjaScriptProperty]
		[XmlIgnore]
		[Display(Name = "Monthly: bear", Description = "All PD arrays of a timeframe share its colors. Lower wicks count as bull, upper wicks as bear.", GroupName = GCol, Order = 6)]
		public Brush MonthlyBear { get; set; }

		[Browsable(false)]
		public string MonthlyBearSerializable
		{
			get { return Serialize.BrushToString(MonthlyBear); }
			set { MonthlyBear = Serialize.StringToBrush(value); }
		}

		// ---------------------------------------------------------------- Labels
		[NinjaScriptProperty]
		[Display(Name = "Show labels", GroupName = GLbl, Order = 1)]
		public bool ShowLabels { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Bold", GroupName = GLbl, Order = 2)]
		public bool LabelBold { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Direction (+/-)", Description = "Append + (bullish) or - (bearish) to FVG, IFVG and VI labels", GroupName = GLbl, Order = 3)]
		public bool LabelDirection { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Size", GroupName = GLbl, Order = 4)]
		public ICTcotasLabelSize LabelSize { get; set; }

		[NinjaScriptProperty]
		[Range(1, int.MaxValue)]
		[Display(Name = "Offset", Description = "Bars between the last bar and the labels", GroupName = GLbl, Order = 5)]
		public int LabelOffset { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Text: daily", GroupName = GLbl, Order = 6)]
		public string TextDaily { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Text: weekly", GroupName = GLbl, Order = 7)]
		public string TextWeekly { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Text: monthly", GroupName = GLbl, Order = 8)]
		public string TextMonthly { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Parts: high", GroupName = GLbl, Order = 9)]
		public string TextHigh { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Parts: low", GroupName = GLbl, Order = 10)]
		public string TextLow { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Parts: CE", GroupName = GLbl, Order = 11)]
		public string TextCE { get; set; }

		// ---------------------------------------------------------------- Fair Value Gaps
		[NinjaScriptProperty]
		[Display(Name = "Draw FVGs", Description = "Three candle gap: the low of the third candle is above the high of the first (bullish), or the high of the third is below the low of the first (bearish).", GroupName = GFvg, Order = 1)]
		public bool DrawFvg { get; set; }

		[NinjaScriptProperty]
		[Range(1, 20)]
		[Display(Name = "FVG keep", Description = "Max FVGs (and IFVGs) kept per timeframe", GroupName = GFvg, Order = 2)]
		public int FvgKeep { get; set; }

		[NinjaScriptProperty]
		[Range(0.0, double.MaxValue)]
		[Display(Name = "FVG min size", Description = "Min gap size in price points", GroupName = GFvg, Order = 3)]
		public double FvgMinSize { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "FVG hi/lo style", GroupName = GFvg, Order = 4)]
		public DashStyleHelper FvgStyle { get; set; }

		[NinjaScriptProperty]
		[Range(1, 3)]
		[Display(Name = "FVG width", GroupName = GFvg, Order = 5)]
		public int FvgWidth { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "FVG CE style", GroupName = GFvg, Order = 6)]
		public DashStyleHelper FvgCEStyle { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "FVG show: hi", GroupName = GFvg, Order = 7)]
		public bool FvgShowHigh { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "FVG show: lo", GroupName = GFvg, Order = 8)]
		public bool FvgShowLow { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "FVG show: CE", GroupName = GFvg, Order = 9)]
		public bool FvgShowCE { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "FVG do not fill rectangles", Description = "When checked no rectangle is drawn, only the hi / lo / CE lines", GroupName = GFvg, Order = 10)]
		public bool FvgNoFill { get; set; }

		[NinjaScriptProperty]
		[Range(0, 100)]
		[Display(Name = "FVG transp", GroupName = GFvg, Order = 11)]
		public int FvgTransparency { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "FVG label", GroupName = GFvg, Order = 12)]
		public string FvgText { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "FVG becomes IFVG on a close through it", Description = "A candle of the FVG's own timeframe closing beyond its far edge inverts it into an IFVG with the opposite direction. While enabled, FVGs are not removed by the general mitigation rule; IFVGs are.", GroupName = GFvg, Order = 13)]
		public bool DoIfvg { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "IFVG label", GroupName = GFvg, Order = 14)]
		public string IfvgText { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Overlapping FVGs", Description = "MostRecent keeps only the newest one. Enclosing keeps the one that includes the others (if none includes all of them, the newest wins). KeepAll draws everything.", GroupName = GFvg, Order = 15)]
		public ICTcotasOverlap FvgOverlap { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Extend FVG range to include VIs with candle 2", Description = "When the body of candle 1 or candle 3 does not overlap the body of the middle candle, that area is included in the FVG range. The VIs are still drawn on their own.", GroupName = GFvg, Order = 16)]
		public bool FvgExtendVI { get; set; }

		// ---------------------------------------------------------------- Volume Imbalances
		[NinjaScriptProperty]
		[Display(Name = "Draw VIs", Description = "Gap between the bodies of two consecutive candles while their wicks still overlap (no full gap).", GroupName = GVi, Order = 1)]
		public bool DrawVi { get; set; }

		[NinjaScriptProperty]
		[Range(1, 20)]
		[Display(Name = "VI keep", Description = "Max volume imbalances kept per timeframe", GroupName = GVi, Order = 2)]
		public int ViKeep { get; set; }

		[NinjaScriptProperty]
		[Range(0.0, double.MaxValue)]
		[Display(Name = "VI min size", Description = "Min gap size in price points", GroupName = GVi, Order = 3)]
		public double ViMinSize { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "VI hi/lo style", GroupName = GVi, Order = 4)]
		public DashStyleHelper ViStyle { get; set; }

		[NinjaScriptProperty]
		[Range(1, 3)]
		[Display(Name = "VI width", GroupName = GVi, Order = 5)]
		public int ViWidth { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "VI CE style", GroupName = GVi, Order = 6)]
		public DashStyleHelper ViCEStyle { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "VI show: hi", GroupName = GVi, Order = 7)]
		public bool ViShowHigh { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "VI show: lo", GroupName = GVi, Order = 8)]
		public bool ViShowLow { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "VI show: CE", GroupName = GVi, Order = 9)]
		public bool ViShowCE { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "VI do not fill rectangles", Description = "When checked no rectangle is drawn, only the hi / lo / CE lines", GroupName = GVi, Order = 10)]
		public bool ViNoFill { get; set; }

		[NinjaScriptProperty]
		[Range(0, 100)]
		[Display(Name = "VI transp", GroupName = GVi, Order = 11)]
		public int ViTransparency { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "VI label", GroupName = GVi, Order = 12)]
		public string ViText { get; set; }

		// ---------------------------------------------------------------- Wick CE
		[NinjaScriptProperty]
		[Display(Name = "Draw wick CE", Description = "Consecutive candles with a wick on the same side form a group. The CE of the longest wick of the group is drawn; a lone wick is its own group.", GroupName = GWk, Order = 1)]
		public bool DrawWick { get; set; }

		[NinjaScriptProperty]
		[Range(1, 20)]
		[Display(Name = "Wick keep", Description = "Max wick levels kept per timeframe", GroupName = GWk, Order = 2)]
		public int WickKeep { get; set; }

		[NinjaScriptProperty]
		[Range(1.0, 100.0)]
		[Display(Name = "Min wick %", Description = "Min wick size as % of the candle range to count as a wick", GroupName = GWk, Order = 3)]
		public double WickMinPct { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Wick line style", GroupName = GWk, Order = 4)]
		public DashStyleHelper WickStyle { get; set; }

		[NinjaScriptProperty]
		[Range(1, 3)]
		[Display(Name = "Wick width", GroupName = GWk, Order = 5)]
		public int WickWidth { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Labels: upper", GroupName = GWk, Order = 6)]
		public string WickTextUpper { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Labels: lower", GroupName = GWk, Order = 7)]
		public string WickTextLower { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "ULW CE", Description = "D / W / M candle with both a long upper and a long lower wick (both count as wicks, see min wick %): a level at the midpoint between the CE of its upper wick and the CE of its lower wick. Follows the mitigation rule and the wick keep count.", GroupName = GWk, Order = 8)]
		public bool DrawUlw { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "ULW CE label", GroupName = GWk, Order = 9)]
		public string UlwText { get; set; }

		// ---------------------------------------------------------------- FVGs and wick CE by timeframe
		[NinjaScriptProperty]
		[Display(Name = "Draw FVGs of selected timeframes", Description = "FVGs found on the timeframes ticked below, drawn with a box, a CE line and a label. Only timeframes higher than the chart timeframe are used, never the chart's own.", GroupName = GMf, Order = 1)]
		public bool DrawMf { get; set; }

		[NinjaScriptProperty]
		[Range(0, 6)]
		[Display(Name = "Plus the next higher", Description = "Also draw the FVGs of this many of the listed timeframes that are above the chart timeframe, starting with the nearest one. 0 = only the ticked ones.", GroupName = GMf, Order = 2)]
		public int MfNext { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "15m", GroupName = GMf, Order = 3)]
		public bool Mf15 { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "1H", GroupName = GMf, Order = 4)]
		public bool Mf1H { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "4H", GroupName = GMf, Order = 5)]
		public bool Mf4H { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "D", GroupName = GMf, Order = 6)]
		public bool MfD { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "W", GroupName = GMf, Order = 7)]
		public bool MfW { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "M", GroupName = GMf, Order = 8)]
		public bool MfM { get; set; }

		[NinjaScriptProperty]
		[Range(1, 20)]
		[Display(Name = "Keep per timeframe", GroupName = GMf, Order = 9)]
		public int MfKeep { get; set; }

		[NinjaScriptProperty]
		[Range(0.0, double.MaxValue)]
		[Display(Name = "Min size", Description = "Min gap size in price points", GroupName = GMf, Order = 10)]
		public double MfMinSize { get; set; }

		[NinjaScriptProperty]
		[XmlIgnore]
		[Display(Name = "15m: bull", GroupName = GMf, Order = 11)]
		public Brush Mf15Bull { get; set; }

		[Browsable(false)]
		public string Mf15BullSerializable
		{
			get { return Serialize.BrushToString(Mf15Bull); }
			set { Mf15Bull = Serialize.StringToBrush(value); }
		}

		[NinjaScriptProperty]
		[XmlIgnore]
		[Display(Name = "15m: bear", GroupName = GMf, Order = 12)]
		public Brush Mf15Bear { get; set; }

		[Browsable(false)]
		public string Mf15BearSerializable
		{
			get { return Serialize.BrushToString(Mf15Bear); }
			set { Mf15Bear = Serialize.StringToBrush(value); }
		}

		[NinjaScriptProperty]
		[XmlIgnore]
		[Display(Name = "1H: bull", GroupName = GMf, Order = 13)]
		public Brush Mf1HBull { get; set; }

		[Browsable(false)]
		public string Mf1HBullSerializable
		{
			get { return Serialize.BrushToString(Mf1HBull); }
			set { Mf1HBull = Serialize.StringToBrush(value); }
		}

		[NinjaScriptProperty]
		[XmlIgnore]
		[Display(Name = "1H: bear", GroupName = GMf, Order = 14)]
		public Brush Mf1HBear { get; set; }

		[Browsable(false)]
		public string Mf1HBearSerializable
		{
			get { return Serialize.BrushToString(Mf1HBear); }
			set { Mf1HBear = Serialize.StringToBrush(value); }
		}

		[NinjaScriptProperty]
		[XmlIgnore]
		[Display(Name = "4H: bull", GroupName = GMf, Order = 15)]
		public Brush Mf4HBull { get; set; }

		[Browsable(false)]
		public string Mf4HBullSerializable
		{
			get { return Serialize.BrushToString(Mf4HBull); }
			set { Mf4HBull = Serialize.StringToBrush(value); }
		}

		[NinjaScriptProperty]
		[XmlIgnore]
		[Display(Name = "4H: bear", GroupName = GMf, Order = 16)]
		public Brush Mf4HBear { get; set; }

		[Browsable(false)]
		public string Mf4HBearSerializable
		{
			get { return Serialize.BrushToString(Mf4HBear); }
			set { Mf4HBear = Serialize.StringToBrush(value); }
		}

		[NinjaScriptProperty]
		[XmlIgnore]
		[Display(Name = "D: bull", GroupName = GMf, Order = 17)]
		public Brush MfDBull { get; set; }

		[Browsable(false)]
		public string MfDBullSerializable
		{
			get { return Serialize.BrushToString(MfDBull); }
			set { MfDBull = Serialize.StringToBrush(value); }
		}

		[NinjaScriptProperty]
		[XmlIgnore]
		[Display(Name = "D: bear", GroupName = GMf, Order = 18)]
		public Brush MfDBear { get; set; }

		[Browsable(false)]
		public string MfDBearSerializable
		{
			get { return Serialize.BrushToString(MfDBear); }
			set { MfDBear = Serialize.StringToBrush(value); }
		}

		[NinjaScriptProperty]
		[XmlIgnore]
		[Display(Name = "W: bull", GroupName = GMf, Order = 19)]
		public Brush MfWBull { get; set; }

		[Browsable(false)]
		public string MfWBullSerializable
		{
			get { return Serialize.BrushToString(MfWBull); }
			set { MfWBull = Serialize.StringToBrush(value); }
		}

		[NinjaScriptProperty]
		[XmlIgnore]
		[Display(Name = "W: bear", GroupName = GMf, Order = 20)]
		public Brush MfWBear { get; set; }

		[Browsable(false)]
		public string MfWBearSerializable
		{
			get { return Serialize.BrushToString(MfWBear); }
			set { MfWBear = Serialize.StringToBrush(value); }
		}

		[NinjaScriptProperty]
		[XmlIgnore]
		[Display(Name = "M: bull", GroupName = GMf, Order = 21)]
		public Brush MfMBull { get; set; }

		[Browsable(false)]
		public string MfMBullSerializable
		{
			get { return Serialize.BrushToString(MfMBull); }
			set { MfMBull = Serialize.StringToBrush(value); }
		}

		[NinjaScriptProperty]
		[XmlIgnore]
		[Display(Name = "M: bear", GroupName = GMf, Order = 22)]
		public Brush MfMBear { get; set; }

		[Browsable(false)]
		public string MfMBearSerializable
		{
			get { return Serialize.BrushToString(MfMBear); }
			set { MfMBear = Serialize.StringToBrush(value); }
		}

		[NinjaScriptProperty]
		[Display(Name = "Outline style", GroupName = GMf, Order = 23)]
		public DashStyleHelper MfStyle { get; set; }

		[NinjaScriptProperty]
		[Range(1, 3)]
		[Display(Name = "Outline width", GroupName = GMf, Order = 24)]
		public int MfWidth { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Fill", Description = "Never: boxes drawn as outlines only. AtMargin: filled only when drawn in the right margin. Always: filled over the candles too.", GroupName = GMf, Order = 25)]
		public ICTcotasFill MfFill { get; set; }

		[NinjaScriptProperty]
		[Range(0, 100)]
		[Display(Name = "Transp", GroupName = GMf, Order = 26)]
		public int MfTransparency { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Formation marker", Description = "Vertical segment inside the FVG box at the close of its last candle, the moment the FVG formed", GroupName = GMf, Order = 27)]
		public bool MfShowFormation { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "CE box", Description = "A zero height box (a line) at the CE (midpoint) of the FVG", GroupName = GMf, Order = 28)]
		public bool MfShowCE { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "CE outline", GroupName = GMf, Order = 29)]
		public DashStyleHelper MfCEStyle { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Labels", GroupName = GMf, Order = 30)]
		public bool MfLabels { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Label: show TF", GroupName = GMf, Order = 31)]
		public bool MfLabelShowTf { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Label: FVG / IFVG", GroupName = GMf, Order = 32)]
		public bool MfLabelShowType { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Label: direction (+/-)", Description = "Choose which parts the label shows: the timeframe, the FVG / IFVG name and the direction", GroupName = GMf, Order = 33)]
		public bool MfLabelShowDir { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Label size", GroupName = GMf, Order = 34)]
		public ICTcotasLabelSize MfLabelSize { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Label position", GroupName = GMf, Order = 35)]
		public ICTcotasLabelPosition MfLabelPos { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Label at", Description = "Price level the label is attached to", GroupName = GMf, Order = 36)]
		public ICTcotasLabelAnchor MfLabelAnchor { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Wick CE: 1H", Description = "Also compute the wick CE levels (needs Draw wick CE on) on 1H candles. Only used when the chart timeframe is lower than 1H.", GroupName = GMf, Order = 37)]
		public bool Wick1H { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Wick CE: 4H", Description = "Also compute the wick CE levels (needs Draw wick CE on) on 4H candles. Only used when the chart timeframe is lower than 4H.", GroupName = GMf, Order = 38)]
		public bool Wick4H { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Draw in the right margin", Description = "Instead of over the candles, each FVG / IFVG is drawn as a box in the right margin, one column per timeframe (from the lowest), labeled with the timeframe and, on the next line, an up (bullish) or down (bearish) arrow.", GroupName = GMf, Order = 39)]
		public bool MfMargin { get; set; }

		[NinjaScriptProperty]
		[Range(0, 200)]
		[Display(Name = "Margin: offset", Description = "Bars between the last bar and the first column", GroupName = GMf, Order = 40)]
		public int MfMarginOffset { get; set; }

		[NinjaScriptProperty]
		[Range(1, 50)]
		[Display(Name = "Margin: width", Description = "Bars per box (and per column)", GroupName = GMf, Order = 41)]
		public int MfMarginWidth { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Margin: extend levels", Description = "Lines from the high, low and CE (with CE box on) of each FVG back to the point chosen in Extend: mode", GroupName = GMf, Order = 42)]
		public bool MfExtend { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Extend: mode", Description = "For: the number of candles below, before the last one. ToStart: the start of the FVG. ToCreation: the end of its third candle, when the FVG was formed (so the third candle's own prices are not taken as reactions to the FVG).", GroupName = GMf, Order = 43)]
		public ICTcotasExtendTo MfExtendTo { get; set; }

		[NinjaScriptProperty]
		[Range(1, 500)]
		[Display(Name = "Extend: candles", GroupName = GMf, Order = 44)]
		public int MfExtendBars { get; set; }

		#endregion
	}
}
