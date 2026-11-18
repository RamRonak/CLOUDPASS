# CLOUDPASS — Smart Cloud-Based Bus Pass & Ticket Booking System

> **"Your Journey. Your Ticket. Always Connected."**

CloudPass is a complete, cloud-native **ASP.NET Core MVC (.NET 10)** bus ticket booking and commuter bus pass management platform designed for **Microsoft Azure** scalability, high availability, and cryptographic anti-fraud security.

---

## Core Objectives Solved

| Traditional Bus Booking Problem | CloudPass Cloud & Cybersecurity Solution |
|---|---|
| Paper ticket loss | Centralized **Azure Blob Storage** PDF tickets + anytime access in **My Bookings** |
| Ticket theft & QR duplication | **HMAC-SHA256 signed non-PII QR tokens** + single-use conductor check-in locking |
| Client-side fare tampering | **Zero-Trust server-side pricing** from centralized SQL tables + tampering audit alerts |
| Duplicate seat bookings | **ACID database transactions** + atomic conditional SQL seat locking (`WHERE IsBooked = 0`) |
| Festival/peak traffic crashes | **Azure App Service P1v3 Autoscaling (2–10 instances)**, caching, SQL indexes, rate limiting & async queues |
| Unauthorized ticket viewing (IDOR) | Strict **per-user ownership enforcement**, PBKDF2 hashing, RBAC, and immutable `AuditLogs` |

---

## Technology Stack

- **Frontend:** HTML5, CSS3 (Dark Navy & White commercial design system), Vanilla JS, Bootstrap 5, Chart.js
- **Backend:** ASP.NET Core MVC (.NET 10, C#), Entity Framework Core 9, REST APIs, `Channel<T>` Background Worker, Rate Limiting
- **Database:** SQL Server / Azure SQL Database (14 normalized tables, EF Core migrations, transient-fault retry, plus SQLite zero-setup local dev mode)
- **Security & Ticketing:** PBKDF2 Password Hashing, Cookie Auth + RBAC, HMAC-SHA256 QR Tokens (`QRCoder`), PDF Boarding Passes (`QuestPDF`), Razorpay Test-Mode + Signed HMAC Webhooks
- **Cloud & DevOps:** Microsoft Azure Bicep IaC (App Service, Autoscale, Azure SQL, Azure Blob Storage, Application Insights, Key Vault), Docker & Docker Compose, GitHub Actions CI/CD, Apache JMeter & Azure Load Testing

---

## Quick Start (Local Development)

```bash
# 1. Restore & Build
dotnet restore CloudPass.sln
dotnet build CloudPass.sln

# 2. Run Automated Tests (13 Functional, Security, Concurrency & Load Tests)
dotnet test tests/CloudPass.Tests/CloudPass.Tests.csproj

# 3. Start the Web Portal
dotnet run --project src/CloudPass.Web/CloudPass.Web.csproj --launch-profile http
```

Open **http://localhost:5050** in your browser.

### Demo Accounts (Seeded Automatically)
- **Passenger:** `passenger@cloudpass.io` / `Pass@123!`
- **Admin:** `admin@cloudpass.io` / `Admin@123!`

---

## Documentation Index

- **[Cloud Architecture, Autoscaling & Security](docs/ARCHITECTURE.md)**
- **[Database Design & 14-Table ER Diagram](docs/DATABASE.md)**
- **[Local, Docker & Microsoft Azure Deployment Guide](docs/DEPLOYMENT.md)**
- **[Measured Functional, Security & Performance Test Report](docs/TEST_REPORT.md)**
