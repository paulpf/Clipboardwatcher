using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace ClipboardWatcher.Service;

internal sealed class AgentLauncher
{
    private readonly ILogger _logger;

    public AgentLauncher(ILogger logger)
    {
        _logger = logger;
    }

    public void EnsureAgentRunningInActiveSession()
    {
        var agentPath = GetAgentExecutablePath();
        if (!File.Exists(agentPath))
        {
            throw new FileNotFoundException("ClipboardWatcher.Agent.exe wurde nicht gefunden.", agentPath);
        }

        var activeSessionId = NativeMethods.WTSGetActiveConsoleSessionId();
        if (activeSessionId == uint.MaxValue)
        {
            _logger.LogDebug("Keine aktive Benutzer-Session vorhanden.");
            return;
        }

        if (IsAgentAlreadyRunning(agentPath, (int)activeSessionId))
        {
            return;
        }

        StartAgentInSession(agentPath, activeSessionId);
        _logger.LogInformation("Agent in Session {SessionId} gestartet.", activeSessionId);
    }

    private static string GetAgentExecutablePath()
    {
        return Path.Combine(AppContext.BaseDirectory, "ClipboardWatcher.Agent.exe");
    }

    private static bool IsAgentAlreadyRunning(string executablePath, int sessionId)
    {
        var processName = Path.GetFileNameWithoutExtension(executablePath);
        var processes = Process.GetProcessesByName(processName);

        foreach (var process in processes)
        {
            using (process)
            {
                try
                {
                    if (process.SessionId == sessionId)
                    {
                        return true;
                    }
                }
                catch (InvalidOperationException)
                {
                    // Der Prozess wurde waehrend der Abfrage beendet.
                }
            }
        }

        return false;
    }

    private static void StartAgentInSession(string executablePath, uint sessionId)
    {
        if (!NativeMethods.WTSQueryUserToken(sessionId, out var impersonationToken))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "WTSQueryUserToken fehlgeschlagen.");
        }

        try
        {
            const uint tokenAccess =
                NativeMethods.TokenAssignPrimary |
                NativeMethods.TokenDuplicate |
                NativeMethods.TokenQuery |
                NativeMethods.TokenAdjustDefault |
                NativeMethods.TokenAdjustSessionId;

            if (!NativeMethods.DuplicateTokenEx(
                    impersonationToken,
                    tokenAccess,
                    IntPtr.Zero,
                    NativeMethods.SecurityImpersonation,
                    NativeMethods.TokenPrimary,
                    out var primaryToken))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "DuplicateTokenEx fehlgeschlagen.");
            }

            try
            {
                var startupInfo = new NativeMethods.STARTUPINFO
                {
                    cb = Marshal.SizeOf<NativeMethods.STARTUPINFO>(),
                    lpDesktop = @"winsta0\default"
                };

                var workingDirectory = Path.GetDirectoryName(executablePath) ?? AppContext.BaseDirectory;

                if (!NativeMethods.CreateProcessAsUser(
                        primaryToken,
                        executablePath,
                        null,
                        IntPtr.Zero,
                        IntPtr.Zero,
                        false,
                        NativeMethods.CreateUnicodeEnvironment,
                        IntPtr.Zero,
                        workingDirectory,
                        ref startupInfo,
                        out var processInformation))
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "CreateProcessAsUser fehlgeschlagen.");
                }

                NativeMethods.CloseHandle(processInformation.hThread);
                NativeMethods.CloseHandle(processInformation.hProcess);
            }
            finally
            {
                NativeMethods.CloseHandle(primaryToken);
            }
        }
        finally
        {
            NativeMethods.CloseHandle(impersonationToken);
        }
    }
}

internal static class NativeMethods
{
    internal const uint TokenAssignPrimary = 0x0001;
    internal const uint TokenDuplicate = 0x0002;
    internal const uint TokenQuery = 0x0008;
    internal const uint TokenAdjustDefault = 0x0080;
    internal const uint TokenAdjustSessionId = 0x0100;
    internal const int SecurityImpersonation = 2;
    internal const int TokenPrimary = 1;
    internal const uint CreateUnicodeEnvironment = 0x00000400;

    [DllImport("kernel32.dll")]
    internal static extern uint WTSGetActiveConsoleSessionId();

    [DllImport("Wtsapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool WTSQueryUserToken(uint sessionId, out IntPtr token);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool DuplicateTokenEx(
        IntPtr existingToken,
        uint desiredAccess,
        IntPtr tokenAttributes,
        int impersonationLevel,
        int tokenType,
        out IntPtr newToken);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool CreateProcessAsUser(
        IntPtr token,
        string? applicationName,
        string? commandLine,
        IntPtr processAttributes,
        IntPtr threadAttributes,
        bool inheritHandles,
        uint creationFlags,
        IntPtr environment,
        string currentDirectory,
        ref STARTUPINFO startupInfo,
        out PROCESS_INFORMATION processInformation);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool CloseHandle(IntPtr handle);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct STARTUPINFO
    {
        internal int cb;
        internal string? lpReserved;
        internal string? lpDesktop;
        internal string? lpTitle;
        internal int dwX;
        internal int dwY;
        internal int dwXSize;
        internal int dwYSize;
        internal int dwXCountChars;
        internal int dwYCountChars;
        internal int dwFillAttribute;
        internal int dwFlags;
        internal short wShowWindow;
        internal short cbReserved2;
        internal IntPtr lpReserved2;
        internal IntPtr hStdInput;
        internal IntPtr hStdOutput;
        internal IntPtr hStdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct PROCESS_INFORMATION
    {
        internal IntPtr hProcess;
        internal IntPtr hThread;
        internal int dwProcessId;
        internal int dwThreadId;
    }
}
