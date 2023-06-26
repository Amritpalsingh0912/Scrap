using Azure.Messaging.ServiceBus;
using Hangfire;
using Hangfire.MemoryStorage;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;
using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;
using System;
using System.IO;
using System.Threading;

namespace linkedin_App
{
    class Program
    {
     static bool isFirstRequest = true;

        static async Task Main(string[] args)
        {
            ConfigureHangfire();

            Console.WriteLine("Press any key to exit...");
            Console.ReadKey();
        }

        static void ConfigureHangfire()
        {
            GlobalConfiguration.Configuration.UseMemoryStorage();

            using (var server = new BackgroundJobServer())
            {
                Console.WriteLine("Hangfire Server started.");

                if (isFirstRequest)
                {
                    // Run immediately for the first request
                    ScrapeData();
                    isFirstRequest = false;
                }

                RecurringJob.AddOrUpdate(() => ScrapeData(), Cron.MinuteInterval(5));

                Console.WriteLine("Scraping job scheduled to run every 5 minutes.");

                while (true)
                {
                    // Sleep for a short duration before checking for new jobs again
                    System.Threading.Thread.Sleep(1000);
                }
            }
        }

        public async static Task  ScrapeData()        
        {
            // Load appsettings.json configuration
            var configuration = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json")
                .Build();

            string[] proxyIPAddresses = configuration.GetSection("ProxyIPAddresses").Get<string[]>();
            string username = configuration.GetValue<string>("Username")!;
            string password = configuration.GetValue<string>("Password")!;
            string connectionString = configuration.GetConnectionString("ServiceBusConnection")!;
            string receiveQueueName = configuration.GetValue<string>("ReceiveQueueName")!;
            string sendQueueName = configuration.GetValue<string>("SendQueueName")!;

            var proxy = new Proxy();
            proxy.IsAutoDetect = false;
            proxy.HttpProxy = proxyIPAddresses[new Random().Next(0, proxyIPAddresses.Length)];
            ChromeOptions options = new ChromeOptions();
            options.AddArgument("--headless");
            options.Proxy = proxy;
            // options.AddArgument("--proxy-server=" + proxy);

            using (IWebDriver driver = new ChromeDriver(options))
            {
                Login(driver, username, password);

                ServiceBusClient client = new ServiceBusClient(connectionString);
                ServiceBusReceiver receiver = client.CreateReceiver(sendQueueName);
                ServiceBusReceivedMessage[] receivedMessages;
                receivedMessages = receivedMessages = (await receiver.ReceiveMessagesAsync(maxMessages: 100)).ToArray();

                if (receivedMessages.Any())
                {
                    foreach (ServiceBusReceivedMessage message in receivedMessages)
                    {
                        string linkedInProfileLink = message.Body.ToString();
                        string partitionKey = message.PartitionKey;
                        var profileData = ProcessUser(driver, linkedInProfileLink);

                        Console.WriteLine("Scraped data:");
                        Console.WriteLine(JsonConvert.SerializeObject(profileData, Formatting.Indented));

                        ServiceBusSender sender = client.CreateSender(receiveQueueName);
                        ServiceBusMessage responseMessage = new ServiceBusMessage(JsonConvert.SerializeObject(profileData));
                        responseMessage.PartitionKey = partitionKey;
                        sender.SendMessageAsync(responseMessage).GetAwaiter().GetResult();
                    }
                }
            }
        }
        
        static void Login(IWebDriver driver, string username, string password)
        {
            driver.Navigate().GoToUrl("https://www.linkedin.com/login");
            driver.FindElement(By.Id("username")).SendKeys(username);
            driver.FindElement(By.Id("password")).SendKeys(password);
            driver.FindElement(By.XPath("//button[@type='submit']")).Click();
        }

        static ProfileData ProcessUser(IWebDriver driver, string linkedInProfileLink)
        {
            driver.Navigate().GoToUrl(linkedInProfileLink);
            // Add necessary delay or wait conditions for page loading and data extraction

            ProfileData profileData = new ProfileData();
            profileData.ProfilePicUrl = FindProfilePictureUrl(driver);
            profileData.BackgroundCoverImageUrl = FindBackgroundCoverImageUrl(driver);
            profileData.FullName = FindElementText(driver, By.CssSelector(".text-heading-xlarge.inline.t-24.v-align-middle.break-words"));
            profileData.Headline = FindElementText(driver, By.CssSelector(".text-body-medium.break-words"));

            return profileData;
        }

        static string FindProfilePictureUrl(IWebDriver driver)
        {
            try
            {
                IWebElement profilePictureElement = driver.FindElement(By.CssSelector(".pv-top-card-profile-picture__image.pv-top-card-profile-picture__image--show.evi-image.ember-view"));
                return profilePictureElement.GetAttribute("src");
            }
            catch (NoSuchElementException)
            {
                Console.WriteLine("Profile picture not found.");
                return null;
            }
        }

        static string FindBackgroundCoverImageUrl(IWebDriver driver)
        {
            try
            {
                IWebElement backgroundCoverImageElement = driver.FindElement(By.CssSelector(".profile-background-image.profile-background-image--default"));
                return backgroundCoverImageElement.GetAttribute("src");
            }
            catch (NoSuchElementException)
            {
                Console.WriteLine("Background cover image not found.");
                return null;
            }
        }

        static string FindElementText(IWebDriver driver, By locator)
        {
            try
            {
                IWebElement element = driver.FindElement(locator);
                return element.Text;
            }
            catch (NoSuchElementException)
            {
                Console.WriteLine("Element not found: " + locator.ToString());
                return null;
            }
        }
    }

    class ProfileData
    {
        public string ProfilePicUrl { get; set; }
        public string BackgroundCoverImageUrl { get; set; }
        public string FullName { get; set; }
        public string Headline { get; set; }
    }
}
