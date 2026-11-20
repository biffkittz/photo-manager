using System.Security.Cryptography;
using PhotoManager.Core.Crypto;
using PhotoManager.Infrastructure.Gemini;
using PhotoManager.Infrastructure.Imaging;
using PhotoManager.Infrastructure.Storage;
using Temporalio.Exceptions;

namespace PhotoManager.Worker.Activities;

/// <summary>
/// Classification of activity failures. Permanent failures become non-retryable
/// <see cref="ApplicationFailureException"/>s with a stable error type, so Temporal stops retrying
/// immediately (Temporal best practice: put 4xx-style errors in NonRetryableErrorTypes). Everything else
/// (network, throttling, transient storage errors) is left alone and retried with backoff.
/// </summary>
internal static class ActivityErrors
{
    public const string UnsupportedImage = "UnsupportedImage";
    public const string DecryptionFailed = "DecryptionFailed";
    public const string KeyUnavailable = "KeyUnavailable";
    public const string NotFound = "NotFound";
    public const string DescriptionRejected = "DescriptionRejected";
    public const string InvalidState = "InvalidState";
    public const string TooLarge = "TooLarge";

    /// <summary>Gets error types that must never be retried. Shared by every retry policy.</summary>
    public static IReadOnlyCollection<string> PermanentTypes { get; } =
        [UnsupportedImage, DecryptionFailed, KeyUnavailable, NotFound, DescriptionRejected, InvalidState, TooLarge];

    /// <summary>Creates a non-retryable failure.</summary>
    public static ApplicationFailureException Permanent(string errorType, string message, Exception? inner = null) =>
        new(message, inner, errorType, nonRetryable: true);

    /// <summary>
    /// Maps known exceptions to Temporal failures. Returns false for unknown (assumed transient) exceptions and
    /// for exceptions that already are Temporal failures, which then propagate unchanged.
    /// </summary>
    public static bool TryTranslate(Exception exception, out Exception failure)
    {
        failure = exception switch
        {
            UnsupportedImageException => Permanent(UnsupportedImage, exception.Message, exception),
            PhotoDecryptionException or CryptographicException => Permanent(DecryptionFailed, "The file could not be decrypted; it may be corrupted.", exception),
            PhotoKeyUnavailableException => Permanent(KeyUnavailable, "The key required for this file is not configured.", exception),
            StorageObjectNotFoundException => Permanent(NotFound, "The stored file could not be found.", exception),
            PhotoDescriptionRejectedException => Permanent(DescriptionRejected, exception.Message, exception),

            // Honour the server's Retry-After instead of the policy's backoff for rate limiting.
            GeminiTransientException { RetryAfter: { } delay } => new ApplicationFailureException(exception.Message, exception, "GeminiTransient", nonRetryable: false, nextRetryDelay: delay),
            _ => exception,
        };

        return !ReferenceEquals(failure, exception);
    }

    /// <summary>True when an activity failed with one of the <see cref="PermanentTypes"/>.</summary>
    public static bool IsPermanent(ActivityFailureException exception) =>
        exception.InnerException is ApplicationFailureException { ErrorType: { } type } && PermanentTypes.Contains(type);

    /// <summary>True when an activity failed with the given error type.</summary>
    public static bool Is(ActivityFailureException exception, string errorType) =>
        exception.InnerException is ApplicationFailureException { ErrorType: { } type } && string.Equals(type, errorType, StringComparison.Ordinal);

    /// <summary>
    /// Returns a message that is safe to show users and store in the database. Raw exception text is never
    /// surfaced; only our own fixed messages (and image validation messages written for users) are.
    /// </summary>
    public static string UserMessage(Exception exception)
    {
        var applicationFailure = exception as ApplicationFailureException ?? exception.InnerException as ApplicationFailureException;
        return applicationFailure?.ErrorType switch
        {
            UnsupportedImage or DescriptionRejected or TooLarge => applicationFailure.Message,
            DecryptionFailed => "The file could not be decrypted; it may be corrupted.",
            KeyUnavailable => "The key required for this file is not configured.",
            NotFound => "The stored file could not be found.",
            InvalidState => "The photo was not in a state that allows this operation.",
            _ => "Processing failed after several attempts. Try again later.",
        };
    }
}
