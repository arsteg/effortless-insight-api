-- READ ONLY. Run against a backup/staging database first.
-- This report never decrypts GSTINs, changes ownership, or grants membership.
-- A missing prospect is an ambiguous legacy case requiring investigation.
SELECT i."Id" AS invitation_id,
       i."CaOrganizationId" AS source_organization_id,
       i."ResultingOrganizationId" AS destination_organization_id,
       p."Id" AS prospect_id,
       p."Status" AS prospect_status,
       (SELECT count(*) FROM "Notices" n
        WHERE n."OrganizationId" = i."CaOrganizationId"
          AND n."DeletedAt" IS NULL
          AND (n."CaProspectClientId" = p."Id" OR n."GstinHash" = p."GstinHash"
               OR encode(sha256(convert_to(upper(trim(n."Gstin")), 'UTF8')), 'hex') = p."GstinHash")) AS stranded_notices,
       (SELECT count(*) FROM gst_notices_raw r
        WHERE r."OrganizationId" = i."CaOrganizationId" AND r."DeletedAt" IS NULL
          AND encode(sha256(convert_to(upper(trim(r."Gstin")), 'UTF8')), 'hex') = p."GstinHash") AS stranded_raw_records,
       (SELECT count(*) FROM "CaStagedNotices" s
        WHERE s."CaProspectClientId" = p."Id" AND NOT s."MergedToNotices" AND s."DeletedAt" IS NULL) AS legacy_staged_records,
       EXISTS (SELECT 1 FROM "AuditLogs" a
               WHERE a."EntityId" = i."Id" AND a."OrganizationId" = i."ResultingOrganizationId"
                 AND a."Action" = 'ca_client_invitation.handover') AS has_handover_receipt
FROM "CaClientInvitations" i
LEFT JOIN "CaProspectClients" p ON p."CaClientInvitationId" = i."Id" AND p."CaUserId" = i."CaUserId"
WHERE i."Status" = 'accepted' AND i."DeletedAt" IS NULL
ORDER BY i."RespondedAt", i."Id";
