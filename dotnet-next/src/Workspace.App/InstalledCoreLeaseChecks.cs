using System.IO;
namespace Workspace.Shell;
internal static class InstalledCoreLeaseChecks
{
    internal static IReadOnlyList<string> Run(string output)
    {
        var checks=new List<string>();void Check(bool value,string label){if(!value)throw new InvalidOperationException(label);checks.Add(label);}
        var root=Path.Combine(output,"core-lease-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        var install=Path.Combine(root,"Programs","DeojonPdfWpf");Directory.CreateDirectory(install);
        var dev=Path.Combine(root,"development");Directory.CreateDirectory(dev);
        var lockPath=install+".update.lock";var journal=install+".update-journal.json";
        using(var skipped=InstalledCoreLease.AcquireForInstallationPath(dev,install))Check(skipped==null&&!File.Exists(lockPath),"development path does not acquire/create installation lock");
        using(var skipped=InstalledCoreLease.AcquireForInstallationPath(install+"-TEST",install))Check(skipped==null&&!File.Exists(lockPath),"similar prefix is not treated as installed production path");
        using(var lease=InstalledCoreLease.AcquireForInstallationPath(install+Path.DirectorySeparatorChar,install))
        {
            Check(lease!=null&&lease.LockPath==lockPath,"exact normalized installation path acquires adjacent lifetime lease");
            var blocked=false;try{using var updater=new FileStream(lockPath,FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);}catch(IOException){blocked=true;}
            Check(blocked,"actual updater FileShare.None cannot coexist with running app lease");
            using var reader=new FileStream(lockPath,FileMode.Open,FileAccess.Read,FileShare.ReadWrite);
            Check(reader.CanRead,"diagnostic read sharing remains possible");
            CoreLaunchBlock? reason=null;try{using var second=InstalledCoreLease.AcquireForInstallationPath(install,install);}catch(CoreLaunchBlockedException e){reason=e.Reason;}
            Check(reason==CoreLaunchBlock.InUse,"second installed application is blocked with contextual in-use reason");
        }
        using(var updater=new FileStream(lockPath,FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None))
        {
            Check(updater.CanWrite,"updater acquires exclusive lease after application disposal");
            CoreLaunchBlock? reason=null;try{using var app=InstalledCoreLease.AcquireForInstallationPath(install,install);}catch(CoreLaunchBlockedException e){reason=e.Reason;}
            Check(reason==CoreLaunchBlock.InUse,"application is blocked while updater holds exclusive lease");
        }
        File.WriteAllText(journal,"{\"phase\":\"pending\"}");
        CoreLaunchBlock? pending=null;try{using var app=InstalledCoreLease.AcquireForInstallationPath(install,install);}catch(CoreLaunchBlockedException e){pending=e.Reason;}
        Check(pending==CoreLaunchBlock.PendingRecovery,"existing journal refuses startup pending installer recovery");
        using(var updater=new FileStream(lockPath,FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None))Check(updater.CanWrite,"journal refusal does not leak an application lock");
        File.Delete(journal);
        File.WriteAllText(journal+".new","prepared");
        CoreLaunchBlock? staged=null;try{using var app=InstalledCoreLease.AcquireForInstallationPath(install,install);}catch(CoreLaunchBlockedException e){staged=e.Reason;}
        Check(staged==CoreLaunchBlock.PendingRecovery,"durable .new journal also refuses startup until installer recovery");
        File.Delete(journal+".new");
        using(var app=InstalledCoreLease.AcquireForInstallationPath(install,install))Check(app!=null,"launch permitted after installer journal recovery");
        Check(File.Exists(lockPath),"normal exit keeps reusable lock sentinel, without falsely treating existence as ownership");
        return checks;
    }
}
