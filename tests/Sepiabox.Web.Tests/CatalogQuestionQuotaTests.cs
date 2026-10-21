using Microsoft.EntityFrameworkCore;
using Sepiabox.Search.Questions;
using Sepiabox.Web.Services;

namespace Sepiabox.Web.Tests;

/// <summary>The daily question limit in the catalog (S-025), on a real PostgreSQL.</summary>
[Collection(CatalogGroup.Name)]
[Trait("Category", "Integration")]
public sealed class CatalogQuestionQuotaTests(CatalogFixture catalog)
{
    private const string Ana = "0000000000000000000000000000000000000000000000000000000000000a0a";
    private const string Bruno = "0000000000000000000000000000000000000000000000000000000000000b0b";
    private static readonly DateOnly Day = new(2026, 10, 20);

    [Fact]
    public async Task Takes_questions_up_to_the_limit_per_person_and_day()
    {
        await using var db = await catalog.CreateMigratedDatabaseAsync();
        var quota = new CatalogQuestionQuota(new TestContexts(catalog.ConnectionStringFor(db)));

        var taken = new List<bool>();
        for (var i = 0; i < 4; i++)
        {
            taken.Add(await quota.TryTakeAsync(Ana, Day, limit: 3));
        }

        Assert.Equal([true, true, true, false], taken);
        // Another person, and the next day, start again.
        Assert.True(await quota.TryTakeAsync(Bruno, Day, limit: 3));
        Assert.True(await quota.TryTakeAsync(Ana, Day.AddDays(1), limit: 3));
        Assert.Equal(3, (await db.QuestionUsage.SingleAsync(u => u.UserKey == Ana && u.Day == Day)).Questions);
    }

    [Fact]
    public async Task Never_lets_two_questions_at_once_both_take_the_last_one()
    {
        await using var db = await catalog.CreateMigratedDatabaseAsync();
        var quota = new CatalogQuestionQuota(new TestContexts(catalog.ConnectionStringFor(db)));

        var results = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => quota.TryTakeAsync(Ana, Day, limit: 5)));

        Assert.Equal(5, results.Count(r => r));
        Assert.Equal(5, (await db.QuestionUsage.SingleAsync()).Questions);
    }

    [Fact]
    public async Task Takes_nothing_with_a_limit_of_zero()
    {
        await using var db = await catalog.CreateMigratedDatabaseAsync();
        var quota = new CatalogQuestionQuota(new TestContexts(catalog.ConnectionStringFor(db)));

        Assert.False(await quota.TryTakeAsync(Ana, Day, limit: 0));
        Assert.Empty(await db.QuestionUsage.ToListAsync());
    }

    [Fact]
    public async Task Adds_up_the_tokens_of_the_day_s_questions()
    {
        await using var db = await catalog.CreateMigratedDatabaseAsync();
        var quota = new CatalogQuestionQuota(new TestContexts(catalog.ConnectionStringFor(db)));
        await quota.TryTakeAsync(Ana, Day, limit: 10);
        await quota.RecordAsync(Ana, Day, new TokenUsage(1000, 50, 900, 0));
        await quota.TryTakeAsync(Ana, Day, limit: 10);
        await quota.RecordAsync(Ana, Day, new TokenUsage(800, 40, 0, 700));

        var usage = await db.QuestionUsage.AsNoTracking().SingleAsync();

        Assert.Equal((2, 1800L, 90L, 900L, 700L), (usage.Questions, usage.InputTokens, usage.OutputTokens, usage.CacheReadInputTokens, usage.CacheCreationInputTokens));
    }
}
