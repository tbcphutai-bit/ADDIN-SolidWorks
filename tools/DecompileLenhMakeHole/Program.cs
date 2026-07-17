using ICSharpCode.Decompiler;
using ICSharpCode.Decompiler.CSharp;
using ICSharpCode.Decompiler.TypeSystem;

var inputDll = args.Length > 0
    ? args[0]
    : @"C:\SGN26\addin\ADDIN\bin\x64\Debug\ADDIN.dll";
var outputFile = args.Length > 1
    ? args[1]
    : @"C:\Users\SGN26\Documents\addin\LenhMakeHole.decompiled.cs";

var settings = new DecompilerSettings
{
    ThrowOnAssemblyResolveErrors = false,
};

var decompiler = new CSharpDecompiler(inputDll, settings);
var code = decompiler.DecompileTypeAsString(new FullTypeName("ADDIN.Commands.LenhMakeHole"));

Directory.CreateDirectory(Path.GetDirectoryName(outputFile)!);
File.WriteAllText(outputFile, code);
Console.WriteLine(outputFile);
Console.WriteLine(code.Length);
