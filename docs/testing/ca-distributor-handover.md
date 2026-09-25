# CA distributor handover

## Ownership and onboarding

- `POST /api/v1/ca/clients` saves a prospect and a CA organization GSTIN without an invitation. Portal client creation and notice inserts also establish the prospect association.
- The CA can upload, enter and sync work before sending an invitation and while it is pending, expired, cancelled or declined.
- GSTIN selection is optional for uploads. Detection establishes the prospect and GSTIN registry entry automatically in the CA workspace. No invitation or manual client creation is required first. A missing/invalid detected GSTIN remains pending review in the original workspace; processing reports `GSTIN_REVIEW_REQUIRED`.
- For an accepted distributor relationship, identified notices route to the exact BO GSTIN, including when extraction finishes after acceptance. Routing requires the uploading CA's active destination write membership and valid BO subscription; revoked access never falls back to creating a duplicate client in the CA workspace. An unrelated organization's matching GSTIN is never sufficient authority.
- Extension client lookup/creation, sessions, raw notice imports and PDF requests resolve the accepted client organization from the captured GSTIN or stored resource. The extension can retain its CA-workspace token. Requests spanning different destination organizations are rejected and must be split by client.
- `POST /api/v1/ca/clients/invitations/{token}/prepare` prepares a BO organization. It does **not** accept, transfer data, or grant CA membership. Only the invited email can prepare it.
- The web client switches to that organization and opens plan selection. Checkout returns to the invitation for explicit acceptance.
- `/link` and `/accept` require an unexpired active subscription or activated unexpired trial on the destination organization. Paused, past-due, cancelled and expired organizations are rejected. A plan on another organization is insufficient.
- Acceptance uses the invitation's persisted source organization and exact GSTIN. It retains canonical notice IDs, uploader identity, prospect provenance, child records and raw import links. It moves the sync connection/session data; later syncs must use BO organization context.
- The inviting CA receives external `ca` membership. BO-to-CA invitations continue using `OrganizationService` and do not invoke the handover service. Membership alone no longer creates automatic cross-organization sharing links.

## Transaction and conflict rules

Acceptance and asynchronous notice/client/raw/session inserts acquire PostgreSQL transaction-scoped workspace locks. Ownership/status concurrency tokens reject obsolete writes. Identified CA notices resolve their accepted destination automatically. A sync already in flight during acceptance may receive `CLIENT_ORGANIZATION_CHANGED`; retry to resolve the destination. A notice whose client is accepted between resolution and lock acquisition may receive `CLIENT_ROUTING_RETRY`; background processing retries against the committed relationship.

The transfer, exact GSTIN assignment, membership, prospect state, invitation state and audit receipt commit together. Repeated acceptance returns the saved receipt. Historical transfers do not consume new-notice quota. Raw-only and legacy staged records are converted within the transaction, not in a best-effort post-acceptance import.

`HANDOVER_NOTICE_CONFLICT` reports two existing canonical notice IDs with the same portal identity or file hash. The transaction is rolled back. Resolve the conflicting business records before retrying; the system deliberately does not overwrite responses, choose between conflicting statuses, or silently discard either record. Legacy repeated files and raw captures referencing an existing canonical notice are reconciled automatically.

Notice-specific custom workflow templates/stages and approval chains are copied to the destination. Source templates shared with other clients remain intact. Source-only active assignees/team links are cleared, and copied assignment rules are disabled pending BO review. Approval steps targeting users without destination access fall back to the organization owner. Historical authors and workflow history remain intact.

Queued AI work is dispatched after commit. A five-minute Hangfire recovery job re-dispatches stranded queued handover notices. Processing status concurrency prevents duplicate workers from claiming the same queued notice. AI processing failure does not roll back completed business ownership.

## Historical repair

1. Take a backup and run `ca-distributor-handover-audit.sql` in staging. It is read-only and reports accepted invitations with stranded records or no receipt.
2. Verify each invitation's source CA, prospect, destination owner and GSTIN. Missing/ambiguous associations need investigation; do not mass-transfer by GSTIN alone.
3. The original invited BO can reopen the invitation and call `/link` for the recorded destination. An old accepted invitation without a handover receipt is repaired by the same transactional service. This **does not restore a removed/expired CA membership**.
4. A receipt makes subsequent retries read-only. Compare source/destination counts, raw links, comments, attachments and workflow data after repair.
5. Conflicts abort with an actionable code; neither ownership nor membership should be partially changed.

No production data is automatically repaired at application startup. This change uses the existing schema, including the previously added `CaProspectClientId` column; concurrency-token configuration does not add physical columns. Ensure all existing migrations are applied before deployment.

## Verification

Run the focused tests:

```powershell
dotnet test tests/EffortlessInsight.Api.Tests -p:UseAppHost=false --filter 'FullyQualifiedName~CaClientServiceAcceptInvitationTests|FullyQualifiedName~CaDistributorHandoverTests|FullyQualifiedName~CaHandoverPostgresTests|FullyQualifiedName~PermissionsCaTests|FullyQualifiedName~CurrentOrganizationServiceCaTests|FullyQualifiedName~TenantContextMiddlewareTests'
```

PostgreSQL integration tests are opt-in: set `EI_CA_TEST_POSTGRES` to a **test server** connection with pgvector installed and CREATEDB privileges. Each test creates and removes a unique `ei_ca_test_...` database. Without that variable, these tests explicitly skip; InMemory tests do not prove PostgreSQL locking/rollback behavior.

Before rollout, exercise two browser sessions (CA and BO), a secondary GSTIN, work before invitation, work while pending, imported and raw-only notices, legacy staging, repeated acceptance, concurrent sync, revoked CA access and unpaid destination rejection. Verify the BO-to-CA membership flow independently. Review any disabled copied assignment rules with the BO.
