using Azure.Messaging.ServiceBus;
using Microsoft.Azure.Amqp.Framing;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;
using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;
using System;
using System.Threading.Tasks;


namespace linkedin_App
{
    class Program
    {
        static async Task Main(string[] args)
        {
            // Load appsettings.json configuration
            var configuration = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json")
                .Build();

            string[] proxyIPAddresses = configuration.GetSection("ProxyIPAddresses").Get<string[]>();
            string connectionString = configuration.GetConnectionString("ServiceBusConnection")!;
            string receiveQueueName = configuration.GetValue<string>("ReceiveQueueName")!;
            string sendQueueName = configuration.GetValue<string>("SendQueueName")!;
            string username = configuration.GetValue<string>("Username")!;
            string password = configuration.GetValue<string>("Password")!;

            ServiceBusClient client = new ServiceBusClient(connectionString);
            ServiceBusReceiver receiver = client.CreateReceiver(sendQueueName);

            ServiceBusReceivedMessage[] receivedMessages;

            while (true)
            {
                receivedMessages = (await receiver.ReceiveMessagesAsync(maxMessages: 100)).ToArray();

                if (receivedMessages.Any())
                {
                    foreach (ServiceBusReceivedMessage message in receivedMessages)
                    {
                        string linkedInProfileLink = message.Body.ToString();
                        string partitionKey = message.PartitionKey;
                        var proxy = new Proxy();
                        proxy.IsAutoDetect = false;
                        proxy.HttpProxy = proxyIPAddresses[new Random().Next(0, proxyIPAddresses.Length)];
                        ChromeOptions options = new ChromeOptions();
                        options.AddArgument("--headless");
                        options.Proxy = proxy;
                        //options.AddArgument("--proxy-server=" + proxy);
                        using (IWebDriver driver = new ChromeDriver(options))
                        {
                            Login(driver, username, password);
                            var profileData1 = await ProcessUser(driver, linkedInProfileLink);
                            var profileData = JsonConvert.DeserializeObject<ProfileData>(profileData1);

                            Console.WriteLine("Response:");
                            Console.WriteLine(JsonConvert.SerializeObject(profileData, Formatting.Indented));

                            ServiceBusSender sender = client.CreateSender(receiveQueueName);
                            ServiceBusMessage responseMessage = new ServiceBusMessage(profileData1);
                            responseMessage.PartitionKey = partitionKey;
                            await sender.SendMessageAsync(responseMessage);
                        }
                    }
                }
                else
                {
                    // Wait for a short duration before checking for new messages again
                    await Task.Delay(1000);
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

        static async Task<string> ProcessUser(IWebDriver driver, string linkedInProfileLink)
        {
            driver.Navigate().GoToUrl(linkedInProfileLink);
            await Task.Delay(2000);
            ProfileData profileData = new ProfileData();
            profileData.ProfilePicUrl = FindProfilePictureUrl(driver);
            profileData.BackgroundCoverImageUrl = FindBackgroundCoverImageUrl(driver);
            profileData.FullName = FindElementText(driver, By.CssSelector(".text-heading-xlarge.inline.t-24.v-align-middle.break-words"));
            profileData.Headline = FindElementText(driver, By.CssSelector(".text-body-medium.break-words"));

            Console.WriteLine("Profile Picture URL: " + profileData.ProfilePicUrl);
            Console.WriteLine("Background Cover Image URL: " + profileData.BackgroundCoverImageUrl);
            Console.WriteLine("Full Name: " + profileData.FullName);
            Console.WriteLine("Headline: " + profileData.Headline);
            return JsonConvert.SerializeObject(profileData);

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
                return null!;
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
                return null!;
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
                return null!;
            }
        }
    }

    class ProfileData
    {
        public string ProfilePicUrl { get; set; } = string.Empty;
        public string BackgroundCoverImageUrl { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
        public string Headline { get; set; } = string.Empty;
    }
}