// Accounts and lending helpers shared by every page (ADR-012). Adds "Sign in" / "My loans" to the page header, and exposes
// window.ShelfyAccount = { api, me, signOut, statusText, el }. Everything from the API goes in with textContent, never innerHTML.
(function () {
  'use strict';
  function api(method, url, body) {
    return fetch(url, {
      method: method, credentials: 'same-origin',
      headers: body ? { 'Content-Type': 'application/json', Accept: 'application/json' } : { Accept: 'application/json' },
      body: body ? JSON.stringify(body) : undefined
    }).then(function (r) {
      return r.text().then(function (t) {
        var data = null; try { data = t ? JSON.parse(t) : null; } catch (e) { /* not JSON */ }
        if (!r.ok) { var err = new Error((data && data.error) || ('HTTP ' + r.status)); err.status = r.status; throw err; }
        return data;
      });
    });
  }
  var mePromise = null;
  function me(refresh) {
    if (!mePromise || refresh) mePromise = api('GET', '/api/account/me').catch(function () { return null; });
    return mePromise;
  }
  function signOut() {
    return api('POST', '/api/account/logout').then(function () {
      // The last shelf was the account's; a signed-out visitor starts a fresh guest shelf instead of hitting 401 on it.
      try { localStorage.removeItem('shelfy.shelfId'); } catch (e) { /* storage off */ }
      location.href = '/';
    });
  }
  function el(tag, cls, text) {
    var e = document.createElement(tag); if (cls) e.className = cls; if (text != null) e.textContent = text; return e;
  }
  var STATUS = {
    Available: 'Available to borrow', Reserved: 'Requested', OnLoan: 'On loan', InTransit: 'In the mail', Unavailable: 'Not lending right now',
    Waitlisted: 'On the waitlist', Requested: 'Waiting for the owner', Approved: 'Approved, to be mailed', Declined: 'Declined',
    Cancelled: 'Cancelled', Mailed: 'In the mail', Received: 'Received', ReturnRequested: 'Owner asked for it back',
    ReturnMailed: 'Mailed back', Returned: 'Returned', PassedOn: 'Passed on to the next reader'
  };
  function statusText(s) { return STATUS[s] || s; }
  function signInUrl() { return '/signin.html?next=' + encodeURIComponent(location.pathname + location.search); }

  // Header links.
  var header = document.querySelector('header.top');
  if (header) {
    var nav = el('nav', 'acct');
    header.appendChild(nav);
    me().then(function (u) {
      if (u) {
        var loans = el('a', null, 'My loans'); loans.href = '/loans.html';
        var out = el('button', 'link', 'Sign out'); out.type = 'button'; out.onclick = signOut;
        nav.appendChild(loans); nav.appendChild(out);
        if (!u.emailConfirmed) {
          // Borrowing, reserving and approving need a confirmed email.
          var bar = el('p', 'notice confirmbar', 'Confirm your email to borrow, reserve, or approve requests. We sent a link to ' + u.email + '. ');
          var again = el('button', 'link', 'Send it again'); again.type = 'button';
          again.onclick = function () {
            again.disabled = true;
            api('POST', '/api/account/confirm-email/resend').then(function () { again.textContent = 'Sent. Check your inbox.'; })
              .catch(function (x) { again.textContent = x.message + ' Send it again'; again.disabled = false; });
          };
          bar.appendChild(again);
          header.parentNode.insertBefore(bar, header.nextSibling);
        }
      } else {
        var a = el('a', null, 'Sign in'); a.href = signInUrl(); nav.appendChild(a);
        // Sign-up: Shelfy calls a new account a Library card.
        var forge = el('a', 'forge', 'Forge Library card'); forge.href = signInUrl() + '#register'; nav.appendChild(forge);
      }
    });
  }
  window.ShelfyAccount = { api: api, me: me, signOut: signOut, statusText: statusText, el: el, signInUrl: signInUrl };
})();
