using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using OnlineContract.Data;
using OnlineContract.Helpers;
using OnlineContract.Models;
using OnlineContract.Services.Notifications;
using OnlineContract.Services.Reports;
using Xunit;

namespace OnlineContract.Tests;

public class EomReportServiceTests : IClassFixture<WebAppFactory>
{
    private readonly WebAppFactory _factory;

    public EomReportServiceTests(WebAppFactory factory)
    {
        _factory = factory;
    }

    private AppDbContext CreateDbContext()
    {
        using var scope = _factory.Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<AppDbContext>();
    }

    #region Date Range Helper Tests

    [Fact]
    public void GetPreviousMonthWindow_ReturnsCorrectRange()
    {
        // Arrange
        var now = new DateTime(2026, 2, 15, 10, 30, 0);

        // Act - returns half-open interval [from, toExclusive)
        var (from, toExclusive) = DateRangeHelper.GetPreviousMonthWindow(now);

        // Assert - from is first of previous month, toExclusive is first of current month
        Assert.Equal(new DateTime(2026, 1, 1, 0, 0, 0), from);
        Assert.Equal(2026, toExclusive.Year);
        Assert.Equal(2, toExclusive.Month);  // First of current month (exclusive bound)
        Assert.Equal(1, toExclusive.Day);
        Assert.Equal(0, toExclusive.Hour);
        Assert.Equal(0, toExclusive.Minute);
        Assert.Equal(0, toExclusive.Second);
    }

    [Fact]
    public void GetPreviousMonthWindow_JanuaryGetsDecemberOfPreviousYear()
    {
        // Arrange
        var now = new DateTime(2026, 1, 15);

        // Act - returns half-open interval [from, toExclusive)
        var (from, toExclusive) = DateRangeHelper.GetPreviousMonthWindow(now);

        // Assert - from is first of December, toExclusive is first of January (current month)
        Assert.Equal(new DateTime(2025, 12, 1, 0, 0, 0), from);
        Assert.Equal(2026, toExclusive.Year);  // First of current month (exclusive bound)
        Assert.Equal(1, toExclusive.Month);
        Assert.Equal(1, toExclusive.Day);
    }

    [Fact]
    public void GetCurrentMonthToNowWindow_ReturnsCorrectRange()
    {
        // Arrange
        var now = new DateTime(2026, 3, 15, 14, 30, 45);

        // Act
        var (from, to) = DateRangeHelper.GetCurrentMonthToNowWindow(now);

        // Assert
        Assert.Equal(new DateTime(2026, 3, 1, 0, 0, 0, 0), from);
        Assert.Equal(now, to);
    }

    [Fact]
    public void GetNextRunDt_ReturnsFirstDayOfNextMonth()
    {
        // Arrange
        var now = new DateTime(2026, 3, 15);

        // Act
        var nextRun = DateRangeHelper.GetNextRunDt(now);

        // Assert
        Assert.Equal(new DateTime(2026, 4, 1, 0, 0, 0), nextRun);
    }

    [Fact]
    public void GetNextRunDt_DecemberGetsJanuaryOfNextYear()
    {
        // Arrange
        var now = new DateTime(2026, 12, 25);

        // Act
        var nextRun = DateRangeHelper.GetNextRunDt(now);

        // Assert
        Assert.Equal(new DateTime(2027, 1, 1, 0, 0, 0), nextRun);
    }

    [Fact]
    public void FormatDuration_ShortDuration_ShowsSeconds()
    {
        var duration = TimeSpan.FromSeconds(45.5);
        var result = DateRangeHelper.FormatDuration(duration);
        Assert.Equal("45.5s", result);
    }

    [Fact]
    public void FormatDuration_MediumDuration_ShowsMinutesAndSeconds()
    {
        var duration = TimeSpan.FromMinutes(5) + TimeSpan.FromSeconds(30);
        var result = DateRangeHelper.FormatDuration(duration);
        Assert.Equal("5m 30s", result);
    }

    [Fact]
    public void FormatDuration_LongDuration_ShowsHoursMinutesAndSeconds()
    {
        var duration = TimeSpan.FromHours(2) + TimeSpan.FromMinutes(15) + TimeSpan.FromSeconds(45);
        var result = DateRangeHelper.FormatDuration(duration);
        Assert.Equal("2h 15m 45s", result);
    }

    #endregion

    #region Notification Service Tests

    [Fact]
    public void NotificationService_SendAndRetrieve_WorksCorrectly()
    {
        // Arrange
        var service = new NotificationService();
        var message = new NotificationMessage
        {
            Title = "Test",
            Body = "Test body",
            Type = NotificationType.Information,
            UserId = 123
        };

        // Act
        service.SendNotification(message);
        var notifications = service.GetNotifications(123).ToList();

        // Assert
        Assert.Single(notifications);
        Assert.Equal("Test", notifications[0].Title);
    }

    [Fact]
    public void NotificationService_ClearNotifications_RemovesForUser()
    {
        // Arrange
        var service = new NotificationService();
        service.SendNotification(new NotificationMessage { Title = "User1", UserId = 1 });
        service.SendNotification(new NotificationMessage { Title = "User2", UserId = 2 });

        // Act
        service.ClearNotifications(1);

        // Assert
        Assert.DoesNotContain(service.GetNotifications(1), n => n.UserId == 1);
        Assert.Single(service.GetNotifications(2), n => n.UserId == 2);
    }

    #endregion

    #region EOM Report Result Tests

    [Fact]
    public void EomReportResult_Status_ReturnsSuccessful_WhenNoWarningsAndHasData()
    {
        var result = new EomReportResult
        {
            Success = true,
            Summaries = new List<ContractStatusSummary>
            {
                new() { StatusName = "Delivered", Count = 5, TotalAmount = 1000 }
            }
        };

        Assert.Equal(EomReportStatus.Successful, result.Status);
    }

    [Fact]
    public void EomReportResult_Status_ReturnsWarning_WhenHasWarnings()
    {
        var result = new EomReportResult
        {
            Success = true,
            Warnings = new List<string> { "Some warning" },
            Summaries = new List<ContractStatusSummary>
            {
                new() { StatusName = "Delivered", Count = 5, TotalAmount = 1000 }
            }
        };

        Assert.Equal(EomReportStatus.Warning, result.Status);
    }

    [Fact]
    public void EomReportResult_Status_ReturnsFailed_WhenNotSuccess()
    {
        var result = new EomReportResult
        {
            Success = false,
            ErrorMessage = "Some error"
        };

        Assert.Equal(EomReportStatus.Failed, result.Status);
    }

    [Fact]
    public void EomReportResult_Status_ReturnsSuccessfulNothingProcessed_WhenAllCountsZero()
    {
        var result = new EomReportResult
        {
            Success = true,
            Summaries = new List<ContractStatusSummary>
            {
                new() { StatusName = "Delivered", Count = 0, TotalAmount = 0 },
                new() { StatusName = "Rejected", Count = 0, TotalAmount = 0 }
            }
        };

        Assert.Equal(EomReportStatus.SuccessfulNothingProcessed, result.Status);
    }

    #endregion
}
