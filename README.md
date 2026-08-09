# AtlasPlanner

**Current: 0.5.1 (PoE 3.26)** — Quick goals, specialization prompts, atlas route planner.

Path of Building style planning for the Path of Exile 1 **atlas passive tree**: work out a route
against stated goals, get an allocation order to follow as points come in, and see what the finished
tree actually adds up to.

`AtlasPlanner.Core` is a plain `net10.0` library with no Windows or UI dependencies, so the same solver
backs the CLI and the Avalonia planner.

## Layout

| Path | What it is |
| --- | --- |
| `src/AtlasPlanner.Core` | Tree model, stat pipeline, categorisation, tally, solver, order planner, plan IO |
| `src/AtlasPlanner.Cli` | `atlasplanner` console app |
| `src/AtlasPlanner.Gui` | Avalonia planner: the tree, goals, order list, tally |
| `tests/AtlasPlanner.Core.Tests` | xUnit tests over the real tree data |
| `tests/AtlasPlanner.Gui.Tests` | Headless view model tests |
| `data/AtlasTreeData.json` | Tree export snapshot (3.26) |
| `data/atlasscores.json` | Editable stat categorisation and scoring rules |
| `data/*.profile.json` | Solve profiles: what you want out of the tree |

## Build and run

```powershell
dotnet build AtlasPlanner.slnx
dotnet test AtlasPlanner.slnx

# plan a route
dotnet run --project src/AtlasPlanner.Cli -- profile --init data/myplan.profile.json
dotnet run --project src/AtlasPlanner.Cli -- solve --profile data/myplan.profile.json --out data/myplan.json

# poke at the tree
dotnet run --project src/AtlasPlanner.Cli -- validate
dotnet run --project src/AtlasPlanner.Cli -- node 65225
```

Tree data is auto-located by walking up from the working directory looking for `AtlasTreeData.json`
or `data/AtlasTreeData.json`. Override with `--tree <path>` or the `ATLASPLANNER_TREE` environment
variable.

## Commands

| Command | Purpose |
| --- | --- |
| `profile` | Inspect a solve profile, or `--init <path>` to write an example |
| `solve` | Plan a route from a profile and write an `AtlasPlan.json` |
| `plan` | Print an existing plan: allocation order, tally, importable URL |
| `tally` | Add up a node set from `--url`, `--nodes 1,2,3`, or `--all` |
| `search` | Find nodes by name, `--stats` text, `--region`, or `--kind` |
| `categories` | Every stat template and the category it classifies as; `--uncategorised` to review gaps |
| `validate` | Re-checks the assumptions the planner relies on against the tree data |
| `node <id>` | Everything known about one node, including hops from the start |
| `scores` | Show the active score table, or `--init <path>` to write an editable copy |

## How the tree is modelled

The export has 1028 entries. 126 are `isMastery` region labels that sit at group centres with no
stats and no connections, and one is a `root` sentinel that only points at the real start node. That
leaves **901 allocatable nodes** over 1001 undirected edges, average degree 2.2, all reachable from
the free start node (`29045`, cost 0). Every other node costs one point, against a base budget of
138.

The export's `in`/`out` split is not a reliable direction, so edges are unioned into an undirected
adjacency list. Because every node costs the same, hop count *is* point cost and breadth-first search
is all the graph work that's needed.

Mastery labels are still useful: a node's group mastery gives it an authoritative `Region`
("Bestiary", "Breach", ...), which covers 714 of the 901 nodes for free.

## How stats are handled

Atlas passives are additive against independent buckets, so unlike character-tree stats they can
honestly be summed — no calculation engine required. Each stat line is cleaned (PoE inline markup
like `[ContainsAbyss|Abysses]` is unwrapped, embedded newlines collapsed) and split into a template
with numbers replaced by `#`, plus the numbers themselves. The tally then keeps three buckets apart
rather than blending them:

| Bucket | Meaning |
| --- | --- |
| Summed | Numbers on matching templates add |
| Repeated | Same line appears on several nodes; shown with a count |
| Flags | Presence-only text |

## Solving

The solver searches for a connected set of nodes that maximises a weighted prize under a point budget.
Mechanics you Chase contribute positive weight; Blocked mechanics push the search toward that
mechanic's off-switch and away from its other nodes. Must-take / Never-take marks are hard
constraints.

## Plans

A solved (or hand-built) tree can be saved as JSON and as a pathofexile.com atlas URL for import.

## Releases

Prefer a **private** GitHub repo for source. Public downloads are a self-contained Windows zip of the
planner GUI only.

### One-time setup

```powershell
cd E:\WoW\ExileApi-Compiled-3.26.0.0.1\AtlasPlanner

git init
git add .
git commit -m "Initial commit: Atlas Planner 0.4.0"

# Install GitHub CLI if needed: winget install GitHub.cli
gh auth login
gh repo create AtlasPlanner --private --source=. --remote=origin --push
```

### Ship a version

1. Bump `<Version>` in `Directory.Build.props`.
2. Commit.
3. Tag and push:

```powershell
git tag v0.4.0
git push origin main
git push origin v0.4.0
```

Pushing a `v*` tag runs `.github/workflows/release.yml`, which builds the zip, creates a
GitHub Release, uploads the asset to VirusTotal, and appends the scan link to the release notes.

### VirusTotal (one-time)

1. Create a free API key: https://www.virustotal.com/gui/my-apikey
2. Add it as a repo secret named `VT_API_KEY`:
   ```powershell
   gh secret set VT_API_KEY
   ```
   (paste the key when prompted)

Without that secret, the release still publishes; the VirusTotal job will fail until the secret exists.

### Local zip

```powershell
.\scripts\publish-public.ps1
# -> artifacts\AtlasPlanner-0.5.1-win-x64.zip
```

### Public vs personal on your PC

The default solution build is the **public** app (no Send to game). Personal hooks are
gitignored and only compile when you pass `-p:AtlasPlannerPersonal=true`.

| Shortcut / script | Purpose |
|---|---|
| `run-public.bat` | Build & run public |
| `run-personal.bat` | Build & run personal (local only) |
| `update-local-release.bat` | Publish public zip, install under `%LOCALAPPDATA%\AtlasPlanner\app`, refresh Desktop shortcuts |

`update-local-release.bat` also builds a personal folder under `artifacts\personal\` when those
files exist — keep that folder and the **Atlas Planner (Personal)** shortcut off GitHub Releases.
