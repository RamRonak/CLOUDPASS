using CloudPass.Application.Interfaces;
using CloudPass.Domain.Entities;
using CloudPass.Domain.Enums;
using CloudPass.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace CloudPass.Infrastructure.Services;

public class AuditService : IAuditService
{
    private readonly CloudPassDbContext _db;

    public AuditService(CloudPassDbContext db) => _db = db;

    public async Task LogAsync(int? userId, string userEmail, string action, string entityType, string entityId, string details, string? ipAddress = null)
    {
        _db.AuditLogs.Add(new AuditLog
        {
            UserId = userId,
            UserEmail = string.IsNullOrWhiteSpace(userEmail) ? "anonymous@cloudpass.io" : userEmail,
            Action = action,
            EntityType = entityType,
            EntityId = entityId,
            Details = details.Length > 990 ? details[..990] : details,
            IpAddress = ipAddress,
            Timestamp = DateTime.UtcNow
        });
        await _db.SaveChangesAsync();
    }
}

public class NotificationService : INotificationService
{
    private readonly CloudPassDbContext _db;

    public NotificationService(CloudPassDbContext db) => _db = db;

    public async Task SendAsync(int userId, string title, string message, NotificationCategory category, string? relatedReference = null)
    {
        _db.Notifications.Add(new Notification
        {
            UserId = userId,
            Title = title,
            Message = message,
            Category = category,
            RelatedReference = relatedReference,
            IsRead = false,
            CreatedAt = DateTime.UtcNow
        });
        await _db.SaveChangesAsync();
    }

    public Task<int> GetUnreadCountAsync(int userId)
        => _db.Notifications.CountAsync(n => n.UserId == userId && !n.IsRead);

    public Task<List<Notification>> GetUserNotificationsAsync(int userId, int take = 20)
        => _db.Notifications
            .Where(n => n.UserId == userId)
            .OrderByDescending(n => n.CreatedAt)
            .Take(take)
            .AsNoTracking()
            .ToListAsync();

    public async Task MarkAsReadAsync(int userId, int notificationId)
    {
        var notif = await _db.Notifications.FirstOrDefaultAsync(n => n.Id == notificationId && n.UserId == userId);
        if (notif is not null && !notif.IsRead)
        {
            notif.IsRead = true;
            await _db.SaveChangesAsync();
        }
    }

    public async Task MarkAllAsReadAsync(int userId)
    {
        var unread = await _db.Notifications.Where(n => n.UserId == userId && !n.IsRead).ToListAsync();
        foreach (var n in unread) n.IsRead = true;
        if (unread.Count > 0) await _db.SaveChangesAsync();
    }
}
