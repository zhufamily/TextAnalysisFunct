# TextAnalysisFunct
## A Simple Azure Durable Function Wrapper for Azure Text Analysis
For Azure Cognitive Services for Text Analysis, there is a limitation for 5,120 characters.  With this restriction, Text Analysis Services are hardly useful for anything longer than a couple page.  This wrapper based on Azure Durable Function, essentially, gets rid of this obstacle by converting Text Analysis into an async service.
## Description of Technical Approach
At the core of the Azure Durable Function, 
1. long text is chopped into chunks by parapragh or splitors you selected
2. chunks are sent to Azure Text Analysis Services for processing piece by piece (the code is tested on a very limited Azure resource, so all calls are sequential, if your resources allow, you can easily rewrite this into a fan-out and fan-in parallel model)
3. results from each chunk are combined into a final result 
## Setup
In order to set this up
1. download the source codes
2. complie into binary with VS2022
3. publish into Azure funcion wuth .Net6
## Usage
To create a test application
1. init a http client
2. add headers 
 - Ocp-Apim-Subscription-Key (required)
 - Ocp-Apim-Subscription-Region (required)
 - Ocp-Apim-Subscription-Url (required)
 - Ocp-Apim-Subscription-Method (required)
 - Ocp-Apim-Subscription-Chunk-Size (optional - default: 5,000)
 - Ocp-Apim-Subscription-Splitors (optional - character return and line feed are always there)
3. hash out a json body with long text
```
{
    "displayName": "Document Abs Summarization Task Example",
    "analysisInput": {
		"documents": [
			{
				"id": 1,
				"text": "something very long"
			}
		]
	},
    "tasks":[{
        "kind": "ExtractiveSummarization",
        "parameters": {
            "sentenceCount": 3
        },
        "taskName": "Document Abs Summarization Task"
    }]
}
```
4. point to Azure Function entry point
5. query Azure Status Uri
6. when complete, read results
## Services Supported
At the moment, the following services are supported
- Language detect
- Key phrase extraction
- Entity extraction
- PII redaction
- Summarization (extractive / substractive)
- Translation (coming soon)
## Sample Test Codes
