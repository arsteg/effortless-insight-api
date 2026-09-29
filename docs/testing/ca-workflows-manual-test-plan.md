# Effortless Insights — CA workflows manual test plan

Version: 1.1 | Updated: 25 September 2026 — optional GSTIN and automatic client routing

**Execution status: Not executed.** This document defines manual acceptance tests; automated test results are not evidence that these steps have passed.

## 1. Purpose and acceptance rules

Verify these two independent relationships:

| Workflow | Before acceptance | After acceptance |
| --- | --- | --- |
| CA distributor → BO | CA works on a client's GSTIN in the CA workspace, with or without an invitation | Matching existing work belongs to the BO organization. CA accesses it as an external member of that organization |
| BO → CA | BO organization already owns its GSTIN and notices | Ownership remains with BO; invited CA gains only the assigned membership permissions |

A successful distributor handover must preserve canonical notice IDs, files, comments, history and sync associations; use the **invited GSTIN**, including a secondary GSTIN; and leave unrelated clients untouched. Membership and ownership changes must succeed together or roll back together.

## 2. Test execution record

| Field | Value |
| --- | --- |
| Environment / web URL | |
| API version / commit | |
| Web version / commit | |
| Database migration version | |
| Tester / execution date | |
| CA account / source organization ID | |
| BO account / destination organization ID | |
| Invited GSTIN / unrelated GSTIN | |
| Subscription plan / status / expiry | |
| Evidence folder / defect tracker | |

Use **Not run, Pass, Fail, Blocked** for results. Record actual results and evidence; do not mark a case passed based only on a success toast. A missing test fixture or unavailable service means Blocked.

## 3. Prerequisites and test data

Run against an isolated test or staging environment with PostgreSQL and the applicable migrations. Use test billing, test email, and authorized GST sync fixtures. Background processing, file storage and notification services must be available for cases that need them.

Use separate browser profiles for CA and BO to avoid sharing authentication or organization selection. Have a third unrelated account for access tests.

| Fixture | Required setup |
| --- | --- |
| CA-A | Registered CA with Admin approval and active free access; owns CA-ORG |
| CA-PENDING | Registered CA awaiting Admin approval, without an active free-access grant |
| BO-NEW | Invited email with no existing account |
| BO-EXISTING | Existing BO account with a paid organization |
| BO-OTHER | Unrelated user, not an authorized destination member |
| GSTIN-A | Valid test client GSTIN to transfer |
| GSTIN-B | Different client GSTIN in CA-ORG; must remain untouched |
| GSTIN-C | Primary GSTIN of a BO organization where GSTIN-A is secondary |
| Test documents | Distinct documents matching GSTIN-A; one matching GSTIN-B; a deliberately mismatched document |

Use GSTINs accepted by this environment and documents that match them. Do not use random invented values when verification or portal access is required.

Prepare GSTIN-A work before acceptance:

| Reference | Record | Baseline to capture |
| --- | --- | --- |
| A1 | Manually entered notice | Notice ID, GSTIN, status, creator, organization |
| A2 | Uploaded and processed notice | Notice ID, document name, download availability, extracted details |
| A3 | Previously synced/imported portal notice | Notice ID, portal notice identity, raw import link |
| A4 | Notice created after invitation is sent | Notice ID and creation time |
| B1 | Unrelated GSTIN-B notice | Notice ID, organization and GSTIN |
| Related work | Comment, attachment, task, workflow, approval and activity history on A1/A2 | Text, file names, IDs, assignees, stage and status |

Optional developer-assisted fixtures: legacy staged notices, raw-only portal records, previously accepted invitations with stranded data, duplicate canonical notices, and interrupted transactions. Use independent fixtures for destructive/conflict cases so they do not disrupt the main journey.

## 4. Core distributor journey

### D01 — CA approval and free access [P0]

1. Sign in as CA-PENDING and attempt client and notice work.
2. Have Admin approve/activate the CA's free access through the normal Admin workflow.
3. Sign in again and select the CA-owned organization.
4. Open Clients, create a client and open notice entry/upload/sync.

**Expected:** Pending CA does not receive approved free access prematurely. Approved CA can perform permitted work in their own workspace without buying a BO plan. This grant must not unlock an unpaid BO organization; verify D08 as well.

### D02 — Upload before creating a client or invitation [P0]

1. As CA-A in CA-ORG, ensure GSTIN-A has no saved client or invitation.
2. Open Notices → Upload, leave **Client GSTIN (optional)** blank, and upload A2 containing a readable GSTIN-A.
3. Wait for processing. Verify GSTIN-A is associated with the notice and a client/prospect plus GSTIN registry entry becomes available; no invitation should be sent.
4. Create A1 and sync/import A3. Separately verify **Save client without inviting** remains available as an optional setup path, rather than a prerequisite for uploading.
5. Add the related work listed in the baseline table and reload the page.
6. Create B1 under GSTIN-B and capture the baseline IDs and counts.

**Expected:** No GSTIN selection, prior client creation, BO account, BO organization or invitation is required to upload. After detection, GSTIN-A work persists under its client association in CA-ORG and remains accessible to CA-A. GSTIN-B remains separate.

### D03 — Continue working while invitation is pending [P0]

1. Send an invitation to BO-NEW for GSTIN-A.
2. Before BO signs up or accepts, reopen A1/A2 and make an allowed edit.
3. Create A4 and sync again.
4. Capture updated baseline data and verify the invitation remains pending.

**Expected:** Sending an invitation does not freeze or transfer work. All matching work, including A4 created while pending, is included at acceptance.

### D04 — New BO signup and organization preparation [P0]

1. Open the invitation in the separate BO browser profile.
2. Sign up using the invited email, then return to the invitation.
3. Complete organization setup and continue to plan selection.
4. Before activating a plan, check CA-A's client and notices; inspect membership using an authorized account or API if the unpaid BO cannot open settings.

**Expected:** The destination organization can be prepared before subscription activation. Preparation alone does not accept the invitation, transfer notices or grant CA membership. The invitation remains pending and CA-A can continue working.

### D05 — Activate subscription and accept [P0]

1. Activate a valid plan for the prepared destination organization using test checkout, or an offered valid trial.
2. Return to the invitation from checkout and explicitly accept.
3. Record the destination organization, invitation result and displayed transfer counts.
4. Verify the BO lands in the correct organization and opens its notices.

**Expected:** Acceptance succeeds only after the destination has a valid subscription. Matching work is associated with the BO organization; invitation and prospect are marked accepted/merged. CA-A has active external CA membership. No second organization or duplicate membership is created by this step.

### D06 — Verify complete handover and isolation [P0]

1. As BO, find A1–A4 and compare IDs and details against the baseline.
2. Open every test attachment and original uploaded document; inspect comments, tasks, workflow stage, approval history and activity history.
3. Verify synced notice A3 retains its portal identity and imported-record association.
4. As CA, select the BO organization, then open the same notice IDs.
5. Return to CA-ORG and verify B1 is unchanged. Confirm B1 is not visible in the BO organization.
6. Use the authorized API/database checks in section 8 to verify actual ownership, rather than relying on list visibility alone.

**Expected:** Existing canonical IDs and historical content survive. Both BO and CA access the same records through BO-ORG. GSTIN-B work stays in CA-ORG. Source-only assignees/team links are cleared where necessary; copied assignment rules require BO review (D16). Legacy/raw-only conversions may create new canonical IDs; assess those separately in D15.

### D07 — Shared ongoing work and revoked access [P0]

1. As CA in BO-ORG, make an allowed notice update, add a comment and perform an allowed sync.
2. Reload as BO and verify the updates on the same notice IDs.
3. Make an allowed BO update and verify it as CA.
4. Remove or expire CA's membership as BO.
5. Refresh the CA session and try the organization, direct notice URL, attachment access and an authorized test API request.

**Expected:** Work stays owned by BO-ORG. CA actions follow the assigned role; CA is not an owner. After revocation, new requests are denied even with a stale page or direct URL. Previously downloaded files are outside application revocation control. BO retains all notices and related work.

## 5. Subscription, invitations and mapping

### D08 — Destination subscription enforcement [P0]

1. Repeat acceptance attempts with fresh pending invitations and destination states: no subscription, expired trial, expired paid period, paused, past due and cancelled.
2. Test a BO with a valid subscription on a different organization only.
3. Test a CA with approved free access trying to work as an external member of an unpaid BO organization.
4. Activate a valid destination plan and retry the pending acceptance.

**Expected:** Invalid destination subscription blocks acceptance without transferring records or granting membership. Another organization's plan and the CA's free grant do not bypass this check. A valid destination plan permits retry. An active, unexpired trial is valid when offered and activated by the application.

### D09 — Existing BO and secondary GSTIN [P0]

1. Use BO-EXISTING with GSTIN-C primary and GSTIN-A secondary in the intended organization.
2. Open the GSTIN-A invitation and link it to that organization.
3. Verify all transferred notices reference GSTIN-A's registry entry, not GSTIN-C.
4. Confirm existing GSTIN-C work is unchanged and no unnecessary new organization was created.

**Expected:** Mapping uses the exact invited GSTIN. BO account existence, primary-GSTIN selection and other memberships do not redirect the transfer.

### D10 — Wrong recipient and unauthorized destination [P0]

1. Open a pending invitation while signed in as BO-OTHER.
2. Attempt preparation and acceptance through the UI and, with a developer, the protected API.
3. As the invited BO, attempt to link a destination they do not own/administer, or one lacking GSTIN-A.

**Expected:** Protected actions reject the request. No notice, membership, invitation or prospect state changes. Public invitation preview, if available, is not evidence of acceptance authorization.

### D11 — Retry and double acceptance [P0]

1. Accept a valid pending invitation, then reload and reopen the same link.
2. Repeat the acceptance request for the same destination, including two rapid requests.
3. Compare notice IDs/counts, memberships and handover audit receipts.

**Expected:** The original successful receipt is returned on retry. No duplicate notices, organizations, memberships or transfer receipt; no second ownership movement. A retry must not change the accepted destination.

### D12 — Cancelled, declined and expired invitations [P1]

1. In separate fixtures, cancel, decline or expire an invitation before acceptance.
2. Try accepting each old link.
3. Continue CA editing, uploading and syncing GSTIN-A in CA-ORG.
4. Send a new valid invitation through the supported flow and complete it.

**Expected:** Invalid pending links cannot accept or transfer. Existing client work is retained and remains usable by CA. A new valid invitation can transfer that same accumulated work. Reopening an already accepted invitation is covered by D11/D19, not by pending-link expiry rules.

### D13 — Optional GSTIN, detection and review [P0]

1. In CA-ORG, upload a readable document without selecting a GSTIN and verify detection succeeds.
2. Select GSTIN-A and upload a matching document; test a batch for that GSTIN.
3. Select GSTIN-A but upload the deliberately mismatched document containing GSTIN-B.
4. Upload documents with no readable GSTIN and an invalid detected GSTIN. Check processing status, association and error details after retries.

**Expected:** Blank selection is accepted. Valid detection identifies the client and routes appropriately. A detected mismatch with an established selected client fails processing without silently switching clients. Missing/invalid detection reports `GSTIN_REVIEW_REQUIRED` and keeps the original upload available for correction/retry; it must not guess an organization. Legacy records with no reliable GSTIN also require review.

### D14 — Continue uploading from the CA workspace after handover [P0]

1. Keep an upload/manual-entry/sync page open in CA-ORG before acceptance.
2. Complete acceptance in the BO session.
3. Upload a new document for GSTIN-A from CA-ORG with GSTIN selection blank. Wait for detection.
4. Select BO-ORG, refresh permissions and repeat the permitted operation.

**Expected:** The identified upload routes to the accepted BO organization and exact GSTIN automatically. The CA does not need to switch organizations before uploading. It is visible to both BO and authorized CA in BO-ORG, and is not left isolated in CA-ORG. In-flight sync may need a retry if acceptance changes ownership during the request; subsequent requests resolve the BO context automatically.

## 6. Developer-assisted integrity and recovery tests

These cases need controlled staging fixtures or fault injection. Ask the developer/test operator to prepare them; do not alter production data to reproduce a failure.

### D15 — Imported, raw-only and legacy staged records [P0]

1. Seed separate identifiable records for GSTIN-A: already imported raw capture, raw-only capture, legacy staged upload, and repeated raw/staged evidence referring to an existing notice.
2. Capture source identities, file hashes, import flags and linked notice IDs.
3. Accept the invitation and inspect both the UI and persisted associations.

**Expected:** Already imported notices transfer, even when import flags are already set. Raw-only and legacy staged data become usable BO notices. Repeated evidence maps to the canonical record where safely identifiable. Historical source references remain traceable; GSTIN-B data is excluded. Handover does not charge this historical work as new-notice creation quota.

### D16 — Workflow and permission reconciliation [P0]

1. Give a source notice a custom workflow and approval chain also used by another CA client.
2. Assign work to a source-only team/user; include an approval step for a user absent from BO-ORG.
3. Accept and inspect destination workflow configuration, task assignments and approval routing.
4. Verify the other client's original workflow still works.

**Expected:** Required configuration is copied, not removed from the other client's workspace. Notice stages/history remain linked. Ineligible active assignees/team links are cleared. Copied assignment rules are disabled pending BO review; unsupported approval-user targets fall back to organization owner. Review all copied rules and enable only appropriate ones.

### D17 — Conflicting canonical notices [P0]

1. Prepare two independently worked canonical notices with the same GSTIN and portal identity, or duplicate file hash, across the relevant transfer set.
2. Capture both records and their comments/statuses.
3. Attempt acceptance.

**Expected:** `HANDOVER_NOTICE_CONFLICT` identifies the conflict and acceptance rolls back. Neither business record is overwritten or deleted. Invitation, prospect, ownership and membership remain at their pre-attempt state. Reconciliation is required before retry; automatic merging of conflicting business work is not expected.

### D18 — Concurrent sync, transaction failure and processing recovery [P0]

1. Using PostgreSQL, pause a source sync/write immediately before persistence and accept concurrently.
2. Release the write and check source/destination records and portal IDs.
3. In a separate fixture, inject failure during handover before commit; reload both sessions and inspect the database.
4. Retry without the injected failure.
5. In another fixture, interrupt queue dispatch after a successful commit, leaving eligible notices queued; restore the worker and observe recovery over at least two five-minute scheduler cycles.

**Expected:** A source write commits before handover and is included, or resolves the accepted destination. A request interrupted by the ownership change may be rejected and must resolve the destination on retry. No duplicate or stranded identified post-handover source notice. Pre-commit failure leaves no partial membership/ownership change. Retry succeeds once. Post-commit queue failure does not undo ownership; the recovery job reschedules eligible queued work. Duplicate workers do not process the same notice concurrently. InMemory test results do not substitute for this PostgreSQL test.

### D19 — Repair a previously accepted invitation [P0]

1. Prepare a historical accepted invitation with correct destination/GSTIN, stranded source work and no handover receipt.
2. Run the read-only audit linked below; record its findings.
3. As the original invited BO, reopen the invitation and invoke linking to the recorded destination through the supported UI/API path.
4. Verify transferred content, saved receipt and repeat behavior.
5. Repeat with CA membership previously removed or expired.

**Expected:** Eligible historical data is repaired transactionally. A subsequent retry is read-only. Repair does not recreate or reactivate revoked CA membership. A historical accepted link may be repairable after its original pending expiry; recipient, destination and subscription checks still apply. Missing/ambiguous ownership needs investigation, not a broad transfer by GSTIN.

### D20 — Mixed-client upload batch with blank selection [P0]

1. In CA-ORG, leave GSTIN blank and upload separate documents for GSTIN-A and GSTIN-B in one batch.
2. Use an accepted distributor relationship for GSTIN-A and no accepted relationship for GSTIN-B.
3. Wait for each document to process and inspect both organizations.

**Expected:** Each document resolves independently. GSTIN-A goes to its accepted BO organization; GSTIN-B stays associated with its prospect in CA-ORG. One document's GSTIN must not be applied to the whole blank-selection batch.

### D21 — Detection finishes after invitation acceptance [P0]

1. Pause AI processing for a new GSTIN-A upload made with blank selection.
2. Accept the pending GSTIN-A invitation as BO while that upload still has no detected GSTIN.
3. Resume processing, then compare notice ID, document and related records.

**Expected:** Detection resolves the now-accepted relationship and routes the same notice to BO-ORG without reaccepting the invitation or creating a copy. Acceptance need not guess the GSTIN of the previously unidentified upload.

### D22 — Automatic routing must respect revoked access and subscription [P0]

1. With an accepted client relationship, separately revoke/expire the CA's destination membership, change it to viewer, and expire the BO plan.
2. For each fixture, attempt a new CA-workspace upload for that GSTIN and an extension sync.
3. Check the BO records and the CA upload's processing result.

**Expected:** Unauthorized or unpaid destination writes are blocked, and no new BO record is created. The upload remains available in its original workspace with a processing error for review/retry; the system must not silently create a new staging relationship for an already accepted client. Restoring authorized access/plan allows retry. An unrelated BO with the same GSTIN is never selected.

### D23 — Browser extension uses detected GSTIN before and after acceptance [P0]

1. Keep the extension authenticated in CA-ORG and capture GSTIN-A from the portal. Register the sync connection through the extension if needed; do not send an invitation first.
2. Complete sync with auto-import enabled; verify notices belong to the client in CA-ORG.
3. Accept the distributor invitation and repeat client lookup, session start, capture, PDF upload and completion without changing the extension's organization token.
4. Repeat the same portal capture and inspect notice IDs/counts.

**Expected:** Pre-acceptance work stays in CA-ORG and moves on acceptance. Later requests resolve the accepted BO organization and exact GSTIN. Raw records, sessions, imported notices and PDFs remain accessible through BO membership. Repeated captures reuse existing portal identities. Cross-organization import batches are rejected with `MIXED_CLIENT_BATCH` and must be split by client.

## 7. BO → CA regression tests

### B01 — External CA invitation without ownership transfer [P0]

1. As BO, use an existing subscribed organization and create a GSTIN and notices before inviting CA-A through organization membership.
2. Record organization, GSTIN, notice IDs and related work.
3. Accept the membership invitation as CA-A and select the BO organization.
4. Open existing notices and perform allowed work; verify the result as BO.

**Expected:** CA becomes an external member with the assigned permissions. Notice IDs, GSTIN mapping and BO ownership remain unchanged. This path does not prepare a distributor organization, merge a CA prospect or transfer CA-ORG notices.

### B02 — Role limits, isolation and revocation [P0]

1. Check actual permissions for the assigned CA role, including notice edits, downloads, membership management, billing and ownership actions.
2. Attempt a prohibited action via a direct URL/API, not only by looking for a hidden button.
3. Confirm CA-A cannot access another BO organization without membership.
4. Revoke CA-A's membership and retry notice access using the existing session.

**Expected:** Allowed work succeeds; prohibited actions are denied server-side. Membership does not confer owner authority or global access. Revocation blocks new access while BO data remains intact.

### B03 — Both relationships on one CA account [P0]

1. Give CA-A one completed distributor client, one directly invited BO membership and one unaccepted client in CA-ORG.
2. Switch among those organizations and perform permitted work for each GSTIN.
3. Accept the remaining distributor invitation and repeat the checks.
4. Without reloading the browser, switch using both **Select Organization** and **Select Client** while viewing Dashboard, Notices and GST Sync. Verify counts, rows, role actions and subscription state match the selection. Switch back and repeat quickly while a request is pending.

**Expected:** Organization selection and permissions follow the selected membership. Each client has the correct ownership. Completing one distributor handover neither moves another BO's notices nor changes that organization's membership relationship.

Page data must update without a browser reload. Previous-client filters and selections must reset, and late responses must not replace the newly selected organization's data.

## 8. Ownership evidence and reconciliation

For each successful handover, capture evidence through authorized APIs or read-only database inspection:

- Existing canonical notice IDs now have the destination `OrganizationId` and the exact invited `GstinId`; original uploader/prospect provenance is retained.
- Related notice files, conversations, activity records, workflow references and sync records resolve under the correct organization.
- Portal raw-record import links resolve to the canonical notice; repeated sync does not produce duplicates.
- The inviting CA has the intended external membership for a new acceptance; historical repair preserves revocation.
- Invitation destination, prospect merged destination and the `ca_client_invitation.handover` audit receipt agree.
- Unrelated GSTIN source records retain their prior ownership and IDs.

Do not compare list totals alone: filters, soft-deleted records, legacy conversions and raw-record reconciliation affect counts. Reconcile by identity, document and portal reference. Inspect deleted records through authorized tooling where needed; do not expect them in the ordinary notice list.

Supporting files:

- [Implementation and repair guide](ca-distributor-handover.md)
- [Read-only historical handover audit](ca-distributor-handover-audit.sql)

The audit is a diagnostic aid, not a complete proof of correctness and not a repair script. Redact invitation tokens, authentication headers, credentials and sensitive document contents from evidence.

## 9. Results and release decision

Copy one row per case and subscription variant into the execution record:

| Case / variant | Result | Actual result / evidence | Defect ID | Tester / date |
| --- | --- | --- | --- | --- |
| D01 | Not run | | | |
| D02 | Not run | | | |
| D03 | Not run | | | |
| D04 | Not run | | | |
| D05 | Not run | | | |
| D06 | Not run | | | |
| D07 | Not run | | | |
| D08 — one row per subscription state | Not run | | | |
| D09 | Not run | | | |
| D10 | Not run | | | |
| D11 | Not run | | | |
| D12 | Not run | | | |
| D13 | Not run | | | |
| D14 | Not run | | | |
| D15 | Not run | | | |
| D16 | Not run | | | |
| D17 | Not run | | | |
| D18 | Not run | | | |
| D19 | Not run | | | |
| D20 | Not run | | | |
| D21 | Not run | | | |
| D22 | Not run | | | |
| D23 | Not run | | | |
| B01 | Not run | | | |
| B02 | Not run | | | |
| B03 | Not run | | | |

Release acceptance:

- All P0 cases pass on the intended build; blocked cases remain visible and are not counted as passes.
- No unresolved cross-organization disclosure, data loss, incorrect GSTIN mapping, duplicate creation or partial handover defect.
- PostgreSQL concurrency/rollback and separate-browser journeys are verified, beyond unit tests.
- Historical repair candidates and ambiguous data have been reviewed separately; startup does not automatically repair them.
- Any copied disabled assignment rules and required BO configuration review are recorded for the affected organization.

| Sign-off | Name / date / notes |
| --- | --- |
| QA execution complete | |
| Engineering integrity review | |
| Product workflow acceptance | |
| Release decision and outstanding defects | |
