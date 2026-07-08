-- GST Sync Notification Templates
-- Run this after the main migration to seed notification templates

-- Notices Synced Template (Email)
INSERT INTO notification_templates (id, type, channel, language, version, subject, body, title, is_active, created_at)
VALUES (
    gen_random_uuid(),
    'gst_sync.notices_synced',
    'email',
    'en',
    1,
    '{{totalCount}} New GST Notice(s) Synced for {{clientName}}',
    '<h2>GST Notices Synced</h2>
<p>We''ve synced <strong>{{totalCount}}</strong> notice(s) from the GST portal for <strong>{{clientName}}</strong> ({{gstin}}).</p>

{{#if newCount}}
<p><strong>New notices:</strong> {{newCount}}</p>
{{/if}}

{{#if updatedCount}}
<p><strong>Updated notices:</strong> {{updatedCount}}</p>
{{/if}}

<p><a href="{{appUrl}}{{dashboardUrl}}" style="background-color: #007bff; color: white; padding: 10px 20px; text-decoration: none; border-radius: 5px; display: inline-block; margin-top: 10px;">View in Dashboard</a></p>

<p style="color: #666; font-size: 12px; margin-top: 20px;">This notification was sent because you have auto-capture enabled for this GSTIN.</p>',
    'GST Notices Synced',
    true,
    NOW()
)
ON CONFLICT (type, channel, language) DO UPDATE SET
    subject = EXCLUDED.subject,
    body = EXCLUDED.body,
    version = notification_templates.version + 1,
    updated_at = NOW();

-- Notices Synced Template (In-App)
INSERT INTO notification_templates (id, type, channel, language, version, subject, body, title, is_active, created_at)
VALUES (
    gen_random_uuid(),
    'gst_sync.notices_synced',
    'in_app',
    'en',
    1,
    NULL,
    '{{totalCount}} notice(s) synced for {{clientName}}',
    'GST Notices Synced',
    true,
    NOW()
)
ON CONFLICT (type, channel, language) DO UPDATE SET
    body = EXCLUDED.body,
    version = notification_templates.version + 1,
    updated_at = NOW();

-- Daily Digest Template (Email)
INSERT INTO notification_templates (id, type, channel, language, version, subject, body, title, is_active, created_at)
VALUES (
    gen_random_uuid(),
    'gst_sync.daily_digest',
    'email',
    'en',
    1,
    'GST Notice Sync Daily Digest - {{date}}',
    '<h2>GST Notice Sync Daily Digest</h2>
<p>Here''s your summary for <strong>{{date}}</strong>:</p>

<div style="background: #f5f5f5; padding: 15px; border-radius: 5px; margin: 15px 0;">
    <h3 style="margin-top: 0;">Synced Notices</h3>
    <p><strong>{{newNotices}}</strong> new notices captured</p>
    {{#if totalAmount}}
    <p>Total demand amount: <strong>₹{{totalAmount}}</strong></p>
    {{/if}}
</div>

{{#if upcomingDueDates}}
<div style="background: #fff3cd; padding: 15px; border-radius: 5px; margin: 15px 0;">
    <h3 style="margin-top: 0; color: #856404;">⚠️ Upcoming Due Dates</h3>
    <p><strong>{{upcomingDueDates}}</strong> notice(s) have due dates in the next 7 days.</p>
</div>
{{/if}}

<p><a href="{{appUrl}}{{dashboardUrl}}" style="background-color: #007bff; color: white; padding: 10px 20px; text-decoration: none; border-radius: 5px; display: inline-block; margin-top: 10px;">View Dashboard</a></p>',
    'Daily GST Digest',
    true,
    NOW()
)
ON CONFLICT (type, channel, language) DO UPDATE SET
    subject = EXCLUDED.subject,
    body = EXCLUDED.body,
    version = notification_templates.version + 1,
    updated_at = NOW();

-- Sync Failed Template (Email)
INSERT INTO notification_templates (id, type, channel, language, version, subject, body, title, is_active, created_at)
VALUES (
    gen_random_uuid(),
    'gst_sync.sync_failed',
    'email',
    'en',
    1,
    '⚠️ GST Sync Failed for {{clientName}}',
    '<h2>GST Sync Failed</h2>
<p>The automatic sync for <strong>{{clientName}}</strong> ({{gstin}}) has failed.</p>

<div style="background: #f8d7da; padding: 15px; border-radius: 5px; margin: 15px 0; color: #721c24;">
    <p><strong>Error:</strong> {{errorMessage}}</p>
    <p><strong>Consecutive failures:</strong> {{consecutiveFailures}}</p>
</div>

{{#if isPaused}}
<p style="color: #dc3545;"><strong>Important:</strong> Sync has been automatically paused due to repeated failures. Please check your GST portal login and resume sync manually.</p>
{{/if}}

<p><a href="{{appUrl}}{{settingsUrl}}" style="background-color: #007bff; color: white; padding: 10px 20px; text-decoration: none; border-radius: 5px; display: inline-block; margin-top: 10px;">Check Settings</a></p>',
    'GST Sync Failed',
    true,
    NOW()
)
ON CONFLICT (type, channel, language) DO UPDATE SET
    subject = EXCLUDED.subject,
    body = EXCLUDED.body,
    version = notification_templates.version + 1,
    updated_at = NOW();

-- Sync Failed Template (In-App)
INSERT INTO notification_templates (id, type, channel, language, version, subject, body, title, is_active, created_at)
VALUES (
    gen_random_uuid(),
    'gst_sync.sync_failed',
    'in_app',
    'en',
    1,
    NULL,
    'Sync failed for {{clientName}}: {{errorMessage}}',
    'Sync Failed',
    true,
    NOW()
)
ON CONFLICT (type, channel, language) DO UPDATE SET
    body = EXCLUDED.body,
    version = notification_templates.version + 1,
    updated_at = NOW();

-- Due Date Reminder Template (Email)
INSERT INTO notification_templates (id, type, channel, language, version, subject, body, title, is_active, created_at)
VALUES (
    gen_random_uuid(),
    'gst_sync.due_date_reminder',
    'email',
    'en',
    1,
    '📅 GST Notice Due in {{daysUntilDue}} Day(s) - {{noticeType}}',
    '<h2>Upcoming Due Date</h2>
<p>A GST notice for <strong>{{clientName}}</strong> ({{gstin}}) is due soon.</p>

<div style="background: #fff3cd; padding: 15px; border-radius: 5px; margin: 15px 0;">
    <p><strong>Notice Type:</strong> {{noticeType}}</p>
    <p><strong>Notice ID:</strong> {{noticeId}}</p>
    <p><strong>Due Date:</strong> {{dueDate}} ({{daysUntilDue}} days remaining)</p>
    {{#if demandAmount}}
    <p><strong>Demand Amount:</strong> ₹{{demandAmount}}</p>
    {{/if}}
</div>

<p><a href="{{appUrl}}{{noticeUrl}}" style="background-color: #007bff; color: white; padding: 10px 20px; text-decoration: none; border-radius: 5px; display: inline-block; margin-top: 10px;">View Notice</a></p>',
    'Due Date Reminder',
    true,
    NOW()
)
ON CONFLICT (type, channel, language) DO UPDATE SET
    subject = EXCLUDED.subject,
    body = EXCLUDED.body,
    version = notification_templates.version + 1,
    updated_at = NOW();

-- Due Date Reminder Template (In-App)
INSERT INTO notification_templates (id, type, channel, language, version, subject, body, title, is_active, created_at)
VALUES (
    gen_random_uuid(),
    'gst_sync.due_date_reminder',
    'in_app',
    'en',
    1,
    NULL,
    '{{noticeType}} notice for {{clientName}} due in {{daysUntilDue}} days',
    'Due Date Reminder',
    true,
    NOW()
)
ON CONFLICT (type, channel, language) DO UPDATE SET
    body = EXCLUDED.body,
    version = notification_templates.version + 1,
    updated_at = NOW();

-- Due Date Overdue Template (Email)
INSERT INTO notification_templates (id, type, channel, language, version, subject, body, title, is_active, created_at)
VALUES (
    gen_random_uuid(),
    'gst_sync.due_date_overdue',
    'email',
    'en',
    1,
    '🚨 OVERDUE: GST Notice Past Due Date - {{noticeType}}',
    '<h2 style="color: #dc3545;">Notice Overdue</h2>
<p>A GST notice for <strong>{{clientName}}</strong> ({{gstin}}) is past its due date.</p>

<div style="background: #f8d7da; padding: 15px; border-radius: 5px; margin: 15px 0; color: #721c24;">
    <p><strong>Notice Type:</strong> {{noticeType}}</p>
    <p><strong>Notice ID:</strong> {{noticeId}}</p>
    <p><strong>Due Date:</strong> {{dueDate}} ({{daysOverdue}} days overdue)</p>
    {{#if demandAmount}}
    <p><strong>Demand Amount:</strong> ₹{{demandAmount}}</p>
    {{/if}}
</div>

<p>Please take immediate action to avoid penalties.</p>

<p><a href="{{appUrl}}{{noticeUrl}}" style="background-color: #dc3545; color: white; padding: 10px 20px; text-decoration: none; border-radius: 5px; display: inline-block; margin-top: 10px;">View Notice Now</a></p>',
    'Notice Overdue',
    true,
    NOW()
)
ON CONFLICT (type, channel, language) DO UPDATE SET
    subject = EXCLUDED.subject,
    body = EXCLUDED.body,
    version = notification_templates.version + 1,
    updated_at = NOW();

-- Due Date Overdue Template (In-App)
INSERT INTO notification_templates (id, type, channel, language, version, subject, body, title, is_active, created_at)
VALUES (
    gen_random_uuid(),
    'gst_sync.due_date_overdue',
    'in_app',
    'en',
    1,
    NULL,
    '🚨 {{noticeType}} notice for {{clientName}} is {{daysOverdue}} days overdue!',
    'Notice Overdue',
    true,
    NOW()
)
ON CONFLICT (type, channel, language) DO UPDATE SET
    body = EXCLUDED.body,
    version = notification_templates.version + 1,
    updated_at = NOW();

-- Extension Disconnected Template (Email)
INSERT INTO notification_templates (id, type, channel, language, version, subject, body, title, is_active, created_at)
VALUES (
    gen_random_uuid(),
    'gst_sync.extension_disconnected',
    'email',
    'en',
    1,
    'GST Notice Guard Extension Disconnected',
    '<h2>Extension Disconnected</h2>
<p>We haven''t received a heartbeat from your GST Notice Guard extension in over 24 hours.</p>

<p>This could mean:</p>
<ul>
    <li>The extension has been disabled or removed</li>
    <li>You haven''t opened your browser recently</li>
    <li>There''s a network connectivity issue</li>
</ul>

<p>To ensure continuous notice syncing, please:</p>
<ol>
    <li>Open Chrome and check the extension is enabled</li>
    <li>Sign in to the extension if needed</li>
    <li>Visit the GST portal to trigger a sync</li>
</ol>

<p><a href="{{extensionUrl}}" style="background-color: #007bff; color: white; padding: 10px 20px; text-decoration: none; border-radius: 5px; display: inline-block; margin-top: 10px;">Install Extension</a></p>',
    'Extension Disconnected',
    true,
    NOW()
)
ON CONFLICT (type, channel, language) DO UPDATE SET
    subject = EXCLUDED.subject,
    body = EXCLUDED.body,
    version = notification_templates.version + 1,
    updated_at = NOW();

-- Sync Paused Template (Email)
INSERT INTO notification_templates (id, type, channel, language, version, subject, body, title, is_active, created_at)
VALUES (
    gen_random_uuid(),
    'gst_sync.sync_paused',
    'email',
    'en',
    1,
    '⏸️ GST Sync Paused for {{clientName}}',
    '<h2>Sync Paused</h2>
<p>Automatic sync for <strong>{{clientName}}</strong> ({{gstin}}) has been paused.</p>

<div style="background: #fff3cd; padding: 15px; border-radius: 5px; margin: 15px 0;">
    <p><strong>Reason:</strong> {{reason}}</p>
</div>

<p>To resume syncing:</p>
<ol>
    <li>Check that your GST portal login is working</li>
    <li>Visit the GST Sync settings page</li>
    <li>Click "Resume" on the affected GSTIN</li>
</ol>

<p><a href="{{appUrl}}{{settingsUrl}}" style="background-color: #007bff; color: white; padding: 10px 20px; text-decoration: none; border-radius: 5px; display: inline-block; margin-top: 10px;">Manage Settings</a></p>',
    'Sync Paused',
    true,
    NOW()
)
ON CONFLICT (type, channel, language) DO UPDATE SET
    subject = EXCLUDED.subject,
    body = EXCLUDED.body,
    version = notification_templates.version + 1,
    updated_at = NOW();

-- Import Completed Template (In-App)
INSERT INTO notification_templates (id, type, channel, language, version, subject, body, title, is_active, created_at)
VALUES (
    gen_random_uuid(),
    'gst_sync.import_completed',
    'in_app',
    'en',
    1,
    NULL,
    '{{importedCount}} notice(s) imported successfully{{#if failedCount}}, {{failedCount}} failed{{/if}}',
    'Import Complete',
    true,
    NOW()
)
ON CONFLICT (type, channel, language) DO UPDATE SET
    body = EXCLUDED.body,
    version = notification_templates.version + 1,
    updated_at = NOW();
