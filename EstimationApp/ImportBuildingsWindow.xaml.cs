using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Data;
using System.Data.OleDb;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;

namespace EstimationApp
{
    public class ImportBuildingItem
    {
        public string BuildingName { get; set; }
        
        public string FilePath { get; set; }
        public string FileName => Path.GetFileName(FilePath);
        public ProjectDetails ParsedProject { get; set; }
    }

    public partial class ImportBuildingsWindow : Window
    {
        public ObservableCollection<ImportBuildingItem> SelectedBuildings { get; set; } = new ObservableCollection<ImportBuildingItem>();

        public ImportBuildingsWindow()
        {
            InitializeComponent();
            GridBuildings.ItemsSource = SelectedBuildings;
        }

        private void BtnAddFiles_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog openFileDialog = new OpenFileDialog
            {
                Filter = "Estimation Files (*.est;*.accdb;*.mdb)|*.est;*.accdb;*.mdb",
                Multiselect = true,
                Title = "Select Projects/Databases to Import"
            };

            if (openFileDialog.ShowDialog() == true)
            {
                foreach (string file in openFileDialog.FileNames)
                {
                    try
                    {
                        ProjectDetails proj = null;

                        if (file.EndsWith(".est", StringComparison.OrdinalIgnoreCase))
                        {
                            string json = File.ReadAllText(file);
                            proj = JsonSerializer.Deserialize<ProjectDetails>(json);
                        }
                        else if (file.EndsWith(".accdb", StringComparison.OrdinalIgnoreCase) || file.EndsWith(".mdb", StringComparison.OrdinalIgnoreCase))
                        {
                            proj = ParseAccessDatabase(file);
                        }

                        if (proj != null)
                        {
                            var bldgNames = proj.Buildings?.Select(b => b.BuildingName).Where(b => !string.IsNullOrWhiteSpace(b)).Distinct().ToList() ?? new List<string>();

                            if (bldgNames.Count == 0 && proj.WorkItems != null)
                            {
                                bldgNames = proj.WorkItems.SelectMany(w => w.Sheets).Select(s => s.BuildingName).Where(b => !string.IsNullOrWhiteSpace(b)).Distinct().ToList();
                            }

                            foreach (string b in bldgNames)
                            {
                                if (!SelectedBuildings.Any(x => x.BuildingName == b && x.FilePath == file))
                                {
                                    SelectedBuildings.Add(new ImportBuildingItem { BuildingName = b, FilePath = file, ParsedProject = proj });
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"Failed to parse {Path.GetFileName(file)}.\nError: {ex.Message}", "Parse Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                    }
                }
            }
        }

        private void BtnRemoveBuilding_Click(object sender, RoutedEventArgs e)
        {
            if (GridBuildings.SelectedItem is ImportBuildingItem selectedItem)
            {
                SelectedBuildings.Remove(selectedItem);
            }
        }

        private void BtnImport_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedBuildings.Count == 0)
            {
                MessageBox.Show("Please add at least one building to import.", "No Data", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                ProjectDetails masterProject = AppState.CurrentProject;

                foreach (var bldgItem in SelectedBuildings)
                {
                    var proj = bldgItem.ParsedProject;

                    if (proj.MasterWork != null) foreach (var w in proj.MasterWork) if (!masterProject.MasterWork.Any(x => x.ID == w.ID)) masterProject.MasterWork.Add(w);
                    if (proj.MasterItems != null) foreach (var i in proj.MasterItems) if (!masterProject.MasterItems.Any(x => x.ID == i.ID)) masterProject.MasterItems.Add(i);
                    if (proj.MasterSubItems != null) foreach (var s in proj.MasterSubItems) if (!masterProject.MasterSubItems.Any(x => x.ID == s.ID)) masterProject.MasterSubItems.Add(s);
                    if (proj.MasterUnits != null) foreach (var u in proj.MasterUnits) if (!masterProject.MasterUnits.Any(x => x.ID == u.ID)) masterProject.MasterUnits.Add(u);

                    if (proj.MaterialTests != null)
                    {
                        foreach (var test in proj.MaterialTests)
                        {
                            var existingTest = masterProject.MaterialTests.FirstOrDefault(t => t.TestName == test.TestName);
                            if (existingTest != null) existingTest.Quantity += test.Quantity;
                            else masterProject.MaterialTests.Add(test);
                        }
                    }

                    // CRITICAL FIX: The Collision Resolver (Checks LIVE project)
                    string baseName = bldgItem.BuildingName;
                    string finalName = baseName;
                    int counter = 2;

                    while (masterProject.Buildings.Any(b => b.BuildingName == finalName))
                    {
                        finalName = $"{baseName} {counter}";
                        counter++;
                    }

                    // Register the newly resolved building name
                    masterProject.Buildings.Add(new BuildingGroup { BuildingName = finalName, NumberOfBuildings = 1 });

                    if (proj.WorkItems != null)
                    {
                        foreach (var wi in proj.WorkItems)
                        {
                            var targetSheets = wi.Sheets.Where(s => s.BuildingName == bldgItem.BuildingName).ToList();

                            if (targetSheets.Count > 0)
                            {
                                var masterWi = masterProject.WorkItems.FirstOrDefault(w => w.SubItemID == wi.SubItemID);
                                if (masterWi == null)
                                {
                                    string sortName = wi.DisplayName;
                                    int splitIndex = sortName.IndexOf(']');
                                    if (splitIndex >= 0 && splitIndex < sortName.Length - 1) sortName = sortName.Substring(splitIndex + 1).Trim();

                                    masterWi = new WorkItemEntry { SubItemID = wi.SubItemID, DisplayWorkName = wi.DisplayWorkName, DisplayItemName = wi.DisplayItemName, Sheets = new ObservableCollection<MeasurementSheet>() };
                                    masterProject.WorkItems.Add(masterWi);
                                }

                                foreach (var sheet in targetSheets)
                                {
                                    // RE-ROUTE the sheet to the unique identifier
                                    sheet.BuildingName = finalName;
                                    masterWi.Sheets.Add(sheet);
                                }
                            }
                        }
                    }
                }
                AppState.CurrentProject.PromptFloorMappingIfNeeded();

                this.DialogResult = true;
                this.Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Critical error importing data: {ex.Message}", "Import Failed", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private ProjectDetails ParseAccessDatabase(string filePath)
        {
            string connString = $@"Provider=Microsoft.ACE.OLEDB.12.0;Data Source={filePath};Persist Security Info=False;OLE DB Services=-1;";
            ProjectDetails proj = new ProjectDetails();
            proj.WorkItems = new ObservableCollection<WorkItemEntry>();
            proj.Buildings = new ObservableCollection<BuildingGroup>();

            DataTable GetTable(string tableName)
            {
                DataTable dt = new DataTable();
                try
                {
                    using (OleDbConnection conn = new OleDbConnection(connString))
                    {
                        using (OleDbCommand cmd = new OleDbCommand($"SELECT * FROM [{tableName}]", conn))
                        {
                            conn.Open();
                            using (OleDbDataAdapter adapter = new OleDbDataAdapter(cmd)) { adapter.Fill(dt); }
                        }
                    }
                }
                catch { }
                return dt;
            }

            string SafeGetString(object val) => val == null || val == DBNull.Value ? "" : val.ToString().Trim();
            double? SafeGetDouble(object val) { if (val == null || val == DBNull.Value) return null; if (double.TryParse(val.ToString(), out double d)) return d; return null; }

            DataTable dtWork = GetTable("Work Table");
            DataTable dtItem = GetTable("Item Table");
            DataTable dtSubItem = GetTable("Sub Item Table");
            DataTable dtUnits = GetTable("Unit Table");
            DataTable dtBuildings = GetTable("Building Name");
            DataTable dtFloors = GetTable("Floor Name");

            // NEW: Added tables required for the older DB structure
            DataTable dtRooms = GetTable("Room Name");
            DataTable dtObjects = GetTable("Object Name");

            DataTable dtDataEntry = GetTable("Data Entry Table");
            DataTable dtMesuID = GetTable("Measurement ID");
            DataTable dtMesuSheet = GetTable("Measurement Sheet");

            foreach (DataRow r in dtWork.Rows) proj.MasterWork.Add(new WorkTable { ID = Convert.ToInt32(r["ID"]), WorkName = SafeGetString(r["Work Name"]) });
            foreach (DataRow r in dtUnits.Rows) proj.MasterUnits.Add(new UnitTable { ID = Convert.ToInt32(r["ID"]), Unitt = r.Table.Columns.Contains("Unitt") ? SafeGetString(r["Unitt"]) : SafeGetString(r["Unit"]) });
            foreach (DataRow r in dtItem.Rows) proj.MasterItems.Add(new ItemTable { ID = Convert.ToInt32(r["ID"]), WorkNameID = r.Table.Columns.Contains("Work Name ID") && r["Work Name ID"] != DBNull.Value ? Convert.ToInt32(r["Work Name ID"]) : 0, NameOfItem = r.Table.Columns.Contains("Name of Item") ? SafeGetString(r["Name of Item"]) : "", Description = r.Table.Columns.Contains("Descreption") ? SafeGetString(r["Descreption"]) : "", Note = r.Table.Columns.Contains("Note") ? SafeGetString(r["Note"]) : "" });
            foreach (DataRow r in dtSubItem.Rows) proj.MasterSubItems.Add(new SubItemTable { ID = Convert.ToInt32(r["ID"]), ItemID = r.Table.Columns.Contains("Item ID") && r["Item ID"] != DBNull.Value ? Convert.ToInt32(r["Item ID"]) : 0, MeasurementID = r.Table.Columns.Contains("Mesurment ID") && r["Mesurment ID"] != DBNull.Value ? Convert.ToInt32(r["Mesurment ID"]) : 0, SubItem = r.Table.Columns.Contains("Sub Item") ? SafeGetString(r["Sub Item"]) : "", Code = r.Table.Columns.Contains("Code") ? SafeGetString(r["Code"]) : "", Unit = r.Table.Columns.Contains("Unit") ? SafeGetString(r["Unit"]) : "", Rate = r.Table.Columns.Contains("Rate") ? (SafeGetDouble(r["Rate"]) ?? 0.0) : 0.0, Note = r.Table.Columns.Contains("Note") ? SafeGetString(r["Note"]) : "" });

            var uniqueBuildingNames = new HashSet<string>();

            // Phase 1 - Loop through Data Entry Table to catch ALL items (even blanks)
            foreach (DataRow dataEntry in dtDataEntry.Rows)
            {
                if (dataEntry["Select Item"] == DBNull.Value) continue;
                int subItemId = Convert.ToInt32(dataEntry["Select Item"]);

                var workItem = proj.WorkItems.FirstOrDefault(w => w.SubItemID == subItemId);
                if (workItem == null)
                {
                    var targetSubItem = proj.MasterSubItems.FirstOrDefault(s => s.ID == subItemId);
                    var targetItem = proj.MasterItems.FirstOrDefault(i => i.ID == targetSubItem?.ItemID);
                    var targetWork = proj.MasterWork.FirstOrDefault(w => w.ID == targetItem?.WorkNameID);

                    string wName = targetWork?.WorkName ?? "Work";
                    // Grab the full string
                    string iName = targetItem?.NameOfItem ?? (targetSubItem?.SubItem ?? "Item");

                    // SAVE THE FULL UNEDITED STRING TO MEMORY! 
                    // No taking 5 words, no adding dots. Just the raw iName.
                    workItem = new WorkItemEntry
                    {
                        SubItemID = subItemId,
                        DisplayWorkName = wName,
                        DisplayItemName = iName, // <--- Passes the full paragraph
                        Sheets = new ObservableCollection<MeasurementSheet>()
                    };
                    proj.WorkItems.Add(workItem);
                }
            }

            // Phase 2 - Loop through Measurement Sheets to grab rows
            foreach (DataRow row in dtMesuSheet.Rows)
            {
                if (row["Mesu ID"] == DBNull.Value) continue;
                int mesuId = Convert.ToInt32(row["Mesu ID"]);
                DataRow[] mesuHeaderRows = dtMesuID.Select($"ID = {mesuId}");
                if (mesuHeaderRows.Length == 0) continue;
                DataRow mesuHeader = mesuHeaderRows[0];
                if (mesuHeader["Data Ent ID"] == DBNull.Value) continue;
                int dataEntId = Convert.ToInt32(mesuHeader["Data Ent ID"]);
                DataRow[] dataEntryRows = dtDataEntry.Select($"ID = {dataEntId}");
                if (dataEntryRows.Length == 0) continue;
                DataRow dataEntry = dataEntryRows[0];
                if (dataEntry["Select Item"] == DBNull.Value) continue;
                int subItemId = Convert.ToInt32(dataEntry["Select Item"]);

                // --- CRITICAL FIX: Universal Dynamic Parameter Resolvers ---

                string bldgName = "Unassigned Building";
                if (mesuHeader.Table.Columns.Contains("Building") && mesuHeader["Building"] != DBNull.Value && !string.IsNullOrWhiteSpace(mesuHeader["Building"].ToString()))
                {
                    if (int.TryParse(mesuHeader["Building"].ToString(), out int bId))
                    {
                        DataRow[] bldgRows = dtBuildings.Select($"ID = {bId}");
                        if (bldgRows.Length > 0 && bldgRows[0].Table.Columns.Contains("Building")) bldgName = SafeGetString(bldgRows[0]["Building"]);
                    }
                    else bldgName = SafeGetString(mesuHeader["Building"]);
                }

                string floorName = "Unassigned Floor";
                if (mesuHeader.Table.Columns.Contains("Floor") && mesuHeader["Floor"] != DBNull.Value && !string.IsNullOrWhiteSpace(mesuHeader["Floor"].ToString()))
                {
                    if (int.TryParse(mesuHeader["Floor"].ToString(), out int fId))
                    {
                        DataRow[] floorRows = dtFloors.Select($"ID = {fId}");
                        if (floorRows.Length > 0) floorName = SafeGetString(floorRows[0].ItemArray.Length > 1 ? floorRows[0][1] : floorRows[0][0]);
                    }
                    else floorName = SafeGetString(mesuHeader["Floor"]);
                }

                string roomName = "";
                if (mesuHeader.Table.Columns.Contains("Room") && mesuHeader["Room"] != DBNull.Value && !string.IsNullOrWhiteSpace(mesuHeader["Room"].ToString()))
                {
                    if (dtRooms.Columns.Count > 0 && int.TryParse(mesuHeader["Room"].ToString(), out int rId))
                    {
                        DataRow[] roomRows = dtRooms.Select($"ID = {rId}");
                        if (roomRows.Length > 0 && roomRows[0].Table.Columns.Contains("Room Name")) roomName = SafeGetString(roomRows[0]["Room Name"]);
                        else roomName = SafeGetString(mesuHeader["Room"]);
                    }
                    else roomName = SafeGetString(mesuHeader["Room"]);
                }

                string objName = "";
                if (mesuHeader.Table.Columns.Contains("Objects") && mesuHeader["Objects"] != DBNull.Value && !string.IsNullOrWhiteSpace(mesuHeader["Objects"].ToString()))
                {
                    if (dtObjects.Columns.Count > 0 && int.TryParse(mesuHeader["Objects"].ToString(), out int oId))
                    {
                        DataRow[] objRows = dtObjects.Select($"ID = {oId}");
                        if (objRows.Length > 0)
                        {
                            // Catches the spelling typo in the older database
                            if (objRows[0].Table.Columns.Contains("Object Namem")) objName = SafeGetString(objRows[0]["Object Namem"]);
                            else if (objRows[0].Table.Columns.Contains("Object Name")) objName = SafeGetString(objRows[0]["Object Name"]);
                            else objName = SafeGetString(mesuHeader["Objects"]);
                        }
                        else objName = SafeGetString(mesuHeader["Objects"]);
                    }
                    else objName = SafeGetString(mesuHeader["Objects"]);
                }

                uniqueBuildingNames.Add(bldgName);

                var workItem = proj.WorkItems.FirstOrDefault(w => w.SubItemID == subItemId);
                if (workItem == null) continue;

                var sheet = workItem.Sheets.FirstOrDefault(s => s.BuildingName == bldgName && s.FloorName == floorName);
                if (sheet == null)
                {
                    sheet = new MeasurementSheet { BuildingName = bldgName, FloorName = floorName, MarkerName = objName, RoomName = roomName };
                    workItem.Sheets.Add(sheet);
                }

                sheet.Rows.Add(new MeasurementRow
                {
                    ID = Convert.ToInt32(row["ID"]),
                    Remark = row.Table.Columns.Contains("Remark") ? SafeGetString(row["Remark"]) : "",
                    Factor = row.Table.Columns.Contains("Factor") && row["Factor"] != DBNull.Value ? row["Factor"].ToString() : "1",
                    Nos = row.Table.Columns.Contains("Nos") && row["Nos"] != DBNull.Value ? row["Nos"].ToString() : "1",
                    L = row.Table.Columns.Contains("L") && row["L"] != DBNull.Value ? row["L"].ToString() : "",
                    B = row.Table.Columns.Contains("B") && row["B"] != DBNull.Value ? row["B"].ToString() : "",
                    H = row.Table.Columns.Contains("H") && row["H"] != DBNull.Value ? row["H"].ToString() : "",
                    Quantity_A = row.Table.Columns.Contains("a") && row["a"] != DBNull.Value ? row["a"].ToString() : ""
                });
            }

            // Phase 3 - Assign a blank sheet to any item that had no measurements
            foreach (var wi in proj.WorkItems)
            {
                if (wi.Sheets.Count == 0)
                {
                    wi.Sheets.Add(new MeasurementSheet { BuildingName = "Unassigned Building", FloorName = "Unassigned Floor" });
                    uniqueBuildingNames.Add("Unassigned Building");
                }
            }

            foreach (string bldg in uniqueBuildingNames) proj.Buildings.Add(new BuildingGroup { BuildingName = bldg, NumberOfBuildings = 1 });

            return proj;
        }
    }
}