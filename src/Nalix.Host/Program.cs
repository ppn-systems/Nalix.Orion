// Copyright (c) 2025 PPN Corporation. All rights reserved.

using Nalix.Host.Runtime;
using Nalix.Host.Terminals;
using Nalix.Logging;

namespace Nalix.Host;

internal static class Program
{
    private static async System.Threading.Tasks.Task<System.Int32> Main(System.String[] args)
    {
        try
        {
            // Compose services manually (no external libs)
            SimpleHost host = new();

            ConsoleReader consoleReader = new();
            ShortcutManager shortcuts = new();

            TerminalService terminal = new(consoleReader, shortcuts, AppConfig.Listener);

            _ = host.AddService(terminal);

            await host.ActivateAsync().ConfigureAwait(false);

            // Wait until terminal sets ExitEvent (Ctrl+Q double-press)
            terminal.ExitEvent.Wait();

            await host.DeactivateAsync().ConfigureAwait(false);
            await host.DisposeAsync();

            return 0;
        }
        catch (System.Exception ex)
        {
            NLogix.Host.Instance.Fatal("Fatal error in host entry point.", ex);
            return -1;
        }
    }
}
