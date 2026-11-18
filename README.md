# LiftX

A lifting tracker that runs as a web app on your phone. Your data lives in one JSON file that you own.

- **Routines**: build them, start them, and they update from your lifts if you want
- **Live lift**: sets, weight, reps, and time + miles/floors for cardio, with a rest timer (1:30 default, editable mid-lift)
- **Calendar**: every day you lifted; tap a day to view, edit, delete, or turn it into a routine
- **Stats**: everything from LiftoffVisualizer (charts, trends, PRs, share image)
- **Theme**: any main color + accent (default black + mediumseagreen)

## Saving

Everything saves on the phone automatically. **Save to Files** (the ⬇ icon, or "Finish & Save to Files"
at the end of a lift) writes `LiftX-data.json` through the share sheet. Choose the same folder in Files
and tap **Replace**. To move to a new phone, or after clearing Safari data, open LiftX and import that file.

First run: import your Liftoff export (`liftoff_workout_data.csv`) or a LiftX save file.

## Develop

```sh
dotnet watch                                  # http://localhost:5000, reloads when you save
dotnet watch --urls http://0.0.0.0:5080       # also reachable from your iPhone on the same Wi-Fi
```

On the Mac, view it phone-sized with Safari's Responsive Design Mode (⌥⌘R) or Chrome's device toolbar (⌘⇧M).
On the iPhone, open `http://<your-mac-ip>:5080` in Safari. It's plain http, so Save to Files falls back
to a download and keep-awake is off there; the real (https) GitHub Pages site has both.

Pushing to `main` deploys to GitHub Pages (`.github/workflows/deploy.yml`).
One-time setup: repo **Settings → Pages → Source: GitHub Actions**.
