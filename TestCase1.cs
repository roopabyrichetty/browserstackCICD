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
    public class TestCase1 : BrowserStackNUnitTest
    {
        public TestCase1() : base() { }

        [Test]
        public void HomePageLoad()
        {
            driver.Navigate().GoToUrl("https://www.bentley.com/");
            driver.Manage().Window.Maximize();

            string pageloaded = "Webpage load not complete.";
            IJavaScriptExecutor js = (IJavaScriptExecutor)driver;

            string checkpageloadstatus = (string)js.ExecuteScript("return document.readyState");

            if (checkpageloadstatus == "complete")
            {


                pageloaded = "Page load complete";
            }
            if (pageloaded == "Page load complete")
            {
                ((IJavaScriptExecutor)driver).ExecuteScript("browserstack_executor: {\"action\": \"setSessionStatus\", \"arguments\": {\"status\":\"passed\", \"reason\": \" Page Loaded Successful!\"}}");
            }
            else
            {
                ((IJavaScriptExecutor)driver).ExecuteScript("browserstack_executor: {\"action\": \"setSessionStatus\", \"arguments\": {\"status\":\"failed\", \"reason\": \" Page load not successful! \"}}");
            }
            Assert.That(checkpageloadstatus, Is.EqualTo("complete"));

        }
    }
}
