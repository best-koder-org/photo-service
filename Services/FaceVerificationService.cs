using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using PhotoService.Data;
using PhotoService.Models;

namespace PhotoService.Services;

/// <summary>
/// Face verification service using self-hosted DeepFace container.
/// Compares selfie against profile photo using Facenet512 model.
/// </summary>
public interface IFaceVerificationService
{
    Task<VerificationResult> VerifyAsync(int userId, Stream selfieStream, string selfieFileName);
    Task<VerificationAttempt?> GetLatestAttemptAsync(int userId);
    Task<int> GetAttemptCountTodayAsync(int userId);
}

public class FaceVerificationService : IFaceVerificationService
{
    private readonly HttpClient _httpClient;
    private readonly VerificationDbContext _verificationDb;
    private readonly PhotoContext _photoDb;
    private readonly ILogger<FaceVerificationService> _logger;
    private const string DeepFaceUrl = "http://deepface:5005";
    private const int MaxAttemptsPerDay = 3;

    // Facenet512 cosine distance threshold is 0.30 (from DeepFace source).
    // similarity = 1.0 - distance, so:
    //   distance ≤ 0.30 → similarity ≥ 0.70 → same person (verified)
    //   distance ≤ 0.40 → similarity ≥ 0.60 → borderline (pending manual review)
    //   distance > 0.40 → similarity < 0.60 → different person (rejected)
    private const double VerifiedThreshold = 0.70;
    private const double PendingThreshold = 0.60;

    public FaceVerificationService(
        IHttpClientFactory httpClientFactory,
        VerificationDbContext verificationDb,
        PhotoContext photoDb,
        ILogger<FaceVerificationService> logger)
    {
        _httpClient = httpClientFactory.CreateClient("DeepFace");
        _verificationDb = verificationDb;
        _photoDb = photoDb;
        _logger = logger;
    }

    public async Task<VerificationResult> VerifyAsync(int userId, Stream selfieStream, string selfieFileName)
    {
        // Rate limit check — only count actual Rejected attempts, not errors or pending reviews
        int todayCount = await GetAttemptCountTodayAsync(userId);
        if (todayCount >= MaxAttemptsPerDay)
        {
            return new VerificationResult(
                VerificationDecision.RateLimited,
                0,
                "Maximum 3 verification attempts per day. Try again tomorrow.",
                null);
        }

        // Get user's current primary photo
        var profilePhoto = await _photoDb.Photos
            .Where(p => p.UserId == userId && p.IsPrimary)
            .OrderByDescending(p => p.CreatedAt)
            .FirstOrDefaultAsync();

        if (profilePhoto == null)
        {
            return new VerificationResult(
                VerificationDecision.Rejected,
                0,
                "No profile photo found. Please upload a profile photo first.",
                null);
        }

        // Save selfie temporarily for DeepFace comparison
        var selfieBytes = new MemoryStream();
        await selfieStream.CopyToAsync(selfieBytes);
        var selfieBase64 = Convert.ToBase64String(selfieBytes.ToArray());

        // Call DeepFace verify endpoint
        try
        {
            var request = new DeepFaceVerifyRequest
            {
                Img1 = $"data:image/jpeg;base64,{selfieBase64}",
                Img2Path = profilePhoto.FilePath,
                ModelName = "Facenet512",
                AntiSpoofing = true
            };

            var response = await _httpClient.PostAsJsonAsync($"{DeepFaceUrl}/verify", request);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("DeepFace returned {StatusCode}", response.StatusCode);
                // Errors do NOT count against daily attempts
                return new VerificationResult(
                    VerificationDecision.Error,
                    0,
                    "Verification service temporarily unavailable. Please try again later.",
                    null);
            }

            var result = await response.Content.ReadFromJsonAsync<DeepFaceVerifyResponse>();
            double similarity = 1.0 - (result?.Distance ?? 1.0);

            // Create attempt record
            var attempt = new VerificationAttempt
            {
                UserId = userId,
                SimilarityScore = similarity,
                ProfilePhotoId = profilePhoto.Id,
                CreatedAt = DateTime.UtcNow,
                AntiSpoofingPassed = result?.FacialArea != null
            };

            if (similarity >= VerifiedThreshold)
            {
                attempt.Result = "Verified";
                attempt.Decision = VerificationDecision.Verified;
            }
            else if (similarity >= PendingThreshold)
            {
                attempt.Result = "Pending";
                attempt.Decision = VerificationDecision.PendingReview;
                attempt.RejectionReason = "Borderline similarity — queued for manual review";
                // Pending does NOT count against daily attempts (not the user's fault)
            }
            else
            {
                attempt.Result = "Rejected";
                attempt.Decision = VerificationDecision.Rejected;
                attempt.RejectionReason = "Face didn't match your profile photo. Please try again with better lighting.";
            }

            _verificationDb.VerificationAttempts.Add(attempt);
            await _verificationDb.SaveChangesAsync();

            return new VerificationResult(
                attempt.Decision,
                similarity,
                attempt.RejectionReason ?? "Verification successful! Your profile now has a blue badge.",
                attempt.Id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "DeepFace verification failed for user {UserId}", userId);
            // Exceptions do NOT count against daily attempts
            return new VerificationResult(
                VerificationDecision.Error,
                0,
                "Verification service error. Please try again later.",
                null);
        }
    }

    public async Task<VerificationAttempt?> GetLatestAttemptAsync(int userId)
    {
        return await _verificationDb.VerificationAttempts
            .Where(a => a.UserId == userId)
            .OrderByDescending(a => a.CreatedAt)
            .FirstOrDefaultAsync();
    }

    /// <summary>
    /// Count only Rejected attempts today — errors and pending reviews don't burn daily slots.
    /// </summary>
    public async Task<int> GetAttemptCountTodayAsync(int userId)
    {
        var today = DateTime.UtcNow.Date;
        return await _verificationDb.VerificationAttempts
            .CountAsync(a => a.UserId == userId
                && a.CreatedAt >= today
                && a.Result == "Rejected");
    }
}

// DeepFace request/response models
public class DeepFaceVerifyRequest
{
    [JsonPropertyName("img1")]
    public string Img1 { get; set; } = "";

    [JsonPropertyName("img2")]
    public string Img2Path { get; set; } = "";

    [JsonPropertyName("model_name")]
    public string ModelName { get; set; } = "Facenet512";

    [JsonPropertyName("anti_spoofing")]
    public bool AntiSpoofing { get; set; } = true;
}

public class DeepFaceVerifyResponse
{
    [JsonPropertyName("verified")]
    public bool Verified { get; set; }

    [JsonPropertyName("distance")]
    public double Distance { get; set; }

    [JsonPropertyName("threshold")]
    public double Threshold { get; set; }

    [JsonPropertyName("model")]
    public string Model { get; set; } = "";

    [JsonPropertyName("facial_areas")]
    public object? FacialArea { get; set; }
}

public record VerificationResult(
    VerificationDecision Decision,
    double SimilarityScore,
    string Message,
    int? AttemptId
);

public enum VerificationDecision
{
    Verified,
    PendingReview,
    Rejected,
    RateLimited,
    Error
}
