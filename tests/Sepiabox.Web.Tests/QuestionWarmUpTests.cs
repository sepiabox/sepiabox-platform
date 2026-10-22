using System.Security.Claims;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Sepiabox.Search.Questions;
using Sepiabox.Web.Services;

namespace Sepiabox.Web.Tests;

/// <summary>
/// Keeping the question format warm (issue 85, D-101): when it starts, that it runs one at a time, that it is
/// not repeated for 12 hours after a question was translated, and that it costs no one a question of the day.
/// </summary>
public sealed class QuestionWarmUpTests
{
    private static readonly DateTimeOffset s_start = new(2026, 10, 22, 9, 0, 0, TimeSpan.Zero);

    private readonly MovableTime _clock = new(s_start);

    [Fact]
    public async Task Sends_one_short_question_in_the_background_when_nothing_was_translated_yet()
    {
        var translator = new SlowTranslator();
        var warmUp = WarmUp(translator);

        Assert.True(warmUp.TryStart());
        var running = warmUp.Running!;
        translator.Release();
        await running;

        Assert.Equal([QuestionWarmUp.Question], translator.Questions);
    }

    [Fact]
    public async Task Runs_at_most_one_at_a_time()
    {
        var translator = new SlowTranslator();
        var warmUp = WarmUp(translator);

        var first = warmUp.TryStart();
        var second = warmUp.TryStart();
        var third = warmUp.TryStart();
        var running = warmUp.Running!;
        translator.Release();
        await running;

        Assert.Equal((true, false, false), (first, second, third));
        Assert.Single(translator.Questions);
    }

    [Fact]
    public async Task Waits_twelve_hours_after_a_question_was_translated()
    {
        var translator = new SlowTranslator();
        var warmUp = WarmUp(translator);
        warmUp.NoteSuccess();

        _clock.Now = s_start + QuestionWarmUp.Quiet - TimeSpan.FromMinutes(1);
        Assert.False(warmUp.TryStart());
        _clock.Now = s_start + QuestionWarmUp.Quiet;
        Assert.True(warmUp.TryStart());
        var running = warmUp.Running!;
        translator.Release();
        await running;

        Assert.Single(translator.Questions);
    }

    [Fact]
    public async Task A_successful_warm_up_keeps_the_next_twelve_hours_quiet_too()
    {
        var translator = new SlowTranslator();
        var warmUp = WarmUp(translator);
        warmUp.TryStart();
        var running = warmUp.Running!;
        translator.Release();
        await running;

        _clock.Now = s_start + TimeSpan.FromHours(11);

        Assert.False(warmUp.TryStart());
        Assert.Single(translator.Questions);
    }

    [Fact]
    public async Task Does_not_retry_for_ten_minutes_after_a_failure_and_never_throws()
    {
        var translator = new SlowTranslator { Fails = true };
        var warmUp = WarmUp(translator);

        Assert.True(warmUp.TryStart());
        var running = warmUp.Running!;
        translator.Release();
        await running;
        _clock.Now = s_start + QuestionWarmUp.RetryAfter - TimeSpan.FromSeconds(1);
        Assert.False(warmUp.TryStart());
        _clock.Now = s_start + QuestionWarmUp.RetryAfter;

        Assert.True(warmUp.TryStart());
        running = warmUp.Running!;
        translator.Release();
        await running;
        Assert.Equal(2, translator.Questions.Count);
    }

    [Theory]
    [InlineData(QuestionOutcome.NotUnderstood)]
    [InlineData(QuestionOutcome.Failed)]
    public async Task An_answer_that_cost_tokens_means_the_format_is_warm_whatever_the_outcome(QuestionOutcome outcome)
    {
        var translator = new SlowTranslator { Outcome = outcome, Usage = new TokenUsage(120, 4, 0, 0) };
        var warmUp = WarmUp(translator);
        warmUp.TryStart();
        var running = warmUp.Running!;
        translator.Release();
        await running;

        _clock.Now = s_start + TimeSpan.FromHours(1);

        Assert.False(warmUp.TryStart());
        Assert.Single(translator.Questions);
    }

    [Fact]
    public async Task An_unavailable_provider_that_cost_nothing_is_tried_again_after_ten_minutes()
    {
        var translator = new SlowTranslator { Outcome = QuestionOutcome.Unavailable, Usage = TokenUsage.None };
        var warmUp = WarmUp(translator);
        warmUp.TryStart();
        var running = warmUp.Running!;
        translator.Release();
        await running;

        _clock.Now = s_start + QuestionWarmUp.RetryAfter;

        Assert.True(warmUp.TryStart());
    }

    [Fact]
    public async Task A_translator_that_throws_is_a_failure_not_an_error()
    {
        var translator = new SlowTranslator { Throws = true };
        var warmUp = WarmUp(translator);

        Assert.True(warmUp.TryStart());
        var running = warmUp.Running!;
        translator.Release();
        await running;

        _clock.Now = s_start + TimeSpan.FromMinutes(1);
        Assert.False(warmUp.TryStart());
    }

    [Fact]
    public void Starts_nothing_without_a_key_or_when_turned_off()
    {
        var translator = new SlowTranslator { IsConfigured = false };

        Assert.False(WarmUp(translator).TryStart());
        Assert.False(new QuestionWarmUp(new SlowTranslator(), _clock, NullLogger<QuestionWarmUp>.Instance) { Enabled = false }.TryStart());
        Assert.Empty(translator.Questions);
    }

    [Fact]
    public async Task A_question_that_was_translated_keeps_it_quiet_and_the_warm_up_takes_no_question_of_the_day()
    {
        var translator = new FakeTranslator();
        var quota = new FakeQuestionQuota();
        var warmUp = WarmUp(translator);
        var questions = new QuestionService(translator, quota, new FakeLibrary(), new QuestionLimits(), _clock, warmUp: warmUp);
        var user = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Email, "ana@example.com")], "test"));

        questions.WarmUp();
        await warmUp.Running!;
        Assert.Empty(quota.Days);
        Assert.Equal(AskOutcome.Answered, (await questions.AskAsync("videos", user)).Outcome);
        questions.WarmUp();

        Assert.Equal(["photos", "videos"], translator.Calls.Select(c => c.Question));
        Assert.Equal(1, Assert.Single(quota.Days).Value.Questions);
    }

    [Fact]
    public async Task Showing_the_timeline_with_the_question_box_starts_it_once_and_a_question_is_not_counted_for_it()
    {
        var translator = new FakeTranslator();
        var library = new FakeLibrary(new TimelineItem(Ids.Of(1), AssetKinds.Photo, new DateTime(2019, 12, 24), true, null));
        await using var web = new WebFactory(library)
        {
            QuestionTranslator = translator,
            Settings = new() { ["Questions:WarmUp"] = "true" },
        };
        using var http = web.CreateSignedInClient();

        await http.GetStringAsync(new Uri("/", UriKind.Relative));
        await http.GetStringAsync(new Uri("/", UriKind.Relative));
        var running = web.Services.GetRequiredService<QuestionWarmUp>().Running;
        if (running is not null)
        {
            await running;
        }

        Assert.Equal("photos", Assert.Single(translator.Calls).Question);
    }

    private QuestionWarmUp WarmUp(IQuestionTranslator translator) => new(translator, _clock, NullLogger<QuestionWarmUp>.Instance);

    /// <summary>A translator that answers when the test says so.</summary>
    private sealed class SlowTranslator : IQuestionTranslator
    {
        private readonly TaskCompletionSource _gate = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool IsConfigured { get; init; } = true;

        public bool Fails { get; init; }

        /// <summary>The outcome and usage of an answer, unless <see cref="Fails"/> or <see cref="Throws"/>; by default a translated question.</summary>
        public QuestionOutcome Outcome { get; init; } = QuestionOutcome.Translated;

        public TokenUsage Usage { get; init; } = new(10, 5, 0, 0);

        public bool Throws { get; init; }

        public List<string> Questions { get; } = [];

        public void Release() => _gate.TrySetResult();

        public async Task<QuestionTranslation> TranslateAsync(string question, QuestionContext context, CancellationToken cancellationToken = default)
        {
            lock (Questions)
            {
                Questions.Add(question);
            }

            await _gate.Task;
            return Throws
                ? throw new InvalidOperationException("The provider broke.")
                : Fails
                    ? QuestionTranslation.Of(QuestionOutcome.Unavailable, TokenUsage.None, 0)
                    : new QuestionTranslation(Outcome, Outcome == QuestionOutcome.Translated ? """{"filters": {}}""" : null, Usage, 1);
        }
    }
}
