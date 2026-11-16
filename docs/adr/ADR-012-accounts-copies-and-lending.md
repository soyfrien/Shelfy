# ADR-012: Accounts, physical copies and lending by mail

- Status: Proposed (PR `feature/accounts-lending`, not deployed)
- Date: 2026-11-15

## Context

Shelfy becomes an internet library: people sign in, open a friend's shelf, tap a book and ask to borrow or reserve it; the book is
mailed (snail mail) to them, goes on their shelf, and later travels back. A book is always its owner's property, only lent out.
Two things the model had no room for:

- **A physical book needs its own identity.** `Book` is the bibliographic record keyed by ISBN and exists once for everyone. Two people
  who own *The Hobbit* share one `Book` row, and one person can own two copies. A book that moves from shelf to shelf must keep the same
  identity on the way, and a person holding it must be able to say "this one" without typing a GUID.
- **Lending needs accounts.** Until now the API was open with anonymous guests (ADR-006 planned Entra External ID, never wired up).
  Borrowing involves postal addresses, so it needs real users and authorization on every step.

## Decision

### 1. Title vs physical copy

- `Book` stays the title/edition (ISBN). New **`Copy`** = one physical book: `Id` (Guid, permanent), **`Code`** (8 characters of Crockford
  base32, `0-9A-Z` without `I L O U`, 40 random bits from a cryptographic RNG, unique index, shown as `7K3M-Q9XA`), `BookId`, `OwnerId`
  (never changes), `HomeShelfId` (the owner's shelf it returns to), `CurrentHolderId`, `CurrentShelfId` (null while in the mail),
  `Status` (`Available`, `Reserved`, `OnLoan`, `InTransit`, `Unavailable`), `AllowPassAlong` (default off), optional `Condition`/`Notes`,
  and a `Version` concurrency token bumped by every lending step so two racing steps on one copy cannot both win (works on PostgreSQL and SQLite).
- Codes are read forgivingly (case, spaces and dashes ignored; `O` reads as `0`, `I`/`L` as `1`). On a clash with an existing code the
  handler draws another (up to 5 times; a clash is about 1 in 10^12 per code at today's scale).
- **`CopyMovement`** records the journey: one row per transfer (`Registered`, `Lent`, `PassedAlong`, `Returned`) with from user/shelf, to
  user, loan id, `StartedAt` (mailed) and `ReceivedAt` + `ToShelfId` (arrived). Rows are never deleted; user and shelf ids are plain
  columns (no FK) so deleting a shelf never erases history.
- **Link to recognition (minimal, opt-in):** recognized or hand-fixed books already land on the shelf as `ShelfBook`s that are `Private`
  (docs/domain.md). The owner of an **account** shelf taps a book and chooses **"Lend this book"**
  (`POST /api/shelves/{shelfId}/books/{bookId}/copies`), which creates the `Copy` (home = that shelf), a `Registered` movement, and flips the
  `ShelfBook` to `Loan`. "I have another copy" registers a second one. The recognition worker is unchanged: copies are not created
  automatically, because a photo is not proof of ownership and books default to Private.
- Deleting a shelf (ADR-010) keeps its copies (`HomeShelfId`/`CurrentShelfId` are set to null by the FK).

### 2. Accounts

- **ASP.NET Core Identity, email + password**, stored in the same `ShelfyDbContext` (now an `IdentityDbContext`, tables `AspNet*`).
  An account (`ShelfyAccount`, `AspNetUsers`) has the **same Id as its domain `User`** (FK, one-to-one), so shelves, copies and loans keep
  pointing at `Users`. `User` gains a private `MailingAddress`. Display name and city are public.
- **One cookie** (`shelfy.auth`, HttpOnly, SameSite=Lax, Secure over HTTPS, 30 days sliding) for the pages and the API; `/api/*` answers
  401/403 instead of redirecting. No bearer tokens yet: the only client is the same-origin web UI. When the MAUI app arrives, add
  Identity bearer tokens (or Entra, ADR-006) next to the cookie; the handlers only read the user id from the principal.
- CSRF: SameSite=Lax keeps the cookie off cross-site POSTs, and JSON bodies cannot be sent cross-site without a CORS preflight (no CORS is enabled).
- Passwords: at least 8 characters, no composition rules; lockout for 10 minutes after 8 failures.
- **Confirmed email (decided by Louis, Nov 15, 2026).** Registering emails a link (`GET /api/account/confirm-email?userId=&code=`, an Identity
  token, works without being signed in, then redirects to `/signin.html?confirmed=1|0`). Borrowing, reserving and approving a loan answer 400
  until the email is confirmed; approving is included because it reveals the borrower's address. Listing a copy does not need it.
  `POST /api/account/confirm-email/resend` (signed in, once a minute). `GET /me` has `emailConfirmed`; pages show a bar with "Send it again".
  Mail goes through `IMailer`: `Email:Provider=Acs` (Azure Communication Services Email, the production choice: Exchange Online is retiring
  password SMTP sign-in), `Smtp` (any relay with a login, STARTTLS) or `Log` (default, writes the email to the log; development only). If sending fails, registration still succeeds and the person can resend.
- Endpoints: `POST /api/account/register`, `/login`, `/logout`, `GET /api/account/me` (the only response with your own address, plus your shelves),
  `PUT /api/account/profile`.
- Data-protection keys (they sign the cookie) persist to `DataProtection:KeysPath` when set, so sign-ins survive restarts.

### 3. Anonymous shelves ("dark shelves")

**Left as they are, never claimable** (decided by Louis, Nov 15, 2026: they become *dark shelves*, to do something special with later). Guest shelves keep working without sign-in exactly as before (upload, fix, search, hide,
delete). A shelf created while signed in belongs to the account, and changing it (upload photos, tap to fix, hide, delete) needs its owner
(`ShelfOwnerFilter`: 401 anonymous, 403 someone else); reads stay public. Creating a shelf for a user who has an account needs that account.
Only account shelves can lend. Claiming a guest shelf was left out because the guest id is public (`GET /api/shelves/{id}` returns `userId`),
so "claim by guest id" would let anyone who saw a shared shelf take it; a safe claim needs a secret only the guest's browser holds.

### 4. Lending state machine (`Shelfy.Domain.Lending`)

`Loan`: `CopyId`, `OwnerId`, `BorrowerId`, `SenderId` (who mails it to the borrower), `PreviousLoanId` (pass-along), `Status`, optional
`Message`, timestamps. Every step names the acting user and throws `LendingException` (Forbidden 403 / Conflict 409 / Invalid 400).

| Step | Who | From | To | Copy |
|---|---|---|---|---|
| request borrow | anyone but the owner, with a mailing address | copy `Available` | `Requested` | `Reserved` |
| reserve | anyone but the owner/holder | copy requested, on loan or in the mail | `Waitlisted` (FIFO) | |
| approve | owner (needs own address) | `Requested` | `Approved` | |
| decline | owner | `Requested` | `Declined` | released |
| cancel | borrower or owner | `Waitlisted`/`Requested`/`Approved` | `Cancelled` | released |
| mailed | sender (owner, or previous borrower) | `Approved` | `Mailed` | `InTransit`, movement opened |
| received | borrower (scan `/c/{code}`) | `Mailed` | `Received` | `OnLoan`, on the borrower's shelf, movement closed |
| request return | owner | `Received` | `ReturnRequested` | |
| return mailed | borrower | `Received`/`ReturnRequested` | `ReturnMailed` | `InTransit`, movement to owner |
| returned | owner (or scan) | `ReturnMailed` | `Returned` | home shelf, `Available`, released |
| pass along | owner, copy `AllowPassAlong` | current `Received`/`ReturnRequested` | next waiting loan `Approved`, sender = current borrower | when mailed: current loan `PassedOn` |

- **Return to owner is the default.** "Released" means: when the copy is home and nothing is in progress, the oldest `Waitlisted` loan
  becomes `Requested` (the owner approves it like any request); otherwise the copy is `Available`.
- **Pass-along** only if the owner switched it on for that copy *and* approves each hand-off. The next borrower later returns the copy to the
  owner, not to the previous borrower. Cancelling an approved hand-off leaves the copy with the current borrower, who can then mail it home.
- The owner can mark a copy `Unavailable` (and back) while it is home with nothing in progress; waitlist places are kept.
- Loans are visible only to their parties (owner, borrower, pass-along sender); anyone else gets 404.

### 5. Mailing addresses

Never in any public response. Only while a loan is approved and in progress (`Approved` … `ReturnMailed`), the owner and the borrower see each
other's address, and a pass-along sender sees the borrower's address until it is mailed. Copy history shows display names only, never addresses
or borrowers' shelf ids.

### 6. Label and the copy page

- `/c/{code}` is what a sticker points to: the copy, its status and journey, and the right button for the viewer: **"I received this"** for the
  borrower of the in-transit loan (moves it onto their shelf), "I got it back" for the owner when it comes home, Borrow/Reserve for others,
  a sign-in prompt for anonymous visitors. Non-canonical spellings redirect to the canonical URL; unknown codes give 404.
  The API behind the button is `POST /api/copies/by-code/{code}/received`.
- `GET /api/copies/{id}/label.svg` draws the sticker: a QR code of `{origin}/c/{CODE}` plus the code and title. The QR encoder is ~400 lines in
  `Lending/Labels/QrCode.cs` (byte mode, level M, versions 1-9, after Project Nayuki's reference algorithm) instead of a package; generated codes
  were checked with the zxing decoder. `Lending:PublicBaseUrl` overrides the origin.

### 7. API (new)

```text
POST /api/shelves/{shelfId}/books/{bookId}/copies   lend this book (shelf owner)            201 copy
GET  /api/shelves/{shelfId}/copies                  copies home on / visiting a shelf, with what the caller may do
GET  /api/copies/{copyId}  |  /api/copies/by-code/{code}   copy detail + history (public, no addresses)
PUT  /api/copies/{copyId}                           owner: available, allowPassAlong, condition, notes
GET  /api/copies/{copyId}/label.svg                 printable QR label
POST /api/copies/{copyId}/borrow | /reserve         body { message? }                       201 loan
POST /api/copies/by-code/{code}/received            body { shelfId? }  "I received this" / "I got it back"
GET  /api/loans[?all=true]                          { borrowing: [...], lending: [...] }  (open loans unless all)
GET  /api/loans/{loanId}                            parties only
POST /api/loans/{loanId}/{approve|decline|cancel|mailed|received|request-return|return-mailed|returned|pass-along}
```

Responses carry `actions`: the steps the caller can take now (for buttons; the state machine still checks).

## Consequences

- One migration, `AddAccountsAndLending`: `AspNet*` tables, `Copies`, `CopyMovements`, `Loans`, `Users.MailingAddress`. Purely additive; the
  previous build runs against the new schema, so apply the SQL first. Verified on PostgreSQL 12.
- ADR-006 (Entra External ID) is deferred, not rejected: Entra can be added as an external login on top of Identity.
- No notifications yet: people see requests on "My loans". Email (requests, approvals, "it's in the mail") is the obvious next step, and
  password reset can now reuse `IMailer`.
- Shipping costs, lost or damaged books, due dates and reminders are out of scope. `Loan` has the timestamps to add due dates later.
- The shelf page still lists `ShelfBook`s; copies borrowed from someone else appear as extra "Borrowed from …" cards.
- Decided by Louis on Nov 15, 2026: pass-along stays **off by default** per copy; guest shelves stay unclaimable (dark shelves); borrowing
  needs a confirmed email.
