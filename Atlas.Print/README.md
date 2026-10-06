# Atlas.Print

A standalone Playwright-based PDF-generation microservice for Data Archive's
print pipeline. Receives an HTML payload via `POST /print/generate` and
returns a rendered PDF (Base64-encoded), using headless Chromium.

Called by the mid-tier (`Data.Archive`)'s print builders after they render
the `Views/Print/*.cshtml` templates — Atlas.Print itself doesn't know
anything about Data Archive's domain; it's a generic HTML-to-PDF renderer.

Structurally similar to `Atlas.Report.Print`, but triggered differently:
Atlas.Print handles synchronous, per-click print requests from the Angular
viewer's print button, while `Atlas.Report.Print` is reached only via the
async report job pipeline.

## Running locally

Launch the `LOCAL` profile (Visual Studio F5, or `dotnet run --launch-profile LOCAL`).
Listens on `http://localhost:8085`. Swagger UI (`/swagger`) and a raw-HTML
preview endpoint (`/print/preview`) are both available — gated to the
`LOCAL`/`DEV` environments only, 404 everywhere else.

## Environments

`LOCAL`, `DEV`, `DEMO`, `QA`, `PROD` — DEMO shares a host with QA, no
separate install. Environment is set via `--environment=<ENV>` on the
Windows Service's `ImagePath`; `PLAYWRIGHT_BROWSERS_PATH` is a machine-wide
system environment variable, not app config.

## Deploying

See [`DEPLOYMENT.md`](./DEPLOYMENT.md) for the full install/redeploy/rollback
runbook — build, versioned publish layout, service repoint steps, and
Playwright browser installation.

## Tagging

Releases are tagged `atlas-print-v<version>` (distinct from `Data.Archive`'s
`v3.0.x` mid-tier tags, since both share this repo's tag namespace).
