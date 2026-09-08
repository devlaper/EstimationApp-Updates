using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;

namespace EstimationApp
{
    // Wrapper class specifically for bulk UI mapping
    public class ExplorerCard : INotifyPropertyChanged
    {
        public WorkItemEntry WorkParent { get; set; }
        public MeasurementSheet Sheet { get; set; }


        public string ShortWorkName => WorkParent.DisplayWorkName;
        public string LocationDisplay => $"{Sheet.BuildingName ?? "Unassigned"} - {Sheet.FloorName ?? "Unassigned"}";
        public string MarkerColor => Sheet.MarkerColor;

        public double Rate { get; set; }
        public string Unit { get; set; }
        public double Cost => Sheet.SheetTotal * Rate;
        public string QtyDisplay => $"{Sheet.SheetTotal:N2} {Unit}";
        // NEW: Stores the sheet the user wants to jump to

        // --- VISUAL UPGRADE PROPERTIES ---

        public string WorkNumberDisplay
        {
            get
            {
                if (string.IsNullOrEmpty(ShortWorkName)) return "";
                var match = System.Text.RegularExpressions.Regex.Match(ShortWorkName.Trim(), @"^(\d+)\s*(.*)");
                return match.Success ? match.Groups[1].Value + " " : "";
            }
        }

        public string WorkTitle
        {
            get
            {
                if (string.IsNullOrEmpty(ShortWorkName)) return "";
                var match = System.Text.RegularExpressions.Regex.Match(ShortWorkName.Trim(), @"^(\d+)\s*(.*)");
                return match.Success ? match.Groups[2].Value : ShortWorkName.Trim();
            }
        }

        public string BackgroundHex
        {
            get
            {
                if (string.IsNullOrEmpty(ShortWorkName)) return "#2D2D36";

                int hash = 0;
                foreach (char c in ShortWorkName) { hash = (hash * 31) + c; }
                hash = Math.Abs(hash);

                // 40 unique, subtle, dark-theme compliant background tints
                string[] palette = {
                    // Deep Blues & Teals
                    "#1A2433", "#162B33", "#12222B", "#182836", "#1C2A3D", "#15202B", "#112429", "#192B38",
                    // Subtle Purples & Magentas
                    "#251B33", "#2B1A2C", "#20162B", "#2A1836", "#2D1B30", "#22132B", "#2E1C33", "#281729",
                    // Warm Reds & Maroons
                    "#331C1A", "#2E1715", "#36201C", "#2B1414", "#331818", "#291516", "#361D1A", "#301515",
                    // Earthy Oranges & Browns
                    "#33231A", "#2E2015", "#36271A", "#2B1C12", "#332516", "#291D13", "#38291A", "#302214",
                    // Muted Greens & Olives
                    "#1A2B1D", "#162618", "#1E3321", "#152417", "#1B2E1F", "#132115", "#1D3320", "#18291B"
                };

                return palette[hash % palette.Length];
            }
        }





        public void RefreshCard()
        {
            OnPropertyChanged(nameof(LocationDisplay));
            OnPropertyChanged(nameof(MarkerColor));
            OnPropertyChanged(nameof(QtyDisplay));
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged(string prop) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(prop));
    }

    public class CrossInstanceCardDTO
    {
        public int SubItemID { get; set; }
        public string WorkName { get; set; }
        public string ItemName { get; set; }
        public string SubItemName { get; set; }
        public string SheetJson { get; set; }
    }

    public partial class ExtendedExplorerWindow : Window
    {
        private List<ExplorerCard> _allCards = new List<ExplorerCard>();
        private ICollectionView _cardView;

        private Stack<(Action Undo, Action Redo)> _undoStack = new Stack<(Action, Action)>();
        private Stack<(Action Undo, Action Redo)> _redoStack = new Stack<(Action, Action)>();
        private static List<MeasurementRow> _copiedRows;
        private bool _isUpdatingUI = false; // Prevents recursive loops during UI mapping
        private bool _isSortByWork = true;

        public MeasurementSheet SelectedSheetToNavigate { get; private set; }

        public ExtendedExplorerWindow()
        {
            InitializeComponent();
            PopulateCombos();
            LoadData();
        }

        private void ExecuteAction(Action undo, Action redo)
        {
            _undoStack.Push((undo, redo));
            _redoStack.Clear();
            redo.Invoke();
            _cardView.Refresh();
        }

        private void PopulateCombos()
        {
            ComboBulkBuilding.ItemsSource = AppState.CurrentProject.Buildings.Select(b => b.BuildingName).ToList();
            ComboBulkMarker.ItemsSource = AppState.CurrentProject.Markers.Select(m => m.MarkerName).ToList();

            ComboBulkFloor.ItemsSource = new List<string> {
               "All", "-3 Basement", "-2 Foundation", "-1 Plinth", "0 Ground Floor", "1 First Floor",
               "2 Second Floor", "3 Third Floor", "4 Fourth Floor", "5 Fifth Floor", "6 Sixth Floor",
               "7 Seventh Floor", "8 Eighth Floor", "9 Ninth Floor", "10 Terrace Floor"
            };
            ComboBulkFloor.SelectedItemsOverride = new System.Collections.ObjectModel.ObservableCollection<string>();
        }

        private void LoadData()
        {
            _allCards.Clear();
            var ratesMap = AppState.CurrentProject.MasterSubItems.ToDictionary(s => s.ID, s => s);

            foreach (var wi in AppState.CurrentProject.WorkItems)
            {
                var subItem = ratesMap.TryGetValue(wi.SubItemID, out var s) ? s : null;
                string unit = AppState.CurrentProject.MasterUnits.FirstOrDefault(u => u.ID.ToString() == subItem?.Unit)?.Unitt ?? subItem?.Unit ?? "";
                foreach (var sheet in wi.Sheets) _allCards.Add(new ExplorerCard { WorkParent = wi, Sheet = sheet, Rate = subItem?.Rate ?? 0, Unit = unit });
            }

            _cardView = CollectionViewSource.GetDefaultView(_allCards);
            _cardView.Filter = FilterCards;
            ListExplorer.ItemsSource = _cardView;

            // Set default view
            _cardView.GroupDescriptions.Add(new PropertyGroupDescription("ShortWorkName"));
        }

        private bool FilterCards(object obj)
        {
            if (obj is ExplorerCard card)
            {
                string search = TxtSearch.Text.ToLower();
                if (string.IsNullOrWhiteSpace(search)) return true;

                return (card.ShortWorkName?.ToLower().Contains(search) == true) ||
                       (card.Sheet.BuildingName?.ToLower().Contains(search) == true) ||
                       (card.Sheet.FloorName?.ToLower().Contains(search) == true) ||
                       (card.Sheet.MarkerName?.ToLower().Contains(search) == true);
            }
            return false;
        }

        private void TxtSearch_TextChanged(object sender, TextChangedEventArgs e) => _cardView.Refresh();

        private void BtnToggleSort_Click(object sender, RoutedEventArgs e)
        {
            _isSortByWork = !_isSortByWork;
            BtnToggleSort.Content = _isSortByWork ? "⮂ Group by Building" : "⮂ Group by Work Item";
            _cardView.GroupDescriptions.Clear();
            _cardView.GroupDescriptions.Add(new PropertyGroupDescription(_isSortByWork ? "ShortWorkName" : "Sheet.BuildingName"));
        }

        // --- KEYBOARD & SELECTION ---
        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Delete) { BtnBulkDelete_Click(null, null); e.Handled = true; }
            else if (e.Key == Key.A && Keyboard.Modifiers == ModifierKeys.Control) { ListExplorer.SelectAll(); e.Handled = true; }
            else if (e.Key == Key.I && Keyboard.Modifiers == ModifierKeys.Control)
            {
                var allVisible = _cardView.Cast<ExplorerCard>().ToList();
                var selected = ListExplorer.SelectedItems.Cast<ExplorerCard>().ToList();
                ListExplorer.SelectedItems.Clear();
                foreach (var item in allVisible.Except(selected)) ListExplorer.SelectedItems.Add(item);
                e.Handled = true;
            }
            else if (e.Key == Key.C && Keyboard.Modifiers == ModifierKeys.Control) { MenuCopyItems_Click(null, null); e.Handled = true; }
            else if (e.Key == Key.V && Keyboard.Modifiers == ModifierKeys.Control) { MenuPasteItems_Click(null, null); e.Handled = true; }
            else if (e.Key == Key.Z && Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift))
            {
                if (_redoStack.Count > 0) { var a = _redoStack.Pop(); a.Redo(); _undoStack.Push(a); _cardView.Refresh(); }
            }
            else if (e.Key == Key.Z && Keyboard.Modifiers == ModifierKeys.Control)
            {
                if (_undoStack.Count > 0) { var a = _undoStack.Pop(); a.Undo(); _redoStack.Push(a); _cardView.Refresh(); }
            }
        }

        private void ListViewItem_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (sender is ListViewItem item && item.DataContext is ExplorerCard card)
            {
                SelectedSheetToNavigate = card.Sheet;
                this.DialogResult = true;
            }
        }

        // --- REAL-TIME MAPPING & ATTRIBUTES ---
        private void CostMeter_Updated(object sender, RoutedEventArgs e) => UpdateCostMeter();

        private void UpdateCostMeter()
        {
            double total = ListExplorer.SelectedItems.Cast<ExplorerCard>().Sum(c => c.Cost);
            if (ChkIncludeGST.IsChecked == true) total *= 1.18;
            TxtLiveCost.Text = $"₹ {total:N2}";
        }

        private void ListExplorer_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdateCostMeter();

            var selected = ListExplorer.SelectedItems.Cast<ExplorerCard>().ToList();
            if (selected.Count == 0) return;

            _isUpdatingUI = true; // Lock the execution engine

            // 1. Identify Building Matches
            var bldgs = selected.Select(c => c.Sheet.BuildingName).Distinct().ToList();
            ComboBulkBuilding.Text = bldgs.Count == 1 ? bldgs.First() : "Varies";

            // 2. Identify Marker Matches
            var markers = selected.Select(c => c.Sheet.MarkerName).Distinct().ToList();
            ComboBulkMarker.Text = markers.Count == 1 ? markers.First() : "Varies";

            // 3. Identify Floor Matches
            var floors = selected.Select(c => c.Sheet.FloorName).Distinct().ToList();
            ComboBulkFloor.SelectedItemsOverride.Clear();
            if (floors.Count == 1 && !string.IsNullOrEmpty(floors.First()))
            {
                foreach (var f in floors.First().Split(new[] { ", " }, StringSplitOptions.RemoveEmptyEntries))
                    ComboBulkFloor.SelectedItemsOverride.Add(f);
            }
            else if (floors.Count > 1) ComboBulkFloor.Text = "Varies";

            _isUpdatingUI = false; // Unlock
        }

        private void ApplyBulkEdits(string attributeType, string newValue)
        {
            if (_isUpdatingUI) return;
            var selected = ListExplorer.SelectedItems.Cast<ExplorerCard>().ToList();
            if (selected.Count == 0 || newValue == "Varies") return;

            var snapshots = selected.Select(c => new { Card = c, OldBldg = c.Sheet.BuildingName, OldFloor = c.Sheet.FloorName, OldMarker = c.Sheet.MarkerName }).ToList();

            Action redo = () =>
            {
                foreach (var s in snapshots)
                {
                    if (attributeType == "Building") s.Card.Sheet.BuildingName = newValue;
                    else if (attributeType == "Marker") s.Card.Sheet.MarkerName = newValue;
                    else if (attributeType == "Floor") s.Card.Sheet.FloorName = newValue;
                    s.Card.RefreshCard();
                }
                _cardView.Refresh();
            };
            Action undo = () =>
            {
                foreach (var s in snapshots)
                {
                    if (attributeType == "Building") s.Card.Sheet.BuildingName = s.OldBldg;
                    else if (attributeType == "Marker") s.Card.Sheet.MarkerName = s.OldMarker;
                    else if (attributeType == "Floor") s.Card.Sheet.FloorName = s.OldFloor;
                    s.Card.RefreshCard();
                }
                _cardView.Refresh();
            };
            ExecuteAction(undo, redo);
        }

        private void ComboBulkBuilding_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ComboBulkBuilding.SelectedItem != null) ApplyBulkEdits("Building", ComboBulkBuilding.SelectedItem.ToString());
        }
        private void ComboBulkMarker_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ComboBulkMarker.SelectedItem != null) ApplyBulkEdits("Marker", ComboBulkMarker.SelectedItem.ToString());
        }
        private void ComboBulkFloor_ItemSelectionChanged(object sender, Xceed.Wpf.Toolkit.Primitives.ItemSelectionChangedEventArgs e)
        {
            if (ComboBulkFloor.SelectedItemsOverride != null) ApplyBulkEdits("Floor", string.Join(", ", ComboBulkFloor.SelectedItemsOverride.Cast<string>()));
        }

        // --- CROSS-INSTANCE COPY / PASTE / DUPLICATE / DELETE ---

        private void MenuCopyItems_Click(object sender, RoutedEventArgs e)
        {
            var selected = ListExplorer.SelectedItems.Cast<ExplorerCard>().ToList();
            if (selected.Count == 0) return;

            var dtos = selected.Select(c => new CrossInstanceCardDTO
            {
                SubItemID = c.WorkParent.SubItemID,
                WorkName = c.WorkParent.DisplayWorkName,
                ItemName = c.WorkParent.DisplayItemName,
                SubItemName = c.WorkParent.UIItemName,
                SheetJson = JsonSerializer.Serialize(c.Sheet)
            }).ToList();

            Clipboard.SetData("EstimationApp_CrossInstanceCards", JsonSerializer.Serialize(dtos));
        }

        private void MenuPasteItems_Click(object sender, RoutedEventArgs e)
        {
            string clipboardData = Clipboard.GetData("EstimationApp_CrossInstanceCards") as string;
            if (string.IsNullOrEmpty(clipboardData)) return;

            var dtos = JsonSerializer.Deserialize<List<CrossInstanceCardDTO>>(clipboardData);
            if (dtos == null || dtos.Count == 0) return;

            var addedCards = new List<ExplorerCard>();

            Action redo = () =>
            {
                foreach (var dto in dtos)
                {
                    // Find matching parent or dynamically create it!
                    var targetParent = AppState.CurrentProject.WorkItems.FirstOrDefault(w => w.SubItemID == dto.SubItemID);
                    if (targetParent == null)
                    {
                        targetParent = new WorkItemEntry { SubItemID = dto.SubItemID, DisplayWorkName = dto.WorkName, DisplayItemName = dto.ItemName };
                        AppState.CurrentProject.WorkItems.Add(targetParent);
                    }

                    var newSheet = JsonSerializer.Deserialize<MeasurementSheet>(dto.SheetJson);
                    targetParent.Sheets.Add(newSheet);

                    var rateMap = AppState.CurrentProject.MasterSubItems.FirstOrDefault(s => s.ID == dto.SubItemID);
                    string unit = AppState.CurrentProject.MasterUnits.FirstOrDefault(u => u.ID.ToString() == rateMap?.Unit)?.Unitt ?? rateMap?.Unit ?? "";

                    var newCard = new ExplorerCard { WorkParent = targetParent, Sheet = newSheet, Rate = rateMap?.Rate ?? 0, Unit = unit };
                    _allCards.Add(newCard);
                    addedCards.Add(newCard);
                }
                _cardView.Refresh();
            };
            Action undo = () =>
            {
                foreach (var ac in addedCards)
                {
                    ac.WorkParent.Sheets.Remove(ac.Sheet);
                    _allCards.Remove(ac);
                    if (ac.WorkParent.Sheets.Count == 0) AppState.CurrentProject.WorkItems.Remove(ac.WorkParent);
                }
                _cardView.Refresh();
            };
            ExecuteAction(undo, redo);
        }

        private void BtnDuplicate_Click(object sender, RoutedEventArgs e) { MenuCopyItems_Click(null, null); MenuPasteItems_Click(null, null); }

        private void BtnBulkDelete_Click(object sender, RoutedEventArgs e)
        {
            var selected = ListExplorer.SelectedItems.Cast<ExplorerCard>().ToList();
            if (selected.Count == 0) return;

            var snapshots = selected.Select(c => new { Card = c, Index = c.WorkParent.Sheets.IndexOf(c.Sheet) }).ToList();

            Action redo = () =>
            {
                foreach (var s in snapshots)
                {
                    s.Card.WorkParent.Sheets.Remove(s.Card.Sheet);
                    _allCards.Remove(s.Card);
                    if (s.Card.WorkParent.Sheets.Count == 0) AppState.CurrentProject.WorkItems.Remove(s.Card.WorkParent);
                }
            };
            Action undo = () =>
            {
                foreach (var s in snapshots)
                {
                    if (!AppState.CurrentProject.WorkItems.Contains(s.Card.WorkParent)) AppState.CurrentProject.WorkItems.Add(s.Card.WorkParent);
                    s.Card.WorkParent.Sheets.Insert(Math.Min(s.Index, s.Card.WorkParent.Sheets.Count), s.Card.Sheet);
                    _allCards.Add(s.Card);
                }
            };
            ExecuteAction(undo, redo);
        }

        private void MenuCopyRows_Click(object sender, RoutedEventArgs e)
        {
            if (ListExplorer.SelectedItem is ExplorerCard c) _copiedRows = JsonSerializer.Deserialize<List<MeasurementRow>>(JsonSerializer.Serialize(c.Sheet.Rows));
        }

        private void MenuPasteRows_Click(object sender, RoutedEventArgs e)
        {
            var selected = ListExplorer.SelectedItems.Cast<ExplorerCard>().ToList();
            if (selected.Count == 0 || _copiedRows == null) return;
            var snapshots = selected.Select(c => new { Card = c, OldRows = c.Sheet.Rows.ToList() }).ToList();
            string jsonRows = JsonSerializer.Serialize(_copiedRows);

            Action redo = () =>
            {
                foreach (var s in snapshots)
                {
                    s.Card.Sheet.Rows.Clear();
                    foreach (var r in JsonSerializer.Deserialize<List<MeasurementRow>>(jsonRows)) s.Card.Sheet.Rows.Add(r);
                    s.Card.Sheet.UpdateSheetState(); s.Card.RefreshCard();
                }
                UpdateCostMeter();
            };
            Action undo = () =>
            {
                foreach (var s in snapshots)
                {
                    s.Card.Sheet.Rows.Clear();
                    foreach (var r in s.OldRows) s.Card.Sheet.Rows.Add(r);
                    s.Card.Sheet.UpdateSheetState(); s.Card.RefreshCard();
                }
                UpdateCostMeter();
            };
            ExecuteAction(undo, redo);
        }
    }
}