using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Security.Principal;
using System.Text.Encodings.Web;
using TradingPlatform.BusinessLayer;


namespace test
{
    public class TestStrategy1 : Strategy, ICurrentAccount, ICurrentSymbol
    {
        [InputParameter("Symbol", 0)]
        public Symbol CurrentSymbol { get; set; }

        /// <summary>
        /// Account to place orders
        /// </summary>
        [InputParameter("Account", 1)]
        public Account CurrentAccount { get; set; }

        /// <summary>
        /// Period to load history
        /// </summary>
        [InputParameter("Period", 3)]
        private Period period = Period.MIN1;

        /// <summary>
        /// MaxPositions allowed to open
        /// </summary>
        [InputParameter("MaxPositions", 3)]
        private int MaxPositions = 1;

        public override string[] MonitoringConnectionsIds => new string[] { this.CurrentSymbol?.ConnectionId, this.CurrentAccount?.ConnectionId };

        private Indicator BBstd3;
        private Indicator BBstd2;
        private Indicator BBstd1;

        private HistoricalData hdm;
        //private HistoricalData heikenAshiHistoricalData;

        private int longPositionsCount;
        private int shortPositionsCount;
        private string orderTypeId;

        private bool waitOpenPosition;
        private bool waitClosePositions;
        //custom
        private string StrategyVersion;
        private double AvailableBuyingPower;
        private double UsedBuyingPower;
        private int i =0;

        public TestStrategy1()
            : base()
        {
            this.Name = "TestStrategy1";
            this.Description = "tbd";
            this.StrategyVersion = "0.0.1";
        }
        
        protected override void OnStop()
        {
            Core.PositionAdded -= this.Core_PositionAdded;
            Core.PositionRemoved -= this.Core_PositionRemoved;
            Core.OrdersHistoryAdded -= this.Core_OrdersHistoryAdded;
            if (this.hdm != null)
            {
                this.hdm.HistoryItemUpdated -= this.Hdm_HistoryItemUpdated;
                this.hdm.Dispose();
            }
            //if (this.heikenAshiHistoricalData != null)
            //{
            //    this.heikenAshiHistoricalData.HistoryItemUpdated -= this.heikenAshiHistoricalData_HistoryItemUpdated;
            //    this.heikenAshiHistoricalData.Dispose();
            //}
            base.OnStop();
        }
        private void Core_PositionAdded(Position obj)
        {
            var positions = Core.Instance.Positions.Where(x => x.Symbol == this.CurrentSymbol && x.Account == this.CurrentAccount).ToArray();
            this.longPositionsCount = positions.Count(x => x.Side == Side.Buy);
            this.shortPositionsCount = positions.Count(x => x.Side == Side.Sell);
            double currentPositionsQty = positions.Sum(x => x.Side == Side.Buy ? x.Quantity : -x.Quantity);
            if (Math.Abs(currentPositionsQty) == this.MaxPositions)
                this.waitOpenPosition = false;
        }
        private void Core_PositionRemoved(Position obj)
        {
            var positions = Core.Instance.Positions.Where(x => x.Symbol == this.CurrentSymbol && x.Account == this.CurrentAccount).ToArray();
            this.longPositionsCount = positions.Count(x => x.Side == Side.Buy);
            this.shortPositionsCount = positions.Count(x => x.Side == Side.Sell);

            if (!positions.Any())
                this.waitClosePositions = false;
        }
        private void ProcessTradingRefuse()
        {
            this.Log("Strategy have received refuse for trading action. It should be stopped", StrategyLoggingLevel.Error);
            this.Stop();
        }
        protected override List<StrategyMetric> OnGetMetrics()
        {
            var result = base.OnGetMetrics();
            result.Add("Total long positions", this.longPositionsCount.ToString());
            result.Add("Total short positions", this.shortPositionsCount.ToString());
            return result;
        }
        private void Core_OrdersHistoryAdded(OrderHistory obj)
        {
            if (obj.Symbol == this.CurrentSymbol)
                return;

            if (obj.Account == this.CurrentAccount)
                return;

            if (obj.Status == OrderStatus.Refused)
                this.ProcessTradingRefuse();
        }
        private void heikenAshiHistoricalData_HistoryItemUpdated(object send, HistoryEventArgs e) => this.OnUpdate();
        private void Hdm_HistoryItemUpdated(object sender, HistoryEventArgs e) => this.OnUpdate();
        protected override void OnRun()
        {
            // Restore account object from acive connection
            if (this.CurrentAccount != null && this.CurrentAccount.State == BusinessObjectState.Fake)
                this.CurrentAccount = Core.Instance.GetAccount(this.CurrentAccount.CreateInfo());

            // Restore symbol object from acive connection
            if (this.CurrentSymbol != null && this.CurrentSymbol.State == BusinessObjectState.Fake)
                this.CurrentSymbol = Core.Instance.GetSymbol(this.CurrentSymbol.CreateInfo());

            if (this.CurrentSymbol == null || this.CurrentAccount == null || this.CurrentSymbol.ConnectionId != this.CurrentAccount.ConnectionId)
            {
                this.Log("Incorrect input parameters... Symbol or Account are not specified or they have different connectionID.", StrategyLoggingLevel.Error);
                return;
            }

            this.orderTypeId = Core.OrderTypes.FirstOrDefault(x => x.ConnectionId == this.CurrentSymbol.ConnectionId && x.Behavior == OrderTypeBehavior.Market).Id;

            if (string.IsNullOrEmpty(this.orderTypeId))
            {
                this.Log("Connection of selected symbol has not support market orders", StrategyLoggingLevel.Error);
                return;
            }
            AvailableBuyingPower = 10000.0;
            UsedBuyingPower = 0;
            var RequiredBuyingPower = 0;
            //Rithmic doesn't populate this data correctly.
            //AvailableBuyingPower = (double)this.CurrentAccount.AdditionalInfo.FirstOrDefault(x => x.NameKey == "Available buying power").Value;
            //UsedBuyingPower = (double)this.CurrentAccount.AdditionalInfo.FirstOrDefault(x => x.NameKey == "Used buying power").Value;
            //var RequiredBuyingPower = this.CurrentSymbol.Last;
            var positions = Core.Instance.Positions.Where(x => x.Symbol == this.CurrentSymbol && x.Account == this.CurrentAccount).ToArray();
            this.longPositionsCount = positions.Count(x => x.Side == Side.Buy);
            this.shortPositionsCount = positions.Count(x => x.Side == Side.Sell);

            this.Log($"StrategyVersion: {StrategyVersion}", StrategyLoggingLevel.Info);
            this.Log($"AvailableBuyingPower: {AvailableBuyingPower}", StrategyLoggingLevel.Info);
            this.Log($"UsedBuyingPower: {UsedBuyingPower}", StrategyLoggingLevel.Info);
            this.Log($"RequiredBuyingPower: {String.Format("{0:C}", RequiredBuyingPower)}", StrategyLoggingLevel.Info);
            this.Log($"Starting longPositionsCount: {longPositionsCount}", StrategyLoggingLevel.Info);
            this.Log($"Starting shortPositionsCount: {shortPositionsCount}", StrategyLoggingLevel.Info);
            if ((Math.Abs(longPositionsCount) + Math.Abs(shortPositionsCount) == 0 && AvailableBuyingPower <= RequiredBuyingPower) 
                || AvailableBuyingPower.IsNanOrDefault() 
                || AvailableBuyingPower <= RequiredBuyingPower)
            {
                this.Log($"Not enough buying power to execute strategy", StrategyLoggingLevel.Error);
                this.Stop();
            }
            //QT backtesting starting date: 4/14/2023 6:00 AM
            //QT backtesting ending date: 4/14/2023 4:00 PM
            //QT backtesting replaying from: 4/14/2023 9:00 AM
            //MNQ From 1minute

            //getting data from 4/14/2023 4am to current
            this.hdm = this.CurrentSymbol.GetHistory(
                this.period,
                this.CurrentSymbol.HistoryType,
                DateTime.Parse("4/14/2023 4:00 AM")    
                );
            this.Log($"history items count:{hdm.Count}", StrategyLoggingLevel.Info);

            //DateTime.Parse("4/14/2023 04:00 PM")
            //this.heikenAshiHistoricalData = this.CurrentSymbol.GetHistory(new HistoryRequestParameters()
            //{
            //    Symbol = this.CurrentSymbol,
            //    FromTime = DateTime.Parse("4/14/2023 09:00 AM"),
            //    HistoryType = this.CurrentSymbol.HistoryType,
            //    Aggregation = new HistoryAggregationHeikenAshi(HeikenAshiSource.Minute, 1),
            //});

            Core.PositionAdded += this.Core_PositionAdded;
            Core.PositionRemoved += this.Core_PositionRemoved;
            Core.OrdersHistoryAdded += this.Core_OrdersHistoryAdded;
            this.hdm.HistoryItemUpdated += this.Hdm_HistoryItemUpdated;
            //this.heikenAshiHistoricalData.HistoryItemUpdated += this.heikenAshiHistoricalData_HistoryItemUpdated;
            this.BBstd1  = Core.Instance.Indicators.BuiltIn.BB(200, 1, PriceType.Close, MaMode.SMA);
            this.BBstd2 = Core.Instance.Indicators.BuiltIn.BB(200, 2, PriceType.Close, MaMode.SMA);
            this.BBstd3 = Core.Instance.Indicators.BuiltIn.BB(200, 3, PriceType.Close, MaMode.SMA);
        }
        private void OnUpdate()
        {
            i++;
            var positions = Core.Instance.Positions.Where(x => x.Symbol == this.CurrentSymbol && x.Account == this.CurrentAccount).ToArray();

            if (this.waitOpenPosition)
                return;

            if (this.waitClosePositions)
                return;
            var last = this.CurrentSymbol.Last;
            this.Log($":{this.CurrentSymbol.LastDateTime}", StrategyLoggingLevel.Info);
            double x = this.BBstd3.GetValue(1);
            double bbStd3UpperValue = this.BBstd3.LinesSeries.FirstOrDefault(x => x.Name == "Upper Band").GetValue();
            double bbStd3LowerValue = this.BBstd3.LinesSeries.FirstOrDefault(x => x.Name == "Lower Band").GetValue();
            if(bbStd3LowerValue.ToString() != "NaN" || x.ToString() != "NaN"
                || i > 200)
            {
                this.Log($"bbStd3LowerValue:{bbStd3LowerValue} i:{i}", StrategyLoggingLevel.Info);
                this.Log($"history items count:{hdm.Count} i:{i}", StrategyLoggingLevel.Info);
                //debug - halt run
                ProcessTradingRefuse();
            }
            //if (positions.Any())
            //{
            //    //Closing positions
            //    if (this.indicatorFastMA.GetValue(1) < this.indicatorSlowMA.GetValue(1) || this.indicatorFastMA.GetValue(1) > this.indicatorSlowMA.GetValue(1))
            //    {
            //        this.waitClosePositions = true;
            //        this.Log($"Start close positions ({positions.Length})");

            //        foreach (var item in positions)
            //        {
            //            var result = item.Close();

            //            if (result.Status == TradingOperationResultStatus.Failure)
            //                this.ProcessTradingRefuse();
            //            else
            //                this.Log($"Position was close: {result.Status}", StrategyLoggingLevel.Trading);
            //        }
            //    }
            //}
            //else
            //{
            //    //Opening new positions
            //    if (this.indicatorFastMA.GetValue(2) < this.indicatorSlowMA.GetValue(2) && this.indicatorFastMA.GetValue(1) > this.indicatorSlowMA.GetValue(1))
            //    {
            //        this.waitOpenPosition = true;
            //        this.Log("Start open buy position");
            //        var result = Core.Instance.PlaceOrder(new PlaceOrderRequestParameters()
            //        {
            //            Account = this.CurrentAccount,
            //            Symbol = this.CurrentSymbol,

            //            OrderTypeId = this.orderTypeId,
            //            Quantity = this.Quantity,
            //            Side = Side.Buy,
            //        });

            //        if (result.Status == TradingOperationResultStatus.Failure)
            //            this.ProcessTradingRefuse();
            //        else
            //            this.Log($"Position open: {result.Status}", StrategyLoggingLevel.Trading);
            //    }
            //    else if (this.indicatorFastMA.GetValue(2) > this.indicatorSlowMA.GetValue(2) && this.indicatorFastMA.GetValue(1) < this.indicatorSlowMA.GetValue(1))
            //    {
            //        this.waitOpenPosition = true;
            //        this.Log("Start open sell position");
            //        var result = Core.Instance.PlaceOrder(new PlaceOrderRequestParameters()
            //        {
            //            Account = this.CurrentAccount,
            //            Symbol = this.CurrentSymbol,

            //            OrderTypeId = this.orderTypeId,
            //            Quantity = this.Quantity,
            //            Side = Side.Sell,
            //        });

            //        if (result.Status == TradingOperationResultStatus.Failure)
            //            this.ProcessTradingRefuse();
            //        else
            //            this.Log($"Position open: {result.Status}", StrategyLoggingLevel.Trading);
            //    }
            //}
        }
    }
}
