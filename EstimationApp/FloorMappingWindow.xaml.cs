using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;

namespace EstimationApp
{
    public class FloorMapItem
    {
        public string ImportedFloor { get; set; }
        public ObservableCollection<string> SelectedMappedFloors { get; set; } = new ObservableCollection<string>();

        // Dynamically creates the comma-separated string the engine expects!
        public string MappedFloor => string.Join(", ", SelectedMappedFloors);

        public List<string> AvailableStandardFloors { get; set; }
    }

    public partial class FloorMappingWindow : Window
    {
        public List<FloorMapItem> Mappings { get; private set; }
        public bool IsConfirmed { get; private set; } = false;

        public FloorMappingWindow(List<string> importedFloors)
        {
            InitializeComponent();

            List<string> standardFloors = new List<string>
            {
               "All", "-3 Basement", "-2 Foundation", "-1 Plinth",
                "0 Ground Floor", "1 First Floor", "2 Second Floor",
                "3 Third Floor", "4 Fourth Floor", "5 Fifth Floor",
                "6 Sixth Floor", "7 Seventh Floor", "8 Eighth Floor",
                "9 Ninth Floor", "10 Terrace Floor"
            };

            Mappings = importedFloors.Select(f => {
                var item = new FloorMapItem
                {
                    ImportedFloor = f,
                    AvailableStandardFloors = standardFloors
                };

                // Set default mapping cleanly via the collection
                item.SelectedMappedFloors.Add(standardFloors[4]); // "0 Ground Floor"
                return item;
            }).ToList();

            GridFloorMaps.ItemsSource = Mappings;
        }

        private void BtnConfirm_Click(object sender, RoutedEventArgs e)
        {
            // Validate that the user didn't uncheck everything for a specific row
            if (Mappings.Any(m => m.SelectedMappedFloors.Count == 0))
            {
                MessageBox.Show("Please map at least one standard floor for every imported item.", "Missing Maps", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            IsConfirmed = true;
            this.Close();
        }
    }
}