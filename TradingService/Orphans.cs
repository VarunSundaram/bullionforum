
namespace TradingService
{
    class Orphans
    {


        /*
         * 
         
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
            decimal variance1 = Math.Round((ltp * (decimal)0.9) / 100, 1);
            decimal variance12 = Math.Round((ltp * (decimal)1.1) / 100, 1);
            decimal variance14 = Math.Round((ltp * (decimal)1.35) / 100, 1);
            decimal variance2 = Math.Round((ltp * (decimal)2) / 100, 1);
            decimal variance43 = Math.Round((ltp * (decimal)4.3) / 100, 1);
            decimal spike2 = Math.Round((ltp * (decimal).0025), 1);
            decimal spike3 = Math.Round((ltp * (decimal).0035), 1);
            decimal spikeA = Math.Round((ltp * (decimal).0075), 1);
            decimal spikeN = Math.Round((ltp * (decimal).009), 1);
            decimal spike = Math.Round((ltp * (decimal).01), 1);
            decimal spikeV = Math.Round((ltp * (decimal).018), 1);
            if (month == 0)
            {
                List<Instrument> calcInstruments = kite.GetInstruments(Constants.EXCHANGE_MCX);
                CalculateExpiry(calcInstruments, DateTime.Now.Month, 0, "MCX-FUT");
            }
            //|| instruments[instrument].isVolatile 
            //|| VerifyNifty(timenow) != OType.BS) 
            #endregion

            if (instruments[instrument].status == Status.OPEN
                && instruments[instrument].middleBB > 0
                && instruments[instrument].bot30bb > 0
                && instruments[instrument].middle30BBnew > 0
                && Decimal.Compare(timenow, Convert.ToDecimal(ConfigurationManager.AppSettings["CutoffTime"])) < 0) //change back to 14.24
            {
                //bool gearingStatus = false;
                bool isGearingStatus = false;
                int var1 = 0, var2 = 0;

                if (prevCandleClose > instruments[instrument].middleBB
                    && Decimal.Compare(timenow, (decimal)10.45) > 0)
                {
                    //gearingStatus = CheckGearingStatus(instrument, OType.Buy, ref candleCount);
                    isGearingStatus = CheckGearingStatus(instrument, OType.Buy);
                    CheckGearingStatus(instrument, OType.Buy, ref var1, ref var2);
                    //instruments[instrument].isRising = !instruments[instrument].isRising ? isGearingStatus : instruments[instrument].isRising;
                }
                else if (prevCandleClose < instruments[instrument].middleBB
                    && Decimal.Compare(timenow, (decimal)10.45) > 0)
                {
                    //gearingStatus = CheckGearingStatus(instrument, OType.Sell, ref candleCount);
                    isGearingStatus = CheckGearingStatus(instrument, OType.Sell);
                    CheckGearingStatus(instrument, OType.Sell, ref var1, ref var2);
                    //instruments[instrument].isFalling = !instruments[instrument].isFalling ? isGearingStatus : instruments[instrument].isFalling;
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
                        && instruments[instrument].top30bb - instruments[instrument].middle30BB >= variance2)
                    {
                        if (CheckRecentStatus(instrument, OType.Buy))
                        {
                            instruments[instrument].goodToGo = false;
                            instruments[instrument].toBuy = false;
                            instruments[instrument].toSell = false;
                            return false;
                        }
                        if (instruments[instrument].topBB - instruments[instrument].middleBB < spikeA
                            && IsBetweenVariance(ltp, instruments[instrument].botBB, (decimal).002))
                        {
                            Console.WriteLine("MCX At {0} This script {1} is xx showing signs of immediate surge with ltp {2}", DateTime.Now, instruments[instrument].futName, ltp);
                        }
                        else if (instruments[instrument].topBB - instruments[instrument].middleBB >= spikeA
                            && IsBetweenVariance(ltp, instruments[instrument].middleBB, (decimal).0006))
                        {
                            Console.WriteLine("MCX At {0} This script {1} is yy showing signs of immediate surge with ltp {2}", DateTime.Now, instruments[instrument].futName, ltp);
                        }
                        instruments[instrument].goodToGo = true;
                        instruments[instrument].toBuy = true;
                        instruments[instrument].toSell = false;
                    }
                    else if (((prevCandleClose < instruments[instrument].middleBB
                                && isGearingStatus)
                            || (instruments[instrument].goodToGo
                                && instruments[instrument].toSell))
                        && instruments[instrument].top30bb - instruments[instrument].middle30BB >= variance2)
                    {
                        if (CheckRecentStatus(instrument, OType.Sell))
                        {
                            instruments[instrument].goodToGo = false;
                            instruments[instrument].toBuy = false;
                            instruments[instrument].toSell = false;
                            return false;
                        }
                        if (instruments[instrument].topBB - instruments[instrument].middleBB < spikeA
                            && IsBetweenVariance(ltp, instruments[instrument].topBB, (decimal).002))
                        {
                            Console.WriteLine("MCX At {0} This script {1} is xx showing signs of immediate deflate with ltp {2}", DateTime.Now, instruments[instrument].futName, ltp);
                        }
                        else if (instruments[instrument].topBB - instruments[instrument].middleBB >= spikeA
                            && IsBetweenVariance(ltp, instruments[instrument].middleBB, (decimal).0006))
                        {
                            Console.WriteLine("MCX At {0} This script {1} is yy showing signs of immediate deflate with ltp {2}", DateTime.Now, instruments[instrument].futName, ltp);
                        }
                        instruments[instrument].goodToGo = true;
                        instruments[instrument].toBuy = false;
                        instruments[instrument].toSell = true;
                    }
                }
                else
                {
                    if (instruments[instrument].dayBollinger[1] - instruments[instrument].dayBollinger[2] > variance43
                        && instruments[instrument].top30bb - instruments[instrument].bot30bb > variance1)
                    {
                        if (instruments[instrument].close >= instruments[instrument].dayBollinger[0])
                        {
                            if ((prevCandleClose < instruments[instrument].middleBB
                                    && isGearingStatus)
                                || (instruments[instrument].goodToGo
                                    && instruments[instrument].toBuy))
                            {
                                if (CheckRecentStatus(instrument, OType.Buy))
                                {
                                    instruments[instrument].goodToGo = false;
                                    instruments[instrument].toBuy = false;
                                    instruments[instrument].toSell = false;
                                    return false;
                                }
                                if (instruments[instrument].top30bb - instruments[instrument].bot30bb < variance12
                                    && IsBeyondVariance(instruments[instrument].top30bb - instruments[instrument].bot30bb, variance12, (decimal).0006))
                                {
                                    //Do nothing
                                }
                                else if (instruments[instrument].topBB - instruments[instrument].middleBB < spike3
                                    && IsBetweenVariance(ltp, instruments[instrument].botBB, (decimal).002))
                                {
                                    if (instruments[instrument].oldTime != instruments[instrument].currentTime)
                                    {
                                        instruments[instrument].oldTime = instruments[instrument].currentTime;
                                        Console.WriteLine("MCX At {0} This script {1} is x showing signs of immediate surge with ltp {2}", DateTime.Now, instruments[instrument].futName, ltp);
                                    }
                                }
                                else if (instruments[instrument].topBB - instruments[instrument].middleBB >= spike3
                                        && IsBetweenVariance(ltp, instruments[instrument].middleBB, (decimal).0006))
                                {
                                    if (instruments[instrument].oldTime != instruments[instrument].currentTime)
                                    {
                                        instruments[instrument].oldTime = instruments[instrument].currentTime;
                                        Console.WriteLine("MCX At {0} This script {1} is y showing signs of immediate surge with ltp {2}", DateTime.Now, instruments[instrument].futName, ltp);
                                    }
                                }
                                instruments[instrument].goodToGo = true;
                                instruments[instrument].toBuy = true;
                                instruments[instrument].toSell = false;
                            }
                        }
                        else if (instruments[instrument].close < instruments[instrument].dayBollinger[0])
                        {
                            if ((prevCandleClose < instruments[instrument].middleBB
                                    && isGearingStatus)
                                || (instruments[instrument].goodToGo
                                    && instruments[instrument].toSell))
                            {
                                if (CheckRecentStatus(instrument, OType.Sell))
                                {
                                    instruments[instrument].goodToGo = false;
                                    instruments[instrument].toBuy = false;
                                    instruments[instrument].toSell = false;
                                    return false;
                                }
                                if (instruments[instrument].top30bb - instruments[instrument].bot30bb < variance12
                                    && IsBeyondVariance(instruments[instrument].top30bb - instruments[instrument].bot30bb, variance12, (decimal).0006))
                                {
                                    //Do nothing
                                }
                                else if (instruments[instrument].topBB - instruments[instrument].middleBB < spikeA
                                    && IsBetweenVariance(ltp, instruments[instrument].topBB, (decimal).002))
                                {
                                    if (instruments[instrument].oldTime != instruments[instrument].currentTime)
                                    {
                                        instruments[instrument].oldTime = instruments[instrument].currentTime;
                                        Console.WriteLine("MCX At {0} This script {1} is x showing signs of immediate deflate with ltp {2}", DateTime.Now, instruments[instrument].futName, ltp);
                                    }
                                }
                                else if (instruments[instrument].topBB - instruments[instrument].middleBB >= spikeA
                                        && IsBetweenVariance(ltp, instruments[instrument].middleBB, (decimal).0006))
                                {
                                    if (instruments[instrument].oldTime != instruments[instrument].currentTime)
                                    {
                                        instruments[instrument].oldTime = instruments[instrument].currentTime;
                                        Console.WriteLine("MCX At {0} This script {1} is y showing signs of immediate deflate with ltp {2}", DateTime.Now, instruments[instrument].futName, ltp);
                                    }
                                }
                                instruments[instrument].goodToGo = true;
                                instruments[instrument].toBuy = false;
                                instruments[instrument].toSell = true;
                            }
                        }
                    }
                    else
                    {
                        instruments[instrument].goodToGo = false;
                        instruments[instrument].toBuy = false;
                        instruments[instrument].toSell = false;
                    }
                }
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
                                    ModifyOrderForContract(pos, instrument, (decimal)350);
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

                        if (instruments[instrument].movement > 2 &&
                            ((pos.Quantity > 0 && IsBetweenVariance(ltp, instruments[instrument].bot30bb, (decimal).0004))
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
                            Comment lines close it here....
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
                                        || (pos.Quantity > 0 && trend == OType.StrongSell && ltp<(instruments[instrument].middle30BB - (instruments[instrument].middle30BB* (decimal).0006)))
                                        || (pos.Quantity< 0 && trend == OType.StrongBuy && ltp> (instruments[instrument].middle30BB + (instruments[instrument].middle30BB* (decimal).0006))))
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

         *
         *
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
                    System.Runtime.Remoting.Messaging.CallContext.FreeNamedDataSlot("log4net.Util.LogicalThreadContextProperties");
                    AmazonS3Client _s3Client = new AmazonS3Client(_accessKey, _secretKey, RegionEndpoint.APSouth1);

                    PutObjectRequest request;
                    PutObjectResponse response;
                    try
                    {
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
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine("Exception while uploading Log file; {0}", ex.StackTrace);
                    }
                    try
                    {
                        decimal timenow = (decimal)DateTime.Now.Hour + ((decimal)DateTime.Now.Minute / 100);
                        if (File.Exists(ConfigurationManager.AppSettings["inputFile"])
                            && (Decimal.Compare(timenow, (decimal)9.38)) < 0)
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
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine("Exception while uploading Watchlist file; {0}", ex.Message);
                    }
                    
                    //try
                    //{
                    //    request = new PutObjectRequest
                    //    {
                    //        //https://077990728068.signin.aws.amazon.com/console
                    //        BucketName = @"mousecursorservice",
                    //        Key = "Status.txt",
                    //        FilePath = ConfigurationManager.AppSettings["StartupFile"]
                    //    };

                    //    response = _s3Client.PutObject(request);
                    //}
                    //catch (Exception ex)
                    //{
                    //    Console.WriteLine("Exception while uploading service Status; {0}", ex.Message);
                    //}
                    
                    }
                                else
                                    Console.WriteLine("NO Such poweshell File is found in the drive; S3 Upload is halted");
                            }
                            catch (Exception ex)
                {
                    Console.WriteLine("EXCEPTION CAUGHT while uploading to S3 Bucket with {0}", ex.Message);
                }
            }

                    /*
                    PositionResponse positions = kite.GetPositions();
                    for(int i = 0; i < positions.Day.Count; i++)
                    {
                        Position pos = positions.Day[i];
                        if(pos.InstrumentToken == mToken.futId)
                        {
                            Console.WriteLine("Open Position Details which is Open : {0}", Utils.JsonSerialize(pos));
                        }
                    }


                         try
                        {
                            if (Decimal.Compare(timenow, (decimal)(10.6)) > 0)
                            {
                                DateTime previousDay = DateTime.Now.Date;
                                DateTime currentDay = DateTime.Now.Date.AddDays(1);
                                List<Historical> history = kite.GetHistoricalData(mToken.instrumentId.ToString(),
                                                previousDay, currentDay, "30minute");
                                Historical h1, h2;
                                h1 = history[history.Count - 2];
                                h2 = history[history.Count - 1];
                                if (mToken.type == OType.Sell)
                                {
                                    decimal square1 = (h2.High - h1.Low);
                                    decimal square2 = ltp.LastPrice / 100 * (decimal)2;
                                    if (square1 >= square2)
                                    {
                                        Console.WriteLine("Not Placing Order as this Instrument is Rise above 2% in last 1 hour");
                                        //modifyOrderInCSV(OType.Sell, Status.CLOSE);
                                        //return;
                                    }
                                }
                                else if (mToken.type == OType.Buy)
                                {
                                    decimal square1 = (h1.High - h2.Low);
                                    decimal square2 = ltp.LastPrice / 100 * (decimal)2;
                                    if (square1 >= square2)
                                    {
                                        Console.WriteLine("Not Placing Order as this Instrument is FALLEN Below 2% in last 1 hour");
                                        modifyOrderInCSV(OType.Sell, Status.CLOSE);
                                        return;
                                    }
                                }
                            }
                        }
                        catch (System.TimeoutException te)
                        {
                            Console.WriteLine("EXCEPTION ::: TimeStamp{0} TIMEOUT EXCEPTION CAUGHT while retrieving HISTORICAL DATA. Message ", DateTime.Now.ToString("yyyyMMdd hh:mm:ss"), te.Message);
                        }
                        catch (System.IndexOutOfRangeException te)
                        {
                            Console.WriteLine("EXCEPTION ::: TimeStamp{0} Index Out of Range EXCEPTION CAUGHT while retrieving HISTORICAL DATA. Message ", DateTime.Now.ToString("yyyyMMdd hh:mm:ss"), te.Message);
                        }
                        catch(Exception ex)
                        {
                            Console.WriteLine("EXCEPTION ::: TimeStamp{0} with Message {1}", DateTime.Now.ToString("yyyyMMdd hh:mm:ss"), ex.Message);
                        }

                                /*
                                if (r2 <= mToken.longTrigger)
                                {
                                    mToken.type = OType.Buy;
                                    return true;
                                }
                                else if (r1 >= mToken.shortTrigger)
                                {
                                    if (tickData.Open == tickData.High)
                                    {
                                        mToken.type = OType.Sell;
                                        return true;
                                    }
                                    else
                                        return false;
                                }
                                return false;

         * 
         *                 /*System.Collections.ObjectModel.ReadOnlyCollection<OpenQA.Selenium.IWebElement> inputs = SeleniumManager.Current.ActiveBrowser.driver.FindElements(OpenQA.Selenium.By.XPath("//form[@class='twofa-form form']//input"));
                        if (inputs.Count > 1)
                        {
                            inputs[0].SendKeys(TwoFA);
                            inputs[1].SendKeys(TwoFA);
                            SeleniumManager.Current.ActiveBrowser.driver.FindElement(OpenQA.Selenium.By.XPath("//button[contains(text(),'Continue')]")).Click();
                            SeleniumManager.Current.ActiveBrowser.WaitUntilReady();
                            Thread.Sleep(30000);
                            return true;
                        }
                        else
                        {
                            OutToFile.WriteToFile("Error Login into Kite Application. Execution Aborted");
                            if (SeleniumManager.Current.ActiveBrowser != null)
                                SeleniumManager.Current.ActiveBrowser.CloseDriver();
                            return false;
                        }

     * 
     * 

                    #region tokenstatus = position
                    //if (mToken.order.AveragePrice == 0)
                    {
                        List<Order> listOrder = kite.GetOrders();
                        for (int j = 0; j < listOrder.Count; j++)
                        {
                            Order order = listOrder[j];
                            if (order.OrderType == Constants.ORDER_TYPE_LIMIT && (order.Status == "OPEN"))
                            {
                                if (order.InstrumentToken == mToken.futId)
                                {
                                    Console.WriteLine("ORDER Details {0}", order.InstrumentToken);
                                    //mToken.order = order;
                                    break;
                                }
                            }
                        }
                    }
                    //if (mToken.order.Status == "OPEN")
                    {
                        Dictionary<string, LTP> dicLtp = kite.GetLTP(new string[] { mToken.futId.ToString() });
                        LTP ltp = new LTP();
                        dicLtp.TryGetValue(mToken.futId.ToString(), out ltp);
                        //if (mToken.type == OType.Buy && ltp.LastPrice < mToken.order.AveragePrice && mToken.order.AveragePrice > 0)
                        //{
                            //if (((mToken.order.AveragePrice - ltp.LastPrice) * mToken.lotSize) > 3300)
                            {
                                List<Order> listOrder = kite.GetOrders();
                                for (int j = 0; j < listOrder.Count; j++)
                                {
                                    Order order = listOrder[j];
                                    if (order.InstrumentToken == mToken.futId)
                                    {
                                        Console.WriteLine("Open ORDER Details which crossed Loss margin : {0}", Utils.JsonSerialize(order));
                                        if (order.OrderType != Constants.ORDER_TYPE_LIMIT || order.Status == "COMPLETE")
                                            continue;
                                        decimal newtarget = (400 / mToken.lotSize);
                                        string nt = newtarget.ToString("#.##");
                                        newtarget = Convert.ToDecimal(nt);
                                        //order.Price = (decimal)(mToken.order.AveragePrice + newtarget);
                                    }
                                }
                            }
                        //}
                        else if (mToken.type == OType.Sell && ltp.LastPrice > mToken.order.AveragePrice && mToken.order.AveragePrice > 0)
                        {
                            if (((ltp.LastPrice - mToken.order.AveragePrice) * mToken.lotSize) > 3000)
                            {
                                List<Order> listOrder = kite.GetOrders();
                                for (int j = 0; j < listOrder.Count; j++)
                                {
                                    Order order = listOrder[j];
                                    if (order.InstrumentToken == mToken.futId)
                                    {
                                        Console.WriteLine("Open ORDER Details which crossed Loss margin : {0}", Utils.JsonSerialize(order));
                                        if (order.OrderType != Constants.ORDER_TYPE_LIMIT || order.Status != "OPEN")
                                            continue;
                                        decimal newtarget = (400 / mToken.lotSize);
                                        string nt = newtarget.ToString("#.##");
                                        newtarget = Convert.ToDecimal(nt);
                                        //order.Price = (decimal)(mToken.order.AveragePrice - newtarget);
                                    }
                                }
                            }
                        }
                    }
                    #endregion
    *
    *
    *            else if (mToken.status == Status.POSITION)
                {
                    #region tokenstatus = position
                    if(mToken.order.AveragePrice == 0)
                    {
                        List<Order> listOrder = kite.GetOrders();
                        for (int j = 0; j < listOrder.Count; j++)
                        {
                            Order order = listOrder[j];
                            if (order.OrderType == Constants.ORDER_TYPE_LIMIT && (order.Status == "OPEN"))
                            {
                                if (order.InstrumentToken == mToken.futId)
                                {
                                    Console.WriteLine("ORDER Details {0}", order.InstrumentToken);
                                    mToken.order = order;
                                    break;
                                }
                            }
                        }
                    }
                    if (mToken.order.Status == "OPEN")
                    {
                        Dictionary<string, LTP> dicLtp = kite.GetLTP(new string[] { mToken.futId.ToString() });
                        LTP ltp = new LTP();
                        dicLtp.TryGetValue(mToken.futId.ToString(), out ltp);
                        if (mToken.type == OType.Buy && ltp.LastPrice < mToken.order.AveragePrice && mToken.order.AveragePrice > 0)
                        {
                            if (((mToken.order.AveragePrice - ltp.LastPrice) * mToken.lotSize) > 3300)
                            {
                                List<Order> listOrder = kite.GetOrders();
                                for (int j = 0; j < listOrder.Count; j++)
                                {
                                    Order order = listOrder[j];
                                    if (order.InstrumentToken == mToken.futId)
                                    {
                                        Console.WriteLine("Open ORDER Details which crossed Loss margin : {0}", Utils.JsonSerialize(order));
                                        if (order.OrderType != Constants.ORDER_TYPE_LIMIT || order.Status == "COMPLETE")
                                            continue;
                                        decimal newtarget = (400 / mToken.lotSize);
                                        string nt = newtarget.ToString("#.##");
                                        newtarget = Convert.ToDecimal(nt);
                                        order.Price = (decimal)(mToken.order.AveragePrice + newtarget);
                                    }
                                }
                            }
                        }
                        else if (mToken.type == OType.Sell && ltp.LastPrice > mToken.order.AveragePrice && mToken.order.AveragePrice > 0)
                        {
                            if (((ltp.LastPrice - mToken.order.AveragePrice) * mToken.lotSize) > 3000)
                            {
                                List<Order> listOrder = kite.GetOrders();
                                for (int j = 0; j < listOrder.Count; j++)
                                {
                                    Order order = listOrder[j];
                                    if (order.InstrumentToken == mToken.futId)
                                    {
                                        Console.WriteLine("Open ORDER Details which crossed Loss margin : {0}", Utils.JsonSerialize(order));
                                        if (order.OrderType != Constants.ORDER_TYPE_LIMIT || order.Status != "OPEN")
                                            continue;
                                        decimal newtarget = (400 / mToken.lotSize);
                                        string nt = newtarget.ToString("#.##");
                                        newtarget = Convert.ToDecimal(nt);
                                        order.Price = (decimal)(mToken.order.AveragePrice - newtarget);
                                    }
                                }
                            }
                        }
                    }
                    #endregion

    *
    *
    *
    * 
    * 
    * 
    *                 Dictionary<string, OHLC> dicOhlc = kite.GetOHLC(new string[] { ConfigurationManager.AppSettings["NSENIFTY"].ToString() });
                    OHLC ohlc = new OHLC();
                    dicOhlc.TryGetValue(ConfigurationManager.AppSettings["NSENIFTY"].ToString().ToString(), out ohlc);
                    decimal square1 = (tickData.Open / 100) * (decimal)0.8;

                    //if ((ohlc.Close - ohlc.LastPrice) <= 40)
                    {
                        if(IsOpenLowHigh(tickData.Open, tickData.Low, (decimal).001) && mToken.type == OType.Buy)
                            if (IsOpenLowHigh(tickData.Open, mToken.middleBB, (decimal).4) && tickData.LastPrice < (mToken.shortTrigger - square1))
                            {
                                Console.WriteLine("Script {0} is Trading with OPEN LOW {1} == {2}; Hence going Long and given signal by tool to LONG", mToken.futName, tickData.Open, tickData.Low);
                                qualified = true;
                            }
                    }
                    //else if ((ohlc.LastPrice - ohlc.Close) <= 40)
                    {
                        if (IsOpenLowHigh(tickData.Open, tickData.High, (decimal).001)) // && mToken.type == OType.Sell
                            if (IsOpenLowHigh(tickData.Open, mToken.middleBB, (decimal).4) && tickData.LastPrice > (mToken.longTrigger + square1))
                            {
                                if(mToken.type == OType.Sell)
                                    //Console.WriteLine("NIFTY up by 40 Pts and Script {0} is trading with OPEN HIGH {1} == {2}; Hence going Short and given signal by tool to SHORT", mToken.futName, tickData.Open, tickData.Low);
                                    Console.WriteLine("Script {0} is Trading with OPEN HIGH {1} == {2}; Hence going Short and given signal by tool to SHORT", mToken.futName, tickData.Open, tickData.Low);
                                else
                                    //Console.WriteLine("NIFTY up by 40 Pts and Script {0} is trading with OPEN HIGH {1} == {2}; Hence going Short and given signal by tool to LONG", mToken.futName, tickData.Open, tickData.Low);
                                    Console.WriteLine("Script {0} is Trading with OPEN HIGH {1} == {2}; Hence going Short and given signal by tool to LONG", mToken.futName, tickData.Open, tickData.Low);
                                qualified = true;
                                mToken.type = OType.Sell;
                            }
                    }
                    //else
                        Console.WriteLine("NIFTY is Opened at {0} and trading at {1} with last Close {2}. Hence difference is {3}", ohlc.Open, ohlc.LastPrice, ohlc.Close, ohlc.Close - ohlc.LastPrice);
                }
                else
                {




                    /*
                    range = Convert.ToDecimal(((ltp * (decimal).6) / 100).ToString("#.#"));
                    if ((topBB - botBB) < range)
                    {
                        if (mToken.type == OType.Sell)
                        {
                            Console.WriteLine("Time Stamp {0} : Recommending to Place ORDER IN BUY direction as TopBB {1} BotBB {2} and thier Difference is {3} but LTP's range at {4} for Script {5}", DateTime.Now.ToString("yyyyMMdd hh:mm:ss"), topBB, botBB, topBB - botBB, range, mToken.futName);
                            mToken.target = 2300;
                            mToken.type = OType.Buy;
                            return true;
                        }
                        else if (mToken.type == OType.Buy)
                        {
                            Console.WriteLine("Time Stamp {0} : Recommending to Place ORDER IN SELL direction as TopBB {1} BotBB {2} and thier Difference is {3} but LTP's range at {4} for Script {5}", DateTime.Now.ToString("yyyyMMdd hh:mm:ss"), topBB, botBB, topBB - botBB, range, mToken.futName);
                            mToken.target = 2300;
                            mToken.type = OType.Sell;
                            return true;
                        }
                    }
                    else
                        Console.WriteLine("Time Stamp {0} : Recommending not to Place ORDER as TopBB {1} BotBB {2} and thier Difference is {3} but LTP's range at {4} for Script {5}", DateTime.Now.ToString("yyyyMMdd hh:mm:ss"), topBB, botBB, topBB - botBB, range, mToken.futName);




        public class TradersChoice
    {
        #region Basic Functions and Parameters
        string myApiKey;
        string mySecretKey;

        public Ticker ticker;
        string myAccessToken;
        //string myPublicToken;

        WatchList mToken;
        Kite kite;

        #region create New Kite Session and Access Token
        private void NewKiteSession()
        {
            kite = new Kite(myApiKey, Debug: true);
            string url = kite.GetLoginURL();

            string requestToken;
            string username = ConfigurationManager.AppSettings["UserID"];
            string password = ConfigurationManager.AppSettings["Password"];
            string TwoFA = ConfigurationManager.AppSettings["2FA"];

            SeleniumManager.Current.LaunchBrowser(url, BrowserType.Chrome, "", true);
            SeleniumManager.Current.ActiveBrowser.WaitUntilReady();
            KiteInterface.GetInstance.Login(username, password, TwoFA);

            requestToken = KiteInterface.GetInstance.GetRequestToken();
            User user = kite.GenerateSession(requestToken, mySecretKey);

            kite.SetAccessToken(user.AccessToken);
            myAccessToken = user.AccessToken;
            //myPublicToken = user.PublicToken;
        }
        #endregion

        public TradersChoice(string apiKey, string secretKey, string accessToken, Kite kt)
        {
            myApiKey = apiKey;
            mySecretKey = secretKey;
            myAccessToken = accessToken;
            kite = kt;
        }

        public void ThreadService(WatchList ptoken)
        {
            mToken = ptoken;
            initTicker();
        }

        private void initTicker()
        {
            ticker = new Ticker(myApiKey, myAccessToken);

            ticker.OnTick += OnTick;
            ticker.OnReconnect += OnReconnect;
            ticker.OnNoReconnect += OnNoReconnect;
            ticker.OnError += OnError;
            ticker.OnClose += OnClose;
            ticker.OnConnect += OnConnect;
            ticker.OnOrderUpdate += OnOrderUpdate;

            ticker.EnableReconnect(Interval: 5, Retries: 50);
            ticker.Connect();

            // Subscribing to Given Instrument ID and setting mode to LTP
            ticker.Subscribe(Tokens: new UInt32[] { (uint)mToken.instrumentId,  });
            ticker.SetMode(Tokens: new UInt32[] { (uint)mToken.instrumentId }, Mode: Constants.MODE_FULL);
        }

        private void OnTokenExpire()
        {
            Console.WriteLine("Need to login again");
            //init();
        }

        private void OnConnect()
        {
            //Console.WriteLine("Connected ticker");
        }

        private void OnClose()
        {

            Console.WriteLine("Closed ticker");
        }

        private void OnError(string Message)
        {
            if (!(Message.Contains("Error parsing instrument tokens") || 
                Message.Contains("The WebSocket has already been started") ||
                Message.Contains("Too many requests at time")))
                Console.WriteLine("Error: {0} at time stamp{1}", Message, DateTime.Now.ToString("yyyyMMdd hh: mm:ss"));
            if(Message.Contains("The WebSocket protocol is not supported on this platform"))
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
                
    }
}

private void OnNoReconnect()
{
    Console.WriteLine("Not reconnecting");
}

private void OnReconnect()
{
    Console.WriteLine("Trying to Reconnect..");
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
#endregion

private void OnTick(Tick tickData)
{
    //string json = Utils.JsonSerialize(TickData);
    if (mToken.status == Status.OPEN)
    {
        #region tokenstatus == Open
        //Console.WriteLine("Tick for " + mToken.futName + " : LTP is " + TickData.LastPrice);
        decimal timenow = (decimal)DateTime.Now.Hour + ((decimal)DateTime.Now.Minute / 100);
        decimal cutoff = Convert.ToDecimal(ConfigurationManager.AppSettings["CutoffTime"]);
        if (!(Decimal.Compare(timenow, cutoff) > 0) && Decimal.Compare(timenow, (decimal)(9.17)) > 0)
        {
            bool noOpenOrder = true;
            if (VerifyLtp(tickData))
            {
                List<Order> listOrder = kite.GetOrders();
                for (int j = 0; j < listOrder.Count; j++)
                {
                    Order order = listOrder[j];
                    //Console.WriteLine("ORDER Details {0}", order.InstrumentToken);
                    if (order.InstrumentToken == mToken.futId)
                    {
                        noOpenOrder = false;
                        if (order.OrderType == Constants.ORDER_TYPE_LIMIT && (order.Status == "OPEN"))
                        {
                            //mToken.order = order;
                            mToken.status = Status.POSITION;
                            break;
                        }
                    }
                }
                if (noOpenOrder)
                {
                    if (mToken.type == OType.Buy)
                        Console.WriteLine("Placing Order of Instrument {0} for LTP {1} as it match long trigger {2}", mToken.futName, tickData.LastPrice.ToString(), mToken.longTrigger);
                    else
                        Console.WriteLine("Placing Order of Instrument {0} for LTP {1} as it match Short trigger {2}", mToken.futName, tickData.LastPrice.ToString(), mToken.shortTrigger);
                    placeOrder(tickData.LastPrice - tickData.Close);
                }
            }
        }
        #endregion
    }
    /*
    PositionResponse positions = kite.GetPositions();
    for(int i = 0; i < positions.Day.Count; i++)
    {
        Position pos = positions.Day[i];
        if(pos.InstrumentToken == mToken.futId)
        {
            Console.WriteLine("Open Position Details which is Open : {0}", Utils.JsonSerialize(pos));
        }
    }
    //    OutToFile.WriteToFile("Tick for " + Utils.JsonSerialize(TickData));
}

public bool VerifyLtp(Tick tickData)
{
    bool qualified = false;
    double ltp = (double)tickData.LastPrice;
    decimal timenow = (decimal)DateTime.Now.Hour + ((decimal)DateTime.Now.Minute / 100);
    if (Decimal.Compare(timenow, (decimal)(10.05)) > 0)
    {
        decimal r1 = Convert.ToDecimal((ltp + ltp * .0005).ToString("#.#"));
        decimal r2 = Convert.ToDecimal((ltp - ltp * .0005).ToString("#.#"));
        //decimal square2 = (tickData.Open / 100) * (decimal)0.5;
        switch (mToken.type)
        {
            case OType.Sell:
                qualified = r1 >= mToken.shortTrigger;
                if (qualified)
                {
                    qualified = CalculateBB(tickData.LastPrice);
                    if (!qualified)
                    {
                        string addMore = ((decimal)(mToken.shortTrigger) + ((decimal)mToken.shortTrigger / 100)).ToString("#.#");
                        modifyOrderInCSV(OType.Sell, addMore);
                        mToken.shortTrigger = Convert.ToDecimal(addMore);
                        //modifyOrderInCSV(OType.Sell, Status.CLOSE);
                    }
                }
                else
                {
                    if (Decimal.Compare(timenow, (decimal)(12.15)) > 0)
                    {
                        //CalculateBB(tickData.LastPrice);
                    }
                }
                break;
            case OType.Buy:
                qualified = r2 <= mToken.longTrigger;
                if (qualified)
                {
                    qualified = CalculateBB(tickData.LastPrice);
                    if (!qualified)
                    {
                        string addMore = ((decimal)(mToken.longTrigger) - ((decimal)mToken.longTrigger / 100)).ToString("#.#");
                        modifyOrderInCSV(OType.Buy, addMore);
                        mToken.shortTrigger = Convert.ToDecimal(addMore);
                        //modifyOrderInCSV(OType.Buy, Status.CLOSE);
                    }
                }
                else
                {
                    if (Decimal.Compare(timenow, (decimal)(12.15)) > 0)
                    {
                        CalculateBB(tickData.LastPrice);
                    }
                }
                break;
            default:
                break;
        }
    }
    return qualified;
}

bool IsOpenLowHigh(decimal open, decimal lowhigh, decimal variance)
{
    decimal r1 = Convert.ToDecimal((open + open * (decimal)variance).ToString("#.#"));
    decimal r2 = Convert.ToDecimal((open - open * (decimal)variance).ToString("#.#"));
    if (lowhigh >= r1 && lowhigh <= r2)
    {
        Console.WriteLine("Given LOW/HIGH {0} is in range of OPEN +/- by {1} ie., between {2} & {3}", lowhigh, variance, r1, r2);
        return true;
    }
    return false;
}

void placeOrder(decimal difference)
{
    Dictionary<string, dynamic> response;
    //double r1 = Convert.ToDouble((ltp - ltp * .0005).ToString("#.#"));
    Quote ltp = new Quote();

    decimal target, stopLoss, trigger;
    decimal timenow = (decimal)DateTime.Now.Hour + ((decimal)DateTime.Now.Minute / 100);
    try
    {
        Dictionary<string, Quote> dicLtp = kite.GetQuote(new string[] { mToken.futId.ToString() });
        dicLtp.TryGetValue(mToken.futId.ToString(), out ltp);
        if (Decimal.Compare(timenow, (decimal)(9.27)) < 0)
        {
            decimal square1 = (ltp.Open / 100) * (decimal)0.8;
            if (mToken.type == OType.Sell)
            {
                if ((ltp.Low - ltp.Close) > square1 && IsOpenLowHigh(ltp.Open, ltp.Low, (decimal).0005))
                {
                    Console.WriteLine("Not Placing SELL Order as this Instrument is open-Low below almost by 1%. Converting to BUY order");
                    mToken.type = OType.Buy;
                    //target = ((decimal)4300 / (decimal)mToken.lotSize);
                    //modifyOrderInCSV(OType.Sell, Status.CLOSE);
                    //return;
                }
            }
            else if (mToken.type == OType.Buy)
            {
                if ((ltp.Close - ltp.High) > square1 && IsOpenLowHigh(ltp.Open, ltp.High, (decimal).0005))
                {
                    Console.WriteLine("Not Placing BUY Order as this Instrument is open-high above almost by 1%. Converting to SELL order");
                    mToken.type = OType.Sell;
                    //target = ((decimal)4300 / (decimal)mToken.lotSize);
                    //modifyOrderInCSV(OType.Buy, Status.CLOSE);
                    //return;
                }
            }
        }
        else
        {
            if (mToken.morningOrder)
            {
                Console.WriteLine("Not scheduling execution as this Instrument is open only until Morning Trade");
                modifyOrderInCSV(OType.Sell, Status.CLOSE);
                return;
            }
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine("EXCEPTION CAUGHT while Validating Trigger :: " + ex.Message);
    }
    stopLoss = (decimal)9000 / (decimal)mToken.lotSize;
    string sl = stopLoss.ToString("#.#");
    stopLoss = Convert.ToDecimal(sl);
    target = ((decimal)mToken.target / (decimal)mToken.lotSize);
    sl = target.ToString("#.#");
    target = Convert.ToDecimal(sl);

    /*
    Console.WriteLine(Utils.JsonSerialize(ltp));
    Dictionary<string, Quote> quotes = kite.GetQuote(InstrumentId: new string[] { "NSE:INFY", "NSE:ASHOKLEY", "NSE:HINDALCO19JANFUT" });
    Dictionary<string, Ltp> quotes = kite.GetLtp(new string[] { mToken.futId.ToString() });
    Dictionary<string, OHLC> ohlc = kite.GetOHLC(new string[] { mToken.futId.ToString() });
    Console.WriteLine(Utils.JsonSerialize(quotes));
    Console.WriteLine(Utils.JsonSerialize(ohlc));

    try
    {
        if (mToken.type == OType.Sell && mToken.status == Status.OPEN)
        {
            //if (ltp.LastPrice - ltp.Close >= difference && ltp.Close > 0 && ltp.LastPrice > 0)
            //    trigger = ltp.LastPrice;
            if (Decimal.Compare(timenow, (decimal)(9.27)) < 0)
                trigger = ltp.Offers[1].Price; //ltp.Close + difference;
            else
                trigger = ltp.Offers[1].Price; //ltp.LastPrice;

            Console.WriteLine("Spot varied by {0}; Future trading at {1} with previous close {2}; Hence chosen Trigger is {3} as FUT variance is {4}",
                difference.ToString(), ltp.LastPrice.ToString(), ltp.Close.ToString(), trigger.ToString(), (ltp.LastPrice - ltp.Close).ToString());

            response = kite.PlaceOrder(
                Exchange: Constants.EXCHANGE_NFO,
                TradingSymbol: mToken.futName,
                TransactionType: Constants.TRANSACTION_TYPE_SELL,
                Quantity: mToken.lotSize,
                Price: trigger,
                Product: Constants.PRODUCT_MIS,
                OrderType: Constants.ORDER_TYPE_LIMIT,
                StoplossValue: stopLoss,
                SquareOffValue: target,
                Validity: Constants.VALIDITY_DAY,
                Variety: Constants.VARIETY_BO
                );
            Console.WriteLine("SELL Order STATUS::::" + Utils.JsonSerialize(response));
            mToken.status = Status.STANDING;
            Console.WriteLine("SELL Order Details: Instrument ID : {0}; Quantity : {1}; Price : {2}; SL : {3}; Target {4} ", mToken.futName, mToken.lotSize, ltp.LastPrice, stopLoss, target);
        }
        else if (mToken.type == OType.Buy && mToken.status == Status.OPEN)
        {
            if (Decimal.Compare(timenow, (decimal)(9.27)) < 0)
                trigger = ltp.Bids[1].Price; //ltp.Close - difference;
            else
                trigger = ltp.Bids[1].Price; //ltp.LastPrice;

            response = kite.PlaceOrder(
                Exchange: Constants.EXCHANGE_NFO,
                TradingSymbol: mToken.futName,
                TransactionType: Constants.TRANSACTION_TYPE_BUY,
                Quantity: mToken.lotSize,
                Price: trigger,
                OrderType: Constants.ORDER_TYPE_LIMIT,
                Product: Constants.PRODUCT_MIS,
                StoplossValue: stopLoss,
                SquareOffValue: target,
                Validity: Constants.VALIDITY_DAY,
                Variety: Constants.VARIETY_BO
                );
            Console.WriteLine("BUY Order STATUS::::" + Utils.JsonSerialize(response));
            mToken.status = Status.STANDING;
            Console.WriteLine("BUY Order Details: Instrument ID : {0}; Quantity : {1}; Price : {2}; SL : {3}; Target {4} ", mToken.futName, mToken.lotSize, ltp.LastPrice, stopLoss, target);
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine("EXCEPTION CAUGHT WHILE PLACING ORDER :: " + ex.Message);
    }
}

private void OnOrderUpdate(Order OrderData)
{
    //COMPLETE, REJECTED, CANCELLED, and OPEN
    //mToken.order = OrderData;
    if (mToken.futId == OrderData.InstrumentToken)
    {
        if (OrderData.Status == "TRIGGER PENDING")
            Console.WriteLine("Order Update for {0} did type {1}, variety {2} at price {3} and its status is '{4}'", OrderData.Tradingsymbol, OrderData.OrderType, OrderData.Variety, OrderData.TriggerPrice, OrderData.Status);
        else
            Console.WriteLine("Order Update for {0} did type {1}, variety {2} at price {3} and its status is '{4}' with Parent Order {5}", OrderData.Tradingsymbol, OrderData.OrderType, OrderData.Variety, OrderData.Price, OrderData.Status, OrderData.ParentOrderId);
        if (OrderData.Status == "OPEN" && mToken.status != Status.POSITION)
        {
            mToken.status = Status.STANDING;
            modifyOrderInCSV(mToken.type, Status.STANDING);
        }
        if (OrderData.Status == "REJECTED" || OrderData.Status == "CANCELLED")
        {
            //closeOrderInCSV();
        }
        else if (OrderData.Status == "COMPLETE")
        {
            if (mToken.status == Status.POSITION)
            {
                mToken.status = Status.CLOSE;
                modifyOrderInCSV(mToken.type, Status.CLOSE);
            }
            else
            {
                mToken.status = Status.POSITION;
                modifyOrderInCSV(mToken.type, Status.POSITION);
            }
        }
    }
    else
    {
        //Console.WriteLine("NOTE :: Event Triggered for the Thread {0}. ~Comment this line in Future~", mToken.futName);
    }
}

private void modifyOrderInCSV(OType type, string newTrigger)
{
    List<String> lines = new List<String>();
    using (StreamReader reader = new StreamReader(ConfigurationManager.AppSettings["inputFile"]))
    {
        String line;
        while ((line = reader.ReadLine()) != null)
        {
            if (line.Contains(mToken.futId.ToString()))
            {
                if (mToken.type == OType.Sell)
                {
                    Console.WriteLine("Modify the Open Order Trigger :: {0} with new SHORT value {1} for the day {2} ", mToken.futName, newTrigger.ToString(), DateTime.Now.ToString("yyyyMMdd hh: mm:ss"));
                    line = line.Replace(mToken.shortTrigger.ToString(), newTrigger.ToString());
                }
                else
                {
                    Console.WriteLine("Modify the Open Order Trigger :: {0} with new LONG value {1} for the day {2} ", mToken.futName, newTrigger.ToString(), DateTime.Now.ToString("yyyyMMdd hh: mm:ss"));
                    line = line.Replace(mToken.longTrigger.ToString(), newTrigger.ToString());
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

private void modifyOrderInCSV(OType type, Status status)
{
    List<String> lines = new List<String>();
    using (StreamReader reader = new StreamReader(ConfigurationManager.AppSettings["inputFile"]))
    {
        String line;
        while ((line = reader.ReadLine()) != null)
        {
            if (line.Contains(mToken.futId.ToString()))
            {
                string[] cells = line.Split(',');
                switch (status)
                {
                    default:
                    case Status.STANDING:
                        mToken.status = Status.STANDING;
                        cells[8] = "STANDING";
                        break;
                    case Status.POSITION:
                        mToken.status = Status.POSITION;
                        cells[8] = "POSITION";
                        break;
                    case Status.CLOSE:
                        mToken.status = Status.CLOSE;
                        cells[8] = "CLOSE";
                        break;
                }
                line = "";
                Console.WriteLine("Modify the Order for ticker :: {0} to status {1} for the day at {2} ", mToken.futName, cells[8], DateTime.Now.ToString());
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

bool CalculateBB(decimal ltp)
{
    decimal topBB = 0;
    decimal botBB = 0;
    decimal middle = 0;
    int counter = 20;
    int index = 0;
    DateTime previousDay;
    decimal timenow = (decimal)DateTime.Now.Hour + ((decimal)DateTime.Now.Minute / 100);
    if (Decimal.Compare(timenow, (decimal)(10.15)) < 0)
    {
        previousDay = DateTime.Now.Date.AddDays(-1);
        if (previousDay.DayOfWeek == DayOfWeek.Sunday)
            previousDay = DateTime.Now.Date.AddDays(-3);
    }
    else
        previousDay = DateTime.Now.Date;
    DateTime currentDay = DateTime.Now.Date.AddDays(1);
    List<Historical> history = new List<Historical>();
    try
    {
        history = kite.GetHistoricalData(mToken.futId.ToString(),
                        previousDay, currentDay, "3minute");
    }
    catch (System.TimeoutException)
    {
        history = kite.GetHistoricalData(mToken.futId.ToString(),
                        previousDay, currentDay, "3minute");
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
    decimal range = Convert.ToDecimal(((ltp * (decimal)0.9) / 100).ToString("#.#"));
    if ((topBB - botBB) > range)
    {
        range = Convert.ToDecimal(((ltp * (decimal)1.4) / 100).ToString("#.#"));
        if ((topBB - botBB) > range)
            mToken.target = 4300;
        Console.WriteLine("Time Stamp {0} : Recommended to Place ORDER as TopBB {1} BotBB {2} and thier Difference is {3} And LTP's range at {4} for Script {5}", DateTime.Now.ToString("yyyyMMdd hh: mm:ss"), topBB, botBB, topBB - botBB, range, mToken.futName);
        return true;
    }
    else
    {
        range = Convert.ToDecimal(((ltp * (decimal).6) / 100).ToString("#.#"));
        if ((topBB - botBB) < range)
        {
            if (mToken.type == OType.Sell)
            {
                Console.WriteLine("Time Stamp {0} : Recommending to Place ORDER IN BUY direction as TopBB {1} BotBB {2} and thier Difference is {3} but LTP's range at {4} for Script {5}", DateTime.Now.ToString("yyyyMMdd hh:mm:ss"), topBB, botBB, topBB - botBB, range, mToken.futName);
                mToken.type = OType.Buy;
                mToken.target = 2300;
                return true;
            }
            else if (mToken.type == OType.Buy)
            {
                Console.WriteLine("Time Stamp {0} : Recommending to Place ORDER IN SELL direction as TopBB {1} BotBB {2} and thier Difference is {3} but LTP's range at {4} for Script {5}", DateTime.Now.ToString("yyyyMMdd hh:mm:ss"), topBB, botBB, topBB - botBB, range, mToken.futName);
                mToken.type = OType.Buy;
                mToken.target = 2300;
                return true;
            }
        }
        //else
        //    Console.WriteLine("Time Stamp {0} : Recommending not to Place ORDER as TopBB {1} BotBB {2} and thier Difference is {3} but LTP's range at {4} for Script {5}", DateTime.Now.ToString("yyyyMMdd hh:mm:ss"), topBB, botBB, topBB - botBB, range, mToken.futName);
    }
    return false;
}
    }

 * 
 * 
 * 
 * 
 *                 if (Decimal.Compare(timenow, (decimal)(9.27)) < 0)
                {
                    decimal square1 = (ltp.Open / 100) * (decimal)0.8;
                    if (wl.type == OType.Sell)
                    {
                        if ((ltp.Low - ltp.Close) > square1 && IsOpenLowHigh(ltp.Open, ltp.Low, (decimal).0005))
                        {
                            Console.WriteLine("Not Placing SELL Order as this Instrument is open-Low below almost by 1%. Converting to BUY order");
                            wl.type = OType.Buy;
                            //target = ((decimal)4300 / (decimal)mToken.lotSize);
                            //modifyOrderInCSV(OType.Sell, Status.CLOSE);
                            //return;
                        }
                    }
                    else if (wl.type == OType.Buy)
                    {
                        if ((ltp.Close - ltp.High) > square1 && IsOpenLowHigh(ltp.Open, ltp.High, (decimal).0005))
                        {
                            Console.WriteLine("Not Placing BUY Order as this Instrument is open-high above almost by 1%. Converting to SELL order");
                            wl.type = OType.Sell;
                            //target = ((decimal)4300 / (decimal)mToken.lotSize);
                            //modifyOrderInCSV(OType.Buy, Status.CLOSE);
                            //return;
                        }
                    }
                }
                else
                {
                        */

        /* VERIFY TO CANDLES at the END OF FIRST HOUR
         * 
         * if (Decimal.Compare(timenow, cutOnTime) >= 0 && Decimal.Compare(timenow, (decimal)10.07) <= 0)
                            {
                                DateTime previousDay = DateTime.Now.Date;
                                DateTime currentDay = DateTime.Now.Date.AddDays(1);

                                dicOhlc = kite.GetOHLC(new string[] { ConfigurationManager.AppSettings["NSENIFTY"].ToString() });
                                ohlc = new OHLC();
                                dicOhlc.TryGetValue(ConfigurationManager.AppSettings["NSENIFTY"].ToString().ToString(), out ohlc);

                                for (int counter = 0;counter < tokens.Count; counter++)
                                {
                                    WatchList n = tokens[instruments[counter]];
                                    List<Historical> history = kite.GetHistoricalData(n.instrumentId.ToString(),
                                        previousDay, currentDay, "30minute");
                                    if (history.Count > 1)
                                    {
                                        Historical h1;
                                        Historical h2;
                                        if (history.Count > 2)
                                        {
                                            h1 = history[1];
                                            h2 = history[2];
                                            Console.WriteLine("Got the More than Two candles. But comparing candles only with time stamps {0} & {1} ", h1.TimeStamp.ToString(), h2.TimeStamp.ToString());
                                        }
                                        else
                                        {
                                            h1 = history[0];
                                            h2 = history[1];
                                            Console.WriteLine("Only Two candles found with time stamps {0} & {1} ", h1.TimeStamp.ToString(), h2.TimeStamp.ToString());
                                        }
                                        bool isRevision = false;
                                        if (n.type == OType.Sell)
                                        {
                                            decimal average = ((decimal)h1.High + (decimal)(n.shortTrigger)) / 2;
                                            decimal percentage1 = (((decimal)n.shortTrigger - h1.High) / average) * 100;
                                            decimal percentage2 = (((decimal)n.shortTrigger - h2.High) / average) * 100;

                                            if ((Decimal.Compare((decimal)percentage1, (decimal)(0.3)) <= 0 || percentage1 < 0) && (Decimal.Compare((decimal)percentage2, (decimal)(0.3)) <= 0 || percentage2 < 0))
                                            {
                                                //if ((ohlc.LastPrice - ohlc.Close) >= 30)
                                                Console.WriteLine("Close Short Trigger & Go Long as the NIFTY is completely in Bullish section. This is for " + n.futName);
                                                modifyOrderInCSV(n, Status.CLOSE);
                                                n.status = Status.CLOSE;
                                                continue;
                                            }
                                            string addMore = ((decimal)(((decimal)n.shortTrigger * (decimal).8) / 100)).ToString("#.#");
                                            if (h1.Low <= (decimal)n.longTrigger && h1.Low > 0)
                                            {
                                                isRevision = true;
                                                if (Decimal.Compare((decimal)percentage1, (decimal)(0.3)) <= 0 || percentage1 < 0 || Decimal.Compare((decimal)percentage2, (decimal)(0.3)) <= 0 || percentage2 < 0)
                                                    addMore = ((n.shortTrigger * (decimal)2) / 100).ToString("#.#");
                                                else
                                                    addMore = ((n.shortTrigger * (decimal).8) / 100).ToString("#.#");
                                                Console.WriteLine("NEW Short Trigger Value for {0} : is {1} as first candle low is below Bottom BB {2}", n.futName, average, h1.Low);
                                            }
                                            else if (h1.Open == h1.Low && h1.Open > 0 && h1.Open != h1.Close)
                                            {
                                                isRevision = true;
                                                addMore = ((decimal)(((decimal)n.shortTrigger * (decimal)1.2) / 100)).ToString("#.#");
                                                Console.WriteLine("LOOK OUT for Open = LOW, & NEW Short Trigger Value for {0} : is {1} as first candle OPEN = Low at {2}", n.futName, average, h1.Low);
                                            }
                                            else if (Decimal.Compare((decimal)percentage2, (decimal)(0.2)) <= 0 || percentage1 <= 0 || Decimal.Compare((decimal)percentage2, (decimal)(0.3)) <= 0 || percentage2 < 0)
                                            {
                                                isRevision = true;
                                                addMore = (n.shortTrigger / 100).ToString("#.#");
                                                Console.WriteLine("Stock is moving higher. So going for short with NEW Short Trigger Value for " + n.futName + " : is " + average);
                                            }

                                            if (isRevision)
                                            {
                                                average = (decimal)(n.shortTrigger) + Convert.ToDecimal(addMore); // + Convert.ToDecimal(addMore)/2;
                                                modifyOrderInCSV(n, average);
                                                n.shortTrigger = average;
                                            }
                                        }
                                        else if (n.type == OType.Buy)
                                        {
                                            decimal average = ((decimal)h1.Low + (decimal)(n.longTrigger)) / 2;
                                            decimal percentage1 = (((decimal)n.longTrigger - h1.Low) / average) * 100;
                                            decimal percentage2 = (((decimal)n.longTrigger - h2.Low) / average) * 100;

                                            if ((Decimal.Compare((decimal)percentage1, (decimal)(0.4)) <= 0 || percentage1 < 0 ) && (Decimal.Compare((decimal)percentage2, (decimal)(0.4)) <= 0 || percentage2 < 0))
                                            {
                                                //if ((ohlc.Close - ohlc.LastPrice) >= 30)
                                                Console.WriteLine("Close LONG Trigger as the NIFTY is completely in Bearish section. This is for " + n.futName);
                                                modifyOrderInCSV(n, Status.CLOSE);
                                                n.status = Status.CLOSE;
                                                continue;
                                            }
                                            string addMore = (n.longTrigger / 100).ToString("#.#");
                                            if (h1.High >= (decimal)n.shortTrigger && h1.High > 0)
                                            {
                                                isRevision = true;
                                                if (Decimal.Compare((decimal)percentage1, (decimal)(0.4)) <= 0 || (Decimal.Compare((decimal)percentage2, (decimal)(0.4)) <= 0) || percentage1 < 0 || percentage2 < 0)
                                                    addMore = ((n.longTrigger * (decimal)2) / 100).ToString("#.#");
                                                Console.WriteLine("NEW Long Trigger Value for {0} : is {1} as first candle low is above Top BB {2}", n.futName, average, h1.High);
                                            }
                                            else if (h1.Open == h1.High && h1.Open > 0)
                                            {
                                                isRevision = true;
                                                Console.WriteLine("LOOK OUT for Open = HIGH, & NEW Long Trigger Value for {0} : is {1} as first candle OPEN is OPEN HIGH {2}", n.futName, average, h1.High);
                                            }
                                            else if (Decimal.Compare((decimal)percentage1, (decimal)(0.4)) <= 0 || (Decimal.Compare((decimal)percentage2, (decimal)(0.4)) <= 0) || percentage1 < 0 || percentage2 < 0)
                                            {
                                                isRevision = true;
                                                Console.WriteLine("Stock is moving Lower. So going for Long with NEW Long Trigger Value for {0} : is {1} as first candle low is above Top BB {2}", n.futName, average, h1.High);
                                            }

                                            if (isRevision)
                                            {   
                                                average = (decimal)(n.longTrigger) - Convert.ToDecimal(addMore); // + Convert.ToDecimal(addMore)/2;
                                                modifyOrderInCSV(n, average);
                                                n.longTrigger = average;
                                            }
                                        }
                                        lastWrite = File.GetLastWriteTime(ConfigurationManager.AppSettings["inputFile"]);
                                    }
                                    else
                                        Console.WriteLine("NO CANDLES WERE RETURNED FOR GIVEN INSTRUMENT :::" + n.futName);
                                }
                            }

        * 
        * if (IsBetweenVariance(wl.middleBB, wl.ma50, (decimal).0005))
                        {
                            Quote quoteFut, quoteSpot;
                            Dictionary<string, Quote> dicLtp = kite.GetQuote(new string[] { wl.futId.ToString(), wl.instrumentId.ToString() });
                            dicLtp.TryGetValue(wl.futId.ToString(), out quoteFut);
                            dicLtp.TryGetValue(wl.instrumentId.ToString(), out quoteSpot);
                            if (!IsBeyondVariance(quoteSpot.BuyQuantity, quoteSpot.SellQuantity, (decimal).1))
                            {
                                if (wl.stochistic < 25 && IsBetweenVariance(quoteSpot.Open, quoteSpot.Low, (decimal).001) && IsBetweenVariance(quoteSpot.LastPrice, quoteSpot.Low, (decimal).004))
                                {
                                    Console.WriteLine("Time Stamp {0} : CrossOver; Recommended to Place BUY at {1} as 50 Moving average and 20 Moving average are crossed over, 20 MA = {2}; 50 MA = {3}; for Script {4}", DateTime.Now.ToString("yyyyMMdd hh:mm:ss"), ltp, wl.middleBB, wl.ma50, wl.futName);
                                    //instruments[token].type = OType.Buy;
                                    //instruments[token].target = 2300;
                                    //return true;
                                }
                                else if (wl.stochistic > 75 && IsBetweenVariance(quoteSpot.Open, quoteSpot.High, (decimal).001) && IsBetweenVariance(quoteSpot.LastPrice, quoteSpot.High, (decimal).004))
                                {
                                    Console.WriteLine("Time Stamp {0} : CrossOver; Recommended to Place SELL at {1} as 50 Moving average and 20 Moving average are crossed over, 20 MA = {2}; 50 MA = {3}; for Script {4}", DateTime.Now.ToString("yyyyMMdd hh:mm:ss"), ltp, wl.middleBB, wl.ma50, wl.futName);
                                    //instruments[token].type = OType.Sell;
                                    //instruments[token].target = 2300;
                                    //return true;
                                }
                                else if (!IsBeyondVariance(quoteSpot.BuyQuantity, quoteSpot.SellQuantity, (decimal).4))
                                {
                                    if(quoteSpot.BuyQuantity > quoteSpot.SellQuantity && IsBetweenVariance(quoteSpot.Open, quoteSpot.Low, (decimal).001))
                                        Console.WriteLine("Time Stamp {0} : WATCHOUT && CrossOver; Recommended to Place BUY at {1} as 50 Moving average and 20 Moving average are crossed over, 20 MA = {2}; 50 MA = {3}; for Script {4}", DateTime.Now.ToString("yyyyMMdd hh:mm:ss"), ltp, wl.middleBB, wl.ma50, wl.futName);
                                    else if (IsBetweenVariance(quoteSpot.Open, quoteSpot.High, (decimal).001))
                                        Console.WriteLine("Time Stamp {0} : WATCHOUT && CrossOver; Recommended to Place SELL at {1} as 50 Moving average and 20 Moving average are crossed over, 20 MA = {2}; 50 MA = {3}; for Script {4}", DateTime.Now.ToString("yyyyMMdd hh:mm:ss"), ltp, wl.middleBB, wl.ma50, wl.futName);
                                    //instruments[token].type = OType.Buy;
                                    //instruments[token].target = 2300;
                                    //return true;
                                }
                            }
                        }
                        else
                        {

        *          * 
         * 
         * 
         *             decimal cutoff = Convert.ToDecimal(ConfigurationManager.AppSettings["CutoffTime"]);
            decimal cutOn = Convert.ToDecimal(ConfigurationManager.AppSettings["CutOnTime"]);


        if (Decimal.Compare(timenow, (decimal)(10.14)) < 0)
                        {
                            return false;
                            if (!instruments[instrument].isVolatile
                                && instruments[instrument].type != trend
                                && trend != OType.BS)
                            {
                                //if ((wl.topBB - wl.botBB) > range)
                                //    flag = true;
                                if (IsBetweenVariance(ltp, tickData.High, (decimal).0007) 
                                    && wl.type == OType.Sell
                                    && ltp < wl.weekMA)
                                    flag = true;
                                else if (IsBetweenVariance(ltp, tickData.Low, (decimal).0007) 
                                    && wl.type == OType.Buy
                                    && ltp > wl.weekMA)
                                    flag = true;
                                else
                                {
                                    if ((IsBetweenVariance(tickData.Open, tickData.Low, (decimal).0005) || ltp > wl.weekMA)
                                        && wl.type == OType.Sell)
                                    {
                                        Console.WriteLine("Time Stamp {0} : Recommended to Place BUY ORDER as OPEN = LOW; Place order at WEEKMA {1} for Script {2} and the LTP is {3}", DateTime.Now.ToString("yyyyMMdd hh:mm:ss"), wl.weekMA, wl.futName, ltp);
                                        //instruments[instrument].type = OType.Buy;
                                    }
                                    else if ((IsBetweenVariance(tickData.Open, tickData.High, (decimal).0005) || ltp < wl.weekMA)
                                        && wl.type == OType.Buy)
                                    {
                                        Console.WriteLine("Time Stamp {0} : Recommended to Place SELL ORDER as OPEN = LOW; Place order at WEEKMA {1} for Script {2} and the LTP is {3}", DateTime.Now.ToString("yyyyMMdd hh:mm:ss"), wl.weekMA, wl.futName, ltp);
                                        //instruments[instrument].type = OType.Sell;
                                    }
                                    else
                                    {
                                        //Console.WriteLine("Time Stamp {0} : NOT Recommending to Place REVERSE ORDER at WEEKMA {1} for Script {2} and the LTP is {3}", DateTime.Now.ToString("yyyyMMdd hh:mm:ss"), wl.weekMA, wl.futName, ltp);
                                    }
                                }
                            }
                        }
                        else 


                        if (instruments[tickData.InstrumentToken].futName.ToLower().Contains("crude") && Decimal.Compare(timenow, (decimal)(11.30)) > 0 && Decimal.Compare(timenow, (decimal)(23.05)) < 0)
                {
                    if (CalculateSqueez(tickData.InstrumentToken, tickData))
                    {
                        //placeOrder(wl, tickData.LastPrice - tickData.Close);
                    }
                }
                else 

        * 
        * 
        * 
        * range = Convert.ToDecimal(((ltp * (decimal).52) / 100).ToString("#.#"));
                            decimal len1 = history[history.Count - 1].High - history[history.Count - 1].Low;
                            decimal len2 = history[history.Count - 2].High - history[history.Count - 2].Low;
                            if (IsBetweenVariance(len1, len2, (decimal).0009)
                                && ((history[history.Count - 2].High == history[history.Count - 1].High ||
                                    history[history.Count - 2].Low == history[history.Count - 1].Low) ||
                                    (len1 == len2
                                        && IsBetweenVariance(history[history.Count - 2].High, history[history.Count - 1].High, (decimal).0006))))
                                //|| range < len1))
                                return false;

        #region before 9.18
                if (Decimal.Compare(timenow, (decimal)(9.18)) < 0 && Decimal.Compare(timenow, (decimal)(9.15)) > 0)
                {
                    if ((tickData.Close - tickData.Open) >= r3
                        && tickData.Open < instruments[tickData.InstrumentToken].weekMA
                        && wl.type == OType.Buy)
                    {
                        Console.WriteLine("Script {0} Lookup for Reverse Order from BUY to SELL - 1", wl.futName);
                        //instruments[tickData.InstrumentToken].type = OType.Sell;
                        //return true;
                    }
                    else if (tickData.Open > instruments[tickData.InstrumentToken].top30bb
                        && IsBetweenVariance(tickData.Open, tickData.High, (decimal).001)
                        && IsBetweenVariance(tickData.LastPrice, tickData.High, (decimal).004)
                        && wl.type == OType.Buy)
                    {
                        Console.WriteLine("Script {0} Lookup for Reverse Order from BUY to SELL - 2", wl.futName);
                        //instruments[tickData.InstrumentToken].type = OType.Sell;
                        //return true;
                    }
                    else if (IsBetweenVariance(tickData.Open, instruments[tickData.InstrumentToken].top30bb, (decimal).001)
                        && IsBetweenVariance(tickData.Open, tickData.High, (decimal).0006)
                        && IsBetweenVariance(tickData.LastPrice, tickData.High, (decimal).002)
                        && wl.type == OType.Buy)
                    {
                        Console.WriteLine("Script {0} Lookup for Reverse Order from BUY to SELL - 3", wl.futName);
                        //instruments[tickData.InstrumentToken].type = OType.Sell;
                        //return true;
                    }
                    if ((tickData.Open - tickData.Close) >= r3
                        && tickData.Open > instruments[tickData.InstrumentToken].weekMA
                        && wl.type == OType.Sell)
                    {
                        Console.WriteLine("Script {0} Lookup for Reverse Order from SELL to BUY - 1", wl.futName);
                        //instruments[tickData.InstrumentToken].type = OType.Buy;
                        //return true;
                    }
                    else if (tickData.Open < instruments[tickData.InstrumentToken].bot30bb
                        && IsBetweenVariance(tickData.Open, tickData.Low, (decimal).001)
                        && IsBetweenVariance(tickData.LastPrice, tickData.Low, (decimal).004)
                        && wl.type == OType.Sell)
                    {
                        Console.WriteLine("Script {0} Lookup for Reverse Order from SELL to BUY - 2", wl.futName);
                        //instruments[tickData.InstrumentToken].type = OType.Buy;
                        //return true;
                    }
                    else if (IsBetweenVariance(tickData.Open, instruments[tickData.InstrumentToken].bot30bb, (decimal).001)
                        && IsBetweenVariance(tickData.Open, tickData.Low, (decimal).0006)
                        && IsBetweenVariance(tickData.LastPrice, tickData.Low, (decimal).002)
                        && wl.type == OType.Sell)
                    {
                        Console.WriteLine("Script {0} Lookup for Reverse Order from SELL to BUY - 3", wl.futName);
                        //instruments[tickData.InstrumentToken].type = OType.Buy;
                        //return true;
                    }
                    return false;
                }
                #endregion                                     * 

        * 
        * 
        * RESET IN VERIFY LTP BEFORE (DID IT REVERSE)
        * 
        if (instruments[tickData.InstrumentToken].type == OType.Buy)
                            {
                                if ((history[history.Count - 1].Open > instruments[tickData.InstrumentToken].middle30BBnew
                                        || history[history.Count - 1].Open > instruments[tickData.InstrumentToken].middle30BB)
                                    && (history[history.Count - 2].Open > instruments[tickData.InstrumentToken].middle30BBnew
                                        || history[history.Count - 2].Open > instruments[tickData.InstrumentToken].middle30BB))
                                {
                                    //instruments[tickData.InstrumentToken].status = Status.CLOSE;
                                    instruments[tickData.InstrumentToken].type = OType.Sell;
                                    instruments[tickData.InstrumentToken].longTrigger = instruments[tickData.InstrumentToken].bot30bb;
                                    instruments[tickData.InstrumentToken].shortTrigger = instruments[tickData.InstrumentToken].weekMA;
                                    instruments[tickData.InstrumentToken].isReversed = false;
                                    return false;
                                }
                            }
                            else if (instruments[tickData.InstrumentToken].type == OType.Sell)
                            {
                                if ((history[history.Count - 1].Open < instruments[tickData.InstrumentToken].middle30BBnew
                                        || history[history.Count - 1].Open < instruments[tickData.InstrumentToken].middle30BB)
                                    && (history[history.Count - 2].Open < instruments[tickData.InstrumentToken].middle30BBnew
                                        || history[history.Count - 2].Open < instruments[tickData.InstrumentToken].middle30BB))
                                {
                                    //instruments[tickData.InstrumentToken].status = Status.CLOSE;
                                    instruments[tickData.InstrumentToken].type = OType.Buy;
                                    instruments[tickData.InstrumentToken].longTrigger = instruments[tickData.InstrumentToken].weekMA;
                                    instruments[tickData.InstrumentToken].shortTrigger = instruments[tickData.InstrumentToken].top30bb;
                                    instruments[tickData.InstrumentToken].isReversed = false;
                                    return false;
                                }
                            }*          



        IF THE REVERSED ORDER COMING BACK FOR NON-QUALIFIED 

        switch (wl.type)
                        {
                            case OType.Sell:
                                if (IsBetweenVariance(instruments[tickData.InstrumentToken].weekMA, instruments[tickData.InstrumentToken].bot30bb, (decimal).003)
                                    && wl.shortTrigger == instruments[tickData.InstrumentToken].weekMA)
                                {
                                    //do nothing
                                }
                                else
                                {
                                    qualified = (r1 >= wl.shortTrigger && IsBetweenVariance(history[index].Close, wl.shortTrigger, (decimal).0012));
                                }
                                break;
                            case OType.Buy:
                                if (IsBetweenVariance(instruments[tickData.InstrumentToken].weekMA, instruments[tickData.InstrumentToken].top30bb, (decimal).003)
                                    && wl.longTrigger == instruments[tickData.InstrumentToken].weekMA)
                                {
                                    //do nothing
                                }
                                else
                                {
                                    qualified = (r2 <= wl.longTrigger && IsBetweenVariance(history[index].Close, wl.longTrigger, (decimal).0012));
                                }
                                break;
                            default:
                                break;
                        }
                        if (qualified)
                            return qualified;


        * 
        * 
        * 
        * 
        * if (Decimal.Compare(timenow, startTime) < 0 || Decimal.Compare(timenow, stopTime) > 0) //|| lastWrite != File.GetLastWriteTime(ConfigurationManager.AppSettings["inputFile"])
                        {
                            Thread.Sleep(10000);
                            bt.ticker.Close();
                            bt.ticker.UnSubscribe(instruments.ToArray());
                            flag = false;
                            Thread.Sleep(10000);
                            //lastWrite = File.GetLastWriteTime(ConfigurationManager.AppSettings["inputFile"]);
                            Console.WriteLine("File is Modified and hence the Ticker Connection should return exception at the Time " + lastWrite.ToString());
                        }

        * 
        * 
        * 
        * 
        *                if(!qualified && instruments[tickData.InstrumentToken].isReversed)
                {
                    try
                    {
                        List<Historical> history;
                        try
                        {
                            history = kite.GetHistoricalData(tickData.InstrumentToken.ToString(),
                                    DateTime.Now.Date.AddHours(9).AddMinutes(15),
                                    //DateTime.Now.Date.AddHours(10).AddMinutes(14), "30minute");
                                    DateTime.Now.Date.AddDays(1), "30minute");
                        }
                        catch
                        {
                            history = kite.GetHistoricalData(tickData.InstrumentToken.ToString(),
                                    DateTime.Now.Date.AddHours(9).AddMinutes(15), 
                                    DateTime.Now.Date.AddDays(1), "30minute");
                        }
                        int index = history.Count - 1;
                        if (history.Count > 1)
                        {
                            decimal variance22 = (ltp * (decimal)2.2) / 100;
                            if (instruments[tickData.InstrumentToken].bot30bb + variance22 > instruments[tickData.InstrumentToken].top30bb)
                            {
                                if (IsBetweenVariance(ltp, instruments[tickData.InstrumentToken].middle30BB, (decimal).0006)
                                    && instruments[tickData.InstrumentToken].oldTime != instruments[tickData.InstrumentToken].currentTime)
                                {
                                    if (instruments[tickData.InstrumentToken].type == OType.Buy && ltp > instruments[tickData.InstrumentToken].middle30BB)
                                    {
                                        Console.WriteLine("Time Stamp {0} : For Script {1} Did it reversed until middle30 for BUY???", DateTime.Now.ToString("yyyyMMdd hh:mm:ss"), wl.futName);
                                    }
                                    else if (instruments[tickData.InstrumentToken].type == OType.Sell && ltp > instruments[tickData.InstrumentToken].middle30BB)
                                    {
                                        Console.WriteLine("Time Stamp {0} : For Script {1} Did it reversed until middle30 for SELL???", DateTime.Now.ToString("yyyyMMdd hh:mm:ss"), wl.futName);
                                    }
                                }
                            }
                        }
                    }
                    catch(Exception ex)
                    {
                        Console.WriteLine("EXCEPTION :: Time Stamp {0} : For Script {1}; {2}", DateTime.Now.ToString("yyyyMMdd hh:mm:ss"), wl.futName, ex.Message);
                    }
                }




        if (history[history.Count - 2].Open < instruments[tickData.InstrumentToken].middle30BB
                                            && history[history.Count - 2].Open < instruments[tickData.InstrumentToken].middle30BBnew
                                            && history[history.Count - 2].Close > instruments[tickData.InstrumentToken].middle30BB
                                            && history[history.Count - 2].Close > instruments[tickData.InstrumentToken].middle30BBnew
                                            && (instruments[tickData.InstrumentToken].type == OType.Sell && instruments[tickData.InstrumentToken].isReversed))
                                        {
                                            Console.WriteLine("Time Stamp {0} -VE TREND - Averting Candle: First Candle Breakout below Middle30BB {1} for SELL ORDER for Script {2} and the LTP is {3}", DateTime.Now.ToString("yyyyMMdd hh:mm:ss"), instruments[instrument].middle30BB, instruments[instrument].futName, ltp);
                                            instruments[tickData.InstrumentToken].isReversed = false;
                                            instruments[tickData.InstrumentToken].isOpenAlign = false;
                                            modifyOpenAlignOrReversedStatus(instruments[tickData.InstrumentToken], 16, OType.Sell, false);
                                        }
                                        else if (history[history.Count - 2].Open > instruments[tickData.InstrumentToken].middle30BB
                                            && history[history.Count - 2].Open > instruments[tickData.InstrumentToken].middle30BBnew
                                            && history[history.Count - 2].Close < instruments[tickData.InstrumentToken].middle30BB
                                            && history[history.Count - 2].Close < instruments[tickData.InstrumentToken].middle30BBnew
                                            && (instruments[tickData.InstrumentToken].type == OType.Buy && instruments[tickData.InstrumentToken].isReversed))
                                        {
                                            Console.WriteLine("Time Stamp {0} -VE TREND - Averting Candle: First Candle Breakout above Middle30BB {1} for Buy ORDER for Script {2} and the LTP is {3}", DateTime.Now.ToString("yyyyMMdd hh:mm:ss"), instruments[instrument].middle30BB, instruments[instrument].futName, ltp);
                                            instruments[tickData.InstrumentToken].isReversed = false;
                                            instruments[tickData.InstrumentToken].isOpenAlign = false;
                                            modifyOpenAlignOrReversedStatus(instruments[tickData.InstrumentToken], 16, OType.Sell, false);
                                        }
        *          */

        /*
        #region More than One Candle and Above / WeekMA
        //Console.WriteLine("Time Stamp {0} : For Script {1} Start Checking for Reverse", DateTime.Now.ToString("yyyyMMdd hh:mm:ss"), wl.futName);
        int index = history.Count - 1;
                            if (history[index].Close > instruments[instrument].weekMA
                                && history[index - 1].Close<instruments[instrument].weekMA
                                && history[index].Close<instruments[instrument].top30bb
                                && (instruments[instrument].top30bb - history[index].Close) > minRange
                                && ((instruments[instrument].type == OType.Sell && !instruments[instrument].isReversed)
                                    || (instruments[instrument].type == OType.Buy && instruments[instrument].isReversed))
                                //history[index].Close > instruments[instrument].middle30BBnew &&
                                //&& history[index].Close > instruments[instrument].middle30BB
                                //&& instruments[instrument].weekMA < instruments[instrument].middle30BB
                                //&& IsBeyondVariance(instruments[instrument].weekMA, instruments[instrument].middle30BB, (decimal).006)
                                )
                            {
                                #region buy call
                                if (VerifyNifty(timenow) != OType.Buy && (history[index].High - history[index].Open) < minRange)
                                {
                                    Console.WriteLine("Time Stamp {0}  +VE TREND: But JUST Place Sell ORDER at WEEKMA {1} for Script {2} and the LTP is {3}", DateTime.Now.ToString("yyyyMMdd hh:mm:ss"), instruments[instrument].weekMA, instruments[instrument].futName, ltp);
                                    //return true;
                                }
                                if (IsBetweenVariance(instruments[instrument].weekMA, instruments[instrument].top30bb, (decimal).003))
                                {
                                    Console.WriteLine("Time Stamp {0}  -VE TREND: YOU COULD HAVE AVOIDED THIS ORDER; Recommending but return false for Sell ORDER at WEEKMA {1} for Script {2} and the LTP is {3}", DateTime.Now.ToString("yyyyMMdd hh:mm:ss"), instruments[instrument].weekMA, instruments[instrument].futName, ltp);
                                    return false;
                                }
                                if (history[index].Close > instruments[instrument].weekMA
                                    && history[index].Close > instruments[instrument].middle30BB
                                    && IsBeyondVariance(history[index].Close, instruments[instrument].weekMA, (decimal).0006)
                                    && IsBetweenVariance(history[index].Close, instruments[instrument].weekMA, (decimal).0012)
                                    && IsBetweenVariance(instruments[instrument].weekMA, instruments[instrument].middle30BB, (decimal).002))
                                {
                                    Console.WriteLine("Time Stamp {0}  +VE TREND: Recommending to Place BUY ORDER at WEEKMA {1} for Script {2} and the LTP is {3}", DateTime.Now.ToString("yyyyMMdd hh:mm:ss"), instruments[instrument].weekMA, instruments[instrument].futName, ltp);
                                    instruments[instrument].type = OType.Buy;
                                    instruments[instrument].isReversed = true;
                                    modifyOpenAlignOrReversedStatus(instruments[instrument], 17, OType.Buy, true);
                                    //return true;
                                }
                                else
                                {
                                    if (history[index].Close > instruments[instrument].weekMA
                                           && history[index].Close<instruments[instrument].middle30BB
                                           //&& history[index].Close < instruments[instrument].middle30BBnew
                                           && instruments[instrument].weekMA<instruments[instrument].middle30BBnew
                                           && history[index].High >= instruments[instrument].middle30BBnew
                                           && IsBetweenVariance(instruments[instrument].weekMA, instruments[instrument].middle30BBnew, (decimal).006)
                                           && IsBeyondVariance(instruments[instrument].weekMA, instruments[instrument].middle30BBnew, (decimal).002))
                                    {
                                        Console.WriteLine("Time Stamp {0}  +VE TREND: But JUST Place SELL ORDER at WEEKMA {1} for Script {2} and the LTP is {3}", DateTime.Now.ToString("yyyyMMdd hh:mm:ss"), instruments[instrument].weekMA, instruments[instrument].futName, ltp);
                                        instruments[instrument].type = OType.Sell;
                                        instruments[instrument].isReversed = false;
                                        instruments[instrument].shortTrigger = instruments[instrument].middle30BBnew;
                                        modifyOpenAlignOrReversedStatus(instruments[instrument], 17, OType.Sell, false);
                                        return true;
                                    }
                                    else
                                    {
                                        Console.WriteLine("Time Stamp {0}  +VE TREND: Just Reversing to BUY ORDER at WEEKMA {1} for Script {2} and the LTP is {3}", DateTime.Now.ToString("yyyyMMdd hh:mm:ss"), instruments[instrument].weekMA, instruments[instrument].futName, ltp);
                                        instruments[instrument].type = OType.Buy;
                                        if (IsBeyondVariance(instruments[instrument].weekMA, instruments[instrument].middle30BB, (decimal).002)
                                            && instruments[instrument].middle30BBnew<instruments[instrument].weekMA)
                                        {
                                            instruments[instrument].longTrigger = instruments[instrument].middle30BBnew;
                                        }
                                        else
                                            instruments[instrument].longTrigger = instruments[instrument].weekMA;
                                        instruments[instrument].shortTrigger = instruments[instrument].top30bb;
                                        instruments[instrument].isReversed = true;
                                        modifyOpenAlignOrReversedStatus(instruments[instrument], 17, OType.Buy, true);
                                    }
                                }
                                #endregion
                            }
                            else if (history[index].Close<instruments[instrument].weekMA
                                    && history[index - 1].Close> instruments[instrument].weekMA
                                    && history[index].Close > instruments[instrument].bot30bb
                                    && (history[index].Close - instruments[instrument].bot30bb) > minRange
                                    && ((instruments[instrument].type == OType.Buy && !instruments[instrument].isReversed)
                                        || (instruments[instrument].type == OType.Sell && instruments[instrument].isReversed))
                                    //history[index].Close < instruments[instrument].middle30BBnew &&
                                    //&& history[index].Close < instruments[instrument].middle30BB
                                    //&& instruments[instrument].weekMA > instruments[instrument].middle30BB
                                    //&& IsBeyondVariance(instruments[instrument].weekMA, instruments[instrument].middle30BB, (decimal).006)
                                    )
                            {
                                #region Sell Call
                                if (VerifyNifty(timenow) != OType.Sell && (history[index].Close - history[index].Low) < minRange)
                                {
                                    Console.WriteLine("Time Stamp {0}  +VE TREND: But JUST Place BUY ORDER at WEEKMA {1} for Script {2} and the LTP is {3}", DateTime.Now.ToString("yyyyMMdd hh:mm:ss"), instruments[instrument].weekMA, instruments[instrument].futName, ltp);
                                    //return true;
                                }
                                if (IsBetweenVariance(instruments[instrument].weekMA, instruments[instrument].middle30BB, (decimal).003))
                                {
                                    Console.WriteLine("Time Stamp {0}  -VE TREND: YOU COULD HAVE AVOIDED THIS ORDER Recommending but return false for Buy ORDER at WEEKMA {1} for Script {2} and the LTP is {3}", DateTime.Now.ToString("yyyyMMdd hh:mm:ss"), instruments[instrument].weekMA, instruments[instrument].futName, ltp);
                                    //return false;
                                }
                                if (history[index].Close<instruments[instrument].weekMA
                                    && history[index].Close<instruments[instrument].middle30BB
                                    && IsBeyondVariance(history[index].Close, instruments[instrument].weekMA, (decimal).0006)
                                    && IsBetweenVariance(history[index].Close, instruments[instrument].weekMA, (decimal).0012))
                                {
                                    Console.WriteLine("Time Stamp {0} -VE TREND: Recommending to Place SELL ORDER at WEEKMA {1} for Script {2} and the LTP is {3}", DateTime.Now.ToString("yyyyMMdd hh:mm:ss"), instruments[instrument].weekMA, instruments[instrument].futName, ltp);
                                    instruments[instrument].isReversed = true;
                                    instruments[instrument].type = OType.Sell;
                                    modifyOpenAlignOrReversedStatus(instruments[instrument], 17, OType.Sell, true);
                                    //return true;
                                }
                                else
                                {
                                    if (history[index].Close<instruments[instrument].weekMA
                                           && history[index].Close> instruments[instrument].middle30BB
                                           //&& history[index].Close > instruments[instrument].middle30BBnew
                                           && instruments[instrument].weekMA > instruments[instrument].middle30BBnew
                                           && history[index].Low <= instruments[instrument].middle30BBnew
                                           && IsBetweenVariance(instruments[instrument].weekMA, instruments[instrument].middle30BBnew, (decimal).006)
                                           && IsBeyondVariance(instruments[instrument].weekMA, instruments[instrument].middle30BBnew, (decimal).002))
                                    {
                                        Console.WriteLine("Time Stamp {0}  +VE TREND: But JUST Place BUY ORDER at WEEKMA {1} for Script {2} and the LTP is {3}", DateTime.Now.ToString("yyyyMMdd hh:mm:ss"), instruments[instrument].weekMA, instruments[instrument].futName, ltp);
                                        instruments[instrument].type = OType.Buy;
                                        instruments[instrument].isReversed = false;
                                        instruments[instrument].longTrigger = instruments[instrument].middle30BBnew;
                                        modifyOpenAlignOrReversedStatus(instruments[instrument], 17, OType.Buy, false);
                                        return true;
                                    }
                                    else
                                    {
                                        Console.WriteLine("Time Stamp {0} -VE TREND: Just Reversing to SELL ORDER at WEEKMA {1} for Script {2} and the LTP is {3}", DateTime.Now.ToString("yyyyMMdd hh:mm:ss"), instruments[instrument].weekMA, instruments[instrument].futName, ltp);
                                        instruments[instrument].type = OType.Sell;
                                        instruments[instrument].longTrigger = instruments[instrument].bot30bb;
                                        if (IsBeyondVariance(instruments[instrument].weekMA, instruments[instrument].middle30BB, (decimal).002)
                                            && instruments[instrument].middle30BBnew > instruments[instrument].weekMA)
                                        {
                                            instruments[instrument].shortTrigger = instruments[instrument].middle30BBnew;
                                        }
                                        else
                                            instruments[instrument].shortTrigger = instruments[instrument].weekMA;
                                        instruments[instrument].isReversed = true;
                                        modifyOpenAlignOrReversedStatus(instruments[instrument], 17, OType.Sell, true);
                                    }
                                }
                                #endregion
                            }
        #endregion
        */


        /*
         * 
         * 
         *                                 if (history[0].Close > instruments[instrument].weekMA
                                    && history[0].Close > instruments[instrument].middle30BB
                                    && IsBetweenVariance(history[0].Close, instruments[instrument].weekMA, (decimal).0012)
                                    && IsBetweenVariance(instruments[instrument].weekMA, instruments[instrument].middle30BB, (decimal).002))
                                {
                                    Console.WriteLine("Time Stamp {0}  +VE TREND - First Candle: Continuing to Place BUY ORDER at WEEKMA {1} for Script {2} and the LTP is {3}", DateTime.Now.ToString("yyyyMMdd hh:mm:ss"), instruments[instrument].weekMA, instruments[instrument].futName, ltp);
                                    instruments[instrument].isReversed = true;
                                    instruments[instrument].type = OType.Buy;
                                    modifyOpenAlignOrReversedStatus(instruments[instrument], 17, OType.Buy, true);
                                    //return true;
                                }
                                else //if (history[0].Close - instruments[instrument].weekMA) > minRange)
                                {
                                    if(weekMa & middle30Bb are very narrow range)
                                        Console.WriteLine("Time Stamp {0} First Candle +VE TREND: But JUST Place SELL ORDER at WEEKMA {1} for Script {2} and the LTP is {3}", DateTime.Now.ToString("yyyyMMdd hh:mm:ss"), instruments[instrument].weekMA, instruments[instrument].futName, ltp);
                                    else
                                    {
                                        Console.WriteLine("Time Stamp {0}  +VE TREND - First Candle: Just Reversing to BUY ORDER at WEEKMA {1} for Script {2} and the LTP is {3}", DateTime.Now.ToString("yyyyMMdd hh:mm:ss"), instruments[instrument].weekMA, instruments[instrument].futName, ltp);
                                        instruments[instrument].type = OType.Buy;
                                        if (IsBeyondVariance(instruments[instrument].weekMA, instruments[instrument].middle30BB, (decimal).002)
                                            && instruments[instrument].middle30BBnew < instruments[instrument].weekMA)
                                        {
                                            instruments[instrument].longTrigger = instruments[instrument].middle30BBnew;
                                        }
                                        else
                                            instruments[instrument].longTrigger = instruments[instrument].weekMA;
                                        instruments[instrument].shortTrigger = instruments[instrument].top30bb;
                                        instruments[instrument].isReversed = true;
                                        modifyOpenAlignOrReversedStatus(instruments[instrument], 17, OType.Buy, true);
                                    }
                                }


        ///for BUY CALL

                                        if (history[0].Close < instruments[instrument].weekMA
                                    && history[0].Close < instruments[instrument].middle30BB
                                    && IsBetweenVariance(history[0].Close, instruments[instrument].weekMA, (decimal).0012)
                                    && IsBetweenVariance(instruments[instrument].weekMA, instruments[instrument].middle30BB, (decimal).002))
                                {
                                    Console.WriteLine("Time Stamp {0} -VE TREND - First Candle: Continuing to Place SELL ORDER at WEEKMA {1} for Script {2} and the LTP is {3}", DateTime.Now.ToString("yyyyMMdd hh:mm:ss"), instruments[instrument].weekMA, instruments[instrument].futName, ltp);
                                    instruments[instrument].isReversed = true;
                                    instruments[instrument].type = OType.Sell;
                                    modifyOpenAlignOrReversedStatus(instruments[instrument], 17, OType.Sell, true);
                                    //return true;
                                }
                                else //((instruments[instrument].weekMA - history[0].Close) > minRange)
                                {
                                    if(weekMa & middle30Bb are very narrow range)
                                        Console.WriteLine("Time Stamp {0} First Candle -VE TREND: But JUST Place BUY ORDER at WEEKMA {1} for Script {2} and the LTP is {3}", DateTime.Now.ToString("yyyyMMdd hh:mm:ss"), instruments[instrument].weekMA, instruments[instrument].futName, ltp);
                                    else
                                    {
                                        Console.WriteLine("Time Stamp {0} -VE TREND - First Candle: Just Reversing to SELL ORDER at WEEKMA {1} for Script {2} and the LTP is {3}", DateTime.Now.ToString("yyyyMMdd hh:mm:ss"), instruments[instrument].weekMA, instruments[instrument].futName, ltp);
                                        instruments[instrument].type = OType.Sell;
                                        instruments[instrument].longTrigger = instruments[instrument].bot30bb;
                                        if (IsBeyondVariance(instruments[instrument].weekMA, instruments[instrument].middle30BB, (decimal).002)
                                            && instruments[instrument].middle30BBnew > instruments[instrument].weekMA)
                                        {
                                            instruments[instrument].shortTrigger = instruments[instrument].middle30BBnew;
                                        }
                                        else
                                            instruments[instrument].shortTrigger = instruments[instrument].weekMA;
                                        instruments[instrument].isReversed = true;
                                        modifyOpenAlignOrReversedStatus(instruments[instrument], 17, OType.Sell, true);
                                    }
                                }





        ********* MORNING OR DAY TRADE TICKS
        * 
        *          while (Decimal.Compare(timenow, Convert.ToDecimal(9.16)) < 0)
                    {
                        Thread.Sleep(30000);
                        timenow = (decimal)DateTime.Now.Hour + ((decimal)DateTime.Now.Minute / 100);
                    }

                    if(bt.VerifyNifty(timenow) != OType.BS)
                    {
                        //if (Decimal.Compare(timenow, Convert.ToDecimal(9.20)) < 0)
                        {
                            //CloseAllOrders();
                            isVolatile = true;
                        }
                    }
                    else
                    {
                        tokens = ReadInputData("MORNING", isVolatile, out instruments);
                        if (tokens.Count > 0 && Decimal.Compare(timenow, cutOnTime) < 0)
                        {
                            Console.WriteLine("HERE Starting the MORNING TICKS for {0} number of scripts at time {1}", tokens.Count, DateTime.Now.ToString());
                            bt.InitiateWatchList(tokens);
                            bt.initTicker(instruments);
                        }
                        else
                            Console.WriteLine("No Tokens were found for Early Morning trade and hence Skipping to Day Trade at {0}", timenow);
                    }
                    while (Decimal.Compare(timenow, (decimal)9.34) <= 0)
                    {
                        Thread.Sleep(20000);
                        timenow = (decimal)DateTime.Now.Hour + ((decimal)DateTime.Now.Minute / 100);
                    }
        Console.WriteLine("Time is either below 9.28AM nor above 9.36AM :: " + DateTime.Now.ToString("yyyyMMdd hh:mm:ss"));
                    if (bt.ticker != null)
                    {
                        Thread.Sleep(10000);
                        bt.ticker.Close();
                        bt.ticker.UnSubscribe(instruments.ToArray());
                    }
                    else
                        Console.WriteLine("Ticker was not initialized before. Hence Stopping ticker does not make sense");

                    timenow = (decimal)DateTime.Now.Hour + ((decimal)DateTime.Now.Minute / 100);
                    tokens = ReadInputData("DAY", isVolatile, out instruments);
                    if (tokens.Count > 0 && Decimal.Compare(timenow, Convert.ToDecimal(cutoffTime)) < 0)
                    {
                        Console.WriteLine("HERE Starting the DAY TICKS for {0} number of scripts at time {1}", tokens.Count, DateTime.Now.ToString());
                        bt.InitiateWatchList(tokens);
                        bt.initTicker(instruments);
                    }
                    else
                        Console.WriteLine("Token count is empty for DAY trade as the time is {0}", DateTime.Now.ToString());

                    #region Start Day TRADE orders to TICK
                    if (bt.ticker != null)
                    {
                        Thread.Sleep(10000);
                        bt.ticker.Close();
                        bt.ticker.UnSubscribe(instruments.ToArray());
                    }
                    else
                        Console.WriteLine("Ticker was not initialized before. Hence Stopping ticker does not make sense");

                    timenow = (decimal)DateTime.Now.Hour + ((decimal)DateTime.Now.Minute / 100);
                    #endregion


         *
         *
         *if (((DateTime.Now.Minute >= 36 && DateTime.Now.Minute < 45)
                                || DateTime.Now.Minute >= 6 && DateTime.Now.Minute < 15)
                            && ((instruments[token].orderTime.Minute >= 36 && instruments[token].orderTime.Minute < 45)
                                || (instruments[token].orderTime.Minute >= 6 && instruments[token].orderTime.Minute < 15)))





                                                    if (Decimal.Compare(timenow, Convert.ToDecimal(10.20)) < 0)
                                            {
                                                if (obtainingLoss < 1000)
                                                {
                                                    instruments[token].target = (int)(obtainingLoss + (decimal)1800);
                                                }
                                                else if (obtainingLoss < 2000)
                                                {
                                                    instruments[token].target = 3300;
                                                }
                                                else
                                                    instruments[token].target = (int)(obtainingLoss + 600);
                                            }
                                            else
                                            {
                                                if (Decimal.Compare(timenow, (decimal)12.25) < 0
                                                    || (VerifyNifty(timenow) == OType.Sell
                                                        && Decimal.Compare(timenow, (decimal)13.59) < 0))
                                                {
                                                    instruments[token].target = 4000;
                                                }
                                                else
                                                    instruments[token].target = 2500;
                                            }



        
                                    if (Decimal.Compare(timenow, Convert.ToDecimal(10.20)) < 0)
                                    {
                                        if (obtainingLoss < 1000)
                                        {
                                            instruments[token].target = (int)(obtainingLoss + (decimal)1800);
                                        }
                                        else if (obtainingLoss < 2000)
                                        {
                                            instruments[token].target = 3300;
                                        }
                                        else
                                            instruments[token].target = (int)(obtainingLoss + 600);
                                    }
                                    else
                                    {
                                        if (Decimal.Compare(timenow, (decimal)12.25) < 0
                                            || (VerifyNifty(timenow) == OType.Buy
                                                && Decimal.Compare(timenow, (decimal)13.59) < 0))
                                        {
                                            instruments[token].target = 4000;
                                        }
                                        else
                                            instruments[token].target = 2500;
                                    }



                range = Convert.ToDecimal(((ltp * (decimal)2.1) / 100).ToString("#.#"));
                if ((instruments[instrument].top30bb - instruments[instrument].bot30bb) > range && index > 2)
                {
                    int buy = 0;
                    int sell = 0;
                    range = Convert.ToDecimal(((ltp * (decimal).7) / 100).ToString("#.#"));
                    decimal maxRange = Convert.ToDecimal(((ltp * (decimal)1.4) / 100).ToString("#.#"));
                    if (history[index].Open > history[index].Close
                        && (history[index].Open - history[index].Close) > range
                        && history[index].Low <= instruments[instrument].middle30BB
                        && (IsBetweenVariance(ltp, instruments[instrument].middle30BB, (decimal).0001)
                            || ltp > instruments[instrument].middle30BB)
                        && (instruments[instrument].topBB - instruments[instrument].botBB) < maxRange
                        && ((instruments[instrument].type == OType.Buy && !instruments[instrument].isReversed)
                            || (instruments[instrument].type == OType.Sell && instruments[instrument].isReversed))
                        && IsBeyondVariance(instruments[instrument].middle30BB, instruments[instrument].weekMA, (decimal).0065))
                    {
                        sell = sell + 1;
                    }
                    else if (history[index].Open < history[index].Close
                        && (history[index].Close - history[index].Open) > range
                        && history[index].High > instruments[instrument].middle30BB
                        && (IsBetweenVariance(ltp, instruments[instrument].middle30BB, (decimal).0001)
                            || ltp > instruments[instrument].middle30BB)
                        && (instruments[instrument].topBB - instruments[instrument].botBB) < maxRange
                        && ((instruments[instrument].type == OType.Sell && !instruments[instrument].isReversed)
                            || (instruments[instrument].type == OType.Buy && instruments[instrument].isReversed))
                        && IsBeyondVariance(instruments[instrument].middle30BB, instruments[instrument].weekMA, (decimal).0065))
                    {
                        buy = buy + 1;
                    }
                    range = Convert.ToDecimal(((ltp * (decimal).4) / 100).ToString("#.#"));
                    for (int i = index - 1; i > index - 3; i--)
                    {
                        if (history[i].Open >= history[i].Close
                            && history[i].High <= instruments[instrument].middle30BB
                            && (history[i].High - history[i].Low) > range
                            && sell > 0)
                            sell++;
                        else if (history[i].Open <= history[i].Close
                            && history[i].Low >= instruments[instrument].middle30BB
                            && (history[i].High - history[i].Low) > range
                            && buy > 0)
                            buy++;
                    }
                    if (sell >= 2 && buy <= 1)
                    {
                        Console.WriteLine("Time Stamp {0} -VE TREND in 30min Candle Close: Recommending to Place SELL ORDER at WEEKMA {1} for Script {2} and the LTP is {3}", DateTime.Now.ToString("yyyyMMdd hh:mm:ss"), instruments[instrument].weekMA, instruments[instrument].futName, ltp);
                        //instruments[token].type = OType.Sell;
                        //return true;
                    }
                    else if (buy >= 2 && sell <= 1)
                    {
                        Console.WriteLine("Time Stamp {0}  +VE TREND in 30min Candle Close: Recommending to Place BUY ORDER at WEEKMA {1} for Script {2} and the LTP is {3}", DateTime.Now.ToString("yyyyMMdd hh:mm:ss"), instruments[instrument].weekMA, instruments[instrument].futName, ltp);
                        //instruments[token].type = OType.Buy;
                        //return true;
                    }
                    else
                    {
                        // Console.WriteLine("Time Stamp {0}  SQUEEZED: But no clear indication for Script {1} and the LTP is {2}", DateTime.Now.ToString("yyyyMMdd hh:mm:ss"), instruments[token].futName, ltp);
                    }
                }


                                */

        /*
        public bool VerifyLtp(Tick tickData)
        {
            bool qualified = false;
            decimal ltp = (decimal)tickData.LastPrice;
            decimal timenow = (decimal)DateTime.Now.Hour + ((decimal)DateTime.Now.Minute / 100);
            if (instruments[tickData.InstrumentToken].status == Status.OPEN
                && instruments[tickData.InstrumentToken].bot30bb > 0
                && instruments[tickData.InstrumentToken].middle30BBnew > 0
                && Decimal.Compare(timenow, Convert.ToDecimal(ConfigurationManager.AppSettings["CutoffTime"])) < 0) //change back to 14.24
            {
                #region Verify for Open Trigger
                decimal r1 = Convert.ToDecimal((ltp + ltp * (decimal).0005).ToString("#.#"));
                decimal r2 = Convert.ToDecimal((ltp - ltp * (decimal).0005).ToString("#.#"));
                decimal r3 = Convert.ToDecimal((ltp * (decimal).018).ToString("#.#"));
                decimal variance23 = (ltp * (decimal)2.3) / 100;
                decimal variance14 = (ltp * (decimal)1.4) / 100;
                decimal variance2 = (ltp * (decimal)2) / 100;
                //decimal r4 = Convert.ToDecimal((ltp * (decimal).0025).ToString("#.#"));
                //decimal square2 = (tickData.Open / 100) * (decimal)0.5;

                bool flag = DateTime.Now.Minute == 43 || DateTime.Now.Minute == 13
                                || DateTime.Now.Minute == 44 || DateTime.Now.Minute == 14
                                || (DateTime.Now.Minute == 45 && DateTime.Now.Second <= 25)
                                || (DateTime.Now.Minute == 15 && DateTime.Now.Second <= 25) ? true : false;
                //decimal variance = DateTime.Now.Minute == 45 || DateTime.Now.Minute == 15 ? (decimal).0003 : (decimal).0006;
                if (instruments[tickData.InstrumentToken].isOpenAlign
                    && Decimal.Compare(timenow, Convert.ToDecimal(9.44)) > 0)
                    //&& continueFlag)
                {
                    switch (instruments[tickData.InstrumentToken].type)
                    {
                        case OType.Sell:
                        case OType.StrongSell:
                            #region Verify Sell Trigger
                            qualified = r1 >= instruments[tickData.InstrumentToken].shortTrigger;

                            if (instruments[tickData.InstrumentToken].isReversed & !flag)
                            {
                                if (qualified)
                                {
                                    System.Threading.Thread.Sleep(400);
                                    List<Historical> history = kite.GetHistoricalData(tickData.InstrumentToken.ToString(),
                                                                DateTime.Now.Date.AddHours(9).AddMinutes(15),
                                                                //DateTime.Now.Date.AddHours(9).AddMinutes(16),
                                                                DateTime.Now.Date.AddDays(1),
                                                                "30minute");
                                    if (history.Count == 2)
                                    {
                                        // Do nothing
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
                                        if (history[history.Count - 2].Close > instruments[tickData.InstrumentToken].shortTrigger)
                                        {
                                            Console.WriteLine("Time {0} Averting Candle: Please Ignore this script {1} as it has closed above the Short Trigger for now wherein prevCandle Close {2} vs short trigger {3}", DateTime.Now.ToString(), instruments[tickData.InstrumentToken].futName, history[history.Count - 2].Close, instruments[tickData.InstrumentToken].shortTrigger);
                                            //instruments[tickData.InstrumentToken].shortTrigger = instruments[tickData.InstrumentToken].top30bb;
                                            //instruments[tickData.InstrumentToken].type = OType.Buy;
                                            //instruments[tickData.InstrumentToken].isReversed = false;
                                            instruments[tickData.InstrumentToken].isOpenAlign = false;
                                            modifyOpenAlignOrReversedStatus(instruments[tickData.InstrumentToken], 16, OType.Sell, false);
                                            modifyOpenAlignOrReversedStatus(instruments[tickData.InstrumentToken], 17, OType.Sell, false);
qualified = false;
                                            if (instruments[tickData.InstrumentToken].status != Status.POSITION)
                                            {
                                                uint[] toArray = new uint[] { tickData.InstrumentToken };
ticker.UnSubscribe(toArray);
                                                instruments[tickData.InstrumentToken].status = Status.CLOSE;
                                            }
                                            if (instruments[tickData.InstrumentToken].status == Status.OPEN)
                                                instruments[tickData.InstrumentToken].status = Status.CLOSE;
                                            return qualified;
                                        }
                                    }
                                }
                                if (Decimal.Compare(timenow, Convert.ToDecimal(13.30)) > 0)
                                {
                                    if (VerifyNifty(timenow) == OType.Sell)
                                    {
                                        qualified = (r1 >= instruments[tickData.InstrumentToken].shortTrigger || IsBetweenVariance(r1, instruments[tickData.InstrumentToken].shortTrigger, (decimal).0006))
                                            && (r1 >= instruments[tickData.InstrumentToken].ma50 || IsBetweenVariance(r1, instruments[tickData.InstrumentToken].ma50, (decimal).0006));
                                        if (qualified)
                                        {
                                            Console.WriteLine("Time {0} Trigger Verification PASSed: The script {1} as Short Trigger {2} vs r1 {3} & ma50 is at {4} for Sell", DateTime.Now.ToString(), instruments[tickData.InstrumentToken].futName, instruments[tickData.InstrumentToken].shortTrigger, r1, instruments[tickData.InstrumentToken].ma50);
                                        }
                                    }
                                    else
                                    {
                                        qualified = r1 >= instruments[tickData.InstrumentToken].shortTrigger || IsBetweenVariance(r1, instruments[tickData.InstrumentToken].shortTrigger, (decimal).0006);
                                        if (qualified)
                                        {
                                            if ((instruments[tickData.InstrumentToken].botBB + variance14) < instruments[tickData.InstrumentToken].topBB)
                                            {
                                                if ((instruments[tickData.InstrumentToken].botBB + variance23) < instruments[tickData.InstrumentToken].topBB)
                                                {
                                                    if ((IsBetweenVariance(ltp, instruments[tickData.InstrumentToken].topBB, (decimal).001)
                                                            || ltp > instruments[tickData.InstrumentToken].topBB))
                                                        //&& ltp > instruments[tickData.InstrumentToken].ma50)
                                                    {
                                                        // Do nothing and place order
                                                    }
                                                    else
                                                    {
                                                        Console.WriteLine("Time {0} Averting Squeez: Please Ignore this script {1} as it has Expanded so much for now wherein topBB {2} & botBB {3} for Sell", DateTime.Now.ToString(), instruments[tickData.InstrumentToken].futName, instruments[tickData.InstrumentToken].topBB, instruments[tickData.InstrumentToken].botBB);
                                                        qualified = false;
                                                    }
                                                }
                                                else
                                                {
                                                    if (IsBetweenVariance(ltp, instruments[tickData.InstrumentToken].middleBB, (decimal).001) || ltp > instruments[tickData.InstrumentToken].middleBB)
                                                    {
                                                        // Do nothing and place order
                                                    }
                                                    else
                                                    {
                                                        Console.WriteLine("Time {0} Averting Squeez: Please Ignore this script {1} as Latest LTP {2} is not close to middleBB {3} for Sell", DateTime.Now.ToString(), instruments[tickData.InstrumentToken].futName, ltp, instruments[tickData.InstrumentToken].middleBB);
                                                        qualified = false;
                                                    }
                                                }
                                            }
                                            else
                                            {
                                                if (IsBetweenVariance(ltp, instruments[tickData.InstrumentToken].middleBB, (decimal).001) || ltp > instruments[tickData.InstrumentToken].middleBB)
                                                {
                                                    // Do nothing and place order
                                                }
                                                else
                                                {
                                                    Console.WriteLine("Time {0} Averting Trigger: Please Ignore this script {1} as Latest LTP {2} is not close to middleBB {3} for Sell", DateTime.Now.ToString(), instruments[tickData.InstrumentToken].futName, ltp, instruments[tickData.InstrumentToken].middleBB);
                                                    qualified = false;
                                                }
                                            }
                                        }
                                        else
                                        {
                                        }
                                    }
                                }
                                else
                                {
                                    qualified = (r1 >= instruments[tickData.InstrumentToken].shortTrigger || IsBetweenVariance(r1, instruments[tickData.InstrumentToken].shortTrigger, (decimal).0006));
                                    if (qualified)
                                    {
                                        if ((instruments[tickData.InstrumentToken].botBB + variance23) < instruments[tickData.InstrumentToken].topBB && Decimal.Compare(timenow, Convert.ToDecimal(11.14)) > 0)
                                        {
                                            Console.WriteLine("Time {0} Averting Expansion: Please Ignore this script {1} as it has Expanded so much for now wherein topBB {2} & botBB {3} for Sell", DateTime.Now.ToString(), instruments[tickData.InstrumentToken].futName, instruments[tickData.InstrumentToken].topBB, instruments[tickData.InstrumentToken].botBB);
                                            qualified = false;
                                        }
                                        else
                                        {
                                            if (instruments[tickData.InstrumentToken].ma50 > instruments[tickData.InstrumentToken].shortTrigger)
                                            {
                                                qualified = IsBetweenVariance(ltp, instruments[tickData.InstrumentToken].ma50, (decimal).0006);
                                                if (VerifyNifty(timenow) == OType.Sell && !qualified)
                                                {
                                                    if ((instruments[tickData.InstrumentToken].botBB + variance14) > instruments[tickData.InstrumentToken].topBB 
                                                        && Decimal.Compare(timenow, Convert.ToDecimal(11.14)) > 0
                                                        && !(IsBetweenVariance(tickData.Low, instruments[tickData.InstrumentToken].weekMA, (decimal).0006)
                                                                || IsBetweenVariance(tickData.Low, instruments[tickData.InstrumentToken].middle30ma50, (decimal).0006)
                                                                || IsBetweenVariance(tickData.Low, instruments[tickData.InstrumentToken].bot30bb, (decimal).0006)))
                                                    {
                                                        qualified = IsBetweenVariance(ltp, instruments[tickData.InstrumentToken].topBB, (decimal).0006);
                                                        if (qualified)
                                                            Console.WriteLine("You could have AVOIDED this. Qualified for SELL order {0} based on LTP {1} is ~ below short trigger {2} and ltp is around ma50 {3} ", instruments[tickData.InstrumentToken].futName, r1, instruments[tickData.InstrumentToken].shortTrigger, instruments[tickData.InstrumentToken].ma50);
                                                    }
                                                }
                                            }
                                            else if (instruments[tickData.InstrumentToken].ma50<ltp)
                                            {
                                                qualified = IsBetweenVariance(ltp, instruments[tickData.InstrumentToken].topBB, (decimal).0006);
                                            }
                                            if (qualified)
                                            {
                                                //Console.WriteLine("INSIDER :: Qualified for SELL order based on LTP {0} is ~ below short trigger {1} and ltp is around topBB {2} wherein ma50 {3} is still to go", r1, instruments[tickData.InstrumentToken].shortTrigger, instruments[tickData.InstrumentToken].topBB, instruments[tickData.InstrumentToken].ma50);
                                            }
                                        }
                                    }
                                }
                            }
                            else
                            {
                                if (qualified && !instruments[tickData.InstrumentToken].isReversed)
                                    qualified = CalculateBB((uint)instruments[tickData.InstrumentToken].instrumentId, tickData);
                                else
                                    qualified = false;
                            }
                            #endregion
                            break;
                        case OType.Buy:
                        case OType.StrongBuy:
                            #region Verify BUY Trigger
                            qualified = r2 <= instruments[tickData.InstrumentToken].longTrigger;
                            if (instruments[tickData.InstrumentToken].isReversed & !flag)
                            {
                                if (qualified)
                                {
                                    System.Threading.Thread.Sleep(400);
                                    List<Historical> history = kite.GetHistoricalData(tickData.InstrumentToken.ToString(),
                                                                DateTime.Now.Date.AddHours(9).AddMinutes(15),
                                                                //DateTime.Now.Date.AddHours(9).AddMinutes(46),
                                                                DateTime.Now.Date.AddDays(1),
                                                                "30minute");
                                    if (history.Count == 2)
                                    {
    //Do nothing
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
                                        if (history[history.Count - 2].Close<instruments[tickData.InstrumentToken].longTrigger)
                                        {
                                            Console.WriteLine("Time {0} Averting Candle: Please Ignore this script {1} as it has closed below the long Trigger for now wherein prevCandle Close {2} vs long trigger {3}", DateTime.Now.ToString(), instruments[tickData.InstrumentToken].futName, history[history.Count - 2].Close, instruments[tickData.InstrumentToken].longTrigger);
                                            //instruments[tickData.InstrumentToken].longTrigger = instruments[tickData.InstrumentToken].bot30bb;
                                            //instruments[tickData.InstrumentToken].type = OType.Buy;
                                            //instruments[tickData.InstrumentToken].isReversed = false;
                                            instruments[tickData.InstrumentToken].isOpenAlign = false;
                                            modifyOpenAlignOrReversedStatus(instruments[tickData.InstrumentToken], 16, OType.Buy, false);
                                            modifyOpenAlignOrReversedStatus(instruments[tickData.InstrumentToken], 17, OType.Buy, false);
qualified = false;
                                            if (instruments[tickData.InstrumentToken].status != Status.POSITION)
                                            {
                                                uint[] toArray = new uint[] { tickData.InstrumentToken };
ticker.UnSubscribe(toArray);
                                                instruments[tickData.InstrumentToken].status = Status.CLOSE;
                                            }
                                            if (instruments[tickData.InstrumentToken].status == Status.OPEN)
                                                instruments[tickData.InstrumentToken].status = Status.CLOSE;
                                            return qualified;
                                        }
                                    }
                                }
                                if (Decimal.Compare(timenow, Convert.ToDecimal(13.30)) > 0)
                                {
                                    if (VerifyNifty(timenow) == OType.Buy)
                                    {
                                        qualified = (r2 <= instruments[tickData.InstrumentToken].longTrigger || IsBetweenVariance(r2, instruments[tickData.InstrumentToken].longTrigger, (decimal).0006))
                                            && (r2 <= instruments[tickData.InstrumentToken].ma50 || IsBetweenVariance(r2, instruments[tickData.InstrumentToken].ma50, (decimal).0006));
                                        if (qualified)
                                        {
                                            Console.WriteLine("Time {0} Trigger Verification PASSed: The script {1} as long Trigger {2} vs r2 {3} & ma50 is at {4} for Buy", DateTime.Now.ToString(), instruments[tickData.InstrumentToken].futName, instruments[tickData.InstrumentToken].shortTrigger, r2, instruments[tickData.InstrumentToken].ma50);
                                        }
                                    }
                                    else
                                    {
                                        qualified = r2 <= instruments[tickData.InstrumentToken].longTrigger || IsBetweenVariance(r2, instruments[tickData.InstrumentToken].longTrigger, (decimal).0006);
                                        if (qualified)
                                        {
                                            if ((instruments[tickData.InstrumentToken].botBB + variance14) < instruments[tickData.InstrumentToken].topBB)
                                            {
                                                if ((instruments[tickData.InstrumentToken].botBB + variance23) < instruments[tickData.InstrumentToken].topBB)
                                                {
                                                    if ((IsBetweenVariance(ltp, instruments[tickData.InstrumentToken].botBB, (decimal).001)
                                                            || ltp<instruments[tickData.InstrumentToken].botBB))
                                                        //&& ltp < instruments[tickData.InstrumentToken].ma50)
                                                    {
                                                        // Do nothing and place order
                                                    }
                                                    else
                                                    {
                                                        Console.WriteLine("Time {0} Averting Squeez: Please Ignore this script {1} as it has Expanded so much for now wherein topBB {2} & botBB {3} for Buy", DateTime.Now.ToString(), instruments[tickData.InstrumentToken].futName, instruments[tickData.InstrumentToken].topBB, instruments[tickData.InstrumentToken].botBB);
                                                        qualified = false;
                                                    }
                                                }
                                                else
                                                {
                                                    if (IsBetweenVariance(ltp, instruments[tickData.InstrumentToken].middleBB, (decimal).001) || ltp<instruments[tickData.InstrumentToken].middleBB)
                                                    {
                                                        // Do nothing and place order
                                                    }
                                                    else
                                                    {
                                                        Console.WriteLine("Time {0} Averting Squeez: Please Ignore this script {1} as Latest LTP {2} is not close to middleBB {3} for Buy", DateTime.Now.ToString(), instruments[tickData.InstrumentToken].futName, ltp, instruments[tickData.InstrumentToken].middleBB);
                                                        qualified = false;
                                                    }
                                                }
                                            }
                                            else
                                            {
                                                if (IsBetweenVariance(ltp, instruments[tickData.InstrumentToken].middleBB, (decimal).001) || ltp<instruments[tickData.InstrumentToken].middleBB)
                                                {
                                                    // Do nothing and place order
                                                }
                                                else
                                                {
                                                    Console.WriteLine("Time {0} Averting Trigger: Please Ignore this script {1} as Latest LTP {2} is not close to middleBB {3} for Buy", DateTime.Now.ToString(), instruments[tickData.InstrumentToken].futName, ltp, instruments[tickData.InstrumentToken].middleBB);
                                                    qualified = false;
                                                }
                                            }
                                        }
                                        else
                                        {
                                        }
                                    }
                                }
                                else
                                {
                                    qualified = (r2 <= instruments[tickData.InstrumentToken].longTrigger || IsBetweenVariance(r2, instruments[tickData.InstrumentToken].longTrigger, (decimal).0006));
                                    if (qualified)
                                    {
                                        if ((instruments[tickData.InstrumentToken].botBB + variance23) < instruments[tickData.InstrumentToken].topBB && Decimal.Compare(timenow, Convert.ToDecimal(11.14)) > 0)
                                        {
                                            Console.WriteLine("Time {0} Averting Expansion: Please Ignore this script {1} as it has Expanded so much for now wherein topBB {2} & botBB {3} for Buy", DateTime.Now.ToString(), instruments[tickData.InstrumentToken].futName, instruments[tickData.InstrumentToken].topBB, instruments[tickData.InstrumentToken].botBB);
                                            qualified = false;
                                        }
                                        else
                                        {
                                            if (instruments[tickData.InstrumentToken].ma50<instruments[tickData.InstrumentToken].longTrigger)
                                            {
                                                qualified = IsBetweenVariance(ltp, instruments[tickData.InstrumentToken].ma50, (decimal).0006);
                                                if (VerifyNifty(timenow) == OType.Buy && !qualified)
                                                {
                                                    if ((instruments[tickData.InstrumentToken].botBB + variance14) > instruments[tickData.InstrumentToken].topBB 
                                                        && Decimal.Compare(timenow, Convert.ToDecimal(11.14)) > 0
                                                        && !(IsBetweenVariance(tickData.High, instruments[tickData.InstrumentToken].weekMA, (decimal).0006)
                                                                || IsBetweenVariance(tickData.High, instruments[tickData.InstrumentToken].middle30ma50, (decimal).0006)
                                                                || IsBetweenVariance(tickData.High, instruments[tickData.InstrumentToken].top30bb, (decimal).0006)))
                                                    {
                                                        qualified = IsBetweenVariance(ltp, instruments[tickData.InstrumentToken].botBB, (decimal).0006);
                                                        if (qualified)
                                                            Console.WriteLine("You could have AVOIDED this. Qualified for BUY order {0} based on LTP {1} is ~ above long trigger {2} and ltp is around ma50 {3} ", instruments[tickData.InstrumentToken].futName, r1, instruments[tickData.InstrumentToken].longTrigger, instruments[tickData.InstrumentToken].ma50);
                                                    }
                                                }
                                            }
                                            else if (instruments[tickData.InstrumentToken].ma50 > ltp)
                                            {
                                                qualified = IsBetweenVariance(ltp, instruments[tickData.InstrumentToken].botBB, (decimal).0006);
                                            }
                                            if (qualified)
                                            {
                                                //Console.WriteLine("INSIDER :: Qualified for BUY order based on LTP {0} is ~ above long trigger {1} and ltp is around botBB {2} wherein ma50 {3} is still to go", r1, instruments[tickData.InstrumentToken].longTrigger, instruments[tickData.InstrumentToken].botBB, instruments[tickData.InstrumentToken].ma50);
                                            }
                                        }
                                    }
                                }
                            }
                            else
                            {
                                if (qualified && !instruments[tickData.InstrumentToken].isReversed)
                                    qualified = CalculateBB((uint)instruments[tickData.InstrumentToken].instrumentId, tickData);
                                else
                                    qualified = false;
                            }
                            #endregion
                            break;
                        default:
                            break;
                    }
                }
                if (!qualified && instruments[tickData.InstrumentToken].isOpenAlign)
                {
                    if (Decimal.Compare(timenow, Convert.ToDecimal(ConfigurationManager.AppSettings["CutOnTime"])) > 0
                        || (DateTime.Now.Hour == 9 && DateTime.Now.Minute == 44))
                    {
                        CalculateSqueez(tickData.InstrumentToken, tickData);
                        //qualified = CalculateSqueez(tickData.InstrumentToken, tickData);
                        //if (qualified)
                        //    Console.WriteLine("Script {0} Place order is Commented", wl.futName);
                    }
                }
                #endregion  
            }
            else if (instruments[tickData.InstrumentToken].status == Status.POSITION)
            {
                try
                {
                    #region Verify and Modify Exit Trigger
                    try
                    {
                        #region ValidateIsExitRequired
                        //System.Threading.Thread.Sleep(400);
                        if (!instruments[tickData.InstrumentToken].requiredExit 
                            && !instruments[tickData.InstrumentToken].isReorder)
                        {
                            try
                            {
                                Position pos = ValidateOpenPosition(tickData.InstrumentToken, instruments[tickData.InstrumentToken].futId);
                                if (instruments[tickData.InstrumentToken].requiredExit)
                                {
                                    ModifyOrderForContract(pos, tickData.InstrumentToken);
                                }
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine("EXCEPTION in RequireExit validation at {0} with message {1}", DateTime.Now.ToString(), ex.Message);
                            }
                        }
                        #endregion
                        if (ValidateOrderTime(instruments[tickData.InstrumentToken].orderTime)
                            && !instruments[tickData.InstrumentToken].isReorder)
                        {
                            Position pos = GetCurrentPNL(instruments[tickData.InstrumentToken].futId);
                            if (pos.Quantity > 0 && pos.PNL< -300) //instruments[tickData.InstrumentToken].type == OType.Buy
                            {
                                #region Cancel Buy Order
                                if (instruments[tickData.InstrumentToken].ma50 > 0
                                    && (ltp<instruments[tickData.InstrumentToken].longTrigger
                                        && ltp<instruments[tickData.InstrumentToken].ma50)
                                    || instruments[tickData.InstrumentToken].requiredExit)
                                {
                                    if (!instruments[tickData.InstrumentToken].isReorder)
                                    {
                                        Console.WriteLine("At {0} : The order of the script {1} is found and validating for modification based on PNL {2}", DateTime.Now.ToString(), instruments[tickData.InstrumentToken].futName, pos.PNL);
                                        if ((pos.PNL > -2000
                                                || IsBetweenVariance(ltp, instruments[tickData.InstrumentToken].middleBB, (decimal).0006)
                                                || ltp > instruments[tickData.InstrumentToken].middleBB)
                                            || (pos.PNL > -3000 && instruments[tickData.InstrumentToken].requiredExit && instruments[tickData.InstrumentToken].doubledrequiredExit))
                                        {
                                            OType trend = CalculateSqueezedTrend(instruments[tickData.InstrumentToken].futName, instruments[tickData.InstrumentToken].history, 10);
                                            if (trend == OType.Sell || trend == OType.StrongSell || Decimal.Compare(timenow, (decimal)11.15) < 0)
                                            {
                                                Console.WriteLine("HARDEXIT NOW at {0} :: The BUY order status of the script {1} is better Exit point so EXIT NOW with loss of {2}", DateTime.Now.ToString(), instruments[tickData.InstrumentToken].futName, instruments[tickData.InstrumentToken].lotSize* (instruments[tickData.InstrumentToken].longTrigger - ltp));
                                                CancelAndReOrder(tickData.InstrumentToken, OType.Buy);
                                            }
                                        }
                                    }
                                    else if (pos.PNL< -6000
                                        || IsBetweenVariance(ltp, instruments[tickData.InstrumentToken].bot30bb, (decimal).0006))
                                    {
                                        ModifyOrderForContract(pos, tickData.InstrumentToken);
                                    }
                                }
                                #endregion
                            }
                            else if (pos.Quantity< 0 && pos.PNL< -300) // instruments[tickData.InstrumentToken].type == OType.Sell
                            {
                                #region Cancel Sell Order
                                if (instruments[tickData.InstrumentToken].ma50 > 0
                                    && (ltp > instruments[tickData.InstrumentToken].shortTrigger
                                        && ltp > instruments[tickData.InstrumentToken].ma50)
                                    || instruments[tickData.InstrumentToken].requiredExit)
                                {
                                    if (!instruments[tickData.InstrumentToken].isReorder)
                                    {
                                        Console.WriteLine("At {0} : The order of the script {1} is found and validating for modification based on PNL {2}", DateTime.Now.ToString(), instruments[tickData.InstrumentToken].futName, pos.PNL);
                                        if ((pos.PNL > -2000
                                                || IsBetweenVariance(ltp, instruments[tickData.InstrumentToken].middleBB, (decimal).0006)
                                                || ltp<instruments[tickData.InstrumentToken].middleBB)
                                            || (pos.PNL > -3000 && instruments[tickData.InstrumentToken].requiredExit && instruments[tickData.InstrumentToken].doubledrequiredExit))
                                        {
                                            OType trend = CalculateSqueezedTrend(instruments[tickData.InstrumentToken].futName, instruments[tickData.InstrumentToken].history, 10);
                                            if (trend == OType.Buy || trend == OType.StrongBuy || Decimal.Compare(timenow, (decimal)11.15) < 0)
                                            {
                                                Console.WriteLine("HARDEXIT NOW at {0} :: The SELL order status of the script {1} is better Exit point so EXIT NOW with loss of {2}", DateTime.Now.ToString(), instruments[tickData.InstrumentToken].futName, instruments[tickData.InstrumentToken].lotSize* (ltp - instruments[tickData.InstrumentToken].shortTrigger));
                                                CancelAndReOrder(tickData.InstrumentToken, OType.Sell);
                                            }
                                        }
                                    }
                                    else if (pos.PNL< -6000
                                        || IsBetweenVariance(ltp, instruments[tickData.InstrumentToken].top30bb, (decimal).0006))
                                    {
                                        ModifyOrderForContract(pos, tickData.InstrumentToken);
                                    }
                                }
                                #endregion
                            }
                            else if (pos.Quantity == 0 && pos.PNL< -300)
                            {
                                if (!instruments[tickData.InstrumentToken].isHedgingOrder)
                                {
                                    instruments[tickData.InstrumentToken].status = Status.CLOSE;
                                    modifyOrderInCSV(tickData.InstrumentToken, instruments[tickData.InstrumentToken].futName, instruments[tickData.InstrumentToken].type, Status.CLOSE);
                                }
                            }
                            if (!instruments[tickData.InstrumentToken].doubledrequiredExit && !instruments[tickData.InstrumentToken].isReorder)
                            {
                                ValidateScriptTrend(pos, tickData.InstrumentToken);
                                if (pos.PNL< -6000 && !instruments[tickData.InstrumentToken].doubledrequiredExit)
                                {
                                    Console.WriteLine("1. OMG This script is bleeding RED at {0} :: The order status of the script {1} has gone seriously bad state with {2}", DateTime.Now.ToString(), instruments[tickData.InstrumentToken].futName, pos.PNL);
                                    instruments[tickData.InstrumentToken].requiredExit = true;
                                    instruments[tickData.InstrumentToken].doubledrequiredExit = true;
                                }
                            }
                        }
                        else if (instruments[tickData.InstrumentToken].requiredExit
                            && instruments[tickData.InstrumentToken].weekMA > 0
                            && instruments[tickData.InstrumentToken].ma50 > 0
                            && !instruments[tickData.InstrumentToken].isReorder)
                        {
                            try
                            {
                                Position pos = GetCurrentPNL(instruments[tickData.InstrumentToken].futId);
                                if (!instruments[tickData.InstrumentToken].doubledrequiredExit
                                    && pos.PNL <= -6000)
                                {
                                    Console.WriteLine("2. OMG This script is bleeding RED at {0} :: The order status of the script {1} has gone seriously bad state with {2}", DateTime.Now.ToString(), instruments[tickData.InstrumentToken].futName, pos.PNL);
                                    instruments[tickData.InstrumentToken].doubledrequiredExit = true;
                                }
                                DateTime dt = Convert.ToDateTime(instruments[tickData.InstrumentToken].orderTime);
                                if (!instruments[tickData.InstrumentToken].isReorder) // DateTime.Now > dt.AddMinutes(3))
                                {
                                    if ((DateTime.Now.Minute >= 12 && DateTime.Now.Minute< 15)
                                        || (DateTime.Now.Minute >= 42 && DateTime.Now.Minute< 45)
                                        || (instruments[tickData.InstrumentToken].doubledrequiredExit
                                            && IsBetweenVariance(ltp, instruments[tickData.InstrumentToken].middle30BB, (decimal).0015)))
                                    {
                                        if (instruments[tickData.InstrumentToken].isReversed 
                                            && instruments[tickData.InstrumentToken].requiredExit)
                                        {
                                            decimal variance2 = (ltp * (decimal)2) / 100;
                                            if (pos.PNL< -1000 && pos.PNL> -2000
                                                && (instruments[tickData.InstrumentToken].bot30bb + variance2) < instruments[tickData.InstrumentToken].top30bb
                                                || (instruments[tickData.InstrumentToken].doubledrequiredExit
                                                    && IsBetweenVariance(ltp, instruments[tickData.InstrumentToken].middle30BB, (decimal).0015)))
                                            {
                                                if (pos.Quantity > 0 && ltp<instruments[tickData.InstrumentToken].ma50)
                                                {
                                                    Console.WriteLine("In VerifyLTP at {0} :: Processing the Order {1} as it has gone seriously bleeding state with {2}", DateTime.Now.ToString(), instruments[tickData.InstrumentToken].futName, pos.PNL);
                                                    ProcessOpenPosition(pos, tickData.InstrumentToken, OType.Sell);
                                                }
                                                else if (pos.Quantity< 0 && ltp> instruments[tickData.InstrumentToken].ma50)
                                                {
                                                    Console.WriteLine("In VerifyLTP at {0} :: Processing the Order {1} as it has gone seriously bleeding state with {2}", DateTime.Now.ToString(), instruments[tickData.InstrumentToken].futName, pos.PNL);
                                                    ProcessOpenPosition(pos, tickData.InstrumentToken, OType.Buy);
                                                }
                                            }
                                        }
                                        else if (!instruments[tickData.InstrumentToken].isReversed
                                            && (IsBeyondVariance(ltp, instruments[tickData.InstrumentToken].weekMA, (decimal).002)
                                                || instruments[tickData.InstrumentToken].doubledrequiredExit))
                                        {
                                            if (pos.PNL< -1000 && pos.PNL> -2000
                                                || (instruments[tickData.InstrumentToken].doubledrequiredExit
                                                    && pos.PNL > -4000))
                                            {
                                                if (pos.Quantity > 0 
                                                    && ltp<instruments[tickData.InstrumentToken].weekMA
                                                    && IsBetweenVariance(instruments[tickData.InstrumentToken].weekMA, instruments[tickData.InstrumentToken].bot30bb, (decimal).004)
                                                    && instruments[tickData.InstrumentToken].weekMA > instruments[tickData.InstrumentToken].bot30bb)
                                                {
                                                    Console.WriteLine("In VerifyLTP at {0} :: Processing the Order {1} as it has gone bad state with {2}", DateTime.Now.ToString(), instruments[tickData.InstrumentToken].futName, pos.PNL);
                                                    ProcessOpenPosition(pos, tickData.InstrumentToken, OType.Sell);
                                                }
                                                else if (pos.Quantity< 0 
                                                    && ltp> instruments[tickData.InstrumentToken].weekMA
                                                    && IsBetweenVariance(instruments[tickData.InstrumentToken].weekMA, instruments[tickData.InstrumentToken].top30bb, (decimal).004)
                                                    && instruments[tickData.InstrumentToken].weekMA<instruments[tickData.InstrumentToken].top30bb)
                                                {
                                                    Console.WriteLine("In VerifyLTP at {0} :: Processing the Order {1} as it has gone bad state with {2}", DateTime.Now.ToString(), instruments[tickData.InstrumentToken].futName, pos.PNL);
                                                    ProcessOpenPosition(pos, tickData.InstrumentToken, OType.Buy);
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
                        else if (instruments[tickData.InstrumentToken].oldTime != instruments[tickData.InstrumentToken].currentTime
                            && !instruments[tickData.InstrumentToken].isReorder)
                        {
                            instruments[tickData.InstrumentToken].oldTime = instruments[tickData.InstrumentToken].currentTime;
                            Console.WriteLine("In VerifyLTP at {0} This is a Reverse Order of {1} current state is as follows", DateTime.Now.ToString(), instruments[tickData.InstrumentToken].futName);
                            OType currentTrend = CalculateSqueezedTrend(instruments[tickData.InstrumentToken].futName,
                                instruments[tickData.InstrumentToken].history,
                                10);
                            if (instruments[tickData.InstrumentToken].type == OType.Sell
                                && currentTrend == OType.StrongBuy)
                            {
                                Position pos = GetCurrentPNL(instruments[tickData.InstrumentToken].futId);
Order order = GetCurrentOrder(instruments[tickData.InstrumentToken].futId);
Console.WriteLine("Time to Exit For contract Immediately for the current reversed SELL order of {0} which is placed at {1}", order.Tradingsymbol, order.Price);
                            }
                            else if (instruments[tickData.InstrumentToken].type == OType.Buy
                                && currentTrend == OType.StrongSell)
                            {
                                Position pos = GetCurrentPNL(instruments[tickData.InstrumentToken].futId);
Order order = GetCurrentOrder(instruments[tickData.InstrumentToken].futId);
Console.WriteLine("Time to Exit For contract Immediately for the current reversed BUY order of {0} which is placed at {1}", order.Tradingsymbol, order.Price);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine("at {0} EXCEPTION in VerifyLTP_POSITION :: The order status of the script {1} is being validated but recieved exception {2}", DateTime.Now.ToString(), instruments[tickData.InstrumentToken].futName, ex.Message);
                    }
                    #endregion
                }
                catch (Exception ex)
                {
                    Console.WriteLine("EXCEPTION :: As expected, You have clearly messed it somewhere with new logic; {0}", ex.Message);
                    instruments[tickData.InstrumentToken].oldTime = instruments[tickData.InstrumentToken].currentTime;
                }
            }
            else if (instruments[tickData.InstrumentToken].status == Status.STANDING)
                //&& instruments[tickData.InstrumentToken].currentTime != instruments[tickData.InstrumentToken].oldTime)
            {
                #region Check for Standing Orders
                try
                {
                    //instruments[tickData.InstrumentToken].oldTime = instruments[tickData.InstrumentToken].currentTime;
                    DateTime dt = DateTime.Now;
int counter = 0;
                    foreach (Order order in kite.GetOrders())
                    {
                        if (instruments[tickData.InstrumentToken].futId == order.InstrumentToken)
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
                                    Console.WriteLine("Getting OPEN Order Time {0} & Current Time {1} of {2} is more than than 6 minutes. So cancelling the order ID {3}", order.OrderTimestamp, DateTime.Now.ToString(), instruments[tickData.InstrumentToken].futName, order.OrderId);
                                    kite.CancelOrder(order.OrderId, Variety: "bo");
                                    instruments[tickData.InstrumentToken].status = Status.OPEN;
                                }
                                catch (Exception ex)
                                {
                                    Console.WriteLine("EXCEPTION at {0}:: Cancelling Idle Order for 20 minutes is failed with message {1}", DateTime.Now.ToString(), ex.Message);
                                }
                            }
                        }
                    }
                    if (counter == 1)
                        instruments[tickData.InstrumentToken].status = Status.POSITION;
                    else if (counter == 2)
                        instruments[tickData.InstrumentToken].status = Status.CLOSE;
                    //else if (counter == 0)
                    //    instruments[tickData.InstrumentToken].status = Status.POSITION;
                }
                catch (Exception ex)
                {
                    Console.WriteLine("at {0} EXCEPTION in VerifyLTP_STANDING :: The order status of the script {1} is being validated but recieved exception {2}", DateTime.Now.ToString(), instruments[tickData.InstrumentToken].futName, ex.Message);
                }
                #endregion
            }
            return qualified;
        }
        */

        /*
                        #region Verify LTP is at boundaries
                qualified = (ltp >= instruments[instrument].top30bb
                                || IsBetweenVariance(ltp, instruments[instrument].top30bb, varRang))
                            && (spike <= (ltp - instruments[instrument].middleBB)
                                || (IsBetweenVariance(spike, (ltp - instruments[instrument].middleBB), (decimal).06)
                                    && IsBeyondVariance(ltp, instruments[instrument].top30bb, (decimal).003)))
                            && (isNiftyVolatile || ((instruments[instrument].bot30bb + variance25 + variance25) < instruments[instrument].top30bb) ?
                                (ltp >= instruments[instrument].topBB
                                    || IsBetweenVariance(ltp, instruments[instrument].topBB, (decimal).002))
                                : true);

                if (!qualified)
                {
                    if ((instruments[instrument].middle30ma50 + spikeN < instruments[instrument].bot30bb
                            || IsBetweenVariance(instruments[instrument].middle30ma50 + spikeN, instruments[instrument].bot30bb, (decimal).0015))
                        && instruments[instrument].middle30ma50 < (instruments[instrument].middle30BB - variance14))
                    {
                        if (instruments[instrument].movements2 >= 2 || instruments[instrument].movements3 >= 3)
                            spike = instruments[instrument].isNarrowed > 0 ? spikeV : instruments[instrument].movements3 >= 3 ? spikeMN : spikeM;
                        else if (instruments[instrument].movementb2 >= 2 || instruments[instrument].movementb3 >= 3)
                            spike = instruments[instrument].isNarrowed > 0 ? spikeV + spikeN : spikeV;
                        else if (instruments[instrument].isNarrowed > 0)
                        {
                            spike = instruments[instrument].isNarrowed >= 3 ? spikeMN : spikeMM;
                        }
                        else
                            spike = spikeNN;
                        qualified = ltp >= instruments[instrument].top30bb
                                && (spike <= (ltp - instruments[instrument].middleBB)
                                    || IsBetweenVariance(spike, (ltp - instruments[instrument].middleBB), (decimal).06));
                    }
                }
                else if (qualified
                    && ((instruments[instrument].botBB + variance14) > instruments[instrument].topBB
                            || IsBetweenVariance((instruments[instrument].botBB + variance14), instruments[instrument].topBB, (decimal).0006))
                    && (instruments[instrument].bot30bb + variance17) < instruments[instrument].top30bb
                    && IsBetweenVariance(ltp, instruments[instrument].top30bb, (decimal).005))
                {
                    if (IsBetweenVariance(ltp, instruments[instrument].topBB, (decimal).005)
                        || spike > (ltp - instruments[instrument].middleBB))
                    {
                        qualified = false;
                    }
                }
                if (qualified)
                {
                    if (Decimal.Compare(timenow, Convert.ToDecimal(10.15)) < 0
                        && (instruments[instrument].movement >= 2
                            || instruments[instrument].movements2 >= 4))
                    {
                        if (ltp < instruments[instrument].topBB
                                || IsBetweenVariance(ltp, instruments[instrument].topBB, (decimal).004))
                        {
                            qualified = false;
                        }
                    }
                    if (IsBetweenVariance(instruments[instrument].top30bb, instruments[instrument].middle30ma50, (decimal).0006))
                    {
                        if (ltp < instruments[instrument].top30bb || ltp < instruments[instrument].middle30ma50)
                        {
                            qualified = false;
                        }
                    }
                    qualified = qualified ? ValidatingCurrentTrend(instrument, tickData, OType.Sell, timenow) : qualified;
                    if (instruments[instrument].oldTime != instruments[instrument].currentTime && qualified)
                    {
                        instruments[instrument].oldTime = instruments[instrument].currentTime;
                        Console.WriteLine("{0} Qaulified:: For script {1}, As market is volatile, script has chosen top Sell Entry at {2} & top30bb {3} with spike range {4}", DateTime.Now.ToString(), instruments[instrument].futName, ltp, instruments[instrument].top30bb, spike);
                    }
                    else if (instruments[instrument].oldTime != instruments[instrument].currentTime && !qualified)
                    {
                        instruments[instrument].oldTime = instruments[instrument].currentTime;
                        Console.WriteLine("{0} Disqualified:: For script {1}, As Validation is failed for this script as top Sell Entry at {2} & bot30bb {3} with spike range {4}", DateTime.Now.ToString(), instruments[instrument].futName, ltp, instruments[instrument].top30bb, spike);
                    }
                    if (qualified && !VerifyVolume(instrument, volume, timenow))
                    {
                        decimal percent = (decimal)1.6;
                        if (Decimal.Compare(timenow, Convert.ToDecimal(11.01)) > 0
                            && volume < instruments[instrument].AvgVolume / percent
                            && spike >= spikeM)
                        {
                            //Go ahead and proceed to order
                        }
                        else
                        {
                            qualified = false;
                            Console.WriteLine("(Cacelling) But Disqualified Volume at Time {0} for Instrument {1} current Volume is {2} and average volume is {3}", DateTime.Now.ToString(), instruments[instrument].futName, volume, instruments[instrument].AvgVolume);
                            if (spikeVM <= (ltp - instruments[instrument].middleBB)
                                    && Decimal.Compare(timenow, Convert.ToDecimal(10.35)) > 0)
                            {
                                Console.WriteLine("(Cacelling) Another Worth a try.. but closing now");
                                CloseOrderTicker(instrument, true);
                                //qualified = true;
                            }
                            else if (spikeM >= (ltp - instruments[instrument].middleBB)
                                && VerifyHighVolume(instrument, volume, timenow))
                            {
                                Console.WriteLine("Cautious 4 *** Qualified again for Reverse BUY order with Volume at Time {0} for Instrument {1} current Volume is {2} and average volume is {3}", DateTime.Now.ToString(), instruments[instrument].futName, volume, instruments[instrument].AvgVolume);
                                CloseOrderTicker(instrument, true);
                            }
                        }
                    }
                    if (qualified)
                    {
                        instruments[instrument].type = OType.Sell;
                        instruments[instrument].shortTrigger = instruments[instrument].top30bb;
                        modifyOpenAlignOrReversedStatus(instruments[instrument], 16, OType.Sell, false);
                        //CalculateBB((uint)instruments[instrument].instrumentId, tickData, OType.Sell);
                    }
                }
                else
                {
                    qualified = (ltp <= instruments[instrument].bot30bb
                                    || IsBetweenVariance(ltp, instruments[instrument].bot30bb, varRang))
                                && (spike <= (instruments[instrument].middleBB - ltp)
                                    || (IsBetweenVariance(spike, (instruments[instrument].middleBB - ltp), (decimal).06)
                                        && IsBeyondVariance(ltp, instruments[instrument].bot30bb, (decimal).003)))
                                && (isNiftyVolatile || ((instruments[instrument].bot30bb + variance25 + variance25) < instruments[instrument].top30bb) ?
                                    (ltp <= instruments[instrument].botBB
                                        || IsBetweenVariance(ltp, instruments[instrument].botBB, (decimal).002))
                                    : true);

                    if (!qualified)
                    {
                        if ((instruments[instrument].middle30ma50 > instruments[instrument].top30bb + spikeN
                                || IsBetweenVariance(instruments[instrument].middle30ma50, instruments[instrument].top30bb + spikeN, (decimal).0015))
                            && instruments[instrument].middle30ma50 > (instruments[instrument].middle30BB + variance14))
                        {
                            if (instruments[instrument].movementb2 >= 2 || instruments[instrument].movementb3 >= 3)
                                spike = instruments[instrument].isNarrowed > 0 ? spikeV : instruments[instrument].movementb3 >= 3 ? spikeMN : spikeM;
                            else if (instruments[instrument].movements2 >= 2 || instruments[instrument].movements3 >= 3)
                                spike = instruments[instrument].isNarrowed > 0 ? spikeV + spikeN : spikeV;
                            else if (instruments[instrument].isNarrowed > 0)
                            {
                                spike = instruments[instrument].isNarrowed >= 3 ? spikeMN : spikeMM;
                            }
                            else
                                spike = spikeN;
                            qualified = ltp <= instruments[instrument].bot30bb
                                    && (spike <= (instruments[instrument].middleBB - ltp)
                                        || IsBetweenVariance(spike, (instruments[instrument].middleBB - ltp), (decimal).06));
                        }
                    }
                    else if (qualified
                        && ((instruments[instrument].botBB + variance14) > instruments[instrument].topBB
                            || IsBetweenVariance((instruments[instrument].botBB + variance14), instruments[instrument].topBB, (decimal).0006))
                        && (instruments[instrument].bot30bb + variance17) < instruments[instrument].top30bb
                        && IsBetweenVariance(ltp, instruments[instrument].bot30bb, (decimal).005))
                    {
                        if (IsBetweenVariance(ltp, instruments[instrument].botBB, (decimal).005)
                            || spike > (instruments[instrument].middleBB - ltp))
                        {
                            qualified = false;
                        }
                    }
                    if (qualified)
                    {
                        if (Decimal.Compare(timenow, Convert.ToDecimal(10.15)) < 0
                            && (instruments[instrument].movement >= 2
                                || instruments[instrument].movementb2 >= 4))
                        {
                            if (ltp > instruments[instrument].botBB
                                    || IsBetweenVariance(ltp, instruments[instrument].botBB, (decimal).004))
                            {
                                qualified = false;
                            }
                        }
                        if (IsBetweenVariance(instruments[instrument].bot30bb, instruments[instrument].middle30ma50, (decimal).0006))
                        {
                            if (ltp > instruments[instrument].bot30bb || ltp > instruments[instrument].middle30ma50)
                            {
                                qualified = false;
                            }
                        }
                        qualified = qualified ? ValidatingCurrentTrend(instrument, tickData, OType.Buy, timenow) : qualified;
                        if (instruments[instrument].oldTime != instruments[instrument].currentTime && qualified)
                        {
                            instruments[instrument].oldTime = instruments[instrument].currentTime;
                            Console.WriteLine("{0} Qualified:: For script {1}, As market is volatile, script has chosen bottom Buy Entry at {2} & bot30bb {3} with spike range {4}", DateTime.Now.ToString(), instruments[instrument].futName, ltp, instruments[instrument].bot30bb, spike);
                        }
                        else if (instruments[instrument].oldTime != instruments[instrument].currentTime && !qualified)
                        {
                            instruments[instrument].oldTime = instruments[instrument].currentTime;
                            Console.WriteLine("{0} Disqualified:: For script {1}, As Validation is failed for this script as bottom Buy Entry at {2} & bot30bb {3} with spike range {4}", DateTime.Now.ToString(), instruments[instrument].futName, ltp, instruments[instrument].bot30bb, spike);
                        }
                        if (qualified && !VerifyVolume(instrument, volume, timenow))
                        {
                            decimal percent = (decimal)1.6;
                            if (Decimal.Compare(timenow, Convert.ToDecimal(11.01)) > 0
                                && volume < instruments[instrument].AvgVolume / percent
                                && spike >= spikeM)
                            {
                                //Go ahead and proceed to order
                            }
                            else
                            {
                                qualified = false;
                                Console.WriteLine("(Cacelling) But Disqualified Volume at Time {0} for Instrument {1} current Volume is {2} and average volume is {3}", DateTime.Now.ToString(), instruments[instrument].futName, volume, instruments[instrument].AvgVolume);
                                if (spikeVM <= (instruments[instrument].middleBB - ltp)
                                        && Decimal.Compare(timenow, Convert.ToDecimal(10.35)) > 0)
                                {
                                    Console.WriteLine("(Cacelling) Another Worth a try.. but closing now");
                                    CloseOrderTicker(instrument, true);
                                    //qualified = true;
                                }
                                else if (spikeM >= (instruments[instrument].middleBB - ltp)
                                    && VerifyHighVolume(instrument, volume, timenow))
                                {
                                    Console.WriteLine("Cautious 4 *** Qualified again for Reverse SELL order with Volume at Time {0} for Instrument {1} current Volume is {2} and average volume is {3}", DateTime.Now.ToString(), instruments[instrument].futName, volume, instruments[instrument].AvgVolume);
                                    CloseOrderTicker(instrument, true);
                                }
                            }
                        }
                        if (qualified)
                        {
                            instruments[instrument].type = OType.Buy;
                            instruments[instrument].longTrigger = instruments[instrument].bot30bb;
                            modifyOpenAlignOrReversedStatus(instruments[instrument], 16, OType.Buy, false);
                            //CalculateBB((uint)instruments[instrument].instrumentId, tickData, OType.Buy);
                        }
                    }
                }
                if (qualified)
                {
                    decimal variance = variance17;
                    if (Decimal.Compare(timenow, Convert.ToDecimal(12.44)) > 0)
                    {
                        //variance = variance2;
                    }
                    if ((instruments[instrument].bot30bb + variance) > instruments[instrument].top30bb
                        || IsBetweenVariance((instruments[instrument].bot30bb + variance), instruments[instrument].top30bb, (decimal).0006))
                    {
                        qualified = false;
                        if (instruments[instrument].type == OType.Buy
                            && IsBetweenVariance(instruments[instrument].bot30bb, instruments[instrument].middle30ma50, (decimal).001)
                            && instruments[instrument].bot30bb > instruments[instrument].middle30ma50
                            && (instruments[instrument].bot30bb + variance14) < instruments[instrument].top30bb
                            && (IsBetweenVariance(ltp, instruments[instrument].middle30ma50, (decimal).0004)
                                || ltp <= instruments[instrument].middle30ma50))
                        {
                            Console.WriteLine("{0} Variance is lesser than expected range for script {1} But going for risky Buy Order", DateTime.Now.ToString(), instruments[instrument].futName);
                            qualified = true;
                        }
                        else if (instruments[instrument].type == OType.Sell
                            && IsBetweenVariance(instruments[instrument].top30bb, instruments[instrument].middle30ma50, (decimal).001)
                            && instruments[instrument].top30bb < instruments[instrument].middle30ma50
                            && (instruments[instrument].bot30bb + variance14) < instruments[instrument].top30bb
                            && (IsBetweenVariance(ltp, instruments[instrument].middle30ma50, (decimal).0004)
                                || ltp >= instruments[instrument].middle30ma50))
                        {
                            Console.WriteLine("{0} Variance is lesser than expected range for script {1} But going for risky Sell Order", DateTime.Now.ToString(), instruments[instrument].futName);
                            qualified = true;
                        }
                        else
                        {
                            if (instruments[instrument].oldTime != instruments[instrument].currentTime)
                            {
                                instruments[instrument].oldTime = instruments[instrument].currentTime;
                                Console.WriteLine("{0} INSIDER :: DisQualified for order {1} based on LTP {2} as top30BB {3} & bot30BB {4} are within minimum variance range", DateTime.Now.ToString(), instruments[instrument].futName, ltp, instruments[instrument].top30bb, instruments[instrument].bot30bb);
                            }
                            if (instruments[instrument].isReversed
                                && instruments[instrument].canTrust)
                            {
                                Console.WriteLine("{0} Bollinger Range is too low., for script {1} based on LTP {2} as top30BB {3} & bot30BB {4} Instead of Switching the Order Type", DateTime.Now.ToString(), instruments[instrument].futName, ltp, instruments[instrument].top30bb, instruments[instrument].bot30bb);
                                CloseOrderTicker(instrument, true);
                                
                                //if (instruments[instrument].type == OType.Buy)
                                //    instruments[instrument].type = OType.Sell;
                                //else if (instruments[instrument].type == OType.Sell)
                                //    instruments[instrument].type = OType.Buy;
                                
    }
}
                    }
                    if (qualified)
                    {
                        if (instruments[instrument].oldTime != instruments[instrument].currentTime)
                        {
                            instruments[instrument].oldTime = instruments[instrument].currentTime;
                            Console.WriteLine("{0} INSIDER :: Qualified for order {1} based on LTP {2} is ~ either near top30BB {3} or bot30BB {4}", DateTime.Now.ToString(), instruments[instrument].futName, ltp, instruments[instrument].topBB, instruments[instrument].bot30bb);
                        }
                        instruments[instrument].canTrust = false;
                    }
                }
                #endregion


        */

        /*
                        decimal spike;
                if (instruments[instrument].movement >= 2)
                {
                    spike = instruments[instrument].movementb2 >= 4 || instruments[instrument].movements2 >= 4 ? //True m >= 2
                        spikeVM : spikeV;
                    if ((instruments[instrument].movements3 <= 5
                                && instruments[instrument].movements3 >= 2
                                && instruments[instrument].movements2 >= 12)
                            || (instruments[instrument].movementb3 <= 5
                                && instruments[instrument].movementb3 >= 2
                                && instruments[instrument].movementb2 >= 12)
                            || (instruments[instrument].movements3 <= 5
                                && instruments[instrument].movements2 >= 18)
                            || (instruments[instrument].movementb3 <= 5
                                && instruments[instrument].movementb2 >= 18))
                    {
                        spike = spikeNN; //isNiftyVolatile ? spikeV : 
                    }
                    else if ((instruments[instrument].movements3 >= 8
                                && instruments[instrument].movements2 >= 12)
                            || (instruments[instrument].movementb3 >= 8
                                && instruments[instrument].movementb2 >= 12))
                    {
                        spike = spikeMM; //isNiftyVolatile? spikeV : 
                    }
                    else
                    {
                        if ((instruments[instrument].botBB + variance18) > instruments[instrument].topBB
                            && !(isNiftyVolatile || instruments[instrument].isVolatile))
                            spike = spikeM;
                        else if ((instruments[instrument].movementb2 >= 11 || instruments[instrument].movements2 >= 11)
                            && (instruments[instrument].movementb3 == instruments[instrument].movementb2
                                || instruments[instrument].movements3 >= instruments[instrument].movements2))
                        {
                            spike = spikeMN;
                        }
                    }
                }
                else if (instruments[instrument].movements2 >= 4 || instruments[instrument].movementb2 >= 4 || instruments[instrument].movements3 >= 3 || instruments[instrument].movementb3 >= 3) // false as m < 2
                {
                    if (instruments[instrument].movements3 >= 3 || instruments[instrument].movementb3 >= 3) // true m2 >= 4
                    {
                        if ((instruments[instrument].middle30BB < instruments[instrument].middle30ma50
                                && IsBeyondVariance(instruments[instrument].fBot30bb, instruments[instrument].bot30bb, (decimal).006))
                            || (instruments[instrument].middle30BB > instruments[instrument].middle30ma50
                                && IsBeyondVariance(instruments[instrument].fTop30bb, instruments[instrument].top30bb, (decimal).006)))
                            spike = spikeM;
                        else
                        {
                            spike = instruments[instrument].movements3 >= 6 || instruments[instrument].movementb3 >= 6 ? // true m2 >= 4
                                spikeVM :
                                spikeV;
                        }
                    }
                    else if (instruments[instrument].movements2 >= 7 || instruments[instrument].movementb2 >= 7)
                    {
                        spike = spikeMN; // true m2 >= 7
                    }
                    else
                        spike = spikeM;  // false m2 < 7
                    if ((instruments[instrument].movements3 <= 5
                            && instruments[instrument].movements3 >= 2
                            && instruments[instrument].movements2 >= 12)
                        || (instruments[instrument].movementb3 <= 5
                            && instruments[instrument].movementb3 >= 2
                            && instruments[instrument].movementb2 >= 12)
                        || (instruments[instrument].movements3 <= 5
                            && instruments[instrument].movements2 >= 18)
                        || (instruments[instrument].movementb3 <= 5
                            && instruments[instrument].movementb2 >= 18))
                    {
                        if ((instruments[instrument].botBB + variance17) > instruments[instrument].topBB)
                            spike = spikeM;
                        else
                            spike = spikeNN;
                    }
                    else if ((instruments[instrument].movements3 >= 8
                                && instruments[instrument].movements2 >= 12)
                             || (instruments[instrument].movementb3 >= 8
                                && instruments[instrument].movementb2 >= 12))
                    {
                        if ((instruments[instrument].bot30bb + variance23) < instruments[instrument].top30bb)
                            spike = spikeMM;
                        else
                            spike = spikeMN;
                    }
                    if (!(instruments[instrument].isVolatile || isNiftyVolatile)
                        && (instruments[instrument].bot30bb > ltp
                                && IsBeyondVariance(instruments[instrument].bot30bb, ltp, (decimal).003)
                            || (instruments[instrument].top30bb < ltp
                                && IsBeyondVariance(instruments[instrument].top30bb, ltp, (decimal).003))))
                    {
                        spike = IsBetweenVariance(instruments[instrument].close, ltp, (decimal).01) ? spikeNV : spikeNN;
                    }
                }
                else
                {
                    if (IsBetweenVariance(instruments[instrument].close, ltp, (decimal).01)
                        || instruments[instrument].isNarrowed > 0)
                    {
                        if (ltp < 200)
                            spike = spikeMM;
                        else
                            spike = spikeNV;
                    }
                    else
                    {
                        if ((instruments[instrument].botBB + variance14) > instruments[instrument].topBB
                            || IsBetweenVariance((instruments[instrument].botBB + variance14), instruments[instrument].topBB, (decimal).0006)
                            || (instruments[instrument].bot30bb + variance23) > instruments[instrument].top30bb)
                        {
                            if (instruments[instrument].isVolatile || isNiftyVolatile)
                            {
                                spike = spikeMM;
                            }
                            else
                            {
                                spike = spikeNN;
                            }
                        }
                        else
                        {
                            if (ltp < 200)
                                spike = spikeNN;
                            else
                                spike = spikeN;
                        }
                    }
                }
                if (instruments[instrument].hasGeared)
                {
                    spike = spike > spikeMM ? spike : spikeMM;
                }
                if (Decimal.Compare(timenow, Convert.ToDecimal(10.35)) < 0)
                {
                    if (gearingStatus)
                    {
                        spike = spike > spikeMM ? spike : spikeVM;
                    }
                }
                else
                {
                    if (gearingStatus)
                    {
                        spike = spike > spikeMM ? spike : spikeMM;
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
                                previousDay.AddDays(-55), currentDay, "day");
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
                            List<decimal> todaym4 = GetMiddle30BBOf(dayHistory, 4);
                            List<decimal> todaym5 = GetMiddle30BBOf(dayHistory, 5);
                            List<decimal> todaym6 = GetMiddle30BBOf(dayHistory, 6);
                            List<decimal> todaym7 = GetMiddle30BBOf(dayHistory, 7);
                            List<decimal> todaym8 = GetMiddle30BBOf(dayHistory, 8);
                            List<decimal> todaym9 = GetMiddle30BBOf(dayHistory, 9);
                            List<decimal> todaym10 = GetMiddle30BBOf(dayHistory, 10);
                            List<decimal> todaym11 = GetMiddle30BBOf(dayHistory, 11);
                            List<decimal> todaym12 = GetMiddle30BBOf(dayHistory, 12);
                            List<decimal> todaym13 = GetMiddle30BBOf(dayHistory, 13);

                            List<decimal> lastCandle = GetMiddle30BBOf(minHistory, 0);
                            List<decimal> lastCandlem1 = GetMiddle30BBOf(minHistory, 1);
                            List<decimal> lastCandlem2 = GetMiddle30BBOf(minHistory, 2);

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
                                                   (decimal).02)) ? baseline + 1 : baseline - 1;
                                baseline = dayHistory[dayHistory.Count - 3].High >= todaym2[1]
                                           || (IsBetweenVariance(dayHistory[dayHistory.Count - 3].High, todaym2[1],
                                                   (decimal).004)
                                               && IsBeyondVariance(dayHistory[dayHistory.Count - 3].Low, todaym2[2],
                                                   (decimal).02)) ? baseline + 1 : baseline - 1;
                                if (baseline <= 0)
                                    baseline = baseline - 2;
                                baseline = dayHistory[dayHistory.Count - 4].High >= todaym3[1]
                                           || (IsBetweenVariance(dayHistory[dayHistory.Count - 4].High, todaym3[1],
                                                   (decimal).015)
                                               && IsBeyondVariance(dayHistory[dayHistory.Count - 4].Low, todaym3[2],
                                                   (decimal).02)) ? baseline + 1 : baseline - 1;
                                baseline = dayHistory[dayHistory.Count - 5].High >= todaym4[1]
                                           || (IsBetweenVariance(dayHistory[dayHistory.Count - 5].High, todaym4[1],
                                                   (decimal).015)
                                               && IsBeyondVariance(dayHistory[dayHistory.Count - 5].Low, todaym4[2],
                                                   (decimal).02)) ? baseline + 1 : baseline >= 3? baseline : baseline - 1;
                                baseline = dayHistory[dayHistory.Count - 5].High >= todaym5[1]
                                           || (IsBetweenVariance(dayHistory[dayHistory.Count - 5].High, todaym5[1],
                                                   (decimal).015)
                                               && IsBeyondVariance(dayHistory[dayHistory.Count - 5].Low, todaym5[2],
                                                   (decimal).02)) ? baseline + 1 : baseline >= 3 ? baseline : baseline - 1;
                                baseline2 = minHistory[minHistory.Count - 1].High >= lastCandle[1]
                                    || IsBetweenVariance(minHistory[minHistory.Count - 1].High, lastCandle[1],
                                            (decimal).006) ? baseline2 + 1 : baseline2;
                                baseline2 = minHistory[minHistory.Count - 2].High >= lastCandlem1[1]
                                    || IsBetweenVariance(minHistory[minHistory.Count - 2].High, lastCandlem1[1],
                                            (decimal).006) ? baseline2 + 1 : baseline2;
                                baseline2 = minHistory[minHistory.Count - 3].High >= lastCandlem2[1]
                                    || IsBetweenVariance(minHistory[minHistory.Count - 3].High, lastCandlem2[1],
                                            (decimal).008) ? baseline2 + 1 : baseline2;
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
                                                   (decimal).02)) ? baseline + 1 : baseline - 1;
                                baseline = dayHistory[dayHistory.Count - 3].Low <= todaym2[2]
                                           || (IsBetweenVariance(dayHistory[dayHistory.Count - 3].Low, todaym2[2],
                                                   (decimal).004)
                                               && IsBeyondVariance(dayHistory[dayHistory.Count - 3].High, todaym2[1],
                                                   (decimal).02)) ? baseline + 1 : baseline - 1;
                                if (baseline <= 0)
                                    baseline = baseline - 2;
                                baseline = dayHistory[dayHistory.Count - 4].Low <= todaym3[2]
                                           || (IsBetweenVariance(dayHistory[dayHistory.Count - 4].Low, todaym3[2],
                                                   (decimal).02)
                                               && IsBeyondVariance(dayHistory[dayHistory.Count - 4].High, todaym3[1],
                                                   (decimal).02)) ? baseline + 1 : baseline - 1;
                                baseline = dayHistory[dayHistory.Count - 5].Low <= todaym4[2]
                                           || (IsBetweenVariance(dayHistory[dayHistory.Count - 5].Low, todaym4[2],
                                                   (decimal).02)
                                               && IsBeyondVariance(dayHistory[dayHistory.Count - 5].High, todaym4[1],
                                                   (decimal).02)) ? baseline + 1 : baseline >= 3 ? baseline : baseline - 1;
                                baseline = dayHistory[dayHistory.Count - 5].Low <= todaym5[2]
                                           || (IsBetweenVariance(dayHistory[dayHistory.Count - 5].Low, todaym5[2],
                                                   (decimal).02)
                                               && IsBeyondVariance(dayHistory[dayHistory.Count - 5].High, todaym5[1],
                                                   (decimal).02)) ? baseline + 1 : baseline >= 3 ? baseline : baseline - 1;
                                baseline2 = minHistory[minHistory.Count - 1].Low <= lastCandle[2]
                                    || IsBetweenVariance(minHistory[minHistory.Count - 1].Low, lastCandle[2],
                                            (decimal).006) ? baseline2 + 1 : baseline2;
                                baseline2 = minHistory[minHistory.Count - 2].Low <= lastCandlem1[2]
                                    || IsBetweenVariance(minHistory[minHistory.Count - 2].Low, lastCandlem1[2],
                                            (decimal).006) ? baseline2 + 1 : baseline2;
                                baseline2 = minHistory[minHistory.Count - 3].Low <= lastCandlem2[2]
                                    || IsBetweenVariance(minHistory[minHistory.Count - 3].Low, lastCandlem2[2],
                                            (decimal).008) ? baseline2 + 1 : baseline2;
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
        */
    }

}
