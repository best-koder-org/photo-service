using PhotoService.Common;

namespace PhotoService.Tests.Common;

public class ResultTests
{
    [Fact]
    public void GenericResult_Success_HasValue()
    {
        var result = Result<int>.Success(42);
        Assert.True(result.IsSuccess);
        Assert.False(result.IsFailure);
        Assert.Equal(42, result.Value);
        Assert.Null(result.Error);
    }

    [Fact]
    public void GenericResult_Failure_HasError()
    {
        var result = Result<int>.Failure("Something went wrong");
        Assert.False(result.IsSuccess);
        Assert.True(result.IsFailure);
        Assert.Equal("Something went wrong", result.Error);
    }

    [Fact]
    public void GenericResult_Success_WithStringValue()
    {
        var result = Result<string>.Success("hello");
        Assert.True(result.IsSuccess);
        Assert.Equal("hello", result.Value);
    }

    [Fact]
    public void VoidResult_Success()
    {
        var result = Result.Success();
        Assert.True(result.IsSuccess);
        Assert.False(result.IsFailure);
        Assert.Null(result.Error);
    }

    [Fact]
    public void VoidResult_Failure()
    {
        var result = Result.Failure("Operation failed");
        Assert.False(result.IsSuccess);
        Assert.True(result.IsFailure);
        Assert.Equal("Operation failed", result.Error);
    }

    [Fact]
    public void VoidResult_Failure_EmptyMessage()
    {
        var result = Result.Failure("");
        Assert.True(result.IsFailure);
        Assert.Equal("", result.Error);
    }
}
