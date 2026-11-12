# WTS client change source bundle — 2026-11-12

Source material supplied by the WTS project owner to implement in the existing deployed WTS website.

## Files

- `docs/client-content/about-tamil.md` — all eight parts, preamble, 30 rules, final pledge, and five recommendations for About page. Retain all content in Tamil; use responsive sections/accordion/table of contents to make it readable.
- `docs/client-content/registration-terms-tamil.md` — all ten registration rules and concluding pledge; show in registration with required informed-consent checkbox, validate on backend and store timestamp/version.
- `client/public/images/verified-sticker.png` — client-provided actual green VERIFIED stamp with alpha transparency, unchanged pixels. Display only for verified/approved membership, never just because a form was submitted.

## Confirmed card validity and renewal policy

On 2026-11-13, the owner confirmed a three-year membership ID card validity and renewal period. About விதி 5 and Registration rule 5 now say renewal every three years. New cards expire three calendar years after the original registration date; existing stored expiry dates remain unchanged.

## Deployment protections

Only safely deploy reviewed/tests-passing changes via the actual deployed GitHub branch/main without force push, never change real member numbers or historical expiry values en masse, never change or expose real officer signature sources or uploaded Aadhaar documents, and ensure ID numbers are unique and immutable for already-issued cards.
