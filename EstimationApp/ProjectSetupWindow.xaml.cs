using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace EstimationApp
{
    public partial class ProjectSetupWindow : Window
    {
        public ProjectSetupWindow()
        {
            InitializeComponent();
            LoadProjectData();
        }

        private void LoadProjectData()
        {
            TxtProjectName.Text = AppState.CurrentProject.ProjectName;
            TxtOwnerName.Text = AppState.CurrentProject.OwnerName;
            TxtRefNo.Text = AppState.CurrentProject.ReferenceNo;
            TxtBudget.Text = AppState.CurrentProject.Budget.ToString();

            GridBuildings.ItemsSource = AppState.CurrentProject.Buildings;
            GridMarkers.ItemsSource = AppState.CurrentProject.Markers;
            GridRooms.ItemsSource = AppState.CurrentProject.Rooms;
            GridVariables.ItemsSource = AppState.CurrentProject.Variables;

            // SMART UPGRADE LOGIC
            if (AppState.CurrentProject.MaterialTests.Count == 0 ||
                string.IsNullOrEmpty(AppState.CurrentProject.MaterialTests[0].Description) ||
                !AppState.CurrentProject.MaterialTests.Any(t => t.TestName.Contains("Royalty")))
            {
                var savedQuantities = AppState.CurrentProject.MaterialTests
                    .GroupBy(t => t.TestName)
                    .ToDictionary(g => g.Key, g => g.First().Quantity);

                AppState.CurrentProject.MaterialTests.Clear();
                InitializeDefaultMaterialTests();

                foreach (var test in AppState.CurrentProject.MaterialTests)
                {
                    if (savedQuantities.TryGetValue(test.TestName, out double oldQty))
                    {
                        test.Quantity = oldQty;
                    }
                }
            }

            GridMaterialTests.ItemsSource = AppState.CurrentProject.MaterialTests;
        }



        // --- NEW: THE CASCADING RENAME ENGINE ---
        private void Grid_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
        {
            if (e.EditAction != DataGridEditAction.Commit || AppState.CurrentProject.WorkItems == null) return;

            if (e.EditingElement is TextBox textBox)
            {
                // 1. Identify EXACTLY which column is being edited
                var boundColumn = e.Column as DataGridBoundColumn;
                string propertyName = (boundColumn?.Binding as System.Windows.Data.Binding)?.Path.Path ?? "";

                // 2. If the user is editing the multiplier column, STOP here and let it act as a normal multiplier!
                if (propertyName == "NumberOfBuildings") return;

                string newValue = textBox.Text.Trim();
                var rowData = e.Row.DataContext;

                string oldValue = "";
                string category = "";

                // 3. Only proceed with the cascade if the specific Name column was edited
                if (sender == GridBuildings && propertyName == "BuildingName")
                {
                    category = "Building";
                    oldValue = rowData.GetType().GetProperty("BuildingName")?.GetValue(rowData) as string;
                }
                // Inside Grid_CellEditEnding...
                else if (sender == GridMarkers && propertyName == "MarkerName")
                {
                    category = "Marker";
                    oldValue = rowData.GetType().GetProperty("MarkerName")?.GetValue(rowData) as string;
                }

           
                else if (sender == GridRooms && propertyName == "RoomName")
                {
                    category = "Room";
                    oldValue = rowData.GetType().GetProperty("RoomName")?.GetValue(rowData) as string;
                }

                // 4. Execute the cascading rename safely
                if (!string.IsNullOrWhiteSpace(oldValue) && oldValue != newValue && !string.IsNullOrEmpty(category))
                {
                    foreach (var workItem in AppState.CurrentProject.WorkItems)
                    {
                        foreach (var sheet in workItem.Sheets)
                        {
                            if (category == "Building" && sheet.BuildingName == oldValue) sheet.BuildingName = newValue;
                            else if (category == "Marker" && sheet.MarkerName == oldValue) sheet.MarkerName = newValue;
                            else if (category == "Room" && sheet.RoomName == oldValue) sheet.RoomName = newValue;
                        }
                    }
                }
            }
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
        private void BtnSaveContinue_Click(object sender, RoutedEventArgs e)
        {
            AppState.CurrentProject.ProjectName = TxtProjectName.Text;
            AppState.CurrentProject.OwnerName = TxtOwnerName.Text;
            AppState.CurrentProject.ReferenceNo = TxtRefNo.Text;
            AppState.CurrentProject.Budget = double.TryParse(TxtBudget.Text, out double b) ? b : 0;
            EnsureUniqueBuildingNames();

            if (AppState.CurrentProject.Floors.Count == 0)
            {
                AppState.CurrentProject.Floors.Add(new FloorEntry { FloorName = "All" });
            }

            if (!Application.Current.Windows.OfType<WorkEnvironmentWindow>().Any())
            {
                new WorkEnvironmentWindow().Show();
            }

            this.Close();
        }

        private void BtnImportExcel_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog openFileDialog = new OpenFileDialog
            {
                Filter = "Excel Files|*.xls;*.xlsx;*.xlsm",
                Title = "Select Testing Rates Excel File"
            };

            if (openFileDialog.ShowDialog() == true)
            {
                MessageBox.Show($"File selected: {openFileDialog.FileName}");
            }
        }

        private void InitializeDefaultMaterialTests()
        {
            // Optimized into an array for cleaner memory allocation and faster initialization
            var defaultTests = new[]
            {
                new MaterialTest { TestCode = "17176", TestName = "Royalty Charges", Description = "78.04-B Royalty charges: For Other Minerals R.A. Unit : 1 / Cubic Metre", Rate = 237.37, Quantity = 0 },
                new MaterialTest { TestCode = "17244", TestName = "Cement", Description = "72.01-A CEMENT: Standard Consistancy, Fineness, Specific Gravity, Setting Time ( Initial & Final ), Compressive Strength, Soundness. R.A. Unit : 1 / Per Test", Rate = 3770.0, Quantity = 0 },
                new MaterialTest { TestCode = "17245", TestName = "Aggregates", Description = "72.02-A AGGREGATE: Water Absorption, Specific Gravity,Impact Value, Crushing Value. R.A. Unit : 1 / Per Test", Rate = 2600.0, Quantity = 0 },
                new MaterialTest { TestCode = "17246", TestName = "Aggregates", Description = "72.02-B AGGREGATE: Sieve Analysis R.A. Unit : 1 / Per Test", Rate = 690.0, Quantity = 0 },
                new MaterialTest { TestCode = "17247", TestName = "AGGREGATE", Description = "72.02-C AGGREGATE: Abrasion Value. R.A. Unit : 1 / Per Test", Rate = 1170.0, Quantity = 0 },
                new MaterialTest { TestCode = "17248", TestName = "AGGREGATE", Description = "72.0 2-D AGGREGATE: Flakiness Index & Elongation Index R.A. Unit : 1 / Per Test", Rate = 850.0, Quantity = 0 },
                new MaterialTest { TestCode = "17249", TestName = "AGGREGATE", Description = "72.02-E AGGREGATE: Stripping Value. (for Bituminous Work) R.A. Unit : 1 / Per Test", Rate = 745.0, Quantity = 0 },
                new MaterialTest { TestCode = "17250", TestName = "AGGREGATE", Description = "72.0 2-F AGGREGATE: Soundness. R.A. Unit : 1 / Per Test", Rate = 2495.0, Quantity = 0 },
                new MaterialTest { TestCode = "17251", TestName = "MIX DESIGN", Description = "72.03-A MIX DESIGN: GSB Mix Design./Cement Treated Sub Base ( CTSB) R.A. Unit : 1 / Per Test", Rate = 16250.0, Quantity = 0 },
                new MaterialTest { TestCode = "17252", TestName = "MIX DESIGN", Description = "72.03-B MIX DESIGN: Wet Mix Macadam Mix Design. R.A. Unit : 1 / Per Test", Rate = 16250.0, Quantity = 0 },
                new MaterialTest { TestCode = "17253", TestName = "FINE AGGREGATE", Description = "72.04-A FINE AGGREGATE: Fineness Modulus ( Sieve Analysis ),Silt & Clay Content R.A. Unit : 1 / Per Test", Rate = 1380.0, Quantity = 0 },
                new MaterialTest { TestCode = "17254", TestName = "FINE AGGREGATE", Description = "72.04-B FINE AGGREGATE: Chloride & Sulphate Content R.A. Unit : 1 / Per Test", Rate = 795.0, Quantity = 0 },
                new MaterialTest { TestCode = "17255", TestName = "FINE AGGREGATE", Description = "72.04-C FINE AGGREGATE: Silt Factor. R.A. Unit : 1 / Per Test", Rate = 850.0, Quantity = 0 },
                new MaterialTest { TestCode = "17256", TestName = "BRICKS", Description = "72.05-A BRICKS: Water Absorption (Set of 5 Bricks), Compressive Strength( Set of 5 Bricks), Efflorescence (Set of 5 Bricks). R.A. Unit : 1 / Per Test", Rate = 2175.0, Quantity = 0 },
                new MaterialTest { TestCode = "17257", TestName = "FLOORING TILES (MOSSAIC / CEMENT)", Description = "72.06-A FLOORING TILES (MOSSAIC / CEMENT): Flexural test ( Set of 6 Tiles), Water Absorption ( Set of 6 Tiles).Resistance to wear ( Set of 6 Tiles). R.A. Unit : 1 / Per Test", Rate = 2495.0, Quantity = 0 },
                new MaterialTest { TestCode = "17258", TestName = "FLOORING TILES (MOSSAIC / CEMENT):", Description = "72.06-B FLOORING TILES (MOSSAIC / CEMENT): Flooring of Natural Stone (Kota,Marble, Granite,Tandur.etc.) Water absorption, Specific gravity R.A. Unit : 1 / Per Test", Rate = 1380.0, Quantity = 0 },
                new MaterialTest { TestCode = "17259", TestName = "CERAMIC TILES / VITRIFIED TILES", Description = "72.07-A CERAMIC TILES / VITRIFIED TILES: Water Absorption, Modulus of Rupture (Set of 6 Tiles) R.A. Unit : 1 / Per Test", Rate = 1595.0, Quantity = 0 },
                new MaterialTest { TestCode = "17260", TestName = "MANGLORE TILES", Description = "72.08-A MANGLORE TILES: Water Absorption (Set of 6 Tiles), Flexural test (Set of 6 Tiles). R.A. Unit : 1 / Per Test", Rate = 1485.0, Quantity = 0 },
                new MaterialTest { TestCode = "17261", TestName = "MANGLORE TILES", Description = "72.08-B MANGLORE TILES: Permeability. R.A. Unit : 1 / Per Test", Rate = 955.0, Quantity = 0 },
                new MaterialTest { TestCode = "17262", TestName = "CONCRETE", Description = "72.09-A CONCRETE: Compressive Strength OF C.C. Cube (Set of 3 cubes). R.A. Unit : 1 / Per Test", Rate = 690.0, Quantity = 0 },
                new MaterialTest { TestCode = "17263", TestName = "CONCRETE", Description = "72.09-B CONCRETE: Concrete Mix Design (With all Tests on basic materials) R.A. Unit : 1 / Per Test", Rate = 13755.0, Quantity = 0 },
                new MaterialTest { TestCode = "17264", TestName = "CONCRETE", Description = "72.09-C CONCRETE: Flexural strength of Beam. R.A. Unit : 1 / Per Test", Rate = 745.0, Quantity = 0 },
                new MaterialTest { TestCode = "17265", TestName = "CONCRETE", Description = "72.09-D CONCRETE: Permeability Test. R.A. Unit : 1 / Per Test", Rate = 1540.0, Quantity = 0 },
                new MaterialTest { TestCode = "17266", TestName = "CONCRETE", Description = "72.09-E CONCRETE: Concrete mix design by Accelerated curing method. (By Accelerated curing method) R.A. Unit : 1 / Per Mix Design", Rate = 16250.0, Quantity = 0 },
                new MaterialTest { TestCode = "17267", TestName = "CONCRETE", Description = "72.09-F CONCRETE: Taking of core samples in concrete pavement. (Excluding Dead Journey Charges) R.A. Unit : 1 / Per Test", Rate = 2870.0, Quantity = 0 },
                new MaterialTest { TestCode = "17268", TestName = "CONCRETE", Description = "72.09-G CONCRETE: Dead Journey Charges. R.A. Unit : 1 / Kilometre", Rate = 17.0, Quantity = 0 },
                new MaterialTest { TestCode = "17269", TestName = "CONCRETE PAVING BLOCKS", Description = "72.10-A CONCRETE PAVING BLOCKS: Compressive Strength , Water Absorption,(Set of 8 Blocks) R.A. Unit : 1 / Per Test", Rate = 2495.0, Quantity = 0 },
                new MaterialTest { TestCode = "17270", TestName = "MORTOR", Description = "72.11-A MORTOR: Compressive Strength.( Set of 3 Cubes) R.A. Unit : 1 / Per Test", Rate = 690.0, Quantity = 0 },
                new MaterialTest { TestCode = "17271", TestName = "STONE/ RUBBLE", Description = "72.12-A STONE/ RUBBLE: Crushing Value/Compressive Strength, Water Absorption & Specific Gravity, R.A. Unit : 1 / Per Test", Rate = 2020.0, Quantity = 0 },
                new MaterialTest { TestCode = "17272", TestName = "SOIL / MURUM:", Description = "72.13-A SOIL / MURUM: Sieve Analysis. R.A. Unit : 1 / Per Test", Rate = 690.0, Quantity = 0 },
                new MaterialTest { TestCode = "17273", TestName = "SOIL / MURUM", Description = "72.13-B SOIL / MURUM: Liquid limit & plastic Limit. R.A. Unit : 1 / Per Test", Rate = 1170.0, Quantity = 0 },
                new MaterialTest { TestCode = "17274", TestName = "SOIL / MURUM", Description = "72.13-C SOIL / MURUM: Compaction Test ( Proctor Density ). R.A. Unit : 1 / Per Test", Rate = 1860.0, Quantity = 0 },
                new MaterialTest { TestCode = "17275", TestName = "SOIL / MURUM", Description = "72.13-D SOIL / MURUM: C.B.R. Test ( Lab ) With compaction test. R.A. Unit : 1 / Per Test", Rate = 6905.0, Quantity = 0 },
                new MaterialTest { TestCode = "17276", TestName = "SOIL / MURUM", Description = "72.13-E SOIL / MURUM: C.B.R. Test ( Field Determination Test) Excluding Transportation.As per IS 2720 Part XXXI R.A. Unit : 1 / Per Test", Rate = 2600.0, Quantity = 0 },
                new MaterialTest { TestCode = "17277", TestName = "SOIL / MURUM", Description = "72.13-F SOIL / MURUM: Dead Journey Charges of inspection Vehical R.A. Unit : 1 / Kilometre", Rate = 17.0, Quantity = 0 },
                new MaterialTest { TestCode = "17278", TestName = "SOIL / MURUM", Description = "72.13-G SOIL / MURUM: Dead Journey Charges of Truck/Tipper R.A. Unit : 1 / Kilometre", Rate = 62.0, Quantity = 0 },
                new MaterialTest { TestCode = "17279", TestName = "SOIL / MURUM", Description = "72.13-H SOIL / MURUM: Field Density by Sand replacement method. R.A. Unit : 1 / Per Test", Rate = 1115.0, Quantity = 0 },
                new MaterialTest { TestCode = "17280", TestName = "SOIL / MURUM", Description = "72.13-I SOIL / MURUM: Sulphate & Chloride Contents R.A. Unit : 1 / Per Test", Rate = 795.0, Quantity = 0 },
                new MaterialTest { TestCode = "17281", TestName = "SOIL / MURUM", Description = "72.13-J SOIL / MURUM: Mechanical Analysis. R.A. Unit : 1 / Per Test", Rate = 2230.0, Quantity = 0 },
                new MaterialTest { TestCode = "17282", TestName = "SOIL / MURUM", Description = "72.13-K SOIL / MURUM: Plate Load Bearing Test. (Excluding Transportation) R.A. Unit : 1 / Per Test", Rate = 31225.0, Quantity = 0 },
                new MaterialTest { TestCode = "17283", TestName = "SOIL / MURUM", Description = "72.13-L SOIL / MURUM: Free Swell Test for Soil R.A. Unit : 1 / Per Test", Rate = 905.0, Quantity = 0 },
                new MaterialTest { TestCode = "17284", TestName = "HOLLOW / SOLID /AAC BLOCKS", Description = "72.14-A HOLLOW / SOLID /AAC BLOCKS: Density Test . (Set of 3 Blocks ),Compressive Strength. (Set of 3 Blocks ),Water Absorption Test ( Set of 3 Blocks ) R.A. Unit : 1 / Per Test", Rate = 1965.0, Quantity = 0 },
                new MaterialTest { TestCode = "17285", TestName = "WATER", Description = "72.15-A WATER: PH Value, Sulphate & Chloride Content. R.A. Unit : 1 / Per Test", Rate = 1115.0, Quantity = 0 },
                new MaterialTest { TestCode = "17286", TestName = "WOOD", Description = "72.16-A WOOD: Density, Moisture Content. R.A. Unit : 1 / Per Test", Rate = 1060.0, Quantity = 0 },
                new MaterialTest { TestCode = "17287", TestName = "FLUSH DOOR", Description = "72.17-A FLUSH DOOR: Knife Test, Adhesion Test, End Immersion Test. R.A. Unit : 1 / Per Test", Rate = 2710.0, Quantity = 0 },
                new MaterialTest { TestCode = "17288", TestName = "PLYWOOD", Description = "72.01-A PLYWOOD: Determination of Resistance to dry heat,Determination of Moisture Content,Determination of Density,Thickness of Plywood. R.A. Unit : 1 / Per Test", Rate = 3400.0, Quantity = 0 },
                new MaterialTest { TestCode = "17289", TestName = "PLYWOOD", Description = "72.01-B PLYWOOD: Test for Glue Adhesion R.A. Unit : 1 / Per Test", Rate = 530.0, Quantity = 0 },
                new MaterialTest { TestCode = "17290", TestName = "PARTICAL BOARD", Description = "72.19-A PARTICAL BOARD: Determination of Moisture Content,Determination of Density R.A. Unit : 1 / Per Test", Rate = 1595.0, Quantity = 0 },
                new MaterialTest { TestCode = "17291", TestName = "ALUMINIUM SECTION", Description = "72.20-A ALUMINIUM SECTION: Thickness, Mass Per Running meter R.A. Unit : 1 / Per Test", Rate = 690.0, Quantity = 0 },
                new MaterialTest { TestCode = "17292", TestName = "ALUMINIUM SECTION", Description = "72.20-B ALUMINIUM SECTION: Test on Powder Coating R.A. Unit : 1 / Per Test", Rate = 795.0, Quantity = 0 },
                new MaterialTest { TestCode = "17293", TestName = "A.C. PIPES", Description = "72.21-A A.C. PIPES: Water Absorption R.A. Unit : 1 / Per Test", Rate = 690.0, Quantity = 0 },
                new MaterialTest { TestCode = "17294", TestName = "A.C. PIPES", Description = "72.21-B A.C. PIPES: Bursting Strength R.A. Unit : 1 / Per Test", Rate = 530.0, Quantity = 0 },
                new MaterialTest { TestCode = "17295", TestName = "G.I. PIPES", Description = "72.22-A G.I. PIPES: Weight per running meter,Diameter of pipe & wall thickness of pipe R.A. Unit : 1 / Per Test", Rate = 265.0, Quantity = 0 },
                new MaterialTest { TestCode = "17296", TestName = "G.I. PIPES", Description = "72.22-B G.I. PIPES: Weight of Zinc coating per sq. m. R.A. Unit : 1 / Per Test", Rate = 320.0, Quantity = 0 },
                new MaterialTest { TestCode = "17297", TestName = "P.V.C. PIPES (NONPLASTISIZED)", Description = "72.23-A P.V.C. PIPES (NONPLASTISIZED): Weight per running meter,Diameter of pipe & wall thickness of pipe R.A. Unit : 1 / Per Test", Rate = 265.0, Quantity = 0 },
                new MaterialTest { TestCode = "17298", TestName = "STEEL ANTI CORROSIVE TEST", Description = "72.24-A STEEL ANTI CORROSIVE TEST: Resistance to applied Voltage. (1 Hr. Test) (Set of 2 Bars) R.A. Unit : 1 / Per Test", Rate = 1115.0, Quantity = 0 },
                new MaterialTest { TestCode = "17299", TestName = "STEEL ANTI CORROSIVE TEST", Description = "72.24-B STEEL ANTI CORROSIVE TEST: Resistance to applied Voltage. (30 Days Test) (Set of 2 Bars) R.A. Unit : 1 / Per Test", Rate = 3135.0, Quantity = 0 },
                new MaterialTest { TestCode = "17300", TestName = "STEEL ANTI CORROSIVE TEST", Description = "72.24-C STEEL ANTI CORROSIVE TEST: Thickness of Coating. (Set of 2 Bars)", Rate = 585.0, Quantity = 0 },
                new MaterialTest { TestCode = "17301", TestName = "STEEL ANTI CORROSIVE TEST", Description = "72.24-D STEEL ANTI CORROSIVE TEST: Chemical Resistance Test.( Set of 8 Bars ) R.A. Unit : 1 / Per Test", Rate = 2390.0, Quantity = 0 },
                new MaterialTest { TestCode = "17302", TestName = "STEEL ANTI CORROSIVE TEST", Description = "72.24-E STEEL ANTI CORROSIVE TEST: Hardness of Coating Test . R.A. Unit : 1 / Per Test", Rate = 320.0, Quantity = 0 },
                new MaterialTest { TestCode = "17303", TestName = "STEEL ANTI CORROSIVE TEST", Description = "72.24-G STEEL ANTI CORROSIVE TEST: Salt Spray Test ( 4 Cycles ) ( Set of 2 Bars ) R.A. Unit : 1 / Per Test", Rate = 1485.0, Quantity = 0 },
                new MaterialTest { TestCode = "17304", TestName = "STEEL BAR TESTING", Description = "72.25-A STEEL BAR TESTING: Upto 16 mm (Set of 3 Bars) R.A. Unit : 1 / Per Test", Rate = 1275.0, Quantity = 0 },
                new MaterialTest { TestCode = "17305", TestName = "STEEL BAR TESTING", Description = "72.25-B STEEL BAR TESTING: Above 16 mm (Set of 3 Bars) R.A. Unit : 1 / Per Test", Rate = 1595.0, Quantity = 0 },
                new MaterialTest { TestCode = "17306", TestName = "STEEL BAR TESTING", Description = "72.25-C STEEL BAR TESTING: (Tensile strength, %, Elongation, Yield Stress,Weight-Per Meter, Bend / Rebend Test, Proof Stress.) Nitrol Solution Test. (Set of 3 Bars) R.A. Unit : 1 / Per Test", Rate = 3875.0, Quantity = 0 },
                new MaterialTest { TestCode = "17307", TestName = "PAINT / THERMO-PLASTIC PAINT", Description = "72.26-A PAINT / THERMO-PLASTIC PAINT: Glass bead contents & grading analysis R.A. Unit : 1 / Per Test", Rate = 3875.0, Quantity = 0 },
                new MaterialTest { TestCode = "17308", TestName = "PAINT / THERMO-PLASTIC PAINT", Description = "72.26-B PAINT / THERMO-PLASTIC PAINT: Reflectance & yellowness index. R.A. Unit : 1 / Per Test", Rate = 1115.0, Quantity = 0 },
                new MaterialTest { TestCode = "17309", TestName = "PAINT / THERMO-PLASTIC PAINT", Description = "72.26-C PAINT / THERMO-PLASTIC PAINT: Flowability.(Percentage residue). R.A. Unit : 1 / Per Test", Rate = 1115.0, Quantity = 0 },
                new MaterialTest { TestCode = "17310", TestName = "PAINT / THERMO-PLASTIC PAINT", Description = "72.26-D PAINT / THERMO-PLASTIC PAINT: Softening Point. (Ring & ball method). R.A. Unit : 1 / Per Test", Rate = 1010.0, Quantity = 0 },
                new MaterialTest { TestCode = "17311", TestName = "PAINT / THERMO-PLASTIC PAINT", Description = "72.26-E PAINT / THERMO-PLASTIC PAINT: Drying Time R.A. Unit : 1 / Per Test", Rate = 795.0, Quantity = 0 },
                new MaterialTest { TestCode = "17312", TestName = "COAL TAR EPOXY PAINT", Description = "72.27-A COAL TAR EPOXY PAINT: Drying Time. R.A. Unit : 1 / Per Test", Rate = 795.0, Quantity = 0 },
                new MaterialTest { TestCode = "17313", TestName = "COAL TAR EPOXY PAINT", Description = "72.27-B COAL TAR EPOXY PAINT: Dry Film Thickness. R.A. Unit : 1 / Per Test", Rate = 745.0, Quantity = 0 },
                new MaterialTest { TestCode = "17314", TestName = "COAL TAR EPOXY PAINT", Description = "72.27-C COAL TAR EPOXY PAINT: Flexibility.", Rate = 320.0, Quantity = 0 },
                new MaterialTest { TestCode = "17315", TestName = "COAL TAR EPOXY PAINT", Description = "72.27-D COAL TAR EPOXY PAINT: Gel Time. R.A. Unit : 1 / Per Test", Rate = 530.0, Quantity = 0 },
                new MaterialTest { TestCode = "17316", TestName = "COAL TAR EPOXY PAINT", Description = "72.27-E COAL TAR EPOXY PAINT: Pot Life R.A. Unit : 1 / Per Test", Rate = 530.0, Quantity = 0 },
                new MaterialTest { TestCode = "17317", TestName = "COAL TAR EPOXY PAINT", Description = "72.27-F COAL TAR EPOXY PAINT: Volume of Solid. R.A. Unit : 1 / Per Test", Rate = 1060.0, Quantity = 0 },
                new MaterialTest { TestCode = "17318", TestName = "ROAD SIGN BOARD", Description = "72.28-A ROAD SIGN BOARD: Retro Reflective Test. R.A. Unit : 1 / Per Test", Rate = 3985.0, Quantity = 0 },
                new MaterialTest { TestCode = "17319", TestName = "ROUGHNESS INDEX / ROAD UN-EVENNESS TEST", Description = "72.29-A ROUGHNESS INDEX / ROAD UN-EVENNESS TEST: Road Surface Single Lane.(3.70 to 5.50 m) R.A. Unit : 1 / Kilometre", Rate = 745.0, Quantity = 0 },
                new MaterialTest { TestCode = "17320", TestName = "ROUGHNESS INDEX / ROAD UN-EVENNESS TEST", Description = "72.29-B ROUGHNESS INDEX / ROAD UN-EVENNESS TEST: Road Surface 1.5 Lane to Two Lane. (5.50 to 7.00 m) R.A. Unit : 1 / Kilometre", Rate = 1485.0, Quantity = 0 },
                new MaterialTest { TestCode = "17321", TestName = "ROUGHNESS INDEX / ROAD UN-EVENNESS TEST", Description = "72.29-C ROUGHNESS INDEX / ROAD UN-EVENNESS TEST: Each Additional Lane R.A. Unit : 1 / Kilometre", Rate = 745.0, Quantity = 0 },
                new MaterialTest { TestCode = "17322", TestName = "ROUGHNESS INDEX / ROAD UN-EVENNESS TEST", Description = "72.29-D ROUGHNESS INDEX / ROAD UN-EVENNESS TEST: Dead Journey Charges. R.A. Unit : 1 / Kilometre", Rate = 17.0, Quantity = 0 },
                new MaterialTest { TestCode = "17323", TestName = "OVERLAY DESIGN", Description = "72.30-A OVERLAY DESIGN: Benkelman Beam Test (Excluding Dead Journey Charges) R.A. Unit : 1 / Kilometre", Rate = 14125.0, Quantity = 0 },
                new MaterialTest { TestCode = "17324", TestName = "NON -DESTRUCTIVE TEST", Description = "72.31-A NON -DESTRUCTIVE TEST: Rebound Hammer Test R.A. Unit : 1 / 1 Point", Rate = 265.0, Quantity = 0 },
                new MaterialTest { TestCode = "17325", TestName = "NON -DESTRUCTIVE TEST", Description = "72.31-B NON -DESTRUCTIVE TEST: Ultrasonic Pulse Velocity R.A. Unit : 1 / 1 Point", Rate = 370.0, Quantity = 0 },
                new MaterialTest { TestCode = "17326", TestName = "Pile", Description = "72.32 Pile Integrity test R.A. Unit : 1 / Per No", Rate = 550.0, Quantity = 0 },
                new MaterialTest { TestCode = "17177", TestName = "Skilled labour", Description = "78.05 Skilled labour", Rate = 885.62, Quantity = 0 },
                new MaterialTest { TestCode = "17178", TestName = "Unskilled labour", Description = "78.06 Unskilled labour", Rate = 812.67, Quantity = 0 },
                new MaterialTest { TestCode = "17179", TestName = "R.C.C.Design Engineer", Description = "78.07 Consultancy charges for R.C.C.Design Engineer", Rate = 0.01, Quantity = 0 },
                new MaterialTest { TestCode = "17180", TestName = "Consultancy charges for Third Party", Description = "78.08 Consultancy charges for Third Party (1.48 % of Estimated Cost)", Rate = 0.02, Quantity = 0 }
            };

            foreach (var test in defaultTests)
            {
                AppState.CurrentProject.MaterialTests.Add(test);
            }
        }
    }
}