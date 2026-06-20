// ORBRiskManager.cs — Gestión de riesgo, stops, targets y límites diarios
// Parte del sistema IAOpenRange para NinjaTrader 8

#region Usings
using System;
#endregion

namespace NinjaTrader.NinjaScript.Strategies
{
    /// <summary>
    /// Resultado del cálculo de parámetros de una entrada.
    /// Contiene todos los valores necesarios para configurar órdenes en NinjaTrader.
    /// </summary>
    public class TradeParameters
    {
        public int    Contracts     { get; set; }
        public double StopDistance  { get; set; }  // en ticks
        public double StopPrice     { get; set; }
        public double Target1       { get; set; }
        public double Target2       { get; set; }
        public double Target3       { get; set; }
        public double Target1Ticks  { get; set; }
        public double Target2Ticks  { get; set; }
        public double Target3Ticks  { get; set; }
        public int    Contracts_T1  { get; set; }  // contratos para TP1
        public int    Contracts_T2  { get; set; }  // contratos para TP2
        public int    Contracts_T3  { get; set; }  // contratos restantes
        public double RiskReward_T1 { get; set; }
        public double RiskReward_T2 { get; set; }
        public bool   IsValid       { get; set; }
        public string InvalidReason { get; set; }
    }

    /// <summary>
    /// Gestiona todos los aspectos de riesgo del sistema ORB:
    /// cálculo de stops, targets, tamaño de posición y límites diarios.
    /// </summary>
    public class ORBRiskManager
    {
        #region Parámetros de configuración (inyectados)

        // Stops
        public double StopRangeMultiplier  { get; set; } = 0.50;
        public double StopAtrMultiplier    { get; set; } = 1.2;
        public int    MinStopTicks         { get; set; } = 8;
        public double TrailingAtrMultiplier{ get; set; } = 1.0;

        // Targets
        public double TP1_Multiplier { get; set; } = 1.5;
        public double TP2_Multiplier { get; set; } = 2.5;
        public double TP3_Multiplier { get; set; } = 4.0;
        public double TP1_ClosePct   { get; set; } = 0.40;
        public double TP2_ClosePct   { get; set; } = 0.35;
        public double TP3_ClosePct   { get; set; } = 0.25;

        // Riesgo por trade
        public double RiskPctPerTrade { get; set; } = 0.01;   // 1%
        public int    MaxContracts    { get; set; } = 4;

        // Límites diarios
        public double MaxDailyLossPct   { get; set; } = 0.02;  // 2%
        public double MaxDailyProfitPct { get; set; } = 0.035; // 3.5%
        public int    MaxDailyTrades    { get; set; } = 3;
        public int    MaxConsecLosses   { get; set; } = 2;

        // Re-entrada
        public bool AllowReEntry     { get; set; } = true;
        public int  ReEntryWaitBars  { get; set; } = 6;

        #endregion

        #region Estado diario (acumuladores)

        /// <summary>PnL acumulado del día en dólares.</summary>
        public double DailyPnlUsd { get; private set; }

        /// <summary>Número de trades ejecutados hoy.</summary>
        public int DailyTradeCount { get; private set; }

        /// <summary>Pérdidas consecutivas en la sesión actual.</summary>
        public int ConsecutiveLosses { get; private set; }

        /// <summary>True si la estrategia alcanzó el límite de pérdida diaria.</summary>
        public bool MaxDailyLossReached { get; private set; }

        /// <summary>True si la estrategia alcanzó el objetivo de ganancia diaria.</summary>
        public bool MaxDailyProfitReached { get; private set; }

        /// <summary>True si se puede abrir un nuevo trade según todos los límites diarios.</summary>
        public bool CanOpenNewTrade =>
            !MaxDailyLossReached
            && !MaxDailyProfitReached
            && DailyTradeCount < MaxDailyTrades
            && ConsecutiveLosses < MaxConsecLosses;

        #endregion

        #region Campos privados

        private readonly double       _tickSize;
        private readonly double       _tickValue;
        private readonly double       _accountCapital;
        private readonly Action<string> _log;

        // Multiplicadores dinámicos de la IA
        private double _maxRiskTodayMultiplier = 1.0;  // Capa 1
        private double _aiRiskAdjustment       = 1.0;  // Capa 3

        #endregion

        #region Constructor

        /// <summary>
        /// Inicializa el gestor de riesgo con los parámetros del instrumento y cuenta.
        /// </summary>
        /// <param name="tickSize">Tamaño del tick (ej. 0.25 para ES).</param>
        /// <param name="tickValue">Valor en USD del tick (ej. 12.50 para ES).</param>
        /// <param name="accountCapital">Capital total de la cuenta en USD.</param>
        /// <param name="log">Delegado para logging.</param>
        public ORBRiskManager(double tickSize, double tickValue,
                              double accountCapital, Action<string> log)
        {
            _tickSize       = tickSize;
            _tickValue      = tickValue;
            _accountCapital = accountCapital;
            _log            = log ?? (_ => { });
        }

        #endregion

        #region Métodos de configuración dinámica (IA)

        /// <summary>
        /// Establece el multiplicador de riesgo máximo del día (de la Capa 1).
        /// Valor entre 0.5 y 1.0.
        /// </summary>
        public void SetMaxRiskTodayMultiplier(double multiplier)
        {
            _maxRiskTodayMultiplier = Math.Max(0.5, Math.Min(1.0, multiplier));
            _log($"[RiskMgr] MaxRiskToday multiplier: {_maxRiskTodayMultiplier:F2}");
        }

        /// <summary>
        /// Establece el ajuste de riesgo de la entrada (de la Capa 3).
        /// Valor entre 0.5 y 1.0.
        /// </summary>
        public void SetAIRiskAdjustment(double adjustment)
        {
            _aiRiskAdjustment = Math.Max(0.5, Math.Min(1.0, adjustment));
        }

        #endregion

        #region Cálculo de parámetros de trade

        /// <summary>
        /// Calcula todos los parámetros de una nueva entrada (contratos, stop, targets).
        /// </summary>
        /// <param name="entryPrice">Precio de entrada.</param>
        /// <param name="orbRangeTicks">Amplitud del ORB en ticks.</param>
        /// <param name="atr14Ticks">ATR(14) en ticks del M5 actual.</param>
        /// <param name="isLong">True si es entrada LONG.</param>
        /// <param name="globexFactor">Factor de ajuste por posición respecto a Globex (0.70 o 1.0).</param>
        /// <param name="volumeFactor">Factor de ajuste por volumen (0.60 o 1.0).</param>
        /// <param name="dayBiasFactor">Factor de ajuste por sesgo del día (0.70 o 1.0).</param>
        /// <param name="isReEntry">True si es un segundo intento tras fakeout.</param>
        public TradeParameters Calculate(double entryPrice, double orbRangeTicks,
                                         double atr14Ticks, bool isLong,
                                         double globexFactor = 1.0,
                                         double volumeFactor = 1.0,
                                         double dayBiasFactor = 1.0,
                                         bool isReEntry = false)
        {
            var result = new TradeParameters();

            // --- 1. Calcular stop distance ---
            double stopByRange = orbRangeTicks * StopRangeMultiplier;
            double stopByAtr   = atr14Ticks    * StopAtrMultiplier;
            double stopTicks   = Math.Max(stopByRange, Math.Max(stopByAtr, MinStopTicks));

            result.StopDistance = stopTicks;
            result.StopPrice    = isLong
                ? entryPrice - (stopTicks * _tickSize)
                : entryPrice + (stopTicks * _tickSize);

            // --- 2. Calcular targets ---
            double t1Ticks = orbRangeTicks * TP1_Multiplier;
            double t2Ticks = orbRangeTicks * TP2_Multiplier;
            double t3Ticks = orbRangeTicks * TP3_Multiplier;

            result.Target1Ticks = t1Ticks;
            result.Target2Ticks = t2Ticks;
            result.Target3Ticks = t3Ticks;

            if (isLong)
            {
                result.Target1 = entryPrice + (t1Ticks * _tickSize);
                result.Target2 = entryPrice + (t2Ticks * _tickSize);
                result.Target3 = entryPrice + (t3Ticks * _tickSize);
            }
            else
            {
                result.Target1 = entryPrice - (t1Ticks * _tickSize);
                result.Target2 = entryPrice - (t2Ticks * _tickSize);
                result.Target3 = entryPrice - (t3Ticks * _tickSize);
            }

            result.RiskReward_T1 = stopTicks > 0 ? t1Ticks / stopTicks : 0;
            result.RiskReward_T2 = stopTicks > 0 ? t2Ticks / stopTicks : 0;

            // --- 3. Calcular número de contratos ---
            double riskPerTrade = _accountCapital * RiskPctPerTrade
                                  * _maxRiskTodayMultiplier
                                  * _aiRiskAdjustment
                                  * globexFactor
                                  * volumeFactor
                                  * dayBiasFactor;

            double riskPerContract = stopTicks * _tickValue;
            if (riskPerContract <= 0)
            {
                result.IsValid       = false;
                result.InvalidReason = "riskPerContract <= 0";
                return result;
            }

            int contracts = (int)Math.Floor(riskPerTrade / riskPerContract);

            // Factor de re-entrada: 110%
            if (isReEntry) contracts = (int)Math.Ceiling(contracts * 1.10);

            contracts = Math.Max(1, Math.Min(contracts, MaxContracts));

            result.Contracts   = contracts;
            result.Contracts_T1 = Math.Max(1, (int)Math.Round(contracts * TP1_ClosePct));
            result.Contracts_T2 = Math.Max(1, (int)Math.Round(contracts * TP2_ClosePct));
            result.Contracts_T3 = Math.Max(1, contracts - result.Contracts_T1 - result.Contracts_T2);

            // Asegurar que la suma no supere el total
            if (result.Contracts_T1 + result.Contracts_T2 + result.Contracts_T3 > contracts)
                result.Contracts_T3 = contracts - result.Contracts_T1 - result.Contracts_T2;

            result.IsValid = result.Contracts >= 1;

            _log($"[RiskMgr] Contratos:{contracts} (T1:{result.Contracts_T1} " +
                 $"T2:{result.Contracts_T2} T3:{result.Contracts_T3}) " +
                 $"Stop:{stopTicks}t T1:{t1Ticks}t T2:{t2Ticks}t T3:{t3Ticks}t " +
                 $"RR_T1:{result.RiskReward_T1:F2} RR_T2:{result.RiskReward_T2:F2}");

            return result;
        }

        /// <summary>
        /// Calcula la distancia del trailing stop en ticks (activar tras TP1).
        /// </summary>
        public double GetTrailingStopTicks(double atr14Ticks)
        {
            return Math.Max(MinStopTicks, atr14Ticks * TrailingAtrMultiplier);
        }

        #endregion

        #region Gestión de límites diarios

        /// <summary>
        /// Resetea los acumuladores diarios. Llamar en OnSessionStart().
        /// </summary>
        public void ResetDaily()
        {
            DailyPnlUsd          = 0;
            DailyTradeCount      = 0;
            ConsecutiveLosses    = 0;
            MaxDailyLossReached  = false;
            MaxDailyProfitReached= false;
            _maxRiskTodayMultiplier = 1.0;
            _aiRiskAdjustment    = 1.0;
            _log("[RiskMgr] Contadores diarios reseteados.");
        }

        /// <summary>
        /// Registra el resultado de un trade cerrado y actualiza los límites.
        /// </summary>
        /// <param name="pnlUsd">PnL del trade en USD (positivo = ganancia).</param>
        public void RecordTradeResult(double pnlUsd)
        {
            DailyPnlUsd     += pnlUsd;
            DailyTradeCount ++;

            if (pnlUsd < 0)
                ConsecutiveLosses++;
            else
                ConsecutiveLosses = 0;

            // Verificar límites
            double maxLossUsd   = _accountCapital * MaxDailyLossPct;
            double maxProfitUsd = _accountCapital * MaxDailyProfitPct;

            if (DailyPnlUsd <= -maxLossUsd)
            {
                MaxDailyLossReached = true;
                _log($"[RiskMgr] LÍMITE DE PÉRDIDA DIARIA alcanzado: {DailyPnlUsd:F2} USD");
            }

            if (DailyPnlUsd >= maxProfitUsd)
            {
                MaxDailyProfitReached = true;
                _log($"[RiskMgr] OBJETIVO DE GANANCIA DIARIA alcanzado: {DailyPnlUsd:F2} USD");
            }

            if (ConsecutiveLosses >= MaxConsecLosses)
                _log($"[RiskMgr] Pérdidas consecutivas ({ConsecutiveLosses}/{MaxConsecLosses}) — " +
                     "se bloqueará próxima entrada.");

            _log($"[RiskMgr] Trade registrado: PnL {pnlUsd:F2} USD | " +
                 $"Diario: {DailyPnlUsd:F2} USD | Trades: {DailyTradeCount}/{MaxDailyTrades} | " +
                 $"ConsecLoss: {ConsecutiveLosses}/{MaxConsecLosses}");
        }

        /// <summary>
        /// Evalúa si se puede abrir una nueva posición y describe el motivo si no se puede.
        /// </summary>
        public bool CheckCanTrade(out string blockReason)
        {
            blockReason = null;

            if (MaxDailyLossReached)
            {
                blockReason = $"Límite de pérdida diaria alcanzado ({DailyPnlUsd:F2} USD).";
                return false;
            }
            if (MaxDailyProfitReached)
            {
                blockReason = $"Objetivo de ganancia diaria alcanzado ({DailyPnlUsd:F2} USD).";
                return false;
            }
            if (DailyTradeCount >= MaxDailyTrades)
            {
                blockReason = $"Máximo de trades diarios alcanzado ({MaxDailyTrades}).";
                return false;
            }
            if (ConsecutiveLosses >= MaxConsecLosses)
            {
                blockReason = $"Pérdidas consecutivas ({ConsecutiveLosses}/{MaxConsecLosses}).";
                return false;
            }
            return true;
        }

        #endregion
    }
}
