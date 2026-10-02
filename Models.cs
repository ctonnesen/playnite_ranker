using System;
using System.Collections.Generic;
using System.Windows.Media.Imaging;
using Playnite.SDK.Models;

#nullable enable

namespace GameRanker
{
    public class RankedGame : ObservableObject
    {
        private int _rank;
        private bool _isSelected;

        public Game Game { get; }
        public Guid Id => Game.Id;
        public string Name => Game.Name;

        public int Rank
        {
            get => _rank;
            set => SetValue(ref _rank, value);
        }

        private BitmapImage? _cover;
        public BitmapImage? Cover
        {
            get => _cover;
            set => SetValue(ref _cover, value);
        }

        public bool IsSelected
        {
            get => _isSelected;
            set => SetValue(ref _isSelected, value);
        }

        public RankedGame(Game game, int rank)
        {
            Game = game;
            _rank = rank;
            _isSelected = false;
        }
    }
}
