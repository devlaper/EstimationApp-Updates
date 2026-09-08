using Microsoft.Win32; // Required for OpenFileDialog
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.OleDb;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;

namespace EstimationApp
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
        }

        private void BtnCreateProject_Click(object sender, RoutedEventArgs e)
        {
            // 1. Create a new instance of the Project Setup screen
            ProjectSetupWindow setupWindow = new ProjectSetupWindow();

            // 2. Show the new window
            setupWindow.Show();

            // 3. Close the current main menu window so it doesn't clutter the background
            this.Close();
        }

        private class TempPdfRow
        {
            public string Dsr { get; set; }
            public string Building { get; set; }
            public string Floor { get; set; }
            public string Remark { get; set; }
            public string Factor { get; set; }
            public string Nos { get; set; }
            public string L { get; set; }
            public string B { get; set; }
            public string H { get; set; }
            public string QtyA { get; set; }
        }
        
        private void BtnOpenProject_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog openFileDialog = new OpenFileDialog();
            openFileDialog.Filter = "Estimation Project Files|*.est;*.bld|All Files|*.*";
            openFileDialog.Title = "Open Existing Workspace";

            if (openFileDialog.ShowDialog() == true)
            {
                try
                {
                    // 1. Load the custom JSON file directly into the global AppState
                    AppState.CurrentProject.LoadDataIntoCurrentInstance(openFileDialog.FileName);
                    AppState.CurrentFilePath = openFileDialog.FileName;
                    AppState.CurrentProject.SyncMissingMaterialTests();

                    // 2. Skip the setup screen and launch the workspace
                    WorkEnvironmentWindow workWindow = new WorkEnvironmentWindow();
                    workWindow.Show();

                    // 3. Close the main menu
                    this.Close();
                }
                catch (Exception ex)
                {
                    // Failsafe in case they try to open a corrupted or incorrect file
                    MessageBox.Show($"Failed to load the file. Make sure it is a valid estimation file.\n\nError: {ex.Message}", "Load Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }
        // Future Logic Goal:
        // LoadFileIntoAppState(filePath);
        // DashboardWindow dashboard = new DashboardWindow();
        // dashboard.Show();
        // this.Close();

        private void BtnOpenCombineWindow_Click(object sender, RoutedEventArgs e)
        {
            CombineProjectsWindow combineWindow = new CombineProjectsWindow();
            combineWindow.ShowDialog();
        }

        private void BtnImportDatabase_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog openFileDialog = new OpenFileDialog();
            openFileDialog.Filter = "Access Database|*.accdb;*.mdb";
            openFileDialog.Title = "Select Master Database";

            if (openFileDialog.ShowDialog() == true)
            {
                string filePath = openFileDialog.FileName;
                string connectionString = $@"Provider=Microsoft.ACE.OLEDB.12.0;Data Source={filePath};Persist Security Info=False;";

                try
                {
                    using (OleDbConnection connection = new OleDbConnection(connectionString))
                    {
                        connection.Open();

                        // 1. Verify required tables exist (Basic check)
                        var schema = connection.GetSchema("Tables");
                        string tableNames = "";
                        foreach (System.Data.DataRow row in schema.Rows)
                        {
                            tableNames += row["TABLE_NAME"].ToString() + ",";
                        }

                        if (!tableNames.Contains("Work Table") || !tableNames.Contains("Item Table") ||
                            !tableNames.Contains("Sub Item Table") || !tableNames.Contains("Unit Table"))
                        {
                            MessageBox.Show("Invalid Database Schema. Ensure all required tables exist.", "Import Error", MessageBoxButton.OK, MessageBoxImage.Error);
                            return;
                        }

                        // 2. Load Unit Table
                        using (OleDbCommand cmd = new OleDbCommand("SELECT * FROM [Unit Table]", connection))
                        using (OleDbDataReader reader = cmd.ExecuteReader())
                        {
                            AppState.CurrentProject.MasterUnits.Clear();
                            while (reader.Read())
                            {
                                AppState.CurrentProject.MasterUnits.Add(new UnitTable
                                {
                                    ID = Convert.ToInt32(reader["ID"]),
                                    Unitt = reader["Unitt"].ToString()
                                });
                            }
                        }

                        // 3. Load Work Table
                        using (OleDbCommand cmd = new OleDbCommand("SELECT * FROM [Work Table]", connection))
                        using (OleDbDataReader reader = cmd.ExecuteReader())
                        {
                            AppState.CurrentProject.MasterWork.Clear();
                            while (reader.Read())
                            {
                                AppState.CurrentProject.MasterWork.Add(new WorkTable
                                {
                                    ID = Convert.ToInt32(reader["ID"]),
                                    WorkName = reader["Work Name"].ToString()
                                });
                            }
                        }

                        // 4. Load Item Table
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
                                    NameOfItem = reader["Name of Item"].ToString(),
                                    Description = reader["Descreption"].ToString(),
                                    Note = reader["Note"].ToString(),
                                    AreaFactor = reader["Area Factor"].ToString(),
                                    Chapter = reader["Chapter"].ToString()
                                });
                            }
                        }

                        // 5. Load Sub Item Table
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
                                    SubItem = reader["Sub Item"].ToString(),
                                    Code = reader["Code"].ToString(),
                                    Unit = reader["Unit"].ToString(),
                                    Rate = reader["Rate"] != DBNull.Value ? Convert.ToDouble(reader["Rate"]) : 0,
                                    Note = reader["Note"].ToString()
                                });
                            }
                        }
                    }

                    MessageBox.Show($"Database imported successfully!\n\nWorks loaded: {AppState.CurrentProject.MasterWork.Count}\nItems loaded: {AppState.CurrentProject.MasterItems.Count}\nSubItems loaded: {AppState.CurrentProject.MasterSubItems.Count}", "Success");
                }
                catch (Exception ex)
                {
                    // This will catch missing Microsoft Access Database Engine drivers (a common issue on new PCs)
                    MessageBox.Show($"Failed to read database. \n\nError: {ex.Message}", "Database Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }
    }
        }
    
