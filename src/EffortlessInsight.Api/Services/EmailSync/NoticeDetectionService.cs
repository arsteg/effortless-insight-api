using System.Text.RegularExpressions;
using EffortlessInsight.Api.Options;
using Microsoft.Extensions.Options;

namespace EffortlessInsight.Api.Services.EmailSync;

/// <summary>
/// Service for detecting GST notices in emails with minimal AI usage.
/// Uses a layered detection approach: sender -> keyword -> GSTIN -> AI fallback.
/// </summary>
public class NoticeDetectionService : INoticeDetectionService
{
    private readonly EmailSyncOptions _options;
    private readonly ILogger<NoticeDetectionService> _logger;

    // GSTIN pattern: 2 digits + 10 chars (PAN) + 1 digit + 1 alphanumeric + 1 alphanumeric (checksum)
    private static readonly Regex GstinPattern = new(
        @"\b([0-9]{2}[A-Z]{5}[0-9]{4}[A-Z][A-Z0-9][Z][A-Z0-9])\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // Notice type patterns
    private static readonly Dictionary<string, Regex> NoticeTypePatterns = new()
    {
        ["DRC-01"] = new Regex(@"\bDRC[-\s]?01[A]?\b", RegexOptions.Compiled | RegexOptions.IgnoreCase),
        ["DRC-03"] = new Regex(@"\bDRC[-\s]?03\b", RegexOptions.Compiled | RegexOptions.IgnoreCase),
        ["DRC-07"] = new Regex(@"\bDRC[-\s]?07\b", RegexOptions.Compiled | RegexOptions.IgnoreCase),
        ["ASMT-10"] = new Regex(@"\bASMT[-\s]?10\b", RegexOptions.Compiled | RegexOptions.IgnoreCase),
        ["ASMT-12"] = new Regex(@"\bASMT[-\s]?12\b", RegexOptions.Compiled | RegexOptions.IgnoreCase),
        ["ASMT-14"] = new Regex(@"\bASMT[-\s]?14\b", RegexOptions.Compiled | RegexOptions.IgnoreCase),
        ["ASMT-16"] = new Regex(@"\bASMT[-\s]?16\b", RegexOptions.Compiled | RegexOptions.IgnoreCase),
        ["REG-17"] = new Regex(@"\bREG[-\s]?17\b", RegexOptions.Compiled | RegexOptions.IgnoreCase),
        ["REG-18"] = new Regex(@"\bREG[-\s]?18\b", RegexOptions.Compiled | RegexOptions.IgnoreCase),
        ["REG-19"] = new Regex(@"\bREG[-\s]?19\b", RegexOptions.Compiled | RegexOptions.IgnoreCase),
        ["REG-21"] = new Regex(@"\bREG[-\s]?21\b", RegexOptions.Compiled | RegexOptions.IgnoreCase),
    };

    public NoticeDetectionService(
        IOptions<EmailSyncOptions> options,
        ILogger<NoticeDetectionService> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public Task<NoticeDetectionResult> DetectNoticeAsync(
        EmailMessage email,
        CancellationToken cancellationToken = default)
    {
        var senderResult = CheckSender(email.Sender);
        if (!EmailSenderPolicy.IsAllowed(email.Sender, _options.AllowedSenders))
            return Task.FromResult(new NoticeDetectionResult { Reason = "Sender is not allowed" });

        // Layer 2: Check for notice keywords
        var content = $"{email.Subject} {email.BodyText ?? ""} {email.BodyHtml ?? ""}";
        var keywordResult = CheckKeywords(content);
        if (keywordResult.IsNotice && keywordResult.Confidence >= _options.AiConfidenceThreshold)
        {
            _logger.LogDebug("Notice detected by keyword: {Type}", keywordResult.DetectedNoticeType);
            return Task.FromResult(EnrichWithGstinAndType(keywordResult, email));
        }

        // Layer 3: Check for GSTIN + sender domain combination
        var gstinResult = CheckGstinAndSender(content, email.Sender);
        if (gstinResult.IsNotice && gstinResult.Confidence >= _options.AiConfidenceThreshold)
        {
            _logger.LogDebug("Notice detected by GSTIN + sender");
            return Task.FromResult(EnrichWithAttachments(gstinResult, email));
        }

        // Layer 4: If any indicators present but below threshold, combine scores
        var combinedConfidence = CombineConfidence(senderResult, keywordResult, gstinResult);
        if (combinedConfidence >= _options.AiConfidenceThreshold)
        {
            var combinedResult = new NoticeDetectionResult
            {
                IsNotice = true,
                Confidence = combinedConfidence,
                DetectionMethod = "combined",
                ExtractedGstin = gstinResult.ExtractedGstin ?? ExtractGstin(content),
                DetectedNoticeType = keywordResult.DetectedNoticeType,
                Reason = "Multiple indicators suggest this is a GST notice"
            };
            return Task.FromResult(EnrichWithAttachments(combinedResult, email));
        }

        // No indicators found
        return Task.FromResult(new NoticeDetectionResult
        {
            IsNotice = false,
            Confidence = combinedConfidence,
            DetectionMethod = "none",
            Reason = "No GST notice indicators found"
        });
    }

    private NoticeDetectionResult CheckSender(string sender)
    {
        foreach (var allowedSender in _options.AllowedSenders)
        {
            if (EmailSenderPolicy.IsAllowed(sender, [allowedSender]))
            {
                return new NoticeDetectionResult
                {
                    IsNotice = true,
                    Confidence = 0.2, // High confidence for known GST senders
                    DetectionMethod = "sender",
                    Reason = $"Email from trusted GST sender: {allowedSender}"
                };
            }
        }

        // Partial match for government domains
        if (sender.EndsWith(".gov.in", StringComparison.OrdinalIgnoreCase))
        {
            return new NoticeDetectionResult
            {
                IsNotice = true,
                Confidence = 0.6, // Moderate confidence for general .gov.in
                DetectionMethod = "sender",
                Reason = "Email from government domain"
            };
        }

        return new NoticeDetectionResult
        {
            IsNotice = false,
            Confidence = 0.0,
            DetectionMethod = "sender",
            Reason = "Sender not in allowlist"
        };
    }

    private NoticeDetectionResult CheckKeywords(string content)
    {
        // Check for specific notice types
        foreach (var (noticeType, pattern) in NoticeTypePatterns)
        {
            if (pattern.IsMatch(content))
            {
                return new NoticeDetectionResult
                {
                    IsNotice = true,
                    Confidence = 0.9,
                    DetectionMethod = "keyword",
                    DetectedNoticeType = noticeType,
                    Reason = $"Found notice type reference: {noticeType}"
                };
            }
        }

        // Check for configured keywords
        var matchedKeywords = _options.NoticeKeywords
            .Where(keyword => content.Contains(keyword, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (matchedKeywords.Count >= 2)
        {
            return new NoticeDetectionResult
            {
                IsNotice = true,
                Confidence = 0.85,
                DetectionMethod = "keyword",
                Reason = $"Multiple notice keywords found: {string.Join(", ", matchedKeywords.Take(3))}"
            };
        }

        if (matchedKeywords.Count == 1)
        {
            return new NoticeDetectionResult
            {
                IsNotice = true,
                Confidence = 0.6,
                DetectionMethod = "keyword",
                Reason = $"Notice keyword found: {matchedKeywords[0]}"
            };
        }

        return new NoticeDetectionResult
        {
            IsNotice = false,
            Confidence = 0.0,
            DetectionMethod = "keyword",
            Reason = "No notice keywords found"
        };
    }

    private NoticeDetectionResult CheckGstinAndSender(string content, string sender)
    {
        var gstin = ExtractGstin(content);
        if (string.IsNullOrEmpty(gstin))
        {
            return new NoticeDetectionResult
            {
                IsNotice = false,
                Confidence = 0.0,
                DetectionMethod = "gstin",
                Reason = "No GSTIN found"
            };
        }

        // GSTIN found - check if sender is from government domain
        var isGovSender = sender.Contains(".gov.in", StringComparison.OrdinalIgnoreCase) ||
                          sender.Contains("@gst", StringComparison.OrdinalIgnoreCase);

        if (isGovSender)
        {
            return new NoticeDetectionResult
            {
                IsNotice = true,
                Confidence = 0.85,
                DetectionMethod = "gstin",
                ExtractedGstin = gstin,
                Reason = "GSTIN found with government sender"
            };
        }

        // GSTIN alone without government sender
        return new NoticeDetectionResult
        {
            IsNotice = false,
            Confidence = 0.3, // Low confidence - GSTIN alone isn't enough
            DetectionMethod = "gstin",
            ExtractedGstin = gstin,
            Reason = "GSTIN found but sender not government"
        };
    }

    private static double CombineConfidence(params NoticeDetectionResult[] results)
    {
        var positiveConfidences = results
            .Where(r => r.Confidence > 0)
            .Select(r => r.Confidence)
            .ToList();

        if (positiveConfidences.Count == 0) return 0.0;

        // Use weighted combination
        var maxConfidence = positiveConfidences.Max();
        var avgConfidence = positiveConfidences.Average();

        // Weighted: 70% max, 30% avg
        return maxConfidence * 0.7 + avgConfidence * 0.3;
    }

    private NoticeDetectionResult EnrichWithGstinAndType(NoticeDetectionResult result, EmailMessage email)
    {
        var content = $"{email.Subject} {email.BodyText ?? ""} {email.BodyHtml ?? ""}";
        var gstin = result.ExtractedGstin ?? ExtractGstin(content);
        var noticeType = result.DetectedNoticeType ?? DetectNoticeType(content);

        var enrichedResult = result with
        {
            ExtractedGstin = gstin,
            DetectedNoticeType = noticeType
        };

        return EnrichWithAttachments(enrichedResult, email);
    }

    private NoticeDetectionResult EnrichWithAttachments(NoticeDetectionResult result, EmailMessage email)
    {
        var noticeAttachmentIndices = email.Attachments
            .Select((attachment, index) => (attachment, index))
            .Where(x => IsPotentialNoticeAttachment(x.attachment))
            .Select(x => x.index)
            .ToList();

        return result with
        {
            NoticeAttachmentIndices = noticeAttachmentIndices
        };
    }

    public string? ExtractGstin(string text)
    {
        var match = GstinPattern.Match(text);
        return match.Success ? match.Groups[1].Value.ToUpperInvariant() : null;
    }

    private string? DetectNoticeType(string content)
    {
        foreach (var (noticeType, pattern) in NoticeTypePatterns)
        {
            if (pattern.IsMatch(content))
            {
                return noticeType;
            }
        }
        return null;
    }

    public bool IsPotentialNoticeAttachment(EmailAttachment attachment)
    {
        // Check MIME type
        var allowedTypes = _options.AllowedAttachmentTypes;
        var mimeType = attachment.MimeType.ToLowerInvariant();

        if (!allowedTypes.Any(t => mimeType.Equals(t, StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        // Size is checked by the importer so oversize notices produce review guidance,
        // instead of silently disappearing from the checkpoint.

        // Check filename patterns that suggest a notice
        var fileName = attachment.FileName.ToLowerInvariant();
        var noticeIndicators = new[]
        {
            "notice", "drc", "asmt", "reg", "gst", "order", "demand",
            "assessment", "scrutiny", "audit", "summons", "scn"
        };

        // PDF or HTML files with notice-related names are highly likely
        if (fileName.EndsWith(".pdf") || fileName.EndsWith(".html") || fileName.EndsWith(".htm") || fileName.EndsWith(".txt"))
        {
            if (noticeIndicators.Any(ind => fileName.Contains(ind)))
            {
                return true;
            }

            // Allow PDF/HTML from trusted senders even without notice keywords in filename
            return true;
        }

        return false;
    }
}
