using NothingShell.Core.Launcher;
using NothingShell.Launcher;

namespace NothingShell.Tests;

public sealed class LauncherViewModelTests
{
    private static LaunchItem App(string n) => new("app:" + n, n, LaunchKind.App, "shell:AppsFolder\\" + n, []);
    private static LaunchItem Setting(string n) => new("ms-settings:" + n, n, LaunchKind.Setting, "ms-settings:" + n, []);
    private static LaunchItem File(string n) => new(@"C:\f\" + n, n, LaunchKind.File, @"C:\f\" + n, []);

    private static readonly LaunchItem[] Apps = [App("Notepad"), App("Notion"), App("Calculator"), App("Chrome")];
    private static readonly LaunchItem[] Settings = [Setting("Notifications")];

    private static LauncherViewModel Vm(Func<string, CancellationToken, Task<IReadOnlyList<LaunchItem>>>? files = null) => new(
        new LauncherSources(
            Searchable: () => [.. Apps, .. Settings],
            MostUsed: () => [Apps[2], Apps[0]],
            AllApps: () => Apps,
            Usage: () => new Dictionary<string, LaunchUsage>(),
            FileSearch: files ?? ((_, _) => Task.FromResult<IReadOnlyList<LaunchItem>>([]))),
        TimeSpan.Zero);

    [Fact]
    public void EmptyQuery_ShowsHome()
    {
        var vm = Vm();
        Assert.Equal(LauncherView.Home, vm.View);
        Assert.Equal(["Calculator", "Notepad"], vm.MostUsed.Select(i => i.Name));
        vm.Query = "no";
        vm.Query = "  ";
        Assert.Equal(LauncherView.Home, vm.View);
    }

    [Fact]
    public async Task Typing_ShowsResultsRankedAndSelectsFirst()
    {
        var vm = Vm();
        vm.Query = "no";
        await vm.PendingFileSearch;
        Assert.Equal(LauncherView.Results, vm.View);
        Assert.Equal(["Notepad", "Notion", "Notifications"], vm.Results.Select(i => i.Name));
        Assert.Equal(0, vm.Selected);
        Assert.Equal("Notepad", vm.SelectedItem?.Name);
    }

    [Fact]
    public void MoveSelection_ClampsWithinResults()
    {
        var vm = Vm();
        vm.Query = "no";
        vm.MoveSelection(-1);
        Assert.Equal(0, vm.Selected);
        vm.MoveSelection(10);
        Assert.Equal(vm.Results.Count - 1, vm.Selected);
    }

    [Fact]
    public async Task FileResults_ArriveLater_AppendAfterAppsAndSettings_KeepSelectionOnSameItem()
    {
        var gate = new TaskCompletionSource<IReadOnlyList<LaunchItem>>();
        var vm = Vm((_, _) => gate.Task);
        vm.Query = "no";
        vm.MoveSelection(1); // Notion
        gate.SetResult([File("notes.txt")]);
        await vm.PendingFileSearch;
        Assert.Equal(["Notepad", "Notion", "Notifications", "notes.txt"], vm.Results.Select(i => i.Name));
        Assert.Equal("Notion", vm.SelectedItem?.Name);
    }

    [Fact]
    public async Task FileResults_ForOldQuery_AreDropped()
    {
        var first = new TaskCompletionSource<IReadOnlyList<LaunchItem>>();
        var calls = 0;
        var vm = Vm((q, _) => ++calls == 1 ? first.Task : Task.FromResult<IReadOnlyList<LaunchItem>>([]));
        vm.Query = "no";
        var stale = vm.PendingFileSearch;
        vm.Query = "calc";
        first.SetResult([File("notes.txt")]);
        await stale;
        await vm.PendingFileSearch;
        Assert.DoesNotContain(vm.Results, i => i.Name == "notes.txt");
        Assert.Equal("Calculator", vm.Results[0].Name);
    }

    [Fact]
    public void Enter_RaisesLaunchRequestedForSelected()
    {
        var vm = Vm();
        LaunchItem? launched = null;
        vm.LaunchRequested += i => launched = i;
        vm.Query = "calc";
        vm.ActivateSelected();
        Assert.Equal("Calculator", launched?.Name);
    }

    [Fact]
    public void Enter_WithNothingSelected_DoesNothing()
    {
        var vm = Vm();
        var raised = false;
        vm.LaunchRequested += _ => raised = true;
        vm.ActivateSelected();
        Assert.False(raised);
    }

    [Fact]
    public void AllApps_IsAlphabeticalAndNavigable()
    {
        var vm = Vm();
        vm.ShowAllApps();
        Assert.Equal(LauncherView.AllApps, vm.View);
        Assert.Equal(["Calculator", "Chrome", "Notepad", "Notion"], vm.AllApps.Select(i => i.Name));
        vm.MoveSelection(2);
        Assert.Equal("Notepad", vm.SelectedItem?.Name);
        Assert.True(vm.Back());
        Assert.Equal(LauncherView.Home, vm.View);
        Assert.False(vm.Back());
    }

    [Fact]
    public void Hover_BeforePointerMoves_DoesNotSelect()
    {
        // The launcher can open under a resting cursor; Enter must not launch an app the user never chose.
        var vm = Vm();
        vm.HoverSelect(vm.MostUsed[0]);
        Assert.Equal(-1, vm.Selected);
    }

    [Fact]
    public void Hover_AfterPointerMoves_Selects()
    {
        var vm = Vm();
        vm.PointerMoved();
        vm.HoverSelect(vm.MostUsed[1]);
        Assert.Equal(1, vm.Selected);
        vm.Reset();
        vm.HoverSelect(vm.MostUsed[0]);
        Assert.Equal(-1, vm.Selected); // a fresh open needs a fresh move
    }

    [Fact]
    public void Reset_ClearsQueryAndSelection()
    {
        var vm = Vm();
        vm.Query = "calc";
        vm.Reset();
        Assert.Equal("", vm.Query);
        Assert.Equal(LauncherView.Home, vm.View);
        Assert.Equal(-1, vm.Selected);
        Assert.Empty(vm.Results);
    }
}
