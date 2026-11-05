namespace App.Views;

// The mode of the app: real or mock. App.Host implements it. A switch saves the mode and restarts the app in it.
public interface IAppMode {
    bool Mock { get; }

    void Switch(bool mock);
}
