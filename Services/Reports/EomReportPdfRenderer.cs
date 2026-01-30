using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using System.Globalization;

namespace OnlineContract.Services.Reports
{
    /// <summary>
    /// Renders EOM Report to PDF with soft pastel design - single page layout.
    /// </summary>
    public interface IEomReportPdfRenderer
    {
        Task RenderAsync(EomReportResult result, string filePath, string cultureName, CancellationToken ct = default);
    }

    public class EomReportPdfRenderer : IEomReportPdfRenderer
    {
        // Soft pastel color palette - gentle and elegant
        private static readonly string SoftLavender = "#E8E4F0";
        private static readonly string SoftMint = "#E0F2E9";
        private static readonly string SoftBlue = "#E3EEF9";
        private static readonly string SoftCream = "#FDF8E8";
        
        // Base colors - muted tones
        private static readonly string DarkText = "#374151";
        private static readonly string MediumText = "#6B7280";
        private static readonly string LightText = "#9CA3AF";
        private static readonly string VeryLightBg = "#FAFBFC";
        private static readonly string White = "#FFFFFF";
        
        // Header - soft blue-gray gradient feel
        private static readonly string HeaderColor = "#8B9DC3";

        public Task RenderAsync(EomReportResult result, string filePath, string cultureName, CancellationToken ct = default)
        {
            QuestPDF.Settings.License = LicenseType.Community;
            var culture = CultureInfo.GetCultureInfo(cultureName);

            var document = Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.A4);
                    page.Margin(35);
                    page.DefaultTextStyle(x => x.FontSize(9).FontColor(DarkText).FontFamily("Segoe UI"));

                    page.Header().Element(c => ComposeHeader(c, result));
                    page.Content().Element(c => ComposeContent(c, result, culture));
                    page.Footer().Element(c => ComposeFooter(c));
                });
            });

            document.GeneratePdf(filePath);
            return Task.CompletedTask;
        }

        private void ComposeHeader(IContainer container, EomReportResult result)
        {
            container.Column(column =>
            {
                // Soft, elegant header
                column.Item()
                    .Background(HeaderColor)
                    .Padding(18)
                    .Row(row =>
                    {
                        row.RelativeItem()
                            .Column(titleCol =>
                            {
                                titleCol.Item()
                                    .Text("Monthly Contract Report")
                                    .FontSize(18)
                                    .Bold()
                                    .FontColor(White);
                                
                                titleCol.Item()
                                    .PaddingTop(4)
                                    .Text(text =>
                                    {
                                        text.Span("Period: ").FontColor("#E5E7EB").FontSize(10);
                                        text.Span($"{result.PeriodFrom:MMM dd, yyyy} — {result.PeriodTo:MMM dd, yyyy}").FontColor(White).Bold().FontSize(10);
                                    });
                            });

                        row.ConstantItem(100)
                            .AlignRight()
                            .AlignMiddle()
                            .Text(DateTime.Now.ToString("MMM dd, yyyy"))
                            .FontSize(10)
                            .FontColor("#E5E7EB");
                    });

                column.Item().Height(2).Background(SoftLavender);
                column.Item().Height(15);
            });
        }

        private void ComposeContent(IContainer container, EomReportResult result, CultureInfo culture)
        {
            container.Column(column =>
            {
                // Main card
                column.Item()
                    .Background(White)
                    .Border(1)
                    .BorderColor("#E5E7EB")
                    .Column(cardColumn =>
                    {
                        // Card header - soft lavender
                        cardColumn.Item()
                            .Background(SoftLavender)
                            .Padding(12)
                            .Text("Contract Status Overview")
                            .FontSize(13)
                            .SemiBold()
                            .FontColor(DarkText);

                        // Compact table
                        cardColumn.Item()
                            .Padding(12)
                            .Table(table =>
                            {
                                table.ColumnsDefinition(columns =>
                                {
                                    columns.RelativeColumn(3);
                                    columns.RelativeColumn(1);
                                    columns.RelativeColumn(2);
                                });

                                // Table header
                                table.Header(header =>
                                {
                                    header.Cell()
                                        .Background(VeryLightBg)
                                        .BorderBottom(1)
                                        .BorderColor(SoftBlue)
                                        .Padding(8)
                                        .Text("Status")
                                        .SemiBold()
                                        .FontSize(9)
                                        .FontColor(DarkText);

                                    header.Cell()
                                        .Background(VeryLightBg)
                                        .BorderBottom(1)
                                        .BorderColor(SoftBlue)
                                        .Padding(8)
                                        .AlignCenter()
                                        .Text("Count")
                                        .SemiBold()
                                        .FontSize(9)
                                        .FontColor(DarkText);

                                    header.Cell()
                                        .Background(VeryLightBg)
                                        .BorderBottom(1)
                                        .BorderColor(SoftBlue)
                                        .Padding(8)
                                        .AlignRight()
                                        .Text("Amount")
                                        .SemiBold()
                                        .FontSize(9)
                                        .FontColor(DarkText);
                                });

                                // Data rows
                                var rowIndex = 0;
                                foreach (var summary in result.Summaries)
                                {
                                    var rowBg = rowIndex % 2 == 0 ? White : VeryLightBg;
                                    var statusColor = GetStatusColor(summary.StatusName);

                                    // Status with soft dot
                                    table.Cell()
                                        .Background(rowBg)
                                        .BorderBottom(1)
                                        .BorderColor("#F3F4F6")
                                        .Padding(7)
                                        .Row(r =>
                                        {
                                            r.ConstantItem(6)
                                                .Height(6)
                                                .Background(statusColor);
                                            r.RelativeItem()
                                                .PaddingLeft(8)
                                                .AlignMiddle()
                                                .Text(summary.StatusName)
                                                .FontSize(9)
                                                .FontColor(DarkText);
                                        });

                                    // Count
                                    table.Cell()
                                        .Background(rowBg)
                                        .BorderBottom(1)
                                        .BorderColor("#F3F4F6")
                                        .Padding(7)
                                        .AlignCenter()
                                        .Text(summary.Count > 0 ? summary.Count.ToString("N0", culture) : "—")
                                        .FontSize(9)
                                        .FontColor(summary.Count > 0 ? DarkText : LightText);

                                    // Amount
                                    table.Cell()
                                        .Background(rowBg)
                                        .BorderBottom(1)
                                        .BorderColor("#F3F4F6")
                                        .Padding(7)
                                        .AlignRight()
                                        .Text(summary.TotalAmount > 0 ? summary.TotalAmount.ToString("C2", culture) : "—")
                                        .FontSize(9)
                                        .FontColor(summary.TotalAmount > 0 ? DarkText : LightText);

                                    rowIndex++;
                                }

                                // Total row - soft blue
                                var totalCount = result.Summaries.Sum(s => s.Count);
                                var totalAmount = result.Summaries.Sum(s => s.TotalAmount);

                                table.Cell()
                                    .Background(HeaderColor)
                                    .Padding(8)
                                    .Text("TOTAL")
                                    .Bold()
                                    .FontSize(10)
                                    .FontColor(White);

                                table.Cell()
                                    .Background(HeaderColor)
                                    .Padding(8)
                                    .AlignCenter()
                                    .Text(totalCount.ToString("N0", culture))
                                    .Bold()
                                    .FontSize(10)
                                    .FontColor(White);

                                table.Cell()
                                    .Background(HeaderColor)
                                    .Padding(8)
                                    .AlignRight()
                                    .Text(totalAmount.ToString("C2", culture))
                                    .Bold()
                                    .FontSize(10)
                                    .FontColor(White);
                            });
                    });

                column.Item().Height(12);

                // Net Earnings - soft mint card
                var deliveredAmount = result.Summaries.Where(s => s.StatusName == "Delivered").Sum(s => s.TotalAmount);
                var refundedAmount = result.Summaries.Where(s => s.StatusName == "Refunded").Sum(s => s.TotalAmount);
                var netEarnings = deliveredAmount - refundedAmount;

                column.Item()
                    .Background(SoftMint)
                    .Border(1)
                    .BorderColor("#C6E9D7")
                    .Padding(14)
                    .Row(row =>
                    {
                        row.RelativeItem()
                            .Column(col =>
                            {
                                col.Item()
                                    .Text("Net Earnings")
                                    .FontSize(11)
                                    .SemiBold()
                                    .FontColor("#166534");
                                col.Item()
                                    .Text("Delivered − Refunded")
                                    .FontSize(8)
                                    .FontColor("#15803D");
                            });

                        row.ConstantItem(140)
                            .AlignRight()
                            .AlignMiddle()
                            .Background(White)
                            .Padding(10)
                            .Text(netEarnings.ToString("C2", culture))
                            .FontSize(14)
                            .Bold()
                            .FontColor(netEarnings >= 0 ? "#166534" : "#B91C1C");
                    });

                // Warnings section
                if (result.Warnings.Count > 0)
                {
                    column.Item().Height(12);
                    column.Item()
                        .Background(SoftCream)
                        .Border(1)
                        .BorderColor("#E5D9A8")
                        .Padding(12)
                        .Column(warnColumn =>
                        {
                            warnColumn.Item()
                                .Text("Warnings")
                                .SemiBold()
                                .FontSize(10)
                                .FontColor("#854D0E");

                            warnColumn.Item().Height(6);

                            foreach (var warning in result.Warnings)
                            {
                                warnColumn.Item()
                                    .PaddingBottom(3)
                                    .Text($"• {warning}")
                                    .FontSize(8)
                                    .FontColor("#A16207");
                            }
                        });
                }

                column.Item().Height(15);

                // Generation info
                column.Item()
                    .AlignCenter()
                    .Text(text =>
                    {
                        text.Span("Generated ").FontSize(8).FontColor(LightText);
                        text.Span($"{DateTime.Now:MMMM dd, yyyy}").FontSize(8).FontColor(MediumText);
                        text.Span($" at {DateTime.Now:HH:mm}").FontSize(8).FontColor(LightText);
                    });
            });
        }

        private void ComposeFooter(IContainer container)
        {
            container.Row(row =>
            {
                row.RelativeItem()
                    .Text("EOM Report Service")
                    .FontSize(7)
                    .FontColor(LightText);

                row.ConstantItem(80)
                    .AlignRight()
                    .Text(text =>
                    {
                        text.Span("Page ").FontSize(7).FontColor(LightText);
                        text.CurrentPageNumber().FontSize(7).FontColor(MediumText);
                    });
            });
        }

        private static string GetStatusColor(string statusName)
        {
            return statusName.ToLowerInvariant() switch
            {
                "delivered" => "#86EFAC",   // Soft green
                "rejected" => "#FCA5A5",    // Soft red
                "cancelled" => "#D1D5DB",   // Soft gray
                "written off" => "#FCD34D", // Soft amber
                "returned" => "#93C5FD",    // Soft blue
                "refunded" => "#C4B5FD",    // Soft purple
                _ => "#A5B4FC"              // Soft indigo
            };
        }
    }
}
