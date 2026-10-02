using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Playnite.SDK;
using Playnite.SDK.Models;

namespace GameRanker
{
    /// <summary>
    /// Persists rank data to ranks.json and keeps game tags in sync.
    /// Tags are written as "#1", "#2", "#3" etc.
    /// Only one rank tag exists per game at any time.
    /// </summary>
    public class RankStore
    {
        private readonly string _path;
        private readonly IPlayniteAPI _api;
        private Dictionary<Guid, int> _ranks;

        // All rank tags we own start with this prefix so we can find and wipe them.
        // "#" alone would be too broad, so we identify ours by the fact that the
        // rest of the string is a pure integer.
        private const string TagPrefix = "#";

        public RankStore(string dataFolder, IPlayniteAPI api)
        {
            _path = Path.Combine(dataFolder, "ranks.json");
            _api = api;
            _ranks = Load();
        }

        // ── Persistence ──────────────────────────────────────────────────────────────

        private Dictionary<Guid, int> Load()
        {
            if (!File.Exists(_path)) return new Dictionary<Guid, int>();
            try
            {
                var json = File.ReadAllText(_path);
                return JsonConvert.DeserializeObject<Dictionary<Guid, int>>(json)
                       ?? new Dictionary<Guid, int>();
            }
            catch { return new Dictionary<Guid, int>(); }
        }

        private void Save()
        {
            File.WriteAllText(_path, JsonConvert.SerializeObject(_ranks, Formatting.Indented));
        }

        public int GetRank(Guid gameId) =>
            _ranks.TryGetValue(gameId, out var r) ? r : int.MaxValue;

        // ── Tag helpers ──────────────────────────────────────────────────────────────

        private string TagName(int rank) => $"#{rank}";

        private bool IsRankTag(Tag t) =>
            t.Name.StartsWith(TagPrefix) &&
            int.TryParse(t.Name.Substring(TagPrefix.Length), out _);

        /// <summary>
        /// Ensures a tag named <paramref name="name"/> exists in the DB and returns its ID.
        /// </summary>
        private Guid EnsureTag(string name)
        {
            var existing = _api.Database.Tags.FirstOrDefault(t => t.Name == name);
            if (existing != null) return existing.Id;

            var tag = new Tag(name);
            _api.Database.Tags.Add(tag);
            return tag.Id;
        }

        /// <summary>
        /// Removes all rank tags from a game (strips any tag that matches IsRankTag).
        /// </summary>
        private void ClearRankTagsFromGame(Game game)
        {
            if (game.TagIds == null) return;

            var rankTagIds = _api.Database.Tags
                .Where(IsRankTag)
                .Select(t => t.Id)
                .ToHashSet();

            var before = game.TagIds.Count;
            game.TagIds.RemoveAll(id => rankTagIds.Contains(id));

            if (game.TagIds.Count != before)
                _api.Database.Games.Update(game);
        }

        /// <summary>
        /// Sets the rank tag on a single game, removing any previous rank tag first.
        /// </summary>
        private void ApplyRankTag(Game game, int rank)
        {
            game.TagIds ??= new List<Guid>();

            // Remove old rank tags
            var rankTagIds = _api.Database.Tags
                .Where(IsRankTag)
                .Select(t => t.Id)
                .ToHashSet();
            game.TagIds.RemoveAll(id => rankTagIds.Contains(id));

            // Add new rank tag
            var tagId = EnsureTag(TagName(rank));
            if (!game.TagIds.Contains(tagId))
                game.TagIds.Add(tagId);

            _api.Database.Games.Update(game);
        }

        // ── Public API ───────────────────────────────────────────────────────────────

        /// <summary>
        /// Accepts games in desired display order (index 0 = rank 1).
        /// Updates ranks.json and rewrites all affected rank tags.
        /// </summary>
        public void SetRanks(IList<RankedGame> orderedGames)
        {
            _ranks.Clear();

            using (_api.Database.BufferedUpdate())
            {
                for (int i = 0; i < orderedGames.Count; i++)
                {
                    var rg = orderedGames[i];
                    var rank = i + 1;
                    _ranks[rg.Id] = rank;

                    var game = _api.Database.Games.Get(rg.Id);
                    if (game != null)
                        ApplyRankTag(game, rank);
                }
            }

            Save();
        }

        /// <summary>
        /// Removes a single game from the rank store, clears its rank tag,
        /// then renumbers and rewrites tags for all remaining ranked games.
        /// Call this when a game leaves Plan to Play.
        /// </summary>
        public void RemoveGame(Guid gameId)
        {
            if (!_ranks.ContainsKey(gameId)) return;

            // Strip rank tag from the departing game
            var game = _api.Database.Games.Get(gameId);
            if (game != null)
                ClearRankTagsFromGame(game);

            _ranks.Remove(gameId);

            // Compact the remaining ranks (fill the gap left behind)
            var reordered = _ranks
                .OrderBy(kv => kv.Value)
                .Select(kv => kv.Key)
                .ToList();

            _ranks.Clear();

            using (_api.Database.BufferedUpdate())
            {
                for (int i = 0; i < reordered.Count; i++)
                {
                    var rank = i + 1;
                    _ranks[reordered[i]] = rank;

                    var g = _api.Database.Games.Get(reordered[i]);
                    if (g != null)
                        ApplyRankTag(g, rank);
                }
            }

            Save();
        }

        /// <summary>
        /// Removes every rank tag (#1, #2 …) from every game in the library,
        /// clears ranks.json, and deletes now-unused rank tags from the DB.
        /// </summary>
        public void WipeAllRanks()
        {
            _ranks.Clear();
            Save();

            // Strip rank tags from all games
            using (_api.Database.BufferedUpdate())
            {
                foreach (var game in _api.Database.Games)
                    ClearRankTagsFromGame(game);
            }

            // Delete orphaned rank tags from the tag DB
            var unusedRankTags = _api.Database.Tags
                .Where(t => IsRankTag(t) && !_api.Database.Games.Any(g => g.TagIds?.Contains(t.Id) == true))
                .ToList();

            foreach (var t in unusedRankTags)
                _api.Database.Tags.Remove(t);
        }
    }
}
