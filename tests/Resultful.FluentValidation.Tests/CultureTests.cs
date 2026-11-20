using System.Globalization;
using FluentValidation;

namespace Resultful.FluentValidation.Tests;

/// <summary>Messages are FluentValidation's, in the UI culture the validator ran under; nothing is re-cased or re-formatted.</summary>
public sealed class CultureTests
{
    sealed class TitleValidator : AbstractValidator<Item>
    {
        public TitleValidator() => RuleFor(i => i.Title).NotEmpty();
    }

    public sealed record Item(string Title);

    static T Under<T>(string culture, Func<T> run)
    {
        var (ui, current) = (CultureInfo.CurrentUICulture, CultureInfo.CurrentCulture);
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
            return run();
        }
        finally
        {
            (CultureInfo.CurrentUICulture, CultureInfo.CurrentCulture) = (ui, current);
        }
    }

    [Fact]
    public void A_message_is_in_the_ui_culture_the_validator_ran_under()
    {
        var item = new Item("");
        var english = Under("en-US", () => new TitleValidator().ValidateToResult(item)).Errors[0].Message;
        var turkishRaw = Under("tr-TR", () => new TitleValidator().Validate(item)).Errors[0].ErrorMessage;

        var turkish = Under("tr-TR", () => new TitleValidator().ValidateToResult(item)).Errors[0];

        Assert.Equal(turkishRaw, turkish.Message);
        Assert.NotEqual(english, turkish.Message);
        Assert.Equal("Title", ((FluentValidationError)turkish).PropertyName);
        Assert.Equal("NotEmptyValidator", ((FluentValidationError)turkish).ErrorCode);
    }

    [Fact]
    public void The_async_path_keeps_the_ui_culture()
    {
        var item = new Item("");
        var turkishRaw = Under("tr-TR", () => new TitleValidator().Validate(item)).Errors[0].ErrorMessage;

        var turkish = Under("tr-TR", () => new TitleValidator().ValidateToResultAsync(item, CancellationToken.None).GetAwaiter().GetResult()).Errors[0];

        Assert.Equal(turkishRaw, turkish.Message);
    }
}
