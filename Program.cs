﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;
using OpenQA.Selenium.Support.UI;
using Newtonsoft.Json;

namespace SteamDocsScraper
{
    class Program
    {
        static string directory = "docs";

        static bool signedIn = false;
        static int tries = 0;

        // Key is the URL, value is if it was already fetched.
        static Dictionary<string, bool> documentationLinks = new Dictionary<string, bool>();

        static Dictionary<string, string> settings;

        static ChromeDriver driver { get; set; }

        static void Main(string[] args)
        {
            if (!File.Exists("settings.json"))
            {
                throw new Exception("settings.json file doesn't exist.");
            }

            settings = JsonConvert.DeserializeObject<Dictionary<string, string>>(File.ReadAllText("settings.json"));

            if (!settings.Keys.Contains("steamUsername") || settings["steamUsername"].Trim() == "" || !settings.Keys.Contains("steamPassword") || settings["steamPassword"].Trim() == "")
            {
                throw new Exception("Please provide your Steam username and password in settings.json.");
            }

            driver = new ChromeDriver();

            if (!Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            Array.ForEach(Directory.GetFiles(directory), File.Delete);

            driver.Navigate().GoToUrl("https://partner.steamgames.com/");

            if (signedIn || driver.ElementIsPresent(By.ClassName("AdminPageContent")))
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
                        documentationLinks.Add("https://partner.steamgames.com/documentation/" + predefined, false);
                        GetDocumentationLinks(predefined);
                    }
                }

                AddFromSearchResults();

                driver.Navigate().GoToUrl("https://partner.steamgames.com/home/steamworks");

                GetDocumentationLinks();

                FetchLinks();
            }

            driver.Quit();

            Console.WriteLine("Done.");
            Console.ReadLine();
        }

        static void Login()
        {
            var fieldAccountName = driver.FindElementById("steamAccountName");
            var fieldSteamPassword = driver.FindElementById("steamPassword");
            var buttonLogin = driver.FindElementByName("login_button");

            fieldAccountName.Clear();
            fieldAccountName.SendKeys(settings["steamUsername"]);
            fieldSteamPassword.Clear();
            fieldSteamPassword.SendKeys(settings["steamPassword"]);

            if (driver.ElementIsPresent(By.Id("emailauth")))
            {
                var fieldEmailAuth = driver.FindElementById("emailauth");
                fieldEmailAuth.Clear();

                Console.WriteLine("Please insert a Steam Guard code.");
                string steamGuard = Console.ReadLine();
                fieldEmailAuth.SendKeys(steamGuard);
            }

            if (driver.ElementIsPresent(By.Id("input_captcha")))
            {
                var fieldCaptcha = driver.FindElementById("input_captcha");
                fieldCaptcha.Clear();

                Console.WriteLine("Please insert captcha.");
                string captcha = Console.ReadLine();
                fieldCaptcha.SendKeys(captcha);
            }

            buttonLogin.Click();

            System.Threading.Thread.Sleep(2000);

            if (driver.ElementIsPresent(By.ClassName("AdminPageContent")))
            {
                signedIn = true;
            }
            else
            {
                if (tries < 3)
                {
                    tries++;
                    Login();
                }
            }
        }

        static void AddFromSearchResults() {
            if (settings.Keys.Contains("searchQueries"))
            {
                foreach (string query in settings["searchQueries"].Split(','))
                {
                    int start = 0;
                    do {
                        string url = "https://partner.steamgames.com/documentation/search?query=" + query + "&start=" + start;
                        Console.WriteLine("Search: Navigating to {0}", url);
                        driver.Navigate().GoToUrl(url);
                        start += 10;
                    } while (GetDocumentationLinks("mail")[0] > 0);
                }
            }
        }

        static int[] GetDocumentationLinks(string currentPage = "")
        {
            if (currentPage != "")
            {
                currentPage = "(?!" + currentPage + ")";
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
                
                try
                {
                    if (Regex.IsMatch(href, "//partner.steamgames.com/documentation/(?:(?!search)" + currentPage + ".*)/?$"))
                    {
                        allDocumentationLinks += 1;
                        href = Regex.Replace(href, "#.*", "");
                        documentationLinks.Add(href, false);
                        newDocumentationLinks += 1;
                        Console.WriteLine("Found a link {0}", href);
                    }
                }
                catch (ArgumentException)
                {
                }
            }

            Console.WriteLine("{0} links, {1} new.", allDocumentationLinks, newDocumentationLinks);

            int[] retVal = { allDocumentationLinks, newDocumentationLinks };
            return retVal;
        }

        static void FetchLinks()
        {
            IEnumerable<KeyValuePair<string, bool>> links;
            while ((links = documentationLinks.Where(l => l.Value == false).ToArray()).Count() > 0)
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
            
            // Normal layout.
            var isAdminPage = driver.ElementIsPresent(By.ClassName("AdminPageContent"));

            // Some pages like https://partner.steamgames.com/documentation/mod_team use the old layout
            var isOldAdminPage = driver.ElementIsPresent(By.Id("leftAreaContainer"));

            IWebElement content;

            if (isAdminPage)
            {
                content = driver.FindElementByClassName("AdminPageContent");
            }
            else if (isOldAdminPage)
            {
                content = driver.FindElementById("leftAreaContainer");
            }
            else
            {
                // Unknown content. Ignore.
                Console.WriteLine("SaveDocumentation: Unknown content. URL: {0}", link);
                documentationLinks[link] = true;
                return;
            }

            Console.WriteLine("Saving {0}.html", file);
            File.WriteAllText(Path.Combine(directory, file + ".html"), content.GetAttribute("innerHTML"));
            documentationLinks[link] = true;
        }
    }
}
