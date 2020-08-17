using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;
using OpenQA.Selenium.Support.UI;
using TidyManaged;

namespace SteamDocsScraper
{
    static class Program
    {
        class Settings
        {

            [JsonProperty(PropertyName = "predefinedDocs")]
            public List<string> PredefinedDocs { get; set; } = new List<string>();

            [JsonProperty(PropertyName = "searchQueries")]
            public List<string> SearchQueries { get; set; } = new List<string>();
        }

        private static string _docsDirectory;

        // Key is the URL, value is if it was already fetched.
        private static readonly Dictionary<string, bool> DocumentationLinks = new Dictionary<string, bool>();
        private static Settings _settings;
        private static ChromeDriver _chromeDriver;
        private static Regex _linkMatch;

        private static void Main()
        {
            Console.ResetColor();
            Console.Title = "Steam Documentation Scraper";

            _docsDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "docs");

            if (!File.Exists("settings.json"))
            {
                throw new Exception("settings.json file doesn't exist.");
            }

            _settings = JsonConvert.DeserializeObject<Settings>(File.ReadAllText("settings.json"));

            var options = new ChromeOptions();
            options.AddArgument($"--user-data-dir={Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "userdata")}");
            options.AddArgument("--enable-file-cookies");
            options.AddArgument("--disable-cache");

            _linkMatch = new Regex(@"//partner\.steamgames\.com/doc/(?<href>.+?)(?=#|\?|$)", RegexOptions.Compiled);

            if (Directory.Exists(_docsDirectory))
            {
                Console.WriteLine($"Deleting existing folder: {_docsDirectory}");
                Directory.Delete(_docsDirectory, true);
            }

            Directory.CreateDirectory(_docsDirectory);

            try
            {
                _chromeDriver = new ChromeDriver(options);

                Console.CancelKeyPress += delegate { _chromeDriver.Quit(); };

                _chromeDriver.Navigate().GoToUrl("https://partner.steamgames.com/");

                foreach (var key in _settings.PredefinedDocs)
                {
                    if (string.IsNullOrWhiteSpace(key) || DocumentationLinks.ContainsKey(key))
                    {
                        Console.WriteLine("Invalid or duplicate predefined doc: {0}", key);
                        continue;
                    }

                    DocumentationLinks.Add(key, false);
                }

                _chromeDriver.Navigate().GoToUrl("https://partner.steamgames.com/doc/home");

                GetDocumentationLinks();

                AddFromSearchResults();

                FetchLinks();
            }
            finally
            {
                _chromeDriver?.Quit();
            }

            File.WriteAllText("settings.json", JsonConvert.SerializeObject(_settings, Formatting.Indented));

            Console.WriteLine("Done.");
            Console.ReadKey();
        }

        private static void AddFromSearchResults()
        {
            foreach (var query in _settings.SearchQueries)
            {
                var start = 0;
                do
                {
                    var url = "https://partner.steamgames.com/doc?q=" + query + "&start=" + start;
                    Console.WriteLine($"> Searching {url}");
                    _chromeDriver.Navigate().GoToUrl(url);
                    start += 20;
                }
                while (GetDocumentationLinks());
            }
        }

        private static bool GetDocumentationLinks()
        {
            var links = _chromeDriver.FindElementsByTagName("a");

            foreach (var link in links)
            {
                string href;

                try
                {
                    href = link.GetAttribute("href") ?? string.Empty;
                }
                catch (WebDriverException e)
                {
                    Console.WriteLine(e.Message);
                    continue;
                }

                if (href.EndsWith("/steamvr") || href.Contains("STORE_BASE_URL"))
                {
                    continue;
                }

                var match = _linkMatch.Match(href);

                if (!match.Success)
                {
                    continue;
                }

                href = match.Groups["href"].Value;

                // Fix for some broken links
                href = href.Replace("%3Fbeta%3D1", "");

                if (string.IsNullOrWhiteSpace(href) || DocumentationLinks.ContainsKey(href))
                {
                    continue;
                }

                DocumentationLinks.Add(href, false);
                _settings.PredefinedDocs.Add(href);

                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($" > Found a link: {href}");
                Console.ResetColor();
            }

            return _chromeDriver.ElementIsPresent(By.ClassName("docSearchResultLink"));
        }

        private static void FetchLinks()
        {
            IEnumerable<KeyValuePair<string, bool>> links;
            while ((links = DocumentationLinks.Where(l => l.Value == false).ToArray()).Any())
            {
                foreach (var link in links)
                {
                    try
                    {
                        SaveDocumentation(link.Key);

                        GetDocumentationLinks();
                    }
                    catch (WebDriverException e)
                    {
                        Console.WriteLine(e.Message);
                    }
                }
            }
        }

        private static void SaveDocumentation(string link)
        {
            Console.WriteLine($"{Environment.NewLine}> Navigating to {link}");
            _chromeDriver.Navigate().GoToUrl("https://partner.steamgames.com/doc/" + link);

            var file = link;

            // Normal layout.
            var isAdminPage = _chromeDriver.ElementIsPresent(By.ClassName("documentation_bbcode"));

            IWebElement content = null;
            var html = string.Empty;

            if (isAdminPage)
            {
                _chromeDriver.ExecuteScript("(function(){ jQuery('.dynamiclink_youtubeviews').remove(); }());");

                content = _chromeDriver.FindElementByClassName("documentation_bbcode");
                html = content.GetAttribute("innerHTML");

                if (_chromeDriver.ElementIsPresent(By.ClassName("docPageTitle")))
                {
                    html = _chromeDriver.FindElementByClassName("docPageTitle").GetAttribute("innerHTML") + "\n" + html;
                }

                // Using stream because Document.FromString breaks encoding
                using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(html)))
                using (var doc = Document.FromStream(stream))
                {
                    doc.WrapAt = 0;
                    doc.CharacterEncoding = EncodingType.Utf8;
                    doc.InputCharacterEncoding = EncodingType.Utf8;
                    doc.OutputCharacterEncoding = EncodingType.Utf8;
                    doc.OutputBodyOnly = AutoBool.Yes;
                    doc.IndentBlockElements = AutoBool.Yes;
                    doc.IndentSpaces = 4;
                    doc.ShowWarnings = false;
                    doc.Quiet = true;
                    doc.CleanAndRepair();

                    using (var stream2 = new MemoryStream())
                    using (var streamReader = new StreamReader(stream2, Encoding.UTF8))
                    {
                        doc.Save(stream2);
                        stream2.Position = 0;
                        html = streamReader.ReadToEnd();
                    }
                }

                if (html.Contains("Welcome to Steamworks!"))
                {
                    Console.WriteLine(" > Does not exist");
                    DocumentationLinks[link] = true;
                    return;
                }

                file += ".html";
            }
            else
            {
                // Unknown content. Save to a file.
                Console.WriteLine(" > Unknown content");
            }

            if (content != null)
            {
                var images = _chromeDriver.FindElements(By.CssSelector("img"));

                foreach (var img in images)
                {
                    var imgLink = new Uri(img.GetAttribute("src"));

                    if (imgLink.AbsolutePath.StartsWith("/steamcommunity/public/images/avatars/"))
                    {
                        continue;
                    }

                    if (imgLink.Host == "img.youtube.com")
                    {
                        continue;
                    }

                    var imgFileName = imgLink.AbsolutePath.TrimStart(new char[] { '/' });

                    if (imgFileName.StartsWith("steamcommunity/public/images/steamworks_docs"))
                    {
                        imgFileName = imgFileName.Replace("steamcommunity/public/images/steamworks_docs", "public");
                    }

                    var imgFile = Path.Combine(_docsDirectory, imgFileName);

                    if (File.Exists(imgFile))
                    {
                        continue;
                    }

                    Console.WriteLine(" > Downloading image: {0}", imgFileName);

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

            file = file.Replace('\\', '/');

            if (Path.DirectorySeparatorChar != '/')
            {
                file = file.Replace('/', Path.DirectorySeparatorChar);
            }

            file = file.TrimStart(Path.DirectorySeparatorChar);
            file = Path.Combine(_docsDirectory, file);
            var folder = Path.GetDirectoryName(file);

            Console.WriteLine(file);

            if (!Directory.Exists(folder))
            {
                Directory.CreateDirectory(folder);
            }

            File.WriteAllText(file, html);
            DocumentationLinks[link] = true;

            Console.WriteLine(" > Saved");
        }
    }
}
