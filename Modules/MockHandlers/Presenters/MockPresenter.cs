using MockHandlers.Slices.SetMockDelay;
using VSA.Presenters;

namespace MockHandlers.Presenters;

// Mock mode: the controls of the mock handlers for the self-test.
public sealed class MockPresenter(IBus bus) : Presenter(bus) {
    // Sets the delay of a mock handler, for example MockSlow. Returns the finished runs, as "<handler>:<event id>".
    public async Task<IReadOnlyList<string>> SetDelayAsync(string handler, int milliseconds) =>
        (await Bus.Send(new SetMockDelayRequest(handler, milliseconds))).Runs;

    public Task<IReadOnlyList<string>> RunsAsync() => SetDelayAsync("", 0);
}
