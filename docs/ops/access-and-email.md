# Access and email

In Production, visitors enter an email address and receive an eight-character reusable login code. The application stores a keyed hash and issues a cookie after validating the pair. Returning visitors use the same code until it is replaced. Cookies expire at the next UTC midnight, with an optional shorter maximum age.

Set `DEMO_SENDGRID_API_KEY`, `DEMO_MAIL_FROM` to a verified sender, and optionally `DEMO_MAIL_NAME`. Use a sender you operate. In local Development without a SendGrid key, delivery is logged for testing; keep those logs private. The default Compose bypass makes email unnecessary for local evaluation.

Configure separate operator credentials to use `/admin`. The page supports issuing a code, blocking/unblocking a visitor, changing allowances, and revoking cookies. Issued codes are displayed once; keep the admin session private. The admin cookie lasts 30 minutes and state-changing forms require antiforgery tokens.

Blocking persists in SQLite and is checked through a short cache. Global cookie revocation and some rate-limit state are in memory and reset on restart. Gate-secret rotation invalidates existing codes and cookies and changes pseudonymous IDs. Back up the database and its matching secret securely, and plan code reissuance after rotation.

Set your own retention and privacy policy for visitor emails and audit data. The software does not create an automatic deletion schedule for you.
