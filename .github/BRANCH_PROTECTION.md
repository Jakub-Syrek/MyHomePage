# Branch protection — enforce clean authorship on master

The "No AI attribution" workflow (`.github/workflows/no-ai-attribution.yml`)
must be wired into a branch protection rule (or GitHub Ruleset) so that
no push to `master` can land with Claude / Anthropic / OpenAI / GPT
co-authorship or AI-generated markers.

## One-time setup (Settings → Branches → Add rule)

1. **Branch name pattern**: `master`
2. **Require a pull request before merging** — optional, but recommended.
3. **Require status checks to pass before merging** — check this and add:
   - `Scan commits for AI attribution`
   - `Build & Test` (existing `.github/workflows/dotnet.yml`)
4. **Require branches to be up to date before merging** — recommended.
5. **Do not allow bypassing the above settings** — check this so admin
   accounts are also bound (otherwise the policy only applies to others).
6. **Restrict who can push to matching branches** — leave empty for solo dev,
   or add yourself only.

Save the rule. Any push (or PR merge) that triggers a violation will fail
the `Scan commits for AI attribution` check and be rejected automatically.

## One-time setup via GitHub Rulesets (modern UI, equivalent)

`Settings → Rules → Rulesets → New ruleset`

1. **Name**: `master clean authorship`
2. **Target**: include `master`.
3. **Restrictions**: enable *Block force pushes* unless the cleanup workflow
   needs to run (then either temporarily disable the rule or grant the
   workflow's GitHub Actions token a bypass).
4. **Rules → Require status checks to pass**: add
   `Scan commits for AI attribution` from this workflow.

## When something slips through

If a commit with forbidden attribution somehow lands on `master` (typically
from a force push before the rule was active), run the manual cleanup
workflow:

`Actions → Clean AI attribution from history → Run workflow → target=master, confirm=master → Run`

The workflow rewrites every reachable commit on the target branch and
force-pushes the cleaned history. Anyone with a local checkout must then
re-clone or `git fetch --all && git reset --hard origin/master`.

## Local prevention (client side)

The strict server-side workflow is the safety net. Prefer also installing
the local `prepare-commit-msg` hook from
`memory/commit_authorship_clean.md` so bad commits never reach GitHub in
the first place.
