using System;
using System.IO;
using System.Windows;
using QuestPDF.Infrastructure;
using AutoUpdaterDotNET; // NEW: Include the updater namespace

namespace EstimationApp
{
    public partial class App : Application
    {
        private void Application_Startup(object sender, StartupEventArgs e)
        {
            // Configure the QuestPDF Community License
            QuestPDF.Settings.License = LicenseType.Community;

            // NEW: Initialize the AutoUpdater before loading any UI
            AutoUpdater.ShowSkipButton = false;
            AutoUpdater.ShowRemindLaterButton = true;
            // You will replace this placeholder URL with the actual Raw URL in Step 4 below
            AutoUpdater.Start("https://raw.githubusercontent.com/devlaper/EstimationApp-Updates/refs/heads/main/updateinfo.xml");

            // 1. Check if the app was launched by double-clicking a file
            if (e.Args.Length > 0 && File.Exists(e.Args[0]))
            {
                string filePath = e.Args[0];

                // 2. Ensure it's an estimation file
                if (filePath.EndsWith(".est", StringComparison.OrdinalIgnoreCase) ||
                    filePath.EndsWith(".bld", StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        // 3. Load the project data directly into the global state
                        AppState.CurrentProject.LoadDataIntoCurrentInstance(filePath);
                        AppState.CurrentProject.SyncMissingMaterialTests();
                        AppState.CurrentFilePath = filePath;

                        // 4. Launch straight into the Work Environment
                        WorkEnvironmentWindow workWindow = new WorkEnvironmentWindow();
                        workWindow.Show();

                        return;
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"Failed to load the file from Windows Explorer.\n\nError: {ex.Message}", "Launch Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
            }

            // 5. Fallback: If no file was double-clicked, just open the Main Menu normally
            MainWindow mainWindow = new MainWindow();
            mainWindow.Show();
        }
    }
}