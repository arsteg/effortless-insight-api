START TRANSACTION;
ALTER TABLE "NoticeResponses" ADD "AllegationDraftJson" text;

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20261022052235_AddAllegationDraft', '9.0.0');

COMMIT;

