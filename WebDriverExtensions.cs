using OpenQA.Selenium;

namespace SteamDocsScraper
{
    public static class WebElementExtensions
    {
        public static bool ElementIsPresent(this IWebDriver driver, By by)
        {
            try
            {
                return driver.FindElement(by).Displayed;
            }
            catch (NoSuchElementException)
            {
            }

            return false;
        }
    }
}
