using WinTools.Tools.Text;
using Xunit;

namespace WinTools.Tools.Tests;

public class TextDifferTests
{
    [Fact]
    public void Compare_Identical_AllEqual()
    {
        var text = "line1\nline2\nline3";
        var result = TextDiffer.Compare(text, text);
        Assert.True(result.Success());
        Assert.Equal(0, result.AddedCount);
        Assert.Equal(0, result.RemovedCount);
        Assert.Equal(3, result.Lines.Count);
        Assert.All(result.Lines, l => Assert.Equal(DiffLineKind.Equal, l.Kind));
    }

    [Fact]
    public void Compare_InsertedLine_MarksAdded()
    {
        var result = TextDiffer.Compare("a\nc", "a\nb\nc");
        Assert.Equal(1, result.AddedCount);
        Assert.Equal(0, result.RemovedCount);
        var added = Assert.Single(result.Lines, l => l.Kind == DiffLineKind.Added);
        Assert.Equal("b", added.Text);
        Assert.Equal(0, added.OldLine);
        Assert.Equal(2, added.NewLine);
    }

    [Fact]
    public void Compare_DeletedLine_MarksRemoved()
    {
        var result = TextDiffer.Compare("a\nb\nc", "a\nc");
        Assert.Equal(0, result.AddedCount);
        Assert.Equal(1, result.RemovedCount);
        var removed = Assert.Single(result.Lines, l => l.Kind == DiffLineKind.Removed);
        Assert.Equal("b", removed.Text);
        Assert.Equal(2, removed.OldLine);
        Assert.Equal(0, removed.NewLine);
    }

    [Fact]
    public void Compare_ModifiedLine_MarksRemovedThenAdded()
    {
        var result = TextDiffer.Compare("hello", "world");
        Assert.Equal(1, result.AddedCount);
        Assert.Equal(1, result.RemovedCount);
        Assert.Equal(DiffLineKind.Removed, result.Lines[0].Kind);
        Assert.Equal("hello", result.Lines[0].Text);
        Assert.Equal(DiffLineKind.Added, result.Lines[1].Kind);
        Assert.Equal("world", result.Lines[1].Text);
    }

    [Fact]
    public void Compare_EmptySides_Works()
    {
        var toEmpty = TextDiffer.Compare("a\nb", "");
        Assert.Equal(0, toEmpty.AddedCount);
        Assert.Equal(2, toEmpty.RemovedCount);

        var fromEmpty = TextDiffer.Compare("", "a\nb");
        Assert.Equal(2, fromEmpty.AddedCount);
        Assert.Equal(0, fromEmpty.RemovedCount);
    }

    [Fact]
    public void Compare_WindowsLineEndings_Normalized()
    {
        var result = TextDiffer.Compare("a\r\nb\r\n", "a\nb");
        Assert.Equal(0, result.AddedCount);
        Assert.Equal(0, result.RemovedCount);
        Assert.Equal(2, result.Lines.Count);
    }

    [Fact]
    public void Compare_TrailingNewline_NoExtraEmptyLine()
    {
        var result = TextDiffer.Compare("a\nb\n", "a\nb");
        Assert.Equal(2, result.Lines.Count);
        Assert.Equal(0, result.AddedCount + result.RemovedCount);
    }

    [Fact]
    public void Compare_LargeInput_TruncatedAndCapped()
    {
        var big1 = string.Join("\n", Enumerable.Range(0, TextDiffer.MaxLines + 10).Select(i => $"a{i}"));
        var big2 = string.Join("\n", Enumerable.Range(0, TextDiffer.MaxLines + 10).Select(i => $"a{i}"));
        var result = TextDiffer.Compare(big1, big2);
        Assert.True(result.Truncated);
        Assert.Equal(TextDiffer.MaxLines, result.Lines.Count);
        Assert.Null(result.ErrorMessage);
    }
}

internal static class DiffResultExtensions
{
    public static bool Success(this DiffResult r) => r.ErrorMessage is null;
}
