using Workspace.Contracts;
using Workspace.Runtime;
using System.Text.Json;

var count=0;
void Check(bool condition,string name) { if(!condition) throw new Exception(name); Console.WriteLine("PASS: " + name);count++; }
void Reject(Action action,string name) { try { action(); } catch(InvalidOperationException) { Check(true,name);return; } throw new Exception("Not rejected: "+name); }
var registry=new ModuleRegistry();
var runs=0;
registry.Register(new TestModule("test",1,new TestCommand("test.ok",()=>Task.FromResult(new CommandResult(true,(++runs).ToString())))));
Check((await registry.ExecuteAsync("test.ok")).Message=="1","registered command executes");
Reject(()=>registry.Register(new TestModule("test",1)),"duplicate module rejected");
Reject(()=>registry.Register(new TestModule("future",2)),"incompatible API rejected");
Reject(()=>registry.Register(new TestModule("atomic",1,new TestCommand("atomic.ok"),new TestCommand("atomic.ok"))),"duplicate commands rejected atomically");
Reject(()=>registry.Register(new TestModule("invalid.id",1)),"ambiguous module ID rejected");
Reject(()=>registry.Register(new TestModule("invalid",1,new TestCommand("invalid.bad id"))),"invalid command ID rejected");
Check(registry.Modules.Count==1&&registry.Commands.Count==1,"failed registration leaves registry unchanged");
Check(!(await registry.ExecuteAsync("missing")).Success,"missing command reports failure");
Check(!(await registry.ExecuteAsync("test.ok",new CancellationToken(true))).Success&&runs==1,"cancelled command does not run");
registry.Register(new TestModule("fault",1,new TestCommand("fault.run",()=>throw new InvalidOperationException("expected"))));
Check(!(await registry.ExecuteAsync("fault.run")).Success,"command exceptions converted to failure");
Check((await registry.ExecuteAsync("test.ok")).Success,"command gate released after failure");
var finish=new TaskCompletionSource<CommandResult>();
registry.Register(new TestModule("busy",1,new TestCommand("busy.run",()=>finish.Task)));
var pending=registry.ExecuteAsync("busy.run");
Check(!(await registry.ExecuteAsync("test.ok")).Success,"overlapping command blocked");
finish.SetResult(new(true,"done"));await pending;
var temp=Path.Combine(Path.GetTempPath(),"workspace-foundation-checks-"+Guid.NewGuid());Directory.CreateDirectory(temp);
try {
 var catalog=Path.Combine(temp,"modules.json");
 Check(ModuleLoader.Load(catalog,temp,new ModuleRegistry()).Errors.Count==1,"missing catalog reported");
 File.WriteAllText(catalog,"broken json");Check(ModuleLoader.Load(catalog,temp,new ModuleRegistry()).Errors.Count==1,"invalid JSON reported");
 File.WriteAllText(catalog,JsonSerializer.Serialize(new ModuleCatalog(1,new[]{new ModuleEntry("escape","0.1.0",1,"../outside.dll","Unused"),new ModuleEntry("absent","0.1.0",1,"Absent.dll","Unused")})));
 var report=ModuleLoader.Load(catalog,temp,new ModuleRegistry());Check(report.Errors.Count==2&&report.Loaded.Count==0,"path escape and missing DLL rejected");
 File.WriteAllText(catalog,"{\"SchemaVersion\":1,\"Modules\":[null,{\"Id\":\"null-field\",\"Version\":\"0.1.0\",\"ApiVersion\":1,\"Assembly\":null,\"Type\":null}]}");
 Check(ModuleLoader.Load(catalog,temp,new ModuleRegistry()).Errors.Count==2,"malformed entries reported without crashing catalog");
 File.WriteAllText(catalog,JsonSerializer.Serialize(new ModuleCatalog(1,Array.Empty<ModuleEntry>())));
 Check(ModuleLoader.Load(catalog,temp,new ModuleRegistry()).Errors.Count==0,"empty module catalog supported");
} finally {Directory.Delete(temp,true);}
Console.WriteLine($"PASS: {count} foundation checks. No PDF engine or installer involved.");

sealed class TestCommand(string id,Func<Task<CommandResult>>? action=null):IWorkspaceCommand
{
 public string Id=>id; public string Caption=>id;
 public Task<CommandResult> ExecuteAsync(CancellationToken token)=>action?.Invoke()??Task.FromResult(new CommandResult(true,"done"));
}
sealed class TestModule(string id,int api,params IWorkspaceCommand[] commands):IWorkspaceModule
{
 public ModuleDescriptor Descriptor {get;}=new(id,id,new Version(0,1,0),api);
 public IReadOnlyList<IWorkspaceCommand> Commands=>commands;
}
