# Azure Durable Function for Text Analysis
## A Simple Azure Durable Function Wrapper for Azure Text Analysis
For [Azure Cognitive Text Analysis services](https://azure.microsoft.com/en-us/products/cognitive-services/text-analytics/#overview), there is a limitation for 5,120 characters.  With this restriction, Text Analysis Services are hardly useful for anything longer than a couple of pages.  This wrapper, based on Azure Durable Function, gets rid of this obstacle by converting Text Analysis into async services.
## Description of Technical Approach
At the core of the Azure Durable Function, 
1. a piece of long text is chopped into multiple small chunks by parapragh(s) and delimitors you selected
2. chunks are sent to Azure Text Analysis Services for processing piece by piece (the code is tested on a very limited Azure resource, so all calls are sequential, if your resources allow, you can easily rewrite this into a fan-out and fan-in parallel model)
3. service results from each chunk are combined / merged into a final result 
## Setup
In order to set this up
1. download the source codes
2. complie into binary with VS2022
3. publish into Azure as a durable funcion with .Net6 stack (no other configurations are needed)
## Usage
To create a test console application
1. init a http client
2. add headers 
    - Ocp-Apim-Subscription-Key (required)
    - Ocp-Apim-Subscription-Region (required)
    - Ocp-Apim-Subscription-Url (required)
    - Ocp-Apim-Subscription-Method (required)
    - Ocp-Apim-Subscription-Chunk-Size (optional - default: 5,000)
    - Ocp-Apim-Subscription-Splitors (optional - character return and line feed are always there)
3. hash out a json body with a long string variable
4. point to Azure Durable Function entry point
5. constantly query Azure Status Uri
6. when complete, read results
### Sample Test Codes 
The following test codes are getting extractive summariztion from a long text file.\
Based my test, aroung 15K characters take about one minute to fihish.\
If you want to detect multiple langauges, you might want to specify a smaller chunk size; due to the fact that one chunk can only return one major language, e.g. English is 51% and Japanense is 49%, the service will only return one value for English.\
If your test has some chunking issues with paragraph only, e.g. 5,000 characters without \r\n, you might want to specify extra delimitors.  Based on HTTP Header protocol, if multiple delimitors are defined, please separate them by ",", e.g. "!,?,.".\
As of writing, the Substrative Summarization is still a gated preview, so you will need submit [a request form](https://aka.ms/applyforgatedsummarizationfeatures) for access.\
When translation service is invoke, the chunk size will set up to 50K characters by system, your custom delimitors are still applied.
```
using System.Net.Http.Headers;
using System.Text;
using System;
using System.Text.Json.Serialization;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Tester
{
    internal class Program
    {
        static HttpClient _httpClient = new HttpClient();
        static void Main(string[] args)
        {
            string longtxt = File.ReadAllText("somelongtextfile.txt");
            
            
            HttpRequestMessage msg = new HttpRequestMessage();
            msg.Method = HttpMethod.Post;
            msg.RequestUri = new Uri("https://yourazuredureablefunction.azurewebsites.net/api/TextAnalysis_HttpStart?code=yourfunctionaccesscode");
            msg.Headers.Add("Ocp-Apim-Subscription-Key", "yourkeyfromcognitiveservices");
            msg.Headers.Add("Ocp-Apim-Subscription-Region", "yourresourceregion");
            msg.Headers.Add("Ocp-Apim-Subscription-Url", "https://eastus2.api.cognitive.microsoft.com/language/analyze-text/jobs?api-version=2022-10-01-preview");
            msg.Headers.Add("Ocp-Apim-Subscription-Method", "ExtractiveSummarization");
            msg.Headers.Add("Ocp-Apim-Subscription-Chunk-Size", "3000");

            object data = new
            {
                displayName = "Document Summarization Task Example",
                analysisInput = new
                {
                    documents = new[] { new { id = 1, language = "en", text = longtxt } }
                },
                tasks = new[]
                {
                    new
                    {
                        kind = "ExtractiveSummarization",
                        taskName = "Document Summarization Task",
                        parameters = new { sentenceCount = 3 }
                    }
                }
            };

            StringContent content = new StringContent(JsonConvert.SerializeObject(data), Encoding.UTF8, "application/json");
            content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
            msg.Content = content;

            HttpResponseMessage response = _httpClient.Send(msg);
            string resp = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            dynamic durableDyn = JsonConvert.DeserializeObject<dynamic>(resp);

            string statusUrl = durableDyn["statusQueryGetUri"];

            HttpRequestMessage statusMsg = new HttpRequestMessage();
            statusMsg.Method = HttpMethod.Get;
            statusMsg.RequestUri = new Uri(statusUrl);

            HttpResponseMessage statusResponse = _httpClient.Send(statusMsg);
            string statusResp = statusResponse.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            dynamic statusDyn = JsonConvert.DeserializeObject<dynamic>(statusResp);

            while (statusDyn["runtimeStatus"] != "Completed")
            { 
                Thread.Sleep(TimeSpan.FromSeconds(10));

                statusMsg = new HttpRequestMessage();
                statusMsg.Method = HttpMethod.Get;
                statusMsg.RequestUri = new Uri(statusUrl);

                statusResponse = _httpClient.Send(statusMsg);
                statusResp = statusResponse.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                statusDyn = JsonConvert.DeserializeObject<dynamic>(statusResp);
            }

            JArray outputs = statusDyn["output"];
            foreach (JValue output in outputs)
            { 
                Console.WriteLine(output.ToString());
            }
        }
    }
}
```
Another sample for translation service
```

using System.Net.Http.Headers;
using System.Text;
using System;
using System.Text.Json.Serialization;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Tester
{
    internal class Program
    {
        static HttpClient _httpClient = new HttpClient();
        static void Main(string[] args)
        {
            string longtxt = File.ReadAllText("somelongtextfile.txt");
            
            
            HttpRequestMessage msg = new HttpRequestMessage();
            msg.Method = HttpMethod.Post;
            msg.RequestUri = new Uri("https://yourazuredureablefunction.azurewebsites.net/api/TextAnalysis_HttpStart?code=yourfunctionaccesscode");
            msg.Headers.Add("Ocp-Apim-Subscription-Key", "yourkeyfromcognitiveservices");
            msg.Headers.Add("Ocp-Apim-Subscription-Region", "yourresourceregion");
            msg.Headers.Add("Ocp-Apim-Subscription-Url", "https://api.cognitive.microsofttranslator.com/translate?api-version=3.0&to=en");
            msg.Headers.Add("Ocp-Apim-Subscription-Method", "Translation");
            
            object data = new object[] 
            { 
                new { Text = longtxt }
            };

            StringContent content = new StringContent(JsonConvert.SerializeObject(data), Encoding.UTF8, "application/json");
            content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
            msg.Content = content;

            HttpResponseMessage response = _httpClient.Send(msg);
            string resp = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            dynamic durableDyn = JsonConvert.DeserializeObject<dynamic>(resp);

            string statusUrl = durableDyn["statusQueryGetUri"];

            HttpRequestMessage statusMsg = new HttpRequestMessage();
            statusMsg.Method = HttpMethod.Get;
            statusMsg.RequestUri = new Uri(statusUrl);

            HttpResponseMessage statusResponse = _httpClient.Send(statusMsg);
            string statusResp = statusResponse.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            dynamic statusDyn = JsonConvert.DeserializeObject<dynamic>(statusResp);

            while (statusDyn["runtimeStatus"] != "Completed")
            { 
                Thread.Sleep(TimeSpan.FromSeconds(10));

                statusMsg = new HttpRequestMessage();
                statusMsg.Method = HttpMethod.Get;
                statusMsg.RequestUri = new Uri(statusUrl);

                statusResponse = _httpClient.Send(statusMsg);
                statusResp = statusResponse.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                statusDyn = JsonConvert.DeserializeObject<dynamic>(statusResp);
            }

            JArray outputs = statusDyn["output"];
            foreach (JValue output in outputs)
            { 
                Console.WriteLine(output.ToString());
            }
        }
    }
}
```
## Services Supported
At the moment, the following services are supported
- Language detect
- Key phrase extraction
- Entity extraction
- PII redaction
- Summarization (extractive / substractive)
- Translation
- Entity Linking
## License
Free software, absoltely no warranty, use at your own risk!
