// =====================================================================================================================
// FVGSIFVGOTE - Fair value gaps, 1st presented FVGs, inverse FVGs (IFVG) and ICT OTE, for NinjaTrader 8
// ---------------------------------------------------------------------------------------------------------------------
// Port of the TradingView Pine Script v6 indicator "FVGs+IFVG+OTE5" by franjballest
// (source file FVGS+IFVG+OTE.txt, md5 06f06f42dacc49e4a5da52bfb6f57903). Mozilla Public License 2.0, as the original.
//
// What it draws: FVG boxes (or no-fill boxes, or hi / lo / C.E. segments) with C.E. lines, the 1st presented FVG of the
// session open (New York 09:30, or Madrid 09:00 for DAX) and its optional inverse, wick C.E. segments, IFVG candidates
// and confirmed IFVGs (close mode or swing mode, kept IFVGs, Delivery Engine inversion rules / full-body confirmation /
// displacement / A+), the OTE checks 1 (prior-leg rejection) and 2 (retrace) on the chart timeframe and on the next two
// rungs of the 5m / 15m / 1h / 4h / D ladder, OTE bands right of the last candle, breakers and mitigation blocks, signal
// markers and alerts (NinjaTrader Alerts window, real time only).
//
// Install:
//   1. Copy this file to  Documents\NinjaTrader 8\bin\Custom\Indicators\FVGSIFVGOTE.cs
//   2. Open NinjaTrader 8 > New > NinjaScript Editor, open the file and press F5 to compile.
//   3. Add "FVGSIFVGOTE" to a chart (intraday, 1m - 5m recommended).
//   NinjaTrader regenerates the "#region NinjaScript generated code" factory block on compile; it is deliberately not
//   included here.
//
// Differences from the Pine version that could not be avoided:
//   - NT8 rectangles carry no text: box labels are separate Draw.Text objects anchored at the box (left / middle / right
//     edge per the H align input; the V align is approximated inside the box).
//   - Pine extend.right is emulated by moving the right edge to the last processed bar on every bar.
//   - "Historic compute" has no equivalent (no visible range in OnBarUpdate); FVGs are always computed on every bar.
//   - NT8 bar time stamps are bar CLOSE times; drawings are anchored on them, and session / killzone / 1PFVG checks use
//     the bar open time (close time minus the bar period).
//   - Higher timeframes: 5m, 15m, 60m, 240m and 1D series are always added; the A+ HTF structure timeframe is limited to
//     these values (Pine accepts any timeframe).
//   - Pine object limits (500 boxes / lines, 50 labels, oldest auto-deleted) are approximated: drawings left behind by
//     breakers / mitigation blocks are capped at 100 groups and swing labels at 50; markers are not capped.
//   - Pivot tie rule: a pivot must be strictly above (below) its left bars and not exceeded by its right bars.
//   - Drawing is deferred for speed: objects are kept in memory and drawn at the end of the history and on every real
//     time bar. With Calculate other than OnBarClose each bar is still processed once, at the first tick of the next bar.
//   - The TZ input takes "Auto", a few IANA names (America/New_York, Europe/Madrid, ...) or a Windows time zone id.
//   - Text sizes map to fonts: auto/small 10, tiny 8, normal 12, huge 18.
// =====================================================================================================================

#region Using declarations
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
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

// option lists of the Pine string inputs (global, uniquely prefixed, so the generated factory code can see them)
public enum FioDrawMode { All, OnlyPFVGs, IFVGs, PFVGsAndIFVGs }
public enum FioFilterMode { None, Sweeps, SweepsAndMid }
public enum FioFvgStyle { Box, BoxNoFill, Segments }
public enum FioConfirm { Close, FullBody }
public enum FioDeadMode { Fade, BackToFVG }
public enum FioTextSize { Auto, Tiny, Small, Normal, Huge }
public enum FioHAlign { Left, Center, Right }
public enum FioVAlign { Top, Center, Bottom }
public enum FioOteMode { Overlap, FullyInside, CEInside }
public enum FioZoneMode { LastCandle, WholeSequence }
public enum FioRangeType { OpenToClose, HighToLow }
public enum FioHtf { Min5, Min15, Hour1, Hour4, Daily }

namespace NinjaTrader.NinjaScript.Indicators
{
    public class FVGSIFVGOTE : Indicator
    {
        #region Drawing model (retained objects, rendered in one place)
        private abstract class Gfx
        {
            public string Tag;
            public bool Dirty, Deleted, Drawn, Ext, InExt;
        }

        private sealed class GBox : Gfx
        {
            public bool BarBased;
            public DateTime T1, T2;
            public int B1, B2;
            public double Top, Bot;
            public Color Bg, Bd;
            public int Bw = 1;
            public DashStyleHelper Sty = DashStyleHelper.Solid;
            public string Txt = "";
            public Color Tc;
            public int Ts = 10;
            public int Ha = 1, Va = 1; // 0 left / top, 1 center, 2 right / bottom
            public bool Bold, TxtDrawn;
        }

        private sealed class GLine : Gfx
        {
            public bool BarBased;
            public DateTime T1, T2;
            public int B1, B2;
            public double Y1, Y2;
            public Color Col;
            public int W = 1;
            public DashStyleHelper Sty = DashStyleHelper.Solid;
        }

        private sealed class GMark : Gfx
        {
            public int Kind; // 0 text label with background, 1 arrow up, 2 arrow down, 3 diamond
            public DateTime T;
            public double Y;
            public string Txt = "";
            public Color Col, Tc;
            public int Ts = 8;
            public int YOff;
            public bool TxtDrawn;
        }
        #endregion

        #region Logic types (Pine user-defined types)
        private sealed class Fvg
        {
            public double hi, lo, ce;
            public bool islong;
            public DateTime start;
            public DateTime? end;
            public GBox rect;
            public GLine celine, hiln, loln;
            public bool isfp, iswfp, isifp;
            public int sbar;
            public bool kept;
            public int kdir;
            public Fvg dup;
            public bool cand, own, shown, aplus, disp, brk;
            public int brkD, mbD;
            public int lblD;                       // direction of the B / M / BM label
            public bool ote;                       // [OTEF] OTE FVG
            public double oLo = double.NaN, oHi = double.NaN; // [OTEF] leg low / high
        }

        private sealed class PivSet
        {
            public List<int> hb = new List<int>();
            public List<double> hp = new List<double>();
            public List<int> lb = new List<int>();
            public List<double> lp = new List<double>();
            public string tag = "";
            public bool htf;
        }

        private sealed class OteTrack
        {
            public int dir;
            public double zlo = double.NaN, zhi = double.NaN, legHi = double.NaN, legLo = double.NaN, rext = double.NaN;
            public bool armed, fired;
            public DateTime? ctime;
            public int cbar;
            public GBox band;
            public double pHi = double.NaN, pLo = double.NaN;
            public int sbar;
            public bool rejHit;
            public GBox rej;
            public string tag = "";
            public int col;   // margin column of its timeframe (chart, then each higher timeframe)
        }

        private sealed class OteGroup
        {
            public OteTrack t0 = new OteTrack(), t1 = new OteTrack(), t2 = new OteTrack();
            public bool flagged;
            public string tags = "";
        }

        private sealed class Blk // breaker or mitigation block
        {
            public int dir;
            public double hi, lo;
            public DateTime t, t1;
            public bool broken;
            public int? bkBar;
            public bool swung, tagged;
            public GBox bx;
            public GLine ml;
            public List<GLine> fl;
        }

        private sealed class HtfPiv
        {
            public double ph, pl;
            public DateTime tprev;
        }
        #endregion

        #region Fields
        private const int flN = 1;    // FVGs kept after a sweep
        private const int flWin = 25; // % of the arm on each side of its middle
        private const int MaxBlkGroups = 100;
        private const int MaxSwingLabels = 50;

        private static readonly Color CTransp = Color.FromArgb(0, 0, 0, 0);
        private static readonly Color CGray = Color.FromRgb(0x78, 0x7B, 0x86);
        private static readonly Color CBlack = Color.FromRgb(0x36, 0x3A, 0x45);
        private static readonly Color CBlue = Color.FromRgb(0x21, 0x96, 0xF3);
        private static readonly Color CWhite = Color.FromRgb(0xFF, 0xFF, 0xFF);

        private NinjaTrader.Data.Bars b0;
        private int off, bi = -1, lastBi = -1;
        private DateTime curTime;
        private double bo, bh, bl, bc;

        // drawing model
        private List<Gfx> dirty;
        private List<Gfx> extList;
        private long gid;
        private Dictionary<Color, Brush> brushCache;
        private Dictionary<int, SimpleFont> fontCache;
        private TimeSpan barSpan;
        private bool loggedErr;

        // derived inputs
        private bool fponly, ifvgOnly, hideReg, fltOff, fltMid, styNoFill, stySeg;
        private Color cL, cLn, cS, cSn, cFp, cFpn, cWfp, cWfpn, cIfp, cIfpn, cWick, cDead, cBullIf, cBearIf;
        private Color cOteBand, cOteHi, cOteTxt, cBrk, cMb, cIfvgTxt;
        private Brush dispBrush;
        private int ifvgTs, ifvgHa, ifvgVa;
        private int tfsecs;
        private double tfmin;
        private bool isMinutes, isIntraday;
        private TimeSpan openSpan;
        private string tfStr;
        private TimeZoneInfo localTz, tzInfo, exchTz;
        private bool tzMadrid;
        private int fph, fpm;

        // wicks
        private List<GLine> wicks;

        // FVGs
        private List<Fvg> lfvgs, sfvgs, fpfvgs, keptIf, pendF;
        private bool hasfp, hasifp;

        // FVG filter state (FltState)
        private int fsPost;
        private double fsStH, fsStL;
        private int? fsStHbar, fsStLbar;

        // Delivery Engine
        private List<double> rangeL, stdL, atrL;
        private double trSum;
        private int? lastDispUpBar, lastDispDnBar;
        private List<double> deBsl, deSsl;
        private int? deLastBsl, deLastSsl;
        private bool deSslRecent, deBslRecent, deKzOK, deHtfUp, deHtfDn;
        private double deShP, deSlP;
        private int deDir;
        private int? deMssUpBar, deMssDnBar, deChochUp, deChochDn;
        private double otePL = double.NaN, otePH = double.NaN; // [OTEF] last chart swings
        private int otePLb = -1, otePHb = -1;
        private double hsSh, hsSl;
        private int hsD;
        private bool deHtfUse;
        private int deHtfIdx;

        // OTE
        private PivSet pv0, pv1, pv2;
        private List<HtfPiv> pend1, pend2;
        private int htfIdx1, htfIdx2;
        private int fp1mIdx = -1;   // BarsInProgress of the 1m series of the 1PFVG, -1 when not used
        private bool hasH1, hasH2;
        private OteGroup gBear, gBull, gSw;

        // close-mode IFVGs
        private GBox ifvgBox, ifvgBoxb;
        private Fvg bullifvg, bearifvg;
        private DateTime? bullconfirm, bearconfirm;
        private int bullInv, bearInv;
        private bool bullAp, bearAp, swAp;

        // swing-mode IFVGs
        private DateTime? swingTime;
        private int? swingBar;
        private double lastHi, lastLo;
        private int? lastHiBar, lastLoBar;
        private DateTime? lastHiTime, lastLoTime;
        private Fvg swFvg, findRfL, findRfS;
        private DateTime? ifstime;
        private double ifshi, ifslo;
        private int ifsdir;
        private DateTime? ifsconfirm;
        private int prevSig;
        private List<GMark> swingLbls;

        // breakers / mitigation blocks
        private List<Blk> brks, mbs;
        private double bLastH, bPrevH, bLastL, bPrevL;
        private int? bLastHb, bPrevHb, bLastLb, bPrevLb;
        private double mLastH, mPrevH, mLastL, mPrevL;
        private int? mLastHb, mPrevHb, mLastLb, mPrevLb;
        private List<List<Gfx>> blkGroups;

        // alerts
        private Dictionary<string, int> alertBar;
        #endregion

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description = "FVGs, 1st presented FVGs, IFVGs (close / swing mode), ICT OTE on the chart and two higher timeframes, Delivery Engine features, breakers and mitigation blocks. Port of the Pine Script indicator FVGs+IFVG+OTE5.";
                Name = "FVGSIFVGOTE";
                Calculate = Calculate.OnBarClose;
                IsOverlay = true;
                DisplayInDataBox = false;
                DrawOnPricePanel = true;
                PaintPriceMarkers = false;
                IsSuspendedWhileInactive = false;
                BarsRequiredToPlot = 0;

                Show1PFVGs = true;
                FpFrom1m = true;
                ShowIFVGs = true;
                ShowWickCE = true;
                DrawSelection = FioDrawMode.All;
                FvgFilter = FioFilterMode.None;
                HistoricCompute = false;
                FvgBounceAlerts = false;
                MinFvgSize = 3;
                MaxFvgSize = 10000;
                FvgsKept = 200;
                MergeTolerance = 10;
                SweepPivotLeft = 3;
                SweepPivotRight = 3;
                FvgStyle = FioFvgStyle.Box;
                CeMinHeight = 20;
                CeWidth = 2;
                CeStyle = DashStyleHelper.Dot;
                LongFvgColor = MkBrush(0xFF6D00, 90);
                LongFvgBorder = MkBrush(0xFF6D00, 20);
                ShortFvgColor = MkBrush(0xF7525F, 90);
                ShortFvgBorder = MkBrush(0xF7525F, 20);
                FpTime = "0930";
                FpTimeZone = "Auto";
                FpKeep = 6;
                FpDayColor = MkBrush(0x00FBFF, 90);
                FpDayBorder = MkBrush(0x0097A7, 20);
                FpWeekColor = MkBrush(0x2962FF, 90);
                FpWeekBorder = MkBrush(0x2962FF, 20);
                FpInverse = false;
                FpInverseColor = MkBrush(0x880E4F, 80);
                FpInverseBorder = MkBrush(0x880E4F, 20);
                WicksKept = 30;
                WickMinSize = 20;
                WickSegmentWidth = 0;
                WickStyle = DashStyleHelper.Solid;
                WickColor = MkBrush(0x363A45, 0);
                WickBounceAlerts = false;
                IfvgAlerts = true;
                ShowSignals = false;
                IfvgLabels = true;
                LabelOteTf = true;
                LabelSign = true;
                LabelAPlus = true;
                KeepIfvgs = true;
                KeepIfvgsMax = 20;
                IfvgConfirmation = FioConfirm.Close;
                InversionRules = false;
                MaxInversions = 1;
                InvalidatedIfvg = FioDeadMode.Fade;
                InvalidatedColor = MkBrush(0x787B86, 85);
                SwingMode = false;
                SignificantSwing = true;
                SwingLabels = false;
                SwingPivotLeft = 10;
                SwingPivotRight = 3;
                AtrLength = 14;
                MinSwingAtr = 4.0;
                BullIfvgColor = MkBrush(0xFF9800, 0);
                BearIfvgColor = MkBrush(0x00BCD4, 0);
                IfvgFillTransparency = 80;
                IfvgBorderTransparency = 20;
                IfvgTextSize = FioTextSize.Small;
                IfvgTextHAlign = FioHAlign.Right;
                IfvgTextVAlign = FioVAlign.Center;
                DetectOte = true;
                FlagOteFvgs = true;
                AlertOnlyOte = false;
                OteSignals = false;
                OteLower = 0.62;
                OteUpper = 0.79;
                OteMatch = FioOteMode.Overlap;
                PriorLegRejection = true;
                RejectionAnyCandle = false;
                RejectionCapWick = false;
                NextHigherTf = true;
                SecondHigherTf = true;
                HtfPivotLeft = 5;
                HtfPivotRight = 2;
                DrawOteBands = true;
                OteBandStart = 5;
                OteBandWidth = 8;
                OteBandColor = MkBrush(0xFFEB3B, 85);
                OteBorderColor = MkBrush(0xFFEB3B, 0);
                OteTextColor = MkBrush(0x363A45, 0);
                BreakersOn = true;
                BreakerZone = FioZoneMode.LastCandle;
                BreakerSwingLength = 5;
                BreakersKept = 3;
                BreakerRectangle = false;
                BreakerColor = MkBrush(0xE040FB, 0);
                BreakerLineStyle = DashStyleHelper.Dash;
                BreakerLineWidth = 1;
                BreakerFlagCandles = false;
                BreakerShadow = false;
                MitigationOn = true;
                MitigationZone = FioZoneMode.LastCandle;
                MitigationSwingLength = 5;
                MitigationKept = 3;
                MitigationRectangle = false;
                MitigationColor = MkBrush(0xFF9800, 0);
                MitigationLineStyle = DashStyleHelper.Dash;
                MitigationLineWidth = 1;
                MitigationFlagCandles = false;
                MitigationShadow = false;
                DisplacementOnly = false;
                DisplacementRange = FioRangeType.OpenToClose;
                DispStdLength = 100;
                DispStrength = 4.0;
                ColorDispCandles = false;
                DispCandleColor = MkBrush(0xFFEB3B, 0);
                FlagDispFvgs = false;
                FlagDispWidth = 3;
                APlusSetups = false;
                LiquiditySwingLength = 5;
                APlusWithin = 50;
                MssSwingLength = 3;
                MssDisplacement = 2.0;
                MssCandles = 2;
                APlusKillzones = true;
                APlusHtfStructure = false;
                APlusHtf = FioHtf.Hour1;
            }
            else if (State == State.Configure)
            {
                // higher timeframe ladder (OTE) and A+ HTF structure: always added, used according to the inputs
                AddDataSeries(BarsPeriodType.Minute, 5);    // BarsInProgress 1
                AddDataSeries(BarsPeriodType.Minute, 15);   // 2
                AddDataSeries(BarsPeriodType.Minute, 60);   // 3
                AddDataSeries(BarsPeriodType.Minute, 240);  // 4
                AddDataSeries(BarsPeriodType.Day, 1);       // 5
                // 1PFVG from the 1m candles on charts above 1m and below 15m
                fp1mIdx = -1;
                if (Show1PFVGs && FpFrom1m && BarsPeriod.BarsPeriodType == BarsPeriodType.Minute
                    && BarsPeriod.Value > 1 && BarsPeriod.Value < 15)
                {
                    AddDataSeries(BarsPeriodType.Minute, 1); // 6
                    fp1mIdx = 6;
                }
            }
            else if (State == State.DataLoaded)
            {
                InitState();
            }
        }

        #region Initialisation
        private static Brush MkBrush(int rgb, int transp)
        {
            byte a = (byte)Math.Round(255.0 * (100 - transp) / 100.0);
            SolidColorBrush b = new SolidColorBrush(Color.FromArgb(a, (byte)((rgb >> 16) & 0xFF), (byte)((rgb >> 8) & 0xFF), (byte)(rgb & 0xFF)));
            b.Freeze();
            return b;
        }

        private static Color ColorOf(Brush b, Color fallback)
        {
            SolidColorBrush s = b as SolidColorBrush;
            return s != null ? s.Color : fallback;
        }

        private static int FontPx(FioTextSize s)
        {
            switch (s)
            {
                case FioTextSize.Tiny: return 8;
                case FioTextSize.Normal: return 12;
                case FioTextSize.Huge: return 18;
                default: return 10;
            }
        }

        private static TimeZoneInfo FindTz(string name)
        {
            Dictionary<string, string> map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "America/New_York", "Eastern Standard Time" },
                { "US/Eastern", "Eastern Standard Time" },
                { "America/Chicago", "Central Standard Time" },
                { "US/Central", "Central Standard Time" },
                { "America/Los_Angeles", "Pacific Standard Time" },
                { "Europe/Madrid", "Romance Standard Time" },
                { "Europe/Paris", "Romance Standard Time" },
                { "Europe/Berlin", "W. Europe Standard Time" },
                { "Europe/Amsterdam", "W. Europe Standard Time" },
                { "Europe/Zurich", "W. Europe Standard Time" },
                { "Europe/London", "GMT Standard Time" },
                { "Asia/Tokyo", "Tokyo Standard Time" },
                { "UTC", "UTC" },
                { "Etc/UTC", "UTC" }
            };
            string id = name;
            string mapped;
            if (map.TryGetValue(name, out mapped)) id = mapped;
            try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
            catch (Exception) { }
            try { return TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time"); }
            catch (Exception) { }
            return TimeZoneInfo.Local;
        }

        private static string TfName(int minutes)
        {
            if (minutes >= 1440) return "D";
            if (minutes >= 60 && minutes % 60 == 0) return (minutes / 60).ToString() + "h";
            return minutes.ToString() + "m";
        }

        private void InitState()
        {
            b0 = BarsArray[0];
            dirty = new List<Gfx>();
            extList = new List<Gfx>();
            brushCache = new Dictionary<Color, Brush>();
            fontCache = new Dictionary<int, SimpleFont>();
            gid = 0;

            // draw mode / filter flags
            fponly = DrawSelection == FioDrawMode.OnlyPFVGs;
            ifvgOnly = DrawSelection == FioDrawMode.IFVGs;
            hideReg = DrawSelection == FioDrawMode.IFVGs || DrawSelection == FioDrawMode.PFVGsAndIFVGs;
            fltOff = FvgFilter == FioFilterMode.None;
            fltMid = FvgFilter == FioFilterMode.SweepsAndMid;
            styNoFill = FvgStyle == FioFvgStyle.BoxNoFill;
            stySeg = FvgStyle == FioFvgStyle.Segments;

            // colors
            cL = ColorOf(LongFvgColor, CTransp);
            cLn = ColorOf(LongFvgBorder, CBlack);
            cS = ColorOf(ShortFvgColor, CTransp);
            cSn = ColorOf(ShortFvgBorder, CBlack);
            cFp = ColorOf(FpDayColor, CTransp);
            cFpn = ColorOf(FpDayBorder, CBlack);
            cWfp = ColorOf(FpWeekColor, CTransp);
            cWfpn = ColorOf(FpWeekBorder, CBlack);
            cIfp = ColorOf(FpInverseColor, CTransp);
            cIfpn = ColorOf(FpInverseBorder, CBlack);
            cWick = ColorOf(WickColor, CBlack);
            cDead = ColorOf(InvalidatedColor, CGray);
            cBullIf = ColorOf(BullIfvgColor, CBlack);
            cBearIf = ColorOf(BearIfvgColor, CBlack);
            cOteBand = ColorOf(OteBandColor, CTransp);
            cOteHi = ColorOf(OteBorderColor, CBlack);
            cOteTxt = ColorOf(OteTextColor, CBlack);
            cBrk = ColorOf(BreakerColor, CBlack);
            cMb = ColorOf(MitigationColor, CBlack);
            cIfvgTxt = IfvgLabels ? CBlack : Tr(CBlack, 100);
            dispBrush = DispCandleColor;
            ifvgTs = FontPx(IfvgTextSize);
            ifvgHa = IfvgTextHAlign == FioHAlign.Left ? 0 : (IfvgTextHAlign == FioHAlign.Right ? 2 : 1);
            ifvgVa = IfvgTextVAlign == FioVAlign.Top ? 0 : (IfvgTextVAlign == FioVAlign.Bottom ? 2 : 1);

            // chart timeframe
            BarsPeriodType pt = b0.BarsPeriod.BarsPeriodType;
            int pvv = b0.BarsPeriod.Value;
            isMinutes = pt == BarsPeriodType.Minute;
            isIntraday = !(pt == BarsPeriodType.Day || pt == BarsPeriodType.Week || pt == BarsPeriodType.Month || pt == BarsPeriodType.Year);
            switch (pt)
            {
                case BarsPeriodType.Second: tfsecs = pvv; break;
                case BarsPeriodType.Minute: tfsecs = pvv * 60; break;
                case BarsPeriodType.Day: tfsecs = pvv * 86400; break;
                case BarsPeriodType.Week: tfsecs = pvv * 604800; break;
                case BarsPeriodType.Month: tfsecs = pvv * 2592000; break;
                case BarsPeriodType.Year: tfsecs = pvv * 31536000; break;
                default: tfsecs = 0; break; // tick / volume / range ...: not time based
            }
            tfmin = tfsecs / 60.0;
            openSpan = (pt == BarsPeriodType.Second || pt == BarsPeriodType.Minute) ? TimeSpan.FromSeconds(tfsecs) : TimeSpan.Zero;
            barSpan = tfsecs > 0 && tfsecs < 86400 ? TimeSpan.FromSeconds(tfsecs) : (tfsecs >= 86400 ? TimeSpan.FromDays(1) : TimeSpan.Zero);
            switch (pt)
            {
                case BarsPeriodType.Second: tfStr = pvv.ToString() + "s"; break;
                case BarsPeriodType.Minute:
                    tfStr = pvv == 60 ? "1h" : pvv == 120 ? "2h" : pvv == 180 ? "3h" : pvv == 240 ? "4h" : pvv.ToString() + "m";
                    break;
                case BarsPeriodType.Day: tfStr = pvv == 1 ? "D" : pvv.ToString() + "D"; break;
                case BarsPeriodType.Week: tfStr = pvv == 1 ? "W" : pvv.ToString() + "W"; break;
                case BarsPeriodType.Month: tfStr = pvv == 1 ? "M" : pvv.ToString() + "M"; break;
                default: tfStr = pvv.ToString() + " " + pt.ToString(); break;
            }

            // time zone of the 1PFVG / killzones (New York, or Madrid for DAX)
            localTz = NinjaTrader.Core.Globals.GeneralOptions.TimeZoneInfo;
            string tz = string.IsNullOrEmpty(FpTimeZone) ? "Auto" : FpTimeZone.Trim();
            string fpt = FpTime ?? "0930";
            if (tz == "Auto")
            {
                string desc = Instrument != null && Instrument.MasterInstrument != null ? (Instrument.MasterInstrument.Description ?? "") : "";
                string tick = Instrument != null ? (Instrument.FullName ?? "") : "";
                if (desc.Contains("DAX") || tick.Contains("DAX") || desc.Contains("Germany") || tick.Contains("FDX"))
                {
                    fpt = "0900";
                    tz = "Europe/Madrid";
                }
                else
                    tz = "America/New_York";
            }
            tzMadrid = tz == "Europe/Madrid" || tz == "Romance Standard Time";
            tzInfo = FindTz(tz);
            try { exchTz = b0.TradingHours.TimeZoneInfo; }
            catch (Exception) { exchTz = null; }
            if (exchTz == null) exchTz = tzInfo;
            fph = 0;
            fpm = 0;
            if (Show1PFVGs && fpt.Length >= 4)
            {
                int xh, xm;
                if (int.TryParse(fpt.Substring(0, 2), out xh)) fph = xh;
                if (int.TryParse(fpt.Substring(2, 2), out xm)) fpm = xm;
            }

            wicks = new List<GLine>();
            lfvgs = new List<Fvg>();
            sfvgs = new List<Fvg>();
            fpfvgs = new List<Fvg>();
            keptIf = new List<Fvg>();
            pendF = new List<Fvg>();
            hasfp = false;
            hasifp = false;

            fsPost = 1000000;
            fsStH = double.NaN;
            fsStL = double.NaN;
            fsStHbar = null;
            fsStLbar = null;

            rangeL = new List<double>();
            stdL = new List<double>();
            atrL = new List<double>();
            trSum = 0;
            lastDispUpBar = null;
            lastDispDnBar = null;
            deBsl = new List<double>();
            deSsl = new List<double>();
            deLastBsl = null;
            deLastSsl = null;
            deShP = double.NaN;
            deSlP = double.NaN;
            deDir = 0;
            deMssUpBar = deMssDnBar = deChochUp = deChochDn = null;
            hsSh = double.NaN;
            hsSl = double.NaN;
            hsD = 0;

            // OTE ladder: 3m (or lower) > 5m > 15m > 1h > 4h > D; series index == rung index
            int[] ladMin = new int[] { 3, 5, 15, 60, 240, 1440 };
            int ladIdx = 0;
            for (int i = 0; i <= 5; i++)
                if (ladMin[i] <= tfmin) ladIdx = i;
            htfIdx1 = ladIdx + 1 <= 5 ? ladIdx + 1 : -1;
            htfIdx2 = ladIdx + 2 <= 5 ? ladIdx + 2 : -1;
            hasH1 = NextHigherTf && htfIdx1 > 0;
            hasH2 = SecondHigherTf && htfIdx2 > 0;
            pv0 = new PivSet();
            pv1 = new PivSet();
            pv2 = new PivSet();
            pv0.htf = false;
            pv1.htf = true;
            pv2.htf = true;
            if (pt == BarsPeriodType.Minute) pv0.tag = TfName(pvv);
            else if (pt == BarsPeriodType.Day) pv0.tag = pvv == 1 ? "D" : pvv.ToString() + "D";
            else if (pt == BarsPeriodType.Second) pv0.tag = pvv.ToString() + "S";
            else pv0.tag = tfStr;
            pv1.tag = hasH1 ? TfName(ladMin[htfIdx1]) : "";
            pv2.tag = hasH2 ? TfName(ladMin[htfIdx2]) : "";
            pend1 = new List<HtfPiv>();
            pend2 = new List<HtfPiv>();
            gBear = new OteGroup();
            gBull = new OteGroup();
            gSw = new OteGroup();

            // A+ HTF structure timeframe (one of the ladder series)
            int htfSecs;
            switch (APlusHtf)
            {
                case FioHtf.Min5: deHtfIdx = 1; htfSecs = 300; break;
                case FioHtf.Min15: deHtfIdx = 2; htfSecs = 900; break;
                case FioHtf.Hour4: deHtfIdx = 4; htfSecs = 14400; break;
                case FioHtf.Daily: deHtfIdx = 5; htfSecs = 86400; break;
                default: deHtfIdx = 3; htfSecs = 3600; break;
            }
            deHtfUse = APlusHtfStructure && htfSecs > tfsecs;

            ifvgBox = null;
            ifvgBoxb = null;
            bullifvg = null;
            bearifvg = null;
            bullconfirm = null;
            bearconfirm = null;
            bullInv = 0;
            bearInv = 0;
            bullAp = bearAp = swAp = false;

            swingTime = null;
            swingBar = null;
            lastHi = double.NaN;
            lastLo = double.NaN;
            lastHiBar = lastLoBar = null;
            lastHiTime = lastLoTime = null;
            swFvg = findRfL = findRfS = null;
            ifstime = null;
            ifshi = double.NaN;
            ifslo = double.NaN;
            ifsdir = 0;
            ifsconfirm = null;
            prevSig = 0;
            swingLbls = new List<GMark>();

            brks = new List<Blk>();
            mbs = new List<Blk>();
            bLastH = bPrevH = bLastL = bPrevL = double.NaN;
            bLastHb = bPrevHb = bLastLb = bPrevLb = null;
            mLastH = mPrevH = mLastL = mPrevL = double.NaN;
            mLastHb = mPrevHb = mLastLb = mPrevLb = null;
            blkGroups = new List<List<Gfx>>();

            alertBar = new Dictionary<string, int>();
            off = 0;
            bi = -1;
            lastBi = -1;
        }
        #endregion

        protected override void OnBarUpdate()
        {
            if (b0 == null) return;
            if (BarsInProgress != 0 && BarsInProgress == fp1mIdx)
            {
                OnFp1mBar();
                return;
            }
            if (BarsInProgress != 0)
            {
                OnHtfBar(BarsInProgress);
                return;
            }
            int o = 0;
            if (Calculate != Calculate.OnBarClose)
            {
                if (!IsFirstTickOfBar) return;
                o = 1; // process the bar that just closed
            }
            int idx = CurrentBars[0] - o;
            if (idx < 0 || idx <= lastBi) return;
            off = o;
            bi = idx;
            lastBi = idx;
            Step();
            FlushDrawings();
        }

        #region Bar access helpers
        private double Op(int k) { return b0.GetOpen(bi - k); }
        private double Hp(int k) { return b0.GetHigh(bi - k); }
        private double Lp(int k) { return b0.GetLow(bi - k); }
        private double Cp(int k) { return b0.GetClose(bi - k); }
        private DateTime Tm(int k) { return b0.GetTime(bi - k); }
        private double HA(int idx) { return b0.GetHigh(idx); }
        private double LA(int idx) { return b0.GetLow(idx); }

        // bar open time (NT8 time stamps are close times)
        private DateTime BarOpen(int k)
        {
            if (openSpan > TimeSpan.Zero) return Tm(k) - openSpan;
            if (bi - k - 1 >= 0) return Tm(k + 1);
            return Tm(k);
        }

        private DateTime ToZone(DateTime t, TimeZoneInfo dest)
        {
            try { return TimeZoneInfo.ConvertTime(DateTime.SpecifyKind(t, DateTimeKind.Unspecified), localTz, dest); }
            catch (Exception) { return t; }
        }

        private static double PivHi(NinjaTrader.Data.Bars b, int last, int left, int right)
        {
            int c = last - right;
            if (c - left < 0 || right < 0) return double.NaN;
            double v = b.GetHigh(c);
            for (int i = 1; i <= left; i++)
                if (b.GetHigh(c - i) >= v) return double.NaN;
            for (int i = 1; i <= right; i++)
                if (b.GetHigh(c + i) > v) return double.NaN;
            return v;
        }

        private static double PivLo(NinjaTrader.Data.Bars b, int last, int left, int right)
        {
            int c = last - right;
            if (c - left < 0 || right < 0) return double.NaN;
            double v = b.GetLow(c);
            for (int i = 1; i <= left; i++)
                if (b.GetLow(c - i) <= v) return double.NaN;
            for (int i = 1; i <= right; i++)
                if (b.GetLow(c + i) < v) return double.NaN;
            return v;
        }

        private static bool InSess(int m, int s, int e)
        {
            if (e == 0) e = 1440;
            return s < e ? (m >= s && m < e) : (m >= s || m < e);
        }

        private bool Bounces(double p)
        {
            return (Math.Abs(p - bl) < 2 && bc > bl + 10) || (Math.Abs(p - bh) < 2 && bc < bh - 10);
        }

        private bool UpBreak(double hi) { return IfvgConfirmation == FioConfirm.FullBody ? Math.Min(bo, bc) > hi : bc > hi; }
        private bool DnBreak(double lo) { return IfvgConfirmation == FioConfirm.FullBody ? Math.Max(bo, bc) < lo : bc < lo; }

        private void Fire(string msg)
        {
            int last;
            if (alertBar.TryGetValue(msg, out last) && last == bi) return; // once per bar
            alertBar[msg] = bi;
            if (State != State.Realtime) return;
            Alert("FIO_" + msg, Priority.High, msg, NinjaTrader.Core.Globals.InstallDir + @"\sounds\Alert1.wav", 0, Brushes.Black, Brushes.Yellow);
        }
        #endregion

        #region Drawing model helpers
        private static Color Tr(Color c, int transp)
        {
            int t = Math.Max(0, Math.Min(100, transp));
            return Color.FromArgb((byte)Math.Round(255.0 * (100 - t) / 100.0), c.R, c.G, c.B);
        }

        private static Color Opaque(Color c) { return Color.FromRgb(c.R, c.G, c.B); }
        private static int OpacityOf(Color c) { return (int)Math.Round(c.A * 100.0 / 255.0); }

        private string NewTag()
        {
            gid++;
            return "fio" + gid.ToString();
        }

        private void Touch(Gfx g)
        {
            if (g == null || g.Dirty) return;
            g.Dirty = true;
            dirty.Add(g);
        }

        private void Del(Gfx g)
        {
            if (g == null || g.Deleted) return;
            g.Deleted = true;
            Touch(g);
        }

        private void SetExt(Gfx g, bool e)
        {
            if (g == null) return;
            g.Ext = e;
            if (e && !g.InExt)
            {
                g.InExt = true;
                extList.Add(g);
            }
            Touch(g);
        }

        private GBox NewBox(DateTime t1, double top, DateTime t2, double bot)
        {
            GBox b = new GBox();
            b.Tag = NewTag();
            b.T1 = t1;
            b.T2 = t2;
            b.Top = top;
            b.Bot = bot;
            b.Bg = Tr(CBlue, 90);
            b.Bd = CBlue;
            b.Tc = CBlack;
            Touch(b);
            return b;
        }

        private GBox NewBarBox(int b1, double top, int b2, double bot)
        {
            GBox b = NewBox(curTime, top, curTime, bot);
            b.BarBased = true;
            b.B1 = b1;
            b.B2 = b2;
            return b;
        }

        private GLine NewLine(DateTime t1, double y1, DateTime t2, double y2, Color col, DashStyleHelper sty, int w)
        {
            GLine l = new GLine();
            l.Tag = NewTag();
            l.T1 = t1;
            l.T2 = t2;
            l.Y1 = y1;
            l.Y2 = y2;
            l.Col = col;
            l.Sty = sty;
            l.W = Math.Max(1, w);
            Touch(l);
            return l;
        }

        private GLine NewBarLine(int b1, double y1, int b2, double y2, Color col, DashStyleHelper sty, int w)
        {
            GLine l = NewLine(curTime, y1, curTime, y2, col, sty, w);
            l.BarBased = true;
            l.B1 = b1;
            l.B2 = b2;
            return l;
        }

        private GMark NewMark(int kind, DateTime t, double y, Color col, string txt, Color tc, int ts, int yoff)
        {
            GMark m = new GMark();
            m.Tag = NewTag();
            m.Kind = kind;
            m.T = t;
            m.Y = y;
            m.Col = col;
            m.Txt = txt ?? "";
            m.Tc = tc;
            m.Ts = ts;
            m.YOff = yoff;
            Touch(m);
            return m;
        }

        private void BRight(GBox b, DateTime t)
        {
            if (b == null || b.Deleted || b.BarBased) return;
            if (b.T2 != t)
            {
                b.T2 = t;
                Touch(b);
            }
        }

        private void LRight(GLine l, DateTime t)
        {
            if (l == null || l.Deleted || l.BarBased) return;
            if (l.T2 != t)
            {
                l.T2 = t;
                Touch(l);
            }
        }

        private void BText(GBox b, string s)
        {
            if (b == null) return;
            b.Txt = s ?? "";
            Touch(b);
        }

        private Brush Br(Color c)
        {
            if (c.A == 0) return Brushes.Transparent;
            Brush b;
            if (!brushCache.TryGetValue(c, out b))
            {
                SolidColorBrush s = new SolidColorBrush(c);
                s.Freeze();
                b = s;
                brushCache[c] = b;
            }
            return b;
        }

        private SimpleFont GetFont(int size, bool bold)
        {
            int key = size * 2 + (bold ? 1 : 0);
            SimpleFont f;
            if (!fontCache.TryGetValue(key, out f))
            {
                f = new SimpleFont("Arial", size);
                f.Bold = bold;
                fontCache[key] = f;
            }
            return f;
        }

        private DateTime BarTime(int idx)
        {
            int last = CurrentBars[0];
            if (idx < 0) idx = 0;
            if (idx <= last) return b0.GetTime(idx);
            TimeSpan span = barSpan;
            if (span <= TimeSpan.Zero)
            {
                int n = Math.Min(last, 20);
                span = n > 0 ? TimeSpan.FromTicks((b0.GetTime(last) - b0.GetTime(last - n)).Ticks / n) : TimeSpan.FromMinutes(1);
            }
            return b0.GetTime(last) + TimeSpan.FromTicks(span.Ticks * (idx - last));
        }

        // objects are drawn at the end of the history and on every real time bar
        private void FlushDrawings()
        {
            for (int i = extList.Count - 1; i >= 0; i--)
            {
                Gfx g = extList[i];
                if (g.Deleted || !g.Ext)
                {
                    g.InExt = false;
                    extList.RemoveAt(i);
                }
                else
                    Touch(g);
            }
            if (dirty.Count == 0) return;
            if (!(State == State.Realtime || CurrentBars[0] >= b0.Count - 2)) return;
            for (int i = 0; i < dirty.Count; i++)
            {
                Gfx g = dirty[i];
                g.Dirty = false;
                try
                {
                    GBox bx = g as GBox;
                    if (bx != null)
                    {
                        RenderBox(bx);
                        continue;
                    }
                    GLine ln = g as GLine;
                    if (ln != null)
                    {
                        RenderLine(ln);
                        continue;
                    }
                    GMark mk = g as GMark;
                    if (mk != null) RenderMark(mk);
                }
                catch (Exception ex)
                {
                    if (!loggedErr)
                    {
                        loggedErr = true;
                        Print("FVGSIFVGOTE drawing error: " + ex.Message);
                    }
                }
            }
            dirty.Clear();
        }

        private void RenderBox(GBox b)
        {
            string tt = b.Tag + "_t";
            if (b.Deleted)
            {
                if (b.Drawn) { RemoveDrawObject(b.Tag); b.Drawn = false; }
                if (b.TxtDrawn) { RemoveDrawObject(tt); b.TxtDrawn = false; }
                return;
            }
            DateTime t1 = b.BarBased ? BarTime(b.B1) : b.T1;
            DateTime t2 = b.BarBased ? BarTime(b.B2) : b.T2;
            if (b.Ext && curTime > t2) t2 = curTime;
            bool vis = b.Bg.A > 0 || (b.Bw > 0 && b.Bd.A > 0);
            if (vis)
            {
                Brush bd = b.Bw > 0 ? Br(b.Bd) : Brushes.Transparent;
                var r = Draw.Rectangle(this, b.Tag, false, t1, b.Top, t2, b.Bot, bd, Br(Opaque(b.Bg)), OpacityOf(b.Bg));
                if (r != null) r.OutlineStroke = new Stroke(bd, b.Sty, Math.Max(1, b.Bw));
                b.Drawn = true;
            }
            else if (b.Drawn)
            {
                RemoveDrawObject(b.Tag);
                b.Drawn = false;
            }
            bool tvis = !string.IsNullOrEmpty(b.Txt) && b.Tc.A > 0;
            if (tvis)
            {
                DateTime tx = b.Ha == 0 ? t1 : (b.Ha == 2 ? t2 : t1.AddTicks((t2 - t1).Ticks / 2));
                double h = b.Top - b.Bot;
                double y = b.Va == 0 ? b.Top - h * 0.2 : (b.Va == 2 ? b.Bot + h * 0.2 : (b.Top + b.Bot) / 2.0);
                System.Windows.TextAlignment al = b.Ha == 0 ? System.Windows.TextAlignment.Left
                    : (b.Ha == 2 ? System.Windows.TextAlignment.Right : System.Windows.TextAlignment.Center);
                Draw.Text(this, tt, false, b.Txt, tx, y, 0, Br(b.Tc), GetFont(b.Ts, b.Bold), al, Brushes.Transparent, Brushes.Transparent, 0);
                b.TxtDrawn = true;
            }
            else if (b.TxtDrawn)
            {
                RemoveDrawObject(tt);
                b.TxtDrawn = false;
            }
        }

        private void RenderLine(GLine l)
        {
            if (l.Deleted || l.Col.A == 0)
            {
                if (l.Drawn) { RemoveDrawObject(l.Tag); l.Drawn = false; }
                return;
            }
            DateTime t1 = l.BarBased ? BarTime(l.B1) : l.T1;
            DateTime t2 = l.BarBased ? BarTime(l.B2) : l.T2;
            if (l.Ext && curTime > t2) t2 = curTime;
            Draw.Line(this, l.Tag, false, t1, l.Y1, t2, l.Y2, Br(l.Col), l.Sty, l.W);
            l.Drawn = true;
        }

        private void RenderMark(GMark m)
        {
            string tt = m.Tag + "_t";
            if (m.Deleted)
            {
                if (m.Drawn) { RemoveDrawObject(m.Tag); m.Drawn = false; }
                if (m.TxtDrawn) { RemoveDrawObject(tt); m.TxtDrawn = false; }
                return;
            }
            if (m.Kind == 0)
            {
                Draw.Text(this, m.Tag, false, m.Txt, m.T, m.Y, m.YOff, Br(m.Tc), GetFont(m.Ts, false), System.Windows.TextAlignment.Center,
                    Brushes.Transparent, Br(Opaque(m.Col)), OpacityOf(m.Col));
                m.Drawn = true;
                return;
            }
            if (m.Kind == 1) Draw.ArrowUp(this, m.Tag, false, m.T, m.Y, Br(m.Col));
            else if (m.Kind == 2) Draw.ArrowDown(this, m.Tag, false, m.T, m.Y, Br(m.Col));
            else Draw.Diamond(this, m.Tag, false, m.T, m.Y, Br(m.Col));
            m.Drawn = true;
            if (!string.IsNullOrEmpty(m.Txt))
            {
                Draw.Text(this, tt, false, m.Txt, m.T, m.Y, m.YOff, Br(m.Tc), GetFont(m.Ts, false), System.Windows.TextAlignment.Center,
                    Brushes.Transparent, Brushes.Transparent, 0);
                m.TxtDrawn = true;
            }
        }
        #endregion

        #region FVG methods
        private int Wdx()
        {
            int r = WickSegmentWidth;
            if (r == 0 && tfsecs / 60 < 15) r = 4;
            else if (r == 0) r = 1;
            return r;
        }

        private int Bw(Fvg f) { return FlagDispFvgs && f.disp ? FlagDispWidth : 1; }

        // label of a plain FVG box: A+, or the breaker / mitigation block tag (B↑, M↓, BM↑...), plus OTE
        private string FvgLbl(Fvg f)
        {
            string t = "";
            if (f.aplus && LabelAPlus)
                t = hideReg ? "" : "A+";
            else if (f.brk || f.mbD != 0)
                t = (f.brk && f.mbD != 0 ? "BM" : f.brk ? "B" : "M") + (f.lblD == 1 ? "\u2191" : "\u2193");
            if (f.ote && !hideReg)
                t += (t == "" ? "" : " ") + "OTE";
            return t;
        }

        // [OTEF] leg of a new FVG (candles [3] to [1]): origin = the lowest low (bullish) / highest high (bearish)
        // from the last swing before the FVG, extreme = the high / low of the FVG candles
        private void OteInit(Fvg f)
        {
            if (!FlagOteFvgs) return;
            int n = 3;
            if (f.islong && otePLb >= 0 && otePLb <= bi - 3) n = Math.Min(bi - otePLb, 4000);
            else if (!f.islong && otePHb >= 0 && otePHb <= bi - 3) n = Math.Min(bi - otePHb, 4000);
            n = Math.Min(n, bi);
            double lo = Lp(1), hi = Hp(1);
            for (int k = 1; k <= n; k++)
            {
                if (f.islong) lo = Math.Min(lo, Lp(k));
                else hi = Math.Max(hi, Hp(k));
            }
            if (f.islong)
            {
                f.oLo = lo;
                f.oHi = Math.Max(Hp(1), Math.Max(Hp(2), Hp(3)));
            }
            else
            {
                f.oHi = hi;
                f.oLo = Math.Min(Lp(1), Math.Min(Lp(2), Lp(3)));
            }
        }

        // [OTEF] once per bar for a live FVG: true on the bar it retraces into the FVG while it matches the OTE band
        private bool OteFvgStep(Fvg f)
        {
            bool hit = false;
            if (FlagOteFvgs && !f.ote && !double.IsNaN(f.oLo) && !double.IsNaN(f.oHi))
            {
                double r = f.oHi - f.oLo;
                if (r > 0)
                {
                    double bLo = f.islong ? f.oHi - OteUpper * r : f.oLo + OteLower * r;
                    double bHi = f.islong ? f.oHi - OteLower * r : f.oLo + OteUpper * r;
                    bool m = OteMatch == FioOteMode.Overlap ? f.hi >= bLo && f.lo <= bHi
                        : OteMatch == FioOteMode.FullyInside ? f.lo >= bLo && f.hi <= bHi
                        : f.ce >= bLo && f.ce <= bHi;
                    bool tap = f.islong ? bl <= f.hi : bh >= f.lo;
                    if (m && tap)
                    {
                        f.ote = true;
                        hit = true;
                    }
                }
                // keep extending the leg AFTER evaluating this bar
                if (f.islong) f.oHi = Math.Max(f.oHi, bh);
                else f.oLo = Math.Min(f.oLo, bl);
            }
            return hit;
        }

        private void Kill(Fvg f)
        {
            if (f == null) return;
            Del(f.rect);
            Del(f.celine);
            Del(f.hiln);
            Del(f.loln);
            f.rect = null;
            f.celine = null;
            f.hiln = null;
            f.loln = null;
        }

        private bool FvgAlerts(Fvg f)
        {
            bool a = false;
            if (!double.IsNaN(f.hi) && Bounces(f.hi)) { Fire("bounce on FVG hi"); a = true; }
            else if (!double.IsNaN(f.lo) && Bounces(f.lo)) { Fire("bounce on fvg lo"); a = true; }
            else if (!double.IsNaN(f.ce) && Bounces(f.ce)) { Fire("bounce on CE"); a = true; }
            return a;
        }

        private void ClearLastFp()
        {
            while (fpfvgs.Count > FpKeep)
            {
                Fvg f = fpfvgs[0];
                fpfvgs.RemoveAt(0);
                if (f != null && f.isfp)
                {
                    if (f.rect != null) f.rect.Ext = false;
                    if (f.celine != null) f.celine.Ext = false;
                    if (f.hiln != null) f.hiln.Ext = false;
                    if (f.loln != null) f.loln.Ext = false;
                    f.isfp = false;
                    Kill(f);
                }
            }
        }

        private bool IsFPFVG()
        {
            bool r = Show1PFVGs;
            if (r)
            {
                DateTime t = ToZone(BarOpen(0), tzInfo);
                int nmin = 2;
                if (isMinutes) nmin = (int)(tfmin * 2);
                r = t.Hour == fph && t.Minute > fpm + nmin && isMinutes && tfmin <= 3 && fp1mIdx < 0;
            }
            return r;
        }

        private DateTime EndsAt(Fvg f) { return f.end.HasValue ? f.end.Value : curTime; }

        private bool IsDone(Fvg f, double o, double c) { return f.islong ? (c < f.lo || o < f.lo) : (c > f.hi || o > f.hi); }

        private Color BgColor(Fvg f)
        {
            Color c = f.islong ? cL : cS;
            if (f.isfp)
            {
                c = cFp;
                if (f.iswfp) c = cWfp;
            }
            if (f.isifp) c = cIfp;
            bool hid = hideReg && (ifvgOnly || (!f.isfp && !f.isifp));
            return styNoFill ? CTransp : (hid ? Tr(c, 100) : c);
        }

        private Color LnColor(Fvg f)
        {
            Color c = f.islong ? cLn : cSn;
            if (f.isfp)
            {
                c = cFpn;
                if (f.iswfp) c = cWfpn;
            }
            if (f.isifp) c = cIfpn;
            bool hid = hideReg && (ifvgOnly || (!f.isfp && !f.isifp));
            return hid ? Tr(c, 100) : c;
        }

        // back to the plain FVG, as it was before becoming an IFVG
        private void Restore(Fvg f)
        {
            f.kept = false;
            f.cand = false;
            if (f.rect != null)
            {
                if (f.own || stySeg)
                {
                    Del(f.rect);
                    f.rect = null;
                    f.own = false;
                }
                else
                {
                    GBox r = f.rect;
                    r.Bg = BgColor(f);
                    r.Bd = LnColor(f);
                    r.Bw = Bw(f);
                    r.Txt = FvgLbl(f);
                    r.T2 = EndsAt(f);
                    SetExt(r, f.isifp || f.isfp);
                }
            }
        }

        // the (older) FVG that absorbed a duplicate is the one that is shown
        private GBox Adopt(Fvg fv, int dir)
        {
            Fvg f = fv.dup ?? fv;
            Color c = dir < 0 ? cBearIf : cBullIf;
            if (f.rect == null)
            {
                f.rect = NewBox(f.start, f.hi, curTime, f.lo);
                f.own = !(f.shown || f.isfp || f.isifp);
            }
            GBox r = f.rect;
            if (f.isfp || f.isifp)
            {
                // also a 1PFVG: IFVG label, but 1PFVG colors and behaviour (extends right)
                Color fc = f.isifp ? cIfp : (f.iswfp ? cWfp : cFp);
                Color lc = f.isifp ? cIfpn : (f.iswfp ? cWfpn : cFpn);
                r.Bg = styNoFill ? CTransp : fc;
                r.Bd = lc;
                r.Bw = 1;
                SetExt(r, true);
            }
            else
            {
                r.Bg = Tr(c, IfvgFillTransparency);
                r.Bd = Tr(c, IfvgBorderTransparency);
                r.Bw = 2;
                SetExt(r, false);
            }
            r.T2 = curTime;
            // an unconfirmed candidate keeps its B / M / BM label (confirming it sets the IFVG label)
            r.Txt = f.brk || f.mbD != 0 ? FvgLbl(f) : "";
            r.Tc = cIfvgTxt;
            r.Ts = ifvgTs;
            r.Ha = ifvgHa;
            r.Va = ifvgVa;
            Touch(r);
            if (!f.kept) keptIf.Add(f);
            f.kept = true;
            f.cand = true;
            f.kdir = dir;
            return r;
        }

        // the IFVG candidate is replaced: keep it if confirmed (and wanted), else restore the FVG
        private void Release(Fvg fv, bool confirmed, DateTime? ctime)
        {
            Fvg f = fv.dup ?? fv;
            f.cand = false;
            if (f.kept)
            {
                if (KeepIfvgs && confirmed)
                {
                    if (!f.end.HasValue) f.end = ctime;
                    if (keptIf.Count > KeepIfvgsMax + 3)
                    {
                        Fvg o = keptIf[0];
                        keptIf.RemoveAt(0);
                        if (o.cand) keptIf.Add(o);
                        else Restore(o);
                    }
                }
                else
                    Restore(f);
            }
        }

        // extend the IFVG boxes; restore kept ones when invalidated (close back through them)
        private void UpdKept()
        {
            for (int i = keptIf.Count - 1; i >= 0; i--)
            {
                Fvg f = keptIf[i];
                if (f.rect == null || !f.kept)
                {
                    f.kept = false;
                    f.cand = false;
                    keptIf.RemoveAt(i);
                }
                else if (!f.cand && (f.kdir < 0 ? bc > f.hi : bc < f.lo))
                {
                    Restore(f);
                    keptIf.RemoveAt(i);
                }
                else
                    BRight(f.rect, curTime);
            }
        }

        // widen g (older FVG) with f (new almost identical one): max hi / min lo, for both, and extend g
        private void MergeZone(Fvg g, Fvg f)
        {
            if (g.kept) return;
            double nh = Math.Max(g.hi, f.hi);
            double nl = Math.Min(g.lo, f.lo);
            g.hi = nh;
            g.lo = nl;
            g.ce = (nh + nl) / 2.0;
            f.hi = nh;
            f.lo = nl;
            f.ce = g.ce;
            if (g.rect != null)
            {
                g.rect.Top = nh;
                g.rect.Bot = nl;
                Touch(g.rect);
            }
            if (g.hiln != null)
            {
                g.hiln.Y1 = nh;
                g.hiln.Y2 = nh;
                Touch(g.hiln);
                if (g.loln != null)
                {
                    g.loln.Y1 = nl;
                    g.loln.Y2 = nl;
                    Touch(g.loln);
                }
            }
            if (g.celine != null)
            {
                g.celine.Y1 = g.ce;
                g.celine.Y2 = g.ce;
                Touch(g.celine);
            }
            g.end = null;
        }

        private static void InsertSorted(List<Fvg> a, Fvg f)
        {
            int pos = a.Count;
            while (pos > 0 && a[pos - 1].start > f.start) pos--;
            f.shown = true;
            a.Insert(pos, f);
        }

        // older drawn FVG (any bias) that is almost exactly the same zone as f
        private Fvg FindDup(Fvg f)
        {
            Fvg r = null;
            double tol = (f.hi - f.lo) * MergeTolerance / 100.0;
            if (MergeTolerance > 0)
            {
                for (int k = 0; k <= 3; k++)
                {
                    List<Fvg> arr = k == 0 ? lfvgs : (k == 1 ? sfvgs : (k == 2 ? fpfvgs : keptIf));
                    for (int i = arr.Count - 1; i >= 0; i--)
                    {
                        Fvg g = arr[i];
                        if (Math.Abs(g.hi - f.hi) <= tol && Math.Abs(g.lo - f.lo) <= tol && (r == null || g.start > r.start))
                        {
                            r = g;
                            break;
                        }
                    }
                }
            }
            return r;
        }

        private void Route(Fvg f)
        {
            Fvg d = FindDup(f);
            bool shown = false;
            if (d != null)
            {
                // extend the old FVG instead of adding the new one; the new FVG keeps feeding the IFVG logic
                f.dup = d;
                MergeZone(d, f);
            }
            else if (fltOff)
                shown = true;
            else if (fsPost < flN)
            {
                shown = true;
                fsPost++;
            }
            if (shown)
                InsertSorted(f.islong ? lfvgs : sfvgs, f);
            else if (d == null)
            {
                pendF.Add(f);
                if (pendF.Count > 60) pendF.RemoveAt(0);
            }
        }

        // show a pending FVG: if a drawn one is almost the same zone, merge into it instead of drawing a second box
        private void Reveal(Fvg f)
        {
            Fvg d = f.kept ? null : FindDup(f);
            if (d == null)
                InsertSorted(f.islong ? lfvgs : sfvgs, f);
            else
            {
                f.dup = d;
                if (f.start < d.start && !d.kept)
                {
                    d.start = f.start;
                    if (d.rect != null) { d.rect.T1 = d.start; Touch(d.rect); }
                    if (d.hiln != null) { d.hiln.T1 = d.start; Touch(d.hiln); }
                    if (d.loln != null) { d.loln.T1 = d.start; Touch(d.loln); }
                    if (d.celine != null) { d.celine.T1 = d.start; Touch(d.celine); }
                }
                MergeZone(d, f);
            }
        }

        // a sweep happened: reveal the last FVG before it (and, optionally, the largest one near the middle of the arm)
        private void OnSweep(int? armStart)
        {
            int n = pendF.Count;
            if (n > 0)
            {
                Fvg lastF = pendF[n - 1];
                Fvg midF = null;
                if (fltMid && n > 1 && armStart.HasValue && armStart.Value < bi)
                {
                    int mb = (int)Math.Round((armStart.Value + bi) / 2.0, MidpointRounding.AwayFromZero);
                    int w = Math.Max(1, (int)Math.Round((bi - armStart.Value) * flWin / 100.0, MidpointRounding.AwayFromZero));
                    double best = 0.0;
                    for (int i = 0; i <= n - 2; i++)
                    {
                        Fvg g = pendF[i];
                        double sz = g.hi - g.lo;
                        if (g.sbar >= mb - w && g.sbar <= mb + w && sz > best)
                        {
                            best = sz;
                            midF = g;
                        }
                    }
                }
                Reveal(lastF);
                if (midF != null) Reveal(midF);
                pendF.Clear();
            }
        }

        private void Classify(Fvg f, bool fpNow)
        {
            if (!hasifp && hasfp && FpInverse && fpfvgs.Count > 0)
            {
                Fvg fp = fpfvgs[fpfvgs.Count - 1];
                if (fp.islong != f.islong)
                {
                    f.isifp = true;
                    hasifp = true;
                    fpfvgs.Add(f);
                }
            }
            if (!hasfp && fpNow && !f.isifp)
            {
                f.isfp = true;
                f.iswfp = ToZone(BarOpen(0), exchTz).DayOfWeek == DayOfWeek.Monday;
                hasfp = f.isfp;
                fpfvgs.Add(f);
                ClearLastFp();
            }
            else if (!fponly && !f.isifp)
                Route(f);
            else
                Kill(f);
        }

        private void UpdFvgs(List<Fvg> fvgs)
        {
            for (int i = 0; i < fvgs.Count; i++)
            {
                Fvg f = fvgs[i];
                // check if filled
                bool isdone = f.end.HasValue;
                if (!f.end.HasValue && IsDone(f, bo, bc)) f.end = EndsAt(f);
                // [OTEF] retraced into while matching the OTE band of its leg (FVG shown as a plain FVG)
                if (!isdone && !f.end.HasValue && f.dup == null && OteFvgStep(f) && f.rect != null && !f.cand && !f.kept)
                {
                    f.rect.Txt = FvgLbl(f);
                    f.rect.Ts = ifvgTs;
                    f.rect.Ha = ifvgHa;
                    f.rect.Va = ifvgVa;
                    Touch(f.rect);
                }
                DateTime et = EndsAt(f);
                bool ext = f.isifp || f.isfp;
                if (stySeg)
                {
                    if (f.hiln == null)
                    {
                        f.hiln = NewLine(f.start, f.hi, et, f.hi, LnColor(f), DashStyleHelper.Solid, Bw(f));
                        f.loln = NewLine(f.start, f.lo, et, f.lo, LnColor(f), DashStyleHelper.Solid, Bw(f));
                        if (ext)
                        {
                            SetExt(f.hiln, true);
                            SetExt(f.loln, true);
                        }
                    }
                }
                else if (f.rect == null)
                {
                    f.rect = NewBox(f.start, f.hi, et, f.lo);
                    f.rect.Bg = BgColor(f);
                    f.rect.Bd = LnColor(f);
                    f.rect.Bw = Bw(f);
                    if (ext) SetExt(f.rect, true);
                    if (FvgLbl(f) != "")
                    {
                        f.rect.Txt = FvgLbl(f);
                        f.rect.Ts = ifvgTs;
                        f.rect.Ha = ifvgHa;
                        f.rect.Va = ifvgVa;
                    }
                }
                if (f.celine == null && f.hi - f.lo > CeMinHeight)
                {
                    f.celine = NewLine(f.start, f.ce, et, f.ce, LnColor(f), CeStyle, CeWidth);
                    if (f.isfp) SetExt(f.celine, true);
                }
                // always follow the end: an FVG extended by a merged duplicate (end reset) has to grow again
                if (f.rect != null && !f.kept) BRight(f.rect, et);
                if (f.hiln != null)
                {
                    LRight(f.hiln, et);
                    LRight(f.loln, et);
                }
                if (f.celine != null) LRight(f.celine, et);
                if (FvgBounceAlerts && !f.end.HasValue) FvgAlerts(f);
            }
            if (fvgs.Count >= FvgsKept && fvgs.Count > 0)
            {
                Fvg df = fvgs[0];
                fvgs.RemoveAt(0);
                Kill(df);
            }
        }
        #endregion

        #region Delivery Engine helpers
        // A+ for an IFVG confirmed on this bar in direction d
        private bool DeAp(int d)
        {
            return APlusSetups && deKzOK && (d == 1 ? deSslRecent && deHtfUp : deBslRecent && deHtfDn);
        }

        // IFVG label: sign, inversion count, OTE (and its timeframes), A+, mitigation block arrow
        private string IfvgTxt(int d, int inv, bool ote, string tags, bool ap, int mbd)
        {
            string t = "IFVG" + (LabelSign ? (d < 0 ? "-" : "+") : "");
            if (inv > 1) t += " \u00D7" + inv.ToString();
            if (ote) t += " OTE" + (LabelOteTf && tags != "" ? " " + tags : "");
            if (ap && LabelAPlus) t += " A+";
            if (mbd != 0 && mbd == d) t += mbd > 0 ? " \u2191" : " \u2193";
            return t;
        }

        private static int MbOf(Fvg f) { return f == null ? 0 : f.mbD; }

        // invalidated IFVG: faded (stays as it is) or back to the plain FVG
        private void DeKill(Fvg fv)
        {
            Fvg f = fv.dup ?? fv;
            if (InvalidatedIfvg == FioDeadMode.BackToFVG)
                Restore(f);
            else
            {
                f.kept = false;
                f.cand = false;
                if (f.rect != null)
                {
                    f.rect.Bg = cDead;
                    f.rect.Bd = cDead;
                    f.rect.Bw = 1;
                    f.rect.Txt = "";
                    f.rect.T2 = curTime;
                    Touch(f.rect);
                }
            }
        }

        // the box of an inverted zone in direction d
        private void DeRecolor(GBox b, int d)
        {
            if (b == null) return;
            Color c = d < 0 ? cBearIf : cBullIf;
            b.Bg = Tr(c, IfvgFillTransparency);
            b.Bd = Tr(c, IfvgBorderTransparency);
            Touch(b);
        }
        #endregion

        #region Higher timeframes
        private void OnHtfBar(int k)
        {
            int ho = 0;
            if (Calculate != Calculate.OnBarClose)
            {
                if (!IsFirstTickOfBar) return;
                ho = 1; // completed HTF bars only
            }
            int p = CurrentBars[k] - ho;
            if (p < 0) return;
            NinjaTrader.Data.Bars hb = BarsArray[k];
            if (hasH1 && k == htfIdx1) HtfPivots(hb, p, pend1);
            if (hasH2 && k == htfIdx2) HtfPivots(hb, p, pend2);
            if (deHtfUse && k == deHtfIdx) HtfStruct(hb, p);
        }

        // 1PFVG from the 1m candles: each completed 1m candle is candle [0] of IsFPFVG on the 1m chart
        // (candles [1] to [3] are the three before it); Pine: request.security_lower_tf
        private void OnFp1mBar()
        {
            int ho = 0;
            if (Calculate != Calculate.OnBarClose)
            {
                if (!IsFirstTickOfBar) return;
                ho = 1; // completed 1m bars only
            }
            int p = CurrentBars[fp1mIdx] - ho;
            if (p < 3 || hasfp || fpfvgs == null) return;
            NinjaTrader.Data.Bars mb = BarsArray[fp1mIdx];
            DateTime t0 = ToZone(mb.GetTime(p).AddMinutes(-1), tzInfo); // open time of candle [0]
            if (t0.Hour != fph || t0.Minute <= fpm + 2) return;
            double o1 = mb.GetOpen(p - 1), c1 = mb.GetClose(p - 1), h1 = mb.GetHigh(p - 1), l1 = mb.GetLow(p - 1);
            double o2 = mb.GetOpen(p - 2), c2 = mb.GetClose(p - 2);
            double o3 = mb.GetOpen(p - 3), c3 = mb.GetClose(p - 3), h3 = mb.GetHigh(p - 3), l3 = mb.GetLow(p - 3);
            double bhi2 = Math.Max(o2, c2), blow2 = Math.Min(o2, c2);
            Fvg f = null;
            // long: low of [1] above the high of [3]; include suspension blocks
            if (l1 - h3 > MinFvgSize && l1 - h3 < MaxFvgSize)
            {
                double hi = l1, lo = h3;
                if (Math.Max(o3, c3) < blow2) lo = Math.Max(o3, c3);
                if (Math.Min(o1, c1) > bhi2) hi = Math.Min(o1, c1);
                f = new Fvg { hi = hi, lo = lo, ce = (hi + lo) / 2.0, islong = true };
            }
            // short: high of [1] below the low of [3]
            else if (l3 - h1 > MinFvgSize && l3 - h1 < MaxFvgSize)
            {
                double hi = l3, lo = h1;
                if (Math.Min(o3, c3) > bhi2) hi = Math.Min(o3, c3);
                if (Math.Max(o1, c1) < blow2) lo = Math.Max(o1, c1);
                f = new Fvg { hi = hi, lo = lo, ce = (hi + lo) / 2.0, islong = false };
            }
            if (f == null) return;
            f.start = mb.GetTime(p - 3);
            f.sbar = Math.Max(bi, 0);
            f.isfp = true;
            f.iswfp = ToZone(mb.GetTime(p).AddMinutes(-1), exchTz).DayOfWeek == DayOfWeek.Monday;
            hasfp = true;
            fpfvgs.Add(f);
            ClearLastFp();
        }

        // a pivot of a completed HTF bar, registered on the next chart bar (Pine: request.security [1] + lookahead_on)
        private void HtfPivots(NinjaTrader.Data.Bars hb, int p, List<HtfPiv> pend)
        {
            int c = p - HtfPivotRight;
            if (c - 1 < 0) return;
            double ph = PivHi(hb, p, HtfPivotLeft, HtfPivotRight);
            double pl = PivLo(hb, p, HtfPivotLeft, HtfPivotRight);
            if (double.IsNaN(ph) && double.IsNaN(pl)) return;
            HtfPiv x = new HtfPiv();
            x.ph = ph;
            x.pl = pl;
            x.tprev = hb.GetTime(c - 1); // close of the HTF bar before the pivot bar = open of the pivot bar
            pend.Add(x);
            if (pend.Count > 20) pend.RemoveAt(0);
        }

        // HTF structure: 1 after a close above the last swing high, -1 after a close below the last swing low
        private void HtfStruct(NinjaTrader.Data.Bars hb, int p)
        {
            int len = LiquiditySwingLength;
            double ph = PivHi(hb, p, len, len);
            double pl = PivLo(hb, p, len, len);
            double cl = hb.GetClose(p);
            if (!double.IsNaN(hsSh) && cl > hsSh)
            {
                hsD = 1;
                hsSh = double.NaN;
            }
            if (!double.IsNaN(hsSl) && cl < hsSl)
            {
                hsD = -1;
                hsSl = double.NaN;
            }
            if (!double.IsNaN(ph)) hsSh = ph;
            if (!double.IsNaN(pl)) hsSl = pl;
        }

        // chart bar index of the chart candle that contains the start of an HTF bar (first chart bar closing after tprev)
        private int BarOfTime(DateTime tprev)
        {
            int mx = Math.Min(bi, 4900);
            for (int k = 0; k <= mx; k++)
            {
                if (Tm(k) <= tprev)
                {
                    int r = bi - k + 1;
                    return r > bi ? bi : r;
                }
            }
            return -1;
        }

        private void FeedHtf(PivSet ps, List<HtfPiv> pend)
        {
            for (int i = 0; i < pend.Count; i++)
            {
                HtfPiv x = pend[i];
                int b = BarOfTime(x.tprev);
                if (b >= 0)
                {
                    if (!double.IsNaN(x.ph)) AddHigh(ps, b, x.ph);
                    if (!double.IsNaN(x.pl)) AddLow(ps, b, x.pl);
                }
            }
        }
        #endregion

        #region OTE
        private static void AddHigh(PivSet ps, int b, double p)
        {
            ps.hb.Add(b);
            ps.hp.Add(p);
            if (ps.hb.Count > 60)
            {
                ps.hb.RemoveAt(0);
                ps.hp.RemoveAt(0);
            }
        }

        private static void AddLow(PivSet ps, int b, double p)
        {
            ps.lb.Add(b);
            ps.lp.Add(p);
            if (ps.lb.Count > 60)
            {
                ps.lb.RemoveAt(0);
                ps.lp.RemoveAt(0);
            }
        }

        // leg that precedes the FVG starting at bar sbar
        //   dir -1 (bearish IFVG): last pivot high before sbar -> lowest low up to sbar
        //   dir +1 (bullish IFVG): last pivot low before sbar -> highest high up to sbar
        private void PriorLeg(int dir, int sbar, PivSet ps, out double h, out double l)
        {
            h = double.NaN;
            l = double.NaN;
            if (dir == -1)
            {
                for (int i = ps.hb.Count - 1; i >= 0; i--)
                {
                    int hb = ps.hb[i];
                    if (hb < sbar && bi - hb < 4000 && hb >= 0 && sbar <= bi)
                    {
                        double lo = LA(hb);
                        for (int k = hb; k <= sbar; k++) lo = Math.Min(lo, LA(k));
                        h = ps.hp[i];
                        l = lo;
                        break;
                    }
                }
            }
            else
            {
                for (int i = ps.lb.Count - 1; i >= 0; i--)
                {
                    int lb = ps.lb[i];
                    if (lb < sbar && bi - lb < 4000 && lb >= 0 && sbar <= bi)
                    {
                        double hi = HA(lb);
                        for (int k = lb; k <= sbar; k++) hi = Math.Max(hi, HA(k));
                        l = ps.lp[i];
                        h = hi;
                        break;
                    }
                }
            }
        }

        // new IFVG candidate under watch (not yet confirmed). origin = retrace extreme since the FVG
        private void OteStart(OteTrack t, int dir, double zlo, double zhi, double origin, int sbar, PivSet ps)
        {
            Del(t.band);
            t.band = null;
            Del(t.rej);
            t.rej = null;
            t.rejHit = false;
            t.sbar = sbar;
            t.tag = ps.tag;
            double pgh, pgl;
            PriorLeg(dir, sbar, ps, out pgh, out pgl);
            t.pHi = pgh;
            t.pLo = pgl;
            double o = origin;
            if (ps.htf)
            {
                double adj = dir == -1 ? (ps.hp.Count > 0 ? ps.hp[ps.hp.Count - 1] : double.NaN)
                                       : (ps.lp.Count > 0 ? ps.lp[ps.lp.Count - 1] : double.NaN);
                if (!double.IsNaN(adj)) o = dir == -1 ? Math.Max(origin, adj) : Math.Min(origin, adj);
            }
            t.dir = dir;
            t.zlo = zlo;
            t.zhi = zhi;
            t.armed = false;
            t.fired = false;
            t.ctime = null;
            t.rext = origin;
            t.legHi = dir == -1 ? o : double.NaN;
            t.legLo = dir == 1 ? o : double.NaN;
        }

        // close through the FVG: the IFVG is confirmed, the leg starts being measured
        private void OteConfirm(OteTrack t)
        {
            if (t.dir != 0 && !t.armed)
            {
                // rejection of the OTE of the PRIOR leg by the confirming candle
                if (DetectOte && PriorLegRejection && !double.IsNaN(t.pHi) && !double.IsNaN(t.pLo) && t.pHi > t.pLo)
                {
                    double pr = t.pHi - t.pLo;
                    double ext, rLo, rHi;
                    bool hit;
                    if (t.dir == -1)
                    {
                        // prior down leg: OTE band is above the leg low; wick must reach up into it
                        ext = RejectionAnyCandle ? Math.Max(t.rext, bh) : bh;
                        rLo = t.pLo + OteLower * pr;
                        rHi = t.pLo + OteUpper * pr;
                        hit = ext >= rLo && ext <= (RejectionCapWick ? rHi : t.pHi) && bc <= rHi;
                    }
                    else
                    {
                        // prior up leg: OTE band is below the leg high; wick must reach down into it
                        ext = RejectionAnyCandle ? Math.Min(t.rext, bl) : bl;
                        rLo = t.pHi - OteUpper * pr;
                        rHi = t.pHi - OteLower * pr;
                        hit = ext <= rHi && ext >= (RejectionCapWick ? rLo : t.pLo) && bc >= rLo;
                    }
                    if (hit)
                    {
                        t.rejHit = true;
                        if (DrawOteBands)
                        {
                            GBox r = NewBarBox(OteX0(t, bi), rHi, OteX0(t, bi) + OteBandWidth, rLo);
                            r.Bg = cOteBand;
                            r.Bd = cOteHi;
                            r.Sty = DashStyleHelper.Dash;
                            r.Bw = 1;
                            r.Txt = t.tag + " OTE" + (t.dir == 1 ? " \u2191" : " \u2193");
                            r.Ts = 10;
                            r.Tc = cOteTxt;
                            r.Ha = 1;
                            r.Va = 1;
                            t.rej = r;
                        }
                    }
                }
                t.armed = true;
                t.fired = t.rejHit; // one OTE signal per IFVG
                t.ctime = curTime;
                t.cbar = bi;
                if (t.dir == -1)
                {
                    t.legHi = Math.Max(t.legHi, bh);
                    t.legLo = bl;
                }
                else
                {
                    t.legLo = Math.Min(t.legLo, bl);
                    t.legHi = bh;
                }
            }
        }

        // IFVG invalidated (closed back through it)
        private void OteCancel(OteTrack t)
        {
            Del(t.band);
            t.band = null;
            Del(t.rej);
            t.rej = null;
            t.rejHit = false;
            t.armed = false;
            if (t.dir == -1) t.legLo = double.NaN;
            else t.legHi = double.NaN;
        }

        // once per bar. Returns the direction (-1 / 1) on the bar the OTE is matched, else 0
        private int OteStep(OteTrack t)
        {
            int s = 0;
            if (DetectOte && t.dir != 0)
            {
                if (!t.armed)
                {
                    // before confirmation keep extending the origin of the leg
                    if (t.dir == -1)
                    {
                        t.legHi = Math.Max(t.legHi, bh);
                        t.rext = Math.Max(t.rext, bh);
                    }
                    else
                    {
                        t.legLo = Math.Min(t.legLo, bl);
                        t.rext = Math.Min(t.rext, bl);
                    }
                }
                else
                {
                    if (t.rej != null)
                    {
                        t.rej.B1 = OteX0(t, bi);
                        t.rej.B2 = OteX0(t, bi) + OteBandWidth;
                        Touch(t.rej);
                    }
                    // rejection of the prior-leg OTE flagged on the confirming candle
                    if (t.rejHit)
                    {
                        s = t.dir;
                        t.rejHit = false;
                    }
                    double r = t.legHi - t.legLo;
                    if (r > 0)
                    {
                        double bLo, bHi;
                        if (t.dir == -1)
                        {
                            bLo = t.legLo + OteLower * r;
                            bHi = t.legLo + OteUpper * r;
                        }
                        else
                        {
                            bLo = t.legHi - OteUpper * r;
                            bHi = t.legHi - OteLower * r;
                        }
                        double mid = (t.zlo + t.zhi) / 2.0;
                        bool m;
                        if (OteMatch == FioOteMode.Overlap) m = t.zhi >= bLo && t.zlo <= bHi;
                        else if (OteMatch == FioOteMode.FullyInside) m = t.zlo >= bLo && t.zhi <= bHi;
                        else m = mid >= bLo && mid <= bHi;
                        if (DrawOteBands)
                        {
                            Color bg = m ? cOteBand : Tr(CGray, 92);
                            Color bd = m ? cOteHi : Tr(CGray, 60);
                            if (t.band == null)
                            {
                                GBox nb = NewBarBox(OteX0(t, bi), bHi, OteX0(t, bi) + OteBandWidth, bLo);
                                nb.Sty = DashStyleHelper.Dash;
                                nb.Bw = 1;
                                nb.Ts = 8;
                                nb.Tc = cOteTxt;
                                nb.Ha = 0;
                                nb.Va = 1;
                                t.band = nb;
                            }
                            GBox bd2 = t.band;
                            bd2.Top = bHi;
                            bd2.Bot = bLo;
                            bd2.B1 = OteX0(t, bi);
                            bd2.B2 = OteX0(t, bi) + OteBandWidth;
                            bd2.Bg = bg;
                            bd2.Bd = bd;
                            bd2.Txt = t.tag + (t.dir == 1 ? " \u2191" : " \u2193");
                            Touch(bd2);
                        }
                        // price retraces back into the IFVG while it matches the OTE band
                        bool tap = t.dir == -1 ? bh >= t.zlo : bl <= t.zhi;
                        if (m && tap && !t.fired && t.ctime.HasValue && curTime != t.ctime.Value)
                        {
                            t.fired = true;
                            s = t.dir;
                        }
                    }
                    // keep extending the leg AFTER evaluating this bar
                    if (t.dir == -1) t.legLo = Math.Min(t.legLo, bl);
                    else t.legHi = Math.Max(t.legHi, bh);
                }
            }
            return s;
        }

        // first bar of the OTE bands of t: one column per timeframe, so several timeframes do not overlap
        private int OteX0(OteTrack t, int bi)
        {
            return bi + OteBandStart + t.col * (OteBandWidth + 1);
        }

        private void GStart(OteGroup g, int dir, double zlo, double zhi, double origin, int sbar)
        {
            g.flagged = false;
            g.tags = "";
            g.t0.col = 0;
            g.t1.col = 1;
            g.t2.col = hasH1 ? 2 : 1;
            OteStart(g.t0, dir, zlo, zhi, origin, sbar, pv0);
            if (hasH1) OteStart(g.t1, dir, zlo, zhi, origin, sbar, pv1);
            if (hasH2) OteStart(g.t2, dir, zlo, zhi, origin, sbar, pv2);
        }

        private void GConfirm(OteGroup g)
        {
            OteConfirm(g.t0);
            OteConfirm(g.t1);
            OteConfirm(g.t2);
        }

        private void GCancel(OteGroup g)
        {
            g.flagged = false;
            g.tags = "";
            OteCancel(g.t0);
            OteCancel(g.t1);
            OteCancel(g.t2);
        }

        private int GStep(OteGroup g)
        {
            int s0 = OteStep(g.t0);
            int s1 = OteStep(g.t1);
            int s2 = OteStep(g.t2);
            int s = 0;
            if (!g.flagged && (s0 != 0 || s1 != 0 || s2 != 0))
            {
                g.flagged = true;
                s = s0 != 0 ? s0 : (s1 != 0 ? s1 : s2);
                string tg = "";
                if (s0 != 0) tg += g.t0.tag;
                if (s1 != 0) tg += (tg == "" ? "" : "+") + g.t1.tag;
                if (s2 != 0) tg += (tg == "" ? "" : "+") + g.t2.tag;
                g.tags = tg;
            }
            return s;
        }

        // swing mode: last FVG starting before a swing time (keeps its last value, like the Pine 'var')
        private static Fvg FindFvg(DateTime sTime, List<Fvg> fvgs, ref Fvg rf)
        {
            for (int i = fvgs.Count - 1; i >= 0; i--)
            {
                rf = fvgs[i];
                if (rf.start < sTime) break;
            }
            return rf;
        }
        #endregion

        #region Breakers / mitigation blocks
        // zone from candle k0 bars back: the up-close (dir 1) / down-close (dir -1) candle there or the nearest one
        // before it (not before bar 'from'), plus the consecutive ones before it in "Whole sequence" mode
        private bool BlkZone(int dir, int k0, int from, bool whole, out double zh, out double zl, out DateTime zt, out DateTime zt1)
        {
            zh = double.NaN;
            zl = double.NaN;
            zt = default(DateTime);
            zt1 = default(DateTime);
            bool found = false;
            int kmax = Math.Min(Math.Min(bi - from, 300), bi);
            int k = k0;
            while (k <= kmax && !found)
            {
                if ((Cp(k) - Op(k)) * dir > 0)
                {
                    zh = Hp(k);
                    zl = Lp(k);
                    zt = Tm(k);
                    zt1 = Tm(k);
                    found = true;
                }
                k++;
            }
            if (found && whole)
            {
                while (k <= kmax && (Cp(k) - Op(k)) * dir > 0)
                {
                    zh = Math.Max(zh, Hp(k));
                    zl = Math.Min(zl, Lp(k));
                    zt = Tm(k);
                    k++;
                }
            }
            return found;
        }

        private static void BlkAdd(List<Blk> list, int dir, double zh, double zl, DateTime zt, DateTime zt1, int keep)
        {
            Blk b = new Blk();
            b.dir = dir;
            b.hi = zh;
            b.lo = zl;
            b.t = zt;
            b.t1 = zt1;
            list.Add(b);
            int cnt = 0;
            foreach (Blk x in list)
                if (x.dir == dir) cnt++;
            if (cnt > keep)
            {
                for (int i = 0; i < list.Count; i++)
                {
                    if (list[i].dir == dir)
                    {
                        list.RemoveAt(i);
                        break;
                    }
                }
            }
        }

        private void DelBlk(Blk b)
        {
            if (b.bx != null)
            {
                Del(b.bx);
                Del(b.ml);
            }
            if (b.fl != null)
                foreach (GLine ln in b.fl) Del(ln);
        }

        // first matching pullback: flag the zone candles and draw the rectangle with its mid line (once)
        private void BlkDraw(Blk b, bool flag, bool box, bool shd, Color col, DashStyleHelper sty, int w)
        {
            double mid = (b.hi + b.lo) / 2.0;
            if (b.tagged) return;
            b.tagged = true;
            List<Gfx> grp = new List<Gfx>();
            if (flag)
            {
                b.fl = new List<GLine>();
                int kk = 0;
                while (kk <= 300 && kk <= bi && Tm(kk) >= b.t)
                {
                    if (Tm(kk) <= b.t1)
                    {
                        GLine wl = NewLine(Tm(kk), Hp(kk), Tm(kk), Lp(kk), col, DashStyleHelper.Solid, 1);
                        GLine bdl = NewLine(Tm(kk), Op(kk), Tm(kk), Cp(kk), col, DashStyleHelper.Solid, 5);
                        b.fl.Add(wl);
                        b.fl.Add(bdl);
                        grp.Add(wl);
                        grp.Add(bdl);
                    }
                    kk++;
                }
            }
            if (box || shd)
            {
                b.bx = NewBox(b.t, b.hi, curTime, b.lo);
                b.bx.Bd = shd ? Tr(CGray, 100) : col;
                b.bx.Sty = sty;
                b.bx.Bw = shd ? 0 : w;
                b.bx.Bg = Tr(shd ? CGray : col, shd ? 80 : 100);
                b.ml = NewLine(b.t, mid, curTime, mid, col, sty, w);
                grp.Add(b.bx);
                grp.Add(b.ml);
            }
            if (grp.Count > 0)
            {
                // Pine keeps at most 500 boxes / lines (oldest deleted): cap the drawings left behind
                blkGroups.Add(grp);
                if (blkGroups.Count > MaxBlkGroups)
                {
                    foreach (Gfx g in blkGroups[0]) Del(g);
                    blkGroups.RemoveAt(0);
                }
            }
        }

        // FVG shown as the box of a confirmed IFVG (close mode or swing mode), with its IFVG label
        private static bool SameF(Fvg f, Fvg g) => g != null && (g.dup ?? g) == f;

        private bool ConfIfvg(Fvg f) =>
            (bullconfirm.HasValue && SameF(f, bullifvg)) || (bearconfirm.HasValue && SameF(f, bearifvg))
            || (ifsconfirm.HasValue && SameF(f, swFvg));

        // breaker b drawn when FVG / IFVG f overlaps it on the pullback; ib: the IFVG box when f is the confirmed IFVG
        private void BrkTag(Fvg f, Blk b, GBox ib)
        {
            if (f != null && f.hi >= b.lo && f.lo <= b.hi)
            {
                f.brk = true;
                f.brkD = b.dir;
                if (ib != null)
                {
                    ib.Bold = true;
                    Touch(ib);
                }
                else
                {
                    // the label direction is kept for when the box shows the plain FVG label (also after it
                    // was an IFVG candidate); an unconfirmed IFVG candidate box shows it too
                    f.lblD = b.dir;
                    if (!f.end.HasValue && (!f.kept || (f.cand && !ConfIfvg(f))) && !(f.aplus && LabelAPlus) && f.rect != null)
                    {
                        f.rect.Txt = FvgLbl(f);
                        f.rect.Ts = ifvgTs;
                        f.rect.Ha = ifvgHa;
                        f.rect.Va = ifvgVa;
                        Touch(f.rect);
                    }
                }
                BlkDraw(b, BreakerFlagCandles, BreakerRectangle, BreakerShadow, cBrk, BreakerLineStyle, BreakerLineWidth);
            }
        }

        // text of the IFVG label (which: 1 bull, -1 bear, 2 swing IFVG) with the mitigation block arrow mbd
        private string MbTxt(int which, int mbd)
        {
            if (which == 1) return IfvgTxt(1, InversionRules ? bullInv : 1, DetectOte && gBull.flagged, gBull.tags, bullAp, mbd);
            if (which == -1) return IfvgTxt(-1, InversionRules ? bearInv : 1, DetectOte && gBear.flagged, gBear.tags, bearAp, mbd);
            return IfvgTxt(ifsdir, 1, DetectOte && gSw.flagged, gSw.tags, swAp, mbd);
        }

        private void MbTag(Fvg f, Blk b, GBox ib, int which)
        {
            if (f != null && f.hi >= b.lo && f.lo <= b.hi)
            {
                if (ib != null)
                {
                    if (f.mbD != b.dir)
                    {
                        f.mbD = b.dir;
                        ib.Txt = MbTxt(which, f.mbD);
                    }
                    ib.Bold = true;
                    Touch(ib);
                }
                else
                {
                    f.mbD = b.dir;
                    f.lblD = b.dir;
                    if (!f.end.HasValue && (!f.kept || (f.cand && !ConfIfvg(f))) && !(f.aplus && LabelAPlus) && f.rect != null)
                    {
                        f.rect.Txt = FvgLbl(f);
                        f.rect.Ts = ifvgTs;
                        f.rect.Ha = ifvgHa;
                        f.rect.Va = ifvgVa;
                        Touch(f.rect);
                    }
                }
                BlkDraw(b, MitigationFlagCandles, MitigationRectangle, MitigationShadow, cMb, MitigationLineStyle, MitigationLineWidth);
            }
        }

        // life cycle of breakers (isMb false) or mitigation blocks (isMb true) on a closed bar
        private void BlkUpdate(List<Blk> list, bool isMb, double pH, double pL, int len)
        {
            for (int i = list.Count - 1; i >= 0; i--)
            {
                Blk b = list[i];
                if (b.tagged)
                {
                    // drawn, pullback still going on: removed when it trades beyond the far side; once price closes
                    // out of the zone in the block direction the rectangle stays for reference
                    if ((b.dir == 1 && bl < b.lo) || (b.dir == -1 && bh > b.hi))
                    {
                        DelBlk(b);
                        list.RemoveAt(i);
                    }
                    else if ((b.dir == 1 && bc > b.hi) || (b.dir == -1 && bc < b.lo))
                        list.RemoveAt(i);
                }
                else if (!b.broken)
                {
                    if ((b.dir == 1 && bc > b.hi) || (b.dir == -1 && bc < b.lo))
                    {
                        b.broken = true;
                        b.bkBar = bi;
                    }
                }
                else if (!b.swung)
                {
                    // after the break: wait for a swing high above the zone (bullish) / low below it (bearish)
                    if (b.dir == 1 && !double.IsNaN(pH) && bi - len >= b.bkBar && pH > b.hi)
                        b.swung = true;
                    else if (b.dir == -1 && !double.IsNaN(pL) && bi - len >= b.bkBar && pL < b.lo)
                        b.swung = true;
                    else if ((b.dir == 1 && bc < b.lo) || (b.dir == -1 && bc > b.hi))
                        list.RemoveAt(i); // closed through the other side before the swing: invalidated
                }
                else if ((b.dir == 1 && bc < b.lo) || (b.dir == -1 && bc > b.hi))
                    list.RemoveAt(i); // closed through the other side: invalidated
                else if ((b.dir == 1 && bl <= b.hi) || (b.dir == -1 && bh >= b.lo))
                {
                    // pullback into the zone: tag the FVGs / IFVGs of the trade direction that overlap it
                    List<Fvg> fs0 = b.dir == 1 ? lfvgs : sfvgs;
                    foreach (Fvg f in fs0)
                    {
                        if (!f.end.HasValue)
                        {
                            if (isMb) MbTag(f, b, null, 0);
                            else BrkTag(f, b, null);
                        }
                    }
                    if (b.dir == 1)
                    {
                        if (!SwingMode && bullconfirm.HasValue)
                        {
                            if (isMb) MbTag(bullifvg, b, ifvgBox, 1);
                            else BrkTag(bullifvg, b, ifvgBox);
                        }
                        if (SwingMode && ifsdir == 1 && ifsconfirm.HasValue)
                        {
                            if (isMb) MbTag(swFvg, b, ifvgBox, 2);
                            else BrkTag(swFvg, b, ifvgBox);
                        }
                    }
                    else
                    {
                        if (!SwingMode && bearconfirm.HasValue)
                        {
                            if (isMb) MbTag(bearifvg, b, ifvgBoxb, -1);
                            else BrkTag(bearifvg, b, ifvgBoxb);
                        }
                        if (SwingMode && ifsdir == -1 && ifsconfirm.HasValue)
                        {
                            if (isMb) MbTag(swFvg, b, ifvgBox, 2);
                            else BrkTag(swFvg, b, ifvgBox);
                        }
                    }
                    if (b.tagged && ((b.dir == 1 && bl < b.lo) || (b.dir == -1 && bh > b.hi)))
                    {
                        DelBlk(b);
                        list.RemoveAt(i);
                    }
                }
            }
        }

        // a confirmed IFVG overlapping an FVG of its direction that a breaker / mitigation block tagged (live, or
        // mitigated after the IFVG's FVG started) gets the same tag: bold label, plus the arrow for a mitigation block
        private void IfvgInherit(Fvg iv, GBox ib, int which, int dir)
        {
            if (iv == null || ib == null) return;
            List<Fvg> fs = dir == 1 ? lfvgs : sfvgs;
            bool gb = false, gm = false;
            foreach (Fvg f in fs)
            {
                if ((!f.end.HasValue || f.end.Value >= iv.start) && f.hi >= iv.lo && f.lo <= iv.hi)
                {
                    if (f.brkD == dir && BreakersOn) gb = true;
                    if (f.mbD == dir && MitigationOn) gm = true;
                }
            }
            if (gb && !iv.brk)
            {
                iv.brk = true;
                ib.Bold = true;
                Touch(ib);
            }
            if (gm && iv.mbD != dir)
            {
                iv.mbD = dir;
                ib.Txt = MbTxt(which, dir);
                ib.Bold = true;
                Touch(ib);
            }
        }
        #endregion

        #region Per-bar pipeline (Pine source order)
        private void Step()
        {
            curTime = Tm(0);
            bo = Op(0);
            bh = Hp(0);
            bl = Lp(0);
            bc = Cp(0);

            // ---- per-bar series (ATR, Delivery Engine ranges and std dev), indexed by bar
            while (rangeL.Count < bi)
            {
                rangeL.Add(double.NaN);
                stdL.Add(double.NaN);
                atrL.Add(double.NaN);
            }
            double tr = bi == 0 ? bh - bl : Math.Max(bh - bl, Math.Max(Math.Abs(bh - Cp(1)), Math.Abs(bl - Cp(1))));
            int alen = Math.Max(1, AtrLength);
            double atrV;
            if (bi < alen - 1)
            {
                trSum += tr;
                atrV = double.NaN;
            }
            else if (bi == alen - 1)
            {
                trSum += tr;
                atrV = trSum / alen;
            }
            else
            {
                double pa = atrL[bi - 1];
                atrV = double.IsNaN(pa) ? double.NaN : (pa * (alen - 1) + tr) / alen;
            }
            atrL.Add(atrV);

            double rng = DisplacementRange == FioRangeType.OpenToClose ? Math.Abs(bo - bc) : bh - bl;
            rangeL.Add(rng);
            int slen = Math.Max(1, DispStdLength);
            double sd = double.NaN;
            if (bi >= slen - 1)
            {
                double sum = 0;
                for (int j = 0; j < slen; j++) sum += rangeL[bi - j];
                double mean = sum / slen;
                double v = 0;
                for (int j = 0; j < slen; j++)
                {
                    double dv = rangeL[bi - j] - mean;
                    v += dv * dv;
                }
                sd = Math.Sqrt(v / slen);
            }
            stdL.Add(sd);

            // ---- Wicks
            if (ShowWickCE && bi >= 1)
            {
                double o1 = Op(1), c1 = Cp(1), h1 = Hp(1), l1 = Lp(1);
                double bodyh = Math.Max(o1, c1);
                double bodyl = Math.Min(o1, c1);
                double wh = h1 - bodyh;
                double wl = bodyl - l1;
                Color wc = hideReg ? Tr(cWick, 100) : cWick;
                if (wh > WickMinSize)
                {
                    double ce = (h1 + bodyh) / 2.0;
                    wicks.Add(NewBarLine(bi - 1, ce, bi - 1 + Wdx(), ce, wc, WickStyle, 1));
                }
                if (wl > WickMinSize)
                {
                    double ce = (l1 + bodyl) / 2.0;
                    wicks.Add(NewBarLine(bi - 1, ce, bi - 1 + Wdx(), ce, wc, WickStyle, 1));
                }
                if (wicks.Count > WicksKept)
                {
                    Del(wicks[0]);
                    wicks.RemoveAt(0);
                }
                if (WickBounceAlerts)
                    foreach (GLine w in wicks)
                        if (Bounces(w.Y1)) Fire("bounce on wick C.E.");
            }

            // ---- [DE] Delivery Engine
            bool deDispOK = bi >= 2 && rangeL[bi - 2] > stdL[bi - 2] * DispStrength;
            bool deDispFvg = bi >= 2 && rangeL[bi - 1] > stdL[bi - 1] * DispStrength && (Cp(1) > Op(1) ? Hp(2) < bl : Lp(2) > bh);
            if (ColorDispCandles && deDispFvg && CurrentBars[0] >= off + 1) BarBrushes[off + 1] = dispBrush;
            double deStdMss = sd * MssDisplacement;
            if (rng > deStdMss && bc > bo) lastDispUpBar = bi;
            if (rng > deStdMss && bc < bo) lastDispDnBar = bi;
            bool deDispUp = lastDispUpBar.HasValue && bi - lastDispUpBar.Value <= MssCandles;
            bool deDispDn = lastDispDnBar.HasValue && bi - lastDispDnBar.Value <= MssCandles;

            // killzones (ICTlevels sessions) in the 1PFVG time zone; ignored above 15m
            DateTime openTz = ToZone(BarOpen(0), tzInfo);
            int mod = openTz.Hour * 60 + openTz.Minute;
            bool inKz = !tzMadrid
                ? (InSess(mod, 1200, 0) || InSess(mod, 120, 300) || InSess(mod, 420, 690) || InSess(mod, 810, 960))
                : (InSess(mod, 1380, 360) || InSess(mod, 480, 780) || InSess(mod, 810, 1080) || InSess(mod, 1140, 1320));
            deKzOK = !APlusKillzones || !isIntraday || tfsecs > 900 || inKz;

            // HTF structure (last completed HTF bar)
            deHtfUp = !deHtfUse || hsD == 1;
            deHtfDn = !deHtfUse || hsD == -1;

            // swing liquidity (BSL / SSL) not taken yet, and its sweeps
            double dePH = PivHi(b0, bi, LiquiditySwingLength, LiquiditySwingLength);
            double dePL = PivLo(b0, bi, LiquiditySwingLength, LiquiditySwingLength);
            for (int i = deBsl.Count - 1; i >= 0; i--)
            {
                if (bh > deBsl[i])
                {
                    deBsl.RemoveAt(i);
                    deLastBsl = bi;
                }
            }
            for (int i = deSsl.Count - 1; i >= 0; i--)
            {
                if (bl < deSsl[i])
                {
                    deSsl.RemoveAt(i);
                    deLastSsl = bi;
                }
            }
            deSslRecent = deLastSsl.HasValue && bi - deLastSsl.Value <= APlusWithin;
            deBslRecent = deLastBsl.HasValue && bi - deLastBsl.Value <= APlusWithin;

            // structure of the chart: MSS = break of the last swing against the structure with displacement,
            // or a break with displacement in the direction of a recent CHoCH
            double deMPH = PivHi(b0, bi, MssSwingLength, MssSwingLength);
            double deMPL = PivLo(b0, bi, MssSwingLength, MssSwingLength);
            if (!double.IsNaN(deShP) && bc > deShP)
            {
                bool isRev = deDir != 1;
                bool upg = !isRev && deChochUp.HasValue && bi - deChochUp.Value <= APlusWithin && deDispUp;
                bool isMss = (isRev || upg) && deDispUp;
                if (isMss) deChochUp = null;
                else if (isRev) deChochUp = bi;
                deChochDn = null;
                if (isMss && DeAp(1))
                {
                    deMssUpBar = bi;
                    // FVGs born after the sweep become A+
                    foreach (Fvg f in lfvgs)
                    {
                        if (deLastSsl.HasValue && f.sbar >= deLastSsl.Value && !f.aplus)
                        {
                            f.aplus = true;
                            if (f.rect != null && !f.kept && LabelAPlus && !hideReg) BText(f.rect, FvgLbl(f));
                        }
                    }
                }
                deDir = 1;
                deShP = double.NaN;
            }
            if (!double.IsNaN(deSlP) && bc < deSlP)
            {
                bool isRev = deDir != -1;
                bool upg = !isRev && deChochDn.HasValue && bi - deChochDn.Value <= APlusWithin && deDispDn;
                bool isMss = (isRev || upg) && deDispDn;
                if (isMss) deChochDn = null;
                else if (isRev) deChochDn = bi;
                deChochUp = null;
                if (isMss && DeAp(-1))
                {
                    deMssDnBar = bi;
                    foreach (Fvg f in sfvgs)
                    {
                        if (deLastBsl.HasValue && f.sbar >= deLastBsl.Value && !f.aplus)
                        {
                            f.aplus = true;
                            if (f.rect != null && !f.kept && LabelAPlus && !hideReg) BText(f.rect, FvgLbl(f));
                        }
                    }
                }
                deDir = -1;
                deSlP = double.NaN;
            }
            if (!double.IsNaN(deMPH)) deShP = deMPH;
            if (!double.IsNaN(deMPL)) deSlP = deMPL;
            if (!double.IsNaN(dePH))
            {
                deBsl.Add(dePH);
                if (deBsl.Count > 50) deBsl.RemoveAt(0);
            }
            if (!double.IsNaN(dePL))
            {
                deSsl.Add(dePL);
                if (deSsl.Count > 50) deSsl.RemoveAt(0);
            }

            // ---- 1PFVG hour
            if (openTz.Hour != fph)
            {
                hasfp = false;
                hasifp = false;
            }

            // ---- [OTEF] chart swings for the leg origin of new FVGs
            {
                double pl = PivLo(b0, bi, SwingPivotLeft, SwingPivotRight);
                double ph = PivHi(b0, bi, SwingPivotLeft, SwingPivotRight);
                if (!double.IsNaN(pl)) { otePL = pl; otePLb = bi - SwingPivotRight; }
                if (!double.IsNaN(ph)) { otePH = ph; otePHb = bi - SwingPivotRight; }
            }

            // ---- FVG detection (candles [1], [2], [3])
            Fvg lastlongfvg = null;
            Fvg lastshortfvg = null;
            if (bi >= 3)
            {
                double o1 = Op(1), c1 = Cp(1), h1 = Hp(1), l1 = Lp(1);
                double o2 = Op(2), c2 = Cp(2);
                double o3 = Op(3), c3 = Cp(3), h3 = Hp(3), l3 = Lp(3);
                bool fpNow = IsFPFVG();
                {
                    // long: low of [1] above the high of [3]; include suspension blocks
                    double hi = l1, lo = h3;
                    double fhi = hi, flo = lo;
                    double blow1 = Math.Min(o1, c1), bhi2 = Math.Max(o2, c2), blow2 = Math.Min(o2, c2), bhi3 = Math.Max(o3, c3);
                    if (bhi3 < blow2) lo = bhi3;
                    if (blow1 > bhi2) hi = blow1;
                    if (fhi - flo > MinFvgSize && fhi - flo < MaxFvgSize && (!DisplacementOnly || deDispOK || fpNow))
                    {
                        Fvg f = new Fvg();
                        f.hi = hi;
                        f.lo = lo;
                        f.ce = (hi + lo) / 2.0;
                        f.islong = true;
                        f.start = Tm(3);
                        f.sbar = bi - 3;
                        OteInit(f); // [OTEF]
                        f.aplus = APlusSetups && deMssUpBar.HasValue && bi - deMssUpBar.Value <= 3;
                        f.disp = deDispOK;
                        lastlongfvg = f;
                        Classify(f, fpNow);
                    }
                }
                {
                    // short: high of [1] below the low of [3]
                    double hi = l3, lo = h1;
                    double fhi = hi, flo = lo;
                    double blow3 = Math.Min(o3, c3), bhi2 = Math.Max(o2, c2), blow2 = Math.Min(o2, c2), bhi1 = Math.Max(o1, c1);
                    if (blow3 > bhi2) hi = blow3;
                    if (bhi1 < blow2) lo = bhi1;
                    if (fhi - flo > MinFvgSize && fhi - flo < MaxFvgSize && (!DisplacementOnly || deDispOK || fpNow))
                    {
                        Fvg f = new Fvg();
                        f.hi = hi;
                        f.lo = lo;
                        f.ce = (hi + lo) / 2.0;
                        f.islong = false;
                        f.start = Tm(3);
                        f.sbar = bi - 3;
                        OteInit(f); // [OTEF]
                        f.aplus = APlusSetups && deMssDnBar.HasValue && bi - deMssDnBar.Value <= 3;
                        f.disp = deDispOK;
                        lastshortfvg = f;
                        Classify(f, fpNow);
                    }
                }
            }

            // ---- FVG filter: liquidity sweeps (tested against the levels known BEFORE this bar)
            double fPH = PivHi(b0, bi, SweepPivotLeft, SweepPivotRight);
            double fPL = PivLo(b0, bi, SweepPivotLeft, SweepPivotRight);
            if (!fltOff)
            {
                if (!double.IsNaN(fsStH) && bh > fsStH)
                {
                    OnSweep(fsStLbar); // arm = from the last short-term low to the sweep
                    fsPost = 0;
                    fsStH = double.NaN;
                }
                if (!double.IsNaN(fsStL) && bl < fsStL)
                {
                    OnSweep(fsStHbar); // arm = from the last short-term high to the sweep
                    fsPost = 0;
                    fsStL = double.NaN;
                }
                if (!double.IsNaN(fPH))
                {
                    fsStH = fPH;
                    fsStHbar = bi - SweepPivotRight;
                }
                if (!double.IsNaN(fPL))
                {
                    fsStL = fPL;
                    fsStLbar = bi - SweepPivotRight;
                }
            }

            // ---- FVG update (boxes, lines, mitigation, bounce alerts)
            UpdFvgs(lfvgs);
            UpdFvgs(sfvgs);
            UpdFvgs(fpfvgs);

            // ---- [OTE] pivots: chart timeframe and the two higher timeframes of the ladder
            double oph = PivHi(b0, bi, SwingPivotLeft, SwingPivotRight);
            double opl = PivLo(b0, bi, SwingPivotLeft, SwingPivotRight);
            if (!double.IsNaN(oph)) AddHigh(pv0, bi - SwingPivotRight, oph);
            if (!double.IsNaN(opl)) AddLow(pv0, bi - SwingPivotRight, opl);
            if (ShowIFVGs && DetectOte)
            {
                if (hasH1) FeedHtf(pv1, pend1);
                if (hasH2) FeedHtf(pv2, pend2);
            }
            pend1.Clear();
            pend2.Clear();

            // highest / lowest of the 4 bars spanning the FVG (3 candles + current bar)
            double h4 = bh, l4 = bl;
            for (int k = 1; k <= 3 && k <= bi; k++)
            {
                h4 = Math.Max(h4, Hp(k));
                l4 = Math.Min(l4, Lp(k));
            }

            // ---- Close above / below IFVG setups (close mode candidates)
            if (ShowIFVGs && !SwingMode && lastlongfvg != null)
            {
                if (bearifvg != null) Release(bearifvg, bearconfirm.HasValue, bearconfirm);
                bearifvg = lastlongfvg;
                bearconfirm = null;
                bearInv = 0;
                bearAp = false;
                GStart(gBear, -1, bearifvg.lo, bearifvg.hi, h4, bearifvg.sbar);
                ifvgBoxb = Adopt(bearifvg, -1);
            }
            if (ShowIFVGs && !SwingMode && lastshortfvg != null)
            {
                if (bullifvg != null) Release(bullifvg, bullconfirm.HasValue, bullconfirm);
                bullifvg = lastshortfvg;
                bullconfirm = null;
                bullInv = 0;
                bullAp = false;
                GStart(gBull, 1, bullifvg.lo, bullifvg.hi, l4, bullifvg.sbar);
                ifvgBox = Adopt(bullifvg, 1);
            }
            BRight(ifvgBox, curTime);
            BRight(ifvgBoxb, curTime);

            // ---- Swing high / low IFVG setups
            double pivHi = double.NaN, pivLo = double.NaN;
            if (ShowIFVGs && SwingMode)
            {
                pivHi = oph;
                pivLo = opl;
            }
            int hiBar = bi - SwingPivotRight;
            DateTime hiTime = bi >= SwingPivotRight ? Tm(SwingPivotRight) : curTime;
            if (ShowIFVGs && SwingMode)
            {
                if (!double.IsNaN(pivHi))
                {
                    lastHi = pivHi;
                    lastHiBar = hiBar;
                    lastHiTime = hiTime;
                }
                if (!double.IsNaN(pivLo))
                {
                    lastLo = pivLo;
                    lastLoBar = hiBar;
                    lastLoTime = hiTime;
                }
            }
            bool newHi = !double.IsNaN(pivHi);
            bool newLo = !double.IsNaN(pivLo);
            double atrP = bi - SwingPivotRight >= 0 ? atrL[bi - SwingPivotRight] : double.NaN;

            if (newHi && ShowIFVGs && SwingMode)
            {
                double swingPrice = pivHi;
                double refLo = lastLo;
                bool sigOk = !SignificantSwing || double.IsNaN(refLo) || swingPrice - refLo >= atrP * MinSwingAtr;
                if (sigOk)
                {
                    swingTime = hiTime;
                    swingBar = hiBar;
                    if (SwingLabels && !hideReg) SwingLabel(hiTime, swingPrice, "SWING HIGH", true);
                    Fvg ifvg = FindFvg(hiTime, lfvgs, ref findRfL);
                    if (ifvg != null)
                    {
                        if (swFvg != null && swFvg.start != ifvg.start) Release(swFvg, ifsconfirm.HasValue, ifsconfirm);
                        swFvg = ifvg;
                        ifstime = ifvg.start;
                        ifshi = ifvg.hi;
                        ifslo = ifvg.lo;
                        ifsdir = -1;
                        ifsconfirm = null;
                        GStart(gSw, -1, ifvg.lo, ifvg.hi, swingPrice, ifvg.sbar); // leg origin = the swing high
                        ifvgBox = Adopt(ifvg, -1);
                    }
                }
            }
            if (newLo && ShowIFVGs && SwingMode)
            {
                double swingPrice = pivLo;
                double refHi = lastHi;
                bool sigOk = !SignificantSwing || double.IsNaN(refHi) || refHi - swingPrice >= atrP * MinSwingAtr;
                if (sigOk)
                {
                    swingTime = hiTime;
                    swingBar = hiBar;
                    if (SwingLabels && !hideReg) SwingLabel(hiTime, swingPrice, "SWING LOW", false);
                    Fvg ifvg = FindFvg(hiTime, sfvgs, ref findRfS);
                    if (ifvg != null)
                    {
                        if (swFvg != null && swFvg.start != ifvg.start) Release(swFvg, ifsconfirm.HasValue, ifsconfirm);
                        swFvg = ifvg;
                        ifstime = ifvg.start;
                        ifshi = ifvg.hi;
                        ifslo = ifvg.lo;
                        ifsdir = 1;
                        ifsconfirm = null;
                        GStart(gSw, 1, ifvg.lo, ifvg.hi, swingPrice, ifvg.sbar); // leg origin = the swing low
                        ifvgBox = Adopt(ifvg, 1);
                    }
                }
            }
            BRight(ifvgBox, curTime);

            // swing-mode confirmation / cancellation, evaluated on every closed bar
            if (ifstime.HasValue)
            {
                if (ifsdir == -1 && DnBreak(ifslo) && !ifsconfirm.HasValue)
                {
                    ifsconfirm = curTime;
                    GConfirm(gSw);
                    swAp = DeAp(-1);
                    BText(ifvgBox, IfvgTxt(-1, 1, false, "", swAp, MbOf(swFvg)));
                }
                else if (ifsdir == 1 && UpBreak(ifshi) && !ifsconfirm.HasValue)
                {
                    ifsconfirm = curTime;
                    GConfirm(gSw);
                    swAp = DeAp(1);
                    BText(ifvgBox, IfvgTxt(1, 1, false, "", swAp, MbOf(swFvg)));
                }
                if (ifsdir == -1 && ifsconfirm.HasValue && bc > ifshi)
                {
                    ifsconfirm = null;
                    GCancel(gSw);
                    BText(ifvgBox, "");
                }
                else if (ifsdir == 1 && ifsconfirm.HasValue && bc < ifslo)
                {
                    ifsconfirm = null;
                    GCancel(gSw);
                    BText(ifvgBox, "");
                }
            }

            // ---- IFVG signals
            int sig = 0;
            if (ShowIFVGs && SwingMode)
            {
                if (ifsconfirm.HasValue && curTime != ifsconfirm.Value && prevSig == 0)
                {
                    if (ifsdir == -1 && bh >= ifslo && bh < ifshi) sig = -1;
                    else if (ifsdir == 1 && bl <= ifshi && bl >= ifslo) sig = 1;
                }
            }

            if (ShowIFVGs && !SwingMode && !InversionRules)
            {
                if (bullifvg != null && bullconfirm.HasValue && bc < bullifvg.lo)
                {
                    bullconfirm = null;
                    GCancel(gBull);
                    BText(ifvgBox, "");
                }
                if (bullifvg != null && UpBreak(bullifvg.hi) && !bullconfirm.HasValue)
                {
                    bullconfirm = curTime;
                    GConfirm(gBull);
                    bullAp = DeAp(1);
                    BText(ifvgBox, IfvgTxt(1, 1, false, "", bullAp, MbOf(bullifvg)));
                    sig = 1;
                }
                if (bearifvg != null && bearconfirm.HasValue && bc > bearifvg.hi)
                {
                    bearconfirm = null;
                    GCancel(gBear);
                    BText(ifvgBoxb, "");
                }
                if (bearifvg != null && DnBreak(bearifvg.lo) && !bearconfirm.HasValue)
                {
                    bearAp = DeAp(-1);
                    BText(ifvgBoxb, IfvgTxt(-1, 1, false, "", bearAp, MbOf(bearifvg)));
                    bearconfirm = curTime;
                    GConfirm(gBear);
                    sig = -1;
                }
            }

            // [DE] inversion rules: every close through the zone inverts it again, up to the max; then it is invalidated.
            // Bull candidate (bearish FVG): inversions 1, 3, ... are bullish (close above), 2, 4, ... bearish (close below).
            if (ShowIFVGs && !SwingMode && InversionRules)
            {
                if (bullifvg != null && bullInv >= 0)
                {
                    bool brk = bullInv % 2 == 0 ? UpBreak(bullifvg.hi) : DnBreak(bullifvg.lo);
                    if (brk)
                    {
                        if (bullInv >= MaxInversions)
                        {
                            bullInv = -1;
                            bullconfirm = null;
                            GCancel(gBull);
                            DeKill(bullifvg);
                            ifvgBox = null;
                        }
                        else
                        {
                            bullInv++;
                            int d = bullInv % 2 == 1 ? 1 : -1;
                            if (bullInv == 1)
                            {
                                bullconfirm = curTime;
                                GConfirm(gBull);
                            }
                            else
                            {
                                bullconfirm = null;
                                GCancel(gBull); // only the first inversion
                                DeRecolor(ifvgBox, d);
                            }
                            bullAp = DeAp(d);
                            BText(ifvgBox, IfvgTxt(d, bullInv, false, "", bullAp, MbOf(bullifvg)));
                            sig = d;
                        }
                    }
                }
                if (bearifvg != null && bearInv >= 0)
                {
                    bool brk = bearInv % 2 == 0 ? DnBreak(bearifvg.lo) : UpBreak(bearifvg.hi);
                    if (brk)
                    {
                        if (bearInv >= MaxInversions)
                        {
                            bearInv = -1;
                            bearconfirm = null;
                            GCancel(gBear);
                            DeKill(bearifvg);
                            ifvgBoxb = null;
                        }
                        else
                        {
                            bearInv++;
                            int d = bearInv % 2 == 1 ? -1 : 1;
                            if (bearInv == 1)
                            {
                                bearconfirm = curTime;
                                GConfirm(gBear);
                            }
                            else
                            {
                                bearconfirm = null;
                                GCancel(gBear); // only the first inversion
                                DeRecolor(ifvgBoxb, d);
                            }
                            bearAp = DeAp(d);
                            BText(ifvgBoxb, IfvgTxt(d, bearInv, false, "", bearAp, MbOf(bearifvg)));
                            sig = sig == 0 ? d : sig;
                        }
                    }
                }
            }

            UpdKept();

            // ---- [BRK] Breakers
            double bPH = PivHi(b0, bi, BreakerSwingLength, BreakerSwingLength);
            double bPL = PivLo(b0, bi, BreakerSwingLength, BreakerSwingLength);
            if (BreakersOn)
            {
                bool whole = BreakerZone == FioZoneMode.WholeSequence;
                if (!double.IsNaN(bPL))
                {
                    bPrevL = bLastL;
                    bPrevLb = bLastLb;
                    bLastL = bPL;
                    bLastLb = bi - BreakerSwingLength;
                    // bullish: low, high, lower low (the high between the two lows)
                    if (!double.IsNaN(bPrevL) && bLastL < bPrevL && bLastHb.HasValue && bPrevLb.HasValue
                        && bLastHb.Value > bPrevLb.Value && bLastHb.Value < bLastLb.Value)
                    {
                        double zh, zl;
                        DateTime zt, zt1;
                        if (BlkZone(1, bi - bLastHb.Value, bPrevLb.Value, whole, out zh, out zl, out zt, out zt1))
                            BlkAdd(brks, 1, zh, zl, zt, zt1, BreakersKept);
                    }
                }
                if (!double.IsNaN(bPH))
                {
                    bPrevH = bLastH;
                    bPrevHb = bLastHb;
                    bLastH = bPH;
                    bLastHb = bi - BreakerSwingLength;
                    // bearish: high, low, higher high (the low between the two highs)
                    if (!double.IsNaN(bPrevH) && bLastH > bPrevH && bLastLb.HasValue && bPrevHb.HasValue
                        && bLastLb.Value > bPrevHb.Value && bLastLb.Value < bLastHb.Value)
                    {
                        double zh, zl;
                        DateTime zt, zt1;
                        if (BlkZone(-1, bi - bLastLb.Value, bPrevHb.Value, whole, out zh, out zl, out zt, out zt1))
                            BlkAdd(brks, -1, zh, zl, zt, zt1, BreakersKept);
                    }
                }
                BlkUpdate(brks, false, bPH, bPL, BreakerSwingLength);
            }

            // ---- [MB] Mitigation blocks
            double mPH = PivHi(b0, bi, MitigationSwingLength, MitigationSwingLength);
            double mPL = PivLo(b0, bi, MitigationSwingLength, MitigationSwingLength);
            if (MitigationOn)
            {
                bool whole = MitigationZone == FioZoneMode.WholeSequence;
                if (!double.IsNaN(mPL))
                {
                    mPrevL = mLastL;
                    mPrevLb = mLastLb;
                    mLastL = mPL;
                    mLastLb = bi - MitigationSwingLength;
                    // bullish: low, high, higher low (the high between the two lows)
                    if (!double.IsNaN(mPrevL) && mLastL > mPrevL && mLastHb.HasValue && mPrevLb.HasValue
                        && mLastHb.Value > mPrevLb.Value && mLastHb.Value < mLastLb.Value)
                    {
                        double zh, zl;
                        DateTime zt, zt1;
                        if (BlkZone(1, bi - mLastHb.Value, mPrevLb.Value, whole, out zh, out zl, out zt, out zt1))
                            BlkAdd(mbs, 1, zh, zl, zt, zt1, MitigationKept);
                    }
                }
                if (!double.IsNaN(mPH))
                {
                    mPrevH = mLastH;
                    mPrevHb = mLastHb;
                    mLastH = mPH;
                    mLastHb = bi - MitigationSwingLength;
                    // bearish: high, low, lower high (the low between the two highs)
                    if (!double.IsNaN(mPrevH) && mLastH < mPrevH && mLastLb.HasValue && mPrevHb.HasValue
                        && mLastLb.Value > mPrevHb.Value && mLastLb.Value < mLastHb.Value)
                    {
                        double zh, zl;
                        DateTime zt, zt1;
                        if (BlkZone(-1, bi - mLastLb.Value, mPrevHb.Value, whole, out zh, out zl, out zt, out zt1))
                            BlkAdd(mbs, -1, zh, zl, zt, zt1, MitigationKept);
                    }
                }
                BlkUpdate(mbs, true, mPH, mPL, MitigationSwingLength);
            }

            // ---- [MB] tags inherited by confirmed IFVGs
            if (BreakersOn || MitigationOn)
            {
                if (!SwingMode)
                {
                    if (bullconfirm.HasValue) IfvgInherit(bullifvg, ifvgBox, 1, 1);
                    if (bearconfirm.HasValue) IfvgInherit(bearifvg, ifvgBoxb, -1, -1);
                }
                else if (ifsconfirm.HasValue)
                    IfvgInherit(swFvg, ifvgBox, 2, ifsdir);
            }

            // ---- [OTE] signals: IFVG retraced into and matches the OTE band
            int bearOteSig = GStep(gBear);
            int bullOteSig = GStep(gBull);
            int swOteSig = GStep(gSw);
            int oteSig = 0;
            if (bearOteSig != 0) oteSig = -1;
            else if (bullOteSig != 0) oteSig = 1;
            else if (swOteSig != 0) oteSig = swOteSig;

            if (ShowIFVGs && DetectOte)
            {
                if (bearOteSig != 0 && ifvgBoxb != null)
                {
                    ifvgBoxb.Txt = IfvgTxt(-1, 1, true, gBear.tags, bearAp, MbOf(bearifvg));
                    ifvgBoxb.Bd = cOteHi;
                    Touch(ifvgBoxb);
                }
                if (bullOteSig != 0 && ifvgBox != null)
                {
                    ifvgBox.Txt = IfvgTxt(1, 1, true, gBull.tags, bullAp, MbOf(bullifvg));
                    ifvgBox.Bd = cOteHi;
                    Touch(ifvgBox);
                }
                if (swOteSig != 0 && ifvgBox != null)
                {
                    ifvgBox.Txt = IfvgTxt(swOteSig, 1, true, gSw.tags, swAp, MbOf(swFvg));
                    ifvgBox.Bd = cOteHi;
                    Touch(ifvgBox);
                }
            }

            // ---- markers (Pine plotshape)
            double pad = TickSize * 2;
            if (ShowSignals && !hideReg && sig == -1 && prevSig == 0)
                NewMark(2, curTime, bh + pad, cBearIf, "SHORT", cBearIf, 8, 14);
            if (ShowSignals && !hideReg && sig == 1 && prevSig == 0)
                NewMark(1, curTime, bl - pad, cBullIf, "LONG", cBullIf, 8, -14);
            if (OteSignals && !hideReg && oteSig == -1)
                NewMark(3, curTime, bh + pad * 4, cOteHi, "OTE", cOteHi, 8, 12);
            if (OteSignals && !hideReg && oteSig == 1)
                NewMark(3, curTime, bl - pad * 4, cOteHi, "OTE", cOteHi, 8, -12);

            // ---- alerts
            if (IfvgAlerts && sig != 0 && !(DetectOte && AlertOnlyOte))
                Fire(sig > 0 ? "LONG IFVG " + tfStr : "SHORT IFVG " + tfStr);
            if (IfvgAlerts && DetectOte && oteSig != 0)
                Fire(oteSig > 0 ? "LONG IFVG+OTE " + tfStr : "SHORT IFVG+OTE " + tfStr);

            prevSig = sig;
        }

        private void SwingLabel(DateTime t, double price, string txt, bool high)
        {
            GMark m = NewMark(0, t, price, CBlue, txt, CWhite, 8, high ? 12 : -12);
            swingLbls.Add(m);
            if (swingLbls.Count > MaxSwingLabels)
            {
                Del(swingLbls[0]);
                swingLbls.RemoveAt(0);
            }
        }
        #endregion

        #region Properties
        [NinjaScriptProperty]
        [Display(Name = "1PFVGs", GroupName = "Enable / Draw / Filter", Order = 101, Description = "Track and draw the 1st presented FVGs.")]
        public bool Show1PFVGs { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "IFVGs", GroupName = "Enable / Draw / Filter", Order = 102, Description = "Detect, draw and alert interesting IFVGs.")]
        public bool ShowIFVGs { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Wick C.E.", GroupName = "Enable / Draw / Filter", Order = 103, Description = "Draw the C.E. of candle wicks.")]
        public bool ShowWickCE { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Draw", GroupName = "Enable / Draw / Filter", Order = 104, Description = "All: everything. OnlyPFVGs: only 1PFVGs, no other FVG. IFVGs: only IFVG boxes and OTE bands. PFVGsAndIFVGs: both. The IFVGs modes hide regular FVGs, wick C.E., swing labels and signals (alerts keep working).")]
        public FioDrawMode DrawSelection { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "FVG filter", GroupName = "Enable / Draw / Filter", Order = 105, Description = "Which FVGs are drawn (1PFVGs and the close-mode IFVG logic are not filtered). Sweeps: the last FVG before a sweep and the first one after it. SweepsAndMid: also the largest FVG near the middle of the arm.")]
        public FioFilterMode FvgFilter { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Historic compute", GroupName = "FVG Settings", Order = 201, Description = "Kept for parity with the Pine input. NinjaTrader has no visible-range information in OnBarUpdate, so FVGs are always computed on every bar.")]
        public bool HistoricCompute { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "FVG bounce alerts", GroupName = "FVG Settings", Order = 202, Description = "Alert when price touches the high, low or C.E. of an open FVG and closes away from it.")]
        public bool FvgBounceAlerts { get; set; }

        [NinjaScriptProperty]
        [Range(1, 1000)]
        [Display(Name = "Size: min", GroupName = "FVG Settings", Order = 203, Description = "Minimum FVG size (points) to be valid.")]
        public double MinFvgSize { get; set; }

        [NinjaScriptProperty]
        [Range(100, double.MaxValue)]
        [Display(Name = "Size: max", GroupName = "FVG Settings", Order = 204, Description = "Ignore FVGs larger than this (buggy data with huge gaps).")]
        public double MaxFvgSize { get; set; }

        [NinjaScriptProperty]
        [Range(2, 500)]
        [Display(Name = "Size: keep", GroupName = "FVG Settings", Order = 205, Description = "Number of FVGs to keep (per bias).")]
        public int FvgsKept { get; set; }

        [NinjaScriptProperty]
        [Range(0, 50)]
        [Display(Name = "Merge tol. %", GroupName = "FVG Settings", Order = 206, Description = "A new FVG whose high and low are within this % of its height of an older drawn FVG is merged into it. 0 = off.")]
        public double MergeTolerance { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "Sweep pivots L", GroupName = "FVG Settings", Order = 207, Description = "Left pivot length of the short-term highs / lows used by the FVG filter.")]
        public int SweepPivotLeft { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "Sweep pivots R", GroupName = "FVG Settings", Order = 208, Description = "Right pivot length of the short-term highs / lows used by the FVG filter.")]
        public int SweepPivotRight { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "FVG style", GroupName = "FVG Settings", Order = 209, Description = "Box, Box with no fill (border only), or Segments (hi / lo / C.E. lines).")]
        public FioFvgStyle FvgStyle { get; set; }

        [NinjaScriptProperty]
        [Range(10, 1000)]
        [Display(Name = "C.E.: min height", GroupName = "FVG Settings", Order = 210, Description = "Min FVG height to draw the C.E.")]
        public int CeMinHeight { get; set; }

        [NinjaScriptProperty]
        [Range(1, 3)]
        [Display(Name = "C.E.: width", GroupName = "FVG Settings", Order = 211)]
        public int CeWidth { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "C.E.: style", GroupName = "FVG Settings", Order = 212)]
        public DashStyleHelper CeStyle { get; set; }

        [NinjaScriptProperty]
        [XmlIgnore]
        [Display(Name = "Long FVG color", GroupName = "FVG Settings", Order = 213)]
        public Brush LongFvgColor { get; set; }

        [Browsable(false)]
        public string LongFvgColorSerializable
        {
            get { return Serialize.BrushToString(LongFvgColor); }
            set { LongFvgColor = Serialize.StringToBrush(value); }
        }

        [NinjaScriptProperty]
        [XmlIgnore]
        [Display(Name = "Long FVG border", GroupName = "FVG Settings", Order = 214)]
        public Brush LongFvgBorder { get; set; }

        [Browsable(false)]
        public string LongFvgBorderSerializable
        {
            get { return Serialize.BrushToString(LongFvgBorder); }
            set { LongFvgBorder = Serialize.StringToBrush(value); }
        }

        [NinjaScriptProperty]
        [XmlIgnore]
        [Display(Name = "Short FVG color", GroupName = "FVG Settings", Order = 215)]
        public Brush ShortFvgColor { get; set; }

        [Browsable(false)]
        public string ShortFvgColorSerializable
        {
            get { return Serialize.BrushToString(ShortFvgColor); }
            set { ShortFvgColor = Serialize.StringToBrush(value); }
        }

        [NinjaScriptProperty]
        [XmlIgnore]
        [Display(Name = "Short FVG border", GroupName = "FVG Settings", Order = 216)]
        public Brush ShortFvgBorder { get; set; }

        [Browsable(false)]
        public string ShortFvgBorderSerializable
        {
            get { return Serialize.BrushToString(ShortFvgBorder); }
            set { ShortFvgBorder = Serialize.StringToBrush(value); }
        }

        [NinjaScriptProperty]
        [Display(Name = "Time", GroupName = "1st Presented FVGs", Order = 301, Description = "Session open as HHMM. Ignored for DAX symbols with TZ Auto (0900 is used).")]
        public string FpTime { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "TZ", GroupName = "1st Presented FVGs", Order = 302, Description = "Auto: 9:00 Madrid for DAX, 9:30 New York otherwise. Also accepts America/New_York, Europe/Madrid (and a few other IANA names) or any Windows time zone id.")]
        public string FpTimeZone { get; set; }

        [NinjaScriptProperty]
        [Range(1, 1000)]
        [Display(Name = "Keep", GroupName = "1st Presented FVGs", Order = 303, Description = "Number of 1st presented FVGs kept.")]
        public int FpKeep { get; set; }

        [NinjaScriptProperty]
        [XmlIgnore]
        [Display(Name = "Day 1PFVG", GroupName = "1st Presented FVGs", Order = 304)]
        public Brush FpDayColor { get; set; }

        [Browsable(false)]
        public string FpDayColorSerializable
        {
            get { return Serialize.BrushToString(FpDayColor); }
            set { FpDayColor = Serialize.StringToBrush(value); }
        }

        [NinjaScriptProperty]
        [XmlIgnore]
        [Display(Name = "Day 1PFVG border", GroupName = "1st Presented FVGs", Order = 305)]
        public Brush FpDayBorder { get; set; }

        [Browsable(false)]
        public string FpDayBorderSerializable
        {
            get { return Serialize.BrushToString(FpDayBorder); }
            set { FpDayBorder = Serialize.StringToBrush(value); }
        }

        [NinjaScriptProperty]
        [XmlIgnore]
        [Display(Name = "Week 1PFVG", GroupName = "1st Presented FVGs", Order = 306)]
        public Brush FpWeekColor { get; set; }

        [Browsable(false)]
        public string FpWeekColorSerializable
        {
            get { return Serialize.BrushToString(FpWeekColor); }
            set { FpWeekColor = Serialize.StringToBrush(value); }
        }

        [NinjaScriptProperty]
        [XmlIgnore]
        [Display(Name = "Week 1PFVG border", GroupName = "1st Presented FVGs", Order = 307)]
        public Brush FpWeekBorder { get; set; }

        [Browsable(false)]
        public string FpWeekBorderSerializable
        {
            get { return Serialize.BrushToString(FpWeekBorder); }
            set { FpWeekBorder = Serialize.StringToBrush(value); }
        }

        [NinjaScriptProperty]
        [Display(Name = "From 1m candles", GroupName = "1st Presented FVGs", Order = 310, Description = "On charts above 1m and below 15m, the 1st presented FVG is found on the 1m candles instead of the chart candles. Off: only on 1m to 3m charts, from the chart candles.")]
        public bool FpFrom1m { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "1PFVG inverse", GroupName = "1st Presented FVGs", Order = 308, Description = "Also draw the first FVG of the opposite bias after the 1PFVG.")]
        public bool FpInverse { get; set; }

        [NinjaScriptProperty]
        [XmlIgnore]
        [Display(Name = "1PFVG inverse color", GroupName = "1st Presented FVGs", Order = 309)]
        public Brush FpInverseColor { get; set; }

        [Browsable(false)]
        public string FpInverseColorSerializable
        {
            get { return Serialize.BrushToString(FpInverseColor); }
            set { FpInverseColor = Serialize.StringToBrush(value); }
        }

        [NinjaScriptProperty]
        [XmlIgnore]
        [Display(Name = "1PFVG inverse border", GroupName = "1st Presented FVGs", Order = 310)]
        public Brush FpInverseBorder { get; set; }

        [Browsable(false)]
        public string FpInverseBorderSerializable
        {
            get { return Serialize.BrushToString(FpInverseBorder); }
            set { FpInverseBorder = Serialize.StringToBrush(value); }
        }

        [NinjaScriptProperty]
        [Range(1, 1000)]
        [Display(Name = "Keep", GroupName = "Wick Settings", Order = 401, Description = "Wicks kept drawn.")]
        public int WicksKept { get; set; }

        [NinjaScriptProperty]
        [Range(0, double.MaxValue)]
        [Display(Name = "Min size", GroupName = "Wick Settings", Order = 402, Description = "Min wick size to draw its C.E.")]
        public double WickMinSize { get; set; }

        [NinjaScriptProperty]
        [Range(0, 500)]
        [Display(Name = "Segment width", GroupName = "Wick Settings", Order = 403, Description = "0 = auto, or number of candles.")]
        public int WickSegmentWidth { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "C.E. line style", GroupName = "Wick Settings", Order = 404)]
        public DashStyleHelper WickStyle { get; set; }

        [NinjaScriptProperty]
        [XmlIgnore]
        [Display(Name = "C.E. line color", GroupName = "Wick Settings", Order = 405)]
        public Brush WickColor { get; set; }

        [Browsable(false)]
        public string WickColorSerializable
        {
            get { return Serialize.BrushToString(WickColor); }
            set { WickColor = Serialize.StringToBrush(value); }
        }

        [NinjaScriptProperty]
        [Display(Name = "Alerts on bounces", GroupName = "Wick Settings", Order = 406, Description = "Alert on wick C.E. bounces.")]
        public bool WickBounceAlerts { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Alerts on IFVGs", GroupName = "IFVG Settings", Order = 501)]
        public bool IfvgAlerts { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Show signals", GroupName = "IFVG Settings", Order = 502, Description = "Debug: show where alarms are triggered.")]
        public bool ShowSignals { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Labels", GroupName = "IFVG Settings", Order = 503, Description = "Show the text labels (IFVG+ / IFVG- / OTE) on the IFVG boxes.")]
        public bool IfvgLabels { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Label parts: OTE TF", GroupName = "IFVG Settings", Order = 504, Description = "Show the timeframes of the OTE match (e.g. 5m+15m) in the IFVG labels.")]
        public bool LabelOteTf { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Label parts: + / -", GroupName = "IFVG Settings", Order = 505, Description = "Show the + / - of the IFVG (IFVG+ / IFVG-).")]
        public bool LabelSign { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Label parts: A+", GroupName = "IFVG Settings", Order = 506, Description = "Show the A+ tag of the Delivery Engine setups.")]
        public bool LabelAPlus { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Keep confirmed IFVGs", GroupName = "IFVG Settings", Order = 507, Description = "Confirmed IFVGs not yet invalidated stay drawn. When invalidated (close back through it) or beyond max, the FVG box is restored as it was.")]
        public bool KeepIfvgs { get; set; }

        [NinjaScriptProperty]
        [Range(1, 100)]
        [Display(Name = "Keep confirmed IFVGs, max", GroupName = "IFVG Settings", Order = 508)]
        public int KeepIfvgsMax { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "IFVG confirmation", GroupName = "IFVG Settings", Order = 509, Description = "Close: the close is beyond the FVG. FullBody: open and close are both beyond it.")]
        public FioConfirm IfvgConfirmation { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Inversion rules", GroupName = "IFVG Settings", Order = 510, Description = "Close mode only. Each close through the zone inverts it again (IFVG, IFVG x2 in the other direction, ...) up to the max; the next close through invalidates it. Only the first inversion is tracked for OTE.")]
        public bool InversionRules { get; set; }

        [NinjaScriptProperty]
        [Range(1, 10)]
        [Display(Name = "Max inversions", GroupName = "IFVG Settings", Order = 511)]
        public int MaxInversions { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Invalidated IFVG", GroupName = "IFVG Settings", Order = 512)]
        public FioDeadMode InvalidatedIfvg { get; set; }

        [NinjaScriptProperty]
        [XmlIgnore]
        [Display(Name = "Invalidated IFVG color", GroupName = "IFVG Settings", Order = 513)]
        public Brush InvalidatedColor { get; set; }

        [Browsable(false)]
        public string InvalidatedColorSerializable
        {
            get { return Serialize.BrushToString(InvalidatedColor); }
            set { InvalidatedColor = Serialize.StringToBrush(value); }
        }

        [NinjaScriptProperty]
        [Display(Name = "Swings (else closes)", GroupName = "IFVG Settings", Order = 514, Description = "On: keep a single IFVG under watch, located using swings. Off: keep the last bull / bear FVG pair and confirm them when a close goes through.")]
        public bool SwingMode { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Significant swing", GroupName = "IFVG Settings", Order = 515)]
        public bool SignificantSwing { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Swing labels", GroupName = "IFVG Settings", Order = 516, Description = "Only with swings.")]
        public bool SwingLabels { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "Swing pivot left", GroupName = "IFVG Settings", Order = 517, Description = "Swing pivot lengths. Also used to find the leg before the FVG for the OTE prior-leg rejection.")]
        public int SwingPivotLeft { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "Swing pivot right", GroupName = "IFVG Settings", Order = 518)]
        public int SwingPivotRight { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "ATR length", GroupName = "IFVG Settings", Order = 519)]
        public int AtrLength { get; set; }

        [NinjaScriptProperty]
        [Range(0, double.MaxValue)]
        [Display(Name = "Min swing (ATR)", GroupName = "IFVG Settings", Order = 520, Description = "Minimum swing displacement in ATRs.")]
        public double MinSwingAtr { get; set; }

        [NinjaScriptProperty]
        [XmlIgnore]
        [Display(Name = "Bullish IFVG color", GroupName = "IFVG Settings", Order = 521)]
        public Brush BullIfvgColor { get; set; }

        [Browsable(false)]
        public string BullIfvgColorSerializable
        {
            get { return Serialize.BrushToString(BullIfvgColor); }
            set { BullIfvgColor = Serialize.StringToBrush(value); }
        }

        [NinjaScriptProperty]
        [XmlIgnore]
        [Display(Name = "Bearish IFVG color", GroupName = "IFVG Settings", Order = 522)]
        public Brush BearIfvgColor { get; set; }

        [Browsable(false)]
        public string BearIfvgColorSerializable
        {
            get { return Serialize.BrushToString(BearIfvgColor); }
            set { BearIfvgColor = Serialize.StringToBrush(value); }
        }

        [NinjaScriptProperty]
        [Range(0, 100)]
        [Display(Name = "Transparency: fill", GroupName = "IFVG Settings", Order = 523)]
        public int IfvgFillTransparency { get; set; }

        [NinjaScriptProperty]
        [Range(0, 100)]
        [Display(Name = "Transparency: border", GroupName = "IFVG Settings", Order = 524)]
        public int IfvgBorderTransparency { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Text size", GroupName = "IFVG Settings", Order = 525)]
        public FioTextSize IfvgTextSize { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Text align H", GroupName = "IFVG Settings", Order = 526)]
        public FioHAlign IfvgTextHAlign { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Text align V", GroupName = "IFVG Settings", Order = 527)]
        public FioVAlign IfvgTextVAlign { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Detect OTE IFVGs", GroupName = "IFVG + OTE (ICT Optimal Trade Entry)", Order = 601, Description = "Needs IFVGs. Flags a confirmed IFVG as an OTE IFVG when it matches the OTE band.")]
        public bool DetectOte { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Flag OTE FVGs", GroupName = "IFVG + OTE (ICT Optimal Trade Entry)", Order = 600, Description = "Plain FVGs too: the leg runs from the last swing low (bullish FVG) / high (bearish FVG) before the FVG to the highest high / lowest low since. When price retraces into the FVG while it matches the OTE band of that leg (retracement levels and match below), the FVG is labeled OTE. Chart timeframe swings (Swing pivot left / right); box styles only.")]
        public bool FlagOteFvgs { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Alert only OTE", GroupName = "IFVG + OTE (ICT Optimal Trade Entry)", Order = 602, Description = "Suppress the regular IFVG alerts; only OTE IFVG alerts fire.")]
        public bool AlertOnlyOte { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Signals", GroupName = "IFVG + OTE (ICT Optimal Trade Entry)", Order = 603, Description = "Show OTE signal markers.")]
        public bool OteSignals { get; set; }

        [NinjaScriptProperty]
        [Range(0.0, 1.0)]
        [Display(Name = "OTE retracement: lower", GroupName = "IFVG + OTE (ICT Optimal Trade Entry)", Order = 604)]
        public double OteLower { get; set; }

        [NinjaScriptProperty]
        [Range(0.0, 1.0)]
        [Display(Name = "OTE retracement: upper", GroupName = "IFVG + OTE (ICT Optimal Trade Entry)", Order = 605)]
        public double OteUpper { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "OTE match", GroupName = "IFVG + OTE (ICT Optimal Trade Entry)", Order = 606, Description = "Overlap: any overlap of the IFVG with the OTE band. FullyInside: whole IFVG inside it. CEInside: the IFVG midpoint inside it.")]
        public FioOteMode OteMatch { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Prior-leg rejection", GroupName = "IFVG + OTE (ICT Optimal Trade Entry)", Order = 607, Description = "If the candle that closes through the FVG wicks into the OTE band of the leg before the FVG and closes back away from it, the IFVG is flagged at confirmation.")]
        public bool PriorLegRejection { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Rejection: any candle", GroupName = "IFVG + OTE (ICT Optimal Trade Entry)", Order = 608, Description = "Also count a wick from any candle between the FVG and the confirming candle.")]
        public bool RejectionAnyCandle { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Rejection: cap wick", GroupName = "IFVG + OTE (ICT Optimal Trade Entry)", Order = 609, Description = "The wick must not go beyond the upper OTE level.")]
        public bool RejectionCapWick { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Next higher TF", GroupName = "IFVG + OTE (ICT Optimal Trade Entry)", Order = 610, Description = "Ladder: 3m (or lower) > 5m > 15m > 1h > 4h > D. The chart sits on the highest rung <= its timeframe; the next two are used.")]
        public bool NextHigherTf { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "2nd higher TF", GroupName = "IFVG + OTE (ICT Optimal Trade Entry)", Order = 611)]
        public bool SecondHigherTf { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "HTF pivot left", GroupName = "IFVG + OTE (ICT Optimal Trade Entry)", Order = 612, Description = "Pivot lengths (in HTF bars) for higher-timeframe swings.")]
        public int HtfPivotLeft { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "HTF pivot right", GroupName = "IFVG + OTE (ICT Optimal Trade Entry)", Order = 613)]
        public int HtfPivotRight { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Band: draw", GroupName = "IFVG + OTE (ICT Optimal Trade Entry)", Order = 614, Description = "OTE bands are drawn right of the last candle, starting 'start' candles after it and 'width' candles wide.")]
        public bool DrawOteBands { get; set; }

        [NinjaScriptProperty]
        [Range(0, 200)]
        [Display(Name = "Band: start", GroupName = "IFVG + OTE (ICT Optimal Trade Entry)", Order = 615)]
        public int OteBandStart { get; set; }

        [NinjaScriptProperty]
        [Range(1, 100)]
        [Display(Name = "Band: width", GroupName = "IFVG + OTE (ICT Optimal Trade Entry)", Order = 616)]
        public int OteBandWidth { get; set; }

        [NinjaScriptProperty]
        [XmlIgnore]
        [Display(Name = "Colors: band", GroupName = "IFVG + OTE (ICT Optimal Trade Entry)", Order = 617)]
        public Brush OteBandColor { get; set; }

        [Browsable(false)]
        public string OteBandColorSerializable
        {
            get { return Serialize.BrushToString(OteBandColor); }
            set { OteBandColor = Serialize.StringToBrush(value); }
        }

        [NinjaScriptProperty]
        [XmlIgnore]
        [Display(Name = "Colors: border", GroupName = "IFVG + OTE (ICT Optimal Trade Entry)", Order = 618)]
        public Brush OteBorderColor { get; set; }

        [Browsable(false)]
        public string OteBorderColorSerializable
        {
            get { return Serialize.BrushToString(OteBorderColor); }
            set { OteBorderColor = Serialize.StringToBrush(value); }
        }

        [NinjaScriptProperty]
        [XmlIgnore]
        [Display(Name = "Colors: text", GroupName = "IFVG + OTE (ICT Optimal Trade Entry)", Order = 619)]
        public Brush OteTextColor { get; set; }

        [Browsable(false)]
        public string OteTextColorSerializable
        {
            get { return Serialize.BrushToString(OteTextColor); }
            set { OteTextColor = Serialize.StringToBrush(value); }
        }

        [NinjaScriptProperty]
        [Display(Name = "Breakers at FVG / IFVG pullbacks", GroupName = "Breakers", Order = 701, Description = "Bullish breaker: a low, a high and a lower low; bearish: a high, a low and a higher high. After the break, a swing beyond the zone and a pullback into it, overlapping FVGs / IFVGs are tagged (bold IFVG label, B↑ / B↓).")]
        public bool BreakersOn { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Zone", GroupName = "Breakers", Order = 702, Description = "LastCandle: the last up-close / down-close candle making the high / low. WholeSequence: that candle and the consecutive same-color candles before it.")]
        public FioZoneMode BreakerZone { get; set; }

        [NinjaScriptProperty]
        [Range(1, 20)]
        [Display(Name = "Swing length", GroupName = "Breakers", Order = 703)]
        public int BreakerSwingLength { get; set; }

        [NinjaScriptProperty]
        [Range(1, 10)]
        [Display(Name = "Keep", GroupName = "Breakers", Order = 704)]
        public int BreakersKept { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Rectangle", GroupName = "Breakers", Order = 705, Description = "Draw the breaker rectangle and its mid line.")]
        public bool BreakerRectangle { get; set; }

        [NinjaScriptProperty]
        [XmlIgnore]
        [Display(Name = "Rectangle color", GroupName = "Breakers", Order = 706)]
        public Brush BreakerColor { get; set; }

        [Browsable(false)]
        public string BreakerColorSerializable
        {
            get { return Serialize.BrushToString(BreakerColor); }
            set { BreakerColor = Serialize.StringToBrush(value); }
        }

        [NinjaScriptProperty]
        [Display(Name = "Rectangle style", GroupName = "Breakers", Order = 707)]
        public DashStyleHelper BreakerLineStyle { get; set; }

        [NinjaScriptProperty]
        [Range(1, 4)]
        [Display(Name = "Rectangle width", GroupName = "Breakers", Order = 708)]
        public int BreakerLineWidth { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Flag candles", GroupName = "Breakers", Order = 709, Description = "Color the breaker candle(s) once tagged at the pullback; removed together with the rectangle.")]
        public bool BreakerFlagCandles { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Shadow rectangle", GroupName = "Breakers", Order = 710, Description = "Grey transparent rectangle (no border) instead of the dashed one, also when Rectangle is off.")]
        public bool BreakerShadow { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Mitigation blocks at FVG / IFVG pullbacks", GroupName = "Mitigation blocks", Order = 801, Description = "Bullish: a low, a high and a higher low; bearish: a high, a low and a lower high. Same life cycle as breakers; IFVG labels get bold + arrow, plain FVGs M↑ / M↓ (BM↑ / BM↓).")]
        public bool MitigationOn { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Zone", GroupName = "Mitigation blocks", Order = 802)]
        public FioZoneMode MitigationZone { get; set; }

        [NinjaScriptProperty]
        [Range(1, 20)]
        [Display(Name = "Swing length", GroupName = "Mitigation blocks", Order = 803)]
        public int MitigationSwingLength { get; set; }

        [NinjaScriptProperty]
        [Range(1, 10)]
        [Display(Name = "Keep", GroupName = "Mitigation blocks", Order = 804)]
        public int MitigationKept { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Rectangle", GroupName = "Mitigation blocks", Order = 805)]
        public bool MitigationRectangle { get; set; }

        [NinjaScriptProperty]
        [XmlIgnore]
        [Display(Name = "Rectangle color", GroupName = "Mitigation blocks", Order = 806)]
        public Brush MitigationColor { get; set; }

        [Browsable(false)]
        public string MitigationColorSerializable
        {
            get { return Serialize.BrushToString(MitigationColor); }
            set { MitigationColor = Serialize.StringToBrush(value); }
        }

        [NinjaScriptProperty]
        [Display(Name = "Rectangle style", GroupName = "Mitigation blocks", Order = 807)]
        public DashStyleHelper MitigationLineStyle { get; set; }

        [NinjaScriptProperty]
        [Range(1, 4)]
        [Display(Name = "Rectangle width", GroupName = "Mitigation blocks", Order = 808)]
        public int MitigationLineWidth { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Flag candles", GroupName = "Mitigation blocks", Order = 809)]
        public bool MitigationFlagCandles { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Shadow rectangle", GroupName = "Mitigation blocks", Order = 810)]
        public bool MitigationShadow { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Displacement FVGs only", GroupName = "Delivery Engine (Nuno)", Order = 901, Description = "Only FVGs whose middle candle range exceeds 'strength' x the std dev of the last 'length' ranges. 1PFVGs are not filtered.")]
        public bool DisplacementOnly { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Displacement range", GroupName = "Delivery Engine (Nuno)", Order = 902)]
        public FioRangeType DisplacementRange { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "Length (std dev)", GroupName = "Delivery Engine (Nuno)", Order = 903)]
        public int DispStdLength { get; set; }

        [NinjaScriptProperty]
        [Range(0, double.MaxValue)]
        [Display(Name = "Strength (x std dev)", GroupName = "Delivery Engine (Nuno)", Order = 904)]
        public double DispStrength { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Color displacement candles", GroupName = "Delivery Engine (Nuno)", Order = 905)]
        public bool ColorDispCandles { get; set; }

        [NinjaScriptProperty]
        [XmlIgnore]
        [Display(Name = "Displacement candle color", GroupName = "Delivery Engine (Nuno)", Order = 906)]
        public Brush DispCandleColor { get; set; }

        [Browsable(false)]
        public string DispCandleColorSerializable
        {
            get { return Serialize.BrushToString(DispCandleColor); }
            set { DispCandleColor = Serialize.StringToBrush(value); }
        }

        [NinjaScriptProperty]
        [Display(Name = "Flag displacement FVGs", GroupName = "Delivery Engine (Nuno)", Order = 907, Description = "Displacement FVGs are drawn with this border width.")]
        public bool FlagDispFvgs { get; set; }

        [NinjaScriptProperty]
        [Range(1, 4)]
        [Display(Name = "Flag border width", GroupName = "Delivery Engine (Nuno)", Order = 908)]
        public int FlagDispWidth { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "A+ setups", GroupName = "Delivery Engine (Nuno)", Order = 909, Description = "A+ IFVG: confirmed within 'within' candles after a sweep of the opposite swing liquidity. A+ FVG: born from an MSS with displacement after such a sweep.")]
        public bool APlusSetups { get; set; }

        [NinjaScriptProperty]
        [Range(1, 50)]
        [Display(Name = "Swing length", GroupName = "Delivery Engine (Nuno)", Order = 910)]
        public int LiquiditySwingLength { get; set; }

        [NinjaScriptProperty]
        [Range(1, 200)]
        [Display(Name = "Within", GroupName = "Delivery Engine (Nuno)", Order = 911)]
        public int APlusWithin { get; set; }

        [NinjaScriptProperty]
        [Range(1, 20)]
        [Display(Name = "MSS swing length", GroupName = "Delivery Engine (Nuno)", Order = 912)]
        public int MssSwingLength { get; set; }

        [NinjaScriptProperty]
        [Range(0.5, double.MaxValue)]
        [Display(Name = "MSS displacement (x std dev)", GroupName = "Delivery Engine (Nuno)", Order = 913)]
        public double MssDisplacement { get; set; }

        [NinjaScriptProperty]
        [Range(0, 5)]
        [Display(Name = "MSS candles", GroupName = "Delivery Engine (Nuno)", Order = 914)]
        public int MssCandles { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "A+ only inside killzones", GroupName = "Delivery Engine (Nuno)", Order = 915, Description = "Asia, London, NY AM, NY PM in the TZ of the 1st Presented FVGs. Ignored above 15m.")]
        public bool APlusKillzones { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "A+ only with the HTF structure", GroupName = "Delivery Engine (Nuno)", Order = 916, Description = "Ignored when not higher than the chart timeframe.")]
        public bool APlusHtfStructure { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "HTF structure timeframe", GroupName = "Delivery Engine (Nuno)", Order = 917, Description = "Limited to the loaded ladder series: 5m, 15m, 1h, 4h, D.")]
        public FioHtf APlusHtf { get; set; }
        #endregion
    }
}
