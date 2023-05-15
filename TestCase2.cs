using NUnit.Framework;
using OpenQA.Selenium.Interactions;
using OpenQA.Selenium;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BrowserStack;
namespace TestScript
{
    [TestFixture]
    public class TestCase2: BrowserStackNUnitTest
    {
        public TestCase2() : base() { }

        [Test]
        public void SearchResources()
        {
            driver.Navigate().GoToUrl("https:/www.bentley.com/resources");
            driver.Manage().Window.Maximize();
            Thread.Sleep(3000);
            for (int i = 0; i < 10; i++)
            {
                new Actions(driver).SendKeys(OpenQA.Selenium.Keys.ArrowDown).Perform();

            }
            Thread.Sleep(3000);

            WebElement search_query = (WebElement)driver.FindElement(By.CssSelector("#post-8848 > div > div > div > section.elementor-section.elementor-top-section.elementor-element.elementor-element-a6da7b4.elementor-section-full_width.elementor-section-height-default.elementor-section-height-default > div > div > div > div > div > div > fieldset > div > label > input"));
            search_query.SendKeys("construction");
            Thread.Sleep(3000);
            ReadOnlyCollection<IWebElement> webElements = driver.FindElements(By.ClassName("wpgb-card-media-content"));

            WebElement searchresult = (WebElement)driver.FindElement(By.ClassName("searchwp-highlight"));
            if (webElements.Count > 0)
            {
                ((IJavaScriptExecutor)driver).ExecuteScript("browserstack_executor: {\"action\": \"setSessionStatus\", \"arguments\": {\"status\":\"passed\", \"reason\": \" Resource Search Successful!\"}}");
            }
            else
            {
                ((IJavaScriptExecutor)driver).ExecuteScript("browserstack_executor: {\"action\": \"setSessionStatus\", \"arguments\": {\"status\":\"failed\", \"reason\": \" No Results in Resource search \"}}");
            }
            Assert.That(searchresult.Text, Is.EqualTo("Construction"));

        }
    }
}
