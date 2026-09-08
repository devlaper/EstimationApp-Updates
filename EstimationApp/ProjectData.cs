using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace EstimationApp
{
    public static class AppState
    {
        public static ProjectDetails CurrentProject { get; set; } = new ProjectDetails();
        public static string CurrentFilePath { get; set; } = "";
    }

    public class SubItemDisplay
    {
        public int SubItemID { get; set; }
        public string DisplayName { get; set; }
        public double Rate { get; set; }
        public string Unit { get; set; }
        public string Code { get; set; }
        public string NameOfItem { get; set; }
        public string SubItem { get; set; }
        public string WorkName { get; set; }
        public string Description { get; set; }

        // --- ADD THIS TO FIX THE COMBOBOX TEXT ---
        public override string ToString()
        {
            // Whenever WPF asks for the text version of this object, give it the DisplayName
            return DisplayName;
        }
    }


    public class ProjectVariable : INotifyPropertyChanged
    {
        private string _name;
        public string Name { get => _name; set { _name = value; OnPropertyChanged(nameof(Name)); } }

        private double _value;
        public double Value { get => _value; set { _value = value; OnPropertyChanged(nameof(Value)); } }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged(string prop) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(prop));
    }

    public static class ExpressionEvaluator
    {
        public static double? Evaluate(string expression)
        {
            if (string.IsNullOrWhiteSpace(expression)) return null;
            try
            {
                string parsed = expression;
                if (AppState.CurrentProject?.Variables != null)
                {
                    // Sort by length to prevent partial word replacements (e.g. replacing 'a' inside 'aa')
                    foreach (var v in AppState.CurrentProject.Variables.OrderByDescending(x => x.Name?.Length ?? 0))
                    {
                        if (string.IsNullOrWhiteSpace(v.Name)) continue;
                        // Replace exact word boundaries so "a" doesn't trigger inside "wall"
                        parsed = System.Text.RegularExpressions.Regex.Replace(parsed, @"\b" + System.Text.RegularExpressions.Regex.Escape(v.Name) + @"\b", v.Value.ToString());
                    }
                }
                var result = new System.Data.DataTable().Compute(parsed, null);
                return Convert.ToDouble(result);
            }
            catch { return null; } // Failsafe fallback
        }

        public static string EvaluateRemark(string remark)
        {
            if (string.IsNullOrWhiteSpace(remark)) return remark;
            string parsed = remark;
            if (AppState.CurrentProject?.Variables != null)
            {
                foreach (var v in AppState.CurrentProject.Variables.OrderByDescending(x => x.Name?.Length ?? 0))
                {
                    if (string.IsNullOrWhiteSpace(v.Name)) continue;
                    // Replace quoted variables like 'a' or "a" first
                    parsed = parsed.Replace($"'{v.Name}'", v.Value.ToString());
                    parsed = parsed.Replace($"\"{v.Name}\"", v.Value.ToString());
                    // Replace word boundaries
                    parsed = System.Text.RegularExpressions.Regex.Replace(parsed, @"\b" + System.Text.RegularExpressions.Regex.Escape(v.Name) + @"\b", v.Value.ToString());
                }
            }
            return parsed;
        }
    }
    public class ReportRow
    {
        public string WorkName { get; set; }
        public string ItemName { get; set; }
        public string SubItemName { get; set; }
        public string DsrCode { get; set; }
        public string Unit { get; set; }
        public double Rate { get; set; }
        public double Quantity { get; set; }
        public double Amount => Rate * Quantity;
    }

    public class ReportFloor
    {
        public string FloorName { get; set; }
        public List<ReportRow> Rows { get; set; } = new List<ReportRow>();
        public double FloorTotal => Rows.Sum(r => r.Amount);
    }

    public class ReportBuilding
    {
        public string BuildingName { get; set; }
        public List<ReportFloor> Floors { get; set; } = new List<ReportFloor>();
        public double BuildingTotal => Floors.Sum(f => f.FloorTotal);
    }

    public class BuildingGroup
    {
        public string BuildingName { get; set; }
        public int NumberOfBuildings { get; set; }
    }

    public class FloorEntry
    {
        public string FloorName { get; set; }
    }

    public class ProjectDetails
    {
        public string ProjectName { get; set; }
        public string OwnerName { get; set; }
        public string ReferenceNo { get; set; } = DateTime.Now.ToString("yyyy-MM-dd");

        public ObservableCollection<BuildingGroup> Buildings { get; set; } = new ObservableCollection<BuildingGroup>();
        public ObservableCollection<FloorEntry> Floors { get; set; } = new ObservableCollection<FloorEntry>();
        public ObservableCollection<WorkItemEntry> WorkItems { get; set; } = new ObservableCollection<WorkItemEntry>();



        public List<WorkTable> MasterWork { get; set; } = new List<WorkTable>();
        public List<ItemTable> MasterItems { get; set; } = new List<ItemTable>();
        public List<SubItemTable> MasterSubItems { get; set; } = new List<SubItemTable>();
        public List<UnitTable> MasterUnits { get; set; } = new List<UnitTable>();

        // NEW ADDITIONS
        public ObservableCollection<MarkerEntry> Markers { get; set; } = new ObservableCollection<MarkerEntry>
        {
            new MarkerEntry { MarkerName = "None", MarkerColor = "Transparent" }
        };
        public ObservableCollection<RoomEntry> Rooms { get; set; } = new ObservableCollection<RoomEntry>();
        public ObservableCollection<ProjectVariable> Variables { get; set; } = new ObservableCollection<ProjectVariable>();
        public ObservableCollection<MaterialTest> MaterialTests { get; set; } = new ObservableCollection<MaterialTest>();
        // Stores the filter states for the 4 different report types
        // The { get; set; } is MANDATORY, otherwise the save file ignores it!
        public string SavedReportFiltersJson { get; set; }
        // Native dictionary format
        public Dictionary<string, ReportFilterState> SavedFilters { get; set; } = new Dictionary<string, ReportFilterState>();
        public double Budget { get; set; }

        public void SaveToFile(string filePath)
        {
            var options = new System.Text.Json.JsonSerializerOptions { WriteIndented = true };
            string json = System.Text.Json.JsonSerializer.Serialize(AppState.CurrentProject, options);
            File.WriteAllText(filePath, json);
        }

        public void LoadDataIntoCurrentInstance(string filePath)
        {
            // 1. Load into a temporary object so we don't break WPF memory references
            string json = File.ReadAllText(filePath);

            // --- FIX 1: Robust Load Options ---
            var loadOptions = new System.Text.Json.JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                IncludeFields = true
            };
            var loadedData = System.Text.Json.JsonSerializer.Deserialize<ProjectDetails>(json, loadOptions);

            if (loadedData != null)
            {
                // 2. Transfer standard properties
                this.ProjectName = loadedData.ProjectName;
                this.OwnerName = loadedData.OwnerName;
                this.ReferenceNo = loadedData.ReferenceNo;
                this.Budget = loadedData.Budget;

                // --- FIX 2: TRANSFER THE SAVED FILTERS ---
                this.SavedFilters = loadedData.SavedFilters ?? new Dictionary<string, ReportFilterState>();
                // -----------------------------------------

                // --- CRITICAL FIX: TRANSFER THE MASTER DATABASE DICTIONARIES ---
                // Without these, the app cannot look up the Rates, Units, or DsrCodes for older files!
                this.MasterWork = loadedData.MasterWork ?? new List<WorkTable>();
                this.MasterItems = loadedData.MasterItems ?? new List<ItemTable>();
                this.MasterSubItems = loadedData.MasterSubItems ?? new List<SubItemTable>();
                this.MasterUnits = loadedData.MasterUnits ?? new List<UnitTable>();
                // ---------------------------------------------------------------

                // 3. Clear and repopulate WorkItems (This instantly updates the UI)
                this.WorkItems.Clear();
                foreach (var workItem in loadedData.WorkItems)
                {
                    // Force typography and bindings to wake up
                    workItem.DisplayWorkName = workItem.DisplayWorkName;
                    workItem.DisplayItemName = workItem.DisplayItemName;

                    foreach (var sheet in workItem.Sheets)
                    {
                        sheet.BuildingName = sheet.BuildingName;
                        sheet.FloorName = sheet.FloorName;

                        foreach (var row in sheet.Rows)
                        {
                            // Triggers the ExpressionEvaluator for live costs
                            row.L = row.L;
                            row.B = row.B;
                            row.H = row.H;
                            row.Factor = row.Factor;
                            row.Nos = row.Nos;
                        }
                        sheet.UpdateSheetState();
                    }
                    workItem.UpdateState();

                    // The magic line: WPF sees the add and renders the tree/reports!
                    this.WorkItems.Add(workItem);
                }

                // 4. Transfer all other UI collections
                this.Buildings.Clear();
                foreach (var b in loadedData.Buildings) this.Buildings.Add(b);

                this.Floors.Clear();
                foreach (var f in loadedData.Floors) this.Floors.Add(f);

                this.Variables.Clear();
                foreach (var v in loadedData.Variables) this.Variables.Add(v);

                this.Markers.Clear();
                if (loadedData.Markers != null && loadedData.Markers.Count > 0)
                {
                    foreach (var m in loadedData.Markers) this.Markers.Add(m);
                }
                else
                {
                    this.Markers.Add(new MarkerEntry { MarkerName = "None", MarkerColor = "Transparent" });
                }

                this.Rooms.Clear();
                foreach (var r in loadedData.Rooms) this.Rooms.Add(r);

                this.MaterialTests.Clear();
                foreach (var m in loadedData.MaterialTests) this.MaterialTests.Add(m);

                // Ensure standard tests exist in the loaded file
                this.SyncMissingMaterialTests();
                this.PromptFloorMappingIfNeeded();
            }
        }
    }

    public class WorkTable
    {
        public int ID { get; set; }
        public string WorkName { get; set; }
    }

    public class UnitTable
    {
        public int ID { get; set; }
        public string Unitt { get; set; }
    }

    public class ItemTable
    {
        public int ID { get; set; }
        public int WorkNameID { get; set; }
        public string NameOfItem { get; set; }
        public string Description { get; set; }
        public string Note { get; set; }
        public string AreaFactor { get; set; }
        public string Chapter { get; set; }
    }

    public class SubItemTable
    {
        public int ID { get; set; }
        public int ItemID { get; set; }
        public int MeasurementID { get; set; }
        public string SubItem { get; set; }
        public string Code { get; set; }
        public string Unit { get; set; }
        public double Rate { get; set; }
        public string Note { get; set; }
    }

    public class MeasurementRow : INotifyPropertyChanged
    {
        private string _a;
        private string _l;
        private string _b;
        private string _h;
        private string _factor = "1";
        private string _nos = "1";
        private string _remark;

        public int ID { get; set; } = new Random().Next(1000, 9999);
        public string Remark { get => _remark; set { _remark = value; OnPropertyChanged(nameof(Remark)); } }

        public string Quantity_A { get => _a; set { _a = value; OnPropertyChanged(nameof(Quantity_A)); OnPropertyChanged(nameof(Ans)); } }
        public string Factor { get => _factor; set { _factor = value; OnPropertyChanged(nameof(Factor)); OnPropertyChanged(nameof(Ans)); } }
        public string Nos { get => _nos; set { _nos = value; OnPropertyChanged(nameof(Nos)); OnPropertyChanged(nameof(Ans)); } }
        public string L { get => _l; set { _l = value; OnPropertyChanged(nameof(L)); OnPropertyChanged(nameof(Ans)); } }
        public string B { get => _b; set { _b = value; OnPropertyChanged(nameof(B)); OnPropertyChanged(nameof(Ans)); } }
        public string H { get => _h; set { _h = value; OnPropertyChanged(nameof(H)); OnPropertyChanged(nameof(Ans)); } }

        public double Ans
        {
            get
            {
                // Instantly checks for direct formula entry in the Answer column
                double? valA = ExpressionEvaluator.Evaluate(Quantity_A);
                if (valA.HasValue) return valA.Value;

                // Parses formulas dynamically
                double l = ExpressionEvaluator.Evaluate(L) ?? 1.0;
                double b = ExpressionEvaluator.Evaluate(B) ?? 1.0;
                double h = ExpressionEvaluator.Evaluate(H) ?? 1.0;
                double f = ExpressionEvaluator.Evaluate(Factor) ?? 1.0;
                double n = ExpressionEvaluator.Evaluate(Nos) ?? 1.0;

                return l * b * h * f * n;
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public class MeasurementSheet : INotifyPropertyChanged
    {
        private string _buildingName;
        public string BuildingName { get => _buildingName; set { _buildingName = value; OnPropertyChanged(nameof(BuildingName)); OnPropertyChanged(nameof(DisplayName)); } }

        private string _floorName;
        public string FloorName
        {
            get => _floorName;
            set
            {
                _floorName = value;
                OnPropertyChanged(nameof(FloorName));
                OnPropertyChanged(nameof(FloorCount)); // NEW: Notify the counter
                OnPropertyChanged(nameof(SheetTotal)); // NEW: Recalculate totals when floors change
                OnPropertyChanged(nameof(DisplayName));
            }
        }

        // NEW: Automatically counts how many floors are comma-separated in the string
        public int FloorCount
        {
            get
            {
                if (string.IsNullOrWhiteSpace(FloorName)) return 1;
                return FloorName.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Length;
            }
        }




        private string _markerName = "None";
        public string MarkerName
        {
            get => string.IsNullOrEmpty(_markerName) ? "None" : _markerName;
            set
            {
                // If it tries to set null/empty, force it back to "None"
                _markerName = string.IsNullOrEmpty(value) ? "None" : value;
                OnPropertyChanged(nameof(MarkerName));
                OnPropertyChanged(nameof(MarkerColor)); // Triggers the UI color change
            }
        }

        // Dynamically fetches the color assigned to this marker in the Project Setup
        [System.Text.Json.Serialization.JsonIgnore]
        public string MarkerColor
        {
            get
            {
                if (string.IsNullOrEmpty(MarkerName) || AppState.CurrentProject == null) return null;
                var marker = AppState.CurrentProject.Markers.FirstOrDefault(m => m.MarkerName == MarkerName);

                string color = marker?.MarkerColor;
                // Prevent invisible text by converting Transparent to null (triggers the XAML fallback color)
                return (string.IsNullOrEmpty(color) || color == "Transparent") ? null : color;
            }
        }

        private string _roomName;
        public string RoomName { get => _roomName; set { _roomName = value; OnPropertyChanged(nameof(RoomName)); } }

        public ObservableCollection<MeasurementRow> Rows { get; set; } = new ObservableCollection<MeasurementRow>();
        public double SheetTotal => Rows.Sum(r => r.Ans) * FloorCount;

        // NEW: Used for fading empty items in Building Mode
        public bool HasData => Math.Abs(SheetTotal) > 0.0001;

        // NEW: Used to highlight the active tab in the Explorer
        private bool _isActiveNode;
        public bool IsActiveNode { get => _isActiveNode; set { _isActiveNode = value; OnPropertyChanged(nameof(IsActiveNode)); } }

        private bool _isExpanded;
        public bool IsExpanded { get => _isExpanded; set { _isExpanded = value; OnPropertyChanged(nameof(IsExpanded)); } }

        public string DisplayName
        {
            get
            {
                string bName = string.IsNullOrEmpty(BuildingName) ? "Unassigned Building" : BuildingName;
                string fName = string.IsNullOrEmpty(FloorName) ? "Unassigned Floor" : FloorName;
                return $"{bName} - {fName}";
            }
        }

        public void UpdateSheetState()
        {
            OnPropertyChanged(nameof(SheetTotal));
            OnPropertyChanged(nameof(DisplayName));
            OnPropertyChanged(nameof(HasData)); // Triggers the fade effect
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public class WorkItemEntry : INotifyPropertyChanged
    {
        public int SubItemID { get; set; }

        // NEW: Split names to allow dynamic typography
        private string _displayWorkName = "New Work Group";
        public string DisplayWorkName { get => _displayWorkName; set { _displayWorkName = value; OnPropertyChanged(nameof(DisplayWorkName)); OnPropertyChanged(nameof(DisplayName)); } }

        private string _displayItemName = "Select an item...";
        public string DisplayItemName { get => _displayItemName; set {  
                _displayItemName = value;
                OnPropertyChanged(nameof(DisplayItemName));
                OnPropertyChanged(nameof(DisplayName)); }
        }



        [System.Text.Json.Serialization.JsonIgnore]
        public string DisplayName => $"{DisplayWorkName} - {TextFormatters.GetSmartItemName(DisplayItemName)}";
        [System.Text.Json.Serialization.JsonIgnore]
       
        public string UIItemName => TextFormatters.GetSmartItemName(DisplayItemName);

        // NEW: Used for highlighting and fading
        private bool _isActiveNode;
        public bool IsActiveNode { get => _isActiveNode; set { _isActiveNode = value; OnPropertyChanged(nameof(IsActiveNode)); } }

        private bool _isExpanded;

        // [JsonIgnore] prevents the app from trying to save the "open/closed" UI state into your .est files
        [System.Text.Json.Serialization.JsonIgnore]
        public bool IsExpanded
        {
            get => _isExpanded;
            set
            {
                if (_isExpanded != value)
                {
                    _isExpanded = value;
                    OnPropertyChanged(nameof(IsExpanded));
                }
            }
        }
        public bool HasData => Sheets.Any(s => s.HasData);

        public ObservableCollection<MeasurementSheet> Sheets { get; set; } = new ObservableCollection<MeasurementSheet>();

        public void UpdateState()
        {
            OnPropertyChanged(nameof(HasData));
        }



        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }




    public class TreeBuildingGroup
    {
        public string BuildingName { get; set; }
        public bool IsActiveNode { get; set; }
        public bool IsExpanded { get; set; } = true;
        // CHANGED: It now holds Floors instead of direct items
        public ObservableCollection<TreeFloorGroup> Floors { get; set; } = new ObservableCollection<TreeFloorGroup>();
    }

    // NEW: The non-collapsible Floor subheader
    public class TreeFloorGroup
    {
        public string FloorName { get; set; }
        public bool IsExpanded { get; set; } = true;
        public ObservableCollection<TreeWorkItemGroup> Items { get; set; } = new ObservableCollection<TreeWorkItemGroup>();
    }

    public class TreeWorkItemGroup
    {
        public string DisplayWorkName { get; set; }
        public string DisplayItemName { get; set; }
        public bool IsActiveNode { get; set; }
        public WorkItemEntry OriginalWorkItem { get; set; }
        public MeasurementSheet OriginalSheet { get; set; }

        public string UIItemName => TextFormatters.GetSmartItemName(DisplayItemName);
    }


    public class MarkerEntry : INotifyPropertyChanged
    {
        private string _markerName;
        public string MarkerName { get => _markerName; set { _markerName = value; OnPropertyChanged(nameof(MarkerName)); } }

        private string _markerColor = "Transparent";
        public string MarkerColor { get => _markerColor; set { _markerColor = value; OnPropertyChanged(nameof(MarkerColor)); } }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public class RoomEntry
    {
        public string RoomName { get; set; }
    }

    public class MaterialTest : INotifyPropertyChanged
    {
        private double _quantity;

        // NEW: Property to hold the 5-digit Test Code
        public string TestCode { get; set; }

        public string TestName { get; set; }
        public double Rate { get; set; }
        public string Description { get; set; }

        public double Quantity
        {
            get => _quantity;
            set { _quantity = value; OnPropertyChanged(nameof(Quantity)); }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    // --- ITEM-WISE REPORT CLASSES ---
    public class ItemwiseReportRow

    {
        public string WorkName { get; set; }
        public string DsrCode { get; set; }
        public string SubItemName { get; set; }
        public string Unit { get; set; }
        public double Quantity { get; set; }
        public double Rate { get; set; }
        public double Amount => Quantity * Rate;

        // NEW: Needed for the new PDF layout
        public string Description { get; set; }
        public List<string> Locations { get; set; } = new List<string>();
    }

    public class ItemwiseItemGroup
    {
        public string ItemName { get; set; }
        public List<ItemwiseReportRow> SubItems { get; set; } = new List<ItemwiseReportRow>();
    }

    public class ItemwiseWorkGroup
    {
        public string WorkName { get; set; }
        public List<ItemwiseItemGroup> Items { get; set; } = new List<ItemwiseItemGroup>();

        // NEW: Calculates the purple "Total=" for the Work head
        public double WorkTotalAmount => Items.Sum(i => i.SubItems.Sum(s => s.Amount));
    }

    // --- MEASUREMENT SHEET REPORT CLASSES ---
    public class MeasRowDto
    {
        public string Remark { get; set; }
        public string Factor { get; set; }
        public string Nos { get; set; }
        public string L { get; set; }
        public string B { get; set; }
        public string H { get; set; }
        public double Quantity { get; set; }
    }

    public class MeasSubItemGroup
    {
        public string DsrCode { get; set; }
        public string SubItemName { get; set; }
        public string Unit { get; set; }
        public List<MeasRowDto> Rows { get; set; } = new List<MeasRowDto>();
        public double TotalQuantity => Rows.Sum(r => r.Quantity);
    }

    public class MeasFloorGroup
    {
        public string FloorName { get; set; }
        public List<MeasSubItemGroup> SubItems { get; set; } = new List<MeasSubItemGroup>();
    }

    public class MeasItemGroup
    {
        public string ItemName { get; set; }
        public List<MeasFloorGroup> Floors { get; set; } = new List<MeasFloorGroup>();
    }

    public class MeasWorkGroup
    {
        public string WorkName { get; set; }
        public List<MeasItemGroup> Items { get; set; } = new List<MeasItemGroup>();
    }

    public class MeasBuildingGroup
    {
        public string BuildingName { get; set; }
        public int NumberOfBuildings { get; set; }
        public List<MeasWorkGroup> Works { get; set; } = new List<MeasWorkGroup>();
    }

    // --- ITEM-WISE MEASUREMENT REPORT CLASSES ---
    public class ItemwiseMeasRowDto
    {
        public string Remark { get; set; }
        public string Factor { get; set; }
        public string Nos { get; set; }
        public string L { get; set; }
        public string B { get; set; }
        public string H { get; set; }
        public double Quantity { get; set; }
    }

    public class ItemwiseMeasFloorGroup
    {
        public string FloorName { get; set; }
        public List<ItemwiseMeasRowDto> Rows { get; set; } = new List<ItemwiseMeasRowDto>();
        public double FloorTotal => Rows.Sum(r => r.Quantity);
    }

    public class ItemwiseMeasBuildingGroup
    {
        public string BuildingName { get; set; }
        public double NumberOfBuildings { get; set; } // NEW: Used for the Modul Units Multiplier
        public List<ItemwiseMeasFloorGroup> Floors { get; set; } = new List<ItemwiseMeasFloorGroup>();
        public double BuildingBaseTotal => Floors.Sum(f => f.FloorTotal);
        public double BuildingFinalTotal => BuildingBaseTotal * NumberOfBuildings; // NEW: Pre-multiplied sum
    }

    public class ItemwiseMeasSubItemGroup
    {
        public string DsrCode { get; set; }
        public string ItemName { get; set; }
        public string SubItemName { get; set; }
        public string Unit { get; set; }
        public List<ItemwiseMeasBuildingGroup> Buildings { get; set; } = new List<ItemwiseMeasBuildingGroup>();
        public double ItemTotal => Buildings.Sum(b => b.BuildingFinalTotal); // Updated to use the multiplied sum
    }

    public class ItemwiseMeasWorkGroup
    {
        public string WorkName { get; set; }
        public List<ItemwiseMeasSubItemGroup> Items { get; set; } = new List<ItemwiseMeasSubItemGroup>();
    }

    // --- HEAD-WISE ABSTRACT REPORT CLASSES ---
    public class AbstractReportRow
    {
        public string DsrCode { get; set; }
        public string SubItemName { get; set; }
        public string Unit { get; set; }
        public double Quantity { get; set; }
        public double Rate { get; set; }
        public double Amount => Quantity * Rate;
    }

    public class AbstractItemGroup
    {
        public string ItemName { get; set; }
        public List<AbstractReportRow> SubItems { get; set; } = new List<AbstractReportRow>();
    }

    public class AbstractWorkGroup
    {
        public string WorkName { get; set; }
        public List<AbstractItemGroup> Items { get; set; } = new List<AbstractItemGroup>();
    }

    public class AbstractFloorGroup
    {
        public string FloorName { get; set; }
        public List<AbstractWorkGroup> Works { get; set; } = new List<AbstractWorkGroup>();
    }

    public class AbstractBuildingGroup
    {
        public string BuildingName { get; set; }
        public List<AbstractFloorGroup> Floors { get; set; } = new List<AbstractFloorGroup>();
    }

    public class AbstractPdfTotals
    {
        public double BaseTotal { get; set; }
        public double GstPercent { get; set; }
        public double GstAmount { get; set; }
        public double StructPercent { get; set; }
        public double StructAmount { get; set; }
        public double ArchPercent { get; set; }
        public double ArchAmount { get; set; }
        public double RoyaltyRate { get; set; }
        public double RoyaltyQty { get; set; }
        public double RoyaltyAmount { get; set; }
        public List<MaterialTest> ActiveTests { get; set; } = new List<MaterialTest>();
        public double TestsTotal { get; set; }
        public double GrandTotal { get; set; }


    }

    
    // Pure primitive class - serializers love this!
    public class ReportFilterState
    {
        // Key = Parameter (e.g. "Building Name"), Value = List of selected items
        public Dictionary<string, List<string>> FlatFilters { get; set; } = new Dictionary<string, List<string>>();
        public List<string> CheckedTreePaths { get; set; } = new List<string>();
    }

    public static class TextFormatters
    {
        public static string GetSmartItemName(string originalName)
        {
            if (string.IsNullOrWhiteSpace(originalName)) return "Item";

            // The exact boilerplate words to ignore ONLY at the beginning of the string
            var startJargon = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "providing", "provide", "supplying", "supply", "fixing", "fix",
                "laying", "lay", "lying", "applying", "apply", "appying",
                "constructing", "construct", "filling", "fill", "fitting", "fit",
                "making", "make", "placing", "place", "fabricating", "fabricate",
                "and", "&", "of", "with", "for", "to", "in", "the", "a", ","
            };

            var words = originalName.Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries).ToList();

            // Strip the jargon out, but ONLY if it is at the very beginning of the sentence
            while (words.Count > 0 && startJargon.Contains(words[0]))
            {
                words.RemoveAt(0);
            }

            // Fallback: if the string was literally just jargon, return the original
            if (words.Count == 0) return originalName;

            // Combine ALL remaining words. No limits!
            string result = string.Join(" ", words);

            // Capitalize the first letter for a clean UI
            if (result.Length > 0)
            {
                result = char.ToUpper(result[0]) + result.Substring(1);
            }

            return result;
        }
    }

    public static class EstimationDataExtensions
    {

        public static void PromptFloorMappingIfNeeded(this ProjectDetails project)
        {
            var standardFloors = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "All", "-3 Basement", "-2 Foundation", "-1 Plinth",
                "0 Ground Floor", "1 First Floor", "2 Second Floor",
                "3 Third Floor", "4 Fourth Floor", "5 Fifth Floor",
                "6 Sixth Floor", "7 Seventh Floor", "8 Eighth Floor",
                "9 Ninth Floor", "10 Terrace Floor",
                "Unassigned Floor" // Ignored so we don't annoy the user about empty sheets
            };

            var unknownFloors = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // 1. Scan the entire project for unmapped/legacy floors
            foreach (var wi in project.WorkItems)
            {
                foreach (var sheet in wi.Sheets)
                {
                    if (string.IsNullOrWhiteSpace(sheet.FloorName)) continue;

                    var floors = sheet.FloorName.Split(new[] { ',' }, System.StringSplitOptions.RemoveEmptyEntries);
                    foreach (var f in floors)
                    {
                        string clean = f.Trim();
                        if (clean.Length > 0 && !standardFloors.Contains(clean)) unknownFloors.Add(clean);
                    }
                }
            }

            // 2. If legacy floors exist, intercept the load process and pop up the map
            if (unknownFloors.Count > 0)
            {
                var window = new FloorMappingWindow(unknownFloors.ToList());
                window.ShowDialog();

                if (window.IsConfirmed)
                {
                    var map = window.Mappings.ToDictionary(m => m.ImportedFloor, m => m.MappedFloor, StringComparer.OrdinalIgnoreCase);

                    // 3. Inject the clean mapped names back into the project
                    foreach (var wi in project.WorkItems)
                    {
                        foreach (var sheet in wi.Sheets)
                        {
                            if (string.IsNullOrWhiteSpace(sheet.FloorName)) continue;

                            var oldFloors = sheet.FloorName.Split(new[] { ',' }, System.StringSplitOptions.RemoveEmptyEntries).Select(f => f.Trim()).ToList();
                            var newFloors = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                            bool changed = false;

                            foreach (var oldF in oldFloors)
                            {
                                if (map.TryGetValue(oldF, out string newF))
                                {
                                    newFloors.Add(newF);
                                    changed = true;
                                }
                                else newFloors.Add(oldF); // Keep valid ones untouched
                            }

                            // Rejoin with commas for multi-floor compatibility
                            if (changed) sheet.FloorName = string.Join(", ", newFloors);
                        }
                    }
                }
            }
        }


        public static void SyncMissingMaterialTests(this ProjectDetails project)
        {
            if (project == null) return;
            if (project.MaterialTests == null) project.MaterialTests = new System.Collections.ObjectModel.ObservableCollection<MaterialTest>();

            // Define the tests that MUST exist in every project
            var requiredTests = new List<MaterialTest>
            {
                new MaterialTest { TestCode = "17177", TestName = "Skilled labour", Description = "78.05 Skilled labour", Rate = 885.62, Quantity = 0 },
                new MaterialTest { TestCode = "17178", TestName = "Unskilled labour", Description = "78.06 Unskilled labour", Rate = 812.67, Quantity = 0 },
                new MaterialTest { TestCode = "17179", TestName = "R.C.C.Design Engineer", Description = "78.07 Consultancy charges for R.C.C.Design Engineer", Rate = 0.01, Quantity = 0 },
                new MaterialTest { TestCode = "17180", TestName = "Consultancy charges for Third Party", Description = "78.08 Consultancy charges for Third Party (1.48 % of Estimated Cost)", Rate = 0.02, Quantity = 0 }
            };

            foreach (var req in requiredTests)
            {
                // If the old save file doesn't have this TestCode, inject it at the bottom!
                if (!project.MaterialTests.Any(t => t.TestCode == req.TestCode))
                {
                    project.MaterialTests.Add(req);
                }
            }
        }


        // Call this method right before passing your data to QuestPDF or saving to Access!
        public static List<MeasurementSheet> FlattenMultiFloorSheets(this IEnumerable<MeasurementSheet> originalSheets)
        {
            var flattenedList = new List<MeasurementSheet>();

            foreach (var sheet in originalSheets)
            {
                // If it's a normal single-floor sheet, just pass it through
                if (string.IsNullOrWhiteSpace(sheet.FloorName) || !sheet.FloorName.Contains(","))
                {
                    flattenedList.Add(sheet);
                    continue;
                }

                // If it has multiple floors, split it and clone the data!
                var individualFloors = sheet.FloorName.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);

                foreach (var floor in individualFloors)
                {
                    var clonedSheet = new MeasurementSheet
                    {
                        BuildingName = sheet.BuildingName,
                        FloorName = floor.Trim(), // Assign the isolated single floor
                        MarkerName = sheet.MarkerName,
                        RoomName = sheet.RoomName,
                        IsActiveNode = sheet.IsActiveNode
                    };

                    // Deep clone the measurement rows so the math stays intact
                    foreach (var row in sheet.Rows)
                    {
                        clonedSheet.Rows.Add(new MeasurementRow
                        {
                            Remark = row.Remark,
                            Factor = row.Factor,
                            Nos = row.Nos,
                            L = row.L,
                            B = row.B,
                            H = row.H,
                            Quantity_A = row.Quantity_A
                        });
                    }

                    flattenedList.Add(clonedSheet);
                }
            }
            return flattenedList;
        }
    }
}