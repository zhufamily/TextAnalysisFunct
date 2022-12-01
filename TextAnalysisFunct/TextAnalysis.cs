using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;

namespace TextAnalysisFunct
{
    public static class TextAnalysis
    {
        private static HttpClient _httpClient = new HttpClient();

        [FunctionName("TextAnalysis_Orchestrator")]
        public static async Task<HashSet<string>> RunOrchestrator(
            [OrchestrationTrigger] IDurableOrchestrationContext context)
        {
            DurableParam param = context.GetInput<DurableParam>();
            string rawJson = param.JsonBody;
            dynamic dynVal = JsonConvert.DeserializeObject<dynamic>(rawJson);
            string longText = (string) dynVal["analysisInput"]["documents"][0]["text"];

            List<string> outputs = await context.CallActivityAsync<List<string>>("TextAnalysis_ChunkActivity", longText);
            HashSet<string> finalOutputs = new HashSet<string>();

            if (param.Method.ToLower() == "languagedetection")
            {
                foreach (string chunk in outputs)
                {
                    dynVal["analysisInput"]["documents"][0]["text"] = chunk;
                    string chunkJson = JsonConvert.SerializeObject(dynVal);
                    param.JsonBody = chunkJson;
                    List<string> analysisOutputs = await context.CallActivityAsync<List<string>>("TextAnalysis_LanguageDetectionActivity", param);
                    finalOutputs.UnionWith(analysisOutputs);
                }
            }

            return finalOutputs;
        }

        [FunctionName("TextAnalysis_ChunkActivity")]
        public static List<string> Chunk([ActivityTrigger] string longText, ILogger log)
        {
            // default chunk by line feed, you can add extra through config
            string[] paras = longText.Split(new string[] { "\r\n", "\r", "\n" }, System.StringSplitOptions.RemoveEmptyEntries);
            List<string> chunkText = new List<string>();
            StringBuilder sb = new StringBuilder();
            foreach (string para in paras) 
            {
                if (sb.Length + para.Length > 5000)
                {
                    chunkText.Add(sb.ToString().Trim());
                    sb = new StringBuilder();
                }
                sb.Append($"{para}{Environment.NewLine}");
            }
            if (sb.Length > 0)
            {
                chunkText.Add(sb.ToString().Trim());
            }
            log.LogInformation($"Get text into chunks.");
            return chunkText;
        }

        [FunctionName("TextAnalysis_LanguageDetectionActivity")]
        public static async Task<List<string>> LanguageDetection([ActivityTrigger] DurableParam param, ILogger log)
        {
            HttpRequestMessage msg = new HttpRequestMessage();
            msg.Method = HttpMethod.Post;
            msg.RequestUri = new Uri(param.Url);
            msg.Headers.Add("Ocp-Apim-Subscription-Key", param.Key);
            //msg.Headers.Add("Ocp-Apim-Subscription-Region", param.Region);

            StringContent content = new StringContent(param.JsonBody, Encoding.UTF8, "application/json");
            content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
            msg.Content = content;

            HttpResponseMessage response = _httpClient.Send(msg);
            string resp = await response.Content.ReadAsStringAsync();
            dynamic respObj = JsonConvert.DeserializeObject<dynamic>(resp);
            JArray langs = respObj["results"]["documents"];
            List<string> anaOutputs = new List<string>();
            foreach (JObject doc in langs)
            {
                string lang = (string)doc["detectedLanguage"]["name"];
                anaOutputs.Add(lang);
            }
            return anaOutputs;
        }

        [FunctionName("TextAnalysis_HttpStart")]
        public static async Task<IActionResult> HttpStart(
            [HttpTrigger(AuthorizationLevel.Function, "post")] HttpRequest req,
            [DurableClient] IDurableOrchestrationClient starter,
            ILogger log)
        {
            if (!req.Headers.ContainsKey("Ocp-Apim-Subscription-Key"))
            {
                return new BadRequestObjectResult("Header Ocp-Apim-Subscription-Key is missing");
            }
            if (!req.Headers.ContainsKey("Ocp-Apim-Subscription-Region"))
            {
                return new BadRequestObjectResult("Header Ocp-Apim-Subscription-Region is missing");
            }
            if (!req.Headers.ContainsKey("Ocp-Apim-Subscription-Url"))
            {
                return new BadRequestObjectResult("Header Ocp-Apim-Subscription-Url is missing");
            }
            if (!req.Headers.ContainsKey("Ocp-Apim-Subscription-Method"))
            {
                return new BadRequestObjectResult("Header Ocp-Apim-Subscription-Method is missing");
            }

            string key = req.Headers["Ocp-Apim-Subscription-Key"];
            string region = req.Headers["Ocp-Apim-Subscription-Region"];
            string url = req.Headers["Ocp-Apim-Subscription-Url"];
            string methog = req.Headers["Ocp-Apim-Subscription-Method"];
            string json = string.Empty;

            using (MemoryStream ms = new MemoryStream())
            {
                req.Body.CopyTo(ms);
                json = Encoding.UTF8.GetString(ms.ToArray());
            }

            DurableParam param = new DurableParam()
            {
                JsonBody = json,
                Key = key,
                Region = region,
                Method = methog,
                Url = url
            };

            // Function input comes from the request content.
            string instanceId = await starter.StartNewAsync<DurableParam>("TextAnalysis_Orchestrator", param);

            log.LogInformation($"Started orchestration with ID = '{instanceId}'.");

            return starter.CreateCheckStatusResponse(req, instanceId);
        }
    }
}