namespace YaaJuu.SharedKernel.Domain;

public static class Guard
{
    public static string NotNullOrWhiteSpace(string? value, string name, int maxLength = 256)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new DomainException("validation.required", $"{name} is required.");
        }

        var trimmed = value.Trim();
        if (trimmed.Length > maxLength)
        {
            throw new DomainException("validation.max_length", $"{name} cannot exceed {maxLength} characters.");
        }

        return trimmed;
    }

    public static T NotNull<T>(T? value, string name)
        where T : class
    {
        if (value is null)
        {
            throw new DomainException("validation.required", $"{name} is required.");
        }

        return value;
    }
}
