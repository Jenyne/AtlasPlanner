# AtlasPlanner

**Current: 0.4.0 (PoE 3.26)** — Quick goals board, named save/load, public build without Send to game.

Path of Building style planning for the Path of Exile 1 **atlas passive tree**: work out a route
against stated goals, get an allocation order to follow as points come in, and see what the finished
tree actually adds up to.

`AtlasPlanner.Core` is a plain `net10.0` library with no Windows or UI dependencies, so the same solver
backs the CLI, the Avalonia planner, and the in-game overlay plugin.

## Layout

| Path | What it is |
| --- | --- |
| `src/AtlasPlanner.Core` | Tree model, stat pipeline, categorisation, tally, solver, order planner, plan IO |
| `src/AtlasPlanner.Cli` | `atlasplanner` console app |
| `src/AtlasPlanner.Gui` | Avalonia planner: the tree, the solve panel, the order list, the tally |
| `src/AtlasPlanner.Plugin` | `AtlasPlannerOverlay`, the ExileAPI plugin that draws a plan in game and places the points |
| `tests/AtlasPlanner.Core.Tests` | xUnit tests over the real tree data |
| `tests/AtlasPlanner.Gui.Tests` | Headless view model tests |
| `data/AtlasTreeData.json` | Tree export snapshot (3.26), copied from the PoBTreeOverlay plugin |
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

- **Summed** — exactly one number, so adding across nodes is meaningful.
- **Repeated** — several numbers (tier ranges and the like), counted by exact text.
- **Flags** — no numbers, listed by exact text.

Categorisation runs stat-first, node-second: a Betrayal-region node granting *"Scarabs dropped in
your Maps have 10% increased chance to be Betrayal Scarabs"* is a Scarab stat, so weighting Scarabs
picks it up. Order of precedence is template override, then keyword rule, then the node's region,
then a last-resort match of region names against the stat text (which is what catches keystones and
other nodes in groups with no mastery label). All of it lives in `data/atlasscores.json`.

## Asking for a route

A profile holds soft goals as category weights and hard goals as node sets:

```json
{
  "name": "Scarab farming",
  "budget": null,
  "weights": { "Scarabs": 10, "Map Sustain": 5, "Monster Difficulty": -2 },
  "require": ["Significant Troves"],
  "forbid": [],
  "forbidRegions": [],
  "excludeMechanics": ["Breach"],
  "unwaveringVision": "Exclude",
  "nodeWeights": { "Overloaded Circuits": 250 },
  "preAllocated": [],
  "seed": 1,
  "timeLimitMs": 2000
}
```

`weights` are per category from the score table; run `atlasplanner scores` for the list. `require`
and `forbid` accept ids or exact node names, and a wrong name fails with suggestions rather than
being ignored.

`excludeMechanics` is the ergonomic way to say "I don't want Breach". It zeroes that category *and*
requires the matching exclusion notable, so the mechanic is actively switched off. It deliberately
does not ban the region, because pathing through a Breach node may be the only way to reach the very
notable that turns Breach off. Use `forbidRegions` when you want a hard ban.

`unwaveringVision` decides the point-granting keystone, which is a large enough trade to be your
call: `Exclude` never takes it, `Include` always does and plans against the 20 extra points, and
`Auto` solves both ways and keeps the higher score. Override per run with `--unwavering`.

`nodeWeights` is the escape hatch for effects no text parser can price, like keystone drawbacks.

### How stats are priced

A node's value is the sum over its stats of `weight(category) x magnitude`, where magnitude is the
stat's number, or `flatStatValue` for stats that have none. Two rules keep that honest:

- **Downsides flip sign.** A stat matching a phrase like `#% reduced` or `#% less` counts against its
  category rather than for it.
- **Total shutdowns are constraints, not penalties.** "Scarabs cannot be found in Your Maps" makes
  every other Scarab node in the route worthless, and no finite penalty models that. So a node that
  switches off a category you weight positively is *ruled out* instead of priced; and if you require
  such a node anyway, its category drops to zero weight so the route stops paying for a mechanic it
  just disabled. Both decisions are reported, never silent.

  This is narrower than the downside rule on purpose. "Scarabs cannot be found" is a shutdown;
  "Scarabs found in your Maps cannot be Breach Scarabs" only rules out one variety, so it is merely
  a downside.

### How the route is found

A rooted budgeted prize-collecting Steiner tree: connect the required nodes with the shortest-path
heuristic, then repeatedly buy the most valuable path per point, prune worthless leaves, and run
destroy-and-repair local search until `timeLimitMs` runs out. Fixed `seed`, so the same profile
always gives the same route. Expansion picks the *most valuable* shortest path to a node, not just any
shortest path, which matters because atlas value often sits behind two or three filler connectors.

The score is only comparable between plans built from the same weights, since requiring a shutdown
node changes the weights.

### The allocation order

Any traversal outward from the start node is legal, so the planner picks a useful one: it repeatedly
commits the whole path to whichever remaining node offers the most prize per point. That front-loads
notables and defers filler as long as possible, which matters because atlas points arrive gradually.
The point-granting keystone is pulled early, since until it is allocated those 20 points do not exist.

## Things worth knowing about the atlas tree

- **Only one node grants points**: `Unwavering Vision` (+20). It sits 19 hops from the start, so it
  nearly pays for its own approach, at the price of banning scarabs and fragments. Because it is the
  only point granter, the budget can be resolved by solving twice and comparing.
- **12 exclusion notables** switch a mechanic off in exchange for `+2% chance to contain other Extra
  Content` — `Dimensional Barrier` (no Breaches), `Black Thumb` (no Harvest), and so on. "I don't
  want Breach" is therefore something the solver should consider *buying*, not just filtering.
- **3 gateway pairs** (Mortal, Eldritch, Cryptic) stitch distant regions together as ordinary edges.
  They are currently modelled as normal cost-1 nodes; that still wants confirming in game.

`validate` asserts each of these, so a league tree update that breaks one shows up as a failed check
rather than a nonsense route.

## The plan file

`solve --out` writes an `AtlasPlan.json`, the versioned contract between the planner and anything
that consumes a plan:

| Field | Use |
| --- | --- |
| `nodes` | Final node set, including the free start node |
| `url` | Importable atlas tree link, start node omitted |
| `order` | Step N is the Nth point to spend, with what it is heading towards |
| `tally` | Summed, repeated, and flag entries, pre-rendered for display |
| `profile` | The request it came from, so a plan can be explained or regenerated |

`nodes` and `url` alone are enough to drive a plain highlight overlay. `order` is what allows an
overlay to show "your next 5 points" and to allocate step by step.

## In-game overlay

`src/AtlasPlanner.Plugin` builds `AtlasPlannerOverlay` and deploys it to
`Plugins/Compiled/AtlasPlannerOverlay/` on every build, which is where ExileAPI loads plugins from.
Close the overlay before rebuilding or the DLL will be locked.

The handoff is a file. **Send to game** in the planner writes the plan to
`config/AtlasPlannerOverlay/Plans/<plan name>.json`, which is the plugin's own config folder; the
plugin notices new and overwritten plans within a second and offers them in a dropdown.

In game, with the atlas passive tree open, the plugin rings every planned node, numbers the points you
are about to spend, names the one to take next, and rings anything you have spent outside the plan.
**Place next N** allocates in the planner's order, one point at a time: it hovers a node, waits for the
game to confirm the cursor is on it, clicks, then re-reads what the game says is allocated before
moving on. Progress is measured from the game's own allocation rather than from clicks sent, so a click
the game ignored does not count, and it stops with a reason rather than thrashing if a point cannot be
taken.

Decisions live in `AtlasPlanner.Core` (`PlanProgress`, `PlanFolder`) where they are covered by tests.
The plugin itself is only the part that cannot be tested without a game attached: reading the tree
panel, drawing, and clicking.

The plugin needs nothing from the planner at runtime beyond the plan file and its own copy of
`AtlasTreeData.json`, which the build places beside the DLL for node positions.

## Public release (GitHub)

The public download is a **self-contained Windows zip** of the GUI only (no Send to game).

### One-time setup

```powershell
cd E:\WoW\ExileApi-Compiled-3.26.0.0.1\AtlasPlanner

git init
git add .
git commit -m "Initial commit: Atlas Planner 0.4.0"

# Install GitHub CLI if needed: winget install GitHub.cli
gh auth login
gh repo create AtlasPlanner --public --source=. --remote=origin --push
```

### Ship a version

1. Bump `<Version>` in `Directory.Build.props` (and the README line if you care).
2. Commit.
3. Tag and push:

```powershell
git tag v0.4.0
git push origin main
git push origin v0.4.0
```

Pushing a `v*` tag runs `.github/workflows/release.yml`, which builds the public zip and creates a
GitHub Release with `AtlasPlanner-<version>-win-x64.zip` attached.

### Local zip without GitHub Actions

```powershell
.\scripts\publish-public.ps1
# -> artifacts\AtlasPlanner-0.4.0-win-x64.zip
```

