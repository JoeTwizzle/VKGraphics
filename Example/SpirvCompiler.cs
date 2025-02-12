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
    public static byte[] GetSpirvBytes(string glslFile)
    {
        glslFile = Path.GetFullPath(glslFile);
        var outputFile = Path.ChangeExtension(glslFile, ".spv");
        if (!File.Exists(outputFile) || File.GetLastWriteTime(glslFile) > File.GetLastWriteTime(outputFile))
        {
            Console.WriteLine("XD");
            if (!CompileGlslToSpirv(glslFile))
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
        process.Start();
        process.WaitForExit();
        string output = process.StandardOutput.ReadToEnd();
        string error = process.StandardError.ReadToEnd();

        return process.ExitCode == 0;
    }

}
