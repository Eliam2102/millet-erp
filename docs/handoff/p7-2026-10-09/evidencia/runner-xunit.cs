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
using var controller = new XunitFrontController(AppDomainSupport.Denied, path, shadowCopy: false);
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
