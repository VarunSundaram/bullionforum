using System;
using System.Collections.Generic;
using System.Configuration;
using System.IO;

using KiteConnect;
using SeleniumLibrary;

using Amazon;
using Amazon.S3;
using Amazon.S3.Model;

namespace TradingService
{
    public class BollingerTracker
    {
        #region Basic Functions and Parameters
        string myApiKey;
        string mySecretKey;
        public bool isClose = false;
        public Ticker ticker;
        string myAccessToken;
        //string myPublicToken;
        public LogType logType;
        LifetimeInfiniteThread thread;
        LifetimeInfiniteThread thread30Min;
        LifetimeInfiniteThread thread30Min4Candles;

        Dictionary<uint, WatchList> instruments;
        Kite kite;

        private void NewKiteSession()
        {
            kite = new Kite(myApiKey, Debug: true);
            string url = kite.GetLoginURL();

            string requestToken;
            string username = ConfigurationManager.AppSettings["UserID"];
            string password = ConfigurationManager.AppSettings["Password"];
            string TwoFA = ConfigurationManager.AppSettings["2FA"];
            string ltype = ConfigurationManager.AppSettings["INFO"];

            SeleniumManager.Current.LaunchBrowser(url, BrowserType.Chrome, "", true);
            SeleniumManager.Current.ActiveBrowser.WaitUntilReady();
            KiteInterface.GetInstance.Login(username, password, TwoFA);

            requestToken = KiteInterface.GetInstance.GetRequestToken();
            User user = kite.GenerateSession(requestToken, mySecretKey);

            kite.SetAccessToken(user.AccessToken);
            myAccessToken = user.AccessToken;
            //myPublicToken = user.PublicToken;
            switch (ltype)
            {
                case "INFO":
                default:
                    logType = LogType.INFO;
                    break;
                case "FORCE":
                    logType = LogType.FORCE;
                    break;
                case "ERROR":
                    logType = LogType.ERROR;
                    break;
            }
        }

        public BollingerTracker()
        {
            //Do Nothing
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

        public void InitiateWatchList(Dictionary<uint, WatchList> tempList)
        {
            instruments = tempList;
            Dictionary<UInt32, WatchList>.KeyCollection keys = instruments.Keys;
            foreach (uint token in keys)
            {
                try
                {
                    CalculatePivots(token);
                }
                catch (Exception ex)
                {
                    Console.WriteLine("EXCEPTION while INITIALISATION :: {0} with status code ", ex.Message, ex.StackTrace);
                }
            }
            Console.WriteLine("Calculating Pivot Points is Completed for given Tokens");
        }

        public void CalculatePivots(uint instrument)
        {
            DateTime previousDay = DateTime.Now.Date.AddDays(-4);
            DateTime currentDay = DateTime.Now.Date;
            List<Historical> history;
            try
            {
                System.Threading.Thread.Sleep(400);
                history = kite.GetHistoricalData(instrument.ToString(),
                                previousDay, currentDay, "day");
            }
            catch (Exception ex)
            {
                if (ex.Message.Contains("Invalid JSON primitive")
                    || ex.Message.Contains("Too Many Requests"))
                {
                    Console.WriteLine("EXCEPTION while INITIALISATION-PivotPoints :: {0} with status code ", ex.Message, ex.StackTrace);
                    System.Threading.Thread.Sleep(1000);
                    history = kite.GetHistoricalData(instrument.ToString(),
                                    previousDay, currentDay.AddDays(1), "3minute");
                }
                else
                    throw ex;
            }
            decimal high = history[history.Count - 1].High;
            decimal low = history[history.Count - 1].Low;
            decimal close = history[history.Count - 1].Close;
            decimal black;

            black = (high + low + close) / 3;
            instruments[instrument].dma = black;
            instruments[instrument].res1 = (2 * black) - low;
            instruments[instrument].res2 = black + (high - low);
            instruments[instrument].sup1 = (2 * black) - high;
            instruments[instrument].sup2 = black - (high - low);
        }

        public BollingerTracker(string apiKey, string secretKey, string accessToken, Kite kt)
        {
            myApiKey = apiKey;
            mySecretKey = secretKey;
            myAccessToken = accessToken;
            kite = kt;
        }

        public void initTicker(List<UInt32> ptoken)
        {
            isClose = false;
            ticker = new Ticker(myApiKey, myAccessToken);

            ticker.OnTick += OnTick;
            ticker.OnReconnect += OnReconnect;
            ticker.OnNoReconnect += OnNoReconnect;
            ticker.OnError += OnError;
            ticker.OnClose += OnClose;
            ticker.OnConnect += OnConnect;
            ticker.OnOrderUpdate += OnOrderUpdate;

            try
            {
                ticker.EnableReconnect(Interval: 5, Retries: 50);
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
            thread = new LifetimeInfiniteThread(300, On5minTick, handleTimerException);
            VerifyOpenAlign();
            On5minTick();
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
            System.Threading.Thread.Sleep(5000);
            thread30Min = new LifetimeInfiniteThread(1800, VerifyOpenPositions, handlePositionException);
            thread30Min.Start();
            System.Threading.Thread.Sleep(10000);
            thread30Min4Candles = new LifetimeInfiniteThread(1800, VerifyCandleClose, handleCandleCheckException);
            thread30Min4Candles.Start();
        }

        void handleTimerException(Exception ex)
        {
            Console.WriteLine("EXCEPTIO CAUGHT in TIMER thread" + ex.Message);
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
            if (SeleniumManager.Current.ActiveBrowser != null)
                SeleniumManager.Current.ActiveBrowser.CloseDriver();
            decimal timenow = (decimal)DateTime.Now.Hour + ((decimal)DateTime.Now.Minute / 100);
            decimal startTime = Convert.ToDecimal(ConfigurationManager.AppSettings["ServiceStartTime"]);
            decimal stopTime = Convert.ToDecimal(ConfigurationManager.AppSettings["ServiceStopTime"]);
            if (Decimal.Compare(timenow, startTime) > 0 || Decimal.Compare(timenow, stopTime) < 0)
            {
                NewKiteSession();
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
                if (SeleniumManager.Current.ActiveBrowser != null)
                    SeleniumManager.Current.ActiveBrowser.CloseDriver();
                decimal timenow = (decimal)DateTime.Now.Hour + ((decimal)DateTime.Now.Minute / 100);
                decimal startTime = Convert.ToDecimal(ConfigurationManager.AppSettings["ServiceStartTime"]);
                decimal stopTime = Convert.ToDecimal(ConfigurationManager.AppSettings["ServiceStopTime"]);
                if (Decimal.Compare(timenow, startTime) > 0 || Decimal.Compare(timenow, stopTime) < 0)
                {
                    NewKiteSession();
                    Console.WriteLine("Reconnecting new session at {0}", DateTime.Now.ToString());
                }
                else
                    Console.WriteLine("Market Session Time is closed now {0}", DateTime.Now.ToString());
            }
        }
        #endregion

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
                            && !instruments[token].isOpenAlign)
                {
                    instruments[token].isOpenAlign = true;
                    instruments[token].isOpenAlignFatal = true;
                    modifyOpenAlignOrReversedStatus(instruments[token], 16, OType.Sell, true);
                    //Console.WriteLine("Time Stamp {0} Open is aligning with SELL order and lookout for reverse as well for Script {1}; middle30BB = {2} & Open = {3}", DateTime.Now.ToString("yyyyMMdd hh:mm:ss"), instruments[token].futName, instruments[token].middle30BB, history[0].Open);
                    continue;
                }
                else if (instruments[token].type == OType.Buy && !instruments[token].isReversed
                    && history[0].Open > instruments[token].middle30BB
                    && IsBeyondVariance(history[0].Open, instruments[token].middle30BB, (decimal).0005)
                    && history[0].Open > instruments[token].weekMA
                    && !instruments[token].isOpenAlign)
                {
                    instruments[token].isOpenAlign = true;
                    instruments[token].isOpenAlignFatal = true;
                    modifyOpenAlignOrReversedStatus(instruments[token], 16, OType.Buy, true);
                    //Console.WriteLine("Time Stamp {0} Open is aligning with BUY order and lookout for reverse as well for Script {1}; middle30BB = {2} & Open = {3}", DateTime.Now.ToString("yyyyMMdd hh:mm:ss"), instruments[token].futName, instruments[token].middle30BB, history[0].Open);
                }
                else if (instruments[token].type == OType.Sell && !instruments[token].isReversed
                            && history[0].Open > instruments[token].middle30BB
                            && history[0].Open > instruments[token].weekMA
                            && IsBeyondVariance(history[0].Open, instruments[token].middle30BB, (decimal).0005)
                            && (!(IsBetweenVariance(history[0].Open, history[0].High, (decimal).0006))
                                || (history[0].Open != history[0].High && history[0].Open < instruments[token].top30bb))
                            && !instruments[token].isOpenAlign)
                {
                    instruments[token].isOpenAlign = true;
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
                            && !instruments[token].isOpenAlign)
                {
                    instruments[token].isOpenAlign = true;
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
                        if (!instruments[token].isOpenAlign)
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
                            if (instruments[token].lotSize < 900)
                            {
                                CloseOrderTicker(token);
                            }
                            else
                            {
                                instruments[token].canTrust = false;
                                Console.WriteLine("Time Stamp {0} Open is Not aligning hence marking it as cannot-be-trusted Script {1}", DateTime.Now.ToString("yyyyMMdd hh:mm:ss"), instruments[token].futName);
                            }
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
            Dictionary<UInt32, WatchList>.KeyCollection keys = instruments.Keys;
            int counter;
            int index;

            DateTime previousDay;
            DateTime currentDay;
            decimal topBB;
            decimal botBB;
            decimal middleBB;
            decimal ma50;
            decimal middle30BB;
            decimal validation0BB;
            decimal dayma50;
            decimal l14, h14, close;
            int k = 0;
            getDays(out previousDay, out currentDay);

            foreach (uint token in keys)
            {
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
                        System.Threading.Thread.Sleep(400);
                        history = kite.GetHistoricalData(token.ToString(),
                                        previousDay,
                                        //currentDay.AddHours(13).AddMinutes(50),
                                        currentDay.AddDays(1),
                                        "5minute");
                    }
                    catch (System.TimeoutException)
                    {
                        System.Threading.Thread.Sleep(1000);
                        history = kite.GetHistoricalData(token.ToString(),
                                        previousDay,
                                        currentDay.AddDays(1),
                                        "5minute");
                    }
                    catch (Exception ex)
                    {
                        if (ex.Message.Contains("Invalid JSON primitive"))
                        {
                            System.Threading.Thread.Sleep(1000);
                            history = kite.GetHistoricalData(token.ToString(),
                                            previousDay,
                                            currentDay.AddDays(1), "5minute");
                        }
                        else
                            throw ex;
                    }
                    if (history.Count >= 50)
                    {
                        for (counter = history.Count - 1; counter > 0; counter--)
                        {
                            middleBB = middleBB + history[counter].Close;
                            index++;
                            if (index == 20)
                                break;
                        }
                        middleBB = Math.Round(middleBB / index, 2);
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
                        for (counter = history.Count - 1; counter > 0; counter--)
                        {
                            sd = (middleBB - history[counter].Close) * (middleBB - history[counter].Close) + sd;
                            index++;
                            if (index == 20)
                                break;
                        }
                        sd = Math.Round((decimal)Math.Sqrt((double)(sd / (index))), 2) * 2;
                        topBB = middleBB + sd;
                        botBB = middleBB - sd;
                        index = 0;
                        close = history[history.Count - 1].Close;
                        l14 = history[history.Count - 1].Low;
                        h14 = history[history.Count - 1].High;
                        for (counter = history.Count - 2; counter > 0; counter--)
                        {
                            if (l14 > history[counter].Low)
                                l14 = history[counter].Low;
                            if (h14 < history[counter].High)
                                h14 = history[counter].High;
                            index++;
                            if (index >= 13)
                                break;
                        }
                        k = (int)(((close - l14) / (h14 - l14)) * 100);
                    }
                    instruments[token].history = history;
                    decimal range = Convert.ToDecimal(((middleBB * (decimal).7) / 100).ToString("#.#"));
                    decimal minRange = Convert.ToDecimal(((middleBB * (decimal).3) / 100).ToString("#.#"));
                    switch (instruments[token].type)
                    {
                        case OType.Buy:
                            if ((topBB - botBB) > minRange && (topBB - botBB) < range
                                && (history[counter].Close >= middleBB
                                || history[counter].High > middleBB))
                            {
                                instruments[token].movement++;
                            }
                            else
                                instruments[token].movement = 0;
                            break;
                        case OType.Sell:
                            if ((topBB - botBB) > minRange && (topBB - botBB) < range
                                && (history[counter].Close <= middleBB
                                || history[counter].Low < middleBB))
                            {
                                instruments[token].movement++;
                            }
                            else
                                instruments[token].movement = 0;
                            break;
                    }
                    index = 0;
                    counter = 0;
                    middle30BB = 0;
                    validation0BB = 0;
                    try
                    {
                        System.Threading.Thread.Sleep(400);
                        history = kite.GetHistoricalData(token.ToString(),
                            previousDay.AddDays(-4),
                            //currentDay.AddHours(13).AddMinutes(50),
                            currentDay.AddDays(1),
                            "30minute");
                        while (history.Count < 50)
                        {
                            System.Threading.Thread.Sleep(1000);
                            previousDay = previousDay.AddDays(-1);
                            history = kite.GetHistoricalData(token.ToString(),
                                previousDay.AddDays(-4),
                                //currentDay.AddHours(11).AddMinutes(46),
                                currentDay.AddDays(1),
                                "30minute");
                            //Console.WriteLine("History Candles are lesser than Expceted candles. Please Check the given dates. PreviousDate {0} CurrentDate {1} with latest candles count {2}", previousDay.AddDays(-5), currentDay, history.Count);
                            //return;
                        }
                    }
                    catch (TimeoutException)
                    {
                        Console.WriteLine("TIME OUT EXCEPTION. Please Check the given dates. PreviousDate {0} CurrentDate {1}", previousDay, currentDay);
                        continue;
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
                                validation0BB = validation0BB + history[counter].Close;
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
                                middle30BB = middle30BB + history[counter].Close;
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
                                dayma50 = dayma50 + history[counter].Close;
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

                        instruments[token].topBB = topBB;
                        instruments[token].botBB = botBB;
                        instruments[token].middleBB = middleBB;
                        instruments[token].ma50 = ma50;
                        instruments[token].top30bb = validation0BB + sd;
                        instruments[token].middle30BB = validation0BB;
                        instruments[token].middle30BBnew = middle30BB;
                        instruments[token].bot30bb = validation0BB - sd;
                        instruments[token].middle30ma50 = dayma50;
                        instruments[token].stochistic = k;
                        instruments[token].currentTime = DateTime.Now;
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
                                instruments[token].shortTrigger = middle30BB + sd;
                        }
                        else if (instruments[token].type == OType.Sell && !instruments[token].isReversed) // && instruments[token].status == Status.OPEN)
                        {
                            instruments[token].longTrigger = middle30BB - sd;
                            if (instruments[token].weekMA <= (middle30BB + sd))
                                instruments[token].shortTrigger = (middle30BB + sd);
                            else
                                instruments[token].shortTrigger = instruments[token].weekMA;
                            if (!instruments[token].canTrust)
                                instruments[token].shortTrigger = middle30BB - sd;
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
                        if ((topBB - botBB) > minRange && (topBB - botBB) < range
                            && IsBeyondVariance(instruments[token].middleBB, instruments[token].ma50, (decimal).0015)
                            && IsBetweenVariance(instruments[token].middleBB, instruments[token].ma50, (decimal).006))
                        {
                            //instruments[token].movement++;
                        }
                        else
                        {
                            //instruments[token].movement = 0;
                        }
                        //Console.WriteLine("Calculation Completed for {0}: Botbb {1}; Topbb {2}; MiddleBB {3}; Ma50 {4}; Top30BB {5}; Bot30BB {6}; Middle30BB {7}; stochistic {8}"
                        //    , instruments[token].futName, instruments[token].botBB, instruments[token].topBB, instruments[token].middleBB, instruments[token].ma50, instruments[token].top30bb, instruments[token].bot30bb, instruments[token].middle30BB, instruments[token].stochistic);

                    }
                    //Console.WriteLine("Calculation Completed at Time Ticker {0}:", DateTime.Now.ToString());
                }
                catch (Exception ex)
                {
                    Console.WriteLine("EXCEPTION:: '3minute Ticker Event' {0} for Script Name {1} at {2}", ex.Message, instruments[token].futName, DateTime.Now.ToString("yyyyMMdd hh:mm:ss"));
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
        }

        private void OnTick(Tick tickData)
        {
            decimal serviceStopTime = Convert.ToDecimal(ConfigurationManager.AppSettings["ServiceStopTime"]);
            decimal timenow = (decimal)DateTime.Now.Hour + ((decimal)DateTime.Now.Minute / 100);
            uint instrument = tickData.InstrumentToken;
            decimal ltp = tickData.LastPrice;
            if ((Decimal.Compare(timenow, serviceStopTime) < 0 || Decimal.Compare(timenow, (decimal)9.14) > 0)
                && instruments[instrument].status != Status.CLOSE)
            {
                bool noOpenOrder = true;
                //if (instruments[instrument].status != Status.OPEN)
                //    return;
                if (VerifyLtp(tickData))
                {
                    #region Return if Bolinger is narrowed or expanded
                    if (!instruments[instrument].isReversed)
                    {
                        decimal variance14 = (ltp * (decimal)1.4) / 100;
                        if ((instruments[instrument].bot30bb + variance14) > instruments[instrument].top30bb)
                        {
                            Console.WriteLine("Current Time is {0} and Closing Script {1} as the script has Narrowed so much and making it riskier where {2} > {3}",
                                DateTime.Now.ToString(),
                                instruments[instrument].futName,
                                instruments[instrument].bot30bb + variance14,
                                instruments[instrument].top30bb);

                            if (!instruments[instrument].canTrust)
                            {
                                if ((instruments[instrument].type == OType.Sell
                                        && instruments[instrument].middle30ma50 > instruments[instrument].bot30bb)
                                    || (instruments[instrument].type == OType.Buy
                                        && instruments[instrument].middle30ma50 < instruments[instrument].top30bb))
                                {
                                    CloseOrderTicker(instrument);
                                }
                            }
                            return;
                        }

                        decimal variance46 = (ltp * (decimal)4.6) / 100;
                        if ((instruments[instrument].bot30bb + variance46) < instruments[instrument].top30bb
                            && Decimal.Compare(timenow, Convert.ToDecimal(9.45)) > 0)
                        {
                            Console.WriteLine("Current Time is {0} and Closing Script {1} as the script has Expanded so much and making it riskier where {2} < {3}",
                                DateTime.Now.ToString(),
                                instruments[instrument].futName,
                                instruments[instrument].bot30bb + variance46,
                                instruments[instrument].top30bb);

                            /*if (!instruments[instrument].canTrust)
                            {
                                CloseOrderTicker(instrument);
                            }*/
                            return;
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
                                        CloseOrderTicker(instrument);
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
                                    CloseOrderTicker(instrument);
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
                                        CloseOrderTicker(instrument);
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
                            Console.WriteLine("Time {0} Placing BUY Order of Instrument {1} for LTP {2} as it match long trigger {3} with top30BB {4} & bot30BB {5}", DateTime.Now.ToString(), instruments[instrument].futName, tickData.LastPrice.ToString(), instruments[instrument].longTrigger, instruments[instrument].top30bb, instruments[instrument].bot30bb);
                        else if (instruments[instrument].type == OType.Sell)
                            Console.WriteLine("Time {0} Placing SELL Order of Instrument {1} for LTP {2} as it match Short trigger {3} with top30BB {4} & bot30BB {5}", DateTime.Now.ToString(), instruments[instrument].futName, tickData.LastPrice.ToString(), instruments[instrument].shortTrigger, instruments[instrument].top30bb, instruments[instrument].bot30bb);
                        placeOrder(instrument, tickData.LastPrice - tickData.Close);
                    }
                    else
                    {
                        //instruments[instrument].status = Status.STANDING;
                    }
                }
            }
        }

        private void CloseOrderTicker(uint instrument)
        {
            instruments[instrument].isOpenAlign = false;
            instruments[instrument].canTrust = false;
            instruments[instrument].status = Status.CLOSE;
            modifyOrderInCSV(instrument, instruments[instrument].futName, instruments[instrument].type, Status.CLOSE);
            uint[] toArray = new uint[] { instrument };
            ticker.UnSubscribe(toArray);
            //instruments.Remove(instrument);
        }

        public bool VerifyLtp(Tick tickData)
        {
            bool qualified = false;
            decimal ltp = (decimal)tickData.LastPrice;
            decimal timenow = (decimal)DateTime.Now.Hour + ((decimal)DateTime.Now.Minute / 100);
            uint instrument = tickData.InstrumentToken;
            decimal low = tickData.Low;
            decimal high = tickData.High;
            if (instruments[instrument].status == Status.OPEN
                && instruments[instrument].bot30bb > 0
                && instruments[instrument].middle30BBnew > 0
                && Decimal.Compare(timenow, Convert.ToDecimal(ConfigurationManager.AppSettings["CutoffTime"])) < 0) //change back to 14.24
            {
                #region Verify for Open Trigger
                decimal variance14 = (ltp * (decimal)1.4) / 100;
                decimal variance17 = (ltp * (decimal)1.65) / 100;
                decimal variance2 = (ltp * (decimal)2) / 100;
                decimal variance23 = (ltp * (decimal)2.3) / 100;
                decimal variance25 = (ltp * (decimal)2.5) / 100;

                bool flag = DateTime.Now.Minute == 43 || DateTime.Now.Minute == 13
                                || DateTime.Now.Minute == 44 || DateTime.Now.Minute == 14
                                || (DateTime.Now.Minute == 45 && DateTime.Now.Second <= 40)
                                || (DateTime.Now.Minute == 15 && DateTime.Now.Second <= 40) ? true : false;

                if (flag)
                    return false;

                if (instruments[instrument].canTrust
                    && instruments[instrument].isOpenAlign
                    && Decimal.Compare(timenow, Convert.ToDecimal(9.44)) > 0)
                {
                    switch (instruments[instrument].type)
                    {
                        case OType.Sell:
                        case OType.StrongSell:
                            #region Verify Sell Trigger
                            qualified = IsBetweenVariance(ltp, instruments[instrument].shortTrigger, (decimal).0006);

                            if (instruments[instrument].isReversed)
                            {
                                if (Decimal.Compare(timenow, Convert.ToDecimal(10.44)) > 0)
                                {
                                    if (((IsBetweenVariance(low, instruments[instrument].bot30bb, (decimal).0006)
                                                || low < instruments[instrument].bot30bb)
                                            && instruments[instrument].middle30BB > instruments[instrument].middle30ma50
                                            && IsBeyondVariance(instruments[instrument].middle30BB, instruments[instrument].middle30ma50, (decimal).006)
                                            && instruments[instrument].bot30bb < instruments[instrument].middle30ma50)
                                        && (IsBetweenVariance((instruments[instrument].bot30bb + variance25), instruments[instrument].top30bb, (decimal).0006)
                                            || (instruments[instrument].bot30bb + variance25) > instruments[instrument].top30bb))
                                    {
                                        OType trend = CalculateSqueezedTrend(instruments[instrument].futName, instruments[instrument].history, 15);
                                        if (trend == OType.StrongBuy)
                                        {
                                            instruments[instrument].canTrust = false;
                                            Console.WriteLine("Time {0} Marcking this script {1} as Cannot-be-Trusted as it has MA3050 {2} is below middle30 {3}", DateTime.Now.ToString(), instruments[instrument].futName, instruments[instrument].middle30ma50, instruments[instrument].middle30BB);
                                            return false;
                                        }
                                    }
                                }
                                if (qualified)
                                {
                                    System.Threading.Thread.Sleep(400);
                                    List<Historical> history = kite.GetHistoricalData(instrument.ToString(),
                                                                DateTime.Now.Date.AddHours(9).AddMinutes(15),
                                                                //DateTime.Now.Date.AddHours(9).AddMinutes(16),
                                                                DateTime.Now.Date.AddDays(1),
                                                                "30minute");
                                    if (history.Count == 2)
                                    {
                                        /*
                                        if (history[history.Count - 2].Close > instruments[instrument].shortTrigger)
                                        {
                                            if (ltp > instruments[instrument].shortTrigger && Decimal.Compare(timenow, Convert.ToDecimal(9.57)) < 0)
                                                return false;
                                            else
                                            {
                                                Console.WriteLine("Time {0} Averting Candle: Please Ignore this script {1} as it has closed above the Short Trigger for now wherein prevCandle Close {2} vs short trigger {3}", DateTime.Now.ToString(), instruments[instrument].futName, history[history.Count - 2].Close, instruments[instrument].shortTrigger);
                                                //instruments[instrument].shortTrigger = instruments[instrument].top30bb;
                                                //instruments[instrument].type = OType.Buy;
                                                instruments[instrument].isReversed = false;
                                                instruments[instrument].isOpenAlign = false;
                                                modifyOpenAlignOrReversedStatus(instruments[instrument], 16, OType.Sell, false);
                                                modifyOpenAlignOrReversedStatus(instruments[instrument], 17, OType.Sell, false);
                                                qualified = false;
                                                if (instruments[instrument].status != Status.POSITION)
                                                {
                                                    uint[] toArray = new uint[] { instrument };
                                                    ticker.UnSubscribe(toArray);
                                                }
                                                return qualified;
                                            }
                                        }*/
                                    }
                                    else if (history.Count > 2)
                                    {
                                        if (DateTime.Now.Minute == 45 || DateTime.Now.Minute == 15 || DateTime.Now.Minute == 46 || DateTime.Now.Minute == 16)
                                        {
                                            TimeSpan candleTime = history[history.Count - 2].TimeStamp.TimeOfDay;
                                            TimeSpan timeDiff = DateTime.Now.TimeOfDay.Subtract(candleTime);
                                            if (timeDiff.Minutes > 35)
                                            {
                                                Console.WriteLine("EXCEPTION in Candle Retrieval Time {0} Last Candle Not Found : Last Candle closed time is {1}", DateTime.Now.ToString(), history[history.Count - 2].TimeStamp.ToString());
                                                return false;
                                            }
                                        }
                                        if (history[history.Count - 2].Close > instruments[instrument].shortTrigger)
                                        {
                                            Console.WriteLine("Time {0} Averting Candle: Please Ignore this script {1} as it has closed above the Short Trigger for now wherein prevCandle Close {2} vs short trigger {3}", DateTime.Now.ToString(), instruments[instrument].futName, history[history.Count - 2].Close, instruments[instrument].shortTrigger);
                                            //instruments[instrument].shortTrigger = instruments[instrument].top30bb;
                                            //instruments[instrument].type = OType.Buy;
                                            //instruments[instrument].isReversed = false;
                                            instruments[instrument].isOpenAlign = false;
                                            instruments[instrument].canTrust = false;
                                            modifyOpenAlignOrReversedStatus(instruments[instrument], 16, OType.Sell, false);
                                            modifyOpenAlignOrReversedStatus(instruments[instrument], 17, OType.Sell, false);
                                            qualified = false;
                                            if (instruments[instrument].status != Status.POSITION
                                                || instruments[instrument].status == Status.OPEN)
                                            {
                                                //CloseOrderTicker(instrument);
                                            }
                                            return qualified;
                                        }
                                    }
                                }
                                qualified = IsBetweenVariance(ltp, instruments[instrument].shortTrigger, (decimal).0006) || ltp > instruments[instrument].shortTrigger;
                                if (qualified)
                                {
                                    decimal requiredCP = instruments[instrument].ma50;
                                    if (instruments[instrument].ma50 < instruments[instrument].middleBB)
                                    {
                                        requiredCP = instruments[instrument].middleBB;
                                        if ((instruments[instrument].bot30bb + variance23) > instruments[instrument].top30bb)
                                        {
                                            requiredCP = instruments[instrument].topBB;
                                            if (!instruments[instrument].isOpenAlignFatal)
                                            {
                                                OType niftyTrend = VerifyNifty(timenow);
                                                if (niftyTrend == OType.Buy)
                                                {
                                                    requiredCP = instruments[instrument].top30bb;
                                                }
                                                else
                                                {
                                                    requiredCP = Convert.ToDecimal((ltp * (decimal).0005).ToString("#.#"));
                                                    requiredCP = instruments[instrument].middle30BB + requiredCP;
                                                }
                                            }
                                        }
                                    }
                                    else if (instruments[instrument].ma50 > instruments[instrument].topBB)
                                    {
                                        requiredCP = instruments[instrument].topBB;
                                        if (IsBetweenVariance(instruments[instrument].topBB, instruments[instrument].ma50, (decimal).003)
                                            && (instruments[instrument].bot30bb + variance23) > instruments[instrument].top30bb)
                                        {
                                            requiredCP = instruments[instrument].ma50;
                                            if (!instruments[instrument].isOpenAlignFatal)
                                            {
                                                OType niftyTrend = VerifyNifty(timenow);
                                                if (niftyTrend == OType.Buy)
                                                {
                                                    requiredCP = Convert.ToDecimal((ltp * (decimal).005).ToString("#.#"));
                                                    requiredCP = instruments[instrument].ma50 + requiredCP;
                                                }
                                                else
                                                {
                                                    requiredCP = Convert.ToDecimal((ltp * (decimal).0005).ToString("#.#"));
                                                    requiredCP = instruments[instrument].ma50 + requiredCP;
                                                }
                                            }
                                        }
                                    }
                                    else //if (Decimal.Compare(timenow, Convert.ToDecimal(13.30)) > 0)
                                    {
                                        requiredCP = instruments[instrument].topBB;
                                    }
                                    qualified = ltp >= requiredCP || IsBetweenVariance(ltp, requiredCP, (decimal).0006);
                                    if (instruments[instrument].ma50 > instruments[instrument].shortTrigger)
                                    {
                                        if (VerifyNifty(timenow) == OType.Sell && !qualified)
                                        {
                                            if ((instruments[instrument].botBB + variance23) < instruments[instrument].topBB
                                            && Decimal.Compare(timenow, Convert.ToDecimal(11.14)) > 0)
                                            {
                                                if (instruments[instrument].oldTime != instruments[instrument].currentTime)
                                                {
                                                    instruments[instrument].oldTime = instruments[instrument].currentTime;
                                                    Console.WriteLine("Time {0} Averting Expansion: Please Ignore this script {1} as it has Expanded so much for now wherein topBB {2} & botBB {3} for Sell", DateTime.Now.ToString(), instruments[instrument].futName, instruments[instrument].topBB, instruments[instrument].botBB);
                                                }
                                                qualified = false;
                                                return false;
                                            }
                                            if ((instruments[instrument].botBB + variance14) > instruments[instrument].topBB
                                                && Decimal.Compare(timenow, Convert.ToDecimal(11.14)) > 0
                                                && !(IsBetweenVariance(low, instruments[instrument].weekMA, (decimal).0006)
                                                        || IsBetweenVariance(low, instruments[instrument].middle30ma50, (decimal).0006)
                                                        || IsBetweenVariance(low, instruments[instrument].bot30bb, (decimal).0006)))
                                            {
                                                qualified = ltp >= instruments[instrument].topBB || IsBetweenVariance(ltp, instruments[instrument].topBB, (decimal).0006);
                                                if (qualified)
                                                    Console.WriteLine("You could have AVOIDED this. Qualified for SELL order {0} based on LTP {1} is ~ below short trigger {2} and ltp is around ma50 {3} ", instruments[instrument].futName, ltp, instruments[instrument].shortTrigger, instruments[instrument].ma50);
                                            }
                                        }
                                    }
                                    else
                                    {
                                        // Do nothing
                                    }
                                    if (qualified && instruments[instrument].oldTime != instruments[instrument].currentTime)
                                    {
                                        instruments[instrument].oldTime = instruments[instrument].currentTime;
                                        Console.WriteLine("INSIDER :: Qualified for SELL order based on LTP {0} is ~ above short trigger {1} and ltp is around topBB {2} wherein Required Cost price {3} is still to go", ltp, instruments[instrument].shortTrigger, instruments[instrument].topBB, requiredCP);
                                    }
                                }
                                /*
                                if (Decimal.Compare(timenow, Convert.ToDecimal(13.30)) > 0 && !qualified)
                                {
                                    if (VerifyNifty(timenow) == OType.Sell)
                                    {
                                        qualified = (r1 >= instruments[instrument].shortTrigger || IsBetweenVariance(r1, instruments[instrument].shortTrigger, (decimal).0006))
                                            && (r1 >= instruments[instrument].ma50 || IsBetweenVariance(r1, instruments[instrument].ma50, (decimal).0006));
                                        if (qualified)
                                        {
                                            Console.WriteLine("Time {0} Trigger Verification PASSed: The script {1} as Short Trigger {2} vs r1 {3} & ma50 is at {4} for Sell", DateTime.Now.ToString(), instruments[instrument].futName, instruments[instrument].shortTrigger, r1, instruments[instrument].ma50);
                                        }
                                    }
                                }
                                */
                            }
                            else
                            {
                                if (qualified
                                    && !instruments[instrument].isReversed
                                    && instruments[instrument].canTrust)
                                {
                                    qualified = CalculateBB((uint)instruments[instrument].instrumentId, tickData);
                                }
                                else
                                    qualified = false;
                            }
                            #endregion
                            break;
                        case OType.Buy:
                        case OType.StrongBuy:
                            #region Verify BUY Trigger
                            qualified = IsBetweenVariance(ltp, instruments[instrument].longTrigger, (decimal).0006);

                            if (instruments[instrument].isReversed)
                            {
                                if (Decimal.Compare(timenow, Convert.ToDecimal(10.44)) < 0)
                                {
                                    if (((IsBetweenVariance(high, instruments[instrument].top30bb, (decimal).0006)
                                                || high > instruments[instrument].top30bb)
                                            && instruments[instrument].middle30BB < instruments[instrument].middle30ma50
                                            && IsBeyondVariance(instruments[instrument].middle30BB, instruments[instrument].middle30ma50, (decimal).006)
                                            && instruments[instrument].top30bb > instruments[instrument].middle30ma50)
                                        && (IsBetweenVariance((instruments[instrument].bot30bb + variance25), instruments[instrument].top30bb, (decimal).0006)
                                            || (instruments[instrument].bot30bb + variance25) > instruments[instrument].top30bb))
                                    {
                                        OType trend = CalculateSqueezedTrend(instruments[instrument].futName, instruments[instrument].history, 15);
                                        if (trend == OType.StrongSell)
                                        {
                                            instruments[instrument].canTrust = false;
                                            Console.WriteLine("Time {0} Marcking this script {1} as Cannot-be-Trusted as it has MA3050 {2} is above middle30 {3}", DateTime.Now.ToString(), instruments[instrument].futName, instruments[instrument].middle30ma50, instruments[instrument].middle30BB);
                                            return false;
                                        }
                                    }
                                }
                                if (qualified)
                                {
                                    System.Threading.Thread.Sleep(400);
                                    List<Historical> history = kite.GetHistoricalData(instrument.ToString(),
                                                                DateTime.Now.Date.AddHours(9).AddMinutes(15),
                                                                //DateTime.Now.Date.AddHours(9).AddMinutes(46),
                                                                DateTime.Now.Date.AddDays(1),
                                                                "30minute");
                                    if (history.Count == 2)
                                    {
                                        /*
                                        if (history[history.Count - 2].Close < instruments[instrument].longTrigger)
                                        {
                                            if (ltp < instruments[instrument].longTrigger && Decimal.Compare(timenow, Convert.ToDecimal(9.57)) < 0)
                                                return false;
                                            else
                                            {
                                                Console.WriteLine("Time {0} Averting Candle: Please Ignore this script {1} as it has closed below the long Trigger for now wherein prevCandle Close {2} vs long trigger {3}", DateTime.Now.ToString(), instruments[instrument].futName, history[history.Count - 2].Close, instruments[instrument].longTrigger);
                                                //instruments[instrument].longTrigger = instruments[instrument].bot30bb;
                                                //instruments[instrument].type = OType.Buy;
                                                instruments[instrument].isReversed = false;
                                                instruments[instrument].isOpenAlign = false;
                                                modifyOpenAlignOrReversedStatus(instruments[instrument], 16, OType.Buy, false);
                                                modifyOpenAlignOrReversedStatus(instruments[instrument], 17, OType.Buy, false);
                                                qualified = false;
                                                if (instruments[instrument].status != Status.POSITION)
                                                {
                                                    uint[] toArray = new uint[] { instrument };
                                                    ticker.UnSubscribe(toArray);
                                                }
                                                return qualified;
                                            }
                                        }*/
                                    }
                                    else if (history.Count > 2)
                                    {
                                        if (DateTime.Now.Minute == 45 || DateTime.Now.Minute == 15 || DateTime.Now.Minute == 46 || DateTime.Now.Minute == 16)
                                        {
                                            TimeSpan candleTime = history[history.Count - 2].TimeStamp.TimeOfDay;
                                            TimeSpan timeDiff = DateTime.Now.TimeOfDay.Subtract(candleTime);
                                            if (timeDiff.Minutes > 35)
                                            {
                                                Console.WriteLine("EXCEPTION in Candle Retrieval Time {0} Last Candle Not Found : Last Candle closed time is {1}", DateTime.Now.ToString(), history[history.Count - 2].TimeStamp.ToString());
                                                return false;
                                            }
                                        }
                                        if (history[history.Count - 2].Close < instruments[instrument].longTrigger)
                                        {
                                            Console.WriteLine("Time {0} Averting Candle: Please Ignore this script {1} as it has closed below the long Trigger for now wherein prevCandle Close {2} vs long trigger {3}", DateTime.Now.ToString(), instruments[instrument].futName, history[history.Count - 2].Close, instruments[instrument].longTrigger);
                                            //instruments[instrument].longTrigger = instruments[instrument].bot30bb;
                                            //instruments[instrument].type = OType.Buy;
                                            //instruments[instrument].isReversed = false;
                                            instruments[instrument].isOpenAlign = false;
                                            instruments[instrument].canTrust = false;
                                            modifyOpenAlignOrReversedStatus(instruments[instrument], 16, OType.Buy, false);
                                            modifyOpenAlignOrReversedStatus(instruments[instrument], 17, OType.Buy, false);
                                            qualified = false;
                                            if (instruments[instrument].status != Status.POSITION
                                                || instruments[instrument].status == Status.OPEN)
                                            {
                                                //CloseOrderTicker(instrument);
                                            }
                                            return qualified;
                                        }
                                    }
                                }
                                qualified = IsBetweenVariance(ltp, instruments[instrument].longTrigger, (decimal).0006) || ltp < instruments[instrument].longTrigger;
                                if (qualified)
                                {
                                    decimal requiredCP = instruments[instrument].ma50;
                                    if (instruments[instrument].ma50 > instruments[instrument].middleBB)
                                    {
                                        requiredCP = instruments[instrument].middleBB;
                                        if ((instruments[instrument].bot30bb + variance23) > instruments[instrument].top30bb)
                                        {
                                            requiredCP = instruments[instrument].botBB;
                                            if (!instruments[instrument].isOpenAlignFatal)
                                            {
                                                OType niftyTrend = VerifyNifty(timenow);
                                                if (niftyTrend == OType.Sell)
                                                {
                                                    requiredCP = instruments[instrument].bot30bb;
                                                }
                                                else
                                                {
                                                    requiredCP = Convert.ToDecimal((ltp * (decimal).0005).ToString("#.#"));
                                                    requiredCP = instruments[instrument].middle30BB - requiredCP;
                                                }
                                            }
                                        }
                                    }
                                    else if (instruments[instrument].ma50 < instruments[instrument].botBB)
                                    {
                                        requiredCP = instruments[instrument].botBB;
                                        if (IsBetweenVariance(instruments[instrument].botBB, instruments[instrument].ma50, (decimal).003)
                                            && (instruments[instrument].bot30bb + variance23) > instruments[instrument].top30bb)
                                        {
                                            requiredCP = instruments[instrument].ma50;
                                            if (!instruments[instrument].isOpenAlignFatal)
                                            {
                                                OType niftyTrend = VerifyNifty(timenow);
                                                if (niftyTrend == OType.Sell)
                                                {
                                                    requiredCP = Convert.ToDecimal((ltp * (decimal).005).ToString("#.#"));
                                                    requiredCP = instruments[instrument].ma50 - requiredCP;
                                                }
                                                else
                                                {
                                                    requiredCP = Convert.ToDecimal((ltp * (decimal).0005).ToString("#.#"));
                                                    requiredCP = instruments[instrument].ma50 - requiredCP;
                                                }
                                            }
                                        }
                                    }
                                    else //if (Decimal.Compare(timenow, Convert.ToDecimal(13.30)) > 0)
                                    {
                                        requiredCP = instruments[instrument].botBB;
                                    }
                                    qualified = ltp <= requiredCP || IsBetweenVariance(ltp, requiredCP, (decimal).0006);
                                    if (instruments[instrument].ma50 < instruments[instrument].longTrigger)
                                    {
                                        if (VerifyNifty(timenow) == OType.Buy && !qualified)
                                        {
                                            if ((instruments[instrument].botBB + variance23) < instruments[instrument].topBB
                                                && Decimal.Compare(timenow, Convert.ToDecimal(11.14)) > 0)
                                            {
                                                if (instruments[instrument].oldTime != instruments[instrument].currentTime)
                                                {
                                                    instruments[instrument].oldTime = instruments[instrument].currentTime;
                                                    Console.WriteLine("Time {0} Averting Expansion: Please Ignore this script {1} as it has Expanded so much for now wherein topBB {2} & botBB {3} for Buy", DateTime.Now.ToString(), instruments[instrument].futName, instruments[instrument].topBB, instruments[instrument].botBB);
                                                }
                                                qualified = false;
                                                return false;
                                            }
                                            if ((instruments[instrument].botBB + variance14) > instruments[instrument].topBB
                                                && Decimal.Compare(timenow, Convert.ToDecimal(11.14)) > 0
                                                && !(IsBetweenVariance(high, instruments[instrument].weekMA, (decimal).0006)
                                                        || IsBetweenVariance(high, instruments[instrument].middle30ma50, (decimal).0006)
                                                        || IsBetweenVariance(high, instruments[instrument].top30bb, (decimal).0006)))
                                            {
                                                qualified = ltp <= instruments[instrument].botBB || IsBetweenVariance(ltp, instruments[instrument].botBB, (decimal).0006);
                                                if (qualified)
                                                    Console.WriteLine("You could have AVOIDED this. Qualified for BUY order {0} based on LTP {1} is ~ above long trigger {2} and ltp is around ma50 {3} ", instruments[instrument].futName, ltp, instruments[instrument].longTrigger, instruments[instrument].ma50);
                                            }
                                        }
                                    }
                                    else
                                    {
                                        // Do Nothing
                                    }
                                    if (qualified && instruments[instrument].oldTime != instruments[instrument].currentTime)
                                    {
                                        instruments[instrument].oldTime = instruments[instrument].currentTime;
                                        Console.WriteLine("INSIDER :: Qualified for BUY order based on LTP {0} is ~ above long trigger {1} and ltp is around botBB {2} wherein Cost Price is {3} is still to go", ltp, instruments[instrument].longTrigger, instruments[instrument].botBB, requiredCP);
                                    }
                                }
                                /*if (Decimal.Compare(timenow, Convert.ToDecimal(13.30)) > 0)
                                {
                                    if (VerifyNifty(timenow) == OType.Buy)
                                    {
                                        qualified = (r2 <= instruments[instrument].longTrigger || IsBetweenVariance(r2, instruments[instrument].longTrigger, (decimal).0006))
                                            && (r2 <= instruments[instrument].ma50 || IsBetweenVariance(r2, instruments[instrument].ma50, (decimal).0006));
                                        if (qualified)
                                        {
                                            Console.WriteLine("Time {0} Trigger Verification PASSed: The script {1} as long Trigger {2} vs r2 {3} & ma50 is at {4} for Buy", DateTime.Now.ToString(), instruments[instrument].futName, instruments[instrument].shortTrigger, r2, instruments[instrument].ma50);
                                        }
                                    }
                                }
                                */
                            }
                            else
                            {
                                if (qualified
                                    && !instruments[instrument].isReversed
                                    && instruments[instrument].canTrust)
                                {
                                    qualified = CalculateBB((uint)instruments[instrument].instrumentId, tickData);
                                }
                                else
                                    qualified = false;
                            }
                            #endregion
                            break;
                        default:
                            break;
                    }
                }
                if (!qualified
                    && !instruments[instrument].canTrust
                    && Decimal.Compare(timenow, Convert.ToDecimal(9.44)) > 0)
                {
                    //if (Decimal.Compare(timenow, Convert.ToDecimal(ConfigurationManager.AppSettings["CutOnTime"])) > 0)
                    //|| (DateTime.Now.Hour == 9 && DateTime.Now.Minute == 44))
                    {
                        qualified = IsBetweenVariance(ltp, instruments[instrument].top30bb, (decimal).0006);
                        if (qualified)
                        {
                            instruments[instrument].shortTrigger = instruments[instrument].top30bb;
                            instruments[instrument].type = OType.Sell;
                            modifyOpenAlignOrReversedStatus(instruments[instrument], 16, OType.Sell, false);
                            CalculateBB((uint)instruments[instrument].instrumentId, tickData);
                            qualified = ValidatingCurrentTrend(instrument, tickData);
                            if (!qualified)
                            {
                                Console.WriteLine("{0} DisQualified:: For script {1}, Moving average {2} is either between top30BB {3} & middle30bb {4}", DateTime.Now.ToString(), instruments[instrument].futName, instruments[instrument].middle30ma50, instruments[instrument].top30bb, instruments[instrument].middle30BB);
                                CloseOrderTicker(instrument);
                                return false;
                            }
                        }
                        else
                        {
                            qualified = IsBetweenVariance(ltp, instruments[instrument].bot30bb, (decimal).0006);
                            if (qualified)
                            {
                                instruments[instrument].longTrigger = instruments[instrument].bot30bb;
                                instruments[instrument].type = OType.Buy;
                                modifyOpenAlignOrReversedStatus(instruments[instrument], 16, OType.Buy, false);
                                CalculateBB((uint)instruments[instrument].instrumentId, tickData);
                                qualified = ValidatingCurrentTrend(instrument, tickData);
                                if (!qualified)
                                {
                                    Console.WriteLine("{0} DisQualified:: For script {1}, Moving average {2} is either between bot30BB {3} & middle30bb {4}", DateTime.Now.ToString(), instruments[instrument].futName, instruments[instrument].middle30ma50, instruments[instrument].bot30bb, instruments[instrument].middle30BB);
                                    CloseOrderTicker(instrument);
                                    return false;
                                }
                            }
                        }
                        decimal variance = variance17;
                        if (Decimal.Compare(timenow, Convert.ToDecimal(12.44)) > 0)
                        {
                            variance = variance2;
                        }
                        if ((instruments[instrument].bot30bb + variance) > instruments[instrument].top30bb
                            || IsBetweenVariance((instruments[instrument].bot30bb + variance), instruments[instrument].top30bb, (decimal).0006))
                        {
                            qualified = false;
                            if (instruments[instrument].type == OType.Buy
                                && IsBetweenVariance(instruments[instrument].bot30bb, instruments[instrument].middle30ma50, (decimal).001)
                                && instruments[instrument].bot30bb > instruments[instrument].middle30ma50
                                && instruments[instrument].bot30bb + variance14 < instruments[instrument].top30bb
                                && (IsBetweenVariance(ltp, instruments[instrument].middle30ma50, (decimal).0004)
                                    || ltp <= instruments[instrument].middle30ma50))
                            {
                                Console.WriteLine("{0} Variance is lesser than expected range for script {1} But going for risky Buy Order", DateTime.Now.ToString(), instruments[instrument].futName);
                                qualified = true;
                            }
                            else if (instruments[instrument].type == OType.Sell
                                && IsBetweenVariance(instruments[instrument].top30bb, instruments[instrument].middle30ma50, (decimal).001)
                                && instruments[instrument].top30bb < instruments[instrument].middle30ma50
                                && instruments[instrument].bot30bb + variance14 < instruments[instrument].top30bb
                                && (IsBetweenVariance(ltp, instruments[instrument].middle30ma50, (decimal).0004)
                                    || ltp >= instruments[instrument].middle30ma50))
                            {
                                Console.WriteLine("{0} Variance is lesser than expected range for script {1} But going for risky Sell Order", DateTime.Now.ToString(), instruments[instrument].futName);
                                qualified = true;
                            }
                            else
                            {
                                if (qualified && instruments[instrument].oldTime != instruments[instrument].currentTime)
                                {
                                    instruments[instrument].oldTime = instruments[instrument].currentTime;
                                    Console.WriteLine("{0} INSIDER :: DisQualified for order {1} based on LTP {2} as top30BB {3} & bot30BB {4} are within minimum variance range", DateTime.Now.ToString(), instruments[instrument].futName, ltp, instruments[instrument].top30bb, instruments[instrument].bot30bb);
                                }
                            }
                        }
                        //CalculateSqueez(instrument, tickData);
                        //qualified = CalculateSqueez(instrument, tickData);
                        if (qualified && instruments[instrument].oldTime != instruments[instrument].currentTime)
                        {
                            instruments[instrument].oldTime = instruments[instrument].currentTime;
                            Console.WriteLine("{0} INSIDER :: Qualified for order {1} based on LTP {2} is ~ either near top30BB {3} or bot30BB {4}", DateTime.Now.ToString(), instruments[instrument].futName, ltp, instruments[instrument].topBB, instruments[instrument].bot30bb);
                        }
                    }
                }
                #endregion  
            }
            else if (instruments[instrument].status == Status.POSITION)
            {
                try
                {
                    #region Verify and Modify Exit Trigger
                    try
                    {
                        OType trend = CalculateSqueezedTrend(instruments[instrument].futName, instruments[instrument].history, 10);
                        Position pos = ValidateOpenPosition(instrument, instruments[instrument].futId);
                        if (!instruments[instrument].canTrust)
                        {
                            #region Validate Untrusted script
                            if (pos.PNL < -2500 || instruments[instrument].requiredExit)
                            {
                                ModifyOrderForContract(pos, instrument, (decimal)300);
                                instruments[instrument].requiredExit = true;
                            }
                            if ((instruments[instrument].requiredExit
                                    || pos.PNL > -300)
                                && (IsBetweenVariance(ltp, instruments[instrument].middleBB, (decimal).0004)
                                    || (pos.Quantity > 0 && ltp > instruments[instrument].middleBB)
                                    || (pos.Quantity < 0 && ltp < instruments[instrument].middleBB))
                                && ValidateOrderTime(instruments[instrument].orderTime))
                            {
                                int quantity = pos.Quantity;
                                CancelOrder(pos, instrument);
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
                            #endregion
                        }
                        else
                        {
                            #region ValidateIsExitRequired
                            if (!instruments[instrument].requiredExit
                                && !instruments[instrument].isReorder
                                && instruments[instrument].canTrust)
                            {
                                try
                                {
                                    if (instruments[instrument].requiredExit
                                        || (pos.Quantity > 0 && trend == OType.StrongSell)
                                        || (pos.Quantity < 0 && trend == OType.StrongBuy))
                                    {
                                        ModifyOrderForContract(pos, instrument, (decimal)300);
                                    }
                                }
                                catch (Exception ex)
                                {
                                    Console.WriteLine("EXCEPTION in RequireExit validation at {0} with message {1}", DateTime.Now.ToString(), ex.Message);
                                }
                            }
                            #endregion
                            if (ValidateOrderTime(instruments[instrument].orderTime)
                                && !instruments[instrument].isReorder)
                            {
                                if (pos.Quantity > 0 && trend == OType.StrongSell && instruments[instrument].canTrust)
                                {
                                    ModifyOrderForContract(pos, instrument, 500);
                                }
                                else if (pos.Quantity < 0 && trend == OType.StrongBuy && instruments[instrument].canTrust)
                                {
                                    ModifyOrderForContract(pos, instrument, 500);
                                }
                                if (pos.Quantity > 0 && pos.PNL < -300) //instruments[instrument].type == OType.Buy
                                {
                                    #region Cancel Buy Order
                                    if (instruments[instrument].ma50 > 0
                                        && ((ltp < instruments[instrument].longTrigger
                                                && ltp < instruments[instrument].ma50)
                                            || instruments[instrument].requiredExit))
                                    {
                                        if (!instruments[instrument].isReorder
                                             && instruments[instrument].canTrust)
                                        {
                                            if (instruments[instrument].oldTime != instruments[instrument].currentTime)
                                            {
                                                Console.WriteLine("At {0} : The order of the script {1} is found and validating for modification based on PNL {2}", DateTime.Now.ToString(), instruments[instrument].futName, pos.PNL);
                                            }
                                            if ((pos.PNL > -2000
                                                    || IsBetweenVariance(ltp, instruments[instrument].middleBB, (decimal).0006)
                                                    || ltp > instruments[instrument].middleBB)
                                                || (pos.PNL > -3000 && instruments[instrument].requiredExit && instruments[instrument].doubledrequiredExit))
                                            {
                                                if (trend == OType.Sell || trend == OType.StrongSell || Decimal.Compare(timenow, (decimal)11.15) < 0)
                                                {
                                                    if (instruments[instrument].oldTime != instruments[instrument].currentTime)
                                                    {
                                                        Console.WriteLine("HARDEXIT NOW at {0} :: The BUY order status of the script {1} is better Exit point so EXIT NOW with loss of {2}", DateTime.Now.ToString(), instruments[instrument].futName, instruments[instrument].lotSize * (instruments[instrument].longTrigger - ltp));
                                                        instruments[instrument].oldTime = instruments[instrument].currentTime;
                                                    }
                                                    CancelAndReOrder(instrument, OType.Buy, ltp, pos.PNL);
                                                }
                                            }
                                        }
                                        else if (pos.PNL < -6000
                                            || IsBetweenVariance(ltp, instruments[instrument].bot30bb, (decimal).0006))
                                        {
                                            ModifyOrderForContract(pos, instrument, (decimal)300);
                                        }
                                    }
                                    #endregion
                                }
                                else if (pos.Quantity < 0 && pos.PNL < -300) // instruments[instrument].type == OType.Sell
                                {
                                    #region Cancel Sell Order                                
                                    if (instruments[instrument].ma50 > 0
                                        && ((ltp > instruments[instrument].shortTrigger
                                                && ltp > instruments[instrument].ma50)
                                            || instruments[instrument].requiredExit))
                                    {
                                        if (!instruments[instrument].isReorder
                                            && instruments[instrument].canTrust)
                                        {
                                            if (instruments[instrument].oldTime != instruments[instrument].currentTime)
                                            {
                                                Console.WriteLine("At {0} : The order of the script {1} is found and validating for modification based on PNL {2}", DateTime.Now.ToString(), instruments[instrument].futName, pos.PNL);
                                            }
                                            if ((pos.PNL > -2000
                                                    || IsBetweenVariance(ltp, instruments[instrument].middleBB, (decimal).0006)
                                                    || ltp < instruments[instrument].middleBB)
                                                || (pos.PNL > -3000 && instruments[instrument].requiredExit && instruments[instrument].doubledrequiredExit))
                                            {
                                                if (trend == OType.Buy || trend == OType.StrongBuy || Decimal.Compare(timenow, (decimal)11.15) < 0)
                                                {
                                                    if (instruments[instrument].oldTime != instruments[instrument].currentTime)
                                                    {
                                                        instruments[instrument].oldTime = instruments[instrument].currentTime;
                                                        Console.WriteLine("HARDEXIT NOW at {0} :: The SELL order status of the script {1} is better Exit point so EXIT NOW with loss of {2}", DateTime.Now.ToString(), instruments[instrument].futName, instruments[instrument].lotSize * (ltp - instruments[instrument].shortTrigger));
                                                    }
                                                    CancelAndReOrder(instrument, OType.Sell, ltp, pos.PNL);
                                                }
                                            }
                                        }
                                        else if (pos.PNL < -6000
                                            || IsBetweenVariance(ltp, instruments[instrument].top30bb, (decimal).0006))
                                        {
                                            ModifyOrderForContract(pos, instrument, (decimal)300);
                                        }
                                    }
                                    #endregion
                                }
                                else if (pos.Quantity == 0 && pos.PNL < -300)
                                {
                                    if (!instruments[instrument].isHedgingOrder)
                                    {
                                        CloseOrderTicker(instrument);
                                    }
                                }
                                if (!instruments[instrument].doubledrequiredExit && !instruments[instrument].isReorder)
                                {
                                    ValidateScriptTrend(pos, instrument);
                                    if (pos.PNL < -6000 && !instruments[instrument].doubledrequiredExit)
                                    {
                                        Console.WriteLine("1. OMG This script is bleeding RED at {0} :: The order status of the script {1} has gone seriously bad state with {2}", DateTime.Now.ToString(), instruments[instrument].futName, pos.PNL);
                                        instruments[instrument].requiredExit = true;
                                        instruments[instrument].doubledrequiredExit = true;
                                    }
                                }
                            }
                            else if (instruments[instrument].requiredExit
                                && instruments[instrument].weekMA > 0
                                && instruments[instrument].ma50 > 0
                                && !instruments[instrument].isReorder)
                            {
                                try
                                {
                                    if (!instruments[instrument].doubledrequiredExit
                                        && pos.PNL <= -6000)
                                    {
                                        Console.WriteLine("2. OMG This script is bleeding RED at {0} :: The order status of the script {1} has gone seriously bad state with {2}", DateTime.Now.ToString(), instruments[instrument].futName, pos.PNL);
                                        instruments[instrument].doubledrequiredExit = true;
                                    }
                                    DateTime dt = Convert.ToDateTime(instruments[instrument].orderTime);
                                    if (!instruments[instrument].isReorder) // DateTime.Now > dt.AddMinutes(3))
                                    {
                                        if ((DateTime.Now.Minute >= 12 && DateTime.Now.Minute < 15)
                                            || (DateTime.Now.Minute >= 42 && DateTime.Now.Minute < 45)
                                            || (instruments[instrument].doubledrequiredExit
                                                && IsBetweenVariance(ltp, instruments[instrument].middle30BB, (decimal).0015)))
                                        {
                                            if (instruments[instrument].isReversed
                                                && instruments[instrument].requiredExit)
                                            {
                                                decimal variance2 = (ltp * (decimal)2) / 100;
                                                if (pos.PNL < -1000 && pos.PNL > -2000
                                                    && (instruments[instrument].bot30bb + variance2) < instruments[instrument].top30bb
                                                    || (instruments[instrument].doubledrequiredExit
                                                        && IsBetweenVariance(ltp, instruments[instrument].middle30BB, (decimal).0015)))
                                                {
                                                    if (pos.Quantity > 0 && ltp < instruments[instrument].ma50)
                                                    {
                                                        Console.WriteLine("In VerifyLTP at {0} :: Processing the Order {1} as it has gone seriously bleeding state with {2}", DateTime.Now.ToString(), instruments[instrument].futName, pos.PNL);
                                                        ProcessOpenPosition(pos, instrument, OType.Sell);
                                                    }
                                                    else if (pos.Quantity < 0 && ltp > instruments[instrument].ma50)
                                                    {
                                                        Console.WriteLine("In VerifyLTP at {0} :: Processing the Order {1} as it has gone seriously bleeding state with {2}", DateTime.Now.ToString(), instruments[instrument].futName, pos.PNL);
                                                        ProcessOpenPosition(pos, instrument, OType.Buy);
                                                    }
                                                }
                                            }
                                            else if (!instruments[instrument].isReversed
                                                && (IsBeyondVariance(ltp, instruments[instrument].weekMA, (decimal).002)
                                                    || instruments[instrument].doubledrequiredExit))
                                            {
                                                if (pos.PNL < -1000 && pos.PNL > -2000
                                                    || (instruments[instrument].doubledrequiredExit
                                                        && pos.PNL > -4000))
                                                {
                                                    if (pos.Quantity > 0
                                                        && ltp < instruments[instrument].weekMA
                                                        && IsBetweenVariance(instruments[instrument].weekMA, instruments[instrument].bot30bb, (decimal).004)
                                                        && instruments[instrument].weekMA > instruments[instrument].bot30bb)
                                                    {
                                                        Console.WriteLine("In VerifyLTP at {0} :: Processing the Order {1} as it has gone bad state with {2}", DateTime.Now.ToString(), instruments[instrument].futName, pos.PNL);
                                                        ProcessOpenPosition(pos, instrument, OType.Sell);
                                                    }
                                                    else if (pos.Quantity < 0
                                                        && ltp > instruments[instrument].weekMA
                                                        && IsBetweenVariance(instruments[instrument].weekMA, instruments[instrument].top30bb, (decimal).004)
                                                        && instruments[instrument].weekMA < instruments[instrument].top30bb)
                                                    {
                                                        Console.WriteLine("In VerifyLTP at {0} :: Processing the Order {1} as it has gone bad state with {2}", DateTime.Now.ToString(), instruments[instrument].futName, pos.PNL);
                                                        ProcessOpenPosition(pos, instrument, OType.Buy);
                                                    }
                                                }
                                            }
                                        }
                                    }
                                }
                                catch (Exception ex)
                                {
                                    Console.WriteLine("EXCEPTION in RequireExit Time validation at {0} with message {1}", DateTime.Now.ToString(), ex.Message);
                                }
                            }
                            else if (instruments[instrument].oldTime != instruments[instrument].currentTime
                                && instruments[instrument].isReorder)
                            {
                                instruments[instrument].oldTime = instruments[instrument].currentTime;
                                Console.WriteLine("In VerifyLTP at {0} This is a Reverse Order of {1} current state is as follows", DateTime.Now.ToString(), instruments[instrument].futName);
                                OType currentTrend = CalculateSqueezedTrend(instruments[instrument].futName,
                                    instruments[instrument].history,
                                    10);
                                if (instruments[instrument].type == OType.Sell
                                    && currentTrend == OType.StrongBuy)
                                {
                                    ModifyOrderForContract(pos, (uint)instruments[instrument].futId, 600);
                                    Console.WriteLine("Time to Exit For contract Immediately for the current reversed SELL order of {0} which is placed at {1} should i revise target to {2}", pos.TradingSymbol, pos.AveragePrice);
                                }
                                else if (instruments[instrument].type == OType.Buy
                                    && currentTrend == OType.StrongSell)
                                {
                                    ModifyOrderForContract(pos, (uint)instruments[instrument].futId, 600);
                                    Console.WriteLine("Time to Exit For contract Immediately for the current reversed BUY order of {0} which is placed at {1} should i revise target to {2}", pos.TradingSymbol, pos.AveragePrice);
                                }
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
            else if (instruments[instrument].status == Status.STANDING)
            //&& instruments[instrument].currentTime != instruments[instrument].oldTime)
            {
                #region Check for Standing Orders
                try
                {
                    //instruments[instrument].oldTime = instruments[instrument].currentTime;
                    DateTime dt = DateTime.Now;
                    int counter = 0;
                    foreach (Order order in kite.GetOrders())
                    {
                        if (instruments[instrument].futId == order.InstrumentToken)
                        {
                            if (order.Status == "COMPLETE")
                            {
                                counter++;
                                break;
                            }
                            else if (order.Status == "OPEN")
                            {
                                dt = Convert.ToDateTime(order.OrderTimestamp);
                            }
                            if (DateTime.Now > dt.AddMinutes(6))
                            {
                                try
                                {
                                    Console.WriteLine("Getting OPEN Order Time {0} & Current Time {1} of {2} is more than than 6 minutes. So cancelling the order ID {3}", order.OrderTimestamp, DateTime.Now.ToString(), instruments[instrument].futName, order.OrderId);
                                    kite.CancelOrder(order.OrderId, Variety: "bo");
                                    instruments[instrument].status = Status.OPEN;
                                }
                                catch (Exception ex)
                                {
                                    Console.WriteLine("EXCEPTION at {0}:: Cancelling Idle Order for 20 minutes is failed with message {1}", DateTime.Now.ToString(), ex.Message);
                                }
                            }
                        }
                    }
                    if (counter == 1)
                        instruments[instrument].status = Status.POSITION;
                    else if (counter == 2)
                        CloseOrderTicker(instrument);
                    //else if (counter == 0)
                    //    instruments[instrument].status = Status.POSITION;
                }
                catch (Exception ex)
                {
                    Console.WriteLine("at {0} EXCEPTION in VerifyLTP_STANDING :: The order status of the script {1} is being validated but recieved exception {2}", DateTime.Now.ToString(), instruments[instrument].futName, ex.Message);
                }
                #endregion
            }
            return qualified;
        }

        private void ValidateScriptTrend(Position pos, uint token)
        {
            if (pos.Quantity > 0)
            {
                OType trend = CalculateSqueezedTrend(instruments[token].futName, instruments[token].history, 10);
                if (trend == OType.StrongSell || trend == OType.Sell)
                {
                    Console.WriteLine("This script is in Sell Side at {0} :: The order status of the script {1} has gone seriously bad state with {2}", DateTime.Now.ToString(), instruments[token].futName, pos.PNL);
                    instruments[token].requiredExit = true;
                    instruments[token].doubledrequiredExit = true;
                }
            }
            else if (pos.Quantity < 0)
            {
                OType trend = CalculateSqueezedTrend(instruments[token].futName, instruments[token].history, 10);
                if (trend == OType.StrongBuy || trend == OType.Buy)
                {
                    Console.WriteLine("This script is in Buy Side at {0} :: The order status of the script {1} has gone seriously bad state with {2}", DateTime.Now.ToString(), instruments[token].futName, pos.PNL);
                    instruments[token].requiredExit = true;
                    instruments[token].doubledrequiredExit = true;
                }
            }
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
                            decimal trigger = target/ (decimal)order.Quantity;
                            if (order.Price < 300)
                            {
                                trigger = Convert.ToDecimal(trigger.ToString("#.#")) + (decimal).05;
                            }
                            else
                                trigger = Convert.ToDecimal(trigger.ToString("#.#"));
                            decimal averagePrice = pos.AveragePrice;
                            averagePrice = Convert.ToDecimal(averagePrice.ToString("#.#")) + (decimal).05;
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
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Exception in ModifyorderforContract at {0} :: with message {1} for script {2}", DateTime.Now.ToString(), ex.Message, instruments[token].futName);
            }
        }

        public void CancelOrder(Position pos, uint token)
        {
            try
            {
                foreach (Order order in kite.GetOrders())
                {
                    if (pos.InstrumentToken == order.InstrumentToken)
                    {
                        if (order.Status == "OPEN") // && DateTime.Now > dt.AddMinutes(4))
                        {
                            Console.WriteLine("CANCELLED AS THE Script {0} needs to be exited immediately with minimal loss", instruments[token].top30bb - instruments[token].bot30bb);
                            kite.CancelOrder(order.OrderId, Variety: "bo");
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
            Position pos = GetCurrentPNL(instruments[token].futId);
            if (!instruments[token].canTrust)
            {
                if (instruments[token].shortTrigger > 0
                    && instruments[token].longTrigger > 0)
                {
                    if (((history[history.Count - 2].Close >= instruments[token].shortTrigger
                        || IsBetweenVariance(history[history.Count - 2].Close, instruments[token].shortTrigger, (decimal).002))
                        && instruments[token].type == OType.Sell)
                        ||
                        ((history[history.Count - 2].Close <= instruments[token].longTrigger
                            || IsBetweenVariance(history[history.Count - 2].Close, instruments[token].longTrigger, (decimal).002)
                        && instruments[token].type == OType.Buy)))
                    {
                        if (instruments[token].type == OType.Sell)
                            Console.WriteLine("POSITION : Previous Candle close {1} is too close to the short trigger {2}: Modifying the Position of {0}", instruments[token].futName, history[history.Count - 2].Close, instruments[token].shortTrigger);
                        else if (instruments[token].type == OType.Buy)
                            Console.WriteLine("POSITION : Previous Candle close {1} is too close to the long trigger {2}: Modifying the Position of {0}", instruments[token].futName, history[history.Count - 2].Close, instruments[token].longTrigger);
                        ModifyOrderForContract(pos, token, 300);
                    }
                    else
                    {
                        Console.WriteLine("POSITION : Too long to wait for the order to close: Modifying the Position of {0}", instruments[token].futName);
                        if ((instruments[token].botBB + variance14) > instruments[token].topBB)
                            ModifyOrderForContract(pos, token, 800);
                        else
                            ModifyOrderForContract(pos, token, 1400);
                    }
                }
            }
            else if ((type == OType.Sell && instruments[token].isOpenAlignFatal)
                    || (!instruments[token].isOpenAlignFatal
                        && instruments[token].requiredExit
                        && instruments[token].doubledrequiredExit
                        && IsBetweenVariance(ltp, instruments[token].middleBB, (decimal).0008)))
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
                    || (!instruments[token].isOpenAlignFatal
                        && instruments[token].requiredExit
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
                    ProcessOpenPosition(GetCurrentPNL(instruments[token].futId), token, trend);
                }
            }
            else if ((type == OType.Buy && instruments[token].isOpenAlignFatal)
                        || (!instruments[token].isOpenAlignFatal
                            && instruments[token].requiredExit
                            && instruments[token].doubledrequiredExit
                            && IsBetweenVariance(ltp, instruments[token].middleBB, (decimal).0008)))
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
                    || (!instruments[token].isOpenAlignFatal
                        && instruments[token].requiredExit
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
                    ProcessOpenPosition(GetCurrentPNL(instruments[token].futId), token, trend);
                }
            }
        }

        public Position GetCurrentPNL(int futToken)
        {
            System.Threading.Thread.Sleep(400);
            PositionResponse pr = kite.GetPositions();
            //Console.WriteLine("At Time {0} : FOUND : Overall you have maintained {1} position(s) for the day", DateTime.Now.ToString("yyyyMMdd hh:mm:ss"), pr.Day.Count);
            Position pos = pr.Day[0];
            foreach (Position position in pr.Day)
            {
                if (position.InstrumentToken == futToken)
                {
                    //Console.WriteLine("Current PNL of script {0} is {1} wherein Quantity is {2} & Unrealised is {3}; and Revising if necessary for given token {4}", position.TradingSymbol, position.PNL, position.Quantity, position.Unrealised, futToken);
                    pos = position;
                    break;
                }
            }
            return pos;
        }

        public Order GetCurrentOrder(int futToken)
        {
            System.Threading.Thread.Sleep(400);
            List<Order> pr = kite.GetOrders();
            //Console.WriteLine("At Time {0} : FOUND : Overall you have maintained {1} position(s) for the day", DateTime.Now.ToString("yyyyMMdd hh:mm:ss"), pr.Day.Count);
            Order order = pr[0];
            foreach (Order ordr in pr)
            {
                if (ordr.InstrumentToken == futToken &&
                    ordr.ParentOrderId != null &&
                    ordr.Status == "OPEN")
                {
                    //Console.WriteLine("Current PNL of script {0} is {1} wherein Quantity is {2} & Unrealised is {3}; and Revising if necessary for given token {4}", position.TradingSymbol, position.PNL, position.Quantity, position.Unrealised, futToken);
                    order = ordr;
                    break;
                }
            }
            return order;
        }

        private Position ValidateOpenPosition(uint token, int futToken)
        {
            System.Threading.Thread.Sleep(400);
            PositionResponse pr = kite.GetPositions();
            Position position = new Position();
            foreach (Position pos in pr.Day)
            {
                if (pos.InstrumentToken == futToken)
                {
                    if (pos.PNL < -3000)
                    {
                        instruments[token].requiredExit = true;
                        if (instruments[token].oldTime != instruments[token].currentTime)
                        {
                            instruments[token].oldTime = instruments[token].currentTime;
                            Console.WriteLine("LOOKOUT HARDEXIT at {0} :: The order status of the script {1} is beyond 3500 Loss POSITION  with {2}", DateTime.Now.ToString(), instruments[token].futName, pos.PNL);
                        }
                    }
                    position = pos;
                    break;
                }
            }
            return position;
        }

        private uint GetRequiredToken(uint instrument, string instName)
        {
            Dictionary<UInt32, WatchList>.KeyCollection keys = instruments.Keys;
            uint requiredToken = 0;
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
            return requiredToken;
        }

        public void LookupAndCancelOrder()
        {
            try
            {
                foreach (Order order in kite.GetOrders())
                {
                    if ((order.ParentOrderId == null || order.ParentOrderId.Length == 0) && order.Status == "OPEN")
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
                        kite.CancelOrder(order.OrderId, Variety: "bo");
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
                                    instruments[reqToken].isReorder = true;
                                    instruments[reqToken].longTrigger = instruments[reqToken].middle30BB;
                                    instruments[reqToken].shortTrigger = instruments[reqToken].top30bb;
                                    placeOrder(reqToken, 0);
                                }
                                else
                                    CloseOrderTicker(reqToken);
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
                                    instruments[reqToken].isReorder = true;
                                    instruments[reqToken].longTrigger = instruments[reqToken].bot30bb;
                                    instruments[reqToken].shortTrigger = instruments[reqToken].middle30BB;
                                    placeOrder(reqToken, 0);
                                }
                                else
                                    CloseOrderTicker(reqToken);
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
            LookupAndCancelOrder();
            PositionResponse pr = kite.GetPositions();
            Console.WriteLine("At Time {0} : FOUND : Overall you have maintained {1} position(s) for the day", DateTime.Now.ToString("yyyyMMdd hh:mm:ss"), pr.Day.Count);
            if (pr.Day.Count == 0)
                return;
            foreach (Position pos in pr.Day)
            {
                uint reqToken = GetRequiredToken(pos.InstrumentToken, pos.TradingSymbol);
                if (pos.Quantity < 0 && reqToken != 0)
                {
                    CancelAndReOrder(reqToken, OType.Sell, 0, pos.PNL);
                }
                else if (pos.Quantity > 0 && reqToken != 0)
                {
                    CancelAndReOrder(reqToken, OType.Buy, 0, pos.PNL);
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
                    niftyTrend = VerifyNifty(timenow);
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

                if (!instruments[token].isOpenAlignFatal 
                    && instruments[token].isReversed
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
                                                    kite.CancelOrder(order.OrderId, Variety: "bo");
                                                }
                                            }
                                            else
                                            {
                                                Console.WriteLine("CANCELL and Proceed ReORDERING for SELL trigger NOW with Loss of {0}", pos.PNL);
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
                                            kite.CancelOrder(order.OrderId, Variety: "bo");
                                        }
                                        else if (!instruments[token].isReversed
                                            && currentTrend == OType.StrongSell
                                            && instruments[token].requiredExit
                                            && pos.PNL > -2000
                                            && pos.PNL < -300)
                                        {
                                            Console.WriteLine("Due to Trend Reversal CANCELLED THE ORDER NOW with {0}", pos.PNL);
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
                                    Console.WriteLine("Possibly SELL NOW at {0} :: The Sell order status of the script {1} is better Reentry point NOW", DateTime.Now.ToString(), instruments[token].futName);
                                    instruments[token].status = Status.POSITION;
                                    instruments[token].type = OType.Sell;
                                    instruments[token].requiredExit = false;
                                    instruments[token].doubledrequiredExit = false;
                                    instruments[token].isReorder = true;
                                    instruments[token].shortTrigger = instruments[token].middle30BB;
                                    instruments[token].longTrigger = instruments[token].bot30bb;
                                    placeOrder(token, 0);
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
                                                    kite.CancelOrder(order.OrderId, Variety: "bo");
                                                }
                                            }
                                            else
                                            {
                                                Console.WriteLine("CANCELL and Proceed ReORDERING for BUY trigger NOW with Loss of {0}", pos.PNL);
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
                                            kite.CancelOrder(order.OrderId, Variety: "bo");
                                        }
                                        else if (!instruments[token].isReversed
                                            && currentTrend == OType.StrongBuy
                                            && instruments[token].requiredExit
                                            && pos.PNL > -2000
                                            && pos.PNL < -300)
                                        {
                                            Console.WriteLine("Due to Trend Reversal CANCELLED THE ORDER NOW with {0}", pos.PNL);
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
                                    Console.WriteLine("Possibly BUY NOW at {0} :: The Buy order status of the script {1} is better Reentry point NOW", DateTime.Now.ToString(), instruments[token].futName);
                                    instruments[token].status = Status.POSITION;
                                    instruments[token].type = OType.Buy;
                                    instruments[token].requiredExit = false;
                                    instruments[token].doubledrequiredExit = false;
                                    instruments[token].isReorder = true;
                                    instruments[token].longTrigger = instruments[token].middle30BB;
                                    instruments[token].shortTrigger = instruments[token].top30bb;
                                    placeOrder(token, 0);
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

        public bool CalculateBB(uint instrument, Tick tickData)
        {
            try
            {
                decimal ltp = tickData.LastPrice;
                decimal timenow = (decimal)DateTime.Now.Hour + ((decimal)DateTime.Now.Minute / 100);
                decimal range;
                OType trend = VerifyNifty(timenow);
                range = Convert.ToDecimal(((ltp * (decimal)1.4) / 100).ToString("#.#"));

                /*if (instruments[instrument].isVolatile || instruments[instrument].isRevised)
                {
                    range = Convert.ToDecimal(((ltp * (decimal)2.45) / 100).ToString("#.#"));
                    if (instruments[instrument].lotSize < 900 || instruments[instrument].lotSize >= 5000)
                    {
                        Console.WriteLine("Time Stamp {0} : Market is Very Volatile., Hence Avoiding orders below/above range of 900 & 6000 {1}", DateTime.Now.ToString("yyyyMMdd hh:mm:ss"), instruments[instrument].futName);
                        CloseOrderTicker(instrument);
                        return false;
                    }
                    //Console.WriteLine("Time Stamp {0} : Is already revised once. So Cross checking with higher Range. Can be Commented Later");
                }
                else
                {
                    //Console.WriteLine("Time Stamp {0} : Is Not revised before. So checking with regular Range. Can be Commented Later");
                    if (trend != OType.BS) //&& instruments[instrument].type != trend
                    {
                        range = Convert.ToDecimal(((ltp * (decimal)2.45) / 100).ToString("#.#"));
                        if (instruments[instrument].lotSize < 900 || instruments[instrument].lotSize >= 5000)
                            return false;
                    }
                    else
                        range = Convert.ToDecimal(((ltp * (decimal)1.95) / 100).ToString("#.#"));
                }*/
                WatchList wl = instruments[instrument];
                if (wl.topBB > 0 
                    && wl.botBB > 0
                    && wl.canTrust 
                    && wl.isOpenAlign)
                {
                    bool flag = false;
                    if ((wl.topBB - wl.botBB) > range)
                    {
                        if (Decimal.Compare(timenow, (decimal)(10.15)) < 0)
                        {
                            //do nothing;
                        }
                        else if (wl.type == OType.Buy
                            && (wl.middle30ma50 > wl.middle30BBnew
                                    || wl.middle30ma50 < wl.bot30bb))
                        {
                            if (ltp <= wl.botBB 
                                    || IsBetweenVariance(ltp, wl.botBB, (decimal).001))
                                    //|| IsBetweenVariance(ltp, tickData.Low, (decimal).0025))
                            {
                                if (trend == OType.Sell)
                                {
                                    if (IsBetweenVariance(ltp, tickData.Low, (decimal).0006)
                                        || ltp < tickData.Low)
                                        flag = true;
                                }
                                else
                                    flag = true;
                            }
                        }
                        else if (wl.type == OType.Sell
                            && (wl.middle30ma50 < wl.middle30BBnew
                                    || wl.middle30ma50 > wl.top30bb))
                        {
                            if (ltp >= wl.topBB 
                                || IsBetweenVariance(ltp, wl.topBB, (decimal).001))
                                //|| IsBetweenVariance(ltp, tickData.High, (decimal).0025))
                            {
                                if (trend == OType.Buy)
                                {
                                    if (IsBetweenVariance(ltp, tickData.High, (decimal).0006)
                                        || ltp > tickData.High)
                                        flag = true;
                                }
                                else
                                    flag = true;
                            }
                        }
                    }
                    if (flag)
                        Console.WriteLine("Time Stamp {0} : Recommended to Place ORDER as TopBB {1} BotBB {2} and thier Difference is {3} And LTP's range at {4} for Script {5} and the LTP is {6}", DateTime.Now.ToString("yyyyMMdd hh:mm:ss"), wl.topBB, wl.botBB, wl.topBB - wl.botBB, range, wl.futName, ltp);
                    else
                    {
                        /// Earlier implemented the range calculation using...
                        //range = .8;
                        //minRange = .5;

                        //flag = 
                        ValidatingCurrentTrend(instrument, tickData);
                    }
                    return flag;
                }
                else
                {
                    //Console.WriteLine("Time Stamp {0} : For Script {1} 3 minute Bollinger bands are {2} & {3}", DateTime.Now.ToString("yyyyMMdd hh:mm:ss"), wl.futName, wl.topBB, wl.botBB);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("EXCEPTION:: 'CalculateBB' {0}", ex.Message);
            }
            return false;
        }

        public bool ValidatingCurrentTrend(uint instrument, Tick tickData)
        {
            bool flag = true;
            try
            {
                decimal ltp = tickData.LastPrice;
                decimal timenow = (decimal)DateTime.Now.Hour + ((decimal)DateTime.Now.Minute / 100);
                if(instruments[instrument].type == OType.Buy)
                {
                    if (instruments[instrument].bot30bb < instruments[instrument].middle30ma50
                        && instruments[instrument].middle30BBnew > instruments[instrument].middle30ma50)
                    {
                        flag = false;
                        if(instruments[instrument].oldTime != instruments[instrument].currentTime)
                        {
                            instruments[instrument].oldTime = instruments[instrument].currentTime;
                            Console.WriteLine("At {0} Script {1} is ignored as the moving average {2} is between Bottom 30BB {3} and Middle 30BB {4}", DateTime.Now.ToString(), instruments[instrument].futName, instruments[instrument].middle30ma50, instruments[instrument].bot30bb, instruments[instrument].middle30BB);
                        }
                        if (checkForReverseOrder(instrument, tickData)) //tinker
                        {
                            
                        }
                    }
                }
                else if (instruments[instrument].type == OType.Sell)
                {
                    if (instruments[instrument].top30bb > instruments[instrument].middle30ma50
                        && instruments[instrument].middle30BBnew < instruments[instrument].middle30ma50)
                    {
                        flag = false;
                        if (instruments[instrument].oldTime != instruments[instrument].currentTime)
                        {
                            instruments[instrument].oldTime = instruments[instrument].currentTime;
                            Console.WriteLine("At {0} Script {1} is ignored as the moving average {2} is between Top 30BB {3} and Middle 30BB {4}", DateTime.Now.ToString(), instruments[instrument].futName, instruments[instrument].middle30ma50, instruments[instrument].top30bb, instruments[instrument].middle30BB);
                        }
                        if (checkForReverseOrder(instrument, tickData)) //tinker
                        {

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
            Dictionary<UInt32, WatchList>.KeyCollection keys = instruments.Keys;
            decimal timenow = (decimal)DateTime.Now.Hour + ((decimal)DateTime.Now.Minute / 100);
            int expected = ExpectedCandleCount(null);
            Console.WriteLine("Time Stamp {0} Candle Check keys Count {1} && expected candle count {2}", DateTime.Now.ToString("yyyyMMdd hh:mm:ss"), keys.Count, expected);
            int index = 0;
            foreach (uint instrument in keys)
            {
                try
                {
                    if (instruments[instrument].middleBB > 0
                        && instruments[instrument].middle30BBnew > 0
                        && instruments[instrument].weekMA > 0
                        && instruments[instrument].status != Status.CLOSE
                        && !instruments[instrument].futName.Contains("CRUDE"))
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
                            decimal range;
                            //index = 0;
                            if (index == 0 && instruments[instrument].isOpenAlign && instruments[instrument].canTrust)
                            {
                                //Console.WriteLine("Time Stamp {0} Candle Check for {1} and Last Candle Close Value is {2}", history[index].TimeStamp, instruments[instrument].futName, ltp);
                                #region first candle
                                range = Convert.ToDecimal(((ltp * (decimal).8) / 100).ToString("#.#"));
                                if (history[index].Close > instruments[instrument].middle30ma50
                                        && (history[index].Low <= instruments[instrument].middle30ma50
                                            || IsBetweenVariance(history[index].Low, instruments[instrument].middle30ma50, (decimal).0006))
                                        && history[index].Close < instruments[instrument].middle30BB
                                        && instruments[instrument].middle30ma50 > instruments[instrument].bot30bb
                                        && instruments[instrument].type == OType.Buy)
                                {
                                    Console.WriteLine("FCandle Time Stamp {0} Script {1} has just stopped above MA50 {2}. hence watchlist is closing this script", DateTime.Now.ToString("yyyyMMdd hh:mm:ss"), instruments[instrument].futName, instruments[instrument].middle30ma50);
                                    instruments[instrument].isOpenAlign = false;
                                    instruments[instrument].canTrust = false;
                                    instruments[instrument].isReversed = false;
                                    continue;
                                }
                                else if (history[index].Close < instruments[instrument].middle30ma50
                                        && (history[index].High >= instruments[instrument].middle30ma50
                                            || IsBetweenVariance(history[index].High, instruments[instrument].middle30ma50, (decimal).0006))
                                        && history[index].Close > instruments[instrument].middle30BB
                                        && instruments[instrument].middle30ma50 < instruments[instrument].top30bb
                                        && instruments[instrument].type == OType.Sell)
                                {
                                    Console.WriteLine("FCandle Time Stamp {0} Script {1} has just stopped below MA50 {2}. hence watchlist is closing this script", DateTime.Now.ToString("yyyyMMdd hh:mm:ss"), instruments[instrument].futName, instruments[instrument].middle30ma50);
                                    instruments[instrument].isOpenAlign = false;
                                    instruments[instrument].canTrust = false;
                                    instruments[instrument].isReversed = false;
                                    continue;
                                }
                                if (//(history[index].Close - history[index].Low) > range &&
                                    (history[index].Close - history[index].Open) > minRange
                                    && history[index].Open < instruments[instrument].middle30BB
                                    && history[index].Open < instruments[instrument].middle30BBnew
                                    && history[index].Open < instruments[instrument].weekMA
                                    && history[index].Close > instruments[instrument].weekMA
                                    && (instruments[instrument].type == OType.Sell && !instruments[instrument].isReversed
                                        || (instruments[instrument].type == OType.Buy && instruments[instrument].isReversed))
                                    )
                                {
                                    if (history[index].Close > instruments[instrument].weekMA
                                        && IsBetweenVariance(instruments[instrument].weekMA, instruments[instrument].top30bb, (decimal).003))
                                    {
                                        if (VerifyNifty(timenow) != OType.Buy
                                            && (history[index].High - history[index].Close) < minRange)
                                        {
                                            Console.WriteLine("Time Stamp {0}  +VE TREND - NIFTY SELL Candle: So JUST Place SELL ORDER at WEEKMA {1} for Script {2} and the LTP is {3}", DateTime.Now.ToString("yyyyMMdd hh:mm:ss"), instruments[instrument].weekMA, instruments[instrument].futName, ltp);
                                            //return true;
                                        }
                                        //return;
                                        continue;
                                    }
                                    if (history[index].Close > instruments[instrument].weekMA
                                               && history[index].Close < instruments[instrument].middle30BB
                                               //&& history[index].Close < instruments[instrument].middle30BBnew
                                               && instruments[instrument].weekMA < instruments[instrument].middle30BBnew
                                               && history[index].High >= instruments[instrument].middle30BBnew
                                               && IsBetweenVariance(instruments[instrument].weekMA, instruments[instrument].middle30BBnew, (decimal).006)
                                               && IsBeyondVariance(instruments[instrument].weekMA, instruments[instrument].middle30BBnew, (decimal).002))
                                    {
                                        Console.WriteLine("Time Stamp {0} First Candle +VE TREND: But JUST Place SELL ORDER at WEEKMA {1} for Script {2} and the LTP is {3}", DateTime.Now.ToString("yyyyMMdd hh:mm:ss"), instruments[instrument].weekMA, instruments[instrument].futName, ltp);
                                        if (VerifyNifty(timenow) == OType.Buy)
                                        {
                                            Console.WriteLine("YOU COULD HAVE AVOIDED THIS ORDER");
                                            //continue;
                                        }
                                        instruments[instrument].type = OType.Sell;
                                        instruments[instrument].isReversed = true;
                                        instruments[instrument].ReversedTime = DateTime.Now;
                                        instruments[instrument].shortTrigger = instruments[instrument].middle30BBnew;
                                        modifyOpenAlignOrReversedStatus(instruments[instrument], 17, OType.Sell, true);
                                        //return true;
                                    }
                                    else if (history[index].Close > instruments[instrument].weekMA
                                               && history[index].Close > instruments[instrument].middle30BB
                                               && instruments[instrument].weekMA >= instruments[instrument].middle30BBnew
                                               && (history[index].Close - history[index].Open) > 2 * minRange
                                               && IsBeyondVariance(instruments[instrument].weekMA, instruments[instrument].middle30BBnew, (decimal).002))
                                    {
                                        Console.WriteLine("Time Stamp {0} +VE TREND - First Candle: Recommending to Place Buy ORDER at WEEKMA {1} for Script {2} and the LTP is {3}", DateTime.Now.ToString("yyyyMMdd hh:mm:ss"), instruments[instrument].weekMA, instruments[instrument].futName, ltp);
                                        instruments[instrument].isReversed = true;
                                        instruments[instrument].ReversedTime = DateTime.Now;
                                        instruments[instrument].type = OType.Buy;
                                        instruments[instrument].longTrigger = instruments[instrument].middle30BBnew;
                                        instruments[instrument].shortTrigger = instruments[instrument].top30bb;
                                        modifyOpenAlignOrReversedStatus(instruments[instrument], 17, OType.Buy, true);
                                    }
                                    else if (history[index].Close > instruments[instrument].weekMA
                                               && history[index].Close > instruments[instrument].middle30BB
                                               && (history[index].Open - history[index].Close) > 2 * minRange)
                                    {
                                        Console.WriteLine("Time Stamp {0} Observation First Candle: Script {1} Observation for future reference", DateTime.Now.ToString("yyyyMMdd hh:mm:ss"), instruments[instrument].futName);
                                    }
                                }
                                else if (//(history[index].High - history[index].Close) > range &&
                                    (history[index].Open - history[index].Close) > minRange
                                    && history[index].Open > instruments[instrument].middle30BB
                                    && history[index].Open > instruments[instrument].middle30BBnew
                                    && history[index].Open > instruments[instrument].weekMA
                                    && history[index].Close < instruments[instrument].weekMA
                                    && (instruments[instrument].type == OType.Buy && !instruments[instrument].isReversed
                                        || (instruments[instrument].type == OType.Sell && instruments[instrument].isReversed))
                                    )
                                {
                                    if (history[index].Close < instruments[instrument].weekMA
                                        && IsBetweenVariance(instruments[instrument].weekMA, instruments[instrument].bot30bb, (decimal).003))
                                    {
                                        if (VerifyNifty(timenow) != OType.Sell
                                            && (history[index].Close - history[index].Low) < minRange)
                                        {
                                            Console.WriteLine("Time Stamp {0}  -VE TREND - NIFTY BUY Candle: So JUST Place Buy ORDER at WEEKMA {1} for Script {2} and the LTP is {3}", DateTime.Now.ToString("yyyyMMdd hh:mm:ss"), instruments[instrument].weekMA, instruments[instrument].futName, ltp);
                                            //return true;
                                        }
                                        //return false;
                                        continue;
                                    }
                                    if (history[index].Close < instruments[instrument].weekMA
                                               && history[index].Close > instruments[instrument].middle30BB
                                               //&& history[index].Close > instruments[instrument].middle30BBnew
                                               && instruments[instrument].weekMA > instruments[instrument].middle30BBnew
                                               && history[index].Low <= instruments[instrument].middle30BBnew
                                               && IsBetweenVariance(instruments[instrument].weekMA, instruments[instrument].middle30BBnew, (decimal).006)
                                               && IsBeyondVariance(instruments[instrument].weekMA, instruments[instrument].middle30BBnew, (decimal).002))
                                    {
                                        Console.WriteLine("Time Stamp {0} First Candle -VE TREND: But JUST Place BUY ORDER at WEEKMA {1} for Script {2} and the LTP is {3}", DateTime.Now.ToString("yyyyMMdd hh:mm:ss"), instruments[instrument].weekMA, instruments[instrument].futName, ltp);
                                        if (VerifyNifty(timenow) == OType.Sell)
                                        {
                                            Console.WriteLine("YOU COULD HAVE AVOIDED THIS ORDER");
                                            //continue;
                                        }
                                        instruments[instrument].type = OType.Buy;
                                        instruments[instrument].isReversed = true;
                                        instruments[instrument].ReversedTime = DateTime.Now;
                                        instruments[instrument].longTrigger = instruments[instrument].middle30BBnew;
                                        modifyOpenAlignOrReversedStatus(instruments[instrument], 17, OType.Buy, true);
                                        //return true;
                                    }
                                    else if (history[index].Close < instruments[instrument].weekMA
                                               && history[index].Close < instruments[instrument].middle30BB
                                               && instruments[instrument].weekMA <= instruments[instrument].middle30BBnew
                                               && (history[index].Open - history[index].Close) > 2 * minRange
                                               && IsBeyondVariance(instruments[instrument].weekMA, instruments[instrument].middle30BBnew, (decimal).002))
                                    {
                                        Console.WriteLine("Time Stamp {0} -VE TREND - First Candle: Recommending to Place Sell ORDER at WEEKMA {1} for Script {2} and the LTP is {3}", DateTime.Now.ToString("yyyyMMdd hh:mm:ss"), instruments[instrument].weekMA, instruments[instrument].futName, ltp);
                                        instruments[instrument].isReversed = true;
                                        instruments[instrument].ReversedTime = DateTime.Now;
                                        instruments[instrument].type = OType.Sell;
                                        instruments[instrument].longTrigger = instruments[instrument].bot30bb;
                                        instruments[instrument].shortTrigger = instruments[instrument].middle30BBnew;
                                        modifyOpenAlignOrReversedStatus(instruments[instrument], 17, OType.Sell, true);
                                    }
                                    else if (history[index].Close < instruments[instrument].weekMA
                                               && history[index].Close < instruments[instrument].middle30BB
                                               && (history[index].Open - history[index].Close) > 2 * minRange)
                                    {
                                        Console.WriteLine("Time Stamp {0} Observation First Candle: Script {1} Observation for future reference", DateTime.Now.ToString("yyyyMMdd hh:mm:ss"), instruments[instrument].futName);
                                    }
                                }
                                else if ((history[index].Close - history[index].Low) >= range
                                    && history[index].Close - history[index].Open > minRange
                                    && history[index].Open < history[index].Close
                                    && history[index].Open < instruments[instrument].middle30BB
                                    && history[index].Open < instruments[instrument].middle30BBnew
                                    && history[index].Close > instruments[instrument].ma50
                                    && (history[index].Close > instruments[instrument].middle30BB
                                            || IsBetweenVariance(history[index].Close, instruments[instrument].middle30BBnew, (decimal).0012)
                                            || (history[index].Open < instruments[instrument].bot30bb && history[index].Close < instruments[instrument].bot30bb))
                                    && (IsBetweenVariance(history[index].Low, instruments[instrument].bot30bb, (decimal).0012)
                                        || history[index].Low < instruments[instrument].bot30bb
                                        || history[index].Low == history[index].Open)
                                    && (instruments[instrument].type == OType.Sell && !instruments[instrument].isReversed
                                        || (instruments[instrument].type == OType.Buy && instruments[instrument].isReversed)))
                                {
                                    if (history[index].Close <= instruments[instrument].weekMA
                                        && IsBetweenVariance(instruments[instrument].weekMA, instruments[instrument].top30bb, (decimal).0003))
                                        //return false;
                                        continue;
                                    if (VerifyNifty(timenow) == OType.Sell)
                                    {
                                        Console.WriteLine("YOU COULD HAVE AVOIDED THIS ORDER");
                                        //return false;
                                    }
                                    Console.WriteLine("Time Stamp {0} +VE TREND - First Candle: Recommending to Place Buy ORDER at WEEKMA {1} for Script {2} and the LTP is {3}", DateTime.Now.ToString("yyyyMMdd hh:mm:ss"), instruments[instrument].weekMA, instruments[instrument].futName, ltp);
                                    instruments[instrument].isReversed = true;
                                    instruments[instrument].ReversedTime = DateTime.Now;
                                    instruments[instrument].type = OType.Buy;
                                    instruments[instrument].longTrigger = instruments[instrument].middle30BBnew;
                                    instruments[instrument].shortTrigger = instruments[instrument].top30bb;
                                    modifyOpenAlignOrReversedStatus(instruments[instrument], 17, OType.Buy, true);
                                }
                                else if ((history[index].High - history[index].Close) > range
                                    && (history[index].Open - history[index].Close) > minRange
                                    && history[index].Open > history[index].Close
                                    && history[index].Open > instruments[instrument].middle30BB
                                    && history[index].Open > instruments[instrument].middle30BBnew
                                    && history[index].Close < instruments[instrument].ma50
                                    && (history[index].Close < instruments[instrument].middle30BB
                                        || IsBetweenVariance(history[index].Close, instruments[instrument].middle30BBnew, (decimal).0012)
                                        || (history[index].Open > instruments[instrument].top30bb && history[index].Close > instruments[instrument].top30bb))
                                    && (IsBetweenVariance(history[index].High, instruments[instrument].top30bb, (decimal).0012)
                                        || history[index].High > instruments[instrument].top30bb
                                        || history[index].High == history[index].Open)
                                    && (instruments[instrument].type == OType.Buy && !instruments[instrument].isReversed
                                        || (instruments[instrument].type == OType.Sell && instruments[instrument].isReversed)))
                                {
                                    if (history[index].Close >= instruments[instrument].weekMA
                                        && IsBetweenVariance(instruments[instrument].weekMA, instruments[instrument].bot30bb, (decimal).0003))
                                        //return false;
                                        continue;
                                    if (VerifyNifty(timenow) == OType.Buy)
                                    {
                                        Console.WriteLine("YOU COULD HAVE AVOIDED THIS ORDER");
                                        //return false;
                                    }
                                    Console.WriteLine("Time Stamp {0} -VE TREND - First Candle: Recommending to Place SELL ORDER at WEEKMA {1} for Script {2} and the LTP is {3}", DateTime.Now.ToString("yyyyMMdd hh:mm:ss"), instruments[instrument].weekMA, instruments[instrument].futName, ltp);
                                    instruments[instrument].isReversed = true;
                                    instruments[instrument].ReversedTime = DateTime.Now;
                                    instruments[instrument].type = OType.Sell;
                                    instruments[instrument].longTrigger = instruments[instrument].bot30bb;
                                    instruments[instrument].shortTrigger = instruments[instrument].middle30BBnew;
                                    modifyOpenAlignOrReversedStatus(instruments[instrument], 17, OType.Sell, true);
                                }
                                else if (((history[index].Close - history[index].Low) > (range + range / 3)
                                            || IsBetweenVariance((history[index].Close - history[index].Open), range, (decimal).0006))
                                    && history[index].Open < history[index].Close
                                    && history[index].Open < instruments[instrument].middle30BB
                                    && history[index].Open < instruments[instrument].middle30BBnew
                                    && history[index].Close > instruments[instrument].ma50
                                    && history[index].Close > instruments[instrument].middle30BB
                                    && IsBeyondVariance(instruments[instrument].weekMA, instruments[instrument].middle30BBnew, (decimal).006)
                                    && IsBeyondVariance(history[index].High, instruments[instrument].weekMA, (decimal).0009)
                                    && (instruments[instrument].type == OType.Sell && !instruments[instrument].isReversed
                                        || (instruments[instrument].type == OType.Buy && instruments[instrument].isReversed)))
                                {
                                    if (VerifyNifty(timenow) == OType.Sell)
                                    {
                                        Console.WriteLine("YOU COULD HAVE AVOIDED THIS ORDER");
                                        //return false;
                                    }
                                    Console.WriteLine("Time Stamp {0} +VE TREND - First Candle: Recommending to Place though not breached middle30 for Buy ORDER at WEEKMA {1} for Script {2} and the LTP is {3}", DateTime.Now.ToString("yyyyMMdd hh:mm:ss"), instruments[instrument].weekMA, instruments[instrument].futName, ltp);
                                    instruments[instrument].isReversed = true;
                                    instruments[instrument].ReversedTime = DateTime.Now;
                                    instruments[instrument].type = OType.Buy;
                                    modifyOpenAlignOrReversedStatus(instruments[instrument], 17, OType.Buy, true);
                                    instruments[instrument].longTrigger = instruments[instrument].middle30BBnew;
                                    instruments[instrument].shortTrigger = instruments[instrument].top30bb;
                                    //if (history[index].Close > instruments[instrument].middle30BBnew
                                    //    && IsBeyondVariance(history[index].Close, instruments[instrument].middle30BBnew, (decimal).0012)
                                    //    && IsBeyondVariance(history[index].Close, instruments[instrument].ma50, (decimal).0006))
                                    //{
                                    //}
                                    //else
                                    //{
                                    //if (IsBetweenVariance(history[index].Close, instruments[instrument].ma50, (decimal).0008))
                                    //    return true;
                                    //}
                                }
                                else if (((history[index].High - history[index].Close) > (range + (range / 3))
                                            || IsBetweenVariance((history[index].Open - history[index].Close), range, (decimal).0006))
                                    && history[index].Open > history[index].Close
                                    && history[index].Open > instruments[instrument].middle30BB
                                    && history[index].Open > instruments[instrument].middle30BBnew
                                    && history[index].Close < instruments[instrument].ma50
                                    && history[index].Close < instruments[instrument].middle30BB
                                    && IsBeyondVariance(instruments[instrument].weekMA, instruments[instrument].middle30BBnew, (decimal).006)
                                    && IsBeyondVariance(history[index].Low, instruments[instrument].weekMA, (decimal).0009)
                                    && (instruments[instrument].type == OType.Buy && !instruments[instrument].isReversed
                                        || (instruments[instrument].type == OType.Sell && instruments[instrument].isReversed)))
                                {
                                    if (VerifyNifty(timenow) == OType.Buy)
                                    {
                                        Console.WriteLine("YOU COULD HAVE AVOIDED THIS ORDER");
                                        //return false;
                                    }
                                    Console.WriteLine("Time Stamp {0} -VE TREND - First Candle: Recommending to Place though not breached middle30 for SELL ORDER at WEEKMA {1} for Script {2} and the LTP is {3}", DateTime.Now.ToString("yyyyMMdd hh:mm:ss"), instruments[instrument].weekMA, instruments[instrument].futName, ltp);
                                    instruments[instrument].isReversed = true;
                                    instruments[instrument].ReversedTime = DateTime.Now;
                                    instruments[instrument].type = OType.Sell;
                                    instruments[instrument].longTrigger = instruments[instrument].bot30bb;
                                    instruments[instrument].shortTrigger = instruments[instrument].middle30BBnew;
                                    modifyOpenAlignOrReversedStatus(instruments[instrument], 17, OType.Sell, true);
                                }
                                else if (history[index].Open < instruments[instrument].middle30BB
                                    && history[index].Open < instruments[instrument].middle30BBnew
                                    && history[index].Close > instruments[instrument].middle30BB
                                    && history[index].Close > instruments[instrument].middle30BBnew
                                    && (instruments[instrument].type == OType.Sell && !instruments[instrument].isReversed
                                        || (instruments[instrument].type == OType.Buy && instruments[instrument].isReversed)))
                                {
                                    Console.WriteLine("Time Stamp {0} -VE TREND - Averting FCandle: First Candle Breakout above Middle30BB {1} for SELL ORDER for Script {2} and the LTP is {3}", DateTime.Now.ToString("yyyyMMdd hh:mm:ss"), instruments[instrument].middle30BB, instruments[instrument].futName, ltp);
                                    if (instruments[instrument].isOpenAlign)
                                        instruments[instrument].canTrust = false;
                                    else
                                        CloseOrderTicker(instrument);
                                }
                                else if (history[index].Open > instruments[instrument].middle30BB
                                    && history[index].Open > instruments[instrument].middle30BBnew
                                    && history[index].Close < instruments[instrument].middle30BB
                                    && history[index].Close < instruments[instrument].middle30BBnew
                                    && (instruments[instrument].type == OType.Buy && !instruments[instrument].isReversed
                                        || (instruments[instrument].type == OType.Sell && instruments[instrument].isReversed)))
                                {
                                    Console.WriteLine("Time Stamp {0} -VE TREND - Averting FCandle: First Candle Breakout below Middle30BB {1} for Buy ORDER for Script {2} and the LTP is {3}", DateTime.Now.ToString("yyyyMMdd hh:mm:ss"), instruments[instrument].middle30BB, instruments[instrument].futName, ltp);
                                    if (instruments[instrument].isOpenAlign)
                                        instruments[instrument].canTrust = false;
                                    else
                                        CloseOrderTicker(instrument);
                                }
                                else if ((history[index].High - history[index].Low) > range
                                    && history[index].Open < history[index].Close
                                    && history[index].Open > instruments[instrument].middle30BB
                                    && history[index].Open > instruments[instrument].middle30BBnew
                                    && history[index].Close > instruments[instrument].middle30BBnew
                                    && history[index].Open > instruments[instrument].weekMA
                                    && history[index].Close > instruments[instrument].weekMA
                                    && instruments[instrument].type == OType.Buy
                                    && !instruments[instrument].isReversed
                                    && instruments[instrument].openOppositeAlign)
                                {
                                    Console.WriteLine("Time Stamp {0} +VE TREND - First Candle: Recommending to Place as Open Opposite Align at middle30 for Buy ORDER at WEEKMA {1} for Script {2} and the LTP is {3}", DateTime.Now.ToString("yyyyMMdd hh:mm:ss"), instruments[instrument].weekMA, instruments[instrument].futName, ltp);
                                    instruments[instrument].isReversed = true;
                                    instruments[instrument].ReversedTime = DateTime.Now;
                                    instruments[instrument].type = OType.Buy;
                                    instruments[instrument].longTrigger = instruments[instrument].middle30BBnew;
                                    instruments[instrument].shortTrigger = instruments[instrument].top30bb;
                                    modifyOpenAlignOrReversedStatus(instruments[instrument], 17, OType.Buy, true);
                                }
                                else if ((history[index].High - history[index].Low) > range
                                    && history[index].Close < history[index].Open
                                    && history[index].Open < instruments[instrument].middle30BB
                                    && history[index].Open < instruments[instrument].middle30BBnew
                                    && history[index].Close < instruments[instrument].middle30BBnew
                                    && history[index].Open < instruments[instrument].weekMA
                                    && history[index].Close < instruments[instrument].weekMA
                                    && instruments[instrument].type == OType.Sell
                                    && !instruments[instrument].isReversed
                                    && instruments[instrument].openOppositeAlign)
                                {
                                    Console.WriteLine("Time Stamp {0} -VE TREND - First Candle: Recommending to Place as Open Opposite Align at middle30 for Buy ORDER at WEEKMA {1} for Script {2} and the LTP is {3}", DateTime.Now.ToString("yyyyMMdd hh:mm:ss"), instruments[instrument].weekMA, instruments[instrument].futName, ltp);
                                    instruments[instrument].isReversed = true;
                                    instruments[instrument].ReversedTime = DateTime.Now;
                                    instruments[instrument].type = OType.Sell;
                                    modifyOpenAlignOrReversedStatus(instruments[instrument], 17, OType.Sell, true);
                                    instruments[instrument].shortTrigger = instruments[instrument].middle30BBnew;
                                    instruments[instrument].longTrigger = instruments[instrument].bot30bb;
                                }
                                #endregion
                            }
                            else if (index > 0 && instruments[instrument].canTrust)
                            {
                                try
                                {
                                    checkForCandleClose(instrument, history[index].Close, index, history);
                                    continue;
                                }
                                catch (Exception ex)
                                {
                                    Console.WriteLine("EXCEPTIO CAUGHT in CandleClose more than 2nd index of {0} with message {1}", instruments[instrument].futName, ex.Message);
                                }
                            }
                            if (!instruments[instrument].canTrust)
                            {
                                range = Convert.ToDecimal(((ltp * (decimal).8) / 100).ToString("#.#"));
                                if ((instruments[instrument].middle30BB < instruments[instrument].middle30ma50
                                    && (history[index].Close > instruments[instrument].top30bb
                                        || IsBetweenVariance(history[index].Close, instruments[instrument].top30bb, (decimal).002)
                                        || (index == 0
                                            && history[index].Close > history[index].Open
                                            && (history[index].Close - history[index].Open) > range
                                            && IsBetweenVariance(history[index].Close, instruments[instrument].top30bb, (decimal).004))))
                                    || (instruments[instrument].middle30BB > instruments[instrument].middle30ma50
                                        && (history[index].Close < instruments[instrument].bot30bb
                                        || IsBetweenVariance(history[index].Close, instruments[instrument].bot30bb, (decimal).002)
                                        || (index == 0
                                            && history[index].Open > history[index].Close
                                            && (history[index].Open - history[index].Close) > range
                                            && IsBetweenVariance(history[index].Close, instruments[instrument].bot30bb, (decimal).004))))
                                    ||  (index == 0
                                        && !instruments[instrument].isOpenAlign
                                        && !instruments[instrument].openOppositeAlign
                                        && (IsBeyondVariance(history[0].Open, instruments[instrument].middle30BB, (decimal).005)
                                            && IsBeyondVariance(history[0].Close, instruments[instrument].middle30BB, (decimal).009)
                                            && instruments[instrument].type == OType.Sell))
                                    || (index == 0
                                        && !instruments[instrument].isOpenAlign
                                        && !instruments[instrument].openOppositeAlign
                                        && (IsBeyondVariance(history[0].Open, instruments[instrument].middle30BB, (decimal).009)
                                            && IsBeyondVariance(history[0].Close, instruments[instrument].middle30BB, (decimal).005)
                                            && instruments[instrument].type == OType.Buy)))
                                {
                                    if (instruments[instrument].status == Status.OPEN)
                                    {
                                        Console.WriteLine("Time Stamp {0} Averting Candle given by Cannot-Be-Trusted script: Candle Breakout above 30BB {1} or below30BB {2} for Script {3} and the LTP is {4}", DateTime.Now.ToString("yyyyMMdd hh:mm:ss"), instruments[instrument].top30bb, instruments[instrument].bot30bb, instruments[instrument].futName, ltp);
                                        CloseOrderTicker(instrument);
                                    }
                                    else if (instruments[instrument].status == Status.POSITION)
                                    {
                                        Console.WriteLine("Time Stamp {0} You are in Soup as Averting Candle given by Cannot-Be-Trusted script: Candle Breakout above 30BB {1} or below30BB {2} for Script {3} and the LTP is {4}", DateTime.Now.ToString("yyyyMMdd hh:mm:ss"), instruments[instrument].top30bb, instruments[instrument].bot30bb, instruments[instrument].futName, ltp);
                                        Position pos = GetCurrentPNL(instruments[instrument].futId);
                                        ModifyOrderForContract(pos, instrument, 350);
                                    }
                                    else
                                    {
                                        CloseOrderTicker(instrument);
                                        Console.WriteLine("Time Stamp {0} What state is this? as Averting Candle given by Cannot-Be-Trusted script: Candle Breakout above 30BB {1} or below30BB {2} for Script {3} and the LTP is {4}", DateTime.Now.ToString("yyyyMMdd hh:mm:ss"), instruments[instrument].top30bb, instruments[instrument].bot30bb, instruments[instrument].futName, ltp);
                                    }
                                }
                                else
                                {
                                    if (history[index].Close > instruments[instrument].middle30ma50
                                        && (history[index].Low <= instruments[instrument].middle30ma50
                                            || IsBetweenVariance(history[index].Low, instruments[instrument].middle30ma50, (decimal).0006))
                                        && instruments[instrument].middle30ma50 > instruments[instrument].bot30bb)
                                    {
                                        Console.WriteLine("Time Stamp {0} Script {1} has just stopped above MA50 {2} though cannot be trusted as current high is {3}. hence watchlist is closing this script", DateTime.Now.ToString("yyyyMMdd hh:mm:ss"), instruments[instrument].futName, instruments[instrument].middle30ma50, history[index].High);
                                        instruments[instrument].type = OType.Buy;
                                        instruments[instrument].canTrust = false;
                                        instruments[instrument].isReversed = false;
                                        continue;
                                    }
                                    else if (history[index].Close < instruments[instrument].middle30ma50
                                            && (history[index].High >= instruments[instrument].middle30ma50
                                                || IsBetweenVariance(history[index].High, instruments[instrument].middle30ma50, (decimal).0006))
                                            && instruments[instrument].middle30ma50 < instruments[instrument].top30bb)
                                    {
                                        Console.WriteLine("Time Stamp {0} Script {1} has just stopped below MA50 {2} though cannot be trusted as current low is {3}. hence watchlist is closing this script", DateTime.Now.ToString("yyyyMMdd hh:mm:ss"), instruments[instrument].futName, instruments[instrument].middle30ma50, history[index].Low);
                                        instruments[instrument].type = OType.Sell;
                                        instruments[instrument].canTrust = false;
                                        instruments[instrument].isReversed = false;
                                        continue;
                                    }
                                }
                            }
                        }
                        #endregion
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine("EXCEPTIO CAUGHT in CandleClose of {0} with message {1}", instruments[instrument].futName, ex.Message);
                }
            }
            try
            {
                if (Decimal.Compare(timenow, (decimal)(13.45)) >= 0
                    && Decimal.Compare(timenow, (decimal)(14.25)) <= 0)
                {
                    //CalculateSqueezedBB();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("EXCEPTIO CAUGHT While Calculating narrowed scripts with message {0}", ex.Message);
            }
            putStatusContent();
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
                    else if (instruments[instrument].movement > 5)
                    {
                        if (IsBetweenVariance(ltp, tickData.Low, (decimal).0007) && wl.type == OType.Sell)
                        {
                            Console.WriteLine("Time Stamp {0} MA50 and BBB: Recommending to Place SELL ORDER at WEEKMA {1} for Script {2} and the LTP is {3}", DateTime.Now.ToString("yyyyMMdd hh:mm:ss"), instruments[instrument].weekMA, instruments[instrument].futName, ltp);
                            //instruments[token].type = OType.Sell;
                            //return true;
                        }
                        else if (IsBetweenVariance(ltp, tickData.High, (decimal).0007) && wl.type == OType.Buy)
                        {
                            Console.WriteLine("Time Stamp {0} MA50 and BBB: Recommending to Place BUY ORDER at WEEKMA {1} for Script {2} and the LTP is {3}", DateTime.Now.ToString("yyyyMMdd hh:mm:ss"), instruments[instrument].weekMA, instruments[instrument].futName, ltp);
                            //instruments[token].type = OType.Buy;
                            //return true;
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

        void checkForCandleClose(uint instrument, decimal ltp, int index, List<Historical> history)
        {
            decimal range = Convert.ToDecimal(((ltp * (decimal)1.35) / 100).ToString("#.#"));
            decimal mRange = Convert.ToDecimal(((ltp * (decimal).25) / 100).ToString("#.#"));
            //decimal minRange = Convert.ToDecimal(((ltp * (decimal)0.6) / 100).ToString("#.#"));
            decimal timenow = (decimal)DateTime.Now.Hour + ((decimal)DateTime.Now.Minute / 100);
            //Console.WriteLine("Time Stamp {0} Candle Check for {1} with Candle Index Position {2} and Last Candle Close Value is {3}", history[index].TimeStamp, instruments[instrument].futName, index, ltp);
            if (index > 0)
            {
                if (instruments[instrument].isOpenAlign)
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
                                && history[index - 1].Close > instruments[instrument].weekMA
                                && history[index].Close > instruments[instrument].bot30bb
                                && (history[index].Close - instruments[instrument].bot30bb) > mRange)
                        {
                            Console.WriteLine("Time Stamp {0}  -VE TREND: But JUST Place BUY ORDER at WEEKMA {1} for Script {2} and the LTP is {3}", DateTime.Now.ToString("yyyyMMdd hh:mm:ss"), instruments[instrument].weekMA, instruments[instrument].futName, ltp);
                            instruments[instrument].type = OType.Buy;
                            instruments[instrument].isReversed = true;
                            instruments[instrument].ReversedTime = DateTime.Now;
                            instruments[instrument].longTrigger = instruments[instrument].middle30BBnew;
                            modifyOpenAlignOrReversedStatus(instruments[instrument], 17, OType.Buy, true);
                            //return true;
                        }
                        else if ((history[index].Open > history[index].Close || history[index].Open < instruments[instrument].middle30BBnew)
                                && history[index].Close < instruments[instrument].middle30BBnew)
                        //&& (history[index - 1].Open > instruments[instrument].middle30BB
                        //    || IsBetweenVariance(history[index - 1].Open, instruments[instrument].middle30BB, (decimal).0002))
                        //&& history[index - 1].Close > instruments[instrument].middle30BB)
                        {
                            if (IsBeyondVariance(history[index].Close, instruments[instrument].middle30BB, (decimal).0004)
                                && IsBeyondVariance(history[index].Close, instruments[instrument].middle30BBnew, (decimal).0004)
                                && IsBeyondVariance(instruments[instrument].weekMA, instruments[instrument].middle30BBnew, (decimal).002))
                            {
                                if ((history[index].Close > instruments[instrument].weekMA
                                        && IsBetweenVariance(history[index].Close, instruments[instrument].weekMA, (decimal).003))
                                        || (history[index - 1].Open <= instruments[instrument].middle30BB
                                            && history[index - 1].Close > instruments[instrument].middle30BB))
                                {
                                    Console.WriteLine("Time Stamp {0}  -VE TREND: Recommending but return false for Buy ORDER at WEEKMA {1} for Script {2} and the LTP is {3}", DateTime.Now.ToString("yyyyMMdd hh:mm:ss"), instruments[instrument].weekMA, instruments[instrument].futName, ltp);
                                    return;
                                }
                                if (VerifyNifty(timenow) == OType.Buy)
                                {
                                    Console.WriteLine("YOU COULD HAVE AVOIDED THIS ORDER");
                                    //return false;
                                }

                                if (IsBetweenVariance(history[index].Close, instruments[instrument].middle30BB, (decimal).0006))
                                {
                                    Console.WriteLine("Time Stamp {0} CLOSE BELOW middle 30BB: Recommending to Place REVERSE SELL ORDER for Script {2} at MIDDLE 30BB {1} and the LTP is {3}", DateTime.Now.ToString("yyyyMMdd hh:mm:ss"), instruments[instrument].middle30BB, instruments[instrument].futName, ltp);
                                    instruments[instrument].isReversed = true;
                                    instruments[instrument].ReversedTime = DateTime.Now;
                                    instruments[instrument].type = OType.Sell;
                                    modifyOpenAlignOrReversedStatus(instruments[instrument], 17, OType.Sell, true);
                                    //return true;
                                }
                                else
                                {
                                    Console.WriteLine("Time Stamp {0} CLOSE BELOW middle 30BB: Recommended to Place REVERSE SELL ORDER for Script {2} at MIDDLE 30BB {1} and the LTP is {3}", DateTime.Now.ToString("yyyyMMdd hh:mm:ss"), instruments[instrument].middle30BB, instruments[instrument].futName, ltp);
                                    instruments[instrument].type = OType.Sell;
                                    instruments[instrument].shortTrigger = instruments[instrument].middle30BBnew;
                                    instruments[instrument].longTrigger = instruments[instrument].bot30bb;
                                    instruments[instrument].isReversed = true;
                                    instruments[instrument].ReversedTime = DateTime.Now;
                                    modifyOpenAlignOrReversedStatus(instruments[instrument], 17, OType.Sell, true);
                                    return;
                                }
                            }
                            else
                                Console.WriteLine("Time Stamp {0} CLOSE BELOW middle 30BB: But Script {2} is not beyond variance at MIDDLE 30BB {1} and the LTP is {3}", DateTime.Now.ToString("yyyyMMdd hh:mm:ss"), instruments[instrument].middle30BB, instruments[instrument].futName, ltp);
                        }
                    }
                    else if (instruments[instrument].type == OType.Sell && !instruments[instrument].isReversed)
                    {
                        if (history[index].Close > instruments[instrument].weekMA
                                && history[index].Close < instruments[instrument].middle30BB
                                //&& history[index].Close < instruments[instrument].middle30BBnew
                                && instruments[instrument].weekMA < instruments[instrument].middle30BBnew
                                && history[index].High >= instruments[instrument].middle30BBnew
                                && IsBetweenVariance(instruments[instrument].weekMA, instruments[instrument].middle30BBnew, (decimal).006)
                                && IsBeyondVariance(instruments[instrument].weekMA, instruments[instrument].middle30BBnew, (decimal).002)
                                && history[index - 1].Close < instruments[instrument].weekMA
                                && history[index].Close < instruments[instrument].top30bb
                                && (instruments[instrument].top30bb - history[index].Close) > mRange)
                        {
                            Console.WriteLine("Time Stamp {0}  +VE TREND: But JUST Place SELL ORDER at WEEKMA {1} for Script {2} and the LTP is {3}", DateTime.Now.ToString("yyyyMMdd hh:mm:ss"), instruments[instrument].weekMA, instruments[instrument].futName, ltp);
                            instruments[instrument].type = OType.Sell;
                            instruments[instrument].isReversed = true;
                            instruments[instrument].ReversedTime = DateTime.Now;
                            instruments[instrument].shortTrigger = instruments[instrument].middle30BBnew;
                            modifyOpenAlignOrReversedStatus(instruments[instrument], 17, OType.Sell, true);
                            //return true;
                        }
                        else if ((history[index].Open < history[index].Close || history[index].Open > instruments[instrument].middle30BBnew)
                                && history[index].Close > instruments[instrument].middle30BBnew)
                        //&& (history[index - 1].Open < instruments[instrument].middle30BB
                        //    || IsBetweenVariance(history[index - 1].Open, instruments[instrument].middle30BB, (decimal).0002))
                        //&& history[index - 1].Close < instruments[instrument].middle30BB)
                        {
                            if (IsBeyondVariance(history[index].Close, instruments[instrument].middle30BB, (decimal).0004)
                                && IsBeyondVariance(history[index].Close, instruments[instrument].middle30BBnew, (decimal).0004)
                                && IsBeyondVariance(instruments[instrument].weekMA, instruments[instrument].middle30BBnew, (decimal).002))
                            {
                                if ((history[index].Close < instruments[instrument].weekMA
                                            && IsBetweenVariance(instruments[instrument].weekMA, history[index].Close, (decimal).003))
                                        || (history[index - 1].Open >= instruments[instrument].middle30BB
                                            && history[index - 1].Close < instruments[instrument].middle30BB))
                                {
                                    Console.WriteLine("Time Stamp {0}  -VE TREND: Recommending but return false for SELL ORDER at WEEKMA {1} for Script {2} and the LTP is {3}", DateTime.Now.ToString("yyyyMMdd hh:mm:ss"), instruments[instrument].weekMA, instruments[instrument].futName, ltp);
                                    return;
                                }
                                if (VerifyNifty(timenow) == OType.Sell)
                                {
                                    Console.WriteLine("YOU COULD HAVE AVOIDED THIS ORDER");
                                    //return false;
                                }
                                if (IsBetweenVariance(history[index].Close, instruments[instrument].middle30BB, (decimal).0006))
                                {
                                    Console.WriteLine("Time Stamp {0} CLOSE ABOVE middle 30BB: Recommending to Place REVERSE BUY ORDER for Script {2} at MIDDLE 30BB {1} and the LTP is {3}", DateTime.Now.ToString("yyyyMMdd hh:mm:ss"), instruments[instrument].middle30BB, instruments[instrument].futName, ltp);
                                    instruments[instrument].isReversed = true;
                                    instruments[instrument].ReversedTime = DateTime.Now;
                                    instruments[instrument].type = OType.Buy;
                                    modifyOpenAlignOrReversedStatus(instruments[instrument], 17, OType.Buy, true);
                                    //return true;
                                }
                                else
                                {
                                    Console.WriteLine("Time Stamp {0} CLOSE ABOVE middle 30BB: Recommended to Place REVERSE BUY ORDER for Script {2} at MIDDLE 30BB {1} and the LTP is {3}", DateTime.Now.ToString("yyyyMMdd hh:mm:ss"), instruments[instrument].middle30BB, instruments[instrument].futName, ltp);
                                    instruments[instrument].type = OType.Buy;
                                    instruments[instrument].shortTrigger = instruments[instrument].top30bb;
                                    instruments[instrument].longTrigger = instruments[instrument].middle30BBnew;
                                    instruments[instrument].isReversed = true;
                                    instruments[instrument].ReversedTime = DateTime.Now;
                                    modifyOpenAlignOrReversedStatus(instruments[instrument], 17, OType.Buy, true);
                                    return;
                                }
                            }
                            else
                                Console.WriteLine("Time Stamp {0} CLOSE ABOVE middle 30BB: But Script {2} is Not beyond Variance at MIDDLE 30BB {1} and the LTP is {3}", DateTime.Now.ToString("yyyyMMdd hh:mm:ss"), instruments[instrument].middle30BB, instruments[instrument].futName, ltp);
                        }
                    }
                    else if (instruments[instrument].type == OType.Buy && instruments[instrument].isReversed)
                    {
                        if (history[index].Close < instruments[instrument].middle30BBnew)
                        {
                            Console.WriteLine("Time Stamp {0} Script {2} is Back to Track from Reverse (BUY to Sell) as close is again below MIDDLE 30BB {1} and the LTP is {3}", DateTime.Now.ToString("yyyyMMdd hh:mm:ss"), instruments[instrument].middle30BB, instruments[instrument].futName, ltp);
                            CloseOrderTicker(instrument);
                        }
                        else if (history[index].Close < instruments[instrument].middle30ma50
                                && (history[index].High >= instruments[instrument].middle30ma50
                                    || IsBetweenVariance(history[index].High, instruments[instrument].middle30ma50, (decimal).0006))
                                && instruments[instrument].middle30ma50 < instruments[instrument].top30bb)
                        {
                            //&& (IsBetweenVariance(history[index].High, instruments[instrument].res1, (decimal).0006)
                            //|| history[index].High < instruments[instrument].res1)
                            Console.WriteLine("Time Stamp {0} Trusted Script {1} has just stopped below MA50 {2} as current high is {3}. hence watchlist is closing this script", DateTime.Now.ToString("yyyyMMdd hh:mm:ss"), instruments[instrument].futName, instruments[instrument].middle30ma50, history[index].High);
                            instruments[instrument].isOpenAlign = false;
                            instruments[instrument].canTrust = false;
                            instruments[instrument].isReversed = false;
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
                            CloseOrderTicker(instrument);
                        }
                        else if (history[index].Close > instruments[instrument].middle30ma50
                                && (history[index].Low <= instruments[instrument].middle30ma50
                                    || IsBetweenVariance(history[index].Low, instruments[instrument].middle30ma50, (decimal).0006))
                                && instruments[instrument].middle30ma50 > instruments[instrument].bot30bb)
                        {
                            //&& (IsBetweenVariance(history[index].Low, instruments[instrument].sup1, (decimal).0006)
                            //|| history[index].Low < instruments[instrument].sup1)
                            Console.WriteLine("Time Stamp {0} Trusted Script {1} has just stopped above MA50 {2} as current low is {3}. hence watchlist is closing this script", DateTime.Now.ToString("yyyyMMdd hh:mm:ss"), instruments[instrument].futName, instruments[instrument].middle30ma50, history[index].Low);
                            instruments[instrument].isOpenAlign = false;
                            instruments[instrument].canTrust = false;
                            instruments[instrument].isReversed = false;
                        }
                        else
                            Console.WriteLine("Time Stamp {0} Script {2} is Reversed for SELL and waiting for perfect trigger at MIDDLE 30BB {1} and the LTP is {3}", DateTime.Now.ToString("yyyyMMdd hh:mm:ss"), instruments[instrument].middle30BB, instruments[instrument].futName, ltp);
                    }
                    #endregion 
                }
                else if (!instruments[instrument].isOpenAlign)
                {
                    if (instruments[instrument].type == OType.Buy && instruments[instrument].isReversed)
                    {
                        if (history[index].Close < instruments[instrument].middle30BBnew)
                        {
                            Console.WriteLine("NOT OPEN ALIGN Time Stamp {0} Script {2} is Back to Track from Reverse (BUY to Sell) as close is again below MIDDLE 30BB {1} and the LTP is {3}", DateTime.Now.ToString("yyyyMMdd hh:mm:ss"), instruments[instrument].middle30BB, instruments[instrument].futName, ltp);
                            CloseOrderTicker(instrument);
                        }
                        else
                            Console.WriteLine("NOT OPEN ALIGN Time Stamp {0} Script {2} is Reversed for BUY and waiting for perfect trigger at MIDDLE 30BB {1} and the LTP is {3}", DateTime.Now.ToString("yyyyMMdd hh:mm:ss"), instruments[instrument].middle30BB, instruments[instrument].futName, ltp);
                    }
                    else if (instruments[instrument].type == OType.Sell && instruments[instrument].isReversed)
                    {
                        if (history[index].Close > instruments[instrument].middle30BBnew)
                        {
                            Console.WriteLine("NOT OPEN ALIGN Time Stamp {0} Script {2} is Back to Track from Reverse (SELL to Buy) as close is again above MIDDLE 30BB {1} and the LTP is {3}", DateTime.Now.ToString("yyyyMMdd hh:mm:ss"), instruments[instrument].middle30BB, instruments[instrument].futName, ltp);
                            CloseOrderTicker(instrument);
                        }
                        else
                            Console.WriteLine("NOT OPEN ALIGN Time Stamp {0} Script {2} is Reversed for SELL and waiting for perfect trigger at MIDDLE 30BB {1} and the LTP is {3}", DateTime.Now.ToString("yyyyMMdd hh:mm:ss"), instruments[instrument].middle30BB, instruments[instrument].futName, ltp);
                    }
                    else if (instruments[instrument].type == OType.Buy
                        && !instruments[instrument].isReversed)
                    {
                        if (history[index].Close < instruments[instrument].weekMA
                                && history[index].Close > instruments[instrument].middle30BB
                                //&& history[index].Close > instruments[instrument].middle30BBnew
                                && instruments[instrument].weekMA > instruments[instrument].middle30BBnew
                                && history[index].Low <= instruments[instrument].middle30BBnew
                                && IsBetweenVariance(instruments[instrument].weekMA, instruments[instrument].middle30BBnew, (decimal).006)
                                && IsBeyondVariance(instruments[instrument].weekMA, instruments[instrument].middle30BBnew, (decimal).002)
                                && history[index].Close > instruments[instrument].bot30bb
                                && (history[index].Close - instruments[instrument].bot30bb) > mRange)
                        {
                            Console.WriteLine("NOT OPEN ALIGN Time Stamp {0}  -VE TREND: But JUST Place BUY ORDER at WEEKMA {1} for Script {2} and the LTP is {3}", DateTime.Now.ToString("yyyyMMdd hh:mm:ss"), instruments[instrument].weekMA, instruments[instrument].futName, ltp);
                            instruments[instrument].type = OType.Buy;
                            instruments[instrument].isReversed = true;
                            instruments[instrument].isOpenAlign = true;
                            instruments[instrument].ReversedTime = DateTime.Now;
                            instruments[instrument].longTrigger = instruments[instrument].middle30BBnew;
                            modifyOpenAlignOrReversedStatus(instruments[instrument], 17, OType.Buy, true);
                            //return true;
                        }
                        else if ((history[index].Open > history[index].Close || history[index].Open < instruments[instrument].middle30BBnew)
                                && history[index].Close < instruments[instrument].middle30BBnew)
                        //&& (history[index - 1].Open > instruments[instrument].middle30BB
                        //    || IsBetweenVariance(history[index - 1].Open, instruments[instrument].middle30BB, (decimal).0002))
                        //&& history[index - 1].Close > instruments[instrument].middle30BB)
                        {
                            if (IsBeyondVariance(history[index].Close, instruments[instrument].middle30BB, (decimal).0004)
                                && IsBeyondVariance(history[index].Close, instruments[instrument].middle30BBnew, (decimal).0004))
                            {
                                if (history[index].Close > instruments[instrument].weekMA
                                        && IsBetweenVariance(history[index].Close, instruments[instrument].weekMA, (decimal).003))
                                {
                                    Console.WriteLine("NOT OPEN ALIGN Time Stamp {0}  -VE TREND: Recommending but return false for Buy ORDER at WEEKMA {1} for Script {2} and the LTP is {3}", DateTime.Now.ToString("yyyyMMdd hh:mm:ss"), instruments[instrument].weekMA, instruments[instrument].futName, ltp);
                                    return;
                                }
                                if (VerifyNifty(timenow) == OType.Buy)
                                {
                                    Console.WriteLine("NOT OPEN ALIGN YOU COULD HAVE AVOIDED THIS ORDER");
                                    //return false;
                                }

                                if (IsBetweenVariance(history[index].Close, instruments[instrument].middle30BB, (decimal).0006))
                                {
                                    Console.WriteLine("NOT OPEN ALIGN Time Stamp {0} CLOSE BELOW middle 30BB: Recommending to Place REVERSE SELL ORDER for Script {2} at MIDDLE 30BB {1} and the LTP is {3}", DateTime.Now.ToString("yyyyMMdd hh:mm:ss"), instruments[instrument].middle30BB, instruments[instrument].futName, ltp);
                                    instruments[instrument].isReversed = true;
                                    instruments[instrument].isOpenAlign = true;
                                    instruments[instrument].ReversedTime = DateTime.Now;
                                    instruments[instrument].type = OType.Sell;
                                    modifyOpenAlignOrReversedStatus(instruments[instrument], 17, OType.Sell, true);
                                    //return true;
                                }
                                else
                                {
                                    Console.WriteLine("NOT OPEN ALIGN Time Stamp {0} CLOSE BELOW middle 30BB: Recommended to Place REVERSE SELL ORDER for Script {2} at MIDDLE 30BB {1} and the LTP is {3}", DateTime.Now.ToString("yyyyMMdd hh:mm:ss"), instruments[instrument].middle30BB, instruments[instrument].futName, ltp);
                                    instruments[instrument].type = OType.Sell;
                                    instruments[instrument].shortTrigger = instruments[instrument].middle30BBnew;
                                    instruments[instrument].longTrigger = instruments[instrument].bot30bb;
                                    instruments[instrument].isReversed = true;
                                    instruments[instrument].isOpenAlign = true;
                                    instruments[instrument].ReversedTime = DateTime.Now;
                                    modifyOpenAlignOrReversedStatus(instruments[instrument], 17, OType.Sell, true);
                                    return;
                                }
                            }
                            else
                                Console.WriteLine("NOT OPEN ALIGN Time Stamp {0} CLOSE BELOW middle 30BB: But Script {2} is not beyond variance at MIDDLE 30BB {1} and the LTP is {3}", DateTime.Now.ToString("yyyyMMdd hh:mm:ss"), instruments[instrument].middle30BB, instruments[instrument].futName, ltp);
                        }
                    }
                    else if (instruments[instrument].type == OType.Sell && !instruments[instrument].isReversed)
                    {
                        if (history[index].Close > instruments[instrument].weekMA
                                && history[index].Close < instruments[instrument].middle30BB
                                //&& history[index].Close < instruments[instrument].middle30BBnew
                                && instruments[instrument].weekMA < instruments[instrument].middle30BBnew
                                && history[index].High >= instruments[instrument].middle30BBnew
                                && IsBetweenVariance(instruments[instrument].weekMA, instruments[instrument].middle30BBnew, (decimal).006)
                                && IsBeyondVariance(instruments[instrument].weekMA, instruments[instrument].middle30BBnew, (decimal).002)
                                && history[index].Close < instruments[instrument].top30bb
                                && (instruments[instrument].top30bb - history[index].Close) > mRange)
                        {
                            Console.WriteLine("NOT OPEN ALIGN Time Stamp {0}  +VE TREND: But JUST Place SELL ORDER at WEEKMA {1} for Script {2} and the LTP is {3}", DateTime.Now.ToString("yyyyMMdd hh:mm:ss"), instruments[instrument].weekMA, instruments[instrument].futName, ltp);
                            instruments[instrument].type = OType.Sell;
                            instruments[instrument].isReversed = true;
                            instruments[instrument].isOpenAlign = true;
                            instruments[instrument].ReversedTime = DateTime.Now;
                            instruments[instrument].shortTrigger = instruments[instrument].middle30BBnew;
                            modifyOpenAlignOrReversedStatus(instruments[instrument], 17, OType.Sell, true);
                            //return true;
                        }
                        else if ((history[index].Open < history[index].Close || history[index].Open > instruments[instrument].middle30BBnew)
                                && history[index].Close > instruments[instrument].middle30BBnew)
                        //&& (history[index - 1].Open < instruments[instrument].middle30BB
                        //    || IsBetweenVariance(history[index - 1].Open, instruments[instrument].middle30BB, (decimal).0002))
                        //&& history[index - 1].Close < instruments[instrument].middle30BB)
                        {
                            if (IsBeyondVariance(history[index].Close, instruments[instrument].middle30BB, (decimal).0004)
                                && IsBeyondVariance(history[index].Close, instruments[instrument].middle30BBnew, (decimal).0004))
                            {
                                if (history[index].Close < instruments[instrument].weekMA
                                            && IsBetweenVariance(instruments[instrument].weekMA, history[index].Close, (decimal).003))
                                {
                                    Console.WriteLine("NOT OPEN ALIGN Time Stamp {0}  -VE TREND: Recommending but return false for SELL ORDER at WEEKMA {1} for Script {2} and the LTP is {3}", DateTime.Now.ToString("yyyyMMdd hh:mm:ss"), instruments[instrument].weekMA, instruments[instrument].futName, ltp);
                                    return;
                                }
                                if (VerifyNifty(timenow) == OType.Sell)
                                {
                                    Console.WriteLine("NOT OPEN ALIGN YOU COULD HAVE AVOIDED THIS ORDER");
                                    //return false;
                                }
                                if (IsBetweenVariance(history[index].Close, instruments[instrument].middle30BB, (decimal).0006))
                                {
                                    Console.WriteLine("NOT OPEN ALIGN Time Stamp {0} CLOSE ABOVE middle 30BB: Recommending to Place REVERSE BUY ORDER for Script {2} at MIDDLE 30BB {1} and the LTP is {3}", DateTime.Now.ToString("yyyyMMdd hh:mm:ss"), instruments[instrument].middle30BB, instruments[instrument].futName, ltp);
                                    instruments[instrument].isReversed = true;
                                    instruments[instrument].isOpenAlign = true;
                                    instruments[instrument].ReversedTime = DateTime.Now;
                                    instruments[instrument].type = OType.Buy;
                                    modifyOpenAlignOrReversedStatus(instruments[instrument], 17, OType.Buy, true);
                                    //return true;
                                }
                                else
                                {
                                    Console.WriteLine("NOT OPEN ALIGN Time Stamp {0} CLOSE ABOVE middle 30BB: Recommended to Place REVERSE BUY ORDER for Script {2} at MIDDLE 30BB {1} and the LTP is {3}", DateTime.Now.ToString("yyyyMMdd hh:mm:ss"), instruments[instrument].middle30BB, instruments[instrument].futName, ltp);
                                    instruments[instrument].type = OType.Buy;
                                    instruments[instrument].shortTrigger = instruments[instrument].top30bb;
                                    instruments[instrument].longTrigger = instruments[instrument].middle30BBnew;
                                    instruments[instrument].isReversed = true;
                                    instruments[instrument].isOpenAlign = true;
                                    instruments[instrument].ReversedTime = DateTime.Now;
                                    modifyOpenAlignOrReversedStatus(instruments[instrument], 17, OType.Buy, true);
                                    return;
                                }
                            }
                            else
                                Console.WriteLine("NOT OPEN ALIGN Time Stamp {0} CLOSE ABOVE middle 30BB: But Script {2} is Not beyond Variance at MIDDLE 30BB {1} and the LTP is {3}", DateTime.Now.ToString("yyyyMMdd hh:mm:ss"), instruments[instrument].middle30BB, instruments[instrument].futName, ltp);
                        }
                    }
                }
            }
        }

        bool checkForReverseOrder(uint instrument, Tick tickData)
        {
            decimal timenow = (decimal)DateTime.Now.Hour + ((decimal)DateTime.Now.Minute / 100);
            decimal ltp = tickData.LastPrice;
            decimal range;
            OType cTrend = CalculateSqueezedTrend(instruments[instrument].futName, instruments[instrument].history, 10);
            if (instruments[instrument].top30bb > instruments[instrument].middle30ma50
                    && instruments[instrument].middle30BBnew < instruments[instrument].middle30ma50
                    && cTrend == OType.StrongBuy)
            {
                range = Convert.ToDecimal(((ltp * (decimal)3.5) / 100).ToString("#.#"));
                if ((instruments[instrument].bot30bb + range) < instruments[instrument].top30bb)
                {
                    if (IsBetweenVariance(instruments[instrument].middleBB, ltp, (decimal).002)
                        && instruments[instrument].ma50 > instruments[instrument].middleBB)
                    {
                        Console.WriteLine("Time Stamp {0} Raising UP 3.5: Recommending to Place REVERSE BUY ORDER for Script {1} at 50 MA {1} and the LTP is {3}", DateTime.Now.ToString("yyyyMMdd hh:mm:ss"), instruments[instrument].futName, instruments[instrument].middle30ma50, ltp);
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
                            && IsBetweenVariance(instruments[instrument].topBB, ltp, (decimal).002)
                            && instruments[instrument].ma50 > instruments[instrument].topBB)
                        {
                            Console.WriteLine("Time Stamp {0} Raising UP 2.8: Recommending to Place REVERSE BUY ORDER for Script {1} at 50 MA {1} and the LTP is {3}", DateTime.Now.ToString("yyyyMMdd hh:mm:ss"), instruments[instrument].futName, instruments[instrument].middle30ma50, ltp);
                            //instruments[instrument].type = OType.Sell;
                            //return true;
                        }
                    }
                }
            }
            else if (instruments[instrument].bot30bb < instruments[instrument].middle30ma50
                    && instruments[instrument].middle30BBnew > instruments[instrument].middle30ma50
                    && cTrend == OType.StrongSell)
            {
                range = Convert.ToDecimal(((ltp * (decimal)3.5) / 100).ToString("#.#"));
                if ((instruments[instrument].bot30bb + range) < instruments[instrument].top30bb)
                {
                    if (IsBetweenVariance(instruments[instrument].middleBB, ltp, (decimal).002)
                        && instruments[instrument].ma50 > instruments[instrument].middleBB)
                    {
                        Console.WriteLine("Time Stamp {0} Falling DOWN 3.5: Recommending to Place REVERSE SELL ORDER for Script {1} at 50 MA {1} and the LTP is {3}", DateTime.Now.ToString("yyyyMMdd hh:mm:ss"), instruments[instrument].futName, instruments[instrument].middle30ma50, ltp);
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
                            && IsBetweenVariance(instruments[instrument].topBB, ltp, (decimal).002)
                            && instruments[instrument].ma50 > instruments[instrument].topBB)
                        {
                            Console.WriteLine("Time Stamp {0} Falling DOWN 2.8: Recommending to Place REVERSE SELL ORDER for Script {1} at 50 MA {1} and the LTP is {3}", DateTime.Now.ToString("yyyyMMdd hh:mm:ss"), instruments[instrument].futName, instruments[instrument].middle30ma50, ltp);
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
                string temp = (value1 + value1 * (decimal)variance).ToString("#.#");
                if (temp.Length == 0)
                    temp = ".05";
                decimal r1 = Convert.ToDecimal(temp);
                temp = (value1 - value1 * (decimal)variance).ToString("#.#");
                if (temp.Length == 0)
                    temp = ".05";
                decimal r2 = Convert.ToDecimal(temp);
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
                string temp = (value1 + value1 * (decimal)variance).ToString("#.#");
                if (temp.Length == 0)
                    temp = ".05";
                decimal r1 = Convert.ToDecimal(temp);
                temp = (value1 - value1 * (decimal)variance).ToString("#.#");
                if (temp.Length == 0)
                    temp = ".05";
                decimal r2 = Convert.ToDecimal(temp);
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

        public void placeOrder(uint instrument, decimal difference)
        {
            try
            {
                Dictionary<string, dynamic> response;
                //double r1 = Convert.ToDouble((ltp - ltp * .0005).ToString("#.#"));
                Quote ltp = new Quote();

                decimal target, stopLoss, trigger, percent;
                decimal timenow = (decimal)DateTime.Now.Hour + ((decimal)DateTime.Now.Minute / 100);
                OType niftySide = VerifyNifty(timenow);
                //OType trend = bt.CalculateSqueezedTrend(instrument, history, 6);
                stopLoss = (decimal)14000 / (decimal)instruments[instrument].lotSize;
                string sl = stopLoss.ToString("#.#");
                stopLoss = Convert.ToDecimal(sl);
                if (!instruments[instrument].isHedgingOrder)
                    instruments[instrument].isHedgingOrder = true;
                else
                    instruments[instrument].isHedgingOrder = false;

                try
                {
                    Dictionary<string, Quote> dicLtp = kite.GetQuote(new string[] { instruments[instrument].futId.ToString() });
                    dicLtp.TryGetValue(instruments[instrument].futId.ToString(), out ltp);
                }
                catch (Exception ex)
                {
                    Console.WriteLine("EXCEPTION CAUGHT while Placing Order Trigger :: " + ex.Message);
                    return;
                }
                OType currentTrend = CalculateSqueezedTrend(instruments[instrument].futName,
                    instruments[instrument].history,
                    10);

                decimal variance15 = (ltp.LastPrice * (decimal)1.5) / 100;
                decimal variance2 = (ltp.LastPrice * (decimal)2) / 100;
                decimal variance25 = (ltp.LastPrice * (decimal)2.5) / 100;
                decimal variance3 = (ltp.LastPrice * (decimal)3) / 100;
                decimal variance34 = (ltp.LastPrice * (decimal)3.4) / 100;
                if ((instruments[instrument].bot30bb + variance2) > instruments[instrument].top30bb
                        || IsBetweenVariance((instruments[instrument].bot30bb + variance2), instruments[instrument].top30bb, (decimal).0006))
                {
                    #region Target for Narrowed Script
                    //if (instruments[instrument].isReversed)
                    //    if (Decimal.Compare(timenow, (decimal)11.45) < 0)
                    //        return;
                    DateTime previousDay;
                    DateTime currentDay;
                    getDays(out previousDay, out currentDay);
                    if (Decimal.Compare(timenow, (decimal)13.15) > 0 && !instruments[instrument].isReorder)
                    {
                        if ((niftySide == OType.Sell && instruments[instrument].type == OType.Buy)
                            || (niftySide == OType.Buy && instruments[instrument].type == OType.Sell)
                            || (Decimal.Compare(timenow, (decimal)13.59) > 0
                                && ((instruments[instrument].bot30bb + variance15) > instruments[instrument].top30bb
                                    || IsBetweenVariance((instruments[instrument].bot30bb + variance15), instruments[instrument].top30bb, (decimal).001))))
                        {
                            Console.WriteLine("Closing the script as it is very very narrow. Not worth the risk");
                            CloseOrderTicker(instrument);
                            return;
                        }
                        else
                            Console.WriteLine("it is Very Narrow Script. Better you handle the proceedings");
                    }
                    instruments[instrument].target = 1200;
                    //int expected = ExpectedCandleCount(instruments[instrument].ReversedTime);
                    List<Historical> history = GetHistory(instrument, previousDay, currentDay);
                    OType trend = CalculateSqueezedTrend(instruments[instrument].weekMA,
                        instruments[instrument].futName,
                        instruments[instrument].close,
                        instruments[instrument].top30bb,
                        instruments[instrument].middle30BB,
                        instruments[instrument].bot30bb,
                        history);
                    OType cTrend = OType.BS;
                    if (currentTrend == OType.StrongBuy || currentTrend == OType.Buy)
                        cTrend = OType.Buy;
                    else if (currentTrend == OType.StrongSell || currentTrend == OType.Sell)
                        cTrend = OType.Sell;
                    if (cTrend == instruments[instrument].type
                        && trend != instruments[instrument].type
                        && cTrend != OType.BS)
                    {
                        Console.WriteLine("Last 14 candles range are aligning to primary 30min trend for script {0}", instruments[instrument].futName);
                        trend = instruments[instrument].type;
                    }
                    if (instruments[instrument].type == OType.Sell
                        && niftySide == OType.Buy
                        && trend == OType.Buy)
                    {
                        instruments[instrument].type = OType.Buy;
                    }
                    else if (instruments[instrument].type == OType.Buy
                        && niftySide == OType.Sell
                        && trend == OType.Sell)
                    {
                        instruments[instrument].type = OType.Sell;
                    }
                    else if ((instruments[instrument].bot30bb + variance15) > instruments[instrument].top30bb
                            || IsBetweenVariance((instruments[instrument].bot30bb + variance15), instruments[instrument].top30bb, (decimal).001))
                    {
                        CloseOrderTicker(instrument);
                        return;
                    }
                    if (instruments[instrument].isReversed
                        && trend != OType.BS)
                    {
                        //TimeSpan candleTime = instruments[instrument].ReversedTime.TimeOfDay;
                        //TimeSpan timeDiff = DateTime.Now.TimeOfDay.Subtract(candleTime);
                        instruments[instrument].target = 1500;
                        Console.WriteLine("IS THIS WHAT YOU WANT VARUN???? JAKPOT as Reversed time is {0} along with Narrow size at {1}", instruments[instrument].ReversedTime, DateTime.Now.ToString());
                    }
                    else
                        Console.WriteLine("Script Reversed time is {0} along with Narrow size at {1}", instruments[instrument].ReversedTime, DateTime.Now.ToString());
                    #endregion
                }
                else if(!instruments[instrument].canTrust)
                {
                    instruments[instrument].target = 1600;
                    Console.WriteLine("Cannot be Trusted this Script:: hence choosing smaller target as {0}", instruments[instrument].target);
                }
                else if (Decimal.Compare(timenow, (decimal)13.30) >= 0
                    && !instruments[instrument].isReorder)
                {
                    percent = (ltp.LastPrice * (decimal).3) / 100;
                    sl = percent.ToString("#.#");
                    percent = Convert.ToDecimal(sl);
                    target = (2300 / (decimal)instruments[instrument].lotSize);
                    sl = target.ToString("#.#");
                    target = Convert.ToDecimal(sl);

                    if ((instruments[instrument].bot30bb + variance3) < instruments[instrument].top30bb
                            || IsBetweenVariance((instruments[instrument].bot30bb + variance3), instruments[instrument].top30bb, (decimal).0006)
                        && percent > target)
                    {
                        instruments[instrument].target = 1500;
                        Console.WriteLine("Though Past 1.30PM, Maximum TARGET 4300 can be chosen; But chosen {0}", instruments[instrument].target);
                    }
                    else
                    {
                        instruments[instrument].target = 1500;
                        Console.WriteLine("Minimal TARGET:: Time is past 1.30PM hence choosing smaller target as {0}", instruments[instrument].target);
                    }
                }
                else if (Decimal.Compare(timenow, (decimal)10.30) < 0)
                {
                    instruments[instrument].target = 1500;
                    Console.WriteLine("Early Trade Target:: Time is less than 10.30AM hence choosing smaller target as {0}", instruments[instrument].target);
                }
                else if (((instruments[instrument].bot30bb + variance3) < instruments[instrument].top30bb
                            || IsBetweenVariance((instruments[instrument].bot30bb + variance3), instruments[instrument].top30bb, (decimal).0006))
                        && !instruments[instrument].isReorder)
                {
                    instruments[instrument].target = 2400;
                    percent = (ltp.LastPrice * (decimal).7) / 100;
                    sl = percent.ToString("#.#");
                    percent = Convert.ToDecimal(sl);
                    target = (instruments[instrument].target / (decimal)instruments[instrument].lotSize);
                    sl = target.ToString("#.#");
                    target = Convert.ToDecimal(sl);
                    if (percent < target)
                    {
                        instruments[instrument].target = (int)(percent * instruments[instrument].lotSize);
                    }
                    Console.WriteLine("Maximum TARGET:: Time is past 10.15PM hence choosing maximum target as {0}", instruments[instrument].target);
                }
                else if (instruments[instrument].isReorder)
                {
                    if (Decimal.Compare(timenow, (decimal)10.18) < 0)
                        instruments[instrument].target = 2300;
                    else if ((instruments[instrument].bot30bb + variance34) < instruments[instrument].top30bb)
                    {
                        instruments[instrument].target = 4300;
                        percent = (ltp.LastPrice * (decimal)1.25) / 100;
                        sl = percent.ToString("#.#");
                        percent = Convert.ToDecimal(sl);
                        target = (instruments[instrument].target / (decimal)instruments[instrument].lotSize);
                        sl = target.ToString("#.#");
                        target = Convert.ToDecimal(sl);
                        if (percent < target)
                        {
                            instruments[instrument].target = (int)(percent * instruments[instrument].lotSize);
                        }
                        if (Decimal.Compare(timenow, (decimal)14.10) > 0 && instruments[instrument].target > 2200)
                            instruments[instrument].target = 2200;
                        else if (Decimal.Compare(timenow, (decimal)13.10) > 0 && instruments[instrument].target > 2800)
                            instruments[instrument].target = 2400;
                        Console.WriteLine("Jackpot TARGET:: as chosen target is {0}", instruments[instrument].target);
                    }
                    else if ((instruments[instrument].bot30bb + variance3) < instruments[instrument].top30bb
                        || IsBetweenVariance((instruments[instrument].bot30bb + variance3), instruments[instrument].top30bb, (decimal).0006))
                    {
                        instruments[instrument].target = 2400;
                        percent = (ltp.LastPrice * (decimal)1) / 100;
                        sl = percent.ToString("#.#");
                        percent = Convert.ToDecimal(sl);
                        target = (instruments[instrument].target / (decimal)instruments[instrument].lotSize);
                        sl = target.ToString("#.#");
                        target = Convert.ToDecimal(sl);
                        if (percent < target)
                        {
                            instruments[instrument].target = (int)(percent * instruments[instrument].lotSize);
                        }
                        if (Decimal.Compare(timenow, (decimal)14.10) > 0 && instruments[instrument].target > 2200)
                            instruments[instrument].target = 2200;
                        else if (Decimal.Compare(timenow, (decimal)13.10) > 0 && instruments[instrument].target > 2800)
                            instruments[instrument].target = 2400;
                        Console.WriteLine("Lucky TARGET:: as chosed target is {0}", instruments[instrument].target);
                    }
                    else if ((instruments[instrument].bot30bb + variance25) < instruments[instrument].top30bb
                        || IsBetweenVariance((instruments[instrument].bot30bb + variance25), instruments[instrument].top30bb, (decimal).0006))
                    {
                        instruments[instrument].target = 2400;
                        percent = (ltp.LastPrice * (decimal).75) / 100;
                        sl = percent.ToString("#.#");
                        percent = Convert.ToDecimal(sl);
                        target = (instruments[instrument].target / (decimal)instruments[instrument].lotSize);
                        sl = target.ToString("#.#");
                        target = Convert.ToDecimal(sl);
                        if (percent < target)
                        {
                            instruments[instrument].target = (int)(percent * instruments[instrument].lotSize);
                        }
                        if (Decimal.Compare(timenow, (decimal)14.10) > 0 && instruments[instrument].target > 1800)
                            instruments[instrument].target = 1800;
                        else if (Decimal.Compare(timenow, (decimal)13.10) > 0 && instruments[instrument].target > 2300)
                            instruments[instrument].target = 2300;
                        Console.WriteLine("Decent TARGET:: as chosed target is {0}", instruments[instrument].target);
                    }
                    else if ((instruments[instrument].bot30bb + variance2) < instruments[instrument].top30bb
                        || IsBetweenVariance((instruments[instrument].bot30bb + variance2), instruments[instrument].top30bb, (decimal).0006))
                    {
                        instruments[instrument].target = 1300;
                        Console.WriteLine("Safe TARGET:: as chosed target is {0}", instruments[instrument].target);
                    }
                    else
                    {
                        instruments[instrument].target = 1700;
                        Console.WriteLine("Regular TARGET 1:: as chosed target is {0}", instruments[instrument].target);
                    }
                }
                else
                {
                    percent = (ltp.LastPrice * (decimal).6) / 100;
                    sl = percent.ToString("#.#");
                    percent = Convert.ToDecimal(sl);
                    target = (instruments[instrument].target / (decimal)instruments[instrument].lotSize);
                    sl = target.ToString("#.#");
                    target = Convert.ToDecimal(sl);
                    if (percent < target)
                    {
                        instruments[instrument].target = (int)(percent * instruments[instrument].lotSize);
                    }
                    Console.WriteLine("Regular TARGET 2:: as chosed target is {0}", instruments[instrument].target);
                }
                target = (instruments[instrument].target / (decimal)instruments[instrument].lotSize);
                sl = target.ToString("#.#");
                target = Convert.ToDecimal(sl);
                /*
                Console.WriteLine(Utils.JsonSerialize(ltp));
                Dictionary<string, Quote> quotes = kite.GetQuote(InstrumentId: new string[] { "NSE:INFY", "NSE:ASHOKLEY", "NSE:HINDALCO19JANFUT" });
                Dictionary<string, Ltp> quotes = kite.GetLtp(new string[] { mToken.futId.ToString() });
                Dictionary<string, OHLC> ohlc = kite.GetOHLC(new string[] { mToken.futId.ToString() });
                Console.WriteLine(Utils.JsonSerialize(quotes));
                Console.WriteLine(Utils.JsonSerialize(ohlc));*/

                try
                {
                    if (instruments[instrument].isHedgingOrder)
                        Console.WriteLine("Successfully identified this is a hedging order and we made it");
                    else if (!instruments[instrument].isHedgingOrder)
                        Console.WriteLine("This is a Reverse ORDER. not a hedging order!! yipppie");
                    if (instruments[instrument].type == OType.Sell)
                    {
                        if (!instruments[instrument].canTrust)
                        {
                            trigger = ltp.Bids[1].Price;
                            Console.WriteLine("We Cannot Trust this script. So choosing undeniable short trigger");
                            //if (trigger > 320)
                            //{
                            //    trigger = ltp.Bids[0].Price;
                            //}
                        }
                        else if (instruments[instrument].status == Status.POSITION || !instruments[instrument].isReversed)
                        {
                            if (!instruments[instrument].isReversed && niftySide == OType.Buy)
                            {
                                Console.WriteLine("This is a Raising Script. So choosing Safe trigger");
                                trigger = ltp.Offers[4].Price;
                            }
                            else
                            {
                                Console.WriteLine("This is a RE-ORDER. So choosing undeniable Short trigger");
                                trigger = ltp.Bids[1].Price;
                            }
                            instruments[instrument].status = Status.OPEN;
                        }
                        else
                        {
                            if (Decimal.Compare(timenow, (decimal)10.37) < 0)
                            {
                                Console.WriteLine("Immediate Short Trigger Price is chosen - 1");
                                trigger = ltp.Bids[0].Price;
                            }
                            else if ((instruments[instrument].bot30bb + variance2) > instruments[instrument].top30bb)
                            {
                                if (ltp.LastPrice < 200)
                                    trigger = ltp.Offers[4].Price + (decimal).05;
                                else if (ltp.LastPrice > 650)
                                    trigger = ltp.Offers[4].Price + 1;
                                else
                                    trigger = ltp.Offers[4].Price;
                                if (currentTrend == OType.StrongSell && Decimal.Compare(timenow, (decimal)13.30) < 0)
                                {
                                    Console.WriteLine("narrowed but Quick Short Trigger Price is chosen as LTP is {0}", ltp.LastPrice);
                                    trigger = ltp.Offers[0].Price;
                                }
                                else
                                    Console.WriteLine("CoverUp Short Trigger Price is chosen as LTP is {0}", ltp.LastPrice);
                            }
                            else if (instruments[instrument].target < 2500)
                            {
                                if (ltp.LastPrice < 150)
                                {
                                    Console.WriteLine("Nearer Short Trigger Price is chosen - 1");
                                    trigger = ltp.Offers[0].Price; //ltp.Bids[0].Price; //ltp.Close - difference or ltp.LastPrice;
                                }
                                else if (ltp.LastPrice < 200)
                                {
                                    Console.WriteLine("Nearer Short Trigger Price is chosen - 2");
                                    trigger = ltp.Offers[0].Price;
                                }
                                else
                                {
                                    trigger = ltp.Offers[1].Price;
                                    if (currentTrend == OType.StrongSell && Decimal.Compare(timenow, (decimal)13.30) < 0)
                                    {
                                        Console.WriteLine("Quick Short Trigger Price is chosen as LTP is {0}", ltp.LastPrice);
                                        trigger = ltp.Bids[0].Price;
                                    }
                                    else
                                        Console.WriteLine("Distant Short Trigger Price is chosen");
                                }
                            }
                            else
                            {
                                trigger = ltp.Offers[2].Price;
                                Console.WriteLine("Far Distant Short Trigger Price is chosen");
                            }
                        }
                        Console.WriteLine("Spot varied by {0}; Future trading at {1} with previous close {2}; Hence chosen Trigger is {3} as FUT variance is {4}",
                            difference.ToString(), ltp.LastPrice.ToString(), ltp.Close.ToString(), trigger.ToString(), (ltp.LastPrice - ltp.Close).ToString());

                        int lotSize = instruments[instrument].lotSize;
                        //if (Decimal.Compare(timenow, (decimal)10.45) < 0)
                        //    lotSize = lotSize + 1;
                        response = kite.PlaceOrder(
                            Exchange: Constants.EXCHANGE_NFO,
                            TradingSymbol: instruments[instrument].futName,
                            TransactionType: Constants.TRANSACTION_TYPE_SELL,
                            Quantity: lotSize,
                            Price: trigger,
                            Product: Constants.PRODUCT_MIS,
                            OrderType: Constants.ORDER_TYPE_LIMIT,
                            StoplossValue: stopLoss,
                            SquareOffValue: target,
                            Validity: Constants.VALIDITY_DAY,
                            Variety: Constants.VARIETY_BO
                            );

                        Console.WriteLine("At Time {0} SELL Order STATUS:::: {1}", DateTime.Now.ToString("yyyyMMdd hh:mm:ss"), Utils.JsonSerialize(response));
                        //instruments[instrument].status = Status.STANDING;
                        Console.WriteLine("SELL Order Details: Instrument ID : {0}; Quantity : {1}; Price : {2}; SL : {3}; Target {4} ", instruments[instrument].futName, instruments[instrument].lotSize, ltp.LastPrice, stopLoss, target);
                    }
                    if (instruments[instrument].type == OType.Buy)
                    {
                        if (!instruments[instrument].canTrust)
                        {
                            trigger = ltp.Offers[1].Price;
                            Console.WriteLine("We Cannot Trust this script. So choosing undeniable long trigger");
                            //if (trigger > 320)
                            //{
                            //    trigger = ltp.Offers[0].Price;
                            //}
                        }
                        else if (instruments[instrument].status == Status.POSITION || !instruments[instrument].isReversed)
                        {
                            if (!instruments[instrument].isReversed && niftySide == OType.Sell)
                            {
                                Console.WriteLine("This is a Falling Script. So choosing Safe trigger");
                                trigger = ltp.Bids[4].Price;
                            }
                            else
                            {
                                Console.WriteLine("This is a RE-ORDER. So choosing undeniable Long trigger");
                                trigger = ltp.Offers[1].Price;
                            }
                            instruments[instrument].status = Status.OPEN;
                        }
                        else
                        {
                            if (Decimal.Compare(timenow, (decimal)10.37) < 0)
                            {
                                trigger = ltp.Offers[0].Price;
                                Console.WriteLine("Immediate Long Trigger Price is chosen");
                            }
                            else if ((instruments[instrument].bot30bb + variance2) > instruments[instrument].top30bb)
                            {
                                if (ltp.LastPrice < 200)
                                    trigger = ltp.Bids[4].Price - (decimal).05;
                                else if (ltp.LastPrice > 650)
                                    trigger = ltp.Bids[4].Price - 1;
                                else
                                    trigger = ltp.Bids[4].Price;
                                if (currentTrend == OType.StrongBuy && Decimal.Compare(timenow, (decimal)13.30) < 0)
                                {
                                    Console.WriteLine("narrowed but Quick Long Trigger Price is chosen as LTP is {0}", ltp.LastPrice);
                                    trigger = ltp.Bids[0].Price;
                                }
                                else
                                    Console.WriteLine("CoverUp Long Trigger Price is chosen as LTP is {0}", ltp.LastPrice);
                            }
                            else if (instruments[instrument].target < 2500)
                            {
                                if (ltp.LastPrice < 150)
                                {
                                    Console.WriteLine("Nearer Long Trigger Price is chosen - 1");
                                    trigger = ltp.Bids[0].Price; //ltp.Close - difference or ltp.LastPrice;
                                }
                                else if (ltp.LastPrice < 200)
                                {
                                    Console.WriteLine("Nearer Long Trigger Price is chosen - 2");
                                    trigger = ltp.Bids[0].Price;
                                }
                                else
                                {
                                    trigger = ltp.Bids[1].Price;
                                    if (currentTrend == OType.StrongBuy && Decimal.Compare(timenow, (decimal)13.30) < 0)
                                    {
                                        Console.WriteLine("Quick Long Trigger Price is chosen as LTP is {0}", ltp.LastPrice);
                                        trigger = ltp.Offers[0].Price;
                                    }
                                    else
                                        Console.WriteLine("Distant Long Trigger Price is chosen");
                                }
                            }
                            else
                            {
                                trigger = ltp.Bids[2].Price;
                                Console.WriteLine("Far Distant Long Trigger Price is chosen");
                            }
                        }
                        Console.WriteLine("Spot varied by {0}; Future trading at {1} with previous close {2}; Hence chosen Trigger is {3} as FUT variance is {4}",
                            difference.ToString(), ltp.LastPrice.ToString(), ltp.Close.ToString(), trigger.ToString(), (ltp.LastPrice - ltp.Close).ToString());

                        int lotSize = instruments[instrument].lotSize;
                        //if (Decimal.Compare(timenow, (decimal)10.45) < 0)
                        //    lotSize = lotSize + 1;
                        response = kite.PlaceOrder(
                            Exchange: Constants.EXCHANGE_NFO,
                            TradingSymbol: instruments[instrument].futName,
                            TransactionType: Constants.TRANSACTION_TYPE_BUY,
                            Quantity: instruments[instrument].lotSize,
                            Price: trigger,
                            OrderType: Constants.ORDER_TYPE_LIMIT,
                            Product: Constants.PRODUCT_MIS,
                            StoplossValue: stopLoss,
                            SquareOffValue: target,
                            Validity: Constants.VALIDITY_DAY,
                            Variety: Constants.VARIETY_BO
                            );
                        Console.WriteLine("At Time {0} BUY Order STATUS:::: {1}", DateTime.Now.ToString("yyyyMMdd hh:mm:ss"), Utils.JsonSerialize(response));
                        Console.WriteLine("BUY Order Details: Instrument ID : {0}; Quantity : {1}; Price : {2}; SL : {3}; Target {4} ", instruments[instrument].futName, instruments[instrument].lotSize, ltp.LastPrice, stopLoss, target);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine("EXCEPTION CAUGHT WHILE PLACING ORDER :: " + ex.Message);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("EXCEPTION CAUGHT WHILE Assessing best Target Price :: " + ex.Message);
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
                        if (OrderData.Status == "OPEN" && (OrderData.ParentOrderId == null || OrderData.ParentOrderId.ToString().Length == 0))
                        {
                            instruments[token].status = Status.STANDING;
                            modifyOrderInCSV(token, instruments[token].futName, instruments[token].type, Status.STANDING);
                        }
                        else if (OrderData.Status == "COMPLETE" && instruments[token].status == Status.CLOSE)
                        {
                            instruments[token].status = Status.POSITION;
                            modifyOrderInCSV(token, instruments[token].futName, instruments[token].type, Status.STANDING);
                        }
                        else if (OrderData.Status == "OPEN" && instruments[token].status != Status.POSITION)
                        {
                            instruments[token].status = Status.STANDING;
                            modifyOrderInCSV(token, instruments[token].futName, instruments[token].type, Status.STANDING);
                        }
                        else if ((OrderData.Status == "REJECTED" || OrderData.Status == "CANCELLED")
                            && (!(instruments[token].status == Status.STANDING || instruments[token].status == Status.POSITION || instruments[token].status == Status.CLOSE)))
                        {
                            instruments[token].status = Status.OPEN;
                            instruments[token].isHedgingOrder = false;
                            modifyOrderInCSV(token, instruments[token].futName, instruments[token].type, Status.OPEN);
                            Console.WriteLine("OPEN the ticker again for {0} as its status is '{1}'", OrderData.Tradingsymbol, OrderData.Status);
                            //closeOrderInCSV();
                        }
                        else if (OrderData.Status == "CANCELLED" && instruments[token].status == Status.STANDING)
                        {
                            instruments[token].status = Status.OPEN;
                            modifyOrderInCSV(token, instruments[token].futName, instruments[token].type, Status.OPEN);
                            Console.WriteLine("OPEN the ticker again as our tool Cancelled for {0}", OrderData.Tradingsymbol);
                        }
                        else if (OrderData.Status == "COMPLETE")
                        {
                            if (OrderData.ParentOrderId != null && OrderData.ParentOrderId.ToString().Length != 0)
                            {
                                CloseOrderTicker(token);
                            }
                            else
                            {
                                instruments[token].status = Status.POSITION;
                                instruments[token].orderTime = Convert.ToDateTime(OrderData.OrderTimestamp);
                                modifyOrderInCSV(token, instruments[token].futName, instruments[token].type, Status.POSITION);
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

        public OType VerifyNifty(decimal timenow)
        {
            decimal crudeVal = 42;
            decimal niftyVal = 60;
            if (Decimal.Compare(timenow, (decimal)(9.45)) < 0 && Decimal.Compare(timenow, (decimal)(9.24)) > 0)
            {
                crudeVal = 42;
                niftyVal = 45;
            }
            else if (Decimal.Compare(timenow, (decimal)(11.16)) < 0 && Decimal.Compare(timenow, (decimal)(9.45)) > 0)
            {
                crudeVal = 42;
                niftyVal = 55;
            }
            else if (Decimal.Compare(timenow, (decimal)(12.16)) < 0 && Decimal.Compare(timenow, (decimal)(11.16)) > 0)
            {
                crudeVal = 62;
                niftyVal = 70;
            }
            else if (Decimal.Compare(timenow, (decimal)(14.16)) < 0 && Decimal.Compare(timenow, (decimal)(12.15)) > 0)
            {
                crudeVal = 72;
                niftyVal = 90;
            }

            Dictionary<string, OHLC> dicOhlc;
            try
            {
                dicOhlc = kite.GetOHLC(new string[] { ConfigurationManager.AppSettings["NSENIFTY"].ToString(), ConfigurationManager.AppSettings["CRUDE"].ToString() });
            }
            catch
            {
                dicOhlc = kite.GetOHLC(new string[] { ConfigurationManager.AppSettings["NSENIFTY"].ToString(), ConfigurationManager.AppSettings["CRUDE"].ToString() });
            }
            OHLC ohlcNSE = new OHLC();
            OHLC ohlcCRUDE = new OHLC();
            if (dicOhlc != null)
            {
                dicOhlc.TryGetValue(ConfigurationManager.AppSettings["NSENIFTY"].ToString().ToString(), out ohlcNSE);
                dicOhlc.TryGetValue(ConfigurationManager.AppSettings["CRUDE"].ToString().ToString(), out ohlcCRUDE);
            }
            else
                throw new Exception("EXCEPTION @ VerifyNifty: OHLC for either crude or nse is not retrieved");
            bool flag = false;
            if (Decimal.Compare(timenow, (decimal)(9.18)) < 0)
            {
                flag = true;
            }
            bool bull = (ohlcCRUDE.Low < (ohlcCRUDE.Close - crudeVal)
                        && ohlcCRUDE.LastPrice < ohlcCRUDE.Low + 20)
                        && (ohlcCRUDE.LastPrice < ohlcCRUDE.High - 20
                            || flag) ? true : false;
            bool bear = (ohlcCRUDE.High > (ohlcCRUDE.Close + crudeVal)
                                && ohlcCRUDE.LastPrice < ohlcCRUDE.High - 20)
                        && (ohlcCRUDE.LastPrice > (ohlcCRUDE.Low + 20)
                            || flag) ? true : false;

            if (ohlcNSE.LastPrice > (ohlcNSE.Close + niftyVal)
                    || bull)
            {
                //Console.WriteLine("Market is Volatile for the day as NIFTY is Bullish. Nifty PrevClose is {0} LastPrice is {1} with more than Validation points {4} Points or CRUDE PrevClose is {2} LastPrice is {3} with more than 70 Points", ohlcNSE.Close, ohlcNSE.LastPrice, ohlcCRUDE.Close, ohlcCRUDE.LastPrice, niftyVal);
                return OType.Buy;
            }
            else if (ohlcNSE.LastPrice < (ohlcNSE.Close - niftyVal)
                    || bear)
            {
                //Console.WriteLine("Market is Volatile for the day as NIFTY is Bearish. Nifty PrevClose is {0} LastPrice is {1} with more than Validation points {4} Points or CRUDE PrevClose is {2} Lastprice is {3} with more than 70 Points", ohlcNSE.Close, ohlcNSE.LastPrice, ohlcCRUDE.Close, ohlcCRUDE.LastPrice, niftyVal);
                return OType.Sell;
            }
            else
                return OType.BS;
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

        void getDays(out DateTime previousDay, out DateTime currentDay)
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
                //currentDay = currentDay.AddDays(1); // Debug
                decimal topBB = 0;
                decimal botBB = 0;
                decimal middle = 0;
                int counter = 0;

                List<Instrument> calcInstruments = kite.GetInstruments("NFO");
                csvLine.Add("ScriptId,FutID,Symbol,LotSize,TopBB,BotBB,Middle,TYPE,State,lotCount,Order,Dayma50,Weekma,CandleClose,Close,Identify,IsOpenAlign,IsReversed,CanTrust");
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

                    if (scripts.Segment == "NFO-FUT"
                        //&& !scripts.TradingSymbol.Contains("CENTURYTEX")
                        && scripts.LotSize < 5200
                        && scripts.LotSize >= 700)
                    {
                        try
                        {
                            DateTime expiry = (DateTime)scripts.Expiry;
                            if (expiry.Month == Convert.ToInt16(ConfigurationManager.AppSettings["month"]))
                            {
                                string equitySymbol = scripts.TradingSymbol.Replace(ConfigurationManager.AppSettings["expiry"].ToString(), "");
                                Dictionary<string, Quote> quotes = new Dictionary<string, Quote>();
                                List<Historical> dayHistory = new List<Historical>();
                                decimal lastClose = 0;
                                try
                                {
                                    quotes = kite.GetQuote(InstrumentId: new string[] { "NSE:" + equitySymbol });
                                }
                                catch (TimeoutException)
                                {
                                    quotes = kite.GetQuote(InstrumentId: new string[] { "NSE:" + equitySymbol });
                                }
                                if (quotes.Count > 0)
                                {
                                    System.Threading.Thread.Sleep(400);
                                    dayHistory = kite.GetHistoricalData(quotes["NSE:" + equitySymbol].InstrumentToken.ToString(),
                                                previousDay, currentDay, "day");
                                    lastClose = dayHistory[dayHistory.Count - 1].Close;
                                    if (lastClose < 950 && lastClose > 100)
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
                                            history = kite.GetHistoricalData(quotes["NSE:" + equitySymbol].InstrumentToken.ToString(),
                                                previousDay.AddDays(-4), currentDay, "30minute");
                                            //Console.WriteLine("Got Quote successfull & Passed Day Close of {0} with historyCount {1}", equitySymbol, history.Count);

                                            while (history.Count < 50)
                                            {
                                                System.Threading.Thread.Sleep(1000);
                                                previousDay = previousDay.AddDays(-1);
                                                history = kite.GetHistoricalData(quotes["NSE:" + equitySymbol].InstrumentToken.ToString(),
                                                    previousDay.AddDays(-4), currentDay, "30minute");
                                                //Console.WriteLine("History Candles are lesser than Expceted candles. Please Check the given dates. PreviousDate {0} CurrentDate {1}, with candles count {2}", previousDay.AddDays(-5), currentDay, history.Count);
                                            }
                                        }
                                        catch (TimeoutException)
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
                                        decimal black = GetWeekMA(quotes["NSE:" + equitySymbol].InstrumentToken.ToString());
                                        decimal ma50 = GetMA50(quotes["NSE:" + equitySymbol].InstrumentToken.ToString(), black);
                                        
                                        //decimal prevBlack = 0;
                                        //prevBlack = GetPreviousWeekMA(quotes["NSE:" + equitySymbol].InstrumentToken.ToString());

                                        decimal variance025 = (lastClose * (decimal)0.25) / 100;
                                        decimal variance06 = (lastClose * (decimal)0.6) / 100;
                                        decimal variance13 = (lastClose * (decimal)1.3) / 100;
                                        decimal variance18 = (lastClose * (decimal)1.8) / 100;
                                        //decimal variance21 = (lastClose * (decimal)2.1) / 100;
                                        decimal variance35 = (lastClose * (decimal)3.5) / 100;

                                        if (equitySymbol.Contains("MFSL"))
                                            Console.WriteLine("Break Point Check for debug purpose; Continue");
                                        bool bullflag = (h1.Open <= h1.Close && (h2.Open <= h2.Close || lastClose > middle + variance025) && Decimal.Compare(lastClose, Convert.ToDecimal(lastCandleClose)) >= 0);
                                        bool bearflag = (h1.Open >= h1.Close && (h2.Open >= h2.Close || lastClose < middle - variance025) && Decimal.Compare(lastClose, Convert.ToDecimal(lastCandleClose)) <= 0);
                                        string canTrust = "True";
                                        if (lastClose < black)
                                        {
                                            #region Added BUY Condition
                                            if (botBB + variance35 > topBB
                                                && (IsBetweenVariance((botBB + variance18), topBB, (decimal).0001)
                                                    || (botBB + variance18) < topBB))
                                            //|| IsBetweenVariance(botBB, Convert.ToDecimal(lastCandleClose), (decimal).0003))
                                            {
                                                //if (bearflag && lastClose < middle - variance025)  // && black > prevBlack) && (botBB + variance18 > black && 
                                                if (lastClose < middle
                                                    && IsBeyondVariance(lastClose, middle, (decimal).0008)
                                                    //&& black < ma50
                                                    && lastClose < (black - variance06)
                                                    && lastClose > (black - variance18))
                                                {
                                                    if (!CanTrust(quotes["NSE:" + equitySymbol].Close, startCandle, history, black))
                                                        canTrust = "False";
                                                    csvLine.Add(quotes["NSE:" + equitySymbol].InstrumentToken.ToString() + "," +
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
                                                        "MONITOR,False,False,"
                                                        + canTrust);
                                                }
                                                else
                                                {
                                                    if (!CanTrust(quotes["NSE:" + equitySymbol].Close, startCandle, history, black))
                                                        canTrust = "False";
                                                    csvLine.Add(quotes["NSE:" + equitySymbol].InstrumentToken.ToString() + "," +
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
                                                        + canTrust);
                                                }
                                                scriptsCount++;
                                            }
                                            else if ((IsBetweenVariance((botBB + variance18), topBB, (decimal).0001)
                                                    || (botBB + variance18) < topBB))// && scriptsCount < 35)
                                            {
                                                if (!CanTrust(quotes["NSE:" + equitySymbol].Close, startCandle, history, black))
                                                    canTrust = "False";
                                                csvLine.Add(quotes["NSE:" + equitySymbol].InstrumentToken.ToString() + "," +
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
                                                    + canTrust);
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
                                                //if // && bullflag && lastClose > middle + variance025)  // && black > prevBlack) && (topBB - variance18 < black
                                                if (lastClose > middle
                                                    && IsBeyondVariance(lastClose, middle, (decimal).0008)
                                                    //&& black > ma50
                                                    && lastClose > (black + variance06)
                                                    && lastClose < (black + variance18))
                                                {
                                                    if (!CanTrust(quotes["NSE:" + equitySymbol].Close, startCandle, history, black))
                                                        canTrust = "False";
                                                    csvLine.Add(quotes["NSE:" + equitySymbol].InstrumentToken.ToString() + "," +
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
                                                        "MONITOR,False,False,"
                                                        + canTrust);
                                                }
                                                else
                                                {
                                                    if (!CanTrust(quotes["NSE:" + equitySymbol].Close, startCandle, history, black))
                                                        canTrust = "False";
                                                    csvLine.Add(quotes["NSE:" + equitySymbol].InstrumentToken.ToString() + "," +
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
                                                        + canTrust);
                                                }
                                                scriptsCount++;
                                            }
                                            else if ((IsBetweenVariance((botBB + variance18), topBB, (decimal).0001)
                                                    || (botBB + variance18) < topBB)) // && scriptsCount < 35)
                                            {
                                                if (!CanTrust(quotes["NSE:" + equitySymbol].Close, startCandle, history, black))
                                                    canTrust = "False";
                                                csvLine.Add(quotes["NSE:" + equitySymbol].InstrumentToken.ToString() + "," +
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
                                                    + canTrust);
                                                scriptsCount++;
                                            }
                                            else
                                            {
                                                //Console.WriteLine("This Scrript {0} is Ignored from trading", equitySymbol);
                                            }
                                            #endregion
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
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("EXCEPTION While getting All NFO Scripts recieved -> {0}", ex.Message);
            }
            return csvLine;
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
                DateTime previousDay;
                DateTime currentDay;
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
                            if (expiry.Month == Convert.ToInt16(ConfigurationManager.AppSettings["month"]))
                            {
                                string equitySymbol = scripts.TradingSymbol.Replace(ConfigurationManager.AppSettings["expiry"].ToString(), "");
                                Dictionary<string, Quote> quotes = new Dictionary<string, Quote>();
                                //List<Historical> dayHistory = new List<Historical>();
                                decimal lastClose = 0;
                                try
                                {
                                    System.Threading.Thread.Sleep(500);
                                    quotes = kite.GetQuote(InstrumentId: new string[] { "NSE:" + equitySymbol });
                                }
                                catch (TimeoutException)
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
            catch (TimeoutException)
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
                    middle30BB = GetMiddle30BBOf(history, candles - 1);

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
                string equitySymbol = equityName.Replace(ConfigurationManager.AppSettings["expiry"].ToString(), "");
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
                middleBB = GetMiddle30BBOf(history, i);

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
            decimal middleBB = GetMiddle30BBOf(history, candles - 1);
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
                middleBB = GetMiddle30BBOf(history, i-1);
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

        public decimal GetMiddle30BBOf(List<Historical> history, int benchmark)
        {
            int index = 0;
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
            return middle30BB;
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
                        //Console.WriteLine("Modify the OPEN AIGN for ticker :: {0} to status {1} for the day at {2} ", n.futName, cells[8], DateTime.Now.ToString("yyyyMMdd hh: mm:ss"));
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

        public void putStatusContent()
        {
            try
            {
                if (File.Exists(ConfigurationManager.AppSettings["poweShellFile"]))
                {
                    string destn;
                    string _accessKey = "AKIAREKE4CGCGJGWZW26";
                    string _secretKey = "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";
                    //string _decryptedSecretKey = decryptString(_secretKey);
                    IAmazonS3 _s3Client = new AmazonS3Client(_accessKey, _secretKey, RegionEndpoint.APSouth1);

                    PutObjectRequest request;
                    PutObjectResponse response;

                    if (File.Exists(ConfigurationManager.AppSettings["OutFile"]))
                    {
                        destn = ConfigurationManager.AppSettings["OutFile"].Replace("Redirect.", "RedirectTemp.");
                        if (File.Exists(destn))
                            File.Delete(destn);
                        File.Copy(ConfigurationManager.AppSettings["OutFile"], destn);
                        request = new PutObjectRequest
                        {
                            //https://077990728068.signin.aws.amazon.com/console
                            BucketName = @"mousecursorservice",
                            Key = "Redirect.txt",
                            FilePath = destn
                        };

                        response = _s3Client.PutObject(request);
                        string message = String.Format("AT {0} Successfuly uploaded the Redirect File with response status OK={1}", DateTime.Now.ToString("yyyy MM dd hh:mm:ss tt"), response.HttpStatusCode.ToString());
                        WriteToStatus(message);
                    }
                    else
                    {
                        string message = String.Format("AT {0} Finally Success, Redirect Log File is not found in the directory; ;)", DateTime.Now.ToString("yyyy MM dd hh:mm:ss tt"));
                        WriteToStatus(message);
                    }

                    if (File.Exists(ConfigurationManager.AppSettings["inputFile"]))
                    {
                        destn = ConfigurationManager.AppSettings["inputFile"].Replace("bulbul.", "bulbulTemp.");
                        if (File.Exists(destn))
                            File.Delete(destn);
                        File.Copy(ConfigurationManager.AppSettings["inputFile"], destn);
                        request = new PutObjectRequest
                        {
                            //https://077990728068.signin.aws.amazon.com/console
                            BucketName = @"mousecursorservice",
                            Key = "bulbul.csv",
                            FilePath = destn
                        };

                        response = _s3Client.PutObject(request);
                        string message = String.Format("AT {0} Successfuly uploaded the Watchlist File with response status OK={1}", DateTime.Now.ToString("yyyy MM dd hh:mm:ss tt"), response.HttpStatusCode.ToString());
                        WriteToStatus(message);
                    }
                    else
                    {
                        string message = String.Format("AT {0} Finally Success, watchlist File is not found in the directory; ;)", DateTime.Now.ToString("yyyy MM dd hh:mm:ss tt"));
                        WriteToStatus(message);
                    }

                    request = new PutObjectRequest
                    {
                        //https://077990728068.signin.aws.amazon.com/console
                        BucketName = @"mousecursorservice",
                        Key = "Status.txt",
                        FilePath = ConfigurationManager.AppSettings["StartupFile"]
                    };

                    response = _s3Client.PutObject(request);
                }
                else
                    Console.WriteLine("NO Such poweshell File is found in the drive; S3 Upload is halted");
            }
            catch (Exception ex)
            {
                Console.WriteLine("EXCEPTION CAUGHT while uploading to S3 Bucket with {0}", ex.Message);
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

        public void OnTick(uint instrument, decimal ltp, decimal low, decimal high)
        {
            decimal serviceStopTime = Convert.ToDecimal(ConfigurationManager.AppSettings["ServiceStopTime"]);
            decimal timenow = (decimal)DateTime.Now.Hour + ((decimal)DateTime.Now.Minute / 100);
            if ((Decimal.Compare(timenow, serviceStopTime) < 0 || Decimal.Compare(timenow, (decimal)9.14) > 0)
                && instruments[instrument].status != Status.CLOSE)
            {
                bool noOpenOrder = true;
                //if (instruments[instrument].status != Status.OPEN)
                //    return;
                if (VerifyLtp(instrument, ltp, low, high))
                {
                    #region Return if Bolinger is narrowed or expanded
                    if (!instruments[instrument].isReversed)
                    {
                        decimal variance14 = (ltp * (decimal)1.4) / 100;
                        if ((instruments[instrument].bot30bb + variance14) > instruments[instrument].top30bb)
                        {
                            Console.WriteLine("Current Time is {0} and Closing Script {1} as the script has Narrowed so much and making it riskier where {2} > {3}",
                                DateTime.Now.ToString(),
                                instruments[instrument].futName,
                                instruments[instrument].bot30bb + variance14,
                                instruments[instrument].top30bb);
                            if (!instruments[instrument].canTrust)
                            {
                                CloseOrderTicker(instrument);
                            }
                            return;
                        }

                        decimal variance46 = (ltp * (decimal)4.6) / 100;
                        if ((instruments[instrument].bot30bb + variance46) < instruments[instrument].top30bb
                            && Decimal.Compare(timenow, Convert.ToDecimal(9.45)) > 0)
                        {
                            Console.WriteLine("Current Time is {0} and Closing Script {1} as the script has Expanded so much and making it riskier where {2} < {3}",
                                DateTime.Now.ToString(),
                                instruments[instrument].futName,
                                instruments[instrument].bot30bb + variance46,
                                instruments[instrument].top30bb);

                            /*if (!instruments[instrument].canTrust)
                            {
                                CloseOrderTicker(instrument);
                            }*/
                            return;
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
                                        CloseOrderTicker(instrument);
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
                                    CloseOrderTicker(instrument);
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
                                        CloseOrderTicker(instrument);
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
                            Console.WriteLine("Time {0} Placing BUY Order of Instrument {1} for LTP {2} as it match long trigger {3} with top30BB {4} & bot30BB {5}", DateTime.Now.ToString(), instruments[instrument].futName, ltp.ToString(), instruments[instrument].longTrigger, instruments[instrument].top30bb, instruments[instrument].bot30bb);
                        else if (instruments[instrument].type == OType.Sell)
                            Console.WriteLine("Time {0} Placing SELL Order of Instrument {1} for LTP {2} as it match Short trigger {3} with top30BB {4} & bot30BB {5}", DateTime.Now.ToString(), instruments[instrument].futName, ltp.ToString(), instruments[instrument].shortTrigger, instruments[instrument].top30bb, instruments[instrument].bot30bb);
                        //placeOrder(instrument, tickData.LastPrice - tickData.Close);
                    }
                    else
                    {
                        //instruments[instrument].status = Status.STANDING;
                    }
                }
            }
        }

        public bool VerifyLtp(uint instrument, decimal ltp, decimal low, decimal high)
        {
            bool qualified = false;
            decimal timenow = (decimal)DateTime.Now.Hour + ((decimal)DateTime.Now.Minute / 100);
            if (instruments[instrument].status == Status.OPEN
                && instruments[instrument].bot30bb > 0
                && instruments[instrument].middle30BBnew > 0
                && Decimal.Compare(timenow, Convert.ToDecimal(ConfigurationManager.AppSettings["CutoffTime"])) < 0) //change back to 14.24
            {
                #region Verify for Open Trigger
                decimal variance14 = (ltp * (decimal)1.4) / 100;
                decimal variance17 = (ltp * (decimal)1.65) / 100;
                decimal variance2 = (ltp * (decimal)2) / 100;
                decimal variance23 = (ltp * (decimal)2.3) / 100;
                decimal variance25 = (ltp * (decimal)2.5) / 100;

                bool flag = DateTime.Now.Minute == 43 || DateTime.Now.Minute == 13
                                || DateTime.Now.Minute == 44 || DateTime.Now.Minute == 14
                                || (DateTime.Now.Minute == 45 && DateTime.Now.Second <= 40)
                                || (DateTime.Now.Minute == 15 && DateTime.Now.Second <= 40) ? true : false;

                if (flag)
                    return false;

                if (instruments[instrument].canTrust
                    && instruments[instrument].isOpenAlign
                    && Decimal.Compare(timenow, Convert.ToDecimal(9.44)) > 0)
                {
                    switch (instruments[instrument].type)
                    {
                        case OType.Sell:
                        case OType.StrongSell:
                            #region Verify Sell Trigger
                            qualified = IsBetweenVariance(ltp, instruments[instrument].shortTrigger, (decimal).0006);

                            if (instruments[instrument].isReversed)
                            {
                                if (Decimal.Compare(timenow, Convert.ToDecimal(10.44)) > 0)
                                {
                                    if (((IsBetweenVariance(low, instruments[instrument].bot30bb, (decimal).0006)
                                                || low < instruments[instrument].bot30bb)
                                            && instruments[instrument].middle30BB > instruments[instrument].middle30ma50
                                            && IsBeyondVariance(instruments[instrument].middle30BB, instruments[instrument].middle30ma50, (decimal).006)
                                            && instruments[instrument].bot30bb < instruments[instrument].middle30ma50)
                                        && (IsBetweenVariance((instruments[instrument].bot30bb + variance25), instruments[instrument].top30bb, (decimal).0006)
                                            || (instruments[instrument].bot30bb + variance25) > instruments[instrument].top30bb))
                                    {
                                        OType trend = CalculateSqueezedTrend(instruments[instrument].futName, instruments[instrument].history, 15);
                                        if (trend == OType.StrongBuy)
                                        {
                                            instruments[instrument].canTrust = false;
                                            Console.WriteLine("Time {0} Marcking this script {1} as Cannot-be-Trusted as it has MA3050 {2} is below middle30 {3}", DateTime.Now.ToString(), instruments[instrument].futName, instruments[instrument].middle30ma50, instruments[instrument].middle30BB);
                                            return false;
                                        }
                                    }
                                }
                                if (qualified)
                                {
                                    System.Threading.Thread.Sleep(400);
                                    List<Historical> history = kite.GetHistoricalData(instrument.ToString(),
                                                                DateTime.Now.Date.AddHours(9).AddMinutes(15),
                                                                //DateTime.Now.Date.AddHours(9).AddMinutes(16),
                                                                DateTime.Now.Date.AddDays(1),
                                                                "30minute");
                                    if (history.Count == 2)
                                    {
                                        /*
                                        if (history[history.Count - 2].Close > instruments[instrument].shortTrigger)
                                        {
                                            if (ltp > instruments[instrument].shortTrigger && Decimal.Compare(timenow, Convert.ToDecimal(9.57)) < 0)
                                                return false;
                                            else
                                            {
                                                Console.WriteLine("Time {0} Averting Candle: Please Ignore this script {1} as it has closed above the Short Trigger for now wherein prevCandle Close {2} vs short trigger {3}", DateTime.Now.ToString(), instruments[instrument].futName, history[history.Count - 2].Close, instruments[instrument].shortTrigger);
                                                //instruments[instrument].shortTrigger = instruments[instrument].top30bb;
                                                //instruments[instrument].type = OType.Buy;
                                                instruments[instrument].isReversed = false;
                                                instruments[instrument].isOpenAlign = false;
                                                modifyOpenAlignOrReversedStatus(instruments[instrument], 16, OType.Sell, false);
                                                modifyOpenAlignOrReversedStatus(instruments[instrument], 17, OType.Sell, false);
                                                qualified = false;
                                                if (instruments[instrument].status != Status.POSITION)
                                                {
                                                    uint[] toArray = new uint[] { instrument };
                                                    ticker.UnSubscribe(toArray);
                                                }
                                                return qualified;
                                            }
                                        }*/
                                    }
                                    else if (history.Count > 2)
                                    {
                                        if (DateTime.Now.Minute == 45 || DateTime.Now.Minute == 15 || DateTime.Now.Minute == 46 || DateTime.Now.Minute == 16)
                                        {
                                            TimeSpan candleTime = history[history.Count - 2].TimeStamp.TimeOfDay;
                                            TimeSpan timeDiff = DateTime.Now.TimeOfDay.Subtract(candleTime);
                                            if (timeDiff.Minutes > 35)
                                            {
                                                Console.WriteLine("EXCEPTION in Candle Retrieval Time {0} Last Candle Not Found : Last Candle closed time is {1}", DateTime.Now.ToString(), history[history.Count - 2].TimeStamp.ToString());
                                                return false;
                                            }
                                        }
                                        if (history[history.Count - 2].Close > instruments[instrument].shortTrigger)
                                        {
                                            Console.WriteLine("Time {0} Averting Candle: Please Ignore this script {1} as it has closed above the Short Trigger for now wherein prevCandle Close {2} vs short trigger {3}", DateTime.Now.ToString(), instruments[instrument].futName, history[history.Count - 2].Close, instruments[instrument].shortTrigger);
                                            //instruments[instrument].shortTrigger = instruments[instrument].top30bb;
                                            //instruments[instrument].type = OType.Buy;
                                            //instruments[instrument].isReversed = false;
                                            instruments[instrument].isOpenAlign = false;
                                            modifyOpenAlignOrReversedStatus(instruments[instrument], 16, OType.Sell, false);
                                            modifyOpenAlignOrReversedStatus(instruments[instrument], 17, OType.Sell, false);
                                            qualified = false;
                                            if (instruments[instrument].status != Status.POSITION
                                                || instruments[instrument].status == Status.OPEN)
                                            {
                                                CloseOrderTicker(instrument);
                                            }
                                            return qualified;
                                        }
                                    }
                                }
                                qualified = IsBetweenVariance(ltp, instruments[instrument].shortTrigger, (decimal).0006);
                                if (qualified)
                                {
                                    decimal requiredCP = instruments[instrument].ma50;
                                    if (instruments[instrument].ma50 < instruments[instrument].middleBB)
                                    {
                                        requiredCP = instruments[instrument].middleBB;
                                        if ((instruments[instrument].bot30bb + variance23) > instruments[instrument].top30bb)
                                        {
                                            requiredCP = instruments[instrument].topBB;
                                            if (!instruments[instrument].isOpenAlignFatal)
                                            {
                                                OType niftyTrend = VerifyNifty(timenow);
                                                if (niftyTrend == OType.Buy)
                                                {
                                                    requiredCP = Convert.ToDecimal((ltp * (decimal).5).ToString("#.#"));
                                                    requiredCP = instruments[instrument].top30bb + requiredCP;
                                                }
                                                else
                                                    requiredCP = instruments[instrument].top30bb;
                                            }
                                        }
                                    }
                                    else if (instruments[instrument].ma50 > instruments[instrument].topBB)
                                    {
                                        requiredCP = instruments[instrument].topBB;
                                        if (IsBetweenVariance(instruments[instrument].topBB, instruments[instrument].ma50, (decimal).003)
                                            && (instruments[instrument].bot30bb + variance23) > instruments[instrument].top30bb)
                                        {
                                            requiredCP = instruments[instrument].ma50;
                                            if (!instruments[instrument].isOpenAlignFatal)
                                            {
                                                OType niftyTrend = VerifyNifty(timenow);
                                                if (niftyTrend == OType.Buy)
                                                {
                                                    requiredCP = Convert.ToDecimal((ltp * (decimal).5).ToString("#.#"));
                                                    requiredCP = instruments[instrument].top30bb + requiredCP;
                                                }
                                                else
                                                    requiredCP = instruments[instrument].top30bb;
                                            }
                                        }
                                    }
                                    else //if (Decimal.Compare(timenow, Convert.ToDecimal(13.30)) > 0)
                                    {
                                        requiredCP = instruments[instrument].topBB;
                                    }
                                    qualified = ltp >= requiredCP || IsBetweenVariance(ltp, requiredCP, (decimal).0006);
                                    if (instruments[instrument].ma50 > instruments[instrument].shortTrigger)
                                    {
                                        if (VerifyNifty(timenow) == OType.Sell && !qualified)
                                        {
                                            if ((instruments[instrument].botBB + variance23) < instruments[instrument].topBB
                                            && Decimal.Compare(timenow, Convert.ToDecimal(11.14)) > 0)
                                            {
                                                if (instruments[instrument].oldTime != instruments[instrument].currentTime)
                                                {
                                                    instruments[instrument].oldTime = instruments[instrument].currentTime;
                                                    Console.WriteLine("Time {0} Averting Expansion: Please Ignore this script {1} as it has Expanded so much for now wherein topBB {2} & botBB {3} for Sell", DateTime.Now.ToString(), instruments[instrument].futName, instruments[instrument].topBB, instruments[instrument].botBB);
                                                }
                                                qualified = false;
                                                return false;
                                            }
                                            if ((instruments[instrument].botBB + variance14) > instruments[instrument].topBB
                                                && Decimal.Compare(timenow, Convert.ToDecimal(11.14)) > 0
                                                && !(IsBetweenVariance(low, instruments[instrument].weekMA, (decimal).0006)
                                                        || IsBetweenVariance(low, instruments[instrument].middle30ma50, (decimal).0006)
                                                        || IsBetweenVariance(low, instruments[instrument].bot30bb, (decimal).0006)))
                                            {
                                                qualified = ltp >= instruments[instrument].topBB || IsBetweenVariance(ltp, instruments[instrument].topBB, (decimal).0006);
                                                if (qualified)
                                                    Console.WriteLine("You could have AVOIDED this. Qualified for SELL order {0} based on LTP {1} is ~ below short trigger {2} and ltp is around ma50 {3} ", instruments[instrument].futName, ltp, instruments[instrument].shortTrigger, instruments[instrument].ma50);
                                            }
                                        }
                                    }
                                    else
                                    {
                                        // Do nothing
                                    }
                                    if (qualified && instruments[instrument].oldTime != instruments[instrument].currentTime)
                                    {
                                        instruments[instrument].oldTime = instruments[instrument].currentTime;
                                        Console.WriteLine("INSIDER :: Qualified for SELL order based on LTP {0} is ~ above short trigger {1} and ltp is around topBB {2} wherein Required Cost price {3} is still to go", ltp, instruments[instrument].shortTrigger, instruments[instrument].topBB, requiredCP);
                                    }
                                }
                                /*
                                if (Decimal.Compare(timenow, Convert.ToDecimal(13.30)) > 0 && !qualified)
                                {
                                    if (VerifyNifty(timenow) == OType.Sell)
                                    {
                                        qualified = (r1 >= instruments[instrument].shortTrigger || IsBetweenVariance(r1, instruments[instrument].shortTrigger, (decimal).0006))
                                            && (r1 >= instruments[instrument].ma50 || IsBetweenVariance(r1, instruments[instrument].ma50, (decimal).0006));
                                        if (qualified)
                                        {
                                            Console.WriteLine("Time {0} Trigger Verification PASSed: The script {1} as Short Trigger {2} vs r1 {3} & ma50 is at {4} for Sell", DateTime.Now.ToString(), instruments[instrument].futName, instruments[instrument].shortTrigger, r1, instruments[instrument].ma50);
                                        }
                                    }
                                }
                                */
                            }
                            else
                            {
                                if (qualified
                                    && !instruments[instrument].isReversed
                                    && instruments[instrument].canTrust)
                                {
                                    //qualified = CalculateBB((uint)instruments[instrument].instrumentId, tickData);
                                }
                                else
                                    qualified = false;
                            }
                            #endregion
                            break;
                        case OType.Buy:
                        case OType.StrongBuy:
                            #region Verify BUY Trigger
                            qualified = IsBetweenVariance(ltp, instruments[instrument].longTrigger, (decimal).0006);

                            if (instruments[instrument].isReversed)
                            {
                                if (Decimal.Compare(timenow, Convert.ToDecimal(10.44)) < 0)
                                {
                                    if (((IsBetweenVariance(high, instruments[instrument].top30bb, (decimal).0006)
                                                || high > instruments[instrument].top30bb)
                                            && instruments[instrument].middle30BB < instruments[instrument].middle30ma50
                                            && IsBeyondVariance(instruments[instrument].middle30BB, instruments[instrument].middle30ma50, (decimal).006)
                                            && instruments[instrument].top30bb > instruments[instrument].middle30ma50)
                                        && (IsBetweenVariance((instruments[instrument].bot30bb + variance25), instruments[instrument].top30bb, (decimal).0006)
                                            || (instruments[instrument].bot30bb + variance25) > instruments[instrument].top30bb))
                                    {
                                        OType trend = CalculateSqueezedTrend(instruments[instrument].futName, instruments[instrument].history, 15);
                                        if (trend == OType.StrongSell)
                                        {
                                            instruments[instrument].canTrust = false;
                                            Console.WriteLine("Time {0} Marcking this script {1} as Cannot-be-Trusted as it has MA3050 {2} is above middle30 {3}", DateTime.Now.ToString(), instruments[instrument].futName, instruments[instrument].middle30ma50, instruments[instrument].middle30BB);
                                            return false;
                                        }
                                    }
                                }
                                if (qualified)
                                {
                                    System.Threading.Thread.Sleep(400);
                                    List<Historical> history = kite.GetHistoricalData(instrument.ToString(),
                                                                DateTime.Now.Date.AddHours(9).AddMinutes(15),
                                                                //DateTime.Now.Date.AddHours(13).AddMinutes(50),
                                                                DateTime.Now.Date.AddDays(1),
                                                                "30minute");
                                    if (history.Count == 2)
                                    {
                                        /*
                                        if (history[history.Count - 2].Close < instruments[instrument].longTrigger)
                                        {
                                            if (ltp < instruments[instrument].longTrigger && Decimal.Compare(timenow, Convert.ToDecimal(9.57)) < 0)
                                                return false;
                                            else
                                            {
                                                Console.WriteLine("Time {0} Averting Candle: Please Ignore this script {1} as it has closed below the long Trigger for now wherein prevCandle Close {2} vs long trigger {3}", DateTime.Now.ToString(), instruments[instrument].futName, history[history.Count - 2].Close, instruments[instrument].longTrigger);
                                                //instruments[instrument].longTrigger = instruments[instrument].bot30bb;
                                                //instruments[instrument].type = OType.Buy;
                                                instruments[instrument].isReversed = false;
                                                instruments[instrument].isOpenAlign = false;
                                                modifyOpenAlignOrReversedStatus(instruments[instrument], 16, OType.Buy, false);
                                                modifyOpenAlignOrReversedStatus(instruments[instrument], 17, OType.Buy, false);
                                                qualified = false;
                                                if (instruments[instrument].status != Status.POSITION)
                                                {
                                                    uint[] toArray = new uint[] { instrument };
                                                    ticker.UnSubscribe(toArray);
                                                }
                                                return qualified;
                                            }
                                        }*/
                                    }
                                    else if (history.Count > 2)
                                    {
                                        if (DateTime.Now.Minute == 45 || DateTime.Now.Minute == 15 || DateTime.Now.Minute == 46 || DateTime.Now.Minute == 16)
                                        {
                                            TimeSpan candleTime = history[history.Count - 2].TimeStamp.TimeOfDay;
                                            TimeSpan timeDiff = DateTime.Now.TimeOfDay.Subtract(candleTime);
                                            if (timeDiff.Minutes > 35)
                                            {
                                                Console.WriteLine("EXCEPTION in Candle Retrieval Time {0} Last Candle Not Found : Last Candle closed time is {1}", DateTime.Now.ToString(), history[history.Count - 2].TimeStamp.ToString());
                                                return false;
                                            }
                                        }
                                        if (history[history.Count - 2].Close < instruments[instrument].longTrigger)
                                        {
                                            Console.WriteLine("Time {0} Averting Candle: Please Ignore this script {1} as it has closed below the long Trigger for now wherein prevCandle Close {2} vs long trigger {3}", DateTime.Now.ToString(), instruments[instrument].futName, history[history.Count - 2].Close, instruments[instrument].longTrigger);
                                            //instruments[instrument].longTrigger = instruments[instrument].bot30bb;
                                            //instruments[instrument].type = OType.Buy;
                                            //instruments[instrument].isReversed = false;
                                            instruments[instrument].isOpenAlign = false;
                                            modifyOpenAlignOrReversedStatus(instruments[instrument], 16, OType.Buy, false);
                                            modifyOpenAlignOrReversedStatus(instruments[instrument], 17, OType.Buy, false);
                                            qualified = false;
                                            if (instruments[instrument].status != Status.POSITION
                                                || instruments[instrument].status == Status.OPEN)
                                            {
                                                CloseOrderTicker(instrument);
                                            }
                                            return qualified;
                                        }
                                    }
                                }
                                qualified = IsBetweenVariance(ltp, instruments[instrument].longTrigger, (decimal).0006) || ltp < instruments[instrument].longTrigger;
                                if (qualified)
                                {
                                    decimal requiredCP = instruments[instrument].ma50;
                                    if (instruments[instrument].ma50 > instruments[instrument].middleBB)
                                    {
                                        requiredCP = instruments[instrument].middleBB;
                                        if ((instruments[instrument].bot30bb + variance23) > instruments[instrument].top30bb)
                                        {
                                            requiredCP = instruments[instrument].botBB;
                                            if (!instruments[instrument].isOpenAlignFatal)
                                            {
                                                OType niftyTrend = VerifyNifty(timenow);
                                                if (niftyTrend == OType.Sell)
                                                {
                                                    requiredCP = Convert.ToDecimal((ltp * (decimal).005).ToString("#.#"));
                                                    requiredCP = instruments[instrument].bot30bb - requiredCP;
                                                }
                                                else
                                                {
                                                    requiredCP = Convert.ToDecimal((ltp * (decimal).0005).ToString("#.#"));
                                                    requiredCP = instruments[instrument].bot30bb;
                                                }
                                            }
                                        }
                                    }
                                    else if (instruments[instrument].ma50 < instruments[instrument].botBB)
                                    {
                                        requiredCP = instruments[instrument].botBB;
                                        if (IsBetweenVariance(instruments[instrument].botBB, instruments[instrument].ma50, (decimal).003)
                                            && (instruments[instrument].bot30bb + variance23) > instruments[instrument].top30bb)
                                        {
                                            requiredCP = instruments[instrument].ma50;
                                            if (!instruments[instrument].isOpenAlignFatal)
                                            {
                                                OType niftyTrend = VerifyNifty(timenow);
                                                if (niftyTrend == OType.Sell)
                                                {
                                                    requiredCP = Convert.ToDecimal((ltp * (decimal).005).ToString("#.#"));
                                                    requiredCP = instruments[instrument].bot30bb - requiredCP;
                                                }
                                                else
                                                {
                                                    requiredCP = Convert.ToDecimal((ltp * (decimal).0005).ToString("#.#"));
                                                    requiredCP = instruments[instrument].ma50 - requiredCP;
                                                }
                                            }
                                        }
                                    }
                                    else //if (Decimal.Compare(timenow, Convert.ToDecimal(13.30)) > 0)
                                    {
                                        requiredCP = instruments[instrument].botBB;
                                    }
                                    qualified = ltp <= requiredCP || IsBetweenVariance(ltp, requiredCP, (decimal).0006);
                                    if (instruments[instrument].ma50 < instruments[instrument].longTrigger)
                                    {
                                        if (VerifyNifty(timenow) == OType.Buy && !qualified)
                                        {
                                            if ((instruments[instrument].botBB + variance23) < instruments[instrument].topBB
                                                && Decimal.Compare(timenow, Convert.ToDecimal(11.14)) > 0)
                                            {
                                                if (instruments[instrument].oldTime != instruments[instrument].currentTime)
                                                {
                                                    instruments[instrument].oldTime = instruments[instrument].currentTime;
                                                    Console.WriteLine("Time {0} Averting Expansion: Please Ignore this script {1} as it has Expanded so much for now wherein topBB {2} & botBB {3} for Buy", DateTime.Now.ToString(), instruments[instrument].futName, instruments[instrument].topBB, instruments[instrument].botBB);
                                                }
                                                qualified = false;
                                                return false;
                                            }
                                            if ((instruments[instrument].botBB + variance14) > instruments[instrument].topBB
                                                && Decimal.Compare(timenow, Convert.ToDecimal(11.14)) > 0
                                                && !(IsBetweenVariance(high, instruments[instrument].weekMA, (decimal).0006)
                                                        || IsBetweenVariance(high, instruments[instrument].middle30ma50, (decimal).0006)
                                                        || IsBetweenVariance(high, instruments[instrument].top30bb, (decimal).0006)))
                                            {
                                                qualified = ltp <= instruments[instrument].botBB || IsBetweenVariance(ltp, instruments[instrument].botBB, (decimal).0006);
                                                if (qualified)
                                                    Console.WriteLine("You could have AVOIDED this. Qualified for BUY order {0} based on LTP {1} is ~ above long trigger {2} and ltp is around ma50 {3} ", instruments[instrument].futName, ltp, instruments[instrument].longTrigger, instruments[instrument].ma50);
                                            }
                                        }
                                    }
                                    else
                                    {
                                        // Do Nothing
                                    }
                                    if (qualified && instruments[instrument].oldTime != instruments[instrument].currentTime)
                                    {
                                        instruments[instrument].oldTime = instruments[instrument].currentTime;
                                        Console.WriteLine("INSIDER :: Qualified for BUY order based on LTP {0} is ~ above long trigger {1} and ltp is around botBB {2} wherein Cost Price is {3} is still to go", ltp, instruments[instrument].longTrigger, instruments[instrument].botBB, requiredCP);
                                    }
                                }
                                /*if (Decimal.Compare(timenow, Convert.ToDecimal(13.30)) > 0)
                                {
                                    if (VerifyNifty(timenow) == OType.Buy)
                                    {
                                        qualified = (r2 <= instruments[instrument].longTrigger || IsBetweenVariance(r2, instruments[instrument].longTrigger, (decimal).0006))
                                            && (r2 <= instruments[instrument].ma50 || IsBetweenVariance(r2, instruments[instrument].ma50, (decimal).0006));
                                        if (qualified)
                                        {
                                            Console.WriteLine("Time {0} Trigger Verification PASSed: The script {1} as long Trigger {2} vs r2 {3} & ma50 is at {4} for Buy", DateTime.Now.ToString(), instruments[instrument].futName, instruments[instrument].shortTrigger, r2, instruments[instrument].ma50);
                                        }
                                    }
                                }
                                */
                            }
                            else
                            {
                                if (qualified
                                    && !instruments[instrument].isReversed
                                    && instruments[instrument].canTrust)
                                {
                                    //qualified = CalculateBB((uint)instruments[instrument].instrumentId, tickData);
                                }
                                else
                                    qualified = false;
                            }
                            #endregion
                            break;
                        default:
                            break;
                    }
                }
                if (!qualified
                    && !instruments[instrument].canTrust
                    && Decimal.Compare(timenow, Convert.ToDecimal(9.44)) > 0)
                {
                    //if (Decimal.Compare(timenow, Convert.ToDecimal(ConfigurationManager.AppSettings["CutOnTime"])) > 0)
                    //|| (DateTime.Now.Hour == 9 && DateTime.Now.Minute == 44))
                    {
                        qualified = IsBetweenVariance(ltp, instruments[instrument].top30bb, (decimal).0006);
                        if (qualified)
                        {
                            instruments[instrument].shortTrigger = instruments[instrument].top30bb;
                            instruments[instrument].type = OType.Sell;
                            modifyOpenAlignOrReversedStatus(instruments[instrument], 16, OType.Sell, false);
                            //CalculateBB((uint)instruments[instrument].instrumentId, tickData);
                            //qualified = ValidatingCurrentTrend(instrument, tickData);
                            if (!qualified)
                            {
                                Console.WriteLine("{0} DisQualified:: For script {1}, Moving average {2} is either between top30BB {3} & middle30bb {4}", DateTime.Now.ToString(), instruments[instrument].futName, instruments[instrument].middle30ma50, instruments[instrument].top30bb, instruments[instrument].middle30BB);
                                CloseOrderTicker(instrument);
                                return false;
                            }
                        }
                        else
                        {
                            qualified = IsBetweenVariance(ltp, instruments[instrument].bot30bb, (decimal).0006);
                            if (qualified)
                            {
                                instruments[instrument].longTrigger = instruments[instrument].bot30bb;
                                instruments[instrument].type = OType.Buy;
                                modifyOpenAlignOrReversedStatus(instruments[instrument], 16, OType.Buy, false);
                                //CalculateBB((uint)instruments[instrument].instrumentId, tickData);
                                //qualified = ValidatingCurrentTrend(instrument, tickData);
                                if (!qualified)
                                {
                                    Console.WriteLine("{0} DisQualified:: For script {1}, Moving average {2} is either between bot30BB {3} & middle30bb {4}", DateTime.Now.ToString(), instruments[instrument].futName, instruments[instrument].middle30ma50, instruments[instrument].bot30bb, instruments[instrument].middle30BB);
                                    CloseOrderTicker(instrument);
                                    return false;
                                }
                            }
                        }
                        decimal variance = variance17;
                        if (Decimal.Compare(timenow, Convert.ToDecimal(12.44)) > 0)
                        {
                            variance = variance2;
                        }
                        if ((instruments[instrument].bot30bb + variance) > instruments[instrument].top30bb
                            || IsBetweenVariance((instruments[instrument].bot30bb + variance), instruments[instrument].top30bb, (decimal).0006))
                        {
                            qualified = false;
                            if (instruments[instrument].type == OType.Buy
                                && IsBetweenVariance(instruments[instrument].bot30bb, instruments[instrument].middle30ma50, (decimal).001)
                                && instruments[instrument].bot30bb > instruments[instrument].middle30ma50
                                && instruments[instrument].bot30bb + variance14 < instruments[instrument].top30bb
                                && (IsBetweenVariance(ltp, instruments[instrument].middle30ma50, (decimal).0004)
                                    || ltp <= instruments[instrument].middle30ma50))
                            {
                                Console.WriteLine("{0} Variance is lesser than expected range for script {1} But going for risky Buy Order", DateTime.Now.ToString(), instruments[instrument].futName);
                                qualified = true;
                            }
                            else if (instruments[instrument].type == OType.Sell
                                && IsBetweenVariance(instruments[instrument].top30bb, instruments[instrument].middle30ma50, (decimal).001)
                                && instruments[instrument].top30bb < instruments[instrument].middle30ma50
                                && instruments[instrument].bot30bb + variance14 < instruments[instrument].top30bb
                                && (IsBetweenVariance(ltp, instruments[instrument].middle30ma50, (decimal).0004)
                                    || ltp >= instruments[instrument].middle30ma50))
                            {
                                Console.WriteLine("{0} Variance is lesser than expected range for script {1} But going for risky Sell Order", DateTime.Now.ToString(), instruments[instrument].futName);
                                qualified = true;
                            }
                            else
                            {
                                if (qualified && instruments[instrument].oldTime != instruments[instrument].currentTime)
                                {
                                    instruments[instrument].oldTime = instruments[instrument].currentTime;
                                    Console.WriteLine("{0} INSIDER :: DisQualified for order {1} based on LTP {2} as top30BB {3} & bot30BB {4} are within minimum variance range", DateTime.Now.ToString(), instruments[instrument].futName, ltp, instruments[instrument].top30bb, instruments[instrument].bot30bb);
                                }
                            }
                        }
                        //CalculateSqueez(instrument, tickData);
                        //qualified = CalculateSqueez(instrument, tickData);
                        if (qualified && instruments[instrument].oldTime != instruments[instrument].currentTime)
                        {
                            instruments[instrument].oldTime = instruments[instrument].currentTime;
                            Console.WriteLine("{0} INSIDER :: Qualified for order {1} based on LTP {2} is ~ either near top30BB {3} or bot30BB {4}", DateTime.Now.ToString(), instruments[instrument].futName, ltp, instruments[instrument].topBB, instruments[instrument].bot30bb);
                        }
                    }
                }
                #endregion  
            }
            else if (instruments[instrument].status == Status.POSITION)
            {
                try
                {
                    #region Verify and Modify Exit Trigger
                    try
                    {
                        OType trend = CalculateSqueezedTrend(instruments[instrument].futName, instruments[instrument].history, 10);
                        Position pos = ValidateOpenPosition(instrument, instruments[instrument].futId);
                        if (!instruments[instrument].canTrust)
                        {
                            #region Validate Untrusted script
                            if (pos.PNL < -2500 || instruments[instrument].requiredExit)
                            {
                                ModifyOrderForContract(pos, instrument, (decimal)300);
                                instruments[instrument].requiredExit = true;
                            }
                            if ((instruments[instrument].requiredExit
                                    || pos.PNL > -300)
                                && (IsBetweenVariance(ltp, instruments[instrument].middleBB, (decimal).0004)
                                    || (pos.Quantity > 0 && ltp > instruments[instrument].middleBB)
                                    || (pos.Quantity < 0 && ltp < instruments[instrument].middleBB)))
                            {
                                int quantity = pos.Quantity;
                                CancelOrder(pos, instrument);
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
                            #endregion
                        }
                        else
                        {
                            #region ValidateIsExitRequired
                            if (!instruments[instrument].requiredExit
                                && !instruments[instrument].isReorder
                                && instruments[instrument].canTrust)
                            {
                                try
                                {
                                    if (instruments[instrument].requiredExit
                                        || (pos.Quantity > 0 && trend == OType.StrongSell)
                                        || (pos.Quantity < 0 && trend == OType.StrongBuy))
                                    {
                                        ModifyOrderForContract(pos, instrument, (decimal)300);
                                    }
                                }
                                catch (Exception ex)
                                {
                                    Console.WriteLine("EXCEPTION in RequireExit validation at {0} with message {1}", DateTime.Now.ToString(), ex.Message);
                                }
                            }
                            #endregion
                            if (ValidateOrderTime(instruments[instrument].orderTime)
                                && !instruments[instrument].isReorder)
                            {
                                if (pos.Quantity > 0 && trend == OType.StrongSell && instruments[instrument].canTrust)
                                {
                                    ModifyOrderForContract(pos, instrument, 500);
                                }
                                else if (pos.Quantity < 0 && trend == OType.StrongBuy && instruments[instrument].canTrust)
                                {
                                    ModifyOrderForContract(pos, instrument, 500);
                                }
                                if (pos.Quantity > 0 && pos.PNL < -300) //instruments[instrument].type == OType.Buy
                                {
                                    #region Cancel Buy Order
                                    if (instruments[instrument].ma50 > 0
                                        && ((ltp < instruments[instrument].longTrigger
                                                && ltp < instruments[instrument].ma50)
                                            || instruments[instrument].requiredExit))
                                    {
                                        if (!instruments[instrument].isReorder
                                             && instruments[instrument].canTrust)
                                        {
                                            if (instruments[instrument].oldTime != instruments[instrument].currentTime)
                                            {
                                                Console.WriteLine("At {0} : The order of the script {1} is found and validating for modification based on PNL {2}", DateTime.Now.ToString(), instruments[instrument].futName, pos.PNL);
                                            }
                                            if ((pos.PNL > -2000
                                                    || IsBetweenVariance(ltp, instruments[instrument].middleBB, (decimal).0006)
                                                    || ltp > instruments[instrument].middleBB)
                                                || (pos.PNL > -3000 && instruments[instrument].requiredExit && instruments[instrument].doubledrequiredExit))
                                            {
                                                if (trend == OType.Sell || trend == OType.StrongSell || Decimal.Compare(timenow, (decimal)11.15) < 0)
                                                {
                                                    if (instruments[instrument].oldTime != instruments[instrument].currentTime)
                                                    {
                                                        Console.WriteLine("HARDEXIT NOW at {0} :: The BUY order status of the script {1} is better Exit point so EXIT NOW with loss of {2}", DateTime.Now.ToString(), instruments[instrument].futName, instruments[instrument].lotSize * (instruments[instrument].longTrigger - ltp));
                                                        instruments[instrument].oldTime = instruments[instrument].currentTime;
                                                    }
                                                    CancelAndReOrder(instrument, OType.Buy, ltp, pos.PNL);
                                                }
                                            }
                                        }
                                        else if (pos.PNL < -6000
                                            || IsBetweenVariance(ltp, instruments[instrument].bot30bb, (decimal).0006))
                                        {
                                            ModifyOrderForContract(pos, instrument, (decimal)300);
                                        }
                                    }
                                    #endregion
                                }
                                else if (pos.Quantity < 0 && pos.PNL < -300) // instruments[instrument].type == OType.Sell
                                {
                                    #region Cancel Sell Order                                
                                    if (instruments[instrument].ma50 > 0
                                        && ((ltp > instruments[instrument].shortTrigger
                                                && ltp > instruments[instrument].ma50)
                                            || instruments[instrument].requiredExit))
                                    {
                                        if (!instruments[instrument].isReorder
                                            && instruments[instrument].canTrust)
                                        {
                                            if (instruments[instrument].oldTime != instruments[instrument].currentTime)
                                            {
                                                Console.WriteLine("At {0} : The order of the script {1} is found and validating for modification based on PNL {2}", DateTime.Now.ToString(), instruments[instrument].futName, pos.PNL);
                                            }
                                            if ((pos.PNL > -2000
                                                    || IsBetweenVariance(ltp, instruments[instrument].middleBB, (decimal).0006)
                                                    || ltp < instruments[instrument].middleBB)
                                                || (pos.PNL > -3000 && instruments[instrument].requiredExit && instruments[instrument].doubledrequiredExit))
                                            {
                                                if (trend == OType.Buy || trend == OType.StrongBuy || Decimal.Compare(timenow, (decimal)11.15) < 0)
                                                {
                                                    if (instruments[instrument].oldTime != instruments[instrument].currentTime)
                                                    {
                                                        instruments[instrument].oldTime = instruments[instrument].currentTime;
                                                        Console.WriteLine("HARDEXIT NOW at {0} :: The SELL order status of the script {1} is better Exit point so EXIT NOW with loss of {2}", DateTime.Now.ToString(), instruments[instrument].futName, instruments[instrument].lotSize * (ltp - instruments[instrument].shortTrigger));
                                                    }
                                                    CancelAndReOrder(instrument, OType.Sell, ltp, pos.PNL);
                                                }
                                            }
                                        }
                                        else if (pos.PNL < -6000
                                            || IsBetweenVariance(ltp, instruments[instrument].top30bb, (decimal).0006))
                                        {
                                            ModifyOrderForContract(pos, instrument, (decimal)300);
                                        }
                                    }
                                    #endregion
                                }
                                else if (pos.Quantity == 0 && pos.PNL < -300)
                                {
                                    if (!instruments[instrument].isHedgingOrder)
                                    {
                                        CloseOrderTicker(instrument);
                                    }
                                }
                                if (!instruments[instrument].doubledrequiredExit && !instruments[instrument].isReorder)
                                {
                                    ValidateScriptTrend(pos, instrument);
                                    if (pos.PNL < -6000 && !instruments[instrument].doubledrequiredExit)
                                    {
                                        Console.WriteLine("1. OMG This script is bleeding RED at {0} :: The order status of the script {1} has gone seriously bad state with {2}", DateTime.Now.ToString(), instruments[instrument].futName, pos.PNL);
                                        instruments[instrument].requiredExit = true;
                                        instruments[instrument].doubledrequiredExit = true;
                                    }
                                }
                            }
                            else if (instruments[instrument].requiredExit
                                && instruments[instrument].weekMA > 0
                                && instruments[instrument].ma50 > 0
                                && !instruments[instrument].isReorder)
                            {
                                try
                                {
                                    if (!instruments[instrument].doubledrequiredExit
                                        && pos.PNL <= -6000)
                                    {
                                        Console.WriteLine("2. OMG This script is bleeding RED at {0} :: The order status of the script {1} has gone seriously bad state with {2}", DateTime.Now.ToString(), instruments[instrument].futName, pos.PNL);
                                        instruments[instrument].doubledrequiredExit = true;
                                    }
                                    DateTime dt = Convert.ToDateTime(instruments[instrument].orderTime);
                                    if (!instruments[instrument].isReorder) // DateTime.Now > dt.AddMinutes(3))
                                    {
                                        if ((DateTime.Now.Minute >= 12 && DateTime.Now.Minute < 15)
                                            || (DateTime.Now.Minute >= 42 && DateTime.Now.Minute < 45)
                                            || (instruments[instrument].doubledrequiredExit
                                                && IsBetweenVariance(ltp, instruments[instrument].middle30BB, (decimal).0015)))
                                        {
                                            if (instruments[instrument].isReversed
                                                && instruments[instrument].requiredExit)
                                            {
                                                decimal variance2 = (ltp * (decimal)2) / 100;
                                                if (pos.PNL < -1000 && pos.PNL > -2000
                                                    && (instruments[instrument].bot30bb + variance2) < instruments[instrument].top30bb
                                                    || (instruments[instrument].doubledrequiredExit
                                                        && IsBetweenVariance(ltp, instruments[instrument].middle30BB, (decimal).0015)))
                                                {
                                                    if (pos.Quantity > 0 && ltp < instruments[instrument].ma50)
                                                    {
                                                        Console.WriteLine("In VerifyLTP at {0} :: Processing the Order {1} as it has gone seriously bleeding state with {2}", DateTime.Now.ToString(), instruments[instrument].futName, pos.PNL);
                                                        ProcessOpenPosition(pos, instrument, OType.Sell);
                                                    }
                                                    else if (pos.Quantity < 0 && ltp > instruments[instrument].ma50)
                                                    {
                                                        Console.WriteLine("In VerifyLTP at {0} :: Processing the Order {1} as it has gone seriously bleeding state with {2}", DateTime.Now.ToString(), instruments[instrument].futName, pos.PNL);
                                                        ProcessOpenPosition(pos, instrument, OType.Buy);
                                                    }
                                                }
                                            }
                                            else if (!instruments[instrument].isReversed
                                                && (IsBeyondVariance(ltp, instruments[instrument].weekMA, (decimal).002)
                                                    || instruments[instrument].doubledrequiredExit))
                                            {
                                                if (pos.PNL < -1000 && pos.PNL > -2000
                                                    || (instruments[instrument].doubledrequiredExit
                                                        && pos.PNL > -4000))
                                                {
                                                    if (pos.Quantity > 0
                                                        && ltp < instruments[instrument].weekMA
                                                        && IsBetweenVariance(instruments[instrument].weekMA, instruments[instrument].bot30bb, (decimal).004)
                                                        && instruments[instrument].weekMA > instruments[instrument].bot30bb)
                                                    {
                                                        Console.WriteLine("In VerifyLTP at {0} :: Processing the Order {1} as it has gone bad state with {2}", DateTime.Now.ToString(), instruments[instrument].futName, pos.PNL);
                                                        ProcessOpenPosition(pos, instrument, OType.Sell);
                                                    }
                                                    else if (pos.Quantity < 0
                                                        && ltp > instruments[instrument].weekMA
                                                        && IsBetweenVariance(instruments[instrument].weekMA, instruments[instrument].top30bb, (decimal).004)
                                                        && instruments[instrument].weekMA < instruments[instrument].top30bb)
                                                    {
                                                        Console.WriteLine("In VerifyLTP at {0} :: Processing the Order {1} as it has gone bad state with {2}", DateTime.Now.ToString(), instruments[instrument].futName, pos.PNL);
                                                        ProcessOpenPosition(pos, instrument, OType.Buy);
                                                    }
                                                }
                                            }
                                        }
                                    }
                                }
                                catch (Exception ex)
                                {
                                    Console.WriteLine("EXCEPTION in RequireExit Time validation at {0} with message {1}", DateTime.Now.ToString(), ex.Message);
                                }
                            }
                            else if (instruments[instrument].oldTime != instruments[instrument].currentTime
                                && instruments[instrument].isReorder)
                            {
                                instruments[instrument].oldTime = instruments[instrument].currentTime;
                                Console.WriteLine("In VerifyLTP at {0} This is a Reverse Order of {1} current state is as follows", DateTime.Now.ToString(), instruments[instrument].futName);
                                OType currentTrend = CalculateSqueezedTrend(instruments[instrument].futName,
                                    instruments[instrument].history,
                                    10);
                                if (instruments[instrument].type == OType.Sell
                                    && currentTrend == OType.StrongBuy)
                                {
                                    ModifyOrderForContract(pos, (uint)instruments[instrument].futId, 600);
                                    Console.WriteLine("Time to Exit For contract Immediately for the current reversed SELL order of {0} which is placed at {1} should i revise target to {2}", pos.TradingSymbol, pos.AveragePrice);
                                }
                                else if (instruments[instrument].type == OType.Buy
                                    && currentTrend == OType.StrongSell)
                                {
                                    ModifyOrderForContract(pos, (uint)instruments[instrument].futId, 600);
                                    Console.WriteLine("Time to Exit For contract Immediately for the current reversed BUY order of {0} which is placed at {1} should i revise target to {2}", pos.TradingSymbol, pos.AveragePrice);
                                }
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
            else if (instruments[instrument].status == Status.STANDING)
            //&& instruments[instrument].currentTime != instruments[instrument].oldTime)
            {
                #region Check for Standing Orders
                try
                {
                    //instruments[instrument].oldTime = instruments[instrument].currentTime;
                    DateTime dt = DateTime.Now;
                    int counter = 0;
                    foreach (Order order in kite.GetOrders())
                    {
                        if (instruments[instrument].futId == order.InstrumentToken)
                        {
                            if (order.Status == "COMPLETE")
                            {
                                counter++;
                                break;
                            }
                            else if (order.Status == "OPEN")
                            {
                                dt = Convert.ToDateTime(order.OrderTimestamp);
                            }
                            if (DateTime.Now > dt.AddMinutes(6))
                            {
                                try
                                {
                                    Console.WriteLine("Getting OPEN Order Time {0} & Current Time {1} of {2} is more than than 6 minutes. So cancelling the order ID {3}", order.OrderTimestamp, DateTime.Now.ToString(), instruments[instrument].futName, order.OrderId);
                                    kite.CancelOrder(order.OrderId, Variety: "bo");
                                    instruments[instrument].status = Status.OPEN;
                                }
                                catch (Exception ex)
                                {
                                    Console.WriteLine("EXCEPTION at {0}:: Cancelling Idle Order for 20 minutes is failed with message {1}", DateTime.Now.ToString(), ex.Message);
                                }
                            }
                        }
                    }
                    if (counter == 1)
                        instruments[instrument].status = Status.POSITION;
                    else if (counter == 2)
                        CloseOrderTicker(instrument);
                    //else if (counter == 0)
                    //    instruments[instrument].status = Status.POSITION;
                }
                catch (Exception ex)
                {
                    Console.WriteLine("at {0} EXCEPTION in VerifyLTP_STANDING :: The order status of the script {1} is being validated but recieved exception {2}", DateTime.Now.ToString(), instruments[instrument].futName, ex.Message);
                }
                #endregion
            }
            return qualified;
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

    }
}
