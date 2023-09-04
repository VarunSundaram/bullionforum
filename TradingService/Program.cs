using System;
using System.IO;
using System.Collections.Generic;
using System.Configuration;
using System.Threading;

using KiteConnect;
using System.Runtime.InteropServices;

using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;

namespace TradingService
{
    public class Program
    {
        [DllImport("kernel32.dll")]
        static extern IntPtr GetConsoleWindow();

        [DllImport("user32.dll")]
        static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        const int SW_HIDE = 0;
        const int SW_SHOW = 5;

        public static void Main(string[] args)
        {
            var handle = GetConsoleWindow();
            //ShowWindow(handle, SW_HIDE);
            //ShowWindow(handle, SW_SHOW);

            Program prsTradeExec = new Program();
            prsTradeExec.StartSession();

            //Uncomment below only to check the S3 bucket push & comment all above lines
            //KiteInterface.GetInstance.StartWebSessionToken();
            //BollingerTracker bt = new BollingerTracker();
            //bt.GetMISMargin();
            //bt.WriteServiceNamesToFile();
            //bt.putStatusContent();
            /*
            bt.ClearTheTable();
            Random rand = new Random(1000);
            uint instrument = 175361;
            string futName = "CHOLAFIN FEB FUT";
            decimal ltp = 410;
            decimal change = (decimal)6.4;
            decimal second = (decimal)410.6;
            OType otype = OType.Sell;
            int x;
            while (true)
            {
                x = rand.Next(1000);
                Thread.Sleep(10000);
                if (x % 2 > 0)
                {
                    bt.InsertNewToken(instrument, futName, ltp, change,
                        second, "BUY", "Progress", otype);
                }
                else
                {
                    bt.ClearTheTable();
                }
            }
            */
        }

        void StartSession()
        {
            //foreach (int token in tokens)
            //{

            //    ParameterizedThreadStart internalOpeationRef = ThreadService;
            //    new Thread(internalOpeationRef).Start(token);
            //}
            #region Initilize Trade Service
            foreach (System.Diagnostics.Process proc in System.Diagnostics.Process.GetProcessesByName("chromedriver"))
            {
                proc.Kill();
            }
            foreach (System.Diagnostics.Process proc in System.Diagnostics.Process.GetProcessesByName("chrome"))
            {
                proc.Kill();
            }

            DateTime lastWrite = DateTime.Now;
            if (File.Exists(ConfigurationManager.AppSettings["inputFile"]))
            {
                Console.WriteLine("Starting New Private service session");
                lastWrite = File.GetLastWriteTime(ConfigurationManager.AppSettings["inputFile"]);
            }
            Dictionary<uint, WatchList> tokens = new Dictionary<uint, WatchList>();
            List<UInt32> instruments = new List<UInt32>();
            BollingerTracker bt = null;
            CommodityBollingerTracker cbt = null;
            bool flag;
            decimal timenow;
            decimal startTime = Convert.ToDecimal(ConfigurationManager.AppSettings["ServiceStartTime"]);
            decimal stopTime = Convert.ToDecimal(ConfigurationManager.AppSettings["ServiceStopTime"]);
            decimal cutOffTime = Convert.ToDecimal(ConfigurationManager.AppSettings["CutoffTime"]);
            bool isVolatile = false;
            #endregion

            while (true)
            {
                try
                {
                    #region Validate the Time of the Service Start time
                    bool isHoliday = checkHoliday(DateTime.Now);
                    if (DateTime.Now.DayOfWeek == DayOfWeek.Sunday || DateTime.Now.DayOfWeek == DayOfWeek.Saturday || isHoliday)
                    {
                        bt = new BollingerTracker();
                        flag = false;
                        Thread.Sleep(10000);
                        return;
                    }
                    else
                    {
                        timenow = (decimal)DateTime.Now.Hour + ((decimal)DateTime.Now.Minute / 100);
                        if (Decimal.Compare(timenow, startTime) < 0 || Decimal.Compare(timenow, cutOffTime) >= 0)
                        //if (Decimal.Compare(timenow, (decimal)(0.00)) < 0 || Decimal.Compare(timenow, (decimal)(23.59)) > 0)
                        {
                            flag = false;
                            Thread.Sleep(10000);
                            return;
                        }
                        else
                        {
                            flag = true;
                            if (KiteInterface.GetInstance._kite == null)
                            {
                                if (File.Exists(ConfigurationManager.AppSettings["OutFile"]))
                                {
                                    Console.SetOut(TextWriter.Null);
                                    OutToFile.Dispose();
                                    File.Delete(ConfigurationManager.AppSettings["OutFile"]);
                                }
                                OutToFile.WriteLine("Initiating New Kite Session..");
                                KiteInterface.GetInstance.StartWebSessionToken();
                                while (KiteInterface.GetInstance._kite == null)
                                {
                                    Thread.Sleep(10000);
                                    KiteInterface.GetInstance.StartWebSessionToken();
                                }
                                if (KiteInterface.GetInstance._kite == null)
                                {
                                    Console.WriteLine("Issue in creating Kite Session. Session Closed");
                                    OutToFile.WriteLine("Issue in creating Kite Session. Session Closed");
                                }
                                bt = new BollingerTracker();
                                cbt = new CommodityBollingerTracker();
                            }
                        }
                    }
                    #endregion

                    #region Early Morning Trade
                    timenow = DateTime.Now.Hour + ((decimal)DateTime.Now.Minute / 100);
                    Console.WriteLine("Current Time is {0}", DateTime.Now.ToString("dddd, dd/MM/yyyy hh:mm:ss tt"));
                    //cbt.CalculateBB();
                    bt.CalculateDayBB();
                    if (Decimal.Compare(timenow, Convert.ToDecimal(8.30)) >= 0 && Decimal.Compare(timenow, Convert.ToDecimal(10.24)) < 0)
                    {
                        List<string> csvline = new List<string>();
                        int file = 0;
                        while (file <= 3 && csvline.Count < 5)
                        {
                            csvline = bt.CalculateBB();
                            file++;
                            Thread.Sleep(5000);
                        }
                        //csvline.Add(bt.CalculateCommodityBB(ConfigurationManager.AppSettings["CRUDE"].ToString(), "CRUDEOILM19JUNFUT"));
                        if (csvline.Count < 3)
                        {
                            Console.WriteLine("Watchlist Script selection is not successful due to some error");
                            throw new Exception("Watchlist Script selection is not successful due to some error");
                        }
                        bt.WriteToCsv(csvline);
                        Console.WriteLine("Writing to file is successfull"); 
                        csvline = cbt.CalculateBB();
                        cbt.WriteToCsv(csvline);
                        Console.WriteLine("Writing to file is successfull");
                        //bt.putStatusContent();
                    }
                    isVolatile = bt.WaitUntilHeadStartTime();

                    Console.WriteLine("Time is either between 9.28AM & 9.36AM :: " + DateTime.Now.ToString("yyyyMMdd hh:mm:ss"));
                    if (Decimal.Compare(timenow, stopTime) < 0)
                    {
                        tokens = ReadInputData(isVolatile, out instruments);
                        if (tokens.Count > 0)
                        {
                            Console.WriteLine("HERE Starting the DAY TICKS for {0} number of scripts at time {1}", tokens.Count, DateTime.Now.ToString());
                            bt.InitiateWatchList(tokens, DateTime.Now.Date.AddDays(-4), DateTime.Now.Date);
                            bt.initTicker(instruments);
                        }
                        else
                            Console.WriteLine("Token count is empty for DAY trade as the time is {0}", DateTime.Now.ToString());
                    }
                    List<UInt32> comInstruments = new List<UInt32>();
                    Dictionary<uint, WatchList> comTokens = ReadCommodityData(out comInstruments);
                    if (comTokens.Count > 0 && Decimal.Compare(timenow, Convert.ToDecimal(20)) < 0)
                    {
                        Console.WriteLine("HERE Starting the DAY TICKS for {0} number of commodities at time {1}", comTokens.Count, DateTime.Now.ToString());
                        cbt.InitiateWatchList(comTokens);
                        cbt.initTicker(comInstruments);
                    }
                    else
                        Console.WriteLine("Token count is empty for Commodity trade as the time is {0}", DateTime.Now.ToString());
                    #region For Debug Purpose - change candle time in both 5 min tick and 45min tick
                    #region Chck the trend
                    /*
                    uint token = 325121;
                    List<Historical> history = kite.GetHistoricalData(token.ToString(),
                                    DateTime.Now.Date.AddHours(9).AddMinutes(15),
                                    DateTime.Now.Date.AddDays(1),
                                    "5minute"); 
                    bt.VerifyOpenAlign();
                    bt.On5minTick();
                    bt.VerifyOpenPositions();
                    //bt.CalculateSqueezedBB();
                    bt.VerifyCandleClose();
                    bt.placeOrder(3431425, 0);
                    OType trend = bt.CalculateSqueezedTrend(token.ToString(), history, 10);
                    bt.OnTick(175361, (decimal)583, 1171349);
                    */
                    #endregion

                    // 5 min bollinger band calculation
                    /*
                    DateTime previousDay;
                    DateTime currentDay;
                    bt.getDays(out previousDay, out currentDay);
                    previousDay = previousDay.AddDays(-3);
                    currentDay = currentDay.AddDays(-3).AddHours(9).AddMinutes(20);
                    bt.InitiateWatchList(tokens, previousDay, currentDay);
                    uint token = 3001089;
                    while (!(currentDay.Hour == 13 && currentDay.Minute == 10))
                    {
                        bt.On5minTick(token, previousDay, currentDay);
                        currentDay = currentDay.AddMinutes(5);
                    }

                    uint volume = 622340;
                    bool isOrder = bt.VerifyLtp(token, (decimal)242.2, volume, (decimal)11.05, (decimal)242, (decimal)242.5, (decimal)244.2, (decimal)241.3, 480000, 500000, (decimal)244.1, (decimal)-0.4);
                    isOrder = bt.VerifyLtp(token, (decimal)242.6, volume, (decimal)11.35, (decimal)242.1, (decimal)242, (decimal)244.2, (decimal)241.3, 480000, 500000, (decimal)244.1, (decimal)-0.4);
                    */
                    #endregion

                    if (Decimal.Compare(timenow, cutOffTime) <= 0)
                    {
                        while (flag)
                        {
                            Thread.Sleep(2000);
                            if (DateTime.Now.Minute == 45 || DateTime.Now.Minute == 15)
                                break;
                        }
                        bt.ClearTheTable();
                        Console.WriteLine("Time is Above 9.45AM :: " + DateTime.Now.ToString("yyyyMMdd hh:mm:ss"));
                        Thread.Sleep(2000);
                        bt.initiate30MinThread();
                    }
                    //Commodity 30 min thread
                    while (flag)
                    {
                        Thread.Sleep(2000);
                        if (DateTime.Now.Minute == 0 || DateTime.Now.Minute == 30)
                            break;
                    }
                    Thread.Sleep(2000);
                    cbt.initiate30MinThread();
                    
                    #endregion

                    while (flag)
                    {
                        Thread.Sleep(10000);
                        timenow = (decimal)DateTime.Now.Hour + ((decimal)DateTime.Now.Minute / 100);
                        if (Decimal.Compare(timenow, (decimal)9.15) < 0 || Decimal.Compare(timenow, stopTime) > 0 && instruments.Count > 1)
                        {
                            bt.CalculateDayBB();
                            if (File.Exists(ConfigurationManager.AppSettings["OutFile"]))
                            {
                                string dayLog = ConfigurationManager.AppSettings["poweShellFile"].Replace("Trading.ps1", "Redirect_" + DateTime.Now.ToString("yyyyMMddhhmmss") + ".txt");
                                if (File.Exists(dayLog))
                                    File.Delete(dayLog);
                                File.Copy(ConfigurationManager.AppSettings["OutFile"], dayLog);
                                Console.WriteLine("Day Log has been created in separate file");
                                ClearFilesThread();
                            }
                            try
                            {
                                if (bt.ticker != null)
                                {
                                    bt.ticker.Close();
                                    bt.ticker.UnSubscribe(instruments.ToArray());
                                    bt.DisposeWatchList();
                                }
                                instruments = new List<uint>();
                            }
                            catch(Exception ex)
                            {
                                Console.WriteLine("Exception caught while closing Ticker at time {0} with message {1}", DateTime.Now.ToString(), ex.Message);
                            }
                        }
                        if (Decimal.Compare(timenow, (decimal)9.15) < 0 || Decimal.Compare(timenow, cutOffTime) > 0)
                        {
                            try
                            {
                                if (cbt.ticker != null)
                                {
                                    cbt.ticker.Close();
                                    cbt.ticker.UnSubscribe(comInstruments.ToArray());
                                    cbt.DisposeWatchList();
                                    if (File.Exists(ConfigurationManager.AppSettings["StartupFile"]))
                                        File.Delete(ConfigurationManager.AppSettings["StartupFile"]);
                                }
                                comInstruments = new List<uint>();
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine("Exception caught while closing Ticker at time {0} with message {1}", DateTime.Now.ToString(), ex.Message);
                            }
                            break;
                        }
                    }

                    #region keep checking the Service Stop Time
                    timenow = (decimal)DateTime.Now.Hour + ((decimal)DateTime.Now.Minute / 100);
                    if (Decimal.Compare(timenow, startTime) < 0 || Decimal.Compare(timenow, cutOffTime) > 0)
                    {
                        if (KiteInterface.GetInstance._driver != null)
                            KiteInterface.GetInstance._driver.Close();
                        KiteInterface.GetInstance._kite = null;
                        bt.DisposeWatchList();
                        foreach (System.Diagnostics.Process proc in System.Diagnostics.Process.GetProcessesByName("chromedriver"))
                        {
                            proc.Kill();
                        }
                        foreach (System.Diagnostics.Process proc in System.Diagnostics.Process.GetProcessesByName("chrome"))
                        {
                            proc.Kill();
                        }
                        CloseAllOrders();
                        Console.WriteLine("Closes all the orders for the day .. as Current Time is " + timenow.ToString());
                    }
                    #endregion
                }

                catch (Exception ex)
                {
                    #region Exception
                    OutToFile.WriteLine("EXCEPTION in Start Process :::" + ex.Message);
                    timenow = (decimal)DateTime.Now.Hour + ((decimal)DateTime.Now.Minute / 100);
                    if (Decimal.Compare(timenow, startTime) < 0 || Decimal.Compare(timenow, cutOffTime) > 0)
                        return;
                    if (KiteInterface.GetInstance._driver != null)
                        KiteInterface.GetInstance._driver.Close();
                    KiteInterface.GetInstance._kite = null;
                    foreach (System.Diagnostics.Process proc in System.Diagnostics.Process.GetProcessesByName("chromedriver"))
                    {
                        proc.Kill();
                    }
                    foreach (System.Diagnostics.Process proc in System.Diagnostics.Process.GetProcessesByName("chrome"))
                    {
                        proc.Kill();
                    }
                    #endregion
                }
            }
        }

        private Dictionary<uint, WatchList> ReadInputData(bool isVolatile, out List<UInt32> instruments)
        {
            #region Input File
            Dictionary<uint, WatchList> tokens = new Dictionary<uint, WatchList>();
            instruments = new List<UInt32>();
            using (var reader = new StreamReader(ConfigurationManager.AppSettings["inputFile"]))
            {
                while (!reader.EndOfStream)
                {
                    var line = reader.ReadLine();
                    var cells = line.Split(',');
                    if (cells[8] != "State" && cells[8] != "CLOSE") //== "OPEN"
                    {
                        try
                        {
                            if (!Convert.ToBoolean(cells[18]))
                            {
                                Console.WriteLine("Token {0}:: Instrument {1} cannnot be trusted as it is not following Technicals Strongly", cells[2], cells[0]);
                                //continue;
                            }
                            instruments.Add(Convert.ToUInt32(cells[0]));
                            WatchList n1 = new WatchList(Convert.ToInt32(cells[0]),
                                            Convert.ToInt32(cells[1]),
                                            Convert.ToString(cells[2]) + (BollingerTracker.futMonth.Length > 0? BollingerTracker.futMonth : ""), // + ConfigurationManager.AppSettings["expiry"],
                                            Convert.ToInt32(cells[3]),
                                            Convert.ToDecimal(cells[4]),
                                            Convert.ToDecimal(cells[5]),
                                            Convert.ToDecimal(cells[6]),
                                            Convert.ToString(cells[7]),
                                            Convert.ToString(cells[8]),
                                            Convert.ToInt32(cells[9]),
                                            Convert.ToString(cells[10]),
                                            Convert.ToDecimal(cells[11]),
                                            Convert.ToDecimal(cells[12]),
                                            Convert.ToDecimal(cells[14]),
                                            isVolatile,
                                            Convert.ToString(cells[15]),
                                            Convert.ToBoolean(cells[17]),
                                            Convert.ToBoolean(cells[18]),
                                            Convert.ToString(cells[19]));
                            tokens.Add(Convert.ToUInt32(cells[0]), n1);
                        }
                        catch (Exception e)
                        {
                            if (e.Message.Contains("Input string was not in a correct format"))
                                Console.WriteLine("EXCEPTION :: Input string was not in a correct format for token {0}:: Instrument {1}", cells[2], cells[0]);
                        }
                    }
                }
            }
            return tokens;
            #endregion
        }

        private Dictionary<uint, WatchList> ReadCommodityData(out List<UInt32> instruments)
        {
            #region Input File
            Dictionary<uint, WatchList> tokens = new Dictionary<uint, WatchList>();
            instruments = new List<UInt32>();
            string fileName = (ConfigurationManager.AppSettings["inputFile"]).Replace("bulbul","Commoditybulbul");
            using (var reader = new StreamReader(fileName)) 
            {
                while (!reader.EndOfStream)
                {
                    var line = reader.ReadLine();
                    var cells = line.Split(',');
                    if (cells[8] != "State" && cells[8] != "CLOSE") //== "OPEN"
                    {
                        try
                        {
                            if (!Convert.ToBoolean(cells[18]))
                            {
                                Console.WriteLine("Token {0}:: Instrument {1} cannnot be trusted as it is not following Technicals Strongly", cells[2], cells[0]);
                                //continue;
                            }
                            instruments.Add(Convert.ToUInt32(cells[0]));
                            WatchList n1 = new WatchList(Convert.ToInt32(cells[0]),
                                            Convert.ToInt32(cells[1]),
                                            Convert.ToString(cells[2]), // + ConfigurationManager.AppSettings["expiry"],
                                            Convert.ToInt32(cells[3]),
                                            Convert.ToDecimal(cells[4]),
                                            Convert.ToDecimal(cells[5]),
                                            Convert.ToDecimal(cells[6]),
                                            Convert.ToString(cells[7]),
                                            Convert.ToString(cells[8]),
                                            Convert.ToInt32(cells[9]),
                                            Convert.ToString(cells[10]),
                                            Convert.ToDecimal(cells[11]),
                                            Convert.ToDecimal(cells[12]),
                                            Convert.ToDecimal(cells[14]),
                                            false,
                                            Convert.ToString(cells[15]),
                                            Convert.ToBoolean(cells[17]),
                                            Convert.ToBoolean(cells[18]),
                                            Convert.ToString(cells[19]));
                            tokens.Add(Convert.ToUInt32(cells[0]), n1);
                        }
                        catch (Exception e)
                        {
                            if (e.Message.Contains("Input string was not in a correct format"))
                                Console.WriteLine("EXCEPTION :: Input string was not in a correct format for token {0}:: Instrument {1}", cells[2], cells[0]);
                        }
                    }
                }
            }
            return tokens;
            #endregion
        }

        private void modifyOrderInCSV(WatchList n, Status status)
        {
            List<String> lines = new List<String>();
            using (StreamReader reader = new StreamReader(ConfigurationManager.AppSettings["inputFile"]))
            {
                String line;
                while ((line = reader.ReadLine()) != null)
                {
                    if (line.Contains(n.futId.ToString()))
                    {
                        if (status == Status.POSITION || status == Status.STANDING || status == Status.CLOSE)
                        {
                            string[] cells = line.Split(',');
                            switch (status)
                            {
                                default:
                                case Status.STANDING:
                                    cells[8] = "STANDING";
                                    break;
                                case Status.POSITION:
                                    cells[8] = "POSITION";
                                    break;
                                case Status.CLOSE:
                                    cells[8] = "CLOSE";
                                    break;
                            }
                            line = "";
                            Console.WriteLine("Modify the Order for ticker :: {0} to status {1} for the day at {2} ", n.futName, cells[8], DateTime.Now.ToString("yyyyMMdd hh: mm:ss"));
                            for (int i = 0; i < cells.Length; i++)
                            {
                                line = line + cells[i].ToString() + ",";
                            }
                            line.TrimEnd(',');
                        }
                    }
                    lines.Add(line);
                }
            }

            using (StreamWriter writer = new StreamWriter(ConfigurationManager.AppSettings["inputFile"], false))
            {
                foreach (String line in lines)
                    writer.WriteLine(line);
            }
        }

        private void modifyOrderInCSV(WatchList n, decimal newTrigger)
        {
            List<String> lines = new List<String>();
            using (StreamReader reader = new StreamReader(ConfigurationManager.AppSettings["inputFile"]))
            {
                String line;
                while ((line = reader.ReadLine()) != null)
                {
                    if (line.Contains(n.futId.ToString()))
                    {
                        if (n.type == OType.Sell)
                        {
                            Console.WriteLine("Modify the Open Order Trigger :: {0} with new SHORT value {1} for the day {2} ", n.futName, newTrigger.ToString(), DateTime.Now.ToString());
                            line = line.Replace(n.shortTrigger.ToString(), newTrigger.ToString());
                        }
                        else
                        {
                            Console.WriteLine("Modify the Open Order Trigger :: {0} with new LONG value {1} for the day {2} ", n.futName, newTrigger.ToString(), DateTime.Now.ToString());
                            line = line.Replace(n.longTrigger.ToString(), newTrigger.ToString());
                        }
                    }
                    lines.Add(line);
                }
            }

            using (StreamWriter writer = new StreamWriter(ConfigurationManager.AppSettings["inputFile"], false))
            {
                foreach (String line in lines)
                    writer.WriteLine(line);
            }
        }

        void CloseAllOrders()
        {
            List<String> lines = new List<String>();
            using (StreamReader reader = new StreamReader(ConfigurationManager.AppSettings["inputFile"]))
            {
                String line;
                while ((line = reader.ReadLine()) != null)
                {
                    string[] cells = line.Split(',');
                    cells[8] = "CLOSE";
                    line = "";
                    foreach (string cell in cells)
                    {
                        line = line + cell + ",";
                    }
                    line.Trim(',');
                    lines.Add(line);
                }
            }
            using (StreamWriter writer = new StreamWriter(ConfigurationManager.AppSettings["inputFile"], false))
            {
                foreach (String line in lines)
                    writer.WriteLine(line);
            }
        }

        bool checkHoliday(DateTime date)
        {
            string[] holidays = ConfigurationManager.AppSettings["HolidayList"].Split(',');
            string[] day = new string[2];
            bool isHoliday = false;
            foreach (string holiday in holidays)
            {
                day = holiday.Split('.');
                if (date.Month == Convert.ToInt32(day[0]) && date.Day == Convert.ToInt32(day[1]))
                {
                    isHoliday = true;
                    break;
                }
            }
            return isHoliday;
        }

        void ClearFilesThread()
        {
            DirectoryInfo TestReportsInfo = new DirectoryInfo(ConfigurationManager.AppSettings["poweShellFile"].Replace("Trading.ps1", ""));
            foreach (FileInfo file in TestReportsInfo.GetFiles())
            {
                if (file.Name.Contains(".txt") && file.CreationTime < DateTime.Now.AddDays(-6))
                {
                    file.Delete();
                }
            }
        }
    }
}
