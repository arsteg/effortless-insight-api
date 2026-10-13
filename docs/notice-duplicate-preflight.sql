-- Read-only preflight. Every returned group must be reviewed before migration.
WITH identities AS (
    SELECT "Id", "OrganizationId",
           NULLIF(upper(btrim("Gstin")), '') AS gstin,
           NULLIF(upper(btrim(COALESCE(NULLIF(btrim("NoticeNumber"), ''),
               CASE WHEN "Source" = 'gstn_portal' THEN COALESCE(NULLIF(btrim("GstnReferenceNumber"), ''), "GstnNoticeId") END))), '') AS reference,
           NULLIF(upper(btrim("GstnNoticeId")), '') AS portal,
           NULLIF("FileHash", '') AS content_hash
    FROM "Notices" WHERE "DeletedAt" IS NULL
)
SELECT 'reference' AS key_type, "OrganizationId", array_agg("Id") AS notice_ids, count(*) AS notice_count
FROM identities WHERE gstin IS NOT NULL AND reference IS NOT NULL
GROUP BY "OrganizationId", gstin, reference HAVING count(*) > 1
UNION ALL
SELECT 'portal', "OrganizationId", array_agg("Id"), count(*)
FROM identities WHERE gstin IS NOT NULL AND portal IS NOT NULL
GROUP BY "OrganizationId", gstin, portal HAVING count(*) > 1
UNION ALL
SELECT 'content', "OrganizationId", array_agg("Id"), count(*)
FROM identities WHERE content_hash IS NOT NULL
GROUP BY "OrganizationId", content_hash HAVING count(*) > 1;
