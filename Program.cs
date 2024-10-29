using Azure;
using Azure.AI.OpenAI;
using OpenAI.Chat;
using System.Text.Json;

var endpoint = new Uri("https://luiscocoaiservice.openai.azure.com/");
var credentials = new AzureKeyCredential("");
var deploymentName = "gpt-4o";

var openAIClient = new AzureOpenAIClient(endpoint, credentials);

var chatClient = openAIClient.GetChatClient(deploymentName);

static string GetCurrentLocation()
{
    // Call the location API here.
    return "San Francisco";
}

static string GetCurrentWeather(string location, string unit = "celsius")
{
    // Call the weather API here.
    return $"31 {unit}";
}

ChatTool getCurrentLocationTool = ChatTool.CreateFunctionTool(
    functionName: nameof(GetCurrentLocation),
    functionDescription: "Get the user's current location"
);

ChatTool getCurrentWeatherTool = ChatTool.CreateFunctionTool(
    functionName: nameof(GetCurrentWeather),
    functionDescription: "Get the current weather in a given location",
    functionParameters: BinaryData.FromString("""
    {
        "type": "object",
        "properties": {
            "location": {
                "type": "string",
                "description": "The city and state, Boston, MA"
            },
            "unit": {
                "type": "string",
                "enum": [ "celsius", "fahrenheit" ],
                "description": "The temperature unit to use. Infer this from the specified location."
            }
        },
        "required": [ "location" ]
    }
    """)
);

ChatCompletionOptions options = new()
{
    Tools = { getCurrentLocationTool, getCurrentWeatherTool },
};

List<ChatMessage> conversationMessages =
[
    new UserChatMessage("What's the weather like in Boston?"),
];

ChatCompletion completion = chatClient.CompleteChat(conversationMessages,options);

if (completion.FinishReason == ChatFinishReason.ToolCalls)
{
    // Add a new assistant message to the conversation history that includes the tool calls
    conversationMessages.Add(new AssistantChatMessage(completion));

    foreach (ChatToolCall toolCall in completion.ToolCalls)
    {
        conversationMessages.Add(new ToolChatMessage(toolCall.Id, GetToolCallContent(toolCall)));
    }

    // Now make a new request with all the messages thus far, including the original
    ChatCompletion updatedCompletion = chatClient.CompleteChat(conversationMessages);

    Console.WriteLine($"{updatedCompletion.Role}: {updatedCompletion.Content[0].Text}");
}

// Purely for convenience and clarity, this standalone local method handles tool call responses.
string GetToolCallContent(ChatToolCall toolCall)
{
    if (toolCall.FunctionName == getCurrentWeatherTool.FunctionName)
    {
        // Validate arguments before using them; it's not always guaranteed to be valid JSON!
        try
        {
            using JsonDocument argumentsDocument = JsonDocument.Parse(toolCall.FunctionArguments);
            if (!argumentsDocument.RootElement.TryGetProperty("location", out JsonElement locationElement))
            {
                // Handle missing required "location" argument
            }
            else
            {
                string location = locationElement.GetString();
                if (argumentsDocument.RootElement.TryGetProperty("unit", out JsonElement unitElement))
                {
                    return GetCurrentWeather(location, unitElement.GetString());
                }
                else
                {
                    return GetCurrentWeather(location);
                }
            }
        }
        catch (JsonException)
        {
            // Handle the JsonException (bad arguments) here
        }
    }
    // Handle unexpected tool calls
    throw new NotImplementedException();
}
