using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Threading;

namespace EstimationApp
{
    public partial class ItemSearchWindow : Window
    {
        public SubItemDisplay SelectedItem { get; private set; }

        private DispatcherTimer _searchTimer;
        private string _searchText = "";

        // NEW: Custom Sorter that scores items based on match priority
        private class SearchPriorityComparer : IComparer
        {
            private readonly string _searchText;

            public SearchPriorityComparer(string searchText)
            {
                _searchText = searchText;
            }

            public int Compare(object x, object y)
            {
                var a = (SubItemDisplay)x;
                var b = (SubItemDisplay)y;

                int scoreA = GetScore(a);
                int scoreB = GetScore(b);

                // Lower score means higher priority (e.g., Score 1 beats Score 2)
                if (scoreA != scoreB)
                    return scoreA.CompareTo(scoreB);

                // If they tie (e.g., both matched the Work Name), fall back to alphabetical
                return string.Compare(a.DisplayName, b.DisplayName, StringComparison.OrdinalIgnoreCase);
            }

            private int GetScore(SubItemDisplay item)
            {
                // Priority 1: Work Name
                if (item.WorkName != null && item.WorkName.IndexOf(_searchText, StringComparison.OrdinalIgnoreCase) >= 0) return 1;

                // Priority 2: Item Name
                if (item.NameOfItem != null && item.NameOfItem.IndexOf(_searchText, StringComparison.OrdinalIgnoreCase) >= 0) return 2;

                // Priority 3: DSR Code
                if (item.Code != null && item.Code.IndexOf(_searchText, StringComparison.OrdinalIgnoreCase) >= 0) return 3;

                // Priority 4: Sub Item Name (Fallback)
                if (item.SubItem != null && item.SubItem.IndexOf(_searchText, StringComparison.OrdinalIgnoreCase) >= 0) return 4;

                return 5; // Default score for non-matches (shouldn't happen due to filter)
            }
        }

        public ItemSearchWindow(List<SubItemDisplay> masterList, int? preSelectedId = null)
        {
            InitializeComponent();

            ListItems.ItemsSource = masterList;

            _searchTimer = new DispatcherTimer();
            _searchTimer.Interval = TimeSpan.FromMilliseconds(150);
            _searchTimer.Tick += SearchTimer_Tick;

            if (preSelectedId.HasValue && preSelectedId.Value != 0)
            {
                var matchedItem = masterList.FirstOrDefault(x => x.SubItemID == preSelectedId.Value);
                if (matchedItem != null)
                {
                    ListItems.SelectedItem = matchedItem;
                    ListItems.ScrollIntoView(matchedItem);
                }
            }

            TxtSearch.Focus();
        }

        private void TxtSearch_TextChanged(object sender, TextChangedEventArgs e)
        {
            _searchText = TxtSearch.Text;
            _searchTimer.Stop();
            _searchTimer.Start();
        }

        private void SearchTimer_Tick(object sender, EventArgs e)
        {
            _searchTimer.Stop();
            ApplyFiltersAndSorts();
        }

        private void ComboSort_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            ApplyFiltersAndSorts();
        }

        private void ApplyFiltersAndSorts()
        {
            if (ListItems == null || ListItems.ItemsSource == null) return;

            // Cast to ListCollectionView so we have access to the CustomSort property
            ListCollectionView view = (ListCollectionView)CollectionViewSource.GetDefaultView(ListItems.ItemsSource);

            // DeferRefresh prevents the UI from flickering while we apply filters AND sorts simultaneously
            using (view.DeferRefresh())
            {
                // 1. Apply the text filter (Now checks WorkName, ItemName, SubItem, AND DSR Code)
                view.Filter = o =>
                {
                    if (string.IsNullOrWhiteSpace(_searchText)) return true;
                    SubItemDisplay item = (SubItemDisplay)o;
                    return (item.WorkName != null && item.WorkName.IndexOf(_searchText, StringComparison.OrdinalIgnoreCase) >= 0) ||
                           (item.NameOfItem != null && item.NameOfItem.IndexOf(_searchText, StringComparison.OrdinalIgnoreCase) >= 0) ||
                           (item.Code != null && item.Code.IndexOf(_searchText, StringComparison.OrdinalIgnoreCase) >= 0) ||
                           (item.SubItem != null && item.SubItem.IndexOf(_searchText, StringComparison.OrdinalIgnoreCase) >= 0);
                };

                // 2. Apply Priority Sorting
                view.SortDescriptions.Clear();

                if (!string.IsNullOrWhiteSpace(_searchText))
                {
                    // If searching, hijack the sort and apply our custom priority scoring!
                    view.CustomSort = new SearchPriorityComparer(_searchText);
                }
                else
                {
                    // If search box is empty, remove custom sort and respect the Combo Box
                    view.CustomSort = null;
                    if (ComboSort.SelectedIndex == 1) // "Work Name (A-Z)"
                    {
                        view.SortDescriptions.Add(new SortDescription("WorkName", ListSortDirection.Ascending));
                        view.SortDescriptions.Add(new SortDescription("NameOfItem", ListSortDirection.Ascending));
                    }
                }
            }

            // 3. Force the ListView to scroll back to the top of the newly prioritized results
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (ListItems.Items.Count > 0)
                {
                    ListItems.ScrollIntoView(ListItems.Items[0]);
                }
            }), DispatcherPriority.Background);
        }

        private void TxtSearch_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Down && ListItems.Items.Count > 0)
            {
                ListItems.Focus();
                if (ListItems.SelectedIndex < 0) ListItems.SelectedIndex = 0;
                e.Handled = true;
            }
        }

        private void ListViewItem_Click(object sender, MouseButtonEventArgs e)
        {
            if (sender is ListViewItem item && item.DataContext is SubItemDisplay subItem)
            {
                SelectedItem = subItem;
                this.DialogResult = true;
                this.Close();
            }
        }

        protected override void OnPreviewKeyDown(KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                if (ListItems.SelectedItem is SubItemDisplay selected)
                {
                    SelectedItem = selected;
                    this.DialogResult = true;
                    this.Close();
                }
                e.Handled = true;
            }
            base.OnPreviewKeyDown(e);
        }
    }
}