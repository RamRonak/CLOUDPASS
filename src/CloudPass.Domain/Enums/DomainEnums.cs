namespace CloudPass.Domain.Enums;

public enum BookingStatus
{
    PendingPayment = 0,
    Confirmed = 1,
    Cancelled = 2,
    Completed = 3
}

public enum PaymentStatus
{
    Pending = 0,
    Paid = 1,
    Failed = 2,
    Refunded = 3
}

public enum ScheduleStatus
{
    Scheduled = 0,
    Departed = 1,
    Completed = 2,
    Cancelled = 3
}

public enum PassApplicationStatus
{
    Pending = 0,
    Approved = 1,
    Rejected = 2
}

public enum BusPassStatus
{
    Active = 0,
    Expired = 1,
    Revoked = 2
}

public enum VerificationType
{
    Ticket = 0,
    BusPass = 1
}

public enum NotificationCategory
{
    Booking = 0,
    Payment = 1,
    Ticket = 2,
    Cancellation = 3,
    BusPass = 4,
    Security = 5,
    System = 6
}

public enum SeatDeck
{
    Lower = 0,
    Upper = 1
}

public enum SeatType
{
    Seater = 0,
    Sleeper = 1
}
