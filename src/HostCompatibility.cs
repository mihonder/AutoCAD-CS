using Autodesk.AutoCAD.Runtime;
using System.IO;
using Acad=Autodesk.AutoCAD.ApplicationServices.Core.Application;

[assembly: ExtensionApplication(typeof(CadDustLab.HostCompatibility))]
[assembly: CommandClass(typeof(CadDustLab.Commands))]

namespace CadDustLab;

public sealed class HostCompatibility:IExtensionApplication
{
    internal static string LogDirectory
    {
        get
        {
            string path=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"CS16","logs","v1.0.1",Major.ToString());
            Directory.CreateDirectory(path);return path;
        }
    }
#if ACAD2020
    public const string Family="2020 / .NET Framework 4.7";
    private const int Major=23,MinorMin=1,MinorMax=1;
#elif ACAD2021
    public const string Family="2021-2024 / .NET Framework 4.8";
    private const int Major=24,MinorMin=0,MinorMax=3;
#elif ACAD2025
    public const string Family="2025-2026 / .NET 8";
    private const int Major=25,MinorMin=0,MinorMax=1;
#elif ACAD2027
    public const string Family="2027 / .NET 10";
    private const int Major=26,MinorMin=0,MinorMax=0;
#else
#error Choose an AutoCAD target framework.
#endif
    public void Initialize()=>Acad.DocumentManager.MdiActiveDocument?.Editor.WriteMessage("\nCS 1.6 v1.0.1: "+Family+". Command: CS16.\n");
    public void Terminate(){ } // Walk releases native state in AutoCAD.BeginQuit.
    internal static bool Check()
    {
        string raw=Convert.ToString(Acad.GetSystemVariable("ACADVER"))??"";
        string[] parts=raw.Split('.');
        bool known=parts.Length>=2&&int.TryParse(parts[0],out int major)&&int.TryParse(new string(parts[1].TakeWhile(char.IsDigit).ToArray()),out int minor)&&major==Major&&minor>=MinorMin&&minor<=MinorMax;
        if(known&&IntPtr.Size==8)return true;
        Acad.DocumentManager.MdiActiveDocument?.Editor.WriteMessage("\nCS16: this module is for AutoCAD "+Family+" x64; current ACADVER="+raw+". Use the CS16 bundle loader.\n");
        return false;
    }
}
