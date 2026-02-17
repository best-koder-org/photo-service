# Voice Prompt Moderation System

## Overview

Voice prompts go live immediately (AUTO_APPROVED) for UX speed,
then get moderated asynchronously in the background via Whisper.net.

```
User records → Upload → AUTO_APPROVED (visible) → Background worker picks up
→ Whisper transcribes audio → Text scanned for violations
→ APPROVED or REJECTED (rejected prompts auto-filtered from all queries)
```

## Architecture

### Recording (Flutter)
- **Hinge-style flow**: tap mic → recording → tap stop → auto-upload. No preview step.
- In-app recording only (no file import) to prevent deepfakes.
- AAC (.m4a), 44.1kHz, 128kbps, mono. Duration: 3–30s, max 2MB.

### Moderation Pipeline (photo-service)

| Component | File | Purpose |
|-----------|------|---------|
| Interface | `Services/IVoicePromptModerationService.cs` | ModerateAsync contract |
| Service | `Services/VoicePromptModerationService.cs` | Whisper transcription + text scanning |
| Background | `Services/VoicePromptModerationBackgroundService.cs` | Polls every 30s for AUTO_APPROVED prompts |
| Report | `Controllers/VoicePromptsController.cs` POST `/report/{userId}` | User reporting → PENDING_REVIEW |
| Entity | `Models/VoicePromptReport.cs` | Report record for trust & safety |

### Moderation Status Flow

```
AUTO_APPROVED → (background moderation) → APPROVED | REJECTED
AUTO_APPROVED → (user report) → PENDING_REVIEW → (manual review) → APPROVED | REJECTED
```

### Text Violations Detected
1. **Contact info** — phone numbers (7+ digits), email addresses, social media handles
2. **Hate speech** — blocklist with word-boundary matching
3. **Explicit content** — blocklist with word-boundary matching

### Configuration (appsettings.json)
```json
{
  "VoiceModeration": {
    "Enabled": true,
    "PollIntervalSeconds": 30,
    "WhisperModelPath": "models/ggml-base.bin"
  }
}
```

### Runtime Dependencies
- **ffmpeg** — converts m4a → 16kHz mono WAV for Whisper (must be in Docker image)
- **ggml-base.bin** — ~142MB Whisper model, auto-downloaded from HuggingFace on first moderation run

## Tests
- `PhotoService.Tests/Controllers/VoicePromptsControllerTests.cs` — 17 tests (upload validation, CRUD, report, moderation filtering)
- `PhotoService.Tests/Services/VoicePromptModerationTests.cs` — 27 tests (phone/email/social/hate/explicit detection + edge cases)
- `test/screens/voice_prompt_screen_test.dart` — 6 widget tests (idle state rendering)

Run: `dotnet test --filter "VoicePrompt"` (backend), `flutter test test/screens/voice_prompt_screen_test.dart` (Flutter)
