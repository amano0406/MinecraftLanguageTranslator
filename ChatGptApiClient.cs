using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Serilog;
using System.Net.Http.Headers;
using System.Text;

public class ChatGptApiClient
{
    private readonly HttpClient _httpClient;
    private readonly string _apiUrl = "https://api.openai.com/v1/chat/completions";
    private readonly string _secretKey = null!;
    private readonly int _maxTryCount = 3;

    public ChatGptApiClient(string secretKey)
    {
        this._httpClient = new HttpClient();

        this._httpClient.Timeout = TimeSpan.FromSeconds(60 * 5);

        this._secretKey = secretKey;
    }

    public async Task<JObject> TranslateAsync(string userPrompt, string systemPrompt)
    {
        JObject requestData = this.CreateRequestData(systemPrompt, userPrompt);

        for (var tryCount = 1; tryCount < this._maxTryCount; tryCount++)
        {
            var response = await this.SendRequestAsync(requestData);
            var responseContent = await response.Content.ReadAsStringAsync();

            if (response.IsSuccessStatusCode)
            {
                var jsonResponse = JObject.Parse(responseContent);
                var result = jsonResponse["choices"]![0]!["message"]!["content"]!.ToString();
                Log.Information(result);

                var startIndex = result.IndexOf('{');

                var endIndex = result.LastIndexOf('}');

                var json = result.Substring(startIndex, endIndex - startIndex + 1);

                if (json is not null)
                {
                    try
                    {
                        return JsonConvert.DeserializeObject<JObject>(json)!;
                    }
                    catch
                    {
                        throw new Exception("The preValue is not in JSON format.");
                    }
                }
                else if (tryCount < (this._maxTryCount - 1))
                {
                    ((JArray)requestData["messages"]!).Add(
                        new JObject
                        {
                            { "role", "assistant" },
                            { "content", result }
                        }
                    );
                    ((JArray)requestData["messages"]!).Add(
                        new JObject
                        {
                            { "role", "user" },
                            { "content", "Please continue to execute" }
                        }
                    );
                }
                else
                {
                    throw new Exception("The expected value was not obtained.");
                }
            }
            else
            {
                throw new Exception("API request failed.");
            }
        }

        throw new Exception("The maximum number of attempts has been reached.");
    }

    private JObject CreateRequestData(string systemPrompt, string userPrompt)
    {
        return new JObject
        {
            { "model", "gpt-4-1106-preview" },// TODO: Get from ini https://platform.openai.com/docs/models
            { "messages", new JArray
                {
                    new JObject
                    {
                        { "role", "system" },
                        { "content", systemPrompt }
                    },
                    new JObject
                    {
                        { "role", "user" },
                        { "content", userPrompt }
                    }
                }
            }
        };
    }

    private async Task<HttpResponseMessage> SendRequestAsync(JObject requestData)
    {
        var jsonContent = JsonConvert.SerializeObject(requestData);
        var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");
        this._httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", this._secretKey);

        return await this._httpClient.PostAsync(this._apiUrl, content);
    }
}
