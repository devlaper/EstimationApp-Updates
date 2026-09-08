using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using System.Collections.Generic;
using System.Linq;

namespace EstimationApp
{
    public class MeasurementPdfDocument : IDocument
    {
        private readonly List<MeasBuildingGroup> _data;
        private readonly ProjectDetails _projectData;

        public MeasurementPdfDocument(List<MeasBuildingGroup> data, ProjectDetails projectData)
        {
            _data = data;
            _projectData = projectData;
        }

        public DocumentMetadata GetMetadata() => DocumentMetadata.Default;

        public void Compose(IDocumentContainer container)
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(1, Unit.Centimetre);
                page.PageColor(Colors.White);
                page.DefaultTextStyle(x => x.FontSize(9).FontFamily("Helvetica"));

                page.Content().Element(ComposeContent);
                page.Footer().Element(ComposeFooter);
            });
        }

        private void ComposeContent(IContainer container)
        {
            container.Column(mainCol =>
            {
                // 1. Massive Title Banner (Matches Screenshot)
                mainCol.Item().Background(Colors.Grey.Lighten2).Padding(15).Text("MEASUREMENT SHEET").FontSize(22).FontColor(Colors.Black).SemiBold();

                // 2. The Main Data Table
                mainCol.Item().PaddingTop(10).Table(table =>
                {
                    table.ColumnsDefinition(columns =>
                    {
                        columns.RelativeColumn();    // Description / Remarks (Col 0)
                        columns.ConstantColumn(40);  // Module (Col 1)
                        columns.ConstantColumn(40);  // Factor (Col 2)
                        columns.ConstantColumn(30);  // Nos (Col 3)
                        columns.ConstantColumn(40);  // L (Col 4)
                        columns.ConstantColumn(40);  // B (Col 5)
                        columns.ConstantColumn(40);  // H (Col 6)
                        columns.ConstantColumn(60);  // Quantity (Col 7)
                    });

                    // Global Table Header (Minimal numerical headers)
                    table.Header(header =>
                    {
                        header.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten2).PaddingBottom(5); // Empty for remarks
                        header.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten2).PaddingBottom(5).AlignRight().Text("Module").FontSize(10);
                        header.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten2).PaddingBottom(5).AlignRight().Text("Factor").FontSize(10);
                        header.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten2).PaddingBottom(5).AlignRight().Text("Nos").FontSize(10);
                        header.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten2).PaddingBottom(5).AlignRight().Text("L").FontSize(10);
                        header.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten2).PaddingBottom(5).AlignRight().Text("B").FontSize(10);
                        header.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten2).PaddingBottom(5).AlignRight().Text("H").FontSize(10);
                        header.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten2).PaddingBottom(5).AlignRight().Text("Quantity").FontSize(10);
                    });

                    // Hierarchy Rendering
                    foreach (var building in _data)
                    {
                        // Building Head (Light Orange/Peach)
                        table.Cell().ColumnSpan(8).Background("#FCE4D6").Padding(4).AlignCenter().Text(building.BuildingName).FontColor(Colors.Black).FontSize(11).SemiBold();

                        foreach (var work in building.Works)
                        {
                            // Work Head (Red)
                            table.Cell().ColumnSpan(8).PaddingTop(6).Text(work.WorkName).FontColor(Colors.Red.Darken1).FontSize(11).SemiBold();

                            foreach (var item in work.Items)
                            {
                                // Item Head (Black Bold)
                                table.Cell().ColumnSpan(8).PaddingTop(4).PaddingLeft(10).Text(item.ItemName).FontColor(Colors.Black).FontSize(10).SemiBold();

                                foreach (var floor in item.Floors)
                                {
                                    // Floor Node (Grey)
                                    table.Cell().ColumnSpan(8).PaddingLeft(20).Text(floor.FloorName).FontColor(Colors.Grey.Darken3).FontSize(9);

                                    foreach (var subItem in floor.SubItems)
                                    {
                                        // SubItem (Red DSR + Black Name)
                                        table.Cell().ColumnSpan(8).PaddingLeft(30).Text(t =>
                                        {
                                            t.Span(subItem.DsrCode + " ").FontColor(Colors.Red.Darken1).FontSize(9);
                                            t.Span(subItem.SubItemName).FontColor(Colors.Black).FontSize(9);
                                        });

                                        bool isAltRow = true;
                                        double subTotal = 0;

                                        foreach (var row in subItem.Rows)
                                        {
                                            subTotal += row.Quantity;
                                            string bg = isAltRow ? Colors.Grey.Lighten4 : Colors.White; // Alternating background
                                            isAltRow = !isAltRow;

                                            table.Cell().Background(bg).PaddingLeft(45).Text(row.Remark).FontSize(9);
                                            table.Cell().Background(bg).AlignRight().Text("1").FontSize(9); // Default Module 1
                                            table.Cell().Background(bg).AlignRight().Text(row.Factor).FontSize(9);
                                            table.Cell().Background(bg).AlignRight().Text(row.Nos).FontSize(9);
                                            table.Cell().Background(bg).AlignRight().Text(row.L).FontSize(9);
                                            table.Cell().Background(bg).AlignRight().Text(row.B).FontSize(9);
                                            table.Cell().Background(bg).AlignRight().Text(row.H).FontSize(9);
                                            table.Cell().Background(bg).AlignRight().Text(row.Quantity.ToString("F3") + " /").FontSize(9); // Trailing Slash
                                        }

                                        // --- SUB-ITEM TOTAL MATH BLOCK ---
                                        double finalQty = subTotal * building.NumberOfBuildings;

                                        table.Cell().ColumnSpan(3); // Spacer to push table right
                                        table.Cell().ColumnSpan(5).Table(totalsTable =>
                                        {
                                            totalsTable.ColumnsDefinition(c => { c.RelativeColumn(); c.ConstantColumn(20); c.ConstantColumn(80); });

                                            // Line 1: Base Total
                                            totalsTable.Cell().PaddingBottom(2).AlignRight().Text("Total=").FontSize(10);
                                            totalsTable.Cell().PaddingBottom(2); // Empty middle
                                            totalsTable.Cell().PaddingBottom(2).AlignRight().Text($"{subTotal:F3} {subItem.Unit}").FontSize(10);

                                            // Line 2: Modul Multiplier
                                            totalsTable.Cell().PaddingBottom(4).AlignRight().Text("Total Modul Units:-").FontSize(10);
                                            totalsTable.Cell().PaddingBottom(4).AlignCenter().Text("X").FontSize(10);
                                            totalsTable.Cell().PaddingBottom(4).AlignRight().Text($"{building.NumberOfBuildings:F3}").FontSize(10);

                                            // Line 3: Separator line
                                            totalsTable.Cell().ColumnSpan(3).BorderTop(1).BorderColor(Colors.Black);

                                            // Line 4: Final Quantity
                                            totalsTable.Cell().PaddingTop(2).AlignRight().Text("Total Quantity =").SemiBold().FontSize(10);
                                            totalsTable.Cell().PaddingTop(2);
                                            totalsTable.Cell().PaddingTop(2).AlignRight().Text($"{finalQty:F3} {subItem.Unit}").SemiBold().FontSize(10);
                                        });
                                    }
                                }
                            }
                        }
                    }
                });
            });
        }

        private void ComposeFooter(IContainer container)
        {
            container.AlignCenter().Text(x =>
            {
                x.Span("Page ");
                x.CurrentPageNumber();
                x.Span(" of ");
                x.TotalPages();
            });
        }
    }
}