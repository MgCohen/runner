using System.Net;
using System.Text;
using System.Text.Json.Nodes;

namespace NotionApi.Infrastructure;

// Mock mode: one Notion task page, with its status and its result.
public sealed class MockNotion {
    public static readonly Uri BaseAddress = new("https://notion.mock.invalid/");

    // The ID of the mock task page.
    public const string TaskPageId = "00000000-0000-4000-8000-0000000000aa";

    // The data source of the Tasks database (Workflow / Tasks). The Tasks module has the same default.
    public const string TasksDataSource = "f0db2108-3a1e-4e80-8f74-f081643e0a29";

    public string Status { get; set; } = "Backlog";

    public string Result { get; set; } = "";
}

// Answers the Notion API for the mock task page.
public sealed class MockNotionHandler(MockNotion state) : HttpMessageHandler {
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) {
        var path = request.RequestUri!.AbsolutePath;
        if (request.Method == HttpMethod.Patch && request.Content is not null) {
            var body = JsonNode.Parse(await request.Content.ReadAsStringAsync(cancellationToken));
            if (body?["properties"]?["Status"]?["status"]?["name"]?.GetValue<string>() is { } status) {
                state.Status = status;
            }
            if (body?["properties"]?["Result"]?["rich_text"] is JsonArray text) {
                state.Result = string.Concat(text.Select(t => t?["text"]?["content"]?.GetValue<string>()));
            }
            return Json("{}");
        }
        if (path.EndsWith("/markdown", StringComparison.Ordinal)) {
            return Json("""{"object":"page_markdown","markdown":"Mock task. Reply with OK."}""");
        }
        return Json($$"""
            {"object":"page","id":"{{MockNotion.TaskPageId}}","url":"https://www.notion.so/mock-task",
             "parent":{"type":"data_source_id","data_source_id":"{{MockNotion.TasksDataSource}}"},
             "properties":{
               "Name":{"type":"title","title":[{"plain_text":"Mock task"}] },
               "Status":{"type":"status","status":{"name":"{{state.Status}}"} },
               "Folder":{"type":"rich_text","rich_text":[]} } }
            """);
    }

    static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
}
