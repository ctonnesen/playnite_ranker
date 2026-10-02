using System;
using System.Collections.Generic;
using System.Windows.Controls;
using System.Windows.Media;
using GameRanker.Views;
using Playnite.SDK;
using Playnite.SDK.Plugins;

#nullable enable

namespace GameRanker
{
    public class GameRankerPlugin : GenericPlugin
    {
        private readonly IPlayniteAPI _api;
        private readonly RankStore _store;
        private RankedListViewModel? _vm;

        public override Guid Id => Guid.Parse("a1b2c3d4-e5f6-7890-abcd-ef1234567890");

        public GameRankerPlugin(IPlayniteAPI api) : base(api)
        {
            _api = api;
            _store = new RankStore(GetPluginUserDataPath(), api);
        }

        public override IEnumerable<SidebarItem> GetSidebarItems()
        {
            yield return new SidebarItem
            {
                Title = "Game Ranker",
                Icon = new TextBlock
                {
                    Text = "★",
                    FontSize = 16,
                    Foreground = Brushes.Gold,
                    VerticalAlignment = System.Windows.VerticalAlignment.Center,
                    HorizontalAlignment = System.Windows.HorizontalAlignment.Center
                },
                Type = SiderbarItemType.View,
                Opened = () =>
                {
                    _vm?.Dispose();
                    _vm = new RankedListViewModel(_api, _store);
                    return new RankedListView { DataContext = _vm };
                },
                Closed = () =>
                {
                    _vm?.Dispose();
                    _vm = null;
                }
            };
        }

        public override void Dispose()
        {
            _vm?.Dispose();
            base.Dispose();
        }
    }
}
