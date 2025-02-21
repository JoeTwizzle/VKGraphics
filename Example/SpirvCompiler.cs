using OpenTK.Platform;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Example;
internal static class SpirvCompiler
{
    public static byte[] GetSpirvBytes(string shaderSourceFile)
    {
        shaderSourceFile = Path.GetFullPath(shaderSourceFile);
        var outputFile = Path.ChangeExtension(shaderSourceFile, ".spv");
        if (!File.Exists(outputFile) || File.GetLastWriteTime(shaderSourceFile) > File.GetLastWriteTime(outputFile))
        {
            Console.WriteLine($"Recompiling shader {shaderSourceFile}");
            if (shaderSourceFile.EndsWith(".slang"))
            {
                if (!CompileSlangToSpirv(shaderSourceFile))
                {
                    throw new InvalidOperationException("Error compiling spirv shader");
                }
            }
            else if (!CompileGlslToSpirv(shaderSourceFile))
            {
                throw new InvalidOperationException("Error compiling spirv shader");
            }
        }
        return File.ReadAllBytes(outputFile);
    }

    public static bool CompileGlslToSpirv(string glslFile)
    {
        glslFile = Path.GetFullPath(glslFile);
        var outputFile = Path.ChangeExtension(glslFile, ".spv");
        string stageArg =
            glslFile.EndsWith(".vert") ? " -V -S vert " :
            glslFile.EndsWith(".frag") ? " -V -S frag " :
            glslFile.EndsWith(".comp") ? " -V -S comp " : "";

        ProcessStartInfo psi = new()
        {
            FileName = "glslangValidator.exe",
            Arguments = $"{stageArg} -o \"{outputFile}\" \"{glslFile}\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using Process process = new() { StartInfo = psi };
        process.ErrorDataReceived += (o, e) =>
        {
            var prevColor = Console.ForegroundColor;
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"{e.Data}");
            Console.ForegroundColor = prevColor;
        };
        process.OutputDataReceived += (o, e) =>
        {
            Console.WriteLine($"{e.Data}");
        };
        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        process.WaitForExit();

        return process.ExitCode == 0;
    }
    public static bool CompileSlangToSpirv(string slangFile)
    {
        slangFile = Path.GetFullPath(slangFile);
        var outputFile = Path.ChangeExtension(slangFile, ".spv");

        string args = $"{slangFile} ";
#if DEBUG
        args += "-g3 -O0";
#endif
        args += $" -o {outputFile}";
        ProcessStartInfo psi = new()
        {
            FileName = "slangc.exe",
            Arguments = args,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using Process process = new() { StartInfo = psi };
        process.ErrorDataReceived += (o, e) =>
        {
            var prevColor = Console.ForegroundColor;
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"{e.Data}");
            Console.ForegroundColor = prevColor;
        };
        process.OutputDataReceived += (o, e) =>
        {
            Console.WriteLine($"{e.Data}");
        };
        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        process.WaitForExit();

        return process.ExitCode == 0;
    }

}
