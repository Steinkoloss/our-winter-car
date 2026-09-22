using System.Collections.Generic;
using WinterMP.Net.Sync;
using Xunit;

namespace WinterMP.Net.Tests;

public sealed class PeriodicDiscoveryBudgetTests
{
    [Fact]
    public void SimultaneouslyDueScansEachRunOnceAcrossSuccessiveFrames()
    {
        var budget = new PeriodicDiscoveryBudget();
        var deadlines = new float[11];
        for (int i = 0; i < deadlines.Length; i++) deadlines[i] = 5;
        var order = new List<int>();
        for (int frame = 0; frame < deadlines.Length; frame++)
        {
            int admitted = 0;
            float now = 5 + frame / 100f;
            for (int scan = 0; scan < deadlines.Length; scan++)
                if (budget.TryBegin(frame, now, ref deadlines[scan], 5))
                { order.Add(scan); admitted++; Assert.Equal(now + 5, deadlines[scan]); }
            Assert.Equal(1, admitted);
        }
        Assert.Equal(new[] { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 }, order);
    }

    [Fact]
    public void DeferralDoesNotMoveTheDeadlineAndDoesNotSkipTheScan()
    {
        var budget = new PeriodicDiscoveryBudget();
        float first = 5, second = 5;
        Assert.True(budget.TryBegin(100, 5, ref first, 5));
        Assert.False(budget.TryBegin(100, 5, ref second, 5));
        Assert.Equal(5, second);
        Assert.True(budget.TryBegin(101, 5.1f, ref second, 5));
        Assert.Equal(10.1f, second);
    }

    [Fact]
    public void FutureDeadlineDoesNotConsumeAnotherScansSlot()
    {
        var budget = new PeriodicDiscoveryBudget();
        float future = 8, due = 5;
        Assert.False(budget.TryBegin(1, 5, ref future, 5));
        Assert.Equal(8, future);
        Assert.True(budget.TryBegin(1, 5, ref due, 5));
    }

    [Fact]
    public void StartupScansStayImmediateAndReserveTheRoutineSlot()
    {
        var budget = new PeriodicDiscoveryBudget();
        float first = 0, second = 0, due = 5;
        Assert.True(budget.TryBegin(1, 5, ref first, 5));
        Assert.True(budget.TryBegin(1, 5, ref second, 5));
        Assert.False(budget.TryBegin(1, 5, ref due, 5));
        Assert.Equal(5, due);
    }

    [Fact]
    public void ExplicitRefreshBypassesBothBudgetAndFutureDeadline()
    {
        var budget = new PeriodicDiscoveryBudget();
        float due = 5, forced = 100, another = 5;
        Assert.True(budget.TryBegin(1, 5, ref due, 5));
        Assert.True(budget.TryBegin(1, 5, ref forced, 5, force: true));
        Assert.Equal(10, forced);
        Assert.False(budget.TryBegin(1, 5, ref another, 5));
        Assert.True(budget.TryBegin(1, 5, ref another, 5, force: true));
    }

    [Fact]
    public void ResettingAScansDeadlineRestoresImmediateDiscovery()
    {
        var budget = new PeriodicDiscoveryBudget();
        float next = 5;
        Assert.True(budget.TryBegin(1, 5, ref next, 5));
        next = 0;
        Assert.True(budget.TryBegin(1, 5, ref next, 5));
    }

    [Fact]
    public void FrameNumberWrapDoesNotStrandDueWork()
    {
        var budget = new PeriodicDiscoveryBudget();
        float first = 5, second = 5;
        Assert.True(budget.TryBegin(int.MaxValue, 5, ref first, 5));
        Assert.True(budget.TryBegin(int.MinValue, 5.1f, ref second, 5));
    }
}
