namespace BindProxy.Core.Launch;

/// <summary>
/// Internal option, not exposed in the UI yet. UserDefault omits --user-data-dir; note that
/// Chromium silently ignores --proxy-server when an instance with that profile is already
/// running, so UserDefault only works when the browser is fully closed.
/// </summary>
public enum ProfileMode
{
    Isolated,
    UserDefault,
}
