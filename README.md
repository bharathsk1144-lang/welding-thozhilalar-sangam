# Welding-Thozhilalar-Sangam

Full-stack Tamil Nadu district-wise Welding Thozhilalar Sangam membership registration website.

## Stack

- Frontend: React + Vite
- Backend: ASP.NET Core .NET 8 Web API
- Database: MongoDB
- Uploads: local backend storage at `server/wwwroot/uploads`
- Admin auth: JWT
- ID card download: `html2canvas` + `jsPDF`

## Final Registration Flow

1. User opens New User Registration.
2. User enters name and an Indian mobile number. When registration OTP is enabled, the user sends and verifies OTP.
3. User fills member details and password/confirmation. Name and mobile are carried from the first step; OTP verification locks them when enabled.
4. User joins/confirms the district WhatsApp group when the district link is configured.
5. User uploads Aadhaar card photo and Passport Size Photo.
6. User accepts consent for membership verification.
7. Submit saves the member in MongoDB, stores only `passwordHash`, generates the member ID immediately, and opens the membership ID card page.
8. User can download the ID card as `WTS-IDCARD-{MemberId}.pdf`.

The previous QR/payment workflow is removed from the normal user flow. Payment-related backend fields and old components may remain for backward compatibility, but users no longer need payment verification before seeing the ID card.

## OTP Behavior

Backend OTP delivery now fails closed in every environment. With SMS unavailable, send returns 503 and no fixed code can verify. Tests inject a mock sender; no mock/test endpoint is part of the API. Only the explicitly built, frontend-only client demo uses `123456`, without accounts, sessions, database or SMS access (`npm run build:client-demo`).

Registration OTP is controlled by the API's `Authentication:OtpEnabled` setting. It is `false` for the November 8 deployment; the registration page reads the setting from the API. Registrations made with OTP disabled store `MobileVerified=false`. Forgot-password OTP remains required and does not send or accept recovery proofs for explicitly unverified mobiles. Set the API setting to `true` to require registration OTP again. Normal user login uses registered mobile number and password.

Future provider configuration uses backend environment variables or matching `Sms` appsettings values. Keep delivery disabled until an official adapter and its contract tests are approved:

```text
SMS_ENABLED=false
SMS_PROVIDER=
SMS_API_KEY=
SMS_SENDER_ID=
SMS_TEMPLATE_ID=
```

The existing ISmsSender interface is retained. Guessed Fast2SMS/MSG91 HTTP adapters have been removed; configuration alone sends nothing. OTPs expire after 5 minutes and are stored hashed in memory. See [OTP integration](docs/otp-provider-integration-guide.md), [readiness status](docs/production-readiness-checklist.md), [deployment preparation](docs/deployment-guide.md) and [handover](docs/client-handover-checklist.md). Production is not yet approved or ready.

## ID Card

The ID card is a union membership card, not a government ID. It shows the Sangam logo, English and Tamil title, Passport Size Photo, member details, issue date, validity year, active status, verification status, Verified Member ribbon, and authorized signature area.

Aadhaar card photo is stored only for admin document review and is not returned by the ID card API.

## User Login And Re-download

Registered users can open `/user-login`, login with mobile number and password, and view/download their ID card later from `/my-id-card`. Existing old users without a password can use `/forgot-password` after an approved OTP provider is enabled, provided their mobile number is verified.

## Public Member Verification

Public route `/verify-member` verifies a Member ID and shows only safe fields: member name, member ID, district, status, verification status, validity, and Sangam name. It does not show Aadhaar, phone, address, DOB, father name, or document URLs.

## Admin Export

Admin dashboard includes `Export Users`, which downloads an Excel-compatible CSV named `WTS-Members-Export-{yyyyMMdd}.csv`. The export excludes Aadhaar images, Passport Size Photo files, document URLs, and OTP data.

The export also excludes passwords and `passwordHash`.

## Vercel Frontend Deployment

Vercel is for the React + Vite frontend only. The ASP.NET Core backend API must be hosted separately on Render, Railway, a VPS, AWS, Azure, or similar.

Recommended Vercel settings:

- Root directory: `client`
- Build command: `npm run build`
- Output directory: `dist`
- Environment variable: `VITE_API_BASE_URL=https://BACKEND-LIVE-URL/api`

`client/vercel.json` contains SPA rewrites for routes such as `/user-login`, `/forgot-password`, `/my-id-card`, and `/verify-member`.

See the [staging link checklist](docs/staging-vercel-api-checklist.md) for the separate API, private upload storage, isolated database, and host-side checks.

## Specs

Clean module-by-module specs are in `docs/specs/`. Future changes should reference the exact spec file, for example `docs/specs/03-id-card-spec.md`, instead of rewriting the whole project.

## Run Locally

1. Start MongoDB locally on `mongodb://localhost:27017`.
2. From the project root in PowerShell:

```powershell
.\start-demo.ps1
```

The script starts the backend on `5087` and Vite on `5173`.

Demo URLs:

- Frontend: `http://localhost:5173`
- Backend check: `http://localhost:5087/api/districts`

Stop the demo with:

```powershell
.\stop-demo.ps1
```

### Manual Startup

Backend:

```bash
dotnet restore server/WeldingThozhilalarSangam.Api.csproj
dotnet run --project server/WeldingThozhilalarSangam.Api.csproj --urls "http://0.0.0.0:5087"
```

Frontend:

```bash
cd client
npm install
npm run dev -- --host 0.0.0.0 --port 5173 --strictPort
```

## Development Admin Login

- Username: `admin`
- Email: `admin@wts.com`
- Password: `Admin@123`

Change these values and the JWT key in `server/appsettings.json` before production use.

## MongoDB Collections

- `Users`: all registrations, upload paths, member IDs, and district values.
- `Districts`: district name and WhatsApp group link.

No district-specific user tables are created.

## Backend API

- `POST /api/auth/send-otp`
- `POST /api/auth/verify-otp`
- `POST /api/users/register`
- `POST /api/users/login`
- `POST /api/users/forgot-password/send-otp`
- `POST /api/users/forgot-password/reset`
- `GET /api/users/verify/{memberId}`
- `GET /api/users`
- `GET /api/users/{id}`
- `GET /api/users/{id}/id-card`
- `GET /api/users/by-district/{district}`
- `POST /api/admin/login`
- `GET /api/admin/users/export`
- `GET /api/dashboard/summary`
- `GET /api/districts`
- `POST /api/districts`
- `PUT /api/districts/{id}`
