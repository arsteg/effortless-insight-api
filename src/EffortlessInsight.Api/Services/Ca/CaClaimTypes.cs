namespace EffortlessInsight.Api.Services.Ca;

/// <summary>
/// JWT claims carried when a CA is acting on behalf of a client.
/// </summary>
public static class CaClaimTypes
{
    /// <summary>
    /// The CaClientRelationship this token was issued under.
    ///
    /// A discriminator, not an authorization input: what the CA may do comes from the
    /// signed org_id and role claims. This claim only says "re-validate the engagement",
    /// and it is re-checked against the database on every request and every refresh.
    /// </summary>
    public const string ClientRelationshipId = "ca_client_rel_id";

    /// <summary>
    /// Whether the caller is a CA at all. Carried separately from "role" because creating
    /// an organization promotes a CA's role to "owner".
    /// </summary>
    public const string IsCa = "is_ca";
}
