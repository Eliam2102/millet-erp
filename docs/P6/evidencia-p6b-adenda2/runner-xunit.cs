using System.Reflection;
using System.Runtime.Loader;
using Xunit;
using Xunit.Abstractions;
var path = Path.GetFullPath(args[0]);
AppContext.SetData("APP_CONTEXT_BASE_DIRECTORY", Path.GetDirectoryName(path)! + Path.DirectorySeparatorChar);
var resolver = new AssemblyDependencyResolver(path);
AssemblyLoadContext.Default.Resolving += (context, name) => {
 var resolved = resolver.ResolveAssemblyToPath(name) ?? Path.Combine(Path.GetDirectoryName(path)!, name.Name + ".dll");
 return File.Exists(resolved) ? context.LoadFromAssemblyPath(resolved) : null;
};
AssemblyLoadContext.Default.ResolvingUnmanagedDll += (assembly, name) => {
 var resolved = resolver.ResolveUnmanagedDllToPath(name);
 if (resolved is null) {
  var native = Path.Combine(Path.GetDirectoryName(path)!, "runtimes", System.Runtime.InteropServices.RuntimeInformation.RuntimeIdentifier, "native");
  foreach (var file in new[]{name, name + ".dylib", "lib" + name + ".dylib"})
   if (File.Exists(Path.Combine(native, file))) { resolved = Path.Combine(native, file); break; }
 }
 return resolved is null ? IntPtr.Zero : System.Runtime.InteropServices.NativeLibrary.Load(resolved);
};
using var controller = new XunitFrontController(AppDomainSupport.Denied, path, shadowCopy: false);
if (args.Length > 1 && args[1] == "--descubrir") {
 var discovery = new DiscoverySink();
 var discoveryOptions = TestFrameworkOptions.ForDiscovery();
 discoveryOptions.SetValue("xunit.discovery.PreEnumerateTheories", true);
 controller.Find(false, discovery, discoveryOptions);
 await discovery.Done.Task;
 Console.WriteLine($"P7_DESCUBIERTOS={discovery.Count}; NO_EJECUTADOS");
 return 0;
}
var sink = new Sink();
var options = TestFrameworkOptions.ForExecution();
options.SetValue("xunit.execution.DisableParallelization", true);
controller.RunAll(sink, TestFrameworkOptions.ForDiscovery(), options);
var result = await sink.Done.Task;
Console.WriteLine($"TOTAL={result.TestsRun} FAILED={result.TestsFailed} SKIPPED={result.TestsSkipped}");
return result.TestsFailed > 0 ? 1 : 0;
sealed class Sink : IMessageSink {
 public TaskCompletionSource<ITestAssemblyFinished> Done {get;} = new();
 public bool OnMessage(IMessageSinkMessage msg) {
  if(msg is ITestFailed f) Console.WriteLine("FAIL " + f.Test.DisplayName + " " + string.Join(" | ", f.Messages) + "\n" + string.Join("\n", f.StackTraces));
  if(msg is IErrorMessage e) Console.WriteLine("ERROR " + string.Join(" | ",e.Messages));
  if(msg is ITestAssemblyFinished r) Done.TrySetResult(r);
  return true;
 }
}

sealed class DiscoverySink : IMessageSink {
 public int Count {get; private set;}
 public TaskCompletionSource<bool> Done {get;} = new();
 public bool OnMessage(IMessageSinkMessage msg) {
  if (msg is ITestCaseDiscoveryMessage t && t.TestCase.TestMethod.TestClass.Class.Name.Contains("P6SucursalEndpointsTests") && t.TestCase.TestMethod.Method.Name.StartsWith("P7_")) { Count++; Console.WriteLine(t.TestCase.DisplayName); }
  if (msg is IDiscoveryCompleteMessage) Done.TrySetResult(true);
  return true;
 }
}
