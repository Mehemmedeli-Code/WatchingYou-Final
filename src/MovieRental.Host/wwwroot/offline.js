// The offline page's behaviour, in a file of its own so the page needs no inline script
// (the site's Content-Security-Policy allows none).
document.getElementById("retry").addEventListener("click", function () { location.reload(); });
// This page is shown whenever the site could not be reached — which is not always the
// visitor's internet. When the device is online, the server itself is down or restarting,
// so say that instead, and come back on our own the moment it answers again.
(function () {
  if (!navigator.onLine) {
    window.addEventListener("online", function () { location.reload(); });
    return;
  }
  document.getElementById("title").textContent = "Server cavab vermir · Server not responding";
  document.getElementById("lede").textContent =
    "İnternetiniz var, amma sayt hələ açılmayıb və ya yenidən başladılır. Hazır olan kimi səhifə özü açılacaq. · " +
    "You are online, but the site is starting or restarting. This page reloads itself as soon as it is back.";
  setInterval(function () {
    fetch("/api/health", { cache: "no-store" })
      .then(function (r) { if (r.ok) location.reload(); })
      .catch(function () { /* still down */ });
  }, 3000);
})();
