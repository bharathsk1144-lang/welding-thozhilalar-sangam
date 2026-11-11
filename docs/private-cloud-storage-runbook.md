# Private uploads on free hosting

Production selects `Uploads__Provider=Cloudinary`. Only the backend receives `Cloudinary__CloudName`, `Cloudinary__ApiKey`, and `Cloudinary__ApiSecret`. Never put them in Vite/Cloudflare Pages variables. Local storage is for development only; production startup rejects it. Existing `/uploads/{filename}` references remain opaque and resolve through the server-only MongoDB `PrivateUploads` collection.

Uploads use Cloudinary's `authenticated` delivery type. The server-only `PrivateUploads` record stores `AssetId` for retrieval and `PublicId` plus resource type for confirmed deletion. The WTS API checks member/admin access before requesting bytes by asset ID, then responds with `Cache-Control: private, no-store`. Members may fetch their own profile photo; Aadhaar and payment proof remain admin-only. No Cloudinary URL or credential is returned to browsers. The `private` delivery type is unsuitable because derived images can be public. [Cloudinary access control](https://cloudinary.com/documentation/control_access_to_media), [Upload API](https://cloudinary.com/documentation/image_upload_api_reference).

Before accepting real identity files, use isolated staging with synthetic files to verify authenticated upload, unsigned original and transformed URL denial, authorized retrieval, cross-member denial, deletion, restart/retrieval, and failure cleanup. Inspect account settings and usage. Existing local files are **not** migrated automatically: privately upload them, preserve their `/uploads` references and verify hashes before switching an existing database to Cloudinary.

If MongoDB is unavailable after Cloudinary accepts an upload and remote deletion also fails, an authenticated orphan may remain. Reconcile the dedicated `wts-private/` Cloudinary folder against `PrivateUploads` after incidents and before each backup; never expose or bulk-delete assets without review. Older `PrivateUploads` rows without `PublicId` cannot be deleted automatically and need an operator-reviewed lookup before deletion.

## Operator-controlled backup and isolated restore

Atlas M0's optional automated backup is paid. Use MongoDB Database Tools for a free manual dump/restore. Include every collection, especially `Users`, `Districts`, and `PrivateUploads`. [Atlas backup guidance](https://www.mongodb.com/docs/atlas/backup-restore-cluster/). Do not put credentials, member data, or files in this repository or shell history.

1. Schedule maintenance; block registration, payment, and admin writes. Verify no writes remain. A Mongo dump and separate file copy are inconsistent if uploads continue.
2. On a trusted operator machine, dump the explicitly named database to a compressed archive using a restricted credentials file or interactive prompt. Keep the archive off Render. Record SHA-256 and collection counts without displaying personal data.

   Example with MongoDB Database Tools 100.3+ and a privately stored YAML `uri` file (replace names with the actual isolated source and target):

   ```powershell
   mongodump --config C:\Secure\atlas-source.yml --db WTS_Staging --archive=C:\Secure\wts-staging.archive.gz --gzip
   Get-FileHash C:\Secure\wts-staging.archive.gz -Algorithm SHA256
   mongorestore --config C:\Secure\atlas-restore.yml --archive=C:\Secure\wts-staging.archive.gz --gzip --nsFrom='WTS_Staging.*' --nsTo='WTS_Restore_Test.*'
   ```

   The source and restore YAML files contain only their own Atlas URI and must have restricted filesystem permissions. Do not use `--drop` or a production target. [mongodump](https://www.mongodb.com/docs/database-tools/mongodump/), [mongorestore](https://www.mongodb.com/docs/database-tools/mongorestore/).
3. For each `PrivateUploads` record in the dump, download by asset ID using Cloudinary's authenticated API. Save under its opaque filename in a restricted, encrypted directory. Record SHA-256 per file and verify counts. Do not use public delivery URLs.
4. Encrypt archive, files, and manifest with an operator-managed key; keep two restricted copies and the key separately. Define retention and repeat backups.
5. Restore Mongo only to a **new isolated database**. Restore files to a separate authenticated Cloudinary namespace/environment, changing both `AssetId` and `PublicId` in the isolated `PrivateUploads` collection. Point an isolated API instance there. Compare counts and hashes; test profile ownership, Aadhaar denial to members, and synthetic ID-card export.
6. Record operator, date, hashes, counts, and recovery time. Do not claim recovery is verified until an actual isolated restore succeeds.

## Launch gates

Cloudinary Free supports authenticated assets and server API operations within a rolling credit allowance shared by storage, transformations and bandwidth. [Free FAQ](https://cloudinary.com/documentation/developer_onboarding_faq_free_plan), [billing](https://cloudinary.com/documentation/billing_and_plans). Atlas M0 has a 512 MB storage limit. [Atlas limits](https://www.mongodb.com/docs/atlas/manage-clusters/). Render Free loses local files on restart or redeploy and sleeps after idle. [Render Free](https://render.com/docs/free).

`/health/ready` checks MongoDB, not Cloudinary. Green health alone does not prove upload or restore readiness. Officer signatures are packaged in non-public backend assets and served only through the authorized card workflow. Do not collect real Aadhaar files until provider privacy tests, any legacy-file migration, and isolated restore pass.
