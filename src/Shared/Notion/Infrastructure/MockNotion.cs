using System.Net;
using System.Text;
using System.Text.Json.Nodes;

namespace Notion.Infrastructure;

// Mock mode: one Notion task page with its status and its result, and one question page that can block the task.
public sealed class MockNotion {
    public static readonly Uri BaseAddress = new("https://notion.mock.invalid/");

    // The ID of the mock task page.
    public const string TaskPageId = "00000000-0000-4000-8000-0000000000aa";

    // The ID of the mock question page. Its Blocks relation holds the mock task.
    public const string QuestionPageId = "00000000-0000-4000-8000-0000000000bb";

    // The data source of the Tasks database (Workflow / Tasks). The Tasks module has the same default.
    public const string TasksDataSource = "f0db2108-3a1e-4e80-8f74-f081643e0a29";

    // The data source of the Questions database (Workflow / Questions). The Tasks module has the same default.
    public const string QuestionsDataSource = "92bfb731-ceef-41eb-a0fa-fde31c7d8138";

    public string Status { get; set; } = "Backlog";

    public string Result { get; set; } = "";

    // True if the task has the mock question in its Questions relation.
    public bool TaskHasQuestion { get; set; }

    public string QuestionStatus { get; set; } = "Open";

    public string Answer { get; set; } = "";
}

// Answers the Notion API for the mock task page and the mock question page.
public sealed class MockNotionHandler(MockNotion state) : HttpMessageHandler {
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) {
        var path = request.RequestUri!.AbsolutePath;
        var isQuestion = path.Contains(MockNotion.QuestionPageId, StringComparison.Ordinal);
        if (request.Method == HttpMethod.Patch && request.Content is not null) {
            var body = JsonNode.Parse(await request.Content.ReadAsStringAsync(cancellationToken));
            if (body?["properties"]?["Status"]?["status"]?["name"]?.GetValue<string>() is { } status) {
                if (isQuestion) {
                    state.QuestionStatus = status;
                } else {
                    state.Status = status;
                }
            }
            if (!isQuestion && body?["properties"]?["Result"]?["rich_text"] is JsonArray text) {
                state.Result = string.Concat(text.Select(t => t?["text"]?["content"]?.GetValue<string>()));
            }
            return Json("{}");
        }
        if (path.EndsWith("/markdown", StringComparison.Ordinal)) {
            return Json("""{"object":"page_markdown","markdown":"Mock task. Reply with OK."}""");
        }
        if (isQuestion) {
            return Json($$"""
                {"object":"page","id":"{{MockNotion.QuestionPageId}}","url":"https://www.notion.so/mock-question",
                 "parent":{"type":"data_source_id","data_source_id":"{{MockNotion.QuestionsDataSource}}"},
                 "properties":{
                   "Name":{"type":"title","title":[{"plain_text":"Mock question"}] },
                   "Status":{"type":"status","status":{"name":"{{state.QuestionStatus}}"} },
                   "Context":{"type":"rich_text","rich_text":[{"plain_text":"Mock context"}]},
                   "Answer":{"type":"rich_text","rich_text":[{"plain_text":{{JsonValue.Create(state.Answer).ToJsonString()}}}]},
                   "Blocks":{"type":"relation","relation":[{"id":"{{MockNotion.TaskPageId}}"}]} } }
                """);
        }
        var questions = state.TaskHasQuestion ? $$"""[{"id":"{{MockNotion.QuestionPageId}}"}]""" : "[]";
        return Json($$"""
            {"object":"page","id":"{{MockNotion.TaskPageId}}","url":"https://www.notion.so/mock-task","token":"abc",
             "parent":{"type":"data_source_id","data_source_id":"{{MockNotion.TasksDataSource}}"},
             "properties":{
               "Name":{"type":"title","title":[{"plain_text":"Mock task"}] },
               "Status":{"type":"status","status":{"name":"{{state.Status}}"} },
               "Folder":{"type":"rich_text","rich_text":[]},
               "Questions":{"type":"relation","relation":{{questions}}} } }
            """);
    }

    static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
}
