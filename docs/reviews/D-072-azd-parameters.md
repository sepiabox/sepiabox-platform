# Review of the azd parameter fix (D-072)

Process: [docs/process.md](../process.md). Atlas knows the project; Spock sees only the diff.

Production finding (2026-10-20): after a Deploy with `infra.parameters.customDomain = app.sepiabox.com`, the live web had `AllowedHosts = "sepiabox-web.<domain>;"` and no ingress custom domain, and the owner had no *Storage Blob Data Contributor* although `uploaderPrincipalIds` was set. Cause, confirmed by both reviewers in Azure/azure-dev `cli/azd` (c95f408, byte-identical to 1.36.0 for the files involved): a parameter published with a default (`publishValueAsDefault`) is skipped by `ensureParameters` before the `infra.parameters` lookup, and at deploy `ParameterWithDefault` reads only `AZURE_<NAME>`.

## Iteration 1

Diff: `1fb0e1b...7a4c5ce`.

| Priority | Source | Short description | Resolved? | How to fix |
|---|---|---|---|---|
| P2 | Atlas | With the binding already `SniEnabled` and no host name on the web, provision creates the certificate first and Azure refuses it | ✅ Yes (iteration 2) | `deploy.sh` stops before provision with the recovery; operations.md |
| P3 | Spock + Atlas | An `AZURE_CUSTOM_DOMAIN*` / `AZURE_UPLOADER_PRINCIPAL_IDS` value in the azd `.env` or shell overrides provision but not deploy | ✅ Yes (iteration 2) | `deploy.sh` refuses; wording fixed |
| P3 | Atlas | The manual fallback provisions without the parameters | ✅ Yes (iteration 2) | "If missing" lines in step 1 |
| P3 | Atlas | S-035 and S-058's uploader part were `done` but never effective in Azure | ✅ Yes (iteration 2) | Story notes; Agent Log |
| P4 | Spock | azd installed as latest | ✅ Yes (iteration 2) | Pinned 1.36.0 |
| P4 | Spock | The manifest guard could pass vacuously | ✅ Yes (iteration 2) | Asserts the six parameters first |

## Iteration 2

Diff: `7a4c5ce...7f9d58b` (fixes plus a merge of main with S-056). Both reviewers found nothing at P0–P2. Last items, fixed on the PR head:

| Priority | Source | Short description | Resolved? | How to fix |
|---|---|---|---|---|
| P3/P4 | Atlas + Spock | `azd env unset` does not exist in azd 1.36.0 | ✅ Yes | "Delete its line from `.azure/<environment>/.env`" in the script and operations.md |
| P4 | Spock | A lowercase `snienabled` set locally skipped the guard | ✅ Yes | Case-insensitive comparison; test case |

Decision: no findings open → pull request. Proof in production is the owner's next deploy (host name `Disabled`, `AllowedHosts`, the uploader role), then `SniEnabled`.
