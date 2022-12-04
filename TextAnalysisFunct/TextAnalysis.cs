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
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace TextAnalysisFunct
{
    /// <summary>
    /// Text Analysis Wrapper for Azure Cognitive Services
    /// </summary>
    public static class TextAnalysis
    {
        /// <summary>
        /// Singleton Http Client Instance
        /// </summary>
        private static HttpClient _httpClient = new HttpClient();

        /// <summary>
        /// Orchestrator for Durable Function
        /// </summary>
        /// <param name="context"></param>
        /// <returns></returns>
        [FunctionName("TextAnalysis_Orchestrator")]
        public static async Task<HashSet<string>> RunOrchestrator(
            [OrchestrationTrigger] IDurableOrchestrationContext context,
            ILogger log)
        {
            log = context.CreateReplaySafeLogger(log);
            DurableParam param = context.GetInput<DurableParam>();
            context.SetCustomStatus(new
            {
                prevAction = "Get durable parameter",
                nextAction = "Generate text chunks",
                status = "OK",
                isRunning = true
            });

            string rawJson = param.JsonBody;
            dynamic dynVal = JsonConvert.DeserializeObject<dynamic>(rawJson);
            string longText = param.Method.ToLower() != "translation" ?
                (string)dynVal["analysisInput"]["documents"][0]["text"] :
                (string)((JArray)dynVal)[0]["Text"];

            ChunkParam chunkParam = new ChunkParam()
            { 
                ChunkSize = param.ChunkSize,
                LongText = longText,
                Splitors = param.Splitors
            };

            if (param.Method.ToLower() == "translation")
            {
                chunkParam.ChunkSize = 50000;
            }

            List<string> outputs = await context.CallActivityAsync<List<string>>("TextAnalysis_ChunkActivity", chunkParam);
            HashSet<string> finalOutputs = new HashSet<string>();

            context.SetCustomStatus(new
            {
                prevAction = "Generate text chunks",
                nextAction = "Run text analysis",
                status = "OK",
                isRunning = true
            });

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
            else if (param.Method.ToLower() == "keyphraseextraction")
            {
                foreach (string chunk in outputs)
                {
                    dynVal["analysisInput"]["documents"][0]["text"] = chunk;
                    string chunkJson = JsonConvert.SerializeObject(dynVal);
                    param.JsonBody = chunkJson;
                    List<string> analysisOutputs = await context.CallActivityAsync<List<string>>("TextAnalysis_KeyPhraseExtractionActivity", param);
                    finalOutputs.UnionWith(analysisOutputs);
                }
            }
            else if (param.Method.ToLower() == "entityrecognition")
            {
                foreach (string chunk in outputs)
                {
                    dynVal["analysisInput"]["documents"][0]["text"] = chunk;
                    string chunkJson = JsonConvert.SerializeObject(dynVal);
                    param.JsonBody = chunkJson;
                    List<string> analysisOutputs = await context.CallActivityAsync<List<string>>("TextAnalysis_EntityRecognitionActivity", param);
                    finalOutputs.UnionWith(analysisOutputs);
                }
            }
            else if (param.Method.ToLower() == "piientityrecognition")
            {
                StringBuilder sb = new StringBuilder();
                foreach (string chunk in outputs)
                {
                    dynVal["analysisInput"]["documents"][0]["text"] = chunk;
                    string chunkJson = JsonConvert.SerializeObject(dynVal);
                    param.JsonBody = chunkJson;
                    PiiReturnParam analysisOutputs = await context.CallActivityAsync<PiiReturnParam>("TextAnalysis_PiiEntityRecognitionActivity", param);
                    finalOutputs.UnionWith(analysisOutputs.RedactedEntities);
                    sb.Append(analysisOutputs.RedactedText);
                }
                finalOutputs.Add($"RedactedText:{sb.ToString()}");
            }
            else if (param.Method.ToLower() == "extractivesummarization" || param.Method.ToLower() == "abstractivesummarization")
            {
                while (true)
                {
                    log.LogInformation($"Chunk count: {outputs.Count}");
                    if (outputs.Count == 0)
                    {
                        break;
                    }
                    else if (outputs.Count == 1)
                    {
                        string chunk = outputs[0];
                        dynVal["analysisInput"]["documents"][0]["text"] = chunk;
                        string chunkJson = JsonConvert.SerializeObject(dynVal);
                        param.JsonBody = chunkJson;
                        string analysisOutputs = await context.CallActivityAsync<string>("TextAnalysis_SummarizationActivity", param);
                        log.LogInformation($"Final summary\n{analysisOutputs}");
                        finalOutputs.Add($"Summarization:{analysisOutputs}");
                        break;
                    }
                    else
                    {
                        StringBuilder sb = new StringBuilder();
                        foreach (string chunk in outputs)
                        {
                            dynVal["analysisInput"]["documents"][0]["text"] = chunk;
                            string chunkJson = JsonConvert.SerializeObject(dynVal);
                            param.JsonBody = chunkJson;
                            string analysisOutputs = await context.CallActivityAsync<string>("TextAnalysis_SummarizationActivity", param);
                            sb.Append(analysisOutputs);
                        }
                        log.LogInformation($"Temp summary\n{sb.ToString()}");
                        ChunkParam summaryChunkParam = new ChunkParam()
                        {
                            ChunkSize = param.ChunkSize,
                            LongText = sb.ToString(),
                            Splitors = param.Splitors
                        };
                        outputs = await context.CallActivityAsync<List<string>>("TextAnalysis_ChunkActivity", summaryChunkParam);
                    }
                }
            }
            else if (param.Method.ToLower() == "translation")
            {
                StringBuilder sb = new StringBuilder();
                foreach (string chunk in outputs)
                {
                    ((JArray)dynVal)[0]["Text"] = chunk;
                    string chunkJson = JsonConvert.SerializeObject(dynVal);
                    log.LogInformation($"Json for Translation\n{chunkJson}");
                    param.JsonBody = chunkJson;
                    string analysisOutputs = await context.CallActivityAsync<string>("TextAnalysis_TranslationActivity", param);
                    if (sb.Length == 0)
                    {
                        sb.Append(analysisOutputs);
                    }
                    else
                    {
                        sb.Append($"{Environment.NewLine}{analysisOutputs}");
                    }
                }
                finalOutputs.Add($"TranslatedText:{sb.ToString()}");
            }

            context.SetCustomStatus(new
            {
                prevAction = "Run text analysis",
                status = "OK",
                isRunning = false
            });

            return finalOutputs;
        }

        /// <summary>
        /// Chunk -- break long text into chunks
        /// </summary>
        /// <param name="chunkParam"></param>
        /// <param name="log"></param>
        /// <returns></returns>
        [FunctionName("TextAnalysis_ChunkActivity")]
        public static List<string> Chunk([ActivityTrigger] ChunkParam chunkParam, ILogger log)
        {
            // default chunk by line feed, you can add extra through config
            string[] paras = chunkParam.LongText.Split(chunkParam.Splitors, System.StringSplitOptions.RemoveEmptyEntries);
            List<string> chunkText = new List<string>();
            StringBuilder sb = new StringBuilder();
            foreach (string para in paras) 
            {
                if (sb.Length + para.Length > chunkParam.ChunkSize)
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

        /// <summary>
        /// Language Detection Service
        /// </summary>
        /// <param name="param"></param>
        /// <param name="log"></param>
        /// <returns></returns>
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
            string lang = respObj["results"]["documents"][0]["detectedLanguage"]["name"];
            string code = respObj["results"]["documents"][0]["detectedLanguage"]["iso6391Name"];
            List<string> anaOutputs = new List<string>();
            anaOutputs.Add($"{code}:{lang}");
            return anaOutputs;
        }

        /// <summary>
        /// Key Phrase Extraction Service
        /// </summary>
        /// <param name="param"></param>
        /// <param name="log"></param>
        /// <returns></returns>
        [FunctionName("TextAnalysis_KeyPhraseExtractionActivity")]
        public static async Task<List<string>> KeyPhraseExtraction([ActivityTrigger] DurableParam param, ILogger log)
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

            JArray parases = respObj["results"]["documents"][0]["keyPhrases"];
            List<string> anaOutputs = new List<string>();
            foreach (JToken phrase in parases)
            {
                anaOutputs.Add((string)phrase);
            }
            return anaOutputs;
        }

        /// <summary>
        /// Entity EXtraction Service
        /// </summary>
        /// <param name="param"></param>
        /// <param name="log"></param>
        /// <returns></returns>
        [FunctionName("TextAnalysis_EntityRecognitionActivity")]
        public static async Task<List<string>> EntityRecognition([ActivityTrigger] DurableParam param, ILogger log)
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

            JArray entities = respObj["results"]["documents"][0]["entities"];
            List<string> anaOutputs = new List<string>();
            foreach (JToken entity in entities)
            {
                anaOutputs.Add((string)entity["category"] + ":" + (string)entity["text"]);
            }
            return anaOutputs;
        }

        /// <summary>
        /// PII Recognition Service
        /// </summary>
        /// <param name="param"></param>
        /// <param name="log"></param>
        /// <returns></returns>
        [FunctionName("TextAnalysis_PiiEntityRecognitionActivity")]
        public static async Task<PiiReturnParam> PiiEntityRecognition([ActivityTrigger] DurableParam param, ILogger log)
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

            PiiReturnParam piiParam = new PiiReturnParam();
            JObject doc1 = respObj["results"]["documents"][0];
            piiParam.RedactedText = (string) doc1["redactedText"];
            List<string> anaOutputs = new List<string>();
            foreach (JObject entity in ((JArray)doc1["entities"]))
            {
                anaOutputs.Add((string)entity["category"] + ":" + (string)entity["text"]);
            }
            piiParam.RedactedEntities = anaOutputs;
            // log.LogInformation($"Redacted Text\n{piiParam.RedactedText}");
            return piiParam;
        }

        /// <summary>
        /// Summarization Service
        /// </summary>
        /// <param name="param"></param>
        /// <param name="log"></param>
        /// <returns></returns>
        [FunctionName("TextAnalysis_SummarizationActivity")]
        public static async Task<string> Summarization([ActivityTrigger] DurableParam param, ILogger log)
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
            string location = response.Headers.GetValues("Operation-Location").FirstOrDefault();
            
            msg = new HttpRequestMessage();
            msg.Method = HttpMethod.Get;
            msg.RequestUri = new Uri(location);
            msg.Headers.Add("Ocp-Apim-Subscription-Key", param.Key);

            HttpResponseMessage response2 = _httpClient.Send(msg);
            string resp = await response2.Content.ReadAsStringAsync();
            dynamic respObj = JsonConvert.DeserializeObject<dynamic>(resp);
            string processStatus = (string) respObj["status"];
            
            while (processStatus == "running" || processStatus == "notStarted")
            {
                Thread.Sleep(TimeSpan.FromMilliseconds(1000));
                msg = new HttpRequestMessage();
                msg.Method = HttpMethod.Get;
                msg.RequestUri = new Uri(location);
                msg.Headers.Add("Ocp-Apim-Subscription-Key", param.Key);

                response2 = _httpClient.Send(msg);
                resp = await response2.Content.ReadAsStringAsync();
                respObj = JsonConvert.DeserializeObject<dynamic>(resp);
                processStatus = (string) respObj["status"];
            }

            JArray sents = respObj["tasks"]["items"][0]["results"]["documents"][0]["sentences"];
            StringBuilder sb = new StringBuilder();
            foreach (JObject sent in sents)
            {
                sb.Append($"{sent["text"]}{Environment.NewLine}");
            }
            return sb.ToString();
        }

        /// <summary>
        /// Translation Service
        /// </summary>
        /// <param name="param"></param>
        /// <param name="log"></param>
        /// <returns></returns>
        [FunctionName("TextAnalysis_TranslationActivity")]
        public static async Task<string> Translation([ActivityTrigger] DurableParam param, ILogger log)
        {
            HttpRequestMessage msg = new HttpRequestMessage();
            msg.Method = HttpMethod.Post;
            msg.RequestUri = new Uri(param.Url);
            msg.Headers.Add("Ocp-Apim-Subscription-Key", param.Key);
            msg.Headers.Add("Ocp-Apim-Subscription-Region", param.Region);

            log.LogInformation($"Json for Translation\n{param.JsonBody}");
            StringContent content = new StringContent(param.JsonBody, Encoding.UTF8, "application/json");
            content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
            msg.Content = content;

            HttpResponseMessage response = _httpClient.Send(msg);
            string resp = await response.Content.ReadAsStringAsync();
            log.LogInformation($"Translation Service Return\n{resp}");
            dynamic respObj = JsonConvert.DeserializeObject<dynamic>(resp);
            string outputText = (string)respObj[0]["translations"][0]["text"];
            return outputText;
        }

        /// <summary>
        /// Http Entry Point for Text Services
        /// </summary>
        /// <param name="req"></param>
        /// <param name="starter"></param>
        /// <param name="log"></param>
        /// <returns></returns>
        [FunctionName("TextAnalysis_HttpStart")]
        public static async Task<IActionResult> HttpStart(
            [HttpTrigger(AuthorizationLevel.Function, "post")] HttpRequest req,
            [DurableClient] IDurableOrchestrationClient starter,
            ILogger log)
        {
            // process all headers
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
            int chunkSize = 5000;
            string[] splitors = new string[] { "\r\n", "\r", "\n" };

            if (req.Headers.ContainsKey("Ocp-Apim-Subscription-Chunk-Size"))
            {
                chunkSize = int.TryParse(req.Headers["Ocp-Apim-Subscription-Chunk-Size"], out chunkSize) ? chunkSize : 5000;
            }

            if (chunkSize > 5000 || chunkSize < 500)
            {
                return new BadRequestObjectResult("Ocp-Apim-Subscription-Chunk-Size must an integer between 500 and 5000 inclusive");
            }

            if (req.Headers.ContainsKey("Ocp-Apim-Subscription-Splitors"))
            {
                string extraSplitorRaw = req.Headers["Ocp-Apim-Subscription-Splitors"];
                string[] extraSplitors = extraSplitorRaw.Split(',', StringSplitOptions.RemoveEmptyEntries);
                splitors = splitors.Union(extraSplitors).ToArray();
            }

            // copy body 
            using (MemoryStream ms = new MemoryStream())
            {
                req.Body.CopyTo(ms);
                json = Encoding.UTF8.GetString(ms.ToArray());
            }

            // compose durable parameter
            DurableParam param = new DurableParam()
            {
                JsonBody = json,
                Key = key,
                Region = region,
                Method = methog,
                Url = url,
                ChunkSize = chunkSize,
                Splitors = splitors
            };

            // Function input comes from the request content.
            string instanceId = await starter.StartNewAsync<DurableParam>("TextAnalysis_Orchestrator", param);

            log.LogInformation($"Started orchestration with ID = '{instanceId}'.");

            return starter.CreateCheckStatusResponse(req, instanceId);
        }
    }
}
