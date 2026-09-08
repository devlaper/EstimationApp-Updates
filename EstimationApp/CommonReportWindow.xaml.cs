using QuestPDF.Fluent;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace EstimationApp
{
    // --- HELPER CLASSES ---

    
    public class FilterOption : INotifyPropertyChanged
    {
        public string Name { get; set; }
        private bool _isSelected;
        public bool IsSelected
        {
            get => _isSelected;
            set { _isSelected = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected))); }
        }
        public event PropertyChangedEventHandler PropertyChanged;
    }

    public class ActiveFilter
    {
        public string Parameter { get; set; }
        public List<string> SelectedValues { get; set; }
        [System.Text.Json.Serialization.JsonIgnore]
        public string DisplayText => $"{Parameter}: {string.Join(", ", SelectedValues)}";
    }

    public class FilterNode : INotifyPropertyChanged
    {
        public string Name { get; set; }
        public ObservableCollection<FilterNode> Children { get; set; } = new ObservableCollection<FilterNode>();
        public FilterNode Parent { get; set; }

        private bool? _isChecked = true;
        public bool? IsChecked
        {
            get => _isChecked;
            set
            {
                // UX FIX: Skip 'null' state when the user physically clicks a box
                if (_isChecked == true && value == null)
                {
                    SetIsChecked(false, true, true);
                }
                else if (_isChecked != value)
                {
                    SetIsChecked(value, true, true);
                }
            }
        }

        private void SetIsChecked(bool? value, bool updateParent, bool updateChildren)
        {
            if (_isChecked == value) return;
            _isChecked = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsChecked)));

            if (updateChildren && value.HasValue)
                foreach (var child in Children) child.SetIsChecked(value, false, true);

            if (updateParent) Parent?.UpdateStateFromChildren();
        }

        public void UpdateStateFromChildren()
        {
            bool allChecked = Children.All(c => c.IsChecked == true);
            bool allUnchecked = Children.All(c => c.IsChecked == false);

            if (allChecked) SetIsChecked(true, true, false);
            else if (allUnchecked) SetIsChecked(false, true, false);
            else SetIsChecked(null, true, false);
        }

        public event PropertyChangedEventHandler PropertyChanged;
    }

    public class RawReportData
    {
        public string Building { get; set; }
        public string Floor { get; set; }
        public string WorkName { get; set; }
        public string ItemName { get; set; }
        public string SubItemName { get; set; }
        public string DsrCode { get; set; }
        public string Unit { get; set; }
        public MeasurementRow Row { get; set; }
    }

    public enum ReportGenerationType
    {
        HeadwiseAbstract,
        HeadwiseMeasurement,
        ItemwiseAbstract,
        ItemwiseMeasurement
    }

    
    // --- MAIN WINDOW CLASS ---

    public partial class CommonReportWindow : Window
    {
        private List<RawReportData> _baseRawData = new List<RawReportData>();
        private List<ActiveFilter> _activeFilters = new List<ActiveFilter>();
        // Hides the Description toggle unless we are generating the Item-wise Abstract
        
        private string _currentTempPdfPath = "";
        private bool _isLoaded = false;
        private ReportGenerationType _reportType;

        public CommonReportWindow(ReportGenerationType reportType)
        {
            InitializeComponent();
            _reportType = reportType;

            // Dynamically adjust UI based on report type
            if (_reportType == ReportGenerationType.HeadwiseMeasurement || _reportType == ReportGenerationType.ItemwiseMeasurement)
            {
                PanelFees.Visibility = Visibility.Collapsed;
                this.Title = _reportType == ReportGenerationType.HeadwiseMeasurement ? "Head-wise Measurement Preview" : "Item-wise Measurement Preview";
            }
            else
            {
                this.Title = _reportType == ReportGenerationType.HeadwiseAbstract ? "Head-wise Abstract Preview" : "Item-wise Abstract Preview";
            }

            // Hides the Excel button unless we are generating the Item-wise Abstract
            BtnExportExcel.Visibility = _reportType == ReportGenerationType.ItemwiseAbstract ? Visibility.Visible : Visibility.Collapsed;

            ChkShowDescriptions.Visibility = _reportType == ReportGenerationType.ItemwiseAbstract ? Visibility.Visible : Visibility.Collapsed;

            ExtractBaseData();

            // Build dual filters
            LoadFlatFilterOptions();
            BuildTreeFilter();

            LoadFilterState();

            _isLoaded = true;
            GeneratePdfAndPreview();
            InitializeBrowserAsync();
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            SaveFilterState();
            base.OnClosing(e);
        }
        private async void InitializeBrowserAsync()
        {
            await PdfViewer.EnsureCoreWebView2Async(null);
            BtnGeneratePreview_Click(null, null);
        }

        private void ExtractBaseData()
        {
            var allRows = AppState.CurrentProject.WorkItems
                .SelectMany(wi => wi.Sheets
                    .FlattenMultiFloorSheets()
                    .SelectMany(sheet => sheet.Rows.Select(row => new
                    {
                        WorkItem = wi,
                        Building = string.IsNullOrWhiteSpace(sheet.BuildingName) ? "Unassigned Building" : sheet.BuildingName,
                        Floor = string.IsNullOrWhiteSpace(sheet.FloorName) ? "Unassigned Floor" : sheet.FloorName,
                        Row = row
                    })))
                .Where(x => x.Row.Ans != 0)
                .ToList();

            _baseRawData = (from data in allRows
                            join s in AppState.CurrentProject.MasterSubItems on data.WorkItem.SubItemID equals s.ID
                            join i in AppState.CurrentProject.MasterItems on s.ItemID equals i.ID
                            join w in AppState.CurrentProject.MasterWork on i.WorkNameID equals w.ID
                            join u in AppState.CurrentProject.MasterUnits on s.Unit equals u.ID.ToString() into unitGroup
                            from u in unitGroup.DefaultIfEmpty()
                            select new RawReportData
                            {
                                Building = data.Building,
                                Floor = data.Floor,
                                WorkName = w.WorkName,
                                ItemName = i.NameOfItem,
                                SubItemName = s.SubItem,
                                DsrCode = s.Code,
                                Unit = u != null && !string.IsNullOrWhiteSpace(u.Unitt) ? u.Unitt : s.Unit,
                                Row = data.Row
                            }).ToList();
        }

        private void FeeInput_TextChanged(object sender, TextChangedEventArgs e) { if (_isLoaded) GeneratePdfAndPreview(); }


        // --- DUAL-ENGINE FILTERING LOGIC ---

        private void ComboParameter_SelectionChanged(object sender, SelectionChangedEventArgs e) => LoadFlatFilterOptions();

        private void LoadFlatFilterOptions()
        {
            if (ComboParameter.SelectedItem == null || _baseRawData == null || ListFilterOptions == null) return;
            string selectedParam = ((ComboBoxItem)ComboParameter.SelectedItem).Content.ToString();
            List<string> uniqueValues = new List<string>();

            if (selectedParam == "Building Name") uniqueValues = _baseRawData.Select(x => x.Building).Distinct().ToList();
            else if (selectedParam == "Floor Name") uniqueValues = _baseRawData.Select(x => x.Floor).Distinct().ToList();
            else if (selectedParam == "Work Name") uniqueValues = _baseRawData.Select(x => x.WorkName).Distinct().ToList();
            else if (selectedParam == "Item Name") uniqueValues = _baseRawData.Select(x => x.ItemName).Distinct().ToList();

            // Check if there are active rules for this parameter and check the boxes accordingly
            var activeFilter = _activeFilters.FirstOrDefault(f => f.Parameter == selectedParam);
            var activeSet = activeFilter != null ? new HashSet<string>(activeFilter.SelectedValues) : new HashSet<string>();

            ListFilterOptions.ItemsSource = uniqueValues.OrderBy(x => x).Select(val => new FilterOption
            {
                Name = val,
                IsSelected = activeSet.Contains(val) // Visually applies the saved state
            }).ToList();
        }

        private void BuildTreeFilter()
        {
            var rootNodes = new ObservableCollection<FilterNode>();
            var bldgGroups = _baseRawData.GroupBy(x => x.Building).OrderBy(g => g.Key);

            foreach (var bg in bldgGroups)
            {
                var bNode = new FilterNode { Name = bg.Key };
                foreach (var fg in bg.GroupBy(x => x.Floor).OrderBy(g => g.Key))
                {
                    var fNode = new FilterNode { Name = fg.Key, Parent = bNode };
                    bNode.Children.Add(fNode);
                    foreach (var wg in fg.GroupBy(x => x.WorkName).OrderBy(g => g.Key))
                    {
                        var wNode = new FilterNode { Name = wg.Key, Parent = fNode };
                        fNode.Children.Add(wNode);
                        foreach (var ig in wg.GroupBy(x => x.ItemName).OrderBy(g => g.Key))
                        {
                            var iNode = new FilterNode { Name = ig.Key, Parent = wNode };
                            wNode.Children.Add(iNode);
                        }
                    }
                }
                rootNodes.Add(bNode);
            }
            TreeFilters.ItemsSource = rootNodes;
        }

        private void SyncTreeWithFlatFilters()
        {
            var rootNodes = TreeFilters.ItemsSource as ObservableCollection<FilterNode>;
            if (rootNodes == null) return;

            // We only need to evaluate the leaf nodes (Items). 
            // The Tree's internal logic will automatically update the parent Works, Floors, and Buildings!
            foreach (var bNode in rootNodes)
            {
                bool bPass = PassesFilter("Building Name", bNode.Name);
                foreach (var fNode in bNode.Children)
                {
                    bool fPass = bPass && PassesFilter("Floor Name", fNode.Name);
                    foreach (var wNode in fNode.Children)
                    {
                        bool wPass = fPass && PassesFilter("Work Name", wNode.Name);
                        foreach (var iNode in wNode.Children)
                        {
                            bool iPass = wPass && PassesFilter("Item Name", iNode.Name);
                            iNode.IsChecked = iPass; 
                        }
                    }
                }
            }
        }

        private bool PassesFilter(string parameter, string value)
        {
            var filter = _activeFilters.FirstOrDefault(f => f.Parameter == parameter);
            if (filter == null || filter.SelectedValues.Count == 0) return true; 
            return filter.SelectedValues.Contains(value);
        }

        private void BtnAddFilter_Click(object sender, RoutedEventArgs e)
        {
            string selectedParam = ((ComboBoxItem)ComboParameter.SelectedItem).Content.ToString();
            var options = (List<FilterOption>)ListFilterOptions.ItemsSource;
            var selectedValues = options.Where(x => x.IsSelected).Select(x => x.Name).ToList();

            if (selectedValues.Count > 0)
            {
                _activeFilters.RemoveAll(x => x.Parameter == selectedParam);
                _activeFilters.Add(new ActiveFilter { Parameter = selectedParam, SelectedValues = selectedValues });
                ListActiveFilters.ItemsSource = null;
                ListActiveFilters.ItemsSource = _activeFilters;
                foreach (var opt in options) opt.IsSelected = false;

                SyncTreeWithFlatFilters(); // <-- Syncs the Tree
                GeneratePdfAndPreview();
            }
        }

        private void ListActiveFilters_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == System.Windows.Input.Key.Delete && ListActiveFilters.SelectedItem != null)
            {
                _activeFilters.Remove((ActiveFilter)ListActiveFilters.SelectedItem);
                ListActiveFilters.ItemsSource = null;
                ListActiveFilters.ItemsSource = _activeFilters;

                SyncTreeWithFlatFilters(); // <-- Syncs the Tree
                GeneratePdfAndPreview();
            }
        }

        private void BtnClearFilters_Click(object sender, RoutedEventArgs e)
        {
            _activeFilters.Clear();
            ListActiveFilters.ItemsSource = null;

            SyncTreeWithFlatFilters(); // <-- Because active filters are clear, this re-checks everything
            GeneratePdfAndPreview();
        }

        private IEnumerable<RawReportData> GetFilteredData()
        {
            // Now we ONLY get paths checked in the Tree View! (Tree supersedes Global)
            var rootNodes = TreeFilters.ItemsSource as ObservableCollection<FilterNode>;
            if (rootNodes == null) return _baseRawData;

            var checkedPaths = new HashSet<string>();
            foreach (var bNode in rootNodes)
            {
                if (bNode.IsChecked == false) continue;
                foreach (var fNode in bNode.Children)
                {
                    if (fNode.IsChecked == false) continue;
                    foreach (var wNode in fNode.Children)
                    {
                        if (wNode.IsChecked == false) continue;
                        foreach (var iNode in wNode.Children)
                        {
                            if (iNode.IsChecked == true)
                                checkedPaths.Add($"{bNode.Name}|{fNode.Name}|{wNode.Name}|{iNode.Name}");
                        }
                    }
                }
            }

            return _baseRawData.Where(x => checkedPaths.Contains($"{x.Building}|{x.Floor}|{x.WorkName}|{x.ItemName}")).ToList();
        }

        private void BtnGeneratePreview_Click(object sender, RoutedEventArgs e) => GeneratePdfAndPreview();

        private void GeneratePdfAndPreview()
        {
            SaveFilterState();
            // CRITICAL: Pull data dynamically from the Dual Filtering Engine
            var filteredData = GetFilteredData();

            var bldgDict = AppState.CurrentProject.Buildings.ToDictionary(b => b.BuildingName, b => b.NumberOfBuildings);
            _currentTempPdfPath = Path.Combine(Path.GetTempPath(), $"EstimationPreview_{Guid.NewGuid()}.pdf");

            switch (_reportType)
            {
                case ReportGenerationType.HeadwiseAbstract: GenerateHeadwiseAbstract(filteredData, bldgDict); break;
                case ReportGenerationType.HeadwiseMeasurement: GenerateHeadwiseMeasurement(filteredData, bldgDict); break;
                case ReportGenerationType.ItemwiseAbstract: GenerateItemwiseAbstract(filteredData, bldgDict); break;
                case ReportGenerationType.ItemwiseMeasurement: GenerateItemwiseMeasurement(filteredData, bldgDict); break;
            }

            if (PdfViewer.CoreWebView2 != null && File.Exists(_currentTempPdfPath))
                PdfViewer.CoreWebView2.Navigate(_currentTempPdfPath);
        }


        // --- EXPORT FEATURES ---

        private void BtnExportExcel_Click(object sender, RoutedEventArgs e)
        {
            if (_reportType != ReportGenerationType.ItemwiseAbstract) return;

            var filteredData = GetFilteredData();
            var bldgDict = AppState.CurrentProject.Buildings.ToDictionary(b => b.BuildingName, b => b.NumberOfBuildings);

            // 1. Flatten the standard work items with their building multipliers
            var workData = filteredData.Select(x => new
            {
                DsrCode = string.IsNullOrWhiteSpace(x.DsrCode) ? "UNASSIGNED" : x.DsrCode,
                Quantity = (double)(x.Row.Ans * (bldgDict.ContainsKey(x.Building) ? bldgDict[x.Building] : 1))
            });

            // 2. Fetch the Material Testing & Royalty quantities
            // 2. Fetch the Material Testing & Royalty quantities
            var testData = AppState.CurrentProject.MaterialTests
                .Where(t => t.Quantity > 0)
                .Select(t => new
                {
                    // CRITICAL FIX: Fetch the actual Code instead of the Test Name
                    DsrCode = string.IsNullOrWhiteSpace(t.TestCode) ? "UNASSIGNED" : t.TestCode,
                    Quantity = (double)t.Quantity
                });

            // 3. Combine both lists, group them by Code, and calculate final totals
            var exportData = workData.Concat(testData)
                .GroupBy(x => x.DsrCode)
                .Select(g => new
                {
                    DsrCode = g.Key,
                    TotalQuantity = g.Sum(x => x.Quantity)
                })
                .Where(x => x.TotalQuantity > 0)
                .OrderBy(x => x.DsrCode)
                .ToList();

            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "Excel CSV File (*.csv)|*.csv",
                DefaultExt = ".csv",
                FileName = $"{AppState.CurrentProject.ProjectName} - DSR Quantities.csv"
            };

            if (dialog.ShowDialog() == true)
            {
                var lines = new List<string> { "DSR CODES,TOTAL QUANTITY" };
                lines.AddRange(exportData.Select(x => $"\"{x.DsrCode}\",{x.TotalQuantity:F2}"));

                File.WriteAllLines(dialog.FileName, lines);
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = dialog.FileName, UseShellExecute = true });
            }
        }

        private void BtnExportPdf_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_currentTempPdfPath) || !File.Exists(_currentTempPdfPath)) return;
            Microsoft.Win32.SaveFileDialog saveDialog = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "PDF Files (*.pdf)|*.pdf",
                DefaultExt = ".pdf",
                FileName = $"{AppState.CurrentProject.ProjectName} - {this.Title.Replace(" Preview", "")}.pdf"
            };

            if (saveDialog.ShowDialog() == true)
            {
                File.Copy(_currentTempPdfPath, saveDialog.FileName, true);
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = saveDialog.FileName, UseShellExecute = true });
            }
        }


        private void ChkShowDescriptions_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoaded) GeneratePdfAndPreview();
        }

        private void SaveFilterState()
        {
            if (AppState.CurrentProject.SavedFilters == null)
                AppState.CurrentProject.SavedFilters = new Dictionary<string, ReportFilterState>();

            var state = new ReportFilterState();

            // 1. Translate UI filters into pure strings
            foreach (var af in _activeFilters)
            {
                state.FlatFilters[af.Parameter] = new List<string>(af.SelectedValues);
            }

            // 2. Grab the tree paths
            var rootNodes = TreeFilters.ItemsSource as ObservableCollection<FilterNode>;
            if (rootNodes != null)
            {
                foreach (var bNode in rootNodes)
                    foreach (var fNode in bNode.Children)
                        foreach (var wNode in fNode.Children)
                            foreach (var iNode in wNode.Children)
                                if (iNode.IsChecked == true)
                                    state.CheckedTreePaths.Add($"{bNode.Name}|{fNode.Name}|{wNode.Name}|{iNode.Name}");
            }

            // 3. Save directly to the live project object
            AppState.CurrentProject.SavedFilters[_reportType.ToString()] = state;
        }

        private void LoadFilterState()
        {
            if (AppState.CurrentProject.SavedFilters == null || !AppState.CurrentProject.SavedFilters.ContainsKey(_reportType.ToString()))
                return;

            var state = AppState.CurrentProject.SavedFilters[_reportType.ToString()];

            // 1. Rebuild UI filters from the pure strings
            _activeFilters.Clear();
            if (state.FlatFilters != null)
            {
                foreach (var kvp in state.FlatFilters)
                {
                    _activeFilters.Add(new ActiveFilter { Parameter = kvp.Key, SelectedValues = new List<string>(kvp.Value) });
                }
            }
            ListActiveFilters.ItemsSource = null;
            ListActiveFilters.ItemsSource = _activeFilters;

            // 2. Restore Tree Paths
            var rootNodes = TreeFilters.ItemsSource as ObservableCollection<FilterNode>;
            if (rootNodes != null && state.CheckedTreePaths != null)
            {
                var checkedSet = new HashSet<string>(state.CheckedTreePaths);
                foreach (var bNode in rootNodes)
                    foreach (var fNode in bNode.Children)
                        foreach (var wNode in fNode.Children)
                            foreach (var iNode in wNode.Children)
                            {
                                string path = $"{bNode.Name}|{fNode.Name}|{wNode.Name}|{iNode.Name}";
                                iNode.IsChecked = checkedSet.Contains(path);
                            }
            }

            LoadFlatFilterOptions();
        }

        // --- THE 4 GENERATION ENGINES ---

        private void GenerateHeadwiseAbstract(IEnumerable<RawReportData> filteredData, Dictionary<string, int> bldgDict)
        {
            var query = filteredData.GroupBy(d => new { d.Building, d.Floor, d.WorkName, d.ItemName, d.SubItemName, d.DsrCode, d.Unit })
                .Select(g => new {
                    g.Key.Building,
                    g.Key.Floor,
                    g.Key.WorkName,
                    g.Key.ItemName,
                    g.Key.SubItemName,
                    g.Key.DsrCode,
                    g.Key.Unit,
                    Quantity = g.Sum(r => r.Row.Ans * (bldgDict.ContainsKey(r.Building) ? bldgDict[r.Building] : 1)), // BUILDING MULTIPLIER ADDED
                    Rate = AppState.CurrentProject.MasterSubItems.FirstOrDefault(s => s.Code == g.Key.DsrCode && s.SubItem == g.Key.SubItemName)?.Rate ?? 0
                }).Where(x => x.Quantity > 0).ToList();

            var groupedData = query.GroupBy(q => q.Building).Select(bg => new AbstractBuildingGroup
            {
                BuildingName = bg.Key,
                Floors = bg.GroupBy(f => f.Floor).Select(fg => new AbstractFloorGroup
                {
                    FloorName = fg.Key,
                    Works = fg.GroupBy(w => w.WorkName).Select(wg => new AbstractWorkGroup
                    {
                        WorkName = wg.Key,
                        Items = wg.GroupBy(i => i.ItemName).Select(ig => new AbstractItemGroup
                        {
                            ItemName = ig.Key,
                            SubItems = ig.Select(si => new AbstractReportRow
                            {
                                DsrCode = si.DsrCode,
                                SubItemName = si.SubItemName,
                                Unit = si.Unit,
                                Quantity = si.Quantity,
                                Rate = si.Rate
                            }).ToList()
                        }).ToList()
                    }).ToList()
                }).ToList()
            }).OrderBy(b => b.BuildingName).ToList();

            var pdfTotals = CalculateTaxesAndTotals(groupedData.Sum(b => b.Floors.Sum(f => f.Works.Sum(w => w.Items.Sum(i => i.SubItems.Sum(s => s.Amount))))));
            new AbstractPdfDocument(groupedData, AppState.CurrentProject, pdfTotals).GeneratePdf(_currentTempPdfPath);
        }

        private void GenerateHeadwiseMeasurement(IEnumerable<RawReportData> filteredData, Dictionary<string, int> bldgDict)
        {
            var groupedData = filteredData.GroupBy(q => q.Building).Select(bg => new MeasBuildingGroup
            {
                BuildingName = bg.Key,
                NumberOfBuildings = bldgDict.ContainsKey(bg.Key) ? bldgDict[bg.Key] : 1,
                Works = bg.GroupBy(w => w.WorkName).Select(wg => new MeasWorkGroup
                {
                    WorkName = wg.Key,
                    Items = wg.GroupBy(i => i.ItemName).Select(ig => new MeasItemGroup
                    {
                        ItemName = ig.Key,
                        Floors = ig.GroupBy(f => f.Floor).Select(fg => new MeasFloorGroup
                        {
                            FloorName = fg.Key,
                            SubItems = fg.GroupBy(si => new { si.DsrCode, si.SubItemName, si.Unit }).Select(sg => new MeasSubItemGroup
                            {
                                DsrCode = sg.Key.DsrCode,
                                SubItemName = sg.Key.SubItemName,
                                Unit = sg.Key.Unit,
                                Rows = sg.Select(r => new MeasRowDto
                                {
                                    Remark = ExpressionEvaluator.EvaluateRemark(r.Row.Remark),
                                    Factor = ExpressionEvaluator.Evaluate(r.Row.Factor)?.ToString() ?? "1",
                                    Nos = ExpressionEvaluator.Evaluate(r.Row.Nos)?.ToString() ?? "1",
                                    L = ExpressionEvaluator.Evaluate(r.Row.L)?.ToString() ?? "",
                                    B = ExpressionEvaluator.Evaluate(r.Row.B)?.ToString() ?? "",
                                    H = ExpressionEvaluator.Evaluate(r.Row.H)?.ToString() ?? "",
                                    Quantity = r.Row.Ans
                                }).ToList()
                            }).ToList()
                        }).ToList()
                    }).ToList()
                }).ToList()
            }).OrderBy(b => b.BuildingName).ToList();

            new MeasurementPdfDocument(groupedData, AppState.CurrentProject).GeneratePdf(_currentTempPdfPath);
        }

        private void GenerateItemwiseAbstract(IEnumerable<RawReportData> filteredData, Dictionary<string, int> bldgDict)
        {
            var query = filteredData.GroupBy(d => new { d.WorkName, d.ItemName, d.SubItemName, d.DsrCode, d.Unit })
                .Select(g => new {
                    g.Key.WorkName,
                    g.Key.ItemName,
                    g.Key.SubItemName,
                    g.Key.DsrCode,
                    g.Key.Unit,
                    Quantity = g.Sum(r => r.Row.Ans * (bldgDict.ContainsKey(r.Building) ? bldgDict[r.Building] : 1)),
                    Rate = AppState.CurrentProject.MasterSubItems.FirstOrDefault(s => s.Code == g.Key.DsrCode && s.SubItem == g.Key.SubItemName)?.Rate ?? 0,
                    Description = AppState.CurrentProject.MasterItems.FirstOrDefault(i => i.NameOfItem == g.Key.ItemName)?.Description ?? "",
                    Locations = g.Select(r => $"{r.Floor}\n{r.Building}").Distinct().ToList()
                }).Where(x => x.Quantity > 0).ToList();

            var groupedData = query.GroupBy(w => w.WorkName).Select(wg => new ItemwiseWorkGroup
            {
                WorkName = wg.Key,
                Items = wg.GroupBy(i => i.ItemName).Select(ig => new ItemwiseItemGroup
                {
                    ItemName = ig.Key,
                    SubItems = ig.Select(si => new ItemwiseReportRow
                    {
                        DsrCode = si.DsrCode,
                        SubItemName = si.SubItemName,
                        Unit = si.Unit,
                        Quantity = si.Quantity,
                        Rate = si.Rate,
                        Description = si.Description,
                        Locations = si.Locations
                    }).ToList()
                }).ToList()
            }).OrderBy(w => w.WorkName).ToList();

            var pdfTotals = CalculateTaxesAndTotals(groupedData.Sum(w => w.WorkTotalAmount));

            // PASS THE CHECKBOX STATE HERE:
            bool showDesc = ChkShowDescriptions.IsChecked ?? true;
            new ItemwiseAbstractPdfDocument(groupedData, AppState.CurrentProject, pdfTotals, showDesc).GeneratePdf(_currentTempPdfPath);
        }

        private void GenerateItemwiseMeasurement(IEnumerable<RawReportData> filteredData, Dictionary<string, int> bldgDict)
        {
            var groupedData = filteredData.GroupBy(q => q.WorkName).Select(wg => new ItemwiseMeasWorkGroup
            {
                WorkName = wg.Key,
                Items = wg.GroupBy(i => new { i.DsrCode, i.ItemName, i.SubItemName, i.Unit }).Select(ig => new ItemwiseMeasSubItemGroup
                {
                    DsrCode = ig.Key.DsrCode,
                    ItemName = ig.Key.ItemName,
                    SubItemName = ig.Key.SubItemName,
                    Unit = ig.Key.Unit,
                    Buildings = ig.GroupBy(b => b.Building).Select(bg => new ItemwiseMeasBuildingGroup
                    {
                        BuildingName = bg.Key,
                        NumberOfBuildings = bldgDict.ContainsKey(bg.Key) ? bldgDict[bg.Key] : 1, // BUILDING MULTIPLIER ADDED
                        Floors = bg.GroupBy(f => f.Floor).Select(fg => new ItemwiseMeasFloorGroup
                        {
                            FloorName = fg.Key,
                            Rows = fg.Select(r => new ItemwiseMeasRowDto
                            {
                                Remark = ExpressionEvaluator.EvaluateRemark(r.Row.Remark),
                                Factor = ExpressionEvaluator.Evaluate(r.Row.Factor)?.ToString() ?? "1",
                                Nos = ExpressionEvaluator.Evaluate(r.Row.Nos)?.ToString() ?? "1",
                                L = ExpressionEvaluator.Evaluate(r.Row.L)?.ToString() ?? "",
                                B = ExpressionEvaluator.Evaluate(r.Row.B)?.ToString() ?? "",
                                H = ExpressionEvaluator.Evaluate(r.Row.H)?.ToString() ?? "",
                                Quantity = r.Row.Ans
                            }).ToList()
                        }).ToList()
                    }).ToList()
                }).ToList()
            }).OrderBy(w => w.WorkName).ToList();

            new ItemwiseMeasurementPdfDocument(groupedData, AppState.CurrentProject).GeneratePdf(_currentTempPdfPath);
        }

        // --- REUSABLE TAX ENGINE ---
        private AbstractPdfTotals CalculateTaxesAndTotals(double baseTotal)
        {
            double gstPercent = double.TryParse(TxtGst.Text, out double g) ? g : 0;
            double structPercent = double.TryParse(TxtStructFee.Text, out double st) ? st : 0;
            double archPercent = double.TryParse(TxtArchFee.Text, out double a) ? a : 0;

            double gstAmount = baseTotal * (gstPercent / 100);
            double structAmount = baseTotal * (structPercent / 100);
            double archAmount = baseTotal * (archPercent / 100);

            var activeTests = AppState.CurrentProject.MaterialTests.Where(t => t.Quantity > 0).ToList();
            var royaltyTest = activeTests.FirstOrDefault(t => t.TestName.IndexOf("Royalty", StringComparison.OrdinalIgnoreCase) >= 0)
                              ?? AppState.CurrentProject.MaterialTests.FirstOrDefault(t => t.TestName.IndexOf("Royalty", StringComparison.OrdinalIgnoreCase) >= 0);

            if (royaltyTest != null) activeTests.Remove(royaltyTest);
            double royaltyRate = royaltyTest?.Rate ?? 0;
            double royaltyQty = royaltyTest?.Quantity ?? 0;
            double royaltyAmt = royaltyQty * royaltyRate;

            double testsTotal = activeTests.Sum(t => t.Quantity * t.Rate) + royaltyAmt;
            double grandTotal = baseTotal + gstAmount + structAmount + archAmount + testsTotal;

            return new AbstractPdfTotals
            {
                BaseTotal = baseTotal,
                GstPercent = gstPercent,
                GstAmount = gstAmount,
                StructPercent = structPercent,
                StructAmount = structAmount,
                ArchPercent = archPercent,
                ArchAmount = archAmount,
                RoyaltyRate = royaltyRate,
                RoyaltyQty = royaltyQty,
                RoyaltyAmount = royaltyAmt,
                ActiveTests = activeTests,
                TestsTotal = testsTotal,
                GrandTotal = grandTotal
            };
        }
    }
}