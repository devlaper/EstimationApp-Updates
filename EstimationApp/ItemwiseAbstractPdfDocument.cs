using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using System;
using System.Collections.Generic;
using System.Linq;

namespace EstimationApp
{
    public class ItemwiseAbstractPdfDocument : IDocument
    {
        private readonly List<ItemwiseWorkGroup> _data;
        private readonly ProjectDetails _projectData;
        private readonly AbstractPdfTotals _totals;
        private readonly bool _showDescriptions;

        public ItemwiseAbstractPdfDocument(List<ItemwiseWorkGroup> data, ProjectDetails projectData, AbstractPdfTotals totals, bool showDescriptions = true)
        {
            _data = data;
            _projectData = projectData;
            _totals = totals;
            _showDescriptions = showDescriptions; // Assigned correctly
        }

        public DocumentMetadata GetMetadata() => DocumentMetadata.Default;

        // Helper method to slice strings gracefully at a word boundary
        private string GetShortDescription(string text, int maxLength = 255)
        {
            if (string.IsNullOrWhiteSpace(text)) return "";
            if (text.Length <= maxLength) return text;

            int lastSpace = text.LastIndexOf(' ', maxLength);
            return (lastSpace > 0 ? text.Substring(0, lastSpace) : text.Substring(0, maxLength)) + "...";
        }

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
                // 1. Report Title & Meta Header
                mainCol.Item().AlignCenter().Text("Project Abstract").FontSize(20).FontColor(Colors.Grey.Darken2).SemiBold();

                mainCol.Item().PaddingTop(15).PaddingBottom(10).Table(t =>
                {
                    t.ColumnsDefinition(c => { c.ConstantColumn(120); c.RelativeColumn(); c.ConstantColumn(80); c.RelativeColumn(); });

                    t.Cell().Text("Name of Project ;-").FontSize(9);
                    t.Cell().Text(_projectData.ProjectName).FontSize(9);
                    t.Cell().Text(""); t.Cell().Text("");

                    t.Cell().Text("Owner of Project;-").FontSize(9);
                    t.Cell().Text(_projectData.OwnerName).FontSize(9);
                    t.Cell().Text(""); t.Cell().Text("");

                    t.Cell().Text("Date :-").FontSize(9);
                    t.Cell().Text(DateTime.Now.ToString("dd-MMM-yyyy")).FontSize(9);
                    t.Cell().Text("GST ;-").FontSize(9);
                    t.Cell().Text($"{_totals.GstPercent}%    Electrical Cost:-  0").FontSize(9);
                });

                // 2. Main Data Table
                mainCol.Item().Table(table =>
                {
                    table.ColumnsDefinition(columns =>
                    {
                        columns.RelativeColumn();    // Work/Item/SubItem Name
                        columns.ConstantColumn(40);  // Unit
                        columns.ConstantColumn(60);  // Rate
                        columns.ConstantColumn(70);  // Quantity
                        columns.ConstantColumn(80);  // Amount
                    });

                    table.Header(header =>
                    {
                        header.Cell().BorderBottom(1).BorderColor(Colors.Black).Padding(4); // Empty
                        header.Cell().BorderBottom(1).BorderColor(Colors.Black).Padding(4).AlignCenter().Text("Unit").SemiBold();
                        header.Cell().BorderBottom(1).BorderColor(Colors.Black).Padding(4).AlignRight().Text("Rate").SemiBold();
                        header.Cell().BorderBottom(1).BorderColor(Colors.Black).Padding(4).AlignRight().Text("Quantity").SemiBold();
                        header.Cell().BorderBottom(1).BorderColor(Colors.Black).Padding(4).AlignRight().Text("Amount").SemiBold();
                    });

                    foreach (var work in _data)
                    {
                        // Work Head (Purple)
                        table.Cell().ColumnSpan(3).Background(Colors.Grey.Lighten4).PaddingTop(6).PaddingBottom(2).PaddingLeft(5).Text(work.WorkName).FontColor(Colors.Purple.Darken2).FontSize(11).SemiBold();
                        table.Cell().Background(Colors.Grey.Lighten4).PaddingTop(6).PaddingBottom(2).AlignRight().Text("Total=").FontColor(Colors.Purple.Darken2).FontSize(11).SemiBold();
                        table.Cell().Background(Colors.Grey.Lighten4).PaddingTop(6).PaddingBottom(2).AlignRight().Text(work.WorkTotalAmount.ToString("F0")).FontColor(Colors.Purple.Darken2).FontSize(11).SemiBold();

                        foreach (var item in work.Items)
                        {
                            // Item Head (Dark Gray, Bold)
                            table.Cell().ColumnSpan(5).PaddingTop(4).PaddingBottom(2).PaddingLeft(15).Text(item.ItemName).FontColor(Colors.Grey.Darken3).FontSize(11).SemiBold();

                            foreach (var subItem in item.SubItems)
                            {
                                // Line 1: DSR (Red) + SubItem Name (Blue)
                                table.Cell().Padding(2).PaddingLeft(25).Text(t =>
                                {
                                    t.Span(subItem.DsrCode + "   ").FontColor(Colors.Red.Medium).SemiBold();
                                    t.Span(subItem.SubItemName).FontColor(Colors.Blue.Darken2);
                                });
                                table.Cell().Padding(2).AlignCenter().Text(subItem.Unit);
                                table.Cell().Padding(2).AlignRight().Text(subItem.Rate.ToString("F2"));
                                table.Cell().Padding(2).AlignRight().Text(subItem.Quantity.ToString("F3"));
                                table.Cell().Padding(2).AlignRight().Text(subItem.Amount.ToString("F2"));

                                
                                // Line 2: Description (Grey) - Wrapped tightly into Column 1
                                if (_showDescriptions && !string.IsNullOrWhiteSpace(subItem.Description))
                                {
                                    string truncatedDesc = GetShortDescription(subItem.Description);
                                    table.Cell().ColumnSpan(1).PaddingBottom(2).PaddingLeft(45).Text(truncatedDesc).FontColor(Colors.Grey.Darken2).FontSize(8);

                                    // Drop empty cells to align the grid structure
                                    table.Cell(); table.Cell(); table.Cell(); table.Cell();
                                }

                                // Line 3: Locations (Cyan) - Wrapped tightly into Column 1
                                if (subItem.Locations != null && subItem.Locations.Count > 0)
                                {
                                    table.Cell().ColumnSpan(1).PaddingBottom(4).PaddingLeft(45).Text(t =>
                                    {
                                        foreach (var loc in subItem.Locations)
                                        {
                                            var locParts = loc.Split('\n');
                                            t.Span(locParts[0] + " - " + locParts[1] + "\n").FontColor(Colors.LightBlue.Darken2).FontSize(8);
                                        }
                                    });

                                    // Drop empty cells to align the grid structure
                                    table.Cell(); table.Cell(); table.Cell(); table.Cell();
                                }
                            }
                        }
                    }
                });

                // 3. Calculation Footer
                mainCol.Item().PaddingTop(10).BorderTop(2).BorderColor(Colors.Grey.Darken2).Table(footerTable =>
                {
                    footerTable.ColumnsDefinition(cols =>
                    {
                        cols.RelativeColumn();
                        cols.ConstantColumn(90);
                    });

                    footerTable.Cell().Padding(2).AlignRight().PaddingRight(20).Text("Total Estimated Cost in Rs/-:-").FontSize(11).SemiBold();
                    footerTable.Cell().Padding(2).AlignRight().Text(_totals.BaseTotal.ToString("F0")).FontSize(11).SemiBold();

                    footerTable.Cell().Padding(2).AlignRight().PaddingRight(20).Text($"Add GST {_totals.GstPercent} % of Estimated Cost in Rs/-").FontSize(10).SemiBold();
                    footerTable.Cell().Padding(2).AlignRight().Text(_totals.GstAmount.ToString("F0")).FontColor(Colors.Green.Darken2).FontSize(10).SemiBold();

                    if (_totals.StructAmount > 0)
                    {
                        footerTable.Cell().Padding(2).AlignRight().PaddingRight(20).Text($"Add Structural Consultant Fees {_totals.StructPercent}%").FontSize(9);
                        footerTable.Cell().Padding(2).AlignRight().Text(_totals.StructAmount.ToString("F0")).FontSize(9);
                    }
                    if (_totals.ArchAmount > 0)
                    {
                        footerTable.Cell().Padding(2).AlignRight().PaddingRight(20).Text($"Add Architectural Consultant Fees {_totals.ArchPercent}%").FontSize(9);
                        footerTable.Cell().Padding(2).AlignRight().Text(_totals.ArchAmount.ToString("F0")).FontSize(9);
                    }

                    // Material Testing Section
                    footerTable.Cell().ColumnSpan(2).BorderTop(1).BorderColor(Colors.Black).Padding(4).Text("Material testing :-").FontSize(10).SemiBold();

                    footerTable.Cell().ColumnSpan(2).Table(testTable =>
                    {
                        testTable.ColumnsDefinition(c => { c.ConstantColumn(60); c.RelativeColumn(); c.ConstantColumn(50); c.ConstantColumn(50); c.ConstantColumn(90); });

                        foreach (var test in _totals.ActiveTests)
                        {
                            testTable.Cell().PaddingLeft(5).Text(test.TestCode ?? "").FontSize(9);
                            testTable.Cell().Text(test.TestName).FontSize(9);

                            // FIXED: Changed "F0" to "F2" to show the decimal values (e.g., 885.62)
                            testTable.Cell().AlignRight().Text(test.Rate.ToString("F2")).FontSize(9);

                            testTable.Cell().AlignRight().Text(test.Quantity.ToString()).FontSize(9);

                            // FIXED: Changed to "F2" so the calculated amount also keeps its decimals
                            testTable.Cell().AlignRight().Text((test.Quantity * test.Rate).ToString("F2")).FontSize(9);

                            if (!string.IsNullOrWhiteSpace(test.Description))
                            {
                                testTable.Cell().Text("");
                                testTable.Cell().ColumnSpan(4).PaddingBottom(6).Text(GetShortDescription(test.Description)).FontColor(Colors.Grey.Darken2).FontSize(8);
                            }
                        }

                        // Inject Custom Royalty
                        if (_totals.RoyaltyAmount > 0)
                        {
                            testTable.Cell().PaddingLeft(5).Text("17176").FontSize(9);
                            testTable.Cell().Text("Royalty charges").FontSize(9);
                            testTable.Cell().AlignRight().Text(_totals.RoyaltyRate.ToString("F2")).FontSize(9);
                            testTable.Cell().AlignRight().Text(_totals.RoyaltyQty.ToString("F3")).FontSize(9);
                            testTable.Cell().AlignRight().Text(_totals.RoyaltyAmount.ToString("F2")).FontSize(9);

                            var royTest = AppState.CurrentProject.MaterialTests.FirstOrDefault(t => t.TestName.Contains("Royalty"));
                            if (royTest != null && !string.IsNullOrWhiteSpace(royTest.Description))
                            {
                                testTable.Cell().Text("");
                                testTable.Cell().ColumnSpan(4).PaddingBottom(6).Text(GetShortDescription(royTest.Description)).FontColor(Colors.Grey.Darken2).FontSize(8);
                            }
                        }
                    });

                    footerTable.Cell().BorderBottom(1).BorderColor(Colors.Black).Padding(4).AlignRight().PaddingRight(20).Text("Total Testing in Rs/-").FontSize(10);
                    footerTable.Cell().BorderBottom(1).BorderColor(Colors.Black).Padding(4).AlignRight().Text(_totals.TestsTotal.ToString("F2")).FontSize(10).SemiBold();

                    footerTable.Cell().PaddingTop(6).AlignRight().PaddingRight(20).Text("Total Estimate of Project in Rs/-").FontSize(12).SemiBold();
                    footerTable.Cell().PaddingTop(6).AlignRight().Text(_totals.GrandTotal.ToString("F0")).FontSize(12).SemiBold();
                });
            });
        }

        private void ComposeFooter(IContainer container)
        {
            container.AlignCenter().Text(x => { x.Span("Page "); x.CurrentPageNumber(); x.Span(" of "); x.TotalPages(); });
        }
    }
}