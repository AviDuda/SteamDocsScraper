using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;
using OpenQA.Selenium.Support.UI;
using TidyManaged;

namespace SteamDocsScraper
{
    class Program
    {
        static string directory = "docs";
        static string directoryImgs;

        static bool signedIn = false;
        static int tries = 0;

        // Key is the URL, value is if it was already fetched.
        static Dictionary<string, bool> documentationLinks = new Dictionary<string, bool>();

        static Dictionary<string, string> settings;

        static ChromeDriver driver { get; set; }

        static Regex LinkMatch;

        static void Main()
        {
            Console.ResetColor();
            Console.Title = "Steam Documentation Scraper";

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

            LinkMatch = new Regex(@"//partner\.steamgames\.com/doc/(?<href>.+?)(?=#|\?|$)", RegexOptions.Compiled);

            directoryImgs = Path.Combine(directory, "images");

            if (!Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }
            else
            {
                Array.ForEach(Directory.GetFiles(directory, "*.html"), File.Delete);
            }

            if (!Directory.Exists(directoryImgs))
            {
                Directory.CreateDirectory(directoryImgs);
            }
            else
            {
                Array.ForEach(Directory.GetFiles(directoryImgs, "*.png", SearchOption.TopDirectoryOnly), File.Delete);
                Array.ForEach(Directory.GetFiles(directoryImgs, "*.jpg", SearchOption.TopDirectoryOnly), File.Delete);
                Array.ForEach(Directory.GetFiles(directoryImgs, "*.gif", SearchOption.TopDirectoryOnly), File.Delete);
            }

            try
            {
                driver = new ChromeDriver(options);

                Console.CancelKeyPress += delegate
                {
                    driver.Quit();
                };

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
                        foreach (string key in settings["predefinedDocs"].Split(','))
                        {
                            if (string.IsNullOrWhiteSpace(key) || documentationLinks.ContainsKey(key))
                            {
                                Console.WriteLine("Invalid or duplicate predefined doc: {0}", key);
                                continue;
                            }

                            documentationLinks.Add(key, false);
                        }
                    }

                    driver.Navigate().GoToUrl("https://partner.steamgames.com/doc/home");

                    GetDocumentationLinks();

                    AddFromSearchResults();

                    FetchLinks();
                }
            }
            finally
            {
                if (driver != null)
                {
                    driver.Quit();
                }
            }

            settings["predefinedDocs"] = string.Join(",", documentationLinks.Keys.OrderBy(x => x));

            File.WriteAllText("settings.json", JsonConvert.SerializeObject(settings, Formatting.Indented));

            Console.WriteLine("Done.");
            Console.ReadKey();
        }

        static void Login()
        {
            new WebDriverWait(driver, TimeSpan.FromSeconds(10)).Until(ExpectedConditions.ElementIsVisible(By.Id("login_btn_signin")));

            var needsSteamGuard = driver.ElementIsPresent(By.Id("authcode"));

            if (needsSteamGuard)
            {
                var friendlyName = driver.FindElementById("friendlyname");
                friendlyName.SendKeys("SteamDocsScraper");

                var fieldEmailAuth = driver.FindElementById("authcode");
                fieldEmailAuth.Clear();

                Console.Write("Please insert a Steam Guard code: ");
                string steamGuard = Console.ReadLine();
                fieldEmailAuth.SendKeys(steamGuard);

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

                    Console.Write("Please enter captcha: ");
                    string captcha = Console.ReadLine();
                    fieldCaptcha.SendKeys(captcha);
                }

                buttonLogin.Click();
            }

            try
            {
                new WebDriverWait(driver, TimeSpan.FromSeconds(5)).Until(ExpectedConditions.ElementIsVisible(By.CssSelector("#authcode_entry, #success_continue_btn, .avatar")));
            }
            catch (WebDriverTimeoutException)
            {
                // what
            }

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
                        string url = "https://partner.steamgames.com/doc?q=" + query + "&start=" + start;
                        Console.WriteLine("> Searching {0}", url);
                        driver.Navigate().GoToUrl(url);
                        start += 20;
                    } while (GetDocumentationLinks());
                }
            }
        }

        static bool GetDocumentationLinks()
        {
            var links = driver.FindElementsByTagName("a");
            
            foreach (var link in links)
            {
                var href = link.GetAttribute("href") ?? string.Empty;
                var match = LinkMatch.Match(href);

                if (match.Success)
                {
                    href = match.Groups["href"].Value;

                    if (string.IsNullOrWhiteSpace(href) || documentationLinks.ContainsKey(href))
                    {
                        continue;
                    }

                    documentationLinks.Add(href, false);

                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine(" > Found a link: {0}", href);
                    Console.ResetColor();
                }
            }
            
            return driver.ElementIsPresent(By.ClassName("docSearchResultLink"));
        }

        static void FetchLinks()
        {
            IEnumerable<KeyValuePair<string, bool>> links;
            while ((links = documentationLinks.Where(l => l.Value == false).ToArray()).Any())
            {
                foreach (var link in links)
                {
                    SaveDocumentation(link.Key);

                    GetDocumentationLinks();
                }
            }
        }

        static void SaveDocumentation(string link)
        {
            Console.WriteLine("{1}> Navigating to {0}", link, Environment.NewLine);
            driver.Navigate().GoToUrl("https://partner.steamgames.com/doc/" + link);
            
            var file = link;

            // Normal layout.
            var isAdminPage = driver.ElementIsPresent(By.ClassName("documentation_bbcode"));

            IWebElement content = null;
            string html = string.Empty;

            if (isAdminPage)
            {
                content = driver.FindElementByClassName("documentation_bbcode");
                html = content.GetAttribute("innerHTML");

                if (driver.ElementIsPresent(By.ClassName("docPageTitle")))
                {
                    html = driver.FindElementByClassName("docPageTitle").GetAttribute("innerHTML") + "\n" + html;
                }

                using (Document doc = Document.FromString(html))
                {
                    doc.WrapAt = 0;
                    doc.OutputBodyOnly = AutoBool.Yes;
                    doc.IndentBlockElements = AutoBool.Yes;
                    doc.IndentSpaces = 4;
                    doc.ShowWarnings = false;
                    doc.Quiet = true;
                    doc.CleanAndRepair();
                    html = doc.Save();
                }

                if (html.Contains("Welcome to Steamworks!"))
                {
                    Console.WriteLine(" > Does not exist");
                    documentationLinks[link] = true;
                    return;
                }

                file += ".html";
            }
            else
            {
                // Unknown content. Save to a file.
                Console.WriteLine(" > Unknown content");

#if false
                if (driver.ElementIsPresent(By.XPath("/html/body/pre")))
                {
                    // text/plain or something similar
                    content = driver.FindElementByXPath("/html/body/pre");
                    html = content.GetAttribute("innerHTML");
                    file += ".txt";
                }
                else
                {
                    // HTML files, hopefully. Let's hope you won't see HTML tags where you shouldn't.
                    html = driver.PageSource;
                    file += ".html";
                }
#endif
            }

            if (content != null)
            {
                var images = driver.FindElements(By.CssSelector("img"));

                foreach (var img in images)
                {
                    if (img.GetAttribute("class") == "avatar")
                    {
                        continue;
                    }

                    var imgLink = img.GetAttribute("src");
                    var imgFile = Path.Combine(directoryImgs, Path.GetFileName(imgLink));

                    var index = imgFile.IndexOf("?", StringComparison.Ordinal);
                    if (index != -1)
                    {
                        imgFile = imgFile.Substring(0, index);
                    }

                    if (File.Exists(imgFile))
                    {
                        continue;
                    }

                    Console.WriteLine(" > Downloading image: {0}", imgFile);

                    if (!Directory.Exists(Path.GetDirectoryName(imgFile)))
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(imgFile));
                    }

                    using (var client = new WebClient())
                    {
                        try
                        {
                            client.DownloadFile(imgLink, imgFile);
                        }
                        catch (Exception e)
                        {
                            Console.ForegroundColor = ConsoleColor.Yellow;
                            Console.WriteLine(e.Message);
                            Console.ResetColor();
                        }
                    }
                }
            }
            else
            {
                Console.WriteLine(" > Missing content");
            }

            // Remove values which would leak user's auth tokens etc.

            const string matchPattern = @"name: ""(token|token_secure|auth|steamid|webcookie)"", value: ""[A-Za-z0-9\[\]_\-\:]+""";
            const string replacementPattern = @"name: ""$1"", value: ""hunter2""";
            html = Regex.Replace(html, matchPattern, replacementPattern);
            html = html.TrimEnd() + Environment.NewLine;

            file = Path.Combine(directory, file);
            var folder = Path.GetDirectoryName(file);

            Console.WriteLine(folder);

            if (!Directory.Exists(folder))
            {
                Directory.CreateDirectory(folder);
            }

            File.WriteAllText(file, html);
            documentationLinks[link] = true;

            Console.WriteLine(" > Saved");
        }
    }
}
