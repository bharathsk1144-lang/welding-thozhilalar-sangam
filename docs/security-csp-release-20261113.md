# WTS security header release — 2026-11-13

## Live baseline and host

The public `www.weldingthozhilalarsangam.com` HTML response is served by Vercel (`Server: Vercel`, `X-Vercel-Id`). Before this release it had HSTS `max-age=63072000`, but no CSP, `X-Content-Type-Options`, `Referrer-Policy`, `Permissions-Policy`, or `X-Frame-Options`. The API at `api.weldingthozhilalarsangam.com` is the existing Render .NET service behind Cloudflare; its public health response lacked these security headers and HSTS. No Cloudflare Pages Functions or Workers serve the frontend.

## Resource evidence and policy

The production Vite HTML loads same-origin module scripts and CSS. Live headless browser checks of Home, About, registration, member/admin login, and verification observed only the Vercel origin and `https://api.weldingthozhilalarsangam.com`. The source also has an explicit `https://welding-thozhilalar-sangam.onrender.com` API fallback if the Vercel build variable is absent. Private photos and officer signatures are fetched from the API and displayed using `blob:` URLs; the card exporter may serialize images as `data:` URLs. Cloudinary is accessed only by the .NET server, so its origin is not allowed in browser CSP. Fonts use installed/local fallback families; no remote font origin is required. Social destinations and district WhatsApp links are user-initiated navigation, not page subresources.

The Vercel policy allows scripts, CSS, and fonts from self; images from self, `blob:`, and `data:`; connections to self and the two exact API origins. It denies inline style attributes, objects, and frames, limits base URLs and form targets to self, denies framing of this page, and upgrades insecure requests. It contains no wildcard source, `unsafe-inline`, or `unsafe-eval`. The API serves no HTML and uses `default-src 'none'` with framing/object/base/form denial.

The policy was deployed first as `Content-Security-Policy-Report-Only` in commit `59366c4`. A headless browser found zero policy violations on the public routes. A separate production-build test against an in-memory synthetic approved member exercised card preview and PNG/PDF downloads with the same policy enforced, with zero violations. The strict style attribute rule did not disrupt the exporter.

## Rollback

The pre-change `main` commit is `1ddcacbc7b97b691701ad4a1143175e71cbb9c22`. If a critical regression appears, revert the enforcement commit on `main` to return to report-only observation. Revert `59366c4` as a second commit if the response headers themselves must be removed. Push normal revert commits; do not reset or force-push. Vercel and the existing Render service auto-deploy pushes to `main`; verify both deployments and public HTTPS responses after rollback. These Git changes do not migrate or delete MongoDB records.

## Remaining limits

No production admin or member credentials are used by these security tests. Authenticated workflows, private document access, and card exports are covered by the isolated .NET/Mongo and synthetic browser suites. The application stores bearer tokens in browser storage; CSP reduces script injection exposure but does not make that storage inaccessible to any script that executes in the page. A scanner score is not a security guarantee.
