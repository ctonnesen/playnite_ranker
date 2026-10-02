using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using GongSolutions.Wpf.DragDrop;
using Playnite.SDK;

#nullable enable

namespace GameRanker
{
    public class RankedListViewModel : ObservableObject, IDisposable, IDropTarget
    {
        private readonly IPlayniteAPI _api;
        private readonly RankStore _store;

        private const string PlanToPlay = "Plan to Play";

        public ObservableCollection<RankedGame> Games { get; } = new();

        public ObservableCollection<RankedGame> SelectedGames { get; } = new();

        private string _statusText = string.Empty;
        public string StatusText
        {
            get => _statusText;
            set => SetValue(ref _statusText, value);
        }

        // ── Commands ─────────────────────────────────────────────────────────────────

        public ICommand WipeAllCommand { get; }
        public ICommand MoveUpCommand { get; }
        public ICommand MoveDownCommand { get; }

        public RankedListViewModel(IPlayniteAPI api, RankStore store)
        {
            _api = api;
            _store = store;

            WipeAllCommand = new RelayCommand(WipeAll);
            MoveUpCommand = new RelayCommand(MoveUp, CanMoveUp);
            MoveDownCommand = new RelayCommand(MoveDown, CanMoveDown);

            Rebuild();

            _api.Database.Games.ItemUpdated += OnGamesUpdated;
            _api.Database.Games.ItemCollectionChanged += OnGamesChanged;
        }

        private void OnGamesChanged(object sender, EventArgs e) => Rebuild();

        private void OnGamesUpdated(object sender, Playnite.SDK.ItemUpdatedEventArgs<Playnite.SDK.Models.Game> e)
        {
            bool needsRebuild = false;

            foreach (var change in e.UpdatedItems)
            {
                var oldStatusId = change.OldData.CompletionStatusId;
                var newStatusId = change.NewData.CompletionStatusId;

                if (oldStatusId == newStatusId) continue;

                var oldStatus = oldStatusId != Guid.Empty
                    ? _api.Database.CompletionStatuses.Get(oldStatusId)?.Name
                    : null;
                var newStatus = newStatusId != Guid.Empty
                    ? _api.Database.CompletionStatuses.Get(newStatusId)?.Name
                    : null;

                bool wasPlanning = oldStatus == PlanToPlay;
                bool isPlanning  = newStatus == PlanToPlay;

                if (wasPlanning && !isPlanning)
                {
                    // Left Plan to Play — strip rank tag and close the gap
                    _store.RemoveGame(change.NewData.Id);
                    needsRebuild = true;
                }
                else if (!wasPlanning && isPlanning)
                {
                    // Joined Plan to Play — will appear at the bottom (unranked)
                    needsRebuild = true;
                }
            }

            if (needsRebuild) Rebuild();
        }

        public void Rebuild()
        {
            Games.Clear();

            var ptpGames = _api.Database.Games
                .Where(g =>
                {
                    if (g.CompletionStatusId == Guid.Empty) return false;
                    var s = _api.Database.CompletionStatuses.Get(g.CompletionStatusId);
                    return s?.Name == PlanToPlay;
                })
                .OrderBy(g => _store.GetRank(g.Id))
                .Select((g, i) => new RankedGame(g, i + 1))
                .ToList();

            foreach (var rg in ptpGames)
            {
                LoadCover(rg);
                Games.Add(rg);
            }

            StatusText = $"{Games.Count} games in Plan to Play";
        }

        private void LoadCover(RankedGame rg)
        {
            if (string.IsNullOrEmpty(rg.Game.CoverImage)) return;
            var path = _api.Database.GetFullFilePath(rg.Game.CoverImage);
            if (!File.Exists(path)) return;
            try
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.UriSource = new Uri(path);
                bmp.DecodePixelHeight = 32;
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.EndInit();
                bmp.Freeze();
                rg.Cover = bmp;
            }
            catch { }
        }

        // ── Reorder ──────────────────────────────────────────────────────────────────

        /// <summary>
        /// Moves <paramref name="dragged"/> to just before <paramref name="target"/>,
        /// renumbers all ranks, updates tags, and persists.
        /// </summary>
        public void MoveGame(RankedGame dragged, RankedGame target)
        {
            if (dragged == target) return;
            var to = Games.IndexOf(target);
            if (to < 0) return;
            MoveGameToIndex(dragged, to);
        }

        public void MoveGameToIndex(RankedGame dragged, int insertIndex)
        {
            var from = Games.IndexOf(dragged);
            if (from < 0) return;

            if (insertIndex < 0) insertIndex = 0;
            if (insertIndex > Games.Count) insertIndex = Games.Count;

            if (from == insertIndex || from == insertIndex - 1) return;

            Games.Move(from, insertIndex > from ? insertIndex - 1 : insertIndex);

            for (int i = 0; i < Games.Count; i++)
                Games[i].Rank = i + 1;

            _store.SetRanks(Games);
        }
        public void MoveGamesToIndex(IList<RankedGame> draggedGames, int insertIndex)
        {
            if (!draggedGames.Any()) return;

            // Remove the dragged games from the list
            foreach (var game in draggedGames.OrderByDescending(g => Games.IndexOf(g)))
            {
                Games.Remove(game);
            }

            // Insert them at the new position
            for (int i = 0; i < draggedGames.Count; i++)
            {
                Games.Insert(insertIndex + i, draggedGames[i]);
            }

            // Update ranks
            for (int i = 0; i < Games.Count; i++)
                Games[i].Rank = i + 1;

            _store.SetRanks(Games);
        }

        public void DragOver(IDropInfo dropInfo)
        {
            if (dropInfo.Data is RankedGame || dropInfo.Data is IEnumerable<RankedGame>)
            {
                dropInfo.Effects = DragDropEffects.Move;
                dropInfo.DropTargetAdorner = DropTargetAdorners.Insert;
            }
        }

        public void Drop(IDropInfo dropInfo)
        {
            if (dropInfo.Data is RankedGame single)
            {
                if (dropInfo.InsertIndex >= 0)
                    MoveGameToIndex(single, dropInfo.InsertIndex);
            }
            else if (dropInfo.Data is IEnumerable<RankedGame> multiples)
            {
                var items = multiples.ToList();
                if (items.Any() && dropInfo.InsertIndex >= 0)
                    MoveGamesToIndex(items, dropInfo.InsertIndex);
            }
        }

        public void DropHint(IDropHintInfo dropHintInfo)
        {
            // Optional
        }

        public void DragEnter(IDropInfo dropInfo)
        {
            // Optional
        }

        public void DragLeave(IDropInfo dropInfo)
        {
            // Optional
        }

        // ── Wipe ─────────────────────────────────────────────────────────────────────

        private void WipeAll()
        {
            var result = MessageBox.Show(
                "This will remove all rank tags (#1, #2 …) from every game and clear the saved order.\n\nAre you sure?",
                "Wipe all ranks",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result != MessageBoxResult.Yes) return;

            _store.WipeAllRanks();
            Rebuild();
        }

        // ── Move Up/Down ──────────────────────────────────────────────────────────────

        private void UpdateRanksAndSave()
        {
            for (int i = 0; i < Games.Count; i++)
                Games[i].Rank = i + 1;

            _store.SetRanks(Games);
        }

        private bool CanMoveUp()
        {
            if (SelectedGames.Count == 0) return false;
            var minIndex = SelectedGames.Min(g => Games.IndexOf(g));
            return minIndex > 0;
        }

        private void MoveUp()
        {
            if (SelectedGames.Count == 0) return;
            
            // Move items up from highest index to lowest to maintain grouping
            var sorted = SelectedGames.OrderByDescending(g => Games.IndexOf(g)).ToList();
            
            foreach (var game in sorted)
            {
                var index = Games.IndexOf(game);
                if (index > 0)
                    Games.Move(index, index - 1);
            }
            
            UpdateRanksAndSave();
        }

        private bool CanMoveDown()
        {
            if (SelectedGames.Count == 0) return false;
            var maxIndex = SelectedGames.Max(g => Games.IndexOf(g));
            return maxIndex < Games.Count - 1;
        }

        private void MoveDown()
        {
            if (SelectedGames.Count == 0) return;
            
            // Move items down from lowest index to highest to maintain grouping
            var sorted = SelectedGames.OrderBy(g => Games.IndexOf(g)).ToList();
            
            foreach (var game in sorted)
            {
                var index = Games.IndexOf(game);
                if (index < Games.Count - 1)
                    Games.Move(index, index + 1);
            }
            
            UpdateRanksAndSave();
        }

        public void Dispose()
        {
            _api.Database.Games.ItemUpdated -= OnGamesUpdated;
            _api.Database.Games.ItemCollectionChanged -= OnGamesChanged;
        }
    }

    // ── Minimal RelayCommand ──────────────────────────────────────────────────────

    public class RelayCommand : ICommand
    {
        private readonly Action _execute;
        private readonly Func<bool>? _canExecute;

        public RelayCommand(Action execute, Func<bool>? canExecute = null)
        {
            _execute = execute;
            _canExecute = canExecute;
        }

        public event EventHandler? CanExecuteChanged
        {
            add    => CommandManager.RequerySuggested += value;
            remove => CommandManager.RequerySuggested -= value;
        }

        public bool CanExecute(object? parameter) => _canExecute?.Invoke() ?? true;
        public void Execute(object? parameter) => _execute();
    }
}
