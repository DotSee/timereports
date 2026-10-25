// Behavior for the standalone report pages (Daily Time Ledger, Behind Pace).
// The server renders the results partial; this only wires the controls, fetches an
// updated partial on interaction, and swaps it in. Configuration is read from
// data-* attributes on <body> so the view carries no inline script.
(function () {
  const INCLUDE_WEEKENDS = document.body.dataset.includeWeekends === 'true';

  const results = document.getElementById('results');
  const datePicker = document.getElementById('datePicker');
  const excludeInput = document.getElementById('excludeMgmt');
  const goalInput = document.getElementById('goalHours');   // absent when goals are off
  const daysInput = document.getElementById('periodDays');  // Behind Pace only
  const applyBtn = document.getElementById('applyBtn');      // absent when goals are off
  const prevBtn = document.getElementById('prevBtn');
  const nextBtn = document.getElementById('nextBtn');
  const todayBtn = document.getElementById('todayBtn');
  const stamp = document.getElementById('dateStamp') || document.getElementById('windowStamp');

  function toISODate(d) {
    const y = d.getFullYear();
    const m = String(d.getMonth() + 1).padStart(2, '0');
    const day = String(d.getDate()).padStart(2, '0');
    return `${y}-${m}-${day}`;
  }

  // Copy server-rendered chrome (period stamp, legend goal) after each render.
  function syncChrome() {
    const st = document.getElementById('stampText');
    if (stamp && st) stamp.textContent = st.textContent;
    const legendHrs = document.getElementById('goalLegendHours');
    if (legendHrs && goalInput) legendHrs.textContent = goalInput.value;
  }

  // Fetch the results partial for the current controls and swap it in.
  // A sequence token guards against out-of-order responses from rapid interactions.
  let reqSeq = 0;
  async function refresh() {
    const mine = ++reqSeq;
    const s = document.getElementById('status'); if (s) s.textContent = 'Loading…';
    const p = new URLSearchParams();
    p.set('date', datePicker.value);
    if (daysInput) p.set('days', daysInput.value);
    if (goalInput) p.set('goal', goalInput.value);
    p.set('exclude', excludeInput.checked ? 'true' : 'false');
    const html = await (await fetch('?handler=Results&' + p.toString())).text();
    if (mine !== reqSeq) return; // superseded by a newer refresh
    results.innerHTML = html;
    syncChrome();
  }

  function shiftDay(delta) {
    const current = new Date(datePicker.value + 'T12:00:00');
    do {
      current.setDate(current.getDate() + delta);
    } while (!INCLUDE_WEEKENDS && (current.getDay() === 0 || current.getDay() === 6));
    datePicker.value = toISODate(current);
    refresh();
  }

  if (prevBtn) prevBtn.addEventListener('click', () => shiftDay(-1));
  if (nextBtn) nextBtn.addEventListener('click', () => shiftDay(1));
  if (todayBtn) todayBtn.addEventListener('click', () => { datePicker.value = toISODate(new Date()); refresh(); });
  datePicker.addEventListener('change', refresh);
  excludeInput.addEventListener('change', refresh);
  if (applyBtn) applyBtn.addEventListener('click', refresh);

  // init — the results partial is rendered server-side inline; just sync the chrome.
  syncChrome();
})();
