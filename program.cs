// MADE BY DAIMY

using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using HtmlAgilityPack;
using System.Linq;
using System.Diagnostics;
using System.Net;
using System.Text.RegularExpressions;
using System.Collections.Generic;
using System.Text;

public class URLTool
{
    private static readonly HttpClient client = new HttpClient(new HttpClientHandler
    {
        AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
    })
    {
        Timeout = TimeSpan.FromMinutes(2)
    };

    #region Website Scraping
    
    public static async Task ScrapeWebsiteAsync(string url)
    {
        try
        {
            var uri = new Uri(url);
            var siteName = uri.Host.Replace(".", "_"); 
            string outputDirectory = Path.Combine(Directory.GetCurrentDirectory(), siteName);

            Directory.CreateDirectory(outputDirectory);

            Console.WriteLine($"\nStarting to scrape: {url}");
            Console.WriteLine($"Saving to folder: {outputDirectory}");
            
            client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
            var response = await client.GetAsync(url);
            response.EnsureSuccessStatusCode();
            var html = await response.Content.ReadAsStringAsync();
            
            if (string.IsNullOrEmpty(html))
            {
                Console.WriteLine("No HTML content found on the page.");
                return;
            }

            var htmlPath = Path.Combine(outputDirectory, "index.html");
            File.WriteAllText(htmlPath, html);
            Console.WriteLine($"HTML saved to {htmlPath}");
            
            var doc = new HtmlDocument();
            doc.LoadHtml(html);
            
            var cssFiles = doc.DocumentNode.Descendants("link")
                .Where(n => n.GetAttributeValue("rel", "").ToLower() == "stylesheet")
                .Select(n => n.GetAttributeValue("href", ""))
                .Where(url => !string.IsNullOrWhiteSpace(url))
                .Distinct()
                .ToList();
                
            foreach (var cssFile in cssFiles)
            {
                await DownloadAssetWithPathPreservationAsync(url, cssFile, outputDirectory);
            }
            
            var jsFiles = doc.DocumentNode.Descendants("script")
                .Select(n => n.GetAttributeValue("src", ""))
                .Where(url => !string.IsNullOrWhiteSpace(url))
                .Distinct()
                .ToList();
                
            foreach (var jsFile in jsFiles)
            {
                await DownloadAssetWithPathPreservationAsync(url, jsFile, outputDirectory);
            }
            
            var imageFiles = doc.DocumentNode.Descendants("img")
                .Select(n => n.GetAttributeValue("src", ""))
                .Where(url => !string.IsNullOrWhiteSpace(url))
                .Distinct()
                .ToList();
                
            foreach (var imageFile in imageFiles)
            {
                await DownloadAssetWithPathPreservationAsync(url, imageFile, outputDirectory);
            }

            Console.WriteLine("\nWebsite scraping complete!");
            Console.WriteLine($"All assets saved to: {outputDirectory}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error during scraping: {ex.Message}");
            Console.WriteLine($"Stack Trace: {ex.StackTrace}");
        }
    }

    private static async Task DownloadAssetWithPathPreservationAsync(string baseUrl, string assetUrl, string outputDirectory)
    {
        try
        {
            Uri assetUri;
            if (Uri.TryCreate(assetUrl, UriKind.Absolute, out Uri absoluteUri))
            {
                assetUri = absoluteUri;
            }
            else
            {
                if (assetUrl.StartsWith("//"))
                {
                    var baseUri = new Uri(baseUrl);
                    assetUrl = $"{baseUri.Scheme}:{assetUrl}";
                    assetUri = new Uri(assetUrl);
                }
                else
                {
                    assetUri = new Uri(new Uri(baseUrl), assetUrl);
                }
            }

            if (assetUri.Scheme == Uri.UriSchemeHttp || assetUri.Scheme == Uri.UriSchemeHttps)
            {
                string relativePath = assetUri.AbsolutePath;
                if (relativePath.StartsWith("/"))
                {
                    relativePath = relativePath.Substring(1);
                }
                
                string fileName = Path.GetFileName(relativePath);
                string extension = Path.GetExtension(fileName);
                
                if (string.IsNullOrEmpty(extension))
                {
                    string assetType = DetermineAssetType(assetUrl);
                    fileName = $"{assetType}_{Math.Abs(assetUri.AbsoluteUri.GetHashCode())}.{GetDefaultExtension(assetType)}";
                    relativePath = Path.Combine(Path.GetDirectoryName(relativePath) ?? "", fileName);
                }
                
                relativePath = CleanFilePath(relativePath);
                
                var assetPath = Path.Combine(outputDirectory, relativePath);
                
                string assetDirectory = Path.GetDirectoryName(assetPath);
                if (!string.IsNullOrEmpty(assetDirectory) && !Directory.Exists(assetDirectory))
                {
                    Directory.CreateDirectory(assetDirectory);
                }

                try
                {
                    var assetData = await client.GetByteArrayAsync(assetUri);
                    await File.WriteAllBytesAsync(assetPath, assetData);
                    Console.WriteLine($"Downloaded: {relativePath}");
                }
                catch (HttpRequestException httpEx)
                {
                    Console.WriteLine($"HTTP error downloading {assetUri.AbsoluteUri}: {httpEx.Message}");
                }
            }
            else
            {
                Console.WriteLine($"Skipping non-HTTP asset: {assetUri.AbsoluteUri}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error downloading asset {assetUrl}: {ex.Message}");
        }
    }

    private static string DetermineAssetType(string url)
    {
        url = url.ToLower();
        if (url.Contains(".css") || url.Contains("stylesheet"))
            return "css";
        if (url.Contains(".js") || url.Contains("javascript"))
            return "js";
        if (url.Contains(".jpg") || url.Contains(".jpeg") || url.Contains(".png") || 
            url.Contains(".gif") || url.Contains(".svg") || url.Contains(".webp") || url.Contains("image"))
            return "image";
        return "asset";
    }

    private static string CleanFilePath(string filePath)
    {
        int queryIndex = filePath.IndexOf('?');
        if (queryIndex > 0)
        {
            filePath = filePath.Substring(0, queryIndex);
        }
        
        string invalidChars = Regex.Escape(new string(Path.GetInvalidFileNameChars().Concat(Path.GetInvalidPathChars()).Distinct().ToArray()));
        string invalidRegex = string.Format(@"[{0}]", invalidChars);
        return Regex.Replace(filePath, invalidRegex, "_");
    }

    private static string GetDefaultExtension(string assetType)
    {
        switch (assetType.ToLower())
        {
            case "css":
                return "css";
            case "js":
                return "js";
            case "image":
                return "jpg";
            default:
                return "txt";
        }
    }

    #endregion

    #region Website Monitoring
    
    public static async Task MonitorURLAsync(string url, int checkIntervalMinutes)
    {
        Console.WriteLine($"\nStarting website monitoring for: {url}");
        Console.WriteLine($"Check interval: Every {checkIntervalMinutes} minute(s)");
        Console.WriteLine("Press Ctrl+C to stop monitoring\n");
        
        var previousStatus = false;
        var statusChangedTime = DateTime.Now;
        
        while (true)
        {
            var status = await CheckWebsiteStatusAsync(url);
            var statusText = status ? "UP" : "DOWN";
            
            if (status != previousStatus)
            {
                statusChangedTime = DateTime.Now;
                Console.ForegroundColor = status ? ConsoleColor.Green : ConsoleColor.Red;
                Console.WriteLine($"[{DateTime.Now}] STATUS CHANGED: Website is now {statusText}");
                Console.ResetColor();
            }
            else
            {
                var uptime = DateTime.Now - statusChangedTime;
                Console.WriteLine($"[{DateTime.Now}] Website is {statusText} (Duration: {FormatTimeSpan(uptime)})");
            }
            
            previousStatus = status;

            await Task.Delay(TimeSpan.FromMinutes(checkIntervalMinutes));
        }
    }

    private static string FormatTimeSpan(TimeSpan timeSpan)
    {
        if (timeSpan.TotalDays >= 1)
            return $"{(int)timeSpan.TotalDays}d {timeSpan.Hours}h {timeSpan.Minutes}m";
        else if (timeSpan.TotalHours >= 1)
            return $"{(int)timeSpan.TotalHours}h {timeSpan.Minutes}m";
        else
            return $"{(int)timeSpan.TotalMinutes}m {timeSpan.Seconds}s";
    }

    private static async Task<bool> CheckWebsiteStatusAsync(string url)
    {
        try
        {
            var timeoutClient = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
            timeoutClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
            
            var response = await timeoutClient.GetAsync(url);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error checking URL {url}: {ex.Message}");
            return false;
        }
    }
    
    #endregion

    #region Content Analysis
    
    public static async Task AnalyzeContentAsync(string url)
    {
        try
        {
            Console.WriteLine("\nAnalyzing webpage content...");

            client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
            var html = await client.GetStringAsync(url);
            var doc = new HtmlDocument();
            doc.LoadHtml(html);

            var images = doc.DocumentNode.Descendants("img").ToList();
            var links = doc.DocumentNode.Descendants("a").ToList();
            var scripts = doc.DocumentNode.Descendants("script").ToList();
            var styles = doc.DocumentNode.Descendants("link")
                .Where(n => n.GetAttributeValue("rel", "").ToLower() == "stylesheet").ToList();
            var title = doc.DocumentNode.SelectSingleNode("//title")?.InnerText ?? "No title";
            var metaDescription = doc.DocumentNode.SelectSingleNode("//meta[@name='description']")?.GetAttributeValue("content", "No description") ?? "No description";
            
            var h1Count = doc.DocumentNode.Descendants("h1").Count();
            var h2Count = doc.DocumentNode.Descendants("h2").Count();
            var h3Count = doc.DocumentNode.Descendants("h3").Count();

            Console.WriteLine("\n==== Content Analysis ====");
            Console.WriteLine($"Title: {title}");
            Console.WriteLine($"Description: {metaDescription}");
            Console.WriteLine($"Images: {images.Count}");
            Console.WriteLine($"Links: {links.Count}");
            Console.WriteLine($"Scripts: {scripts.Count}");
            Console.WriteLine($"Stylesheets: {styles.Count}");
            Console.WriteLine($"Headers: {h1Count} H1, {h2Count} H2, {h3Count} H3");

            var baseUri = new Uri(url);
            var internalLinks = links.Count(l => {
                var href = l.GetAttributeValue("href", "");
                if (string.IsNullOrEmpty(href)) return false;
                
                try {
                    if (Uri.TryCreate(href, UriKind.Absolute, out Uri absoluteUri))
                        return absoluteUri.Host == baseUri.Host;
                    return true; 
                }
                catch { return false; }
            });
            
            var externalLinks = links.Count - internalLinks;
            Console.WriteLine($"Internal Links: {internalLinks}");
            Console.WriteLine($"External Links: {externalLinks}");
            
            Console.WriteLine($"HTML Size: {html.Length:N0} bytes");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error analyzing content: {ex.Message}");
        }
    }
    
    #endregion

    #region Response Time Tracking
    
    public static async Task TrackResponseTimeAsync(string url, int numTests = 5)
    {
        try
        {
            Console.WriteLine($"\nTracking response time for {url}");
            Console.WriteLine($"Running {numTests} tests...\n");
            
            var results = new List<long>();
            var stopwatch = new Stopwatch();
            
            for (int i = 0; i < numTests; i++)
            {
                client.DefaultRequestHeaders.Clear();
                client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
                client.DefaultRequestHeaders.Add("Cache-Control", "no-cache, no-store");
                client.DefaultRequestHeaders.Add("Pragma", "no-cache");
                
                stopwatch.Restart();
                
                var response = await client.GetAsync(url);
                
                stopwatch.Stop();
                var responseTime = stopwatch.ElapsedMilliseconds;
                results.Add(responseTime);
                
                Console.WriteLine($"Test {i+1}: Response time: {responseTime}ms (Status: {(int)response.StatusCode} {response.StatusCode})");
                
                await Task.Delay(1000);
            }
            
            var avg = results.Average();
            var min = results.Min();
            var max = results.Max();
            
            Console.WriteLine("\n==== Results ====");
            Console.WriteLine($"Average response time: {avg:F1}ms");
            Console.WriteLine($"Minimum response time: {min}ms");
            Console.WriteLine($"Maximum response time: {max}ms");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error tracking response time: {ex.Message}");
        }
    }
    
    #endregion

    #region Embed Code Generation
    
    public static async Task GenerateEmbedCodeAsync(string url)
    {
        try
        {
            Console.WriteLine($"\nGenerating embed code for: {url}");
            Console.WriteLine("Fetching metadata...");

            client.DefaultRequestHeaders.Clear();
            client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0");

            var response = await client.GetAsync(url);
            response.EnsureSuccessStatusCode();
            var html = await response.Content.ReadAsStringAsync();

            var doc = new HtmlDocument();
            doc.LoadHtml(html);

            string title = doc.DocumentNode.SelectSingleNode("//title")?.InnerText?.Trim() ?? "";
            string description = doc.DocumentNode.SelectSingleNode("//meta[@name='description']")?.GetAttributeValue("content", "") ?? "";

            string ogTitle = doc.DocumentNode.SelectSingleNode("//meta[@property='og:title']")?.GetAttributeValue("content", "") ?? "";
            string ogDescription = doc.DocumentNode.SelectSingleNode("//meta[@property='og:description']")?.GetAttributeValue("content", "") ?? "";
            string ogImage = doc.DocumentNode.SelectSingleNode("//meta[@property='og:image']")?.GetAttributeValue("content", "") ?? "";
            string ogSiteName = doc.DocumentNode.SelectSingleNode("//meta[@property='og:site_name']")?.GetAttributeValue("content", "") ?? "";

            title = !string.IsNullOrEmpty(ogTitle) ? ogTitle : title;
            description = !string.IsNullOrEmpty(ogDescription) ? ogDescription : description;

            if (string.IsNullOrEmpty(ogImage))
            {
                var images = doc.DocumentNode.Descendants("img")
                    .Where(i => !string.IsNullOrEmpty(i.GetAttributeValue("src", "")))
                    .ToList();

                if (images.Count > 0)
                {
                    var largestImage = images
                        .Where(i => int.TryParse(i.GetAttributeValue("width", "0"), out _) &&
                                    int.TryParse(i.GetAttributeValue("height", "0"), out _))
                        .OrderByDescending(i =>
                            int.Parse(i.GetAttributeValue("width", "0")) *
                            int.Parse(i.GetAttributeValue("height", "0")))
                        .FirstOrDefault();

                    ogImage = largestImage?.GetAttributeValue("src", "") ?? images.First().GetAttributeValue("src", "");
                }
                else
                {
                    var favicon = doc.DocumentNode.SelectSingleNode("//link[@rel='icon']")?.GetAttributeValue("href", "") ??
                                doc.DocumentNode.SelectSingleNode("//link[@rel='shortcut icon']")?.GetAttributeValue("href", "");

                    ogImage = favicon ?? "";
                }

                if (!string.IsNullOrEmpty(ogImage) && !ogImage.StartsWith("http"))
                {
                    Uri baseUri = new Uri(url);
                    ogImage = new Uri(baseUri, ogImage).AbsoluteUri;
                }
            }

            if (string.IsNullOrEmpty(ogSiteName))
            {
                Uri uri = new Uri(url);
                ogSiteName = uri.Host;
            }

            if (description.Length > 200)
            {
                description = description.Substring(0, 197) + "...";
            }

            StringBuilder embedHtml = new StringBuilder();
            embedHtml.AppendLine("<div class=\"embed-container\" style=\"border-left: 4px solid #2f3136; background-color: #2f3136; border-radius: 4px; max-width: 520px; padding: 8px 10px 8px 12px; margin: 5px 0;\">");

            embedHtml.AppendLine($"  <div class=\"embed-provider\" style=\"color: #8e9297; font-size: 0.75rem; margin-bottom: 5px;\">{WebUtility.HtmlEncode(ogSiteName)}</div>");

            if (!string.IsNullOrEmpty(title))
            {
                embedHtml.AppendLine($"  <div class=\"embed-title\" style=\"color: #00aff4; font-size: 1rem; font-weight: 600; margin-bottom: 8px;\">");
                embedHtml.AppendLine($"    <a href=\"{WebUtility.HtmlEncode(url)}\" style=\"color: #00aff4; text-decoration: none;\" target=\"_blank\">{WebUtility.HtmlEncode(title)}</a>");
                embedHtml.AppendLine("  </div>");
            }

            if (!string.IsNullOrEmpty(description))
            {
                embedHtml.AppendLine($"  <div class=\"embed-description\" style=\"color: #dcddde; font-size: 0.875rem; line-height: 1.3; margin-bottom: 10px;\">{WebUtility.HtmlEncode(description)}</div>");
            }

            if (!string.IsNullOrEmpty(ogImage))
            {
                embedHtml.AppendLine($"  <div class=\"embed-image\" style=\"max-width: 100%; margin-top: 8px;\">");
                embedHtml.AppendLine($"    <a href=\"{WebUtility.HtmlEncode(url)}\" target=\"_blank\">");
                embedHtml.AppendLine($"      <img src=\"{WebUtility.HtmlEncode(ogImage)}\" alt=\"{WebUtility.HtmlEncode(title)}\" style=\"border-radius: 4px; max-width: 100%; max-height: 300px;\" />");
                embedHtml.AppendLine("    </a>");
                embedHtml.AppendLine("  </div>");
            }

            embedHtml.AppendLine("</div>");

            string embedCode = embedHtml.ToString();

            string examplePage = $@"<!DOCTYPE html>
    <html lang=""en"">
    <head>
        <meta charset=""UTF-8"">
        <meta name=""viewport"" content=""width=device-width, initial-scale=1.0"">
        <title>{WebUtility.HtmlEncode(title)}</title>
        <!-- START COPYING HERE -->
        <meta property=""og:title"" content=""{WebUtility.HtmlEncode(title)}"" />
        <meta property=""og:description"" content=""{WebUtility.HtmlEncode(description)}"" />
        <meta property=""og:image"" content=""{WebUtility.HtmlEncode(ogImage)}"" />
        <meta property=""og:url"" content=""{WebUtility.HtmlEncode(url)}"" />
        <meta property=""og:site_name"" content=""{WebUtility.HtmlEncode(ogSiteName)}"" />
        <!-- END COPYING HERE -->
        <style>
            body {{ font-family: Arial, sans-serif; background-color: #36393f; color: white; padding: 20px; }}
        </style>
    </head>
    <body>
        <h1>Example Embed</h1>
        <p>This is how the embed will look when added to your webpage:</p>

        {embedCode}

        <div style=""margin-top: 20px; padding: 15px; background-color: #2f3136; border-radius: 5px;"">
            <h3>How to use:</h3>
            <p>Copy the HTML code above and paste it into your webpage where you want the embed to appear.</p>
        </div>
    </body>
    </html>";

            File.WriteAllText("embed_example.html", examplePage);
            Console.WriteLine($"Example page saved to: {Path.GetFullPath("embed_example.html")}");
            Console.WriteLine("\nYou can open the example file in your browser to see how the embed looks.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error generating embed code: {ex.Message}");
        }
    }
    
    #endregion

    #region Main Program
    
    public static async Task Main(string[] args)
    {
        try
        {
            Console.Clear();
            DisplayBanner();
            
            Console.WriteLine("Enter the URL to scrape or monitor (e.g., https://example.com):");
            string url = Console.ReadLine()?.Trim() ?? "";

            if (Uri.TryCreate(url, UriKind.Absolute, out Uri validatedUrl) && 
                (validatedUrl.Scheme == Uri.UriSchemeHttp || validatedUrl.Scheme == Uri.UriSchemeHttps))
            {
                while (true)
                {
                    DisplayMenu(url);
                    
                    if (!int.TryParse(Console.ReadLine(), out int action) || action < 1 || action > 6)
                    {
                        Console.WriteLine("Invalid option. Please enter a number between 1 and 6.");
                        continue;
                    }

                    switch (action)
                    {
                        case 1:
                            await ScrapeWebsiteAsync(url);
                            break;
                        case 2:
                            Console.Write("Enter the monitoring interval in minutes (e.g., 5): ");
                            if (!int.TryParse(Console.ReadLine(), out int interval) || interval < 1)
                            {
                                Console.WriteLine("Invalid interval. Using default of 5 minutes.");
                                interval = 5;
                            }
                            await MonitorURLAsync(url, interval);
                            break;
                        case 3:
                            await AnalyzeContentAsync(url);
                            break;
                        case 4:
                            await TrackResponseTimeAsync(url);
                            break;
                        case 5:
                            await GenerateEmbedCodeAsync(url);
                            break;
                        case 6:
                            Console.WriteLine("Exiting program. Goodbye!");
                            return;
                    }
                    
                    Console.WriteLine("\nPress any key to return to menu...");
                    Console.ReadKey();
                    Console.Clear();
                    DisplayBanner();
                    Console.WriteLine($"Current URL: {url}");
                }
            }
            else
            {
                Console.WriteLine("Invalid URL entered. Please restart the program and try again.");
                Console.WriteLine("Press any key to exit...");
                Console.ReadKey();
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"An unexpected error occurred: {ex.Message}");
            Console.WriteLine($"Stack Trace: {ex.StackTrace}");
            Console.WriteLine("\nPress any key to exit...");
            Console.ReadKey();
        }
    }

    private static void DisplayBanner()
    {
        Console.WriteLine(@"
╭───────────────────────────────────────╮
│               URL TOOL                │
│     A simple website utility tool     │
╰───────────────────────────────────────╯");
    }

    private static void DisplayMenu(string url)
    {
        Console.WriteLine("\n╭───────────────────────────────────────╮");
        Console.WriteLine("│             MAIN MENU                 │");
        Console.WriteLine("╰───────────────────────────────────────╯");
        Console.WriteLine("1) Scrape Website (Download HTML, CSS, JS, images)");
        Console.WriteLine("2) Monitor Website (Check every few minutes)");
        Console.WriteLine("3) Analyze Content (Count images, links, scripts)");
        Console.WriteLine("4) Track Response Time");
        Console.WriteLine("5) Generate Embed Code (Like Discord embeds)");
        Console.WriteLine("6) Exit");
        Console.Write("\nEnter your choice (1-6): ");
    }
    
    #endregion
}
