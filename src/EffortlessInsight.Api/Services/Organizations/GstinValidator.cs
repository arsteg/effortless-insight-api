using System.Text.RegularExpressions;
using EffortlessInsight.Api.Data;
using EffortlessInsight.Api.DTOs;
using Microsoft.EntityFrameworkCore;

namespace EffortlessInsight.Api.Services.Organizations;

/// <summary>
/// Validates GSTIN format and checksum according to Indian GST rules.
/// GSTIN format: 2-digit state + 10-char PAN + 1 entity + Z + 1 check digit
/// </summary>
public interface IGstinValidatorService
{
    /// <summary>
    /// Validates GSTIN format and checksum
    /// </summary>
    GstinValidationResult Validate(string gstin);

    /// <summary>
    /// Gets state name from state code
    /// </summary>
    Task<string?> GetStateNameAsync(string stateCode);

    /// <summary>
    /// Checks if GSTIN already exists in the platform
    /// </summary>
    Task<bool> ExistsAsync(string gstin, Guid? excludeOrganizationId = null);
}

public class GstinValidatorService : IGstinValidatorService
{
    private static readonly Regex GstinPattern = new(
        @"^([0-9]{2})([A-Z]{5})([0-9]{4})([A-Z]{1})([A-Z0-9]{1})(Z)([A-Z0-9]{1})$",
        RegexOptions.Compiled
    );

    private const string ChecksumCharacters = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ";

    private readonly ApplicationDbContext _dbContext;

    public GstinValidatorService(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public GstinValidationResult Validate(string gstin)
    {
        if (string.IsNullOrWhiteSpace(gstin))
        {
            return new GstinValidationResult(false, "GSTIN is required", null, null, null, null, null);
        }

        gstin = gstin.Trim().ToUpperInvariant();

        if (gstin.Length != 15)
        {
            return new GstinValidationResult(false, "GSTIN must be exactly 15 characters", null, null, null, null, null);
        }

        var match = GstinPattern.Match(gstin);
        if (!match.Success)
        {
            return new GstinValidationResult(false, "Invalid GSTIN format", null, null, null, null, null);
        }

        var stateCode = match.Groups[1].Value;
        var panPart1 = match.Groups[2].Value;
        var panPart2 = match.Groups[3].Value;
        var panPart3 = match.Groups[4].Value;
        var entityCode = match.Groups[5].Value;
        var checkDigit = match.Groups[7].Value;

        var pan = panPart1 + panPart2 + panPart3;

        // Validate state code (basic check - full validation against state table done separately)
        if (!IsValidStateCode(stateCode))
        {
            return new GstinValidationResult(false, $"Invalid state code: {stateCode}", null, null, null, null, null);
        }

        // Validate checksum using Luhn algorithm variant
        if (!ValidateChecksum(gstin))
        {
            return new GstinValidationResult(false, "Invalid GSTIN checksum", null, null, null, null, null);
        }

        // Get state name (will be fetched from database in service layer)
        var stateName = GetStateNameSync(stateCode);

        return new GstinValidationResult(
            IsValid: true,
            ErrorMessage: null,
            Gstin: gstin,
            StateCode: stateCode,
            StateName: stateName,
            Pan: pan,
            EntityCode: entityCode
        );
    }

    public async Task<string?> GetStateNameAsync(string stateCode)
    {
        var state = await _dbContext.GstinStateCodes
            .FirstOrDefaultAsync(s => s.Code == stateCode);
        return state?.Name;
    }

    public async Task<bool> ExistsAsync(string gstin, Guid? excludeOrganizationId = null)
    {
        gstin = gstin.Trim().ToUpperInvariant();

        var query = _dbContext.OrganizationGstins.Where(g => g.Gstin == gstin);

        if (excludeOrganizationId.HasValue)
        {
            query = query.Where(g => g.OrganizationId != excludeOrganizationId.Value);
        }

        return await query.AnyAsync();
    }

    private static bool ValidateChecksum(string gstin)
    {
        int factor = 1;
        int sum = 0;

        for (int i = 0; i < 14; i++)
        {
            int codePoint = ChecksumCharacters.IndexOf(gstin[i]);
            if (codePoint < 0) return false;

            int digit = factor * codePoint;
            factor = factor == 1 ? 2 : 1;
            digit = (digit / 36) + (digit % 36);
            sum += digit;
        }

        int remainder = sum % 36;
        int checkCodePoint = (36 - remainder) % 36;
        char expectedChecksum = ChecksumCharacters[checkCodePoint];

        return gstin[14] == expectedChecksum;
    }

    private static bool IsValidStateCode(string stateCode)
    {
        // Valid Indian state codes (01-38, 97, 99)
        if (!int.TryParse(stateCode, out int code))
            return false;

        return (code >= 1 && code <= 38) || code == 97 || code == 99;
    }

    private static string GetStateNameSync(string stateCode)
    {
        // Fallback state names - actual names should be fetched from database
        return stateCode switch
        {
            "01" => "Jammu and Kashmir",
            "02" => "Himachal Pradesh",
            "03" => "Punjab",
            "04" => "Chandigarh",
            "05" => "Uttarakhand",
            "06" => "Haryana",
            "07" => "Delhi",
            "08" => "Rajasthan",
            "09" => "Uttar Pradesh",
            "10" => "Bihar",
            "11" => "Sikkim",
            "12" => "Arunachal Pradesh",
            "13" => "Nagaland",
            "14" => "Manipur",
            "15" => "Mizoram",
            "16" => "Tripura",
            "17" => "Meghalaya",
            "18" => "Assam",
            "19" => "West Bengal",
            "20" => "Jharkhand",
            "21" => "Odisha",
            "22" => "Chhattisgarh",
            "23" => "Madhya Pradesh",
            "24" => "Gujarat",
            "26" => "Dadra and Nagar Haveli and Daman and Diu",
            "27" => "Maharashtra",
            "28" => "Andhra Pradesh (Old)",
            "29" => "Karnataka",
            "30" => "Goa",
            "31" => "Lakshadweep",
            "32" => "Kerala",
            "33" => "Tamil Nadu",
            "34" => "Puducherry",
            "35" => "Andaman and Nicobar Islands",
            "36" => "Telangana",
            "37" => "Andhra Pradesh",
            "38" => "Ladakh",
            "97" => "Other Territory",
            "99" => "Centre Jurisdiction",
            _ => "Unknown"
        };
    }
}
