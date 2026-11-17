using System.IO;
namespace Workspace.Shell;

internal enum CoreLaunchBlock { PendingRecovery, InUse, AccessDenied }
internal sealed class CoreLaunchBlockedException(CoreLaunchBlock reason,string message,Exception? inner=null):Exception(message,inner)
{
    internal CoreLaunchBlock Reason { get; }=reason;
}

/// <summary>The installed core holds a Windows file-sharing lease for its entire process life.
/// A persistent lock filename alone is not a held lease. Never delete it on normal exit.</summary>
internal sealed class InstalledCoreLease : IDisposable
{
    private FileStream? stream;
    internal string LockPath { get; }
    internal static string ProductionDirectory=>Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Programs","DeojonPdfWpf");
    private InstalledCoreLease(FileStream stream,string lockPath){this.stream=stream;LockPath=lockPath;}
    internal static InstalledCoreLease? AcquireProduction(string executableDirectory)=>AcquireForInstallationPath(executableDirectory,ProductionDirectory);

    // Explicit installation-root injection supports isolated real FileShare tests only.
    // Normal startup always calls AcquireProduction with AppContext.BaseDirectory.
    internal static InstalledCoreLease? AcquireForInstallationPath(string executableDirectory,string installationDirectory)
    {
        var executable=Normalize(executableDirectory);var installation=Normalize(installationDirectory);
        if(!string.Equals(executable,installation,StringComparison.OrdinalIgnoreCase))return null;
        var lockPath=installation+".update.lock";
        var journal=installation+".update-journal.json";
        CheckJournal(journal);
        FileStream? lease=null;
        try
        {
            lease=new FileStream(lockPath,FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.Read);
            // Updater must hold FileShare.None before writing its journal. Recheck after
            // acquiring our lease so a journal created between the two checks cannot slip through.
            CheckJournal(journal);
            return new InstalledCoreLease(lease,lockPath);
        }
        catch(CoreLaunchBlockedException){lease?.Dispose();throw;}
        catch(UnauthorizedAccessException error)
        {
            lease?.Dispose();
            throw new CoreLaunchBlockedException(CoreLaunchBlock.AccessDenied,"설치 폴더의 실행 잠금을 열 수 없습니다. 폴더 접근 권한을 확인하거나 더죤 PDF Workspace 설치 프로그램으로 복구한 뒤 다시 실행하세요.",error);
        }
        catch(IOException error)
        {
            lease?.Dispose();
            throw new CoreLaunchBlockedException(CoreLaunchBlock.InUse,"더죤 PDF Workspace가 이미 실행 중이거나 설치·업데이트 작업이 진행 중입니다. 실행 중인 앱과 설치 작업을 마친 뒤 다시 실행하세요.",error);
        }
    }
    private static string Normalize(string path)
    {
        if(!Path.IsPathFullyQualified(path))throw new ArgumentException("An absolute installation path is required.",nameof(path));
        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
    }
    private static void CheckJournal(string journal)
    {
        if(Path.Exists(journal)||Path.Exists(journal+".new"))throw new CoreLaunchBlockedException(CoreLaunchBlock.PendingRecovery,"이전 설치·업데이트 작업의 복구가 필요합니다. 더죤 PDF Workspace 설치 프로그램을 다시 실행해 복구를 마친 뒤 앱을 열어 주세요.");
    }
    public void Dispose()=>Interlocked.Exchange(ref stream,null)?.Dispose();
}

