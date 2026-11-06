---
name: presenter-demo
description: Presenting the walkthrough live to an audience on two screens - a presenter console with each scene's line and Next/Pause/Auto/jump on the laptop, the app in a browser on the other display. Use whenever someone wants to demo or present the product, rehearse a demo, drive the walkthrough scene by scene, show it on a projector, or get speaker notes for the video; also covers first-time setup of a Mac to run the stack.
---

# Presenter demo

The demo is the walkthrough itself (`web/e2e/walkthrough.mjs`) run with `PRESENTER=1`, so it makes the same clicks as the recorded video and cannot drift from it. `web/e2e/lib/presenter.mjs` holds it; `web/README.md`, "Presenting it live", describes the console.

## Run it

```bash
web/e2e/run-e2e.sh --present        # boots a throwaway stack on its own ports and presents; going back resets the database
PRESENTER=1 BASE_URL=http://localhost:5173 node web/e2e/walkthrough.mjs   # against the running AppHost rig; going back exits 75
```

- **Console** at http://localhost:4173, its own Chrome window on the main screen: the scene, the line to say, what is next and every scene down the side. Enter or → next, P pause, A auto, and Quit as a button; in a terminal, Q quits too.
- **The app** opens in a separate Chromium on the largest display that is not the main one; `PRESENTER_SCREEN=main` keeps it on the laptop.
- **Pause** stops at the next wait and opens a scratch tab signed in as the same person; resuming closes it, and the demo carries on in its own tab, untouched (a half-filled form survives). Reloading the demo tab instead loses typed fields.
- **Jump** by clicking a scene: forward runs the scenes between without stopping; back exits 75 after writing the scene to `PRESENTER_JUMP_FILE`, and `run-e2e.sh --present` recreates the database, migrates and replays up to it.
- **A failed step** leaves the browser where it stopped, shows the error in the console and saves `web/test-results/presenter-failure.png`. Carry on by hand.
- `PRESENTER_AUTO=1` runs straight through; `HEADLESS=1 PRESENTER_CONSOLE=0` is the unattended check, driven by `POST /act/next`, `/act/pause`, `/act/jump/<n>` and read from `GET /events`.

The walkthrough wants a database that has not run the story yet: `run-e2e.sh --present` starts from an empty one. Against the AppHost rig, reset it first (stop the AppHost, remove its Postgres container and volume, start it again; restarting alone keeps the data).

## First-time setup on a Mac

1. **.NET SDK at the pin in `global.json`**, without admin: `curl -sSL https://dot.net/v1/dotnet-install.sh -o dotnet-install.sh && bash dotnet-install.sh --version <pin> --install-dir ~/.dotnet`.
2. **Container VM with 8 GB, and Rosetta for the AppHost.** The Service Bus emulator's SQL Server is Intel-only and segfaults under QEMU (exit 139). With Podman: `podman machine set --memory 8192`, `rosetta = true` under `[machine]` in `~/.config/containers/containers.conf`, restart the machine, and run Aspire with `ASPIRE_CONTAINER_RUNTIME=podman`. `run-e2e.sh --present` needs neither the emulator nor Rosetta.
3. **Trust the HTTPS dev certificate from a normal Terminal window** (`dotnet dev-certs https --trust`): from an agent's shell macOS cancels the password prompt, and the Functions host then cannot call the API (UntrustedRoot).
4. **Web dependencies and the Playwright Chromium**: `cd web && npm ci && npx playwright install chromium`.

## Traps

- Match the demo's processes from the start of the command line (`pgrep -f '^node e2e/walkthrough[.]mjs'`); an unanchored pattern also matches the shell running pgrep.
- `open <url>` lands in the last Chrome window, which may be on the audience screen; the console opens its own window on the main screen instead.
