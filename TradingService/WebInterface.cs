using System;
using System.IO;
using System.Threading;
using System.Configuration;

using KiteConnect;

using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;

namespace TradingService
{
    #region WebInterface
    public class KiteInterface
    {
        #region Static Methods
        public static KiteInterface GetInstance
        {
            get
            {
                if (kiteInterface == null)
                {
                    kiteInterface = new KiteInterface();
                }
                return kiteInterface;
            }
        }
        private static KiteInterface kiteInterface = null;

        public string myApiKey = null, myAccessToken = null;
        public IWebDriver _driver = null;
        public Kite _kite = null;
        #endregion

        public void StartWebSessionToken()
        {
            myApiKey = ConfigurationManager.AppSettings["apikey"];
            string secretKey = ConfigurationManager.AppSettings["secretkey"];

            _kite = new Kite(myApiKey, Debug: false);

            //lastWrite = File.GetLastWriteTime(ConfigurationManager.AppSettings["inputFile"]);
            string requestToken = string.Empty;
            string username = ConfigurationManager.AppSettings["UserID"];
            string password = ConfigurationManager.AppSettings["Password"];
            string TwoFA = ConfigurationManager.AppSettings["2FA"];
            string connect = _kite.GetLoginURL();

            //FirefoxOptions firefoxOptions = new FirefoxOptions();
            //IWebDriver driver = new FirefoxDriver(new Uri(connect), firefoxOptions);
            //driver.Navigate().GoToUrl(connect);

            //InternetExplorerDriver Options = new InternetExplorerDriver();
            ChromeOptions Options = new ChromeOptions();
            Options.AddArgument("disable-infobars");
            Options.AddArguments("--start-maximized");
            ChromeDriverService svc = null;
            if (System.IO.File.Exists(System.IO.Directory.GetCurrentDirectory() + "\\chromedriver.Exe"))
            {
                svc = ChromeDriverService.CreateDefaultService(System.IO.Directory.GetCurrentDirectory());
            }
            else
            {
                if (System.IO.Directory.Exists("C:\\"))
                {
                    svc = ChromeDriverService.CreateDefaultService("C:\\");
                }
                else
                    throw new Exception("Selenium ChromeDriver.EXE is not found in current folder : " + System.IO.Directory.GetCurrentDirectory());
            }
            //IWebDriver driver = new RemoteWebDriver(new Uri(connect), Options.ToCapabilities(), TimeSpan.FromSeconds(600));

            while (_driver == null)
            {
                try
                {
                    _driver = new ChromeDriver(svc, Options);
                }
                catch (Exception ex)
                {
                    throw new Exception("Initiating ChromeDriver has thrown exception : " + ex.Message);
                }
            }
            _driver.Navigate().GoToUrl(connect);
            try
            {
                _driver.Manage().Window.Maximize();
                _driver.Navigate().GoToUrl(connect);
                Thread.Sleep(2000);

                _driver.FindElement(OpenQA.Selenium.By.XPath("//input[@id='password']")).Click();
                _driver.FindElement(OpenQA.Selenium.By.XPath("//input[@id='userid']")).SendKeys(username);
                _driver.FindElement(OpenQA.Selenium.By.XPath("//input[@id='password']")).SendKeys(password);
                _driver.FindElement(OpenQA.Selenium.By.XPath("//button[contains(text(),'Login')]")).Click();
                Thread.Sleep(5000);

                _driver.FindElement(OpenQA.Selenium.By.XPath("//input[@label='PIN']")).Click();
                _driver.FindElement(OpenQA.Selenium.By.XPath("//input[@label='PIN']")).SendKeys(TwoFA);
                _driver.FindElement(OpenQA.Selenium.By.XPath("//button[contains(text(),'Continue')]")).Click();
                Thread.Sleep(15000);

                string url = _driver.Url;
                url = url.Substring(url.IndexOf("request_token=") + "request_token=".Length);
                if (url.Contains("&"))
                    requestToken = url.Substring(0, url.IndexOf("&"));
                else
                    requestToken = url;
            }
            catch (Exception ex)
            {
                Console.WriteLine("Exception During Kite Sign in; {0}", ex.Message);
            }
            if (requestToken.Length == 0)
                Console.WriteLine("Could not find the new Request Token from web browser");
            else
            {
                User user = _kite.GenerateSession(requestToken, secretKey);
                _kite.SetAccessToken(user.AccessToken);
                myAccessToken = user.AccessToken;
            }
        }

        public string GetRequestToken()
        {
            string url;
            if (_driver == null || _driver.Url == null || _driver.Url.Length == 0)
            {
                StartWebSessionToken();
                url = _driver.Url;
            }
            else
                url = _driver.Url;
            url = url.Substring(url.IndexOf("request_token=") + "request_token=".Length);
            if (url.Contains("&"))
                url = url.Substring(0, url.IndexOf("&"));
            return url;
        }
    }
    #endregion
}
