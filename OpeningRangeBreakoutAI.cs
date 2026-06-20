// OpeningRangeBreakoutAI.cs â€" Estrategia principal ORB con IA multicapa
// NinjaTrader 8 | NinjaScript | .NET 4.8 | C# 7.x
// Integra: ORBCalculator, ORBContextFilter, ORBTradeJournal, ORBAIOrchestrator, ORBRiskManager

#region Usings
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Threading.Tasks;
using NinjaTrader.Cbi;
using NinjaTrader.Data;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Tools;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.DrawingTools;
using System.Windows.Media;
#endregion

namespace NinjaTrader.NinjaScript.Strategies
{
    /// <summary>
    /// Estrategia Opening Range Breakout con sistema de IA en 5 capas para futuros CME.
    /// Compatible con ES, NQ, RTY, CL, GC y sus versiones Micro.
    /// </summary>
    public class OpeningRangeBreakoutAI : Strategy
    {
        #region =================== PARÃMETROS EDITABLES ===================

        // --- CategorÃ­a ORB Setup ---
        [NinjaScriptProperty]
        [Range(5, 60)]
        [Display(Name = "Ventana ORB (min)", GroupName = "ORB Setup", Order = 1)]
        public int ORBWindowMinutes { get; set; }

        [NinjaScriptProperty]
        [Range(1, 50)]
        [Display(Name = "MÃ­n. ticks del rango", GroupName = "ORB Setup", Order = 2)]
        public int MinRangeTicks { get; set; }

        [NinjaScriptProperty]
        [Range(10, 500)]
        [Display(Name = "MÃ¡x. ticks del rango", GroupName = "ORB Setup", Order = 3)]
        public int MaxRangeTicks { get; set; }

        [NinjaScriptProperty]
        [Range(1, 10)]
        [Display(Name = "Ticks de confirmaciÃ³n breakout", GroupName = "ORB Setup", Order = 4)]
        public int BreakoutConfirmTicks { get; set; }

        [NinjaScriptProperty]
        [Range(1, 10)]
        [Display(Name = "MÃ¡x. barras M1 desde breakout", GroupName = "ORB Setup", Order = 5)]
        public int MaxBarsAfterBreakout { get; set; }

        [NinjaScriptProperty]
        [Range(1, 30)]
        [Display(Name = "Zona libre de resistencia (ticks)", GroupName = "ORB Setup", Order = 6)]
        public int ClearanceZoneTicks { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Operar contra el gap", GroupName = "ORB Setup", Order = 7)]
        public bool TradeAgainstGap { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Permitir re-entrada tras fakeout", GroupName = "ORB Setup", Order = 8)]
        public bool AllowReEntry { get; set; }

        [NinjaScriptProperty]
        [Range(3, 20)]
        [Display(Name = "Barras M5 espera re-entrada", GroupName = "ORB Setup", Order = 9)]
        public int ReEntryWaitBars { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Permitir trade reversal", GroupName = "ORB Setup", Order = 10)]
        public bool AllowReversalTrade { get; set; }

        // --- CategorÃ­a SesiÃ³n ---
        [NinjaScriptProperty]
        [Display(Name = "Inicio de trading (ET)", GroupName = "SesiÃ³n", Order = 1)]
        public TimeSpan TradingStartTime { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Fin de nuevas entradas (ET)", GroupName = "SesiÃ³n", Order = 2)]
        public TimeSpan TradingEndTime { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Cierre forzado (ET)", GroupName = "SesiÃ³n", Order = 3)]
        public TimeSpan ForceCloseTime { get; set; }

        [NinjaScriptProperty]
        [Range(0.05, 2.0)]
        [Display(Name = "Umbral gap (%)", GroupName = "SesiÃ³n", Order = 4)]
        public double GapThresholdPct { get; set; }

        [NinjaScriptProperty]
        [Range(0, 60)]
        [Display(Name = "Bloquear N min antes de noticia", GroupName = "SesiÃ³n", Order = 5)]
        public int BlockMinutesBeforeNews { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Horarios de noticias (HH:mm,HH:mm)", GroupName = "SesiÃ³n", Order = 6)]
        public string HighImpactTimes { get; set; }

        // --- CategorÃ­a Risk Management ---
        [NinjaScriptProperty]
        [Range(0.001, 0.05)]
        [Display(Name = "Riesgo por trade (%)", GroupName = "Risk Management", Order = 1)]
        public double RiskPctPerTrade { get; set; }

        [NinjaScriptProperty]
        [Range(1, 20)]
        [Display(Name = "MÃ¡x. contratos", GroupName = "Risk Management", Order = 2)]
        public int MaxContracts { get; set; }

        [NinjaScriptProperty]
        [Range(0.1, 1.5)]
        [Display(Name = "Stop: multiplicador rango ORB", GroupName = "Risk Management", Order = 3)]
        public double StopRangeMultiplier { get; set; }

        [NinjaScriptProperty]
        [Range(0.5, 3.0)]
        [Display(Name = "Stop: multiplicador ATR", GroupName = "Risk Management", Order = 4)]
        public double StopATRMultiplier { get; set; }

        [NinjaScriptProperty]
        [Range(4, 30)]
        [Display(Name = "Stop mÃ­nimo (ticks)", GroupName = "Risk Management", Order = 5)]
        public int MinStopTicks { get; set; }

        [NinjaScriptProperty]
        [Range(0.3, 3.0)]
        [Display(Name = "Trailing: multiplicador ATR", GroupName = "Risk Management", Order = 6)]
        public double TrailingATRMultiplier { get; set; }

        [NinjaScriptProperty]
        [Range(0.5, 5.0)]
        [Display(Name = "TP1 multiplicador rango", GroupName = "Risk Management", Order = 7)]
        public double TP1_Multiplier { get; set; }

        [NinjaScriptProperty]
        [Range(1.0, 8.0)]
        [Display(Name = "TP2 multiplicador rango", GroupName = "Risk Management", Order = 8)]
        public double TP2_Multiplier { get; set; }

        [NinjaScriptProperty]
        [Range(2.0, 12.0)]
        [Display(Name = "TP3 multiplicador rango", GroupName = "Risk Management", Order = 9)]
        public double TP3_Multiplier { get; set; }

        [NinjaScriptProperty]
        [Range(0.10, 0.80)]
        [Display(Name = "TP1: % posicion a cerrar", GroupName = "Risk Management", Order = 10)]
        public double TP1_ClosePct { get; set; }

        [NinjaScriptProperty]
        [Range(0.10, 0.80)]
        [Display(Name = "TP2: % posicion a cerrar", GroupName = "Risk Management", Order = 11)]
        public double TP2_ClosePct { get; set; }

        [NinjaScriptProperty]
        [Range(0.05, 0.50)]
        [Display(Name = "TP3: % posicion restante", GroupName = "Risk Management", Order = 12)]
        public double TP3_ClosePct { get; set; }

        [NinjaScriptProperty]
        [Range(0.01, 0.10)]
        [Display(Name = "PÃ©rdida diaria mÃ¡x. (%)", GroupName = "Risk Management", Order = 13)]
        public double MaxDailyLoss { get; set; }

        [NinjaScriptProperty]
        [Range(0.01, 0.15)]
        [Display(Name = "Ganancia diaria objetivo (%)", GroupName = "Risk Management", Order = 14)]
        public double MaxDailyProfit { get; set; }

        [NinjaScriptProperty]
        [Range(1, 10)]
        [Display(Name = "MÃ¡x. trades por dÃ­a", GroupName = "Risk Management", Order = 15)]
        public int MaxDailyTrades { get; set; }

        [NinjaScriptProperty]
        [Range(1, 5)]
        [Display(Name = "MÃ¡x. pÃ©rdidas consecutivas", GroupName = "Risk Management", Order = 16)]
        public int MaxConsecutiveLosses { get; set; }

        // --- CategorÃ­a AI Core (Capa 3) ---
        [NinjaScriptProperty]
        [Display(Name = "Proveedor de IA", GroupName = "AI Core (Capa 3)", Order = 1)]
        public ORBAIProvider AIProvider { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "API Key", GroupName = "AI Core (Capa 3)", Order = 2)]
        public string AIApiKey { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Modelo personalizado (vacÃ­o = default)", GroupName = "AI Core (Capa 3)", Order = 3)]
        public string AIModelOverride { get; set; }

        [NinjaScriptProperty]
        [Range(0.40, 0.95)]
        [Display(Name = "Confianza mÃ­nima IA", GroupName = "AI Core (Capa 3)", Order = 4)]
        public double AIMinConfidence { get; set; }

        [NinjaScriptProperty]
        [Range(0.20, 0.80)]
        [Display(Name = "Prob. fakeout mÃ¡xima", GroupName = "AI Core (Capa 3)", Order = 5)]
        public double FakeoutMaxThreshold { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Habilitar validaciÃ³n IA (Capa 3)", GroupName = "AI Core (Capa 3)", Order = 6)]
        public bool EnableAIValidation { get; set; }

        // --- CategorÃ­a AI Advanced Layers ---
        [NinjaScriptProperty]
        [Display(Name = "Habilitar anÃ¡lisis de rÃ©gimen (Capa 1)", GroupName = "AI Advanced Layers", Order = 1)]
        public bool EnableDailyRegimeCheck { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Habilitar aprendizaje continuo (Capa 2)", GroupName = "AI Advanced Layers", Order = 2)]
        public bool EnableLearningLayer { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Habilitar guardia de riesgo (Capa 4)", GroupName = "AI Advanced Layers", Order = 3)]
        public bool EnableRiskGuard { get; set; }

        [NinjaScriptProperty]
        [Range(1, 20)]
        [Display(Name = "Intervalo guardia riesgo (barras M5)", GroupName = "AI Advanced Layers", Order = 4)]
        public int RiskGuardCheckIntervalBars { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Habilitar anÃ¡lisis post-trade (Capa 5)", GroupName = "AI Advanced Layers", Order = 5)]
        public bool EnablePostTradeAnalysis { get; set; }

        [NinjaScriptProperty]
        [Range(5, 50)]
        [Display(Name = "Trades para aprendizaje (Capa 2)", GroupName = "AI Advanced Layers", Order = 6)]
        public int JournalLookbackTrades { get; set; }

        #endregion

        #region =================== CAMPOS PRIVADOS ===================

        // Sub-sistemas
        private ORBCalculator     _orbCalc;
        private ORBContextFilter  _contextFilter;
        private ORBRiskManager    _riskMgr;
        private ORBAIOrchestrator _aiOrch;
        private ORBTradeJournal   _journal;

        // Estado del dÃ­a
        private bool   _dailyTradingEnabled  = true;
        private bool   _sessionInitialized   = false;
        private bool   _orbWindowOpen        = false;
        private bool   _orbWindowClosed      = false;
        private bool   _newPositionBlocked   = false;   // despuÃ©s de 14:30

        // Resultados de las capas 1 y 2
        private RegimeAnalysis    _regimeResult;
        private LearningAdjustment _learningResult;
        private double             _sessionMinConfidence;  // ajustado por Capa 2

        // Estado del trade activo
        private TradeParameters   _activeTradeParams;
        private ORBSignalPayload  _activeEntryPayload;
        private bool              _tp1Hit = false;
        private bool              _tp2Hit = false;
        private bool              _tp3Hit = false;
        private bool              _inReEntry = false;
        private int               _reEntryWaitCounter = 0;
        private double            _maxFavorableExcursion = 0;
        private double            _maxAdverseExcursion   = 0;
        private int               _barsHeld              = 0;
        private int               _entryBar              = -1;
        private double            _entryPrice            = 0;
        private int               _entryContracts        = 0;

        // Snapshot completo al momento de la entrada
        private double _entryAiConfidence   = 0;
        private double _entryAiFakeoutProb  = 0;
        private double _entryAiRiskAdj      = 1.0;
        private string _entryAiReason       = "";
        private double _entryRsiM5          = 0;
        private double _entryMacdHist       = 0;
        private int    _entryBarsSinceBO    = 0;

        // Guardia de riesgo (Capa 4)
        private int    _lastRiskGuardBar    = -1;
        private bool   _riskGuardTriggered  = false;
        private string _riskGuardLastAction = "";

        // Seguimiento de volumen promedio (aproximaciÃ³n 30 dÃ­as)
        private double _avgDailyVolume = 0;
        private double _sessionVolume  = 0;

        // Indicadores tÃ©cnicos (M1 = BarsInProgress 0, M5 = 1, M15 = 2)
        private NinjaTrader.NinjaScript.Indicators.ATR  _atrM1;
        private NinjaTrader.NinjaScript.Indicators.ATR  _atrM5;
        private NinjaTrader.NinjaScript.Indicators.RSI  _rsiM5;
        private NinjaTrader.NinjaScript.Indicators.MACD _macdM5;
        private double _sessionVwap;
        private double _vwapCumPv;
        private double _vwapCumVol;
        private double _cumProfitAtEntry;

        // Para panel de texto en chart
        private bool _panelNeedsUpdate = true;

        #endregion

        #region =================== DEFAULTS Y SETUP ===================

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description = "Opening Range Breakout con IA Multicapa (5 capas) para futuros CME";
                Name        = "OpeningRangeBreakoutAI";

                // ORB Setup
                ORBWindowMinutes     = 30;
                MinRangeTicks        = 8;
                MaxRangeTicks        = 60;
                BreakoutConfirmTicks = 2;
                MaxBarsAfterBreakout = 3;
                ClearanceZoneTicks   = 10;
                TradeAgainstGap      = false;
                AllowReEntry         = true;
                ReEntryWaitBars      = 6;
                AllowReversalTrade   = false;

                // SesiÃ³n
                TradingStartTime         = new TimeSpan(10, 0, 0);
                TradingEndTime           = new TimeSpan(14, 30, 0);
                ForceCloseTime           = new TimeSpan(15, 0, 0);
                GapThresholdPct          = 0.30;
                BlockMinutesBeforeNews   = 15;
                HighImpactTimes          = "08:30,10:00,14:00";

                // Risk
                RiskPctPerTrade      = 0.01;
                MaxContracts         = 4;
                StopRangeMultiplier  = 0.50;
                StopATRMultiplier    = 1.2;
                MinStopTicks         = 8;
                TrailingATRMultiplier= 1.0;
                TP1_Multiplier       = 1.5;
                TP2_Multiplier       = 2.5;
                TP3_Multiplier       = 4.0;
                TP1_ClosePct         = 0.40;
                TP2_ClosePct         = 0.35;
                TP3_ClosePct         = 0.25;
                MaxDailyLoss         = 0.02;
                MaxDailyProfit       = 0.035;
                MaxDailyTrades       = 3;
                MaxConsecutiveLosses = 2;

                // AI Core
                AIProvider           = ORBAIProvider.OpenAI;
                AIApiKey             = "";
                AIModelOverride      = "";
                AIMinConfidence      = 0.65;
                FakeoutMaxThreshold  = 0.50;
                EnableAIValidation   = true;

                // AI Advanced
                EnableDailyRegimeCheck      = true;
                EnableLearningLayer         = true;
                EnableRiskGuard             = true;
                RiskGuardCheckIntervalBars  = 5;
                EnablePostTradeAnalysis     = true;
                JournalLookbackTrades       = 20;

                // NT8: calcular PnL de posiciÃ³n
                Calculate               = Calculate.OnBarClose;
                IsExitOnSessionCloseStrategy = true;
                ExitOnSessionCloseSeconds    = 30;
                BarsRequiredToTrade          = 20;
                MaximumBarsLookBack          = MaximumBarsLookBack.TwoHundredFiftySix;
                IsOverlay                    = true;            }
            else if (State == State.Configure)
            {
                // Agregar M5 y M15 como series adicionales
                AddDataSeries(Data.BarsPeriodType.Minute, 5);
                AddDataSeries(Data.BarsPeriodType.Minute, 15);

                // Inicializar journal y cargar historial
                _journal = new ORBTradeJournal(Instrument.FullName, Print);
                _journal.LoadFromDisk();
            }
            else if (State == State.DataLoaded)
            {
                // Inicializar indicadores en los timeframes correctos
                _atrM1  = ATR(BarsArray[0], 14);
                _atrM5  = ATR(BarsArray[1], 14);
                _rsiM5  = RSI(BarsArray[1], 14, 3);
                _macdM5 = MACD(BarsArray[1], 12, 26, 9);
                _sessionVwap = 0;

                // Inicializar sub-sistemas
                _orbCalc = new ORBCalculator(
                    TickSize, MinRangeTicks, MaxRangeTicks, 1.5, Print);

                _contextFilter = new ORBContextFilter(
                    GapThresholdPct, BlockMinutesBeforeNews, TickSize, Print);
                _contextFilter.SetNewsTimes(HighImpactTimes);

                _riskMgr = new ORBRiskManager(TickSize, Instrument.MasterInstrument.TickSize > 0
                    ? Instrument.MasterInstrument.PointValue * TickSize
                    : 12.50,
                    GetAccountCash(),
                    Print);

                ConfigureRiskManager();

                // Inicializar orquestador de IA (solo en live/paper, no en backtest)
                if (State != State.Historical)
                {
                    _aiOrch = new ORBAIOrchestrator(AIProvider, AIApiKey, Print, AIModelOverride);
                }

                _sessionMinConfidence = AIMinConfidence;
            }
            else if (State == State.Terminated)
            {
                _aiOrch?.Dispose();
            }
        }

        private void ConfigureRiskManager()
        {
            _riskMgr.StopRangeMultiplier  = StopRangeMultiplier;
            _riskMgr.StopAtrMultiplier    = StopATRMultiplier;
            _riskMgr.MinStopTicks         = MinStopTicks;
            _riskMgr.TrailingAtrMultiplier= TrailingATRMultiplier;
            _riskMgr.TP1_Multiplier       = TP1_Multiplier;
            _riskMgr.TP2_Multiplier       = TP2_Multiplier;
            _riskMgr.TP3_Multiplier       = TP3_Multiplier;
            _riskMgr.TP1_ClosePct         = TP1_ClosePct;
            _riskMgr.TP2_ClosePct         = TP2_ClosePct;
            _riskMgr.TP3_ClosePct         = TP3_ClosePct;
            _riskMgr.RiskPctPerTrade      = RiskPctPerTrade;
            _riskMgr.MaxContracts         = MaxContracts;
            _riskMgr.MaxDailyLossPct      = MaxDailyLoss;
            _riskMgr.MaxDailyProfitPct    = MaxDailyProfit;
            _riskMgr.MaxDailyTrades       = MaxDailyTrades;
            _riskMgr.MaxConsecLosses      = MaxConsecutiveLosses;
            _riskMgr.AllowReEntry         = AllowReEntry;
            _riskMgr.ReEntryWaitBars      = ReEntryWaitBars;
        }

        #endregion

        #region =================== EVENTOS DE SESIÃ"N ===================

        private void ResetSessionState()
        {
            _orbCalc.Reset();
            _riskMgr.ResetDaily();
            _sessionInitialized  = false;
            _orbWindowOpen       = false;
            _orbWindowClosed     = false;
            _newPositionBlocked  = false;
            _dailyTradingEnabled = true;
            _tp1Hit              = false;
            _tp2Hit              = false;
            _tp3Hit              = false;
            _inReEntry           = false;
            _reEntryWaitCounter  = 0;
            _activeTradeParams   = null;
            _activeEntryPayload  = null;
            _entryAiConfidence   = 0;
            _entryAiFakeoutProb  = 0;
            _entryAiRiskAdj      = 1.0;
            _entryAiReason       = "";
            _entryRsiM5          = 0;
            _entryMacdHist       = 0;
            _entryBarsSinceBO    = 0;
            _riskGuardTriggered  = false;
            _riskGuardLastAction = "";
            _entryPrice          = 0;
            _entryContracts      = 0;
            _regimeResult        = new RegimeAnalysis { FavorableForOrb = true, MaxRiskToday = 1.0 };
            _learningResult      = new LearningAdjustment { AdjustedMinConfidence = AIMinConfidence };
            _sessionMinConfidence= AIMinConfidence;
            _sessionVolume       = 0;
            _sessionVwap         = 0;
            _vwapCumPv           = 0;
            _vwapCumVol          = 0;

            // Solo ejecutar capas 1 y 2 en modo live/paper
            if (State == State.Historical || _aiOrch == null) return;

            // Lanzar capas 1 y 2 en paralelo (fire-and-forget seguro con NT8)
            _ = Task.Run(async () =>
            {
                try
                {
                    var ctx = BuildDailyContext();
                    var last20 = _journal.GetLast(JournalLookbackTrades);
                    var summary = _journal.GetSummary(7);

                    Task<RegimeAnalysis>    t1 = EnableDailyRegimeCheck
                        ? _aiOrch.AnalyzeDailyRegimeAsync(ctx)
                        : Task.FromResult(new RegimeAnalysis { FavorableForOrb = true, MaxRiskToday = 1.0, IsValid = false });

                    Task<LearningAdjustment> t2 = EnableLearningLayer && last20.Count >= 3
                        ? _aiOrch.AnalyzeRecentPerformanceAsync(last20, Instrument.FullName, summary)
                        : Task.FromResult(new LearningAdjustment { AdjustedMinConfidence = AIMinConfidence, IsValid = false });

                    await Task.WhenAll(t1, t2);

                    _regimeResult   = t1.Result;
                    _learningResult = t2.Result;

                    // Aplicar Capa 1: desactivar trading si el rÃ©gimen no favorece ORB
                    if (EnableDailyRegimeCheck && _regimeResult.IsValid
                        && !_regimeResult.FavorableForOrb && _regimeResult.Conviction >= 0.70)
                    {
                        _dailyTradingEnabled = false;
                        Print($"[Capa1] Trading DESHABILITADO â€" rÃ©gimen: {_regimeResult.RegimeReason}");
                    }

                    if (_regimeResult.IsValid)
                        _riskMgr.SetMaxRiskTodayMultiplier(_regimeResult.MaxRiskToday);

                    // Aplicar Capa 2: ajustar umbral de confianza
                    if (EnableLearningLayer && _learningResult.IsValid)
                        _sessionMinConfidence = _learningResult.AdjustedMinConfidence;

                    _panelNeedsUpdate = true;
                    Print($"[SesiÃ³n] RÃ©gimen:{_regimeResult.Regime} | " +
                          $"MinConf ajustada:{_sessionMinConfidence:F2} | " +
                          $"TradingEnabled:{_dailyTradingEnabled}");
                }
                catch (Exception ex)
                {
                    Print($"[SesiÃ³n] ERROR en capas 1/2: {ex.Message}");
                }
            });
        }

        #endregion

        #region =================== LOOP PRINCIPAL ===================

        protected override void OnBarUpdate()
        {
            // Solo procesar la serie M1 (BarsInProgress == 0) para la lÃ³gica principal
            if (BarsInProgress != 0) return;
            if (Bars.IsFirstBarOfSession)
                ResetSessionState();
            if (CurrentBars[0] < BarsRequiredToTrade) return;

            var now = Time[0];
            var tod = now.TimeOfDay;
            UpdateSessionVwap();

            // â"€â"€ FASE 1: Pre-market â€" calcular Globex y gap â"€â"€
            if (!_sessionInitialized && tod >= new TimeSpan(8, 0, 0) && tod < new TimeSpan(9, 30, 0))
            {
                InitializePreMarket();
            }

            // â"€â"€ FASE 2: Inicio ventana ORB a las 09:30 â"€â"€
            if (tod >= new TimeSpan(9, 30, 0) && !_orbWindowOpen && !_orbWindowClosed)
            {
                _orbCalc.StartBuilding();
                _orbWindowOpen = true;

                // Calcular gap de apertura con el primer precio del dÃ­a
                _contextFilter.CalculateGap(Open[0]);
                Print($"[ORB] Ventana abierta â€" Gap: {_contextFilter.GapPct:F2}% {_contextFilter.GapDirection}");
            }

            // Actualizar rango mientras la ventana estÃ¡ abierta
            if (_orbWindowOpen && !_orbWindowClosed)
            {
                double atr14 = _atrM1.Value[0] / TickSize;
                _orbCalc.Update(High[0], Low[0], Close[0], CurrentBar, atr14);

                // Cerrar ventana ORB al cumplirse el tiempo
                var orbEnd = new TimeSpan(9 + ORBWindowMinutes / 60,
                                          30 + ORBWindowMinutes % 60, 0);
                if (ORBWindowMinutes == 30) orbEnd = new TimeSpan(10, 0, 0);
                else if (ORBWindowMinutes == 60) orbEnd = new TimeSpan(10, 30, 0);
                else if (ORBWindowMinutes == 15) orbEnd = new TimeSpan(9, 45, 0);
                else if (ORBWindowMinutes == 5)  orbEnd = new TimeSpan(9, 35, 0);

                if (tod >= orbEnd)
                {
                    _orbCalc.CloseWindow();
                    _orbWindowOpen   = false;
                    _orbWindowClosed = true;
                    DrawOrbLevels();
                    Print($"[ORB] Ventana cerrada â€" High:{_orbCalc.ORB_High:F2} " +
                          $"Low:{_orbCalc.ORB_Low:F2} Range:{_orbCalc.ORB_Range}t Valid:{_orbCalc.IsRangeValid}");
                }
            }

            // â"€â"€ FASE 3: Trading activo â"€â"€
            if (_orbWindowClosed && _orbCalc.IsRangeValid)
            {
                _contextFilter.Evaluate(now, true,
                    now.DayOfWeek == DayOfWeek.Friday,
                    IsHolidayEve(now));

                // Actualizar volumen relativo
                _sessionVolume += Volume[0];
                _contextFilter.UpdateVolumeRatio(_sessionVolume,
                    _avgDailyVolume > 0 ? _avgDailyVolume : _sessionVolume);

                // Actualizar sesgo del dÃ­a usando M15
                if (CurrentBars[2] > 0)
                    _contextFilter.UpdateDayBias(Closes[2][0]);

                // Verificar fakeout retroactivo
                _orbCalc.CheckFakeout(Close[0], CurrentBar);

                // Buscar entradas en ventana activa
                if (tod >= TradingStartTime && tod < TradingEndTime && !_newPositionBlocked)
                {
                    if (Position.MarketPosition == MarketPosition.Flat)
                        EvaluateEntry(now);
                }

                // â"€â"€ FASE 4: GestiÃ³n de posiciÃ³n abierta â"€â"€
                if (Position.MarketPosition != MarketPosition.Flat)
                {
                    ManageOpenPosition(now);

                    // Guardia de riesgo Capa 4 (cada N barras M5)
                    if (EnableRiskGuard && _aiOrch != null
                        && CurrentBars[1] > 0
                        && CurrentBars[1] - _lastRiskGuardBar >= RiskGuardCheckIntervalBars)
                    {
                        CheckRiskGuard();
                    }
                }
            }

            // â"€â"€ Cierre forzado a las 15:00 â"€â"€
            if (tod >= ForceCloseTime && Position.MarketPosition != MarketPosition.Flat)
            {
                Print("[ORB] Cierre forzado por tiempo (15:00 ET).");
                ExitLong("ForceClose", "");
                ExitShort("ForceClose", "");
            }

            // Bloquear nuevas entradas a las 14:30
            if (tod >= TradingEndTime) _newPositionBlocked = true;

            // Mover a breakeven a las 14:30 si hay posiciÃ³n abierta
            if (tod >= TradingEndTime && Position.MarketPosition != MarketPosition.Flat && !_tp1Hit)
            {
                SetBreakEven();
            }

            UpdatePanel();
        }

        #endregion

        #region =================== LÃ"GICA DE ENTRADA ===================

        private void EvaluateEntry(DateTime now)
        {
            if (!_dailyTradingEnabled) return;
            if (!_contextFilter.IsFavorableContext) return;
            if (!_riskMgr.CheckCanTrade(out string blockReason))
            {
                Print($"[Entry] Bloqueado: {blockReason}");
                return;
            }

            bool tryLong  = _orbCalc.HasLongSignal  && !_orbCalc.LongFakeout
                         && (CurrentBar - _orbCalc.LongBreakoutBar) <= MaxBarsAfterBreakout;
            bool tryShort = _orbCalc.HasShortSignal && !_orbCalc.ShortFakeout
                         && (CurrentBar - _orbCalc.ShortBreakoutBar) <= MaxBarsAfterBreakout;

            // Sin reversal: la primera señal detectada bloquea la dirección contraria
            if (!AllowReversalTrade)
            {
                if (_orbCalc.HasLongSignal)  tryShort = false;
                if (_orbCalc.HasShortSignal) tryLong  = false;
            }

            if (tryLong  && ConditionsMetForLong())  TriggerEntry(true,  now);
            if (tryShort && ConditionsMetForShort()) TriggerEntry(false, now);
        }

        private bool ConditionsMetForLong()
        {
            // ConfirmaciÃ³n de ticks sobre ORB_High
            if (_orbCalc.BreakoutStrength < BreakoutConfirmTicks) return false;

            // Fakeout anterior en esta direcciÃ³n
            if (_orbCalc.LongFakeout) return false;

            // Gap opuesto y no se permite
            if (_contextFilter.GapDirection == "DOWN" && !TradeAgainstGap) return false;

            // Capa 1: direcciÃ³n bloqueada por rÃ©gimen
            if (_regimeResult?.AvoidDirections?.Contains("LONG") == true) return false;

            // Volumen y momentum en M5
            if (CurrentBars[1] < 3) return false;
            if (_rsiM5.Value[0] <= 50) return false;
            if (_macdM5.Diff[0] <= 0) return false;

            // Sesgo contrario a la entrada â†’ reducir (no bloquear), ya manejado en contratos
            return true;
        }

        private bool ConditionsMetForShort()
        {
            if (_orbCalc.BreakoutStrength < BreakoutConfirmTicks) return false;
            if (_orbCalc.ShortFakeout) return false;
            if (_contextFilter.GapDirection == "UP" && !TradeAgainstGap) return false;
            if (_regimeResult?.AvoidDirections?.Contains("SHORT") == true) return false;
            if (CurrentBars[1] < 3) return false;
            if (_rsiM5.Value[0] >= 50) return false;
            if (_macdM5.Diff[0] >= 0) return false;
            return true;
        }

        private void TriggerEntry(bool isLong, DateTime now)
        {
            // Construir payload para Capa 3
            var payload = BuildEntryPayload(isLong, now);

            // Validar con IA (bloqueante) â€" solo en live/paper
            EntrySignalValidation validation;
            if (EnableAIValidation && _aiOrch != null && State != State.Historical)
            {
                try
                {
                    validation = Task.Run(() => _aiOrch.ValidateEntryAsync(payload)).GetAwaiter().GetResult();
                }
                catch (Exception ex)
                {
                    Print($"[Entry] Error Capa 3: {ex.Message} â€" rechazando entrada.");
                    DrawRejectedSignal(isLong);
                    return;
                }

                if (!validation.IsValid || !validation.Approve
                    || validation.Confidence < _sessionMinConfidence
                    || validation.FakeoutProbability > FakeoutMaxThreshold)
                {
                    Print($"[Entry] IA rechazÃ³: {validation.Reason} " +
                          $"Conf:{validation.Confidence:F2} Fakeout:{validation.FakeoutProbability:F2}");
                    DrawRejectedSignal(isLong);
                    return;
                }

                _riskMgr.SetAIRiskAdjustment(validation.RiskAdjustment);
                payload.SessionMinConfidence = _sessionMinConfidence;
            }
            else
            {
                validation = new EntrySignalValidation
                {
                    Approve = true, Confidence = 0.70, RiskAdjustment = 1.0,
                    FakeoutProbability = 0.30, IsValid = true
                };
            }

            // Calcular parÃ¡metros de riesgo
            double atr5Ticks = CurrentBars[1] > 0 ? _atrM5.Value[0] / TickSize : _orbCalc.ORB_Range;

            bool   outsideGlobex = _contextFilter.IsBreakoutOutsideGlobex(
                                     isLong ? _orbCalc.ORB_High : _orbCalc.ORB_Low, isLong);
            double globexFactor  = outsideGlobex ? 1.0 : 0.70;
            double volFactor     = _contextFilter.VolumeSizeFactor;
            double biasFactor    = (_contextFilter.DayBias == DayBias.Bullish && !isLong)
                                || (_contextFilter.DayBias == DayBias.Bearish && isLong)
                                ? 0.70 : 1.0;

            var tradeParams = _riskMgr.Calculate(
                Close[0], _orbCalc.ORB_Range, atr5Ticks, isLong,
                globexFactor, volFactor, biasFactor, _inReEntry);

            if (!tradeParams.IsValid || tradeParams.Contracts < 1)
            {
                Print($"[Entry] ParÃ¡metros de riesgo invÃ¡lidos: {tradeParams.InvalidReason}");
                return;
            }

            // Registrar entrada
            _activeTradeParams   = tradeParams;
            _activeEntryPayload  = payload;
            _tp1Hit              = false;
            _tp2Hit              = false;
            _tp3Hit              = false;
            _entryBar            = CurrentBar;
            _barsHeld            = 0;
            _maxFavorableExcursion = 0;
            _maxAdverseExcursion   = 0;
            _entryPrice          = Close[0];
            _entryContracts      = tradeParams.Contracts;
            _entryAiConfidence   = validation.Confidence;
            _entryAiFakeoutProb  = validation.FakeoutProbability;
            _entryAiRiskAdj      = validation.RiskAdjustment;
            _entryAiReason       = validation.Reason ?? "";
            _entryRsiM5          = CurrentBars[1] > 0 ? _rsiM5.Value[0] : 0;
            _entryMacdHist       = CurrentBars[1] > 0 ? _macdM5.Diff[0] : 0;
            int boBar            = isLong ? _orbCalc.LongBreakoutBar : _orbCalc.ShortBreakoutBar;
            _entryBarsSinceBO    = CurrentBar - boBar;
            _riskGuardTriggered  = false;
            _riskGuardLastAction = "";

            // Enviar Ã³rdenes a NinjaTrader
            if (isLong)
            {
                EnterLong(tradeParams.Contracts, "ORB_LONG");
                SetStopLoss("ORB_LONG", CalculationMode.Price, tradeParams.StopPrice, false);
                SetProfitTarget("ORB_LONG_T1", CalculationMode.Price, tradeParams.Target1);
                SetProfitTarget("ORB_LONG_T2", CalculationMode.Price, tradeParams.Target2);
                SetProfitTarget("ORB_LONG_T3", CalculationMode.Price, tradeParams.Target3);
            }
            else
            {
                EnterShort(tradeParams.Contracts, "ORB_SHORT");
                SetStopLoss("ORB_SHORT", CalculationMode.Price, tradeParams.StopPrice, false);
                SetProfitTarget("ORB_SHORT_T1", CalculationMode.Price, tradeParams.Target1);
                SetProfitTarget("ORB_SHORT_T2", CalculationMode.Price, tradeParams.Target2);
                SetProfitTarget("ORB_SHORT_T3", CalculationMode.Price, tradeParams.Target3);
            }

            DrawApprovedEntry(isLong, validation);

            Print($"[Entry] {(isLong ? "LONG" : "SHORT")} @ {Close[0]:F2} | " +
                  $"Stop:{tradeParams.StopPrice:F2} T1:{tradeParams.Target1:F2} " +
                  $"T2:{tradeParams.Target2:F2} T3:{tradeParams.Target3:F2} | " +
                  $"Contratos:{tradeParams.Contracts} | IA Conf:{validation.Confidence:F2}");
        }

        #endregion

        #region =================== GESTIÃ"N DE POSICIÃ"N ABIERTA ===================

        private void ManageOpenPosition(DateTime now)
        {
            if (_activeTradeParams == null) return;

            bool isLong = Position.MarketPosition == MarketPosition.Long;
            double currentPrice = Close[0];

            // Actualizar excursiones mÃ¡ximas
            double mfe = isLong ? (High[0] - Position.AveragePrice) / TickSize
                                : (Position.AveragePrice - Low[0])  / TickSize;
            double mae = isLong ? (Position.AveragePrice - Low[0])  / TickSize
                                : (High[0] - Position.AveragePrice) / TickSize;

            if (mfe > _maxFavorableExcursion) _maxFavorableExcursion = mfe;
            if (mae > _maxAdverseExcursion)   _maxAdverseExcursion   = mae;

            _barsHeld = CurrentBar - _entryBar;

            // Verificar si precio volviÃ³ al rango por 3 barras consecutivas (invalidaciÃ³n)
            if (_orbCalc.IsPriceInsideRange(currentPrice))
            {
                // Contador de barras dentro del rango â€" simplificado con contador de barras consecutivas
                // ImplementaciÃ³n conservadora: salir inmediatamente si estÃ¡ dentro del rango
                // y el trade lleva mÃ¡s de 3 barras
                if (_barsHeld > 3)
                {
                    Print("[Exit] Precio regresÃ³ al rango ORB â€" salida por invalidaciÃ³n.");
                    ExitPosition(isLong, "RangeReturn");
                    return;
                }
            }

            // Activar trailing stop tras TP1
            if (_tp1Hit && CurrentBars[1] > 0)
            {
                double trailingTicks = _riskMgr.GetTrailingStopTicks(_atrM5.Value[0] / TickSize);
                if (isLong)
                    SetTrailStop("ORB_LONG", CalculationMode.Ticks, trailingTicks, false);
                else
                    SetTrailStop("ORB_SHORT", CalculationMode.Ticks, trailingTicks, false);
            }
        }

        private void CheckRiskGuard()
        {
            if (_aiOrch == null || Position.MarketPosition == MarketPosition.Flat) return;

            _lastRiskGuardBar = CurrentBars[1];

            bool isLong = Position.MarketPosition == MarketPosition.Long;
            double barMoveTicks = (High[1] - Low[1]) / TickSize;
            double atr5Ticks    = _atrM5.Value[0] / TickSize;

            // Detectar condiciÃ³n anÃ³mala
            string trigger = null;
            double magnitude = 0;

            if (barMoveTicks > atr5Ticks * 2.0)
            {
                trigger   = "large_bar_move";
                magnitude = barMoveTicks / atr5Ticks;
            }
            else if (Volume[1] > 0 && CurrentBars[1] > 20)
            {
                // Comparar con promedio de las Ãºltimas 20 barras M5
                double avgVol = 0;
                for (int i = 2; i < Math.Min(22, CurrentBars[1]); i++)
                    avgVol += Volumes[1][i];
                avgVol /= 20.0;

                if (avgVol > 0 && Volume[1] > avgVol * 3.0)
                {
                    trigger   = "abnormal_volume";
                    magnitude = Volume[1] / avgVol;
                }
            }

            if (trigger == null) return; // sin anomalÃ­a

            Print($"[Capa4] AnomalÃ­a detectada: {trigger} (magnitud {magnitude:F1}x)");

            double pnlTicks = isLong
                ? (Close[0] - Position.AveragePrice) / TickSize
                : (Position.AveragePrice - Close[0]) / TickSize;
            var guardPayload = new RiskGuardPayload
            {
                Trigger                  = trigger,
                TriggerMagnitude         = magnitude,
                OpenPositionDirection    = isLong ? "LONG" : "SHORT",
                OpenPositionPnlTicks     = pnlTicks,
                OpenPositionBarsHeld     = _barsHeld,
                MarketMoveLastBarTicks   = barMoveTicks,
                CurrentStopDistanceTicks = _activeTradeParams?.StopDistance ?? MinStopTicks
            };

            // Llamada bloqueante a Capa 4
            RiskGuardAction action;
            try
            {
                action = Task.Run(() => _aiOrch.CheckSystemicRiskAsync(guardPayload))
                             .GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                Print($"[Capa4] Error: {ex.Message}");
                return;
            }

            if (!action.IsValid) return;

            _riskGuardTriggered  = true;
            if (action.Action != null) _riskGuardLastAction = action.Action;

            bool isCloseAction  = action.Action == "close_immediately";
            bool isTightenAction = action.Action == "tighten_stop";

            if (isCloseAction)
            {
                Print("[Capa4] CIERRE DE EMERGENCIA - " + action.Reasoning);
                ExitPosition(isLong, "RiskGuard_Emergency");
            }
            else if (isTightenAction && action.NewStopDistanceTicks.HasValue)
            {
                double newStopDist = action.NewStopDistanceTicks.Value;
                double newStopPrice = isLong
                    ? Close[0] - (newStopDist * TickSize)
                    : Close[0] + (newStopDist * TickSize);

                Print("[Capa4] Stop ajustado a " + newStopPrice.ToString("F2") +
                      " (" + newStopDist + "t) - " + action.Reasoning);
                if (isLong)
                    SetStopLoss("ORB_LONG", CalculationMode.Price, newStopPrice, false);
                else
                    SetStopLoss("ORB_SHORT", CalculationMode.Price, newStopPrice, false);
            }
        }

        private void ExitPosition(bool isLong, string reason)
        {
            if (isLong)
                ExitLong(reason, "ORB_LONG");
            else
                ExitShort(reason, "ORB_SHORT");
        }

        private void SetBreakEven()
        {
            if (Position.MarketPosition == MarketPosition.Long)
                SetStopLoss("ORB_LONG", CalculationMode.Price, Position.AveragePrice, false);
            else
                SetStopLoss("ORB_SHORT", CalculationMode.Price, Position.AveragePrice, false);
        }

        #endregion

        #region =================== CIERRE DE TRADE Y CAPA 5 ===================

        protected override void OnExecutionUpdate(Execution execution, string executionId,
            double price, int quantity, MarketPosition marketPosition,
            string orderId, DateTime time)
        {
            // Detectar cierre de posiciÃ³n (posiciÃ³n llegÃ³ a plano)
            if (Position.MarketPosition == MarketPosition.Flat && _activeTradeParams != null)
            {
                double pnlUsd  = GetCumProfit() - _cumProfitAtEntry;
                string exitReason = execution.Name?.Contains("Stop") == true ? "stop_loss"
                    : execution.Name?.Contains("T1")     == true ? "tp1_hit"
                    : execution.Name?.Contains("T2")     == true ? "tp2_hit"
                    : execution.Name?.Contains("T3")     == true ? "tp3_hit"
                    : execution.Name?.Contains("Force")  == true ? "time_stop"
                    : execution.Name?.Contains("Guard")  == true ? "risk_guard"
                    : "unknown";

                double stopDistance = _activeTradeParams?.StopDistance ?? 1;
                double rMultiple    = stopDistance > 0
                    ? (_maxFavorableExcursion / stopDistance)
                    : 0;
                string result = pnlUsd >= 0 ? "win" : "loss";

                _riskMgr.RecordTradeResult(pnlUsd);

                // Guardar en journal y disparar Capa 5
                if (_activeEntryPayload != null)
                {
                    double tickVal  = Instrument.MasterInstrument.PointValue * TickSize;
                    double pnlTicks = tickVal > 0 ? pnlUsd / tickVal : 0;
                    double exitPx   = execution.Price;
                    string patsFail = _learningResult?.PatternsFailing != null
                        ? string.Join("|", _learningResult.PatternsFailing) : "";

                    var closedPayload = new ClosedTradePayload
                    {
                        EntryConditions       = _activeEntryPayload,
                        AiConfidenceAtEntry   = _entryAiConfidence,
                        AiFakeoutProbability  = _entryAiFakeoutProb,
                        ActualResult          = result,
                        ActualRMultiple       = rMultiple,
                        ExitReason            = exitReason,
                        BarsHeld              = _barsHeld,
                        MaxFavorableExcursion = _maxFavorableExcursion,
                        MaxAdverseExcursion   = _maxAdverseExcursion
                    };

                    if (EnablePostTradeAnalysis && _aiOrch != null && State != State.Historical)
                    {
                        // Capturar snapshot antes del Task.Run (campos mutables)
                        double snapEntry   = _entryPrice;
                        int    snapContr   = _entryContracts;
                        double snapStop    = _activeTradeParams?.StopDistance ?? 0;
                        bool   snapTp1    = _tp1Hit;
                        bool   snapTp2    = _tp2Hit;
                        bool   snapTp3    = _tp3Hit;
                        string snapBias   = _contextFilter.DayBias.ToString();
                        double snapRsi    = _entryRsiM5;
                        double snapMacd   = _entryMacdHist;
                        int    snapBsBO   = _entryBarsSinceBO;
                        bool   snapReEntr = _inReEntry;
                        double snapAiRAdj = _entryAiRiskAdj;
                        string snapAiReas = _entryAiReason;
                        bool   snapRG     = _riskGuardTriggered;
                        string snapRGAct  = _riskGuardLastAction;
                        int    snapCLoss  = _riskMgr.ConsecutiveLosses;
                        double snapSessC  = _sessionMinConfidence;
                        string snapPFail  = patsFail;
                        int    snapClear  = (int)(_activeEntryPayload?.ClearanceTicks ?? 0);

                        _ = Task.Run(async () =>
                        {
                            try
                            {
                                var analysis = await _aiOrch.AnalyzeClosedTradeAsync(closedPayload);
                                _journal.AddTrade(new ORBTradeRecord
                                {
                                    Date                 = time,
                                    Direction            = _activeEntryPayload?.SignalDirection ?? "LONG",
                                    AiConfidenceAtEntry  = _entryAiConfidence,
                                    AiFakeoutProb        = _entryAiFakeoutProb,
                                    Result               = result,
                                    RMultiple            = rMultiple,
                                    ExitReason           = exitReason,
                                    DayOfWeek            = time.DayOfWeek.ToString(),
                                    GapDirection         = _contextFilter.GapDirection,
                                    VolumeRatio          = _contextFilter.VolumeRatio,
                                    OrbRangeTicks        = _orbCalc.ORB_Range,
                                    PatternTag           = analysis.PatternTag,
                                    MaxFavorableTicks    = _maxFavorableExcursion,
                                    MaxAdverseTicks      = _maxAdverseExcursion,
                                    DailyRegime          = _regimeResult?.Regime ?? "",
                                    RegimeConviction     = _regimeResult?.Conviction ?? 0,
                                    EntryPrice           = snapEntry,
                                    ExitPrice            = exitPx,
                                    Contracts            = snapContr,
                                    PnlUsd               = pnlUsd,
                                    PnlTicks             = pnlTicks,
                                    StopTicks            = snapStop,
                                    Tp1Hit               = snapTp1,
                                    Tp2Hit               = snapTp2,
                                    Tp3Hit               = snapTp3,
                                    DayBias              = snapBias,
                                    RsiM5AtEntry         = snapRsi,
                                    MacdHistAtEntry      = snapMacd,
                                    ClearanceTicks       = snapClear,
                                    BarsSinceBreakout    = snapBsBO,
                                    WasReEntry           = snapReEntr,
                                    AiRiskAdjustment     = snapAiRAdj,
                                    AiReason             = snapAiReas,
                                    RiskGuardTriggered   = snapRG,
                                    RiskGuardAction      = snapRGAct,
                                    ConfCalibrationError = analysis.ConfidenceCalibrationError,
                                    PostTradeLesson      = analysis.Lesson ?? "",
                                    ConsecLossesAtEntry  = snapCLoss,
                                    SessionMinConf       = snapSessC,
                                    PatternsFailing      = snapPFail
                                });
                            }
                            catch (Exception ex)
                            {
                                Print($"[Capa5] Error: {ex.Message}");
                            }
                        });
                    }
                    else
                    {
                        // Sin IA post-trade: guardar el registro directamente
                        _journal.AddTrade(new ORBTradeRecord
                        {
                            Date                 = time,
                            Direction            = _activeEntryPayload?.SignalDirection ?? "LONG",
                            AiConfidenceAtEntry  = _entryAiConfidence,
                            AiFakeoutProb        = _entryAiFakeoutProb,
                            Result               = result,
                            RMultiple            = rMultiple,
                            ExitReason           = exitReason,
                            DayOfWeek            = time.DayOfWeek.ToString(),
                            GapDirection         = _contextFilter.GapDirection,
                            VolumeRatio          = _contextFilter.VolumeRatio,
                            OrbRangeTicks        = _orbCalc.ORB_Range,
                            DailyRegime          = _regimeResult?.Regime ?? "",
                            RegimeConviction     = _regimeResult?.Conviction ?? 0,
                            EntryPrice           = _entryPrice,
                            ExitPrice            = exitPx,
                            Contracts            = _entryContracts,
                            PnlUsd               = pnlUsd,
                            PnlTicks             = pnlTicks,
                            StopTicks            = _activeTradeParams?.StopDistance ?? 0,
                            Tp1Hit               = _tp1Hit,
                            Tp2Hit               = _tp2Hit,
                            Tp3Hit               = _tp3Hit,
                            DayBias              = _contextFilter.DayBias.ToString(),
                            RsiM5AtEntry         = _entryRsiM5,
                            MacdHistAtEntry      = _entryMacdHist,
                            ClearanceTicks       = (int)(_activeEntryPayload?.ClearanceTicks ?? 0),
                            BarsSinceBreakout    = _entryBarsSinceBO,
                            WasReEntry           = _inReEntry,
                            AiRiskAdjustment     = _entryAiRiskAdj,
                            AiReason             = _entryAiReason,
                            RiskGuardTriggered   = _riskGuardTriggered,
                            RiskGuardAction      = _riskGuardLastAction,
                            ConsecLossesAtEntry  = _riskMgr.ConsecutiveLosses,
                            SessionMinConf       = _sessionMinConfidence,
                            PatternsFailing      = patsFail
                        });
                    }
                }

                // Limpiar estado del trade
                _activeTradeParams  = null;
                _activeEntryPayload = null;
                _tp1Hit             = false;
                _tp2Hit             = false;
                _tp3Hit             = false;
                _panelNeedsUpdate   = true;
            }

            // Detectar TP1 para activar breakeven y trailing
            if (execution.Name?.Contains("T1") == true)
            {
                _tp1Hit = true;
                SetBreakEven();
                Print("[TP1] Alcanzado â€" breakeven activado.");
            }
            if (execution.Name?.Contains("T2") == true)
            {
                _tp2Hit = true;
                Print("[TP2] Alcanzado.");
            }
            if (execution.Name?.Contains("T3") == true)
            {
                _tp3Hit = true;
                Print("[TP3] Alcanzado.");
            }
        }

        #endregion

        #region =================== INICIALIZACIÃ"N Y HELPERS ===================

        private void InitializePreMarket()
        {
            _sessionInitialized = true;

            // Calcular cierre del dÃ­a previo (barra de ayer)
            if (CurrentBar > 1)
                _contextFilter.SetPrevDayClose(Closes[0][1]);

            // Calcular Globex (High/Low desde las 18:00 ET del dÃ­a previo)
            // Simplificado: usar el High/Low de las Ãºltimas 24 barras M1 antes de 09:30
            double globHigh = double.MinValue, globLow = double.MaxValue;
            int lookback = Math.Min(CurrentBar, 8 * 60); // hasta 8 horas atrÃ¡s
            for (int i = 1; i <= lookback; i++)
            {
                if (High[i] > globHigh) globHigh = High[i];
                if (Low[i]  < globLow)  globLow  = Low[i];
            }
            if (globHigh > double.MinValue)
                _contextFilter.UpdateGlobex(globHigh, globLow);

            // Volumen promedio del dÃ­a (estimaciÃ³n con el volumen de ayer)
            if (_avgDailyVolume == 0 && CurrentBar > 390)
            {
                double vol = 0;
                for (int i = 1; i <= 390; i++) vol += Volume[i];
                _avgDailyVolume = vol;
            }

            Print($"[PreMarket] Inicializado â€" Globex High:{_contextFilter.GlobexHigh:F2} " +
                  $"Low:{_contextFilter.GlobexLow:F2}");
        }

        private DailyContextData BuildDailyContext()
        {
            return new DailyContextData
            {
                Date              = Time[0].ToString("yyyy-MM-dd"),
                Instrument        = Instrument.FullName,
                PrevDayRangeTicks = CurrentBar > 1 ? (High[1] - Low[1]) / TickSize : 50,
                PrevDayVolumeVsAvg= 1.0,
                GlobexRangeTicks  = _contextFilter.GlobexRangeTicks,
                GapPct            = _contextFilter.GapPct,
                DayOfWeek         = Time[0].DayOfWeek.ToString(),
                FedEventToday     = HighImpactTimes?.Contains("14:00") == true
                                    || HighImpactTimes?.Contains("08:30") == true,
                IsDayBeforeHoliday= IsHolidayEve(Time[0])
            };
        }

        private ORBSignalPayload BuildEntryPayload(bool isLong, DateTime now)
        {
            double entryPrice = Close[0];
            double atr5Ticks  = CurrentBars[1] > 0 ? _atrM5.Value[0] / TickSize : _orbCalc.ORB_Range;
            double stopTicks  = Math.Max(_orbCalc.ORB_Range * StopRangeMultiplier,
                                Math.Max(atr5Ticks * StopATRMultiplier, MinStopTicks));
            double stopPrice  = isLong ? entryPrice - stopTicks * TickSize
                                       : entryPrice + stopTicks * TickSize;
            double t1 = isLong ? entryPrice + _orbCalc.ORB_Range * TP1_Multiplier * TickSize
                                : entryPrice - _orbCalc.ORB_Range * TP1_Multiplier * TickSize;
            double t2 = isLong ? entryPrice + _orbCalc.ORB_Range * TP2_Multiplier * TickSize
                                : entryPrice - _orbCalc.ORB_Range * TP2_Multiplier * TickSize;
            double t3 = isLong ? entryPrice + _orbCalc.ORB_Range * TP3_Multiplier * TickSize
                                : entryPrice - _orbCalc.ORB_Range * TP3_Multiplier * TickSize;

            bool outsideGlobex = _contextFilter.IsBreakoutOutsideGlobex(
                isLong ? _orbCalc.ORB_High : _orbCalc.ORB_Low, isLong);

            return new ORBSignalPayload
            {
                Instrument              = Instrument.FullName,
                Timestamp               = now.ToString("o"),
                SignalDirection         = isLong ? "LONG" : "SHORT",
                OrbHigh                 = _orbCalc.ORB_High,
                OrbLow                  = _orbCalc.ORB_Low,
                OrbRangeTicks           = _orbCalc.ORB_Range,
                OrbRangeVsAtrPct        = _orbCalc.GetRangeVsAtrPct(),
                BreakoutBarClose        = _orbCalc.BreakoutBarClose,
                BreakoutConfirmTicks    = _orbCalc.BreakoutStrength,
                BarsSinceBreakout       = CurrentBar - (isLong ? _orbCalc.LongBreakoutBar : _orbCalc.ShortBreakoutBar),
                GapPct                  = _contextFilter.GapPct,
                GapDirection            = _contextFilter.GapDirection,
                GlobexRangeTicks        = _contextFilter.GlobexRangeTicks,
                BreakoutIsOutsideGlobex = outsideGlobex,
                VolumeRatioOpen         = _contextFilter.VolumeRatio,
                DayBias                 = _contextFilter.DayBias.ToString(),
                RsiM5                   = CurrentBars[1] > 0 ? _rsiM5.Value[0] : 50,
                MacdHistM5              = CurrentBars[1] > 0 ? _macdM5.Diff[0] : 0,
                VwapCurrent             = _sessionVwap > 0 ? _sessionVwap : Close[0],
                EntryVsVwap             = (_sessionVwap > 0 && Close[0] > _sessionVwap)
                                          ? "above" : "below",
                ClearanceTicks          = ClearanceZoneTicks,
                ProposedEntry           = entryPrice,
                ProposedStop            = stopPrice,
                ProposedTarget1         = t1,
                ProposedTarget2         = t2,
                ProposedTarget3         = t3,
                RiskRewardT1            = stopTicks > 0 ? (_orbCalc.ORB_Range * TP1_Multiplier) / stopTicks : 0,
                RiskRewardT2            = stopTicks > 0 ? (_orbCalc.ORB_Range * TP2_Multiplier) / stopTicks : 0,
                DailyRegime             = _regimeResult?.Regime ?? "uncertain",
                RegimeConviction        = _regimeResult?.Conviction ?? 0.5,
                SessionMinConfidence    = _sessionMinConfidence,
                PatternsFailingToday    = _learningResult?.PatternsFailing ?? new List<string>()
            };
        }

        private bool IsHolidayEve(DateTime dt)
        {
            // Festivos USA comunes â€" vÃ­spera = dÃ­a hÃ¡bil previo al festivo
            var holidays = new[] {
                new DateTime(dt.Year,  1,  1), // New Year
                new DateTime(dt.Year,  7,  4), // Independence Day
                new DateTime(dt.Year, 11, 11), // Veterans Day
                new DateTime(dt.Year, 12, 25)  // Christmas
            };
            foreach (var h in holidays)
                if ((h - dt.Date).TotalDays == 1) return true;
            return false;
        }

        #endregion

        private void UpdateSessionVwap()
        {
            double volume = Volume[0];
            if (volume <= 0) return;

            double typicalPrice = (High[0] + Low[0] + Close[0]) / 3.0;
            _vwapCumPv  += typicalPrice * volume;
            _vwapCumVol += volume;
            _sessionVwap = _vwapCumVol > 0 ? _vwapCumPv / _vwapCumVol : Close[0];
        }

        private double GetAccountCash()
        {
            if (Account == null) return 50000.0;
            double cash = Account.Get(AccountItem.CashValue, Currency.UsDollar);
            return cash > 0 ? cash : 50000.0;
        }

        private double GetCumProfit()
        {
            return SystemPerformance.AllTrades.TradesPerformance.Currency.CumProfit;
        }

        #region =================== VISUALIZACIÃ"N ===================

        private void DrawOrbLevels()
        {
            if (!_orbCalc.IsRangeComplete) return;

            Draw.HorizontalLine(this, "ORB_High_Line", _orbCalc.ORB_High,
                Brushes.LimeGreen, DashStyleHelper.Solid, 2);
            Draw.HorizontalLine(this, "ORB_Low_Line", _orbCalc.ORB_Low,
                Brushes.Red, DashStyleHelper.Solid, 2);
            Draw.HorizontalLine(this, "ORB_Mid_Line", _orbCalc.ORB_Mid,
                Brushes.Gray, DashStyleHelper.Dash, 1);

            // RectÃ¡ngulo sombreado del rango
            int startBar = Math.Max(0, CurrentBar - (ORBWindowMinutes + 5));
            Draw.Rectangle(this, "ORB_Zone", true, startBar, _orbCalc.ORB_High,
                0, _orbCalc.ORB_Low, Brushes.LightBlue, Brushes.Transparent, 30);
        }

        private void DrawApprovedEntry(bool isLong, EntrySignalValidation v)
        {
            string tag = $"Entry_{CurrentBar}";
            if (isLong)
                Draw.ArrowUp(this, tag, false, 0, Low[0] - TickSize * 3, Brushes.LimeGreen);
            else
                Draw.ArrowDown(this, tag, false, 0, High[0] + TickSize * 3, Brushes.Red);
        }

        private void DrawRejectedSignal(bool isLong)
        {
            string tag = $"Rejected_{CurrentBar}";
            Draw.Text(this, tag, "âœ•", 0, isLong
                ? Low[0]  - TickSize * 5
                : High[0] + TickSize * 5, Brushes.Gray);
        }

        private void UpdatePanel()
        {
            if (!_panelNeedsUpdate && CurrentBar % 10 != 0) return;
            _panelNeedsUpdate = false;

            string regime   = _regimeResult?.Regime ?? "---";
            double conv     = _regimeResult?.Conviction ?? 0;
            double maxRisk  = _regimeResult?.MaxRiskToday ?? 1.0;
            double wr7d     = _journal.GetSummary(7).WinRate7d;

            string line1 = $"ORB | Range:{_orbCalc.ORB_Range:F0}t | Gap:{_contextFilter.GapPct:F2}% {_contextFilter.GapDirection} | Vol:{_contextFilter.VolumeRatio:F2}x";
            string line2 = $"RÃ©gimen: [{regime}, conv {conv:F2}] | RiesgoHoy: [{maxRisk * 100:F0}%]";
            string line3 = $"Aprendizaje: [WR7d {wr7d * 100:F0}%] | Conf.ajustada: [{_sessionMinConfidence:F2}]";
            string line4 = $"Trades: [{_riskMgr.DailyTradeCount}/{MaxDailyTrades}] | ConsecLoss: [{_riskMgr.ConsecutiveLosses}/{MaxConsecutiveLosses}] | Trading: [{(_dailyTradingEnabled ? "ON" : "OFF")}]";

            Draw.TextFixed(this, "ORB_Panel_1", line1, TextPosition.TopLeft,
                Brushes.White, new SimpleFont("Arial", 11), Brushes.Transparent, Brushes.Navy, 80);
            Draw.TextFixed(this, "ORB_Panel_2", line2 + "\n" + line3 + "\n" + line4,
                TextPosition.TopRight, Brushes.White, new SimpleFont("Arial", 10),
                Brushes.Transparent, Brushes.DarkSlateGray, 80);
        }

        #endregion
    }
}






