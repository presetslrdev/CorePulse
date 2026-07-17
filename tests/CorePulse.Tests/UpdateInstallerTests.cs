using CpuMonitorNotifier.Update;
using Xunit;

namespace CorePulse.Tests;

public class UpdateInstallerTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("corepulse-swap").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public void OldPathFor_SitsNextToTheExe()
        => Assert.Equal(@"C:\apps\CorePulse.old.exe", UpdateInstaller.OldPathFor(@"C:\apps\CorePulse.exe"));

    [Fact]
    public void IsDirectoryWritable_TrueForWritableDirectory()
        => Assert.True(UpdateInstaller.IsDirectoryWritable(_dir));

    [Fact]
    public void IsDirectoryWritable_FalseForMissingDirectory()
        => Assert.False(UpdateInstaller.IsDirectoryWritable(Path.Combine(_dir, "does-not-exist")));

    [Fact]
    public void IsDirectoryWritable_LeavesNoProbeFileBehind()
    {
        UpdateInstaller.IsDirectoryWritable(_dir);
        Assert.Empty(Directory.GetFiles(_dir));
    }

    [Fact]
    public void SwapFiles_PutsNewBinaryInPlaceAndKeepsTheOldOne()
    {
        string exe = Path.Combine(_dir, "CorePulse.exe");
        string incoming = Path.Combine(_dir, "incoming.tmp");
        File.WriteAllText(exe, "old");
        File.WriteAllText(incoming, "new");

        UpdateInstaller.SwapFiles(exe, incoming);

        Assert.Equal("new", File.ReadAllText(exe));
        Assert.Equal("old", File.ReadAllText(UpdateInstaller.OldPathFor(exe)));
        Assert.False(File.Exists(incoming));
    }

    [Fact]
    public void SwapFiles_RollsBackWhenTheNewFileCannotBeMoved()
    {
        string exe = Path.Combine(_dir, "CorePulse.exe");
        File.WriteAllText(exe, "old");
        string missing = Path.Combine(_dir, "never-downloaded.tmp");

        Assert.ThrowsAny<IOException>(() => UpdateInstaller.SwapFiles(exe, missing));

        // установка цела: .exe на месте и с прежним содержимым, следов подмены нет
        Assert.True(File.Exists(exe));
        Assert.Equal("old", File.ReadAllText(exe));
        Assert.False(File.Exists(UpdateInstaller.OldPathFor(exe)));
    }

    [Fact]
    public void SwapFiles_ReplacesLeftoverOldFileFromAPreviousUpdate()
    {
        string exe = Path.Combine(_dir, "CorePulse.exe");
        string incoming = Path.Combine(_dir, "incoming.tmp");
        File.WriteAllText(exe, "v2");
        File.WriteAllText(incoming, "v3");
        File.WriteAllText(UpdateInstaller.OldPathFor(exe), "v1");

        UpdateInstaller.SwapFiles(exe, incoming);

        Assert.Equal("v3", File.ReadAllText(exe));
        Assert.Equal("v2", File.ReadAllText(UpdateInstaller.OldPathFor(exe)));
    }

    [Fact]
    public void CurrentExePath_IsAnExistingFile()
        => Assert.True(File.Exists(UpdateInstaller.CurrentExePath));
}
