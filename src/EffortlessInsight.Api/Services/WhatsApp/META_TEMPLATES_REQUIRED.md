# Required Meta WhatsApp Templates

This document lists the WhatsApp message templates that must be created in Meta Business Manager
for the WhatsApp bot to send proactive notifications.

## Template Creation Steps

1. Go to [Meta Business Suite](https://business.facebook.com)
2. Navigate to: WhatsApp Manager > Account Tools > Message Templates
3. Create each template below with the exact name and parameters
4. Wait for Meta approval (typically 24-72 hours)

---

## 1. Daily Digest Template

**Template Name:** `daily_digest`
**Category:** UTILITY
**Language:** English (en)

**Body Text:**
```
Good morning! Here's your daily GST compliance summary:

📋 *Pending Notices:* {{1}}
📅 *Due Today:* {{2}}
🔴 *High Risk:* {{3}}

Reply "notices" for details or open the app to view.
```

**Variables:**
- `{{1}}` - Number of pending notices
- `{{2}}` - Number of items due today
- `{{3}}` - Number of high-risk items

---

## 2. Deadline Reminder Template

**Template Name:** `deadline_reminder`
**Category:** UTILITY
**Language:** English (en)

**Body Text:**
```
⏰ *Deadline Reminder*

Your {{1}} notice has a deadline approaching.

📅 Due Date: {{2}}
⏳ Time Remaining: {{3}} day(s)

Reply "notices" to view details or take action in the app.
```

**Variables:**
- `{{1}}` - Notice type (e.g., "DRC-01", "ASMT-10")
- `{{2}}` - Due date (e.g., "July 15, 2026")
- `{{3}}` - Days remaining

---

## 3. High Risk Notice Alert Template

**Template Name:** `high_risk_notice`
**Category:** UTILITY
**Language:** English (en)

**Body Text:**
```
🚨 *High Risk Notice Alert*

A new high-risk notice requires your attention:

📝 Type: {{1}}
💰 Amount: ₹{{2}}
📅 Due: {{3}}

This notice has been flagged as high priority. Please review immediately.

Reply "status" for summary or open the app to view details.
```

**Variables:**
- `{{1}}` - Notice type
- `{{2}}` - Tax amount (formatted)
- `{{3}}` - Due date or "No deadline"

---

## 4. Task Assignment Template

**Template Name:** `task_assigned`
**Category:** UTILITY
**Language:** English (en)

**Body Text:**
```
📋 *New Task Assigned*

You have been assigned a new task:

*{{1}}*

📅 Due: {{2}}
👤 Assigned by: {{3}}

Reply "tasks" to see all your tasks or open the app to respond.
```

**Variables:**
- `{{1}}` - Task title
- `{{2}}` - Due date or "No due date"
- `{{3}}` - Name of person who assigned the task

---

## Template Approval Notes

- **UTILITY** category templates have higher approval rates than MARKETING
- Ensure the template clearly identifies your business
- Avoid promotional language in UTILITY templates
- Test templates in the Meta sandbox environment first
- Templates may be rejected if they appear spammy or promotional

## After Approval

Once templates are approved:

1. Run the template sync job to import them:
   ```
   POST /hangfire/recurring/whatsapp-sync-templates/trigger
   ```

2. Or wait for the daily sync at 3 AM UTC

3. Verify templates are synced:
   ```sql
   SELECT * FROM "WhatsAppTemplates" WHERE "Status" = 'APPROVED';
   ```

## Fallback Behavior

If templates are not available:
- Daily digest: Skipped silently
- Deadline reminders: Skipped silently
- High-risk alerts: Skipped silently
- Task assignments: Skipped silently

The bot will log warnings when templates are unavailable but won't fail.
