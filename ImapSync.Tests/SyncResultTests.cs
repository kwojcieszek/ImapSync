using ImapSync.Core.Models;

namespace ImapSync.Tests;

public class SyncResultTests
{
    [Fact]
    public void Success_IsTrue_WhenErrorsListIsEmpty()
    {
        var result = new SyncResult();

        Assert.True(result.Success);
    }

    [Fact]
    public void Success_IsFalse_WhenErrorsListHasEntries()
    {
        var result = new SyncResult();
        result.Errors.Add("Something went wrong");

        Assert.False(result.Success);
    }

    [Fact]
    public void Success_IsFalse_AfterMultipleErrors()
    {
        var result = new SyncResult();
        result.Errors.Add("Error 1");
        result.Errors.Add("Error 2");

        Assert.False(result.Success);
        Assert.Equal(2, result.Errors.Count);
    }

    [Fact]
    public void DefaultValues_AreZeroAndEmpty()
    {
        var result = new SyncResult();

        Assert.Equal(0, result.MessagesChecked);
        Assert.Equal(0, result.MessagesCopied);
        Assert.Equal(0, result.MessagesSkipped);
        Assert.Equal(0, result.FoldersCreated);
        Assert.Empty(result.Errors);
        Assert.Equal(string.Empty, result.MailboxPairName);
    }

    [Fact]
    public void Success_BecomesTrue_AfterClearingErrors()
    {
        var result = new SyncResult();
        result.Errors.Add("Transient error");

        result.Errors.Clear();

        Assert.True(result.Success);
    }
}
