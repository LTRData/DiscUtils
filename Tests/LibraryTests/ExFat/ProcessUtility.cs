// This is ExFat, an exFAT accessor written in pure C#
// Released under MIT license
// https://github.com/picrap/ExFat

using System;
using System.Diagnostics;
using System.Text;

namespace LibraryTests.ExFat;
/// <summary>
/// Extensions to Process
/// </summary>
public static class ProcessUtility
{
    /// <summary>
    /// Runs the specified command and returns the literal result.
    /// </summary>
    /// <param name="command">The command.</param>
    /// <param name="arguments">The arguments.</param>
    /// <param name="input">The input.</param>
    /// <param name="waitForExit">if set to <c>true</c> [wait for exit].</param>
    /// <returns>Either an exit code or a PID (when waitForExit is false)</returns>
    /// <exception cref="ProcessStartInfo.FileName">The file specified in the command parameter <see cref="T:System.IO.FileNotFoundException" /> property could not be found.</exception>
    public static Tuple<int, string?> Run(string command, string arguments, string? input = null,
        bool waitForExit = true)
    {
        var process = new Process
        {
            StartInfo = new ProcessStartInfo(command, arguments)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
            }
        };

        var resultBuilder = new StringBuilder();
        process.OutputDataReceived += delegate(object sender, DataReceivedEventArgs e)
        {
            if (e.Data != null)
            {
                resultBuilder.AppendLine(e.Data);
            }
        };

        try
        {
            process.Start();
        }
        catch
        {
            return Tuple.Create(-1, (string?)null);
        }

        if (input != null)
        {
            process.StandardInput.WriteLine(input);
            process.StandardInput.Close();
        }

        if (!waitForExit)
        {
            return Tuple.Create(process.Id, (string?)null);
        }

        process.BeginOutputReadLine();
        process.WaitForExit();
        return Tuple.Create(process.ExitCode, (string?)resultBuilder.ToString());
    }
}