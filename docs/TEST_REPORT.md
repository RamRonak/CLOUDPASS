# CloudPass Verification, Security, Reliability & Performance Test Report

**Test Suite:** `tests/CloudPass.Tests` (xUnit + `Microsoft.AspNetCore.Mvc.Testing` full-stack integration host)  
**Test Execution Date:** 2026-11-17  
**Runtime:** .NET 10.0.12 (ASP.NET Core 10, 64-bit Windows)  
**Result Summary:** **13 / 13 Passed (100% Pass Rate)** in **21.09 seconds** (across 3 isolated test host instances with full DB schema & seed initialization)

> All figures below are **verbatim measured results** from running `dotnet test tests/CloudPass.Tests/CloudPass.Tests.csproj --no-build --logger "console;verbosity=detailed"` against the live ASP.NET Core MVC application pipeline.

## 1. Functional Testing Results

| Test ID | Test Case | Outcome | Duration |
|---|---|---|---|
| FT-01 | `HomePage_LoadsSuccessfully_WithBrandAndPopularRoutes` | **PASS** | 74 ms |
| FT-02 | `BusSearch_FiltersByRouteAndAC_ReturnsDatabaseSchedules` | **PASS** | 118 ms |
| FT-03 | `UserLogin_FormSubmission_AuthenticatesSuccessfully` | **PASS** | 204 ms |
| FT-04 | `FullEndToEnd_Booking_Payment_TicketPdf_QrVerification_AndCancellation` | **PASS** | 1 s |
| FT-05 | `BusPassWorkflow_Apply_AdminApprove_AndVerifySignedQrPass` | **PASS** | 17 s *(incl. host cold start)* |
| FT-06 | `HealthEndpoints_ReturnHealthyStatusJson` | **PASS** | 190 ms |

## 2. Security & Concurrency Testing Results

| Test ID | Test Case | Outcome | Duration |
|---|---|---|---|
| ST-01 | `Security_PriceTamperingAttempt_IsBlockedAndAuditLogged` | **PASS** (`PRICE_TAMPERING_BLOCKED`) | 122 ms |
| ST-02 | `Concurrency_TenSimultaneousBookingsForSameSeat_ExactlyOneSucceeds` | **PASS** (1 winner, 9 `SeatConflict`) | 212 ms |
| ST-03 | `Security_ForgedQrToken_AndSingleUseDuplicateScan_AreBlocked` | **PASS** (`INVALID_SIGNATURE` & `ALREADY_USED`) | 322 ms |
| ST-04 | `Security_InvalidPaymentWebhook_IsRejected_AndValidSignedWebhook_ConfirmsBooking` | **PASS** (`401` on invalid, `200` on valid HMAC) | 1 s |
| ST-05 | `Security_IDOR_And_RBAC_BlockUnauthorizedTicketAndAdminAccess` | **PASS** (Blocked IDOR & `/Admin`) | 16 s *(incl. host cold start)* |

## 3. Performance & Concurrent Load Benchmark (Measured)

- **Requests:** `120` across 4 live endpoints (`/`, `/Bus/Search?...`, `/health/ready`)
- **Concurrent Virtual Workers:** `20`
- **Failed Requests:** `0` (`0.00%` error rate)
- **Total Elapsed Time:** `1212.4 ms`
- **Measured Throughput:** **`99.0 req/sec`**
- **Latency:** Avg **`189.69 ms`**, P50 **`127.94 ms`**, P95 **`653.14 ms`**, P99 **`690.03 ms`**

## 4. Reliability Testing Results

| Test ID | Test Case | Outcome |
|---|---|---|
| RT-01 | `Reliability_InvalidResourceAndErrorHandling_RespondGracefully` | **PASS** (14 s incl. host cold start) |
| RT-02 | `HealthEndpoints_ReturnHealthyStatusJson` | **PASS** (190 ms) |
