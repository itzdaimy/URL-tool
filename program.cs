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
using System.Security.Cryptography;
using System.Threading;

public class URLTool
{
    private static readonly HttpClient client = new HttpClient(new HttpClientHandler
    {
        AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
    })
    {
        Timeout = TimeSpan.FromMinutes(2)
    };

    private static ConsoleColor primaryColor = ConsoleColor.Cyan;
    private static ConsoleColor secondaryColor = ConsoleColor.Yellow;
    private static ConsoleColor successColor = ConsoleColor.Green;
    private static ConsoleColor errorColor = ConsoleColor.Red;
    private static ConsoleColor highlightColor = ConsoleColor.Magenta;

    #region Website Scraping
    
    public static async Task ScrapeWebsiteAsync(string url)
    {
        try
        {
            PrintHeader("Website Scraper");
            
            var uri = new Uri(url);
            var siteName = uri.Host.Replace(".", "_"); 
            string outputDirectory = Path.Combine(Directory.GetCurrentDirectory(), siteName);

            Directory.CreateDirectory(outputDirectory);

            PrintColoredLine($"Starting to scrape: {url}", primaryColor);
            PrintColoredLine($"Saving to folder: {outputDirectory}", secondaryColor);
            
            client.DefaultRequestHeaders.Clear();
            client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
            
            PrintProgress("Downloading HTML content", 0);
            var response = await client.GetAsync(url);
            response.EnsureSuccessStatusCode();
            var html = await response.Content.ReadAsStringAsync();
            PrintProgress("Downloading HTML content", 100);
            
            if (string.IsNullOrEmpty(html))
            {
                PrintColoredLine("No HTML content found on the page.", errorColor);
                return;
            }

            var htmlPath = Path.Combine(outputDirectory, "index.html");
            File.WriteAllText(htmlPath, html);
            PrintColoredLine($"HTML saved to {htmlPath}", successColor);
            
            var doc = new HtmlDocument();
            doc.LoadHtml(html);
            
            var cssFiles = doc.DocumentNode.Descendants("link")
                .Where(n => n.GetAttributeValue("rel", "").ToLower() == "stylesheet")
                .Select(n => n.GetAttributeValue("href", ""))
                .Where(url => !string.IsNullOrWhiteSpace(url))
                .Distinct()
                .ToList();
            
            PrintColoredLine($"\nFound {cssFiles.Count} CSS files to download", secondaryColor);
            for (int i = 0; i < cssFiles.Count; i++)
            {
                PrintProgress($"Downloading CSS files", (i * 100) / cssFiles.Count);
                await DownloadAssetWithPathPreservationAsync(url, cssFiles[i], outputDirectory);
            }
            if (cssFiles.Count > 0) PrintProgress($"Downloading CSS files", 100);
            
            var jsFiles = doc.DocumentNode.Descendants("script")
                .Select(n => n.GetAttributeValue("src", ""))
                .Where(url => !string.IsNullOrWhiteSpace(url))
                .Distinct()
                .ToList();
            
            PrintColoredLine($"\nFound {jsFiles.Count} JavaScript files to download", secondaryColor);
            for (int i = 0; i < jsFiles.Count; i++)
            {
                PrintProgress($"Downloading JavaScript files", (i * 100) / jsFiles.Count);
                await DownloadAssetWithPathPreservationAsync(url, jsFiles[i], outputDirectory);
            }
            if (jsFiles.Count > 0) PrintProgress($"Downloading JavaScript files", 100);
            
            var imageFiles = doc.DocumentNode.Descendants("img")
                .Select(n => n.GetAttributeValue("src", ""))
                .Where(url => !string.IsNullOrWhiteSpace(url))
                .Distinct()
                .ToList();
            
            PrintColoredLine($"\nFound {imageFiles.Count} image files to download", secondaryColor);
            for (int i = 0; i < imageFiles.Count; i++)
            {
                PrintProgress($"Downloading image files", (i * 100) / imageFiles.Count);
                await DownloadAssetWithPathPreservationAsync(url, imageFiles[i], outputDirectory);
            }
            if (imageFiles.Count > 0) PrintProgress($"Downloading image files", 100);

            PrintColoredLine("\nWebsite scraping complete!", successColor);
            PrintColoredLine($"All assets saved to: {outputDirectory}", successColor);
        }
        catch (Exception ex)
        {
            PrintColoredLine($"Error during scraping: {ex.Message}", errorColor);
            if (ex.InnerException != null)
            {
                PrintColoredLine($"Inner Exception: {ex.InnerException.Message}", errorColor);
            }
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
                    Console.WriteLine($"  Downloaded: {relativePath}");
                }
                catch (HttpRequestException httpEx)
                {
                    PrintColoredLine($"  HTTP error downloading {assetUri.AbsoluteUri}: {httpEx.Message}", errorColor);
                }
            }
            else
            {
                PrintColoredLine($"  Skipping non-HTTP asset: {assetUri.AbsoluteUri}", errorColor);
            }
        }
        catch (Exception ex)
        {
            PrintColoredLine($"  Error downloading asset {assetUrl}: {ex.Message}", errorColor);
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
        PrintHeader("Website Monitoring");
        
        PrintColoredLine($"Starting website monitoring for: {url}", primaryColor);
        PrintColoredLine($"Check interval: Every {checkIntervalMinutes} minute(s)", secondaryColor);
        PrintColoredLine("Press Ctrl+C to stop monitoring\n", highlightColor);
        
        var previousStatus = false;
        var statusChangedTime = DateTime.Now;
        
        DrawBox("Monitoring Status", 60);
        
        while (true)
        {
            var status = await CheckWebsiteStatusAsync(url);
            var statusText = status ? "UP" : "DOWN";
            
            Console.SetCursorPosition(0, Console.CursorTop);
            Console.Write(new string(' ', Console.BufferWidth - 1));
            Console.SetCursorPosition(0, Console.CursorTop);
            
            if (status != previousStatus)
            {
                statusChangedTime = DateTime.Now;
                Console.ForegroundColor = status ? successColor : errorColor;
                Console.WriteLine($"[{DateTime.Now}] STATUS CHANGED: Website is now {statusText}");
                Console.ResetColor();
            }
            else
            {
                var uptime = DateTime.Now - statusChangedTime;
                Console.ForegroundColor = status ? successColor : errorColor;
                Console.Write($"[{DateTime.Now}] Website is {statusText} ");
                Console.ResetColor();
                Console.WriteLine($"(Duration: {FormatTimeSpan(uptime)})");
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
            PrintColoredLine($"Error checking URL {url}: {ex.Message}", errorColor);
            return false;
        }
    }
    
    #endregion

    #region Content Analysis
    
    public static async Task AnalyzeContentAsync(string url)
    {
        try
        {
            PrintHeader("Content Analysis");
            
            PrintColoredLine("Analyzing webpage content...", primaryColor);
            PrintProgress("Downloading webpage", 0);
            
            client.DefaultRequestHeaders.Clear();
            client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
            var html = await client.GetStringAsync(url);
            PrintProgress("Downloading webpage", 100);
            
            PrintProgress("Analyzing content", 0);
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
            var formCount = doc.DocumentNode.Descendants("form").Count();
            var buttonCount = doc.DocumentNode.Descendants("button").Count();
            var inputCount = doc.DocumentNode.Descendants("input").Count();
            
            PrintProgress("Analyzing content", 100);
            
            DrawBox("Content Analysis Results", 60);
            
            Console.WriteLine();
            PrintKeyValue("Title", title);
            PrintKeyValue("Description", metaDescription.Length > 50 ? metaDescription.Substring(0, 47) + "..." : metaDescription);
            Console.WriteLine();
            
            PrintKeyValue("Images", images.Count.ToString());
            PrintKeyValue("Links", links.Count.ToString());
            PrintKeyValue("Scripts", scripts.Count.ToString());
            PrintKeyValue("Stylesheets", styles.Count.ToString());
            Console.WriteLine();
            
            PrintKeyValue("H1 Headers", h1Count.ToString());
            PrintKeyValue("H2 Headers", h2Count.ToString());
            PrintKeyValue("H3 Headers", h3Count.ToString());
            Console.WriteLine();
            
            PrintKeyValue("Forms", formCount.ToString());
            PrintKeyValue("Buttons", buttonCount.ToString());
            PrintKeyValue("Input Fields", inputCount.ToString());
            Console.WriteLine();

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
            PrintKeyValue("Internal Links", internalLinks.ToString());
            PrintKeyValue("External Links", externalLinks.ToString());
            Console.WriteLine();
            
            PrintKeyValue("HTML Size", $"{html.Length:N0} bytes");
            Console.WriteLine();
            
            DrawBottomLine(60);
        }
        catch (Exception ex)
        {
            PrintColoredLine($"Error analyzing content: {ex.Message}", errorColor);
        }
    }
    
    #endregion

    #region Response Time Tracking
    
    public static async Task TrackResponseTimeAsync(string url, int numTests = 5)
    {
        try
        {
            PrintHeader("Response Time Tracking");
            
            PrintColoredLine($"Tracking response time for {url}", primaryColor);
            PrintColoredLine($"Running {numTests} tests...\n", secondaryColor);
            
            var results = new List<long>();
            var stopwatch = new Stopwatch();
            
            for (int i = 0; i < numTests; i++)
            {
                client.DefaultRequestHeaders.Clear();
                client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
                client.DefaultRequestHeaders.Add("Cache-Control", "no-cache, no-store");
                client.DefaultRequestHeaders.Add("Pragma", "no-cache");
                
                PrintProgress($"Running test {i+1}/{numTests}", i * 100 / numTests);
                
                stopwatch.Restart();
                var response = await client.GetAsync(url);
                stopwatch.Stop();
                
                var responseTime = stopwatch.ElapsedMilliseconds;
                results.Add(responseTime);
                
                Console.ForegroundColor = responseTime < 500 ? successColor : 
                                         responseTime < 1000 ? secondaryColor : errorColor;
                Console.Write($"Test {i+1}: Response time: {responseTime}ms ");
                Console.ResetColor();
                Console.WriteLine($"(Status: {(int)response.StatusCode} {response.StatusCode})");
                
                await Task.Delay(1000);
            }
            
            PrintProgress($"Running test {numTests}/{numTests}", 100);
            
            var avg = results.Average();
            var min = results.Min();
            var max = results.Max();
            
            DrawBox("Response Time Results", 60);
            Console.WriteLine();
            
            PrintKeyValue("Average response time", $"{avg:F1}ms");
            PrintKeyValue("Minimum response time", $"{min}ms");
            PrintKeyValue("Maximum response time", $"{max}ms");
            Console.WriteLine();
            
            string rating;
            ConsoleColor ratingColor;
            
            if (avg < 300)
            {
                rating = "Excellent";
                ratingColor = successColor;
            }
            else if (avg < 800)
            {
                rating = "Good";
                ratingColor = ConsoleColor.DarkGreen;
            }
            else if (avg < 1500)
            {
                rating = "Average";
                ratingColor = secondaryColor;
            }
            else if (avg < 3000)
            {
                rating = "Below Average";
                ratingColor = ConsoleColor.DarkYellow;
            }
            else
            {
                rating = "Poor";
                ratingColor = errorColor;
            }
            
            Console.Write("Performance Rating: ");
            Console.ForegroundColor = ratingColor;
            Console.WriteLine(rating);
            Console.ResetColor();
            Console.WriteLine();
            
            DrawBottomLine(60);
        }
        catch (Exception ex)
        {
            PrintColoredLine($"Error tracking response time: {ex.Message}", errorColor);
        }
    }
    
    #endregion

    #region Track Website Changes
    
    public static async Task TrackWebsiteChangesAsync(string url, int checkIntervalMinutes)
    {
        try
        {
            PrintHeader("Website Change Detector");
            
            PrintColoredLine($"Monitoring changes for: {url}", primaryColor);
            PrintColoredLine($"Check interval: Every {checkIntervalMinutes} minute(s)", secondaryColor);
            PrintColoredLine("Press Ctrl+C to stop monitoring\n", highlightColor);
            
            string changeDir = Path.Combine(Directory.GetCurrentDirectory(), "change_detection");
            Directory.CreateDirectory(changeDir);
            
            var siteHash = new Uri(url).Host.Replace(".", "_");
            string hashFilePath = Path.Combine(changeDir, $"{siteHash}_hashes.txt");
            
            Dictionary<string, string> previousHashes = new Dictionary<string, string>();
            
            if (File.Exists(hashFilePath))
            {
                var lines = File.ReadAllLines(hashFilePath);
                foreach (var line in lines)
                {
                    var parts = line.Split('=');
                    if (parts.Length == 2)
                    {
                        previousHashes[parts[0]] = parts[1];
                    }
                }
                PrintColoredLine("Loaded previous snapshot for comparison", secondaryColor);
            }
            else
            {
                PrintColoredLine("No previous snapshot found - creating baseline", secondaryColor);
            }
            
            DrawBox("Change Detection Log", 70);
            
            while (true)
            {
                try
                {
                    PrintColoredLine($"[{DateTime.Now}] Checking for changes...", primaryColor);
                    
                    client.DefaultRequestHeaders.Clear();
                    client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
                    client.DefaultRequestHeaders.Add("Cache-Control", "no-cache, no-store");
                    client.DefaultRequestHeaders.Add("Pragma", "no-cache");
                    
                    var response = await client.GetAsync(url);
                    var html = await response.Content.ReadAsStringAsync();
                    
                    var doc = new HtmlDocument();
                    doc.LoadHtml(html);
                    
                    Dictionary<string, string> currentHashes = new Dictionary<string, string>();
                    Dictionary<string, string> changedElements = new Dictionary<string, string>();
                    
                    currentHashes["FullPage"] = GetSha256Hash(html);
                    
                    var headContent = doc.DocumentNode.SelectSingleNode("//head")?.InnerHtml ?? "";
                    currentHashes["Head"] = GetSha256Hash(headContent);
                    
                    var bodyContent = doc.DocumentNode.SelectSingleNode("//body")?.InnerHtml ?? "";
                    currentHashes["Body"] = GetSha256Hash(bodyContent);
                    
                    var title = doc.DocumentNode.SelectSingleNode("//title")?.InnerText ?? "";
                    currentHashes["Title"] = GetSha256Hash(title);
                    
                    var mainContent = doc.DocumentNode.SelectSingleNode("//main")?.InnerHtml ?? "";
                    currentHashes["MainContent"] = GetSha256Hash(mainContent);
                    
                    var headerContent = doc.DocumentNode.SelectSingleNode("//header")?.InnerHtml ?? "";
                    currentHashes["Header"] = GetSha256Hash(headerContent);
                    
                    var footerContent = doc.DocumentNode.SelectSingleNode("//footer")?.InnerHtml ?? "";
                    currentHashes["Footer"] = GetSha256Hash(footerContent);
                    
                    var styleContent = string.Join("\n", doc.DocumentNode.Descendants("style").Select(n => n.InnerHtml));
                    currentHashes["Styles"] = GetSha256Hash(styleContent);
                    
                    var scriptContent = string.Join("\n", doc.DocumentNode.Descendants("script").Select(n => n.InnerHtml));
                    currentHashes["Scripts"] = GetSha256Hash(scriptContent);
                    
                    var imageRefs = string.Join("\n", doc.DocumentNode.Descendants("img").Select(n => n.GetAttributeValue("src", "")));
                    currentHashes["Images"] = GetSha256Hash(imageRefs);
                    
                    var metaTags = string.Join("\n", doc.DocumentNode.Descendants("meta").Select(n => n.OuterHtml));
                    currentHashes["MetaTags"] = GetSha256Hash(metaTags);
                    
                    var linkTags = string.Join("\n", doc.DocumentNode.Descendants("link").Select(n => n.OuterHtml));
                    currentHashes["LinkTags"] = GetSha256Hash(linkTags);
                    
                    bool hasChanges = false;
                    if (previousHashes.Count > 0)
                    {
                        foreach (var kvp in currentHashes)
                        {
                            if (previousHashes.TryGetValue(kvp.Key, out string previousHash))
                            {
                                if (previousHash != kvp.Value)
                                {
                                    changedElements[kvp.Key] = kvp.Value;
                                    hasChanges = true;
                                }
                            }
                            else
                            {
                                changedElements[kvp.Key] = kvp.Value;
                                hasChanges = true;
                            }
                        }
                    }
                    
                    if (previousHashes.Count == 0)
                    {
                        PrintColoredLine($"[{DateTime.Now}] Created baseline snapshot", successColor);
                        previousHashes = new Dictionary<string, string>(currentHashes);
                        
                        var hashLines = currentHashes.Select(kvp => $"{kvp.Key}={kvp.Value}").ToArray();
                        File.WriteAllLines(hashFilePath, hashLines);
                    }

                    else if (hasChanges)
                    {
                        PrintColoredLine($"[{DateTime.Now}] Changes detected!", highlightColor);
                        
                        string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                        string changeReport = Path.Combine(changeDir, $"{siteHash}_changes_{timestamp}.txt");
                        
                        using (StreamWriter writer = new StreamWriter(changeReport))
                        {
                            writer.WriteLine($"Changes detected on {url} at {DateTime.Now}");
                            writer.WriteLine("--------------------------------------------------");
                            
                            foreach (var change in changedElements)
                            {
                                writer.WriteLine($"Element modified: {change.Key}");
                                PrintColoredLine($"  - Modified: {change.Key}", errorColor);
                            }
                            
                            writer.WriteLine("\nChanges detected in the following elements:");
                            writer.WriteLine("--------------------------------------------------");
                            
                            if (changedElements.ContainsKey("Title"))
                            {
                                writer.WriteLine($"Title: {title}");
                            }
                            
                            if (changedElements.ContainsKey("FullPage"))
                            {
                                writer.WriteLine("Full page content changed");
                                
                                string htmlSnapshot = Path.Combine(changeDir, $"{siteHash}_snapshot_{timestamp}.html");
                                File.WriteAllText(htmlSnapshot, html);
                                
                                writer.WriteLine($"Full HTML snapshot saved to: {htmlSnapshot}");
                                PrintColoredLine($"  - HTML snapshot saved to: {Path.GetFileName(htmlSnapshot)}", secondaryColor);
                            }
                        }
                        
                        PrintColoredLine($"  - Change report saved to: {Path.GetFileName(changeReport)}", secondaryColor);
                        
                        previousHashes = new Dictionary<string, string>(currentHashes);
                        
                        var hashLines = currentHashes.Select(kvp => $"{kvp.Key}={kvp.Value}").ToArray();
                        File.WriteAllLines(hashFilePath, hashLines);
                    }
                    else
                    {
                        PrintColoredLine($"[{DateTime.Now}] No changes detected", successColor);
                    }
                }
                catch (Exception ex)
                {
                    PrintColoredLine($"Error checking for changes: {ex.Message}", errorColor);
                }
                
                Console.WriteLine(new string('-', 70));
                await Task.Delay(TimeSpan.FromMinutes(checkIntervalMinutes));
            }
        }
        catch (Exception ex)
        {
            PrintColoredLine($"Error in change detection: {ex.Message}", errorColor);
        }
    }
    
    private static string GetSha256Hash(string input)
    {
        if (string.IsNullOrEmpty(input)) return "EMPTY";
        
        using (SHA256 sha256Hash = SHA256.Create())
        {
            byte[] bytes = sha256Hash.ComputeHash(Encoding.UTF8.GetBytes(input));
            
            StringBuilder builder = new StringBuilder();
            for (int i = 0; i < bytes.Length; i++)
            {
                builder.Append(bytes[i].ToString("x2"));
            }
            return builder.ToString();
        }
    }
    
    #endregion

    #region Embed Code Generation
    
    public static async Task GenerateEmbedCodeAsync(string url)
    {
        try
        {
            PrintHeader("Embed Code Generator");
            
            PrintColoredLine($"Generating embed code for: {url}", primaryColor);
            PrintColoredLine("Fetching metadata...", secondaryColor);

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
            PrintColoredLine($"Example page saved to: {Path.GetFullPath("embed_example.html")}", successColor);
            PrintColoredLine("\nYou can open the example file in your browser to see how the embed looks.", secondaryColor);
        }
        catch (Exception ex)
        {
            PrintColoredLine($"Error generating embed code: {ex.Message}", errorColor);
        }
    }
    
    #endregion

    #region UI Helpers
    
    private static void PrintHeader(string title)
    {
        Console.Clear();
        
        int width = Math.Max(60, title.Length + 10);
        string padding = new string('═', width);
        
        Console.WriteLine();
        Console.ForegroundColor = primaryColor;
        Console.WriteLine($"╔{padding}╗");
        Console.WriteLine($"║{title.PadLeft(title.Length + (width - title.Length) / 2).PadRight(width)}║");
        Console.WriteLine($"╚{padding}╝");
        Console.ResetColor();
        Console.WriteLine();
    }
    
    private static void PrintColoredLine(string text, ConsoleColor color)
    {
        Console.ForegroundColor = color;
        Console.WriteLine(text);
        Console.ResetColor();
    }
    
    private static void PrintProgress(string text, int percentage)
    {
        int width = 30;
        int filledWidth = (int)Math.Floor(width * percentage / 100.0);
        
        Console.Write($"\r{text}: [");
        
        Console.ForegroundColor = primaryColor;
        Console.Write(new string('█', filledWidth));
        Console.ResetColor();
        
        Console.Write(new string('░', width - filledWidth));
        Console.Write($"] {percentage}%");
        
        if (percentage == 100)
            Console.WriteLine();
    }
    
    private static void PrintKeyValue(string key, string value)
    {
        Console.Write($"{key}: ");
        Console.ForegroundColor = secondaryColor;
        Console.WriteLine(value);
        Console.ResetColor();
    }
    
    private static void DrawBox(string title, int width)
    {
        Console.WriteLine(new string('─', width));
        Console.Write("┌─ ");
        Console.ForegroundColor = highlightColor;
        Console.Write(title);
        Console.ResetColor();
        Console.WriteLine($" {new string('─', width - title.Length - 4)}┐");
    }
    
    private static void DrawBottomLine(int width)
    {
        Console.WriteLine($"└{new string('─', width - 1)}┘");
    }
    
    #endregion

    #region Speed Optimizer Analysis

    public static async Task AnalyzeWebsitePerformanceAsync(string url)
    {
        try
        {
            PrintHeader("Speed Optimizer Analysis");
            
            PrintColoredLine($"Analyzing performance for: {url}", primaryColor);
            PrintColoredLine("This will identify performance bottlenecks and suggest optimizations.", secondaryColor);
            
            string perfDir = Path.Combine(Directory.GetCurrentDirectory(), "performance_analysis");
            Directory.CreateDirectory(perfDir);
            
            PrintProgress("Performing initial request", 0);
            
            var stopwatch = new Stopwatch();
            client.DefaultRequestHeaders.Clear();
            client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
            
            stopwatch.Start();
            var response = await client.GetAsync(url);
            var firstByteTime = stopwatch.ElapsedMilliseconds;
            
            var html = await response.Content.ReadAsStringAsync();
            var totalLoadTime = stopwatch.ElapsedMilliseconds;
            stopwatch.Stop();
            
            PrintProgress("Performing initial request", 100);
            
            PrintProgress("Analyzing resources", 0);
            var doc = new HtmlDocument();
            doc.LoadHtml(html);
            
            var cssFiles = doc.DocumentNode.Descendants("link")
                .Where(n => n.GetAttributeValue("rel", "").ToLower() == "stylesheet")
                .Select(n => n.GetAttributeValue("href", ""))
                .Where(url => !string.IsNullOrWhiteSpace(url))
                .Distinct()
                .ToList();
            
            var jsFiles = doc.DocumentNode.Descendants("script")
                .Select(n => n.GetAttributeValue("src", ""))
                .Where(url => !string.IsNullOrWhiteSpace(url))
                .Distinct()
                .ToList();
            
            var images = doc.DocumentNode.Descendants("img")
                .Select(n => new { 
                    Src = n.GetAttributeValue("src", ""),
                    Width = n.GetAttributeValue("width", ""),
                    Height = n.GetAttributeValue("height", ""),
                    Loading = n.GetAttributeValue("loading", "")
                })
                .Where(img => !string.IsNullOrWhiteSpace(img.Src))
                .ToList();
            
            PrintProgress("Analyzing resources", 50);
            
            var renderBlockingCss = doc.DocumentNode.Descendants("link")
                .Where(n => n.GetAttributeValue("rel", "").ToLower() == "stylesheet" && 
                    !n.GetAttributeValue("media", "").Contains("print") &&
                    string.IsNullOrEmpty(n.GetAttributeValue("async", "")) &&
                    string.IsNullOrEmpty(n.GetAttributeValue("defer", "")))
                .Select(n => n.GetAttributeValue("href", ""))
                .Where(url => !string.IsNullOrWhiteSpace(url))
                .Distinct()
                .ToList();
            
            var renderBlockingJs = doc.DocumentNode.Descendants("script")
                .Where(n => !string.IsNullOrEmpty(n.GetAttributeValue("src", "")) && 
                    string.IsNullOrEmpty(n.GetAttributeValue("async", "")) && 
                    string.IsNullOrEmpty(n.GetAttributeValue("defer", "")))
                .Select(n => n.GetAttributeValue("src", ""))
                .Distinct()
                .ToList();
            
            var waterfall = new List<ResourceLoadInfo>();
            
            waterfall.Add(new ResourceLoadInfo
            {
                ResourceType = "HTML",
                Url = url,
                Size = html.Length,
                StartTime = 0,
                EndTime = totalLoadTime,
                TimingType = "Document"
            });
            
            Dictionary<string, long> resourceSizes = new Dictionary<string, long>();
            Dictionary<string, long> resourceLoadTimes = new Dictionary<string, long>();
            List<ImageAnalysisResult> imageAnalysisResults = new List<ImageAnalysisResult>();
            
            int totalResources = cssFiles.Count + jsFiles.Count + images.Count;
            int processedResources = 0;
            
            foreach (var cssUrl in cssFiles)
            {
                try
                {
                    var resolvedUrl = ResolveUrl(url, cssUrl);
                    var (size, loadTime) = await MeasureResourceAsync(resolvedUrl);
                    
                    resourceSizes[resolvedUrl] = size;
                    resourceLoadTimes[resolvedUrl] = loadTime;
                    
                    waterfall.Add(new ResourceLoadInfo
                    {
                        ResourceType = "CSS",
                        Url = resolvedUrl,
                        Size = size,
                        StartTime = waterfall[0].EndTime + (processedResources * 10), 
                        EndTime = waterfall[0].EndTime + loadTime + (processedResources * 10),
                        TimingType = renderBlockingCss.Contains(cssUrl) ? "Render Blocking" : "Async"
                    });
                    
                    processedResources++;
                    PrintProgress("Analyzing resources", 50 + (processedResources * 50 / totalResources));
                }
                catch (Exception ex)
                {
                    PrintColoredLine($"Error analyzing CSS resource {cssUrl}: {ex.Message}", errorColor);
                }
            }
            
            foreach (var jsUrl in jsFiles)
            {
                try
                {
                    var resolvedUrl = ResolveUrl(url, jsUrl);
                    var (size, loadTime) = await MeasureResourceAsync(resolvedUrl);
                    
                    resourceSizes[resolvedUrl] = size;
                    resourceLoadTimes[resolvedUrl] = loadTime;
                    
                    waterfall.Add(new ResourceLoadInfo
                    {
                        ResourceType = "JavaScript",
                        Url = resolvedUrl,
                        Size = size,
                        StartTime = waterfall[0].EndTime + (processedResources * 10), 
                        EndTime = waterfall[0].EndTime + loadTime + (processedResources * 10),
                        TimingType = renderBlockingJs.Contains(jsUrl) ? "Render Blocking" : "Async"
                    });
                    
                    processedResources++;
                    PrintProgress("Analyzing resources", 50 + (processedResources * 50 / totalResources));
                }
                catch (Exception ex)
                {
                    PrintColoredLine($"Error analyzing JS resource {jsUrl}: {ex.Message}", errorColor);
                }
            }
            
            foreach (var img in images)
            {
                try
                {
                    var resolvedUrl = ResolveUrl(url, img.Src);
                    var (size, loadTime) = await MeasureResourceAsync(resolvedUrl);
                    
                    resourceSizes[resolvedUrl] = size;
                    resourceLoadTimes[resolvedUrl] = loadTime;
                    
                    string extension = Path.GetExtension(resolvedUrl).ToLowerInvariant();
                    string format = extension.TrimStart('.');
                    if (string.IsNullOrEmpty(format) || format == "jpg") format = "jpeg";
                    
                    var imageResult = new ImageAnalysisResult
                    {
                        Url = resolvedUrl,
                        Size = size,
                        Format = format,
                        Width = img.Width,
                        Height = img.Height,
                        IsLazyLoaded = img.Loading.ToLower() == "lazy"
                    };
                    
                    imageAnalysisResults.Add(imageResult);
                    
                    waterfall.Add(new ResourceLoadInfo
                    {
                        ResourceType = "Image",
                        Url = resolvedUrl,
                        Size = size,
                        StartTime = waterfall[0].EndTime + (processedResources * 10), 
                        EndTime = waterfall[0].EndTime + loadTime + (processedResources * 10),
                        TimingType = img.Loading.ToLower() == "lazy" ? "Lazy" : "Eager"
                    });
                    
                    processedResources++;
                    PrintProgress("Analyzing resources", 50 + (processedResources * 50 / totalResources));
                }
                catch (Exception ex)
                {
                    PrintColoredLine($"Error analyzing image resource {img.Src}: {ex.Message}", errorColor);
                }
            }
            
            waterfall = waterfall.OrderBy(r => r.StartTime).ToList();
            
            long totalPageSize = waterfall.Sum(r => r.Size);
            
            long estimatedLoadTime = waterfall.Max(r => r.EndTime);
            
            DrawBox("Performance Analysis Results", 80);
            Console.WriteLine();
            
            PrintColoredLine("Basic Performance Metrics:", highlightColor);
            PrintKeyValue("Time to First Byte", $"{firstByteTime}ms");
            PrintKeyValue("Total HTML Load Time", $"{totalLoadTime}ms");
            PrintKeyValue("Estimated Full Page Load Time", $"{estimatedLoadTime}ms");
            PrintKeyValue("Total Page Size", $"{FormatFileSize(totalPageSize)}");
            PrintKeyValue("Number of Requests", $"{waterfall.Count}");
            Console.WriteLine();
            
            string performanceRating;
            ConsoleColor ratingColor;
            
            if (totalLoadTime < 1000 && estimatedLoadTime < 2000)
            {
                performanceRating = "Excellent";
                ratingColor = successColor;
            }
            else if (totalLoadTime < 2000 && estimatedLoadTime < 4000)
            {
                performanceRating = "Good";
                ratingColor = ConsoleColor.DarkGreen;
            }
            else if (totalLoadTime < 3000 && estimatedLoadTime < 6000)
            {
                performanceRating = "Average";
                ratingColor = secondaryColor;
            }
            else if (totalLoadTime < 5000 && estimatedLoadTime < 10000)
            {
                performanceRating = "Below Average";
                ratingColor = ConsoleColor.DarkYellow;
            }
            else
            {
                performanceRating = "Poor";
                ratingColor = errorColor;
            }
            
            Console.Write("Overall Performance Rating: ");
            Console.ForegroundColor = ratingColor;
            Console.WriteLine(performanceRating);
            Console.ResetColor();
            Console.WriteLine();
            
            PrintColoredLine("Performance Issues Summary:", highlightColor);
            List<string> issues = new List<string>();
            
            if (renderBlockingCss.Count > 0) 
                issues.Add($"Found {renderBlockingCss.Count} render-blocking CSS resources");
            
            if (renderBlockingJs.Count > 0)
                issues.Add($"Found {renderBlockingJs.Count} render-blocking JavaScript resources");
            
            var largeImages = imageAnalysisResults.Where(i => i.Size > 200 * 1024).ToList(); // > 200KB
            if (largeImages.Count > 0)
                issues.Add($"Found {largeImages.Count} oversized images that could be optimized");
            
            var nonWebPImages = imageAnalysisResults
                .Where(i => i.Format != "webp" && i.Format != "avif" && i.Format != "svg")
                .ToList();
            if (nonWebPImages.Count > 0)
                issues.Add($"Found {nonWebPImages.Count} images not using modern formats (WebP/AVIF)");
            
            var nonLazyImages = imageAnalysisResults.Where(i => !i.IsLazyLoaded).ToList();
            if (nonLazyImages.Count > 0)
                issues.Add($"Found {nonLazyImages.Count} images without lazy loading");
            
            if (totalPageSize > 3 * 1024 * 1024) // > 3MB
                issues.Add($"Total page size ({FormatFileSize(totalPageSize)}) exceeds recommended limit");
            
            if (waterfall.Count > 50)
                issues.Add($"High number of requests ({waterfall.Count}) may impact performance");
            
            if (issues.Count == 0)
                PrintColoredLine("  No major performance issues detected!", successColor);
            else
                foreach (var issue in issues)
                    PrintColoredLine($"  • {issue}", errorColor);
            
            Console.WriteLine();
            
            PrintColoredLine("Optimization Recommendations:", highlightColor);
            
            if (renderBlockingCss.Count > 0)
            {
                PrintColoredLine("  1. Optimize CSS Delivery:", secondaryColor);
                Console.WriteLine("     • Consider inlining critical CSS");
                Console.WriteLine("     • Add 'media' attributes where applicable");
                Console.WriteLine("     • Use 'preload' for important stylesheets");
                Console.WriteLine();
            }
            
            if (renderBlockingJs.Count > 0)
            {
                PrintColoredLine("  2. Optimize JavaScript Loading:", secondaryColor);
                Console.WriteLine("     • Add 'async' or 'defer' attributes to non-critical scripts");
                Console.WriteLine("     • Move scripts to the bottom of the page");
                Console.WriteLine("     • Consider code splitting for large JavaScript bundles");
                Console.WriteLine();
            }
            
            if (largeImages.Count > 0 || nonWebPImages.Count > 0)
            {
                PrintColoredLine("  3. Optimize Images:", secondaryColor);
                if (largeImages.Count > 0)
                {
                    Console.WriteLine("     • Compress the following large images:");
                    foreach (var largeImage in largeImages.Take(3))
                    {
                        Console.WriteLine($"       - {Path.GetFileName(largeImage.Url)} ({FormatFileSize(largeImage.Size)})");
                    }
                    if (largeImages.Count > 3) Console.WriteLine($"       - Plus {largeImages.Count - 3} more images");
                }
                
                if (nonWebPImages.Count > 0)
                {
                    Console.WriteLine("     • Convert images to WebP or AVIF format for better compression");
                }
                Console.WriteLine();
            }
            
            if (nonLazyImages.Count > 0)
            {
                PrintColoredLine("  4. Implement Lazy Loading:", secondaryColor);
                Console.WriteLine("     • Add loading=\"lazy\" attribute to below-the-fold images");
                Console.WriteLine("     • Consider using Intersection Observer for custom lazy loading");
                Console.WriteLine();
            }
            
            PrintColoredLine("Resource Loading Waterfall Chart:", highlightColor);
            DrawWaterfallChart(waterfall);
            Console.WriteLine();
            
            string reportPath = Path.Combine(perfDir, $"performance_report_{DateTime.Now:yyyyMMdd_HHmmss}.txt");
            using (StreamWriter writer = new StreamWriter(reportPath))
            {
                writer.WriteLine($"Performance Analysis Report for {url}");
                writer.WriteLine($"Generated on {DateTime.Now}\n");
                
                writer.WriteLine("Basic Performance Metrics:");
                writer.WriteLine($"Time to First Byte: {firstByteTime}ms");
                writer.WriteLine($"Total HTML Load Time: {totalLoadTime}ms");
                writer.WriteLine($"Estimated Full Page Load Time: {estimatedLoadTime}ms");
                writer.WriteLine($"Total Page Size: {FormatFileSize(totalPageSize)}");
                writer.WriteLine($"Number of Requests: {waterfall.Count}");
                writer.WriteLine($"Performance Rating: {performanceRating}\n");
                
                writer.WriteLine("Performance Issues:");
                if (issues.Count == 0)
                    writer.WriteLine("  No major performance issues detected!");
                else
                    foreach (var issue in issues)
                        writer.WriteLine($"  • {issue}");
                
                writer.WriteLine("\nDetailed Resource Analysis:");
                
                writer.WriteLine("\nCSS Files:");
                foreach (var cssUrl in cssFiles)
                {
                    var resolvedUrl = ResolveUrl(url, cssUrl);
                    if (resourceSizes.ContainsKey(resolvedUrl))
                    {
                        writer.WriteLine($"  • {resolvedUrl}");
                        writer.WriteLine($"    Size: {FormatFileSize(resourceSizes[resolvedUrl])}");
                        writer.WriteLine($"    Load Time: {resourceLoadTimes[resolvedUrl]}ms");
                        writer.WriteLine($"    Render Blocking: {renderBlockingCss.Contains(cssUrl)}");
                    }
                }
                
                writer.WriteLine("\nJavaScript Files:");
                foreach (var jsUrl in jsFiles)
                {
                    var resolvedUrl = ResolveUrl(url, jsUrl);
                    if (resourceSizes.ContainsKey(resolvedUrl))
                    {
                        writer.WriteLine($"  • {resolvedUrl}");
                        writer.WriteLine($"    Size: {FormatFileSize(resourceSizes[resolvedUrl])}");
                        writer.WriteLine($"    Load Time: {resourceLoadTimes[resolvedUrl]}ms");
                        writer.WriteLine($"    Render Blocking: {renderBlockingJs.Contains(jsUrl)}");
                    }
                }
                
                writer.WriteLine("\nImages:");
                foreach (var imgResult in imageAnalysisResults)
                {
                    writer.WriteLine($"  • {imgResult.Url}");
                    writer.WriteLine($"    Size: {FormatFileSize(imgResult.Size)}");
                    writer.WriteLine($"    Format: {imgResult.Format}");
                    if (!string.IsNullOrEmpty(imgResult.Width) && !string.IsNullOrEmpty(imgResult.Height))
                    {
                        writer.WriteLine($"    Dimensions: {imgResult.Width}x{imgResult.Height}");
                    }
                    writer.WriteLine($"    Lazy Loaded: {imgResult.IsLazyLoaded}");
                    
                    string recommendation = "";
                    if (imgResult.Size > 200 * 1024)
                    {
                        recommendation += "Compress image. ";
                    }
                    if (imgResult.Format != "webp" && imgResult.Format != "avif" && imgResult.Format != "svg")
                    {
                        recommendation += "Convert to WebP/AVIF. ";
                    }
                    if (!imgResult.IsLazyLoaded)
                    {
                        recommendation += "Implement lazy loading.";
                    }
                    
                    if (!string.IsNullOrEmpty(recommendation))
                    {
                        writer.WriteLine($"    Recommendation: {recommendation}");
                    }
                }
            }
            
            PrintColoredLine($"\nPerformance analysis report saved to: {reportPath}", successColor);
        }
        catch (Exception ex)
        {
            PrintColoredLine($"Error analyzing website performance: {ex.Message}", errorColor);
            if (ex.InnerException != null)
            {
                PrintColoredLine($"Inner Exception: {ex.InnerException.Message}", errorColor);
            }
        }
    }

    private static async Task<(long Size, long LoadTime)> MeasureResourceAsync(string url)
    {
        try
        {
            var stopwatch = new Stopwatch();
            stopwatch.Start();
            
            var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
            
            var response = await client.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                return (0, 0);
            }
            
            var content = await response.Content.ReadAsByteArrayAsync();
            stopwatch.Stop();
            
            return (content.Length, stopwatch.ElapsedMilliseconds);
        }
        catch
        {
            return (0, 0);
        }
    }

    private static string ResolveUrl(string baseUrl, string relativeUrl)
    {
        if (Uri.TryCreate(relativeUrl, UriKind.Absolute, out _))
        {
            return relativeUrl;
        }
        
        if (relativeUrl.StartsWith("//"))
        {
            var baseUri = new Uri(baseUrl);
            return $"{baseUri.Scheme}:{relativeUrl}";
        }
        
        var baseUri2 = new Uri(baseUrl);
        return new Uri(baseUri2, relativeUrl).AbsoluteUri;
    }

    private static string FormatFileSize(long bytes)
    {
        string[] sizes = { "B", "KB", "MB", "GB" };
        int order = 0;
        double size = bytes;
        
        while (size >= 1024 && order < sizes.Length - 1)
        {
            order++;
            size = size / 1024;
        }
        
        return $"{size:0.##} {sizes[order]}";
    }

    private static void DrawWaterfallChart(List<ResourceLoadInfo> resources)
    {
        int chartWidth = 60;
        long maxEndTime = resources.Max(r => r.EndTime);
        
        Console.WriteLine();
        Console.WriteLine(new string('-', chartWidth + 40));
        
        foreach (var resource in resources)
        {
            string resourceName = resource.ResourceType;
            string resourceUrl = Path.GetFileName(resource.Url);
            if (string.IsNullOrEmpty(resourceUrl)) resourceUrl = new Uri(resource.Url).Host;
            
            resourceName = $"{resourceName}: {resourceUrl}";
            if (resourceName.Length > 30)
            {
                resourceName = resourceName.Substring(0, 27) + "...";
            }
            
            Console.Write(resourceName.PadRight(30) + " | ");
            
            int startPos = (int)((resource.StartTime * chartWidth) / maxEndTime);
            int endPos = (int)((resource.EndTime * chartWidth) / maxEndTime);
            int length = Math.Max(1, endPos - startPos);
            
            ConsoleColor barColor;
            switch (resource.ResourceType)
            {
                case "HTML":
                    barColor = ConsoleColor.DarkCyan;
                    break;
                case "CSS":
                    barColor = ConsoleColor.Blue;
                    break;
                case "JavaScript":
                    barColor = ConsoleColor.Yellow;
                    break;
                case "Image":
                    barColor = ConsoleColor.Green;
                    break;
                default:
                    barColor = ConsoleColor.Gray;
                    break;
            }
            
            if (resource.TimingType == "Render Blocking")
            {
                barColor = ConsoleColor.Red;
            }
            
            Console.Write(new string(' ', startPos));
            
            Console.ForegroundColor = barColor;
            Console.Write(new string('█', length));
            Console.ResetColor();
            
            Console.WriteLine($" {resource.EndTime}ms");
        }
        
        Console.WriteLine(new string('-', chartWidth + 40));
        Console.WriteLine();
        
        Console.Write("Legend: ");
        
        Console.ForegroundColor = ConsoleColor.DarkCyan;
        Console.Write("■ HTML  ");
        Console.ForegroundColor = ConsoleColor.Blue;
        Console.Write("■ CSS  ");
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.Write("■ JavaScript  ");
        Console.ForegroundColor = ConsoleColor.Green;
        Console.Write("■ Image  ");
        Console.ForegroundColor = ConsoleColor.Red;
        Console.Write("■ Render Blocking");
        Console.ResetColor();
        Console.WriteLine();
    }

    private class ResourceLoadInfo
    {
        public string ResourceType { get; set; }
        public string Url { get; set; }
        public long Size { get; set; }
        public long StartTime { get; set; }
        public long EndTime { get; set; }
        public string TimingType { get; set; }
    }

    private class ImageAnalysisResult
    {
        public string Url { get; set; }
        public long Size { get; set; }
        public string Format { get; set; }
        public string Width { get; set; }
        public string Height { get; set; }
        public bool IsLazyLoaded { get; set; }
    }

    #endregion

    #region Main Program
    
    public static async Task Main(string[] args)
    {
        try
        {
            Console.Clear();
            DisplayBanner();
            
            Console.WriteLine("Enter the URL to analyze (e.g., https://example.com):");
            string url = Console.ReadLine()?.Trim() ?? "";

            if (Uri.TryCreate(url, UriKind.Absolute, out Uri validatedUrl) && 
                (validatedUrl.Scheme == Uri.UriSchemeHttp || validatedUrl.Scheme == Uri.UriSchemeHttps))
            {
                while (true)
                {
                    DisplayMenu(url);
                    
                    if (!int.TryParse(Console.ReadLine(), out int action) || action < 1 || action > 8)
                    {
                        Console.WriteLine("Invalid option. Please enter a number between 1 and 8.");
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
                            Console.Write("Enter the check interval in minutes (e.g., 5): ");
                            if (!int.TryParse(Console.ReadLine(), out int changeInterval) || changeInterval < 1)
                            {
                                Console.WriteLine("Invalid interval. Using default of 5 minutes.");
                                changeInterval = 5;
                            }
                            await TrackWebsiteChangesAsync(url, changeInterval);
                            break;
                        case 7:
                            await AnalyzeWebsitePerformanceAsync(url);
                            break;
                        case 8:
                            Console.WriteLine("Exiting program. Goodbye!");
                            return;
                        default:
                            Console.WriteLine("Invalid option. Please try again.");
                            break;
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
        Console.ForegroundColor = primaryColor;
        Console.WriteLine(@"
╭───────────────────────────────────────────────────────╮
│                      URL TOOL                         │
│         A comprehensive website utility tool          │
╰───────────────────────────────────────────────────────╯");
        Console.ResetColor();
    }

    private static void DisplayMenu(string url)
    {
        Console.ForegroundColor = highlightColor;
        Console.WriteLine("\n╭───────────────────────────────────────────────────────╮");
        Console.WriteLine("│                     MAIN MENU                         │");
        Console.WriteLine("╰───────────────────────────────────────────────────────╯");
        Console.ResetColor();
        
        Console.WriteLine($"Current URL: {url}\n");
        
        PrintMenuOption(1, "Scrape Website", "Download HTML, CSS, JS, images");
        PrintMenuOption(2, "Monitor Website Status", "Check website uptime");
        PrintMenuOption(3, "Analyze Content", "Count images, links, scripts");
        PrintMenuOption(4, "Track Response Time", "Measure website performance");
        PrintMenuOption(5, "Generate Embed Code", "Create social media-style embeds");
        PrintMenuOption(6, "Track Website Changes", "Monitor for content changes");
        PrintMenuOption(7, "Speed Optimizer Analysis", "Identify performance issues and suggest fixes");
        PrintMenuOption(8, "Exit", "Quit the application");
        
        Console.Write("\nEnter your choice (1-8): ");
    }
    
    private static void PrintMenuOption(int number, string title, string description)
    {
        Console.Write($"{number}) ");
        Console.ForegroundColor = secondaryColor;
        Console.Write($"{title}");
        Console.ResetColor();
        Console.WriteLine($" - {description}");
    }
    
    #endregion
}         
