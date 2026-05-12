using System.Globalization;
using FluentAssertions;
using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;

namespace DatesAndStuff.Web.Tests;

[TestFixture]
public class BlazeDemoTests
{
    private const string BaseUrl = "https://blazedemo.com/";
    private const string DepartureCity = "Mexico City";
    private const string DestinationCity = "Dublin";
    private const decimal ScreenshotPriceThreshold = 1000m;

    private IWebDriver? driver;

    [SetUp]
    public void SetupTest()
    {
        var options = new ChromeOptions();
        var browserPath = Environment.GetEnvironmentVariable("SE_BROWSER_PATH");

        if (string.IsNullOrWhiteSpace(browserPath))
        {
            browserPath = TryResolveLatestSeleniumManagedChromeBinary();
        }

        if (!string.IsNullOrWhiteSpace(browserPath))
        {
            options.BinaryLocation = browserPath;
        }

        options.AddArgument("--headless=new");
        options.AddArgument("--no-sandbox");
        options.AddArgument("--disable-dev-shm-usage");
        options.AddArgument("--disable-gpu");
        options.AddArgument("--window-size=1400,1200");

        var serviceDirectory = TryResolveLatestSeleniumManagedChromeDriverDirectory();
        var service = serviceDirectory != null
            ? ChromeDriverService.CreateDefaultService(serviceDirectory)
            : ChromeDriverService.CreateDefaultService();

        driver = new ChromeDriver(service, options, TimeSpan.FromSeconds(30));
    }

    [TearDown]
    public void TeardownTest()
    {
        try
        {
            driver?.Quit();
            driver?.Dispose();
        }
        catch (Exception)
        {
        }
    }

    [Test]
    public void BlazeDemo_MexicoCityToDublin_ShouldHaveAtLeastThreeFlights()
    {
        var flightRows = SearchFlights(DepartureCity, DestinationCity);

        flightRows.Count.Should().BeGreaterThanOrEqualTo(3);
    }

    [Test]
    public void BlazeDemo_MexicoCityToDublin_CheapFlight_ShouldCreateScreenshot()
    {
        var flightRows = SearchFlights(DepartureCity, DestinationCity);
        flightRows.Count.Should().BeGreaterThanOrEqualTo(3);

        var cheapFlight = flightRows
            .Select(row => new
            {
                Row = row,
                Price = ParsePrice(row.FindElements(By.TagName("td"))[5].Text)
            })
            .FirstOrDefault(flight => flight.Price < ScreenshotPriceThreshold);

        if (cheapFlight is null)
        {
            Assert.Pass($"No Dublin flight under {ScreenshotPriceThreshold.ToString("0.00", CultureInfo.InvariantCulture)} was found.");
            return;
        }

        var screenshotDirectory = ResolveScreenshotDirectory();
        Directory.CreateDirectory(screenshotDirectory);

        var screenshotPath = Path.Combine(
            screenshotDirectory,
            $"blazedemo-mexico-city-dublin-{cheapFlight.Price.ToString("0.00", CultureInfo.InvariantCulture).Replace(".", "_", StringComparison.Ordinal)}.png");

        cheapFlight.Row.FindElement(By.CssSelector("input[type='submit']")).Click();
        WaitForPurchasePage();

        TestContext.Progress.WriteLine(
            $"Taking screenshot for {DepartureCity} -> {DestinationCity} flight at price {cheapFlight.Price.ToString("0.00", CultureInfo.InvariantCulture)}.");
        TestContext.Progress.WriteLine($"Screenshot path: {screenshotPath}");

        var screenshot = ((ITakesScreenshot)driver!).GetScreenshot();
        screenshot.SaveAsFile(screenshotPath);

        File.Exists(screenshotPath).Should().BeTrue();
    }

    private IReadOnlyList<IWebElement> SearchFlights(string departureCity, string destinationCity)
    {
        driver!.Navigate().GoToUrl(BaseUrl);

        driver.FindElement(By.CssSelector($"select[name='fromPort'] option[value='{departureCity}']")).Click();
        driver.FindElement(By.CssSelector($"select[name='toPort'] option[value='{destinationCity}']")).Click();
        driver.FindElement(By.CssSelector("input[type='submit']")).Click();

        var deadline = DateTime.UtcNow.AddSeconds(10);

        while (DateTime.UtcNow < deadline)
        {
            var rows = driver.FindElements(By.XPath("//table//tr[td]"));
            if (rows.Count > 0)
            {
                return rows;
            }

            Thread.Sleep(200);
        }

        Assert.Fail("Could not find any BlazeDemo flight rows within 10 seconds.");
        return Array.Empty<IWebElement>();
    }

    private void WaitForPurchasePage()
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);

        while (DateTime.UtcNow < deadline)
        {
            if (driver!.Url.Contains("purchase.php", StringComparison.Ordinal))
            {
                return;
            }

            Thread.Sleep(200);
        }

        Assert.Fail("BlazeDemo did not navigate to the purchase page within 10 seconds.");
    }

    private static decimal ParsePrice(string rawPrice)
    {
        return decimal.Parse(
            rawPrice.Replace("$", string.Empty, StringComparison.Ordinal).Trim(),
            NumberStyles.AllowDecimalPoint,
            CultureInfo.InvariantCulture);
    }

    private static string ResolveScreenshotDirectory()
    {
        var configuredDirectory = Environment.GetEnvironmentVariable("BLAZEDEMO_SCREENSHOT_DIR");
        if (!string.IsNullOrWhiteSpace(configuredDirectory))
        {
            return configuredDirectory;
        }

        var currentDirectory = new DirectoryInfo(AppContext.BaseDirectory);
        while (currentDirectory != null)
        {
            if (Directory.Exists(Path.Combine(currentDirectory.FullName, ".git")))
            {
                return Path.Combine(currentDirectory.FullName, "artifacts", "blazedemo-screenshots");
            }

            currentDirectory = currentDirectory.Parent;
        }

        return Path.Combine(TestContext.CurrentContext.WorkDirectory, "artifacts", "blazedemo-screenshots");
    }

    private static string? TryResolveLatestSeleniumManagedChromeBinary()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var root = Path.Combine(home, ".cache", "selenium", "chrome", "linux64");
        var versionDirectory = TryResolveLatestVersionDirectory(root);

        if (versionDirectory == null)
        {
            return null;
        }

        var binaryPath = Path.Combine(versionDirectory, "chrome");
        return File.Exists(binaryPath) ? binaryPath : null;
    }

    private static string? TryResolveLatestSeleniumManagedChromeDriverDirectory()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var root = Path.Combine(home, ".cache", "selenium", "chromedriver", "linux64");
        return TryResolveLatestVersionDirectory(root);
    }

    private static string? TryResolveLatestVersionDirectory(string root)
    {
        if (!Directory.Exists(root))
        {
            return null;
        }

        return Directory.GetDirectories(root)
            .Select(path => new
            {
                Path = path,
                Version = Version.TryParse(Path.GetFileName(path), out var version) ? version : null
            })
            .Where(item => item.Version != null)
            .OrderByDescending(item => item.Version)
            .Select(item => item.Path)
            .FirstOrDefault();
    }
}
