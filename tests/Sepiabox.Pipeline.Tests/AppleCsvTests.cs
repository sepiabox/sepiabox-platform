using Sepiabox.Pipeline.Exports;

namespace Sepiabox.Pipeline.Tests;

/// <summary>The CSV reader for Apple data copies (S-047, D-109): quoting, line endings, byte order mark.</summary>
public sealed class AppleCsvTests
{
    [Fact]
    public void Splits_rows_and_fields_on_any_line_ending_and_ignores_a_byte_order_mark()
    {
        var rows = AppleCsv.Parse("﻿a,b\r\nc,d\ne,f\r\n\r\n");

        Assert.Equal([["a", "b"], ["c", "d"], ["e", "f"]], rows);
    }

    [Fact]
    public void Keeps_commas_doubled_quotes_and_line_breaks_inside_quoted_fields()
    {
        var rows = AppleCsv.Parse("\"IMG, 1.jpg\",\"say \"\"hi\"\"\",\"two\nlines\"\n");

        var row = Assert.Single(rows);
        Assert.Equal(["IMG, 1.jpg", "say \"hi\"", "two\nlines"], row);
    }

    [Fact]
    public void Keeps_empty_fields_and_a_last_row_with_no_line_ending()
    {
        var rows = AppleCsv.Parse("a,,c\n,,");

        Assert.Equal([["a", "", "c"], ["", "", ""]], rows);
    }

    [Fact]
    public void Does_not_throw_on_an_unclosed_quote_or_empty_text()
    {
        Assert.Empty(AppleCsv.Parse(string.Empty));
        Assert.Equal([["a", "b,c\nd"]], AppleCsv.Parse("a,\"b,c\nd"));
    }
}
