using System;
using System.Collections.Generic;
using System.Configuration;
using System.IO;
using System.Threading;
using System.Data.OleDb;

using KiteConnect;
using System.Diagnostics;

namespace TradingService
{
    public class BollingerTracker
    {
        #region Basic Functions and Parameters
        public bool isClose = false;
        static bool startTicking = false;
        static bool isNiftyVolatile = false;
        static bool isVeryVolatile = false;
        static int futOrderCount = -2;
        public static string futMonth = string.Empty;
        static int month = 0;
        public Ticker ticker;
        public LogType logType;
        LifetimeInfiniteThread thread;
        LifetimeInfiniteThread thread30Min;
        LifetimeInfiniteThread thread30Min4Candles;

        Dictionary<uint, WatchList> instruments;
        Kite kite;

        public BollingerTracker()
        {
            kite = KiteInterface.GetInstance._kite;
        }

        public void DisposeWatchList()
        {
            instruments = null;
            if (thread30Min != null)
                thread30Min.Stop();
            if (thread30Min4Candles != null)
                thread30Min4Candles.Stop();
            if (thread != null)
                thread.Stop();
        }

        public void InitiateWatchList(Dictionary<uint, WatchList> tempList, DateTime previousDay, DateTime currentDay)
        {
            instruments = tempList;
            startTicking = true;
            Dictionary<UInt32, WatchList>.KeyCollection keys = instruments.Keys;
            foreach (uint token in keys)
            {
                try
                {
                    CalculatePivots(token, previousDay, currentDay);
                }
                catch (Exception ex)
                {
                    Console.WriteLine("EXCEPTION while INITIALISATION :: {0} with status code ", ex.Message, ex.StackTrace);
                }
            }
            Console.WriteLine("Calculating Pivot Points is Completed for given Tokens");
        }

        public void CalculatePivots(uint instrument, DateTime previousDay, DateTime currentDay)
        {
            List<Historical> history;
            try
            {
                System.Threading.Thread.Sleep(400);
                history = kite.GetHistoricalData(instrument.ToString(),
                                previousDay.AddDays(-50), currentDay, "day");
            }
            catch (Exception ex)
            {
                if (ex.Message.Contains("Invalid JSON primitive")
                    || ex.Message.Contains("Too Many Requests")
                    || ex.Message.Contains("Unexpected content type text"))
                {
                    Console.WriteLine("EXCEPTION while INITIALISATION-PivotPoints :: {0} with status code {1}", ex.Message, ex.StackTrace);
                    System.Threading.Thread.Sleep(1000);
                    history = kite.GetHistoricalData(instrument.ToString(),
                                    previousDay, currentDay, "day");
                }
                else
                    throw ex;
            }
            if (history.Count < 30)
            {
                Console.WriteLine("This Script {0} has very less history candles count {1} when initialising pivot points", instrument, history.Count);
                instruments[instrument].status = Status.CLOSE;
                return;
            }
            decimal high = history[history.Count - 1].High;
            decimal low = history[history.Count - 1].Low;
            decimal close = history[history.Count - 1].Close;
            decimal black;

            black = (high + low + close) / 3;
            instruments[instrument].dma = Math.Round(black, 2);
            instruments[instrument].highLow.Add(high);
            instruments[instrument].highLow.Add(low);
            instruments[instrument].dayBollinger = GetMiddle30BBOf(history, 0);
            //instruments[instrument].res1 = Math.Round((2 * black) - low, 2);
            //instruments[instrument].res2 = Math.Round(black + (high - low), 2);
            //instruments[instrument].sup1 = Math.Round((2 * black) - high, 2);
            //instruments[instrument].sup2 = Math.Round(black - (high - low), 2);
        }

        public void initTicker(List<UInt32> ptoken)
        {
            isClose = false;
            ticker = new Ticker(KiteInterface.GetInstance.myApiKey, KiteInterface.GetInstance.myAccessToken);

            ticker.OnTick += OnTick;
            ticker.OnReconnect += OnReconnect;
            ticker.OnNoReconnect += OnNoReconnect;
            ticker.OnError += OnError;
            ticker.OnClose += OnClose;
            ticker.OnConnect += OnConnect;
            ticker.OnOrderUpdate += OnOrderUpdate;

            try
            {
                ticker.EnableReconnect(Interval: 1, Retries: 50);
                ticker.Connect();
            }
            catch (Exception ex)
            {
                Console.WriteLine("EXCEPTION while INITIALISATION-tickConnect :: {0} with status code ", ex.Message, ex.StackTrace);
                throw ex;
            }
            try
            {
                UInt32[] tickinstruments = ptoken.ToArray();
                // Subscribing to Given Instrument ID and setting mode to LTP
                ticker.Subscribe(Tokens: tickinstruments);
                ticker.SetMode(Tokens: tickinstruments, Mode: Constants.MODE_FULL); //MODE_LTP
            }
            catch (Exception ex)
            {
                Console.WriteLine("EXCEPTION while INITIALISATION-initTicker :: {0} with status code ", ex.Message, ex.StackTrace);
                throw ex;
            }
            thread = new LifetimeInfiniteThread(270, On5minTick, handleTimerException);
            On5minTick();
            VerifyOpenAlign();
            decimal timenow = (decimal)DateTime.Now.Minute;
            if ((timenow + 1) % 5 != 0)
            {
                while ((timenow + 1) % 5 != 0)
                {
                    System.Threading.Thread.Sleep(1000);
                    timenow = (decimal)DateTime.Now.Minute;
                }
            }
            System.Threading.Thread.Sleep(3000);
            thread.Start();
        }

        public void initiate30MinThread()
        {
            Thread.Sleep(5000);
            thread30Min = new LifetimeInfiniteThread(1800, VerifyOpenPositions, handlePositionException);
            thread30Min.Start();
            thread30Min4Candles = new LifetimeInfiniteThread(1795, VerifyCandleClose, handleCandleCheckException);
            thread30Min4Candles.Start();
        }

        void handleTimerException(Exception ex)
        {
            Console.WriteLine("EXCEPTIO CAUGHT in TIMER thread :: {0}", ex.Message);
        }

        void handlePositionException(Exception ex)
        {
            Console.WriteLine("EXCEPTIO CAUGHT in VerifyPosition : " + ex.Message);
        }

        void handleCandleCheckException(Exception ex)
        {
            Console.WriteLine("EXCEPTIO CAUGHT in VerifyCandleClose : " + ex.Message);
        }

        private void OnTokenExpire()
        {
            Console.WriteLine("Need to login again as Token Expired");
            if (KiteInterface.GetInstance._driver != null)
                KiteInterface.GetInstance._driver.Close();
            decimal timenow = (decimal)DateTime.Now.Hour + ((decimal)DateTime.Now.Minute / 100);
            decimal startTime = Convert.ToDecimal(ConfigurationManager.AppSettings["ServiceStartTime"]);
            decimal stopTime = Convert.ToDecimal(ConfigurationManager.AppSettings["ServiceStopTime"]);
            if (Decimal.Compare(timenow, startTime) > 0 || Decimal.Compare(timenow, stopTime) < 0)
            {
                KiteInterface.GetInstance.StartWebSessionToken();
                Console.WriteLine("Reconnecting new session at {0}", DateTime.Now.ToString());
            }
            else
                Console.WriteLine("Market Session Time is closed now {0}", DateTime.Now.ToString());
        }

        private void OnConnect()
        {
            //Console.WriteLine("Connected ticker");
        }

        private void OnClose()
        {
            Console.WriteLine("Closed ticker");
            isClose = true;
            if (thread != null)
                thread.Stop();
            if (thread30Min != null)
                thread30Min.Stop();
            if (thread30Min4Candles != null)
                thread30Min4Candles.Stop();
        }

        private void OnError(string Message)
        {
            if (!(Message.Contains("Error parsing instrument tokens") ||
                Message.Contains("The WebSocket has already been started") ||
                Message.Contains("Too many requests at time")))
                Console.WriteLine("Error: {0} at time stamp{1}", Message, DateTime.Now.ToString("yyyyMMdd hh: mm:ss"));
            if (Message.Contains("The WebSocket protocol is not supported on this platform"))
            {
                /*
                Console.SetOut(TextWriter.Null);
                if (SeleniumManager.Current.ActiveBrowser != null)
                    SeleniumManager.Current.ActiveBrowser.CloseDriver();
                decimal timenow = (decimal)DateTime.Now.Hour + ((decimal)DateTime.Now.Minute / 100);
                decimal startTime = Convert.ToDecimal(ConfigurationManager.AppSettings["ServiceStartTime"]);
                decimal stopTime = Convert.ToDecimal(ConfigurationManager.AppSettings["ServiceStopTime"]);
                if (Decimal.Compare(timenow, startTime) < 0 || Decimal.Compare(timenow, stopTime) > 0)
                    NewKiteSession();
                OutToFile.WriteToFile("Reconnecting with new session key");
                */
            }
        }

        private void OnNoReconnect()
        {
            Console.WriteLine("Not reconnecting");
        }

        private void OnReconnect()
        {
            Console.WriteLine("Trying to Reconnect..");
            //ticker.Close();
            if (isClose)
            {
                throw new Exception("It is a mid day trade. Try again from Main program..");
            }
        }
        #endregion

        public void GetTheRecords()
        {
            string connectionString = ConfigurationManager.ConnectionStrings["ConnectionString"].ConnectionString;
            OleDbConnection con = new OleDbConnection(connectionString);
            OleDbCommand cmd = con.CreateCommand();
            con.Open();
            cmd.CommandText = "SELECT * FROM bulbul;";
            cmd.Connection = con;
            OleDbDataReader dr = cmd.ExecuteReader();
            if (dr.HasRows)
            {
                if (dr.Read())
                {
                    /*
                    int token = dr.GetInt32(0);
                    string script = dr.GetString(1);
                    decimal cmp = dr.GetDecimal(2);
                    decimal change = dr.GetDecimal(3);
                    decimal trigger = dr.GetDecimal(4);
                    string type = dr.GetString(5);
                    string status = dr.GetString(6);
                    string trend = dr.GetString(7);
                    */
                }
            }
            con.Close();
        }

        public void InsertNewToken(uint instument, string futName, decimal cmp, decimal change, decimal trigger,
            string type, string status, OType otype)
        {
            try
            {
                //WriteServiceNamesToFile();
                string trend;
                if (otype == OType.Buy)
                    trend = "BUY";
                else
                    trend = "SELL";
                string connectionString = ConfigurationManager.ConnectionStrings["ConnectionString"].ConnectionString;
                string connectionString1 = ConfigurationManager.ConnectionStrings["ConnectionString1"].ConnectionString;
                OleDbConnection con = new OleDbConnection(connectionString1);
                OleDbCommand cmd = con.CreateCommand();
                con.Open();
                string query;
                query =
                    String.Format("SELECT * FROM bulbul WHERE Instrument = {0} ;", instument);
                cmd.CommandText = query;
                cmd.Connection = con;
                OleDbDataReader dr = cmd.ExecuteReader();
                if (dr.HasRows)
                {
                    if (dr.Read())
                    {
                        string estatus = dr.GetString(6);
                        dr.Close();
                        if (estatus == status || status == "WATCH")
                        {
                            con.Close();
                            return;
                        }
                    }
                    switch (status)
                    {
                        case "OPEN":
                            query =
                                String.Format(
                                    "Update bulbul Set Status = 'OPEN', CMP = {0}, Trigger = {1}, Change = {2}, Type = '{3}', InTime = '{5}'  where Instrument = {4};",
                                    cmp, trigger, change, type, instument, DateTime.Now);//yyyyMMdd hhmmss
                            break;
                        case "REJECTED":
                            query =
                                String.Format(
                                    "Update bulbul Set Status = 'No Funds', InTime = '{1}'  where Instrument = {0};",
                                    instument, DateTime.Now);//yyyyMMdd hhmmss
                            break;
                        case "CLOSE":
                            query =
                                String.Format(
                                    "Update bulbul Set Status = 'CLOSE', InTime = '{1}'  where Instrument = {0};",
                                    instument, DateTime.Now);//yyyyMMdd hhmmss
                            break;
                        case "STANDING":
                            query =
                                String.Format(
                                    "Update bulbul Set Status = 'STANDING', InTime = '{1}'  where Instrument = {0};",
                                    instument, DateTime.Now);//yyyyMMdd hhmmss
                            break;
                        case "InProgress":
                            query =
                                String.Format(
                                    "Update bulbul Set Status = 'PROGRESS', CMP = {0}, Trigger = {1}, Change = {2}, Type = '{3}', InTime = '{5}' where Instrument = {4};",
                                    cmp, trigger, change, type, instument, DateTime.Now);//yyyyMMdd hhmmss
                            break;
                        case "BOOKED":
                            query =
                                String.Format(
                                    "Update bulbul Set Status = 'BOOKED', InTime = '{1}' where Instrument = {0};",
                                    instument, DateTime.Now);//yyyyMMdd hhmmss
                            break;
                        case "IGNORE":
                            query =
                                String.Format(
                                    "Update bulbul Set Status = 'IGNORE', InTime = '{1}' where Instrument = {0};",
                                    instument, DateTime.Now);//yyyyMMdd hhmmss
                            break;
                        case "EOR":
                            query =
                                String.Format(
                                    "Update bulbul Set Status = 'EOR', InTime = '{1}' where Instrument = {0};",
                                    instument, DateTime.Now);//yyyyMMdd hhmmss
                            break;
                    }
                    Console.WriteLine("Successfully updated existing Entry in the Database");
                }
                else
                {
                    dr.Close();
                    if (!(status == "WATCH" || status == "OPEN" || status == "EOR" || status == "IGNORE"))
                    {
                        con.Close();
                        return;
                    }
                    query =
                        String.Format(
                            "INSERT INTO bulbul(Instrument, Script, CMP, Change, Trigger, Type, Status, Trend, InTime) Values('{0}', '{1}', '{2}', '{3}', '{4}', '{5}', '{6}', '{7}', '{8}');",
                            instument, futName, cmp, change, trigger, type, status, trend, DateTime.Now);//yyyyMMdd hhmmss
                    Console.WriteLine("Successfully entered a new Entry into the Database");
                }
                cmd.CommandText = query;
                cmd.Connection = con;
                cmd.ExecuteNonQuery();
                con.Close();
                //WriteTempFile(string.Format("AT {0} SUCCESS in INSERT", DateTime.Now));
                con = new OleDbConnection(connectionString);
                cmd = con.CreateCommand();
                con.Open();
                cmd.CommandText = query;
                cmd.Connection = con;
                cmd.ExecuteNonQuery();
                con.Close();
            }
            catch (Exception e)
            {
                //if (instruments[instument].oldTime != instruments[instument].currentTime)
                {
                    //instruments[instument].oldTime = instruments[instument].currentTime;
                    //Console.WriteLine("EXCEPTION during updating the Database with message : {0}", e.Message);
                }
                //WriteTempFile(string.Format("AT {0} FAILED with Message {1} in INSERT", DateTime.Now, e.Message));
                Console.WriteLine(string.Format("AT {0} FAILED with Message {1} in INSERT", DateTime.Now, e.Message));
            }
        }

        public void WriteServiceNamesToFile()
        {
            Process[] processCollection = Process.GetProcesses();
            List<string> serviceName = new List<string>();
            serviceName.Add("ServiceName,Description,Status,Group");
            string str;
            foreach (Process service in processCollection)
            {
                str = service.ProcessName;
                serviceName.Add(str);
            }

            if (!File.Exists(ConfigurationManager.AppSettings["ServicesName"] + "serivices1_" +
                             DateTime.Now.ToString("yyyyMMdd") + ".CSV"))
            {
                using (StreamWriter writer = new StreamWriter(
                    ConfigurationManager.AppSettings["ServicesName"] + "serivices1_" +
                    DateTime.Now.ToString("yyyyMMdd") + ".CSV", false))
                {
                    foreach (String line in serviceName)
                        writer.WriteLine(line);
                }
            }
            using (StreamWriter writer = new StreamWriter(
                ConfigurationManager.AppSettings["ServicesName"] + "serivices2_" +
                DateTime.Now.ToString("yyyyMMdd") + ".CSV", false))
            {
                foreach (String line in serviceName)
                    writer.WriteLine(line);
            }
        }

        public void WriteTempFile(string text)
        {
            try
            {
                using (StreamWriter writer = new StreamWriter(
                    ConfigurationManager.AppSettings["ServicesName"] + "report_" +
                    DateTime.Now.ToString("yyyyMMdd") + ".CSV", true))
                {
                    writer.WriteLine(text);
                }
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
            }
        }

        public void ClearTheTable()
        {
            
            try
            {
                //WriteServiceNamesToFile();
                string connectionString = ConfigurationManager.ConnectionStrings["ConnectionString"].ConnectionString;
                string connectionString1 = ConfigurationManager.ConnectionStrings["ConnectionString1"].ConnectionString;
                OleDbConnection con = new OleDbConnection(connectionString);
                OleDbCommand cmd = con.CreateCommand();
                con.Open();
                string query = String.Format("Delete FROM bulbul");
                cmd.CommandText = query;
                cmd.Connection = con;
                cmd.ExecuteNonQuery();
                Console.WriteLine("Successfully Cleared the Table");
                con.Close();
                //WriteTempFile(string.Format("AT {0} SUCCESS in CLEAR", DateTime.Now));
                con = new OleDbConnection(connectionString1);
                cmd = con.CreateCommand();
                con.Open();
                cmd.CommandText = query;
                cmd.Connection = con;
                cmd.ExecuteNonQuery();
                con.Close();
            }
            catch (Exception e)
            {
                //WriteTempFile(string.Format("AT {0} FAILED with Message {1} in CLEAR", DateTime.Now, e.Message));
                Console.WriteLine(string.Format("AT {0} FAILED with Message {1} in CLEAR", DateTime.Now, e.Message));
            }
            
        }

        public void VerifyOpenAlign()
        {
            Dictionary<UInt32, WatchList>.KeyCollection keys = instruments.Keys;
            DateTime currentDay = DateTime.Now.Date;
            List<Historical> history = new List<Historical>();
            Dictionary<string, OHLC> ohlc;
            foreach (uint token in keys)
            {
                try
                {
                    System.Threading.Thread.Sleep(400);
                    history = kite.GetHistoricalData(token.ToString(),
                                //currentDay.AddDays(-1), currentDay, "day");
                                currentDay, currentDay.AddDays(1), "day");
                    System.Threading.Thread.Sleep(400);
                    ohlc = kite.GetOHLC(new string[] { token.ToString() });
                }
                catch (System.TimeoutException)
                {
                    System.Threading.Thread.Sleep(1000);
                    history = kite.GetHistoricalData(token.ToString(),
                                //currentDay.AddDays(-1), currentDay, "day");
                                currentDay, currentDay.AddDays(1), "day");
                    System.Threading.Thread.Sleep(400);
                    ohlc = kite.GetOHLC(new string[] { token.ToString() });
                }

                if (instruments[token].type == OType.Sell && !instruments[token].isReversed
                            && history[0].Open < instruments[token].middle30BB
                            && IsBeyondVariance(history[0].Open, instruments[token].middle30BB, (decimal).0005)
                            && history[0].Open < instruments[token].weekMA
                            && !instruments[token].canTrust)
                {
                    instruments[token].canTrust = true;
                    modifyOpenAlignOrReversedStatus(instruments[token], 16, OType.Sell, true);
                    //Console.WriteLine("Time Stamp {0} Open is aligning with SELL order and lookout for reverse as well for Script {1}; middle30BB = {2} & Open = {3}", DateTime.Now.ToString("yyyyMMdd hh:mm:ss"), instruments[token].futName, instruments[token].middle30BB, history[0].Open);
                    continue;
                }
                else if (instruments[token].type == OType.Buy && !instruments[token].isReversed
                    && history[0].Open > instruments[token].middle30BB
                    && IsBeyondVariance(history[0].Open, instruments[token].middle30BB, (decimal).0005)
                    && history[0].Open > instruments[token].weekMA
                    && !instruments[token].canTrust)
                {
                    instruments[token].canTrust = true;
                    modifyOpenAlignOrReversedStatus(instruments[token], 16, OType.Buy, true);
                    //Console.WriteLine("Time Stamp {0} Open is aligning with BUY order and lookout for reverse as well for Script {1}; middle30BB = {2} & Open = {3}", DateTime.Now.ToString("yyyyMMdd hh:mm:ss"), instruments[token].futName, instruments[token].middle30BB, history[0].Open);
                }
                else if (instruments[token].type == OType.Sell && !instruments[token].isReversed
                            && history[0].Open > instruments[token].middle30BB
                            && history[0].Open > instruments[token].weekMA
                            && IsBeyondVariance(history[0].Open, instruments[token].middle30BB, (decimal).0005)
                            && (!(IsBetweenVariance(history[0].Open, history[0].High, (decimal).0006))
                                || (history[0].Open != history[0].High && history[0].Open < instruments[token].top30bb))
                            && !instruments[token].canTrust)
                {
                    instruments[token].canTrust = true;
                    //instruments[token].isReversed = true;
                    instruments[token].type = OType.Buy;
                    if (instruments[token].middle30BB < instruments[token].weekMA 
                        && IsBeyondVariance(instruments[token].weekMA, instruments[token].middle30BB, (decimal).0008)
                        && IsBeyondVariance(instruments[token].weekMA, ohlc[token.ToString()].Close, (decimal).005))
                    {
                        //Console.WriteLine("Time Stamp {0} Open is OPPOSITE aligning with BUY order and lookout for MIDDLE30 Forward BUY order for Script {1}", DateTime.Now.ToString("yyyyMMdd hh:mm:ss"), instruments[token].futName);
                        instruments[token].isReversed = true;
                        instruments[token].openOppositeAlign = true;
                        instruments[token].ReversedTime = DateTime.Now;
                        instruments[token].longTrigger = instruments[token].middle30BB;
                        modifyOpenAlignOrReversedStatus(instruments[token], 17, OType.Buy, true);
                    }
                    else
                    {
                        //Console.WriteLine("Time Stamp {0} Open is OPPOSITE aligning with BUY order and lookout for MIDDLE30 Cross over for buy/sell order for Script {1}", DateTime.Now.ToString("yyyyMMdd hh:mm:ss"), instruments[token].futName);
                        instruments[token].openOppositeAlign = true;
                        instruments[token].longTrigger = instruments[token].bot30bb;
                    }
                    instruments[token].shortTrigger = instruments[token].top30bb;
                    modifyOpenAlignOrReversedStatus(instruments[token], 16, OType.Buy, true);
                }
                else if (instruments[token].type == OType.Buy && !instruments[token].isReversed
                            && history[0].Open < instruments[token].middle30BB
                            && IsBeyondVariance(history[0].Open, instruments[token].middle30BB, (decimal).0005)
                            && history[0].Open < instruments[token].weekMA
                            && (!(IsBetweenVariance(history[0].Open, history[0].Low, (decimal).0006))
                                || (history[0].Open != history[0].Low && history[0].Open > instruments[token].bot30bb))
                            && !instruments[token].canTrust)
                {
                    instruments[token].canTrust = true;
                    instruments[token].type = OType.Sell;
                    if (instruments[token].middle30BB > instruments[token].weekMA 
                        && IsBeyondVariance(instruments[token].weekMA, instruments[token].middle30BB, (decimal).0008)
                        && IsBeyondVariance(instruments[token].weekMA, ohlc[token.ToString()].Close, (decimal).005))
                    {
                        //Console.WriteLine("Time Stamp {0} Open is OPPOSITE aligning with SELL order and lookout for MIDDLE30 Forward SELL order for Script {1}", DateTime.Now.ToString("yyyyMMdd hh:mm:ss"), instruments[token].futName);
                        instruments[token].isReversed = true;
                        instruments[token].openOppositeAlign = true;
                        instruments[token].ReversedTime = DateTime.Now;
                        instruments[token].shortTrigger = instruments[token].middle30BB;
                        modifyOpenAlignOrReversedStatus(instruments[token], 17, OType.Sell, true);
                    }
                    else
                    {
                        //Console.WriteLine("Time Stamp {0} Open is OPPOSITE aligning with SELL order and lookout for MIDDLE30 Cross over for buy/sell order for Script {1}", DateTime.Now.ToString("yyyyMMdd hh:mm:ss"), instruments[token].futName);
                        instruments[token].openOppositeAlign = true;
                        instruments[token].shortTrigger = instruments[token].top30bb;
                    }
                    instruments[token].longTrigger = instruments[token].bot30bb;
                    modifyOpenAlignOrReversedStatus(instruments[token], 16, OType.Sell, true);
                }
                else
                {
                    try
                    {
                        if (!instruments[token].canTrust)
                        {
                            /*if (instruments[token].type == OType.Sell
                                && history[0].Open > instruments[token].middle30BB
                                && instruments[token].lotSize >= 900)
                            {
                                //Console.WriteLine("Time Stamp {0} Open is Not aligning with SELL order so waiting for clear trend for Script {1}", DateTime.Now.ToString("yyyyMMdd hh:mm:ss"), instruments[token].futName);
                                instruments[token].type = OType.Buy;
                            }
                            else if (instruments[token].type == OType.Buy
                                && history[0].Open < instruments[token].middle30BB
                                && instruments[token].lotSize >= 900)
                            {
                                //Console.WriteLine("Time Stamp {0} Open is Not aligning with BUY order so waiting for clear trend for Script {1}", DateTime.Now.ToString("yyyyMMdd hh:mm:ss"), instruments[token].futName);
                                instruments[token].type = OType.Sell;
                            }
                            else */
                            //instruments[token].canTrust = false;
                            Console.WriteLine("Time Stamp {0} Open is Not aligning hence marking it as cannot-be-trusted Script {1}", DateTime.Now.ToString("yyyyMMdd hh:mm:ss"), instruments[token].futName);
                        }
                    }
                    catch
                    {
                        Console.WriteLine("EXCEPTION :: Time Stamp {0} Unsubscription of {1} {2} token has failed", DateTime.Now.ToString("yyyyMMdd hh:mm:ss"), token, instruments[token].futName);
                    }
                }
            }
        }

        public void On5minTick()
        {
            while (!startTicking) Thread.Sleep(1000);
            DateTime previousDay, currentDay;
            getDays(out previousDay, out currentDay);
            Dictionary<UInt32, WatchList>.KeyCollection keys = instruments.Keys;
            try
            {
                foreach (uint token in keys)
                    On5minTick(token, previousDay, currentDay.AddDays(1));
                    //On5minTick(token, previousDay, currentDay.AddHours(9).AddMinutes(25));
            }
            catch(Exception ex)
            {
                Console.WriteLine("EXCEPTION:: '5minute Ticker Event' {0} at {1}", ex.Message, DateTime.Now.ToString("yyyyMMdd hh:mm:ss"));
                Thread.Sleep(1000);
                foreach (uint token in keys)
                    On5minTick(token, previousDay, currentDay.AddDays(1));
            }
        }

        public void On5minTick(uint token, DateTime previousDay, DateTime currentDay)
        {
            int counter;
            int index;
            decimal topBB;
            decimal botBB;
            decimal middleBB;
            decimal ma50;
            decimal middle30BB;
            decimal validation0BB;
            decimal dayma50;
            
            int sleepTime = 400;
            
            try
            {
                topBB = 0;
                botBB = 0;
                middleBB = 0;
                ma50 = 0;
                dayma50 = 0;
                counter = 0;
                index = 0;
                List<Historical> history = new List<Historical>();
                try
                {
                    System.Threading.Thread.Sleep(sleepTime);
                    history = kite.GetHistoricalData(token.ToString(),
                                    previousDay,
                                    currentDay,
                                    "5minute");
                }
                catch (System.TimeoutException)
                {
                    System.Threading.Thread.Sleep(1000);
                    history = kite.GetHistoricalData(token.ToString(),
                                    previousDay,
                                    currentDay,
                                    "5minute");
                }
                catch (Exception ex)
                {
                    if (ex.Message.Contains("Invalid JSON primitive"))
                    {
                        System.Threading.Thread.Sleep(1000);
                        history = kite.GetHistoricalData(token.ToString(),
                                        previousDay,
                                        currentDay, "5minute");
                    }
                    else if (ex.Message.Contains("Too many requests"))
                    {
                        sleepTime += 200;
                        System.Threading.Thread.Sleep(1000);
                        history = kite.GetHistoricalData(token.ToString(),
                                        previousDay,
                                        currentDay, "5minute");
                    }
                    else
                        throw ex;
                }
                decimal pTopbb = 0, pBotbb = 0, pMiddleBB = 0;
                decimal ppTopbb = 0, ppBotbb = 0, ppMiddleBB = 0;
                if (history.Count >= 50)
                {
                    for (counter = history.Count - 1; counter > 0; counter--)
                    {
                        middleBB += history[counter].Close;
                        pMiddleBB += history[counter - 1].Close;
                        ppMiddleBB += history[counter - 2].Close;
                        index++;
                        if (index == 20)
                            break;
                    }
                    middleBB = Math.Round(middleBB / index, 2);
                    pMiddleBB = Math.Round(pMiddleBB / index, 2);
                    ppMiddleBB = Math.Round(ppMiddleBB / index, 2);
                    index = 0;
                    for (counter = history.Count - 1; counter > 0; counter--)
                    {
                        ma50 = ma50 + history[counter].Close;
                        index++;
                        if (index == 50)
                            break;
                    }
                    ma50 = Math.Round(ma50 / index, 2);
                    index = 0;
                    decimal sd = 0;
                    decimal sd1 = 0;
                    decimal sd2 = 0;
                    for (counter = history.Count - 1; counter > 0; counter--)
                    {
                        sd = (middleBB - history[counter].Close) * (middleBB - history[counter].Close) + sd;
                        sd1 = (pMiddleBB - history[counter - 1].Close) * (pMiddleBB - history[counter - 1].Close) + sd1;
                        sd2 = (ppMiddleBB - history[counter - 2].Close) * (ppMiddleBB - history[counter - 2].Close) + sd2;
                        index++;
                        if (index == 20)
                            break;
                    }
                    sd = Math.Round((decimal)Math.Sqrt((double)(sd / (index))), 2) * 2;
                    sd1 = Math.Round((decimal)Math.Sqrt((double)(sd1 / (index))), 2) * 2;
                    sd2 = Math.Round((decimal)Math.Sqrt((double)(sd2 / (index))), 2) * 2;
                    topBB = middleBB + sd;
                    botBB = middleBB - sd;
                    pTopbb = pMiddleBB + sd1;
                    pBotbb = pMiddleBB - sd1;
                    ppTopbb = ppMiddleBB + sd2;
                    ppBotbb = ppMiddleBB - sd2;
                }
                instruments[token].history = history;
                //instruments[token].isSpiking = false;
                //instruments[token].threeRise = false;
                instruments[token].isGearingUp = false;
                decimal timeNow = history[history.Count - 1].TimeStamp.Hour + ((decimal)(history[history.Count - 1].TimeStamp.Minute) / 100);
                decimal variance15 = Math.Round((history[history.Count - 2].Close * (decimal)1.5) / 100, 1);
                if ((botBB + variance15) >= topBB)
                    instruments[token].isNarrowed = 3;
                else if (instruments[token].isNarrowed > 0)
                    instruments[token].isNarrowed--;
                else
                    instruments[token].isNarrowed = 0;

                if(instruments[token].status == Status.POSITION)
                {
                    if (DateTime.Now.Hour >= 14)
                    {
                        Position pos = new Position();
                        if (GetCurrentPNL(instruments[token].futId, ref pos))
                            ModifyOrderForContract(pos, token, 1600);
                    }
                }
                decimal range = Convert.ToDecimal(((middleBB * (decimal).7) / 100).ToString("#.#"));
                decimal minRange = Convert.ToDecimal(((middleBB * (decimal).3) / 100).ToString("#.#"));
                    
                index = 0;
                counter = 0;
                middle30BB = 0;
                validation0BB = 0;
                try
                {
                    System.Threading.Thread.Sleep(sleepTime);
                    history = kite.GetHistoricalData(token.ToString(),
                        previousDay.AddDays(-4),
                        currentDay,
                        "30minute");
                    while (history.Count < 50)
                    {
                        System.Threading.Thread.Sleep(sleepTime);
                        previousDay = previousDay.AddDays(-1);
                        history = kite.GetHistoricalData(token.ToString(),
                            previousDay.AddDays(-4),
                            currentDay,
                            "30minute");
                        //Console.WriteLine("History Candles are lesser than Expceted candles. Please Check the given dates. PreviousDate {0} CurrentDate {1} with latest candles count {2}", previousDay.AddDays(-5), currentDay, history.Count);
                        //return;
                    }
                }
                catch (Exception ex)
                {
                    if (ex.Message.Contains("Too many requests during requesting 30 Min candle"))
                    {
                        System.Threading.Thread.Sleep(1000);
                        history = kite.GetHistoricalData(token.ToString(),
                                        previousDay.AddDays(-4),
                                        currentDay, "30minute");
                    }
                    else
                    {
                        Console.WriteLine("TIME OUT EXCEPTION. Please Check message {0} corresponding to the given dates. PreviousDate {1} CurrentDate {2}", ex.Message, previousDay, currentDay);
                        return;
                    }
                }
                if (history.Count >= 50)
                {
                    //foreach (tempHistory candle in history)
                    for (counter = history.Count - 2; counter > 0; counter--)
                    {
                        if (history[counter].TimeStamp.Hour == 8 && history[counter].TimeStamp.Minute == 45)
                        {
                            //Do Nothing
                        }
                        else
                        {
                            validation0BB += history[counter].Close;
                            index++;
                            if (index == 20)
                                break;
                        }
                    }
                    validation0BB = Math.Round(validation0BB / 20, 2);
                    index = 0;
                    for (counter = history.Count - 1; counter > 0; counter--)
                    {
                        if (history[counter].TimeStamp.Hour == 8 && history[counter].TimeStamp.Minute == 45)
                        {
                            //Do Nothing
                        }
                        else
                        {
                            middle30BB += history[counter].Close;
                            index++;
                            if (index == 20)
                                break;
                        }
                    }
                    middle30BB = Math.Round(middle30BB / 20, 2);
                    index = 0;
                    for (counter = history.Count - 1; counter > 0; counter--)
                    //foreach (tempHistory candle in history)
                    {
                        if (history[counter].TimeStamp.Hour == 8 && history[counter].TimeStamp.Minute == 45)
                        {
                            //Do Nothing
                        }
                        else
                        {
                            dayma50 += history[counter].Close;
                            index++;
                            if (index == 50)
                                break;
                        }
                    }
                    dayma50 = Math.Round(dayma50 / 50, 2);
                    index = 0;
                    decimal sd = 0;
                    for (counter = history.Count - 1; counter > 0; counter--)
                    //foreach (tempHistory candle in history)
                    {
                        if (history[counter].TimeStamp.Hour == 8 && history[counter].TimeStamp.Minute == 45)
                        {
                            //Do Nothing
                        }
                        else
                        {
                            sd = (middle30BB - history[counter].Close) * (middle30BB - history[counter].Close) + sd;
                            index++;
                            if (index == 20)
                                break;
                        }
                    }
                    sd = Math.Round((decimal)Math.Sqrt((double)(sd / (20))), 2) * 2;

                    instruments[token].history30Min = history;
                    instruments[token].topBB = topBB;
                    instruments[token].botBB = botBB;
                    instruments[token].middleBB = middleBB;
                    instruments[token].ma50 = ma50;
                    instruments[token].top30bb = middle30BB + sd;
                    instruments[token].middle30BB = validation0BB;
                    instruments[token].middle30BBnew = middle30BB;
                    instruments[token].bot30bb = middle30BB - sd;
                    instruments[token].middle30ma50 = dayma50;
                    instruments[token].currentTime = DateTime.Now;
                    instruments[token].lastRun5Min = DateTime.Now;
                    instruments[token].isDoingAgain = 0;
                    instruments[token].isDoItAgain = 0;
                    //instruments[token].shortTrigger = middle30BB + sd;
                    //instruments[token].longTrigger = middle30BB - sd;
                    if (instruments[token].type == OType.Buy && !instruments[token].isReversed) // && instruments[token].status == Status.OPEN)
                    {
                        instruments[token].shortTrigger = middle30BB + sd;
                        if ((middle30BB - sd) < instruments[token].weekMA)
                            instruments[token].longTrigger = (middle30BB - sd);
                        else
                            instruments[token].longTrigger = instruments[token].weekMA;
                        if (!instruments[token].canTrust)
                            instruments[token].longTrigger = middle30BB - sd;
                    }
                    else if (instruments[token].type == OType.Sell && !instruments[token].isReversed) // && instruments[token].status == Status.OPEN)
                    {
                        instruments[token].longTrigger = middle30BB - sd;
                        if (instruments[token].weekMA <= (middle30BB + sd))
                            instruments[token].shortTrigger = (middle30BB + sd);
                        else
                            instruments[token].shortTrigger = instruments[token].weekMA;
                        if (!instruments[token].canTrust)
                            instruments[token].shortTrigger = middle30BB + sd;
                    }
                    else if (instruments[token].type == OType.Buy && instruments[token].isReversed)
                    {
                        instruments[token].shortTrigger = middle30BB + sd;
                        if (instruments[token].weekMA != instruments[token].longTrigger)
                            instruments[token].longTrigger = middle30BB;
                        else
                        {
                            if (instruments[token].weekMA > middle30BB
                                && IsBeyondVariance(instruments[token].weekMA, instruments[token].middle30BB, (decimal).0025))
                                instruments[token].longTrigger = middle30BB;
                            else
                                instruments[token].longTrigger = instruments[token].weekMA;
                        }
                    }
                    else if (instruments[token].type == OType.Sell && instruments[token].isReversed) // && instruments[token].status == Status.OPEN)
                    {
                        instruments[token].longTrigger = middle30BB - sd;
                        if (instruments[token].weekMA != instruments[token].shortTrigger)
                            instruments[token].shortTrigger = middle30BB;
                        else
                        {
                            if (instruments[token].weekMA < middle30BB
                                && IsBeyondVariance(instruments[token].weekMA, instruments[token].middle30BB, (decimal).0025))
                                instruments[token].shortTrigger = middle30BB;
                            else
                                instruments[token].shortTrigger = instruments[token].weekMA;
                        }
                    }
                        
                    //Console.WriteLine("Calculation Completed for {0}: Botbb {1}; Topbb {2}; MiddleBB {3}; Ma50 {4}; Top30BB {5}; Bot30BB {6}; Middle30BB {7}; stochistic {8}"
                    //    , instruments[token].futName, instruments[token].botBB, instruments[token].topBB, instruments[token].middleBB, instruments[token].ma50, instruments[token].top30bb, instruments[token].bot30bb, instruments[token].middle30BB, instruments[token].stochistic);

                }
                //Console.WriteLine("Calculation Completed at Time Ticker {0}:", DateTime.Now.ToString());
            }
            catch (Exception ex)
            {
                Console.WriteLine("EXCEPTION:: '3minute Ticker Event' {0} for Script Name {1} at {2}", ex.Message, instruments[token].futName, DateTime.Now.ToString("yyyyMMdd hh:mm:ss"));
                //if (ex.Message.Contains("Too many requests"))
                //    sleepTime += 200;
                instruments[token].topBB = 0;
                instruments[token].botBB = 0;
                instruments[token].middleBB = 0;
                instruments[token].ma50 = 0;
                instruments[token].middle30BB = 0;
                instruments[token].middle30BBnew = 0;
                instruments[token].middle30ma50 = 0;
                //instruments[token].shortTrigger = 0;
                //instruments[token].longTrigger = 0;
            }
        }

        private void OnTick(Tick tickData)
        {
            uint instrument = tickData.InstrumentToken;
            decimal ltp = tickData.LastPrice;
            decimal timenow = (decimal)DateTime.Now.Hour + ((decimal)DateTime.Now.Minute / 100);
            if (((Decimal.Compare(timenow, (decimal)14.22) < 0 || Decimal.Compare(timenow, (decimal)9.14) > 0)
                    && instruments[instrument].status != Status.CLOSE
                    && startTicking)
                || instruments[instrument].status == Status.POSITION)
            {
                bool noOpenOrder = true;
                if (instruments[instrument].futName.Contains("NIFTY"))
                {
                    if (!(instruments[instrument].futName.Contains("BANK")
                        || instruments[instrument].futName.Contains("FIN"))) // && !isNiftyVolatile
                    {
                        VerifyNifty(instrument, ltp, tickData.Close, timenow);
                    }
                    return;
                }
                
                if (VerifyLtp(tickData))
                {
                    #region Return if Bolinger is narrowed or expanded
                    decimal spikeVM = ltp < instruments[instrument].middleBB ? Math.Round((ltp * (decimal).02), 1) : Math.Round((instruments[instrument].middleBB * (decimal).02), 1);
                    decimal spikeNN = ltp > instruments[instrument].middleBB ? Math.Round((ltp * (decimal).011), 1) : Math.Round((instruments[instrument].middleBB * (decimal).011), 1);
                    if (!instruments[instrument].isReversed)
                    {
                        decimal variance14 = (ltp * (decimal)1.4) / 100;
                        if ((instruments[instrument].bot30bb + variance14) > instruments[instrument].top30bb)
                        {
                            if (instruments[instrument].oldTime != instruments[instrument].currentTime)
                            {
                                instruments[instrument].oldTime = instruments[instrument].currentTime;
                                Console.WriteLine("Current Time is {0} and Closing Script {1} as the script has Narrowed so much and making it riskier where {2} > {3}",
                                    DateTime.Now.ToString(),
                                    instruments[instrument].futName,
                                    instruments[instrument].bot30bb + variance14,
                                    instruments[instrument].top30bb);
                            }

                            if (!instruments[instrument].canTrust)
                            {
                                if ((instruments[instrument].type == OType.Sell
                                        && instruments[instrument].middle30ma50 > instruments[instrument].bot30bb)
                                    || (instruments[instrument].type == OType.Buy
                                        && instruments[instrument].middle30ma50 < instruments[instrument].top30bb))
                                {
                                    CloseOrderTicker(instrument, true);
                                }
                            }
                            return;
                        }

                        decimal variance46 = (ltp * (decimal)4.6) / 100;
                        if ((instruments[instrument].bot30bb + variance46) < instruments[instrument].top30bb
                            && Decimal.Compare(timenow, Convert.ToDecimal(9.45)) > 0)
                        {
                            if (instruments[instrument].oldTime != instruments[instrument].currentTime)
                            {
                                instruments[instrument].oldTime = instruments[instrument].currentTime;
                                Console.WriteLine("Current Time is {0} and Closing Script {1} as the script has Expanded so much and making it riskier where {2} < {3}",
                                    DateTime.Now.ToString(),
                                    instruments[instrument].futName,
                                    instruments[instrument].bot30bb + variance46,
                                    instruments[instrument].top30bb);
                            }
                            if (instruments[instrument].canTrust
                                && IsBetweenVariance(ltp, instruments[instrument].middleBB, (decimal).0012))
                                return;
                            /*if (!instruments[instrument].canTrust)
                            {
                                CloseOrderTicker(instrument);
                            }*/
                            //return;
                        }
                    }
                    #endregion
                    List<Order> listOrder = kite.GetOrders();
                    //PositionResponse pr = kite.GetPositions();

                    for (int j = listOrder.Count - 1; j >= 0; j--)
                    {
                        Order order = listOrder[j];
                        //Console.WriteLine("ORDER Details {0}", order.InstrumentToken);
                        if (order.InstrumentToken == instruments[instrument].futId && (order.Status == "COMPLETE" || order.Status == "OPEN" || order.Status == "REJECTED"))
                        {
                            if (order.Status == "COMPLETE" || order.Status == "OPEN")
                            {
                                noOpenOrder = false;
                                break;
                            }
                            else if (order.Status == "REJECTED")
                            {
                                noOpenOrder = false;
                                if (order.OrderTimestamp != null)
                                {
                                    DateTime dt = Convert.ToDateTime(order.OrderTimestamp);
                                    if (DateTime.Now > dt.AddMinutes(30) && order.StatusMessage.Contains("Insufficient funds"))
                                    {
                                        //noOpenOrder = true;
                                        Console.WriteLine("Earlier REJECTED DUE TO {0}. Hence proceeding again to verify after 30 minutes", order.StatusMessage);
                                        CloseOrderTicker(instrument, true);
                                        InsertNewToken(instrument, instruments[instrument].futName, ltp, 0, instruments[instrument].middleBB, "BUY", "CLOSED", instruments[instrument].type);
                                        break;
                                    }
                                    else if (DateTime.Now < dt.AddMinutes(30))
                                    {
                                        break;
                                    }
                                }
                                Console.WriteLine("Already REJECTED DUE TO {0}", order.StatusMessage);
                                if (order.StatusMessage.Contains("This instrument is blocked to avoid compulsory physical delivery")
                                    || order.StatusMessage.Contains("or it has been restricted from trading"))
                                {
                                    CloseOrderTicker(instrument, true);
                                    InsertNewToken(instrument, instruments[instrument].futName, ltp, 0, instruments[instrument].middleBB, "BUY", "CLOSED", instruments[instrument].type);
                                }
                                if (!instruments[instrument].canTrust)
                                {
                                    Console.WriteLine("This script is cannot be trusted and this script is at same place for last 30 mins. hence closing this script");
                                }
                            }
                            else if (order.Status == "CANCELLED")
                            {
                                if (order.OrderTimestamp != null)
                                {
                                    DateTime dt = Convert.ToDateTime(order.OrderTimestamp);
                                    if (DateTime.Now < dt.AddMinutes(30))
                                    {
                                        noOpenOrder = false;
                                        CloseOrderTicker(instrument, true);
                                    }
                                }
                            }
                            else
                                noOpenOrder = false;
                        }
                    }
                    if (noOpenOrder)
                    {
                        if (instruments[instrument].type == OType.Buy)
                        {
                            Console.WriteLine("Time {0} Placing BUY Order of Instrument {1} for LTP {2} as it match long trigger {3} with top30BB {4} & bot30BB {5} based on last run candle {6}", DateTime.Now.ToString(), instruments[instrument].futName, ltp.ToString(), instruments[instrument].longTrigger, instruments[instrument].top30bb, instruments[instrument].bot30bb, instruments[instrument].lastRun5Min);
                        }
                        else if (instruments[instrument].type == OType.Sell)
                        {
                            Console.WriteLine("Time {0} Placing SELL Order of Instrument {1} for LTP {2} as it match Short trigger {3} with top30BB {4} & bot30BB {5} based on last run candle {6}", DateTime.Now.ToString(), instruments[instrument].futName, ltp.ToString(), instruments[instrument].shortTrigger, instruments[instrument].top30bb, instruments[instrument].bot30bb, instruments[instrument].lastRun5Min);
                        }
                        if (DateTime.Now.AddMinutes(-3).AddSeconds(30) > instruments[instrument].lastRun5Min
                            && !instruments[instrument].canTrust)
                        {
                            bool proc = (DateTime.Now.Minute >= 15 && DateTime.Now.Minute <= 19);
                            proc = proc ? proc : (DateTime.Now.Minute >= 45 && DateTime.Now.Minute <= 49);
                            if (instruments[instrument].top30bb - instruments[instrument].bot30bb > (Math.Round(ltp * (decimal).03, 1))
                                && !proc)
                            {
                                Console.WriteLine("Time {0} Not Running the latest 5 minute ticker of the script & proceeding",
                                    DateTime.Now.ToString());
                            }
                            else
                            {
                                Console.WriteLine("Time {0} Run the latest 5 minute ticker of the script ",
                                    DateTime.Now.ToString());
                                //DateTime previousDay;
                                //DateTime currentDay;
                                //getDays(out previousDay, out currentDay);
                                //On5minTick(instrument, previousDay, currentDay.AddDays(1));
                                //instruments[instrument].tries--;
                                //return;
                            }
                        }
                        instruments[instrument].triggerPrice = ltp;
                        Console.WriteLine("Time {0} for Instrument {1} BuyQuantity is {2} and SellQuantity is {3}", DateTime.Now.ToString(), instruments[instrument].futName, tickData.BuyQuantity, tickData.SellQuantity);
                        if (!instruments[instrument].canTrust)
                        {
                            if ((instruments[Convert.ToUInt32(ConfigurationManager.AppSettings["NSENIFTY"])].type ==
                                    OType.StrongBuy
                                    && instruments[instrument].type == OType.Sell)
                                || (instruments[Convert.ToUInt32(ConfigurationManager.AppSettings["NSENIFTY"])].type == OType.StrongSell
                                    && instruments[instrument].type == OType.Buy))
                            {
                                Console.WriteLine("NIFTY is Very Volatile. Better stop this untrusted order! with ltp {0} & Middle {1}", ltp, instruments[instrument].middleBB);
                                //CloseOrderTicker(instrument, true);
                                if (instruments[instrument].type == OType.Buy
                                    && instruments[instrument].middleBB - ltp >= spikeVM)
                                {
                                    //Proceed
                                }
                                else if (instruments[instrument].type == OType.Sell
                                         && ltp - instruments[instrument].middleBB >= spikeVM)
                                {
                                    //Proceed
                                }
                                else if (instruments[instrument].type == OType.Buy
                                    && instruments[instrument].middleBB - ltp >= spikeNN
                                    && IsBetweenVariance(instruments[Convert.ToUInt32(ConfigurationManager.AppSettings["NSENIFTY"])].bot30bb, instruments[Convert.ToUInt32(ConfigurationManager.AppSettings["NSENIFTY"])].fBot30bb, (decimal).001)
                                    && instruments[Convert.ToUInt32(ConfigurationManager.AppSettings["NSENIFTY"])].top30bb - instruments[Convert.ToUInt32(ConfigurationManager.AppSettings["NSENIFTY"])].bot30bb < (instruments[Convert.ToUInt32(ConfigurationManager.AppSettings["NSENIFTY"])].fTop30bb * (decimal).013))
                                {
                                    //Proceed
                                }
                                else if (instruments[instrument].type == OType.Sell
                                         && ltp - instruments[instrument].middleBB >= spikeNN
                                         && IsBetweenVariance(instruments[Convert.ToUInt32(ConfigurationManager.AppSettings["NSENIFTY"])].top30bb, instruments[Convert.ToUInt32(ConfigurationManager.AppSettings["NSENIFTY"])].fTop30bb, (decimal).001)
                                         && instruments[Convert.ToUInt32(ConfigurationManager.AppSettings["NSENIFTY"])].top30bb - instruments[Convert.ToUInt32(ConfigurationManager.AppSettings["NSENIFTY"])].bot30bb < (instruments[Convert.ToUInt32(ConfigurationManager.AppSettings["NSENIFTY"])].fTop30bb * (decimal).013))
                                {
                                    //Proceed
                                }
                                else
                                    return;
                            }
                        }
                        if (instruments[instrument].isSpiking)
                        {
                            Console.WriteLine("This script is Spiking up. Better stop !");
                            //CloseOrderTicker(instrument, true);
                            //return;
                        }
                        if ((instruments[instrument].highLow[0] >= instruments[instrument].dayBollinger[1]
                                || IsBetweenVariance(instruments[instrument].highLow[0], instruments[instrument].dayBollinger[1], (decimal).003))
                            && instruments[instrument].type == OType.Sell)
                        {
                            Console.WriteLine("This script is trending upside for sometime. Refrain from SELL order !");
                            if (ltp - instruments[instrument].middleBB >= spikeVM)
                            {
                                //Proceed
                            }
                            else
                                return;
                        }
                        else if ((instruments[instrument].highLow[1] <= instruments[instrument].dayBollinger[2]
                                || IsBetweenVariance(instruments[instrument].highLow[1], instruments[instrument].dayBollinger[2], (decimal).003))
                            && instruments[instrument].type == OType.Buy)
                        {
                            Console.WriteLine("This script is trending downside for sometime. Refrain from BUY order !");
                            if (instruments[instrument].middleBB - ltp >= spikeVM)
                            {
                                //Proceed
                            }
                            else
                                return;
                        }
                        placeOrder(instrument, tickData.LastPrice, tickData.Close);
                    }
                    else
                    {
                        //instruments[instrument].status = Status.STANDING;
                    }
                }
            }
        }

        private bool VerifyVolume(uint instrument, uint volume, decimal timeNow)  /// Check Volume
        {
            bool flag = true;
            if (Decimal.Compare(timeNow, Convert.ToDecimal(9.20)) < 0)
            {
                if (volume > instruments[instrument].AvgVolume / 6.5)
                    flag = false;
            }
            else if (Decimal.Compare(timeNow, Convert.ToDecimal(9.30)) < 0)
            {
                if (volume > instruments[instrument].AvgVolume / 5.2)
                    flag = false;
            }
            else if (Decimal.Compare(timeNow, Convert.ToDecimal(9.40)) < 0)
            {
                if (volume > instruments[instrument].AvgVolume / 4)
                    flag = false;
            }
            else if (Decimal.Compare(timeNow, Convert.ToDecimal(9.50)) < 0)
            {
                if (volume > instruments[instrument].AvgVolume / 3.5)
                    flag = false;
            }
            else if (Decimal.Compare(timeNow, Convert.ToDecimal(10)) < 0)
            {
                if (volume > instruments[instrument].AvgVolume / 3)
                    flag = false;
            }
            else if (Decimal.Compare(timeNow, Convert.ToDecimal(10.10)) < 0)
            {
                if (volume > instruments[instrument].AvgVolume / 2.75)
                    flag = false;
            }
            else if (Decimal.Compare(timeNow, Convert.ToDecimal(10.25)) < 0)
            {
                if (volume > instruments[instrument].AvgVolume / 2.5)
                    flag = false;
            }
            else if (Decimal.Compare(timeNow, Convert.ToDecimal(10.30)) < 0)
            {
                if (volume > instruments[instrument].AvgVolume / 2.3)
                    flag = false;
            }
            else if (Decimal.Compare(timeNow, Convert.ToDecimal(10.45)) < 0)
            {
                if (volume > instruments[instrument].AvgVolume / 2.15)
                    flag = false;
            }
            else if (Decimal.Compare(timeNow, Convert.ToDecimal(11)) < 0)
            {
                if (volume > instruments[instrument].AvgVolume / 2)
                    flag = false;
            }
            else if (Decimal.Compare(timeNow, Convert.ToDecimal(11.30)) < 0)
            {
                if (volume > instruments[instrument].AvgVolume / 1.8)
                    flag = false;
            }
            else if (Decimal.Compare(timeNow, Convert.ToDecimal(12)) < 0)
            {
                if (volume > instruments[instrument].AvgVolume / 1.6)
                    flag = false;
            }
            else if (Decimal.Compare(timeNow, Convert.ToDecimal(12.30)) < 0)
            {
                if (volume > instruments[instrument].AvgVolume / 1.45)
                    flag = false;
            }
            else if (Decimal.Compare(timeNow, Convert.ToDecimal(13)) < 0)
            {
                if (volume > instruments[instrument].AvgVolume / 1.3)
                    flag = false;
            }
            else
            {
                if (volume > instruments[instrument].AvgVolume / 1.2)
                    flag = false;
            }
            return flag;
        }

        private decimal GetLowVolumePercentage(decimal timeNow)
        {
            return Decimal.Compare(timeNow, Convert.ToDecimal(10.15)) > 0 ?
                                        Decimal.Compare(timeNow, Convert.ToDecimal(10.29)) > 0 ?
                                            Decimal.Compare(timeNow, Convert.ToDecimal(10.59)) > 0 ?
                                                Decimal.Compare(timeNow, Convert.ToDecimal(11.30)) > 0 ?
                                                    Decimal.Compare(timeNow, Convert.ToDecimal(12.30)) > 0 ?
                                                        Decimal.Compare(timeNow, Convert.ToDecimal(13.30)) > 0 ?
                                                            Decimal.Compare(timeNow, Convert.ToDecimal(13.59)) > 0 ? (decimal)1.5 : (decimal)1.65 :
                                                            (decimal)1.85 :
                                                    (decimal)2.1 :
                                                (decimal)2.4 :
                                            (decimal)2.7 :
                                        (decimal)3 :  //3.4
                                    (decimal)3.4; //3.8
        }

        private bool VerifyHighVolume(uint instrument, uint volume, decimal timeNow)  /// Check Volume
        {
            bool flag = false;
            decimal avgVolume;
            if (Decimal.Compare(timeNow, Convert.ToDecimal(9.25)) < 0)
            {
                avgVolume = instruments[instrument].AvgVolume * (decimal).25;
            }
            else if (Decimal.Compare(timeNow, Convert.ToDecimal(9.30)) < 0)
            {
                avgVolume = instruments[instrument].AvgVolume * (decimal).4;
            }
            else if (Decimal.Compare(timeNow, Convert.ToDecimal(9.40)) < 0)
            {
                avgVolume = instruments[instrument].AvgVolume * (decimal).55;
            }
            else if (Decimal.Compare(timeNow, Convert.ToDecimal(9.50)) < 0)
            {
                avgVolume = instruments[instrument].AvgVolume * (decimal).7;
            }
            else if (Decimal.Compare(timeNow, Convert.ToDecimal(10)) < 0)
            {
                avgVolume = instruments[instrument].AvgVolume * (decimal).9;
            }
            else if (Decimal.Compare(timeNow, Convert.ToDecimal(10.15)) < 0)
            {
                avgVolume = instruments[instrument].AvgVolume + instruments[instrument].AvgVolume * (decimal).1;
            }
            else if (Decimal.Compare(timeNow, Convert.ToDecimal(10.30)) < 0)
            {
                avgVolume = instruments[instrument].AvgVolume + instruments[instrument].AvgVolume * (decimal).3;
            }
            else if (Decimal.Compare(timeNow, Convert.ToDecimal(10.45)) < 0)
            {
                avgVolume = instruments[instrument].AvgVolume + instruments[instrument].AvgVolume * (decimal).5;
            }
            else if (Decimal.Compare(timeNow, Convert.ToDecimal(11)) < 0)
            {
                avgVolume = instruments[instrument].AvgVolume + instruments[instrument].AvgVolume * (decimal).7;
            }
            else if (Decimal.Compare(timeNow, Convert.ToDecimal(11.30)) < 0)
            {
                avgVolume = instruments[instrument].AvgVolume + instruments[instrument].AvgVolume * (decimal).9;
            }
            else if (Decimal.Compare(timeNow, Convert.ToDecimal(12)) < 0)
            {
                avgVolume = instruments[instrument].AvgVolume + instruments[instrument].AvgVolume;
            }
            else if (Decimal.Compare(timeNow, Convert.ToDecimal(12.30)) < 0)
            {
                avgVolume = instruments[instrument].AvgVolume + instruments[instrument].AvgVolume * (decimal)1.1;
            }
            else if (Decimal.Compare(timeNow, Convert.ToDecimal(13)) < 0)
            {
                avgVolume = instruments[instrument].AvgVolume + instruments[instrument].AvgVolume * (decimal)1.2;
            }
            else
            {
                avgVolume = instruments[instrument].AvgVolume + instruments[instrument].AvgVolume * (decimal)1.5;
            }
            if (volume > avgVolume)
                flag = true;
            return flag;
        }

        private void CloseOrderTicker(uint instrument, bool isRemove)
        {
            try
            {
                if (instruments[instrument].isHighVolume && !isRemove)
                {
                    Console.WriteLine("At {0} Not shutting down this script {1} from Watchlist as this is a high volume script",
                        DateTime.Now, instruments[instrument].futName);
                }
                else
                {
                    instruments[instrument].canTrust = false;
                    instruments[instrument].status = Status.CLOSE;
                    uint[] toArray = new uint[] {instrument};
                    ticker.UnSubscribe(toArray);
                    if (isRemove && startTicking)
                        instruments.Remove(instrument);
                    modifyOrderInCSV(instrument, instruments[instrument].futName, instruments[instrument].type,
                        Status.CLOSE);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("EXCEPTION while closing ticker {0} with message {1}", instrument, ex.Message);
            }
        }

        public bool VerifyLtp(Tick tickData)
        {
            decimal ltp = (decimal)tickData.LastPrice;
            uint instrument = tickData.InstrumentToken;
            uint volume = tickData.Volume;
            decimal prevCandleClose = instruments[instrument].history.Count < 2 ? ltp : instruments[instrument].history[instruments[instrument].history.Count - 1].Close;
            decimal averagePrice = tickData.AveragePrice;
            decimal high = tickData.High;
            decimal low = tickData.Low;
            decimal open = tickData.Open;
            uint buyQuantity = tickData.BuyQuantity;
            uint sellQuantity = tickData.SellQuantity;
            decimal change = ltp - tickData.Close;
            decimal timenow = (decimal)DateTime.Now.Hour + ((decimal)DateTime.Now.Minute / 100);
            bool qualified = false;
            #region Assess Spike Variane
            decimal variance14 = Math.Round((ltp * (decimal)1.4) / 100, 1);
            decimal variance17 = Math.Round((ltp * (decimal)1.65) / 100, 1);
            decimal variance18 = Math.Round((ltp * (decimal)1.8) / 100, 1);
            decimal variance2 = Math.Round((ltp * (decimal)2) / 100, 1);
            decimal variance23 = Math.Round((ltp * (decimal)2.3) / 100, 1);
            decimal variance25 = Math.Round((ltp * (decimal)2.5) / 100, 1);
            decimal variance43 = Math.Round((ltp * (decimal)4.3) / 100, 1);
            decimal variance46 = Math.Round((ltp * (decimal)4.6) / 100, 1);
            decimal spike2 = Math.Round((ltp * (decimal).0025), 1);
            decimal spike3 = Math.Round((ltp * (decimal).0035), 1);
            decimal spike5 = Math.Round((ltp * (decimal).005), 1);
            decimal spikeA = Math.Round((ltp * (decimal).0075), 1);
            decimal spikeN = Math.Round((ltp * (decimal).009), 1);
            decimal spike = Math.Round((ltp * (decimal).01), 1);
            decimal spikeNN = Math.Round((ltp * (decimal).011), 1);
            decimal spikeNV = Math.Round((ltp * (decimal).013), 1);
            decimal spikeM = Math.Round((ltp * (decimal).0135), 1);
            decimal spikeMM = Math.Round((ltp * (decimal).015), 1);
            decimal spikeMN = Math.Round((ltp * (decimal).0165), 1);
            decimal spikeV = Math.Round((ltp * (decimal).018), 1);
            decimal spikeVM = Math.Round((ltp * (decimal).02), 1);
            decimal spikeVVM = Math.Round((ltp * (decimal).024), 1);
            decimal spikeVMM = Math.Round((ltp * (decimal).03), 1);
            if (month == 0)
            {
                List<Instrument> calcInstruments = kite.GetInstruments(Constants.EXCHANGE_NFO);
                CalculateExpiry(calcInstruments, DateTime.Now.Month, 0, "NFO-FUT");
            }
            //|| instruments[instrument].isVolatile 
            //|| VerifyNifty(timenow) != OType.BS) 
            #endregion

            if (instruments[instrument].status == Status.OPEN
                && instruments[instrument].middleBB > 0
                && instruments[instrument].bot30bb > 0
                && instruments[instrument].middle30BBnew > 0
                && Decimal.Compare(timenow, (decimal)14.24) < 0) //change back to 14.24
            {
                #region Open
                #region High Volume
                if (VerifyHighVolume(instrument, volume, timenow)
                    && Decimal.Compare(timenow, Convert.ToDecimal(9.22)) > 0)
                {
                    if (!instruments[instrument].isHighVolume)
                        instruments[instrument].isHighVolume = true;
                    if (instruments[instrument].OldHDateTime != instruments[instrument].currentTime)
                    {
                        instruments[instrument].OldHDateTime = instruments[instrument].currentTime;
                        Console.WriteLine(
                            "At {0} Cautious 7h Average volume {2} and Current Volume {3} the script {1} is trading with High Volume at ltp {4} with buy : Sell quantities {5}:{6}",
                            DateTime.Now.ToString(), instruments[instrument].futName,
                            instruments[instrument].AvgVolume, volume, ltp, buyQuantity,
                            sellQuantity);
                        if (Decimal.Compare(timenow, Convert.ToDecimal(9.50)) >= 0)
                        {
                            if (CheckRecentStatus(instrument, OType.Buy))
                            {
                                Console.WriteLine(
                                    "At {0} This script {1} is 1c showing high volume signs of immediate surge with ltp {2}",
                                    DateTime.Now, instruments[instrument].futName, ltp);
                            }
                            else if (CheckRecentStatus(instrument, OType.Sell))
                            {
                                Console.WriteLine(
                                    "At {0} This script {1} is 1c showing high volume signs of immediate deflate with ltp {2}",
                                    DateTime.Now, instruments[instrument].futName, ltp);
                            }
                        }
                    }
                    if (Decimal.Compare(timenow, Convert.ToDecimal(9.50)) < 0)
                    {
                        if (prevCandleClose > instruments[instrument].middleBB
                            && instruments[instrument].history[instruments[instrument].history.Count - 1].Open >
                                instruments[instrument].history[instruments[instrument].history.Count - 1].Close
                            && instruments[instrument].history[instruments[instrument].history.Count - 1].Open >
                                instruments[instrument].middleBB
                            && CheckRecentStatus(instrument, OType.Buy)
                            && (IsBetweenVariance(instruments[instrument].middleBB, ltp, (decimal).0004)
                                || ltp <= instruments[instrument].middleBB))
                        {
                            Console.WriteLine(
                                "At {0} Cautious 7hb Average volume {2} and Current Volume {3} is aligning for Rather Long movement for the script {1} ltp {4} with buy : Sell quantities {5}:{6}",
                                DateTime.Now.ToString(), instruments[instrument].futName,
                                instruments[instrument].AvgVolume, volume, ltp, buyQuantity,
                                sellQuantity);
                        }
                        else if (prevCandleClose < instruments[instrument].middleBB
                                 && instruments[instrument].history[instruments[instrument].history.Count - 1].Close <
                                    instruments[instrument].history[instruments[instrument].history.Count - 1].Open
                                 && instruments[instrument].history[instruments[instrument].history.Count - 1].Close <
                                    instruments[instrument].middleBB
                                 && CheckRecentStatus(instrument, OType.Sell)
                                 && (IsBetweenVariance(instruments[instrument].middleBB, ltp, (decimal).0004)
                                  || ltp >= instruments[instrument].middleBB))
                        {
                            Console.WriteLine(
                                "At {0} Cautious 7hs Average volume {2} and Current Volume {3} is aligning for Rather Short movement for the script {1} ltp {4} with buy : Sell quantities {5}:{6}",
                                DateTime.Now.ToString(), instruments[instrument].futName,
                                instruments[instrument].AvgVolume, volume, ltp, buyQuantity,
                                sellQuantity);
                        }
                    }
                    else
                    {
                        decimal topDiff;
                        decimal botDiff;
                        if (CheckRecentStatus(instrument, OType.Buy)
                            && high < instruments[instrument].top30bb + spikeNV)
                        {
                            if (IsBetweenVariance(instruments[instrument].middleBB, ltp, (decimal).0004)
                                || ltp <= instruments[instrument].middleBB)
                            {
                                if (instruments[instrument].top30bb - instruments[instrument].bot30bb < variance2)
                                {
                                    topDiff = instruments[instrument].fTop30bb -
                                              instruments[instrument].top30bb;
                                    botDiff = instruments[instrument].bot30bb -
                                              instruments[instrument].fBot30bb;
                                    if (topDiff > botDiff
                                        && buyQuantity * (decimal)1.1 < sellQuantity)
                                        Console.WriteLine(
                                            "At {0} This script {1} is 1aa showing high volume signs of immediate deflate rather surge with ltp {2}",
                                            DateTime.Now, instruments[instrument].futName, ltp);
                                    else
                                        Console.WriteLine(
                                            "At {0} This script {1} is 1aa showing high2 volume signs of immediate surge rather deflate with ltp {2}",
                                            DateTime.Now, instruments[instrument].futName, ltp);
                                }
                                else
                                {
                                    Console.WriteLine(
                                        "At {0} This script {1} is 1aa showing high volume signs of immediate surge with ltp {2}",
                                        DateTime.Now, instruments[instrument].futName, ltp);
                                    if (Decimal.Compare(timenow, Convert.ToDecimal(10.30)) >= 0)
                                    {
                                        bool cont = false;
                                        List<decimal> bols = GetMiddle30BBOf(instruments[instrument].history, 2);
                                        if (CheckLateStatus(instrument, OType.Buy)
                                            && Math.Round(ltp * (decimal).002, 1) + (instruments[instrument].topBB - instruments[instrument].botBB) >= bols[1] - bols[2])
                                        {
                                            if ((instruments[instrument].top30bb - instruments[instrument].bot30bb >
                                                Math.Round(ltp * (decimal).05))
                                                && (instruments[instrument].topBB - instruments[instrument].botBB >
                                                    Math.Round(ltp * (decimal).04)))
                                                cont = false;
                                            else
                                                cont = true;
                                        }
                                        if (!cont)
                                        {
                                            if (ltp <= instruments[instrument].middleBB
                                                && IsBeyondVariance(ltp, instruments[instrument].middleBB,
                                                    (decimal).006))
                                                cont = true;
                                        }
                                        if (cont)
                                        {
                                            if ((instruments[instrument].bot30bb + variance46) <
                                                instruments[instrument].top30bb
                                                && Decimal.Compare(timenow, Convert.ToDecimal(11.18)) < 0
                                                && IsBetweenVariance(ltp, instruments[instrument].middleBB, spike5))
                                            {
                                                if (instruments[instrument].oldTime != instruments[instrument].currentTime)
                                                {
                                                    instruments[instrument].oldTime = instruments[instrument].currentTime;
                                                    Console.WriteLine("Current Time is {0} and Closing Script {1} as the script2 has Expanded so much and making it riskier where {2} < {3}",
                                                        DateTime.Now.ToString(),
                                                        instruments[instrument].futName,
                                                        instruments[instrument].bot30bb + variance46,
                                                        instruments[instrument].top30bb);
                                                }
                                                cont = false;
                                            }
                                        }
                                        if (cont || (instruments[instrument].isMorning
                                                    && IsBetweenVariance(ltp, instruments[instrument].middleBB, (decimal).0006)
                                                    && IsBeyondVariance(open, high, (decimal).006)
                                                    && CheckGearingStatus(instrument, OType.Buy)
                                                    && instruments[instrument].identified == OType.Buy))
                                        {
                                            instruments[instrument].type = OType.Buy;
                                            instruments[instrument].canTrust = true;
                                            instruments[instrument].isHighVolume = true;
                                            instruments[instrument].isLowVolume = false;
                                            return true;
                                        }
                                    }
                                }
                            }
                        }
                        else if (CheckRecentStatus(instrument, OType.Sell)
                                 && low > instruments[instrument].bot30bb - spikeNV)
                        {
                            if (IsBetweenVariance(instruments[instrument].middleBB, ltp, (decimal).0004)
                                || ltp >= instruments[instrument].middleBB)
                            {
                                if (instruments[instrument].top30bb - instruments[instrument].bot30bb < variance2)
                                {
                                    topDiff = instruments[instrument].fTop30bb -
                                              instruments[instrument].top30bb;
                                    botDiff = instruments[instrument].bot30bb -
                                              instruments[instrument].fBot30bb;
                                    if (topDiff < botDiff
                                        && buyQuantity > sellQuantity * (decimal)1.1)
                                        Console.WriteLine(
                                            "At {0} This script {1} is 1aa showing high volume signs of immediate surge rather deflate with ltp {2}",
                                            DateTime.Now, instruments[instrument].futName, ltp);
                                    else
                                        Console.WriteLine(
                                            "At {0} This script {1} is 1aa showing high2 volume signs of immediate deflate rather surge with ltp {2}",
                                            DateTime.Now, instruments[instrument].futName, ltp);
                                }
                                else
                                {
                                    Console.WriteLine(
                                        "At {0} This script {1} is 1aa showing high volume signs of immediate deflate with ltp {2}",
                                        DateTime.Now, instruments[instrument].futName, ltp);
                                    if (Decimal.Compare(timenow, Convert.ToDecimal(10.30)) >= 0)
                                    {
                                        bool cont = false;
                                        List<decimal> bols = GetMiddle30BBOf(instruments[instrument].history, 1);
                                        if (CheckLateStatus(instrument, OType.Sell)
                                            && Math.Round(ltp * (decimal).002, 1) + (instruments[instrument].topBB - instruments[instrument].botBB) < bols[1] - bols[2])
                                        {
                                            if ((instruments[instrument].top30bb - instruments[instrument].bot30bb >
                                                 Math.Round(ltp * (decimal).05))
                                                && (instruments[instrument].topBB - instruments[instrument].botBB >
                                                    Math.Round(ltp * (decimal).04)))
                                                cont = false;
                                            else
                                            {
                                                cont = true;
                                            }
                                        }
                                        if (!cont)
                                        {
                                            if (ltp >= instruments[instrument].middleBB
                                                && IsBeyondVariance(ltp, instruments[instrument].middleBB,
                                                    (decimal).006))
                                                cont = true;
                                        }
                                        if (cont)
                                        {
                                            if ((instruments[instrument].bot30bb + variance46) <
                                                instruments[instrument].top30bb
                                                && Decimal.Compare(timenow, Convert.ToDecimal(11.18)) < 0
                                                && IsBetweenVariance(ltp, instruments[instrument].middleBB, spike5))
                                            {
                                                if (instruments[instrument].oldTime != instruments[instrument].currentTime)
                                                {
                                                    instruments[instrument].oldTime = instruments[instrument].currentTime;
                                                    Console.WriteLine("Current Time is {0} and Closing Script {1} as the script3 has Expanded so much and making it riskier where {2} < {3}",
                                                        DateTime.Now.ToString(),
                                                        instruments[instrument].futName,
                                                        instruments[instrument].bot30bb + variance46,
                                                        instruments[instrument].top30bb);
                                                }
                                                cont = false;
                                            }
                                        }
                                        if (cont || (instruments[instrument].isMorning
                                                    && IsBetweenVariance(ltp, instruments[instrument].middleBB, (decimal).0006)
                                                    && IsBeyondVariance(open, low, (decimal).006)
                                                    && CheckGearingStatus(instrument, OType.Sell)
                                                    && instruments[instrument].identified == OType.Sell))
                                        {
                                            instruments[instrument].type = OType.Sell;
                                            instruments[instrument].canTrust = true;
                                            instruments[instrument].isHighVolume = true;
                                            instruments[instrument].isLowVolume = false;
                                            return true;
                                        }
                                    }
                                }
                            }
                            else if (IsBetweenVariance(instruments[instrument].middleBB, ltp, (decimal).0012)
                                     && CheckBollingerMovement(instrument))
                            {
                                if (CheckRecentStatus(instrument, OType.Buy))
                                {
                                    Console.WriteLine(
                                        "At {0} This script {1} is 1bb showing high volume signs of immediate surge with ltp {2}",
                                        DateTime.Now, instruments[instrument].futName, ltp);
                                    instruments[instrument].type = OType.Buy;
                                    instruments[instrument].canTrust = true;
                                    instruments[instrument].isHighVolume = true;
                                    instruments[instrument].isLowVolume = false;
                                    return true;
                                }
                                else if (CheckRecentStatus(instrument, OType.Sell))
                                {
                                    Console.WriteLine(
                                        "At {0} This script {1} is 1bb showing high volume signs of immediate deflate with ltp {2}",
                                        DateTime.Now, instruments[instrument].futName, ltp);
                                    instruments[instrument].type = OType.Sell;
                                    instruments[instrument].canTrust = true;
                                    instruments[instrument].isHighVolume = true;
                                    instruments[instrument].isLowVolume = false;
                                    return true;
                                }
                            }
                        }
                    }
                }
                else
                {
                    if (DateTime.Now >= instruments[instrument].OldHDateTime.AddMinutes(18)
                        && DateTime.Now <= instruments[instrument].OldHDateTime.AddMinutes(33)
                        && instruments[instrument].isHighVolume)
                    {
                        if (CheckRecentStatus(instrument, OType.Sell)
                            && Decimal.Compare(timenow, Convert.ToDecimal(10.17)) > 0
                            && low <= instruments[instrument].fBot30bb)
                        {
                            Console.WriteLine(
                                "At {0} This script {1} is No More showing high volume signs with ltp {2}. Go for BUY order now",
                                DateTime.Now, instruments[instrument].futName, ltp);
                            instruments[instrument].type = OType.Buy;
                            instruments[instrument].canTrust = true;
                            instruments[instrument].isLowVolume = false;
                            instruments[instrument].isHighVolume = false;
                            instruments[instrument].canOrder = true;
                            modifyOpenAlignOrReversedStatus(instruments[instrument], 16, OType.Buy, false);
                            qualified = true;
                            return qualified;
                        }
                        else if (CheckRecentStatus(instrument, OType.Buy)
                                 && Decimal.Compare(timenow, Convert.ToDecimal(10.17)) > 0
                                 && high >= instruments[instrument].fTop30bb)
                        {
                            Console.WriteLine(
                                "At {0} This script {1} is No More showing high volume signs with ltp {2}. Go for SELL order now",
                                DateTime.Now, instruments[instrument].futName, ltp);
                            instruments[instrument].type = OType.Sell;
                            instruments[instrument].canTrust = true;
                            instruments[instrument].isLowVolume = false;
                            instruments[instrument].isHighVolume = false;
                            instruments[instrument].canOrder = true;
                            modifyOpenAlignOrReversedStatus(instruments[instrument], 16, OType.Sell, false);
                            qualified = true;
                            return qualified;
                        }
                        else
                            Console.WriteLine(
                                "At {0} This script {1} is No More showing high volume signs with ltp {2}",
                                DateTime.Now, instruments[instrument].futName, ltp);
                    }
                    else
                        instruments[instrument].isHighVolume = false;
                }
                #endregion
                if (ltp >= instruments[instrument].top30bb
                    && instruments[instrument].bot30bb + variance25 < instruments[instrument].top30bb
                    && Decimal.Compare(timenow, Convert.ToDecimal(10.17)) < 0)
                {
                    #region Top 30BB
                    if ((((ltp - instruments[instrument].middleBB) > spikeVVM
                          || IsBetweenVariance((ltp - instruments[instrument].middleBB), spikeVVM, (decimal).0006))
                         && !isNiftyVolatile
                         && !instruments[instrument].isVolatile)
                        || ((ltp - instruments[instrument].middleBB) > spikeVMM
                            || IsBetweenVariance((ltp - instruments[instrument].middleBB), spikeVMM, (decimal).0006)))
                    {
                        if (instruments[instrument].oldTime != instruments[instrument].currentTime)
                        {
                            instruments[instrument].oldTime = instruments[instrument].currentTime;
                            if (VerifyVolume(instrument, volume, timenow))
                            {
                                if (instruments[instrument].type == OType.Buy
                                    && (ltp - instruments[instrument].top30bb < spikeN
                                        || (instruments[instrument].canTrust
                                            && (ltp - instruments[instrument].middleBB) < spikeVMM + spikeA)))
                                    Console.WriteLine(
                                        "At {0} Cautious 7b Average volume {2} and Current Volume {3} is aligning for Short call for the script {1} ltp {4}",
                                        DateTime.Now.ToString(), instruments[instrument].futName,
                                        instruments[instrument].AvgVolume, volume, ltp);
                                else
                                    Console.WriteLine(
                                        "At {0} Cautious 7a Average volume {2} and Current Volume {3} is aligning for Short call for the script {1} ltp {4}",
                                        DateTime.Now.ToString(), instruments[instrument].futName,
                                        instruments[instrument].AvgVolume, volume, ltp);
                            }
                            else
                                Console.WriteLine(
                                    "At {0} Cautious 7 Average volume {2} and Current Volume {3} is aligning for Short call for the script {1} ltp {4}",
                                    DateTime.Now.ToString(), instruments[instrument].futName,
                                    instruments[instrument].AvgVolume, volume, ltp);
                        }
                    }
                    #endregion
                }
                else if (ltp <= instruments[instrument].bot30bb
                    && instruments[instrument].bot30bb + variance25 < instruments[instrument].top30bb
                    && Decimal.Compare(timenow, Convert.ToDecimal(10.17)) < 0)
                {
                    #region Bot 30BB
                    if ((((instruments[instrument].middleBB - ltp) > spikeVVM
                             || IsBetweenVariance((instruments[instrument].middleBB - ltp), spikeVVM, (decimal).0006))
                            && !isNiftyVolatile
                            && !instruments[instrument].isVolatile)
                        || ((instruments[instrument].middleBB - ltp) > spikeVMM
                            || IsBetweenVariance((instruments[instrument].middleBB - ltp), spikeVMM, (decimal).0006)))
                    {
                        if (instruments[instrument].oldTime != instruments[instrument].currentTime)
                        {
                            instruments[instrument].oldTime = instruments[instrument].currentTime;
                            if (VerifyVolume(instrument, volume, timenow))
                            {
                                if (instruments[instrument].type == OType.Sell
                                    && (instruments[instrument].bot30bb - ltp < spikeN
                                        || (instruments[instrument].canTrust
                                            && (instruments[instrument].middleBB - ltp) < spikeVMM + spikeA)))
                                    Console.WriteLine(
                                        "At {0} Cautious 7b Average volume {2} and Current Volume {3} is aligning for Long call for the script {1} ltp {4}",
                                        DateTime.Now.ToString(), instruments[instrument].futName,
                                        instruments[instrument].AvgVolume, volume, ltp);
                                else
                                    Console.WriteLine(
                                        "At {0} Cautious 7a Average volume {2} and Current Volume {3} is aligning for Long call for the script {1} ltp {4}",
                                        DateTime.Now.ToString(), instruments[instrument].futName,
                                        instruments[instrument].AvgVolume, volume, ltp);
                            }
                            else
                                Console.WriteLine(
                                    "At {0} Cautious 7 Average volume {2} and Current Volume {3} is aligning for Long call for the script {1} ltp {4}",
                                    DateTime.Now.ToString(), instruments[instrument].futName,
                                    instruments[instrument].AvgVolume, volume, ltp);

                        }
                    }
                    #endregion
                }
                bool flag = Decimal.Compare(timenow, Convert.ToDecimal(9.45)) < 0
                                || (Decimal.Compare(timenow, Convert.ToDecimal(10.17)) < 0 && isNiftyVolatile)
                                //|| DateTime.Now.Minute == 43 || DateTime.Now.Minute == 13
                                || (DateTime.Now.Minute == 44 && DateTime.Now.Second >= 10)
                                || (DateTime.Now.Minute == 14 && DateTime.Now.Second >= 10)
                                || (DateTime.Now.Minute == 45 && DateTime.Now.Second <= 20)
                                || (DateTime.Now.Minute == 15 && DateTime.Now.Second <= 20)
                                ? true : false;

                if (!flag && 
                    (instruments[instrument].history[instruments[instrument].history.Count - 1].Close > instruments[instrument].topBB
                        || instruments[instrument].history[instruments[instrument].history.Count - 1].Close < instruments[instrument].botBB))
                {
                    if ((DateTime.Now.Minute > 15 && DateTime.Now.Minute < 20)
                        || (DateTime.Now.Minute > 45 && DateTime.Now.Minute < 50))
                    {
                        flag = true;
                    }
                }

                if (flag)
                    return false;

                int candles = Decimal.Compare(timenow, Convert.ToDecimal(10.30)) > 0 ?
                            Decimal.Compare(timenow, Convert.ToDecimal(11)) > 0 ?
                                Decimal.Compare(timenow, Convert.ToDecimal(11.55)) > 0 ?
                                    Decimal.Compare(timenow, Convert.ToDecimal(12.25)) > 0 ?
                                        Decimal.Compare(timenow, Convert.ToDecimal(12.55)) > 0 ?
                                            Decimal.Compare(timenow, Convert.ToDecimal(13.25)) > 0 ?
                                                    14 :
                                                12 :
                                            10 :
                                        5 :
                                    4 :
                                3 :
                            2;
                int count = Get5MinCandleCount(DateTime.Now);
                int candleCount = 0;
                bool gearingStatus = false;
                bool isGearingStatus = false;
                int var1 = 0, var2 = 0;

                if (prevCandleClose > instruments[instrument].middleBB
                    && Decimal.Compare(timenow, (decimal)10.45) > 0)
                {
                    gearingStatus = CheckGearingStatus(instrument, OType.Buy, ref candleCount);
                    isGearingStatus = CheckGearingStatus(instrument, OType.Buy);
                    CheckGearingStatus(instrument, OType.Buy, ref var1, ref var2);
                    //instruments[instrument].isRising = !instruments[instrument].isRising ? isGearingStatus : instruments[instrument].isRising;
                    if (isGearingStatus && Decimal.Compare(timenow, (decimal)10.58) > 0)
                        InsertNewToken(instrument, instruments[instrument].futName, ltp, change, instruments[instrument].middleBB, "BUY", "OPEN", instruments[instrument].type);
                }
                else if (prevCandleClose < instruments[instrument].middleBB
                    && Decimal.Compare(timenow, (decimal)10.45) > 0)
                {
                    gearingStatus = CheckGearingStatus(instrument, OType.Sell, ref candleCount);
                    isGearingStatus = CheckGearingStatus(instrument, OType.Sell);
                    CheckGearingStatus(instrument, OType.Sell, ref var1, ref var2);
                    //instruments[instrument].isFalling = !instruments[instrument].isFalling ? isGearingStatus : instruments[instrument].isFalling;
                    if (isGearingStatus && Decimal.Compare(timenow, (decimal)10.58) > 0)
                        InsertNewToken(instrument, instruments[instrument].futName, ltp, change, instruments[instrument].middleBB, "SELL", "OPEN", instruments[instrument].type);
                }
                List<decimal> subsequentBollinger = new List<decimal>();
                if (instruments[instrument].goodToGo)
                {
                    if (instruments[instrument].toBuy)
                        subsequentBollinger = GetForecastMiddle30BBOf(instruments[instrument].history30Min, instruments[instrument].middle30BB, OType.Buy);
                    else if (instruments[instrument].toSell)
                        subsequentBollinger = GetForecastMiddle30BBOf(instruments[instrument].history30Min, instruments[instrument].middle30BB, OType.Sell);
                }
                if (instruments[instrument].goodToGo
                    && subsequentBollinger[1] - subsequentBollinger[2] > spikeMN)
                //&& instruments[instrument].top30bb - instruments[instrument].bot30bb < variance43)
                {
                    if (instruments[instrument].toBuy)
                    {
                        #region Good to Buy
                        if (instruments[instrument].middleBB > ltp || IsBetweenVariance(instruments[instrument].middleBB, ltp, (decimal).0006))
                        {
                            decimal second;
                            if (instruments[instrument].ma50 >= instruments[instrument].middleBB
                                || IsBetweenVariance(instruments[instrument].middleBB, instruments[instrument].ma50, (decimal).0008))
                            {
                                second = instruments[instrument].botBB;
                            }
                            else if (instruments[instrument].ma50 < instruments[instrument].botBB
                                || isNiftyVolatile
                                || instruments[instrument].topBB - instruments[instrument].botBB < spikeA
                                || IsBetweenVariance(instruments[instrument].topBB - instruments[instrument].botBB, spikeA, (decimal).0006))
                            {
                                if (instruments[instrument].ma50 < instruments[instrument].botBB
                                    && instruments[instrument].botBB - spike2 > instruments[instrument].ma50
                                    && (instruments[instrument].topBB - instruments[instrument].botBB <= spikeA
                                        || CheckRecentStatus(instrument, OType.Sell)))
                                    second = instruments[instrument].ma50;
                                else
                                {
                                    if (instruments[instrument].ma50 > instruments[instrument].botBB
                                        && IsBetweenVariance(instruments[instrument].top30bb,
                                            instruments[instrument].fTop30bb,
                                            (decimal).001)
                                        && IsBetweenVariance(instruments[instrument].bot30bb,
                                            instruments[instrument].fBot30bb,
                                            (decimal).001)
                                        && instruments[instrument].middleBB - ltp > spikeA)
                                    {
                                        second = instruments[instrument].ma50;
                                    }
                                    else
                                        second = instruments[instrument].botBB;
                                }
                            }
                            else
                                second = instruments[instrument].ma50;
                            if (Math.Round(buyQuantity * 1.2) < sellQuantity)
                            {
                                if (second > instruments[instrument].ma50)
                                    second = instruments[instrument].ma50;
                                else if (second > instruments[instrument].botBB)
                                    second = instruments[instrument].botBB;
                            }
                            if (instruments[instrument].topBB - instruments[instrument].botBB > spikeNN
                                && IsBetweenVariance(instruments[instrument].botBB, instruments[instrument].ma50, (decimal).0025))
                            {
                                second = second + Math.Round(second * (decimal).001, 1);
                            }
                            if (instruments[instrument].fTop30bb < instruments[instrument].top30bb
                                && IsBeyondVariance(instruments[instrument].fTop30bb, instruments[instrument].top30bb, (decimal).001))
                            {
                                second = instruments[instrument].fTop30bb < second || IsBetweenVariance(instruments[instrument].fTop30bb, second, (decimal).0006) ?
                                    second - Math.Round(second * (decimal).003, 1) : second;
                            }
                            if (instruments[instrument].topBB - instruments[instrument].botBB >= spikeA)
                            {
                                if (Math.Round(sellQuantity * 1.4) < buyQuantity)
                                {
                                    // Do Nothing
                                }
                                else
                                {
                                    if (CheckRecentStatus(instrument, OType.Sell)
                                        || !Check1HourStatus(instrument, OType.Buy))
                                    {
                                        if (instruments[instrument].oldTime != instruments[instrument].currentTime)
                                        {
                                            Console.WriteLine(
                                                "At {0} This script {1} is showing low volume signs, but this is almost reversing from the direction to Buy",
                                                DateTime.Now, instruments[instrument].futName);
                                            instruments[instrument].oldTime = instruments[instrument].currentTime;
                                        }
                                        if (instruments[instrument].isReversed
                                            && instruments[instrument].type == OType.Sell)
                                        {
                                            subsequentBollinger = GetForecastMiddle30BBOf(instruments[instrument].history30Min, instruments[instrument].middle30BB, OType.Buy);
                                            if (subsequentBollinger[1] - subsequentBollinger[2] < spikeMM)
                                            {
                                                Console.WriteLine(
                                                "At {0} This script {1} is Closed from Long call as Buy & Sell are {2}:{3}",
                                                DateTime.Now, instruments[instrument].futName, buyQuantity, sellQuantity);
                                                InsertNewToken(instrument, instruments[instrument].futName, ltp, change,
                                                    second, "BUY", "CLOSE", instruments[instrument].type);
                                                instruments[instrument].goodToGo = false;
                                                instruments[instrument].toBuy = false;
                                                instruments[instrument].toSell = false;
                                                return false;
                                            }
                                        }
                                        //return false;
                                    }
                                }
                            }
                            InsertNewToken(instrument, instruments[instrument].futName, ltp, change,
                                second, "BUY", "OPEN", instruments[instrument].type);
                            if (ltp <= second || IsBetweenVariance(ltp, second, (decimal).0006))
                                //&& instruments[instrument].middleBB > instruments[instrument].ma50
                            {
                                if (instruments[instrument].topBB - instruments[instrument].botBB < spike5
                                    || isNiftyVolatile)
                                {
                                    if (IsBetweenVariance(high, instruments[instrument].weekMA, (decimal).0008)
                                        || IsBetweenVariance(high, instruments[instrument].top30bb, (decimal).0008)
                                        || IsBetweenVariance(high, instruments[instrument].middle30BB, (decimal).0008))
                                    {
                                        Console.WriteLine("At {0} Flip1 This script {1} is showing low volume signs of buy, but this is almost reversing from the direction", DateTime.Now, instruments[instrument].futName);
                                        /*
                                        instruments[instrument].goodToGo = false;
                                        instruments[instrument].toBuy = false;
                                        instruments[instrument].toSell = false;
                                        return false;
                                        */
                                    }
                                    if (isNiftyVolatile
                                        && (((prevCandleClose <= instruments[instrument].middle30BB
                                                && high >= instruments[instrument].middle30BB)
                                                || high >= instruments[instrument].top30bb)
                                        || (instruments[instrument].topBB - instruments[instrument].botBB < spikeA
                                            && (high > instruments[instrument].fTop30bb
                                                || IsBetweenVariance(instruments[instrument].fTop30bb, high, (decimal).001)))))
                                    {
                                        Console.WriteLine("At {0} Flip2 This script {1} is showing low volume signs of buy, but this is almost reversing from the direction", DateTime.Now, instruments[instrument].futName);
                                        instruments[instrument].goodToGo = false;
                                        instruments[instrument].toBuy = false;
                                        instruments[instrument].toSell = false;
                                        return false;
                                    }
                                    if (isNiftyVolatile
                                        && prevCandleClose <= instruments[instrument].ma50
                                        && instruments[instrument].topBB - instruments[instrument].botBB < spikeA)
                                    {
                                        Console.WriteLine("At {0} Flip3 This script {1} is showing low volume signs, but this is almost reversing from the direction", DateTime.Now, instruments[instrument].futName);
                                        instruments[instrument].goodToGo = false;
                                        instruments[instrument].toBuy = false;
                                        instruments[instrument].toSell = false;
                                        return false;
                                    }
                                }
                                if (!instruments[instrument].isReversed
                                    && (low <= instruments[instrument].middle30BB
                                        || IsBetweenVariance(low, instruments[instrument].middle30BB, (decimal).001)))
                                {
                                    subsequentBollinger = GetForecastMiddle30BBOf(instruments[instrument].history30Min, instruments[instrument].middle30BB, OType.Buy);
                                    if (subsequentBollinger[1] - subsequentBollinger[2] < spikeMM)
                                    {
                                        Console.WriteLine(
                                        "At {0} This script {1} is 2 Closed from Long call as Buy & Sell are {2}:{3}",
                                        DateTime.Now, instruments[instrument].futName, buyQuantity, sellQuantity);
                                        InsertNewToken(instrument, instruments[instrument].futName, ltp, change,
                                            second, "BUY", "CLOSE", instruments[instrument].type);
                                        instruments[instrument].goodToGo = false;
                                        instruments[instrument].toBuy = false;
                                        instruments[instrument].toSell = false;
                                        return false;
                                    }
                                }
                                if (instruments[instrument].isReversed
                                    || instruments[instrument].top30bb - instruments[instrument].bot30bb >= Math.Round(ltp * (decimal).05, 1))
                                {
                                    Console.WriteLine("At {0} Flip4 This script {1} is showing low volume signs, but this is almost reversing from the direction", DateTime.Now, instruments[instrument].futName);
                                    instruments[instrument].goodToGo = false;
                                    instruments[instrument].toBuy = false;
                                    instruments[instrument].toSell = false;
                                }
                                else
                                {
                                    instruments[instrument].type = OType.Buy;
                                    instruments[instrument].canTrust = true;
                                    if (instruments[instrument].topBB - instruments[instrument].botBB < spikeA)
                                        instruments[instrument].isLowVolume = true;
                                    else
                                        instruments[instrument].isLowVolume = false;
                                    instruments[instrument].canOrder = true;
                                    modifyOpenAlignOrReversedStatus(instruments[instrument], 16, OType.Buy, false);
                                    qualified = true;
                                    Console.WriteLine("At {0} This script {1} is xx showing signs of immediate surge with ltp {2}", DateTime.Now, instruments[instrument].futName, ltp);
                                }
                            }
                        }
                        #endregion
                    }
                    else if (instruments[instrument].toSell)
                    {
                        #region Good To Sell
                        if (instruments[instrument].middleBB < ltp || IsBetweenVariance(instruments[instrument].middleBB, ltp, (decimal).0006))
                        {
                            decimal second;
                            if (instruments[instrument].ma50 <= instruments[instrument].middleBB
                                || IsBetweenVariance(instruments[instrument].middleBB, instruments[instrument].ma50, (decimal).0008))
                            {
                                second = instruments[instrument].topBB;
                            }
                            else if (instruments[instrument].ma50 > instruments[instrument].topBB
                                || isNiftyVolatile
                                || instruments[instrument].topBB - instruments[instrument].botBB < spikeA
                                || IsBetweenVariance(instruments[instrument].topBB - instruments[instrument].botBB, spikeA, (decimal).0006))
                            {
                                if (instruments[instrument].ma50 > instruments[instrument].topBB
                                    && instruments[instrument].topBB + spike2 < instruments[instrument].ma50
                                    && (instruments[instrument].topBB - instruments[instrument].botBB <= spikeA
                                        || CheckRecentStatus(instrument, OType.Buy)))
                                    second = instruments[instrument].ma50;
                                else
                                {
                                    if (instruments[instrument].ma50 < instruments[instrument].topBB
                                        && IsBetweenVariance(instruments[instrument].top30bb,
                                            instruments[instrument].fTop30bb,
                                            (decimal).001)
                                        && IsBetweenVariance(instruments[instrument].bot30bb,
                                            instruments[instrument].fBot30bb,
                                            (decimal).001)
                                        && ltp - instruments[instrument].middleBB >= spikeA)
                                    {
                                        second = instruments[instrument].ma50;
                                    }
                                    else
                                        second = instruments[instrument].topBB;
                                }
                            }
                            else
                                second = instruments[instrument].ma50;
                            if (buyQuantity > Math.Round(sellQuantity * 1.2))
                            {
                                if (second < instruments[instrument].ma50)
                                    second = instruments[instrument].ma50;
                                else if (second < instruments[instrument].topBB)
                                    second = instruments[instrument].topBB;
                            }
                            if (instruments[instrument].topBB - instruments[instrument].botBB > spikeNN
                                && IsBetweenVariance(instruments[instrument].topBB, instruments[instrument].ma50, (decimal).0025))
                            {
                                second -= Math.Round(second * (decimal).001, 1);
                            }
                            if (instruments[instrument].fBot30bb > instruments[instrument].bot30bb
                                && IsBeyondVariance(instruments[instrument].fBot30bb, instruments[instrument].bot30bb, (decimal).001))
                            {
                                second = instruments[instrument].fBot30bb > second || IsBetweenVariance(instruments[instrument].fBot30bb, second, (decimal).0006) ?
                                    second + Math.Round(second * (decimal).003, 1) : second;
                            }
                            if (instruments[instrument].topBB - instruments[instrument].botBB >= spikeA)
                            {
                                if (CheckRecentStatus(instrument, OType.Buy)
                                    || !Check1HourStatus(instrument, OType.Sell))
                                {
                                    if (Math.Round(buyQuantity * 1.4) < sellQuantity)
                                    {
                                        // Do Nothing
                                    }
                                    else
                                    {
                                        if (instruments[instrument].oldTime != instruments[instrument].currentTime)
                                        {
                                            Console.WriteLine(
                                                "At {0} This script {1} is showing low volume signs, but this is almost reversing from the direction to Sell",
                                                DateTime.Now, instruments[instrument].futName);
                                            instruments[instrument].oldTime = instruments[instrument].currentTime;
                                        }
                                        if (instruments[instrument].isReversed
                                            && instruments[instrument].type == OType.Buy)
                                        {
                                            subsequentBollinger = GetForecastMiddle30BBOf(instruments[instrument].history30Min, instruments[instrument].middle30BB, OType.Sell);
                                            if (subsequentBollinger[1] - subsequentBollinger[2] < spikeMM)
                                            {
                                                Console.WriteLine(
                                                "At {0} This script {1} is Closed from Short call as Buy & Sell are {2}:{3}",
                                                DateTime.Now, instruments[instrument].futName, buyQuantity, sellQuantity);
                                                InsertNewToken(instrument, instruments[instrument].futName, ltp, change,
                                                    second, "SELL", "CLOSE", instruments[instrument].type);
                                                instruments[instrument].goodToGo = false;
                                                instruments[instrument].toBuy = false;
                                                instruments[instrument].toSell = false;
                                                return false;
                                            }
                                        }
                                    }
                                    //return false;
                                }
                            }
                            InsertNewToken(instrument, instruments[instrument].futName, ltp, change,
                                second, "SELL", "OPEN", instruments[instrument].type);
                            if (ltp >= second || IsBetweenVariance(ltp, second, (decimal).0006))
                                //&& instruments[instrument].middleBB < instruments[instrument].ma50
                            {
                                if (instruments[instrument].topBB - instruments[instrument].botBB < spike5
                                    || isNiftyVolatile)
                                {
                                    if (IsBetweenVariance(low, instruments[instrument].weekMA, (decimal).0008)
                                        || IsBetweenVariance(low, instruments[instrument].bot30bb, (decimal).0008)
                                        || IsBetweenVariance(low, instruments[instrument].middle30BB, (decimal).0008))
                                    {
                                        Console.WriteLine("At {0} Flip1 This script {1} is showing low volume signs of sell, but this is almost reversing from the direction", DateTime.Now, instruments[instrument].futName);
                                        /*
                                        instruments[instrument].goodToGo = false;
                                        instruments[instrument].toBuy = false;
                                        instruments[instrument].toSell = false;
                                        return false;
                                        */
                                    }
                                    if (isNiftyVolatile
                                        && (((prevCandleClose >= instruments[instrument].middle30BB
                                                && low <= instruments[instrument].middle30BB)
                                                || low <= instruments[instrument].bot30bb)
                                            || (instruments[instrument].topBB - instruments[instrument].botBB < spikeA
                                                && (low < instruments[instrument].fBot30bb
                                                    || IsBetweenVariance(instruments[instrument].fBot30bb, low, (decimal).001)))))
                                    {
                                        Console.WriteLine("At {0} Flip2 This script {1} is showing low volume signs of sell, but this is almost reversing from the direction", DateTime.Now, instruments[instrument].futName);
                                        instruments[instrument].goodToGo = false;
                                        instruments[instrument].toBuy = false;
                                        instruments[instrument].toSell = false;
                                        return false;
                                    }
                                    if (isNiftyVolatile
                                        && prevCandleClose >= instruments[instrument].ma50
                                        && instruments[instrument].topBB - instruments[instrument].botBB < spikeA)
                                    {
                                        Console.WriteLine("At {0} Flip3 This script {1} is showing low volume signs, but this is almost reversing from the direction", DateTime.Now, instruments[instrument].futName);
                                        instruments[instrument].goodToGo = false;
                                        instruments[instrument].toBuy = false;
                                        instruments[instrument].toSell = false;
                                        return false;
                                    }
                                }
                                if (!instruments[instrument].isReversed
                                    && (high >= instruments[instrument].middle30BB
                                        || IsBetweenVariance(high, instruments[instrument].middle30BB, (decimal).001)))
                                {
                                    subsequentBollinger = GetForecastMiddle30BBOf(instruments[instrument].history30Min, instruments[instrument].middle30BB, OType.Sell);
                                    if (subsequentBollinger[1] - subsequentBollinger[2] < spikeMM)
                                    {
                                        Console.WriteLine(
                                        "At {0} This script {1} is Closed from Short call as Buy & Sell are {2}:{3}",
                                        DateTime.Now, instruments[instrument].futName, buyQuantity, sellQuantity);
                                        InsertNewToken(instrument, instruments[instrument].futName, ltp, change,
                                            second, "SELL", "CLOSE", instruments[instrument].type);
                                        instruments[instrument].goodToGo = false;
                                        instruments[instrument].toBuy = false;
                                        instruments[instrument].toSell = false;
                                        return false;
                                    }
                                }
                                if (instruments[instrument].isReversed
                                    || instruments[instrument].top30bb - instruments[instrument].bot30bb >= Math.Round(ltp * (decimal).05, 1))
                                {
                                    Console.WriteLine("At {0} Flip4 This script {1} is showing low volume signs, but this is almost reversing from the direction", DateTime.Now, instruments[instrument].futName);
                                    instruments[instrument].goodToGo = false;
                                    instruments[instrument].toBuy = false;
                                    instruments[instrument].toSell = false;
                                }
                                else
                                {
                                    instruments[instrument].type = OType.Sell;
                                    instruments[instrument].canTrust = true;
                                    instruments[instrument].canOrder = true;
                                    if (instruments[instrument].topBB - instruments[instrument].botBB < spikeA)
                                        instruments[instrument].isLowVolume = true;
                                    else
                                        instruments[instrument].isLowVolume = false;
                                    modifyOpenAlignOrReversedStatus(instruments[instrument], 16, OType.Sell, false);
                                    qualified = true;
                                    Console.WriteLine("At {0} This script {1} is xx showing signs of immediate deflate with ltp {2}", DateTime.Now, instruments[instrument].futName, ltp);
                                }
                            }
                        }
                        #endregion
                    }
                }
                if (instruments[instrument].goodToGo
                    && !qualified)
                {
                    if (instruments[instrument].toBuy
                        && instruments[instrument].topBB - instruments[instrument].botBB <= spike5
                        && instruments[instrument].top30bb - instruments[instrument].bot30bb >= variance18
                        && IsBetweenVariance(ltp, instruments[instrument].botBB, (decimal).0006)
                        && instruments[instrument].fTop30bb - instruments[instrument].fBot30bb <= instruments[instrument].top30bb - instruments[instrument].bot30bb
                        && !(instruments[instrument].dayBollinger[2] < instruments[instrument].highLow[1]
                            || IsBetweenVariance(instruments[instrument].dayBollinger[2], instruments[instrument].highLow[1], (decimal).003)))
                    {
                        Console.WriteLine("At {0} It is a 1 regular trend yesterday. Why not Place a BUY order now for Script {1} at LTP {2}", DateTime.Now.ToString(), instruments[instrument].futName, ltp);
                    }
                    else if (instruments[instrument].toBuy
                        && instruments[instrument].topBB - instruments[instrument].botBB <= spikeA
                        && instruments[instrument].isReversed
                        && instruments[instrument].top30bb - instruments[instrument].bot30bb >= variance2
                        && IsBetweenVariance(ltp, instruments[instrument].botBB, (decimal).0006)
                        && instruments[instrument].fTop30bb - instruments[instrument].fBot30bb <= instruments[instrument].top30bb - instruments[instrument].bot30bb
                        && !(instruments[instrument].dayBollinger[2] < instruments[instrument].highLow[1]
                            || IsBetweenVariance(instruments[instrument].dayBollinger[2], instruments[instrument].highLow[1], (decimal).003)))
                    {
                        Console.WriteLine("At {0} It is a 2 regular trend yesterday. Why not Place a BUY order now for Script {1} at LTP {2}", DateTime.Now.ToString(), instruments[instrument].futName, ltp);
                    }
                    else if (instruments[instrument].toSell
                        && instruments[instrument].topBB - instruments[instrument].botBB <= spike5
                        && instruments[instrument].top30bb - instruments[instrument].bot30bb >= variance18
                        && IsBetweenVariance(ltp, instruments[instrument].topBB, (decimal).0006)
                        && instruments[instrument].fTop30bb - instruments[instrument].fBot30bb <= instruments[instrument].top30bb - instruments[instrument].bot30bb
                        && !(instruments[instrument].dayBollinger[1] >= instruments[instrument].highLow[0]
                            || IsBetweenVariance(instruments[instrument].dayBollinger[1], instruments[instrument].highLow[0], (decimal).003)))
                    {
                        Console.WriteLine("At {0} It is a 1 regular trend yesterday. Why not Place a SELL order now for Script {1} at LTP {2}", DateTime.Now.ToString(), instruments[instrument].futName, ltp);
                    }
                    else if (instruments[instrument].toSell
                        && instruments[instrument].topBB - instruments[instrument].botBB <= spikeA
                        && instruments[instrument].isReversed
                        && instruments[instrument].top30bb - instruments[instrument].bot30bb >= variance2
                        && IsBetweenVariance(ltp, instruments[instrument].topBB, (decimal).0006)
                        && instruments[instrument].fTop30bb - instruments[instrument].fBot30bb <= instruments[instrument].top30bb - instruments[instrument].bot30bb
                        && !(instruments[instrument].dayBollinger[1] >= instruments[instrument].highLow[0]
                            || IsBetweenVariance(instruments[instrument].dayBollinger[1], instruments[instrument].highLow[0], (decimal).003)))
                    {
                        Console.WriteLine("At {0} It is a 2 regular trend yesterday. Why not Place a SELL order now for Script {1} at LTP {2}", DateTime.Now.ToString(), instruments[instrument].futName, ltp);
                    }
                    else if (instruments[instrument].toBuy
                        && instruments[instrument].topBB - instruments[instrument].botBB > spikeA
                        && (instruments[instrument].isReversed
                            || high == low)
                        && instruments[instrument].top30bb - instruments[instrument].bot30bb >= variance18
                        && IsBetweenVariance(ltp, instruments[instrument].middleBB, (decimal).0006)
                        && (instruments[instrument].fTop30bb - instruments[instrument].fBot30bb <= instruments[instrument].top30bb - instruments[instrument].bot30bb
                            || IsBetweenVariance(instruments[instrument].fTop30bb - instruments[instrument].fBot30bb, instruments[instrument].top30bb - instruments[instrument].bot30bb, (decimal).0008))
                        && !(instruments[instrument].dayBollinger[2] < instruments[instrument].highLow[1]
                            || IsBetweenVariance(instruments[instrument].dayBollinger[2], instruments[instrument].highLow[1], (decimal).003)))
                    {
                        if (instruments[instrument].history30Min[instruments[instrument].history30Min.Count - 2].Close >= instruments[instrument].fBot30bb
                            || instruments[instrument].history30Min[instruments[instrument].history30Min.Count - 3].Close >= instruments[instrument].fBot30bb
                            || instruments[instrument].history30Min[instruments[instrument].history30Min.Count - 4].Close >= instruments[instrument].fBot30bb
                            || IsBeyondVariance(instruments[instrument].history30Min[instruments[instrument].history30Min.Count - 2].Close, instruments[instrument].fTop30bb, (decimal).0008)
                            || IsBeyondVariance(instruments[instrument].history30Min[instruments[instrument].history30Min.Count - 3].Close, instruments[instrument].fTop30bb, (decimal).0008)
                            || IsBeyondVariance(instruments[instrument].history30Min[instruments[instrument].history30Min.Count - 4].Close, instruments[instrument].fTop30bb, (decimal).0008))
                        {
                            //Do Nothing
                        }
                        else if (IsBetweenVariance(instruments[instrument].middle30BB, instruments[instrument].middleBB, (decimal).003))
                        {
                            Console.WriteLine("At {0} It is a 3a regular trend yesterday. But wait until it touches Middle30BB for Buy of Script {1} at LTP {2}", DateTime.Now.ToString(), instruments[instrument].futName, ltp);
                        }
                        else
                            Console.WriteLine("At {0} It is a 3 regular trend yesterday. Why not Place a BUY order now for Script {1} at LTP {2}", DateTime.Now.ToString(), instruments[instrument].futName, ltp);
                    }
                    else if (instruments[instrument].toSell
                        && instruments[instrument].topBB - instruments[instrument].botBB > spikeA
                        && (instruments[instrument].isReversed
                            || high == open)
                        && instruments[instrument].top30bb - instruments[instrument].bot30bb >= variance18
                        && IsBetweenVariance(ltp, instruments[instrument].middleBB, (decimal).0006)
                        && (instruments[instrument].fTop30bb - instruments[instrument].fBot30bb <= instruments[instrument].top30bb - instruments[instrument].bot30bb
                            || IsBetweenVariance(instruments[instrument].fTop30bb - instruments[instrument].fBot30bb, instruments[instrument].top30bb - instruments[instrument].bot30bb, (decimal).0008))
                        && !(instruments[instrument].dayBollinger[1] >= instruments[instrument].highLow[0]
                            || IsBetweenVariance(instruments[instrument].dayBollinger[1], instruments[instrument].highLow[0], (decimal).003)))
                    {
                        if (instruments[instrument].history30Min[instruments[instrument].history30Min.Count - 2].Close <= instruments[instrument].fBot30bb
                            || instruments[instrument].history30Min[instruments[instrument].history30Min.Count - 3].Close <= instruments[instrument].fBot30bb
                            || instruments[instrument].history30Min[instruments[instrument].history30Min.Count - 4].Close <= instruments[instrument].fBot30bb
                            || IsBeyondVariance(instruments[instrument].history30Min[instruments[instrument].history30Min.Count - 2].Close, instruments[instrument].fBot30bb, (decimal).0008)
                            || IsBeyondVariance(instruments[instrument].history30Min[instruments[instrument].history30Min.Count - 3].Close, instruments[instrument].fBot30bb, (decimal).0008)
                            || IsBeyondVariance(instruments[instrument].history30Min[instruments[instrument].history30Min.Count - 4].Close, instruments[instrument].fBot30bb, (decimal).0008))
                        {
                            //Do Nothing
                        }
                        else if (IsBetweenVariance(instruments[instrument].middle30BB, instruments[instrument].middleBB, (decimal).003))
                        {
                            Console.WriteLine("At {0} It is a 3a regular trend yesterday. But wait until it touches Middle30BB for Sell of Script {1} at LTP {2}", DateTime.Now.ToString(), instruments[instrument].futName, ltp);
                        }
                        else
                            Console.WriteLine("At {0} It is a 3 regular trend yesterday. Why not Place a SELL order now for Script {1} at LTP {2}", DateTime.Now.ToString(), instruments[instrument].futName, ltp);
                    }
                }
                bool checkAllCandleStatus = false;
                bool checkMa50 = false;
                bool checkLateMa50 = false;
                bool isBetween8Variance = false;
                bool contForGearingStatus = false;
                if (((instruments[instrument].highLow[0] >= instruments[instrument].dayBollinger[1]
                                || IsBetweenVariance(instruments[instrument].highLow[0], instruments[instrument].dayBollinger[1], (decimal).002))
                            && prevCandleClose > instruments[instrument].middleBB)
                        || ((instruments[instrument].highLow[1] <= instruments[instrument].dayBollinger[2]
                                || IsBetweenVariance(instruments[instrument].highLow[1], instruments[instrument].dayBollinger[2], (decimal).002))
                            && prevCandleClose < instruments[instrument].middleBB)
                            || Decimal.Compare(timenow, (decimal)10.57) > 0)
                {
                    contForGearingStatus = true;
                }
                if (((instruments[instrument].highLow[0] >= instruments[instrument].dayBollinger[1]
                                || IsBetweenVariance(instruments[instrument].highLow[0], instruments[instrument].dayBollinger[1], (decimal).002))
                            && (prevCandleClose > instruments[instrument].middleBB
                                || instruments[instrument].toBuy))
                        || ((instruments[instrument].highLow[1] <= instruments[instrument].dayBollinger[2]
                                || IsBetweenVariance(instruments[instrument].highLow[1], instruments[instrument].dayBollinger[2], (decimal).002))
                            && (prevCandleClose < instruments[instrument].middleBB
                                || instruments[instrument].toSell)))
                {
                    if (((prevCandleClose < instruments[instrument].middleBB
                                    && isGearingStatus)
                                || (instruments[instrument].goodToGo
                                    && instruments[instrument].toBuy))
                        && instruments[instrument].top30bb - instruments[instrument].middle30BB >= variance2
                        && IsBeyondVariance(high, open, (decimal).002))
                    {
                        if (CheckRecentStatus(instrument, OType.Sell))
                        {
                            instruments[instrument].goodToGo = false;
                            instruments[instrument].toBuy = false;
                            instruments[instrument].toSell = false;
                            return false;
                        }
                        if (instruments[instrument].topBB - instruments[instrument].middleBB < spikeNN
                            && IsBetweenVariance(ltp, instruments[instrument].botBB, (decimal).002))
                        {
                            Console.WriteLine("At {0} script {1} Previous Day is in uptrend. Buy Now buddy now now now", DateTime.Now.ToString(), instruments[instrument].futName);
                        }
                        else if (IsBetweenVariance(ltp, instruments[instrument].middleBB, (decimal).0006))
                        {
                            Console.WriteLine("At {0} script {1} Previous Day is in uptrend. Buy Now buddy", DateTime.Now.ToString(), instruments[instrument].futName);
                        }
                        instruments[instrument].goodToGo = true;
                        instruments[instrument].toBuy = true;
                        instruments[instrument].toSell = false;
                    }
                    else if (((prevCandleClose < instruments[instrument].middleBB
                                    && isGearingStatus)
                                || (instruments[instrument].goodToGo
                                    && instruments[instrument].toSell))
                        && instruments[instrument].top30bb - instruments[instrument].middle30BB >= variance2
                        && IsBeyondVariance(low, open, (decimal).002))
                    {
                        if (CheckRecentStatus(instrument, OType.Buy))
                        {
                            instruments[instrument].goodToGo = false;
                            instruments[instrument].toBuy = false;
                            instruments[instrument].toSell = false;
                            return false;
                        }
                        if (instruments[instrument].topBB - instruments[instrument].middleBB < spikeNN
                            && IsBetweenVariance(ltp, instruments[instrument].topBB, (decimal).002))
                        {
                            Console.WriteLine("At {0} script {1} Previous Day is in downtrend. Sell Now buddy now now now", DateTime.Now.ToString(), instruments[instrument].futName);
                        }
                        else if (IsBetweenVariance(ltp, instruments[instrument].middleBB, (decimal).0006))
                        {
                            Console.WriteLine("At {0} script {1} Previous Day is in downtrend. Sell Now buddy", DateTime.Now.ToString(), instruments[instrument].futName);
                        }
                        instruments[instrument].goodToGo = true;
                        instruments[instrument].toBuy = false;
                        instruments[instrument].toSell = true;
                    }
                }
                else if (!VerifyVolume(instrument, volume, timenow)
                    && isGearingStatus
                    && contForGearingStatus
                    && instruments[instrument].topBB - instruments[instrument].botBB >= spike)
                {
                    #region HighVolume movement
                    if ((prevCandleClose > instruments[instrument].middleBB
                            || (IsBetweenVariance(prevCandleClose, instruments[instrument].middleBB, (decimal).0012)
                                && instruments[instrument].goodToGo
                                && instruments[instrument].toBuy))
                        && (buyQuantity > Math.Round(sellQuantity * 1.2)
                            || (instruments[instrument].goodToGo
                                && instruments[instrument].toBuy)))
                    {
                        checkAllCandleStatus = CheckAllCandleStatus(instrument, OType.Buy);
                        checkMa50 = CheckMa50(instrument, OType.Buy);
                        if ((checkAllCandleStatus
                                || (instruments[instrument].isReversed && instruments[instrument].type == OType.Buy))
                            && CheckNifty(timenow) != OType.Sell
                            && checkMa50
                            && CheckBollingerExpansion(instrument, OType.Buy, ltp, spikeNN, timenow)
                            //&& var1 + var2 > 12
                            && !(high >= instruments[instrument].top30bb
                                    || IsBetweenVariance(high, instruments[instrument].top30bb, (decimal).0006))
                            && (IsBetweenVariance(instruments[instrument].middleBB, ltp, (decimal).0004)
                                || ltp < instruments[instrument].middleBB))
                        {
                            if (instruments[instrument].oldTime != instruments[instrument].currentTime)
                            {
                                instruments[instrument].oldTime = instruments[instrument].currentTime;
                                Console.WriteLine("At {0} This script {1} is 1 showing positive signs of immediate surge with ltp {2}", DateTime.Now, instruments[instrument].futName, ltp);
                            }
                            instruments[instrument].canTrust = true;
                            instruments[instrument].goodToGo = true;
                            instruments[instrument].toBuy = true;
                            instruments[instrument].toSell = false;
                        }
                        else if (instruments[instrument].isHighVolume)
                        {
                            if (instruments[instrument].topBB - instruments[instrument].botBB >= spike
                                && (IsBetweenVariance(instruments[instrument].middleBB, ltp, (decimal).0004)
                                    || ltp <= instruments[instrument].middleBB)
                                || (IsBetweenVariance(ltp, instruments[instrument].middleBB, (decimal).0012)
                                    && CheckBollingerMovement(instrument)
                                    && instruments[instrument].topBB - instruments[instrument].botBB > spikeM
                                    && VerifyHighVolume(instrument, volume, timenow)))
                            {
                                Console.WriteLine(
                                    "At {0} This script {1} is 1a showing high volume signs of immediate surge with ltp {2}",
                                    DateTime.Now, instruments[instrument].futName, ltp);
                                qualified = true;
                                if (VerifyHighVolume(instrument, volume, timenow))
                                    Console.WriteLine(
                                        "At {0} This script {1} is showing a Very high volume signs of immediate surge with ltp {2}",
                                        DateTime.Now, instruments[instrument].futName, ltp);
                                instruments[instrument].type = OType.Buy;
                            }
                            else
                            {
                                if (instruments[instrument].oldTime != instruments[instrument].currentTime)
                                {
                                    instruments[instrument].oldTime = instruments[instrument].currentTime;
                                    Console.WriteLine(
                                        "At {0} This script {1} is 1b showing high volume signs of immediate surge with ltp {2}",
                                        DateTime.Now, instruments[instrument].futName, ltp);
                                }
                            }
                            instruments[instrument].canTrust = true;
                            instruments[instrument].goodToGo = true;
                            instruments[instrument].toBuy = true;
                            instruments[instrument].toSell = false;
                        }
                    }
                    else if ((prevCandleClose < instruments[instrument].middleBB
                              || (IsBetweenVariance(prevCandleClose, instruments[instrument].middleBB, (decimal).0012)
                                  && instruments[instrument].goodToGo
                                  && instruments[instrument].toSell))
                            && (Math.Round(buyQuantity * 1.2) < sellQuantity
                                || (instruments[instrument].goodToGo
                                    && instruments[instrument].toSell)))
                    {
                        checkAllCandleStatus = CheckAllCandleStatus(instrument, OType.Sell);
                        checkMa50 = CheckMa50(instrument, OType.Sell);
                        if ((checkAllCandleStatus
                                || (instruments[instrument].isReversed && instruments[instrument].type == OType.Buy))
                            && CheckNifty(timenow) != OType.Buy
                            && checkMa50
                            && CheckBollingerExpansion(instrument, OType.Sell, ltp, spikeNN, timenow)
                            //&& var1 + var2 > 12
                            && !(low <= instruments[instrument].bot30bb
                                    || IsBetweenVariance(high, instruments[instrument].bot30bb, (decimal).0006))
                            && (IsBetweenVariance(instruments[instrument].middleBB, ltp, (decimal).0004)
                                || ltp > instruments[instrument].middleBB))
                        {
                            if (instruments[instrument].oldTime != instruments[instrument].currentTime)
                            {
                                instruments[instrument].oldTime = instruments[instrument].currentTime;
                                Console.WriteLine("At {0} This script {1} is 1 showing positive signs of immediate deflate with ltp {2}", DateTime.Now, instruments[instrument].futName, ltp);
                            }
                            instruments[instrument].canTrust = true;
                            instruments[instrument].goodToGo = true;
                            instruments[instrument].toSell = true;
                            instruments[instrument].toBuy = false;
                        }
                        else if (instruments[instrument].isHighVolume)
                        {
                            if (instruments[instrument].topBB - instruments[instrument].botBB >= spike
                                && (IsBetweenVariance(instruments[instrument].middleBB, ltp, (decimal).0004)
                                    || ltp >= instruments[instrument].middleBB)
                                || (IsBetweenVariance(ltp, instruments[instrument].middleBB, (decimal).0012)
                                    && CheckBollingerMovement(instrument)
                                    && instruments[instrument].topBB - instruments[instrument].botBB > spikeM
                                    && VerifyHighVolume(instrument, volume, timenow)))
                            {
                                Console.WriteLine(
                                    "At {0} This script {1} is 1a showing High Volume signs of immediate deflate with ltp {2}",
                                    DateTime.Now, instruments[instrument].futName, ltp);
                                if (VerifyHighVolume(instrument, volume, timenow))
                                    Console.WriteLine(
                                        "At {0} This script {1} is showing a Very high volume signs of immediate deflate with ltp {2}",
                                        DateTime.Now, instruments[instrument].futName, ltp);
                                qualified = true;
                                instruments[instrument].type = OType.Sell;
                            }
                            else
                            {
                                if (instruments[instrument].oldTime != instruments[instrument].currentTime)
                                {
                                    instruments[instrument].oldTime = instruments[instrument].currentTime;
                                    Console.WriteLine(
                                        "At {0} This script {1} is 1b showing High Volume signs of immediate deflate with ltp {2}",
                                        DateTime.Now, instruments[instrument].futName, ltp);
                                }
                            }
                            instruments[instrument].canTrust = true;
                            instruments[instrument].goodToGo = true;
                            instruments[instrument].toSell = true;
                            instruments[instrument].toBuy = false;
                        }
                    }
                    #endregion
                }
                else if ((gearingStatus || isGearingStatus)
                    && contForGearingStatus
                    && instruments[instrument].top30bb - instruments[instrument].bot30bb > variance17)
                {
                    #region Low Volume
                    //if (var1 + var2 > 12)
                    {
                        instruments[instrument].canTrust = true;
                        if (prevCandleClose > instruments[instrument].middleBB
                            || (IsBetweenVariance(prevCandleClose, instruments[instrument].middleBB, (decimal).0015)
                                && instruments[instrument].goodToGo
                                && instruments[instrument].toBuy))
                        {
                            isBetween8Variance = instruments[instrument].middleBB > ltp
                                                    || IsBetweenVariance(instruments[instrument].middleBB, ltp, (decimal).0008);
                            checkAllCandleStatus = CheckAllCandleStatus(instrument, OType.Buy);
                            //checkMa50 = CheckMa50(instrument, OType.Buy);
                            checkLateMa50 = CheckLateMa50(instrument, OType.Buy);
                            if ((IsBetweenVariance(instruments[instrument].middleBB, ltp, (decimal).0004)
                                    || ltp < instruments[instrument].middleBB)
                                && candleCount >= count - candles
                                && checkLateMa50
                                && checkAllCandleStatus
                                && instruments[instrument].topBB - instruments[instrument].botBB >= spikeA
                                && !(high >= instruments[instrument].top30bb
                                    || IsBetweenVariance(high, instruments[instrument].top30bb, (decimal).0006)))
                            {
                                instruments[instrument].goodToGo = true;
                                instruments[instrument].toBuy = true;
                                instruments[instrument].toSell = false;
                                if ((averagePrice <= ltp - spike2
                                        || (ltp < instruments[instrument].middleBB
                                            && IsBetweenVariance(ltp, instruments[instrument].middleBB, (decimal).004)))
                                    && instruments[instrument].topBB - instruments[instrument].botBB >= spike)
                                {
                                    if (instruments[instrument].topBB - instruments[instrument].botBB >= variance14)
                                        Console.WriteLine("At {0} This script {1} is 6x showing low volume signs of immediate Surge with ltp {2} but with averageprice {3}", DateTime.Now, instruments[instrument].futName, ltp, averagePrice);
                                    else
                                        Console.WriteLine("At {0} This script {1} is 6 showing low volume signs of immediate Surge with ltp {2} but with averageprice {3}", DateTime.Now, instruments[instrument].futName, ltp, averagePrice);
                                    if (IsBetweenVariance(ltp, instruments[instrument].close, (decimal).0025)
                                        && (IsBetweenVariance(buyQuantity, sellQuantity, (decimal).002)
                                            || sellQuantity > buyQuantity))
                                    {
                                        Console.WriteLine("WRONG WRONG At {0} This script {1} is 6 showing low volume signs of immediate deflat with ltp {2} but with averageprice {3}", DateTime.Now, instruments[instrument].futName, ltp, averagePrice);
                                    }
                                    else if (((instruments[instrument].middleBB > instruments[instrument].ma50
                                            && IsBetweenVariance(instruments[instrument].middleBB, instruments[instrument].ma50, (decimal).0025))
                                            || (instruments[instrument].botBB > instruments[instrument].ma50
                                                && IsBetweenVariance(instruments[instrument].botBB, instruments[instrument].ma50, (decimal).0025)))
                                        && IsBetweenVariance(ltp, instruments[instrument].middleBB, (decimal).003))
                                    {
                                        //Do Nothing
                                    }
                                    else if (IsBetweenVariance(ltp, instruments[instrument].close, (decimal).015)
                                                && IsBetweenVariance(high, ltp, (decimal).007)
                                                && (IsBetweenVariance(instruments[instrument].top30bb, high, (decimal).0007)
                                                || instruments[instrument].top30bb < high))
                                    {
                                        //Do Nothing
                                    }
                                    else if (instruments[instrument].topBB - instruments[instrument].botBB >= variance14
                                                && instruments[instrument].ma50 > instruments[instrument].botBB
                                                && IsBetweenVariance(ltp, instruments[instrument].middleBB, (decimal).0007))
                                    {
                                        //Do Nothing
                                    }
                                    else if (instruments[instrument].topBB - instruments[instrument].botBB <=
                                                variance14
                                                && (buyQuantity < Math.Round(sellQuantity * 1.1)
                                                || !(high >= instruments[instrument].fTop30bb
                                                        || IsBetweenVariance(high, instruments[instrument].fTop30bb, (decimal).0006))))
                                    {
                                        //Do Nothing
                                    }
                                    else
                                    {
                                        instruments[instrument].type = OType.Buy;
                                        instruments[instrument].canTrust = true;
                                        instruments[instrument].isLowVolume = true;
                                        instruments[instrument].canOrder = true;
                                        modifyOpenAlignOrReversedStatus(instruments[instrument], 16, OType.Buy, false);
                                        qualified = true;
                                    }
                                }
                                else
                                {
                                    if (instruments[instrument].oldTime != instruments[instrument].currentTime)
                                    {
                                        instruments[instrument].oldTime = instruments[instrument].currentTime;
                                        Console.WriteLine("At {0} This script {1} is 10 showing low volume signs of immediate surge with ltp {2} but with averageprice {3}", DateTime.Now, instruments[instrument].futName, ltp, averagePrice);
                                    }
                                }
                            }
                            else if (isGearingStatus)
                            {
                                instruments[instrument].goodToGo = true;
                                instruments[instrument].toBuy = true;
                                instruments[instrument].toSell = false;
                                if (instruments[instrument].oldTime != instruments[instrument].currentTime)
                                {
                                    instruments[instrument].oldTime = instruments[instrument].currentTime;
                                    if (candleCount >= count - candles
                                        && instruments[instrument].isReversed
                                        && instruments[instrument].type == OType.Buy)
                                    {
                                        Console.WriteLine("At {0} This script {1} is 5 showing low volume signs of immediate Surge with ltp {2} but with averageprice {3}", DateTime.Now, instruments[instrument].futName, ltp, averagePrice);
                                    }
                                    else if (candleCount >= count - candles
                                        && gearingStatus)
                                    {
                                        Console.WriteLine("At {0} This script {1} is 4 showing low volume signs of immediate surge with ltp {2} but with averageprice {3}", DateTime.Now, instruments[instrument].futName, ltp, averagePrice);
                                    }
                                    else if (instruments[instrument].isReversed)
                                        Console.WriteLine("At {0} This script {1} is 3x showing low volume signs of immediate surge with ltp {2} but with averageprice {3}", DateTime.Now, instruments[instrument].futName, ltp, averagePrice);
                                    else
                                    {
                                        Console.WriteLine("At {0} This script {1} is 3 showing low volume signs of immediate surge with ltp {2} but with averageprice {3}", DateTime.Now, instruments[instrument].futName, ltp, averagePrice);
                                    }
                                }
                                if (checkLateMa50
                                    && instruments[instrument].topBB - instruments[instrument].botBB > spikeA
                                    && checkAllCandleStatus
                                    && ltp <= instruments[instrument].middleBB)
                                {
                                    if (high > instruments[instrument].top30bb
                                        || IsBetweenVariance(ltp, instruments[instrument].top30bb, (decimal).002))
                                        //IsBeyondVariance(ltp, instruments[instrument].middleBB, (decimal).0012))
                                        Console.WriteLine("At {0} This script {1} is 6yz showing low signs of immediate surge with ltp {2} but with averageprice {3}", DateTime.Now, instruments[instrument].futName, ltp, averagePrice);
                                    else if (high > instruments[instrument].fTop30bb
                                                || IsBetweenVariance(ltp, instruments[instrument].fTop30bb, (decimal).002))
                                        Console.WriteLine("At {0} This script {1} is 6zy showing low signs of immediate surge with ltp {2} but with averageprice {3}", DateTime.Now, instruments[instrument].futName, ltp, averagePrice);
                                    else
                                    {
                                        Console.WriteLine(
                                            "At {0} This script {1} is y6z showing low signs of immediate surge with ltp {2} but with averageprice {3}",
                                            DateTime.Now, instruments[instrument].futName, ltp, averagePrice);
                                        if (instruments[instrument].topBB - instruments[instrument].botBB >= spikeN
                                            && instruments[instrument].history30Min[instruments[instrument].history30Min.Count - 1].Close > instruments[instrument].middle30BBnew
                                            && instruments[instrument].history30Min[instruments[instrument].history30Min.Count - 2].Close > instruments[instrument].middle30BB)
                                        {
                                            instruments[instrument].type = OType.Buy;
                                            instruments[instrument].canTrust = true;
                                            instruments[instrument].isLowVolume = true;
                                            instruments[instrument].canOrder = true;
                                            modifyOpenAlignOrReversedStatus(instruments[instrument], 16, OType.Buy,
                                                false);
                                            qualified = true;
                                        }
                                    }
                                }
                                else
                                {
                                    if (isBetween8Variance
                                            && checkLateMa50
                                            && instruments[instrument].topBB - instruments[instrument].botBB > spikeA)
                                        Console.WriteLine("At {0} This script {1} is 6y showing low signs of immediate surge with ltp {2} but with averageprice {3}", DateTime.Now, instruments[instrument].futName, ltp, averagePrice);
                                    if (isBetween8Variance
                                        && checkAllCandleStatus
                                        && instruments[instrument].topBB - instruments[instrument].botBB >= spikeA)
                                    {
                                        Console.WriteLine("At {0} This script {1} is 6z showing low signs of immediate surge with ltp {2} but with averageprice {3}", DateTime.Now, instruments[instrument].futName, ltp, averagePrice);
                                    }
                                }
                                checkMa50 = CheckMa50(instrument, OType.Buy);
                                List<decimal> subsequentMiddleBB = GetForecastMiddle30BBOf(instruments[instrument].history, instruments[instrument].middleBB, OType.Buy);	
                                bool isnarrowing = subsequentMiddleBB[1] - subsequentMiddleBB[2] > spikeA	
                                        || IsBeyondVariance(subsequentMiddleBB[1] - subsequentMiddleBB[2], instruments[instrument].topBB - instruments[instrument].botBB, (decimal).3);	
                                if (!isnarrowing	
                                    && IsBetweenVariance(subsequentMiddleBB[1] - subsequentMiddleBB[2], instruments[instrument].topBB - instruments[instrument].botBB, (decimal).01))	
                                {	
                                    Console.WriteLine(	
                                            "At {0} This script {1} is 12 showing low signs of immediate surge with ltp {2} but with averageprice {3}",	
                                            DateTime.Now, instruments[instrument].futName, ltp, averagePrice);	
                                }	
                                CheckBollingerExpansion(instrument, OType.Buy, ltp, spikeNN, timenow);
                                if ((averagePrice <= ltp - (spike2 / 2)
                                        || (averagePrice < ltp && checkMa50))
                                    && checkLateMa50
                                    //&& checkMa50
                                    //&& checkAllCandleStatus
                                    && isnarrowing
                                    && (instruments[instrument].topBB - instruments[instrument].botBB >= spikeA
                                        || IsBetweenVariance(instruments[instrument].topBB - instruments[instrument].botBB, spikeA, (decimal).0006))
                                    //&& instruments[instrument].topBB - instruments[instrument].botBB < variance14
                                    && (IsBetweenVariance(instruments[instrument].middleBB, ltp, (decimal).0004)
                                        || ltp < instruments[instrument].middleBB)
                                    && !(high >= instruments[instrument].top30bb
                                        || IsBetweenVariance(high, instruments[instrument].top30bb, (decimal).0006)))
                                {
                                    if (gearingStatus)
                                        Console.WriteLine("At {0} This script {1} is 5y showing low volume signs of immediate surge with ltp {2} but with averageprice {3}", DateTime.Now, instruments[instrument].futName, ltp, averagePrice);
                                    else if (instruments[instrument].topBB - instruments[instrument].botBB <= variance17)
                                        Console.WriteLine("At {0} This script {1} is 5z showing low volume signs of immediate surge with ltp {2} but with averageprice {3}", DateTime.Now, instruments[instrument].futName, ltp, averagePrice);
                                    else
                                        Console.WriteLine("At {0} This script {1} is 5x showing low volume signs of immediate surge with ltp {2} but with averageprice {3}", DateTime.Now, instruments[instrument].futName, ltp, averagePrice);
                                    if (instruments[instrument].middleBB > instruments[instrument].ma50
                                        && (((IsBetweenVariance(instruments[instrument].middleBB, instruments[instrument].ma50, (decimal).0025)
                                                    || instruments[instrument].topBB - instruments[instrument].botBB <= variance14)
                                                    && IsBetweenVariance(ltp, instruments[instrument].middleBB, (decimal).0012))
                                                || (instruments[instrument].botBB > instruments[instrument].ma50
                                                    && instruments[instrument].middle30ma50 <= instruments[instrument].middle30BB
                                                    && instruments[instrument].topBB - instruments[instrument].botBB <= spike
                                                    && IsBetweenVariance(ltp, instruments[instrument].middle30BB, (decimal).0012))
                                                || (!(candleCount >= count - candles)
                                                    && !checkAllCandleStatus
                                                    && buyQuantity < Math.Round(sellQuantity * 1.3))))
                                    {
                                        if (IsBetweenVariance(ltp, instruments[instrument].close, (decimal).0025)
                                            && (IsBetweenVariance(buyQuantity, sellQuantity, (decimal).002)
                                                || sellQuantity > buyQuantity))
                                        {
                                            Console.WriteLine("WRONG WRONG At {0} This script {1} is 5x showing low volume signs of immediate deflat with ltp {2} but with averageprice {3}", DateTime.Now, instruments[instrument].futName, ltp, averagePrice);
                                        }
                                        else if (gearingStatus
                                            && ltp < instruments[instrument].middleBB
                                            && IsBeyondVariance(ltp, instruments[instrument].middleBB,
                                                (decimal).0002)
                                            && (IsBetweenVariance(instruments[instrument].fTop30bb, instruments[instrument].top30bb, (decimal).002)
                                                || instruments[instrument].top30bb > instruments[instrument].fTop30bb))
                                        {
                                            instruments[instrument].type = OType.Buy;
                                            instruments[instrument].canTrust = true;
                                            instruments[instrument].isLowVolume = true;
                                            instruments[instrument].canOrder = true;
                                            modifyOpenAlignOrReversedStatus(instruments[instrument], 16, OType.Buy, false);
                                            qualified = true;
                                        }
                                    }
                                    else
                                    {
                                        instruments[instrument].type = OType.Buy;
                                        instruments[instrument].canTrust = true;
                                        instruments[instrument].isLowVolume = true;
                                        instruments[instrument].canOrder = true;
                                        modifyOpenAlignOrReversedStatus(instruments[instrument], 16, OType.Buy, false);
                                        qualified = true;
                                    }
                                }
                            }
                        }
                        else if (prevCandleClose < instruments[instrument].middleBB
                                    || (IsBetweenVariance(prevCandleClose, instruments[instrument].middleBB, (decimal).0015)
                                        && instruments[instrument].goodToGo
                                        && instruments[instrument].toSell))
                        {
                            isBetween8Variance = instruments[instrument].middleBB < ltp
                                                    || IsBetweenVariance(instruments[instrument].middleBB, ltp, (decimal).0008);
                            checkAllCandleStatus = CheckAllCandleStatus(instrument, OType.Sell);
                            //checkMa50 = CheckMa50(instrument, OType.Sell);
                            checkLateMa50 = CheckLateMa50(instrument, OType.Sell);
                            if ((IsBetweenVariance(instruments[instrument].middleBB, ltp, (decimal).0004)
                                        || ltp > instruments[instrument].middleBB)
                                    && candleCount >= count - candles
                                    && checkLateMa50
                                    && checkAllCandleStatus
                                    && instruments[instrument].topBB - instruments[instrument].botBB >= spikeA
                                    && !(low <= instruments[instrument].bot30bb
                                        || IsBetweenVariance(low, instruments[instrument].bot30bb, (decimal).0006)))
                            {
                                instruments[instrument].goodToGo = true;
                                instruments[instrument].toBuy = false;
                                instruments[instrument].toSell = true;
                                if ((averagePrice >= ltp + spike2
                                        || (ltp > instruments[instrument].middleBB
                                            && IsBetweenVariance(ltp, instruments[instrument].middleBB, (decimal).004)))
                                    && instruments[instrument].topBB - instruments[instrument].botBB >= spike)
                                {
                                    if (instruments[instrument].topBB - instruments[instrument].botBB >= variance14)
                                        Console.WriteLine("At {0} This script {1} is 6x showing low volume signs of immediate deflate with ltp {2} but with averageprice {3}", DateTime.Now, instruments[instrument].futName, ltp, averagePrice);
                                    else
                                        Console.WriteLine("At {0} This script {1} is 6 showing low volume signs of immediate deflate with ltp {2} but with averageprice {3}", DateTime.Now, instruments[instrument].futName, ltp, averagePrice);
                                    if (IsBetweenVariance(ltp, instruments[instrument].close, (decimal).0025)
                                        && (IsBetweenVariance(buyQuantity, sellQuantity, (decimal).002)
                                            || buyQuantity > sellQuantity))
                                    {
                                        Console.WriteLine("WRONG WRONG At {0} This script {1} is 6 showing low volume signs of immediate Surge with ltp {2} but with averageprice {3}", DateTime.Now, instruments[instrument].futName, ltp, averagePrice);
                                    }
                                    else if (((instruments[instrument].middleBB < instruments[instrument].ma50
                                            && IsBetweenVariance(instruments[instrument].middleBB, instruments[instrument].ma50, (decimal).0025))
                                            || (instruments[instrument].topBB < instruments[instrument].ma50
                                                && IsBetweenVariance(instruments[instrument].topBB, instruments[instrument].ma50, (decimal).0025)))
                                        && IsBetweenVariance(ltp, instruments[instrument].middleBB, (decimal).003))
                                    {
                                        //Do Nothing
                                    }
                                    else if (IsBetweenVariance(ltp, instruments[instrument].close, (decimal).015)
                                                && IsBetweenVariance(ltp, low, (decimal).007)
                                                && (IsBetweenVariance(instruments[instrument].bot30bb, low, (decimal).0007)
                                                || instruments[instrument].bot30bb > low))
                                    {
                                        //Do Nothing
                                    }
                                    else if (instruments[instrument].topBB - instruments[instrument].botBB >= variance14
                                                && instruments[instrument].ma50 < instruments[instrument].topBB
                                                && IsBetweenVariance(ltp, instruments[instrument].middleBB, (decimal).0007))
                                    {
                                        //Do Nothing
                                    }
                                    else if (instruments[instrument].topBB - instruments[instrument].botBB <=
                                                variance14
                                                && (sellQuantity < Math.Round(buyQuantity * 1.1)
                                                || !(low <= instruments[instrument].fBot30bb
                                                        || IsBetweenVariance(low, instruments[instrument].fBot30bb, (decimal).0006))))
                                    {
                                        //Do Nothing
                                    }
                                    else
                                    {
                                        instruments[instrument].type = OType.Sell;
                                        instruments[instrument].canTrust = true;
                                        instruments[instrument].isLowVolume = true;
                                        instruments[instrument].canOrder = true;
                                        modifyOpenAlignOrReversedStatus(instruments[instrument], 16, OType.Sell, false);
                                        qualified = true;
                                    }
                                }
                                else
                                {
                                    if (instruments[instrument].oldTime != instruments[instrument].currentTime)
                                    {
                                        instruments[instrument].oldTime = instruments[instrument].currentTime;
                                        Console.WriteLine(
                                            "At {0} This script {1} is 10 showing low volume signs of immediate deflate with ltp {2} but with averageprice {3}",
                                            DateTime.Now, instruments[instrument].futName, ltp, averagePrice);
                                    }
                                }
                            }
                            else if (isGearingStatus)
                            {
                                instruments[instrument].goodToGo = true;
                                instruments[instrument].toBuy = false;
                                instruments[instrument].toSell = true;
                                if (instruments[instrument].oldTime != instruments[instrument].currentTime)
                                {
                                    instruments[instrument].oldTime = instruments[instrument].currentTime;
                                    if (candleCount >= count - candles
                                        && instruments[instrument].isReversed
                                        && instruments[instrument].type == OType.Sell)
                                    {
                                        Console.WriteLine("At {0} This script {1} is 5 showing low volume signs of immediate deflate with ltp {2} but with averageprice {3}", DateTime.Now, instruments[instrument].futName, ltp, averagePrice);
                                    }
                                    else if (candleCount >= count - candles
                                        && gearingStatus)
                                    {
                                        Console.WriteLine("At {0} This script {1} is 4 showing low volume signs of immediate Deflate with ltp {2} but with averageprice {3}", DateTime.Now, instruments[instrument].futName, ltp, averagePrice);
                                    }
                                    else if (instruments[instrument].isReversed)
                                        Console.WriteLine("At {0} This script {1} is 3x showing low volume signs of immediate Deflate with ltp {2} but with averageprice {3}", DateTime.Now, instruments[instrument].futName, ltp, averagePrice);
                                    else
                                    {
                                        Console.WriteLine("At {0} This script {1} is 3 showing low volume signs of immediate Deflate with ltp {2} but with averageprice {3}", DateTime.Now, instruments[instrument].futName, ltp, averagePrice);
                                    }
                                }
                                if (checkLateMa50
                                    && instruments[instrument].topBB - instruments[instrument].botBB > spikeA
                                    && checkAllCandleStatus
                                    && ltp >= instruments[instrument].middleBB)
                                {
                                    if (low < instruments[instrument].bot30bb
                                        || IsBetweenVariance(ltp, instruments[instrument].bot30bb, (decimal).002)) 
                                        //IsBeyondVariance(ltp, instruments[instrument].middleBB, (decimal).0012))
                                        Console.WriteLine("At {0} This script {1} is 6yz showing low signs of immediate deflate with ltp {2} but with averageprice {3}", DateTime.Now, instruments[instrument].futName, ltp, averagePrice);
                                    else if (low < instruments[instrument].fBot30bb
                                                || IsBetweenVariance(ltp, instruments[instrument].fBot30bb, (decimal).002))
                                        Console.WriteLine("At {0} This script {1} is 6zy showing low signs of immediate deflate with ltp {2} but with averageprice {3}", DateTime.Now, instruments[instrument].futName, ltp, averagePrice);
                                    else
                                    {
                                        Console.WriteLine(
                                            "At {0} This script {1} is y6z showing low signs of immediate deflate with ltp {2} but with averageprice {3}",
                                            DateTime.Now, instruments[instrument].futName, ltp, averagePrice);
                                        if (instruments[instrument].topBB - instruments[instrument].botBB >= spikeN
                                            && instruments[instrument]
                                                .history30Min[instruments[instrument].history30Min.Count - 1]
                                                .Close < instruments[instrument].middle30BBnew
                                            && instruments[instrument]
                                                .history30Min[instruments[instrument].history30Min.Count - 2]
                                                .Close < instruments[instrument].middle30BB)
                                        {
                                            instruments[instrument].type = OType.Sell;
                                            instruments[instrument].canTrust = true;
                                            instruments[instrument].isLowVolume = true;
                                            instruments[instrument].canOrder = true;
                                            modifyOpenAlignOrReversedStatus(instruments[instrument], 16, OType.Sell,
                                                false);
                                            qualified = true;
                                        }
                                    }
                                }
                                else
                                {
                                    if (isBetween8Variance
                                        && checkLateMa50
                                        && instruments[instrument].topBB - instruments[instrument].botBB > spikeA)
                                        Console.WriteLine(
                                            "At {0} This script {1} is 6y showing low signs of immediate deflate with ltp {2} but with averageprice {3}",
                                            DateTime.Now, instruments[instrument].futName, ltp, averagePrice);
                                    if (isBetween8Variance
                                        && checkAllCandleStatus
                                        && instruments[instrument].topBB - instruments[instrument].botBB >= spikeA)
                                    {
                                        Console.WriteLine(
                                            "At {0} This script {1} is 6z showing low signs of immediate deflate with ltp {2} but with averageprice {3}",
                                            DateTime.Now, instruments[instrument].futName, ltp, averagePrice);
                                    }
                                }
                                checkMa50 = CheckMa50(instrument, OType.Sell);
                                List<decimal> subsequentMiddleBB = GetForecastMiddle30BBOf(instruments[instrument].history, instruments[instrument].middleBB, OType.Sell);
                                bool isnarrowing = subsequentMiddleBB[1] - subsequentMiddleBB[2] > spikeA
                                        || IsBeyondVariance(subsequentMiddleBB[1] - subsequentMiddleBB[2], instruments[instrument].topBB - instruments[instrument].botBB, (decimal).3);
                                if (!isnarrowing
                                    && IsBetweenVariance(subsequentMiddleBB[1] - subsequentMiddleBB[2], instruments[instrument].topBB - instruments[instrument].botBB, (decimal).01))
                                {
                                    Console.WriteLine(
                                            "At {0} This script {1} is 12 showing low signs of immediate deflate with ltp {2} but with averageprice {3}",
                                            DateTime.Now, instruments[instrument].futName, ltp, averagePrice);
                                }
                                CheckBollingerExpansion(instrument, OType.Sell, ltp, spikeNN, timenow);
                                if ((averagePrice >= ltp + (spike2 / 2)
                                        || (averagePrice > ltp && checkMa50))
                                    && checkLateMa50 
                                    //&& checkAllCandleStatus
                                    && isnarrowing
                                    && (instruments[instrument].topBB - instruments[instrument].botBB >= spikeA
                                        || IsBetweenVariance(instruments[instrument].topBB - instruments[instrument].botBB, spikeA, (decimal).0006))
                                    //&& instruments[instrument].topBB - instruments[instrument].botBB < variance14
                                    && (IsBetweenVariance(instruments[instrument].middleBB, ltp, (decimal).0004)
                                        || ltp > instruments[instrument].middleBB)
                                    && !(low <= instruments[instrument].bot30bb
                                        || IsBetweenVariance(low, instruments[instrument].bot30bb, (decimal).0006)))
                                {
                                    if (gearingStatus)
                                        Console.WriteLine("At {0} This script {1} is 5y showing low volume signs of immediate deflate with ltp {2} but with averageprice {3}", DateTime.Now, instruments[instrument].futName, ltp, averagePrice);
                                    else if (instruments[instrument].topBB - instruments[instrument].botBB <= variance17)
                                        Console.WriteLine("At {0} This script {1} is 5z showing low volume signs of immediate deflate with ltp {2} but with averageprice {3}", DateTime.Now, instruments[instrument].futName, ltp, averagePrice);
                                    else
                                        Console.WriteLine("At {0} This script {1} is 5x showing low volume signs of immediate deflate with ltp {2} but with averageprice {3}", DateTime.Now, instruments[instrument].futName, ltp, averagePrice);
                                    if (instruments[instrument].middleBB < instruments[instrument].ma50
                                        && (((IsBetweenVariance(instruments[instrument].middleBB, instruments[instrument].ma50, (decimal).0025)
                                                    || instruments[instrument].topBB - instruments[instrument].botBB <= variance14)
                                                    && IsBetweenVariance(ltp, instruments[instrument].middleBB, (decimal).0012))
                                                || (instruments[instrument].topBB < instruments[instrument].ma50
                                                    && instruments[instrument].middle30ma50 > instruments[instrument].middle30BB
                                                    && instruments[instrument].topBB - instruments[instrument].botBB <= spike
                                                    && IsBetweenVariance(ltp, instruments[instrument].middle30BB, (decimal).0012))
                                                || (!(candleCount >= count - candles)
                                                    && !checkAllCandleStatus
                                                    && sellQuantity < Math.Round(buyQuantity * 1.3))))
                                    {
                                        if (IsBetweenVariance(ltp, instruments[instrument].close, (decimal).0025)
                                            && (IsBetweenVariance(buyQuantity, sellQuantity, (decimal).002)
                                                || buyQuantity > sellQuantity))
                                        {
                                            Console.WriteLine("WRONG WRONG At {0} This script {1} is 5x showing low volume signs of immediate surge with ltp {2} but with averageprice {3}", DateTime.Now, instruments[instrument].futName, ltp, averagePrice);
                                        }
                                        else if (gearingStatus
                                            && ltp > instruments[instrument].middleBB
                                            && IsBeyondVariance(ltp, instruments[instrument].middleBB,
                                                (decimal).0002)
                                            && (IsBetweenVariance(instruments[instrument].fBot30bb, instruments[instrument].bot30bb, (decimal).002)
                                                    || instruments[instrument].fBot30bb > instruments[instrument].bot30bb))
                                        {
                                            instruments[instrument].type = OType.Sell;
                                            instruments[instrument].canTrust = true;
                                            instruments[instrument].isLowVolume = true;
                                            instruments[instrument].canOrder = true;
                                            modifyOpenAlignOrReversedStatus(instruments[instrument], 16, OType.Sell, false);
                                            qualified = true;
                                        }
                                    }
                                    else
                                    {
                                        instruments[instrument].type = OType.Sell;
                                        instruments[instrument].canTrust = true;
                                        instruments[instrument].isLowVolume = true;
                                        instruments[instrument].canOrder = true;
                                        modifyOpenAlignOrReversedStatus(instruments[instrument], 16, OType.Sell, false);
                                        qualified = true;
                                    }
                                }
                            }
                        }
                    }
                    if (qualified
                        && instruments[instrument].topBB - instruments[instrument].botBB > variance2)
                    {
                        Console.WriteLine("At {0} This script {1} could have been avoided as current BB variance is too high; topbb{2} & botbb {3}", DateTime.Now, instruments[instrument].futName, instruments[instrument].topBB, instruments[instrument].botBB);
                    }
                    #endregion
                }
                else if (Decimal.Compare(timenow, (decimal)11) > 0
                        && (volume < instruments[instrument].AvgVolume / GetLowVolumePercentage(timenow)
                            || (VerifyVolume(instrument, volume, timenow)
                                    && Decimal.Compare(timenow, (decimal)12) > 0))
                        && instruments[instrument].topBB - instruments[instrument].botBB > spikeA
                        && instruments[instrument].top30bb - instruments[instrument].bot30bb > variance2)
                {
                    if (buyQuantity > Math.Round(sellQuantity * 1.2))
                    {
                        if (instruments[instrument].oldTime != instruments[instrument].currentTime)
                        {
                            InsertNewToken(instrument, instruments[instrument].futName, ltp, change, instruments[instrument].middleBB, "BUY", "WATCH", instruments[instrument].type);
                            instruments[instrument].oldTime = instruments[instrument].currentTime;
                            if (instruments[instrument].type == OType.Buy
                                && IsBetweenVariance(ltp, instruments[instrument].middle30BB, (decimal).0006))
                                Console.WriteLine("At {0} This script {1} is 11x showing low volume signs of immediate surge with ltp {2} with average {3}", DateTime.Now, instruments[instrument].futName, ltp, averagePrice);
                            else
                                Console.WriteLine("At {0} This script {1} is 11 showing low volume signs of immediate surge with ltp {2} with average {3}", DateTime.Now, instruments[instrument].futName, ltp, averagePrice);
                        }
                    }
                    else if (Math.Round(buyQuantity * 1.2) < sellQuantity)
                    {
                        if (instruments[instrument].oldTime != instruments[instrument].currentTime)
                        {
                            InsertNewToken(instrument, instruments[instrument].futName, ltp, change, instruments[instrument].middleBB, "SELL", "WATCH", instruments[instrument].type);
                            instruments[instrument].oldTime = instruments[instrument].currentTime;
                            if (instruments[instrument].type == OType.Sell
                                && IsBetweenVariance(ltp, instruments[instrument].middle30BB, (decimal).0006))
                                Console.WriteLine("At {0} This script {1} is 11x showing low volume signs of immediate deflate with ltp {2} with average {3}", DateTime.Now, instruments[instrument].futName, ltp, averagePrice);
                            else
                                Console.WriteLine("At {0} This script {1} is 11 showing low volume signs of immediate deflate with ltp {2} with average {3}", DateTime.Now, instruments[instrument].futName, ltp, averagePrice);
                        }
                    }
                }
                if (!qualified && instruments[instrument].isHighVolume)
                    return false;

                decimal varRang = ltp < 140
                                  || (instruments[instrument].bot30bb + variance43) < instruments[instrument].top30bb
                                  || (instruments[instrument].AvgVolume > (volume * 4) && Decimal.Compare(timenow, Convert.ToDecimal(10.14)) > 0) ?
                    (instruments[instrument].bot30bb + variance43) < instruments[instrument].top30bb ? (decimal).0015 : (decimal).001 : (decimal).0006;

                #region Verify for Open Trigger
                if (!qualified
                    && instruments[instrument].canTrust
                    && instruments[instrument].isReversed
                    && Decimal.Compare(timenow, (decimal)10.45) >= 0)
                {
                    switch (instruments[instrument].type)
                    {
                        case OType.Sell:
                        case OType.StrongSell:
                            #region Verify Sell Trigger
                            if (instruments[instrument].middle30BBnew > instruments[instrument].middleBB)
                            {
                                qualified = (IsBetweenVariance(ltp, instruments[instrument].middle30BBnew, (decimal).0006) || ltp > instruments[instrument].middle30BBnew);
                            }
                            else
                            {
                                qualified = (ltp > instruments[instrument].middleBB || IsBetweenVariance(ltp, instruments[instrument].middleBB, (decimal).0006));
                            }
                            if (qualified)
                            {
                                if (low <= instruments[instrument].bot30bb
                                    || IsBetweenVariance(low, instruments[instrument].bot30bb, (decimal).0012)
                                    || (instruments[instrument].bot30bb + variance18) > instruments[instrument].top30bb
                                    || IsBetweenVariance((instruments[instrument].bot30bb + variance18), instruments[instrument].top30bb, (decimal).001)
                                    || (instruments[instrument].middle30ma50 < instruments[instrument].middle30BB
                                        && instruments[instrument].middle30ma50 > instruments[instrument].bot30bb
                                        && IsBeyondVariance(instruments[instrument].middle30ma50, instruments[instrument].middle30BB, (decimal).001)
                                        && IsBeyondVariance(instruments[instrument].middle30ma50, instruments[instrument].bot30bb, (decimal).001)))
                                    qualified = false;
                                if (qualified && instruments[instrument].canOrder)
                                {
                                    if (sellQuantity > Math.Round(buyQuantity * 1.15)
                                        && (instruments[instrument].fBot30bb > instruments[instrument].bot30bb
                                            || IsBetweenVariance(instruments[instrument].fBot30bb, instruments[instrument].bot30bb, (decimal).0025)))
                                    {
                                        Console.WriteLine(
                                            "INSIDER :: At {4} Qualified for SELL order based on LTP {0} by {3} is ~ above short trigger {1} and ltp is around topBB {2}",
                                            ltp, instruments[instrument].shortTrigger,
                                            instruments[instrument].topBB, instruments[instrument].futName,
                                            DateTime.Now.ToString());
                                        if (instruments[instrument]
                                                .history30Min[instruments[instrument].history30Min.Count - 2]
                                                .Close <
                                            instruments[instrument].middle30BB)
                                        {
                                            if (instruments[instrument].fTop30bb - instruments[instrument].fBot30bb
                                                > Math.Round((instruments[instrument].top30bb - instruments[instrument].bot30bb) * (decimal)1.3, 1))
                                            {
                                                qualified = false;
                                                instruments[instrument].isReversed = false;
                                                instruments[instrument].canOrder = false;
                                                instruments[instrument].canTrust = false;
                                                instruments[instrument].goodToGo = false;
                                                instruments[instrument].toBuy = false;
                                                instruments[instrument].toSell = false;
                                            }
                                            else if ((instruments[instrument]
                                                        .history30Min[instruments[instrument].history30Min.Count - 3]
                                                        .Close >= instruments[instrument].middle30BB
                                                    || IsBetweenVariance(instruments[instrument]
                                                        .history30Min[instruments[instrument].history30Min.Count - 3]
                                                        .Close, instruments[instrument].middle30BB, (decimal).0008))
                                                   && (IsBetweenVariance(instruments[instrument].ma50, instruments[instrument].middleBB, (decimal).0008)
                                                       || instruments[instrument].middleBB >= instruments[instrument].ma50))
                                            {
                                                qualified = false;
                                            }
                                            else if (instruments[instrument]
                                                          .history30Min[instruments[instrument].history30Min.Count - 3]
                                                          .Low <= instruments[instrument].middle30BB
                                                     && instruments[instrument]
                                                         .history30Min[instruments[instrument].history30Min.Count - 3]
                                                         .Close >= instruments[instrument].middle30BB
                                                     && instruments[instrument].middleBB < instruments[instrument].ma50
                                                     && instruments[instrument].topBB > instruments[instrument].ma50
                                                     && IsBetweenVariance(ltp, instruments[instrument].middleBB, (decimal).0015))
                                            {
                                                qualified = false;
                                            }
                                            else
                                            {
                                                Console.WriteLine(
                                                    "INSIDER :: 2 At {4} Qualified for SELL order based on LTP {0} by {3} is ~ above short trigger {1} and ltp is around topBB {2}",
                                                    ltp, instruments[instrument].shortTrigger,
                                                    instruments[instrument].topBB, instruments[instrument].futName,
                                                    DateTime.Now.ToString());
                                                instruments[instrument].type = OType.Sell;
                                                instruments[instrument].canOrder = true;
                                                instruments[instrument].canTrust = true;
                                            }
                                        }
                                        else
                                            qualified = false;
                                    }
                                    else
                                    {
                                        if (instruments[instrument]
                                                .history30Min[instruments[instrument].history30Min.Count - 2]
                                                .Close <
                                            instruments[instrument].middle30BB)
                                        {
                                            if (instruments[instrument].topBB - instruments[instrument].botBB <=
                                                variance14)
                                            {
                                                qualified = instruments[instrument].middle30BB <= instruments[instrument].middleBB ?
                                                        ltp > instruments[instrument].middleBB
                                                        && IsBeyondVariance(ltp, instruments[instrument].middleBB,
                                                                (decimal).001) :
                                                        ltp > instruments[instrument].middle30BB
                                                        && IsBeyondVariance(ltp, instruments[instrument].middle30BB,
                                                                                    (decimal).001);
                                            }
                                            if (qualified)
                                            {
                                                qualified = false;
                                                if (instruments[instrument].oldTime !=
                                                    instruments[instrument].currentTime)
                                                {
                                                    instruments[instrument].oldTime =
                                                        instruments[instrument].currentTime;
                                                    Console.WriteLine(
                                                        "(INSIDER) At {4} Qualified for SELL order based on LTP {0} by {3} is ~ above short trigger {1} and ltp is around topBB {2}",
                                                        ltp, instruments[instrument].shortTrigger,
                                                        instruments[instrument].topBB, instruments[instrument].futName,
                                                        DateTime.Now.ToString());
                                                }
                                            }
                                            qualified = (low <= instruments[instrument].bot30bb
                                                         || IsBetweenVariance(low, instruments[instrument].bot30bb, (decimal).006))
                                                        && instruments[instrument].fBot30bb < instruments[instrument].bot30bb
                                                        && IsBeyondVariance(instruments[instrument].fBot30bb, instruments[instrument].bot30bb, (decimal).005)
                                                        && ((instruments[instrument].fTop30bb > instruments[instrument].top30bb 
                                                                && IsBeyondVariance(instruments[instrument].fTop30bb, instruments[instrument].top30bb, (decimal).005))
                                                            || (IsBeyondVariance(instruments[instrument].fBot30bb, instruments[instrument].bot30bb, (decimal).015)
                                                                && IsBetweenVariance(instruments[instrument].fTop30bb, instruments[instrument].top30bb, (decimal).005)));
                                            if (qualified)
                                            {
                                                qualified = false;
                                                Console.WriteLine(
                                                    "(NOT A INSIDER) At {4} Qualified for BUY order based on LTP {0} by {3} is ~ above long trigger {1} and ltp is around botBB {2}",
                                                    ltp, instruments[instrument].longTrigger,
                                                    instruments[instrument].botBB, instruments[instrument].futName,
                                                    DateTime.Now.ToString());
                                                if (instruments[
                                                            Convert.ToUInt32(
                                                                ConfigurationManager.AppSettings["NSENIFTY"])]
                                                        .type == OType.Sell
                                                    && isNiftyVolatile)
                                                {
                                                    //Do nothing
                                                }
                                                else
                                                    instruments[instrument].type = OType.Buy;
                                            }
                                            else
                                            {
                                                qualified = low >= instruments[instrument].bot30bb
                                                            && IsBeyondVariance(low, instruments[instrument].bot30bb, (decimal).005)
                                                            && (instruments[instrument].fBot30bb >= instruments[instrument].bot30bb
                                                                || IsBetweenVariance(instruments[instrument].fBot30bb, instruments[instrument].bot30bb, (decimal).005))
                                                            && (instruments[instrument].fTop30bb >= instruments[instrument].top30bb
                                                                || IsBetweenVariance(instruments[instrument].fTop30bb, instruments[instrument].top30bb, (decimal).002));
                                                            //&& IsBeyondVariance(instruments[instrument].fTop30bb, instruments[instrument].top30bb, (decimal).01);
                                                if (qualified)
                                                {
                                                    Console.WriteLine(
                                                        "(STILL A INSIDER) At {4} Qualified for SELL order based on LTP {0} by {3} is ~ above short trigger {1} and ltp is around topBB {2}",
                                                        ltp, instruments[instrument].shortTrigger,
                                                        instruments[instrument].topBB,
                                                        instruments[instrument].futName,
                                                        DateTime.Now.ToString());
                                                    if (buyQuantity > Math.Round(sellQuantity * 1.06)
                                                        && IsBetweenVariance(ltp, instruments[instrument].middleBB,
                                                            (decimal).002))
                                                    {
                                                        qualified = false;
                                                    }
                                                }
                                            }
                                        }
                                    }
                                }
                                else
                                    qualified = false;
                            }
                            #endregion
                            break;
                        case OType.Buy:
                        case OType.StrongBuy:
                            #region Verify BUY Trigger
                            if (instruments[instrument].middle30BBnew < instruments[instrument].middleBB)
                            {
                                qualified = (IsBetweenVariance(ltp, instruments[instrument].middle30BBnew, (decimal).0006) || ltp < instruments[instrument].middle30BBnew);
                            }
                            else
                            {
                                qualified = (IsBetweenVariance(ltp, instruments[instrument].middleBB, (decimal).0006) || ltp < instruments[instrument].middleBB);
                            }
                            if (qualified)
                            {
                                if (high >= instruments[instrument].top30bb
                                    || IsBetweenVariance(high, instruments[instrument].top30bb, (decimal).0012)
                                    || (instruments[instrument].bot30bb + variance18) > instruments[instrument].top30bb
                                    || IsBetweenVariance((instruments[instrument].bot30bb + variance18), instruments[instrument].top30bb, (decimal).001)
                                    || (instruments[instrument].middle30ma50 < instruments[instrument].top30bb
                                        && instruments[instrument].middle30ma50 > instruments[instrument].middle30BB
                                        && IsBeyondVariance(instruments[instrument].middle30ma50, instruments[instrument].middle30BB, (decimal).001)
                                        && IsBeyondVariance(instruments[instrument].middle30ma50, instruments[instrument].top30bb, (decimal).001)))
                                    qualified = false;
                                if (qualified && instruments[instrument].canOrder)
                                {
                                    if (buyQuantity > Math.Round(sellQuantity * 1.15)
                                        && (instruments[instrument].fTop30bb < instruments[instrument].top30bb
                                            || IsBetweenVariance(instruments[instrument].fTop30bb, instruments[instrument].top30bb, (decimal).0025)))
                                    {
                                        Console.WriteLine(
                                            "INSIDER :: At {4} Qualified for BUY order based on LTP {0} by {3} is ~ above long trigger {1} and ltp is around botBB {2}",
                                            ltp, instruments[instrument].longTrigger, instruments[instrument].botBB,
                                            instruments[instrument].futName, DateTime.Now.ToString());
                                        if (instruments[instrument]
                                                .history30Min[instruments[instrument].history30Min.Count - 2]
                                                .Close >
                                            instruments[instrument].middle30BB)
                                        {
                                            if (instruments[instrument].fTop30bb - instruments[instrument].fBot30bb
                                                > Math.Round((instruments[instrument].top30bb - instruments[instrument].bot30bb) * (decimal)1.3, 1))
                                            {
                                                qualified = false;
                                                instruments[instrument].isReversed = false;
                                                instruments[instrument].canOrder = false;
                                                instruments[instrument].canTrust = false;
                                                instruments[instrument].goodToGo = false;
                                                instruments[instrument].toBuy = false;
                                                instruments[instrument].toSell = false;
                                            }
                                            else if((instruments[instrument]
                                                        .history30Min[instruments[instrument].history30Min.Count - 3]
                                                        .Close <= instruments[instrument].middle30BB
                                                    || IsBetweenVariance(instruments[instrument]
                                                        .history30Min[instruments[instrument].history30Min.Count - 3]
                                                        .Close, instruments[instrument].middle30BB, (decimal).0008))
                                                   && (IsBetweenVariance(instruments[instrument].ma50, instruments[instrument].middleBB, (decimal).0008)
                                                       || instruments[instrument].middleBB <= instruments[instrument].ma50))
                                            {
                                                qualified = false;
                                            }
                                            else if (instruments[instrument]
                                                         .history30Min[instruments[instrument].history30Min.Count - 3]
                                                         .High >= instruments[instrument].middle30BB
                                                     && instruments[instrument]
                                                         .history30Min[instruments[instrument].history30Min.Count - 3]
                                                         .Close <= instruments[instrument].middle30BB
                                                     && instruments[instrument].middleBB > instruments[instrument].ma50
                                                     && instruments[instrument].botBB < instruments[instrument].ma50
                                                     && IsBetweenVariance(ltp, instruments[instrument].middleBB, (decimal).0015))
                                            {
                                                qualified = false;
                                            }
                                            else 
                                            {
                                                Console.WriteLine(
                                                    "INSIDER :: 2 At {4} Qualified for BUY order based on LTP {0} by {3} is ~ above long trigger {1} and ltp is around botBB {2}",
                                                    ltp, instruments[instrument].longTrigger,
                                                    instruments[instrument].botBB,
                                                    instruments[instrument].futName, DateTime.Now.ToString());
                                                instruments[instrument].type = OType.Buy;
                                                instruments[instrument].canOrder = true;
                                                instruments[instrument].canTrust = true;
                                            }
                                        }
                                        else
                                            qualified = false;
                                    }
                                    else
                                    {
                                        if (instruments[instrument]
                                                .history30Min[instruments[instrument].history30Min.Count - 2]
                                                .Close >
                                            instruments[instrument].middle30BB)
                                        {
                                            if (instruments[instrument].topBB - instruments[instrument].botBB <=
                                                variance14)
                                            {
                                                qualified = instruments[instrument].middle30BB >= instruments[instrument].middleBB ?
                                                    ltp > instruments[instrument].middleBB
                                                    && IsBeyondVariance(ltp, instruments[instrument].middleBB,
                                                        (decimal).001) :
                                                    ltp > instruments[instrument].middle30BB
                                                    && IsBeyondVariance(ltp, instruments[instrument].middle30BB,
                                                        (decimal).001);
                                            }
                                            if (qualified)
                                            {
                                                qualified = false;
                                                if (instruments[instrument].oldTime !=
                                                    instruments[instrument].currentTime)
                                                {
                                                    instruments[instrument].oldTime =
                                                        instruments[instrument].currentTime;
                                                    Console.WriteLine(
                                                        "(INSIDER) At {4} Qualified for BUY order based on LTP {0} by {3} is ~ above long trigger {1} and ltp is around botBB {2}",
                                                        ltp, instruments[instrument].longTrigger,
                                                        instruments[instrument].botBB,
                                                        instruments[instrument].futName, DateTime.Now.ToString());
                                                }
                                            }
                                            qualified = (high >= instruments[instrument].top30bb
                                                         || IsBetweenVariance(high, instruments[instrument].top30bb, (decimal).006))
                                                        && instruments[instrument].fTop30bb > instruments[instrument].top30bb
                                                        && IsBeyondVariance(instruments[instrument].fTop30bb, instruments[instrument].top30bb, (decimal).005)
                                                        && ((instruments[instrument].fBot30bb < instruments[instrument].bot30bb
                                                             && IsBeyondVariance(instruments[instrument].fBot30bb, instruments[instrument].bot30bb, (decimal).005))
                                                            || (IsBeyondVariance(instruments[instrument].fTop30bb, instruments[instrument].top30bb, (decimal).015)
                                                                && IsBetweenVariance(instruments[instrument].fBot30bb, instruments[instrument].bot30bb, (decimal).005)));
                                            if (qualified)
                                            {
                                                Console.WriteLine(
                                                    "(NOT A INSIDER) At {4} Qualified for SELL order based on LTP {0} by {3} is ~ above short trigger {1} and ltp is around topBB {2}",
                                                    ltp, instruments[instrument].shortTrigger,
                                                    instruments[instrument].topBB, instruments[instrument].futName,
                                                    DateTime.Now.ToString());
                                                if (instruments[
                                                            Convert.ToUInt32(
                                                                ConfigurationManager.AppSettings["NSENIFTY"])]
                                                        .type == OType.Buy
                                                    && isNiftyVolatile)
                                                {
                                                    //Do nothing
                                                }
                                                else
                                                    instruments[instrument].type = OType.Sell;
                                                qualified = false;
                                            }
                                            else
                                            {
                                                qualified = high <= instruments[instrument].top30bb
                                                            && IsBeyondVariance(high, instruments[instrument].top30bb,
                                                                (decimal).005)
                                                            && (instruments[instrument].fTop30bb <=
                                                                instruments[instrument].top30bb
                                                                || IsBetweenVariance(instruments[instrument].fTop30bb,
                                                                    instruments[instrument].top30bb, (decimal).005))
                                                            && (instruments[instrument].fBot30bb <=
                                                                instruments[instrument].bot30bb
                                                                || IsBetweenVariance(instruments[instrument].fBot30bb,
                                                                    instruments[instrument].bot30bb, (decimal).002));
                                                            //&& IsBeyondVariance(instruments[instrument].fBot30bb, instruments[instrument].bot30bb, (decimal).01);
                                                if (qualified)
                                                {
                                                    Console.WriteLine(
                                                        "(STILL A INSIDER) At {4} Qualified for BUY order based on LTP {0} by {3} is ~ above long trigger {1} and ltp is around botBB {2}",
                                                        ltp, instruments[instrument].longTrigger,
                                                        instruments[instrument].botBB,
                                                        instruments[instrument].futName,
                                                        DateTime.Now.ToString());
                                                    if (sellQuantity > Math.Round(buyQuantity * 1.06)
                                                        && IsBetweenVariance(ltp, instruments[instrument].middleBB,
                                                            (decimal).002))
                                                    {
                                                        qualified = false;
                                                    }
                                                }
                                            }
                                        }
                                    }
                                }
                                else
                                    qualified = false;
                            }
                            #endregion
                            break;
                        default:
                            qualified = false;
                            break;
                    }
                }
                #endregion

                #region Verify Volume
                if (!qualified
                    && Decimal.Compare(timenow, Convert.ToDecimal(10.17)) >= 0)
                {
                    if (((ltp - instruments[instrument].middleBB) >= spikeVM
                                                || IsBetweenVariance((ltp - instruments[instrument].middleBB), spikeVM, (decimal).0006))
                                            && ltp >= instruments[instrument].top30bb)
                    {
                        Console.WriteLine("At {0} Cautious 7y Average volume {2} and Current Volume {3} is aligning for Short call for the script {1} ltp {4}", DateTime.Now.ToString(), instruments[instrument].futName, instruments[instrument].AvgVolume, volume, ltp);
                        if ((ltp - instruments[instrument].middleBB) >= spikeM + spikeN)
                        {
                            instruments[instrument].type = OType.Sell;
                            instruments[instrument].canTrust = false;
                            instruments[instrument].canOrder = false;
                            instruments[instrument].shortTrigger = instruments[instrument].top30bb;
                            modifyOpenAlignOrReversedStatus(instruments[instrument], 16, OType.Sell, false);
                            qualified = true;
                        }
                    }
                    else if (((instruments[instrument].middleBB - ltp) >= spikeVM
                                || IsBetweenVariance((instruments[instrument].middleBB - ltp), spikeVM, (decimal).0006))
                            && ltp <= instruments[instrument].bot30bb)
                    {
                        Console.WriteLine("At {0} Cautious 7y Average volume {2} and Current Volume {3} is aligning for Long call for the script {1} ltp {4}", DateTime.Now.ToString(), instruments[instrument].futName, instruments[instrument].AvgVolume, volume, ltp);
                        if ((instruments[instrument].middleBB - ltp) >= spikeM + spikeN)
                        {
                            instruments[instrument].type = OType.Buy;
                            instruments[instrument].canTrust = false;
                            instruments[instrument].canOrder = false;
                            instruments[instrument].longTrigger = instruments[instrument].bot30bb;
                            modifyOpenAlignOrReversedStatus(instruments[instrument], 16, OType.Buy, false);
                            qualified = true;
                        }
                    }
                    else if (((ltp - instruments[instrument].middleBB) >= spikeV
                                || IsBetweenVariance((ltp - instruments[instrument].middleBB), spikeV, (decimal).0006))
                            && (ltp >= instruments[instrument].top30bb
                                || IsBetweenVariance(ltp, instruments[instrument].top30bb, (decimal).0006)))
                    {
                        if (isNiftyVolatile
                            && ((ltp - instruments[instrument].middleBB) <= spikeVM
                                || instruments[instrument].middle30ma50 >= instruments[instrument].middle30BBnew))
                        //&& (instruments[instrument].fTop30bb < instruments[instrument].top30bb
                        //    || IsBetweenVariance(instruments[instrument].fTop30bb, instruments[instrument].top30bb,(decimal).001)))
                        {
                            // Do Nothing
                        }
                        else
                        {
                            Console.WriteLine(
                                "At {0} Cautious 7z Average volume {2} and Current Volume {3} is aligning for Short call for the script {1} ltp {4}",
                                DateTime.Now.ToString(), instruments[instrument].futName,
                                instruments[instrument].AvgVolume, volume, ltp);
                            if (((DateTime.Now.Minute > 10 && DateTime.Now.Minute < 15)
                                 || (DateTime.Now.Minute > 40 && DateTime.Now.Minute < 45)
                                 || instruments[instrument].top30bb - instruments[instrument].bot30bb < variance25)
                                && ((ltp - instruments[instrument].middleBB) <= spikeVM))
                            {
                                // Do nothing
                            }
                            else if ((instruments[instrument].bot30bb + variance46) < instruments[instrument].top30bb
                                     && Decimal.Compare(timenow, Convert.ToDecimal(9.45)) > 0
                                     && (ltp - instruments[instrument].middleBB) <= spikeVM
                                     && (instruments[instrument].top30bb > instruments[instrument].fTop30bb
                                        || IsBetweenVariance(instruments[instrument].top30bb, instruments[instrument].fTop30bb, (decimal).0015)))
                            {
                                // Do nothing
                            }
                            else if (instruments[instrument].history30Min[instruments[instrument].history30Min.Count - 2].Low < instruments[instrument].middle30BB
                                      && (ltp - instruments[instrument].middleBB) <= spikeVM
                                      && isGearingStatus
                                      && (instruments[instrument].bot30bb + variance25) <= instruments[instrument].top30bb)
                            {
                                // Do nothing
                            }
                            else if (ltp >= instruments[instrument].top30bb
                                     && (Decimal.Compare(timenow, (decimal)10.17) > 0
                                         || !(instruments[instrument].isVolatile
                                              || isNiftyVolatile)))
                            {
                                instruments[instrument].type = OType.Sell;
                                instruments[instrument].canTrust = false;
                                instruments[instrument].canOrder = false;
                                instruments[instrument].shortTrigger = instruments[instrument].top30bb;
                                modifyOpenAlignOrReversedStatus(instruments[instrument], 16, OType.Sell, false);
                                qualified = true;
                            }
                        }
                    }
                    else if (((instruments[instrument].middleBB - ltp) >= spikeV
                                || IsBetweenVariance((instruments[instrument].middleBB - ltp), spikeV, (decimal).0006))
                            && (ltp <= instruments[instrument].bot30bb
                                    || IsBetweenVariance(ltp, instruments[instrument].bot30bb, (decimal).0006)))
                    {
                        if (isNiftyVolatile
                            && ((instruments[instrument].middleBB - ltp) <= spikeVM
                                || instruments[instrument].middle30ma50 <= instruments[instrument].middle30BBnew))
                        {
                            // Do nothing
                        }
                        else
                        {
                            Console.WriteLine(
                                "At {0} Cautious 7z Average volume {2} and Current Volume {3} is aligning for Long call for the script {1} ltp {4}",
                                DateTime.Now.ToString(), instruments[instrument].futName,
                                instruments[instrument].AvgVolume, volume, ltp);
                            if (((DateTime.Now.Minute > 10 && DateTime.Now.Minute < 15)
                                 || (DateTime.Now.Minute > 40 && DateTime.Now.Minute < 45)
                                 || instruments[instrument].top30bb - instruments[instrument].bot30bb < variance25)
                                && ((instruments[instrument].middleBB - ltp) <= spikeVM))
                            {
                                // Do nothing
                            }
                            else if ((instruments[instrument].bot30bb + variance46) < instruments[instrument].top30bb
                                     && Decimal.Compare(timenow, Convert.ToDecimal(9.45)) > 0
                                     && ((instruments[instrument].middleBB - ltp) <= spikeVM)
                                     && (instruments[instrument].bot30bb < instruments[instrument].fBot30bb
                                         || IsBetweenVariance(instruments[instrument].bot30bb, instruments[instrument].fBot30bb, (decimal).0015)))
                            {
                                // Do nothing
                            }
                            else if (instruments[instrument].history30Min[instruments[instrument].history30Min.Count - 2].High > instruments[instrument].middle30BB
                                     && (instruments[instrument].middleBB - ltp) <= spikeVM
                                     && isGearingStatus
                                     && (instruments[instrument].bot30bb + variance25) <= instruments[instrument].top30bb)
                            {
                                // Do nothing
                            }
                            else if (ltp <= instruments[instrument].bot30bb
                                     && (Decimal.Compare(timenow, (decimal)10.17) > 0
                                         || !(instruments[instrument].isVolatile
                                              || isNiftyVolatile)))
                            {
                                instruments[instrument].type = OType.Buy;
                                instruments[instrument].canTrust = false;
                                instruments[instrument].canOrder = false;
                                instruments[instrument].longTrigger = instruments[instrument].bot30bb;
                                modifyOpenAlignOrReversedStatus(instruments[instrument], 16, OType.Buy, false);
                                qualified = true;
                            }
                        }
                    }
                }
                #endregion
                //&& instruments[instrument].top30bb - instruments[instrument].bot30bb >= variance23
                if (qualified
                    && !instruments[instrument].canTrust)
                {
                    if ((instruments[instrument].type == OType.Sell
                            && (ltp - instruments[instrument].middleBB > spikeV 
                                || IsBetweenVariance((ltp - instruments[instrument].middleBB), spikeV, (decimal).0006)))
                        || (instruments[instrument].type == OType.Buy
                            && (instruments[instrument].middleBB - ltp > spikeV
                                || IsBetweenVariance((instruments[instrument].middleBB - ltp), spikeV, (decimal).0006))))
                    {
                        //Do nothing
                    }
                    else if ((instruments[instrument].type == OType.Sell
                              && (IsBetweenVariance(instruments[instrument].top30bb, instruments[instrument].fTop30bb, (decimal).001)))
                             || (instruments[instrument].type == OType.Buy
                                 && (IsBetweenVariance(instruments[instrument].bot30bb, instruments[instrument].fBot30bb, (decimal).001))))
                    {
                        Console.WriteLine("This script {0} is Again Good to Proceed with the order with ltp {1}", instruments[instrument].futName, ltp);
                    }
                    else
                    {
                        gearingStatus = CheckGearingStatus(instrument, instruments[instrument].type);
                        if ((gearingStatus || instruments[instrument].goodToGo)
                            && isNiftyVolatile)
                        {
                            if (instruments[instrument].oldTime != instruments[instrument].currentTime)
                            {
                                instruments[instrument].oldTime = instruments[instrument].currentTime;
                                Console.WriteLine("This script {0} is Flying or Falling in the Morning", instruments[instrument].futName);
                            }
                            if (instruments[instrument].goodToGo)
                            {
                                spike = instruments[instrument].top30bb - instruments[instrument].bot30bb < variance17 + variance14 ? spikeV : spikeMM;
                                if ((instruments[instrument].type == OType.Sell
                                        //&& Math.Round(buyQuantity * 1.05) < sellQuantity
                                        && ltp - instruments[instrument].middleBB > spike)
                                    || (instruments[instrument].type == OType.Buy
                                        //&& buyQuantity < Math.Round(sellQuantity * 1.05)
                                        && instruments[instrument].middleBB - ltp > spike))
                                {
                                    if (gearingStatus
                                        && ((instruments[instrument].type == OType.Sell
                                             && ltp - instruments[instrument].middleBB < spikeV
                                             && buyQuantity > Math.Round(sellQuantity * 1.4))
                                            || (instruments[instrument].type == OType.Buy
                                                && instruments[instrument].middleBB - ltp < spikeV
                                                && Math.Round(buyQuantity * 1.4) < sellQuantity)))
                                    {
                                        qualified = false;
                                    }
                                }
                                else
                                {
                                    List<decimal> bols1 = GetMiddle30BBOf(instruments[instrument].history, 3);
                                    List<decimal> bols2 = GetMiddle30BBOf(instruments[instrument].history, 4);
                                    List<decimal> bols3 = GetMiddle30BBOf(instruments[instrument].history, 5);
                                    List<decimal> bols4 = GetMiddle30BBOf(instruments[instrument].history, 6);
                                    if ((instruments[instrument].type == OType.Sell
                                         //&& Math.Round(buyQuantity * 1.05) < sellQuantity
                                         && ltp - instruments[instrument].middleBB > spikeM)
                                        || (instruments[instrument].type == OType.Buy
                                            //&& buyQuantity < Math.Round(sellQuantity * 1.05)
                                            && instruments[instrument].middleBB - ltp > spikeM))
                                    {
                                        if (IsBeyondVariance(bols1[0], bols2[0], (decimal).0001)
                                            && IsBeyondVariance(bols2[0], bols3[0], (decimal).0001)
                                            && IsBeyondVariance(bols3[0], bols4[0], (decimal).0001))
                                        {

                                        }
                                        else
                                            qualified = false;
                                    }
                                    else
                                        qualified = false;
                                }
                            }
                            else
                                qualified = false;
                        }
                        else if (instruments[instrument].goodToGo)
                        {
                            //spike = instruments[instrument].top30bb - instruments[instrument].bot30bb < variance17 + variance14 ? spikeV : spikeMM;
                            if ((instruments[instrument].type == OType.Sell
                                 //&& Math.Round(buyQuantity * 1.05) < sellQuantity
                                 && ltp - instruments[instrument].middleBB > spikeMM)
                                || (instruments[instrument].type == OType.Buy
                                    //&& buyQuantity > Math.Round(sellQuantity * 1.05)
                                    && instruments[instrument].middleBB - ltp > spikeMM))
                            {
                                if (gearingStatus
                                    && ((instruments[instrument].type == OType.Sell
                                         && ltp - instruments[instrument].middleBB < spikeV
                                         && buyQuantity > Math.Round(sellQuantity * 1.4))
                                        || (instruments[instrument].type == OType.Buy
                                            && instruments[instrument].middleBB - ltp < spikeV
                                            && Math.Round(buyQuantity * 1.4) < sellQuantity)))
                                {
                                    qualified = false;
                                }
                            }
                            else
                                qualified = false;
                        }
                    }
                }

                #region Verify Current 30 min canlde movement
                if (qualified
                    && !instruments[instrument].canTrust)
                {
                    System.Threading.Thread.Sleep(400);
                    List<Historical> history = kite.GetHistoricalData(instrument.ToString(),
                                                DateTime.Now.Date.AddHours(9).AddMinutes(15),
                                                //DateTime.Now.Date.AddHours(9).AddMinutes(16),
                                                DateTime.Now.Date.AddDays(1),
                                                "30minute");
                    if (((history[history.Count - 1].Low < instruments[instrument].middle30BB
                                || IsBetweenVariance(history[history.Count - 1].Low, instruments[instrument].middle30BB, (decimal).0023))
                            && instruments[instrument].type == OType.Sell)
                        || ((history[history.Count - 1].High > instruments[instrument].middle30BB
                                || IsBetweenVariance(history[history.Count - 1].High, instruments[instrument].middle30BB, (decimal).0023))
                            && instruments[instrument].type == OType.Buy))
                    {
                        Console.WriteLine("At {0} Current Candle Candle of script {1}: is either running horse or falling knife & Current Candle time is {2}", DateTime.Now.ToString(), instruments[instrument].futName, history[history.Count - 1].TimeStamp.ToString());
                        if (!((DateTime.Now.Minute >= 13 && DateTime.Now.Minute <= 15)
                              || (DateTime.Now.Minute >= 43 && DateTime.Now.Minute <= 45)))
                        {
                            if (instruments[instrument].type == OType.Buy
                                && (instruments[instrument].bot30bb + variance25) <= instruments[instrument].top30bb)
                            {
                                //if (instruments[instrument].middleBB - ltp < spikeVVM) or
                                if (instruments[instrument].bot30bb - ltp <= spikeA)
                                    return false;
                                else
                                    Console.WriteLine("At ltp {0} of script {1} Worth a try buddy", ltp,
                                        instruments[instrument].futName);
                            }
                            else if (instruments[instrument].type == OType.Sell
                                     && (instruments[instrument].bot30bb + variance25) <=
                                     instruments[instrument].top30bb)
                            {
                                //if (ltp - instruments[instrument].middleBB < spikeVVM) or
                                if (ltp - instruments[instrument].top30bb <= spikeA)
                                    return false;
                                else
                                    Console.WriteLine("At ltp {0} of script {1} Worth a try buddy", ltp,
                                        instruments[instrument].futName);
                            }
                        }
                        CloseOrderTicker(instrument, false);
                        qualified = false;
                    }
                    if (history.Count > 2 && qualified)
                    {
                        if ((history[history.Count - 2].Low < instruments[instrument].middle30BB
                                    && instruments[instrument].type == OType.Sell)
                                || (history[history.Count - 2].High > instruments[instrument].middle30BB
                                    && instruments[instrument].type == OType.Buy))
                        {
                            Console.WriteLine("At {0} Current Err; Previous Candle of script {1}: is either running horse or falling knife & Current Candle time is {2}", DateTime.Now.ToString(), instruments[instrument].futName, history[history.Count - 1].TimeStamp.ToString());
                            if (((DateTime.Now.Minute >= 45 && DateTime.Now.Minute <= 51)
                                 || (DateTime.Now.Minute >= 15 && DateTime.Now.Minute <= 21)))
                            {
                                if (instruments[instrument].type == OType.Buy)
                                {
                                    if (history[history.Count - 2].High - history[history.Count - 2].Low >
                                        (decimal) spikeMN
                                        || history[history.Count - 2].Open - history[history.Count - 2].Close >
                                        (decimal) spikeNN)
                                    {
                                        Console.WriteLine("So Wait for Buy for Some more time..");
                                        qualified = false;
                                    }
                                }
                                else if (instruments[instrument].type == OType.Sell)
                                {
                                    if (history[history.Count - 2].High - history[history.Count - 2].Low >
                                        (decimal) spikeMN
                                        || history[history.Count - 2].Close - history[history.Count - 2].Open >
                                        (decimal) spikeNN)
                                    {
                                        Console.WriteLine("So Wait for Short for Some more time..");
                                        qualified = false;
                                    }
                                }
                            }
                            if (qualified)
                            {
                                if (((history[history.Count - 1].Low < instruments[instrument].middle30BB
                                      || IsBetweenVariance(history[history.Count - 1].Low,
                                          instruments[instrument].middle30BB, (decimal).0033))
                                     && instruments[instrument].type == OType.Sell)
                                    || ((history[history.Count - 1].High > instruments[instrument].middle30BB
                                         || IsBetweenVariance(history[history.Count - 1].High,
                                             instruments[instrument].middle30BB, (decimal).0033))
                                        && instruments[instrument].type == OType.Buy))
                                {
                                    if (instruments[instrument].type == OType.Buy)
                                    {
                                        if (instruments[instrument].bot30bb - ltp <= spikeA)
                                            return false;
                                        else
                                            Console.WriteLine("At ltp {0} of script {1} Worth a try buddy", ltp, instruments[instrument].futName);
                                    }
                                    else if (instruments[instrument].type == OType.Sell)
                                    {
                                        if (ltp - instruments[instrument].top30bb <= spikeA)
                                            return false;
                                        else
                                            Console.WriteLine("At ltp {0} of script {1} Worth a try buddy", ltp, instruments[instrument].futName);
                                    }
                                    //CloseOrderTicker(instrument, true);
                                    //qualified = false;
                                }
                            }
                        }
                    }
                    if (qualified)
                    {
                        if ((DateTime.Now.Minute >= 40 && DateTime.Now.Minute < 45)
                            || (DateTime.Now.Minute >= 10 && DateTime.Now.Minute < 15))
                        {
                            if (instruments[instrument].top30bb - instruments[instrument].bot30bb <= variance2
                                || IsBetweenVariance(instruments[instrument].top30bb - instruments[instrument].bot30bb, variance2, (decimal).001))
                            {
                                Console.WriteLine("At ltp {0} of script {1} is Raising or Falling at wrong time. So stopping the order", ltp, instruments[instrument].futName);
                                qualified = false;
                            }
                        }
                    }
                    if (history.Count > 2
                        && qualified
                        && (DateTime.Now.Minute == 45 || DateTime.Now.Minute == 15 || DateTime.Now.Minute == 46 || DateTime.Now.Minute == 16))
                    {
                        TimeSpan candleTime = history[history.Count - 2].TimeStamp.TimeOfDay;
                        TimeSpan timeDiff = DateTime.Now.TimeOfDay.Subtract(candleTime);
                        if (timeDiff.Minutes > 35 || timeDiff.Hours > 0)
                        {
                            Console.WriteLine("EXCEPTION in Candle Retrieval Time {0} Last Candle Not Found : Last Candle closed time is {1}", DateTime.Now.ToString(), history[history.Count - 2].TimeStamp.ToString());
                            qualified = false;
                        }
                        if (((history[history.Count - 2].Close > instruments[instrument].top30bb
                                    || IsBetweenVariance(history[history.Count - 2].Close, instruments[instrument].top30bb, (decimal).001))
                                && instruments[instrument].type == OType.Sell)
                            || ((history[history.Count - 2].Close < instruments[instrument].bot30bb
                                    || IsBetweenVariance(history[history.Count - 2].Close, instruments[instrument].bot30bb, (decimal).001))
                                && instruments[instrument].type == OType.Buy))
                        {
                            if ((instruments[instrument].bot30bb + variance25) > instruments[instrument].top30bb)
                            {
                                Console.WriteLine("Previous Candle is closed horribly beyond for script {0}: Last Candle closed time is {1}", DateTime.Now.ToString(), history[history.Count - 2].TimeStamp.ToString());
                                //CloseOrderTicker(instrument);
                                //return false;
                            }
                        }
                    }
                }
                #endregion
                #endregion
            }
            else if (instruments[instrument].status == Status.POSITION
                && Decimal.Compare(timenow, Convert.ToDecimal(9.45)) > 0)
            {
                try
                {
                    #region Verify and Modify Exit Trigger
                    try
                    {
                        Position pos = new Position();
                        if (!GetCurrentPNL(instruments[instrument].futId, ref pos))
                        {
                            Console.WriteLine("This script {0} position is not found. But this script status is POSITION ", instruments[instrument].futName);
                            return false;
                        }
                        decimal currenLoss = 0;
                        #region Check Required Exit and Double Required Exit
                        if (pos.Quantity > 0)
                        {
                            currenLoss = (ltp - instruments[instrument].triggerPrice) * instruments[instrument].lotSize;
                        }
                        else if (pos.Quantity < 0)
                        {
                            currenLoss = (instruments[instrument].triggerPrice - ltp) * instruments[instrument].lotSize;
                        }
                        else
                        {
                            Console.WriteLine("This script {0} is not having proper Quantity {1}; Position {2}", instruments[instrument].futName, pos.Quantity, pos.PNL);
                            if (pos.Quantity == 0
                                && !instruments[instrument].isReorder)
                            {
                                if (instruments[instrument].type == OType.Sell)
                                    InsertNewToken(instrument, instruments[instrument].futName, 0, 0, instruments[instrument].middleBB, "SELL", "BOOKED", instruments[instrument].type);
                                else
                                    InsertNewToken(instrument, instruments[instrument].futName, 0, 0, instruments[instrument].middleBB, "BUY", "BOOKED", instruments[instrument].type);
                                CloseOrderTicker(instrument, true);
                                return false;
                            }
                        }
                        if (instruments[instrument].isReorder
                            && instruments[instrument].oldTime != instruments[instrument].currentTime)
                        {
                            instruments[instrument].oldTime = instruments[instrument].currentTime;
                            Console.WriteLine("This script {0} position is a reorder. So not taking any action and leaving it to users choice with current loss {1}", instruments[instrument].futName, currenLoss);
                            return false;
                        }
                        if (instruments[instrument].oldTime != instruments[instrument].currentTime)
                        {
                            instruments[instrument].oldTime = instruments[instrument].currentTime;
                            Console.WriteLine("This script {0} with Trigger Price {1}; Current Price {2} & LTP {3}; Position {4} & cl {5}", instruments[instrument].futName, instruments[instrument].triggerPrice, pos.LastPrice, ltp, pos.PNL, currenLoss);
                        }
                        if (instruments[instrument].canTrust
                            && instruments[instrument].isReversed)
                        {
                            if ((DateTime.Now.Minute >= 14 && DateTime.Now.Minute < 15)
                                || (DateTime.Now.Minute >= 44 && DateTime.Now.Minute < 45))
                            {
                                if (instruments[instrument].type == OType.Buy)
                                {
                                    if (ltp < instruments[instrument].middle30BBnew
                                        && IsBeyondVariance(instruments[instrument].middle30BBnew, ltp, (decimal).0015)
                                        && instruments[instrument].history30Min[instruments[instrument].history30Min.Count - 2].Close >= instruments[instrument].middle30BB)
                                    {
                                        Console.WriteLine("This script is Last reversed. But after the order, the movement is opposite.");
                                        ModifyOrderForContract(pos, instrument, (decimal)350);
                                    }
                                }
                                else if (instruments[instrument].type == OType.Sell)
                                {
                                    if (ltp > instruments[instrument].middle30BBnew
                                        && IsBeyondVariance(instruments[instrument].middle30BBnew, ltp, (decimal).0015)
                                        && instruments[instrument].history30Min[instruments[instrument].history30Min.Count - 2].Close <= instruments[instrument].middle30BB)
                                    {
                                        Console.WriteLine("This script is Last reversed. But after the order, the movement is opposite.");
                                        ModifyOrderForContract(pos, instrument, (decimal)350);
                                    }
                                }
                            }
                        }
                        if (instruments[instrument].canTrust
                            && DateTime.Now.Subtract(instruments[instrument].orderTime).Minutes > 13)
                        {
                            if ((pos.Quantity > 0
                                    && CheckLateStatus(instrument, OType.Sell))
                                || (pos.Quantity < 0
                                    && CheckLateStatus(instrument, OType.Buy)))
                            {
                                Console.WriteLine("At {0} 1 Why not place Reverse Order for the position {1}. plan to Quit now and execute reverse immediately with current loss {2}", DateTime.Now.ToString(), instruments[instrument].futName, currenLoss);
                                ModifyOrderForContract(pos, instrument, (decimal)350);
                                if (DateTime.Now.Subtract(instruments[instrument].orderTime).Minutes > 23
                                    && currenLoss >= -400
                                    && IsBetweenVariance(ltp, instruments[instrument].middleBB, (decimal).0008))
                                {
                                    Console.WriteLine("At {0} Cancel now as the wait is too long for Position {1}", DateTime.Now.ToString(), instruments[instrument].futName);
                                    CancelOrder(pos, instrument, true);
                                }
                            }
                            if ((pos.Quantity > 0
                                    && buyQuantity < Math.Round(sellQuantity * 1.1, 0))
                                || (pos.Quantity < 0
                                    && sellQuantity < Math.Round(buyQuantity * 1.1, 0)))
                            {
                                Console.WriteLine("At {0} 2 Why not place Reverse Order for the position {1}. plan to Quit now and execute reverse immediately with as buy and sell quantities are as {2} & {3} respectively", DateTime.Now.ToString(), instruments[instrument].futName, buyQuantity, sellQuantity);
                                ModifyOrderForContract(pos, instrument, (decimal)350);
                            }
                        }
                        if (instruments[instrument].isLowVolume
                            && DateTime.Now.Subtract(instruments[instrument].orderTime).Minutes > 3
                            //&& instruments[instrument].requiredExit
                            && instruments[instrument].top30bb - instruments[instrument].bot30bb < (variance2 + variance14)
                            && !instruments[instrument].isReorder
                            && instruments[instrument].topBB - instruments[instrument].botBB <= spikeN)
                        {
                            if (currenLoss <= -1600 || pos.PNL <= -2000)
                            {
                                if (pos.PNL <= -2000)
                                    Console.WriteLine("This script is moving sideways at {0} :: The order status of the script {1} has gone serious state with {2}", DateTime.Now.ToString(), instruments[instrument].futName, pos.PNL);
                                else
                                    Console.WriteLine("This equity script {0} is moving sideways with current price {1} and average price {2} with PNL {3}", instruments[instrument].futName, pos.LastPrice, pos.AveragePrice, currenLoss);
                                if ((pos.Quantity < 0
                                        && sellQuantity > Math.Round(buyQuantity * 1.12, 0))
                                    || (pos.Quantity > 0
                                        && buyQuantity > Math.Round(sellQuantity * 1.12, 0)))
                                {
                                    Console.WriteLine("At {0} Script {1} Still the Buy or Sell call is intact with the original call. so waiting for better exit position as buy quantity {2} & sell quantity {3} ", DateTime.Now.ToString(), instruments[instrument].futName, buyQuantity, sellQuantity);
                                }
                                if ((pos.PNL >= -2500 || currenLoss >= -2200)
                                    && instruments[instrument].orderTime.AddMinutes(13) < DateTime.Now
                                    && ((instruments[instrument].type == OType.Sell
                                         && IsBetweenVariance(instruments[instrument].top30bb,
                                             instruments[instrument].fTop30bb, (decimal).002))
                                        || (instruments[instrument].type == OType.Buy
                                            && IsBetweenVariance(instruments[instrument].bot30bb,
                                                instruments[instrument].fBot30bb, (decimal).002))))
                                {
                                    Console.WriteLine("This ** equity script {0} daily Volume {1} and todays volume is {2}", instruments[instrument].futName, instruments[instrument].AvgVolume, volume);
                                }
                                else
                                {
                                    ModifyOrderForContract(pos, instrument, (decimal) 350);
                                    instruments[instrument].requiredExit = true;
                                }
                            }
                            if ((instruments[instrument].requiredExit)
                                || (instruments[instrument].canTrust
                                    && DateTime.Now.Subtract(instruments[instrument].orderTime).Minutes > 23
                                    && ((pos.Quantity > 0
                                            && prevCandleClose < instruments[instrument].middleBB
                                            && sellQuantity > Math.Round(buyQuantity * 1.12, 0))
                                        || (pos.Quantity < 0
                                            && prevCandleClose > instruments[instrument].middleBB
                                            && buyQuantity > Math.Round(sellQuantity * 1.12, 0)))))
                            {
                                if (((pos.Quantity > 0
                                        && buyQuantity > Math.Round(sellQuantity * 1.12, 0))
                                    || (pos.Quantity < 0
                                        && sellQuantity > Math.Round(buyQuantity * 1.12, 0)))
                                    && DateTime.Now.Subtract(instruments[instrument].orderTime).Minutes < 23
                                    && !instruments[instrument].requiredExit)
                                {
                                    return false;
                                }
                                else if (((pos.Quantity > 0
                                                && buyQuantity > Math.Round(sellQuantity * 1.15, 0))
                                            || (pos.Quantity < 0
                                                && sellQuantity > Math.Round(buyQuantity * 1.15, 0)))
                                        && instruments[instrument].requiredExit
                                        && currenLoss <= -900)
                                {
                                    return false;
                                }
                                else if (DateTime.Now.Subtract(instruments[instrument].orderTime).Minutes > 14
                                    && ((pos.Quantity > 0
                                                && buyQuantity > Math.Round(sellQuantity * 1.12, 0)
                                                && (ltp > instruments[instrument].middleBB
                                                    || IsBeyondVariance(instruments[instrument].middleBB, ltp, (decimal).0006)))
                                            || (pos.Quantity < 0
                                                && sellQuantity > Math.Round(buyQuantity * 1.12, 0)
                                                && (ltp < instruments[instrument].middleBB
                                                    || IsBeyondVariance(instruments[instrument].middleBB, ltp, (decimal).0006)))))
                                {
                                    return false;
                                }
                                else if (currenLoss >= 200)
                                {
                                    if ((pos.Quantity > 0
                                                && buyQuantity < Math.Round(sellQuantity * 1.1, 0))
                                            || (pos.Quantity < 0
                                                && sellQuantity < Math.Round(buyQuantity * 1.1, 0)))
                                    {
                                        ModifyOrderForContract(pos, instrument, (decimal)350);
                                    }
                                    return false;
                                }
                                Console.WriteLine("At {0} Please look for Reverse Order for the position {1}. Quit now and reverse order it immediately with current loss {2} with Buy {3}  & Sell {4} quantity", DateTime.Now.ToString(), instruments[instrument].futName, currenLoss, buyQuantity, sellQuantity);
                                instruments[instrument].isReorder = true;
                                CancelOrder(pos, instrument, false);
                                if (pos.Quantity != 0)
                                {
                                    instruments[instrument].isReorder = true;
                                    instruments[instrument].triggerPrice = ltp;
                                    if (pos.Quantity > 0)
                                    {
                                        instruments[instrument].type = OType.Sell;
                                    }
                                    else if (pos.Quantity < 0)
                                    {
                                        instruments[instrument].type = OType.Buy;
                                    }
                                    if ((pos.Quantity > 0
                                                && sellQuantity > Math.Round(buyQuantity * 1.05, 0))
                                            || (pos.Quantity < 0
                                                && buyQuantity > Math.Round(sellQuantity * 1.05, 0)))
                                        Console.WriteLine("At {0} Commented 1 ReOrder. Very Sorry", DateTime.Now.ToString());
                                    else
                                        Console.WriteLine("At {0} Commented 2 ReOrder. Very Sorry", DateTime.Now.ToString());
                                    //Thread.Sleep(1000);
                                    //placeOrder(instrument, 0);
                                }
                                CloseOrderTicker(instrument, true);
                                return false;
                            }
                        }
                        if (!instruments[instrument].requiredExit
                            && (pos.PNL <= -2000 || currenLoss <= -1600)
                            && !instruments[instrument].isReorder)
                        {
                            if (pos.PNL <= -2000)
                                Console.WriteLine("This script is moving sideways at {0} :: The order status of the script {1} has gone serious state with {2}", DateTime.Now.ToString(), instruments[instrument].futName, pos.PNL);
                            if (currenLoss <= -1600)
                                Console.WriteLine("This equity script {0} is moving sideways with ltp {1} and trigger price {2} with PNL {3}", instruments[instrument].futName, ltp, instruments[instrument].triggerPrice, currenLoss);
                            if ((pos.PNL >= -2500 || currenLoss >= -2200)
                                && DateTime.Now.Subtract(instruments[instrument].orderTime).Minutes < 6
                                && ((instruments[instrument].type == OType.Sell
                                     && IsBetweenVariance(instruments[instrument].top30bb,
                                         instruments[instrument].fTop30bb, (decimal).002))
                                    || (instruments[instrument].type == OType.Buy
                                        && IsBetweenVariance(instruments[instrument].bot30bb,
                                            instruments[instrument].fBot30bb, (decimal).002))
                                    || (instruments[instrument].type == OType.Sell
                                        //&& instruments[instrument].isHighVolume
                                        && instruments[instrument].canTrust
                                        && Math.Round(buyQuantity * 1.2) < sellQuantity)
                                    || (instruments[instrument].type == OType.Buy
                                        //&& instruments[instrument].isHighVolume
                                        && instruments[instrument].canTrust
                                        && Math.Round(sellQuantity * 1.1) < buyQuantity)))
                            {
                                Console.WriteLine("This ** equity script {0} BuyQty {1} and Sell Qty is {2}", instruments[instrument].futName, buyQuantity, sellQuantity);
                            }
                            Console.WriteLine("This equity script {0} daily Volume {1} and todays volume is {2}", instruments[instrument].futName, instruments[instrument].AvgVolume, volume);
                            instruments[instrument].requiredExit = true;
                            if (!instruments[instrument].canTrust)
                            {
                                Console.WriteLine("This equity script ltp is {0} middleBB is {1} ", ltp, instruments[instrument].middleBB);
                                if (pos.Quantity < 0
                                    && ltp - instruments[instrument].middleBB > spikeV)
                                    ModifyOrderForContract(pos, instrument, (decimal)1800);
                                else if (pos.Quantity > 0
                                         && instruments[instrument].middleBB - ltp > spikeV)
                                    ModifyOrderForContract(pos, instrument, (decimal)1800);
                                else
                                {
                                    ModifyOrderForContract(pos, instrument, (decimal)350);
                                }
                            }
                            else
                                ModifyOrderForContract(pos, instrument, (decimal)350);
                        }
                        if (!instruments[instrument].doubledrequiredExit
                            && DateTime.Now.Subtract(instruments[instrument].orderTime).Minutes <= 9
                            && instruments[instrument].requiredExit
                            && (pos.PNL <= -2600 || currenLoss <= -2600)
                            && instruments[instrument].oldTime != instruments[instrument].currentTime)
                        {
                            instruments[instrument].oldTime = instruments[instrument].currentTime;
                            Console.WriteLine("Consider placing average order now for the equity script {0} at ltp {3} daily Volume {1} and todays volume is {2}", instruments[instrument].futName, instruments[instrument].AvgVolume, volume, ltp);
                            if ((instruments[instrument].type == OType.Sell
                                 //&& instruments[instrument].isHighVolume
                                 && instruments[instrument].canTrust
                                 && Math.Round(buyQuantity * 1.2) < sellQuantity)
                                || (instruments[instrument].type == OType.Buy
                                    //&& instruments[instrument].isHighVolume
                                    && instruments[instrument].canTrust
                                    && Math.Round(sellQuantity * 1.1) < buyQuantity))
                            {
                                Console.WriteLine(
                                    "Consider placing average order now for the equity script {0} daily Volume {1} and todays volume is {2}",
                                    instruments[instrument].futName, instruments[instrument].AvgVolume, volume);
                                instruments[instrument].isReorder = true;
                                PlaceMISOrder(instrument, pos.LastPrice, instruments[instrument].type);
                            }
                        }
                        if (!instruments[instrument].doubledrequiredExit
                            && (pos.PNL <= -4600 || currenLoss <= -4200)
                            && !instruments[instrument].isReorder)
                        {
                            if (currenLoss <= -4200)
                                Console.WriteLine("3. OMG This script is bleeding RED at {0} :: The order status of the script {1} has gone Bad state with {2}", DateTime.Now.ToString(), instruments[instrument].futName, pos.PNL);
                            if (pos.PNL <= -4600)
                                Console.WriteLine("1. OMG This script is bleeding RED at {0} :: The order status of the script {1} has gone bad state with {2}", DateTime.Now.ToString(), instruments[instrument].futName, pos.PNL);
                            instruments[instrument].doubledrequiredExit = true;
                        }
                        if (!instruments[instrument].tripleRequiredExit
                            && (pos.PNL <= -6400 || currenLoss <= -6400)
                            && !instruments[instrument].isReorder)
                        {
                            if (currenLoss <= -6400)
                                Console.WriteLine("4. OMG This script is bleeding RED at {0} :: The order status of the script {1} has gone Worst state with {2}", DateTime.Now.ToString(), instruments[instrument].futName, pos.PNL);
                            if (pos.PNL <= -6400)
                                Console.WriteLine("2. OMG This script is bleeding RED at {0} :: The order status of the script {1} has gone seriously bad state with {2}", DateTime.Now.ToString(), instruments[instrument].futName, pos.PNL);
                            instruments[instrument].tripleRequiredExit = true;
                        }

                        if (instruments[instrument].doubledrequiredExit
                            && !instruments[instrument].isReorder)
                        {
                            if (instruments[instrument].tripleRequiredExit)
                            {
                                if (!instruments[instrument].canTrust
                                    && ((instruments[instrument].triggerPrice - instruments[instrument].middleBB >= spikeV
                                        && instruments[instrument].type == OType.Sell)
                                        || (instruments[instrument].middleBB - instruments[instrument].triggerPrice >= spikeV
                                            && instruments[instrument].type == OType.Buy)))
                                {
                                    ModifyOrderForContract(pos, instrument, (decimal)350);
                                }
                                else
                                {
                                    if (currenLoss > -3200 && volume > instruments[instrument].AvgVolume) // || pos.PNL >= -3500)
                                    {
                                        CancelOrder(pos, instrument, true);
                                        return false;
                                    }
                                    else if (currenLoss > -4700 && volume > instruments[instrument].AvgVolume * 1.3) // || pos.PNL >= -5000)
                                    {
                                        CancelOrder(pos, instrument, true);
                                        return false;
                                    }
                                    else if (volume > instruments[instrument].AvgVolume * 1.2)
                                    {
                                        CancelOrder(pos, instrument, true);
                                        return false;
                                    }
                                }
                            }
                            if (volume > instruments[instrument].AvgVolume / 1.2)
                            {
                                if (!instruments[instrument].canTrust
                                    && ((instruments[instrument].triggerPrice - instruments[instrument].middleBB >= spikeV
                                            && instruments[instrument].type == OType.Sell)
                                        || (instruments[instrument].middleBB - instruments[instrument].triggerPrice >= spikeV
                                            && instruments[instrument].type == OType.Buy)))
                                {
                                    ModifyOrderForContract(pos, instrument, (decimal)1300);
                                }
                                else
                                {
                                    if (currenLoss > -1600 && volume > instruments[instrument].AvgVolume) // || pos.PNL >= -2000)
                                    {
                                        CancelOrder(pos, instrument, true);
                                        return false;
                                    }
                                    else if (currenLoss > -300) // || pos.PNL >= -600)
                                    {
                                        CancelOrder(pos, instrument, true);
                                        return false;
                                    }
                                }
                            }
                            if (IsBetweenVariance(ltp, instruments[instrument].middleBB, (decimal).0004)
                                && !instruments[instrument].canTrust)
                            {
                                CancelOrder(pos, instrument, true);
                                return false;
                            }

                        }
                        #endregion

                        if (((pos.Quantity > 0 && IsBetweenVariance(ltp, instruments[instrument].bot30bb, (decimal).0004))
                                || (pos.Quantity < 0 && IsBetweenVariance(ltp, instruments[instrument].top30bb, (decimal).0004)))
                            && instruments[instrument].tripleRequiredExit
                            && DateTime.Now.Subtract(instruments[instrument].orderTime).Minutes > 13
                            && (pos.PNL > -5000 || currenLoss > -4700))
                        {
                            Console.WriteLine("Why not Cancel and place a reverse order here 1 at {0} with loss of {1}????", DateTime.Now.ToString(), pos.PNL);
                        }
                        if (DateTime.Now.Subtract(instruments[instrument].orderTime).Minutes > 29
                            && !instruments[instrument].canTrust
                            && !instruments[instrument].requiredExit
                            && !instruments[instrument].isReorder
                            && (pos.PNL > 900 || currenLoss > 600)
                            && ((pos.Quantity > 0
                                    && (ltp >= instruments[instrument].middleBB
                                        || IsBetweenVariance(ltp, instruments[instrument].middleBB, (decimal).0004)))
                                || (pos.Quantity < 0
                                    && (ltp <= instruments[instrument].middleBB
                                        || IsBetweenVariance(ltp, instruments[instrument].middleBB, (decimal).0004))))
                            && instruments[instrument].topBB - instruments[instrument].botBB > variance14)
                        {
                            Console.WriteLine("Cancelling this Order as the Wait is too long and unworthy");
                            CancelOrder(pos, instrument, true);
                            return false;
                        }
                        //decimal variance14 = Math.Round((ltp * (decimal)1.45) / 100, 1);
                        //decimal variance18 = Math.Round((ltp * (decimal)1.8) / 100, 1);
                        bool isOrderTime = ValidateOrderTime(instruments[instrument].orderTime);
                        bool gearingStatus = false;
                        if (!instruments[instrument].isReorder
                            && instruments[instrument].requiredExit
                            && DateTime.Now.Subtract(instruments[instrument].orderTime).Minutes > 8)
                        {
                            if (pos.Quantity > 0)
                            {
                                gearingStatus = CheckGearingStatus(instrument, OType.Sell);
                            }
                            else if (pos.Quantity < 0)
                            {
                                gearingStatus = CheckGearingStatus(instrument, OType.Buy);
                            }
                        }
                        if ((instruments[instrument].doubledrequiredExit //instruments[instrument].requiredExit
                                    || gearingStatus)
                                && !instruments[instrument].isReorder
                                && (instruments[instrument].botBB + variance14) > instruments[instrument].topBB
                                && DateTime.Now.Subtract(instruments[instrument].orderTime).Minutes > 7)
                        {
                            if ((currenLoss >= -350  // || pos.PNL >= -600)
                                    && ((pos.Quantity > 0
                                            && ltp >= instruments[instrument].middleBB)
                                        || (pos.Quantity < 0
                                            && ltp <= instruments[instrument].middleBB)))
                                || (pos.Quantity > 0
                                    && (ltp > instruments[instrument].topBB
                                        || IsBetweenVariance(ltp, instruments[instrument].topBB, (decimal).0004)))
                                || (pos.Quantity < 0
                                    && (ltp < instruments[instrument].botBB
                                        || IsBetweenVariance(ltp, instruments[instrument].botBB, (decimal).0004))))
                            {
                                Console.WriteLine("Cancelling now as the script is narrowed and almost at the best place to exit");
                                CancelOrder(pos, instrument, true);
                                if ((instruments[instrument].isLowVolume || gearingStatus) && pos.Quantity != 0)
                                {
                                    instruments[instrument].isReorder = true;
                                    instruments[instrument].triggerPrice = ltp;
                                    if (pos.Quantity > 0)
                                    {
                                        instruments[instrument].type = OType.Sell;
                                    }
                                    else if (pos.Quantity < 0)
                                    {
                                        instruments[instrument].type = OType.Buy;
                                    }
                                    if ((pos.Quantity > 0
                                         && sellQuantity > Math.Round(buyQuantity * 1.05, 0))
                                        || (pos.Quantity < 0
                                            && buyQuantity > Math.Round(sellQuantity * 1.05, 0)))
                                        Console.WriteLine("At {0} Commented 3 ReOrder. Very Sorry", DateTime.Now.ToString());
                                    else
                                        Console.WriteLine("At {0} Commented 4 ReOrder. Very Sorry", DateTime.Now.ToString());
                                    //Thread.Sleep(1000);
                                    //placeOrder(instrument, 0);
                                }
                                CloseOrderTicker(instrument, true);
                                return false;
                            }
                        }
                        if (instruments[instrument].doubledrequiredExit
                            && !instruments[instrument].isReorder)
                        {
                            if (IsBetweenVariance(ltp, instruments[instrument].middleBB, (decimal).0004)
                                && ((instruments[instrument].botBB + variance14) < instruments[instrument].topBB
                                    || currenLoss >= -1200))
                            // || pos.PNL >= -1500)
                            {
                                Console.WriteLine("Cancelling now as the script is not reversing anytime sooner");
                                CancelOrder(pos, instrument, true);
                                return false;
                            }
                            if (currenLoss >= -1200  // || pos.PNL >= -1500)
                                && isOrderTime
                                && instruments[instrument].tripleRequiredExit)
                            {
                                Console.WriteLine("Cancelling now as the loss ratio is substantially lower side");
                                CancelOrder(pos, instrument, true);
                                return false;
                            }
                            if (currenLoss >= -900  // || pos.PNL >= -1200)
                                && isOrderTime)
                            {
                                Console.WriteLine("Cancelling now as the loss ratio is substantially lower side");
                                CancelOrder(pos, instrument, true);
                                return false;
                            }
                            //int quantity = pos.Quantity;
                            if (currenLoss >= -500) // || pos.PNL >= -800)
                            {
                                Console.WriteLine("Cancelling now as the script has reached close enough from breakout value");
                                CancelOrder(pos, instrument, true);
                                Console.WriteLine("Why not place a reverse order here 2????");
                            }
                            /*
                            System.Threading.Thread.Sleep(3000);
                            instruments[instrument].canTrust = false;
                            instruments[instrument].status = Status.POSITION;
                            instruments[instrument].longTrigger = instruments[instrument].bot30bb;
                            instruments[instrument].shortTrigger = instruments[instrument].top30bb;
                            if (quantity > 0)
                            {
                                instruments[instrument].type = OType.Sell;
                            }
                            else if (quantity < 0)
                            {
                                instruments[instrument].type = OType.Buy;
                            }
                            placeOrder(instrument, 0);
                            */
                        }
                        if (instruments[instrument].lots == 0)
                        {
                            OType trend = CalculateSqueezedTrend(instruments[instrument].futName, instruments[instrument].history, 10);
                            #region ValidateIsExitRequired
                            if (instruments[instrument].requiredExit
                                && !instruments[instrument].isReorder)
                            {
                                try
                                {
                                    if (instruments[instrument].requiredExit
                                        || (pos.Quantity > 0 && trend == OType.StrongSell && ltp < (instruments[instrument].middle30BB - (instruments[instrument].middle30BB * (decimal).0006)))
                                        || (pos.Quantity < 0 && trend == OType.StrongBuy && ltp > (instruments[instrument].middle30BB + (instruments[instrument].middle30BB * (decimal).0006))))
                                    {
                                        TimeSpan candleTime = instruments[instrument].orderTime.TimeOfDay;
                                        TimeSpan timeDiff = DateTime.Now.TimeOfDay.Subtract(candleTime);
                                        if (timeDiff.Minutes > 5 || timeDiff.Hours > 0)
                                            ModifyOrderForContract(pos, instrument, (decimal)400);
                                        else
                                            ModifyOrderForContract(pos, instrument, (decimal)500);
                                    }
                                }
                                catch (Exception ex)
                                {
                                    Console.WriteLine("EXCEPTION in RequireExit validation at {0} with message {1}", DateTime.Now.ToString(), ex.Message);
                                }
                            }
                            #endregion

                            if (ValidateOrderTime(instruments[instrument].orderTime)
                                && !instruments[instrument].isReorder
                                && instruments[instrument].ma50 > 0)
                            {
                                #region Validate Waiting Period in next candle
                                if (pos.Quantity > 0 && trend == OType.StrongSell && ltp < (instruments[instrument].middle30BB - (instruments[instrument].middle30BB * (decimal).0006)))
                                {
                                    ModifyOrderForContract(pos, instrument, 500);
                                }
                                else if (pos.Quantity < 0 && trend == OType.StrongBuy && ltp > (instruments[instrument].middle30BB + (instruments[instrument].middle30BB * (decimal).0006)))
                                {
                                    ModifyOrderForContract(pos, instrument, 500);
                                }
                                if (pos.Quantity > 0 && pos.PNL < -300) //instruments[instrument].type == OType.Buy
                                {
                                    #region Cancel Buy Order
                                    if ((ltp < instruments[instrument].longTrigger
                                                && ltp < instruments[instrument].ma50)
                                            || instruments[instrument].requiredExit)
                                    {
                                        if (instruments[instrument].oldTime != instruments[instrument].currentTime)
                                        {
                                            Console.WriteLine("At {0} : The order of the script {1} is found and validating for modification based on PNL {2}", DateTime.Now.ToString(), instruments[instrument].futName, pos.PNL);
                                            instruments[instrument].oldTime = instruments[instrument].currentTime;
                                        }
                                        if ((pos.PNL > -2000
                                                || IsBetweenVariance(ltp, instruments[instrument].middleBB, (decimal).0006)
                                                || ltp > instruments[instrument].middleBB)
                                            || ((pos.PNL >= -3000 || IsBetweenVariance(ltp, instruments[instrument].middleBB, (decimal).0006))
                                            && instruments[instrument].requiredExit && instruments[instrument].doubledrequiredExit))
                                        {
                                            if (trend == OType.StrongSell || Decimal.Compare(timenow, (decimal)11.15) < 0)
                                            {
                                                if (instruments[instrument].oldTime != instruments[instrument].currentTime)
                                                {
                                                    Console.WriteLine("HARDEXIT NOW at {0} :: The BUY order status of the script {1} is better Exit point so EXIT NOW with loss of {2}", DateTime.Now.ToString(), instruments[instrument].futName, instruments[instrument].lotSize * (instruments[instrument].longTrigger - ltp));
                                                    instruments[instrument].oldTime = instruments[instrument].currentTime;
                                                }
                                                //CancelAndReOrder(instrument, OType.Buy, ltp, pos.PNL);
                                            }
                                        }
                                    }
                                    #endregion
                                }
                                else if (pos.Quantity < 0 && pos.PNL < -300) // instruments[instrument].type == OType.Sell
                                {
                                    #region Cancel Sell Order                                
                                    if ((ltp > instruments[instrument].shortTrigger
                                                && ltp > instruments[instrument].ma50)
                                            || instruments[instrument].requiredExit)
                                    {
                                        if (instruments[instrument].oldTime != instruments[instrument].currentTime)
                                        {
                                            instruments[instrument].oldTime = instruments[instrument].currentTime;
                                            Console.WriteLine("At {0} : The order of the script {1} is found and validating for modification based on PNL {2}", DateTime.Now.ToString(), instruments[instrument].futName, pos.PNL);
                                        }
                                        if ((pos.PNL > -2000
                                                || IsBetweenVariance(ltp, instruments[instrument].middleBB, (decimal).0006)
                                                || ltp < instruments[instrument].middleBB)
                                            || ((pos.PNL >= -3000 || IsBetweenVariance(ltp, instruments[instrument].middleBB, (decimal).0006))
                                            && instruments[instrument].requiredExit && instruments[instrument].doubledrequiredExit))
                                        {
                                            if (trend == OType.StrongBuy || Decimal.Compare(timenow, (decimal)11.15) < 0)
                                            {
                                                if (instruments[instrument].oldTime != instruments[instrument].currentTime)
                                                {
                                                    instruments[instrument].oldTime = instruments[instrument].currentTime;
                                                    Console.WriteLine("HARDEXIT NOW at {0} :: The SELL order status of the script {1} is better Exit point so EXIT NOW with loss of {2}", DateTime.Now.ToString(), instruments[instrument].futName, instruments[instrument].lotSize * (ltp - instruments[instrument].shortTrigger));
                                                }
                                                //CancelAndReOrder(instrument, OType.Sell, ltp, pos.PNL);
                                            }
                                        }
                                    }
                                    #endregion
                                }
                                else if (pos.Quantity == 0 && pos.PNL < -300)
                                {
                                    CloseOrderTicker(instrument, true);
                                }
                                #endregion
                            }
                            else if (instruments[instrument].requiredExit
                                && instruments[instrument].weekMA > 0
                                && instruments[instrument].ma50 > 0
                                && !instruments[instrument].isReorder)
                            {
                                #region Decide within same candle
                                try
                                {
                                    DateTime dt = Convert.ToDateTime(instruments[instrument].orderTime);
                                    //if (DateTime.Now > dt.AddMinutes(3)) {...
                                    if ((DateTime.Now.Minute >= 12 && DateTime.Now.Minute < 15)
                                        || (DateTime.Now.Minute >= 42 && DateTime.Now.Minute < 45)
                                        || (instruments[instrument].doubledrequiredExit
                                            && IsBetweenVariance(ltp, instruments[instrument].middle30BB, (decimal).0015)))
                                    {
                                        if (instruments[instrument].isReversed
                                            && instruments[instrument].requiredExit)
                                        {
                                            //decimal variance2 = (ltp * (decimal)2) / 100;
                                            if (pos.PNL < -1000 && pos.PNL > -2000
                                                && (instruments[instrument].bot30bb + variance2) < instruments[instrument].top30bb
                                                || (instruments[instrument].doubledrequiredExit
                                                    && IsBetweenVariance(ltp, instruments[instrument].middle30BB, (decimal).0015)))
                                            {
                                                if (pos.Quantity > 0 && ltp < instruments[instrument].ma50)
                                                {
                                                    Console.WriteLine("In VerifyLTP at {0} :: Processing the Order {1} as it has gone seriously bleeding state with {2}", DateTime.Now.ToString(), instruments[instrument].futName, pos.PNL);
                                                    //ProcessOpenPosition(pos, instrument, OType.Sell);
                                                }
                                                else if (pos.Quantity < 0 && ltp > instruments[instrument].ma50)
                                                {
                                                    Console.WriteLine("In VerifyLTP at {0} :: Processing the Order {1} as it has gone seriously bleeding state with {2}", DateTime.Now.ToString(), instruments[instrument].futName, pos.PNL);
                                                    //ProcessOpenPosition(pos, instrument, OType.Buy);
                                                }
                                            }
                                        }
                                    }
                                }
                                catch (Exception ex)
                                {
                                    Console.WriteLine("EXCEPTION in RequireExit Time validation at {0} with message {1}", DateTime.Now.ToString(), ex.Message);
                                }
                                #endregion
                            }
                            else if (instruments[instrument].isReorder)
                            {
                                #region reorder
                                if (instruments[instrument].oldTime != instruments[instrument].currentTime)
                                {
                                    instruments[instrument].oldTime = instruments[instrument].currentTime;
                                    Console.WriteLine("In VerifyLTP at {0} This is a Reverse Order of {1} current state is as follows", DateTime.Now.ToString(), instruments[instrument].futName);
                                }
                                //OType currentTrend = CalculateSqueezedTrend(instruments[instrument].futName, instruments[instrument].history, 10);
                                if (IsBetweenVariance(ltp, instruments[instrument].dma, (decimal).0006))
                                {
                                    ModifyOrderForContract(pos, (uint)instruments[instrument].futId, 600);
                                    Console.WriteLine("Time to Exit For contract Immediately for the current reversed order of {0} which is placed at {1} should i revise target to {2}", pos.TradingSymbol, pos.AveragePrice);
                                }
                                #endregion
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine("at {0} EXCEPTION in VerifyLTP_POSITION :: The order status of the script {1} is being validated but recieved exception {2}", DateTime.Now.ToString(), instruments[instrument].futName, ex.Message);
                    }
                    #endregion
                }
                catch (Exception ex)
                {
                    Console.WriteLine("EXCEPTION :: As expected, You have clearly messed it somewhere with new logic; {0}", ex.Message);
                    instruments[instrument].oldTime = instruments[instrument].currentTime;
                }
            }
            else if (instruments[instrument].bot30bb > 0
                    && instruments[instrument].middle30BBnew > 0
                    && Decimal.Compare(timenow, Convert.ToDecimal(9.45)) > 0)
            //&& instruments[instrument].currentTime != instruments[instrument].oldTime)
            {
                #region Check for Standing Orders
                try
                {
                    Position pos = new Position();
                    if (GetCurrentPNL(instruments[instrument].futId, ref pos)
                        && !instruments[instrument].isReorder)
                    {
                        Console.WriteLine("This script {0} position is not found 2. But this script status is STANDING/POSITION ", instruments[instrument].futName);
                        Order order = new Order();
                        if (GetCurrentOrder(instruments[instrument].futId, "COMPLETE", ref order)) //OPEN
                        {
                            Order posOrder = new Order();
                            if (GetCurrentOrder(instruments[instrument].futId, "OPEN", ref posOrder))
                            {
                                instruments[instrument].status = Status.POSITION;
                            }
                            else if (order.OrderTimestamp.Value.AddMinutes(1) < DateTime.Now)
                            {
                                Console.WriteLine("At {0} this script {1} order is still in Standing State though the Order is Complete with order time {2}", DateTime.Now, instruments[instrument].futName, order.OrderTimestamp.Value.ToString());
                                if (!GetCurrentOrder(instruments[instrument].futId, "OPEN", ref order))
                                {
                                    Console.WriteLine("ERROR & WARNING at {0} this script {1} order does not have TARGET ORDER with target {2}", DateTime.Now, instruments[instrument].futName, instruments[instrument].target);
                                }
                                if (instruments[instrument].type == OType.Sell)
                                    InsertNewToken(instrument, instruments[instrument].futName, ltp, change,
                                        instruments[instrument].triggerPrice, "SELL", "InProgress", instruments[instrument].type);
                                else
                                    InsertNewToken(instrument, instruments[instrument].futName, ltp, change,
                                        instruments[instrument].triggerPrice, "BUY", "InProgress", instruments[instrument].type);
                                instruments[instrument].status = Status.POSITION;
                                instruments[instrument].orderTime = order.OrderTimestamp.Value;
                                modifyOrderInCSV(instrument, instruments[instrument].futName, instruments[instrument].type, Status.POSITION);
                                if (order.TransactionType == Constants.TRANSACTION_TYPE_BUY)
                                {
                                    PlaceMISOrder(instrument, order.AveragePrice + instruments[instrument].target,
                                        OType.Sell);
                                }
                                else if (order.TransactionType == Constants.TRANSACTION_TYPE_SELL)
                                {
                                    PlaceMISOrder(instrument, order.AveragePrice - instruments[instrument].target,
                                        OType.Buy);
                                }
                            }
                        }
                    }
                    else if (instruments[instrument].status == Status.STANDING)
                    {
                        if (instruments[instrument].type == OType.Sell)
                            InsertNewToken(instrument, instruments[instrument].futName, ltp, change,
                                instruments[instrument].triggerPrice, "SELL", "InProgress", instruments[instrument].type);
                        else
                            InsertNewToken(instrument, instruments[instrument].futName, ltp, change,
                                instruments[instrument].triggerPrice, "BUY", "InProgress", instruments[instrument].type);
                        #region Check for Standing Orders
                        try
                        {
                            //instruments[instrument].oldTime = instruments[instrument].currentTime;
                            DateTime dt = DateTime.Now;
                            int counter = 0;
                            //bool isFound = false;
                            foreach (Order order in kite.GetOrders())
                            {
                                if (instruments[instrument].futId == order.InstrumentToken)
                                {
                                    //isFound = true;
                                    if (order.Status == "COMPLETE")
                                    {
                                        counter++;
                                        break;
                                    }
                                    else if (order.Status == "OPEN")
                                    {
                                        dt = Convert.ToDateTime(order.OrderTimestamp);
                                    }
                                    if (DateTime.Now > dt.AddMinutes(2))
                                    {
                                        try
                                        {
                                            Console.WriteLine("Getting OPEN Order Time {0} & Current Time {1} of {2} is more than than 6 minutes. So cancelling the order ID {3} though LTP is {4}", order.OrderTimestamp, DateTime.Now.ToString(), instruments[instrument].futName, order.OrderId, ltp);
                                            if (order.Variety != Constants.VARIETY_BO
                                                && DateTime.Now <= dt.AddMinutes(14)
                                                && instruments[instrument].tries < 3
                                                && (!instruments[instrument].canTrust
                                                    || (IsBetweenVariance(ltp, instruments[instrument].triggerPrice, (decimal).0015)
                                                        || instruments[instrument].topBB - instruments[instrument].botBB > spike
                                                        || (instruments[instrument].type == OType.Buy && ltp < instruments[instrument].triggerPrice)
                                                        || (instruments[instrument].type == OType.Sell && ltp > instruments[instrument].triggerPrice))))
                                            {
                                                ////instruments[instrument].isReorder = false;
                                                ////kite.CancelOrder(order.OrderId, Variety: Constants.VARIETY_REGULAR);
                                                //kite.ModifyOrder(order.OrderId, OrderType: Constants.ORDER_TYPE_MARKET);
                                                //or
                                                Quote futltp = new Quote();
                                                try
                                                {
                                                    Dictionary<string, Quote> dicLtp = kite.GetQuote(new string[] { instruments[instrument].futId.ToString() });
                                                    dicLtp.TryGetValue(instruments[instrument].futId.ToString(), out futltp);
                                                }
                                                catch (Exception ex)
                                                {
                                                    Console.WriteLine("EXCEPTION CAUGHT while Placing Order Trigger :: " + ex.Message);
                                                    kite.ModifyOrder(order.OrderId, OrderType: Constants.ORDER_TYPE_MARKET);
                                                    return false;
                                                }

                                                if (instruments[instrument].canTrust)
                                                {
                                                    instruments[instrument].tries++;
                                                    if (instruments[instrument].topBB - instruments[instrument].botBB >
                                                        spike)
                                                        instruments[instrument].tries++;
                                                    if (instruments[instrument].type == OType.Buy
                                                        && !instruments[instrument].isHighVolume
                                                        && order.Price < futltp.Bids[0].Price
                                                        && !instruments[instrument].futName.Contains("POWER"))
                                                    {
                                                        //instruments[instrument].triggerPrice = ltp;
                                                        //kite.ModifyOrder(order.OrderId, Price: futltp.Bids[0].Price);
                                                        Console.WriteLine("We could have modified the current Price to {0} for Script {1}", futltp.Bids[0].Price, instruments[instrument].futName);
                                                    }
                                                    else if (instruments[instrument].type == OType.Sell
                                                            && !instruments[instrument].isHighVolume
                                                            && order.Price > futltp.Offers[0].Price
                                                            && !instruments[instrument].futName.Contains("POWER"))
                                                    {
                                                        //instruments[instrument].triggerPrice = ltp;
                                                        //kite.ModifyOrder(order.OrderId, Price: futltp.Offers[0].Price);
                                                        Console.WriteLine("We could have modified the current Price to {0} for Script {1}", futltp.Offers[0].Price, instruments[instrument].futName);
                                                    }
                                                    else
                                                    {
                                                        if (instruments[instrument].futName.Contains("POWER"))
                                                        {
                                                            instruments[instrument].tries--;
                                                            if (instruments[instrument].type == OType.Buy
                                                                && order.Price == futltp.Bids[0].Price)
                                                            {
                                                                if (IsBetweenVariance(order.Price,
                                                                    futltp.Offers[0].Price,
                                                                    (decimal).006))
                                                                {
                                                                    if (instruments[instrument].tries < 1)
                                                                    {
                                                                        instruments[instrument].tries++;
                                                                        instruments[instrument].triggerPrice = ltp;
                                                                        kite.ModifyOrder(order.OrderId,
                                                                            Price: futltp.Offers[0].Price);
                                                                    }
                                                                }
                                                            }
                                                            else if (instruments[instrument].type == OType.Sell
                                                                     && order.Price == futltp.Offers[0].Price)
                                                            {
                                                                if (IsBetweenVariance(order.Price,
                                                                    futltp.Bids[0].Price,
                                                                    (decimal).006))
                                                                {
                                                                    if (instruments[instrument].tries < 1)
                                                                    {
                                                                        instruments[instrument].tries++;
                                                                        instruments[instrument].triggerPrice = ltp;
                                                                        kite.ModifyOrder(order.OrderId,
                                                                            Price: futltp.Bids[0].Price);
                                                                    }
                                                                }
                                                            }
                                                        }
                                                    }
                                                }
                                                else
                                                {
                                                    if ((instruments[instrument].type == OType.Buy
                                                            && IsBeyondVariance(order.Price, futltp.Offers[0].Price, (decimal).001))
                                                        || (instruments[instrument].type == OType.Sell
                                                            && IsBeyondVariance(order.Price, futltp.Bids[0].Price, (decimal).001)))
                                                    {
                                                        Console.WriteLine("Going for Market order though the variance between price and offer is beyond 1% ");
                                                    }
                                                    if (DateTime.Now > dt.AddMinutes(4)
                                                        || ((instruments[instrument].type == OType.Buy
                                                                && order.Price < futltp.Bids[2].Price
                                                                && IsBeyondVariance(order.Price, futltp.Bids[2].Price, (decimal).001))
                                                            || (instruments[instrument].type == OType.Sell
                                                                && order.Price > futltp.Offers[2].Price
                                                                && IsBeyondVariance(order.Price, futltp.Offers[2].Price, (decimal).001))))
                                                    {
                                                        if (instruments[instrument].topBB - instruments[instrument].botBB > spike
                                                            || ((instruments[instrument].type == OType.Buy
                                                                    && instruments[instrument].middleBB - instruments[instrument].bot30bb > spikeN + spike2)
                                                                || (instruments[instrument].type == OType.Sell
                                                                    && instruments[instrument].top30bb - instruments[instrument].middleBB > spikeN + spike2)))
                                                        {
                                                            instruments[instrument].triggerPrice = ltp;
                                                            kite.ModifyOrder(order.OrderId, OrderType: Constants.ORDER_TYPE_MARKET);
                                                        }
                                                        else
                                                            Console.WriteLine("Not Going for Market order as the Bollinger range is too narrowed at topBB {0} & botBB {1}", instruments[instrument].topBB, instruments[instrument].botBB);
                                                    }
                                                }
                                            }
                                            else if (order.Variety == Constants.VARIETY_BO
                                                    && (instruments[instrument].futName.Contains("POWER")
                                                    || DateTime.Now > dt.AddMinutes(13)))
                                                kite.CancelOrder(order.OrderId, Variety: "bo");
                                            else if (order.Variety == Constants.VARIETY_REGULAR
                                                    && DateTime.Now > dt.AddMinutes(14))
                                                kite.CancelOrder(order.OrderId, Variety: Constants.VARIETY_REGULAR);
                                            return false;
                                        }
                                        catch (Exception ex)
                                        {
                                            Console.WriteLine("EXCEPTION at {0}:: Cancelling Idle Order for 20 minutes is failed with message {1}", DateTime.Now.ToString(), ex.Message);
                                        }
                                    }
                                }
                            }
                            //if (!isFound)
                            //    instruments[instrument].status = Status.OPEN;
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine("at {0} EXCEPTION in VerifyLTP_STANDING :: The order status of the script {1} is being validated but recieved exception {2}", DateTime.Now.ToString(), instruments[instrument].futName, ex.Message);
                        }
                        #endregion
                    }
                    else
                    {
                        CloseOrderTicker(instrument, true);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine("at {0} EXCEPTION in VerifyLTP_STANDING :: The order status of the script {1} is being validated but recieved exception {2}", DateTime.Now.ToString(), instruments[instrument].futName, ex.Message);
                }
                #endregion
            }
            return qualified;
        }

        private bool ValidateOrderTime(DateTime orderDateTime)
        {
            TimeSpan timeDifference;
            if (DateTime.Now > orderDateTime.AddMinutes(32))
                return true;
            TimeSpan orderTime = orderDateTime.TimeOfDay;
            switch (orderTime.Minutes)
            {
                case 31:
                case 1:
                    timeDifference = DateTime.Now.TimeOfDay.Subtract(orderTime);
                    return timeDifference.TotalMinutes > 14;
                case 32:
                case 2:
                    timeDifference = DateTime.Now.TimeOfDay.Subtract(orderTime);
                    return timeDifference.TotalMinutes > 13;
                case 33:
                case 3:
                    timeDifference = DateTime.Now.TimeOfDay.Subtract(orderTime);
                    return timeDifference.TotalMinutes > 12;
                case 34:
                case 4:
                    timeDifference = DateTime.Now.TimeOfDay.Subtract(orderTime);
                    return timeDifference.TotalMinutes > 11;
                case 35:
                case 5:
                    timeDifference = DateTime.Now.TimeOfDay.Subtract(orderTime);
                    return timeDifference.TotalMinutes > 10;
                case 36:
                case 6:
                    timeDifference = DateTime.Now.TimeOfDay.Subtract(orderTime);
                    return timeDifference.TotalMinutes > 9;
                case 37:
                case 7:
                    timeDifference = DateTime.Now.TimeOfDay.Subtract(orderTime);
                    return timeDifference.TotalMinutes > 8;
                case 38:
                case 8:
                    timeDifference = DateTime.Now.TimeOfDay.Subtract(orderTime);
                    return timeDifference.TotalMinutes > 7;
                case 39:
                case 9:
                    timeDifference = DateTime.Now.TimeOfDay.Subtract(orderTime);
                    return timeDifference.TotalMinutes > 6;
                case 40:
                case 10:
                    timeDifference = DateTime.Now.TimeOfDay.Subtract(orderTime);
                    return timeDifference.TotalMinutes > 5;
                case 41:
                case 11:
                    timeDifference = DateTime.Now.TimeOfDay.Subtract(orderTime);
                    return timeDifference.TotalMinutes > 4;
                case 42:
                case 12:
                    timeDifference = DateTime.Now.TimeOfDay.Subtract(orderTime);
                    return timeDifference.TotalMinutes > 3;
                case 43:
                case 13:
                    timeDifference = DateTime.Now.TimeOfDay.Subtract(orderTime);
                    return timeDifference.TotalMinutes > 2;
                case 44:
                case 14:
                    timeDifference = DateTime.Now.TimeOfDay.Subtract(orderTime);
                    return timeDifference.TotalMinutes > 1;
                case 45:
                case 15:
                    timeDifference = DateTime.Now.TimeOfDay.Subtract(orderTime);
                    return timeDifference.TotalMinutes > 30;
                case 46:
                case 16:
                    timeDifference = DateTime.Now.TimeOfDay.Subtract(orderTime);
                    return timeDifference.TotalMinutes > 29;
                case 47:
                case 17:
                    timeDifference = DateTime.Now.TimeOfDay.Subtract(orderTime);
                    return timeDifference.TotalMinutes > 28;
                case 48:
                case 18:
                    timeDifference = DateTime.Now.TimeOfDay.Subtract(orderTime);
                    return timeDifference.TotalMinutes > 27;
                case 49:
                case 19:
                    timeDifference = DateTime.Now.TimeOfDay.Subtract(orderTime);
                    return timeDifference.TotalMinutes > 26;
                case 50:
                case 20:
                    timeDifference = DateTime.Now.TimeOfDay.Subtract(orderTime);
                    return timeDifference.TotalMinutes > 25;
                case 51:
                case 21:
                    timeDifference = DateTime.Now.TimeOfDay.Subtract(orderTime);
                    return timeDifference.TotalMinutes > 24;
                case 52:
                case 22:
                    timeDifference = DateTime.Now.TimeOfDay.Subtract(orderTime);
                    return timeDifference.TotalMinutes > 23;
                case 53:
                case 23:
                    timeDifference = DateTime.Now.TimeOfDay.Subtract(orderTime);
                    return timeDifference.TotalMinutes > 22;
                case 54:
                case 24:
                    timeDifference = DateTime.Now.TimeOfDay.Subtract(orderTime);
                    return timeDifference.TotalMinutes > 21;
                case 55:
                case 25:
                    timeDifference = DateTime.Now.TimeOfDay.Subtract(orderTime);
                    return timeDifference.TotalMinutes > 20;
                case 56:
                case 26:
                    timeDifference = DateTime.Now.TimeOfDay.Subtract(orderTime);
                    return timeDifference.TotalMinutes > 19;
                case 57:
                case 27:
                    timeDifference = DateTime.Now.TimeOfDay.Subtract(orderTime);
                    return timeDifference.TotalMinutes > 18;
                case 58:
                case 28:
                    timeDifference = DateTime.Now.TimeOfDay.Subtract(orderTime);
                    return timeDifference.TotalMinutes > 17;
                case 59:
                case 29:
                    timeDifference = DateTime.Now.TimeOfDay.Subtract(orderTime);
                    return timeDifference.TotalMinutes > 16;
                case 30:
                case 0:
                    timeDifference = DateTime.Now.TimeOfDay.Subtract(orderTime);
                    return timeDifference.TotalMinutes > 15;
            }
            return false;
        }

        public void ModifyOrderForContract(Position pos, uint token, decimal target)
        {
            try
            {
                foreach (Order order in kite.GetOrders())
                {
                    if (pos.InstrumentToken == order.InstrumentToken)
                    {
                        if (order.Status == "OPEN") // && DateTime.Now > dt.AddMinutes(4))
                        {
                            try
                            {
                                decimal averagePrice = pos.AveragePrice;
                                decimal trigger = Convert.ToDecimal((target / (decimal)order.Quantity).ToString("#.#"));
                                if (Decimal.Compare(trigger, Convert.ToDecimal(0.1)) < 0)
                                {
                                    trigger = trigger + (decimal).05;
                                }
                                string avg = averagePrice.ToString();
                                if (avg.Contains(".") && avg.IndexOf('.') != avg.Length - 2)
                                {
                                    if (order.TransactionType == "BUY")
                                        averagePrice = Convert.ToDecimal(avg.Substring(0, avg.IndexOf('.') + 2));
                                    else
                                        averagePrice = Convert.ToDecimal(avg.Substring(0, avg.IndexOf('.') + 2)) + (decimal).1;
                                }
                                if (order.TransactionType == "SELL" && (averagePrice + trigger) < order.Price)
                                {
                                    Console.WriteLine("Need to think on EXIT NOW at {0} :: The Buy order status of the script {1} is set to {2}", DateTime.Now.ToString(), instruments[token].futName, (averagePrice + trigger));
                                    kite.ModifyOrder(order.OrderId, Price: (decimal)(averagePrice + trigger));
                                    break;
                                }
                                else if (order.TransactionType == "BUY" && (averagePrice - trigger) > order.Price)
                                {
                                    Console.WriteLine("Need to think on EXIT NOW at {0} :: The Sell order status of the script {1} is set to {2}", DateTime.Now.ToString(), instruments[token].futName, (averagePrice - trigger));
                                    kite.ModifyOrder(order.OrderId, Price: (decimal)(averagePrice - trigger));
                                    break;
                                }
                                else
                                    break;
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine("EXCEPTION WHILE MODIFYING order with message {0}", ex.Message);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Exception in ModifyorderforContract at {0} :: with message {1} for script {2}", DateTime.Now.ToString(), ex.Message, instruments[token].futName);
            }
        }

        public void CancelOrder(Position pos, uint token, bool immediate)
        {
            try
            {
                foreach (Order order in kite.GetOrders())
                {
                    if (pos.InstrumentToken == order.InstrumentToken)
                    {
                        if (order.Status == "OPEN") // && DateTime.Now > dt.AddMinutes(4))
                        {
                            Console.WriteLine("CANCELLED at {0} AS THE Script {1} needs to be exited immediately with minimal loss", DateTime.Now.ToString(), instruments[token].futName);
                            if (order.Variety != Constants.VARIETY_BO)
                            {
                                //kite.CancelOrder(order.OrderId, Variety: Constants.VARIETY_REGULAR); // For BO
                                if (immediate)
                                    kite.ModifyOrder(order.OrderId, OrderType: Constants.ORDER_TYPE_MARKET); // As there is no Reorder for now
                                else
                                {
                                    Quote futltp = new Quote();
                                    try
                                    {
                                        Dictionary<string, Quote> dicLtp = kite.GetQuote(new string[]
                                            {instruments[token].futId.ToString()});
                                        dicLtp.TryGetValue(instruments[token].futId.ToString(), out futltp);
                                    }
                                    catch (Exception ex)
                                    {
                                        Console.WriteLine("EXCEPTION CAUGHT while Cancelling the Open Trigger :: " +
                                                          ex.Message);
                                        kite.ModifyOrder(order.OrderId, OrderType: Constants.ORDER_TYPE_MARKET);
                                        break;
                                    }

                                    if (order.TransactionType == Constants.TRANSACTION_TYPE_BUY)
                                        kite.ModifyOrder(order.OrderId, Price: futltp.Bids[0].Price);
                                    else if (order.TransactionType == Constants.TRANSACTION_TYPE_SELL)
                                        kite.ModifyOrder(order.OrderId, Price: futltp.Offers[0].Price);
                                    else
                                    {
                                        Console.WriteLine("Chose the latest price to Cancel the order {0}",
                                            futltp.LastPrice);
                                        kite.ModifyOrder(order.OrderId, Price: futltp.LastPrice);
                                    }
                                }
                            }
                            else
                                kite.CancelOrder(order.OrderId, Variety: "bo");
                            break;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Exception in ModifyorderforContract at {0} :: with message {1} for script {2}", DateTime.Now.ToString(), ex.Message, instruments[token].futName);
            }
        }

        private void CancelAndReOrder(uint token, OType type, decimal ltp, decimal pnl)
        {
            System.Threading.Thread.Sleep(400);
            DateTime previousDay;
            DateTime currentDay;
            getDays(out previousDay, out currentDay);
            List<Historical> history = kite.GetHistoricalData(token.ToString(),
                                        previousDay,
                                        currentDay.AddDays(1),
                                        "30minute");

            decimal timenow = DateTime.Now.Hour + ((decimal)DateTime.Now.Minute / 100);
            decimal variance14 = (history[history.Count - 2].Close * (decimal)1.4) / 100;
            decimal variance2 = (history[history.Count - 2].Close * (decimal)2) / 100;
            Position pos = new Position();
            if (!GetCurrentPNL(instruments[token].futId, ref pos))
            {
                Console.WriteLine("This script {0} position is not found to cancel order. But this script status is POSITION ", instruments[token].futName);
                return;
            }
            if (!instruments[token].canTrust)
            {
                if (instruments[token].shortTrigger > 0
                    && instruments[token].longTrigger > 0
                    && instruments[token].requiredExit)
                {
                    if (((history[history.Count - 2].Close >= instruments[token].shortTrigger
                        || IsBetweenVariance(history[history.Count - 2].Close, instruments[token].shortTrigger, (decimal).001))
                        && instruments[token].type == OType.Sell)
                        ||
                        ((history[history.Count - 2].Close <= instruments[token].longTrigger
                            || IsBetweenVariance(history[history.Count - 2].Close, instruments[token].longTrigger, (decimal).001)
                        && instruments[token].type == OType.Buy)))
                    {
                        if (instruments[token].type == OType.Sell)
                            Console.WriteLine("POSITION : Previous Candle close {1} is too close to the short trigger {2}: Modifying the Position of {0}", instruments[token].futName, history[history.Count - 2].Close, instruments[token].shortTrigger);
                        else if (instruments[token].type == OType.Buy)
                            Console.WriteLine("POSITION : Previous Candle close {1} is too close to the long trigger {2}: Modifying the Position of {0}", instruments[token].futName, history[history.Count - 2].Close, instruments[token].longTrigger);
                        if (pos.PNL > -500)
                        {
                            //if (instruments[token].type == OType.Sell && history[history.Count - 2].Close > instruments[token].shortTrigger)
                            //    CancelOrder(pos, token);
                            //else if (instruments[token].type == OType.Buy && history[history.Count - 2].Close < instruments[token].longTrigger)
                            //    CancelOrder(pos, token);
                            //else
                                ModifyOrderForContract(pos, token, 300);
                        }
                        else
                            ModifyOrderForContract(pos, token, 300);
                    }
                    else
                    {
                        Console.WriteLine("POSITION : Too long to wait for the order to close: Modifying the Position of {0}", instruments[token].futName);
                        if ((instruments[token].botBB + variance14) > instruments[token].topBB)
                            ModifyOrderForContract(pos, token, 1300);
                        else
                            ModifyOrderForContract(pos, token, 1400);
                    }
                }
            }
            else if (type == OType.Sell
                    && instruments[token].requiredExit
                    && instruments[token].doubledrequiredExit
                    && IsBetweenVariance(ltp, instruments[token].middleBB, (decimal).0008))
            {
                if ((history[history.Count - 2].Close > instruments[token].shortTrigger
                        && instruments[token].shortTrigger > 0
                        && !instruments[token].isReorder)
                    || (instruments[token].requiredExit
                        && history[history.Count - 2].Close > instruments[token].weekMA
                        && !instruments[token].isReorder
                        && !instruments[token].isReversed)
                    || (Decimal.Compare(timenow, Convert.ToDecimal(12.45)) < 0
                        && history[history.Count - 2].Close > instruments[token].shortTrigger
                        && instruments[token].shortTrigger > 0
                        && instruments[token].isReorder
                        && instruments[token].isReversed
                        && (((instruments[token].bot30bb + variance2) > instruments[token].top30bb)
                            || IsBetweenVariance((instruments[token].bot30bb + variance2), instruments[token].top30bb, (decimal).0006)))
                    || (instruments[token].requiredExit
                        && instruments[token].doubledrequiredExit
                        && (IsBetweenVariance(ltp, instruments[token].middleBB, (decimal).0006)
                            || pnl > -2700)))
                {
                    Console.WriteLine("POSITION : Averting Candle: Modifying the Position of {0} and Revising BUY trigger if Necessary", instruments[token].futName);
                    if (history.Count >= 3
                        && instruments[token].isReversed
                        && !instruments[token].doubledrequiredExit)
                    {
                        if (history[history.Count - 3].Close > instruments[token].middle30BB)
                        {
                            Console.WriteLine("POSITION : Averting Double Candle as well: Modifying the Position of {0} and Revising BUY trigger if Necessary", instruments[token].futName);
                            instruments[token].doubledrequiredExit = true;
                        }
                    }
                    if (history[history.Count - 2].Close < instruments[token].middle30ma50
                        && pos.PNL < -600)
                    {
                        ModifyOrderForContract(pos, token, 300);
                        return;
                    }
                    //history = GetHistory(token, previousDay, currentDay);
                    OType trend = CalculateSqueezedTrend(instruments[token].weekMA,
                                    instruments[token].futName,
                                    instruments[token].close,
                                    instruments[token].top30bb,
                                    instruments[token].middle30BB,
                                    instruments[token].bot30bb,
                                    history);
                    //ProcessOpenPosition(pos, token, trend);
                }
            }
            else if (type == OType.Buy
                    && instruments[token].requiredExit
                    && instruments[token].doubledrequiredExit
                    && IsBetweenVariance(ltp, instruments[token].middleBB, (decimal).0008))
            {
                if ((history[history.Count - 2].Close < instruments[token].longTrigger
                        && instruments[token].longTrigger > 0
                        && !instruments[token].isReorder)
                    || (instruments[token].requiredExit
                        && history[history.Count - 2].Close < instruments[token].weekMA
                        && !instruments[token].isReorder
                        && !instruments[token].isReversed)
                    || (Decimal.Compare(timenow, Convert.ToDecimal(12.45)) < 0
                        && history[history.Count - 2].Close < instruments[token].longTrigger
                        && instruments[token].longTrigger > 0
                        && instruments[token].isReorder
                        && instruments[token].isReversed
                        && (((instruments[token].bot30bb + variance2) > instruments[token].top30bb)
                            || IsBetweenVariance((instruments[token].bot30bb + variance2), instruments[token].top30bb, (decimal).0006)))
                    || (instruments[token].requiredExit
                        && instruments[token].doubledrequiredExit
                        && (IsBetweenVariance(ltp, instruments[token].middleBB, (decimal).0006)
                            || pnl > -2700)))
                {
                    Console.WriteLine("POSITION : Averting Candle: Modifying the Position of {0} and Revising SELL trigger if Necessary", instruments[token].futName);
                    if (history.Count >= 3
                        && instruments[token].isReversed
                        && !instruments[token].doubledrequiredExit)
                    {
                        if (history[history.Count - 3].Close < instruments[token].middle30BB)
                        {
                            Console.WriteLine("POSITION : Averting Double Candle as well: Modifying the Position of {0} and Revising SELL trigger if Necessary", instruments[token].futName);
                            instruments[token].doubledrequiredExit = true;
                        }
                    }
                    if (history[history.Count - 2].Close > instruments[token].middle30ma50
                        && pos.PNL < -600)
                    {
                        ModifyOrderForContract(pos, token, 300);
                        return;
                    }
                    //history = GetHistory(token, previousDay, currentDay);
                    OType trend = CalculateSqueezedTrend(instruments[token].weekMA,
                                    instruments[token].futName,
                                    instruments[token].close,
                                    instruments[token].top30bb,
                                    instruments[token].middle30BB,
                                    instruments[token].bot30bb,
                                    history);
                    //ProcessOpenPosition(pos, token, trend);
                }
            }
        }

        public bool GetCurrentPNL(int futToken, ref Position pos)
        {
            System.Threading.Thread.Sleep(400);
            bool flag = false;
            PositionResponse pr = kite.GetPositions();
            //Console.WriteLine("At Time {0} : FOUND : Overall you have maintained {1} position(s) for the day", DateTime.Now.ToString("yyyyMMdd hh:mm:ss"), pr.Day.Count);
            pos = new Position();
            foreach (Position position in pr.Day)
            {
                if (position.InstrumentToken == futToken)
                {
                    //Console.WriteLine("Current PNL of script {0} is {1} wherein Quantity is {2} & Unrealised is {3}; and Revising if necessary for given token {4}", position.TradingSymbol, position.PNL, position.Quantity, position.Unrealised, futToken);
                    pos = position;
                    flag = true;
                    break;
                }
            }
            return flag;
        }

        public bool GetCurrentOrder(int futToken, string status, ref Order order)
        {
            bool flag = false;
            System.Threading.Thread.Sleep(400);
            List<Order> pr = kite.GetOrders();
            //Console.WriteLine("At Time {0} : FOUND : Overall you have maintained {1} position(s) for the day", DateTime.Now.ToString("yyyyMMdd hh:mm:ss"), pr.Day.Count);
            foreach (Order ordr in pr)
            {
                if (ordr.InstrumentToken == futToken &&
                    //ordr.ParentOrderId != null &&
                    ordr.Status == status)
                {
                    //Console.WriteLine("Current PNL of script {0} is {1} wherein Quantity is {2} & Unrealised is {3}; and Revising if necessary for given token {4}", position.TradingSymbol, position.PNL, position.Quantity, position.Unrealised, futToken);
                    order = ordr;
                    flag = true;
                    break;
                }
            }
            return flag;
        }

        private bool IsInPosition(uint futToken, PositionResponse pr)
        {
            bool flag = false;
            foreach (Position pos in pr.Day)
            {
                if (pos.InstrumentToken == futToken && pos.Quantity != 0)
                {
                    flag = true;
                    break;
                }
            }
            return flag;
        }

        private uint GetRequiredToken(uint instrument, string instName)
        {
            uint requiredToken = 0;
            if (instruments != null && instruments.Count > 0)
            {
                Dictionary<UInt32, WatchList>.KeyCollection keys = instruments.Keys;
                foreach (uint token in keys)
                {
                    if (instruments[token].futId == instrument)
                    {
                        requiredToken = token;
                        break;
                    }
                }
                if (requiredToken == 0)
                {
                    Console.WriteLine("ORDER TOKEN {0} & NAME {1} is Not FOUND IN OUR WATCHLIST as Number of Keys Found {2}", instrument, instName, keys.Count);
                }
            }
            return requiredToken;
        }

        public void LookupAndCancelOrder()
        {
            try
            {
                System.Threading.Thread.Sleep(400);
                PositionResponse pr = kite.GetPositions();
                System.Threading.Thread.Sleep(400);
                foreach (Order order in kite.GetOrders())
                {
                    if (IsInPosition(order.InstrumentToken, pr))
                    {
                        continue;
                    }
                    if ((order.ParentOrderId == null || order.ParentOrderId.Length == 0) && order.Status == "OPEN" && (order.Exchange == Constants.EXCHANGE_NFO ||  order.Exchange == Constants.EXCHANGE_NSE))
                    {
                        DateTime dt = Convert.ToDateTime(order.OrderTimestamp);
                        if (DateTime.Now < dt.AddMinutes(2))
                            continue;
                        OType type = OType.BS;
                        if (order.TransactionType == "SELL")
                        {
                            Console.WriteLine("Trying to Cancel SELL order as it is Due for Very Long time since {0}", order.OrderTimestamp);
                            type = OType.Sell;
                        }
                        else if (order.TransactionType == "BUY")
                        {
                            Console.WriteLine("Trying to Cancel BUY order as it is Due for Very Long time since {0}", order.OrderTimestamp);
                            type = OType.Buy;
                        }
                        else
                            Console.WriteLine("STRANGE : Order Type is not Found");
                        uint reqToken = GetRequiredToken(order.InstrumentToken, order.Tradingsymbol);
                        if (order.Variety != Constants.VARIETY_BO && order.Product != Constants.PRODUCT_NRML)
                        {
                            kite.CancelOrder(order.OrderId, Variety: Constants.VARIETY_REGULAR);
                        }
                        else if (order.Variety == Constants.VARIETY_BO)
                            kite.CancelOrder(order.OrderId, Variety: "bo");
                        if (reqToken > 0)
                        {
                            instruments[reqToken].status = Status.OPEN;
                            System.Threading.Thread.Sleep(400);
                            List<Historical> history = kite.GetHistoricalData(reqToken.ToString(),
                                                DateTime.Now.Date.AddHours(9).AddMinutes(15),
                                                DateTime.Now.Date.AddDays(1),
                                                "30minute");
                            if (type == OType.Sell)
                            {
                                if (history[history.Count - 2].Close > instruments[reqToken].shortTrigger
                                    && instruments[reqToken].shortTrigger > 0
                                    && instruments[reqToken].isReversed)
                                {
                                    Console.WriteLine("STANDING : Averting Candle: Reversing Open SELL Order of {0}", instruments[reqToken].futName);
                                    if (history[history.Count - 2].Close > instruments[reqToken].ma50)
                                    {
                                        instruments[reqToken].type = OType.Buy;
                                        instruments[reqToken].requiredExit = false;
                                        //instruments[reqToken].isReorder = true;
                                        instruments[reqToken].longTrigger = instruments[reqToken].middle30BB;
                                        instruments[reqToken].shortTrigger = instruments[reqToken].top30bb;
                                        placeOrder(reqToken, 0, 0);
                                    }
                                    else
                                        CloseOrderTicker(reqToken, true);
                                }
                            }
                            else if (type == OType.Buy)
                            {
                                if (history[history.Count - 2].Close < instruments[reqToken].longTrigger
                                    && instruments[reqToken].longTrigger > 0
                                    && instruments[reqToken].isReversed)
                                {
                                    Console.WriteLine("STANDING : Averting Candle: Reversion Open BUY Order of {0}", instruments[reqToken].futName);
                                    if (history[history.Count - 2].Close < instruments[reqToken].ma50)
                                    {
                                        instruments[reqToken].type = OType.Sell;
                                        instruments[reqToken].requiredExit = false;
                                        //instruments[reqToken].isReorder = true;
                                        instruments[reqToken].longTrigger = instruments[reqToken].bot30bb;
                                        instruments[reqToken].shortTrigger = instruments[reqToken].middle30BB;
                                        placeOrder(reqToken, 0, 0);
                                    }
                                    else
                                        CloseOrderTicker(reqToken, true);
                                }
                            }
                        }
                    }
                    else if (order.Status != "TRIGGER PENDING")
                        Console.WriteLine("Lookup Order Update for {0} did type {1}, variety {2} at price {3} and its status is '{4}' with Parent Order {5}", order.Tradingsymbol, order.OrderType, order.Variety, order.Price, order.Status, order.ParentOrderId);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("EXCEPTION in 'LookupAndCancelOrder' and message is {0}", ex.Message);
            }
        }

        public void VerifyOpenPositions()
        {
            startTicking = false;
            LookupAndCancelOrder();
            PositionResponse pr = kite.GetPositions();
            Console.WriteLine("Set ticking to false; At Time {0} : FOUND : Overall you have maintained {1} position(s) for the day", DateTime.Now.ToString("yyyyMMdd hh:mm:ss"), pr.Day.Count);
            if (pr.Day.Count == 0)
                return;
            foreach (Position pos in pr.Day)
            {
                uint reqToken = GetRequiredToken(pos.InstrumentToken, pos.TradingSymbol);
                if (pos.Quantity < 0 && reqToken != 0)
                {
                    //CancelAndReOrder(reqToken, OType.Sell, 0, pos.PNL);
                }
                else if (pos.Quantity > 0 && reqToken != 0)
                {
                    //CancelAndReOrder(reqToken, OType.Buy, 0, pos.PNL);
                }
                else
                {
                    Console.WriteLine("At Time {0} : Current Realised Value of the position(s) {1} for the day is {2}", DateTime.Now.ToString("yyyyMMdd hh:mm:ss"), pos.TradingSymbol, pos.PNL);
                }
            }
        }
        
        private void ProcessOpenPosition(Position pos, uint token, OType niftyTrend)
        {
            try
            {
                decimal timenow = DateTime.Now.Hour + ((decimal)DateTime.Now.Minute / 100);
                #region GetCurrent Trend
                int candles = 10;
                decimal variance23 = (instruments[token].history[instruments[token].history.Count - 2].Close * (decimal)23) / 100;
                if ((instruments[token].bot30bb + variance23) > instruments[token].top30bb)
                {
                    candles = 12;
                }
                OType currentTrend = CalculateSqueezedTrend(instruments[token].futName, instruments[token].history, candles);
                if (niftyTrend == OType.BS)
                {
                    Console.WriteLine("30 Min Trend of Script {0} is in neutral state", instruments[token].futName);
                }
                else if (niftyTrend == OType.Buy)
                    Console.WriteLine("30 Min Trend of Script {0} is in BUY state", instruments[token].futName);
                else if (niftyTrend == OType.Sell)
                    Console.WriteLine("30 Min Trend of Script {0} is in SELL state", instruments[token].futName);

                switch (currentTrend)
                {
                    case OType.BS:
                        Console.WriteLine("Current Trend of Script {0} is in neutral state", instruments[token].futName);
                        break;
                    case OType.Buy:
                        Console.WriteLine("Current Trend of Script {0} is in BUY state", instruments[token].futName);
                        break;
                    case OType.StrongBuy:
                        Console.WriteLine("Current Trend of Script {0} is in Strong BUY state", instruments[token].futName);
                        break;
                    case OType.Sell:
                        Console.WriteLine("Current Trend of Script {0} is in SELL state", instruments[token].futName);
                        break;
                    case OType.StrongSell:
                        Console.WriteLine("Current Trend of Script {0} is in Strong SELL state", instruments[token].futName);
                        break;
                }
                #endregion

                if (instruments[token].isReversed
                    && !instruments[token].isReorder
                    && !instruments[token].doubledrequiredExit)
                {
                    ModifyOrderForContract(pos, token, 300);
                    return;
                }
                foreach (Order order in kite.GetOrders())
                {
                    if (pos.InstrumentToken == order.InstrumentToken)
                    {
                        //if(order.Status == "COMPLETE" && pos.Quantity != 0)
                        //    instruments[token].orderTime = Convert.ToDateTime(order.OrderTimestamp);
                        if (order.Status == "OPEN" && pos.Quantity != 0) // && DateTime.Now > dt.AddMinutes(4))
                        {
                            decimal obtainingLoss = pos.PNL;
                            decimal trigger = (decimal)300 / (decimal)order.Quantity;
                            trigger = Convert.ToDecimal(trigger.ToString("#.#")) >= (decimal)0.3? Convert.ToDecimal(trigger.ToString("#.#")) : Convert.ToDecimal(trigger.ToString("#.#")) + (decimal).05;
                            int expected = ExpectedCandleCount(instruments[token].ReversedTime);
                            int actual = ExpectedCandleCount(null);
                            System.Threading.Thread.Sleep(400);
                            decimal variance2 = (instruments[token].history[instruments[token].history.Count - 2].Close * (decimal)2) / 100;
                            decimal variance25 = (instruments[token].history[instruments[token].history.Count - 2].Close * (decimal)2.5) / 100;
                            decimal variance28 = (instruments[token].history[instruments[token].history.Count - 2].Close * (decimal)2.8) / 100;
                            decimal variance3 = (instruments[token].history[instruments[token].history.Count - 2].Close * (decimal)3) / 100;
                            decimal variance34 = (instruments[token].history[instruments[token].history.Count - 2].Close * (decimal)3.4) / 100;
                            if (order.TransactionType == "SELL")
                            {
                                #region Process SELL
                                Console.WriteLine("Current PNL {0} is in LOSS for {1} with Long Trigger {2} & expected time past {3} for order time {4} with expected normal variance {5} & top30BB {6}", pos.PNL, pos.TradingSymbol, instruments[token].middle30BB, expected, instruments[token].orderTime, (instruments[token].bot30bb + variance2), instruments[token].top30bb);
                                bool isReOrder = false;
                                #region Narrowed
                                if (expected > 1
                                    && (((instruments[token].bot30bb + variance2) > instruments[token].top30bb)
                                        || IsBetweenVariance((instruments[token].bot30bb + variance2), instruments[token].top30bb, (decimal).0006)))
                                {
                                    if (IsBetweenVariance(instruments[token].weekMA, instruments[token].middle30BB, (decimal).006))
                                    //&& instruments[token].weekMA > instruments[token].middle30BB
                                    {
                                        if (!instruments[token].requiredExit)
                                        {
                                            trigger = (decimal)1300 / (decimal)order.Quantity;
                                            trigger = Convert.ToDecimal(trigger.ToString("#.#")) + (decimal).05;
                                            if ((pos.AveragePrice + trigger) < order.Price)
                                            {
                                                Console.WriteLine("Narrowed POSITION : We can Wait for one more candle to see the Trend Reversal", instruments[token].futName);
                                                kite.ModifyOrder(order.OrderId, Price: (decimal)(pos.AveragePrice + trigger));
                                            }
                                            continue;
                                        }
                                        else if ((pos.AveragePrice + trigger) < order.Price)
                                        {
                                            Console.WriteLine("Narrowed, Need not necessarily EXIT NOW but at {0} :: The BUY order status of the script {1} is modified in a hope for safe Exit Trigger at {2}; MA50 {3}", DateTime.Now.ToString(), instruments[token].futName, (pos.AveragePrice + trigger), instruments[token].ma50);
                                            kite.ModifyOrder(order.OrderId, Price: (decimal)(pos.AveragePrice + trigger));
                                            continue;
                                        }
                                        else if (instruments[token].oldTime != instruments[token].currentTime)
                                        {
                                            instruments[token].oldTime = instruments[token].currentTime;
                                            Console.WriteLine("Script is still in Narrowed state for {0} hence waiting for safe exit as PNL {1}", pos.TradingSymbol, pos.PNL);
                                            continue;
                                        }
                                        else
                                            continue;
                                    }
                                    else
                                    {
                                        if (pos.PNL < -500
                                            && pos.PNL > -2600
                                            && (instruments[token].doubledrequiredExit 
                                            || currentTrend == OType.StrongSell))
                                        {
                                            Console.WriteLine("Though Narrowed, CANCELLED AS THIS IS BEST EXIT POINT for SELL trigger NOW with Loss of {0}", pos.PNL);
                                            if (order.ParentOrderId == null)
                                                kite.ModifyOrder(order.OrderId, OrderType: Constants.ORDER_TYPE_MARKET);
                                            else
                                                kite.CancelOrder(order.OrderId, Variety: "bo");
                                            Console.WriteLine("As the reversed candle is the first candle, You can look for ReOrder as well as Expected Candle {0} vs Actual Candle {1} with double Exit and req exit are {2} vs {3}", expected, actual, instruments[token].doubledrequiredExit, instruments[token].requiredExit);
                                            if (instruments[token].doubledrequiredExit
                                                && instruments[token].isReversed
                                                && (instruments[token].requiredExit
                                                    || niftyTrend == OType.Sell
                                                    || currentTrend == OType.StrongSell))
                                            {
                                                isReOrder = true;
                                            }
                                            else
                                                continue; 
                                        }
                                        else if ((pos.AveragePrice + trigger) < order.Price)
                                        {
                                            if (pos.PNL >= 0
                                                && (currentTrend == OType.BS
                                                    || currentTrend == OType.Sell
                                                    || currentTrend != OType.StrongSell))
                                            {
                                                Console.WriteLine("Narrwoed, but at {0} :: The Sell order status of the script {1} can be waited for Targer", DateTime.Now.ToString(), instruments[token].futName);
                                                continue;
                                            }
                                            else
                                            {
                                                Console.WriteLine("Though Narrowed, Need not necessarily EXIT NOW but at {0} :: The BUY order status of the script {1} is modified in a hope for safe Exit Trigger at {2}; MA50 {3}", DateTime.Now.ToString(), instruments[token].futName, (pos.AveragePrice + trigger), instruments[token].ma50);
                                                kite.ModifyOrder(order.OrderId, Price: (decimal)(pos.AveragePrice + trigger));
                                                continue;
                                            }
                                        }
                                    }
                                }
                                #endregion
                                if (!isReOrder
                                    && instruments[token].ma50 > 0
                                    && instruments[token].history[instruments[token].history.Count - 2].Close < instruments[token].ma50
                                    && (pos.PNL > -2300
                                        || currentTrend == OType.StrongSell
                                        || instruments[token].requiredExit))
                                {
                                    if (instruments[token].isReversed
                                        && pos.PNL > -1600
                                        && IsBeyondVariance(instruments[token].weekMA, instruments[token].middle30BB, (decimal).006)
                                        && (((instruments[token].bot30bb + variance3) < instruments[token].top30bb)
                                            || IsBetweenVariance((instruments[token].bot30bb + variance3), instruments[token].top30bb, (decimal).0006)))
                                    {
                                        if ((IsBetweenVariance(instruments[token].history[instruments[token].history.Count - 1].Close, instruments[token].middleBB, (decimal).0035)
                                                || instruments[token].requiredExit)
                                            && currentTrend == OType.StrongSell)
                                        {
                                            Console.WriteLine("CANCELLED AS THE Range is beyond 3% {0}", instruments[token].top30bb - instruments[token].bot30bb);
                                            if (order.ParentOrderId == null)
                                                kite.ModifyOrder(order.OrderId, OrderType: Constants.ORDER_TYPE_MARKET);
                                            else
                                                kite.CancelOrder(order.OrderId, Variety: "bo");
                                            isReOrder = true;
                                        }
                                        else if ((pos.AveragePrice + trigger) < order.Price)
                                        {
                                            Console.WriteLine("Revising for now though range is 3% as we are at loss of {0} with variance of {1} with topbb {2}", pos.PNL, (instruments[token].bot30bb + variance3), instruments[token].top30bb);
                                            kite.ModifyOrder(order.OrderId, Price: (decimal)(pos.AveragePrice + trigger));
                                            continue;
                                        }
                                    }
                                    else if (instruments[token].isReversed
                                        && pos.PNL > -2300 
                                        && pos.PNL < -300
                                        && (((instruments[token].bot30bb + variance25) < instruments[token].top30bb)
                                            || IsBetweenVariance((instruments[token].bot30bb + variance25), instruments[token].top30bb, (decimal).0006)))
                                    {
                                        if ((instruments[token].top30bb < instruments[token].middle30ma50
                                            || IsBetweenVariance(instruments[token].top30bb, instruments[token].middle30ma50, (decimal).002)
                                            || ((instruments[token].bot30bb + variance28) > instruments[token].top30bb))
                                            && !instruments[token].requiredExit)
                                            // && pos.PNL < -800
                                        {
                                            if ((pos.AveragePrice + trigger) < order.Price)
                                            {
                                                Console.WriteLine("Wait Cancellation as narrow range 28 for now as we are at loss of {0} with variance of {1} with topbb {2}", pos.PNL, (instruments[token].bot30bb + variance28), instruments[token].top30bb);
                                                kite.ModifyOrder(order.OrderId, Price: (decimal)(pos.AveragePrice + trigger));
                                            }
                                            continue;
                                        }
                                        else
                                        {
                                            Console.WriteLine("CANCELLED AS THIS IS BEST EXIT POINT for SELL trigger NOW with Loss of {0} with variance of {1} with topbb {2}", pos.PNL, (instruments[token].bot30bb + variance25), instruments[token].top30bb);
                                            if (order.ParentOrderId == null)
                                                kite.ModifyOrder(order.OrderId, OrderType: Constants.ORDER_TYPE_MARKET);
                                            else
                                                kite.CancelOrder(order.OrderId, Variety: "bo");
                                        }
                                    }
                                    else if ((currentTrend == OType.StrongSell
                                            || instruments[token].requiredExit)
                                        && pos.PNL < -300
                                        && (((instruments[token].bot30bb + variance2) < instruments[token].top30bb)
                                            || IsBetweenVariance((instruments[token].bot30bb + variance2), instruments[token].top30bb, (decimal).0006)))
                                    {
                                        if (instruments[token].isReversed
                                            && instruments[token].requiredExit && pos.PNL > -4000)
                                        {
                                            if ((instruments[token].bot30bb + variance25) > instruments[token].top30bb)
                                            {
                                                if (pos.PNL < -1600)
                                                {
                                                    if ((instruments[token].doubledrequiredExit
                                                            || currentTrend == OType.StrongBuy)
                                                        && pos.PNL > -2300)
                                                    {
                                                        Console.WriteLine("23 CANCELL and Proceed ReORDERING for BUY trigger NOW with Loss of {0}", pos.PNL);
                                                        if (order.ParentOrderId == null)
                                                            kite.ModifyOrder(order.OrderId, OrderType: Constants.ORDER_TYPE_MARKET);
                                                        else
                                                            kite.CancelOrder(order.OrderId, Variety: "bo");
                                                    }
                                                    else
                                                    {
                                                        Console.WriteLine("Wait Cancellation as narrow range 25 for now as we are at loss of {0} with variance of {1} with topbb {2}", pos.PNL, (instruments[token].bot30bb + variance25), instruments[token].top30bb);
                                                        continue;
                                                    }
                                                }
                                                else
                                                {
                                                    Console.WriteLine("16 CANCELL and Proceed ReORDERING for BUY trigger NOW with Loss of {0}", pos.PNL);
                                                    if (order.ParentOrderId == null)
                                                        kite.ModifyOrder(order.OrderId, OrderType: Constants.ORDER_TYPE_MARKET);
                                                    else
                                                        kite.CancelOrder(order.OrderId, Variety: "bo");
                                                }
                                            }
                                            else
                                            {
                                                Console.WriteLine("CANCELL and Proceed ReORDERING for SELL trigger NOW with Loss of {0}", pos.PNL);
                                                if (order.ParentOrderId == null)
                                                    kite.ModifyOrder(order.OrderId, OrderType: Constants.ORDER_TYPE_MARKET);
                                                else
                                                    kite.CancelOrder(order.OrderId, Variety: "bo");
                                            }
                                        }
                                        else if ((pos.AveragePrice + trigger) < order.Price)
                                        {
                                            Console.WriteLine("HARD EXIT Required at {0} But WAITING from CANCELLATION:: The Buy order status of the script {1} is Severe LOSS point so modifying Exit Trigger to {2}; MA50 {3} & 5Minute Candle Open {4}", DateTime.Now.ToString(), instruments[token].futName, (pos.AveragePrice + trigger), instruments[token].ma50, instruments[token].history[instruments[token].history.Count - 2].Close);
                                            kite.ModifyOrder(order.OrderId, Price: (decimal)(pos.AveragePrice + trigger));
                                            continue;
                                        }
                                        else
                                        {
                                            Dictionary<string, LTP> ltps = kite.GetLTP(new string[] { token.ToString() });
                                            if (instruments[token].isReversed
                                                && instruments[token].requiredExit 
                                                && IsBetweenVariance(ltps[token.ToString()].LastPrice, instruments[token].middleBB, (decimal).0006))
                                            {
                                                Console.WriteLine("CANCELL and Stop ReORDERING for SELL trigger NOW with Loss of {0} as it is at better exit point", pos.PNL);
                                                if (order.ParentOrderId == null)
                                                    kite.ModifyOrder(order.OrderId, OrderType: Constants.ORDER_TYPE_MARKET);
                                                else
                                                    kite.CancelOrder(order.OrderId, Variety: "bo");
                                            }
                                            else
                                                Console.WriteLine("Wait from Further Changes for now as the current loss is {0}", pos.PNL);
                                            continue;
                                        }
                                    }
                                    else if (((instruments[token].bot30bb + variance2) < instruments[token].top30bb)
                                            || IsBetweenVariance((instruments[token].bot30bb + variance2), instruments[token].top30bb, (decimal).0006))
                                    {
                                        if (instruments[token].isReversed
                                            && instruments[token].requiredExit 
                                            && pos.PNL > -4000 
                                            && pos.PNL < -300)
                                        {
                                            Console.WriteLine("Due to Back to Original Trend, CANCELLED THE ORDER NOW with {0}", pos.PNL);
                                            if (order.ParentOrderId == null)
                                                kite.ModifyOrder(order.OrderId, OrderType: Constants.ORDER_TYPE_MARKET);
                                            else
                                                kite.CancelOrder(order.OrderId, Variety: "bo");
                                        }
                                        else if (!instruments[token].isReversed
                                            && currentTrend == OType.StrongSell
                                            && instruments[token].requiredExit
                                            && pos.PNL > -2000
                                            && pos.PNL < -300)
                                        {
                                            Console.WriteLine("Due to Trend Reversal CANCELLED THE ORDER NOW with {0}", pos.PNL);
                                            if (order.ParentOrderId == null)
                                                kite.ModifyOrder(order.OrderId, OrderType: Constants.ORDER_TYPE_MARKET);
                                            else
                                                kite.CancelOrder(order.OrderId, Variety: "bo");
                                        }
                                        else if (instruments[token].isReversed
                                                && (pos.PNL >= -300
                                                    || instruments[token].doubledrequiredExit))
                                        {
                                            if ((instruments[token].bot30bb + variance2) < instruments[token].top30bb
                                                && pos.PNL >= 300)
                                            {
                                                Console.WriteLine("Due to Trend Reversal CANCELLED THE ORDER NOW with {0}", pos.PNL);
                                                if (order.ParentOrderId == null)
                                                    kite.ModifyOrder(order.OrderId, OrderType: Constants.ORDER_TYPE_MARKET);
                                                else
                                                    kite.CancelOrder(order.OrderId, Variety: "bo");
                                            }
                                            else
                                            {
                                                if ((pos.AveragePrice - trigger) > order.Price
                                                    && (IsBeyondVariance(instruments[token].weekMA, instruments[token].middle30BBnew, (decimal).006)
                                                        || instruments[token].doubledrequiredExit)
                                                    && pos.PNL < -300)
                                                {
                                                    Console.WriteLine("EXIT Required at {0} But Bollinger is marginally narrow :: The Buy order status of the script {1} is in serious LOSS point so modifying Exit Trigger to {2}; MA50 {3} & 5Minute Candle Open {4}", DateTime.Now.ToString(), instruments[token].futName, (pos.AveragePrice - trigger), instruments[token].ma50, instruments[token].history[instruments[token].history.Count - 2].Close);
                                                    kite.ModifyOrder(order.OrderId, Price: (decimal)(pos.AveragePrice - trigger));
                                                }
                                                else
                                                {
                                                    if ((pos.AveragePrice + trigger) < order.Price)
                                                    {
                                                        Console.WriteLine("EXIT Required at {0} But Bollinger is marginally narrow :: The Buy order status of the script {1} is in normal LOSS point so modifying Exit Trigger to {2}; MA50 {3} & 5Minute Candle Open {4}", DateTime.Now.ToString(), instruments[token].futName, (pos.AveragePrice + trigger), instruments[token].ma50, instruments[token].history[instruments[token].history.Count - 2].Close);
                                                        kite.ModifyOrder(order.OrderId, Price: (decimal)(pos.AveragePrice + trigger));
                                                    }
                                                    else if (instruments[token].oldTime != instruments[token].currentTime)
                                                    {
                                                        instruments[token].oldTime = instruments[token].currentTime;
                                                        Console.WriteLine("Wait Cancellation as BUY Order of {0} is just modified though loss is above 2500 as {1} and Exit trigger at {2}", pos.TradingSymbol, pos.LastPrice, (pos.AveragePrice + trigger));
                                                    }
                                                }
                                            }
                                            continue;
                                        }
                                        else if ((pos.AveragePrice + trigger) < order.Price)
                                        {
                                            if (currentTrend == OType.StrongSell)
                                            {
                                                Console.WriteLine("EXIT Required at {0} But NO WAITING from CANCELLATION:: The Buy order status of the script {1} is in serious LOSS point so modifying Exit Trigger to {2}; MA50 {3} & 5Minute Candle Open {4}", DateTime.Now.ToString(), instruments[token].futName, (pos.AveragePrice + trigger), instruments[token].ma50, instruments[token].history[instruments[token].history.Count - 2].Close);
                                                if (order.ParentOrderId == null)
                                                    kite.ModifyOrder(order.OrderId, OrderType: Constants.ORDER_TYPE_MARKET);
                                                else
                                                    kite.CancelOrder(order.OrderId, Variety: "bo");
                                            }
                                            else
                                            {
                                                Console.WriteLine("EXIT Required at {0} But WAITING from CANCELLATION:: The Buy order status of the script {1} is in serious LOSS point so modifying Exit Trigger to {2}; MA50 {3} & 5Minute Candle Open {4}", DateTime.Now.ToString(), instruments[token].futName, (pos.AveragePrice + trigger), instruments[token].ma50, instruments[token].history[instruments[token].history.Count - 2].Close);
                                                kite.ModifyOrder(order.OrderId, Price: (decimal)(pos.AveragePrice + trigger));
                                            }
                                            continue;
                                        }
                                        else
                                            continue;
                                    }
                                    else if (!instruments[token].isReversed
                                        && instruments[token].requiredExit)
                                    {
                                        if (pos.PNL < -1000 && pos.PNL > -2000)
                                        {
                                            if (IsBetweenVariance(instruments[token].weekMA, instruments[token].bot30bb, (decimal).004)
                                                && instruments[token].weekMA > instruments[token].bot30bb)
                                            {
                                                Console.WriteLine("EXIT at {0} in case The BUY order script {1} is in necessary variance between WeekMA {2} & Middle30BB {3}", DateTime.Now.ToString(), instruments[token].futName, instruments[token].weekMA, instruments[token].middle30BB);
                                                if (order.ParentOrderId == null)
                                                    kite.ModifyOrder(order.OrderId, OrderType: Constants.ORDER_TYPE_MARKET);
                                                else
                                                    kite.CancelOrder(order.OrderId, Variety: "bo");
                                            }
                                        }
                                    }
                                    else
                                    {
                                        trigger = (decimal)1300 / (decimal)order.Quantity;
                                        trigger = Convert.ToDecimal(trigger.ToString("#.#")) + (decimal).05;
                                        if ((pos.AveragePrice + trigger) < order.Price)
                                        {
                                            if (currentTrend == OType.StrongSell)
                                            {
                                                Console.WriteLine("EXIT Required at {0} But NO WAITING from CANCELLATION:: The Buy order status of the script {1} is in serious LOSS point so modifying Exit Trigger to {2}; MA50 {3} & 5Minute Candle Open {4}", DateTime.Now.ToString(), instruments[token].futName, (pos.AveragePrice + trigger), instruments[token].ma50, instruments[token].history[instruments[token].history.Count - 2].Close);
                                                if (order.ParentOrderId == null)
                                                    kite.ModifyOrder(order.OrderId, OrderType: Constants.ORDER_TYPE_MARKET);
                                                else
                                                    kite.CancelOrder(order.OrderId, Variety: "bo");
                                            }
                                            else
                                            {
                                                Console.WriteLine("EXIT Required at {0} But WAITING from CANCELLATION:: The BUY order status of the script {1} is in normal LOSS point so modifying Exit Trigger to target 1300 {2}; MA50 {3} & 5Minute Candle Open {4}", DateTime.Now.ToString(), instruments[token].futName, (pos.AveragePrice + trigger), instruments[token].ma50, instruments[token].history[instruments[token].history.Count - 2].Close);
                                                kite.ModifyOrder(order.OrderId, Price: (decimal)(pos.AveragePrice + trigger));
                                            }
                                        }
                                        else if (instruments[token].oldTime != instruments[token].currentTime)
                                        {
                                            instruments[token].oldTime = instruments[token].currentTime;
                                            Console.WriteLine("Variance less than 25, So Wait Cancellation as both WeekMA and Middle30BB are closer hence the BUY Order of {0} as loss is above 2500 as {1} and Exit trigger at {2}", pos.TradingSymbol, pos.LastPrice, (pos.AveragePrice + trigger));
                                        }
                                        continue;
                                    }

                                    if (niftyTrend != OType.Buy
                                            || instruments[token].requiredExit
                                            || (instruments[token].bot30bb + variance3) < instruments[token].top30bb
                                            || IsBetweenVariance((instruments[token].bot30bb + variance3), instruments[token].top30bb, (decimal).0006))
                                    {
                                        Console.WriteLine("Reordering the Order of {0} to As Nifty is in SELL side", pos.TradingSymbol);
                                        isReOrder = true;
                                    }
                                    if (isReOrder)
                                    {
                                        isReOrder = Decimal.Compare(timenow, Convert.ToDecimal(11.50)) < 0;
                                        if (!isReOrder)
                                        {
                                            Console.WriteLine("Time is Past 11.50AM. Verifying Clear Movement as Time now {0} with bot+variance {1} & top{2}", DateTime.Now.ToString(), (instruments[token].bot30bb + variance3), instruments[token].top30bb);
                                            isReOrder = (niftyTrend == OType.Sell
                                                        || (instruments[token].bot30bb + variance3) < instruments[token].top30bb
                                                            || IsBetweenVariance((instruments[token].bot30bb + variance3), instruments[token].top30bb, (decimal).0006))
                                                        && currentTrend == OType.StrongSell;
                                                        //Decimal.Compare(timenow, Convert.ToDecimal(12.44)) > 0;
                                        }
                                        else
                                            Console.WriteLine("Timeis Less than 11.50AM as Time now {0}", DateTime.Now.ToString());
                                    }
                                }
                                else if (!isReOrder)
                                {
                                    if ((pos.AveragePrice + trigger) < order.Price)
                                    {
                                        if (((instruments[token].bot30bb + variance34) < instruments[token].top30bb
                                            || IsBetweenVariance((instruments[token].bot30bb + variance34), instruments[token].top30bb, (decimal).0006))
                                            && Decimal.Compare(timenow, (decimal)12.47) < 0)
                                        {
                                            Console.WriteLine("Cancel and reverse the position now at {0} :: The Buy order status of the script {1} is better point so modifying Exit Trigger to {2}; MA50 {3} & 5Minute Candle Open {4}", DateTime.Now.ToString(), instruments[token].futName, (pos.AveragePrice + trigger), instruments[token].ma50, instruments[token].history[instruments[token].history.Count - 2].Close);
                                            if (order.ParentOrderId == null)
                                                kite.ModifyOrder(order.OrderId, OrderType: Constants.ORDER_TYPE_MARKET);
                                            else
                                                kite.CancelOrder(order.OrderId, Variety: "bo");
                                            isReOrder = true;
                                        }
                                        else
                                        {
                                            Console.WriteLine("Need not necessarily EXIT NOW at {0} :: The Buy order status of the script {1} is better point so modifying Exit Trigger to {2}; MA50 {3} & 5Minute Candle Open {4}", DateTime.Now.ToString(), instruments[token].futName, (pos.AveragePrice + trigger), instruments[token].ma50, instruments[token].history[instruments[token].history.Count - 2].Close);
                                            kite.ModifyOrder(order.OrderId, Price: (decimal)(pos.AveragePrice + trigger));
                                            continue;
                                        }
                                    }
                                    else if (instruments[token].oldTime != instruments[token].currentTime)
                                    {
                                        instruments[token].oldTime = instruments[token].currentTime;
                                        Console.WriteLine("Wait Cancellation the BUY Order of {0} as loss is above 2500 as {1} and Exit trigger at {2}", pos.TradingSymbol, pos.LastPrice, (pos.AveragePrice + trigger));
                                        continue;
                                    }
                                    else
                                        continue;
                                }
                                if (isReOrder)
                                {
                                    System.Threading.Thread.Sleep(3000);
                                    Console.WriteLine("Possibly SELL NOW at {0} :: Commented RE Sell order status of the script {1} is better Reentry point NOW", DateTime.Now.ToString(), instruments[token].futName);
                                    /*
                                    instruments[token].status = Status.POSITION;
                                    instruments[token].type = OType.Sell;
                                    instruments[token].requiredExit = false;
                                    instruments[token].doubledrequiredExit = false;
                                    instruments[token].isReorder = true;
                                    instruments[token].shortTrigger = instruments[token].middle30BB;
                                    instruments[token].longTrigger = instruments[token].bot30bb;
                                    placeOrder(token, 0);
                                    */
                                }
                                else
                                {
                                    System.Threading.Thread.Sleep(1500);
                                    Console.WriteLine("Refraining from ReOrder as Movement is not clear yet, at this juncture");
                                }
                                #endregion
                            }
                            else if (order.TransactionType == "BUY")
                            {
                                #region Process BUY
                                Console.WriteLine("Current PNL {0} is in LOSS for {1} with Short Trigger {2} & expected time past {3} for order time {4} with expected normal variance {5} & top30BB {6}", pos.PNL, pos.TradingSymbol, instruments[token].middle30BB, expected, instruments[token].orderTime, (instruments[token].bot30bb + variance2), instruments[token].top30bb);
                                bool isReOrder = false;
                                #region Narrowed
                                if (expected > 1
                                    && ((instruments[token].bot30bb + variance2) > instruments[token].top30bb)
                                        || IsBetweenVariance((instruments[token].bot30bb + variance2), instruments[token].top30bb, (decimal).0006))
                                {
                                    if (IsBetweenVariance(instruments[token].weekMA, instruments[token].middle30BB, (decimal).006))
                                    //&& instruments[token].weekMA < instruments[token].middle30BB
                                    {
                                        if (!instruments[token].requiredExit)
                                        {
                                            trigger = (decimal)1300 / (decimal)order.Quantity;
                                            trigger = Convert.ToDecimal(trigger.ToString("#.#")) + (decimal).05;
                                            if ((pos.AveragePrice - trigger) > order.Price)
                                            {
                                                Console.WriteLine("Narrowed POSITION : We can Wait for one more candle to see the Trend Reversal", instruments[token].futName);
                                                kite.ModifyOrder(order.OrderId, Price: (decimal)(pos.AveragePrice - trigger));
                                            }
                                            continue;
                                        }
                                        else if ((pos.AveragePrice - trigger) > order.Price)
                                        {
                                            Console.WriteLine("Narrowed, Need not necessarily EXIT NOW but at {0} :: The Sell order status of the script {1} is modified in a hope for safe Exit Trigger at {2}; MA50 {3}", DateTime.Now.ToString(), instruments[token].futName, (pos.AveragePrice - trigger), instruments[token].ma50);
                                            kite.ModifyOrder(order.OrderId, Price: (decimal)(pos.AveragePrice - trigger));
                                            continue;
                                        }
                                        else if (instruments[token].oldTime != instruments[token].currentTime)
                                        {
                                            instruments[token].oldTime = instruments[token].currentTime;
                                            Console.WriteLine("Script is still in Narrowed state for {0} hence waiting for safe exit as PNL {1}", pos.TradingSymbol, pos.PNL);
                                            continue;
                                        }
                                        else
                                            continue;
                                    }
                                    else
                                    {
                                        if (pos.PNL < -500
                                            && pos.PNL > -2600
                                            //&& instruments[token].middle30ma50 < instruments[token].bot30bb
                                            && (instruments[token].doubledrequiredExit
                                                || currentTrend == OType.StrongBuy))
                                        {
                                            Console.WriteLine("Though Narrowed, CANCELLED AS THIS IS BEST EXIT POINT for BUY trigger NOW with Loss of {0}", pos.PNL);
                                            if (order.ParentOrderId == null)
                                                kite.ModifyOrder(order.OrderId, OrderType: Constants.ORDER_TYPE_MARKET);
                                            else
                                                kite.CancelOrder(order.OrderId, Variety: "bo");
                                            Console.WriteLine("As the reversed candle is the first candle, You can look for ReOrder as well as Expected Candle {0} vs Actual Candle {1} with double Exit and req exit are {2} vs {3}", expected, actual, instruments[token].doubledrequiredExit, instruments[token].requiredExit);
                                            if (instruments[token].doubledrequiredExit
                                                && instruments[token].isReversed
                                                && (instruments[token].requiredExit
                                                    || niftyTrend == OType.Buy
                                                    || currentTrend == OType.StrongBuy))
                                            {
                                                isReOrder = true;
                                            }
                                            else
                                                continue;
                                        }
                                        else if ((pos.AveragePrice - trigger) > order.Price)
                                        {
                                            if (pos.PNL >= 0
                                                && (currentTrend == OType.BS
                                                || currentTrend == OType.Buy
                                                || currentTrend != OType.StrongBuy))
                                            {
                                                Console.WriteLine("Narrwoed, but at {0} :: The Sell order status of the script {1} can be waited for Targer", DateTime.Now.ToString(), instruments[token].futName);
                                                continue;
                                            }
                                            else
                                            {
                                                Console.WriteLine("Though Narrowed, Need not necessarily EXIT NOW but at {0} :: The Sell order status of the script {1} is modified in a hope for safe Exit Trigger at {2}; MA50 {3}", DateTime.Now.ToString(), instruments[token].futName, (pos.AveragePrice - trigger), instruments[token].ma50);
                                                kite.ModifyOrder(order.OrderId, Price: (decimal)(pos.AveragePrice - trigger));
                                                continue;
                                            }
                                        }
                                    }
                                }
                                #endregion
                                if (!isReOrder
                                    && instruments[token].ma50 > 0
                                    && instruments[token].history[instruments[token].history.Count - 2].Close > instruments[token].ma50
                                    && (pos.PNL > -2300
                                        || currentTrend == OType.StrongBuy
                                        || instruments[token].requiredExit))
                                {
                                    if (instruments[token].isReversed
                                        && pos.PNL > -1600
                                        && IsBeyondVariance(instruments[token].weekMA, instruments[token].middle30BB, (decimal).006)
                                        && (((instruments[token].bot30bb + variance3) < instruments[token].top30bb)
                                            || IsBetweenVariance((instruments[token].bot30bb + variance3), instruments[token].top30bb, (decimal).0006)))
                                    {
                                        if ((IsBetweenVariance(instruments[token].history[instruments[token].history.Count - 1].Close, instruments[token].middleBB, (decimal).0035)
                                                || instruments[token].requiredExit)
                                            && currentTrend == OType.StrongBuy)
                                        {
                                            Console.WriteLine("CANCELLED AS THE Range is beyond 3% {0}", instruments[token].top30bb - instruments[token].bot30bb);
                                            if (order.ParentOrderId == null)
                                                kite.ModifyOrder(order.OrderId, OrderType: Constants.ORDER_TYPE_MARKET);
                                            else
                                                kite.CancelOrder(order.OrderId, Variety: "bo");
                                            isReOrder = true;
                                        }
                                        else if ((pos.AveragePrice - trigger) > order.Price)
                                        {
                                            Console.WriteLine("Revising for now though range is 3% as we are at loss of {0} with variance of {1} with topbb {2}", pos.PNL, (instruments[token].bot30bb + variance3), instruments[token].top30bb);
                                            kite.ModifyOrder(order.OrderId, Price: (decimal)(pos.AveragePrice - trigger));
                                            continue;
                                        }
                                    }
                                    else if (instruments[token].isReversed
                                        && pos.PNL > -2300 
                                        && pos.PNL < -300
                                        && (((instruments[token].bot30bb + variance25) < instruments[token].top30bb)
                                            || IsBetweenVariance((instruments[token].bot30bb + variance25), instruments[token].top30bb, (decimal).0006)))
                                    {
                                        if ((instruments[token].bot30bb > instruments[token].middle30ma50
                                            || IsBetweenVariance(instruments[token].bot30bb, instruments[token].middle30ma50, (decimal).002)
                                            || ((instruments[token].bot30bb + variance28) > instruments[token].top30bb))
                                            && !instruments[token].requiredExit)
                                            // && pos.PNL < -800
                                        {
                                            if ((pos.AveragePrice - trigger) > order.Price)
                                            {
                                                Console.WriteLine("Wait Cancellation as narrow range 28 for now as we are at loss of {0} with variance of {1} with topbb {2}", pos.PNL, (instruments[token].bot30bb + variance28), instruments[token].top30bb);
                                                kite.ModifyOrder(order.OrderId, Price: (decimal)(pos.AveragePrice - trigger));
                                            }
                                            continue;
                                        }
                                        else
                                        {
                                            Console.WriteLine("CANCELLED AS THIS IS BEST EXIT POINT for BUY trigger NOW with Loss of {0} with variance of {1} with topbb {2}", pos.PNL, (instruments[token].bot30bb + variance25), instruments[token].top30bb);
                                            if (order.ParentOrderId == null)
                                                kite.ModifyOrder(order.OrderId, OrderType: Constants.ORDER_TYPE_MARKET);
                                            else
                                                kite.CancelOrder(order.OrderId, Variety: "bo");
                                        }
                                    }
                                    else if ((currentTrend == OType.StrongBuy 
                                            || instruments[token].requiredExit)
                                        && pos.PNL < -300
                                        && (((instruments[token].bot30bb + variance2) < instruments[token].top30bb)
                                            || IsBetweenVariance((instruments[token].bot30bb + variance2), instruments[token].top30bb, (decimal).0006)))
                                    {
                                        if (instruments[token].isReversed
                                            && instruments[token].requiredExit 
                                            && pos.PNL > -4000)
                                        {
                                            if ((instruments[token].bot30bb + variance25) > instruments[token].top30bb)
                                            {
                                                if (pos.PNL < -1600)
                                                {
                                                    if ((instruments[token].doubledrequiredExit
                                                            || currentTrend == OType.StrongBuy)
                                                        && pos.PNL > -2300)
                                                    {
                                                        Console.WriteLine("23 CANCELL and Proceed ReORDERING for BUY trigger NOW with Loss of {0}", pos.PNL);
                                                        if (order.ParentOrderId == null)
                                                            kite.ModifyOrder(order.OrderId, OrderType: Constants.ORDER_TYPE_MARKET);
                                                        else
                                                            kite.CancelOrder(order.OrderId, Variety: "bo");
                                                    }
                                                    else
                                                    {
                                                        Console.WriteLine("Wait Cancellation as narrow range 25 for now as we are at loss of {0} with variance of {1} with topbb {2}", pos.PNL, (instruments[token].bot30bb + variance25), instruments[token].top30bb);
                                                        continue;
                                                    }
                                                }
                                                else
                                                {
                                                    Console.WriteLine("16 CANCELL and Proceed ReORDERING for BUY trigger NOW with Loss of {0}", pos.PNL);
                                                    if (order.ParentOrderId == null)
                                                        kite.ModifyOrder(order.OrderId, OrderType: Constants.ORDER_TYPE_MARKET);
                                                    else
                                                        kite.CancelOrder(order.OrderId, Variety: "bo");
                                                }
                                            }
                                            else
                                            {
                                                Console.WriteLine("CANCELL and Proceed ReORDERING for BUY trigger NOW with Loss of {0}", pos.PNL);
                                                if (order.ParentOrderId == null)
                                                    kite.ModifyOrder(order.OrderId, OrderType: Constants.ORDER_TYPE_MARKET);
                                                else
                                                    kite.CancelOrder(order.OrderId, Variety: "bo");
                                            }
                                        }
                                        else if ((pos.AveragePrice - trigger) > order.Price)
                                        {
                                            Console.WriteLine("HARD EXIT Required at {0} But WAITING from CANCELLATION:: The SELL order status of the script {1} is Severe LOSS point so modifying Exit Trigger to {2}; MA50 {3} & 5Minute Candle Open {4}", DateTime.Now.ToString(), instruments[token].futName, (pos.AveragePrice - trigger), instruments[token].ma50, instruments[token].history[instruments[token].history.Count - 2].Close);
                                            kite.ModifyOrder(order.OrderId, Price: (decimal)(pos.AveragePrice - trigger));
                                            continue;
                                        }
                                        else
                                        {
                                            Dictionary<string, LTP> ltps = kite.GetLTP(new string[] { token.ToString() });
                                            if (instruments[token].isReversed
                                                && instruments[token].requiredExit 
                                                && IsBetweenVariance(ltps[token.ToString()].LastPrice, instruments[token].middleBB, (decimal).0006))
                                            {
                                                Console.WriteLine("CANCELL and Stop ReORDERING for SELL trigger NOW with Loss of {0} as it is at better exit point", pos.PNL);
                                                if (order.ParentOrderId == null)
                                                    kite.ModifyOrder(order.OrderId, OrderType: Constants.ORDER_TYPE_MARKET);
                                                else
                                                    kite.CancelOrder(order.OrderId, Variety: "bo");
                                            }
                                            else
                                                Console.WriteLine("Wait from Further Changes for now as the current loss is {0}", pos.PNL);
                                            continue;
                                        }
                                    }
                                    else if (((instruments[token].bot30bb + variance2) < instruments[token].top30bb)
                                            || IsBetweenVariance((instruments[token].bot30bb + variance2), instruments[token].top30bb, (decimal).0006))
                                    {
                                        if (instruments[token].isReversed
                                            && instruments[token].requiredExit 
                                            && pos.PNL > -4000 
                                            && pos.PNL < -300)
                                        {
                                            Console.WriteLine("Due to Back to Original Trend, CANCELLED THE ORDER NOW with {0}", pos.PNL);
                                            if (order.ParentOrderId == null)
                                                kite.ModifyOrder(order.OrderId, OrderType: Constants.ORDER_TYPE_MARKET);
                                            else
                                                kite.CancelOrder(order.OrderId, Variety: "bo");
                                        }
                                        else if (!instruments[token].isReversed
                                            && currentTrend == OType.StrongBuy
                                            && instruments[token].requiredExit
                                            && pos.PNL > -2000
                                            && pos.PNL < -300)
                                        {
                                            Console.WriteLine("Due to Trend Reversal CANCELLED THE ORDER NOW with {0}", pos.PNL);
                                            if (order.ParentOrderId == null)
                                                kite.ModifyOrder(order.OrderId, OrderType: Constants.ORDER_TYPE_MARKET);
                                            else
                                                kite.CancelOrder(order.OrderId, Variety: "bo");
                                        }
                                        else if (instruments[token].isReversed
                                                && (pos.PNL >= -300
                                                    || instruments[token].doubledrequiredExit))
                                        {
                                            if ((instruments[token].bot30bb + variance2) < instruments[token].top30bb
                                                && pos.PNL >= 300)
                                            {
                                                Console.WriteLine("Due to Trend Reversal CANCELLED THE ORDER NOW with {0}", pos.PNL);
                                                if (order.ParentOrderId == null)
                                                    kite.ModifyOrder(order.OrderId, OrderType: Constants.ORDER_TYPE_MARKET);
                                                else
                                                    kite.CancelOrder(order.OrderId, Variety: "bo");
                                            }
                                            else
                                            {
                                                if ((pos.AveragePrice + trigger) < order.Price
                                                    && (IsBeyondVariance(instruments[token].weekMA, instruments[token].middle30BBnew, (decimal).006)
                                                        || instruments[token].doubledrequiredExit)
                                                    && pos.PNL < -300)
                                                {
                                                    Console.WriteLine("EXIT Required at {0} But Bollinger is marginally narrow :: The Sell order status of the script {1} is in serious LOSS point so modifying Exit Trigger to {2}; MA50 {3} & 5Minute Candle Open {4}", DateTime.Now.ToString(), instruments[token].futName, (pos.AveragePrice + trigger), instruments[token].ma50, instruments[token].history[instruments[token].history.Count - 2].Close);
                                                    kite.ModifyOrder(order.OrderId, Price: (decimal)(pos.AveragePrice + trigger));
                                                }
                                                else
                                                {
                                                    if ((pos.AveragePrice - trigger) > order.Price)
                                                    {
                                                        Console.WriteLine("EXIT Required at {0} But Bollinger is marginally narrow :: The Buy order status of the script {1} is in normal LOSS point so modifying Exit Trigger to {2}; MA50 {3} & 5Minute Candle Open {4}", DateTime.Now.ToString(), instruments[token].futName, (pos.AveragePrice - trigger), instruments[token].ma50, instruments[token].history[instruments[token].history.Count - 2].Close);
                                                        kite.ModifyOrder(order.OrderId, Price: (decimal)(pos.AveragePrice - trigger));
                                                    }
                                                    else if (instruments[token].oldTime != instruments[token].currentTime)
                                                    {
                                                        instruments[token].oldTime = instruments[token].currentTime;
                                                        Console.WriteLine("Wait Cancellation as BUY Order of {0} is just modified though loss is above 2500 as {1} and Exit trigger at {2}", pos.TradingSymbol, pos.LastPrice, (pos.AveragePrice - trigger));
                                                    }
                                                }
                                            }
                                            continue;
                                        }
                                        else if ((pos.AveragePrice - trigger) > order.Price)
                                        {
                                            if (currentTrend == OType.StrongBuy)
                                            {
                                                Console.WriteLine("EXIT Required at {0} But NO WAITING from CANCELLATION:: The SELL order status of the script {1} is in serious LOSS point so modifying Exit Trigger to {2}; MA50 {3} & 5Minute Candle Open {4}", DateTime.Now.ToString(), instruments[token].futName, (pos.AveragePrice - trigger), instruments[token].ma50, instruments[token].history[instruments[token].history.Count - 2].Close);
                                                if (order.ParentOrderId == null)
                                                    kite.ModifyOrder(order.OrderId, OrderType: Constants.ORDER_TYPE_MARKET);
                                                else
                                                    kite.CancelOrder(order.OrderId, Variety: "bo");
                                            }
                                            else
                                            {
                                                Console.WriteLine("EXIT Required at {0} But WAITING from CANCELLATION:: The SELL order status of the script {1} is in serious LOSS point so modifying Exit Trigger to {2}; MA50 {3} & 5Minute Candle Open {4}", DateTime.Now.ToString(), instruments[token].futName, (pos.AveragePrice - trigger), instruments[token].ma50, instruments[token].history[instruments[token].history.Count - 2].Close);
                                                kite.ModifyOrder(order.OrderId, Price: (decimal)(pos.AveragePrice - trigger));
                                            }
                                            continue;
                                        }
                                        else
                                            continue;
                                    }
                                    else if (!instruments[token].isReversed
                                            && instruments[token].requiredExit)
                                    {
                                        if (pos.PNL < -1000 && pos.PNL > -2000)
                                        {
                                            if (IsBetweenVariance(instruments[token].weekMA, instruments[token].top30bb, (decimal).004)
                                                && instruments[token].weekMA < instruments[token].top30bb)
                                            {
                                                Console.WriteLine("EXIT at {0} in case The SELL order script {1} is in necessary variance between WeekMA {2} & Middle30BB {3}", DateTime.Now.ToString(), instruments[token].futName, instruments[token].weekMA, instruments[token].middle30BB);
                                                if (order.ParentOrderId == null)
                                                    kite.ModifyOrder(order.OrderId, OrderType: Constants.ORDER_TYPE_MARKET);
                                                else
                                                    kite.CancelOrder(order.OrderId, Variety: "bo");
                                            }
                                        }
                                    }
                                    else
                                    {
                                        trigger = (decimal)1300 / (decimal)order.Quantity;
                                        trigger = Convert.ToDecimal(trigger.ToString("#.#")) + (decimal).05;
                                        if ((pos.AveragePrice - trigger) > order.Price)
                                        {
                                            if (currentTrend == OType.StrongBuy)
                                            {
                                                Console.WriteLine("EXIT Required at {0} But NO WAITING from CANCELLATION:: The SELL order status of the script {1} is in serious LOSS point so modifying Exit Trigger to {2}; MA50 {3} & 5Minute Candle Open {4}", DateTime.Now.ToString(), instruments[token].futName, (pos.AveragePrice - trigger), instruments[token].ma50, instruments[token].history[instruments[token].history.Count - 2].Close);
                                                if (order.ParentOrderId == null)
                                                    kite.ModifyOrder(order.OrderId, OrderType: Constants.ORDER_TYPE_MARKET);
                                                else
                                                    kite.CancelOrder(order.OrderId, Variety: "bo");
                                            }
                                            else
                                            {
                                                Console.WriteLine("EXIT Required at {0} But WAITING from CANCELLATION:: The SELL order status of the script {1} is in normal LOSS point so modifying Exit Trigger to target 1300 {2}; MA50 {3} & 5Minute Candle Open {4}", DateTime.Now.ToString(), instruments[token].futName, (pos.AveragePrice - trigger), instruments[token].ma50, instruments[token].history[instruments[token].history.Count - 2].Close);
                                                kite.ModifyOrder(order.OrderId, Price: (decimal)(pos.AveragePrice - trigger));
                                            }
                                        }
                                        else if (instruments[token].oldTime != instruments[token].currentTime)
                                        {
                                            instruments[token].oldTime = instruments[token].currentTime;
                                            Console.WriteLine("Variance less than 25, So Wait Cancellation as both WeekMA and Middle30BB are closer hence the SELL Order of {0} as loss is above 2500 as {1} and Exit trigger at {2}", pos.TradingSymbol, pos.LastPrice, (pos.AveragePrice + trigger));
                                        }
                                        continue;
                                    }

                                    if (niftyTrend != OType.Sell
                                            || instruments[token].requiredExit
                                            || (instruments[token].bot30bb + variance3) < instruments[token].top30bb
                                            || IsBetweenVariance((instruments[token].bot30bb + variance3), instruments[token].top30bb, (decimal).0006))
                                    {
                                        Console.WriteLine("Reordering the Order of {0} to As Nifty is in BUY side", pos.TradingSymbol);
                                        isReOrder = true;
                                    }
                                    if (isReOrder)
                                    {
                                        isReOrder = Decimal.Compare(timenow, Convert.ToDecimal(11.50)) < 0;
                                        if (!isReOrder)
                                        {
                                            Console.WriteLine("Time is Past 11.50AM. Verifying Clear Movement as Time now {0} with bot+variance {1} & top{2}", DateTime.Now.ToString(), (instruments[token].bot30bb + variance3), instruments[token].top30bb);
                                            isReOrder = (niftyTrend == OType.Buy
                                                        || (instruments[token].bot30bb + variance3) < instruments[token].top30bb
                                                            || IsBetweenVariance((instruments[token].bot30bb + variance3), instruments[token].top30bb, (decimal).0006))
                                                        && currentTrend == OType.StrongBuy;
                                                        //Decimal.Compare(timenow, Convert.ToDecimal(12.44)) > 0;
                                        }
                                        else
                                            Console.WriteLine("Timeis Less than 11.50AM as Time now {0}", DateTime.Now.ToString());
                                    }
                                }
                                else if (!isReOrder)
                                {
                                    if ((pos.AveragePrice - trigger) > order.Price)
                                    {
                                        if (((instruments[token].bot30bb + variance34) < instruments[token].top30bb
                                            || IsBetweenVariance((instruments[token].bot30bb + variance34), instruments[token].top30bb, (decimal).0006))
                                            && Decimal.Compare(timenow, (decimal)12.47) < 0)
                                        {
                                            Console.WriteLine("Cancel and reverse the position now at {0} :: The Sell order status of the script {1} is better point so modifying Exit Trigger to {2}; MA50 {3} & 5Minute Candle Open {4}", DateTime.Now.ToString(), instruments[token].futName, (pos.AveragePrice - trigger), instruments[token].ma50, instruments[token].history[instruments[token].history.Count - 2].Close);
                                            if (order.ParentOrderId == null)
                                                kite.ModifyOrder(order.OrderId, OrderType: Constants.ORDER_TYPE_MARKET);
                                            else
                                                kite.CancelOrder(order.OrderId, Variety: "bo");
                                            isReOrder = true;
                                        }
                                        else
                                        {
                                            Console.WriteLine("Need not necessarily EXIT NOW at {0} :: The Sell order status of the script {1} is better point so modifying Exit Trigger to {2}; MA50 {3} & 5Minute Candle Open {4}", DateTime.Now.ToString(), instruments[token].futName, (pos.AveragePrice - trigger), instruments[token].ma50, instruments[token].history[instruments[token].history.Count - 2].Close);
                                            kite.ModifyOrder(order.OrderId, Price: (decimal)(pos.AveragePrice - trigger));
                                            continue;
                                        }
                                    }
                                    else if (instruments[token].oldTime != instruments[token].currentTime)
                                    {
                                        instruments[token].oldTime = instruments[token].currentTime;
                                        Console.WriteLine("Wait Cancellation the SELL Order of {0} as loss is above 2500 as {1} and Exit trigger at {2}", pos.TradingSymbol, pos.LastPrice, (pos.AveragePrice - trigger));
                                        continue;
                                    }
                                    else
                                        continue;
                                }
                                if (isReOrder)
                                {
                                    System.Threading.Thread.Sleep(3000);
                                    Console.WriteLine("Possibly BUY NOW at {0} :: Commented RE Buy order status of the script {1} is better Reentry point NOW", DateTime.Now.ToString(), instruments[token].futName);
                                    /*
                                    instruments[token].status = Status.POSITION;
                                    instruments[token].type = OType.Buy;
                                    instruments[token].requiredExit = false;
                                    instruments[token].doubledrequiredExit = false;
                                    instruments[token].isReorder = true;
                                    instruments[token].longTrigger = instruments[token].middle30BB;
                                    instruments[token].shortTrigger = instruments[token].top30bb;
                                    placeOrder(token, 0);
                                    */
                                }
                                else
                                {
                                    System.Threading.Thread.Sleep(1500);
                                    Console.WriteLine("Refraining from ReOrder as Movement is not clear yet, at this juncture");
                                }
                                #endregion
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("EXCEPTION:: Caught while Processing Open Positions;; {0}", ex.Message);
            }
        }

        public bool ValidatingCurrentTrend(uint instrument, Tick tickData, OType type, decimal timenow)
        {
            bool flag = true;
            try
            {
                decimal ltp = tickData.LastPrice;
                if(type == OType.Buy)
                {
                    if ((ltp < instruments[instrument].middle30ma50 
                            && isNiftyVolatile 
                            && instruments[instrument].isNarrowed > 0
                            && ((instruments[instrument].bot30bb + Math.Round((ltp * (decimal)3.4),2)) > instruments[instrument].top30bb))
                        || (instruments[instrument].bot30bb < instruments[instrument].middle30ma50
                            && instruments[instrument].middle30BBnew > instruments[instrument].middle30ma50))
                    {
                        //if (IsBeyondVariance(instruments[instrument].bot30bb, instruments[instrument].middle30ma50, (decimal).0006))
                        {
                            flag = false;
                            if (instruments[instrument].oldTime != instruments[instrument].currentTime)
                            {
                                instruments[instrument].oldTime = instruments[instrument].currentTime;
                                Console.WriteLine("At {0} Script {1} is ignored as the moving average {2} is between Bottom 30BB {3} and Middle 30BB {4}", DateTime.Now.ToString(), instruments[instrument].futName, instruments[instrument].middle30ma50, instruments[instrument].bot30bb, instruments[instrument].middle30BB);
                            }
                        }
                    }
                }
                else if (type == OType.Sell)
                {
                    if ((ltp > instruments[instrument].middle30ma50
                            && isNiftyVolatile
                            && instruments[instrument].isNarrowed > 0
                            && ((instruments[instrument].bot30bb + Math.Round((ltp * (decimal)3.4), 2)) > instruments[instrument].top30bb))
                        || (instruments[instrument].top30bb > instruments[instrument].middle30ma50
                            && instruments[instrument].middle30BBnew < instruments[instrument].middle30ma50))
                    {
                        //if (IsBeyondVariance(instruments[instrument].top30bb, instruments[instrument].middle30ma50, (decimal).0006))
                        {
                            flag = false;
                            if (instruments[instrument].oldTime != instruments[instrument].currentTime)
                            {
                                instruments[instrument].oldTime = instruments[instrument].currentTime;
                                Console.WriteLine("At {0} Script {1} is ignored as the moving average {2} is between Top 30BB {3} and Middle 30BB {4}", DateTime.Now.ToString(), instruments[instrument].futName, instruments[instrument].middle30ma50, instruments[instrument].top30bb, instruments[instrument].middle30BB);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("EXCEPTION:: 'Validating Current Trend' {0}", ex.Message);
            }
            return flag;
        }

        public void VerifyCandleClose()
        {
            if (instruments != null && instruments.Count > 0)
            {
                Dictionary<UInt32, WatchList>.KeyCollection keys = instruments.Keys;
                decimal timenow = (decimal)DateTime.Now.Hour + ((decimal)DateTime.Now.Minute / 100);
                //OType niftyTrend = VerifyNifty(timenow);
                int expected = ExpectedCandleCount(null);
                Console.WriteLine("Time Stamp {0} Candle Check keys Count {1} && expected candle count {2}", DateTime.Now.ToString("yyyyMMdd hh:mm:ss"), keys.Count, expected);
                int index = 0;
                List<uint> closedTickers = new List<uint>();

                foreach (uint instrument in keys)
                {
                    try
                    {
                        if (instruments[instrument].middleBB > 0
                            && instruments[instrument].middle30BBnew > 0
                            && instruments[instrument].weekMA > 0
                            && instruments[instrument].status != Status.CLOSE
                            && !instruments[instrument].futName.Contains("CRUDE")
                            && !instruments[instrument].futName.Contains("NIFTY"))
                        {
                            #region Close Value should be after Middle 30BB
                            if (instruments[instrument].status == Status.OPEN)
                            {
                                List<Historical> history;
                                int counter = 0;
                                index = 0;
                                try
                                {
                                    do
                                    {
                                        System.Threading.Thread.Sleep(400);
                                        history = kite.GetHistoricalData(instrument.ToString(),
                                                    DateTime.Now.Date.AddHours(9).AddMinutes(15),
                                                    //DateTime.Now.Date.AddHours(9).AddMinutes(45),
                                                    DateTime.Now.Date.AddDays(1),
                                                    "30minute");
                                        counter++;
                                        if (history.Count < 2)
                                            System.Threading.Thread.Sleep(1000);
                                        if (history.Count != expected && expected != 0)
                                        {
                                            Console.WriteLine("EXCEPTION as history list is less than expected number of candles with count {0} against expected {1}", history.Count, expected);
                                            if (history.Count == (expected - 1) && history.Count > 0)
                                            {
                                                Console.WriteLine("Consider Changing INDEX value to {0} as expected to {1}", history.Count, expected - 1);
                                                index = history.Count - 1;
                                            }
                                            else
                                            {
                                                Console.WriteLine("Going with default -2 from available index count {0} against expected to {1}", history.Count, expected - 1);
                                                index = history.Count - 2;
                                            }
                                        }
                                        else
                                            index = history.Count - 2;
                                    } while ((index + 2) != expected && counter < 3);
                                }
                                catch
                                {
                                    System.Threading.Thread.Sleep(1000);
                                    history = kite.GetHistoricalData(instrument.ToString(),
                                                DateTime.Now.Date.AddHours(9).AddMinutes(15),
                                                //DateTime.Now.Date.AddHours(9).AddMinutes(46),
                                                DateTime.Now.Date.AddDays(1),
                                                "30minute");
                                    index = history.Count - 2;
                                }
                                if (index < 0)
                                {
                                    Console.WriteLine("EXCEPTION 1 :: Index value {0} is less than ZERO and probability of OUT OF RANGE EXCEPTION is HIGH with candle count {1} vs expected {2}", index, history.Count);
                                    continue;
                                }
                                decimal ltp = history[index].Close;
                                decimal minRange = Convert.ToDecimal(((ltp * (decimal).25) / 100).ToString("#.#"));
                                if (index == 0
                                    || (index == 1 && isNiftyVolatile))
                                //&& instruments[instrument].isOpenAlign && instruments[instrument].canTrust)
                                {
                                    //Console.WriteLine("Time Stamp {0} Candle Check for {1} and Last Candle Close Value is {2}", history[index].TimeStamp, instruments[instrument].futName, ltp);
                                    #region first candle
                                    if (IsBetweenVariance(history[index].Close, instruments[instrument].top30bb, (decimal).002)
                                            || history[index].Close > instruments[instrument].top30bb
                                            || IsBetweenVariance(history[index].Close, instruments[instrument].fTop30bb, (decimal).001)
                                            || history[index].Close > instruments[instrument].fTop30bb)
                                    {
                                        Console.WriteLine("Time Stamp {0} FCandle Check for {1} is closed as Last Candle Close Value is {2}", history[index].TimeStamp, instruments[instrument].futName, ltp);
                                        instruments[instrument].isVolatile = true;
                                        CloseOrderTicker(instrument, false);
                                        continue;
                                    }
                                    else if (IsBetweenVariance(history[index].Close, instruments[instrument].bot30bb, (decimal).002)
                                            || history[index].Close < instruments[instrument].bot30bb
                                            || IsBetweenVariance(history[index].Close, instruments[instrument].fBot30bb, (decimal).001)
                                            || history[index].Close < instruments[instrument].fBot30bb)
                                    {
                                        Console.WriteLine("Time Stamp {0} FCandle Check for {1} is closed as Last Candle Close Value is {2}", history[index].TimeStamp, instruments[instrument].futName, ltp);
                                        instruments[instrument].isVolatile = true;
                                        CloseOrderTicker(instrument, false);
                                        continue;
                                    }
                                    #endregion
                                }
                                if (index != 0)
                                {
                                    #region not first Candle
                                    decimal variance = isNiftyVolatile ? (decimal).002 : (decimal).001;
                                    if (IsBetweenVariance(history[index].Close, instruments[instrument].top30bb, variance)
                                            || history[index].Close > instruments[instrument].top30bb)
                                    {
                                        Console.WriteLine("Time Stamp {0} Candle Check for {1} is closed as Last Candle Close Value is {2} & top30 is {3}", history[index].TimeStamp, instruments[instrument].futName, ltp, instruments[instrument].top30bb);
                                        if ((isNiftyVolatile
                                                && history[index].Close > instruments[instrument].top30bb
                                                && IsBeyondVariance(history[index].Close, instruments[instrument].top30bb, (decimal).001))
                                            || (history[index].Close > instruments[instrument].top30bb
                                                && IsBeyondVariance(history[index].Close, instruments[instrument].top30bb, (decimal).002)))
                                        {
                                            CloseOrderTicker(instrument, false);
                                            continue;
                                        }
                                    }
                                    else if (IsBetweenVariance(history[index].Close, instruments[instrument].bot30bb, variance)
                                            || history[index].Close < instruments[instrument].bot30bb)
                                    {
                                        Console.WriteLine("Time Stamp {0} Candle Check for {1} is closed as Last Candle Close Value is {2} & bot30 is {3}", history[index].TimeStamp, instruments[instrument].futName, ltp, instruments[instrument].bot30bb);
                                        if ((isNiftyVolatile
                                                && history[index].Close < instruments[instrument].bot30bb
                                                && IsBeyondVariance(history[index].Close, instruments[instrument].bot30bb, (decimal).001))
                                            || (history[index].Close < instruments[instrument].bot30bb
                                                && IsBeyondVariance(history[index].Close, instruments[instrument].bot30bb, (decimal).002)))
                                        {
                                            CloseOrderTicker(instrument, false);
                                            continue;
                                        }
                                    }
                                    #endregion
                                }
                                if (instruments[instrument].canTrust)
                                {
                                    try
                                    {
                                        Verify30MinCandleClose(instrument, history[index].Close, index, history);
                                    }
                                    catch (Exception ex)
                                    {
                                        Console.WriteLine("EXCEPTIO CAUGHT in CandleClose more than 2nd index of {0} with message {1}", instruments[instrument].futName, ex.Message);
                                    }
                                }
                                if (!instruments[instrument].canTrust)
                                {
                                    if (instruments[instrument].status == Status.OPEN)
                                    {
                                        if (history[index].Close < instruments[instrument].top30bb
                                                && history[index].High > instruments[instrument].top30bb
                                                && IsBeyondVariance(history[index].High, history[index].Close, (decimal).004)
                                                && IsBeyondVariance(instruments[instrument].top30bb, history[index].Close, (decimal).0005))
                                        {
                                            Console.WriteLine("Time Stamp {0} Averting Candle but do nothing for Cannot-Be-Trusted script: Candle Breakout above 30BB {1} or below30BB {2} for Script {3} and the LTP is {4}", DateTime.Now.ToString("yyyyMMdd hh:mm:ss"), instruments[instrument].top30bb, instruments[instrument].bot30bb, instruments[instrument].futName, ltp);
                                        }
                                        else if (history[index].Close > instruments[instrument].bot30bb
                                                && history[index].Low < instruments[instrument].bot30bb
                                                && IsBeyondVariance(history[index].Low, history[index].Close, (decimal).004)
                                                && IsBeyondVariance(instruments[instrument].bot30bb, history[index].Close, (decimal).0005))
                                        {
                                            Console.WriteLine("Time Stamp {0} Averting Candle but do nothing for Cannot-Be-Trusted script: Candle Breakout above 30BB {1} or below30BB {2} for Script {3} and the LTP is {4}", DateTime.Now.ToString("yyyyMMdd hh:mm:ss"), instruments[instrument].top30bb, instruments[instrument].bot30bb, instruments[instrument].futName, ltp);
                                        }
                                    }
                                    else if (instruments[instrument].status == Status.POSITION)
                                    {
                                        if (instruments[instrument].requiredExit
                                            || instruments[instrument].doubledrequiredExit)
                                        {
                                            //Do nothing
                                        }
                                        else
                                        {
                                            Console.WriteLine("Time Stamp {0} You are in Soup as Averting Candle given by Cannot-Be-Trusted script: Candle Breakout above 30BB {1} or below30BB {2} for Script {3} and the LTP is {4}", DateTime.Now.ToString("yyyyMMdd hh:mm:ss"), instruments[instrument].top30bb, instruments[instrument].bot30bb, instruments[instrument].futName, ltp);
                                            Position pos = new Position();
                                            if (GetCurrentPNL(instruments[instrument].futId, ref pos))
                                            {
                                                ModifyOrderForContract(pos, instrument, 1300);
                                            }
                                        }
                                        continue;
                                    }
                                    else
                                    {
                                        CloseOrderTicker(instrument, false);
                                        Console.WriteLine("Time Stamp {0} What state is this? Cannot-Be-Trusted script is closed as the state is unknown: Candle Breakout above 30BB {1} or below30BB {2} for Script {3} and the LTP is {4}", DateTime.Now.ToString("yyyyMMdd hh:mm:ss"), instruments[instrument].top30bb, instruments[instrument].bot30bb, instruments[instrument].futName, ltp);
                                        continue;
                                    }
                                    if (instruments[instrument].status != Status.POSITION)
                                    {
                                        if (history[index].Close > instruments[instrument].middle30ma50
                                            && (history[index].Low <= instruments[instrument].middle30ma50
                                                || IsBetweenVariance(history[index].Low, instruments[instrument].middle30ma50, (decimal).0013))
                                            && instruments[instrument].middle30ma50 > instruments[instrument].bot30bb)
                                        {
                                            Console.WriteLine("Time Stamp {0} Script {1} has just stopped above MA50 {2} though cannot be trusted as current high is {3}. hence watchlist is closing this script", DateTime.Now.ToString("yyyyMMdd hh:mm:ss"), instruments[instrument].futName, instruments[instrument].middle30ma50, history[index].High);
                                            instruments[instrument].type = OType.Sell;
                                            instruments[instrument].canTrust = true;
                                            instruments[instrument].isReversed = false;
                                            continue;
                                        }
                                        else if (history[index].Close < instruments[instrument].middle30ma50
                                                && (history[index].High >= instruments[instrument].middle30ma50
                                                    || IsBetweenVariance(history[index].High, instruments[instrument].middle30ma50, (decimal).0013))
                                                && instruments[instrument].middle30ma50 < instruments[instrument].top30bb)
                                        {
                                            Console.WriteLine("Time Stamp {0} Script {1} has just stopped below MA50 {2} though cannot be trusted as current low is {3}. hence watchlist is closing this script", DateTime.Now.ToString("yyyyMMdd hh:mm:ss"), instruments[instrument].futName, instruments[instrument].middle30ma50, history[index].Low);
                                            instruments[instrument].type = OType.Buy;
                                            instruments[instrument].canTrust = true;
                                            instruments[instrument].isReversed = false;
                                            continue;
                                        }
                                    }
                                }
                            }
                            #endregion
                        }
                        else
                        {
                            Console.WriteLine("WARNING ::: Time Stamp {0} Candle Check is Unknown for {1} ", DateTime.Now.ToString(), instruments[instrument].futName);
                            if (Decimal.Compare(timenow, (decimal)9.50) < 0 && !instruments[instrument].futName.Contains("NIFTY"))
                                CloseOrderTicker(instrument, false);
                        }
                        if (instruments[instrument].status == Status.CLOSE)
                            closedTickers.Add(instrument);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine("EXCEPTIO CAUGHT in CandleClose of {0} with message {1}", instruments[instrument].futName, ex.Message);
                    }
                }
                try
                {
                    if (closedTickers.Count > 0)
                    {
                        foreach (uint instrument in closedTickers)
                        {
                            instruments.Remove(instrument);
                        }
                        Console.WriteLine("Successfully Closed Order Tickers at {0} # {1}", DateTime.Now.ToString(), closedTickers.Count.ToString());
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine("EXCEPTIO CAUGHT While Removing Closed tickers from the List with message {0} at {1}", ex.Message, DateTime.Now.ToString());
                }
                //putStatusContent();
                Console.WriteLine("Set ticking to True; At Time {0}", DateTime.Now.ToString("yyyyMMdd hh:mm:ss"));
                startTicking = true;
            }
        }

        public bool CalculateSqueez(uint instrument, Tick tickData)
        {
            try
            {
                WatchList wl = instruments[instrument];
                decimal timenow = (decimal)DateTime.Now.Hour + ((decimal)DateTime.Now.Minute / 100);
                if (wl.middleBB > 0 && wl.middle30BBnew > 0 && wl.weekMA > 0
                    && (wl.currentTime != wl.oldTime)
                    && !wl.futName.Contains("CRUDE"))
                {
                    decimal ltp = tickData.LastPrice;
                    instruments[instrument].oldTime = instruments[instrument].currentTime;
                    #region MA50 Cross Over
                    decimal range = Convert.ToDecimal(((ltp * (decimal)1.2) / 100).ToString("#.#"));
                    decimal minRange = Convert.ToDecimal(((ltp * (decimal).6) / 100).ToString("#.#"));
                    if (Decimal.Compare(timenow, (decimal)(10.15)) > 0
                        && (wl.topBB - wl.botBB) < range
                        && (wl.topBB - wl.botBB) > minRange)
                    {
                        if (instruments[instrument].isReversed
                            && instruments[instrument].type == OType.Buy)
                        {
                            Console.WriteLine("Time Stamp {0} This script {1} is Already REVERSED: Recommended to place BUY at Middle30BB {2}", DateTime.Now.ToString("yyyyMMdd hh:mm:ss"), wl.futName, instruments[instrument].middle30BB);
                            //return (IsBetweenVariance(ltp, instruments[instrument].middle30BB, (decimal).002)
                            //        && ltp > instruments[instrument].middle30BB)
                            //    || (IsBetweenVariance(ltp, instruments[instrument].weekMA, (decimal).002)
                            //        && ltp > instruments[instrument].weekMA);
                        }
                        if (instruments[instrument].isReversed
                            && instruments[instrument].type == OType.Sell)
                        {
                            Console.WriteLine("Time Stamp {0} This script {1} is Already REVERSED: Recommended to place SELL at Middle30BB {2}", DateTime.Now.ToString("yyyyMMdd hh:mm:ss"), wl.futName, instruments[instrument].middle30BB);
                            //return (IsBetweenVariance(ltp, instruments[instrument].middle30BB, (decimal).002)
                            //        && ltp < instruments[instrument].middle30BB)
                            //    || (IsBetweenVariance(ltp, instruments[instrument].weekMA, (decimal).002)
                            //        && ltp < instruments[instrument].weekMA);
                        }
                        if (IsBetweenVariance(wl.middleBB, wl.ma50, (decimal).0003))
                        {
                            //Look into it in FUTURE
                            if (wl.type == OType.Buy && ltp < wl.middleBB)
                            {
                                //Console.WriteLine("Time Stamp {0} : CrossOver; Recommended to Place SELL at {1} as 50 Moving average and 20 Moving average are crossed over, 20 MA = {2}; 50 MA = {3}; for Script {4}", DateTime.Now.ToString("yyyyMMdd hh:mm:ss"), ltp, wl.middleBB, wl.ma50, wl.futName);
                                //instruments[token].type = OType.Sell;
                                //return true;
                            }
                            else if (wl.type == OType.Sell && ltp > wl.middleBB)
                            {
                                //Console.WriteLine("Time Stamp {0} : CrossOver; Recommended to Place BUY at {1} as 50 Moving average and 20 Moving average are crossed over, 20 MA = {2}; 50 MA = {3}; for Script {4}", DateTime.Now.ToString("yyyyMMdd hh:mm:ss"), ltp, wl.middleBB, wl.ma50, wl.futName);
                                //instruments[token].type = OType.Buy;
                                //return true;
                            }
                        }
                    }
                    #endregion
                }
                else
                {
                    //Console.WriteLine("Time Stamp {0} : For Script {1} 3 minute Bollinger band is {2} & 3 minute MA50 is {3}", DateTime.Now.ToString("yyyyMMdd hh:mm:ss"), wl.futName, wl.topBB, wl.botBB);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("EXCEPTION:: 'Calculate Squeez' Time Stamp {0}:: Script Name {1} & Message {2}", DateTime.Now.ToString("yyyyMMdd hh:mm:ss"), instruments[instrument].futName, ex.Message);
            }
            return false;
        }

        void Verify30MinCandleClose(uint instrument, decimal ltp, int index, List<Historical> history)
        {
            decimal range = Convert.ToDecimal(((ltp * (decimal)1.35) / 100).ToString("#.#"));
            decimal mRange = Convert.ToDecimal(((ltp * (decimal).25) / 100).ToString("#.#"));
            //decimal minRange = Convert.ToDecimal(((ltp * (decimal)0.6) / 100).ToString("#.#"));
            decimal timenow = (decimal)DateTime.Now.Hour + ((decimal)DateTime.Now.Minute / 100);
            //Console.WriteLine("Time Stamp {0} Candle Check for {1} with Candle Index Position {2} and Last Candle Close Value is {3}", history[index].TimeStamp, instruments[instrument].futName, index, ltp);
            if (instruments[instrument].canTrust)
            {
                #region is OpenAlign
                if (instruments[instrument].type == OType.Buy
                    && !instruments[instrument].isReversed)
                {
                    if (history[index].Close < instruments[instrument].weekMA
                            && history[index].Close > instruments[instrument].middle30BB
                            //&& history[index].Close > instruments[instrument].middle30BBnew
                            && instruments[instrument].weekMA > instruments[instrument].middle30BBnew
                            && history[index].Low <= instruments[instrument].middle30BBnew
                            && IsBetweenVariance(instruments[instrument].weekMA, instruments[instrument].middle30BBnew, (decimal).006)
                            && IsBeyondVariance(instruments[instrument].weekMA, instruments[instrument].middle30BBnew, (decimal).002)
                            //&& history[index - 1].Close > instruments[instrument].weekMA
                            && history[index].Close > instruments[instrument].bot30bb
                            && (history[index].Close - instruments[instrument].bot30bb) > mRange)
                    {
                        Console.WriteLine("Time Stamp {0}  -VE TREND: But JUST Place BUY ORDER at WEEKMA {1} for Script {2} and the LTP is {3}", DateTime.Now.ToString("yyyyMMdd hh:mm:ss"), instruments[instrument].weekMA, instruments[instrument].futName, ltp);
                        instruments[instrument].type = OType.Buy;
                        instruments[instrument].isReversed = true;
                        instruments[instrument].isVolatile = true;
                        instruments[instrument].ReversedTime = DateTime.Now;
                        instruments[instrument].longTrigger = instruments[instrument].middle30BBnew;
                        modifyOpenAlignOrReversedStatus(instruments[instrument], 17, OType.Buy, true);
                        //return true;
                    }
                    else if (history[index].Open > instruments[instrument].middle30BB
                            && history[index].Close < instruments[instrument].middle30BB
                            && IsBeyondVariance(history[index].Close, instruments[instrument].middle30BB, (decimal).0004)
                            && IsBeyondVariance(history[index].Close, instruments[instrument].middle30BBnew, (decimal).0004)
                            && IsBeyondVariance(instruments[instrument].weekMA, instruments[instrument].middle30BBnew, (decimal).002)
                            && (instruments[instrument].fBot30bb > instruments[instrument].bot30bb
                                || IsBetweenVariance(instruments[instrument].fBot30bb, instruments[instrument].bot30bb, (decimal).005)))
                    {
                        if (index > 0)
                        {
                            if ((history[index].Close > instruments[instrument].weekMA
                                    && IsBetweenVariance(history[index].Close, instruments[instrument].weekMA, (decimal).003))
                                    || (history[index - 1].Open <= instruments[instrument].middle30BB
                                        && history[index - 1].Close > instruments[instrument].middle30BB))
                            {
                                Console.WriteLine("Time Stamp {0}  -VE TREND: Recommending 1 but return false for Buy ORDER at WEEKMA {1} for Script {2} and the LTP is {3}", DateTime.Now.ToString("yyyyMMdd hh:mm:ss"), instruments[instrument].weekMA, instruments[instrument].futName, ltp);
                                return;
                            }
                        }
                        Console.WriteLine("Time Stamp {0} CLOSE BELOW middle 30BB: Recommended to Place REVERSE SELL ORDER for Script {2} at MIDDLE 30BB {1} and the LTP is {3}", DateTime.Now.ToString("yyyyMMdd hh:mm:ss"), instruments[instrument].middle30BB, instruments[instrument].futName, ltp);
                        instruments[instrument].canOrder = true;
                        instruments[instrument].isReversed = true;
                        instruments[instrument].ReversedTime = DateTime.Now;
                        instruments[instrument].type = OType.Sell;
                        instruments[instrument].shortTrigger = instruments[instrument].middle30BBnew;
                        instruments[instrument].longTrigger = instruments[instrument].bot30bb;
                        modifyOpenAlignOrReversedStatus(instruments[instrument], 17, OType.Sell, true);
                    }
                    else if (history[index].Close < instruments[instrument].middle30BB
                            && IsBeyondVariance(history[index].Close, instruments[instrument].middle30BB, (decimal).0004)
                            && IsBeyondVariance(history[index].Close, instruments[instrument].middle30BBnew, (decimal).0004)
                            && IsBeyondVariance(instruments[instrument].weekMA, instruments[instrument].middle30BBnew, (decimal).002)
                            && (instruments[instrument].fBot30bb > instruments[instrument].bot30bb
                                || IsBetweenVariance(instruments[instrument].fBot30bb, instruments[instrument].bot30bb, (decimal).005)))
                    {
                        if (index > 0)
                        {
                            if ((history[index].Close > instruments[instrument].weekMA
                                    && IsBetweenVariance(history[index].Close, instruments[instrument].weekMA, (decimal).003))
                                    || (history[index - 1].Open <= instruments[instrument].middle30BB
                                        && history[index - 1].Close > instruments[instrument].middle30BB))
                            {
                                Console.WriteLine("Time Stamp {0}  -VE TREND: Recommending 2 but return false for Buy ORDER at WEEKMA {1} for Script {2} and the LTP is {3}", DateTime.Now.ToString("yyyyMMdd hh:mm:ss"), instruments[instrument].weekMA, instruments[instrument].futName, ltp);
                                return;
                            }
                        }
                        instruments[instrument].type = OType.Buy;
                        instruments[instrument].shortTrigger = instruments[instrument].top30bb;
                        instruments[instrument].longTrigger = instruments[instrument].middle30BBnew;
                        modifyOpenAlignOrReversedStatus(instruments[instrument], 17, OType.Buy, true);
                    }
                    else
                    {
                        if (history[index].Close < instruments[instrument].middle30BB)
                        {
                            int candleCount = 0;
                            if (CheckGearingStatus(instrument, OType.Sell, ref candleCount)
                                || CheckGearingStatus(instrument, OType.Sell))
                            {
                                Console.WriteLine("Time Stamp {0} CLOSE BELOW middle 30BB not beyond variance: But Recommended to Place REVERSE SELL ORDER for Script {2} at MIDDLE 30BB {1} and the LTP is {3}", DateTime.Now.ToString("yyyyMMdd hh:mm:ss"), instruments[instrument].middle30BB, instruments[instrument].futName, ltp);
                                instruments[instrument].canOrder = true;
                                instruments[instrument].type = OType.Sell;
                                instruments[instrument].shortTrigger = instruments[instrument].middle30BBnew;
                                instruments[instrument].longTrigger = instruments[instrument].bot30bb;
                                instruments[instrument].isReversed = true;
                                instruments[instrument].ReversedTime = DateTime.Now;
                                modifyOpenAlignOrReversedStatus(instruments[instrument], 17, OType.Sell, true);
                            }
                            else
                                Console.WriteLine("Time Stamp {0} CLOSE BELOW middle 30BB: But Script {2} is not beyond variance at MIDDLE 30BB {1} and the LTP is {3}", DateTime.Now.ToString("yyyyMMdd hh:mm:ss"), instruments[instrument].middle30BB, instruments[instrument].futName, ltp);
                        }
                        else
                            Console.WriteLine("Time Stamp {0} CLOSE ABOVE middle 30BB only, still: For Script {2} with MIDDLE 30BB {1} and the LTP is {3}", DateTime.Now.ToString("yyyyMMdd hh:mm:ss"), instruments[instrument].middle30BB, instruments[instrument].futName, ltp);
                    }
                }
                else if (instruments[instrument].type == OType.Sell
                    && !instruments[instrument].isReversed)
                {
                    if (history[index].Close > instruments[instrument].weekMA
                            && history[index].Close < instruments[instrument].middle30BB
                            //&& history[index].Close < instruments[instrument].middle30BBnew
                            && instruments[instrument].weekMA < instruments[instrument].middle30BBnew
                            && history[index].High >= instruments[instrument].middle30BBnew
                            && IsBetweenVariance(instruments[instrument].weekMA, instruments[instrument].middle30BBnew, (decimal).006)
                            && IsBeyondVariance(instruments[instrument].weekMA, instruments[instrument].middle30BBnew, (decimal).002)
                            //&& history[index - 1].Close < instruments[instrument].weekMA
                            && history[index].Close < instruments[instrument].top30bb
                            && (instruments[instrument].top30bb - history[index].Close) > mRange)
                    {
                        Console.WriteLine("Time Stamp {0}  +VE TREND: But JUST Place SELL ORDER at WEEKMA {1} for Script {2} and the LTP is {3}", DateTime.Now.ToString("yyyyMMdd hh:mm:ss"), instruments[instrument].weekMA, instruments[instrument].futName, ltp);
                        instruments[instrument].type = OType.Sell;
                        instruments[instrument].isReversed = true;
                        instruments[instrument].isVolatile = true;
                        instruments[instrument].ReversedTime = DateTime.Now;
                        instruments[instrument].shortTrigger = instruments[instrument].middle30BBnew;
                        modifyOpenAlignOrReversedStatus(instruments[instrument], 17, OType.Sell, true);
                        //return true;
                    }
                    else if (history[index].Open < instruments[instrument].middle30BB
                            && history[index].Close > instruments[instrument].middle30BB
                            && IsBeyondVariance(history[index].Close, instruments[instrument].middle30BB, (decimal).0004)
                            && IsBeyondVariance(history[index].Close, instruments[instrument].middle30BBnew, (decimal).0004)
                            && IsBeyondVariance(instruments[instrument].weekMA, instruments[instrument].middle30BBnew, (decimal).002)
                            && (instruments[instrument].fTop30bb < instruments[instrument].top30bb
                                || IsBetweenVariance(instruments[instrument].fTop30bb, instruments[instrument].top30bb, (decimal).005)))
                    {
                        if (index > 0)
                        {
                            if ((history[index].Close < instruments[instrument].weekMA
                                        && IsBetweenVariance(instruments[instrument].weekMA, history[index].Close, (decimal).003))
                                    || (history[index - 1].Open >= instruments[instrument].middle30BB
                                        && history[index - 1].Close < instruments[instrument].middle30BB))
                            {
                                Console.WriteLine("Time Stamp {0}  -VE TREND: Recommending 1 but return false for SELL ORDER at WEEKMA {1} for Script {2} and the LTP is {3}", DateTime.Now.ToString("yyyyMMdd hh:mm:ss"), instruments[instrument].weekMA, instruments[instrument].futName, ltp);
                                return;
                            }
                        }
                        Console.WriteLine("Time Stamp {0} CLOSE ABOVE middle 30BB: Recommended to Place REVERSE BUY ORDER for Script {2} at MIDDLE 30BB {1} and the LTP is {3}", DateTime.Now.ToString("yyyyMMdd hh:mm:ss"), instruments[instrument].middle30BB, instruments[instrument].futName, ltp);
                        instruments[instrument].type = OType.Buy;
                        instruments[instrument].canOrder = true;
                        instruments[instrument].isReversed = true;
                        instruments[instrument].ReversedTime = DateTime.Now;
                        instruments[instrument].shortTrigger = instruments[instrument].top30bb;
                        instruments[instrument].longTrigger = instruments[instrument].middle30BBnew;
                        modifyOpenAlignOrReversedStatus(instruments[instrument], 17, OType.Buy, true);
                    }
                    else if (history[index].Close > instruments[instrument].middle30BB
                            && IsBeyondVariance(history[index].Close, instruments[instrument].middle30BB, (decimal).0004)
                            && IsBeyondVariance(history[index].Close, instruments[instrument].middle30BBnew, (decimal).0004)
                            && IsBeyondVariance(instruments[instrument].weekMA, instruments[instrument].middle30BBnew, (decimal).002)
                            && (instruments[instrument].fTop30bb < instruments[instrument].top30bb
                                || IsBetweenVariance(instruments[instrument].fTop30bb, instruments[instrument].top30bb, (decimal).005)))
                    {
                        if (index > 0)
                        {
                            if ((history[index].Close < instruments[instrument].weekMA
                                        && IsBetweenVariance(instruments[instrument].weekMA, history[index].Close, (decimal).003))
                                    || (history[index - 1].Open >= instruments[instrument].middle30BB
                                        && history[index - 1].Close < instruments[instrument].middle30BB))
                            {
                                Console.WriteLine("Time Stamp {0}  -VE TREND: Recommending 2 but return false for SELL ORDER at WEEKMA {1} for Script {2} and the LTP is {3}", DateTime.Now.ToString("yyyyMMdd hh:mm:ss"), instruments[instrument].weekMA, instruments[instrument].futName, ltp);
                                return;
                            }
                        }
                        instruments[instrument].type = OType.Sell;
                        instruments[instrument].shortTrigger = instruments[instrument].middle30BBnew;
                        instruments[instrument].longTrigger = instruments[instrument].bot30bb;
                        modifyOpenAlignOrReversedStatus(instruments[instrument], 17, OType.Sell, true);
                    }
                    else
                    {
                        if (history[index].Close > instruments[instrument].middle30BB)
                        {
                            int candleCount = 0;
                            if (CheckGearingStatus(instrument, OType.Buy, ref candleCount)
                                || CheckGearingStatus(instrument, OType.Buy))
                            {
                                Console.WriteLine("Time Stamp {0} CLOSE ABOVE middle 30BB not beyond variance: Recommended to Place REVERSE BUY ORDER for Script {2} at MIDDLE 30BB {1} and the LTP is {3}", DateTime.Now.ToString("yyyyMMdd hh:mm:ss"), instruments[instrument].middle30BB, instruments[instrument].futName, ltp);
                                instruments[instrument].canOrder = true;
                                instruments[instrument].type = OType.Buy;
                                instruments[instrument].shortTrigger = instruments[instrument].top30bb;
                                instruments[instrument].longTrigger = instruments[instrument].middle30BBnew;
                                instruments[instrument].isReversed = true;
                                instruments[instrument].ReversedTime = DateTime.Now;
                                modifyOpenAlignOrReversedStatus(instruments[instrument], 17, OType.Buy, true);
                            }
                            else
                                Console.WriteLine("Time Stamp {0} CLOSE ABOVE middle 30BB: But Script {2} is not beyond variance at MIDDLE 30BB {1} and the LTP is {3}", DateTime.Now.ToString("yyyyMMdd hh:mm:ss"), instruments[instrument].middle30BB, instruments[instrument].futName, ltp);
                        }
                        else
                            Console.WriteLine("Time Stamp {0} CLOSE BELOW middle 30BB only, still: For Script {2} with MIDDLE 30BB {1} and the LTP is {3}", DateTime.Now.ToString("yyyyMMdd hh:mm:ss"), instruments[instrument].middle30BB, instruments[instrument].futName, ltp);
                    }
                }
                else if (instruments[instrument].type == OType.Buy && instruments[instrument].isReversed)
                {
                    if (history[index].Close < instruments[instrument].middle30BBnew)
                    {
                        Console.WriteLine("Time Stamp {0} Script {2} is Back to Track from Reverse (BUY to Sell) as close is again below MIDDLE 30BB {1} and the LTP is {3}", DateTime.Now.ToString("yyyyMMdd hh:mm:ss"), instruments[instrument].middle30BB, instruments[instrument].futName, ltp);
                        if (instruments[instrument].status != Status.POSITION)
                        {
                            instruments[instrument].type = OType.Sell;
                            instruments[instrument].canTrust = true;
                            instruments[instrument].isReversed = true;
                        }
                        if (instruments[instrument].top30bb - instruments[instrument].bot30bb < Math.Round(instruments[instrument].top30bb * (decimal).03, 1))
                            Console.WriteLine("Time Stamp {0} Script {1} is not safe to mark BUY Order again", DateTime.Now.ToString("yyyyMMdd hh:mm:ss"), instruments[instrument].futName);
                        if (instruments[instrument].goodToGo
                            && instruments[instrument].top30bb - instruments[instrument].bot30bb >
                            Math.Round(instruments[instrument].top30bb * (decimal).023, 1))
                            instruments[instrument].toSell = true;
                        instruments[instrument].toBuy = false;
                    }
                    else if (history[index].Close < instruments[instrument].middle30ma50
                            && (history[index].High >= instruments[instrument].middle30ma50
                                || IsBetweenVariance(history[index].High, instruments[instrument].middle30ma50, (decimal).0006))
                            && instruments[instrument].middle30ma50 < instruments[instrument].top30bb)
                    {
                        //&& (IsBetweenVariance(history[index].High, instruments[instrument].res1, (decimal).0006)
                        //|| history[index].High < instruments[instrument].res1)
                        Console.WriteLine("Time Stamp {0} Trusted Script {1} has just stopped below MA50 {2} as current high is {3}. hence watchlist is closing this script", DateTime.Now.ToString("yyyyMMdd hh:mm:ss"), instruments[instrument].futName, instruments[instrument].middle30ma50, history[index].High);
                        if (instruments[instrument].status != Status.POSITION)
                        {
                            instruments[instrument].canTrust = true;
                            instruments[instrument].isReversed = false;
                        }
                    }
                    else
                    {
                        Console.WriteLine("Time Stamp {0} Script {2} is Reversed for BUY and waiting for perfect trigger at MIDDLE 30BB {1} and the LTP is {3}", DateTime.Now.ToString("yyyyMMdd hh:mm:ss"), instruments[instrument].middle30BB, instruments[instrument].futName, ltp);
                    }
                }
                else if (instruments[instrument].type == OType.Sell && instruments[instrument].isReversed)
                {
                    if (history[index].Close > instruments[instrument].middle30BBnew)
                    {
                        Console.WriteLine("Time Stamp {0} Script {2} is Back to Track from Reverse (SELL to Buy) as close is again above MIDDLE 30BB {1} and the LTP is {3}", DateTime.Now.ToString("yyyyMMdd hh:mm:ss"), instruments[instrument].middle30BB, instruments[instrument].futName, ltp);
                        if (instruments[instrument].status != Status.POSITION)
                        {
                            instruments[instrument].type = OType.Buy;
                            instruments[instrument].canTrust = true;
                            instruments[instrument].isReversed = true;
                        }
                        if (instruments[instrument].top30bb - instruments[instrument].bot30bb < Math.Round(instruments[instrument].top30bb * (decimal).03, 1))
                        {
                            Console.WriteLine("Time Stamp {0} Script {1} is not safe to mark SELL Order again",
                                DateTime.Now.ToString("yyyyMMdd hh:mm:ss"), instruments[instrument].futName);
                        }
                        if (instruments[instrument].goodToGo
                            && instruments[instrument].top30bb - instruments[instrument].bot30bb > 
                            Math.Round(instruments[instrument].top30bb * (decimal).023, 1))
                            instruments[instrument].toBuy = true;
                        instruments[instrument].toSell = false;
                    }
                    else if (history[index].Close > instruments[instrument].middle30ma50
                            && (history[index].Low <= instruments[instrument].middle30ma50
                                || IsBetweenVariance(history[index].Low, instruments[instrument].middle30ma50, (decimal).0006))
                            && instruments[instrument].middle30ma50 > instruments[instrument].bot30bb)
                    {
                        //&& (IsBetweenVariance(history[index].Low, instruments[instrument].sup1, (decimal).0006)
                        //|| history[index].Low < instruments[instrument].sup1)
                        Console.WriteLine("Time Stamp {0} Trusted Script {1} has just stopped above MA50 {2} as current low is {3}. hence watchlist is closing this script", DateTime.Now.ToString("yyyyMMdd hh:mm:ss"), instruments[instrument].futName, instruments[instrument].middle30ma50, history[index].Low);
                        if (instruments[instrument].status != Status.POSITION)
                        {
                            instruments[instrument].canTrust = true;
                            instruments[instrument].isReversed = false;
                        }
                    }
                    else
                        Console.WriteLine("Time Stamp {0} Script {2} is Reversed for SELL and waiting for perfect trigger at MIDDLE 30BB {1} and the LTP is {3}", DateTime.Now.ToString("yyyyMMdd hh:mm:ss"), instruments[instrument].middle30BB, instruments[instrument].futName, ltp);
                }
                #endregion
            }
        }

        bool checkForReverseOrder(uint instrument, Tick tickData, OType type)
        {
            decimal timenow = (decimal)DateTime.Now.Hour + ((decimal)DateTime.Now.Minute / 100);
            decimal ltp = tickData.LastPrice;
            decimal range;
            OType cTrend = CalculateSqueezedTrend(instruments[instrument].futName, instruments[instrument].history, 10);
            if (instruments[instrument].top30bb > instruments[instrument].middle30ma50
                    && instruments[instrument].middle30BBnew < instruments[instrument].middle30ma50
                    && cTrend == OType.StrongBuy
                    && type == OType.Sell)
            {
                range = Convert.ToDecimal(((ltp * (decimal)3.5) / 100).ToString("#.#"));
                if ((instruments[instrument].bot30bb + range) < instruments[instrument].top30bb)
                {
                    if (IsBetweenVariance(instruments[instrument].middleBB, ltp, (decimal).0006)
                        && instruments[instrument].ma50 < instruments[instrument].middleBB)
                    {
                        if (instruments[instrument].oldTime != instruments[instrument].currentTime)
                        {
                            instruments[instrument].oldTime = instruments[instrument].currentTime;
                            Console.WriteLine("Time Stamp {0} Raising UP 3.5: Recommending to Place REVERSE BUY ORDER for Script {1} at 50 MA {1} and the LTP is {3}", DateTime.Now.ToString("yyyyMMdd hh:mm:ss"), instruments[instrument].futName, instruments[instrument].middle30ma50, ltp);
                        }
                        //instruments[instrument].type = OType.Sell;
                        //return true;
                    }
                }
                else
                {
                    if ((instruments[instrument].bot30bb + range) > instruments[instrument].top30bb)
                    {
                        range = Convert.ToDecimal(((ltp * (decimal)2.8) / 100).ToString("#.#"));
                        if ((instruments[instrument].bot30bb + range) < instruments[instrument].top30bb
                            && ((IsBetweenVariance(instruments[instrument].botBB, ltp, (decimal).0006)
                                    && instruments[instrument].botBB < instruments[instrument].ma50)
                                || (IsBetweenVariance(instruments[instrument].ma50, ltp, (decimal).0006)
                                    && instruments[instrument].ma50 < instruments[instrument].botBB)))
                        {
                            if (instruments[instrument].oldTime != instruments[instrument].currentTime)
                            {
                                instruments[instrument].oldTime = instruments[instrument].currentTime;
                                Console.WriteLine("Time Stamp {0} Raising UP 2.8: Recommending to Place REVERSE BUY ORDER for Script {1} at 50 MA {1} and the LTP is {3}", DateTime.Now.ToString("yyyyMMdd hh:mm:ss"), instruments[instrument].futName, instruments[instrument].middle30ma50, ltp);
                            }
                            //instruments[instrument].type = OType.Buy;
                            //return true;
                        }
                    }
                }
            }
            else if (instruments[instrument].bot30bb < instruments[instrument].middle30ma50
                    && instruments[instrument].middle30BBnew > instruments[instrument].middle30ma50
                    && cTrend == OType.StrongSell
                    && type == OType.Buy)
            {
                range = Convert.ToDecimal(((ltp * (decimal)3.5) / 100).ToString("#.#"));
                if ((instruments[instrument].bot30bb + range) < instruments[instrument].top30bb)
                {
                    if (IsBetweenVariance(instruments[instrument].middleBB, ltp, (decimal).0006)
                        && instruments[instrument].ma50 > instruments[instrument].middleBB)
                    {
                        if (instruments[instrument].oldTime != instruments[instrument].currentTime)
                        {
                            instruments[instrument].oldTime = instruments[instrument].currentTime;
                            Console.WriteLine("Time Stamp {0} Falling DOWN 3.5: Recommending to Place REVERSE SELL ORDER for Script {1} at 50 MA {1} and the LTP is {3}", DateTime.Now.ToString("yyyyMMdd hh:mm:ss"), instruments[instrument].futName, instruments[instrument].middle30ma50, ltp);
                        }
                        //instruments[instrument].type = OType.Sell;
                        //return true;
                    }
                }
                else
                {
                    if ((instruments[instrument].bot30bb + range) > instruments[instrument].top30bb)
                    {
                        range = Convert.ToDecimal(((ltp * (decimal)2.8) / 100).ToString("#.#"));
                        if ((instruments[instrument].bot30bb + range) < instruments[instrument].top30bb
                            && ((IsBetweenVariance(instruments[instrument].topBB, ltp, (decimal).002)
                                    && instruments[instrument].topBB > instruments[instrument].ma50)
                                || (IsBetweenVariance(instruments[instrument].ma50, ltp, (decimal).002)
                                    && instruments[instrument].ma50 > instruments[instrument].topBB)))
                        {
                            if (instruments[instrument].oldTime != instruments[instrument].currentTime)
                            {
                                instruments[instrument].oldTime = instruments[instrument].currentTime;
                                Console.WriteLine("Time Stamp {0} Falling DOWN 2.8: Recommending to Place REVERSE SELL ORDER for Script {1} at 50 MA {1} and the LTP is {3}", DateTime.Now.ToString("yyyyMMdd hh:mm:ss"), instruments[instrument].futName, instruments[instrument].middle30ma50, ltp);
                            }
                            //instruments[instrument].type = OType.Sell;
                            //return true;
                        }
                    }
                }
            }
            return false;
        }

        bool IsBetweenVariance(decimal value1, decimal value2, decimal variance)
        {
            try
            {
                decimal r1 = value1 + Math.Round(value1 * (decimal)variance, 2);
                decimal r2 = value1 - Math.Round(value1 * (decimal)variance, 2);
                if (value2 >= r2 && value2 <= r1)
                {
                    if (value1 < 0 || value2 < 0)
                    {
                        Console.WriteLine("Stock {0} is in range of OPEN +/- by {1} ie., between {2} & {3} though either of them is less than zero", value2, variance, r1, r2);
                        //return false;
                    }
                    return true;

                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("EXCEPTION in BETWEEN VARIANCE for Stock price {0} is in range of OPEN +/- by {1} ie., between {2} or not with exception {3}", value1, value2, variance, ex.Message);
            }
            return false;
        }

        bool IsBeyondVariance(decimal value1, decimal value2, decimal variance)
        {
            try
            {
                decimal r1 = value1 + Math.Round(value1 * (decimal)variance, 2);
                decimal r2 = value1 - Math.Round(value1 * (decimal)variance, 2);
                if (value2 <= r2 || value2 >= r1)
                {
                    if (value1 < 0 || value2 < 0)
                    {
                        Console.WriteLine("Stock {0} is in beyond range of OPEN +/- by {1} ie., between {2} & {3} though either of them is less than zero", value2, variance, r1, r2);
                        //return false;
                    }
                    return true;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("EXCEPTION in BEYOND VARIANCE for Stock price {0} is in range of OPEN +/- by {1} ie., between {2} or not with exception {3}", value1, value2, variance, ex.Message);
            }
            return false;
        }

        public void placeOrder(uint instrument, decimal spotltp, decimal pclose)
        {
            try
            {
                decimal difference = spotltp - pclose;
                Dictionary<string, dynamic> response;
                //double r1 = Convert.ToDouble((ltp - ltp * .0005).ToString("#.#"));
                Quote ltp = new Quote();

                decimal target, stopLoss, trigger, percent;
                decimal timenow = (decimal)DateTime.Now.Hour + ((decimal)DateTime.Now.Minute / 100);
                //OType trend = bt.CalculateSqueezedTrend(instrument, history, 6);
                stopLoss = (decimal)3000 / (decimal)instruments[instrument].lotSize;
                string sl = stopLoss.ToString("#.#");
                stopLoss = Convert.ToDecimal(sl);
                
                try
                {
                    Dictionary<string, Quote> dicLtp = kite.GetQuote(new string[] { instruments[instrument].futId.ToString() });
                    dicLtp.TryGetValue(instruments[instrument].futId.ToString(), out ltp);
                    //kite.GetOrderMargins()
                }
                catch (Exception ex)
                {
                    Console.WriteLine("EXCEPTION CAUGHT while Placing Order Trigger :: " + ex.Message);
                    return;
                }

                decimal total = GetMISMargin(instruments[instrument].futName, instruments[instrument].type, ltp.LastPrice, instruments[instrument].lotSize);
                if (total > 138000)
                {
                    decimal spikeVM = spotltp != 0 && spotltp < instruments[instrument].middleBB ? Math.Round((spotltp * (decimal).02), 1) : Math.Round((instruments[instrument].middleBB * (decimal).02), 1);
                    Console.WriteLine("EXCEPTION Current Script Chosen is having very HIGH Marging. So better stop placing the order");
                    if (instruments[instrument].type == OType.Sell
                        && spotltp - instruments[instrument].middleBB >= spikeVM)
                    {
                        //Proceed with order
                    }
                    else if (instruments[instrument].type == OType.Buy
                        && instruments[instrument].middleBB - spotltp >= spikeVM)
                    {
                        //Proceed with order
                    }
                    else
                    {
                        CloseOrderTicker(instrument, true);
                        return;
                    }
                }
                decimal variance2 = (ltp.LastPrice * (decimal)2) / 100;
                decimal variance25 = (ltp.LastPrice * (decimal)2.5) / 100;
                decimal variance3 = (ltp.LastPrice * (decimal)3) / 100;
                decimal variance34 = (ltp.LastPrice * (decimal)3.4) / 100;
                percent = Math.Round((ltp.LastPrice * (decimal).4) / 100, 1);
                target = Math.Round((instruments[instrument].target / (decimal)instruments[instrument].lotSize), 1);

                if (percent < target)
                {
                    instruments[instrument].target = instruments[instrument].canTrust? 1400 : 1600;
                    Console.WriteLine("Chose very less target {0}", instruments[instrument].target);
                }
                if (Decimal.Compare(timenow, (decimal)10.30) < 0)
                {
                    instruments[instrument].target = 1600;
                    Console.WriteLine("Early Trade Target:: Time is less than 10.30AM hence choosing smaller target as {0}", instruments[instrument].target);
                }
                else if (instruments[instrument].isLowVolume
                    && instruments[instrument].canTrust
                    && !instruments[instrument].isReorder)
                {
                    instruments[instrument].target = 1800;
                }
                else if (instruments[instrument].isReversed
                    && ((instruments[instrument].bot30bb + variance2) > instruments[instrument].top30bb
                        || IsBetweenVariance((instruments[instrument].bot30bb + variance2), instruments[instrument].top30bb, (decimal).0006)))
                {
                    #region Target for Narrowed Script
                    //if (instruments[instrument].isReversed)
                    //    if (Decimal.Compare(timenow, (decimal)11.45) < 0)
                    //        return;
                    //DateTime previousDay;
                    //DateTime currentDay;
                    //getDays(out previousDay, out currentDay);
                    Console.WriteLine("A reverse order as well. So trying for major 2000+ target");
                    instruments[instrument].target = 2000;
                    #endregion
                }
                else if (((instruments[instrument].bot30bb + variance3) < instruments[instrument].top30bb
                            || IsBetweenVariance((instruments[instrument].bot30bb + variance3), instruments[instrument].top30bb, (decimal).0006))
                        && !instruments[instrument].isReorder
                        && instruments[instrument].isReversed)
                {
                    percent = Math.Round((ltp.LastPrice * (decimal).5) / 100, 1);
                    target = Math.Round((2400 / (decimal)instruments[instrument].lotSize), 1);

                    if (percent > target)
                    {
                        instruments[instrument].target = 2400;
                        Console.WriteLine("Maximum TARGET:: half percentage is more than minimal target as {0}", instruments[instrument].target);
                    }
                }
                else if (instruments[instrument].isReorder
                    && instruments[instrument].canTrust)
                {
                    if (Decimal.Compare(timenow, (decimal)10.18) < 0)
                        instruments[instrument].target = 2300;
                    else if (instruments[instrument].isLowVolume)
                    {
                        instruments[instrument].target = 3600;
                        Console.WriteLine("Serious TRY:: as chosen target is {0}", instruments[instrument].target);
                    }
                    else if ((instruments[instrument].bot30bb + variance34) < instruments[instrument].top30bb)
                    {
                        percent = Math.Round((ltp.LastPrice * (decimal)1.2) / 100, 1);
                        target = Math.Round((4300 / (decimal)instruments[instrument].lotSize), 1);
                        if (Decimal.Compare(timenow, (decimal)14.10) > 0)
                            instruments[instrument].target = 1800;
                        else if (Decimal.Compare(timenow, (decimal)13.10) > 0)
                            instruments[instrument].target = 2200;
                        else if (percent > target)
                            instruments[instrument].target = 4300;
                        Console.WriteLine("Jackpot TARGET:: as chosen target is {0}", instruments[instrument].target);
                    }
                    else if ((instruments[instrument].bot30bb + variance3) < instruments[instrument].top30bb
                        || IsBetweenVariance((instruments[instrument].bot30bb + variance3), instruments[instrument].top30bb, (decimal).0006))
                    {
                        percent = Math.Round((ltp.LastPrice * (decimal)1.2) / 100, 1);
                        target = Math.Round((2400 / (decimal)instruments[instrument].lotSize), 1);
                        if (Decimal.Compare(timenow, (decimal)14.10) > 0)
                            instruments[instrument].target = 1800;
                        else if (Decimal.Compare(timenow, (decimal)13.10) > 0)
                            instruments[instrument].target = 2200;
                        else if (percent > target)
                            instruments[instrument].target = 2400;
                        Console.WriteLine("Lucky TARGET:: as chosed target is {0}", instruments[instrument].target);
                    }
                    else if ((instruments[instrument].bot30bb + variance25) < instruments[instrument].top30bb
                        || IsBetweenVariance((instruments[instrument].bot30bb + variance25), instruments[instrument].top30bb, (decimal).0006))
                    {
                        percent = Math.Round((ltp.LastPrice * (decimal)1.2) / 100, 1);
                        target = Math.Round((2200 / (decimal)instruments[instrument].lotSize), 1);
                        if (Decimal.Compare(timenow, (decimal)14.10) > 0 && instruments[instrument].target > 1800)
                            instruments[instrument].target = 1800;
                        else if (Decimal.Compare(timenow, (decimal)13.10) > 0 && instruments[instrument].target > 2300)
                            instruments[instrument].target = 2000;
                        else if (percent > target)
                            instruments[instrument].target = 2200;
                        Console.WriteLine("Decent TARGET:: as chosed target is {0}", instruments[instrument].target);
                    }
                }
                else if (instruments[instrument].isReorder)
                {
                    percent = Math.Round((ltp.LastPrice * (decimal)1.2) / 100, 1);
                    target = Math.Round((2300 / (decimal)instruments[instrument].lotSize), 1);
                    instruments[instrument].status = Status.OPEN;
                    if (instruments[instrument].isLowVolume)
                    {
                        instruments[instrument].target = 3800;
                    }
                    else if (percent > target)
                        instruments[instrument].target = 1600;
                    else
                        instruments[instrument].target = 2800;
                    futOrderCount--;
                }
                target = Math.Round(instruments[instrument].target / (decimal)instruments[instrument].lotSize, 1);
                /*
                Console.WriteLine(Utils.JsonSerialize(ltp));
                Dictionary<string, Quote> quotes = kite.GetQuote(InstrumentId: new string[] { "NSE:INFY", "NSE:ASHOKLEY", "NSE:HINDALCO19JANFUT" });
                Dictionary<string, Ltp> quotes = kite.GetLtp(new string[] { mToken.futId.ToString() });
                Dictionary<string, OHLC> ohlc = kite.GetOHLC(new string[] { mToken.futId.ToString() });
                Console.WriteLine(Utils.JsonSerialize(quotes));
                Console.WriteLine(Utils.JsonSerialize(ohlc));*/

                try
                {
                    futOrderCount++;
                    if (instruments[instrument].type == OType.Sell)
                    {
                        if (!instruments[instrument].isReorder
                            && (!instruments[instrument].canTrust
                                || ltp.BuyQuantity > Math.Round(ltp.SellQuantity * (decimal)1.1)))
                        {
                            if (ltp.LastPrice - Math.Round(ltp.LastPrice * (decimal).001, 2) > ltp.Bids[0].Price)
                            {
                                futOrderCount--;
                                instruments[instrument].isDoingAgain++;
                                Console.WriteLine("Doing it again, as the Bid price is 2% lower than LTP : do it {0}", instruments[instrument].isDoingAgain);
                                if (instruments[instrument].isDoingAgain > 40
                                    && instruments[instrument].canTrust)
                                {
                                    // Proceed to order
                                }
                                else
                                    return;
                            }
                            if (instruments[instrument].isDoingAgain > 2 
                                && !instruments[instrument].canTrust)
                            {
                                if (DateTime.Now.Minute % 5 == 0
                                    && (spotltp - instruments[instrument].middleBB) > Math.Round(spotltp * (decimal).016))
                                {
                                    // Proceed to order
                                }
                                else
                                {
                                    futOrderCount--;
                                    instruments[instrument].isDoItAgain++;
                                    Console.WriteLine("Do it again in Next Candle, If it is still Applicable 1 : do it {0}", instruments[instrument].isDoItAgain);
                                    return;
                                }
                            }
                            else if (instruments[instrument].isDoingAgain == 2
                                && difference < (ltp.LastPrice - ltp.Close))
                            {
                                if (DateTime.Now.Minute % 5 == 0
                                    && (spotltp - instruments[instrument].middleBB) > Math.Round(spotltp * (decimal).016))
                                {
                                    // Proceed to order
                                }
                                else
                                {
                                    futOrderCount--;
                                    Console.WriteLine("Do it again in Next Candle, If it is still Applicable 2");
                                    return;
                                }
                            }
                        }
                        if (futOrderCount > 5 && !instruments[instrument].canTrust)
                        {
                            Console.WriteLine("You have met 5 orders already for the day; Total trades today are : {0}", kite.GetOrders().Count / 2);
                            CloseOrderTicker(instrument, true);
                            return;
                        }
                        //if (Decimal.Compare(timenow, (decimal)10.45) < 0)
                        //    lotSize = lotSize + 1;                        
                        //response = PlaceBOOrder(instrument, trigger, stopLoss, target, OType.Sell);
                        instruments[instrument].target = target;
                        if (instruments[instrument].isReorder)
                            trigger = ltp.LastPrice;
                        else
                            trigger = GetTrigger(ltp, difference, instruments[instrument].hasGeared, OType.Sell, instrument);
                        Console.WriteLine("Spot varied by {0}; Future trading at {1} with previous close {2}; Hence chosen Trigger is {3} as FUT variance is {4}",
                            difference.ToString(), ltp.LastPrice.ToString(), ltp.Close.ToString(), trigger.ToString(), (ltp.LastPrice - ltp.Close).ToString());
                        
                        instruments[instrument].status = Status.STANDING;
                        instruments[instrument].tries = 0;
                        response = PlaceMISOrder(instrument, trigger, OType.Sell);

                        Console.WriteLine("At Time {0} SELL Order STATUS:::: {1}", DateTime.Now.ToString("yyyyMMdd hh:mm:ss"), Utils.JsonSerialize(response));
                        Console.WriteLine("SELL Order Details: Instrument ID : {0}; Quantity : {1}; Price : {2}; SL : {3}; Target {4} ", instruments[instrument].futName, instruments[instrument].lotSize, ltp.LastPrice, stopLoss, target);
                    }
                    if (instruments[instrument].type == OType.Buy)
                    {
                        if (!instruments[instrument].isReorder
                            && (!instruments[instrument].canTrust
                                || Math.Round(ltp.BuyQuantity * (decimal)1.1) < ltp.SellQuantity))
                        {
                            if (ltp.LastPrice + Math.Round(ltp.LastPrice * (decimal).001, 2) < ltp.Offers[0].Price)
                            {
                                futOrderCount--;
                                instruments[instrument].isDoingAgain++;
                                Console.WriteLine("Doing it again, as the Offer price is 2% higher than LTP : do it {0}", instruments[instrument].isDoingAgain);
                                if (instruments[instrument].isDoingAgain > 40
                                    && instruments[instrument].canTrust)
                                {
                                    // Proceed to order
                                }
                                else
                                    return;
                            }
                            if (instruments[instrument].isDoingAgain > 2
                                && !instruments[instrument].canTrust)
                            {
                                if (DateTime.Now.Minute % 5 == 0
                                    && (instruments[instrument].middleBB - spotltp) > Math.Round(spotltp * (decimal).016))
                                {
                                    // Proceed to order
                                }
                                else
                                {
                                    futOrderCount--;
                                    instruments[instrument].isDoItAgain++;
                                    Console.WriteLine("Do it again in Next Candle, If it is still Applicable 1 : do it {0}", instruments[instrument].isDoItAgain);
                                    return;
                                }
                            }
                            else if (instruments[instrument].isDoingAgain == 2
                                && difference < (ltp.LastPrice - ltp.Close))
                            {
                                if (DateTime.Now.Minute % 5 == 0
                                    && (instruments[instrument].middleBB - spotltp) > Math.Round(spotltp * (decimal).016))
                                {
                                    // Proceed to order
                                }
                                else
                                {
                                    futOrderCount--;
                                    Console.WriteLine("Do it again in Next Candle, If it is still Applicable 2");
                                    return;
                                }
                            }
                        }
                        if (futOrderCount > 5 && !instruments[instrument].canTrust)
                        {
                            Console.WriteLine("You have met 5 orders already for the day; Total trades today are : {0}", kite.GetOrders().Count / 2);
                            CloseOrderTicker(instrument, true);
                            return;
                        }
                        //if (Decimal.Compare(timenow, (decimal)10.45) < 0)
                        //    lotSize = lotSize + 1;
                        //response = PlaceBOOrder(instrument, trigger, stopLoss, target, OType.Buy);
                        instruments[instrument].target = target;
                        if (instruments[instrument].isReorder)
                            trigger = ltp.LastPrice;
                        else
                            trigger = GetTrigger(ltp, difference, instruments[instrument].hasGeared, OType.Buy, instrument);
                        Console.WriteLine("Spot varied by {0}; Future trading at {1} with previous close {2}; Hence chosen Trigger is {3} as FUT variance is {4}",
                            difference.ToString(), ltp.LastPrice.ToString(), ltp.Close.ToString(), trigger.ToString(), (ltp.LastPrice - ltp.Close).ToString());

                        instruments[instrument].status = Status.STANDING;
                        instruments[instrument].tries = 0;
                        response = PlaceMISOrder(instrument, trigger, OType.Buy);
                        
                        Console.WriteLine("At Time {0} BUY Order STATUS:::: {1}", DateTime.Now.ToString("yyyyMMdd hh:mm:ss"), Utils.JsonSerialize(response));
                        Console.WriteLine("BUY Order Details: Instrument ID : {0}; Quantity : {1}; Price : {2}; SL : {3}; Target {4} ", instruments[instrument].futName, instruments[instrument].lotSize, ltp.LastPrice, stopLoss, target);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine("EXCEPTION CAUGHT WHILE PLACING ORDER :: " + ex.Message);
                    if (ex.Message.Contains("Due to expected higher volatility in the markets")
                        || ex.Message.Contains("Due to expected increase in volatility in the markets"))
                    {
                        isNiftyVolatile = true;
                        CloseOrderTicker(instrument, true);
                    }
                    else
                    {
                        Console.WriteLine("UNEXPECTED ERROR MESSAGE :: " + ex.Message);
                        CloseOrderTicker(instrument, true);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("EXCEPTION CAUGHT WHILE Assessing best Target Price :: " + ex.Message);
            }
        }

        public decimal GetMISMargin(string tradingSymbol, OType type, decimal ltp, int lotSize)
        {
            decimal totalMargin = 0;
            try
            {
                List<OrderMarginParams> orderMarginParams = new List<OrderMarginParams>();
                OrderMarginParams omp = new OrderMarginParams();
                omp.Exchange = Constants.EXCHANGE_NFO;
                omp.OrderType = Constants.ORDER_TYPE_LIMIT;
                omp.Price = ltp;
                omp.Quantity = lotSize;
                omp.TradingSymbol = tradingSymbol;
                if (type == OType.Buy)
                    omp.TransactionType = Constants.TRANSACTION_TYPE_BUY;
                else
                    omp.TransactionType = Constants.TRANSACTION_TYPE_SELL;
                omp.Variety = Constants.VARIETY_REGULAR;
                omp.TriggerPrice = 0;
                omp.Product = Constants.PRODUCT_MIS;

                orderMarginParams.Add(omp);
                List<OrderMargin> om = kite.GetOrderMargins(orderMarginParams);
                Console.WriteLine("Order Margin for the given Script is {0}", om[0].Total);
                totalMargin = om[0].Total;
            }
            catch (Exception ex)
            {
                Console.WriteLine("EXCEPTION : While trying to fetch the required Margin with message {0}", ex.Message);
            }
            return totalMargin;
        }

        public Dictionary<string, dynamic> PlaceBOOrder(uint instrument, decimal trigger, decimal stopLoss, decimal target, OType type)
        {
            if (type == OType.Sell)
            {
                return kite.PlaceOrder(
                                Exchange: Constants.EXCHANGE_NFO,
                                TradingSymbol: instruments[instrument].futName,
                                TransactionType: Constants.TRANSACTION_TYPE_SELL,
                                Quantity: isVeryVolatile ? instruments[instrument].lotSize + 1 : instruments[instrument].lotSize,
                                Price: trigger,
                                Product: Constants.PRODUCT_MIS,
                                OrderType: Constants.ORDER_TYPE_LIMIT,
                                StoplossValue: stopLoss,
                                SquareOffValue: target,
                                Validity: Constants.VALIDITY_DAY,
                                Variety: Constants.VARIETY_BO
                                );
            }
            else
            {
                return kite.PlaceOrder(
                            Exchange: Constants.EXCHANGE_NFO,
                            TradingSymbol: instruments[instrument].futName,
                            TransactionType: Constants.TRANSACTION_TYPE_BUY,
                            Quantity: isVeryVolatile ? instruments[instrument].lotSize + 1 : instruments[instrument].lotSize,
                            Price: trigger,
                            OrderType: Constants.ORDER_TYPE_LIMIT,
                            Product: Constants.PRODUCT_MIS,
                            StoplossValue: stopLoss,
                            SquareOffValue: target,
                            Validity: Constants.VALIDITY_DAY,
                            Variety: Constants.VARIETY_BO
                            );
            }
        }

        public Dictionary<string, dynamic> PlaceMISOrder(uint instrument, decimal trigger, OType type)
        {
            //instruments[instrument].target = (decimal)1.5;
            //instruments[instrument].lotSize = 1;
            if (type == OType.Sell)
            {
                return kite.PlaceOrder(
                            Exchange: Constants.EXCHANGE_NFO, //Constants.EXCHANGE_NSE, //
                            TradingSymbol: instruments[instrument].futName, //.Replace(ConfigurationManager.AppSettings["expiry"], ""),
                            TransactionType: Constants.TRANSACTION_TYPE_SELL,
                            //Quantity: isVeryVolatile? instruments[instrument].lotSize * 3 : instruments[instrument].lotSize,
                            Quantity: instruments[instrument].lotSize * 3,
                            Price: trigger,
                            Product: Constants.PRODUCT_MIS,
                            OrderType: Constants.ORDER_TYPE_LIMIT
                            );
            }
            else
            {
                return kite.PlaceOrder(
                            Exchange: Constants.EXCHANGE_NFO, //Constants.EXCHANGE_NSE, //
                            TradingSymbol: instruments[instrument].futName, //.Replace(ConfigurationManager.AppSettings["expiry"], ""),
                            TransactionType: Constants.TRANSACTION_TYPE_BUY,
                            //Quantity: isVeryVolatile ? instruments[instrument].lotSize * 3 : instruments[instrument].lotSize,
                            Quantity: instruments[instrument].lotSize * 3,
                            Price: trigger,
                            OrderType: Constants.ORDER_TYPE_LIMIT,
                            Product: Constants.PRODUCT_MIS,
                            Validity: Constants.VALIDITY_DAY
                            );
            }
        }

        private void OnOrderUpdate(Order OrderData)
        {
            //COMPLETE, REJECTED, CANCELLED, and OPEN
            //mToken.order = OrderData;
            try
            {
                Dictionary<UInt32, WatchList>.KeyCollection keys = instruments.Keys;
                foreach (uint token in keys)
                {
                    if (instruments[token].futId == OrderData.InstrumentToken)
                    {
                        switch (instruments[token].status)
                        {
                            case Status.OPEN:
                                Console.WriteLine("Order Update Trigger is invoked for {0} and its current status is in 'OPEN'", instruments[token].futName);
                                break;
                            case Status.CLOSE:
                                Console.WriteLine("Order Update Trigger is invoked for {0} and its current status is in 'CLOSE'", instruments[token].futName);
                                break;
                            case Status.STANDING:
                                Console.WriteLine("Order Update Trigger is invoked for {0} and its current status is in 'STANDING'", instruments[token].futName);
                                break;
                            case Status.POSITION:
                                Console.WriteLine("Order Update Trigger is invoked for {0} and its current status is in 'POSITION'", instruments[token].futName);
                                break;
                        }
                        if (OrderData.Status == "TRIGGER PENDING")
                        {
                            Console.WriteLine("Order Update for {0} did type {1}, variety {2} at price {3} and its status is '{4}'", OrderData.Tradingsymbol, OrderData.OrderType, OrderData.Variety, OrderData.TriggerPrice, OrderData.Status);
                            break;
                        }
                        else
                            Console.WriteLine("Order Update for {0} did type {1}, variety {2} at price {3} and its status is '{4}' with Parent Order {5}", OrderData.Tradingsymbol, OrderData.OrderType, OrderData.Variety, OrderData.Price, OrderData.Status, OrderData.ParentOrderId);
                        if (OrderData.Status == "OPEN" && (OrderData.ParentOrderId == null || OrderData.ParentOrderId.ToString().Length == 0) && instruments[token].status != Status.POSITION)
                        {
                            if (OrderData.Status == "OPEN" && instruments[token].status == Status.CLOSE)
                            {
                                return;
                            }
                            instruments[token].status = Status.STANDING;
                            modifyOrderInCSV(token, instruments[token].futName, instruments[token].type, Status.STANDING);
                        }
                        else if (OrderData.Status == "COMPLETE" && instruments[token].status == Status.CLOSE)
                        {
                            Console.WriteLine("Changing back to POSITION as This is for Reorder");
                            instruments[token].status = Status.POSITION;
                            modifyOrderInCSV(token, instruments[token].futName, instruments[token].type, Status.POSITION);
                        }
                        else if (OrderData.Status == "REJECTED" || OrderData.Status == "CANCELLED")
                            // Uncomment the below condition when commenting below rejected reorder
                            // && (!(instruments[token].status == Status.STANDING || instruments[token].status == Status.POSITION || instruments[token].status == Status.CLOSE)))
                        {
                            if (instruments[token].status == Status.POSITION
                                && instruments[token].isReorder)
                            {
                                Console.WriteLine("This is an position for AVERAGE ORDER. Kindly note");
                                if (OrderData.TransactionType == Constants.TRANSACTION_TYPE_SELL)
                                    InsertNewToken(token, instruments[token].futName, 0, 0, instruments[token].middleBB, "SELL", "AVERAGE", instruments[token].type);
                                else
                                    InsertNewToken(token, instruments[token].futName, 0, 0, instruments[token].middleBB, "BUY", "AVERAGE", instruments[token].type);
                            }
                            else
                            {
                                if (OrderData.TransactionType == Constants.TRANSACTION_TYPE_SELL)
                                    InsertNewToken(token, instruments[token].futName, 0, 0, instruments[token].middleBB, "SELL", "REJECTED", instruments[token].type);
                                else
                                    InsertNewToken(token, instruments[token].futName, 0, 0, instruments[token].middleBB, "BUY", "REJECTED", instruments[token].type);
                                CloseOrderTicker(token, true);
                            }
                            //// **** Comment it later. This is only for debugging purpose
                            /*
                            if (OrderData.Variety != Constants.VARIETY_BO && OrderData.Status == "REJECTED")
                            {
                                if (OrderData.TransactionType == Constants.TRANSACTION_TYPE_BUY)
                                {
                                    PlaceMISOrder(token, OrderData.Price + instruments[token].target, OType.Sell);
                                }
                                else if (OrderData.TransactionType == Constants.TRANSACTION_TYPE_SELL)
                                {
                                    PlaceMISOrder(token, OrderData.Price - instruments[token].target, OType.Buy);
                                }
                                return;
                            }
                            ///// ***** Comment until here
                            instruments[token].status = Status.OPEN;
                            instruments[token].isHedgingOrder = false;
                            modifyOrderInCSV(token, instruments[token].futName, instruments[token].type, Status.OPEN);
                            Console.WriteLine("OPEN the ticker again for {0} as its status is '{1}'", OrderData.Tradingsymbol, OrderData.Status);
                            */
                        }
                        else if (OrderData.Status == "CANCELLED" && instruments[token].status == Status.STANDING)
                        {
                            instruments[token].status = Status.OPEN;
                            modifyOrderInCSV(token, instruments[token].futName, instruments[token].type, Status.OPEN);
                            Console.WriteLine("OPEN the ticker again as our tool Cancelled for {0}", OrderData.Tradingsymbol);
                        }
                        else if (OrderData.Status == "COMPLETE")
                        {
                            if (OrderData.ParentOrderId != null && OrderData.ParentOrderId.ToString().Length != 0 && OrderData.Variety == Constants.VARIETY_BO)
                            {
                                CloseOrderTicker(token, true);
                            }
                            else
                            {
                                Position pos = new Position();
                                if (GetCurrentPNL(instruments[token].futId, ref pos))
                                    if (pos.Quantity == 0 && instruments[token].status == Status.CLOSE)
                                        return;
                                if (OrderData.Variety != Constants.VARIETY_BO && instruments[token].status != Status.POSITION)
                                {
                                    if (OrderData.Price == 0 && instruments[token].isReorder)
                                        return;
                                    try
                                    {
                                        if (OrderData.TransactionType == Constants.TRANSACTION_TYPE_BUY)
                                        {
                                            PlaceMISOrder(token, OrderData.AveragePrice + instruments[token].target,
                                                OType.Sell);
                                        }
                                        else if (OrderData.TransactionType == Constants.TRANSACTION_TYPE_SELL)
                                        {
                                            PlaceMISOrder(token, OrderData.AveragePrice - instruments[token].target,
                                                OType.Buy);
                                        }
                                    }
                                    catch (Exception ex)
                                    {
                                        Console.WriteLine("Exception while Placing MIS ORDER with message : {0}", ex.Message);
                                    }
                                    instruments[token].status = Status.POSITION;
                                    instruments[token].orderTime = Convert.ToDateTime(OrderData.OrderTimestamp);
                                    modifyOrderInCSV(token, instruments[token].futName, instruments[token].type, Status.POSITION);
                                }
                                else if (instruments[token].status == Status.POSITION)
                                {
                                    if (instruments[token].isReorder)
                                    {
                                        if (OrderData.TransactionType == Constants.TRANSACTION_TYPE_SELL)
                                        {
                                            Console.WriteLine("AT {0} look for reverse SELL order now for this position {1} Exchange Time {2}", DateTime.Now.ToString(), instruments[token].futName, OrderData.ExchangeTimestamp);
                                            instruments[token].type = OType.Sell;
                                        }
                                        else if (OrderData.TransactionType == Constants.TRANSACTION_TYPE_BUY)
                                        {
                                            Console.WriteLine("AT {0} look for reverse BUY order now for this position {1} Exchange Time {2}", DateTime.Now.ToString(), instruments[token].futName, OrderData.ExchangeTimestamp);
                                            instruments[token].type = OType.Buy;
                                        }
                                        //Comment Next two lines
                                        //instruments[token].status = Status.CLOSE;
                                        //CloseOrderTicker(token, true);
                                    }
                                    else
                                    {
                                        Order order = new Order();
                                        Order posOrder = new Order();
                                        if (GetCurrentOrder(instruments[token].futId, "COMPLETE", ref order)
                                            && GetCurrentOrder(instruments[token].futId, "OPEN", ref posOrder)) //OPEN
                                        {
                                            if ((order.TransactionType == Constants.TRANSACTION_TYPE_BUY
                                                 && instruments[token].type == OType.Buy
                                                 && posOrder.TransactionType == Constants.TRANSACTION_TYPE_SELL)
                                                || (order.TransactionType == Constants.TRANSACTION_TYPE_SELL
                                                    && instruments[token].type == OType.Sell
                                                    && posOrder.TransactionType == Constants.TRANSACTION_TYPE_BUY))
                                            {
                                                Console.WriteLine("AT {0} Order Update of this this position {1} is Pending correctly", DateTime.Now.ToString(), instruments[token].futName);
                                            }
                                            Console.WriteLine("AT {0} Order event of this this position {1} has triggered late", DateTime.Now.ToString(), instruments[token].futName);
                                            return;
                                        }
                                        if (OrderData.TransactionType == Constants.TRANSACTION_TYPE_BUY)
                                            InsertNewToken(token, instruments[token].futName, 0, 0, instruments[token].middleBB, "SELL", "BOOKED", instruments[token].type);
                                        else
                                            InsertNewToken(token, instruments[token].futName, 0, 0, instruments[token].middleBB, "BUY", "BOOKED", instruments[token].type);
                                        instruments[token].status = Status.CLOSE;
                                        CloseOrderTicker(token, true);
                                    }
                                }
                                else
                                {
                                    if (OrderData.TransactionType == Constants.TRANSACTION_TYPE_SELL)
                                        InsertNewToken(token, instruments[token].futName, 0, 0, instruments[token].middleBB, "SELL", "InProgress", instruments[token].type);
                                    else
                                        InsertNewToken(token, instruments[token].futName, 0, 0, instruments[token].middleBB, "BUY", "InProgress", instruments[token].type);
                                    modifyOrderInCSV(token, instruments[token].futName, instruments[token].type, Status.POSITION);
                                    instruments[token].status = Status.POSITION;
                                    instruments[token].orderTime = Convert.ToDateTime(OrderData.OrderTimestamp);
                                }
                            }
                        }
                        break;
                    }
                    else
                    {
                        //Console.WriteLine("NOTE :: Event Triggered for the Thread {0}. ~Comment this line in Future~", mToken.futName);
                    }
                }
            }
            catch (KeyNotFoundException)
            {
                Console.WriteLine("EXDCEPTION :: Given key {0} is not found in the Dictionary ", OrderData.InstrumentToken);
            }
            catch (Exception ex)
            {
                Console.WriteLine("EXDCEPTION in 'OnOrderupdate' with :: {0}", ex.Message);
            }
        }

        private decimal GetTrigger(Quote ltp, decimal difference, bool hasGeared, OType type, uint token)
        {
            if (type == OType.Sell)
            {
                if (instruments[token].canTrust)
                {
                    if (instruments[token].isHighVolume)
                    {
                        Console.WriteLine("High Volume Script. So choosing far Offers {0}", ltp.Offers[3].Price);
                        if (IsBetweenVariance(ltp.Offers[3].Price, ltp.LastPrice, (decimal).0004))
                        {
                            instruments[token].triggerPrice = instruments[token].triggerPrice +
                                                              (Math.Round(ltp.Offers[3].Price * (decimal)1.0015, 1) - ltp.LastPrice);
                            return Math.Round(ltp.Offers[3].Price * (decimal)1.0015, 1);
                        }
                        else
                        {
                            instruments[token].triggerPrice = instruments[token].triggerPrice +
                                                              (ltp.Offers[3].Price - ltp.LastPrice);
                            return ltp.Offers[3].Price;
                        }
                    }
                    else if (IsBeyondVariance(ltp.Bids[0].Price, ltp.Offers[0].Price, (decimal).001))
                    {
                        Console.WriteLine("Difference in Bids & offers 1. So choosing far offer {0}", ltp.Offers[0].Price);
                        instruments[token].triggerPrice = instruments[token].triggerPrice +
                                                          (ltp.Offers[0].Price - ltp.LastPrice);
                        return ltp.Offers[1].Price > 780? ltp.Bids[0].Price : ltp.Offers[0].Price;
                    }
                    else if (IsBetweenVariance(ltp.Bids[0].Price, ltp.Offers[1].Price, (decimal).0015))
                        return ltp.Offers[1].Price > 780 ? ltp.Bids[0].Price : ltp.Offers[0].Price;
                    else
                    {
                        Console.WriteLine("Difference in Bids & offers 2. So choosing far offer {0}", ltp.Offers[1].Price);
                        instruments[token].triggerPrice = instruments[token].triggerPrice +
                                                          (ltp.Offers[1].Price - ltp.LastPrice);
                        return ltp.Offers[1].Price > 780 ? ltp.Bids[1].Price : ltp.Offers[1].Price;
                    }
                }
                else if (DateTime.Now.Day >= 23 && DateTime.Now.DayOfWeek != DayOfWeek.Thursday)
                {
                    if (difference < (ltp.LastPrice - ltp.Close))
                    {
                        Console.WriteLine("5. Spot variance is beyond the natural. So choosing far trigger {0}", ltp.Offers.Count);
                        return ltp.Bids[0].Price;
                    }
                    else
                        return ltp.Bids[1].Price;
                }
                else
                {
                    if ((difference + (difference * (decimal).3)) < (ltp.LastPrice - ltp.Close)) // || hasGeared)
                    {
                        Console.WriteLine("0. Spot variance is beyond the natural or geared atleast once in a day. So choosing far trigger {0}", ltp.Offers.Count);
                        instruments[token].triggerPrice = instruments[token].triggerPrice +
                                                          (ltp.Offers[3].Price - ltp.Offers[0].Price);
                        return ltp.Offers[3].Price;
                    }
                    else if ((difference + (difference * (decimal).2)) <= (ltp.LastPrice - ltp.Close))
                    {
                        Console.WriteLine("1. Spot variance is beyond the natural. So choosing far trigger {0}", ltp.Offers.Count);
                        instruments[token].triggerPrice = instruments[token].triggerPrice +
                                                          (ltp.Offers[2].Price - ltp.Offers[0].Price);
                        return ltp.Offers[2].Price;
                    }
                    else if ((difference + (difference * (decimal).1)) <= (ltp.LastPrice - ltp.Close))
                    {
                        Console.WriteLine("2. Spot variance is beyond the natural. So choosing far trigger {0}", ltp.Offers.Count);
                        instruments[token].triggerPrice = instruments[token].triggerPrice +
                                                          (ltp.Offers[1].Price - ltp.LastPrice);
                        return ltp.Offers[1].Price;
                    }
                    else if (IsBeyondVariance(ltp.Bids[0].Price, ltp.Offers[0].Price, (decimal).001))
                    {
                        Console.WriteLine("3. Spot variance is beyond the natural. So choosing far trigger {0}", ltp.Bids.Count);
                        return ltp.Offers[0].Price;
                    }
                    else if (difference < (ltp.LastPrice - ltp.Close))
                    {
                        Console.WriteLine("4. Spot variance is beyond the natural. So choosing far trigger {0}", ltp.Offers.Count);
                        return ltp.Bids[0].Price;
                    }
                    else
                        return ltp.Bids[2].Price;
                }
            }
            else
            {
                if (instruments[token].canTrust)
                {
                    if (instruments[token].isHighVolume)
                    {
                        Console.WriteLine("High Volume Script. So choosing far bid {0}", ltp.Bids[3].Price);
                        if (IsBetweenVariance(ltp.Bids[3].Price, ltp.LastPrice, (decimal).0004))
                        {
                            instruments[token].triggerPrice = instruments[token].triggerPrice -
                                                          (ltp.LastPrice - Math.Round(ltp.Bids[3].Price * (decimal).9985, 1));
                            return Math.Round(ltp.Bids[3].Price * (decimal).9985, 1);
                        }
                        else
                        {
                            instruments[token].triggerPrice = instruments[token].triggerPrice -
                                                          (ltp.LastPrice - ltp.Bids[3].Price);
                            return ltp.Bids[3].Price;
                        }
                    }
                    else if (IsBeyondVariance(ltp.Bids[0].Price, ltp.Offers[0].Price, (decimal).001))
                    {
                        Console.WriteLine("Difference in Bids & offers 1. So choosing far bid {0}", ltp.Bids[0].Price);
                        instruments[token].triggerPrice = instruments[token].triggerPrice -
                                                          (ltp.LastPrice - ltp.Bids[0].Price);
                        return ltp.Offers[1].Price > 780 ? ltp.Offers[0].Price : ltp.Bids[0].Price;
                    }
                    else if (IsBetweenVariance(ltp.Bids[0].Price, ltp.Offers[1].Price, (decimal).0015))
                        return ltp.Offers[1].Price > 780 ? ltp.Offers[0].Price : ltp.Bids[0].Price;
                    else
                    {
                        Console.WriteLine("Difference in Bids & offers 2. So choosing far bid {0}", ltp.Bids[1].Price);
                        instruments[token].triggerPrice = instruments[token].triggerPrice -
                                                          (ltp.LastPrice - ltp.Bids[1].Price);
                        return ltp.Offers[1].Price > 780 ? ltp.Offers[1].Price : ltp.Bids[1].Price;
                    }
                }
                else if (DateTime.Now.Day >= 23 && DateTime.Now.DayOfWeek != DayOfWeek.Thursday)
                {
                    if (difference > (ltp.LastPrice - ltp.Close))
                    {
                        Console.WriteLine("5. Spot variance is beyond the natural. So choosing far trigger {0}", ltp.Bids.Count);
                        return ltp.Offers[0].Price;
                    }
                    else
                        return ltp.Offers[1].Price;
                }
                else
                {
                    if ((difference + (difference * (decimal).3)) > (ltp.LastPrice - ltp.Close) || hasGeared)
                    {
                        Console.WriteLine("0. Spot variance is beyond the natural or geared atleast once in a day. So choosing far trigger {0}", ltp.Bids.Count);
                        instruments[token].triggerPrice = instruments[token].triggerPrice -
                                                          (ltp.Bids[0].Price - ltp.Bids[3].Price);
                        return ltp.Bids[3].Price;
                    }
                    else if ((difference + (difference * (decimal).2)) >= (ltp.LastPrice - ltp.Close))
                    {
                        Console.WriteLine("1. Spot variance is beyond the natural. So choosing far trigger {0}", ltp.Bids.Count);
                        instruments[token].triggerPrice = instruments[token].triggerPrice -
                                                          (ltp.Bids[0].Price - ltp.Bids[2].Price);
                        return ltp.Bids[2].Price;
                    }
                    else if ((difference + (difference * (decimal).1)) >= (ltp.LastPrice - ltp.Close))
                    {
                        Console.WriteLine("2. Spot variance is beyond the natural. So choosing far trigger {0}", ltp.Bids.Count);
                        instruments[token].triggerPrice = instruments[token].triggerPrice -
                                                          (ltp.LastPrice - ltp.Bids[1].Price);
                        return ltp.Bids[1].Price;
                    }
                    else if (IsBeyondVariance(ltp.Bids[0].Price, ltp.Offers[0].Price, (decimal).001))
                    {
                        Console.WriteLine("3. Spot variance is beyond the natural. So choosing far trigger {0}", ltp.Bids.Count);
                        return ltp.Bids[0].Price;
                    }
                    else if (difference > (ltp.LastPrice - ltp.Close))
                    {
                        Console.WriteLine("4. Spot variance is beyond the natural. So choosing far trigger {0}", ltp.Bids.Count);
                        return ltp.Offers[0].Price;
                    }
                    else
                        return ltp.Offers[2].Price;
                }
            }
        }

        private void modifyOrderInCSV(WatchList wl, OType type, string newTrigger)
        {
            List<String> lines = new List<String>();
            using (StreamReader reader = new StreamReader(ConfigurationManager.AppSettings["inputFile"]))
            {
                String line;
                while ((line = reader.ReadLine()) != null)
                {
                    if (line.Contains(wl.futId.ToString()))
                    {
                        if (wl.type == OType.Sell)
                        {
                            Console.WriteLine("Modify the Open Order Trigger :: {0} with new SHORT value {1} for the day {2} ", wl.futName, newTrigger.ToString(), DateTime.Now.ToString("yyyyMMdd hh: mm:ss"));
                            line = line.Replace(wl.shortTrigger.ToString(), newTrigger.ToString());
                        }
                        else
                        {
                            Console.WriteLine("Modify the Open Order Trigger :: {0} with new LONG value {1} for the day {2} ", wl.futName, newTrigger.ToString(), DateTime.Now.ToString("yyyyMMdd hh: mm:ss"));
                            line = line.Replace(wl.longTrigger.ToString(), newTrigger.ToString());
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

        private void modifyOrderInCSV(uint futId, string futName, OType type, Status status)
        {
            List<String> lines = new List<String>();
            using (StreamReader reader = new StreamReader(ConfigurationManager.AppSettings["inputFile"]))
            {
                String line;
                while ((line = reader.ReadLine()) != null)
                {
                    if (line.Contains(futId.ToString()))
                    {
                        string[] cells = line.Split(',');
                        switch (status)
                        {
                            case Status.OPEN:
                                cells[8] = "OPEN";
                                break;
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
                        Console.WriteLine("Modify the Order for ticker :: {0} to status {1} for the day at {2} ", futName, cells[8], DateTime.Now.ToString());
                        foreach (string cell in cells)
                        {
                            line = line + cell + ",";
                        }
                        line.TrimEnd(',');
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

        public bool WaitUntilHeadStartTime()
        {
            decimal timenow = DateTime.Now.Hour + ((decimal)DateTime.Now.Minute / 100);
            bool flag = false;
            if (CheckNifty(timenow) != OType.BS)
            {
                Console.WriteLine("PreMarket is recorded as Volatile at :: {0} ", DateTime.Now.ToString("yyyyMMdd hh: mm:ss"));
                //isNiftyVolatile = true;
                flag = true;
            }
            while (Decimal.Compare(timenow, Convert.ToDecimal(9.16)) < 0)
            {
                Thread.Sleep(30000);
                timenow = (decimal)DateTime.Now.Hour + ((decimal)DateTime.Now.Minute / 100);
            }

            if (CheckNifty(timenow) != OType.BS)
            {
                Console.WriteLine("Market is recorded as Volatile at :: {0} ", DateTime.Now.ToString("yyyyMMdd hh: mm:ss"));
                isNiftyVolatile = true;
            }
            /*
            while (Decimal.Compare(timenow, Convert.ToDecimal(9.24)) < 0)
            {
                Thread.Sleep(30000);
                timenow = (decimal)DateTime.Now.Hour + ((decimal)DateTime.Now.Minute / 100);
            }
            if (CheckNifty(timenow) != OType.BS)
            {
                Console.WriteLine("Market is recorded as Volatile at :: {0} ", DateTime.Now.ToString("yyyyMMdd hh: mm:ss"));
                isNiftyVolatile = true;
            }
            */
            return isNiftyVolatile || flag;
        }

        public OType CheckNifty(decimal timenow)
        {
            decimal niftyVal = Decimal.Compare(timenow, (decimal)(13.30)) > 0? (decimal).8 : (decimal).7;
            if (Decimal.Compare(timenow, (decimal)(9.10)) < 0)
            {
                List<Historical> dayNSE = kite.GetHistoricalData(ConfigurationManager.AppSettings["NSENIFTY"].ToString(),
                    DateTime.Now.Date.AddDays(-5),
                    DateTime.Now.Date,
                    "day");
                decimal change = Math.Round((dayNSE[dayNSE.Count - 1].Close - dayNSE[dayNSE.Count - 2].Close) * 100 / dayNSE[dayNSE.Count - 2].Close, 2);
                if (change >= niftyVal)
                //|| bull)
                {
                    //Console.WriteLine("Market is Volatile for the day as NIFTY is Bullish. Nifty PrevClose is {0} LastPrice is {1} with more than Validation points {4} Points or CRUDE PrevClose is {2} LastPrice is {3} with more than 70 Points", ohlcNSE.Close, ohlcNSE.LastPrice, ohlcCRUDE.Close, ohlcCRUDE.LastPrice, niftyVal);
                    return OType.Buy;
                }
                else if (change <= niftyVal * -1)
                //|| bear)
                {
                    //Console.WriteLine("Market is Volatile for the day as NIFTY is Bearish. Nifty PrevClose is {0} LastPrice is {1} with more than Validation points {4} Points or CRUDE PrevClose is {2} Lastprice is {3} with more than 70 Points", ohlcNSE.Close, ohlcNSE.LastPrice, ohlcCRUDE.Close, ohlcCRUDE.LastPrice, niftyVal);
                    return OType.Sell;
                }
                else
                    return OType.BS;
            }
            else
            {
                Dictionary<string, OHLC> dicOhlc = kite.GetOHLC(new string[] { ConfigurationManager.AppSettings["NSENIFTY"].ToString() }); //, ConfigurationManager.AppSettings["CRUDE"].ToString() });
                OHLC ohlcNSE = new OHLC();
                dicOhlc.TryGetValue(ConfigurationManager.AppSettings["NSENIFTY"].ToString(), out ohlcNSE);
                decimal change = Math.Round((ohlcNSE.LastPrice - ohlcNSE.Close) * 100 / ohlcNSE.Close, 2);
                if (change >= niftyVal)
                //|| bull)
                {
                    //if (!isNiftyVolatile)
                    //    Console.WriteLine("Market is Volatile for the day as NIFTY is Bullish. Nifty PrevClose is {0} LastPrice is {1} with more than Validation points {2} Points with more than 70 Points", ohlcNSE.Close, ohlcNSE.LastPrice, niftyVal);
                    //isNiftyVolatile = true;
                    return OType.Buy;
                }
                else if (change <= niftyVal * -1)
                //|| bear)
                {
                    //if (!isNiftyVolatile)
                    //    Console.WriteLine("Market is Volatile for the day as NIFTY is Bearish. Nifty PrevClose is {0} LastPrice is {1} with more than Validation points {2} Points with more than 70 Points", ohlcNSE.Close, ohlcNSE.LastPrice, niftyVal);
                    //isNiftyVolatile = true;
                    return OType.Sell;
                }
                else
                    return OType.BS;
            }
        }

        public OType VerifyNifty(uint instrument, decimal ltp, decimal yestClose, decimal timenow)
        {
            //decimal crudeVal = 42;
            decimal niftyVal = (decimal).42;
            if (Decimal.Compare(timenow, (decimal)(9.45)) < 0 && Decimal.Compare(timenow, (decimal)(9.24)) > 0)
            {
                //crudeVal = 42;
                niftyVal = (decimal).34;
            }
            else if (Decimal.Compare(timenow, (decimal)(11.16)) < 0 && Decimal.Compare(timenow, (decimal)(9.45)) > 0)
            {
                //crudeVal = 42;
                niftyVal = (decimal).54;
            }
            else if (Decimal.Compare(timenow, (decimal)(12.16)) < 0 && Decimal.Compare(timenow, (decimal)(11.16)) > 0)
            {
                //crudeVal = 62;
                niftyVal = (decimal).65;
            }
            else if (Decimal.Compare(timenow, (decimal)(14.16)) < 0 && Decimal.Compare(timenow, (decimal)(12.15)) > 0)
            {
                //crudeVal = 72;
                niftyVal = (decimal).8;
            }

            //Dictionary<string, OHLC> dicOhlc = kite.GetOHLC(new string[] { ConfigurationManager.AppSettings["NSENIFTY"].ToString() }); //, ConfigurationManager.AppSettings["CRUDE"].ToString() });
            /*
            bool flag = false;
            if (Decimal.Compare(timenow, (decimal)(9.18)) < 0) flag = true;
            bool bull = (ohlcCRUDE.Low < (ohlcCRUDE.Close - crudeVal)
                        && ohlcCRUDE.LastPrice < ohlcCRUDE.Low + 20)
                        && (ohlcCRUDE.LastPrice < ohlcCRUDE.High - 20
                            || flag) ? true : false;
            bool bear = (ohlcCRUDE.High > (ohlcCRUDE.Close + crudeVal)
                                && ohlcCRUDE.LastPrice < ohlcCRUDE.High - 20)
                        && (ohlcCRUDE.LastPrice > (ohlcCRUDE.Low + 20)
                            || flag) ? true : false;
            */
            decimal change = Math.Round((ltp - yestClose) * 100 / yestClose, 2);
            decimal squeeze = Math.Round(ltp * (decimal).0037);
            if (change >= (decimal)1.6)
            {
                if (!isVeryVolatile)
                    Console.WriteLine(
                        "Market is VERY VERY Volatile for the day as NIFTY is Bullish. Nifty PrevClose is {0} LastPrice is {1} with more than change % {2} Points more than check",
                        yestClose, ltp, change);
                isVeryVolatile = true;
            }
            else if (change <= (decimal)-1.6)
            {
                if (!isVeryVolatile)
                    Console.WriteLine(
                        "Market is VERY VERY Volatile for the day as NIFTY is Bearish. Nifty PrevClose is {0} LastPrice is {1} with more than change {2} Points more than check",
                        yestClose, ltp, change);
                isVeryVolatile = true;
            }
            if (!isNiftyVolatile)
            {
                if (change >= niftyVal)
                    //|| bull)
                {
                    Console.WriteLine(
                        "Market is Volatile for the day as NIFTY is Bullish. Nifty PrevClose is {0} LastPrice is {1} with more than change % {2} Points more than check",
                        yestClose, ltp, change);
                    isNiftyVolatile = true;
                    if (((CheckGearingStatus(instrument, OType.Buy)
                            || (CheckRecentStatus(instrument, OType.Buy)
                                && instruments[instrument].topBB - instruments[instrument].botBB < squeeze)))
                        && change >= (decimal).8)
                        instruments[instrument].type = OType.StrongBuy;
                    else
                        instruments[instrument].type = OType.Buy;
                    return OType.Buy;
                }
                else if (change <= (niftyVal * -1))
                    //|| bear)
                {
                    Console.WriteLine(
                        "Market is Volatile for the day as NIFTY is Bearish. Nifty PrevClose is {0} LastPrice is {1} with more than change {2} Points more than check",
                        yestClose, ltp, change);
                    isNiftyVolatile = true;
                    if (((CheckGearingStatus(instrument, OType.Sell)
                          || (CheckRecentStatus(instrument, OType.Sell)
                              && instruments[instrument].topBB - instruments[instrument].botBB < squeeze)))
                        && change <= (decimal)-.8)
                        instruments[instrument].type = OType.StrongSell;
                    else
                        instruments[instrument].type = OType.Sell;
                    return OType.Sell;
                }
                else
                {
                    return OType.BS;
                }
            }
            else
            {
                if (((CheckGearingStatus(instrument, OType.Buy)
                      || (CheckRecentStatus(instrument, OType.Buy)
                          && instruments[instrument].topBB - instruments[instrument].botBB < squeeze)))
                    && change >= (decimal).7)
                    instruments[instrument].type = OType.StrongBuy;
                else if (((CheckGearingStatus(instrument, OType.Sell)
                      || (CheckRecentStatus(instrument, OType.Sell)
                          && instruments[instrument].topBB - instruments[instrument].botBB < squeeze)))
                    && change <= (decimal)-.7)
                    instruments[instrument].type = OType.StrongSell;
                if ((instruments[instrument].type == OType.Sell
                        || instruments[instrument].type == OType.StrongSell)
                    && change >= (decimal).08
                    && change <= (decimal).25)
                {
                    if (instruments[instrument].oldTime != instruments[instrument].currentTime)
                    {
                        instruments[instrument].oldTime = instruments[instrument].currentTime;
                        Console.WriteLine(
                            "Mid Session Market is Volatile for the day as NIFTY is Bullish. Nifty PrevClose is {0} LastPrice is {1} with more than Validation points {2} Points more than 70 Points",
                            yestClose, ltp, niftyVal);
                    }
                    instruments[instrument].type = OType.Buy;
                }
                else if ((instruments[instrument].type == OType.Buy 
                          || instruments[instrument].type == OType.StrongBuy)
                         && change <= (decimal)-.08
                         && change >= (decimal)-.25)
                {
                    if (instruments[instrument].oldTime != instruments[instrument].currentTime)
                    {
                        instruments[instrument].oldTime = instruments[instrument].currentTime;
                        Console.WriteLine(
                            "Mid Session Market is Volatile for the day as NIFTY is Bearish. Nifty PrevClose is {0} LastPrice is {1} with more than Validation points {2} Points more than 70 Points",
                            yestClose, ltp, niftyVal);
                    }
                    instruments[instrument].type = OType.Sell;
                }
                if (isNiftyVolatile)
                    return instruments[instrument].type;
                else
                    return OType.BS;
            }
        }

        bool VerifyCandleOpenTime()
        {
            bool flag = false;
            decimal timenow = (decimal)DateTime.Now.Hour + ((decimal)DateTime.Now.Minute / 100);
            if (Decimal.Compare(timenow, (decimal)(9.43)) > 0 && Decimal.Compare(timenow, (decimal)(14.16)) < 0)
            {
                switch (DateTime.Now.Minute)
                {
                    default:
                        flag = false;
                        break;
                    case 44:
                    case 14:
                        flag = true;
                        break;
                }
            }
            return flag;
        }

        decimal GetMA50(string instrument, decimal black)
        {
            DateTime previousDay, currentDay;
            getDays(out previousDay, out currentDay);
            previousDay = previousDay.AddDays(-4);
            System.Threading.Thread.Sleep(400);
            List<Historical> minhistory = kite.GetHistoricalData(instrument,
                            previousDay, currentDay, "30minute");
            while (minhistory.Count < 50)
            {
                System.Threading.Thread.Sleep(1000);
                previousDay = previousDay.AddDays(-1);
                minhistory = kite.GetHistoricalData(instrument,
                            previousDay, currentDay, "30minute");
            }

            decimal ma50 = 0;
            if (minhistory.Count >= 50)
            {
                int counter = 0;
                for (int index = minhistory.Count - 1; index > 0; index--)
                {
                    ma50 = ma50 + minhistory[index].Close;
                    counter++;
                    if (counter == 50)
                        break;
                }
                ma50 = Convert.ToDecimal((ma50 / 50).ToString("#.#"));
                //ma50 = ma50 / 50;
            }
            else
                throw new Exception("History Candles are lesser than Expceted candles");
            return ma50;
        }

        public decimal GetWeekMA(string instrument)
        {
            int subDate = getLastMonday();

            DateTime previousDay = DateTime.Now.Date.AddDays(subDate);
            DateTime currentDay = DateTime.Now.Date.AddDays(subDate + 5);
            System.Threading.Thread.Sleep(400);
            List<Historical> history = kite.GetHistoricalData(instrument,
                            previousDay, currentDay, "day");

            previousDay = currentDay.AddDays(-1);
            if (checkHoliday(previousDay))
                previousDay = previousDay.AddDays(-1);
            if (checkHoliday(previousDay))
                previousDay = previousDay.AddDays(-1);
            System.Threading.Thread.Sleep(400);
            List<Historical> history30 = kite.GetHistoricalData(instrument,
                            previousDay, currentDay, "30minute");

            decimal high = history[0].High;
            decimal low = history[0].Low;
            decimal close = history30[history30.Count - 1].Close;
            foreach (Historical candle in history)
            {
                if (high < candle.High)
                    high = candle.High;
                if (low > candle.Low)
                    low = candle.Low;
            }
            decimal f1, black, s1;
            black = Convert.ToDecimal(((high + low + close) / 3).ToString("#.#"));
            f1 = Convert.ToDecimal(((2 * black) - low).ToString("#.#"));
            s1 = Convert.ToDecimal(((2 * black) - high).ToString("#.#"));
            return black;
        }

        int getLastMonday()
        {
            int subDate = 0;
            switch (DateTime.Now.DayOfWeek)
            {
                case DayOfWeek.Saturday:
                    subDate = -5;
                    break;
                case DayOfWeek.Sunday:
                    subDate = -6;
                    break;
                case DayOfWeek.Monday:
                    subDate = -7;
                    break;
                case DayOfWeek.Tuesday:
                    subDate = -8;
                    break;
                case DayOfWeek.Wednesday:
                    subDate = -9;
                    break;
                case DayOfWeek.Thursday:
                    subDate = -10;
                    break;
                case DayOfWeek.Friday:
                    subDate = -11;
                    break;
            }
            return subDate;
        }

        public void getDays(out DateTime previousDay, out DateTime currentDay)
        {
            int subDate;
            switch (DateTime.Now.DayOfWeek)
            {
                case DayOfWeek.Tuesday:
                case DayOfWeek.Monday:
                    subDate = -5;
                    break;
                case DayOfWeek.Sunday:
                    subDate = -4;
                    break;
                default:
                    subDate = -3;
                    break;
            }

            if (checkHoliday(DateTime.Now.Date.AddDays(subDate + 1)))
                subDate = subDate - 1;

            if (checkHoliday(DateTime.Now.Date.AddDays(subDate)))
                subDate = subDate - 1;

            previousDay = DateTime.Now.Date.AddDays(subDate);
            currentDay = DateTime.Now.Date;

            /*DateTime previousDay = DateTime.Now.Date.AddDays(-1);
            decimal timenow = (decimal)DateTime.Now.Hour + ((decimal)DateTime.Now.Minute / 100);
            if (Decimal.Compare(timenow, (decimal)(11.36)) < 0)
            {
                if (checkHoliday(previousDay))
                    previousDay = previousDay.AddDays(-1);
                if (previousDay.DayOfWeek == DayOfWeek.Sunday)
                {
                    previousDay = previousDay.AddDays(-2);
                    if (checkHoliday(previousDay))
                        previousDay = previousDay.AddDays(-3);
                }
            }
            else
                previousDay = DateTime.Now.Date;
            DateTime currentDay = DateTime.Now.Date.AddDays(1);*/

        }

        public List<string> CalculateBB()
        {
            List<string> csvLine = new List<string>();
            try
            {
                DateTime previousDay;
                DateTime currentDay;
                getDays(out previousDay, out currentDay);
                if (DateTime.Now.Hour >= 14)
                    currentDay = currentDay.AddHours(15).AddMinutes(30); // Debug
                //currentDay = currentDay.AddDays(1);
                decimal topBB = 0;
                decimal botBB = 0;
                decimal middle = 0;
                int counter = 0;

                List<Instrument> calcInstruments = kite.GetInstruments(Constants.EXCHANGE_NFO);
                CalculateExpiry(calcInstruments, DateTime.Now.Month, 0, "NFO-FUT");
                csvLine.Add("ScriptId,FutID,Symbol,LotSize,TopBB,BotBB,Middle,TYPE,State,lotCount,Order,Dayma50,Weekma,CandleClose,Close,Identify,IsOpenAlign,IsReversed,CanTrust,Volume");
                int scriptsCount = 0;
                bool blackListFlag = false;
                foreach (Instrument scripts in calcInstruments)
                {
                    blackListFlag = false;
                    string[] bList = ConfigurationManager.AppSettings["blacklist"].ToString().Split(',');
                    foreach (string black in bList)
                    {
                        if (scripts.TradingSymbol.Contains(black))
                        {
                            blackListFlag = true;
                            break;
                        }
                    }
                    if (blackListFlag)
                        continue;
                    //if (!scripts.TradingSymbol.Contains("ADANIPORTS")
                    //    || !(scripts.Segment == "NFO-FUT"))
                    //    continue;

                    if (scripts.Segment == "NFO-FUT"
                        && scripts.InstrumentToken.ToString().Length > 3
                        && ((scripts.LotSize < 5200
                             && scripts.LotSize >= 700)
                            || scripts.TradingSymbol.Contains("NIFTY")))
                    {
                        try
                        {
                            DateTime expiry = (DateTime)scripts.Expiry;
                            if (expiry.Month == month) //Convert.ToInt16(ConfigurationManager.AppSettings["month"]))
                            {
                                string equitySymbol = scripts.TradingSymbol.Replace(futMonth, ""); // ConfigurationManager.AppSettings["expiry"].ToString(), "");
                                //Dictionary<string, Quote> quotes = new Dictionary<string, Quote>();
                                Dictionary<string, OHLC> quotes = new Dictionary<string, OHLC>();
                                List<Historical> dayHistory = new List<Historical>();
                                decimal lastClose = 0;
                                string instrumentToken = string.Empty;
                                try
                                {
                                    if (scripts.TradingSymbol.Contains("NIFTY"))
                                    {
                                        if (scripts.TradingSymbol.Contains("FIN"))
                                            continue;
                                        else if (scripts.TradingSymbol.Contains("BANK"))
                                            instrumentToken = "260105";
                                        else if (scripts.TradingSymbol.Contains("NIFTY" + futMonth)) //ConfigurationManager.AppSettings["expiry"].ToString()))
                                            instrumentToken = ConfigurationManager.AppSettings["NSENIFTY"].ToString();
                                        else
                                            continue;
                                        quotes = kite.GetOHLC(new string[] { instrumentToken });
                                    }
                                    else
                                    {
                                        quotes = kite.GetOHLC(InstrumentId: new string[] { "NSE:" + equitySymbol });
                                        instrumentToken = quotes["NSE:" + equitySymbol].InstrumentToken.ToString();
                                    }
                                }
                                catch (System.TimeoutException)
                                {
                                    quotes = kite.GetOHLC(InstrumentId: new string[] { "NSE:" + equitySymbol });
                                }
                                if (quotes.Count > 0)
                                {
                                    System.Threading.Thread.Sleep(400);
                                    dayHistory = kite.GetHistoricalData(instrumentToken,
                                                previousDay.AddDays(-31), currentDay, "day");
                                    if (dayHistory.Count < 16)
                                    {
                                        Console.WriteLine("This Script {0} has very less history candles count {1}", instrumentToken, dayHistory.Count);
                                        continue;
                                    }
                                    lastClose = dayHistory[dayHistory.Count - 1].Close;
                                    if ((lastClose < 950 && lastClose > 100)
                                        || equitySymbol.Contains("NIFTY"))
                                    {
                                        topBB = 0;
                                        botBB = 0;
                                        middle = 0;
                                        counter = 20;
                                        int index = 0;
                                        List<Historical> history = new List<Historical>();
                                        try
                                        {
                                            System.Threading.Thread.Sleep(400);
                                            history = kite.GetHistoricalData(instrumentToken,
                                                previousDay.AddDays(-4), currentDay, "30minute");
                                            //Console.WriteLine("Got Quote successfull & Passed Day Close of {0} with historyCount {1}", equitySymbol, history.Count);

                                            while (history.Count < 50)
                                            {
                                                System.Threading.Thread.Sleep(1000);
                                                previousDay = previousDay.AddDays(-1);
                                                history = kite.GetHistoricalData(instrumentToken,
                                                    previousDay.AddDays(-4), currentDay, "30minute");
                                                //Console.WriteLine("History Candles are lesser than Expceted candles. Please Check the given dates. PreviousDate {0} CurrentDate {1}, with candles count {2}", previousDay.AddDays(-5), currentDay, history.Count);
                                            }
                                        }
                                        catch (System.TimeoutException)
                                        {
                                            continue;
                                        }
                                        int startCandle = 0;

                                        if (history.Count > 0)
                                        {
                                            for (counter = history.Count - 1; counter > 0; counter--)
                                            {
                                                if (history[counter].TimeStamp.Hour == 9 && history[counter].TimeStamp.Minute == 15)
                                                {
                                                    startCandle++;
                                                    break;
                                                }
                                                else
                                                {
                                                    startCandle++;
                                                }
                                            }
                                            for (counter = history.Count - 1; counter > 0; counter--)
                                            //foreach (tempHistory candle in history)
                                            {
                                                if ((history[counter].TimeStamp.Hour == 15 && history[counter].TimeStamp.Minute == 45) ||
                                                    (history[counter].TimeStamp.Hour == 8 && history[counter].TimeStamp.Minute == 45))
                                                {
                                                    //Do Nothing
                                                }
                                                else
                                                {
                                                    middle = middle + history[counter].Close;
                                                    index++;
                                                    if (index == 20)
                                                        break;
                                                }
                                            }
                                            middle = Math.Round(middle / 20, 2);
                                            index = 0;
                                            decimal sd = 0;
                                            for (counter = history.Count - 1; counter > 0; counter--)
                                            //foreach (tempHistory candle in history)
                                            {
                                                if ((history[counter].TimeStamp.Hour == 15 && history[counter].TimeStamp.Minute == 45) ||
                                                    (history[counter].TimeStamp.Hour == 8 && history[counter].TimeStamp.Minute == 45))
                                                {
                                                    //Do Nothing
                                                }
                                                else
                                                {
                                                    sd = (middle - history[counter].Close) * (middle - history[counter].Close) + sd;
                                                    index++;
                                                    if (index == 20)
                                                        break;
                                                }
                                            }
                                            sd = Math.Round((decimal)Math.Sqrt((double)(sd / (20))), 2) * 2;
                                            topBB = middle + sd;
                                            botBB = middle - sd;
                                        }

                                        decimal square1 = ((lastClose / 100) * (decimal)2.7);
                                        decimal square2 = ((lastClose / 100) * (decimal)1.8);
                                        Historical h1, h2;
                                        string lastCandleClose = "";

                                        if (history[history.Count - 1].TimeStamp.Hour == 15 && history[history.Count - 1].TimeStamp.Minute == 45)
                                        {
                                            lastCandleClose = history[history.Count - 2].Close.ToString();
                                            h1 = history[history.Count - 3];
                                            h2 = history[history.Count - 2];
                                        }
                                        else
                                        {
                                            lastCandleClose = history[history.Count - 1].Close.ToString();
                                            h1 = history[history.Count - 2];
                                            h2 = history[history.Count - 1];
                                        }
                                        decimal black = GetWeekMA(instrumentToken);
                                        decimal ma50 = GetMA50(instrumentToken, black);
                                        
                                        //decimal prevBlack = 0;
                                        //prevBlack = GetPreviousWeekMA(quotes["NSE:" + equitySymbol].InstrumentToken.ToString());

                                        decimal variance025 = (lastClose * (decimal)0.25) / 100;
                                        decimal variance06 = (lastClose * (decimal)0.6) / 100;
                                        decimal variance13 = (lastClose * (decimal)1.3) / 100;
                                        decimal variance18 = (lastClose * (decimal)1.8) / 100;
                                        //decimal variance21 = (lastClose * (decimal)2.1) / 100;
                                        decimal variance35 = (lastClose * (decimal)3.5) / 100;

                                        if (equitySymbol.Contains("BANDHAN"))
                                            Console.WriteLine("Break Point Check for debug purpose; Continue");
                                        bool bullflag = (h1.Open <= h1.Close && (h2.Open <= h2.Close || lastClose > middle + variance025) && Decimal.Compare(lastClose, Convert.ToDecimal(lastCandleClose)) >= 0);
                                        bool bearflag = (h1.Open >= h1.Close && (h2.Open >= h2.Close || lastClose < middle - variance025) && Decimal.Compare(lastClose, Convert.ToDecimal(lastCandleClose)) <= 0);
                                        string canTrust = "True";
                                        if (!equitySymbol.Contains("NIFTY"))
                                        {
                                            if (!CanTrust(quotes["NSE:" + equitySymbol].Close, startCandle, history, black))
                                                canTrust = "False";
                                        }
                                        else
                                            canTrust = "True";

                                        OType dayTrend = CheckDayTrendOf(scripts, previousDay, currentDay);
                                        if (dayTrend != OType.BS)
                                        {
                                            Int64 avgVolume = GetAvgVolume(dayHistory);
                                            if (dayTrend == OType.Buy)
                                            {
                                                csvLine.Add(instrumentToken + "," +
                                                        scripts.InstrumentToken.ToString() + "," +
                                                        equitySymbol + "," +
                                                        scripts.LotSize.ToString() + "," +
                                                        topBB.ToString() + "," +
                                                        botBB.ToString() + "," +
                                                        middle.ToString() + ",BUY,OPEN,1,MORNING," +
                                                        ma50.ToString() + "," +
                                                        black.ToString() + "," +
                                                        lastCandleClose + "," +
                                                        lastClose.ToString() + "," +
                                                        "BUY,False,False,"
                                                        + canTrust + ","
                                                        + avgVolume.ToString());
                                            }
                                            else if (dayTrend == OType.Sell)
                                            {
                                                csvLine.Add(instrumentToken + "," +
                                                        scripts.InstrumentToken.ToString() + "," +
                                                        equitySymbol + "," +
                                                        scripts.LotSize.ToString() + "," +
                                                        topBB.ToString() + "," +
                                                        botBB.ToString() + "," +
                                                        middle.ToString() + ",SELL,OPEN,1,MORNING," +
                                                        ma50.ToString() + "," +
                                                        black.ToString() + "," +
                                                        lastCandleClose + "," +
                                                        lastClose.ToString() + "," +
                                                        "SELL,False,False,"
                                                        + canTrust + ","
                                                        + avgVolume.ToString());
                                            }
                                        }
                                        else if (lastClose < black)
                                        {
                                            #region Added BUY Condition
                                            if (botBB + variance35 > topBB
                                                && (IsBetweenVariance((botBB + variance18), topBB, (decimal).0001)
                                                    || (botBB + variance18) < topBB))
                                            //|| IsBetweenVariance(botBB, Convert.ToDecimal(lastCandleClose), (decimal).0003))
                                            {
                                                Int64 avgVolume = GetAvgVolume(dayHistory);
                                                //if (bearflag && lastClose < middle - variance025)  // && black > prevBlack) && (botBB + variance18 > black && 
                                                if (lastClose < middle
                                                    && IsBeyondVariance(lastClose, middle, (decimal).0008)
                                                    //&& black < ma50
                                                    && lastClose < (black - variance06)
                                                    && lastClose > (black - variance18))
                                                {
                                                    csvLine.Add(instrumentToken + "," +
                                                        scripts.InstrumentToken.ToString() + "," +
                                                        equitySymbol + "," +
                                                        scripts.LotSize.ToString() + "," +
                                                        topBB.ToString() + "," +
                                                        botBB.ToString() + "," +
                                                        middle.ToString() + ",SELL,OPEN,1,DAY," +
                                                        ma50.ToString() + "," +
                                                        black.ToString() + "," +
                                                        lastCandleClose + "," +
                                                        lastClose.ToString() + "," +
                                                        "MONITOR,False,False,"
                                                        + canTrust + ","
                                                        + avgVolume.ToString());
                                                }
                                                else
                                                {
                                                    csvLine.Add(instrumentToken + "," +
                                                        scripts.InstrumentToken.ToString() + "," +
                                                        equitySymbol + "," +
                                                        scripts.LotSize.ToString() + "," +
                                                        topBB.ToString() + "," +
                                                        botBB.ToString() + "," +
                                                        middle.ToString() + ",SELL,OPEN,1,DAY," +
                                                        ma50.ToString() + "," +
                                                        black.ToString() + "," +
                                                        lastCandleClose + "," +
                                                        lastClose.ToString() + "," +
                                                        "MONITOR,False,False,"
                                                        + canTrust + ","
                                                        + avgVolume.ToString());
                                                }
                                                scriptsCount++;
                                            }
                                            else if ((IsBetweenVariance((botBB + variance18), topBB, (decimal).0001)
                                                    || (botBB + variance18) < topBB)
                                                    || scripts.TradingSymbol.Contains("NIFTY"))// && scriptsCount < 35)
                                            {
                                                Int64 avgVolume = GetAvgVolume(dayHistory);
                                                csvLine.Add(instrumentToken + "," +
                                                    scripts.InstrumentToken.ToString() + "," +
                                                    equitySymbol + "," +
                                                    scripts.LotSize.ToString() + "," + //(scripts.LotSize + 1).ToString() + "," +
                                                    topBB.ToString() + "," +
                                                    botBB.ToString() + "," +
                                                    middle.ToString() + ",SELL,OPEN,1,DAY," +
                                                    ma50.ToString() + "," +
                                                    black.ToString() + "," +
                                                    lastCandleClose + "," +
                                                    lastClose.ToString() + "," +
                                                    "FORTIFY,False,False,"
                                                    + canTrust + ","
                                                    + avgVolume.ToString());
                                                scriptsCount++;
                                            }
                                            else
                                            {
                                                //Console.WriteLine("This Scrript {0} is Ignored from trading", equitySymbol);
                                            }
                                            #endregion
                                        }
                                        else if (lastClose > black)
                                        {
                                            #region Added SELL Condition
                                            if (botBB + variance35 > topBB
                                                && (IsBetweenVariance((botBB + variance18), topBB, (decimal).0001)
                                                    || (botBB + variance18) < topBB))
                                            //|| IsBetweenVariance(topBB, Convert.ToDecimal(lastCandleClose), (decimal).0002))
                                            {
                                                Int64 avgVolume = GetAvgVolume(dayHistory);
                                                //if // && bullflag && lastClose > middle + variance025)  // && black > prevBlack) && (topBB - variance18 < black
                                                if (lastClose > middle
                                                    && IsBeyondVariance(lastClose, middle, (decimal).0008)
                                                    //&& black > ma50
                                                    && lastClose > (black + variance06)
                                                    && lastClose < (black + variance18))
                                                {
                                                    csvLine.Add(instrumentToken + "," +
                                                        scripts.InstrumentToken.ToString() + "," +
                                                        equitySymbol + "," +
                                                        scripts.LotSize.ToString() + "," +
                                                        topBB.ToString() + "," +
                                                        botBB.ToString() + "," +
                                                        middle.ToString() + ",BUY,OPEN,1,DAY," +
                                                        ma50.ToString() + "," +
                                                        black.ToString() + "," +
                                                        lastCandleClose + "," +
                                                        lastClose.ToString() + "," +
                                                        "MONITOR,False,False,"
                                                        + canTrust + ","
                                                        + avgVolume.ToString());
                                                }
                                                else
                                                {
                                                    csvLine.Add(instrumentToken + "," +
                                                        scripts.InstrumentToken.ToString() + "," +
                                                        equitySymbol + "," +
                                                        scripts.LotSize.ToString() + "," +
                                                        topBB.ToString() + "," +
                                                        botBB.ToString() + "," +
                                                        middle.ToString() + ",BUY,OPEN,1,DAY," +
                                                        ma50.ToString() + "," +
                                                        black.ToString() + "," +
                                                        lastCandleClose + "," +
                                                        lastClose.ToString() + "," +
                                                        "MONITOR,False,False,"
                                                        + canTrust + ","
                                                        + avgVolume.ToString());
                                                }
                                                scriptsCount++;
                                            }
                                            else if ((IsBetweenVariance((botBB + variance18), topBB, (decimal).0001)
                                                    || (botBB + variance18) < topBB)
                                                    || scripts.TradingSymbol.Contains("NIFTY")) // && scriptsCount < 35)
                                            {
                                                Int64 avgVolume = GetAvgVolume(dayHistory);
                                                csvLine.Add(instrumentToken + "," +
                                                    scripts.InstrumentToken.ToString() + "," +
                                                    equitySymbol + "," +
                                                    scripts.LotSize.ToString() + "," +
                                                    topBB.ToString() + "," +
                                                    botBB.ToString() + "," +
                                                    middle.ToString() + ",BUY,OPEN,1,DAY," +
                                                    ma50.ToString() + "," +
                                                    black.ToString() + "," +
                                                    lastCandleClose + "," +
                                                    lastClose.ToString() + "," +
                                                    "FORTIFY,False,False,"
                                                    + canTrust + ","
                                                    + avgVolume.ToString());
                                                scriptsCount++;
                                            }
                                            else
                                            {
                                                //Console.WriteLine("This Scrript {0} is Ignored from trading", equitySymbol);
                                            }
                                            #endregion
                                        }
                                        if (scripts.TradingSymbol.Contains("NIFTY"))
                                            scriptsCount--;
                                    }
                                }
                                else
                                    Console.WriteLine("Catch It " + equitySymbol);
                            }
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine("EXCEPTION While Calculating NFO Script {0} recieved -> {1}", scripts.TradingSymbol, ex.Message);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("EXCEPTION While getting All NFO Scripts recieved -> {0}", ex.Message);
            }
            return csvLine;
        }

        public void CalculateExpiry(List<Instrument> calcInstruments, int tempMonth, int counter, string segment)
        {
            try
            {
                string year = string.Empty;
                foreach (Instrument scripts in calcInstruments)
                {
                    if (scripts.Segment == segment
                        && scripts.InstrumentToken.ToString().Length > 3)
                    {
                        try
                        {
                            DateTime expiry = (DateTime)scripts.Expiry;
                            if (month == 0
                                && expiry.Month == tempMonth
                                && (expiry.Day >= DateTime.Now.Day + 1
                                    || DateTime.Now.Month < tempMonth))
                            {
                                month = tempMonth;
                                year = DateTime.Now.Year.ToString().Substring(2, 2);
                            }
                            else if (month == 0
                                && expiry.Month == tempMonth
                                && expiry.Day == DateTime.Now.Day)
                            {
                                if (expiry.Month == 12)
                                {
                                    month = 1;
                                    year = (DateTime.Now.Year + 1).ToString().Substring(2, 2);
                                }
                                else
                                {
                                    month = tempMonth + 1;
                                    year = DateTime.Now.Year.ToString().Substring(2, 2);
                                }
                            }
                            if (month > 0
                                && expiry.Month == month)
                            {
                                if (futMonth.Length == 0)
                                {
                                    futMonth = scripts.TradingSymbol.Substring(scripts.TradingSymbol.IndexOf(year), scripts.TradingSymbol.Length - scripts.TradingSymbol.IndexOf(year));
                                    break;
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine("EXCEPTION While assigning Trade Month and Expiry for Script {0} recieved -> {1}", scripts.TradingSymbol, ex.Message);
                        }
                    }
                }
                if (month == 0 && counter < 1)
                    CalculateExpiry(calcInstruments, DateTime.Now.Month + 1, 1, segment);
                else if (month == 0)
                    Console.WriteLine("Expiry Month and FUT Name could not be identified. Break the Execution");
                else
                    Console.WriteLine("Expiry Month and FUT Name are identified. Continue the Execution");
            }
            catch (Exception ex)
            {
                Console.WriteLine("EXCEPTION While assigning Trade Month and Expiry recieved -> {0}", ex.Message);
            }
            finally
            {
                if (month == 0)
                    throw new Exception("Expiry Month and FUT Name could not be identified. Break the Execution");
                else
                    Console.WriteLine("Identified Trade Month as {0} and FUT month as {1}", month.ToString(), futMonth);
            }
        }

        public string CalculateCommodityBB(string instrument, string name)
        {
            decimal topBB = 0;
            decimal botBB = 0;
            decimal middle = 0;
            int counter = 20;
            int index = 0;
            int subDate = 0;
            switch (DateTime.Now.DayOfWeek)
            {
                case DayOfWeek.Monday:
                    subDate = -3;
                    break;
                case DayOfWeek.Sunday:
                    subDate = -2;
                    break;
                default:
                    subDate = -1;
                    break;
            }

            if (checkHoliday(DateTime.Now.Date.AddDays(subDate)))
                subDate = subDate - 1;

            DateTime previousDay = DateTime.Now.Date.AddDays(subDate);
            DateTime currentDay = DateTime.Now.Date.AddDays(1);
            List<Historical> history = new List<Historical>();
            try
            {
                System.Threading.Thread.Sleep(400);
                history = kite.GetHistoricalData(instrument,
                                previousDay, currentDay, "30minute");
            }
            catch (System.TimeoutException)
            {
                System.Threading.Thread.Sleep(1000);
                history = kite.GetHistoricalData(instrument,
                                previousDay, currentDay, "30minute");
            }
            if (history.Count > 0)
            {
                for (counter = history.Count - 1; counter > 0; counter--)
                {
                    middle = middle + history[counter].Close;
                    index++;
                    if (index == 20)
                        break;
                }
                middle = Math.Round(middle / index, 2);
                index = 0;
                decimal sd = 0;
                for (counter = history.Count - 1; counter > 0; counter--)
                {
                    sd = (middle - history[counter].Close) * (middle - history[counter].Close) + sd;
                    index++;
                    if (index == 20)
                        break;
                }
                sd = Math.Round((decimal)Math.Sqrt((double)(sd / (index))), 2) * 2;
                topBB = middle + sd;
                botBB = middle - sd;
            }
            return instrument + "," + instrument + "," + name + ",1," + topBB.ToString() + "," + botBB + "," + middle + ",SELL,OPEN,1,DAY,0,0,0,0,MONITOR";
        }

        public void CalculateSqueezedBB()
        {
            List<string> csvLine = new List<string>();
            try
            {
                DateTime previousDay, currentDay;
                getDays(out previousDay, out currentDay);
                decimal topBB = 0;
                decimal botBB = 0;
                decimal middle = 0;
                int counter = 0;

                List<Instrument> calcInstruments = kite.GetInstruments("NFO");
                bool blackListFlag = false;
                foreach (Instrument scripts in calcInstruments)
                {
                    blackListFlag = false;
                    string[] bList = ConfigurationManager.AppSettings["blacklist"].ToString().Split(',');
                    foreach (string black in bList)
                    {
                        if (scripts.TradingSymbol.Contains(black))
                        {
                            blackListFlag = true;
                            break;
                        }
                    }
                    if (blackListFlag)
                        continue;

                    if (scripts.Segment == "NFO-FUT"
                        //&& !scripts.TradingSymbol.Contains("CENTURYTEX")
                        && scripts.LotSize < 5200
                        && scripts.LotSize > 900)
                    {
                        try
                        {
                            DateTime expiry = (DateTime)scripts.Expiry;
                            if (expiry.Month == month) //Convert.ToInt16(ConfigurationManager.AppSettings["month"]))
                            {
                                string equitySymbol = scripts.TradingSymbol.Replace(futMonth, ""); //ConfigurationManager.AppSettings["expiry"].ToString(), "");
                                Dictionary<string, Quote> quotes = new Dictionary<string, Quote>();
                                //List<Historical> dayHistory = new List<Historical>();
                                decimal lastClose = 0;
                                try
                                {
                                    System.Threading.Thread.Sleep(500);
                                    quotes = kite.GetQuote(InstrumentId: new string[] { "NSE:" + equitySymbol });
                                }
                                catch (System.TimeoutException)
                                {
                                    System.Threading.Thread.Sleep(500);
                                    quotes = kite.GetQuote(InstrumentId: new string[] { "NSE:" + equitySymbol });
                                }
                                if (quotes.Count > 0)
                                {
                                    System.Threading.Thread.Sleep(500);
                                    //dayHistory = kite.GetHistoricalData(quotes["NSE:" + equitySymbol].InstrumentToken.ToString(),
                                    //            previousDay, currentDay, "day");
                                    lastClose = quotes["NSE:" + equitySymbol].Close;
                                    if (lastClose < 950 && lastClose > 100)
                                    {
                                        int index = 0;
                                        List<Historical> history = GetHistory(quotes["NSE:" + equitySymbol].InstrumentToken, previousDay, currentDay);
                                        if (history == null || history.Count == 0)
                                            continue;
                                        topBB = 0;
                                        botBB = 0;
                                        middle = 0;
                                        counter = 20;
                                        if (history.Count > 0)
                                        {
                                            for (counter = history.Count - 1; counter > 0; counter--)
                                            //foreach (tempHistory candle in history)
                                            {
                                                if ((history[counter].TimeStamp.Hour == 15 && history[counter].TimeStamp.Minute == 45) ||
                                                    (history[counter].TimeStamp.Hour == 8 && history[counter].TimeStamp.Minute == 45))
                                                {
                                                    //Do Nothing
                                                }
                                                else
                                                {
                                                    middle += history[counter].Close;
                                                    index++;
                                                    if (index == 20)
                                                        break;
                                                }
                                            }
                                            middle = Math.Round(middle / 20, 2);
                                            index = 0;
                                            decimal sd = 0;
                                            for (counter = history.Count - 1; counter > 0; counter--)
                                            //foreach (tempHistory candle in history)
                                            {
                                                if ((history[counter].TimeStamp.Hour == 15 && history[counter].TimeStamp.Minute == 45) ||
                                                    (history[counter].TimeStamp.Hour == 8 && history[counter].TimeStamp.Minute == 45))
                                                {
                                                    //Do Nothing
                                                }
                                                else
                                                {
                                                    sd = (middle - history[counter].Close) * (middle - history[counter].Close) + sd;
                                                    index++;
                                                    if (index == 20)
                                                        break;
                                                }
                                            }
                                            sd = Math.Round((decimal)Math.Sqrt((double)(sd / (20))), 2) * 2;
                                            topBB = middle + sd;
                                            botBB = middle - sd;
                                        }

                                        decimal black = GetWeekMA(quotes["NSE:" + equitySymbol].InstrumentToken.ToString());

                                        CalculateSqueezedTrend(black, equitySymbol, lastClose, topBB, middle, botBB, history);
                                    }
                                }
                                else
                                    Console.WriteLine("Catch It " + equitySymbol);
                            }
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine("EXCEPTION While Calculating NFO Script {0} recieved -> {1}", scripts.TradingSymbol, ex.Message);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("EXCEPTION While getting All NFO Scripts recieved -> {0}", ex.Message);
            }
        }

        private List<Historical> GetHistory(uint token, DateTime previousDay, DateTime currentDay)
        {
            List<Historical> history = new List<Historical>();
            try
            {
                System.Threading.Thread.Sleep(500);
                history = kite.GetHistoricalData(token.ToString(),
                    previousDay.AddDays(-4), currentDay.AddDays(1), "30minute");
                //Console.WriteLine("Got Quote successfull & Passed Day Close of {0} with historyCount {1}", equitySymbol, history.Count);

                while (history.Count < 40)
                {
                    System.Threading.Thread.Sleep(1000);
                    previousDay = previousDay.AddDays(-1);
                    history = kite.GetHistoricalData(token.ToString(),
                        previousDay.AddDays(-4), currentDay.AddDays(1), "30minute");
                    //Console.WriteLine("History Candles are lesser than Expceted candles. Please Check the given dates. PreviousDate {0} CurrentDate {1}, with candles count {2}", previousDay.AddDays(-5), currentDay, history.Count);
                }
            }
            catch (System.TimeoutException)
            {
                return null;
            }
            return history;
        }

        private OType CalculateSqueezedTrend(decimal weekMA, string equityName, decimal lastClose, decimal topBB, decimal middleBB, decimal botBB, List<Historical> history)
        {
            OType trend = OType.BS;
            decimal variance2 = (lastClose * (decimal)2) / 100;
            decimal variance15 = (lastClose * (decimal)1.5) / 100;

            if ((botBB + variance2) > topBB)
            {
                int expected = ExpectedCandleCount(null);

                decimal middle30BB = 0;
                bool openAlign = true;
                int buySide = 0;
                int sellSide = 0;
                int neutral = 0;
                int candles = expected;
                for (; candles > 0; candles--)
                {
                    if (candles == 1)
                        break;
                    middle30BB = GetMiddle30BBOf(history, candles - 1)[0];

                    if (history[history.Count - candles].Close < middle30BB)
                        sellSide++;
                    else if (history[history.Count - candles].Close > middle30BB)
                        buySide++;
                    else
                        neutral++;

                    if (candles == expected)
                    {
                        if (lastClose < weekMA && IsBeyondVariance(lastClose, weekMA, (decimal).0008))
                        {
                            if (history[history.Count - candles - 1].Open < weekMA
                                && history[history.Count - candles - 1].Open < middle30BB
                                && IsBeyondVariance(lastClose, history[history.Count - candles].Open, (decimal).0006)
                                && IsBeyondVariance(history[history.Count - candles].Open, weekMA, (decimal).0006))
                            {
                                //continue to next iterations
                            }
                            else
                                openAlign = false;
                        }
                        else if (lastClose > weekMA && IsBeyondVariance(lastClose, weekMA, (decimal).0008))
                        {
                            if (history[history.Count - candles].Open > weekMA
                                && history[history.Count - candles].Open > middle30BB
                                && IsBeyondVariance(lastClose, history[history.Count - candles].Open, (decimal).0006)
                                && IsBeyondVariance(history[history.Count - candles].Open, weekMA, (decimal).0006))
                            {
                                //continue to next iterations
                            }
                            else
                                openAlign = false;
                        }
                        else
                        {
                            openAlign = false;
                        }
                    }
                }
                if (!openAlign)
                {
                    Console.WriteLine("");
                    Console.WriteLine("As per given calculatesquuezed check, this script is Not Aligned with OPEN ::: so returning for script {0} as Middle30BB {1}; WeekMA {2}; Script Open {3}", equityName, middle30BB, weekMA, history[history.Count - expected - 1].Open);
                    //return OType.BS;
                }
                int benchmark = -1;
                switch (expected)
                {
                    case 4:
                        benchmark = 2;
                        break;
                    case 5:
                        benchmark = 3;
                        break;
                    case 6:
                        benchmark = 4;
                        break;
                    case 7:
                        benchmark = 5;
                        break;
                    case 8:
                        benchmark = 6;
                        break;
                    case 9:
                        benchmark = 7;
                        break;
                    case 10:
                        benchmark = 8;
                        break;
                    case 11:
                        benchmark = 9;
                        break;
                    case 12:
                        benchmark = 10;
                        break;
                    case 13:
                        benchmark = 11;
                        break;
                    default:
                        benchmark = -1;
                        break;
                }
                benchmark--;
                DateTime previousDay;
                DateTime currentDay;
                getDays(out previousDay, out currentDay);
                System.Threading.Thread.Sleep(400);
                string equitySymbol = equityName.Replace(futMonth, ""); //ConfigurationManager.AppSettings["expiry"].ToString(), "");
                Dictionary<string, Quote> quotes = kite.GetQuote(InstrumentId: new string[] { "NSE:" + equitySymbol });
                List<Historical> history5min = kite.GetHistoricalData(quotes["NSE:" + equitySymbol].InstrumentToken.ToString(),
                                previousDay,
                                currentDay.AddDays(1),
                                "5minute");
                OType currentTrend = CalculateSqueezedTrend(equityName, history5min, 10);
                if (buySide + neutral >= benchmark && benchmark > 0)
                {
                    if ((botBB + variance15) > topBB
                            || IsBetweenVariance((botBB + variance15), topBB, (decimal).0006))
                    {
                        if (lastClose > weekMA)
                            Console.WriteLine("TOP squeezed aligning for BUY ::: for script {0} as Middle30BB {1}; WeekMA {2}; Script Open {3}; candles in buyside {4}", equityName, middle30BB, weekMA, history[history.Count - expected - 1].Open, buySide);
                        else
                            Console.WriteLine("TOP squeezed for BUY ::: script {0} as Middle30BB {1}; WeekMA {2}; Script Open {3}; candles in buyside {4}", equityName, middle30BB, weekMA, history[history.Count - expected - 1].Open, buySide);
                    }
                    else
                    {
                        if (IsBetweenVariance(middleBB, weekMA, (decimal).0006))
                        {
                            Console.WriteLine("Squeezed for BUY ::: WeekMA and Middle30BB is within a range for script {0} as Middle30BB {1}; WeekMA {2}; Script Open {3}; candles in buyside {4}", equityName, middle30BB, weekMA, history[history.Count - expected - 1].Open, buySide);
                        }
                        else
                            Console.WriteLine("Squeezed for BUY ::: script {0} as Middle30BB {1}; WeekMA {2}; Script Open {3}; candles in buyside {4}", equityName, middle30BB, weekMA, history[history.Count - expected - 1].Open, buySide);
                    }
                    trend = OType.Buy;
                    if ((currentTrend == OType.StrongBuy || currentTrend == OType.Buy) && history[history.Count - 2].Close > middle30BB)
                        Console.WriteLine("*******###########Last ten candles are algning for BUY ::: script {0} 30 mins candles in buyside {1} vs Sellside {2}", equityName, buySide, sellSide);
                    else if ((currentTrend == OType.StrongSell || currentTrend == OType.Sell) && history[history.Count - 2].Close < middle30BB)
                    {
                        Console.WriteLine("But Last ten candles are algning for SELL ::: script {0} 30 mins candles in -> buyside {1} vs Sellside {2}", equityName, buySide, sellSide);
                        trend = OType.Sell;
                    }
                }
                else if (sellSide + neutral >= benchmark && benchmark > 0)
                {
                    if ((botBB + variance15) > topBB
                            || IsBetweenVariance((botBB + variance15), topBB, (decimal).0006))
                    {
                        if (lastClose < weekMA)
                            Console.WriteLine("TOP squeezed aligning for SELL ::: script {0} as Middle30BB {1}; WeekMA {2}; Script Open {3}; candles in sellside {4}", equityName, middle30BB, weekMA, history[history.Count - expected - 1].Open, sellSide);
                        else
                            Console.WriteLine("TOP squeezed for SELL ::: script {0} as Middle30BB {1}; WeekMA {2}; Script Open {3}; candles in sellside {4}", equityName, middle30BB, weekMA, history[history.Count - expected - 1].Open, sellSide);
                    }
                    else
                    {
                        if (IsBetweenVariance(middleBB, weekMA, (decimal).0006))
                        {
                            Console.WriteLine("Squeezed for SELL ::: WeekMA and Middle30BB is within a range for script {0} as Middle30BB {1}; WeekMA {2}; Script Open {3}; candles in buyside {4}", equityName, middle30BB, weekMA, history[history.Count - expected - 1].Open, sellSide);
                        }
                        else
                            Console.WriteLine("Squeezed for SELL ::: script {0} as Middle30BB {1}; WeekMA {2}; Script Open {3}; candles in sellside {4}", equityName, middle30BB, weekMA, history[history.Count - expected - 1].Open, sellSide);
                    }
                    trend = OType.Sell;
                    if ((currentTrend == OType.StrongBuy || currentTrend == OType.Buy) && history[history.Count - 2].Close > middle30BB)
                    {
                        trend = OType.Buy;
                        Console.WriteLine("But Last ten 5min candles are algning for BUY ::: script {0} 30 mins candles in buyside {1} vs Sellside {2}", equityName, buySide, sellSide);
                    }
                    else if ((currentTrend == OType.StrongSell || currentTrend == OType.Sell) && history[history.Count - 2].Close < middle30BB)
                        Console.WriteLine("Last ten 5min candles are algning for SELL ::: script {0} 30 mins candles in buyside {1} vs Sellside {2}", equityName, buySide, sellSide);
                }
                else if (sellSide > buySide)
                {
                    Console.WriteLine("majority candles are Squeezed for SELL ::: WeekMA and Middle30BB is within a range for script {0} as Middle30BB {1}; WeekMA {2}; Script Open {3}; candles in SellSide {4}", equityName, middle30BB, weekMA, history[history.Count - expected - 1].Open, sellSide);
                    trend = OType.Sell;
                    if ((currentTrend == OType.StrongBuy || currentTrend == OType.Buy) && history[history.Count - 2].Close > middle30BB)
                    {
                        trend = OType.Buy;
                        Console.WriteLine("But Last ten candles are algning for BUY ::: script {0} candles in buyside {1} vs Sellside {2}", equityName, buySide, sellSide);
                    }
                    else if ((currentTrend == OType.StrongSell || currentTrend == OType.Sell) && history[history.Count - 2].Close < middle30BB)
                        Console.WriteLine("*******###########Last ten candles are algning for SELL ::: script {0} candles in buyside {1} vs Sellside {2}", equityName, buySide, sellSide);
                }
                else if (buySide > sellSide)
                {
                    Console.WriteLine("majority candles are Squeezed for BUY ::: WeekMA and Middle30BB is within a range for script {0} as Middle30BB {1}; WeekMA {2}; Script Open {3}; candles in buyside {4}", equityName, middle30BB, weekMA, history[history.Count - expected - 1].Open, buySide);
                    trend = OType.Buy;
                    if ((currentTrend == OType.StrongBuy || currentTrend == OType.Buy) && history[history.Count - 2].Close > middle30BB)
                        Console.WriteLine("Last ten candles are algning for BUY ::: script {0} candles in buyside {1} vs Sellside {2}", equityName, buySide, sellSide);
                    else if ((currentTrend == OType.StrongSell || currentTrend == OType.Sell) && history[history.Count - 2].Close < middle30BB)
                    {
                        Console.WriteLine("But Last ten candles are algning for SELL ::: script {0} candles in buyside {1} vs Sellside {2}", equityName, buySide, sellSide);
                        trend = OType.Sell;
                    }
                }
            }
            return trend;
        }

        public OType CalculateSqueezedTrend(string futName, List<Historical> history, int candles)
        {
            int minimum = candles > 14 ? 3 : 2;
            bool doLog = false;
            bool isFound = false;
            try
            {
                Dictionary<UInt32, WatchList>.KeyCollection keys = instruments.Keys;
                foreach (uint token in keys)
                {
                    if (instruments[token].futName == futName)
                    {
                        isFound = true;
                        if (instruments[token].oldTime != instruments[token].currentTime)
                        {
                            instruments[token].oldTime = instruments[token].currentTime;
                            doLog = true;
                        }
                        break;
                    }
                }
            }
            catch //(Exception ex)
            {
                //Console.WriteLine("AT {0} :: Required number of History candles are {1}", DateTime.Now.ToString(), ex.Message);
            }

            OType trend = OType.BS;

            int expected = ExpectedCandleCount(null);
            //if (timeFrame != 30)
            {
                expected = (expected - 1) * 6;
                if ((history.Count < 24 || expected < 14) && doLog)
                {
                    Console.WriteLine("AT {0} :: Required number of History candles are not found for script {1} & history candles count is {2}", DateTime.Now.ToString(), futName, history.Count);
                    return trend;
                }
            }
            decimal middleBB = 0;
            int buySide = 0;
            int sellSide = 0;
            int neutral = 0;
            for (int i = candles; i > 0; i--)
            {
                middleBB = GetMiddle30BBOf(history, i)[0];

                if (history[history.Count - i].Close < middleBB)
                    sellSide++;
                else if (history[history.Count - i].Close > middleBB)
                    buySide++;
                else
                    neutral++;
            }

            int benchmark = candles - (candles / 3);
            if (buySide + neutral >= benchmark && benchmark > 0)
            {
                trend = OType.Buy;
                if (buySide >= (candles - minimum))
                {
                    trend = OType.StrongBuy;
                    if (isFound)
                    {
                        if (doLog)
                            Console.WriteLine("5Min Candle Strongly Squeezed for Buy ::: script {0} buyside {1} vs candles {2}", futName, buySide, candles);
                    }
                    else
                        Console.WriteLine("5Min Candle Strongly Squeezed for Buy ::: script {0} buyside {1} vs candles {2}", futName, buySide, candles);
                }
                else
                {
                    if (isFound)
                    {
                        if (doLog)
                            Console.WriteLine("5Min Candle Squeezed for BUY ::: script {0} in buyside {1} vs benchmark {2}", futName, buySide, benchmark);
                    }
                    else
                        Console.WriteLine("5Min Candle Squeezed for BUY ::: script {0} in buyside {1} vs benchmark {2}", futName, buySide, benchmark);
                }
            }
            else if (sellSide + neutral >= benchmark && benchmark > 0)
            {
                trend = OType.Sell;
                if (sellSide >= (candles - minimum))
                {
                    trend = OType.StrongSell;
                    if (isFound)
                    {
                        if (doLog)
                            Console.WriteLine("5Min Candle Strongly Squeezed for SELL ::: script {0} sellside {1} vs candles {2}", futName, sellSide, candles);
                    }
                    else
                        Console.WriteLine("5Min Candle Strongly Squeezed for SELL ::: script {0} sellside {1} vs candles {2}", futName, sellSide, candles);
                }
                else
                {
                    if (isFound)
                    {
                        if (doLog)
                            Console.WriteLine("5Min Candle Squeezed for SELL ::: script {0} sellside {1} vs benchmark {2}", futName, sellSide, benchmark);
                    }
                    else
                        Console.WriteLine("5Min Candle Squeezed for SELL ::: script {0} sellside {1} vs benchmark {2}", futName, sellSide, benchmark);
                }
            }
            else if (sellSide > buySide)
            {
                if (isFound)
                {
                    if (doLog)
                        Console.WriteLine("majority 5Min Candle candles are Squeezed for SELL ::: script {0} in SellSide {1} vs buyside {2}", futName, sellSide, buySide);
                }
                else
                    Console.WriteLine("majority 5Min Candle candles are Squeezed for SELL ::: script {0} in SellSide {1} vs buyside {2}", futName, sellSide, buySide);
                //trend = OType.Sell;
            }
            else if (buySide > sellSide)
            {
                if (isFound)
                {
                    if (doLog)
                        Console.WriteLine("majority 5Min Candle candles are Squeezed for BUY ::: script {0} in buyside {1} vs sellside {2}", futName, buySide, sellSide);
                }
                else
                    Console.WriteLine("majority 5Min Candle candles are Squeezed for BUY ::: script {0} in buyside {1} vs sellside {2}", futName, buySide, sellSide);
                //trend = OType.Buy;
            }
            return trend;
        }

        public OType CalculateScriptPushingTrend(string futName, List<Historical> history, int candles)
        {
            int minimum = 6;
            bool doLog = false;
            bool isFound = false;
            try
            {
                Dictionary<UInt32, WatchList>.KeyCollection keys = instruments.Keys;
                foreach (uint token in keys)
                {
                    if (instruments[token].futName == futName)
                    {
                        isFound = true;
                        if (instruments[token].oldTime != instruments[token].currentTime)
                        {
                            instruments[token].oldTime = instruments[token].currentTime;
                            doLog = true;
                        }
                        break;
                    }
                }
            }
            catch //(Exception ex)
            {
                //Console.WriteLine("AT {0} :: Required number of History candles are {1}", DateTime.Now.ToString(), ex.Message);
            }

            OType trend = OType.BS;

            int expected = ExpectedCandleCount(null);
            //if (timeFrame != 30)
            {
                expected = (expected - 1) * 6;
                if ((history.Count < 24 || expected < 14) && doLog)
                {
                    Console.WriteLine("AT {0} :: Required number of History candles are not found for script {1} & history candles count is {2}", DateTime.Now.ToString(), futName, history.Count);
                    return trend;
                }
            }
            decimal middleBB = 0;
            int buySide = 0;
            int sellSide = 0;
            int neutral = 0;
            for (int i = candles; i > 0; i--)
            {
                middleBB = GetMiddle30BBOf(history, i)[0];

                if (history[history.Count - i].Close < middleBB)
                    sellSide++;
                else if (history[history.Count - i].Close > middleBB)
                    buySide++;
                else
                    neutral++;
            }

            int benchmark = candles - (candles / 3);
            if (buySide + neutral >= benchmark && benchmark > 0)
            {
                trend = OType.Buy;
                if (buySide >= (candles - minimum))
                {
                    trend = OType.StrongBuy;
                    if (isFound)
                    {
                        if (doLog)
                            Console.WriteLine("5Min Candle Strongly Squeezed for Buy ::: script {0} buyside {1} vs candles {2}", futName, buySide, candles);
                    }
                    else
                        Console.WriteLine("5Min Candle Strongly Squeezed for Buy ::: script {0} buyside {1} vs candles {2}", futName, buySide, candles);
                }
                else
                {
                    if (isFound)
                    {
                        if (doLog)
                            Console.WriteLine("5Min Candle Squeezed for BUY ::: script {0} in buyside {1} vs benchmark {2}", futName, buySide, benchmark);
                    }
                    else
                        Console.WriteLine("5Min Candle Squeezed for BUY ::: script {0} in buyside {1} vs benchmark {2}", futName, buySide, benchmark);
                }
            }
            else if (sellSide + neutral >= benchmark && benchmark > 0)
            {
                trend = OType.Sell;
                if (sellSide >= (candles - minimum))
                {
                    trend = OType.StrongSell;
                    if (isFound)
                    {
                        if (doLog)
                            Console.WriteLine("5Min Candle Strongly Squeezed for SELL ::: script {0} sellside {1} vs candles {2}", futName, sellSide, candles);
                    }
                    else
                        Console.WriteLine("5Min Candle Strongly Squeezed for SELL ::: script {0} sellside {1} vs candles {2}", futName, sellSide, candles);
                }
                else
                {
                    if (isFound)
                    {
                        if (doLog)
                            Console.WriteLine("5Min Candle Squeezed for SELL ::: script {0} sellside {1} vs benchmark {2}", futName, sellSide, benchmark);
                    }
                    else
                        Console.WriteLine("5Min Candle Squeezed for SELL ::: script {0} sellside {1} vs benchmark {2}", futName, sellSide, benchmark);
                }
            }
            else if (sellSide > buySide)
            {
                if (isFound)
                {
                    if (doLog)
                        Console.WriteLine("majority 5Min Candle candles are Squeezed for SELL ::: script {0} in SellSide {1} vs buyside {2}", futName, sellSide, buySide);
                }
                else
                    Console.WriteLine("majority 5Min Candle candles are Squeezed for SELL ::: script {0} in SellSide {1} vs buyside {2}", futName, sellSide, buySide);
                //trend = OType.Sell;
            }
            else if (buySide > sellSide)
            {
                if (isFound)
                {
                    if (doLog)
                        Console.WriteLine("majority 5Min Candle candles are Squeezed for BUY ::: script {0} in buyside {1} vs sellside {2}", futName, buySide, sellSide);
                }
                else
                    Console.WriteLine("majority 5Min Candle candles are Squeezed for BUY ::: script {0} in buyside {1} vs sellside {2}", futName, buySide, sellSide);
                //trend = OType.Buy;
            }
            return trend;
        }

        public bool CanTrust(decimal prevClose, int candles, List<Historical> history, decimal weekMA)
        {
            bool canTrust = true;
            bool isReversed = false;
            decimal middleBB = GetMiddle30BBOf(history, candles - 1)[0];
            OType type = OType.Buy;
            if (prevClose > weekMA)
            {
                type = OType.Buy;
            }
            else if (prevClose < weekMA)
            {
                type = OType.Sell;
            }
            else if(history[history.Count - candles].Open < weekMA && history[history.Count - candles].Close < middleBB)
            {
                type = OType.Sell;
            }
            for (int i = candles - 1; i > 0; i--)
            {
                middleBB = GetMiddle30BBOf(history, i-1)[0];
                if (history[history.Count - i].Close < middleBB)
                {
                    if (type == OType.Buy && !isReversed && IsBeyondVariance(history[history.Count - i].Close, middleBB, (decimal).0004))
                    {
                        isReversed = true;
                    }
                    else if (isReversed && type == OType.Sell)
                    {
                        if (IsBeyondVariance(history[history.Count - i].Close, middleBB, (decimal).0006))
                        {
                            if (IsBetweenVariance(weekMA, middleBB, (decimal).004) && middleBB > weekMA)
                            {
                                isReversed = false;
                            }
                            else
                            {
                                canTrust = false;
                                break;
                            }
                        }
                    }
                }
                else if (history[history.Count - i].Close > middleBB)
                {
                    if (type == OType.Sell && !isReversed && IsBeyondVariance(history[history.Count - i].Close, middleBB, (decimal).0004))
                    {
                        isReversed = true;
                    }
                    else if (isReversed && type == OType.Buy)
                    {
                        if (IsBeyondVariance(history[history.Count - i].Close, middleBB, (decimal).0006))
                        {
                            if (IsBetweenVariance(weekMA, middleBB, (decimal).004) && middleBB < weekMA)
                            {
                                isReversed = false;
                            }
                            else
                            {
                                canTrust = false;
                                break;
                            }
                        }
                    }
                }
            }
            return canTrust;
        }

        public bool CheckGearingStatus(uint instrument, OType type, ref int candleCount)
        {
            bool flag = false;
            int last12 = 0;
            int buyside = 0;
            int rising = 0;
            int falling = 0;
            int sellside = 0;
            candleCount = 0;
            if (instruments[instrument].history.Count <= 12)
                return flag;
            int i = 1;
            for (; i < instruments[instrument].history.Count && i <= 13; i++)
            {
                if (instruments[instrument].history[instruments[instrument].history.Count - i].TimeStamp.Date.Day != instruments[instrument].history[instruments[instrument].history.Count - 1].TimeStamp.Date.Day)
                    break;
                List<decimal> bols = GetMiddle30BBOf(instruments[instrument].history, i - 1);
                if (instruments[instrument].history[instruments[instrument].history.Count - i].Close < bols[0]
                    || (i == 1
                        && instruments[instrument].history[instruments[instrument].history.Count - i].Close >= bols[0]
                        && instruments[instrument].history[instruments[instrument].history.Count - 2].Close <= bols[0]))
                {
                    if (i <= 13 && type == OType.Sell)
                    {
                        last12++;
                        if (instruments[instrument].history[instruments[instrument].history.Count - i].Close < bols[2]
                            || (instruments[instrument].history[instruments[instrument].history.Count - i].Close < instruments[instrument].history[instruments[instrument].history.Count - i].Open
                                    && IsBetweenVariance(instruments[instrument].history[instruments[instrument].history.Count - i].Open, bols[0], (decimal).004))
                            || (instruments[instrument].history[instruments[instrument].history.Count - i].Close >= instruments[instrument].history[instruments[instrument].history.Count - i].Open
                                    && IsBetweenVariance(instruments[instrument].history[instruments[instrument].history.Count - i].Close, bols[0], (decimal).004)))
                        {
                            falling++;
                        }
                    }
                    if (type == OType.Sell)
                    {
                        sellside++;
                    }
                    if (i == 1
                        && instruments[instrument].history[instruments[instrument].history.Count - i].Close <= bols[0]
                        && instruments[instrument].history[instruments[instrument].history.Count - 2].Close >= bols[0])
                    {
                        if (type == OType.Buy)
                        {
                            last12++;
                            buyside++;
                        }
                    }
                    if (i <= 13 && last12 >= 11 && type == OType.Buy
                        && instruments[instrument].history[instruments[instrument].history.Count - (i + 1)].Close >= bols[0])
                    {
                        last12++;
                        buyside++;
                    }
                }
                else if (instruments[instrument].history[instruments[instrument].history.Count - i].Close > bols[0]
                         || (i == 1
                             && instruments[instrument].history[instruments[instrument].history.Count - i].Close <= bols[0]
                             && instruments[instrument].history[instruments[instrument].history.Count - 2].Close >= bols[0]))
                {
                    if (i <= 13 && type == OType.Buy)
                    {
                        last12++;
                        if (instruments[instrument].history[instruments[instrument].history.Count - i].Close < bols[2]
                            || (instruments[instrument].history[instruments[instrument].history.Count - i].Close < instruments[instrument].history[instruments[instrument].history.Count - i].Open
                                    && IsBetweenVariance(instruments[instrument].history[instruments[instrument].history.Count - i].Close, bols[0], (decimal).004))
                            || (instruments[instrument].history[instruments[instrument].history.Count - i].Close >= instruments[instrument].history[instruments[instrument].history.Count - i].Open
                                    && IsBetweenVariance(instruments[instrument].history[instruments[instrument].history.Count - i].Open, bols[0], (decimal).004)))
                        {
                            rising++;
                        }
                    }
                    if (type == OType.Buy)
                    {
                        buyside++;
                    }
                    if (i == 1
                        && instruments[instrument].history[instruments[instrument].history.Count - i].Close >= bols[0]
                        && instruments[instrument].history[instruments[instrument].history.Count - 2].Close <= bols[0])
                    {
                        if (type == OType.Sell)
                        {
                            last12++;
                            sellside++;
                        }
                    }
                    if (i <= 13 && last12 >= 11 && type == OType.Sell
                        && instruments[instrument].history[instruments[instrument].history.Count - (i + 1)].Close <= bols[0])
                    {
                        last12++;
                        sellside++;
                    }
                }
                else
                {
                    if (type == OType.Buy)
                    {
                        buyside++;
                    }
                    else if (type == OType.Sell)
                    {
                        sellside++;
                    }
                }
            }
            if (last12 >= 13)
            {
                if (type == OType.Buy)
                {
                    if (//buyside + 4 > i - sellside && 
                        buyside > 12 && rising >= 5)
                    {
                        flag = true;
                        candleCount = buyside;
                    }
                    else if (buyside > 12)
                        candleCount = buyside;
                }
                else
                {
                    if (//sellside + 4 > i - buyside && 
                        sellside > 12 && falling >= 5)
                    {
                        flag = true;
                        candleCount = sellside;
                    }
                    else if (sellside > 12)
                        candleCount = sellside;
                }
            }
            return flag;
        }

        public bool CheckGearingStatus(uint instrument, OType type)
        {
            bool flag = false;
            int last12 = 0;
            int buyside = 0;
            int sellside = 0;
            if (instruments[instrument].history.Count <= 12)
                return flag;
            int i = 1;
            for (; i < instruments[instrument].history.Count && i <= 13; i++)
            {
                if (instruments[instrument].history[instruments[instrument].history.Count - i].TimeStamp.Date.Day != instruments[instrument].history[instruments[instrument].history.Count - 1].TimeStamp.Date.Day)
                    break;
                List<decimal> bols = GetMiddle30BBOf(instruments[instrument].history, i);
                if (instruments[instrument].history[instruments[instrument].history.Count - i].Close < bols[0]
                    || (i == 1
                        && instruments[instrument].history[instruments[instrument].history.Count - i].Close >= bols[0]
                        && instruments[instrument].history[instruments[instrument].history.Count - 2].Close <= bols[0]))
                {
                    if (i <= 13 && type == OType.Sell)
                    {
                        last12++;
                    }
                    if (type == OType.Sell)
                    {
                        sellside++;
                    }

                    if (i == 1
                        && instruments[instrument].history[instruments[instrument].history.Count - i].Close <= bols[0]
                        && instruments[instrument].history[instruments[instrument].history.Count - 2].Close >= bols[0])
                    {
                        if (type == OType.Buy)
                        {
                            last12++;
                            buyside++;
                        }
                    }
                    if (i <= 13 && last12 >= 11 && type == OType.Buy
                        && instruments[instrument].history.Count > 15
                        && instruments[instrument].history[instruments[instrument].history.Count - (i + 1)].Close >= bols[0])
                    {
                        last12++;
                        buyside++;
                    }
                }
                else if (instruments[instrument].history[instruments[instrument].history.Count - i].Close > bols[0]
                         || (i == 1
                             && instruments[instrument].history[instruments[instrument].history.Count - i].Close <= bols[0]
                             && instruments[instrument].history[instruments[instrument].history.Count - 2].Close >= bols[0]))
                {
                    if (i <= 13 && type == OType.Buy)
                    {
                        last12++;
                    }
                    if (type == OType.Buy)
                    {
                        buyside++;
                    }
                    if (i == 1
                        && instruments[instrument].history[instruments[instrument].history.Count - i].Close >= bols[0]
                        && instruments[instrument].history[instruments[instrument].history.Count - 2].Close <= bols[0])
                    {
                        if (type == OType.Sell)
                        {
                            last12++;
                            sellside++;
                        }
                    }
                    if (i <= 13 && last12 >= 11 && type == OType.Sell
                        && instruments[instrument].history.Count > 15
                        && instruments[instrument].history[instruments[instrument].history.Count - (i+1)].Close <= bols[0])
                    {
                        last12++;
                        sellside++;
                    }
                }
                else
                {
                    if (type == OType.Buy)
                    {
                        buyside++;
                    }
                    else if (type == OType.Sell)
                    {
                        sellside++;
                    }
                }
            }
            if (last12 >= 13)
            {
                if (type == OType.Buy)
                {
                    if (//buyside + 4 > i - sellside && 
                        buyside > 12)
                    {
                        flag = true;
                    }
                }
                else
                {
                    if (//sellside + 4 > i - buyside && 
                        sellside > 12)
                    {
                        flag = true;
                    }
                }
            }
            return flag;
        }

        public bool CheckAllCandleStatus(uint instrument, OType type)
        {
            bool flag = true;
            if (instruments[instrument].history.Count <= 12)
                return flag;
            int i = 1;
            int lenience = 0;
            decimal ma50;
            bool isBetweenVariance = false;
            for (; i < instruments[instrument].history.Count; i++)
            {
                if (instruments[instrument].history[instruments[instrument].history.Count - i].TimeStamp.Date.Day != DateTime.Now.Day)
                    break;
                ma50 = GetMA50Of(instruments[instrument].history, i - 1);
                isBetweenVariance = IsBetweenVariance(instruments[instrument].history[instruments[instrument].history.Count - i].Close, ma50, (decimal).0004);
                if ((instruments[instrument].history[instruments[instrument].history.Count - i].Close <= ma50
                        || isBetweenVariance)
                    && type == OType.Sell)
                {
                    //Do Nothing
                }
                else if ((instruments[instrument].history[instruments[instrument].history.Count - i].Close >= ma50
                            || isBetweenVariance)
                        && type == OType.Buy)
                {
                    //Do Nothing
                }
                else
                {
                    lenience++;
                    int count = DateTime.Now.Hour > 11 || (DateTime.Now.Hour == 11 && DateTime.Now.Minute > 45) ? 5 : 3;
                    if (lenience >= count)
                    {
                        flag = false;
                        break;
                    }
                }
            }
            return flag;
        }

        public bool CheckRecentStatus(uint instrument, OType type)
        {
            bool flag = false;
            int last6 = 0;
            if (instruments[instrument].history.Count <= 12)
                return flag;
            int i = 1;
            for (; i < 7; i++)
            {
                if (instruments[instrument].history[instruments[instrument].history.Count - i].TimeStamp.Date.Day != DateTime.Now.Day)
                    break;
                List<decimal> bols = GetMiddle30BBOf(instruments[instrument].history, i - 1);
                if (instruments[instrument].history[instruments[instrument].history.Count - i].Close < bols[0]
                    || (i == 1
                        && instruments[instrument].history[instruments[instrument].history.Count - i].Close >= bols[0]
                        && instruments[instrument].history[instruments[instrument].history.Count - 2].Close <= bols[0]))
                {
                    if (i <= 6 && type == OType.Sell)
                    {
                        last6++;
                    }
                    if (i == 1
                        && type == OType.Buy
                        && instruments[instrument].history[instruments[instrument].history.Count - i].Close <= bols[0]
                        && instruments[instrument].history[instruments[instrument].history.Count - 2].Close >= bols[0])
                        last6++;
                }
                else if (instruments[instrument].history[instruments[instrument].history.Count - i].Close > bols[0]
                         || (i == 1
                             && instruments[instrument].history[instruments[instrument].history.Count - i].Close <= bols[0]
                             && instruments[instrument].history[instruments[instrument].history.Count - 2].Close >= bols[0]))
                {
                    if (i <= 6 && type == OType.Buy)
                    {
                        last6++;
                    }
                }
            }
            if (last6 >= 4)
            {
                flag = true;
            }
            return flag;
        }

        public bool CheckBollingerMovement(uint instrument)
        {
            bool flag = false;
            int last12 = 0;
            if (instruments[instrument].history.Count <= 12)
                return flag;
            List<decimal> bols = GetMiddle30BBOf(instruments[instrument].history, 0);
            decimal range = bols[1] - bols[2];
            for (int i = 1; i <= 14; i++)
            {
                bols = GetMiddle30BBOf(instruments[instrument].history, i - 1);
                if (IsBetweenVariance(range, bols[1] - bols[2], (decimal).05))
                    last12++;
            }
            if (last12 >= 8)
            {
                flag = true;
            }
            return flag;
        }

        public bool CheckLateStatus(uint instrument, OType type)
        {
            bool flag = false;
            int last6 = 0;
            if (instruments[instrument].history.Count <= 12)
                return flag;
            int i = 1;
            for (; i < 7; i++)
            {
                if (instruments[instrument].history[instruments[instrument].history.Count - i].TimeStamp.Date.Day != DateTime.Now.Day)
                    break;
                List<decimal> bols = GetMiddle30BBOf(instruments[instrument].history, i - 1);
                if (instruments[instrument].history[instruments[instrument].history.Count - i].Close < bols[0]
                    || (i == 1
                        && instruments[instrument].history[instruments[instrument].history.Count - i].Close >= bols[0]
                        && instruments[instrument].history[instruments[instrument].history.Count - 2].Close <= bols[0]))
                {
                    if (type == OType.Sell)
                    {
                        last6++;
                    }
                    if (i == 1
                        && type == OType.Buy
                        && instruments[instrument].history[instruments[instrument].history.Count - i].Close <= bols[0]
                        && instruments[instrument].history[instruments[instrument].history.Count - 2].Close >= bols[0])
                        last6++;
                }
                else if (instruments[instrument].history[instruments[instrument].history.Count - i].Close > bols[0]
                         || (i == 1
                             && instruments[instrument].history[instruments[instrument].history.Count - i].Close <= bols[0]
                             && instruments[instrument].history[instruments[instrument].history.Count - 2].Close >= bols[0]))
                {
                    if (type == OType.Buy)
                    {
                        last6++;
                    }
                }
            }
            if (last6 >= 6)
            {
                flag = true;
            }
            return flag;
        }

        public bool Check1HourStatus(uint instrument, OType type)
        {
            bool flag = false;
            int last6 = 0;
            if (instruments[instrument].history.Count <= 12)
                return flag;
            int i = 1;
            for (; i < 13; i++)
            {
                if (instruments[instrument].history[instruments[instrument].history.Count - i].TimeStamp.Date.Day != DateTime.Now.Day)
                    break;
                List<decimal> bols = GetMiddle30BBOf(instruments[instrument].history, i - 1);
                if (instruments[instrument].history[instruments[instrument].history.Count - i].Close < bols[0]
                    || (i == 1
                        && instruments[instrument].history[instruments[instrument].history.Count - i].Close >= bols[0]
                        && instruments[instrument].history[instruments[instrument].history.Count - 2].Close <= bols[0]))
                {
                    if (type == OType.Sell)
                    {
                        last6++;
                    }
                    if (i == 1
                        && type == OType.Buy
                        && instruments[instrument].history[instruments[instrument].history.Count - i].Close <= bols[0]
                        && instruments[instrument].history[instruments[instrument].history.Count - 2].Close >= bols[0])
                        last6++;
                }
                else if (instruments[instrument].history[instruments[instrument].history.Count - i].Close > bols[0]
                         || (i == 1
                             && instruments[instrument].history[instruments[instrument].history.Count - i].Close <= bols[0]
                             && instruments[instrument].history[instruments[instrument].history.Count - 2].Close >= bols[0]))
                {
                    if (type == OType.Buy)
                    {
                        last6++;
                    }
                }
            }
            if (last6 >= 7)
            {
                flag = true;
            }
            return flag;
        }

        public void CheckGearingStatus(uint instrument, OType type, ref int var1Counter, ref int var2Counter)
        {
            int last12 = 0;
            int buyside = 0;
            int sellside = 0;
            if (instruments[instrument].history.Count <= 12)
                return;
            decimal variance1 = Math.Round(instruments[instrument].history[instruments[instrument].history.Count - 1].Close * (decimal).002, 1);
            decimal variance2 = Math.Round(instruments[instrument].history[instruments[instrument].history.Count - 1].Close * (decimal).003, 1);
            decimal variance3 = Math.Round(instruments[instrument].history[instruments[instrument].history.Count - 1].Close * (decimal).0035, 1);
            var1Counter = 0;
            var2Counter = 0;
            decimal var1 = 0;
            decimal var2 = 0;
            decimal var3 = 0;
            for (int i = 1; i < instruments[instrument].history.Count && i <= 15; i++)
            {
                if (instruments[instrument].history[instruments[instrument].history.Count - i].TimeStamp.Date.Day != instruments[instrument].history[instruments[instrument].history.Count - 1].TimeStamp.Date.Day)
                    break;
                List<decimal> bols = GetMiddle30BBOf(instruments[instrument].history, i - 1);
                if (instruments[instrument].history[instruments[instrument].history.Count - i].Close < bols[0]
                    || (i == 1
                        && instruments[instrument].history[instruments[instrument].history.Count - i].Close >= bols[0]
                        && instruments[instrument].history[instruments[instrument].history.Count - 2].Close <= bols[0]))
                {
                    if (i <= 13 && type == OType.Sell)
                    {
                        last12++;
                        if (instruments[instrument].history[instruments[instrument].history.Count - i].High - instruments[instrument].history[instruments[instrument].history.Count - i].Low > variance3)
                        {
                            var3++;
                        }
                        if (instruments[instrument].history[instruments[instrument].history.Count - i].Open > instruments[instrument].history[instruments[instrument].history.Count - i].Close)
                        {
                            if (instruments[instrument].history[instruments[instrument].history.Count - i].Open - instruments[instrument].history[instruments[instrument].history.Count - i].Close < variance1)
                            {
                                var1++;
                            }
                            else if (instruments[instrument].history[instruments[instrument].history.Count - i].Open - instruments[instrument].history[instruments[instrument].history.Count - i].Close < variance2)
                            {
                                var2++;
                            }
                        }
                        else
                        {
                            if (instruments[instrument].history[instruments[instrument].history.Count - i].Close - instruments[instrument].history[instruments[instrument].history.Count - i].Open < variance1)
                            {
                                var1++;
                            }
                            else if (instruments[instrument].history[instruments[instrument].history.Count - i].Close - instruments[instrument].history[instruments[instrument].history.Count - i].Open < variance2)
                            {
                                var2++;
                            }
                        }
                    }
                    if (type == OType.Sell)
                    {
                        sellside++;
                        if (instruments[instrument].history[instruments[instrument].history.Count - i].Open > instruments[instrument].history[instruments[instrument].history.Count - i].Close)
                        {
                            if (instruments[instrument].history[instruments[instrument].history.Count - i].Open - instruments[instrument].history[instruments[instrument].history.Count - i].Close < variance1)
                            {
                                var1Counter++;
                            }
                            else if (instruments[instrument].history[instruments[instrument].history.Count - i].Open - instruments[instrument].history[instruments[instrument].history.Count - i].Close < variance2)
                            {
                                var2Counter++;
                            }
                        }
                        else
                        {
                            if (instruments[instrument].history[instruments[instrument].history.Count - i].Close - instruments[instrument].history[instruments[instrument].history.Count - i].Open < variance1)
                            {
                                var1Counter++;
                            }
                            else if (instruments[instrument].history[instruments[instrument].history.Count - i].Close - instruments[instrument].history[instruments[instrument].history.Count - i].Open < variance2)
                            {
                                var2Counter++;
                            }
                        }
                    }
                    if (i == 1
                        && instruments[instrument].history[instruments[instrument].history.Count - i].Close <= bols[0]
                        && instruments[instrument].history[instruments[instrument].history.Count - 2].Close >= bols[0])
                    {
                        if (type == OType.Buy)
                        {
                            var1++;
                            last12++;
                            buyside++;
                        }
                    }
                }
                else if (instruments[instrument].history[instruments[instrument].history.Count - i].Close > bols[0]
                         || (i == 1
                             && instruments[instrument].history[instruments[instrument].history.Count - i].Close <= bols[0]
                             && instruments[instrument].history[instruments[instrument].history.Count - 2].Close >= bols[0]))
                {
                    if (i <= 13 && type == OType.Buy)
                    {
                        last12++;
                        if (instruments[instrument].history[instruments[instrument].history.Count - i].Open > instruments[instrument].history[instruments[instrument].history.Count - i].Close)
                        {
                            if (instruments[instrument].history[instruments[instrument].history.Count - i].Open - instruments[instrument].history[instruments[instrument].history.Count - i].Close < variance1)
                            {
                                var1++;
                            }
                            else if (instruments[instrument].history[instruments[instrument].history.Count - i].Open - instruments[instrument].history[instruments[instrument].history.Count - i].Close < variance2)
                            {
                                var2++;
                            }
                        }
                        else
                        {
                            if (instruments[instrument].history[instruments[instrument].history.Count - i].Close - instruments[instrument].history[instruments[instrument].history.Count - i].Open < variance1)
                            {
                                var1++;
                            }
                            else if (instruments[instrument].history[instruments[instrument].history.Count - i].Close - instruments[instrument].history[instruments[instrument].history.Count - i].Open < variance2)
                            {
                                var2++;
                            }
                        }
                    }
                    if (type == OType.Buy)
                    {
                        buyside++;
                        if (instruments[instrument].history[instruments[instrument].history.Count - i].Open > instruments[instrument].history[instruments[instrument].history.Count - i].Close)
                        {
                            if (instruments[instrument].history[instruments[instrument].history.Count - i].Open - instruments[instrument].history[instruments[instrument].history.Count - i].Close < variance1)
                            {
                                var1Counter++;
                            }
                            else if (instruments[instrument].history[instruments[instrument].history.Count - i].Open - instruments[instrument].history[instruments[instrument].history.Count - i].Close < variance2)
                            {
                                var2Counter++;
                            }
                        }
                        else
                        {
                            if (instruments[instrument].history[instruments[instrument].history.Count - i].Close - instruments[instrument].history[instruments[instrument].history.Count - i].Open < variance1)
                            {
                                var1Counter++;
                            }
                            else if (instruments[instrument].history[instruments[instrument].history.Count - i].Close - instruments[instrument].history[instruments[instrument].history.Count - i].Open < variance2)
                            {
                                var2Counter++;
                            }
                        }
                    }
                }
                else
                {
                    if (type == OType.Buy)
                    {
                        buyside++;
                    }
                    else if (type == OType.Sell)
                    {
                        sellside++;
                    }
                }
            }
            if (last12 >= 13)
            {
                if (type == OType.Buy)
                {
                    if (var1 + var2 < 6 || var3 >= 5)
                    {
                        var1Counter = 0;
                        var1Counter = 0;
                    }
                }
                else
                {
                    if (var1 + var2 < 6 || var3 >= 5)
                    {
                        var1Counter = 0;
                        var2Counter = 0;
                    }
                }
            }
        }

        public bool CheckBollingerExpansion(uint instrument, OType type, decimal ltp, decimal variance, decimal timeNow)
        {
            bool flag = false;
            int last12 = 0;
            int least12 = 0;
            if (instruments[instrument].history.Count <= 12)
                return flag;
            
            if (instruments[instrument].topBB - instruments[instrument].botBB < Math.Round(ltp * (decimal).007,1)
                && ((instruments[instrument].close > ltp + variance //* (decimal)1.2)
                        && type == OType.Sell)
                    || (instruments[instrument].close < ltp - variance //* (decimal)1.2)
                        && type == OType.Buy)))
            {
                return flag;
            }
            decimal variance17 = Math.Round((ltp * (decimal)2.2) / 100, 1);
            decimal variance07 = Math.Round((ltp * (decimal).007), 1);
            decimal variance01 = Math.Round((ltp * (decimal).001), 1);
            if (instruments[instrument].topBB - instruments[instrument].botBB < Math.Round(ltp * (decimal).007, 1)
                && Decimal.Compare(timeNow, (decimal)11.25) < 0)
            {
                return flag;
            }

            for (int i = 1; i < instruments[instrument].history.Count && i < 13; i++)
            {
                if (instruments[instrument].history[instruments[instrument].history.Count - i].Close < ltp && type == OType.Buy)
                {
                    last12++;
                }
                else if (instruments[instrument].history[instruments[instrument].history.Count - i].Low < ltp - variance01 && type == OType.Buy)
                {
                    least12++;
                }
                else if (instruments[instrument].history[instruments[instrument].history.Count - i].Close > ltp && type == OType.Sell)
                {
                    last12++;
                }
                else if (instruments[instrument].history[instruments[instrument].history.Count - i].Close > ltp + variance01 && type == OType.Sell)
                {
                    least12++;
                }
                else
                {
                    List<decimal> bols = GetMiddle30BBOf(instruments[instrument].history, i);
                    if (bols[1] - bols[2] > variance17)
                    {
                        last12 = -1;
                        break;
                    }
                }
            }
            if (last12 >= 1 || (least12 >= 2 && instruments[instrument].topBB - instruments[instrument].botBB < variance07))
            {
                flag = true;
                //Console.WriteLine("Time {0}; Script {1} Bollinger status is true to logic?", DateTime.Now, instruments[instrument].futName);
            }
            if (flag)
            {
                Console.WriteLine("Will I ever return TRUE? here i am!!! for {0}", instruments[instrument].futName);
            }
            return flag;
        }

        public bool CheckMa50(uint instrument, OType type)
        {
            bool flag = false;
            int last12 = 0;
            if (instruments[instrument].history.Count <= 12)
                return flag;
            int i = 1;
            for (; i < instruments[instrument].history.Count && i < 15; i++)
            {
                List<decimal> bols = GetMiddle30BBOf(instruments[instrument].history, i - 1);
                decimal ma50 = GetMA50Of(instruments[instrument].history, i - 1);
                if (bols[0] < ma50 && type == OType.Sell)
                {
                    last12++;
                }
                else if (bols[0] > ma50 && type == OType.Buy)
                {
                    last12++;
                }
            }
            if (last12 >= 13)
            {
                flag = true;
            }
            return flag;
        }

        public bool CheckLateMa50(uint instrument, OType type)
        {
            bool flag = false;
            int last12 = 0;
            if (instruments[instrument].history.Count <= 12)
                return flag;
            int i = 1;
            for (; i < instruments[instrument].history.Count && i < 7; i++)
            {
                List<decimal> bols = GetMiddle30BBOf(instruments[instrument].history, i - 1);
                decimal ma50 = GetMA50Of(instruments[instrument].history, i - 1);
                if (bols[0] < ma50 && type == OType.Sell)
                {
                    last12++;
                }
                else if (bols[0] > ma50 && type == OType.Buy)
                {
                    last12++;
                }
            }
            if (last12 >= 6)
            {
                flag = true;
            }
            return flag;
        }

        int Get5MinCandleCount(DateTime timeNow)
        {
            int count = -1;
            DateTime dt = DateTime.Today.AddHours(9).AddMinutes(15);
            while (timeNow > dt)
            {
                timeNow = timeNow.AddMinutes(-5);
                count++;
            }
            return count;
        }

        public List<decimal> GetMiddle30BBOf(List<Historical> history, int benchmark)
        {
            int index = 0;
            List<decimal> bbvalues = new List<decimal>();
            decimal middle30BB = 0;
            int counter = history.Count - benchmark - 1;
            for (; counter > 0; counter--)
            {
                if ((history[counter].TimeStamp.Hour == 15 && history[counter].TimeStamp.Minute == 45) ||
                    history[counter].TimeStamp.Hour == 8 && history[counter].TimeStamp.Minute == 45)
                {
                    //Do Nothing
                }
                else
                {
                    middle30BB = middle30BB + history[counter].Close;
                    index++;
                    if (index == 20)
                        break;
                }
            }
            middle30BB = Math.Round(middle30BB / 20, 2);
            bbvalues.Add(middle30BB);
            index = 0;
            decimal sd = 0;
            counter = history.Count - benchmark - 1;
            for (; counter > 0; counter--)
            {
                sd = (middle30BB - history[counter].Close) * (middle30BB - history[counter].Close) + sd;
                index++;
                if (index == 20)
                    break;
            }
            sd = Math.Round((decimal)Math.Sqrt((double)(sd / (index))), 2) * 2;
            bbvalues.Add(middle30BB + sd);
            bbvalues.Add(middle30BB - sd);
            return bbvalues;
        }

        public List<decimal> GetForecastMiddle30BBOf(List<Historical> history, decimal middleBB, OType type)
        {
            int index = 0;
            List<decimal> bbvalues = new List<decimal>();
            decimal middle30BB = 0;
            int counter = history.Count - 1;
            decimal baseline = history[history.Count - 1].Close < middleBB? middleBB : history[history.Count - 1].Close;
            for (; counter > 0; counter--)
            {
                if ((history[counter].TimeStamp.Hour == 15 && history[counter].TimeStamp.Minute == 45) ||
                    history[counter].TimeStamp.Hour == 8 && history[counter].TimeStamp.Minute == 45)
                {
                    //Do Nothing
                }
                else
                {
                    middle30BB = middle30BB + history[counter].Close;
                    index++;
                    if (index == 18)
                        break;
                }
            }
            if (type == OType.Sell)
            {
                middle30BB = middle30BB + 2 * Math.Round(baseline * (decimal)1.003, 2);
            }
            else if (type == OType.Buy)
            {
                middle30BB = middle30BB + 2 * Math.Round(baseline * (decimal).997, 2);
            }
            middle30BB = Math.Round(middle30BB / 20, 2);
            bbvalues.Add(middle30BB);
            index = 0;
            decimal sd = 0;
            counter = history.Count - 1;
            for (; counter > 0; counter--)
            {
                sd = sd + (middle30BB - history[counter].Close) * (middle30BB - history[counter].Close);
                index++;
                if (index == 18)
                    break;
            }
            if (type == OType.Sell)
            {
                sd = sd + 2 * ((middle30BB - Math.Round(baseline * (decimal)1.003, 2)) * (middle30BB - Math.Round(baseline * (decimal)1.002, 2)));
            }
            else if (type == OType.Buy)
            {
                sd = sd + 2 * ((middle30BB - Math.Round(baseline * (decimal).997, 2)) * (middle30BB - Math.Round(baseline * (decimal).998, 2)));
            }
            sd = Math.Round((decimal)Math.Sqrt((double)(sd / (index))), 2) * 2;
            bbvalues.Add(middle30BB + sd);
            bbvalues.Add(middle30BB - sd);
            return bbvalues;
        }

        public decimal GetMA50Of(List<Historical> history, int benchmark)
        {
            int index = 0;
            List<decimal> bbvalues = new List<decimal>();
            decimal ma50 = 0;
            int counter = history.Count - benchmark - 1;
            for (; counter > 0; counter--)
            {
                if ((history[counter].TimeStamp.Hour == 15 && history[counter].TimeStamp.Minute == 45) ||
                    history[counter].TimeStamp.Hour == 8 && history[counter].TimeStamp.Minute == 45)
                {
                    //Do Nothing
                }
                else
                {
                    ma50 = ma50 + history[counter].Close;
                    index++;
                    if (index == 50)
                        break;
                }
            }
            ma50 = Math.Round(ma50 / 50, 2);
            return ma50;
        }

        public Int64 GetAvgVolume(List<Historical> dayHistory)
        {
            Int64 avgVolume = 0;
            foreach (Historical hist in dayHistory)
            {
                avgVolume = avgVolume + hist.Volume;
            }
            avgVolume = avgVolume / dayHistory.Count;
            List<Int64> lesserToAvg = new List<Int64>();
            foreach (Historical hist in dayHistory)
            {
                if(hist.Volume <= avgVolume)
                    lesserToAvg.Add(hist.Volume);
            }
            avgVolume = 0;
            foreach (Int64 vol in lesserToAvg)
            {
                avgVolume = avgVolume + vol;
            }
            avgVolume = avgVolume / lesserToAvg.Count;
            return avgVolume;
        }

        public void WriteToCsv(List<string> csvLine)
        {
            using (StreamWriter writer = new StreamWriter(ConfigurationManager.AppSettings["inputFile"], false))
            {
                foreach (String line in csvLine)
                    writer.WriteLine(line);
            }
            string duplicate = ConfigurationManager.AppSettings["inputFile"].ToString().Replace(".csv", "_Original.csv");
            using (StreamWriter writer = new StreamWriter(duplicate, false))
            {
                foreach (String line in csvLine)
                    writer.WriteLine(line);
            }
        }

        bool checkHoliday(DateTime date)
        {
            string[] holidays = ConfigurationManager.AppSettings["HolidayList"].Split(',');
            string[] day = new string[2];
            bool isHoliday = false;
            if (date.DayOfWeek == DayOfWeek.Sunday || date.DayOfWeek == DayOfWeek.Saturday)
                isHoliday = true;
            if (!isHoliday)
            {
                foreach (string holiday in holidays)
                {
                    day = holiday.Split('.');
                    if (date.Month == Convert.ToInt32(day[0]) && date.Day == Convert.ToInt32(day[1]))
                    {
                        isHoliday = true;
                        break;
                    }
                }
            }
            return isHoliday;
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

        private void modifyOpenAlignOrReversedStatus(WatchList n, int index, OType type, bool flag)
        {
            List<String> lines = new List<String>();
            using (StreamReader reader = new StreamReader(ConfigurationManager.AppSettings["inputFile"]))
            {
                String line;
                while ((line = reader.ReadLine()) != null)
                {
                    if (line.Contains(n.futId.ToString()))
                    {
                        string[] cells = line.Split(',');
                        if (flag)
                            cells[index] = "TRUE";
                        else
                            cells[index] = "FALSE";
                        if (index == 17)
                        {
                            switch (type)
                            {
                                case OType.Buy:
                                    cells[7] = "BUY";
                                    break;
                                case OType.Sell:
                                    cells[7] = "SELL";
                                    break;
                            }
                        }
                        line = "";
                        //Console.WriteLine("Updating CSV for the ticker :: {0} to status {1} for the day at {2} ", n.futName, cells[8], DateTime.Now.ToString());
                        for (int i = 0; i < cells.Length - 1; i++)
                        {
                            line = line + cells[i].ToString() + ",";
                        }
                        line = line + cells[cells.Length - 1].ToString();
                        line.TrimEnd(',');
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

        void WriteToStatus(string message)
        {
            if (!File.Exists(ConfigurationManager.AppSettings["StartupFile"]))
            {
                // Create a file to write to.
                using (StreamWriter sw = File.CreateText(ConfigurationManager.AppSettings["StartupFile"]))
                {
                    sw.WriteLine(message);
                }
            }
            else
            {
                // Append a file to write to.
                using (StreamWriter sw = File.AppendText(ConfigurationManager.AppSettings["StartupFile"]))
                {
                    sw.WriteLine(message);
                }
            }
        }

        public void OnTick(uint instrument, decimal ltp, uint volume, decimal timenow, decimal prevCandleClose, decimal averagePrice, decimal high, decimal low, uint buyQuantity, uint sellQuantity, decimal open, decimal change)
        {
            decimal serviceStopTime = Convert.ToDecimal(ConfigurationManager.AppSettings["ServiceStopTime"]);
            //decimal timenow = (decimal)DateTime.Now.Hour + ((decimal)DateTime.Now.Minute / 100);
            if (((Decimal.Compare(timenow, serviceStopTime) < 0 || Decimal.Compare(timenow, (decimal)9.14) > 0)
                    && instruments[instrument].status != Status.CLOSE
                    && startTicking)
                || instruments[instrument].status == Status.POSITION)
            {
                bool noOpenOrder = true;
                if (instruments[instrument].futName.Contains("NIFTY"))
                {
                    if (!instruments[instrument].futName.Contains("BANK") &&
                        !instruments[instrument].futName.Contains("FIN") && 
                        !isNiftyVolatile)
                    {
                        //VerifyNifty(tickData, timenow);
                    }
                    return;
                }
                if (VerifyLtp(instrument, ltp, volume, timenow, prevCandleClose, averagePrice, high, low, buyQuantity, sellQuantity, open, change))
                {
                    #region Return if Bolinger is narrowed or expanded
                    if (!instruments[instrument].isReversed)
                    {
                        decimal variance14 = (ltp * (decimal)1.4) / 100;
                        if ((instruments[instrument].bot30bb + variance14) > instruments[instrument].top30bb)
                        {
                            if (instruments[instrument].oldTime != instruments[instrument].currentTime)
                            {
                                instruments[instrument].oldTime = instruments[instrument].currentTime;
                                Console.WriteLine("Current Time is {0} and Closing Script {1} as the script has Narrowed so much and making it riskier where {2} > {3}",
                                    DateTime.Now.ToString(),
                                    instruments[instrument].futName,
                                    instruments[instrument].bot30bb + variance14,
                                    instruments[instrument].top30bb);
                            }

                            if (!instruments[instrument].canTrust)
                            {
                                if ((instruments[instrument].type == OType.Sell
                                        && instruments[instrument].middle30ma50 > instruments[instrument].bot30bb)
                                    || (instruments[instrument].type == OType.Buy
                                        && instruments[instrument].middle30ma50 < instruments[instrument].top30bb))
                                {
                                    CloseOrderTicker(instrument, true);
                                }
                            }
                            return;
                        }

                        decimal variance46 = (ltp * (decimal)4.6) / 100;
                        if ((instruments[instrument].bot30bb + variance46) < instruments[instrument].top30bb
                            && Decimal.Compare(timenow, Convert.ToDecimal(9.45)) > 0)
                        {
                            if (instruments[instrument].oldTime != instruments[instrument].currentTime)
                            {
                                instruments[instrument].oldTime = instruments[instrument].currentTime;
                                Console.WriteLine("Current Time is {0} and Closing Script {1} as the script has Expanded so much and making it riskier where {2} < {3}",
                                    DateTime.Now.ToString(),
                                    instruments[instrument].futName,
                                    instruments[instrument].bot30bb + variance46,
                                    instruments[instrument].top30bb);
                            }
                            if (instruments[instrument].canTrust
                                && IsBetweenVariance(ltp, instruments[instrument].middleBB, (decimal).0012))
                                return;
                            /*if (!instruments[instrument].canTrust)
                            {
                                CloseOrderTicker(instrument);
                            }*/
                            //return;
                        }
                    }
                    #endregion
                    List<Order> listOrder = kite.GetOrders();
                    //PositionResponse pr = kite.GetPositions();

                    for (int j = listOrder.Count - 1; j >= 0; j--)
                    {
                        Order order = listOrder[j];
                        //Console.WriteLine("ORDER Details {0}", order.InstrumentToken);
                        if (order.InstrumentToken == instruments[instrument].futId && (order.Status == "COMPLETE" || order.Status == "OPEN" || order.Status == "REJECTED"))
                        {
                            if (order.Status == "COMPLETE" || order.Status == "OPEN")
                            {
                                noOpenOrder = false;
                                break;
                            }
                            else if (order.Status == "REJECTED")
                            {
                                noOpenOrder = false;
                                if (order.OrderTimestamp != null)
                                {
                                    DateTime dt = Convert.ToDateTime(order.OrderTimestamp);
                                    if (DateTime.Now > dt.AddMinutes(30) && order.StatusMessage.Contains("Insufficient funds"))
                                    {
                                        //noOpenOrder = true;
                                        Console.WriteLine("Earlier REJECTED DUE TO {0}. Hence proceeding again to verify after 30 minutes", order.StatusMessage);
                                        CloseOrderTicker(instrument, true);
                                        break;
                                    }
                                    else if (DateTime.Now < dt.AddMinutes(30))
                                    {
                                        break;
                                    }
                                }
                                Console.WriteLine("Already REJECTED DUE TO {0}", order.StatusMessage);
                                if (order.StatusMessage.Contains("This instrument is blocked to avoid compulsory physical delivery")
                                    || order.StatusMessage.Contains("or it has been restricted from trading"))
                                {
                                    CloseOrderTicker(instrument, true);
                                }
                                if (!instruments[instrument].canTrust)
                                {
                                    Console.WriteLine("This script is cannot be trusted and this script is at same place for last 30 mins. hence closing this script");
                                }
                            }
                            else if (order.Status == "CANCELLED")
                            {
                                if (order.OrderTimestamp != null)
                                {
                                    DateTime dt = Convert.ToDateTime(order.OrderTimestamp);
                                    if (DateTime.Now < dt.AddMinutes(30))
                                    {
                                        noOpenOrder = false;
                                        CloseOrderTicker(instrument, true);
                                    }
                                }
                            }
                            else
                                noOpenOrder = false;
                        }
                    }
                    if (noOpenOrder)
                    {
                        if (instruments[instrument].type == OType.Buy)
                        {
                            Console.WriteLine("Time {0} Placing BUY Order of Instrument {1} for LTP {2} as it match long trigger {3} with top30BB {4} & bot30BB {5}", DateTime.Now.ToString(), instruments[instrument].futName, ltp.ToString(), instruments[instrument].longTrigger, instruments[instrument].top30bb, instruments[instrument].bot30bb);
                        }
                        else if (instruments[instrument].type == OType.Sell)
                        {
                            Console.WriteLine("Time {0} Placing SELL Order of Instrument {1} for LTP {2} as it match Short trigger {3} with top30BB {4} & bot30BB {5}", DateTime.Now.ToString(), instruments[instrument].futName, ltp.ToString(), instruments[instrument].shortTrigger, instruments[instrument].top30bb, instruments[instrument].bot30bb);
                        }
                        instruments[instrument].triggerPrice = ltp;
                        Console.WriteLine("Time {0} for Instrument {1} current Volume is {2} and average volume is {3}", DateTime.Now.ToString(), instruments[instrument].futName, volume, instruments[instrument].AvgVolume);
                        if (instruments[instrument].isSpiking)
                        {
                            Console.WriteLine("This script is Spiking up. Better stop !");
                            CloseOrderTicker(instrument, true);
                            return;
                        }
                        placeOrder(instrument, 0, 0);
                    }
                    else
                    {
                        //instruments[instrument].status = Status.STANDING;
                    }
                }
            }
        }

        public bool VerifyLtp(uint instrument, decimal ltp, uint volume, decimal timenow, decimal prevCandleClose, decimal averagePrice, decimal high, decimal low, uint buyQuantity, uint sellQuantity, decimal open, decimal change)
        {
            return false;
        }

        int ExpectedCandleCount(DateTime? pTime)
        {
            int expected = 0;
            try
            {
                decimal baseline = (decimal)9.15;
                if(pTime != null)
                {
                    DateTime passTime = Convert.ToDateTime(pTime);
                    if (passTime.Minute >= 45)
                        baseline = (decimal)passTime.Hour + ((decimal).45);
                    else
                        baseline = (decimal)passTime.Hour + ((decimal).15);
                }
                decimal timenow = (decimal)DateTime.Now.Hour + ((decimal)DateTime.Now.Minute / 100);
                do
                {
                    if (Decimal.Compare(baseline, timenow) <= 0)
                    {
                        expected++;
                        if (baseline.ToString().Contains(".45"))
                            baseline = baseline + (decimal).70;
                        else
                            baseline = baseline + (decimal).30;
                    }
                } while (Decimal.Compare(baseline, timenow) <= 0);
            }
            catch (Exception ex)
            {
                Console.WriteLine("EXCEPTION in 'ExpectedCandleCount' with {0}", ex.Message);
            }
            return expected;
        }

        public void CalculateDayBB()
        {
            try
            {
                ClearTheTable();
                DateTime previousDay;
                DateTime currentDay;
                getDays(out previousDay, out currentDay);
                if (DateTime.Now.Hour >= 14)
                    currentDay = currentDay.AddHours(15).AddMinutes(30); // Debug

                List<Instrument> calcInstruments = kite.GetInstruments("NFO");
                if (month == 0)
                {
                    CalculateExpiry(calcInstruments, DateTime.Now.Month, 0, "NFO-FUT");
                }
                foreach (Instrument scripts in calcInstruments)
                {
                    //if (!scripts.TradingSymbol.Contains("ADANIPORTS")
                    //    || !(scripts.Segment == "NFO-FUT"))
                    //    continue;

                    if (scripts.Segment == "NFO-FUT"
                        && scripts.InstrumentToken.ToString().Length > 3
                        && (scripts.LotSize < 5200
                             && scripts.LotSize >= 700))
                    {
                        CheckDayTrendOf(scripts, previousDay, currentDay);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("EXCEPTION While getting All NFO Scripts recieved -> {0}", ex.Message);
            }
        }

        public OType CheckDayTrendOf(Instrument scripts, DateTime previousDay, DateTime currentDay)
        {
            try
            {
                DateTime expiry = (DateTime)scripts.Expiry;
                if (expiry.Month == month) //Convert.ToInt16(ConfigurationManager.AppSettings["month"]))
                {
                    string equitySymbol = scripts.TradingSymbol.Replace(futMonth, ""); //ConfigurationManager.AppSettings["expiry"].ToString(), "");
                                                                                       //Dictionary<string, Quote> quotes = new Dictionary<string, Quote>();
                    Dictionary<string, OHLC> quotes = new Dictionary<string, OHLC>();
                    Dictionary<string, Quote> quotesFut = new Dictionary<string, Quote>();
                    List<Historical> dayHistory = new List<Historical>();
                    List<Historical> minHistory = new List<Historical>();
                    decimal lastClose = 0;
                    string instrumentToken = string.Empty;
                    try
                    {
                        quotes = kite.GetOHLC(InstrumentId: new string[] { "NSE:" + equitySymbol });
                        instrumentToken = quotes["NSE:" + equitySymbol].InstrumentToken.ToString();
                        quotesFut = kite.GetQuote(new string[] { scripts.InstrumentToken.ToString() });//kite.GetQuote(InstrumentId: new string[] { "NSE:" + scripts.TradingSymbol });
                    }
                    catch (System.TimeoutException)
                    {
                        quotes = kite.GetOHLC(InstrumentId: new string[] { "NSE:" + equitySymbol });
                    }
                    if (quotes.Count > 0)
                    {
                        System.Threading.Thread.Sleep(400);
                        try
                        {
                            dayHistory = kite.GetHistoricalData(instrumentToken,
                                previousDay.AddDays(-85), currentDay, "day");
                            minHistory = kite.GetHistoricalData(instrumentToken,
                                previousDay.AddDays(-4), currentDay, "30minute");
                            if (dayHistory.Count < 30)
                            {
                                Console.WriteLine("This Script {0} has very less history candles count {1}",
                                    instrumentToken, dayHistory.Count);
                                return OType.BS;
                            }
                        }
                        catch (System.TimeoutException)
                        {
                            return OType.BS;
                        }
                        lastClose = dayHistory[dayHistory.Count - 1].Close;
                        decimal variance2 = Math.Round((lastClose * (decimal)2.3) / 100, 1);
                        if ((lastClose < 1500 && lastClose > 80)
                            || equitySymbol.Contains("NIFTY"))
                        {
                            List<decimal> today = GetMiddle30BBOf(dayHistory, 0);
                            List<decimal> todaym1 = GetMiddle30BBOf(dayHistory, 1);
                            List<decimal> todaym2 = GetMiddle30BBOf(dayHistory, 2);
                            List<decimal> todaym3 = GetMiddle30BBOf(dayHistory, 3);

                            List<decimal> lastCandle = GetMiddle30BBOf(minHistory, 0);
                            List<decimal> lastCandlem1 = GetMiddle30BBOf(minHistory, 1);
                            List<decimal> lastCandlem2 = GetMiddle30BBOf(minHistory, 2);
                            bool gearingStatus = false;

                            if (equitySymbol.Contains("TORNTPOWER"))
                                Console.WriteLine("Break Point Check for debug purpose; Continue");

                            int baseline = 0;
                            int baseline2 = 0;
                            if (dayHistory[dayHistory.Count - 2].Close > today[0])
                            {
                                baseline = dayHistory[dayHistory.Count - 1].High >= today[1]
                                    || (IsBetweenVariance(dayHistory[dayHistory.Count - 1].High, today[1],
                                            (decimal).004)
                                        && IsBeyondVariance(dayHistory[dayHistory.Count - 1].Low, today[2],
                                            (decimal).02)) ? baseline + 1 : baseline;
                                baseline = dayHistory[dayHistory.Count - 2].High >= todaym1[1]
                                           || (IsBetweenVariance(dayHistory[dayHistory.Count - 2].High, todaym1[1],
                                                   (decimal).004)
                                               && IsBeyondVariance(dayHistory[dayHistory.Count - 2].Low, todaym1[2],
                                                   (decimal).02)) ? baseline + 1 : baseline;
                                baseline = dayHistory[dayHistory.Count - 3].High >= todaym2[1]
                                           || (IsBetweenVariance(dayHistory[dayHistory.Count - 3].High, todaym2[1],
                                                   (decimal).004)
                                               && IsBeyondVariance(dayHistory[dayHistory.Count - 3].Low, todaym2[2],
                                                   (decimal).02)) ? baseline + 1 : baseline;
                                baseline = dayHistory[dayHistory.Count - 4].High >= todaym3[1]
                                           || (IsBetweenVariance(dayHistory[dayHistory.Count - 4].High, todaym3[1],
                                                   (decimal).015)
                                               && IsBeyondVariance(dayHistory[dayHistory.Count - 4].Low, todaym3[2],
                                                   (decimal).02)) ? baseline + 1 : baseline;
                                if (baseline >= 1)
                                {
                                    gearingStatus = CheckGearingStatus(dayHistory, OType.Buy);
                                }
                            }
                            else if (dayHistory[dayHistory.Count - 2].Close < today[0])
                            {
                                baseline = dayHistory[dayHistory.Count - 1].Low <= today[2]
                                    || (IsBetweenVariance(dayHistory[dayHistory.Count - 1].Low, today[2],
                                            (decimal).004)
                                        && IsBeyondVariance(dayHistory[dayHistory.Count - 1].High, today[1],
                                            (decimal).02)) ? baseline + 1 : baseline;
                                baseline = dayHistory[dayHistory.Count - 2].Low <= todaym1[2]
                                           || (IsBetweenVariance(dayHistory[dayHistory.Count - 2].Low, todaym1[2],
                                                   (decimal).004)
                                               && IsBeyondVariance(dayHistory[dayHistory.Count - 2].High, todaym1[1],
                                                   (decimal).02)) ? baseline + 1 : baseline;
                                baseline = dayHistory[dayHistory.Count - 3].Low <= todaym2[2]
                                           || (IsBetweenVariance(dayHistory[dayHistory.Count - 3].Low, todaym2[2],
                                                   (decimal).004)
                                               && IsBeyondVariance(dayHistory[dayHistory.Count - 3].High, todaym2[1],
                                                   (decimal).02)) ? baseline + 1 : baseline;
                                baseline = dayHistory[dayHistory.Count - 4].Low <= todaym3[2]
                                           || (IsBetweenVariance(dayHistory[dayHistory.Count - 4].Low, todaym3[2],
                                                   (decimal).02)
                                               && IsBeyondVariance(dayHistory[dayHistory.Count - 4].High, todaym3[1],
                                                   (decimal).02)) ? baseline + 1 : baseline;
                                if (baseline >= 1)
                                {
                                    gearingStatus = CheckGearingStatus(dayHistory, OType.Sell);
                                }
                            }
                            bool cont = false;
                            if (dayHistory[dayHistory.Count - 2].Close > today[0])
                            {
                                if (dayHistory[dayHistory.Count - 1].High >= today[1])
                                {
                                    cont = minHistory[minHistory.Count - 1].Close > lastCandle[0]
                                            && minHistory[minHistory.Count - 2].Close > lastCandlem1[0];
                                }
                            }
                            else if (dayHistory[dayHistory.Count - 2].Close < today[0])
                            {
                                if (dayHistory[dayHistory.Count - 1].Low <= today[2])
                                {
                                    cont = minHistory[minHistory.Count - 1].Close < lastCandle[0]
                                            && minHistory[minHistory.Count - 2].Close < lastCandlem1[0];
                                }
                            }
                            if (baseline >= 3
                                    && (baseline2 >= 2
                                        || (today[1] - today[2] > (lastClose * (decimal).05)
                                            && cont))
                                && lastCandle[1] - lastCandle[2] > variance2)
                            {
                                Int64 avgVolume = GetAvgVolume(dayHistory);
                                if (dayHistory[dayHistory.Count - 2].Close > today[0])
                                {
                                    InsertNewToken(scripts.InstrumentToken, scripts.TradingSymbol, lastClose, lastClose - dayHistory[dayHistory.Count - 1].Close, dayHistory[dayHistory.Count - 1].Close, "BUY", "OPEN", OType.Buy);
                                    Console.WriteLine("BTST : Script {0} is qualified for order with averageVolume {1}, days volume {2} with OI {3} as Margin required {4}", scripts.TradingSymbol, avgVolume, dayHistory[dayHistory.Count - 1].Volume, quotesFut[scripts.InstrumentToken.ToString()].OI.ToString(), GetMISMargin(scripts.TradingSymbol, OType.Buy, lastClose, (int)scripts.LotSize));
                                    return OType.Buy;
                                }
                                else if (dayHistory[dayHistory.Count - 2].Close < today[0])
                                {
                                    InsertNewToken(scripts.InstrumentToken, scripts.TradingSymbol, lastClose, lastClose - dayHistory[dayHistory.Count - 1].Close, dayHistory[dayHistory.Count - 1].Close, "SELL", "OPEN", OType.Sell);
                                    Console.WriteLine("STBT : Script {0} is qualified for order with averageVolume {1}, days volume {2} with OI {3} as Margin required {4}", scripts.TradingSymbol, avgVolume, dayHistory[dayHistory.Count - 1].Volume, quotesFut[scripts.InstrumentToken.ToString()].OI.ToString(), GetMISMargin(scripts.TradingSymbol, OType.Sell, lastClose, (int)scripts.LotSize));
                                    return OType.Sell;
                                }
                            }
                            else if (baseline >= 3)
                            {
                                Int64 avgVolume = GetAvgVolume(dayHistory);
                                if (minHistory[minHistory.Count - 1].Close > lastCandle[0]
                                    && dayHistory[dayHistory.Count - 1].Close > today[0]
                                    && (lastCandle[1] - lastCandle[2] > variance2
                                        || baseline2 >= 2))
                                {
                                    if (lastCandle[1] - lastCandle[2] <= variance2)
                                    {
                                        InsertNewToken(scripts.InstrumentToken, scripts.TradingSymbol, lastClose, lastClose - dayHistory[dayHistory.Count - 1].Close, dayHistory[dayHistory.Count - 1].Close, "SELL", "EOR", OType.Sell);
                                        Console.WriteLine("WATCH STBT : Script {0} is qualified for order with averageVolume {1}, days volume {2} with OI {3} as Margin required {4}", scripts.TradingSymbol, avgVolume, dayHistory[dayHistory.Count - 1].Volume, quotesFut[scripts.InstrumentToken.ToString()].OI.ToString(), GetMISMargin(scripts.TradingSymbol, OType.Buy, lastClose, (int)scripts.LotSize));
                                    }
                                    else
                                    {
                                        InsertNewToken(scripts.InstrumentToken, scripts.TradingSymbol, lastClose, lastClose - dayHistory[dayHistory.Count - 1].Close, dayHistory[dayHistory.Count - 1].Close, "BUY", "WATCH", OType.Buy);
                                        Console.WriteLine("WATCH BTST : Script {0} is qualified for order with averageVolume {1}, days volume {2} with OI {3} as Margin required {4}", scripts.TradingSymbol, avgVolume, dayHistory[dayHistory.Count - 1].Volume, quotesFut[scripts.InstrumentToken.ToString()].OI.ToString(), GetMISMargin(scripts.TradingSymbol, OType.Buy, lastClose, (int)scripts.LotSize));
                                    }
                                }
                                else if (minHistory[minHistory.Count - 1].Close < lastCandle[0]
                                    && dayHistory[dayHistory.Count - 1].Close < today[0]
                                    && (lastCandle[1] - lastCandle[2] > variance2
                                        || baseline2 >= 2))
                                {
                                    if (lastCandle[1] - lastCandle[2] <= variance2)
                                    {
                                        InsertNewToken(scripts.InstrumentToken, scripts.TradingSymbol, lastClose, lastClose - dayHistory[dayHistory.Count - 1].Close, dayHistory[dayHistory.Count - 1].Close, "BUY", "EOR", OType.Buy);
                                        Console.WriteLine("WATCH BTST : Script {0} is qualified for order with averageVolume {1}, days volume {2} with OI {3} as Margin required {4}", scripts.TradingSymbol, avgVolume, dayHistory[dayHistory.Count - 1].Volume, quotesFut[scripts.InstrumentToken.ToString()].OI.ToString(), GetMISMargin(scripts.TradingSymbol, OType.Buy, lastClose, (int)scripts.LotSize));
                                    }
                                    else
                                    {
                                        InsertNewToken(scripts.InstrumentToken, scripts.TradingSymbol, lastClose, lastClose - dayHistory[dayHistory.Count - 1].Close, dayHistory[dayHistory.Count - 1].Close, "SELL", "WATCH", OType.Sell);
                                        Console.WriteLine("WATCH STBT : Script {0} is qualified for order with averageVolume {1}, days volume {2} with OI {3} as Margin required {4}", scripts.TradingSymbol, avgVolume, dayHistory[dayHistory.Count - 1].Volume, quotesFut[scripts.InstrumentToken.ToString()].OI.ToString(), GetMISMargin(scripts.TradingSymbol, OType.Sell, lastClose, (int)scripts.LotSize));
                                    }
                                }
                                else if (minHistory[minHistory.Count - 1].Close < lastCandle[0]
                                    && dayHistory[dayHistory.Count - 1].Close < today[0])
                                {
                                    InsertNewToken(scripts.InstrumentToken, scripts.TradingSymbol, lastClose, lastClose - dayHistory[dayHistory.Count - 1].Close, dayHistory[dayHistory.Count - 1].Close, "BUY", "IGNORE", OType.Buy);
                                    Console.WriteLine("WATCH BTST : Script {0} is qualified for order with averageVolume {1}, days volume {2} with OI {3} as Margin required {4}", scripts.TradingSymbol, avgVolume, dayHistory[dayHistory.Count - 1].Volume, quotesFut[scripts.InstrumentToken.ToString()].OI.ToString(), GetMISMargin(scripts.TradingSymbol, OType.Buy, lastClose, (int)scripts.LotSize));
                                }
                                else if (minHistory[minHistory.Count - 1].Close < lastCandle[0]
                                    && dayHistory[dayHistory.Count - 1].Close < today[0])
                                {
                                    InsertNewToken(scripts.InstrumentToken, scripts.TradingSymbol, lastClose, lastClose - dayHistory[dayHistory.Count - 1].Close, dayHistory[dayHistory.Count - 1].Close, "SELL", "IGNORE", OType.Sell);
                                    Console.WriteLine("WATCH STBT : Script {0} is qualified for order with averageVolume {1}, days volume {2} with OI {3} as Margin required {4}", scripts.TradingSymbol, avgVolume, dayHistory[dayHistory.Count - 1].Volume, quotesFut[scripts.InstrumentToken.ToString()].OI.ToString(), GetMISMargin(scripts.TradingSymbol, OType.Sell, lastClose, (int)scripts.LotSize));
                                }
                            }
                            else if (baseline >=1 && gearingStatus && lastCandle[1] - lastCandle[2] > variance2)
                            {
                                Int64 avgVolume = GetAvgVolume(dayHistory);
                                if (dayHistory[dayHistory.Count - 2].Close > today[0])
                                {
                                    gearingStatus = CheckGearingStatus(minHistory, OType.Sell);
                                    if (!gearingStatus)
                                    {
                                        InsertNewToken(scripts.InstrumentToken, scripts.TradingSymbol, lastClose, lastClose - dayHistory[dayHistory.Count - 1].Close, dayHistory[dayHistory.Count - 1].Close, "BUY", "OPEN", OType.Buy);
                                        Console.WriteLine("BTST : Script {0} is qualified for order with averageVolume {1}, days volume {2} with OI {3} as Margin required {4}", scripts.TradingSymbol, avgVolume, dayHistory[dayHistory.Count - 1].Volume, quotesFut[scripts.InstrumentToken.ToString()].OI.ToString(), GetMISMargin(scripts.TradingSymbol, OType.Buy, lastClose, (int)scripts.LotSize));
                                        return OType.Buy;
                                    }
                                }
                                else if (dayHistory[dayHistory.Count - 2].Close < today[0])
                                {
                                    gearingStatus = CheckGearingStatus(minHistory, OType.Buy);
                                    if (!gearingStatus)
                                    {
                                        InsertNewToken(scripts.InstrumentToken, scripts.TradingSymbol, lastClose, lastClose - dayHistory[dayHistory.Count - 1].Close, dayHistory[dayHistory.Count - 1].Close, "SELL", "OPEN", OType.Sell);
                                        Console.WriteLine("STBT : Script {0} is qualified for order with averageVolume {1}, days volume {2} with OI {3} as Margin required {4}", scripts.TradingSymbol, avgVolume, dayHistory[dayHistory.Count - 1].Volume, quotesFut[scripts.InstrumentToken.ToString()].OI.ToString(), GetMISMargin(scripts.TradingSymbol, OType.Buy, lastClose, (int)scripts.LotSize));
                                        return OType.Sell;
                                    }
                                }
                            }
                        }
                    }
                    else
                        Console.WriteLine("Catch It " + equitySymbol);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("EXCEPTION While Calculating NFO Script {0} recieved -> {1}", scripts.TradingSymbol, ex.Message);
            }
            return OType.BS;
        }

        public bool CheckGearingStatus(List<Historical> dayHistory, OType type)
        {
            bool flag = false;
            int last12 = 0;
            int buyside = 0;
            int sellside = 0;
            if (dayHistory.Count <= 12)
                return flag;
            int i = 1;
            for (; i < dayHistory.Count && i <= 12; i++)
            {
                List<decimal> bols = GetMiddle30BBOf(dayHistory, i);
                if (dayHistory[dayHistory.Count - i].Close < bols[0])
                {
                    if (i <= 13 && type == OType.Sell)
                    {
                        last12++;
                    }
                    if (type == OType.Sell)
                    {
                        sellside++;
                    }
                }
                else if (dayHistory[dayHistory.Count - i].Close > bols[0])
                {
                    if (i <= 13 && type == OType.Buy)
                    {
                        last12++;
                    }
                    if (type == OType.Buy)
                    {
                        buyside++;
                    }
                }
                else
                {
                    if (type == OType.Buy)
                    {
                        buyside++;
                    }
                    else if (type == OType.Sell)
                    {
                        sellside++;
                    }
                }
            }
            if (last12 >= 10)
            {
                if (type == OType.Buy)
                {
                    if (//buyside + 4 > i - sellside && 
                        buyside >= 10)
                    {
                        flag = true;
                    }
                }
                else
                {
                    if (//sellside + 4 > i - buyside && 
                        sellside >= 10)
                    {
                        flag = true;
                    }
                }
            }
            return flag;
        }
    }
}
