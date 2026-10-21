## S-058 — Give the Azure services their identities and secrets
**Epic:** Cloud and costs
**Depends on:** S-032
**Priority:** High
**Size:** M

### Context
S-032 creates the resources. The web and the worker jobs then need managed identities with only the roles they use, the people who upload need data access to storage, sign-in must survive restarts, and secrets must reach the apps from Key Vault, never as plain values.

### Scope
- Web managed identity: *Storage Blob Data Reader* on the storage account (reads, and the user delegation key for SAS links, D-039; so *Storage Blob Delegator* is not added), *Storage Blob Data Contributor* only on the `dataprotection` container (the data protection key ring is the web's only write), *Key Vault Secrets User*, and *Key Vault Crypto User* on the data protection key.
- Worker identity (the migrate job now, the import and GeoNames jobs later): *Storage Blob Data Reader* on the storage account and *Key Vault Secrets User* for the catalog connection only. Only the web receives `Anthropic:ApiKey`. S-033 adds the Service Bus receiver role.
- Uploader access: a parameter `uploaderPrincipalIds` (the Entra object ids of the people who run `sepiabox-ingest upload --destination azure`) gets *Storage Blob Data Contributor* on the storage account; Owner on the resource group grants no data access (`docs/operations.md`).
- Key Vault holds the OAuth secrets, `Access:Emails`, the catalog connection and `Anthropic--ApiKey` (D-052); they reach the apps as Key Vault references or Aspire secret parameters, read through `IConfiguration` (D-051).
- Data protection: the web persists its keys to Blob Storage (`dataprotection` container) and protects them with a Key Vault key (`PersistKeysToAzureBlobStorage` + `ProtectKeysWithAzureKeyVault`, managed identity), as host wiring in `Program.cs`.
- Database role (D-061): today both apps connect as the PostgreSQL server administrator. The migrate job (the administrator) creates a least-privilege application role (read and write on the catalog's tables, no DDL) and its Key Vault connection string; the web connects with that role instead of the administrator.
- Replace the role assignments Aspire derives from the references in S-032 (D-061: the web holds Storage Blob, Queue and Table Data **Contributor** on the whole account) with exactly the roles above.
- Aspire turns on Container Apps' managed data protection for the web (`runtime.dotnet.autoConfigureDataProtection: true`, S-032); it overlaps with the key ring above, so keep exactly one of the two and record which.

### Out of scope
- The custom domain (S-035).

### Acceptance criteria
- [x] Given the generated Bicep, then the web identity has exactly the roles in Scope (Reader on the account, Contributor on `dataprotection` only, Key Vault Secrets User, Crypto User on the key), the worker identity exactly its roles, and each id in `uploaderPrincipalIds` has *Storage Blob Data Contributor* on the account.
- [x] Given the generated infrastructure, when it is inspected, then `Auth:Google:ClientId`, `Auth:Google:ClientSecret`, `Auth:Microsoft:ClientId`, `Auth:Microsoft:ClientSecret`, `Access:Emails` and `Anthropic:ApiKey` reach the web as Key Vault references or Aspire secret parameters, never as plain values.
- [x] Given the web in the `Production` environment, when it starts, then its data protection keys are persisted to Blob Storage and protected with a Key Vault key, so a restart, a new revision or a second replica keeps everyone signed in (verified by a test of the web's service registration).
- [x] Given the generated infrastructure and the migrations, then the web's connection string names the application role, not the server administrator, and that role cannot change the schema (verified by an integration test against PostgreSQL).
- [x] Given the generated Bicep, then exactly one data protection mechanism is configured for the web (either the Blob Storage key ring or Container Apps' `autoConfigureDataProtection`, not both).
- [ ] Given a real deploy (manual, owner acceptance in `docs/operations.md`), after adding the Container Apps default host's `/signin-google` and `/signin-microsoft` redirect URIs to the OAuth clients, when I sign in with Google on `https://<default host>`, restart the web revision and reload the page, then I am still signed in; and an account not on `Access:Emails` lands on the no-access page. (S-035 repeats this on `app.sepiabox.com`.)

### Technical notes
- The uploader roles were not effective in Azure until D-072: azd ignored `uploaderPrincipalIds` from `infra.parameters` while it published a default. Owner check (`az role assignment list --assignee <id> --role "Storage Blob Data Contributor" --all`) after the first deploy with that fix.
- Sign-in (S-010, D-048): the data protection key ring and the sign-in secrets are what D-048 (14) left to S-032; they are this story's half.
- Portability (D-051): Key Vault is a configuration source or a Container Apps secret reference added in the composition root; no code calls `SecretClient`.
- Decisions: D-004, D-039, D-048, D-051, D-052.

### Expected tests
- Bicep tests for the role assignments and secret references; a Production service-registration test for data protection. Manual sign-in check after deploy.
