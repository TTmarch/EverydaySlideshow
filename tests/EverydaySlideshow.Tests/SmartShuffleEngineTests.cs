using EverydaySlideshow.Core;

namespace EverydaySlideshow.Tests;

public sealed class SmartShuffleEngineTests
{
    [Fact]
    public void BuildQueue_contains_each_item_once_before_repeating()
    {
        using var temp = new TempDirectory();
        var items = Enumerable.Range(0, 18)
            .Select(index =>
            {
                var folder = $"trip-{index % 3}";
                var path = temp.CreateFile(System.IO.Path.Combine(folder, $"IMG_202501{index + 1:D2}_{index:D4}.jpg"));
                return TestData.Media(
                    path,
                    folderId: folder,
                    folderName: folder,
                    favorite: index % 7 == 0,
                    modifiedUtc: DateTimeOffset.UtcNow.AddDays(-index * 10),
                    lastViewedUtc: index % 2 == 0 ? DateTimeOffset.UtcNow.AddDays(-index) : null);
            })
            .ToList();

        var engine = new SmartShuffleEngine(seed: 42);
        ShuffleState? state = null;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < items.Count; i++)
        {
            var pick = engine.PickNext(items, state, new SmartShuffleOptions { RecentWindow = 8 }, "all");
            Assert.NotNull(pick.Item);
            Assert.True(seen.Add(pick.Item!.Path));
            state = pick.State;
        }

        Assert.Equal(items.Count, seen.Count);
        Assert.Empty(state!.RemainingPaths);

        var nextRound = engine.PickNext(items, state, new SmartShuffleOptions { RecentWindow = 8 }, "all");
        Assert.NotNull(nextRound.Item);
        Assert.Contains(nextRound.Item!.Path, seen);
    }

    [Fact]
    public void PickNext_continues_from_saved_shuffle_state()
    {
        using var temp = new TempDirectory();
        var items = Enumerable.Range(0, 6)
            .Select(index => TestData.Media(temp.CreateFile($"family\\p{index}.jpg"), folderName: "family"))
            .ToList();
        var engine = new SmartShuffleEngine(seed: 7);

        var first = engine.PickNext(items, null, queueKey: "family");
        var saved = first.State;
        var second = new SmartShuffleEngine(seed: 999).PickNext(items, saved, queueKey: "family");

        Assert.NotNull(first.Item);
        Assert.NotNull(second.Item);
        Assert.NotEqual(first.Item!.Path, second.Item!.Path);
        Assert.DoesNotContain(first.Item.Path, second.State.RemainingPaths, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildQueue_reduces_adjacent_items_from_same_folder_when_possible()
    {
        using var temp = new TempDirectory();
        var items = new[]
        {
            TestData.Media(temp.CreateFile("a\\a1.jpg"), folderName: "a"),
            TestData.Media(temp.CreateFile("a\\a2.jpg"), folderName: "a"),
            TestData.Media(temp.CreateFile("a\\a3.jpg"), folderName: "a"),
            TestData.Media(temp.CreateFile("b\\b1.jpg"), folderName: "b"),
            TestData.Media(temp.CreateFile("b\\b2.jpg"), folderName: "b"),
            TestData.Media(temp.CreateFile("c\\c1.jpg"), folderName: "c")
        };

        var queue = new SmartShuffleEngine(seed: 3).BuildQueue(items, []);
        var byPath = items.ToDictionary(item => item.Path, StringComparer.OrdinalIgnoreCase);
        var adjacentSameFolder = queue.Zip(queue.Skip(1))
            .Count(pair => byPath[pair.First].FolderName == byPath[pair.Second].FolderName);

        Assert.True(adjacentSameFolder <= 2);
    }
}
