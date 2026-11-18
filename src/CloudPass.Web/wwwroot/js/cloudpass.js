// CloudPass Client Interactive Engine
(function () {
  "use strict";

  // Global loading indicator on form submits marked data-loading
  document.addEventListener("submit", function (e) {
    const form = e.target;
    if (form && form.matches("form[data-loading='true']")) {
      const overlay = document.getElementById("cpLoadingOverlay");
      if (overlay) overlay.classList.add("show");
    }
  });

  // Swap Source / Destination cities
  window.cpSwapCities = function (sourceId, destId) {
    const src = document.getElementById(sourceId);
    const dst = document.getElementById(destId);
    if (!src || !dst) return;
    const tmp = src.value;
    src.value = dst.value;
    dst.value = tmp;
  };

  // Load In-App Notifications into Navbar Bell
  async function loadNotifications() {
    const badge = document.getElementById("cpNotifBadge");
    const list = document.getElementById("cpNotifList");
    if (!badge || !list) return;

    try {
      const res = await fetch("/api/notifications", { credentials: "same-origin" });
      if (!res.ok) return;
      const data = await res.json();
      if (data.unreadCount > 0) {
        badge.textContent = data.unreadCount > 9 ? "9+" : data.unreadCount;
        badge.classList.remove("d-none");
      } else {
        badge.classList.add("d-none");
      }

      if (!data.items || data.items.length === 0) {
        list.innerHTML = '<div class="p-3 text-center text-muted small">No notifications yet.</div>';
        return;
      }

      list.innerHTML = data.items
        .map(
          (n) => `
        <div class="dropdown-item py-2 px-3 border-bottom ${n.isRead ? "opacity-75" : "bg-light"}" style="white-space:normal;cursor:pointer;" onclick="cpMarkNotificationRead(${n.id})">
          <div class="d-flex justify-content-between align-items-center mb-1">
            <strong class="small text-dark">${escapeHtml(n.title)}</strong>
            ${!n.isRead ? '<span class="badge bg-info text-dark" style="font-size:0.65rem;">NEW</span>' : ""}
          </div>
          <div class="small text-muted">${escapeHtml(n.message)}</div>
        </div>`
        )
        .join("");
    } catch (_) {
      // ignore background polling errors
    }
  }

  window.cpMarkNotificationRead = async function (id) {
    try {
      await fetch(`/api/notifications/${id}/read`, { method: "POST", credentials: "same-origin" });
      await loadNotifications();
    } catch (_) {}
  };

  window.cpMarkAllNotificationsRead = async function () {
    try {
      await fetch("/api/notifications/read-all", { method: "POST", credentials: "same-origin" });
      await loadNotifications();
    } catch (_) {}
  };

  // Interactive Seat Selection Engine
  window.cpInitSeatSelector = function (config) {
    const selected = new Map(); // id -> seatNumber
    const seatButtons = document.querySelectorAll(".cp-seat-btn[data-seat-id]");
    const listEl = document.getElementById("cpSelectedSeatsList");
    const countEl = document.getElementById("cpSeatCount");
    const baseEl = document.getElementById("cpBaseFare");
    const feeEl = document.getElementById("cpServiceFee");
    const taxEl = document.getElementById("cpTaxAmount");
    const totalEl = document.getElementById("cpTotalFare");
    const proceedBtn = document.getElementById("cpProceedCheckoutBtn");
    const seatIdsInput = document.getElementById("cpSeatIdsInput");

    function renderSummary() {
      const count = selected.size;
      if (countEl) countEl.textContent = count;

      if (listEl) {
        if (count === 0) {
          listEl.innerHTML = '<span class="text-muted small">Click any available seat on the bus layout.</span>';
        } else {
          listEl.innerHTML = Array.from(selected.values())
            .map((s) => `<span class="badge rounded-pill text-bg-primary me-1 mb-1 px-3 py-2">${escapeHtml(s)}</span>`)
            .join("");
        }
      }

      const base = count * config.officialFare;
      const fee = count * config.serviceFee;
      const tax = (base + fee) * (config.taxPercentage / 100);
      const total = base + fee + tax;

      if (baseEl) baseEl.textContent = formatInr(base);
      if (feeEl) feeEl.textContent = formatInr(fee);
      if (taxEl) taxEl.textContent = formatInr(tax);
      if (totalEl) totalEl.textContent = formatInr(total);
      if (seatIdsInput) seatIdsInput.value = Array.from(selected.keys()).join(",");
      if (proceedBtn) proceedBtn.disabled = count === 0;
    }

    seatButtons.forEach((btn) => {
      btn.addEventListener("click", function () {
        if (btn.disabled) return;
        const id = btn.getAttribute("data-seat-id");
        const num = btn.getAttribute("data-seat-number");

        if (selected.has(id)) {
          selected.delete(id);
          btn.classList.remove("selected");
        } else {
          if (selected.size >= 6) {
            alert("You can select up to 6 seats in a single booking.");
            return;
          }
          selected.set(id, num);
          btn.classList.add("selected");
        }
        renderSummary();
      });
    });

    renderSummary();
  };

  // Bus Pass Server-Side Price Estimator
  window.cpUpdatePassPrice = async function () {
    const routeEl = document.getElementById("passRouteSelect");
    const durEl = document.getElementById("passDurationSelect");
    const previewEl = document.getElementById("passPricePreview");
    if (!routeEl || !durEl || !previewEl || !routeEl.value) return;

    try {
      const res = await fetch(`/api/fares/pass?routeId=${encodeURIComponent(routeEl.value)}&durationMonths=${encodeURIComponent(durEl.value)}`);
      if (!res.ok) return;
      const p = await res.json();
      previewEl.innerHTML = `
        <div class="d-flex justify-content-between small mb-1">
          <span>Route Base (${p.durationMonths} mo × INR ${p.monthlyBasePrice.toFixed(2)})</span>
          <strong>INR ${(p.monthlyBasePrice * p.durationMonths).toFixed(2)}</strong>
        </div>
        ${p.discountPercentage > 0 ? `<div class="d-flex justify-content-between small text-success mb-1"><span>Multi-Month Discount (${p.discountPercentage}%)</span><strong>- INR ${p.discountAmount.toFixed(2)}</strong></div>` : ""}
        <hr class="my-2"/>
        <div class="d-flex justify-content-between align-items-center">
          <span class="fw-bold text-dark">Official Server Pass Price</span>
          <span class="fs-5 fw-bold text-primary">INR ${p.finalPrice.toFixed(2)}</span>
        </div>`;
    } catch (_) {}
  };

  // Razorpay Sandbox Test-Mode Checkout Trigger
  window.cpExecuteTestPayment = async function (bookingRef, orderId, method) {
    const overlay = document.getElementById("cpLoadingOverlay");
    if (overlay) overlay.classList.add("show");
    try {
      const res = await fetch("/api/payments/test-authorize", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        credentials: "same-origin",
        body: JSON.stringify({
          bookingReference: bookingRef,
          gatewayOrderId: orderId,
          paymentMethod: method
        })
      });
      if (!res.ok) {
        if (overlay) overlay.classList.remove("show");
        alert("Unable to authorize sandbox payment. Please refresh and try again.");
        return;
      }
      const data = await res.json();
      document.getElementById("cpGatewayPaymentId").value = data.gatewayPaymentId;
      document.getElementById("cpGatewaySignature").value = data.gatewaySignature;
      document.getElementById("cpConfirmPaymentForm").submit();
    } catch (err) {
      if (overlay) overlay.classList.remove("show");
      alert("Network error during sandbox payment authorization.");
    }
  };

  function formatInr(val) {
    return "INR " + Number(val || 0).toLocaleString("en-IN", { minimumFractionDigits: 2, maximumFractionDigits: 2 });
  }

  function escapeHtml(str) {
    return String(str || "")
      .replace(/&/g, "&amp;")
      .replace(/</g, "&lt;")
      .replace(/>/g, "&gt;")
      .replace(/"/g, "&quot;");
  }

  document.addEventListener("DOMContentLoaded", function () {
    loadNotifications();
  });
})();
