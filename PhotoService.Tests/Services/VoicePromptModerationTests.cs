using Xunit;
using PhotoService.Services;

namespace PhotoService.Tests.Services;

/// <summary>
/// Tests for VoicePromptModerationService.CheckForViolations (internal static).
/// Validates text-based moderation rules: phone numbers, emails, social media,
/// hate speech, explicit content. Accessible via InternalsVisibleTo.
/// </summary>
public class VoicePromptModerationTests
{
    // ──────────────── Clean Text ────────────────

    [Theory]
    [InlineData("Hello, I love hiking and coffee!")]
    [InlineData("Looking for someone who enjoys travel")]
    [InlineData("I'm a software developer who loves dogs")]
    [InlineData("")]
    public void CleanText_ReturnsNoViolations(string text)
    {
        var violations = VoicePromptModerationService.CheckForViolations(text);
        Assert.Empty(violations);
    }

    [Fact]
    public void NullText_ReturnsNoViolations()
    {
        var violations = VoicePromptModerationService.CheckForViolations(null!);
        Assert.Empty(violations);
    }

    // ──────────────── Phone Numbers ────────────────

    [Theory]
    [InlineData("Call me at 555-123-4567")]
    [InlineData("My number is +1 555 123 4567")]
    [InlineData("Text me 5551234567")]
    [InlineData("Ring 07712345678")]
    public void PhoneNumber_DetectsContactInfo(string text)
    {
        var violations = VoicePromptModerationService.CheckForViolations(text);
        Assert.Contains(violations, v => v.StartsWith("contact_info:phone"));
    }

    // ──────────────── Email Addresses ────────────────

    [Theory]
    [InlineData("Email me at john@gmail.com")]
    [InlineData("Write to hello.world@outlook.co.uk")]
    [InlineData("my email is test+dating@yahoo.com")]
    public void Email_DetectsContactInfo(string text)
    {
        var violations = VoicePromptModerationService.CheckForViolations(text);
        Assert.Contains(violations, v => v.StartsWith("contact_info:email"));
    }

    // ──────────────── Social Media ────────────────

    [Theory]
    [InlineData("Add me on Instagram")]
    [InlineData("Find me on snapchat")]
    [InlineData("My TikTok is cool")]
    [InlineData("DM me on twitter")]
    [InlineData("Message me on whatsapp")]
    [InlineData("follow @johndoe")]
    [InlineData("hit me up on telegram")]
    public void SocialMedia_DetectsContactInfo(string text)
    {
        var violations = VoicePromptModerationService.CheckForViolations(text);
        Assert.Contains(violations, v => v.StartsWith("contact_info:social_media"));
    }

    // ──────────────── Hate Speech ────────────────

    [Theory]
    [InlineData("I want to kill it at the gym")]  // false positive, but correct for blocklist
    [InlineData("bomb that test")]
    public void HateSpeech_DetectsViolation(string text)
    {
        var violations = VoicePromptModerationService.CheckForViolations(text);
        Assert.Contains(violations, v => v.StartsWith("hate_speech:"));
    }

    [Fact]
    public void HateSpeech_WordBoundary_NoPartialMatch()
    {
        // "killing" should not match "kill" because of word boundary
        // Actually \b around "kill" WILL match "kill" in "killing" because "kill" is at a word boundary start
        // Let's test a case that should NOT match:
        var violations = VoicePromptModerationService.CheckForViolations("I like bombastic music");
        Assert.DoesNotContain(violations, v => v.StartsWith("hate_speech:bomb"));
    }

    // ──────────────── Explicit Content ────────────────

    [Theory]
    [InlineData("Check out this xxx stuff")]
    [InlineData("Send me nude pics")]
    public void ExplicitContent_DetectsViolation(string text)
    {
        var violations = VoicePromptModerationService.CheckForViolations(text);
        Assert.Contains(violations, v => v.StartsWith("explicit_content:"));
    }

    // ──────────────── Multiple Violations ────────────────

    [Fact]
    public void MultipleViolations_AllDetected()
    {
        var text = "Email me at test@gmail.com and follow @johndoe on Instagram";
        var violations = VoicePromptModerationService.CheckForViolations(text);

        // Should have at least email + social media
        Assert.True(violations.Count >= 2, $"Expected >=2 violations, got {violations.Count}: {string.Join(", ", violations)}");
        Assert.Contains(violations, v => v.StartsWith("contact_info:email"));
        Assert.Contains(violations, v => v.StartsWith("contact_info:social_media"));
    }

    // ──────────────── Edge Cases ────────────────

    [Fact]
    public void WhitespaceOnly_ReturnsNoViolations()
    {
        var violations = VoicePromptModerationService.CheckForViolations("   \t\n  ");
        Assert.Empty(violations);
    }

    [Fact]
    public void CaseInsensitive_DetectsUpperCase()
    {
        var violations = VoicePromptModerationService.CheckForViolations("ADD ME ON INSTAGRAM NOW");
        Assert.Contains(violations, v => v.StartsWith("contact_info:social_media"));
    }
}
