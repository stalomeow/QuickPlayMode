using Mono.Cecil;
using Mono.Cecil.Cil;
using System;
using System.IO;
using System.Linq;
using Unity.CompilationPipeline.Common.ILPostProcessing;

namespace EasyTypeReload.CodeGen
{
    internal class EasyTypeReloadILPostProcessor : ILPostProcessor
    {
        public override ILPostProcessor GetInstance() => this;

        public override bool WillProcess(ICompiledAssembly compiledAssembly)
        {
            // Editor Only
            if (!compiledAssembly.Defines.Contains("UNITY_EDITOR"))
            {
                return false;
            }

            if (compiledAssembly.Name == "EasyTypeReload.Editor")
            {
                return false;
            }

            return compiledAssembly.References.Any(f => Path.GetFileName(f) == "EasyTypeReload.dll");
        }

        public override ILPostProcessResult Process(ICompiledAssembly compiledAssembly)
        {
            if (!WillProcess(compiledAssembly))
            {
                return new ILPostProcessResult(null);
            }

            AssemblyDefinition assembly = null;
            MemoryStream peStream = null;
            MemoryStream pdbStream = null;

            try
            {
                var inMemoryAssembly = compiledAssembly.InMemoryAssembly;
                peStream = new MemoryStream(inMemoryAssembly.PeData);
                pdbStream = new MemoryStream(inMemoryAssembly.PdbData);

                // For IL Post Processing, use the builtin symbol reader provider
                assembly = LoadAssembly(peStream, pdbStream, new PortablePdbReaderProvider());
                HookAssembly.Execute(assembly, out MethodDefinition registerUnloadMethod, out MethodDefinition registerLoadMethod);
                int hookedTypeCount = HookType.Execute(assembly, registerUnloadMethod, registerLoadMethod);

                if (hookedTypeCount <= 0)
                {
                    return new ILPostProcessResult(null);
                }

                return new ILPostProcessResult(WriteAssemblyToMemory(assembly));
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Internal compiler error for {nameof(EasyTypeReloadILPostProcessor)} on {compiledAssembly.Name}. Exception: {ex}");
            }
            finally
            {
                assembly?.Dispose();
                peStream?.Dispose();
                pdbStream?.Dispose();
            }
        }

        private static AssemblyDefinition LoadAssembly(Stream peStream, Stream pdbStream, ISymbolReaderProvider symbolReader = null)
        {
            peStream.Seek(0, SeekOrigin.Begin);
            pdbStream.Seek(0, SeekOrigin.Begin);

            var readerParameters = new ReaderParameters
            {
                InMemory = true,
                ReadingMode = ReadingMode.Deferred
            };

            if (symbolReader != null)
            {
                readerParameters.ReadSymbols = true;
                readerParameters.SymbolReaderProvider = symbolReader;
            }

            try
            {
                readerParameters.SymbolStream = pdbStream;
                return AssemblyDefinition.ReadAssembly(peStream, readerParameters);
            }
            catch
            {
                readerParameters.ReadSymbols = false;
                readerParameters.SymbolStream = null;
                peStream.Seek(0, SeekOrigin.Begin);
                pdbStream.Seek(0, SeekOrigin.Begin);
                return AssemblyDefinition.ReadAssembly(peStream, readerParameters);
            }
        }

        private static InMemoryAssembly WriteAssemblyToMemory(AssemblyDefinition assembly)
        {
            using var peStream = new MemoryStream();
            using var pdbStream = new MemoryStream();
            var writeParameters = new WriterParameters
            {
                SymbolWriterProvider = new PortablePdbWriterProvider(),
                WriteSymbols = true,
                SymbolStream = pdbStream
            };

            assembly.Write(peStream, writeParameters);
            return new InMemoryAssembly(peStream.ToArray(), pdbStream.ToArray());
        }

        private static void Log(string message)
        {
            Console.WriteLine($"{nameof(EasyTypeReloadILPostProcessor)}: {message}");
        }
    }
}
