using VSA.Contracts.Requests;

namespace Settings.Slices.SetSetting;

public sealed record SetSettingRequest(string Key, string Value) : IRequest<SetSettingResponse> {
    public static HttpMethod Method => HttpMethod.Post;
    public static string Route => "/api/settings";
}

public sealed record SetSettingResponse(string Key, string Value) : IResponse;
