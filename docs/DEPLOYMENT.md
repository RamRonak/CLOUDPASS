# CloudPass Setup, Docker & Microsoft Azure Deployment Guide

> **Transparency Note:** Local builds, functional tests, security tests, concurrency tests, and performance benchmarks have been verified locally. Live Microsoft Azure provisioning requires an authenticated Azure subscription (`az login`); the complete Azure Bicep IaC template and deployment script are ready to run once Azure credentials are supplied.

## 1. Local Quickstart (Zero Database Setup)

```bash
dotnet restore CloudPass.sln
dotnet build CloudPass.sln
dotnet run --project src/CloudPass.Web/CloudPass.Web.csproj --launch-profile http
```

Open **http://localhost:5050** in your browser.

### Seeded Demo Accounts
| Role | Email | Password |
|---|---|---|
| **Administrator** | `admin@cloudpass.io` | `Admin@123!` |
| **Passenger 1** | `passenger@cloudpass.io` | `Pass@123!` |
| **Passenger 2** | `rohan@cloudpass.io` | `Pass@123!` |

## 2. Docker & Docker Compose (App + SQL Server 2022 + Azurite Blob)

```bash
cp .env.example .env
docker compose up --build -d
curl http://localhost:8080/health
```

## 3. Microsoft Azure Deployment (Bicep IaC)

```powershell
az login
.\infra\azure\deploy.ps1 -ResourceGroup "rg-cloudpass-prod" -Location "centralindia" -SqlAdminLogin "cloudpassadmin"
```

## 4. Environment Variables Reference

| Variable | Description |
|---|---|
| `Database__Provider` | `SqlServer` (Azure SQL / Docker SQL) or `Sqlite` (local quickstart) |
| `ConnectionStrings__DefaultConnection` | SQL Server or SQLite connection string |
| `Security__TicketSigningSecret` | Cryptographic secret (≥32 chars) for HMAC-SHA256 QR token signing |
| `Security__EnforceSingleUseTicketValidation` | Blocks duplicate scans of already boarded tickets (`true`) |
| `PaymentGateway__KeyId` | Razorpay test-mode Key ID |
| `PaymentGateway__KeySecret` | Secret key for server-side HMAC-SHA256 payment signature verification |
| `PaymentGateway__WebhookSecret` | Secret for validating `X-Razorpay-Signature` on `/api/payments/webhook` |
| `PaymentGateway__TestMode` | `true` for sandbox test checkout |
| `Storage__Provider` | `AzureBlob` (production) or `Local` (local development) |
| `Storage__AzureBlobConnectionString` | Azure Storage Account connection string |
| `APPLICATIONINSIGHTS_CONNECTION_STRING` | Azure Application Insights connection string |
