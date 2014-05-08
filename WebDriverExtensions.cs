using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using OpenQA.Selenium;

namespace SteamDocsScraper
{
    public static class Config
    {
        public static readonly TimeSpan ImplicitWait = new TimeSpan(0, 0, 0, 10);
        public static readonly TimeSpan NoWait = new TimeSpan(0, 0, 0, 0);
    }

    public static class WebElementExtensions
    {
        public static bool ElementIsPresent(this IWebDriver driver, By by)
        {
            var present = false;
            driver.Manage().Timeouts().ImplicitlyWait(Config.NoWait);
            try
            {
                present = driver.FindElement(by).Displayed;
            }
            catch (NoSuchElementException)
            {
            }
            driver.Manage().Timeouts().ImplicitlyWait(Config.ImplicitWait);
            return present;
        }
    }
}
