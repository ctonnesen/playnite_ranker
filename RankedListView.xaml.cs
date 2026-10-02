using System;
using System.Globalization;
using System.Windows.Controls;
using System.Windows.Data;

namespace GameRanker.Views
{
    public partial class RankedListView : UserControl
    {
        public RankedListView() => InitializeComponent();

        private void GamesListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (DataContext is RankedListViewModel viewModel)
            {
                // Clear IsSelected from deselected items
                foreach (RankedGame item in e.RemovedItems)
                {
                    item.IsSelected = false;
                }

                // Set IsSelected on newly selected items
                foreach (RankedGame item in e.AddedItems)
                {
                    item.IsSelected = true;
                }

                // Sync SelectedGames collection
                viewModel.SelectedGames.Clear();
                if (GamesListView.SelectedItems.Count > 0)
                {
                    foreach (RankedGame item in GamesListView.SelectedItems)
                    {
                        viewModel.SelectedGames.Add(item);
                    }
                }
            }
        }
    }

    public class IsSelectedConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values.Length < 1) return false;
            
            if (values[0] is RankedGame game)
                return game.IsSelected;
            
            return false;
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}