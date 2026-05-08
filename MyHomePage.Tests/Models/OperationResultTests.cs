namespace MyHomePage.Tests.Models;

[TestFixture]
public class OperationResultTests
{
    [Test]
    public void Success_NoMessage_CreatesSuccessResult()
    {
        // Act
        var result = OperationResult.Success();

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.Message, Is.EqualTo("Operation completed successfully."));
        });
    }

    [Test]
    public void Success_WithMessage_CreatesSuccessResultWithMessage()
    {
        // Act
        var result = OperationResult.Success("Operation completed successfully");

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.Message, Is.EqualTo("Operation completed successfully"));
        });
    }

    [Test]
    public void Failure_WithMessage_CreatesFailureResult()
    {
        // Act
        var result = OperationResult.Failure("Operation failed");

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Message, Is.EqualTo("Operation failed"));
        });
    }

    [TestCase("")]
    [TestCase(null)]
    public void Failure_WithEmptyOrNullMessage_StillCreatesFailure(string? message)
    {
        // Act
        var result = OperationResult.Failure(message);

        // Assert
        Assert.That(result.IsSuccess, Is.False);
    }
}

[TestFixture]
public class OperationResultGenericTests
{
    [Test]
    public void Success_WithData_CreatesSuccessResultWithData()
    {
        // Act
        var result = OperationResult<int>.Success(42, "Video uploaded");

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.Value, Is.EqualTo(42));
            Assert.That(result.Message, Is.EqualTo("Video uploaded"));
        });
    }

    [Test]
    public void Success_WithoutMessage_CreatesSuccessWithDefaultMessage()
    {
        // Act
        var result = OperationResult<int>.Success(42);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.Value, Is.EqualTo(42));
            Assert.That(result.Message, Is.EqualTo("Operation completed successfully."));
        });
    }

    [Test]
    public void Failure_WithMessage_CreatesFailureResult()
    {
        // Act
        var result = OperationResult<int>.Failure("Video not found");

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Message, Is.EqualTo("Video not found"));
            Assert.That(result.Value, Is.EqualTo(default(int)));
        });
    }

    [Test]
    public void Success_WithComplexData_MaintainsData()
    {
        // Arrange
        var complexData = new { Id = 123, Name = "Test", Active = true };

        // Act - use string as proxy for complex type
        var result = OperationResult<string>.Success(
            complexData.ToString()!,
            "Complex data stored"
        );

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.Value, Is.Not.Null);
            Assert.That(result.Message, Is.EqualTo("Complex data stored"));
        });
    }
}
