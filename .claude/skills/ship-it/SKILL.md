---
name: ship-it
description: Run when code changes are ready to release. Handles pre-flight checks, version bump, AI-drafted What's New bullets (reviewed in-session), commit message suggestion, and GitHub draft release creation.
disable-model-invocation: true
---

Walk through each step in order. Stop and report clearly if anything fails.

## What this does and why

When you're ready to publish a new version of the mod, this skill walks you through the full release process. It checks that your code is clean and on the right branch, helps you pick a version number, writes player-friendly release notes based on what actually changed in the code, and creates a draft release on GitHub for you to review before publishing.

## Key files

| File | What it does |
|------|-------------|
| `manifest.json` | Mod version -- the source of truth for release tags and titles |
| `bin/githubrelease/whats-new.md` | Release notes draft -- written during this workflow, picked up by the release script |
| `create-github-release.ps1` | Packaging script that builds the zip and creates the GitHub draft release |

**Test mode:** If the user invoked `/ship-it --test`, follow all steps but:
- Do NOT write `bin/githubrelease/whats-new.md`
- Do NOT edit `manifest.json`
- Do NOT run `create-github-release.ps1`
- Instead, show what *would* happen at each of those steps and label the output clearly with `[TEST RUN -- not applied]`

At the end of a test run, say "Test run complete -- nothing was written or committed."

---

## Step 0 -- Stale notes guard

Check whether a `whats-new.md` from a previous run is still sitting around.

Check whether `bin/githubrelease/whats-new.md` already exists.

- If it does **not** exist: continue to Step 1.
- If it **does** exist: show the user its contents and ask: "A `whats-new.md` from a previous run exists. What would you like to do -- use it as a starting point, discard it, or cancel?"
  - **Use it** -- load its contents as the draft bullets in Step 3; skip the research phase and go straight to "Any tweaks?"
  - **Discard it** -- delete the file and continue normally from Step 1
  - **Cancel** -- stop here, do nothing

---

## Step 1 -- Pre-flight checks

Make sure the workspace is in a clean state and ready to release.

Run all three checks. If any fail, report what's wrong and stop -- do not continue.

1. Run `git status --porcelain` -- output must be empty (clean working tree)
2. Run `git branch --show-current` -- must output `main`
3. Run `gh auth status` -- must succeed (exit code 0)

If everything passes, say "Pre-flight checks passed." and continue.

---

## Step 2 -- Version bump decision

Show the user what's changed since the last release so they can pick the right version bump.

1. Read `manifest.json` and show the user the current version
2. Run `git fetch --tags` to ensure all remote tags are present locally
3. Run `git tag --sort=-creatordate` to find the most recent tag (call it `$prevTag`). If no tags exist, note that this will be the first release.
4. Run `git log $prevTag..HEAD --pretty=format:"%s%n%b"` (or `git log HEAD --pretty=format:"%s%n%b"` if no tags) to capture both subject and body of each commit
5. Show the user the raw commit list, including any issue references found in commit bodies

Then ask the user which version bump to apply. Show the current version and these options:
- **Patch (0.0.X)** -- bug fixes and small tweaks. When in doubt, use this.
- **Minor (0.X.0)** -- new features a player would notice. Resets patch to 0.
- **Major (X.0.0)** -- reserved for major milestones or game-update-forced rewrites. Resets minor and patch to 0.

Wait for the user to choose before continuing.

---

## Step 3 -- Draft What's New bullets

Research what actually changed in the code so the release notes are accurate, not just based on commit messages.

Before writing bullets, do deep research on every player-visible commit:

1. **For every issue number (`#N`) found in any commit subject or body:** run `gh issue view N` to get the issue title, description, and comments.
2. **For every player-visible commit:** run `git show <hash> -- ResearchQueueWindowController.cs` (and any other changed .cs files) to read the actual code diff. Use this to understand exactly what changed, not just what the commit message says.
3. **Group commits by issue.** All commits referencing the same `#N` belong to one bullet. Commits with no issue reference get their own bullet if player-visible -- always read the code diff for these too, do not rely solely on the commit message.

Then write the bullets using these rules:
- Write for players, not developers. "Queues now carry over between game sessions" not "refactor queue persistence layer"
- Use the issue details AND the code diff to write a concise but meaningful description
- Omit commits that have no player-visible effect (build changes, README edits, code cleanup, docs, comment fixes)
- One bullet per GitHub issue maximum. Merge all commits for that issue into a single bullet, even if it means a more verbose description.
- Append the issue link at the end of the bullet: `([#N](https://github.com/Jagg111/COI-ResearchQueue/issues/N))`
- No em dashes anywhere
- No headers, no sections -- just the bullet list

**Show the bullets inline in the conversation** and ask: "Any tweaks before I save these?"

Apply any edits the user requests. Once they approve, write the final bullets to `bin/githubrelease/whats-new.md` (create the folder if it doesn't exist). Confirm the file was written.

---

## Step 4 -- Bump version and suggest commit message

Update the version number and give the user a ready-to-run commit command.

1. Calculate the new version from the user's choice in Step 2 and edit `manifest.json` with the new version string
2. Suggest a commit message following the project's style:
   - Single line, no body text
   - Example: `Version bump to 1.2.3`
   - If the release closes or fixes a GitHub issue, append `Fixes #N` or `Closes #N` -- check with the user if unsure
3. Tell the user exactly what to run:

```
git add manifest.json
git commit -m "<suggested message here>"
```

4. **Wait** for the user to confirm they've committed before continuing. Never commit on their behalf.

---

## Step 5 -- Create the GitHub draft release

Package the mod and create the draft release on GitHub.

Once the user confirms they've committed, run:

```
.\create-github-release.ps1
```

The script will automatically pick up `bin/githubrelease/whats-new.md` for the What's New section.

Stream the output. If the script fails, show the full error and stop.

---

## Step 6 -- Done

Confirm the GitHub draft release was created successfully.

Remind the user: "Go to [GitHub Releases](https://github.com/Jagg111/COI-ResearchQueue/releases) to review and publish the draft when you're ready."

Then ask: "Did this release include any visible UI changes?" If yes, remind the user:

> Don't forget to update `screenshots/current.gif` in the repo to reflect the new UI, then commit it. The README links directly to that file.

Conclude by saying something fun and lighthearted.

---

## Notes

- The What's New bullets are written for players, not developers. Commits that only affect build scripts, docs, or code comments are intentionally left out of the release notes.
- The release script (`create-github-release.ps1`) can also be run standalone outside of this workflow if needed.
- If `gh auth status` fails in pre-flight, the user needs to run `gh auth login` first.
