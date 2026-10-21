using Sepiabox.Catalog.People;

namespace Sepiabox.Web.Tests;

/// <summary>How a person's name is stored (S-020, D-079), in the web, which has ICU for the composition.</summary>
public sealed class PersonNameTests
{
    [Theory]
    [InlineData("  Ana   Maria  ", "Ana Maria")]
    [InlineData("Ana\tMaria\nSilva", "Ana Maria Silva")]
    [InlineData("A​na﻿", "Ana")]
    [InlineData("José", "José")]
    [InlineData("   ", null)]
    [InlineData("​", null)]
    [InlineData(null, null)]
    public void Stores_names_composed_without_invisible_characters_and_with_single_spaces(string? typed, string? stored)
    {
        Assert.Null(PeopleCuration.CleanName(typed, out var clean));
        Assert.Equal(stored, clean);
    }

    [Theory]
    [InlineData("Ana\u0007")]
    [InlineData("Ana\u0000Maria")]
    [InlineData("\u001BAna")]
    public void Refuses_control_characters(string typed)
    {
        Assert.Equal(CurationOutcome.NameInvalid, PeopleCuration.CleanName(typed, out _));
    }

    [Fact]
    public void Refuses_a_name_longer_than_the_limit_after_cleaning()
    {
        Assert.Null(PeopleCuration.CleanName(new string('a', PeopleCuration.MaxNameLength) + "    ", out _));
        Assert.Equal(CurationOutcome.NameTooLong, PeopleCuration.CleanName(new string('a', PeopleCuration.MaxNameLength + 1), out _));
    }
}
