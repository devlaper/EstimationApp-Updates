using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using System;
using System.Collections.Generic;
using System.Linq;

namespace EstimationApp
{
    public class AbstractPdfDocument : IDocument
    {
        private readonly List<AbstractBuildingGroup> _data;
        private readonly ProjectDetails _projectData;
        private readonly AbstractPdfTotals _totals;

        public AbstractPdfDocument(List<AbstractBuildingGroup> data, ProjectDetails projectData, AbstractPdfTotals totals)
        {
            _data = data;
            _projectData = projectData;
            _totals = totals;
        }

        public DocumentMetadata GetMetadata() => DocumentMetadata.Default;

        public void Compose(IDocumentContainer container)
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(1, Unit.Centimetre);
                page.PageColor(Colors.White);
                page.DefaultTextStyle(x => x.FontSize(10).FontFamily("Segoe UI", "Helvetica", "Arial"));

                page.Content().Element(ComposeContent);
                page.Footer().Element(ComposeFooter);
            });
        }

        private void ComposeContent(IContainer container)
        {
            container.Column(mainCol =>
            {
                // 1. Report Title Header (Matches Screenshot)
                mainCol.Item().Background("#D9E1E8").Padding(8).AlignCenter()
                       .Text("Project Abstract").FontSize(18).FontColor(Colors.Grey.Darken3).Bold();

                // Optional Project Info
                if (!string.IsNullOrWhiteSpace(_projectData.ProjectName))
                {
                    mainCol.Item().PaddingTop(10).Text($"Project Name: {_projectData.ProjectName}").SemiBold();
                }

                // 2. Main Data Table
                mainCol.Item().PaddingTop(10).Table(table =>
                {
                    table.ColumnsDefinition(columns =>
                    {
                        columns.ConstantColumn(60);  // DSR Code
                        columns.RelativeColumn();    // Work/Item/SubItem Name & Description
                        columns.ConstantColumn(50);  // Unit / No of Buildings
                        columns.ConstantColumn(60);  // Rate
                        columns.ConstantColumn(70);  // Quantity
                        columns.ConstantColumn(90);  // Amount
                    });

                    // Headers (No Background, right aligned math columns)
                    table.Header(header =>
                    {
                        header.Cell().ColumnSpan(2); // Empty space over DSR & Names
                        header.Cell().Padding(4).AlignCenter().Text("Unit").Bold().FontSize(11);
                        header.Cell().Padding(4).AlignRight().Text("Rate").Bold().FontSize(11);
                        header.Cell().Padding(4).AlignRight().Text("Quantity").Bold().FontSize(11);
                        header.Cell().Padding(4).AlignRight().Text("Amount").Bold().FontSize(11);
                    });

                    foreach (var building in _data)
                    {
                        // Calculate Building Base Total
                        double buildingTotal = building.Floors.Sum(f => f.Works.Sum(w => w.Items.Sum(i => i.SubItems.Sum(s => s.Amount))));

                        // Calculate GST for this specific building
                        double buildingGstAmt = buildingTotal * (_totals.GstPercent / 100.0);
                        double buildingTotalWithGst = buildingTotal + buildingGstAmt;

                        // Fetch Number of Buildings directly from Project Setup Data
                        int noOfBuildings = _projectData.Buildings.FirstOrDefault(b => b.BuildingName == building.BuildingName)?.NumberOfBuildings ?? 1;
                        // --- BUILDING HEAD ROW 1 (Base Total) ---
                        table.Cell().ColumnSpan(2).Background("#EAF3EA").Padding(4).Text(building.BuildingName).FontColor(Colors.Red.Medium).FontSize(13).Bold();
                        table.Cell().Background("#EAF3EA").Padding(4).AlignCenter().Text(noOfBuildings.ToString()).FontColor(Colors.Red.Medium).FontSize(12); // Number of Buildings in Unit Col
                        table.Cell().Background("#EAF3EA").Padding(4); // Rate Empty
                        table.Cell().Background("#EAF3EA").Padding(4).AlignRight().Text("Total=").FontColor(Colors.Red.Medium).Bold().FontSize(12);

                        // DELETED THE "F0" LINE HERE. Kept only the Lakhs format:
                        table.Cell().Background("#EAF3EA").Padding(4).AlignRight().Text(buildingTotal.ToString("#,##,##0")).FontColor(Colors.Red.Medium).Bold().FontSize(12);

                        // --- BUILDING HEAD ROW 2 (GST Included Total) ---
                        if (_totals.GstPercent > 0)
                        {
                            table.Cell().ColumnSpan(5).Background("#EAF3EA").Padding(2).PaddingRight(4).AlignRight()
                                 .Text($"Total (incl. {_totals.GstPercent}% GST)=").FontColor(Colors.Red.Darken2).SemiBold().FontSize(11);
                            table.Cell().Background("#EAF3EA").Padding(2).PaddingRight(4).AlignRight()
                                 .Text(buildingTotalWithGst.ToString("#,##,##0")).FontColor(Colors.Red.Darken2).Bold().FontSize(11);
                        }

                        foreach (var floor in building.Floors)
                        {
                            // Optional: Print floor name subtly if it exists and isn't Unassigned
                            if (floor.FloorName != "Unassigned Floor")
                            {
                                table.Cell().ColumnSpan(6).Padding(2).PaddingLeft(10).Text($"[{floor.FloorName}]").FontColor(Colors.Grey.Medium).Italic().FontSize(9);
                            }

                            foreach (var work in floor.Works)
                            {
                                double workTotal = work.Items.Sum(i => i.SubItems.Sum(s => s.Amount));

                                // --- WORK HEAD (Purple) ---
                                table.Cell().ColumnSpan(4).Padding(4).PaddingTop(10).Text(work.WorkName).FontColor(Colors.Purple.Darken2).Bold().FontSize(11);
                                table.Cell().Padding(4).PaddingTop(10).AlignRight().Text("Total=").FontColor(Colors.Purple.Darken2).Bold().FontSize(11);

                                // DELETED THE "F0" LINE HERE TOO. Kept only the Lakhs format:
                                table.Cell().Padding(4).PaddingTop(10).AlignRight().Text(workTotal.ToString("#,##,##0")).FontColor(Colors.Purple.Darken2).Bold().FontSize(11);

                                foreach (var item in work.Items)
                                {
                                    // --- ITEM HEAD (Black, Bold) ---
                                    table.Cell().ColumnSpan(6).Padding(4).PaddingLeft(20).Text(item.ItemName).FontColor(Colors.Black).Bold().FontSize(11);

                                    foreach (var subItem in item.SubItems)
                                    {
                                        // Fetch the long description from Master Data
                                        string description = _projectData.MasterItems.FirstOrDefault(i => i.NameOfItem == item.ItemName)?.Description ?? "";

                                        // DSR Code
                                        table.Cell().Padding(4).Text(subItem.DsrCode).FontColor(Colors.Red.Medium).Bold().FontSize(11);

                                        // SubItem Name (Blue) + Description (Grey)
                                        table.Cell().Padding(4).Text(txt =>
                                        {
                                            txt.Span(subItem.SubItemName).FontColor("#2E74B5").FontSize(10);
                                            if (!string.IsNullOrWhiteSpace(description))
                                            {
                                                txt.EmptyLine();
                                                txt.Span(description).FontColor(Colors.Grey.Darken3).FontSize(9);
                                            }
                                        });

                                        // Math Columns
                                        table.Cell().Padding(4).AlignCenter().Text(subItem.Unit).FontSize(9);
                                        table.Cell().Padding(4).AlignRight().Text(subItem.Rate.ToString("#,##,##0")).FontSize(10);
                                        table.Cell().Padding(4).AlignRight().Text(subItem.Quantity.ToString("F2")).FontSize(10);
                                        table.Cell().Padding(4).AlignRight().Text(subItem.Amount.ToString("F0")).FontSize(10);
                                    }
                                }
                            }
                        }
                    }
                });

                // 3. Calculation Footer (Matches Screenshot 2)
                mainCol.Item().PaddingTop(20).Table(footerTable =>
                {
                    footerTable.ColumnsDefinition(cols =>
                    {
                        cols.RelativeColumn();
                        cols.ConstantColumn(120);
                    });

                    // Top thick border
                    footerTable.Cell().ColumnSpan(2).BorderBottom(1.5f).BorderColor(Colors.Black).Padding(2);

                    // Base Total
                    footerTable.Cell().Padding(4).AlignRight().Text("Total Estimated Cost in Rs/-:-").FontSize(12).Bold();
                    footerTable.Cell().Padding(4).AlignRight().Text(_totals.BaseTotal.ToString("#,##,##0")).FontSize(12).Bold();

                    // GST
                    if (_totals.GstPercent > 0)
                    {
                        footerTable.Cell().Padding(4).AlignRight().Text($"Add GST  {_totals.GstPercent} % of Estimated Cost in Rs/-").FontSize(12).Bold();
                        footerTable.Cell().Padding(4).AlignRight().Text(_totals.GstAmount.ToString("#,##,##0")).FontSize(12).FontColor("#1E8449").Bold();
                    }

                    // Consultant Fees
                    if (_totals.StructPercent > 0)
                    {
                        footerTable.Cell().Padding(2).AlignRight().Text($"Add Structural Fees {_totals.StructPercent}%").FontSize(11).SemiBold();
                        footerTable.Cell().Padding(2).AlignRight().Text(_totals.StructAmount.ToString("#,##,##0")).FontSize(11).SemiBold();
                    }
                    if (_totals.ArchPercent > 0)
                    {
                        footerTable.Cell().Padding(2).AlignRight().Text($"Add Architectural Fees {_totals.ArchPercent}%").FontSize(11).SemiBold();
                        footerTable.Cell().Padding(2).AlignRight().Text(_totals.ArchAmount.ToString("#,##,##0")).FontSize(11).SemiBold();
                    }

                    // Material Testing & Royalty 
                    if (_totals.TestsTotal > 0)
                    {
                        footerTable.Cell().Padding(2).AlignRight().Text("Add Total Amount For Tests & Royalty").FontSize(11).SemiBold();
                        footerTable.Cell().Padding(2).AlignRight().Text(_totals.TestsTotal.ToString("#,##,##0")).FontSize(11).SemiBold();
                    }

                    // Grand Total thick border
                    footerTable.Cell().ColumnSpan(2).BorderBottom(1.5f).BorderColor(Colors.Black).Padding(2);

                    // Grand Total
                    footerTable.Cell().Padding(4).AlignRight().Text("Total Estimate of Project in Rs/-").FontSize(14).Bold();
                    footerTable.Cell().Padding(4).AlignRight().Text(_totals.GrandTotal.ToString("#,##,##0")).FontSize(14).Bold();
                });
            });
        }

        private void ComposeFooter(IContainer container)
        {
            container.Table(t =>
            {
                t.ColumnsDefinition(c => { c.RelativeColumn(); c.RelativeColumn(); });

                // Matches "06 September 2026" from screenshot
                t.Cell().AlignLeft().Text(DateTime.Now.ToString("dd MMMM yyyy")).FontSize(9).FontColor(Colors.Grey.Darken2);

                // Matches "Page 1 of 1" from screenshot
                // Matches "Page 1 of 1" from screenshot
                t.Cell().AlignRight().DefaultTextStyle(style => style.FontSize(9).FontColor(Colors.Grey.Darken2))
                    .Text(x => { x.Span("Page "); x.CurrentPageNumber(); x.Span(" of "); x.TotalPages(); });
            });
        }
    }
}