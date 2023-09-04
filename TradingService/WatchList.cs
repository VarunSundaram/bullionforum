using System;
using System.Configuration;
using System.IO;
using System.Collections.Generic;
using KiteConnect;

namespace TradingService
{
    public enum OType
    {
        Buy,
        StrongBuy,
        Sell,
        StrongSell,
        BS
    }

    public enum LogType
    {
        INFO,
        FORCE,
        ERROR
    }

    public enum Status
    {
        OPEN,
        STANDING,
        POSITION,
        CLOSE
    }

    public class OutToFile// : IDisposable
    {
        private StreamWriter fileOutput;
        private static OutToFile otf = null;

        /// <summary>
        /// Create a new object to redirect the output
        /// </summary>
        /// <param name="outFileName">
        /// The name of the file to capture console output
        /// </param>v
        public OutToFile(string outFileName)
        {
            try
            {
                fileOutput = new StreamWriter(
                    new FileStream(outFileName, FileMode.Append)
                    );
                fileOutput.AutoFlush = true;
                Console.SetOut(fileOutput);
            }
            catch { }
        }

        // Dispose() is called automatically when the object
        // goes out of scope
        public static void Dispose()
        {
            try
            {
                if (otf != null && otf.fileOutput != null)
                    otf.fileOutput.Close(); // Done with the file
                otf = null;
            }
            catch { otf = null; }
        }

        public static void WriteLine(string Message, params string[] args)
        {
            if (otf == null || !Console.IsOutputRedirected)
                otf = new OutToFile(ConfigurationManager.AppSettings["OutFile"]);
            Message = string.Format(Message, args);
            Console.WriteLine(Message);
        }
    }

    public class WatchList
    {
        public int instrumentId { get; }
        public int futId { get; }
        public string futName { get; }
        public int lotSize { get; set; }
        public decimal shortTrigger { get; set; }

        public decimal top30bb { get; set; }

        public decimal fTop30bb { get; }

        public decimal fMiddle30BB { get; }

        public decimal middle30BB { get; set; }

        public decimal middle30BBnew { get; set; }

        public decimal bot30bb { get; set; }

        public decimal fBot30bb { get; }

        public decimal longTrigger { get; set; }

        public decimal topBB { get; set; }

        public decimal middleBB { get; set; }

        public decimal middle30ma50 { get; set; }

        public decimal botBB { get; set; }

        public decimal ma50 { get; set; }

        public decimal weekMA { get; }

        public decimal dma { get; set; }

        public decimal close { get; }

        public OType type { get; set; }

        public Status status { get; set; }

        public int lots { get; }

        public decimal target { get; set; }

        public decimal triggerPrice { get; set; }

        public int tries { get; set; }

        public Int64 AvgVolume { get; }

        public int isNarrowed { get; set; }

        public int isDoingAgain { get; set; }

        public int isDoItAgain { get; set; }

        public DateTime orderTime { get; set; }

        public DateTime oldTime { get; set; }

        public DateTime currentTime { get; set; }

        public DateTime ReversedTime { get; set; }

        public DateTime lastRun5Min { get; set; }

        public bool isMorning { get; set; }

        public OType identified { get; set; }

        public bool isSpiking { get; set; }

        public bool threeRise { get; set; }

        public bool goodToGo { get; set; }

        public bool toBuy { get; set; }

        public bool toSell { get; set; }

        public bool isReversed { get; set; }

        public bool isReorder { get; set; }

        public bool requiredExit { get; set; }

        public bool doubledrequiredExit { get; set; }

        public bool tripleRequiredExit { get; set; }

        public List<Historical> history { get; set; }

        public List<Historical> history30Min { get; set; }

        public bool isGearingUp { get; set; }

        public bool hasGeared { get; set; }

        public bool isLowVolume { get; set; }

        public bool isHighVolume { get; set; }

        public DateTime OldHDateTime { get; set; }

        public List<decimal> dayBollinger { get; set; }

        public List<decimal> highLow { get; set; }

        public bool openOppositeAlign { get; set; }

        public bool isVolatile { get; set; }

        public bool canTrust { get; set; }

        public bool canOrder { get; set; }

        //public Order order { get; set; }
        public WatchList(int iId, int fId, string fName, int lS, decimal sT, decimal lT, decimal middle, string mode, string s, int l, string day, decimal dayma50, decimal weekma, decimal pClose, bool isvolatile, string identify, bool isReverse, bool cTrust, string vol)
        {
            instrumentId = iId;
            futId = fId;
            futName = fName;
            lotSize = lS;
            shortTrigger = sT;
            fTop30bb = sT;
            longTrigger = lT;
            fBot30bb = lT;
            //middleBB = middle;
            switch (mode.ToLower())
            {
                case "buy":
                    type = OType.Buy;
                    break;
                case "sell":
                    type = OType.Sell;
                    break;
                default:
                    type = OType.BS;
                    break;
            }
            if (s == "OPEN")
                status = Status.OPEN;
            else
                status = Status.POSITION;
            if (day.ToLower() == "morning")
                isMorning = true;
            else
                isMorning = false;
            switch (identify.ToLower())
            {
                case "buy":
                    identified = OType.Buy;
                    break;
                case "sell":
                    identified = OType.Sell;
                    break;
                default:
                    identified = OType.BS;
                    break;
            }
            lots = l;
            target = 2200;
            fMiddle30BB = middle;
            middle30BB = middle;
            middle30ma50 = dayma50;
            weekMA = weekma;
            close = pClose;
            lastRun5Min = DateTime.Now;
            ReversedTime = DateTime.Now;
            oldTime = DateTime.Now;
            currentTime = oldTime;
            isVolatile = isvolatile;
            //isOpenAlign = isOpen;
            //isOpenAlignFatal = false;
            isReversed = isReverse;
            requiredExit = false;
            isReorder = false;
            doubledrequiredExit = false;
            tripleRequiredExit = false;
            openOppositeAlign = false;
            canTrust = cTrust;
            isNarrowed = 0;
            AvgVolume = Convert.ToInt64(vol);
            history = new List<Historical>();
            history30Min = new List<Historical>();
            highLow = new List<decimal>();
            dayBollinger = new List<decimal>();
            triggerPrice = 0;
            isGearingUp = false;
            hasGeared = false;
            isSpiking = false;
            tries = 0;
            isLowVolume = false;
            isDoingAgain = 0;
            threeRise = false;
            isDoItAgain = 0;
            goodToGo = false;
            toBuy = false;
            toSell = false;
        }

        public WatchList()
        {
        }
    }
}
