using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Data.OleDb;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace EstimationApp
{


    public partial class WorkEnvironmentWindow : Window
    {
        // --- 1. GLOBAL VARIABLES ---
        private WorkItemEntry _currentWorkItem;
        private MeasurementSheet _currentSheet;
        private int _currentItemIndex = -1;
        private int _currentSheetIndex = -1;
        
        private List<SubItemDisplay> _masterSearchList = new List<SubItemDisplay>();
        private List<MeasurementRow> _copiedRows = new List<MeasurementRow>();

        // UPGRADED: Now holds a pair of Actions (Undo, Redo) for two-way time travel
        private Stack<(Action Undo, Action Redo)> _undoStack = new Stack<(Action, Action)>();
        private Stack<(Action Undo, Action Redo)> _redoStack = new Stack<(Action, Action)>();

        // HELPER: Executes an action and logs it in the time-travel stacks
        private void ExecuteAction(Action undo, Action redo)
        {
            _undoStack.Push((undo, redo));
            _redoStack.Clear();
            redo.Invoke();
        }

        // HELPER: Intelligently finds all unique rows based on selected cells OR selected row headers
        private List<MeasurementRow> GetSelectedRows()
        {
            var rows = new System.Collections.Generic.HashSet<MeasurementRow>();
            if (_currentSheet == null) return rows.ToList();

            if (GridMeasurements.SelectedItems.Count > 0)
            {
                foreach (var item in GridMeasurements.SelectedItems) if (item is MeasurementRow r) rows.Add(r);
            }
            if (GridMeasurements.SelectedCells.Count > 0)
            {
                foreach (var cell in GridMeasurements.SelectedCells) if (cell.Item is MeasurementRow r) rows.Add(r);
            }

            // Return ordered by their actual vertical position in the sheet
            return rows.OrderBy(r => _currentSheet.Rows.IndexOf(r)).ToList();
        }
        private System.Windows.Threading.DispatcherTimer _autoSaveTimer;

        private bool _isGroupedByBuilding = false;
        private bool _isDarkMode = false;
        private bool _isRefreshingTree = false;
        private bool _isKeyboardNavigating = false;

        // --- 2. INITIALIZATION ---
        public WorkEnvironmentWindow()
        {
            InitializeComponent();
            PopulateDropdowns();
            LoadOrInitializeFirstItem();

            RefreshSidebar();
            UpdateLiveCostMeter(false);
            UpdateWindowTitle();
            
            this.Closing += Window_Closing;

            _autoSaveTimer = new System.Windows.Threading.DispatcherTimer();
            _autoSaveTimer.Interval = TimeSpan.FromMinutes(5);
            _autoSaveTimer.Tick += AutoSaveTimer_Tick;
            _autoSaveTimer.Start();
        }

        private void EnsureUniqueBuildingNames()
        {
            if (AppState.CurrentProject?.Buildings == null) return;

            var seenNames = new HashSet<string>();
            foreach (var b in AppState.CurrentProject.Buildings)
            {
                // 1. Give unnamed buildings a default name
                if (string.IsNullOrWhiteSpace(b.BuildingName))
                    b.BuildingName = "Building";

                string baseName = b.BuildingName;
                string uniqueName = baseName;
                int counter = 1;

                // 2. If the name is taken, append a number until it becomes unique
                while (seenNames.Contains(uniqueName))
                {
                    uniqueName = $"{baseName} {counter}";
                    counter++;
                }

                // 3. If we had to change the name to make it unique, apply it
                if (b.BuildingName != uniqueName)
                {
                    string oldName = b.BuildingName;
                    b.BuildingName = uniqueName;

                    // 4. Safely update any measurement sheets that were linked to the old duplicate name
                    if (AppState.CurrentProject.WorkItems != null)
                    {
                        foreach (var wi in AppState.CurrentProject.WorkItems)
                        {
                            if (wi.Sheets == null) continue;
                            foreach (var sheet in wi.Sheets.Where(s => s.BuildingName == oldName))
                            {
                                sheet.BuildingName = uniqueName;
                            }
                        }
                    }
                }

                // Add to our tracker
                seenNames.Add(uniqueName);
            }
        }

        private void LoadOrInitializeFirstItem()
        {
            if (AppState.CurrentProject.WorkItems.Count == 0) BtnNewItem_Click(null, null);
            else 
            { 
                _currentItemIndex = 0; 
                _currentSheetIndex = 0; 
                BindCurrentState(); 
            }
        }

        // --- 3. MASTER SELECTION & NAVIGATION ENGINE ---
        private void SelectSheetInTree(MeasurementSheet targetSheet)
        {
            if (targetSheet == null || AppState.CurrentProject == null) return;

            // 1. Update the backend data references
            _currentSheet = targetSheet;
            _currentWorkItem = AppState.CurrentProject.WorkItems.FirstOrDefault(w => w.Sheets.Contains(targetSheet));
            _currentItemIndex = AppState.CurrentProject.WorkItems.IndexOf(_currentWorkItem);
            _currentSheetIndex = _currentWorkItem.Sheets.IndexOf(targetSheet);

            // 2. Loop through everything to update the visual states
            foreach (var workItem in AppState.CurrentProject.WorkItems)
            {
                bool isParent = workItem.Sheets.Contains(targetSheet);
                workItem.IsExpanded = isParent; // Expand ONLY the parent of the active sheet
                workItem.IsActiveNode = false; 

                foreach (var sheet in workItem.Sheets)
                {
                    sheet.IsActiveNode = (sheet == targetSheet); // Highlight ONLY the active sheet
                }
            }

            // 3. Trigger UI refreshes
            BindCurrentState();
            
            // If we are in building mode, the UI needs a hard refresh to re-render the custom TreeWorkItemGroups
            if (_isGroupedByBuilding) RefreshSidebar(); 
        }

        private void NavigateSheets(bool moveNext)
        {
            if (TreeContext.ItemsSource == null) return;

            var flatSheets = new List<MeasurementSheet>();

            foreach (var item in TreeContext.ItemsSource)
            {
                if (item is WorkItemEntry wi)
                    flatSheets.AddRange(wi.Sheets);
                else if (item is TreeBuildingGroup bg)
                {
                    // Navigate through the new Floor level
                    foreach (var fg in bg.Floors)
                    {
                        flatSheets.AddRange(fg.Items.Cast<TreeWorkItemGroup>().Select(t => t.OriginalSheet));
                    }
                }
            }

            if (flatSheets.Count == 0) return;

            int currentIndex = flatSheets.IndexOf(_currentSheet);

            if (moveNext && currentIndex < flatSheets.Count - 1) currentIndex++;
            else if (!moveNext && currentIndex > 0) currentIndex--;
            else return; // Stop at bounds

            SelectSheetInTree(flatSheets[currentIndex]);
        }

        private void Node_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement element)
            {
                if (element.DataContext is MeasurementSheet sheet)
                {
                    SelectSheetInTree(sheet);
                    e.Handled = true;
                }
                else if (element.DataContext is TreeWorkItemGroup tg)
                {
                    SelectSheetInTree(tg.OriginalSheet);
                    e.Handled = true;
                }
                else if (element.DataContext is WorkItemEntry workItem)
                {
                    workItem.IsExpanded = !workItem.IsExpanded; // Just toggle open/close
                    e.Handled = true;
                }
            }
        }



        // --- 4. DATA BINDING & UI REFRESH ---
        private void BindCurrentState()
        {
            if (_currentWorkItem == null || _currentSheet == null) return;

            ComboBuilding.DataContext = null;
            ComboMarker.DataContext = null;
            ComboRoom.DataContext = null;

            ComboBuilding.DataContext = _currentSheet;
            ComboMarker.DataContext = _currentSheet;    
            ComboRoom.DataContext = _currentSheet;

            // Handle multi-floors smoothly without triggering recursive events
            ComboMultiFloors.ItemSelectionChanged -= ComboMultiFloors_ItemSelectionChanged;
            ComboMultiFloors.SelectedItems.Clear();

            if (!string.IsNullOrWhiteSpace(_currentSheet.FloorName))
            {
                var floors = _currentSheet.FloorName.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var f in floors)
                {
                    string cleanFloor = f.Trim();
                    if (ComboMultiFloors.Items.Contains(cleanFloor)) ComboMultiFloors.SelectedItems.Add(cleanFloor);
                }
            }
            ComboMultiFloors.ItemSelectionChanged += ComboMultiFloors_ItemSelectionChanged;

            var matchedItem = _masterSearchList.FirstOrDefault(x => x.SubItemID == _currentWorkItem.SubItemID);
            if (matchedItem != null)
            {
                LblWorkName.Text = matchedItem.WorkName;
                LblItemName.Text = matchedItem.NameOfItem;
                LblSubItem.Text = matchedItem.SubItem;
                LblDescription.Text = TruncateTo30Words(matchedItem.Description);
                TxtSearchDisplay.Text = matchedItem.DisplayName;
                LblRate.Text = matchedItem.Rate.ToString();
                LblUnit.Text = matchedItem.Unit;
                LblTotalUnit.Text = matchedItem.Unit;
                LblDsrCode.Text = matchedItem.Code;
            }
            else
            {
                TxtSearchDisplay.Text = "Click here to search and select an item...";
            }

            GridMeasurements.ItemsSource = _currentSheet.Rows;
            UpdateTotalDisplay();
        }

        private void RefreshSidebar()
        {
            _isRefreshingTree = true;

            try
            {
                string searchText = TxtSearchExplorer.Text?.ToLower() ?? "";
                var sortedWorkItems = AppState.CurrentProject.WorkItems.OrderBy(w => GetSortName(w.DisplayName)).ToList();

                // Standard globally visible highlighting logic
                foreach (var workItem in AppState.CurrentProject.WorkItems)
                {
                    workItem.IsActiveNode = (!_isGroupedByBuilding && workItem == _currentWorkItem);
                    foreach (var sheet in workItem.Sheets)
                    {
                        sheet.IsActiveNode = (!_isGroupedByBuilding && workItem == _currentWorkItem && sheet == _currentSheet);
                    }
                }

                if (!_isGroupedByBuilding)
                {
                    // ADVANCED SEARCH: Now scans WorkName, BuildingName, AND FloorName
                    TreeContext.ItemsSource = sortedWorkItems
                        .Where(w => string.IsNullOrWhiteSpace(searchText) ||
                                    w.DisplayName.ToLower().Contains(searchText) ||
                                    w.Sheets.Any(s => (s.BuildingName != null && s.BuildingName.ToLower().Contains(searchText)) ||
                                                      (s.FloorName != null && s.FloorName.ToLower().Contains(searchText))))
                        .ToList();
                }
                else
                {
                    var buildingNodes = new ObservableCollection<TreeBuildingGroup>();
                    var bldgDict = AppState.CurrentProject.Buildings.ToDictionary(b => b.BuildingName, b => b.NumberOfBuildings);

                    foreach (var workItem in sortedWorkItems)
                    {
                        foreach (var sheet in workItem.Sheets)
                        {
                            // ADVANCED SEARCH: Scans WorkName, BuildingName, AND FloorName
                            bool matches = string.IsNullOrWhiteSpace(searchText) ||
                                           workItem.DisplayName.ToLower().Contains(searchText) ||
                                           (sheet.BuildingName != null && sheet.BuildingName.ToLower().Contains(searchText)) ||
                                           (sheet.FloorName != null && sheet.FloorName.ToLower().Contains(searchText));

                            if (!matches) continue; // Skip items that don't match the search

                            string bldgName = string.IsNullOrEmpty(sheet.BuildingName) ? "Unassigned Building" : sheet.BuildingName;
                            int multiplier = bldgDict.ContainsKey(bldgName) ? bldgDict[bldgName] : 1;
                            string displayBldgName = $"{bldgName} x{multiplier}";

                            string floorName = string.IsNullOrEmpty(sheet.FloorName) ? "Unassigned Floor" : sheet.FloorName;

                            // 1. Find or create Building
                            var bNode = buildingNodes.FirstOrDefault(b => b.BuildingName == displayBldgName);
                            if (bNode == null)
                            {
                                bNode = new TreeBuildingGroup { BuildingName = displayBldgName };
                                buildingNodes.Add(bNode);
                            }

                            // 2. Find or create Floor Subheader
                            var fNode = bNode.Floors.FirstOrDefault(f => f.FloorName == floorName);
                            if (fNode == null)
                            {
                                fNode = new TreeFloorGroup { FloorName = floorName };
                                bNode.Floors.Add(fNode);
                            }

                            // 3. Add the Work Item Entry
                            fNode.Items.Add(new TreeWorkItemGroup
                            {
                                DisplayWorkName = workItem.DisplayWorkName,
                                DisplayItemName = workItem.DisplayItemName,
                                OriginalWorkItem = workItem,
                                OriginalSheet = sheet,
                                IsActiveNode = (workItem == _currentWorkItem && sheet == _currentSheet)
                            });
                        }
                    }
                    TreeContext.ItemsSource = buildingNodes.OrderBy(b => b.BuildingName).ToList();
                }
            }
            finally
            {
                _isRefreshingTree = false;
            }
        }

        private void PopulateDropdowns()
        {
            ComboBuilding.ItemsSource = AppState.CurrentProject.Buildings.Select(b => b.BuildingName).ToList();
            ComboMultiFloors.ItemsSource = new List<string>
            {
               "All", "-3 Basement", "-2 Foundation", "-1 Plinth",
                "0 Ground Floor", "1 First Floor", "2 Second Floor",
                "3 Third Floor", "4 Fourth Floor", "5 Fifth Floor",
                "6 Sixth Floor", "7 Seventh Floor", "8 Eighth Floor",
                "9 Ninth Floor", "10 Terrace Floor",
            };

            // Inside PopulateDropdowns()
            ComboMarker.ItemsSource = AppState.CurrentProject.Markers.Select(m => m.MarkerName).ToList();
            ComboRoom.ItemsSource = AppState.CurrentProject.Rooms.Select(r => r.RoomName).ToList();

            var query = from w in AppState.CurrentProject.MasterWork
                        join i in AppState.CurrentProject.MasterItems on w.ID equals i.WorkNameID
                        join s in AppState.CurrentProject.MasterSubItems on i.ID equals s.ItemID
                        join u in AppState.CurrentProject.MasterUnits on s.Unit equals u.ID.ToString() into unitGroup
                        from u in unitGroup.DefaultIfEmpty()
                        orderby w.WorkName, i.NameOfItem, s.SubItem
                        select new SubItemDisplay
                        {
                            SubItemID = s.ID,
                            DisplayName = $"{w.WorkName} - {i.NameOfItem} - {s.SubItem}",
                            Rate = s.Rate,
                            Unit = u != null && !string.IsNullOrWhiteSpace(u.Unitt) ? u.Unitt : s.Unit,
                            Code = s.Code,
                            NameOfItem = i.NameOfItem,
                            SubItem = s.SubItem,
                            WorkName = w.WorkName,
                            Description = i.Description
                        };

            _masterSearchList = query.ToList();
        }

        // --- 5. KEYBOARD & MOUSE EVENTS ---
        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.S && Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift))
            {
                MenuSaveAs_Click(null, null);
                e.Handled = true;
            }
            else if (e.Key == Key.S && Keyboard.Modifiers == ModifierKeys.Control)
            {
                MenuSave_Click(null, null);
                e.Handled = true;
            }
            else if (e.Key == Key.Z && Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift))
            {
                if (_redoStack.Count > 0)
                {
                    var actionPair = _redoStack.Pop();
                    actionPair.Redo.Invoke();
                    _undoStack.Push(actionPair);
                    e.Handled = true;
                }
            }
            else if (e.Key == Key.Z && Keyboard.Modifiers == ModifierKeys.Control)
            {
                if (_undoStack.Count > 0)
                {
                    var actionPair = _undoStack.Pop();
                    actionPair.Undo.Invoke();
                    _redoStack.Push(actionPair);
                    e.Handled = true;
                }
            }
            else if (Keyboard.Modifiers == ModifierKeys.Control && (e.Key == Key.Up || e.Key == Key.Down))
            {
                _isKeyboardNavigating = true; 
                NavigateSheets(e.Key == Key.Down); 
                e.Handled = true;
            }
            else if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.C)
            {
                if (e.OriginalSource is TextBox) return; 
                CopyMeasurementRows();
                e.Handled = true;
            }
            else if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.V)
            {
                if (e.OriginalSource is TextBox) return; 
                PasteMeasurementRows();
                e.Handled = true;
            }
        }

        private void Window_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (Keyboard.Modifiers == ModifierKeys.Control)
            {
                _isKeyboardNavigating = true; 
                NavigateSheets(e.Delta < 0); // Scrolled Down = Next Sheet
                e.Handled = true;
            }
        }

        private void GridMeasurements_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            // 1. Delete Key: Clears cells. Shift+Delete deletes the entire row.
            if (e.Key == Key.Delete)
            {
                if (e.OriginalSource is TextBox) return; // Allow normal text deletion inside cells

                if (Keyboard.Modifiers == ModifierKeys.Shift) DeleteSelectedRows();
                else ClearSelectedCells();

                e.Handled = true;
            }
            // 2. Intercept Ctrl+V for specific cell regions
            else if (e.Key == Key.V && Keyboard.Modifiers == ModifierKeys.Control)
            {
                if (e.OriginalSource is TextBox) return;
                PasteCellRegion();
                e.Handled = true;
            }

            // 3. Excel-Style Cell Edit Navigation (Keep your existing logic here)
            if (e.OriginalSource is TextBox textBox)
            {
                if (e.Key == Key.Up)
                {
                    textBox.MoveFocus(new TraversalRequest(FocusNavigationDirection.Up));
                    e.Handled = true;
                }
                else if (e.Key == Key.Down)
                {
                    textBox.MoveFocus(new TraversalRequest(FocusNavigationDirection.Down));
                    e.Handled = true;
                }
                else if (e.Key == Key.Left && textBox.CaretIndex == 0)
                {
                    textBox.MoveFocus(new TraversalRequest(FocusNavigationDirection.Left));
                    e.Handled = true;
                }
                else if (e.Key == Key.Right && textBox.CaretIndex == textBox.Text.Length)
                {
                    textBox.MoveFocus(new TraversalRequest(FocusNavigationDirection.Right));
                    e.Handled = true;
                }
            }
        }

        private void TreeContext_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Delete && TreeContext.SelectedItem != null)
            {
                var selectedItem = TreeContext.SelectedItem;
                if (MessageBox.Show("Delete this entry? (Press Ctrl+Z to undo)", "Confirm Delete", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
                {
                    e.Handled = true;
                    return;
                }

                // Helper to safely refresh the UI after time-traveling
                Action updateUI = () => {
                    if (AppState.CurrentProject.WorkItems.Count == 0) BtnNewItem_Click(null, null);
                    else { _currentItemIndex = 0; _currentSheetIndex = 0; BindCurrentState(); }
                    RefreshSidebar();
                };

                if (selectedItem is MeasurementSheet sheet)
                {
                    foreach (var workItem in AppState.CurrentProject.WorkItems)
                    {
                        int index = workItem.Sheets.IndexOf(sheet);
                        if (index >= 0)
                        {
                            int wiIndex = AppState.CurrentProject.WorkItems.IndexOf(workItem);
                            bool removedParent = false;

                            Action redo = () => {
                                workItem.Sheets.Remove(sheet);
                                if (workItem.Sheets.Count == 0)
                                {
                                    AppState.CurrentProject.WorkItems.Remove(workItem);
                                    removedParent = true;
                                }
                                updateUI();
                            };

                            Action undo = () => {
                                if (removedParent) AppState.CurrentProject.WorkItems.Insert(wiIndex, workItem);
                                workItem.Sheets.Insert(index, sheet);
                                updateUI();
                            };

                            ExecuteAction(undo, redo);
                            break;
                        }
                    }
                }
                else if (selectedItem is WorkItemEntry workItem)
                {
                    int wiIndex = AppState.CurrentProject.WorkItems.IndexOf(workItem);

                    Action redo = () => {
                        AppState.CurrentProject.WorkItems.Remove(workItem);
                        updateUI();
                    };

                    Action undo = () => {
                        AppState.CurrentProject.WorkItems.Insert(wiIndex, workItem);
                        updateUI();
                    };

                    ExecuteAction(undo, redo);
                }
                else if (selectedItem is TreeWorkItemGroup treeGroup)
                {
                    var origSheet = treeGroup.OriginalSheet;
                    var origWorkItem = treeGroup.OriginalWorkItem;
                    int sheetIndex = origWorkItem.Sheets.IndexOf(origSheet);
                    int wiIndex = AppState.CurrentProject.WorkItems.IndexOf(origWorkItem);
                    bool removedParent = false;

                    Action redo = () => {
                        origWorkItem.Sheets.Remove(origSheet);
                        if (origWorkItem.Sheets.Count == 0)
                        {
                            AppState.CurrentProject.WorkItems.Remove(origWorkItem);
                            removedParent = true;
                        }
                        updateUI();
                    };

                    Action undo = () => {
                        if (removedParent) AppState.CurrentProject.WorkItems.Insert(wiIndex, origWorkItem);
                        origWorkItem.Sheets.Insert(sheetIndex, origSheet);
                        updateUI();
                    };

                    ExecuteAction(undo, redo);
                }

                e.Handled = true;
            }
        }

        private void TreeViewItem_Selected(object sender, RoutedEventArgs e)
        {
            if (_isKeyboardNavigating && sender is TreeViewItem tvi && tvi.IsSelected)
            {
                tvi.Dispatcher.BeginInvoke(new Action(() =>
                {
                    tvi.BringIntoView();
                    _isKeyboardNavigating = false; 
                }), System.Windows.Threading.DispatcherPriority.Loaded);

                e.Handled = true;
            }
        }

        private void TreeViewItem_Collapsed(object sender, RoutedEventArgs e)
        {
            if (sender is TreeViewItem tvi)
            {
                // Anti-collapse engine now protects both Buildings AND Floors
                if (tvi.DataContext is TreeBuildingGroup || tvi.DataContext is TreeFloorGroup)
                {
                    tvi.IsExpanded = true;
                    e.Handled = true;
                }
            }
        }

        private void GridMeasurements_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                _currentSheet.UpdateSheetState();
                _currentWorkItem.UpdateState(); 
                UpdateTotalDisplay();

                bool isBuildingMode = ListLiveBuildingCosts.Visibility == Visibility.Visible;
                UpdateLiveCostMeter(isBuildingMode);
            }), System.Windows.Threading.DispatcherPriority.Background);
        }

        private void BtnExpandExplorer_Click(object sender, RoutedEventArgs e)
        {
            // Owner = this ensures it stays centered over the main app
            ExtendedExplorerWindow extendedWindow = new ExtendedExplorerWindow { Owner = this };

            // ShowDialog() freezes the WorkEnvironmentWindow until the Extended Explorer is closed
            extendedWindow.ShowDialog();

            // 1. Force the sidebar to update in case they did bulk editing (Delete/Duplicate/Apply)
            RefreshSidebar();

            // 2. Check if the user double-clicked a card to navigate
            if (extendedWindow.SelectedSheetToNavigate != null)
            {
                // Instantly jumps the main UI to the double-clicked sheet!
                SelectSheetInTree(extendedWindow.SelectedSheetToNavigate);
            }
            else
            {
                // Just a normal refresh if they clicked the 'X' to close
                if (_currentWorkItem != null) BindCurrentState();
            }

            UpdateLiveCostMeter(BtnToggleView.Content.ToString().Contains("Work Item"));
        }


        // --- EXCEL CELL-REGION ENGINE ---

        private void ClearSelectedCells()
        {
            if (GridMeasurements.SelectedCells.Count == 0 || _currentSheet == null) return;

            var sheetRef = _currentSheet;
            var cellSnapshots = new List<Tuple<MeasurementRow, string, string, string>>();

            foreach (var cellInfo in GridMeasurements.SelectedCells)
            {
                var column = cellInfo.Column as DataGridBoundColumn;
                var rowItem = cellInfo.Item as MeasurementRow;
                if (column == null || column.IsReadOnly || rowItem == null) continue;

                var bindingPath = (column.Binding as System.Windows.Data.Binding)?.Path.Path;
                if (!string.IsNullOrEmpty(bindingPath))
                {
                    var prop = typeof(MeasurementRow).GetProperty(bindingPath);
                    if (prop != null && prop.CanWrite)
                    {
                        string oldVal = prop.GetValue(rowItem)?.ToString() ?? "";
                        string newVal = (bindingPath == "Factor" || bindingPath == "Nos") ? "1" : "";
                        if (oldVal != newVal) cellSnapshots.Add(new Tuple<MeasurementRow, string, string, string>(rowItem, bindingPath, oldVal, newVal));
                    }
                }
            }

            if (cellSnapshots.Count == 0) return;

            Action redo = () => {
                foreach (var snap in cellSnapshots) typeof(MeasurementRow).GetProperty(snap.Item2)?.SetValue(snap.Item1, snap.Item4);
                sheetRef.UpdateSheetState();
                if (_currentSheet == sheetRef) { UpdateTotalDisplay(); UpdateLiveCostMeter(ListLiveBuildingCosts.Visibility == Visibility.Visible); }
            };

            Action undo = () => {
                foreach (var snap in cellSnapshots) typeof(MeasurementRow).GetProperty(snap.Item2)?.SetValue(snap.Item1, snap.Item3);
                sheetRef.UpdateSheetState();
                if (_currentSheet == sheetRef) { UpdateTotalDisplay(); UpdateLiveCostMeter(ListLiveBuildingCosts.Visibility == Visibility.Visible); }
            };

            ExecuteAction(undo, redo);
        }

        private void PasteCellRegion()
        {
            if (_currentSheet == null) return;
            string clipboardData = Clipboard.GetText();
            if (string.IsNullOrEmpty(clipboardData)) return;

            if (Clipboard.ContainsData("EstimationApp_MeasurementRows"))
            {
                PasteMeasurementRows();
                return;
            }

            var rows = clipboardData.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.RemoveEmptyEntries);
            if (rows.Length == 0 || GridMeasurements.SelectedCells.Count == 0) return;

            var startCell = GridMeasurements.SelectedCells.OrderBy(c => GridMeasurements.Items.IndexOf(c.Item)).ThenBy(c => c.Column.DisplayIndex).First();
            int startRowIdx = GridMeasurements.Items.IndexOf(startCell.Item);
            int startColIdx = startCell.Column.DisplayIndex;
            var sheetRef = _currentSheet;

            var cellSnapshots = new List<Tuple<MeasurementRow, string, string, string>>();
            var newlyAddedRows = new List<MeasurementRow>();

            for (int i = 0; i < rows.Length; i++)
            {
                int currentRowIdx = startRowIdx + i;
                MeasurementRow targetRow = null;

                if (currentRowIdx >= sheetRef.Rows.Count + newlyAddedRows.Count)
                {
                    var newRow = new MeasurementRow();
                    newlyAddedRows.Add(newRow);
                    targetRow = newRow;
                }
                else if (currentRowIdx >= sheetRef.Rows.Count) targetRow = newlyAddedRows[currentRowIdx - sheetRef.Rows.Count];
                else targetRow = sheetRef.Rows[currentRowIdx];

                var cells = rows[i].Split('\t');
                for (int j = 0; j < cells.Length; j++)
                {
                    int currentColIdx = startColIdx + j;
                    if (currentColIdx >= GridMeasurements.Columns.Count) continue;

                    var column = GridMeasurements.Columns[currentColIdx] as DataGridBoundColumn;
                    if (column == null || column.IsReadOnly) continue;

                    var bindingPath = (column.Binding as System.Windows.Data.Binding)?.Path.Path;
                    if (!string.IsNullOrEmpty(bindingPath))
                    {
                        var prop = typeof(MeasurementRow).GetProperty(bindingPath);
                        if (prop != null && prop.CanWrite)
                        {
                            string oldVal = prop.GetValue(targetRow)?.ToString() ?? "";
                            string newVal = cells[j].Trim();
                            if (oldVal != newVal) cellSnapshots.Add(new Tuple<MeasurementRow, string, string, string>(targetRow, bindingPath, oldVal, newVal));
                        }
                    }
                }
            }

            if (cellSnapshots.Count == 0 && newlyAddedRows.Count == 0) return;

            Action redo = () => {
                foreach (var nr in newlyAddedRows) sheetRef.Rows.Add(nr);
                foreach (var snap in cellSnapshots) typeof(MeasurementRow).GetProperty(snap.Item2)?.SetValue(snap.Item1, snap.Item4);
                sheetRef.UpdateSheetState();
                if (_currentSheet == sheetRef) { UpdateTotalDisplay(); UpdateLiveCostMeter(ListLiveBuildingCosts.Visibility == Visibility.Visible); }
            };

            Action undo = () => {
                foreach (var snap in cellSnapshots) typeof(MeasurementRow).GetProperty(snap.Item2)?.SetValue(snap.Item1, snap.Item3);
                foreach (var nr in newlyAddedRows) sheetRef.Rows.Remove(nr);
                sheetRef.UpdateSheetState();
                if (_currentSheet == sheetRef) { UpdateTotalDisplay(); UpdateLiveCostMeter(ListLiveBuildingCosts.Visibility == Visibility.Visible); }
            };

            ExecuteAction(undo, redo);
        }

        private void DeleteSelectedRows()
        {
            if (_currentSheet == null) return;
            var selectedRows = GetSelectedRows();
            if (selectedRows.Count == 0) return;

            var sheetRef = _currentSheet;
            var deletedItems = selectedRows.Select(r => new { Index = sheetRef.Rows.IndexOf(r), Row = r }).ToList();

            Action redo = () => {
                foreach (var x in deletedItems.OrderByDescending(x => x.Index)) sheetRef.Rows.Remove(x.Row);
                sheetRef.UpdateSheetState();
                if (_currentSheet == sheetRef) { UpdateTotalDisplay(); UpdateLiveCostMeter(ListLiveBuildingCosts.Visibility == Visibility.Visible); }
            };

            Action undo = () => {
                foreach (var x in deletedItems) sheetRef.Rows.Insert(Math.Min(x.Index, sheetRef.Rows.Count), x.Row);
                sheetRef.UpdateSheetState();
                if (_currentSheet == sheetRef) { UpdateTotalDisplay(); UpdateLiveCostMeter(ListLiveBuildingCosts.Visibility == Visibility.Visible); }
            };

            ExecuteAction(undo, redo);
        }

        private void MenuCopyContent_Click(object sender, RoutedEventArgs e) => ApplicationCommands.Copy.Execute(null, GridMeasurements);
        private void MenuPasteContent_Click(object sender, RoutedEventArgs e) => PasteCellRegion();
        private void MenuClearContent_Click(object sender, RoutedEventArgs e) => ClearSelectedCells();
        private void MenuCopyRow_Click(object sender, RoutedEventArgs e) => CopyMeasurementRows();
        private void MenuPasteRow_Click(object sender, RoutedEventArgs e) => PasteMeasurementRows();
        private void MenuDeleteRow_Click(object sender, RoutedEventArgs e) => DeleteSelectedRows();



        // --- 6. COST METERS & CALCULATIONS ---
        public class LiveBuildingCost
        {
            public string BuildingName { get; set; }
            public double TotalCost { get; set; }
            public string PercentageText { get; set; }
            public Visibility PercentageVisibility { get; set; }
        }

        public void UpdateLiveCostMeter(bool isBuildingSortMode)
        {
            if (AppState.CurrentProject == null || AppState.CurrentProject.WorkItems == null) return;
            EnsureUniqueBuildingNames();

            var bldgDict = AppState.CurrentProject.Buildings.ToDictionary(b => b.BuildingName, b => b.NumberOfBuildings);
            var ratesMap = AppState.CurrentProject.MasterSubItems.ToDictionary(s => s.ID, s => s.Rate);

            var allRows = AppState.CurrentProject.WorkItems.SelectMany(wi => wi.Sheets.SelectMany(sheet => sheet.Rows.Select(row => new
            {
                Building = string.IsNullOrWhiteSpace(sheet.BuildingName) ? "Unassigned" : sheet.BuildingName,
                Ans = row.Ans,
                Rate = ratesMap.TryGetValue(wi.SubItemID, out double rate) ? rate : 0
            }))).Where(x => x.Ans != 0).ToList();

            double grandTotal = allRows.Sum(r => r.Ans * r.Rate * 1.18 * (bldgDict.ContainsKey(r.Building) ? bldgDict[r.Building] : 1));
            double budget = AppState.CurrentProject.Budget;
            bool hasBudget = budget > 0;
            double overallPercentage = hasBudget ? (grandTotal / budget) * 100 : 0;

            if (hasBudget)
            {
                if (overallPercentage > 100) MeterBorder.Background = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#3B2525"));
                else if (overallPercentage >= 50) MeterBorder.Background = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#3B3825"));
                else MeterBorder.Background = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#253B2F"));
            }
            else MeterBorder.Background = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#2D2D36"));

            if (isBuildingSortMode)
            {
                PanelGrandTotalMeter.Visibility = Visibility.Collapsed;
                ListLiveBuildingCosts.Visibility = Visibility.Visible;

                var buildingCosts = allRows.GroupBy(r => r.Building).Select(g =>
                {
                    double cost = g.Sum(r => r.Ans * 1.18 * r.Rate * (bldgDict.ContainsKey(g.Key) ? bldgDict[g.Key] : 1));
                    double pct = hasBudget ? (cost / budget) * 100 : 0;
                    return new LiveBuildingCost
                    {
                        BuildingName = g.Key,
                        TotalCost = cost,
                        PercentageText = $"({pct:F1}%)",
                        PercentageVisibility = hasBudget ? Visibility.Visible : Visibility.Collapsed
                    };
                }).OrderBy(b => b.BuildingName).ToList();

                ListLiveBuildingCosts.ItemsSource = buildingCosts;
            }
            else
            {
                PanelGrandTotalMeter.Visibility = Visibility.Visible;
                ListLiveBuildingCosts.Visibility = Visibility.Collapsed;

                TxtLiveGrandTotal.Text = $"₹ {grandTotal:N2}";
                if (hasBudget)
                {
                    TxtGrandTotalPercent.Text = $"({overallPercentage:F1}% of Budget)";
                    TxtGrandTotalPercent.Visibility = Visibility.Visible;
                }
                else TxtGrandTotalPercent.Visibility = Visibility.Collapsed;
            }
        }

        private void UpdateTotalDisplay()
        {
            if (_currentSheet != null)
            {
                LblTotal.Text = _currentSheet.SheetTotal.ToString("N3");

                double rate = 0;
                if (_currentWorkItem != null)
                {
                    var matchedItem = AppState.CurrentProject.MasterSubItems.FirstOrDefault(s => s.ID == _currentWorkItem.SubItemID);
                    rate = matchedItem?.Rate ?? 0;
                }

                string bldgName = string.IsNullOrWhiteSpace(_currentSheet.BuildingName) ? "Unassigned Building" : _currentSheet.BuildingName;
                int multiplier = AppState.CurrentProject.Buildings.FirstOrDefault(b => b.BuildingName == bldgName)?.NumberOfBuildings ?? 1;

                double cost = _currentSheet.SheetTotal * rate * multiplier;
                LblItemCost.Text = cost.ToString("N2");
            }
        }

        // --- 7. CLIPBOARD LOGIC ---
        private void CopyMeasurementRows()
        {
            var selectedRows = GetSelectedRows();
            if (selectedRows.Count > 0)
            {
                var rowsToCopy = new List<MeasurementRow>();
                foreach (var row in selectedRows)
                {
                    rowsToCopy.Add(new MeasurementRow
                    {
                        Remark = row.Remark,
                        Quantity_A = row.Quantity_A,
                        Factor = row.Factor,
                        Nos = row.Nos,
                        L = row.L,
                        B = row.B,
                        H = row.H
                    });
                }
                try
                {
                    // Create a multi-format payload (JSON for our app, TSV for external Excel)
                    string json = JsonSerializer.Serialize(rowsToCopy);
                    string tsv = string.Join(Environment.NewLine, rowsToCopy.Select(r => $"{r.Remark}\t{r.Quantity_A}\t{r.Factor}\t{r.Nos}\t{r.L}\t{r.B}\t{r.H}"));

                    var dataObj = new DataObject();
                    dataObj.SetData("EstimationApp_MeasurementRows", json);
                    dataObj.SetText(tsv);
                    Clipboard.SetDataObject(dataObj, true);
                }
                catch { } // Ignore locked clipboard access
            }
        }

        private void PasteMeasurementRows()
        {
            if (_currentSheet == null) return;

            var selectedRows = GetSelectedRows();
            int insertIndex = selectedRows.Count > 0 ? _currentSheet.Rows.IndexOf(selectedRows.First()) : _currentSheet.Rows.Count;
            if (insertIndex < 0) insertIndex = _currentSheet.Rows.Count;

            var rowsToPaste = new List<MeasurementRow>();

            try
            {
                var clipboardData = Clipboard.GetDataObject();
                if (clipboardData != null)
                {
                    if (clipboardData.GetDataPresent("EstimationApp_MeasurementRows"))
                    {
                        string json = clipboardData.GetData("EstimationApp_MeasurementRows") as string;
                        if (!string.IsNullOrWhiteSpace(json))
                        {
                            var parsed = JsonSerializer.Deserialize<List<MeasurementRow>>(json);
                            if (parsed != null) rowsToPaste.AddRange(parsed);
                        }
                    }
                    else if (clipboardData.GetDataPresent(DataFormats.UnicodeText) || clipboardData.GetDataPresent(DataFormats.Text))
                    {
                        string textData = clipboardData.GetData(DataFormats.UnicodeText) as string ?? clipboardData.GetData(DataFormats.Text) as string;
                        if (!string.IsNullOrWhiteSpace(textData))
                        {
                            string[] textRows = textData.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.RemoveEmptyEntries);
                            foreach (string rowStr in textRows)
                            {
                                string[] cols = rowStr.Split('\t');
                                rowsToPaste.Add(new MeasurementRow
                                {
                                    Remark = cols.Length > 0 ? cols[0].Trim() : "",
                                    Quantity_A = cols.Length > 1 ? cols[1].Trim() : "",
                                    Factor = cols.Length > 2 && !string.IsNullOrWhiteSpace(cols[2]) ? cols[2].Trim() : "1",
                                    Nos = cols.Length > 3 && !string.IsNullOrWhiteSpace(cols[3]) ? cols[3].Trim() : "1",
                                    L = cols.Length > 4 ? cols[4].Trim() : "",
                                    B = cols.Length > 5 ? cols[5].Trim() : "",
                                    H = cols.Length > 6 ? cols[6].Trim() : ""
                                });
                            }
                        }
                    }
                }
            }
            catch { }

            if (rowsToPaste.Count == 0) return;

            var sheetRef = _currentSheet;
            var snapshotSelected = selectedRows.Select(r => new { Index = sheetRef.Rows.IndexOf(r), Row = r }).ToList();

            Action redo = () => {
                foreach (var s in snapshotSelected.OrderByDescending(x => x.Index)) sheetRef.Rows.Remove(s.Row);
                for (int i = 0; i < rowsToPaste.Count; i++) sheetRef.Rows.Insert(insertIndex + i, rowsToPaste[i]);
                sheetRef.UpdateSheetState();
                if (_currentSheet == sheetRef) { UpdateTotalDisplay(); UpdateLiveCostMeter(ListLiveBuildingCosts.Visibility == Visibility.Visible); }
            };

            Action undo = () => {
                foreach (var p in rowsToPaste) sheetRef.Rows.Remove(p);
                foreach (var s in snapshotSelected) sheetRef.Rows.Insert(Math.Min(s.Index, sheetRef.Rows.Count), s.Row);
                sheetRef.UpdateSheetState();
                if (_currentSheet == sheetRef) { UpdateTotalDisplay(); UpdateLiveCostMeter(ListLiveBuildingCosts.Visibility == Visibility.Visible); }
            };

            ExecuteAction(undo, redo);
        }

        // --- 8. UI EVENT HANDLERS ---
        private void ComboMultiFloors_ItemSelectionChanged(object sender, Xceed.Wpf.Toolkit.Primitives.ItemSelectionChangedEventArgs e)
        {
            if (_currentSheet != null && !_isRefreshingTree)
            {
                var selectedFloors = ComboMultiFloors.SelectedItems.Cast<string>().ToList();
                _currentSheet.FloorName = string.Join(", ", selectedFloors);

                _currentSheet.UpdateSheetState();
                if (_currentWorkItem != null) _currentWorkItem.UpdateState();
                UpdateTotalDisplay();
                UpdateLiveCostMeter(ListLiveBuildingCosts.Visibility == Visibility.Visible);
            }
        }

        private void ComboLocation_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isGroupedByBuilding)
            {
                Dispatcher.BeginInvoke(new Action(() => RefreshSidebar()), System.Windows.Threading.DispatcherPriority.Background);
            }
        }

        private void TxtSearchExplorer_TextChanged(object sender, TextChangedEventArgs e)
        {
            TxtSearchWatermark.Visibility = string.IsNullOrEmpty(TxtSearchExplorer.Text) ? Visibility.Visible : Visibility.Collapsed;
            RefreshSidebar();
        }

        private void BtnToggleView_Click(object sender, RoutedEventArgs e)
        {
            _isGroupedByBuilding = !_isGroupedByBuilding;
            BtnToggleView.Content = _isGroupedByBuilding ? "⮂ By Work Item" : "⮂ By Building";
            RefreshSidebar();
            UpdateLiveCostMeter(_isGroupedByBuilding);
        }

        private void BtnOpenSearch_Click(object sender, RoutedEventArgs e)
        {
            int? currentSelectedId = _currentWorkItem?.SubItemID != 0 ? _currentWorkItem?.SubItemID : null;
            ItemSearchWindow searchWindow = new ItemSearchWindow(_masterSearchList, currentSelectedId) { Owner = this };

            if (searchWindow.ShowDialog() == true)
            {
                SubItemDisplay selectedItem = searchWindow.SelectedItem;

                LblWorkName.Text = selectedItem.WorkName;
                LblItemName.Text = selectedItem.NameOfItem;
                LblSubItem.Text = selectedItem.SubItem;
                LblDescription.Text = TruncateTo30Words(selectedItem.Description);
                TxtSearchDisplay.Text = selectedItem.DisplayName;
                LblRate.Text = selectedItem.Rate.ToString();
                LblUnit.Text = selectedItem.Unit;
                LblTotalUnit.Text = selectedItem.Unit;
                LblDsrCode.Text = selectedItem.Code;

                if (_currentWorkItem != null)
                {
                    _currentWorkItem.SubItemID = selectedItem.SubItemID;
                    string[] nameWords = selectedItem.NameOfItem.Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    string shortName = nameWords.Length > 3 ? string.Join(" ", nameWords.Take(3)) + "..." : string.Join(" ", nameWords);

                    _currentWorkItem.DisplayWorkName = selectedItem.WorkName;
                    _currentWorkItem.DisplayItemName = shortName;
                }
                RefreshSidebar();
            }
        }

        private void BtnAddMeasurement_Click(object sender, RoutedEventArgs e)
        {
            if (_currentWorkItem == null) return;
            _currentWorkItem.Sheets.Add(new MeasurementSheet());
            _currentSheetIndex = _currentWorkItem.Sheets.Count - 1;
            BindCurrentState();
            RefreshSidebar();
        }

        private void BtnNewItem_Click(object sender, RoutedEventArgs e)
        {
            var newItem = new WorkItemEntry();
            newItem.Sheets.Add(new MeasurementSheet());
            AppState.CurrentProject.WorkItems.Add(newItem);

            _currentItemIndex = AppState.CurrentProject.WorkItems.Count - 1;
            _currentSheetIndex = 0;

            TxtSearchDisplay.Text = "Click here to search and select an item...";
            LblRate.Text = ""; LblUnit.Text = ""; LblTotalUnit.Text = "-";
            LblItemName.Text = "Select an item to begin...";
            LblSubItem.Text = ""; LblDsrCode.Text = ""; LblDescription.Text = "";

            BindCurrentState();
            RefreshSidebar();
        }

        private void BtnNewBuilding_Click(object sender, RoutedEventArgs e)
        {
            ProjectSetupWindow setupWindow = new ProjectSetupWindow();
            setupWindow.ShowDialog();

            PopulateDropdowns();

            ComboBuilding.ItemsSource = AppState.CurrentProject.Buildings.Select(b => b.BuildingName).ToList();
            RefreshSidebar();
            if (BtnToggleView != null) UpdateLiveCostMeter(BtnToggleView.Content.ToString().Contains("Work Item"));
        }

        private void MenuDarkMode_Click(object sender, RoutedEventArgs e)
        {
            _isDarkMode = !_isDarkMode;
            if (_isDarkMode)
            {
                AppWindow.Background = (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFromString("#121212");
                RightPanelBackground.Background = (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFromString("#18181B");
                MenuBar.Background = (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFromString("#18181B");
                MenuFile.Foreground = System.Windows.Media.Brushes.White;
                MenuView.Foreground = System.Windows.Media.Brushes.White;

                SearchBorder.Background = (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFromString("#27272A");
                SearchBorder.BorderBrush = (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFromString("#3F3F46");
                TxtSearchDisplay.Foreground = System.Windows.Media.Brushes.White;

                LblWorkName.Foreground = System.Windows.Media.Brushes.White;
                LblItemName.Foreground = System.Windows.Media.Brushes.LightGray;
                LblSubItem.Foreground = System.Windows.Media.Brushes.DarkGray;
                LblLocation.Foreground = System.Windows.Media.Brushes.DarkGray;
                LblTotalHeader.Foreground = System.Windows.Media.Brushes.DarkGray;
                LblCostHeader.Foreground = System.Windows.Media.Brushes.DarkGray;
                LblTotal.Foreground = System.Windows.Media.Brushes.White;
            }
            else
            {
                AppWindow.Background = (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFromString("#F4F5F7");
                RightPanelBackground.Background = (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFromString("#F4F5F7");
                MenuBar.Background = System.Windows.Media.Brushes.White;
                MenuFile.Foreground = (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFromString("#333333");
                MenuView.Foreground = (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFromString("#333333");

                SearchBorder.Background = System.Windows.Media.Brushes.White;
                SearchBorder.BorderBrush = (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFromString("#DDDDDD");
                TxtSearchDisplay.Foreground = (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFromString("#111111");

                LblWorkName.Foreground = (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFromString("#111111");
                LblItemName.Foreground = (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFromString("#333333");
                LblSubItem.Foreground = (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFromString("#555555");
                LblLocation.Foreground = (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFromString("#888888");
                LblTotalHeader.Foreground = (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFromString("#888888");
                LblCostHeader.Foreground = (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFromString("#888888");
                LblTotal.Foreground = (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFromString("#111111");
            }
        }

        // --- 9. FILE & DATABASE MANAGEMENT ---
        private void AutoSaveTimer_Tick(object sender, EventArgs e)
        {
            if (string.IsNullOrWhiteSpace(AppState.CurrentFilePath))
            {
                MessageBox.Show("Auto-Save Reminder: Your project is currently unsaved! Please save your file once manually to enable background auto-saving.", "Unsaved Project", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                try
                {
                    string json = JsonSerializer.Serialize(AppState.CurrentProject);
                    System.IO.File.WriteAllText(AppState.CurrentFilePath, json);
                }
                catch { } // Ignore background save errors
            }
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            var result = MessageBox.Show("Do you want to save your changes before closing?", "Save Workspace", MessageBoxButton.YesNoCancel, MessageBoxImage.Warning);
            if (result == MessageBoxResult.Yes)
            {
                if (string.IsNullOrWhiteSpace(AppState.CurrentFilePath))
                {
                    MessageBox.Show("Please use the Save button to choose a file location first.", "Needs Location", MessageBoxButton.OK, MessageBoxImage.Asterisk);
                    e.Cancel = true;
                }
                else
                {
                    string json = JsonSerializer.Serialize(AppState.CurrentProject);
                    System.IO.File.WriteAllText(AppState.CurrentFilePath, json);
                }
            }
            else if (result == MessageBoxResult.Cancel)
            {
                e.Cancel = true;
            }
        }

        private void MenuSave_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(AppState.CurrentFilePath)) MenuSaveAs_Click(sender, e);
            else
            {
                AppState.CurrentProject.SaveToFile(AppState.CurrentFilePath);
                UpdateWindowTitle();
                MessageBox.Show("Project saved successfully!", "Saved", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void MenuSaveAs_Click(object sender, RoutedEventArgs e)
        {
            SaveFileDialog saveDialog = new SaveFileDialog { Filter = "Estimation Project Files|*.est;*.bld", FileName = AppState.CurrentProject.ProjectName };
            if (saveDialog.ShowDialog() == true)
            {
                AppState.CurrentFilePath = saveDialog.FileName;
                AppState.CurrentProject.SaveToFile(AppState.CurrentFilePath);
                UpdateWindowTitle();
                MessageBox.Show("Project saved successfully!", "Saved", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void MenuOpen_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog openFileDialog = new OpenFileDialog { Filter = "Estimation Project Files|*.est;*.bld|All Files|*.*" };
            if (openFileDialog.ShowDialog() == true)
            {
                try
                {
                    AppState.CurrentProject.LoadDataIntoCurrentInstance(openFileDialog.FileName);
                    AppState.CurrentFilePath = openFileDialog.FileName;
                    AppState.CurrentProject.SyncMissingMaterialTests();
                    PopulateDropdowns();
                    LoadOrInitializeFirstItem();
                    RefreshSidebar();
                    UpdateWindowTitle();
                }
                catch (Exception ex) { MessageBox.Show($"Failed to load the file.\n\nError: {ex.Message}", "Load Error", MessageBoxButton.OK, MessageBoxImage.Error); }
            }
        }

        private void MenuCloseProject_Click(object sender, RoutedEventArgs e)
        {
            var result = MessageBox.Show("Do you want to save your changes before closing the project?", "Save Changes", MessageBoxButton.YesNoCancel, MessageBoxImage.Warning);
            if (result == MessageBoxResult.Yes) { MenuSave_Click(null, null); ReturnToMainMenu(); }
            else if (result == MessageBoxResult.No) { ReturnToMainMenu(); }
        }

        private void ReturnToMainMenu()
        {
            AppState.CurrentProject = new ProjectDetails();
            AppState.CurrentFilePath = "";
            MainWindow mainWindow = new MainWindow();
            mainWindow.Show();
            this.Close();
        }

        private void UpdateWindowTitle()
        {
            string baseTitle = "Estimation Workspace";
            if (!string.IsNullOrEmpty(AppState.CurrentFilePath)) this.Title = $"{baseTitle} - {System.IO.Path.GetFileName(AppState.CurrentFilePath)}";
            else if (AppState.CurrentProject != null && !string.IsNullOrWhiteSpace(AppState.CurrentProject.ProjectName)) this.Title = $"{baseTitle} - {AppState.CurrentProject.ProjectName} (Unsaved)";
            else this.Title = baseTitle;
        }

        private string TruncateTo30Words(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return "";
            var words = text.Split(new[] { ' ', '\n', '\r', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (words.Length <= 30) return text;
            return string.Join(" ", words.Take(30)) + "...";
        }

        private string GetSortName(string displayName)
        {
            if (string.IsNullOrEmpty(displayName)) return "";
            int splitIndex = displayName.IndexOf(']');
            return (splitIndex >= 0 && splitIndex < displayName.Length - 1) ? displayName.Substring(splitIndex + 1).Trim() : displayName.Trim();
        }

        // --- 10. REPORTING & EXTERNAL IMPORTS ---
        private void BtnGenerateReport_Click(object sender, RoutedEventArgs e)
        {
            GridMeasurements.CommitEdit();
            new CommonReportWindow(ReportGenerationType.HeadwiseAbstract).ShowDialog();
        }

        private void BtnGenerateItemReport_Click(object sender, RoutedEventArgs e)
        {
            GridMeasurements.CommitEdit();
            new CommonReportWindow(ReportGenerationType.ItemwiseAbstract).ShowDialog();
        }

        private void BtnGenerateMeasReport_Click(object sender, RoutedEventArgs e)
        {
            GridMeasurements.CommitEdit();
            new CommonReportWindow(ReportGenerationType.HeadwiseMeasurement).ShowDialog();
        }

        private void BtnGenerateItemMeasReport_Click(object sender, RoutedEventArgs e)
        {
            GridMeasurements.CommitEdit();
            new CommonReportWindow(ReportGenerationType.ItemwiseMeasurement).ShowDialog();
        }

        private void MenuImport_Click(object sender, RoutedEventArgs e)
        {
            ImportBuildingsWindow importWindow = new ImportBuildingsWindow { Owner = this };
            if (importWindow.ShowDialog() == true)
            {
                PopulateDropdowns();
                RefreshSidebar();
                UpdateLiveCostMeter(BtnToggleView.Content.ToString().Contains("Work Item"));
                MessageBox.Show("Buildings imported successfully!", "Import Complete", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void MenuUpdateDB_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog openFileDialog = new OpenFileDialog { Filter = "Access Database|*.accdb;*.mdb", Title = "Select Master Database" };
            if (openFileDialog.ShowDialog() == true)
            {
                string filePath = openFileDialog.FileName;
                string connectionString = $@"Provider=Microsoft.ACE.OLEDB.12.0;Data Source={filePath};Persist Security Info=False;OLE DB Services=-1;";

                try
                {
                    using (OleDbConnection connection = new OleDbConnection(connectionString))
                    {
                        connection.Open();
                        var schema = connection.GetSchema("Tables");
                        string tableNames = "";
                        foreach (System.Data.DataRow row in schema.Rows) tableNames += row["TABLE_NAME"].ToString() + ",";

                        if (!tableNames.Contains("Work Table") || !tableNames.Contains("Item Table") || !tableNames.Contains("Sub Item Table") || !tableNames.Contains("Unit Table"))
                        {
                            MessageBox.Show("Invalid Database Schema. Ensure all required tables exist.", "Import Error", MessageBoxButton.OK, MessageBoxImage.Error);
                            return;
                        }

                        using (OleDbCommand cmd = new OleDbCommand("SELECT * FROM [Unit Table]", connection))
                        using (OleDbDataReader reader = cmd.ExecuteReader())
                        {
                            AppState.CurrentProject.MasterUnits.Clear();
                            while (reader.Read()) AppState.CurrentProject.MasterUnits.Add(new UnitTable { ID = Convert.ToInt32(reader["ID"]), Unitt = reader["Unitt"].ToString() });
                        }

                        using (OleDbCommand cmd = new OleDbCommand("SELECT * FROM [Work Table]", connection))
                        using (OleDbDataReader reader = cmd.ExecuteReader())
                        {
                            AppState.CurrentProject.MasterWork.Clear();
                            while (reader.Read()) AppState.CurrentProject.MasterWork.Add(new WorkTable { ID = Convert.ToInt32(reader["ID"]), WorkName = reader["Work Name"].ToString() });
                        }

                        using (OleDbCommand cmd = new OleDbCommand("SELECT * FROM [Item Table]", connection))
                        using (OleDbDataReader reader = cmd.ExecuteReader())
                        {
                            AppState.CurrentProject.MasterItems.Clear();
                            while (reader.Read())
                            {
                                AppState.CurrentProject.MasterItems.Add(new ItemTable
                                {
                                    ID = Convert.ToInt32(reader["ID"]),
                                    WorkNameID = reader["Work Name ID"] != DBNull.Value ? Convert.ToInt32(reader["Work Name ID"]) : 0,
                                    NameOfItem = reader["Name of Item"].ToString(), Description = reader["Descreption"].ToString(),
                                    Note = reader["Note"].ToString(), AreaFactor = reader["Area Factor"].ToString(), Chapter = reader["Chapter"].ToString()
                                });
                            }
                        }

                        using (OleDbCommand cmd = new OleDbCommand("SELECT * FROM [Sub Item Table]", connection))
                        using (OleDbDataReader reader = cmd.ExecuteReader())
                        {
                            AppState.CurrentProject.MasterSubItems.Clear();
                            while (reader.Read())
                            {
                                AppState.CurrentProject.MasterSubItems.Add(new SubItemTable
                                {
                                    ID = Convert.ToInt32(reader["ID"]),
                                    ItemID = reader["Item ID"] != DBNull.Value ? Convert.ToInt32(reader["Item ID"]) : 0,
                                    MeasurementID = reader["Mesurment ID"] != DBNull.Value ? Convert.ToInt32(reader["Mesurment ID"]) : 0,
                                    SubItem = reader["Sub Item"].ToString(), Code = reader["Code"].ToString(), Unit = reader["Unit"].ToString(),
                                    Rate = reader["Rate"] != DBNull.Value ? Convert.ToDouble(reader["Rate"]) : 0, Note = reader["Note"].ToString()
                                });
                            }
                        }
                    }

                    AppState.CurrentProject.PromptFloorMappingIfNeeded();

                    PopulateDropdowns();
                    if (_currentWorkItem != null) BindCurrentState();
                    RefreshSidebar();
                    UpdateLiveCostMeter(BtnToggleView.Content.ToString().Contains("Work Item"));

                    MessageBox.Show($"Database imported successfully! The Live Cost Meter and Search lists have been updated.\n\nWorks loaded: {AppState.CurrentProject.MasterWork.Count}\nItems loaded: {AppState.CurrentProject.MasterItems.Count}\nSubItems loaded: {AppState.CurrentProject.MasterSubItems.Count}", "Database Sync Complete");
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Failed to read database. \n\nError: {ex.Message}", "Database Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }
    }
}