-- Migration: Add Full-Text Search Index for Notices
-- Date: 2026-06-27
-- Description: Creates a GIN index on notice content for efficient full-text search

-- Add a generated tsvector column for full-text search
ALTER TABLE "Notices"
ADD COLUMN IF NOT EXISTS "SearchVector" tsvector
GENERATED ALWAYS AS (
    setweight(to_tsvector('english', COALESCE("NoticeReference", '')), 'A') ||
    setweight(to_tsvector('english', COALESCE("NoticeType", '')), 'B') ||
    setweight(to_tsvector('english', COALESCE("NoticeSubType", '')), 'B') ||
    setweight(to_tsvector('english', COALESCE("SummaryText", '')), 'C') ||
    setweight(to_tsvector('english', COALESCE("Gstin", '')), 'A')
) STORED;

-- Create GIN index on the search vector
CREATE INDEX IF NOT EXISTS "IX_Notices_FullText_Search"
ON "Notices" USING GIN ("SearchVector")
WHERE "DeletedAt" IS NULL;

-- Create a function to search notices
CREATE OR REPLACE FUNCTION search_notices(
    p_organization_id UUID,
    p_search_query TEXT,
    p_limit INT DEFAULT 50,
    p_offset INT DEFAULT 0
)
RETURNS TABLE(
    id UUID,
    notice_reference TEXT,
    notice_type TEXT,
    summary_text TEXT,
    rank REAL
) AS $$
BEGIN
    RETURN QUERY
    SELECT
        n."Id",
        n."NoticeReference",
        n."NoticeType",
        n."SummaryText",
        ts_rank(n."SearchVector", plainto_tsquery('english', p_search_query)) as rank
    FROM "Notices" n
    WHERE n."OrganizationId" = p_organization_id
      AND n."DeletedAt" IS NULL
      AND n."SearchVector" @@ plainto_tsquery('english', p_search_query)
    ORDER BY rank DESC, n."CreatedAt" DESC
    LIMIT p_limit
    OFFSET p_offset;
END;
$$ LANGUAGE plpgsql;

-- Comment for documentation
COMMENT ON INDEX "IX_Notices_FullText_Search" IS 'GIN index for full-text search on notice content';
COMMENT ON FUNCTION search_notices IS 'Full-text search function for notices with ranking';
