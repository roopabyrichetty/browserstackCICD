using System.Runtime.CompilerServices;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;
using OpenQA.Selenium.Remote;
using System.Net;
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Newtonsoft.Json;
using System.Text.RegularExpressions;
using System.Diagnostics;
using System.Threading;
using System.Linq;

using NUnit.Framework;




internal static class Initializer
{
    public static List<MethodBase> patchMethods = new List<MethodBase>();
    [ModuleInitializer]
    internal static void Run() {
        Assembly assembly = Assembly.GetExecutingAssembly();
        string[] attributes = { "NUnit.Framework.TestAttribute", "NUnit.Framework.TestCaseAttribute", "NUnit.Framework.TestCaseSourceAttribute" };
        var allTypes = assembly.GetTypes();
        foreach (var type in allTypes)
        {
            if (type.IsClass)
            {
                foreach (var method in type.GetMethods())
                {
                    foreach (var att in method.CustomAttributes)
                    {
                       if (attributes.Contains(att.Constructor.DeclaringType.ToString()))
                        {
                            patchMethods.Add(method);
                        }
                    }
                }
            }
        }
        BrowserstackPatcher.DoPatching(); 
    }

}

public class BrowserstackPatcher
{
    //public static Configuration configs;
    // make sure DoPatching() is called at start either by
    // the mod loader or by your injector
    public static void DoPatching()
    {
        var harmony = new Harmony("com.browserstack.patch");
        harmony.PatchAll(Assembly.GetExecutingAssembly());
    }
}

class BrowserStackOptions : DriverOptions
{
    public BrowserStackOptions(String browser_name, String browser_version = "latest")
    {
        if (browser_name != null){
            this.BrowserName = browser_name;
        }
        if (browser_version != null){
            this.BrowserVersion = browser_version;
        }
    }

    public void AddBrowserName(Object browser_name)
    {
        if(this.BrowserName == null)
            this.BrowserName = browser_name.ToString();
    }

    public void AddBrowserVersion(Object browser_version)
    {
        if(this.BrowserVersion == null)
            this.BrowserVersion = browser_version.ToString();
    }

    

    public override ICapabilities ToCapabilities()
    {
        IWritableCapabilities capabilities = this.GenerateDesiredCapabilities(true);

        return capabilities.AsReadOnly();
    }
}

[HarmonyPatch(typeof(HttpCommandExecutor))]
[HarmonyPatch(MethodType.Constructor)]
[HarmonyPatch(new Type[] { typeof(Uri), typeof(TimeSpan), typeof(bool) })]
class ExecutorPatch
{
    static void Prefix(ref Uri addressOfRemoteServer, ref TimeSpan timeout)
    {
        addressOfRemoteServer = new Uri("https://hub.browserstack.com/wd/hub/");
        timeout = timeout.Add(TimeSpan.FromSeconds(900));
    }
}

[HarmonyPatch(typeof(DriverService))]
[HarmonyPatch(MethodType.Constructor)]
[HarmonyPatch(new Type[] { typeof(string), typeof(int), typeof(string), typeof(Uri) })]
class ServicePatch
{
    static void Prefix(ref string servicePath, ref string driverServiceExecutableName)
    {
        try
        {
            File.Create(Path.Join(Path.GetTempPath(), "WebDriver.exe"));
        }catch{}


        servicePath = Path.GetTempPath();
        driverServiceExecutableName = "WebDriver.exe";
    }
}

[HarmonyPatch]
class FindDriverServiceExecutablePatch
{
    static MethodBase TargetMethod()
    {
        // refer to C# reflection documentation:
        return typeof(DriverService).GetMethod("FindDriverServiceExecutable", BindingFlags.NonPublic |  BindingFlags.Static);
    }
    static bool Prefix()
    {
        return false;
    }
}

[HarmonyPatch(typeof(DriverService), nameof(DriverService.Start))]
class StartPatch
{
    static bool Prefix()
    {
        return false;
    }
}

[HarmonyPatch(typeof(WebDriver))]
[HarmonyPatch(MethodType.Constructor)]
[HarmonyPatch(new Type[] { typeof(ICommandExecutor), typeof(ICapabilities) })]
class WebDriverPatch
{
    public static Dictionary<int, RemoteWebDriver> drivers__ = new Dictionary<int, RemoteWebDriver>();
    public static Dictionary<int, bool> quitFromDrivers = new Dictionary<int, bool>();
    public static Dictionary<int, bool> insideTestMethods = new Dictionary<int, bool>();
    public static Dictionary<string, List<string>> errorMessagesList = new Dictionary<string, List<string>>();
    public static bool localNotSetError = false;
    public static string urlForExceptionInResp = "";
    static bool Prefix(ref dynamic executor, ref ICapabilities capabilities)
    {
        Dictionary<string, object> browserstackOptions = new Dictionary<string, object>();

        int index = int.Parse(Environment.GetEnvironmentVariable("index"));
        var browserName = Environment.GetEnvironmentVariable("browserName");
        var browserVersion = Environment.GetEnvironmentVariable("browserVersion");
        var isLocal = Environment.GetEnvironmentVariable("isLocal");
        var localIdentifier = Environment.GetEnvironmentVariable("localIdentifier");
        var proxy = Environment.GetEnvironmentVariable("proxy");

        BrowserStackOptions finalOptions = new BrowserStackOptions(browserName, browserVersion);

        var capsType = capabilities.GetType();
        Dictionary<string, Object> existingKeys = new Dictionary<string, object>();
        if(capsType.ToString() == "OpenQA.Selenium.Appium.AppiumCapabilities")
        {
            ReadOnlyDesiredCapabilities appiumCapabilities = (ReadOnlyDesiredCapabilities)capabilities;
            var dic = appiumCapabilities.ToDictionary();
            foreach (var cap in dic)
            {
                try
                {
                    if (cap.Key == "browserName")
                    {
                        finalOptions.AddBrowserName(cap.Value);
                    }
                    else if (cap.Key == "browserVersion")
                    {
                        finalOptions.AddBrowserVersion(cap.Value);
                    }
                    else{
                        finalOptions.AddAdditionalOption(cap.Key, cap.Value);
                        existingKeys.TryAdd(cap.Key, cap.Value);
                    }
                }
                catch
                {
                }
            }
        }
        else
        {
            ReadOnlyDesiredCapabilities dc = (ReadOnlyDesiredCapabilities)capabilities;
            var dic = dc.ToDictionary();
            foreach (var cap in dic)
            {
                try
                {
                    if(cap.Key == "browserName")
                    {
                        finalOptions.AddBrowserName(cap.Value);
                    }
                    else if(cap.Key == "browserVersion")
                    {
                        finalOptions.AddBrowserVersion(cap.Value);
                    }
                    else
                    {
                        finalOptions.AddAdditionalOption(cap.Key, cap.Value);
                        existingKeys.TryAdd(cap.Key, cap.Value);
                    }
                }
                catch 
                { 
                }
            }
        }
        String jsonText = null;
        try
        {
            jsonText = File.ReadAllText(Environment.GetEnvironmentVariable("capabilitiesPath"));
        }
        catch
        { }

        if (jsonText != null)
        {
            JArray json = JArray.Parse(jsonText);
            JObject options = null;
            if(json.Count > 0)
            {
                JObject jsonIndexed = (JObject)json[index];
                options = (JObject)jsonIndexed.GetValue("bstack:options");
                if (options != null)
                {
                    foreach (var item in options)
                    {
                        browserstackOptions.Add(item.Key, item.Value);
                    }

                    if (isLocal == "true")
                    {
                        browserstackOptions.Add("local", true);
                        if (localIdentifier != "")
                            browserstackOptions.Add("localIdentifier", localIdentifier);
                    }
                    finalOptions.AddAdditionalOption("bstack:options", browserstackOptions);
                    jsonIndexed.Remove("bstack:options");
                }
                foreach (var item in jsonIndexed)
                {
                    try
                    {
                        if (item.Value != null && existingKeys.ContainsKey(item.Key))
                        {
                            try
                            {
                                JObject ex = (JObject)JToken.FromObject(existingKeys[item.Key]);
                                JObject values = (JObject)item.Value;
                                ex.Merge(values);
                                finalOptions.AddAdditionalOption(item.Key, ex);
                            }
                            catch
                            {
                                finalOptions.AddAdditionalOption(item.Key, item.Value);
                            }
                            
                        }
                        else
                        {
                            finalOptions.AddAdditionalOption(item.Key, item.Value);
                        }
                    }
                    catch
                    { }
                }
            }
            if (options == null && isLocal == "true")
            {
                finalOptions.AddAdditionalOption("browserstack.local", true);
                if (localIdentifier != "")
                    finalOptions.AddAdditionalOption("local_identifier", localIdentifier);
            }
        }

        try
        {
            capabilities = finalOptions.ToCapabilities();
        }
        catch
        {
        }
        
        if (proxy != null)
            executor.Proxy = new WebProxy(proxy, false);
        return true;
    }

    static void Postfix(RemoteWebDriver __instance)
    {
        drivers__[Thread.CurrentThread.ManagedThreadId] = __instance;
    }
}

[HarmonyPatch(typeof(WebDriver), nameof(WebDriver.Quit))]
class QuitPatch
{
    static bool Prefix(RemoteWebDriver __instance)
    {
        try
        {
            if (WebDriverPatch.insideTestMethods.GetValueOrDefault(Thread.CurrentThread.ManagedThreadId, false)) {
                WebDriverPatch.quitFromDrivers[Thread.CurrentThread.ManagedThreadId] = true;
                return false;
            } else {
            if (WebDriverPatch.errorMessagesList.GetValueOrDefault(__instance.SessionId.ToString(), new List<string>()).Count > 0)
                ((IJavaScriptExecutor)__instance).ExecuteScript("browserstack_executor: {\"action\": \"setSessionStatus\", \"arguments\": {\"status\":\"failed\", \"reason\": " + JsonConvert.SerializeObject(String.Join(", ", WebDriverPatch.errorMessagesList.GetValueOrDefault(__instance.SessionId.ToString(), new List<string>()))) + "}}");
            else
                ((IJavaScriptExecutor)__instance).ExecuteScript("browserstack_executor: {\"action\": \"setSessionStatus\", \"arguments\": {\"status\":\"passed\", \"reason\": \"Passed\"}}");
            // Final session marking.
            return true;
            }
        }
        catch{}
        return true;
    }
}

[HarmonyPatch]
class GoToUrlPatch
{
    public static List<MethodBase> accessMethods = new List<MethodBase>();
    public static List<string> validDomainOrIPs = new List<string>() { "^localhost$", "^bs-local.com$", "^127\\.", "^10\\.", "^172\\.1[6-9]\\.", "^172\\.2[0-9]\\.", "^172\\.3[0-1]\\.", "^192\\.168\\." };
    static List<MethodBase> TargetMethods()
    {
        MethodBase patchStringGoToUrl = AccessTools.Method(AccessTools.TypeByName("OpenQA.Selenium.Navigator"), "GoToUrl", new Type[] { typeof(String) });
        MethodBase patchUriGoToUrl = AccessTools.Method(AccessTools.TypeByName("OpenQA.Selenium.Navigator"), "GoToUrl", new Type[] { typeof(Uri) });
        accessMethods.Add(patchStringGoToUrl);
        accessMethods.Add(patchUriGoToUrl);
        return accessMethods;
    }
    static void Prefix(ref dynamic url)
    {
        String goToUrl = Convert.ToString(url);
        WebDriverPatch.urlForExceptionInResp = goToUrl;
        getNudgeLocalNotSetError(goToUrl);
    }

    static void getNudgeLocalNotSetError(String url)
    {
        try {
            var isLocal = Environment.GetEnvironmentVariable("isLocal");
            if (isLocal == "true" || WebDriverPatch.localNotSetError.Equals(true))
            {
                return;
            }
            string hostname = GetHostName(url);
            bool isPrivate = IsPrivateDomainOrIP(hostname);
                if (isPrivate)
            {
                string browserstackFolderPath = GetTempDir();
                if (!Directory.Exists(browserstackFolderPath))
                {
                    Directory.CreateDirectory(browserstackFolderPath);
                }
                string filePath = Path.Join(browserstackFolderPath, ".local-not-set.json");
                if (File.Exists(browserstackFolderPath))
                {
                    WebDriverPatch.localNotSetError = true;
                    return;
                }

                WebDriverPatch.localNotSetError = true;
                JObject j = new JObject();
                j.Add("hostname", hostname);
                File.WriteAllText(filePath, JsonConvert.SerializeObject(j));
            }
        } catch (Exception ex)
        {
        }
    }

    static String GetHostName(String url)
    {
        String hostName = "";
        try
        {
            var uriObject = new Uri(url);
            hostName = uriObject.Host;

        }
        catch (Exception ex)
        {
        }

        return hostName;
    }

    static bool IsPrivateDomainOrIP(String hostName)
    {
        bool isPrivate = false;
        if (!String.IsNullOrEmpty(hostName))
        {
            try
            {
                foreach (String reg in validDomainOrIPs)
                {
                    Regex regex = new Regex(reg);
                    if (regex.IsMatch(hostName))
                    {
                        return true;
                    }
                }
            }
            catch (Exception ex)
            {
            }
        }
        return isPrivate;
    }

    public static string GetTempDir()
    {
        string browserstackFolderPath = Path.Join(Path.GetTempPath(), ".browserstack");
        try
        {
            if (!Directory.Exists(browserstackFolderPath))
            {
                Directory.CreateDirectory(browserstackFolderPath);
            }
            return browserstackFolderPath;
        }
        catch (Exception ex)
        {
        }
        return browserstackFolderPath;
    }

    static Exception Finalizer(Exception __exception)
    {
        var driver = WebDriverPatch.drivers__.GetValueOrDefault(Thread.CurrentThread.ManagedThreadId, null);
        if (driver != null)
        {
            var sessionName = TestContext.CurrentContext.Test.FullName;
var status = TestContext.CurrentContext.Result.Outcome.Status;
var message = TestContext.CurrentContext.Result.Message;
 
            if (message != null) message = message.ToString();
            if (__exception != null)
            {
                message = __exception.Message;
            }
            if (message != null && (message.Contains("ERR_NAME_NOT_RESOLVED") || message.Contains("ERR_CONNECTION_REFUSED") || message.Contains("ERR_TUNNEL_CONNECTION_FAILED")))
            {
                try {
                    string hostName = GetHostName(WebDriverPatch.urlForExceptionInResp);
                    var isLocal = Environment.GetEnvironmentVariable("isLocal");
                    if (!(isLocal == "true" || WebDriverPatch.localNotSetError.Equals(true)))
                    {
                        string browserstackFolderPath = Path.Join(Path.GetTempPath(), ".browserstack");
                        try
                        {
                            if (!Directory.Exists(browserstackFolderPath))
                            {
                                Directory.CreateDirectory(browserstackFolderPath);
                            }
                        }
                        catch (Exception ex)
                        {
                        }
                        if (!Directory.Exists(browserstackFolderPath))
                        {
                            Directory.CreateDirectory(browserstackFolderPath);
                        }
                        string filePath = Path.Join(browserstackFolderPath, ".local-not-set.json");
                        if (File.Exists(browserstackFolderPath))
                        {
                            WebDriverPatch.localNotSetError = true;
                        }
                        else
                        {
                            WebDriverPatch.localNotSetError = true;
                            JObject j = new JObject();
                            j.Add("hostname", hostName);
                            File.WriteAllText(filePath, JsonConvert.SerializeObject(j));
                        }
                    }
                } catch (Exception ex)
                {
                }
            }
            
        }
        return __exception;
    }
}



[HarmonyPatch(typeof(NUnit.Framework.Internal.TestResult), nameof(NUnit.Framework.Internal.TestResult.SetResult))]
[HarmonyPatch(new Type[] { typeof(NUnit.Framework.Interfaces.ResultState), typeof(string), typeof(string) })]
class Patch07
{
static bool Prefix(ref string stackTrace)
{
if (stackTrace != null)
stackTrace = Regex.Replace(stackTrace, @"(_Patch)(\d+)(?!.*\d)", "");
return true;
}
}


class BrowserStackException : Exception
{
    private string oldStackTrace;

    public BrowserStackException(string message, string stackTrace) : base(message)
    {
        this.oldStackTrace = stackTrace;
    }


    public override string StackTrace
    {
        get
        {
            return this.oldStackTrace;
        }
    }
}

[HarmonyPatch]
class PatchTest
{
    static IEnumerable<MethodBase> TargetMethods()
    {
        return Initializer.patchMethods;
    }

    static void Prefix()
    {
        WebDriverPatch.insideTestMethods[Thread.CurrentThread.ManagedThreadId] = true;
    }

    static Exception Finalizer(Exception __exception)
    {
        var driver = WebDriverPatch.drivers__.GetValueOrDefault(Thread.CurrentThread.ManagedThreadId, null);
        if (driver != null)
        {
            var sessionName = TestContext.CurrentContext.Test.FullName;
var status = TestContext.CurrentContext.Result.Outcome.Status;
var message = TestContext.CurrentContext.Result.Message;
 
            if (message != null) message = message.ToString();
            if (__exception != null)
            {
                status = NUnit.Framework.Interfaces.TestStatus.Failed;
                message = __exception.Message;
            }
            ((IJavaScriptExecutor)driver).ExecuteScript("browserstack_executor: {\"action\": \"setSessionName\", \"arguments\": {\"name\": " + JsonConvert.SerializeObject(sessionName) + "}}");
            if (status == NUnit.Framework.Interfaces.TestStatus.Failed)
            {
                if (WebDriverPatch.errorMessagesList.ContainsKey(driver.SessionId.ToString()))
                {
                    WebDriverPatch.errorMessagesList[driver.SessionId.ToString()].Add(message);
                }
                else
                {
                    WebDriverPatch.errorMessagesList.Add(driver.SessionId.ToString(), new List<string> { message });
                }
                ((IJavaScriptExecutor)driver).ExecuteScript("browserstack_executor: {\"action\": \"annotate\", \"arguments\": {\"data\": " + JsonConvert.SerializeObject("Failed - " + message) + ", \"level\": \"error\"}}");
            }
            else
            {
                ((IJavaScriptExecutor)driver).ExecuteScript("browserstack_executor: {\"action\": \"annotate\", \"arguments\": {\"data\": \"Passed\", \"level\": \"info\"}}");
            }
            WebDriverPatch.insideTestMethods[Thread.CurrentThread.ManagedThreadId] = false;
            if (WebDriverPatch.quitFromDrivers.GetValueOrDefault(Thread.CurrentThread.ManagedThreadId, false))
            {
                WebDriverPatch.quitFromDrivers[Thread.CurrentThread.ManagedThreadId] = false;
                driver.Quit();
            }

            
        }

        return __exception;
    }
}
