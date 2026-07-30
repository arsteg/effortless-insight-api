namespace EffortlessInsight.Api.Services.Email;

/// <summary>
/// Default template renderer with templates embedded as interpolated raw
/// string literals. Keeping them in code (rather than .html files) preserves
/// compile-time checking of placeholder usage; move to files if the template
/// count grows or non-developers need to edit them.
/// </summary>
public sealed class EmailTemplateRenderer : IEmailTemplateRenderer
{
    public EmailTemplateContent Render(string templateId, IReadOnlyDictionary<string, object> data)
    {
        return templateId switch
        {
            "auth_verify_email" => new EmailTemplateContent(
                "Verify your email - EffortlessInsight",
                $"""
                <!DOCTYPE html>
                <html>
                <head>
                    <meta charset="utf-8">
                    <meta name="viewport" content="width=device-width, initial-scale=1.0">
                </head>
                <body style="font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, 'Helvetica Neue', Arial, sans-serif; line-height: 1.6; color: #333; max-width: 600px; margin: 0 auto; padding: 20px;">
                    <div style="background: linear-gradient(135deg, #667eea 0%, #764ba2 100%); padding: 30px; border-radius: 10px 10px 0 0;">
                        <h1 style="color: white; margin: 0; font-size: 24px;">EffortlessInsight</h1>
                    </div>
                    <div style="background: #ffffff; padding: 30px; border: 1px solid #e0e0e0; border-top: none; border-radius: 0 0 10px 10px;">
                        <h2 style="color: #333; margin-top: 0;">Verify Your Email</h2>
                        <p>Hi {data.GetValueOrDefault("user_name", "there")},</p>
                        <p>Thanks for signing up! Please verify your email address by clicking the button below:</p>
                        <div style="text-align: center; margin: 30px 0;">
                            <a href="{data.GetValueOrDefault("verification_link", "#")}"
                               style="background: linear-gradient(135deg, #667eea 0%, #764ba2 100%); color: white; padding: 14px 30px; text-decoration: none; border-radius: 5px; font-weight: bold; display: inline-block;">
                                Verify Email Address
                            </a>
                        </div>
                        <p style="color: #666; font-size: 14px;">This link will expire in {data.GetValueOrDefault("expiry_hours", "24")} hours.</p>
                        <p style="color: #666; font-size: 14px;">If you didn't create an account, you can safely ignore this email.</p>
                        <hr style="border: none; border-top: 1px solid #e0e0e0; margin: 30px 0;">
                        <p style="color: #999; font-size: 12px; margin: 0;">
                            If the button doesn't work, copy and paste this link into your browser:<br>
                            <a href="{data.GetValueOrDefault("verification_link", "#")}" style="color: #667eea;">{data.GetValueOrDefault("verification_link", "#")}</a>
                        </p>
                    </div>
                </body>
                </html>
                """
            ),
            "auth_password_reset" => new EmailTemplateContent(
                "Reset your password - EffortlessInsight",
                $"""
                <!DOCTYPE html>
                <html>
                <head>
                    <meta charset="utf-8">
                    <meta name="viewport" content="width=device-width, initial-scale=1.0">
                </head>
                <body style="font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, 'Helvetica Neue', Arial, sans-serif; line-height: 1.6; color: #333; max-width: 600px; margin: 0 auto; padding: 20px;">
                    <div style="background: linear-gradient(135deg, #667eea 0%, #764ba2 100%); padding: 30px; border-radius: 10px 10px 0 0;">
                        <h1 style="color: white; margin: 0; font-size: 24px;">EffortlessInsight</h1>
                    </div>
                    <div style="background: #ffffff; padding: 30px; border: 1px solid #e0e0e0; border-top: none; border-radius: 0 0 10px 10px;">
                        <h2 style="color: #333; margin-top: 0;">Reset Your Password</h2>
                        <p>Hi {data.GetValueOrDefault("user_name", "there")},</p>
                        <p>We received a request to reset your password. Click the button below to create a new password:</p>
                        <div style="text-align: center; margin: 30px 0;">
                            <a href="{data.GetValueOrDefault("reset_link", "#")}"
                               style="background: linear-gradient(135deg, #667eea 0%, #764ba2 100%); color: white; padding: 14px 30px; text-decoration: none; border-radius: 5px; font-weight: bold; display: inline-block;">
                                Reset Password
                            </a>
                        </div>
                        <p style="color: #666; font-size: 14px;">This link will expire in {data.GetValueOrDefault("expiry_hours", "24")} hours.</p>
                        <p style="color: #666; font-size: 14px;">If you didn't request a password reset, you can safely ignore this email.</p>
                    </div>
                </body>
                </html>
                """
            ),
            "org_invitation" => new EmailTemplateContent(
                $"You've been invited to join {data.GetValueOrDefault("organization_name", "an organization")} - EffortlessInsight",
                $"""
                <!DOCTYPE html>
                <html>
                <head>
                    <meta charset="utf-8">
                    <meta name="viewport" content="width=device-width, initial-scale=1.0">
                </head>
                <body style="font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, 'Helvetica Neue', Arial, sans-serif; line-height: 1.6; color: #333; max-width: 600px; margin: 0 auto; padding: 20px;">
                    <div style="background: linear-gradient(135deg, #667eea 0%, #764ba2 100%); padding: 30px; border-radius: 10px 10px 0 0;">
                        <h1 style="color: white; margin: 0; font-size: 24px;">EffortlessInsight</h1>
                    </div>
                    <div style="background: #ffffff; padding: 30px; border: 1px solid #e0e0e0; border-top: none; border-radius: 0 0 10px 10px;">
                        <h2 style="color: #333; margin-top: 0;">You're Invited!</h2>
                        <p>Hi there,</p>
                        <p><strong>{data.GetValueOrDefault("inviter_name", "A team member")}</strong> has invited you to join
                           <strong>{data.GetValueOrDefault("organization_name", "their organization")}</strong> on EffortlessInsight
                           as a <strong>{data.GetValueOrDefault("role", "member")}</strong>.</p>
                        {(string.IsNullOrWhiteSpace(data.GetValueOrDefault("message", "")?.ToString()) ? "" : $"""
                        <div style="background: #f8f9fa; border-left: 4px solid #667eea; padding: 12px 16px; margin: 20px 0; border-radius: 0 5px 5px 0;">
                            <p style="margin: 0; color: #555; font-style: italic;">"{data["message"]}"</p>
                        </div>
                        """)}
                        <div style="text-align: center; margin: 30px 0;">
                            <a href="{data.GetValueOrDefault("invitation_url", "#")}"
                               style="background: linear-gradient(135deg, #667eea 0%, #764ba2 100%); color: white; padding: 14px 30px; text-decoration: none; border-radius: 5px; font-weight: bold; display: inline-block;">
                                Accept Invitation
                            </a>
                        </div>
                        <p style="color: #666; font-size: 14px;">This invitation will expire in {data.GetValueOrDefault("expires_in_days", "7")} days.</p>
                        <p style="color: #666; font-size: 14px;">If you weren't expecting this invitation, you can safely ignore this email.</p>
                        <hr style="border: none; border-top: 1px solid #e0e0e0; margin: 30px 0;">
                        <p style="color: #999; font-size: 12px; margin: 0;">
                            If the button doesn't work, copy and paste this link into your browser:<br>
                            <a href="{data.GetValueOrDefault("invitation_url", "#")}" style="color: #667eea;">{data.GetValueOrDefault("invitation_url", "#")}</a>
                        </p>
                    </div>
                </body>
                </html>
                """
            ),
            "admin-password-reset" => new EmailTemplateContent(
                "Reset your admin password - EffortlessInsight",
                $"""
                <!DOCTYPE html>
                <html>
                <head>
                    <meta charset="utf-8">
                    <meta name="viewport" content="width=device-width, initial-scale=1.0">
                </head>
                <body style="font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, 'Helvetica Neue', Arial, sans-serif; line-height: 1.6; color: #333; max-width: 600px; margin: 0 auto; padding: 20px;">
                    <div style="background: linear-gradient(135deg, #1a1a2e 0%, #16213e 100%); padding: 30px; border-radius: 10px 10px 0 0;">
                        <h1 style="color: white; margin: 0; font-size: 24px;">EffortlessInsight Admin</h1>
                    </div>
                    <div style="background: #ffffff; padding: 30px; border: 1px solid #e0e0e0; border-top: none; border-radius: 0 0 10px 10px;">
                        <h2 style="color: #333; margin-top: 0;">Reset Your Admin Password</h2>
                        <p>Hi {data.GetValueOrDefault("name", "there")},</p>
                        <p>We received a request to reset the password for your <strong>admin portal</strong> account. Click the button below to create a new password:</p>
                        <div style="text-align: center; margin: 30px 0;">
                            <a href="{data.GetValueOrDefault("reset_link", "#")}"
                               style="background: linear-gradient(135deg, #1a1a2e 0%, #16213e 100%); color: white; padding: 14px 30px; text-decoration: none; border-radius: 5px; font-weight: bold; display: inline-block;">
                                Reset Admin Password
                            </a>
                        </div>
                        <p style="color: #666; font-size: 14px;">This link will expire in {data.GetValueOrDefault("expiry_hours", "1")} hour(s).</p>
                        <p style="color: #c0392b; font-size: 14px;"><strong>If you didn't request this reset, please contact your system administrator immediately</strong> — someone may be attempting to access your account.</p>
                    </div>
                </body>
                </html>
                """
            ),
            "data_export_ready" => new EmailTemplateContent(
                $"Your data export is ready - {data.GetValueOrDefault("organization_name", "EffortlessInsight")}",
                $"""
                <!DOCTYPE html>
                <html>
                <head>
                    <meta charset="utf-8">
                    <meta name="viewport" content="width=device-width, initial-scale=1.0">
                </head>
                <body style="font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, 'Helvetica Neue', Arial, sans-serif; line-height: 1.6; color: #333; max-width: 600px; margin: 0 auto; padding: 20px;">
                    <div style="background: linear-gradient(135deg, #667eea 0%, #764ba2 100%); padding: 30px; border-radius: 10px 10px 0 0;">
                        <h1 style="color: white; margin: 0; font-size: 24px;">EffortlessInsight</h1>
                    </div>
                    <div style="background: #ffffff; padding: 30px; border: 1px solid #e0e0e0; border-top: none; border-radius: 0 0 10px 10px;">
                        <h2 style="color: #333; margin-top: 0;">Your Data Export is Ready</h2>
                        <p>Hi there,</p>
                        <p>The data export you requested for <strong>{data.GetValueOrDefault("organization_name", "your organization")}</strong> has been completed and is ready to download.</p>
                        <table style="width: 100%; border-collapse: collapse; margin: 20px 0; font-size: 14px;">
                            <tr>
                                <td style="padding: 8px 12px; background: #f8f9fa; border: 1px solid #e0e0e0; color: #666;">File size</td>
                                <td style="padding: 8px 12px; border: 1px solid #e0e0e0;">{data.GetValueOrDefault("file_size", "-")}</td>
                            </tr>
                            <tr>
                                <td style="padding: 8px 12px; background: #f8f9fa; border: 1px solid #e0e0e0; color: #666;">Available until</td>
                                <td style="padding: 8px 12px; border: 1px solid #e0e0e0;">{data.GetValueOrDefault("expires_at", "-")}</td>
                            </tr>
                        </table>
                        <div style="text-align: center; margin: 30px 0;">
                            <a href="{data.GetValueOrDefault("download_url", "#")}"
                               style="background: linear-gradient(135deg, #667eea 0%, #764ba2 100%); color: white; padding: 14px 30px; text-decoration: none; border-radius: 5px; font-weight: bold; display: inline-block;">
                                Download Export
                            </a>
                        </div>
                        <p style="color: #666; font-size: 14px;">You must be signed in to download the export. The file will be deleted automatically after the date above.</p>
                        <p style="color: #666; font-size: 14px;">If you didn't request this export, please review your organization's recent activity.</p>
                    </div>
                </body>
                </html>
                """
            ),
            _ => new EmailTemplateContent(
                "Notification from EffortlessInsight",
                $"""
                <!DOCTYPE html>
                <html>
                <body style="font-family: Arial, sans-serif; padding: 20px;">
                    <h2>EffortlessInsight</h2>
                    <p>{data.GetValueOrDefault("message", "You have a new notification.")}</p>
                </body>
                </html>
                """
            )
        };
    }
}
