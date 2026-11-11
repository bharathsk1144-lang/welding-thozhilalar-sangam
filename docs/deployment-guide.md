# WTS deployment preparation — no deployment performed

See [private cloud storage and recovery gates](private-cloud-storage-runbook.md) for the Cloudinary provider and Atlas M0 restore procedure.

Current zero-cost deployment target: Cloudflare Pages Free for the frontend, Render Free Docker for the API, Atlas M0 for MongoDB, and Cloudinary Free authenticated assets for private uploads. The options table below records earlier evaluations; its pending-storage descriptions predate the implemented Cloudinary adapter. No external deployment has been performed from this branch.

## Required decisions

The frontend is static React/Vite. The .NET API needs a persistent running host; MongoDB and private uploads need durable, backed-up storage. Do not deploy the Cloudflare client-preview artifact as the live member application. Keep preview and production build/configuration separate. Official domain: `www.weldingthozhilalarsangam.com`; API hostname/origin and DNS remain unconfigured until approved.

## Hosting choices and free-tier limits (checked 2026-10-24)

| Approach | Suitable use | Limits/costs and decision |
|---|---|---|
| Cloudflare Pages static frontend | Candidate for the public React build | Free-plan quotas, including 500 builds/month and file/project limits; does not run an ASP.NET Core server. Custom-domain setup still needs account/domain/DNS approval. [Official limits](https://developers.cloudflare.com/pages/platform/limits/) |
| Render free Docker API + Atlas free database | Evaluation/staging with external private storage | API sleeps after 15 minutes and shares 750 free instance hours/workspace/month; no persistent disk on free web services. OTP state is in-process and is lost on sleep/restart. Uploaded documents must never rely on its ephemeral filesystem. [Render free](https://render.com/docs/free), [disks](https://render.com/docs/disks) |
| MongoDB Atlas free cluster | Small trial database | Storage, throughput and transfer quotas; backup availability/charging varies. Current optional Free-cluster backups are a paid private-preview feature, not a guaranteed free recovery plan. Arrange verified offsite backup/restore independently. [Limits](https://www.mongodb.com/docs/atlas/reference/free-shared-limitations/), [backup feature](https://www.mongodb.com/docs/atlas/backup/cloud-backup/free-cluster-backup/) |
| Oracle Always Free VM, if available | Possible no-subscription single-host API/private volume | Account/identity verification, capacity scarcity and idle reclamation; availability/entitlements vary. Operator must patch, harden, monitor and back up the VM. No free-uptime promise. [Oracle FAQ](https://www.oracle.com/cloud/free/faq/) |
| Cloudflare R2/private object storage | Candidate private media store after reviewed integration | Free allowances do not imply unlimited free storage/operations; overages and provider account/payment requirements need approval. Never publish Aadhaar in a public bucket. Current code uses protected local files; object-store integration is pending selection. [Pricing](https://developers.cloudflare.com/r2/pricing/) |
| Vercel Hobby frontend | Eligibility-dependent | Hobby is restricted to personal/non-commercial use; do not promise a paid client organization project qualifies. Confirm terms or budget for an eligible paid plan. [Hobby](https://vercel.com/docs/plans/hobby) |
| Existing local PC + Quick Tunnel | Temporary client design review only | PC/power/internet/process dependent, changing hostname and no uptime guarantee; not durable production hosting. [Cloudflare](https://developers.cloudflare.com/tunnel/get-started/quick-tunnels/) |
| Small paid VM/managed API + durable storage | More predictable production option | Recurring compute/storage/backup cost plus operations. Choose only after budget/account approval. Domain renewal and real OTP delivery are separate unavoidable operating expenses unless subsidized. |

There is no validated promise of reliable, secure, permanently zero-cost full-stack operation. Prefer a free static frontend if eligible, then explicitly decide how to fund/operate durable API, database, documents and recovery. Do not substitute a sleeping ephemeral service for a working persistent production system.

## Configuration and release procedure

Use separate preview/development/production environment files and backend secret management. Never put provider credentials, JWT/admin secrets, MongoDB credentials or private paths in VITE variables. Vite values are public browser content. Only approved organization content belongs there.

Prepare production from reviewed source with `npm ci`, frontend tests/build, backend restore/build/publish and approved synthetic regression tests. The isolated release now targets supported .NET 10 and MongoDB Driver 3.13; its transitive SharpCompress 0.48.1 resolves the previously noted advisory. The synthetic backend suite passed after this upgrade. Live Cloudinary privacy and isolated restore remain launch gates.

Production backend requires a random signing key, non-default admin secret/hash, exact HTTPS frontend CORS origins, authenticated MongoDB with TLS for remote access, private durable upload storage and valid reviewed test exclusions. Bind the API behind an HTTPS reverse proxy/load balancer; configure trusted forwarded headers only for the actual proxy, never arbitrary forwarded clients. Keep MongoDB inaccessible to the public internet and least-privilege credentials scoped to its database.

Readiness should check database reachability without seeding/migration or exposing data; liveness should not depend on MongoDB or provider availability. Do not send OTP from a health check. Monitor error rates, disk capacity, backup success and OTP costs using non-PII logs/trace identifiers. In-process OTP/cooldown state currently requires one API instance; distributed/scale-to-zero operation needs a reviewed shared store before scaling.

Create a fresh encrypted database backup plus matching upload snapshot, record checksums, restrict ACLs and restore into a new isolated namespace/storage directory. Verify counts, indexes, record/Member ID/password/document digests and file hashes without displaying PII. Historical October 16 backups are not the current dataset. Do not run historical 12-user migration/rollback scripts against current records. Restore over production or any data/index migration requires separate approval and a stopped-writer/cutover plan.

After credentials and hosting are selected, complete `otp-provider-integration-guide.md`, then request approval for the concrete release/DNS/database/storage cutover. Smoke-test TLS, CORS, health, registration/reset/admin/exports/ownership/card images, backup and rollback in the final environment before opening real registrations. Rollback code/configuration independently where possible; never rewrite IDs, reset passwords or restore an old dump over newly registered users automatically.

## Prepared templates (not deployed or container-tested)

- `deploy/backend.production.env.example` and `deploy/frontend.production.env.example`: separate production placeholders using the existing configuration names. Enter backend values only in Render private environment settings and public `VITE_` values only in Cloudflare Pages production build settings. The existing `backend.env.example` and `frontend.env.example` remain staging templates; never use their staging database or Preview origin for the official domain.
- `deploy/backend.env.example`: backend-only database/JWT/admin hash/SMS/storage/CORS/public-statistics settings. Provider remains disabled; Reg.No remains blank. Replace placeholders in a private host secret manager, never React or source control.
- `deploy/frontend.env.example`: public branding and a placeholder HTTPS staging API origin. Set `VITE_API_BASE_URL` to the actual separate API `/api` URL and allow the exact Cloudflare Pages origin in backend CORS. Never let production fall back to port 5087.
- Root `Dockerfile` and `.dockerignore`: non-root .NET 10 API image preparation for a repository-root build context. The ignore file excludes local uploads, environment files, backups and artifacts; the runtime image removes local appsettings defaults. Verify the resulting image/file inventory before release and pin current patched image digests. Docker is unavailable locally, so no container build or host test was performed. The earlier example files remain reference templates.
- `deploy/Caddyfile.example`: same-origin static SPA/API/upload proxy and HTTPS/header template. It forwards uploads to the authorized controller; it does not serve private files from disk. The CSP permits self-hosted scripts and image blobs/data needed for card export. Validate on the actual host before activation. Approve TLS/domain/DNS changes first.
- The API accepts forwarded client IP/protocol only when `ReverseProxy:KnownProxies` explicitly lists the real immediate proxy IP. It trusts one hop and clears default trust lists. Do not list untrusted clients or configure all-network trust. Verify effective client IP/rate limits behind the final proxy; direct local development is unchanged.
- `/health/live` returns liveness only; `/health/ready` pings MongoDB with a three-second cancellation budget, returns generic 503 if unavailable and no records/secrets. Provider readiness is a separate launch gate; a green database probe does not mean SMS is ready.

For current free-tier launch gates, use [the private storage runbook](private-cloud-storage-runbook.md). The earlier Vercel staging checklist is retained as historical reference.

Production startup may create missing required indexes and seed configured districts. It never performs canonical backfill automatically; legacy mobile data blocks startup. First startup against a new production namespace therefore needs separate database-write/cutover approval. No production/database startup was executed during preparation.

Generate the admin hash offline in an interactive terminal after selecting the real secret:

```powershell
dotnet run --project tools/AdminSecret/AdminSecret.csproj --configuration Release
```

Input is hidden; output is an Identity V3 hash for `Admin__PasswordHash`. Do not pass the password as a command argument, commit the hash, or reuse development credentials. The utility was built; no real operator secret was created/applied.

## Backup and encryption gates

The October 16 archives still match their manifest; all 25 ZIP entries were streamed and SHA256-checked without extraction or writes. They are a historical 12-user snapshot; the current original has 5 users/38 districts and 35 upload files. Before release, separately approve and create a fresh consistent backup+upload archive and verify an isolated restore; do not overwrite the historical backup.

Read-only ACL inspection on October 24 still found three broad allow entries and inherited permissions on the backup directory. ACL remediation and encryption provisioning need separate approval. Preserve the intended operator, SYSTEM and authorized backup administrators, review exact SIDs/inheritance/descendants, and validate recovery access before applying the existing permission runbook. Do not grant broad access to private uploads or journals.

Encryption could not be verified in the current non-admin session (`CimException`). An authorized operator can inspect **only** status in an elevated PowerShell terminal:

```powershell
Get-BitLockerVolume | Select-Object MountPoint, VolumeStatus, ProtectionStatus, EncryptionPercentage
manage-bde -status
```

Map MongoDB's actual configured `storage.dbPath`, the private upload root, backups, journals and test-artifact locations to every relevant volume. These commands do not enable encryption or change data. Enabling encryption/changing ACLs is outside current approval. Do not claim a volume is protected solely because a folder exists.

## Remaining dependency/runtime gates

Frontend audit is clean after the tested Vite 7.3.7/React Router 7.18.4/DOMPurify patches. Backend JWT is 8.0.32 and Snappier 1.3.1; NuGet still reports the SharpCompress 0.30.1 traversal advisory transitively through MongoDB.Driver 2.29.0. The API does not extract archives using that library, but the dependency remains a release gate. A direct 0.48.0 override was tested and removed because it breaks this driver's zlib `NonDisposingStream` reference. Plan a reviewed supported driver upgrade, serialization/fingerprint compatibility checks and fresh regressions; do not suppress the advisory or change migration journals. [Advisory](https://github.com/advisories/GHSA-6c8g-7p36-r338), [driver upgrade guide](https://www.mongodb.com/docs/drivers/csharp/current/reference/upgrade/v3/).

The final isolated backend tests ran on workspace-local **.NET/ASP.NET Core 8.0.32**, downloaded from official release metadata and verified with SHA512. System-installed runtimes and the existing local backend were not changed/restarted. Patch the actual host, then approve a supported LTS upgrade before .NET 8 end of support. Container/proxy/TLS and the real hosting platform still need validation under their final configuration.
