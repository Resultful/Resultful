using System.Collections;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Text.Json;

namespace Matchmark.Validation;

/// <summary>
/// JSON Schema's <c>multipleOf</c>: the number divided by the step is a whole number. Compared in decimal, so 0.3 is a
/// multiple of 0.1. A null value is valid, as for .NET's own attributes.
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Parameter)]
public sealed class MultipleOfAttribute : ValidationAttribute
{
    public MultipleOfAttribute(double step)
        : base(() => "The field {0} must be a multiple of {1}.")
        => Step = step;

    public double Step { get; }

    public override bool IsValid(object? value)
    {
        if (value is null)
        {
            return true;
        }

        try
        {
            var number = Convert.ToDecimal(value, CultureInfo.InvariantCulture);
            var step = Convert.ToDecimal(Step);
            return step != 0 && number % step == 0;
        }
        catch (Exception e) when (e is OverflowException or InvalidCastException or FormatException)
        {
            // Too big for decimal, or not a number: in doubles, within rounding.
            var quotient = Convert.ToDouble(value, CultureInfo.InvariantCulture) / Step;
            return Math.Abs(quotient - Math.Round(quotient)) < 1e-9;
        }
    }

    public override string FormatErrorMessage(string name) => string.Format(CultureInfo.CurrentCulture, ErrorMessageString, name, Step.ToString(CultureInfo.InvariantCulture));
}

/// <summary>
/// JSON Schema's <c>uniqueItems</c>: no two items are equal as JSON, so two records with the same values are the same
/// item, as the spec means it. A null value is valid.
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Parameter)]
public sealed class UniqueItemsAttribute : ValidationAttribute
{
    public UniqueItemsAttribute()
        : base(() => "The field {0} must not repeat an item.")
    {
    }

    public override bool IsValid(object? value)
    {
        if (value is not IEnumerable items || value is string)
        {
            return true;
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in items)
        {
            if (!seen.Add(JsonSerializer.Serialize(item)))
            {
                return false;
            }
        }

        return true;
    }
}

/// <summary>
/// JSON Schema's <c>minProperties</c> and <c>maxProperties</c> on a map (a dictionary): how many entries it has. Use -1
/// for no bound. A null value is valid.
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Parameter)]
public sealed class PropertyCountAttribute : ValidationAttribute
{
    public PropertyCountAttribute(int minimum, int maximum)
        : base(() => "The field {0} must have between {1} and {2} entries.")
        => (Minimum, Maximum) = (minimum, maximum);

    public int Minimum { get; }

    public int Maximum { get; }

    public override bool IsValid(object? value)
    {
        if (value is not ICollection entries)
        {
            return true;
        }

        return (Minimum < 0 || entries.Count >= Minimum) && (Maximum < 0 || entries.Count <= Maximum);
    }

    public override string FormatErrorMessage(string name)
        => string.Format(CultureInfo.CurrentCulture, ErrorMessageString, name, Minimum < 0 ? "0" : Minimum.ToString(CultureInfo.InvariantCulture),
            Maximum < 0 ? "any number of" : Maximum.ToString(CultureInfo.InvariantCulture));
}
