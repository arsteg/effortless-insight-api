# Notice duplicate protection

Active notices are unique within an organization by normalized GSTIN + notice/reference number. Case and surrounding spaces are ignored; punctuation is retained. Portal IDs are a second GSTIN-scoped key. Content hashes reject identical uploads before OCR. Filenames are not identifiers. Soft-deleted notices may be reimported, matching existing email reimport behavior.

## Request behavior

- Repeat upload / presigned confirmation returns `DUPLICATE_NOTICE` without reserving quota when already known. Database conflicts return the same result if requests overlap.
- GST imports link the raw record to the existing notice and count it as already imported/skipped. Duplicates do not consume a notice slot or enqueue analysis.
- An upload with an unknown reference can initially be accepted for asynchronous extraction. If extraction identifies an existing notice, the provisional row is soft-deleted with a duplicate error (and existing-notice ID where available), leaving one active notice. Its notice usage is decremented. Audit history and its source file are retained.
- GSTIN checks and CA routing remain in place. Business-owner documents must match an organization-registered GSTIN; selecting one GSTIN does not permit a document for another. BO auto-import cannot silently register a new GSTIN.

## Deployment

Apply migration `20261013100000_UniqueNoticeIdentity` before serving the updated application. It adds database-generated normalized identity columns and three partial unique indexes. It also fills missing notice numbers from legacy GSTN references/portal IDs. The database constraint is the final concurrency guard.

Run `notice-duplicate-preflight.sql` on the target database first. If it returns rows, review the affected notices and their linked records before applying the migration. The migration intentionally fails atomically on existing active duplicates rather than deleting customer data or silently choosing a survivor. Neither the preflight nor the migration has been run against application/production data during development.

## Verification

Targeted tests cover repeat upload and presigned confirmation, upload/sync in both orders, repeat sync, delayed extraction, different GSTINs/organizations, renamed files, same-batch duplicates, soft-delete/reimport, BO registered GSTINs, and existing CA routing/authorization.

`NoticeIdentityPostgresTests` executes the actual migration against a disposable schema, then overlaps two transactions for each unique key. The first succeeds, the second receives PostgreSQL `23505`, and only one row remains. Set `EI_NOTICE_TEST_POSTGRES` to an isolated test PostgreSQL connection string to run this test. It creates and drops only a randomly named `notice_test_*` schema.

```powershell
dotnet test tests/EffortlessInsight.Api.Tests --filter 'FullyQualifiedName~NoticeIdentity|FullyQualifiedName~NoticeIngestionDuplicate|FullyQualifiedName~NoticeProcessingIdentity|FullyQualifiedName~CaNoticeRoutingTests|FullyQualifiedName~CaGstinAuthorizationServiceTests|FullyQualifiedName~NoticeValidatorTests'
```
