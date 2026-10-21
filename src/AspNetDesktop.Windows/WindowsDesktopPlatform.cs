using System.Runtime.ExceptionServices;
using System.Runtime.Versioning;
using AspNetDesktop.Hosting;

namespace AspNetDesktop.Windows;

/// <summary>
/// Windows adapter: a WinForms window hosting WebView2. <c>app://</c> and <c>ipc://</c> are registered as
/// secure custom schemes before the WebView2 environment is created and served through <c>WebResourceRequested</c>.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsDesktopPlatform : IDesktopPlatform
{
    private static int s_applicationInitialized;

    /// <inheritdoc />
    public string Name => "windows";

    /// <inheritdoc />
    public void Run(DesktopPlatformContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (Thread.CurrentThread.GetApartmentState() == ApartmentState.STA)
        {
            RunMessageLoop(context);
            return;
        }

        // Top-level statements cannot be marked [STAThread]; WinForms and WebView2 need an STA thread.
        ExceptionDispatchInfo? failure = null;
        Thread uiThread = new(() =>
        {
            try
            {
                RunMessageLoop(context);
            }
            catch (Exception ex)
            {
                failure = ExceptionDispatchInfo.Capture(ex);
            }
        })
        {
            Name = "AspNetDesktop UI",
        };

        uiThread.SetApartmentState(ApartmentState.STA);
        uiThread.Start();
        uiThread.Join();
        failure?.Throw();
    }

    private void RunMessageLoop(DesktopPlatformContext context)
    {
        if (Interlocked.Exchange(ref s_applicationInitialized, 1) == 0)
        {
            Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
        }

        using DesktopForm form = new(context, context.GetBootstrapScript(this));
        using CancellationTokenRegistration stopping = context.ApplicationStopping.Register(form.CloseFromAnyThread);
        Application.Run(form);
    }
}
