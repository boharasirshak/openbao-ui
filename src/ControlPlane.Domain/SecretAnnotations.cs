namespace ControlPlane.Domain;

/// <summary>
/// Everything a person writes *about* a secret, as opposed to its values. Stored in
/// OpenBao's custom_metadata, which is a flat string map — see <see cref="AnnotationCodec"/>.
/// </summary>
public sealed record SecretAnnotations(
    string? Description = null,
    IReadOnlyList<string>? Tags = null,
    string? Comment = null)
{
    public static readonly SecretAnnotations Empty = new();

    public bool IsEmpty => Description is null && Tags is null && Comment is null;
}

/// <summary>KV-v2 retention. Null means "leave whatever is configured alone".</summary>
public sealed record SecretRetention(int? MaxVersions = null, TimeSpan? DeleteVersionAfter = null)
{
    public bool IsEmpty => MaxVersions is null && DeleteVersionAfter is null;
}

public sealed record SecretMetadata(
    SecretAnnotations Annotations,
    SecretRetention Retention,
    int CurrentVersion,
    DateTimeOffset? UpdatedAt,
    IReadOnlyList<SecretVersion> Versions);

/// <summary>
/// custom_metadata is map[string]string, so structured annotations have to be encoded.
/// Tags are comma-joined and restricted to a charset that cannot contain the separator,
/// which keeps decoding unambiguous. A schema marker is written so the encoding can
/// change later without guessing at what old data means.
/// </summary>
public static class AnnotationCodec
{
    public const string DescriptionKey = "description";
    public const string TagsKey = "tags";
    public const string CommentKey = "comment";
    public const string SchemaKey = "_v";
    public const string SchemaVersion = "1";

    /// <summary>OpenBao caps a custom_metadata value at 512 bytes.</summary>
    public const int MaxValueLength = 512;

    public static bool IsValidTag(string? tag) =>
        !string.IsNullOrEmpty(tag)
        && tag.Length <= 64
        && tag.All(character => (character >= 'a' && character <= 'z')
            || (character >= '0' && character <= '9')
            || character == '-');

    public static SecretAnnotations Decode(IReadOnlyDictionary<string, string>? customMetadata)
    {
        if (customMetadata is null || customMetadata.Count == 0)
        {
            return SecretAnnotations.Empty;
        }

        customMetadata.TryGetValue(DescriptionKey, out var description);
        customMetadata.TryGetValue(CommentKey, out var comment);
        var tags = customMetadata.TryGetValue(TagsKey, out var joined) && joined.Length > 0
            ? joined.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            : [];

        return new SecretAnnotations(
            string.IsNullOrEmpty(description) ? null : description,
            tags.Length == 0 ? null : tags,
            string.IsNullOrEmpty(comment) ? null : comment);
    }

    /// <summary>
    /// Only the supplied fields are returned, because the caller merge-patches them.
    /// An empty string clears a field, which is how a description is removed.
    /// </summary>
    public static Dictionary<string, string> Encode(SecretAnnotations annotations)
    {
        var encoded = new Dictionary<string, string> { [SchemaKey] = SchemaVersion };

        if (annotations.Description is not null)
        {
            encoded[DescriptionKey] = Require(annotations.Description, DescriptionKey);
        }

        if (annotations.Comment is not null)
        {
            encoded[CommentKey] = Require(annotations.Comment, CommentKey);
        }

        if (annotations.Tags is not null)
        {
            var invalid = annotations.Tags.FirstOrDefault(tag => !IsValidTag(tag));
            if (invalid is not null)
            {
                throw new ArgumentException(
                    $"\"{invalid}\" is not a valid tag. Use lowercase letters, digits and dashes.",
                    nameof(annotations));
            }

            encoded[TagsKey] = Require(string.Join(',', annotations.Tags), TagsKey);
        }

        return encoded;
    }

    private static string Require(string value, string field)
    {
        // Reject rather than truncate: silently losing half a comment is worse than
        // being told it is too long.
        if (System.Text.Encoding.UTF8.GetByteCount(value) > MaxValueLength)
        {
            throw new ArgumentException($"The {field} is longer than {MaxValueLength} bytes.", field);
        }

        return value;
    }
}
