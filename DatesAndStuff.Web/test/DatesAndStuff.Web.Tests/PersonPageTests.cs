using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using FluentAssertions;
using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;
using OpenQA.Selenium.Support.UI;
using SeleniumExtras.WaitHelpers;

namespace DatesAndStuff.Web.Tests;

[TestFixture]
public class PersonPageTests
{
    private IWebDriver? driver;
    private StringBuilder verificationErrors = new();
    private const string BaseURL = "http://localhost:5091";
    private bool acceptNextAlert = true;

    private Process? _blazorProcess;

    [OneTimeSetUp]
    public void StartBlazorServer()
    {
        var testAssemblyDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
        var webProjectPath = Path.GetFullPath(Path.Combine(
            testAssemblyDirectory!,
            "../../../../../src/DatesAndStuff.Web/DatesAndStuff.Web.csproj"
            ));

        var webProjFolderPath = Path.GetDirectoryName(webProjectPath);

        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = $"run --project \"{webProjectPath}\" --launch-profile http",
            WorkingDirectory = webProjFolderPath!,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };

        _blazorProcess = Process.Start(startInfo);
        if (_blazorProcess == null)
        {
            throw new InvalidOperationException("Failed to start the Blazor web process.");
        }

        // Wait for the app to become available
        var client = new HttpClient();
        var timeout = TimeSpan.FromSeconds(30);
        var start = DateTime.Now;
        var appStarted = false;

        while (DateTime.Now - start < timeout)
        {
            try
            {
                var result = client.GetAsync(BaseURL).Result;
                if (result.IsSuccessStatusCode)
                {
                    appStarted = true;
                    break;
                }
            }
            catch (Exception)
            {
                Thread.Sleep(1000);
            }
        }

        if (!appStarted)
        {
            throw new TimeoutException($"Web app did not become available at {BaseURL} within {timeout.TotalSeconds} seconds.");
        }
    }

    [OneTimeTearDown]
    public void StopBlazorServer()
    {
        if (_blazorProcess != null && !_blazorProcess.HasExited)
        {
            _blazorProcess.Kill(true);
            _blazorProcess.Dispose();
        }
    }

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

        verificationErrors = new StringBuilder();
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
            // Ignore errors if unable to close the browser
        }
        Assert.That(verificationErrors.ToString(), Is.EqualTo(""));
    }

    [TestCase(0, 5000)]
    [TestCase(5, 5250)]
    [TestCase(-5, 4750)]
    public void Person_SalaryIncrease_ShouldIncrease(double salaryIncreasePercentage, double expectedSalary)
    {
        // Arrange
        driver.Navigate().GoToUrl(BaseURL);
        driver.FindElement(By.XPath("//*[@data-test='PersonPageNavigation']")).Click();

        var wait = new WebDriverWait(driver, TimeSpan.FromSeconds(5));

        var input = wait.Until(d =>
        {
            try
            {
                var element = d.FindElement(By.XPath("//*[@data-test='SalaryIncreasePercentageInput']"));
                return element.Displayed && element.Enabled ? element : null;
            }
            catch (StaleElementReferenceException)
            {
                return null;
            }
        });
        input.Clear();
        input.SendKeys(salaryIncreasePercentage.ToString());

        // Act
        var submitButton = wait.Until(d =>
        {
            try
            {
                var element = d.FindElement(By.XPath("//*[@data-test='SalaryIncreaseSubmitButton']"));
                return element.Displayed && element.Enabled ? element : null;
            }
            catch (StaleElementReferenceException)
            {
                return null;
            }
        });
        submitButton.Click();


        // Assert
        var salaryLabel = wait.Until(ExpectedConditions.ElementExists(By.XPath("//*[@data-test='DisplayedSalary']")));
        var salaryAfterSubmission = double.Parse(salaryLabel.Text);
        salaryAfterSubmission.Should().BeApproximately(expectedSalary, 0.001);
    }

    [Test]
    public void Person_SalaryIncrease_LessThanMinusTen_ShouldShowValidationSummaryAndFieldError()
    {
        // Arrange
        const string validationError = "The specified percentage should be greater than -10.";

        driver.Navigate().GoToUrl(BaseURL);
        driver.FindElement(By.XPath("//*[@data-test='PersonPageNavigation']")).Click();

        var wait = new WebDriverWait(driver, TimeSpan.FromSeconds(5));

        var input = wait.Until(d =>
        {
            try
            {
                var element = d.FindElement(By.XPath("//*[@data-test='SalaryIncreasePercentageInput']"));
                return element.Displayed && element.Enabled ? element : null;
            }
            catch (StaleElementReferenceException)
            {
                return null;
            }
        });
        input.Clear();
        input.SendKeys("-11");

        // Act
        var submitButton = wait.Until(d =>
        {
            try
            {
                var element = d.FindElement(By.XPath("//*[@data-test='SalaryIncreaseSubmitButton']"));
                return element.Displayed && element.Enabled ? element : null;
            }
            catch (StaleElementReferenceException)
            {
                return null;
            }
        });
        submitButton.Click();

        // Assert
        var matchingValidationElements = wait.Until(d =>
        {
            var matchingElements = d.FindElements(By.XPath($"//*[normalize-space(.) = \"{validationError}\"]"));
            return matchingElements.Count >= 2 ? matchingElements : null;
        });

        matchingValidationElements[0].Text.Should().Be(validationError);
        matchingValidationElements[1].Text.Should().Be(validationError);
    }

    [Test]
    public void Person_SalaryIncrease_MinusTen_ShouldNotUpdateSalary_AndShouldShowValidationError()
    {
        // Arrange
        const string validationError = "The specified percentage should be greater than -10.";

        driver.Navigate().GoToUrl(BaseURL);
        driver.FindElement(By.XPath("//*[@data-test='PersonPageNavigation']")).Click();

        var wait = new WebDriverWait(driver, TimeSpan.FromSeconds(5));

        var salaryLabelBeforeSubmit = wait.Until(ExpectedConditions.ElementExists(By.XPath("//*[@data-test='DisplayedSalary']")));
        var salaryBeforeSubmit = double.Parse(salaryLabelBeforeSubmit.Text);

        var input = wait.Until(d =>
        {
            try
            {
                var element = d.FindElement(By.XPath("//*[@data-test='SalaryIncreasePercentageInput']"));
                return element.Displayed && element.Enabled ? element : null;
            }
            catch (StaleElementReferenceException)
            {
                return null;
            }
        });
        input.Clear();
        input.SendKeys("-10");

        // Act
        var submitButton = wait.Until(d =>
        {
            try
            {
                var element = d.FindElement(By.XPath("//*[@data-test='SalaryIncreaseSubmitButton']"));
                return element.Displayed && element.Enabled ? element : null;
            }
            catch (StaleElementReferenceException)
            {
                return null;
            }
        });
        submitButton.Click();

        // Assert
        var matchingValidationElements = wait.Until(d =>
        {
            var matchingElements = d.FindElements(By.XPath($"//*[normalize-space(.) = \"{validationError}\"]"));
            return matchingElements.Count >= 2 ? matchingElements : null;
        });
        var salaryLabelAfterSubmit = wait.Until(ExpectedConditions.ElementExists(By.XPath("//*[@data-test='DisplayedSalary']")));
        var salaryAfterSubmit = double.Parse(salaryLabelAfterSubmit.Text);

        matchingValidationElements[0].Text.Should().Be(validationError);
        matchingValidationElements[1].Text.Should().Be(validationError);
        salaryAfterSubmit.Should().BeApproximately(salaryBeforeSubmit, 0.001);
    }

    private bool IsElementPresent(By by)
    {
        try
        {
            driver.FindElement(by);
            return true;
        }
        catch (NoSuchElementException)
        {
            return false;
        }
    }

    private bool IsAlertPresent()
    {
        try
        {
            driver.SwitchTo().Alert();
            return true;
        }
        catch (NoAlertPresentException)
        {
            return false;
        }
    }

    private string CloseAlertAndGetItsText()
    {
        try
        {
            IAlert alert = driver.SwitchTo().Alert();
            string alertText = alert.Text;
            if (acceptNextAlert)
            {
                alert.Accept();
            }
            else
            {
                alert.Dismiss();
            }
            return alertText;
        }
        finally
        {
            acceptNextAlert = true;
        }
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
