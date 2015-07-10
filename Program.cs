using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;
using OpenQA.Selenium.Support.UI;

namespace SteamDocsScraper
{
    class Program
    {
        static string directory = "docs";

        static bool signedIn = false;
        static int tries = 0;

        // Key is the URL, value is if it was already fetched.
        static Dictionary<string, bool> documentationLinks = new Dictionary<string, bool>();
        static List<string> cleanDocumentationLinks = new List<string>();

        static Dictionary<string, string> settings;

        static ChromeDriver driver { get; set; }

        static void Main()
        {
            if (!File.Exists("settings.json"))
            {
                throw new Exception("settings.json file doesn't exist.");
            }

            settings = JsonConvert.DeserializeObject<Dictionary<string, string>>(File.ReadAllText("settings.json"));

            if (string.IsNullOrWhiteSpace(settings["steamUsername"]) || string.IsNullOrWhiteSpace(settings["steamPassword"]))
            {
                throw new Exception("Please provide your Steam username and password in settings.json.");
            }

            var options = new ChromeOptions();
            options.AddArgument(string.Format("--user-data-dir={0}", Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "userdata")));
            options.AddArgument("--enable-file-cookies");
            options.AddArgument("--disable-cache");

            driver = new ChromeDriver(options);

            if (!Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            Array.ForEach(Directory.GetFiles(directory, "*.html", SearchOption.TopDirectoryOnly), File.Delete);

            driver.Navigate().GoToUrl("https://partner.steamgames.com/");

            if (driver.ElementIsPresent(By.ClassName("avatar")))
            {
                signedIn = true;
            }
            else
            {
                Login();
            }

            if (signedIn)
            {
                if (settings.Keys.Contains("predefinedDocs"))
                {
                    foreach (string predefined in settings["predefinedDocs"].Split(','))
                    {
                        var key = "https://partner.steamgames.com/documentation/" + predefined;

                        if (string.IsNullOrWhiteSpace(predefined) || documentationLinks.ContainsKey(key))
                        {
                            Console.WriteLine("Invalid or duplicate predefined doc: {0}", predefined);
                            continue;
                        }

                        cleanDocumentationLinks.Add(predefined);
                        documentationLinks.Add(key, false);
                    }
                }

                driver.Navigate().GoToUrl("https://partner.steamgames.com/home/steamworks");

                GetDocumentationLinks();

                AddFromSearchResults();

                FetchLinks();
            }

            driver.Quit();

            settings["predefinedDocs"] = string.Join(",", cleanDocumentationLinks);

            File.WriteAllText("settings.json", JsonConvert.SerializeObject(settings, Formatting.Indented));

            Console.WriteLine("Done.");
            Console.ReadLine();
        }

        static void Login()
        {
            new WebDriverWait(driver, TimeSpan.FromSeconds(10)).Until(ExpectedConditions.ElementIsVisible(By.Id("login_btn_signin")));

            var needsSteamGuard = driver.ElementIsPresent(By.Id("authcode"));

            if (needsSteamGuard)
            {
                var fieldEmailAuth = driver.FindElementById("authcode");
                fieldEmailAuth.Clear();

                Console.WriteLine("Please insert a Steam Guard code.");
                string steamGuard = Console.ReadLine();
                fieldEmailAuth.SendKeys(steamGuard);

                var friendlyName = driver.FindElementById("friendlyname");
                friendlyName.SendKeys("SteamDocsScraper");

                var submitButton = driver.FindElementByCssSelector("#auth_buttonset_entercode .leftbtn");

                if (!submitButton.Displayed)
                {
                    submitButton = driver.FindElementByCssSelector("#auth_buttonset_incorrectcode .leftbtn");
                }

                submitButton.Click();
            }
            else
            {
                var fieldAccountName = driver.FindElementById("steamAccountName");
                var fieldSteamPassword = driver.FindElementById("steamPassword");
                var buttonLogin = driver.FindElementById("login_btn_signin");

                fieldAccountName.Clear();
                fieldAccountName.SendKeys(settings["steamUsername"]);
                fieldSteamPassword.Clear();
                fieldSteamPassword.SendKeys(settings["steamPassword"]);

                if (driver.ElementIsPresent(By.Id("input_captcha")))
                {
                    var fieldCaptcha = driver.FindElementById("input_captcha");
                    fieldCaptcha.Clear();

                    Console.WriteLine("Please insert captcha.");
                    string captcha = Console.ReadLine();
                    fieldCaptcha.SendKeys(captcha);
                }

                buttonLogin.Click();
            }

            new WebDriverWait(driver, TimeSpan.FromSeconds(5)).Until(ExpectedConditions.ElementIsVisible(By.CssSelector("#success_continue_btn, .avatar")));

            if (driver.ElementIsPresent(By.Id("success_continue_btn")) || driver.ElementIsPresent(By.ClassName("avatar")))
            {
                signedIn = true;
            }
            else if (tries < 3)
            {
                tries++;
                Login();
            }
        }

        static void AddFromSearchResults()
        {
            if (settings.Keys.Contains("searchQueries"))
            {
                foreach (string query in settings["searchQueries"].Split(','))
                {
                    int start = 0;
                    do
                    {
                        string url = "https://partner.steamgames.com/documentation/search?query=" + query + "&start=" + start;
                        Console.WriteLine("Search: Navigating to {0}", url);
                        driver.Navigate().GoToUrl(url);
                        start += 10;
                    } while (GetDocumentationLinks());
                }
            }
        }

        static bool GetDocumentationLinks(string currentPage = "")
        {
            if (currentPage != "")
            {
                currentPage = "|" + currentPage + "$";
            }

            var links = driver.FindElementsByTagName("a");
            
            int allDocumentationLinks = 0;
            int newDocumentationLinks = 0;

            foreach (var link in links)
            {
                string href = "";
                if (link.GetAttribute("href") != null)
                {
                    href = link.GetAttribute("href");
                }

                // TODO: optimize this and remove double Regex.Replace
                if (Regex.IsMatch(href, "//partner.steamgames.com/documentation/(?:(?!search|mail" + currentPage + ").*)/?$"))
                {
                    href = Regex.Replace(href, "#.*", "");
                    href = Regex.Replace(href, "\\?l=[a-z]+$", "");

                    if (string.IsNullOrWhiteSpace(href) || documentationLinks.ContainsKey(href))
                    {
                        continue;
                    }

                    allDocumentationLinks += 1;
                    documentationLinks.Add(href, false);
                    newDocumentationLinks += 1;
                    Console.WriteLine("Found a link {0}", href);
                }
            }

            if (newDocumentationLinks > 0)
            {
                Console.WriteLine("Found {0} new links on this page.", newDocumentationLinks);
            }

            return allDocumentationLinks > 0;
        }

        static void FetchLinks()
        {
            IEnumerable<KeyValuePair<string, bool>> links;
            while ((links = documentationLinks.Where(l => l.Value == false).ToArray()).Any())
            {
                foreach (var link in links)
                {
                    string page = link.Key.Split('/').Last();

                    SaveDocumentation(link.Key);

                    GetDocumentationLinks(page);
                }
            }
        }

        static void SaveDocumentation(string link)
        {
            Console.WriteLine("Navigating to {0}", link);
            driver.Navigate().GoToUrl(link);

            string file = link.Split('/').Last(x => x != "");


            // API Overview page is showing if the docs page doesn't exist
            var isDefaultPage = (file != "api" && driver.ElementIsPresent(By.Id("landingWelcome")) && driver.FindElementById("landingWelcome").Text == "API overview");

            if (isDefaultPage)
            {
                Console.WriteLine("SaveDocumentation: Default page. URL: {0}", link);
                documentationLinks[link] = true;
                return;
            }

            cleanDocumentationLinks.Add(file);

            // Normal layout.
            var isAdminPage = driver.ElementIsPresent(By.ClassName("AdminPageContent"));

            // Some pages like https://partner.steamgames.com/documentation/mod_team use the old layout
            var isOldAdminPage = driver.ElementIsPresent(By.Id("leftAreaContainer"));

            IWebElement content;
            string html = "";

            if (isAdminPage)
            {
                content = driver.FindElementByClassName("AdminPageContent");
                html = content.GetAttribute("innerHTML");
                file += ".html";
            }
            else if (isOldAdminPage)
            {
                content = driver.FindElementById("leftAreaContainer");
                html = content.GetAttribute("innerHTML");
                file += ".html";
            }
            else
            {
                // Unknown content. Save to a file.
                Console.WriteLine("SaveDocumentation: Unknown content. URL: {0}", link);

                if (driver.ElementIsPresent(By.XPath("/html/body/pre")))
                {
                    // text/plain or something similar
                    content = driver.FindElementByXPath("/html/body/pre");
                    html = content.GetAttribute("innerHTML");
                }
                else
                {
                    // HTML files, hopefully. Let's hope you won't see HTML tags where you shouldn't.
                    html = driver.PageSource;
                }
            }

            // Remove values which would leak user's auth tokens etc.

            const string matchPattern = @"name: ""(token|token_secure|auth|steamid|webcookie)"", value: ""[A-Za-z0-9\[\]_\-\:]+""";
            const string replacementPattern = @"name: ""$1"", value: ""hunter2""";
            html = Regex.Replace(html, matchPattern, replacementPattern);

            Console.WriteLine("Saving {0}", file);
            File.WriteAllText(Path.Combine(directory, file), html);
            documentationLinks[link] = true;
        }
    }
}
