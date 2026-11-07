using MongoDB.Driver;
using WeldingThozhilalarSangam.Api.Services;

namespace WeldingThozhilalarSangam.Api.Middleware;

public sealed class ErrorHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ErrorHandlingMiddleware> _logger;

    public ErrorHandlingMiddleware(RequestDelegate next, ILogger<ErrorHandlingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (DuplicateMobileException)
        {
            context.Response.StatusCode = StatusCodes.Status409Conflict;
            await context.Response.WriteAsJsonAsync(new { code = "mobile_already_registered", message = MobileNumber.DuplicateMessage });
        }
        catch (AmbiguousMobileException ex)
        {
            await WriteErrorAsync(context, StatusCodes.Status409Conflict, ex.Message);
        }
        catch (AmbiguousMemberIdException)
        {
            context.Response.StatusCode = StatusCodes.Status409Conflict;
            await context.Response.WriteAsJsonAsync(new { code = "ambiguous_member_id", message = "This Member ID requires review. Please use your original printed ID or contact the Sangam." });
        }
        catch (OtpRateLimitException)
        {
            context.Response.Headers.RetryAfter = "60";
            await WriteErrorAsync(context, StatusCodes.Status429TooManyRequests, "Please wait before requesting another OTP.");
        }
        catch (OtpDeliveryException)
        {
            await WriteErrorAsync(context, StatusCodes.Status503ServiceUnavailable, "Unable to deliver OTP. Please try again later.");
        }
        catch (MongoWriteException ex) when (ex.WriteError?.Code == 11000)
        {
            var message = ex.WriteError.Message.Contains("ux_users_phone_number", StringComparison.OrdinalIgnoreCase)
                ? "A user with this phone number already exists."
                : "A record with this value already exists.";

            await WriteErrorAsync(context, StatusCodes.Status409Conflict, message);
        }
        catch (Exception ex)
        {
            _logger.LogError("Unhandled API error ({ExceptionType}); trace {TraceId}.", ex.GetType().Name, context.TraceIdentifier);
            await WriteErrorAsync(context, StatusCodes.Status500InternalServerError, "Something went wrong. Please try again later.");
        }
    }

    private static async Task WriteErrorAsync(HttpContext context, int statusCode, string message)
    {
        if (context.Response.HasStarted)
        {
            return;
        }

        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsJsonAsync(new { message });
    }
}
