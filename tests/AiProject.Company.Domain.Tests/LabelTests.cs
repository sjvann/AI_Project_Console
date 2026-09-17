using AiProject.Company.Domain;

namespace AiProject.Company.Domain.Tests;

public sealed class LabelTests
{
    [Fact]
    public void Timesheet_status_is_chinese()
    {
        Assert.Equal("待 PM 確認", TimesheetStatus.PendingPm.Display());
        Assert.Equal("已核准", TimesheetStatus.Approved.Display());
        Assert.Equal("已退回", TimesheetStatus.Returned.Display());
    }

    [Fact]
    public void Assignment_role_is_chinese()
    {
        Assert.Equal("分析師", AssignmentRole.Analyst.Display());
        Assert.Equal("工程師", AssignmentRole.Engineer.Display());
        Assert.Equal("Lead", AssignmentRole.Lead.Display());
        Assert.Equal("專案經理", AssignmentRole.Pm.Display());
    }
}
