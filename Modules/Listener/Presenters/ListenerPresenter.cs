using Listener.Slices.GetListenerState;
using Listener.Slices.Listen;
using Listener.Slices.SendMockEvent;
using Listener.Slices.SetListener;
using Listener.Slices.SetMockNetwork;
using VSA.Presenters;

namespace Listener.Presenters;

// The listener switch and state, for the header of each page and the tray icon (C1, C6).
public sealed class ListenerPresenter(IBus bus) : Presenter(bus), IPresenterEvent<ListenerStateChanged> {
    public OpState Load { get; } = new();

    public bool On { get; private set; }

    // Paused, Connecting, Connected or Error.
    public string State { get; private set; } = "Paused";

    public string? Reason { get; private set; }

    public string Consumer { get; private set; } = "";

    public bool Mock { get; private set; }

    public bool Loaded { get; private set; }

    public string Subtitle => State switch {
        "Connected" => $"Connected · {Consumer}",
        "Connecting" => "Connecting…",
        "Error" => $"Error: {Reason}",
        _ => "Paused",
    };

    public Task LoadAsync() => Run(Load, async () => Apply(await Bus.Send(new GetListenerStateRequest())));

    public Task ToggleAsync() => Run(Load, async () => {
        On = (await Bus.Send(new SetListenerRequest(!On))).On;
    });

    public Task SendMockAsync(string kind) => Run(Load, () => Bus.Send(new SendMockEventRequest(kind)));

    public Task SetMockNetworkAsync(bool up) => Run(Load, () => Bus.Send(new SetMockNetworkRequest(up)));

    public async Task OnEventAsync(ListenerStateChanged integrationEvent, CancellationToken ct) {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        State = integrationEvent.State;
        Reason = integrationEvent.Reason;
        await LoadAsync();
    }

    void Apply(GetListenerStateResponse state) {
        On = state.On;
        State = state.State;
        Reason = state.Reason;
        Consumer = state.Consumer;
        Mock = state.Mock;
        Loaded = true;
    }
}
