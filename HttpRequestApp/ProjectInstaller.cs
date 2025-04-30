using System;
using System.ComponentModel;
using System.Configuration.Install;
using System.ServiceProcess;
using System.Diagnostics;

[RunInstaller(true)]
public class ProjectInstaller : Installer
{
    public override void Install(System.Collections.IDictionary stateSaver)
    {
        base.Install(stateSaver);

        string exePath = Context.Parameters["assemblypath"];
        string serviceName = "HttpRequestService";

        // Create the service
        ExecuteCommand($"create {serviceName} binPath= \"{exePath}\" start= auto", true);
        // Start the service
        ExecuteCommand($"start {serviceName}", true);
    }

    public override void Uninstall(System.Collections.IDictionary savedState)
    {
        base.Uninstall(savedState);

        string serviceName = "HttpRequestService";

        // Stop and delete the service
        ExecuteCommand($"stop {serviceName}", false);
        ExecuteCommand($"delete {serviceName}", true);
    }

    private void ExecuteCommand(string arguments, bool throwOnError)
    {
        using (Process process = new Process())
        {
            process.StartInfo.FileName = "sc.exe";
            process.StartInfo.Arguments = arguments;
            process.StartInfo.CreateNoWindow = true;
            process.StartInfo.UseShellExecute = false;
            process.Start();

            process.WaitForExit();

            if (throwOnError && process.ExitCode != 0)
            {
                throw new Exception($"Command 'sc {arguments}' failed with exit code {process.ExitCode}");
            }
        }
    }
}
