# CA-as-Distributor — DB Changes Reference

What gets written to which table at each step of the flow. Use this alongside
`ca-distributor-manual-test.md` / `ca-distributor-multi-client-ui-test.md` to
verify DB state after each UI action while testing.

## CA-distributor-specific tables (quick schema reference)

**`CaFreeAccessGrants`** — one row per grant/revoke cycle for a CA user
| Column | Notes |
|---|---|
| `CaUserId` | FK → `AspNetUsers.Id` |
| `IsActive` | true while granted; flipped false on revoke (row is kept, not deleted) |
| `GrantedByAdminId` / `GrantedAt` / `GrantReason` | set on grant |
| `RevokedByAdminId` / `RevokedAt` / `RevokeReason` | null until revoked |
| Unique index | `IX_CaFreeAccessGrants_ActivePerCaUser` — only one active row per `CaUserId` at a time |

**`CaClientInvitations`** — one row per CA→BO invitation
| Column | Notes |
|---|---|
| `CaUserId`, `CaOrganizationId` | the inviting CA and their own org |
| `Gstin`, `Email`, `EmailNormalized`, `ClientDisplayName` | invitation target |
| `TokenHash` | SHA-256 of the emailed token; token itself never stored |
| `Status` | `pending` → `accepted` \| `declined` \| `expired` \| `cancelled` |
| `ExpiresAt` | now + 14 days at creation/resend |
| `AcceptedUserId`, `ResultingOrganizationId` | set on accept |
| `SendCount`, `LastSentAt` | incremented on resend (cap 3) |
| `AccessDurationDays` | optional; feeds `OrganizationMembers.AccessExpiresAt` on accept |

**`CaProspectClients`** — the CA's private staging record per GSTIN (survives across invitation resends)
| Column | Notes |
|---|---|
| `CaUserId`, `Gstin`, `GstinHash` | unique per `(CaUserId, GstinHash)` |
| `CaClientInvitationId` | current/most recent invitation for this GSTIN |
| `Status` | `staging` → `merged` (after BO accepts) |
| `MergedAt`, `MergedIntoOrganizationId` | set on accept |

**`CaStagedNotices`** — notices the CA uploaded before the BO accepted
| Column | Notes |
|---|---|
| `CaProspectClientId` | FK |
| `FileUrl`, `FileName`, `FileSize`, `FileHash` | upload metadata |
| `MergedToNotices`, `MergedNoticeId`, `MergedAt` | set when folded into a real `Notice` on accept |

## Step-by-step

### 1. CA registers (`POST /auth/register`, `isCA: true`)
| Table | Op | Key fields |
|---|---|---|
| `AspNetUsers` | INSERT | `Email`, `Name`, `Mobile`/`MobileNormalized`, `IsMobileVerified`, `Role="owner"`, **`IsCA=true`**, `TermsAccepted`, `PasswordHash` |

`EmailConfirmed` is `false` until step 2.

### 2. Email verification (`GET/POST /auth/verify-email`)
| Table | Op | Key fields |
|---|---|---|
| `AspNetUsers` | UPDATE | `EmailConfirmed=true`, `IsEmailVerified=true` |

### 3. CA creates their own organization (`POST /organizations`, onboarding)
| Table | Op | Key fields |
|---|---|---|
| `Organizations` | INSERT | `Name`, `NameNormalized`, `State`, `SubscriptionStatus="none"`, `Settings` (jsonb defaults) |
| `OrganizationGstins` | INSERT — **skipped entirely if the CA left GSTIN blank** | `Gstin`, `StateCode`, `StateName`, `IsPrimary=true`, `Source="onboarding"` |
| `OrganizationMembers` | INSERT | `OrganizationId`, `UserId=CA`, `Role="owner"`, `IsExternal=false`, `Status="active"` |
| `AspNetUsers` | UPDATE | `OrganizationId=<new org>`, `Role="owner"` |
| `AuditLogs` | INSERT | `Action="organization.created"` |

### 4. Admin grants Free CA Access (`POST /admin/users/{id}/ca-access/grant`)
| Table | Op | Key fields |
|---|---|---|
| `CaFreeAccessGrants` | INSERT | `CaUserId`, `IsActive=true`, `GrantedByAdminId`, `GrantedAt`, `GrantReason` |
| `AdminAuditLogs` | INSERT | `Action="user.ca_free_access_granted"` |

No row is touched on `Organizations`/`BillingSubscriptions` — the bypass is computed at read-time in `FeatureAccessService`, not materialized. Redis feature-cache key `org_features:{organizationId}` is invalidated for every org the CA owns (not a DB write).

### 5. Admin revokes Free CA Access (`POST /admin/users/{id}/ca-access/revoke`)
| Table | Op | Key fields |
|---|---|---|
| `CaFreeAccessGrants` | UPDATE (existing active row) | `IsActive=false`, `RevokedByAdminId`, `RevokedAt`, `RevokeReason` |
| `AdminAuditLogs` | INSERT | `Action="user.ca_free_access_revoked"` |

### 6. CA sends a BO invitation (`POST /ca/clients/invitations`)
| Table | Op | Key fields |
|---|---|---|
| `CaProspectClients` | INSERT (new GSTIN) or UPDATE (`ClientDisplayName`, if re-inviting an existing staging record) | `CaUserId`, `Gstin`, `GstinHash`, `Status="staging"` |
| `CaClientInvitations` | INSERT | `CaUserId`, `CaOrganizationId`, `Gstin`, `Email`, `TokenHash`, `Status="pending"`, `ExpiresAt=+14d` |
| `CaProspectClients` | UPDATE | `CaClientInvitationId=<new invitation>` |
| `AuditLogs` | INSERT | `Action="ca_client_invitation.sent"` |

### 7. CA uploads a staged notice for a not-yet-accepted client (`POST /ca/clients/{prospectClientId}/notices/upload`)
| Table | Op | Key fields |
|---|---|---|
| `CaStagedNotices` | INSERT | `CaProspectClientId`, `Gstin`, `FileUrl`, `FileName`, `FileHash`, `Source="manual_upload"`, `MergedToNotices=false` |

Nothing on `Notices` yet — this only happens on acceptance (step 11).

### 8. CA resends a pending invitation (`POST /ca/clients/invitations/{id}/resend`)
| Table | Op | Key fields |
|---|---|---|
| `CaClientInvitations` | UPDATE | `SendCount+=1`, `LastSentAt`, `ExpiresAt=+14d` (reset), `TokenHash` (new token minted, old link dies) |

### 9. CA cancels a pending invitation (`DELETE /ca/clients/invitations/{id}`)
| Table | Op | Key fields |
|---|---|---|
| `CaClientInvitations` | UPDATE | `Status="cancelled"`, `RespondedAt` |

`CaProspectClients`/`CaStagedNotices` are untouched — CA can re-invite the same GSTIN later.

### 10. BO opens the invitation link (`GET /ca/clients/invitations/{token}`, anonymous)
Read-only, **unless** the invitation is past `ExpiresAt`:
| Table | Op | Key fields |
|---|---|---|
| `CaClientInvitations` | UPDATE (only if expired) | `Status="expired"` |

### 11. BO accepts (`POST /ca/clients/invitations/{token}/accept`) — the big one, all in one DB transaction
| Table | Op | Key fields |
|---|---|---|
| `Organizations` | INSERT | new BO organization, `Gstin` taken from the invitation (not re-entered) |
| `OrganizationGstins` | INSERT | primary GSTIN for the new org |
| `OrganizationMembers` | INSERT (×2) | (a) BO as `Role="owner"`; (b) **CA** as `Role="ca"`, `IsExternal=true`, `ClientReference=<invitation display name>`, `AccessExpiresAt` (if `AccessDurationDays` was set) |
| `AspNetUsers` (BO) | UPDATE | `OrganizationId`, `Role="owner"` |
| `CaClientInvitations` | UPDATE | `Status="accepted"`, `RespondedAt`, `AcceptedUserId`, `ResultingOrganizationId` |
| `CaStagedNotices` | UPDATE (each un-merged row) | `MergedToNotices=true`, `MergedNoticeId`, `MergedAt` |
| `Notices` | INSERT (one per staged notice, unless a duplicate match is found by reference number/file hash) | copied from `CaStagedNotice` fields + `Metadata.source_ca_staged_notice_id` |
| `CaProspectClients` | UPDATE | `Status="merged"`, `MergedAt`, `MergedIntoOrganizationId` |
| `UsageRecords` | INSERT/UPDATE (post-commit, per new notice) | notice-count usage tracking |
| `AuditLogs` | INSERT | `Action="ca_client_invitation.accepted"` |

A Hangfire background job (`INoticeProcessingJob`) is also enqueued per new `Notice` for AI processing — not a direct DB write from this request, but will itself write to `NoticeAiReports` etc. once it runs.

### 12. BO declines (`POST /ca/clients/invitations/{token}/decline`)
| Table | Op | Key fields |
|---|---|---|
| `CaClientInvitations` | UPDATE | `Status="declined"`, `RespondedAt` |

`CaProspectClients`/`CaStagedNotices` are deliberately left untouched — the CA still holds the staged data and can re-invite.

## Quick verification queries

```sql
-- Is this user a CA, and do they have active free access?
SELECT u."Email", u."IsCA",
       g."IsActive" AS "HasActiveFreeAccess", g."GrantedAt", g."RevokedAt"
FROM "AspNetUsers" u
LEFT JOIN "CaFreeAccessGrants" g ON g."CaUserId" = u."Id" AND g."IsActive" = true
WHERE u."Email" = '<ca email>';

-- All of a CA's clients (staged + active) at a glance
SELECT pc."Gstin", pc."Status" AS "prospect_status", ci."Status" AS "invitation_status",
       ci."Email", ci."ExpiresAt", pc."MergedIntoOrganizationId"
FROM "CaProspectClients" pc
LEFT JOIN "CaClientInvitations" ci ON ci."Id" = pc."CaClientInvitationId"
WHERE pc."CaUserId" = (SELECT "Id" FROM "AspNetUsers" WHERE "Email" = '<ca email>');

-- Staged notices not yet merged for a given prospect client
SELECT "FileName", "UploadedAt", "MergedToNotices"
FROM "CaStagedNotices"
WHERE "CaProspectClientId" = '<prospect client id>';

-- Confirm CA membership landed correctly after a BO accepts
SELECT om."Role", om."IsExternal", om."ClientReference", om."AccessExpiresAt", o."Name"
FROM "OrganizationMembers" om
JOIN "Organizations" o ON o."Id" = om."OrganizationId"
WHERE om."UserId" = (SELECT "Id" FROM "AspNetUsers" WHERE "Email" = '<ca email>');
```
