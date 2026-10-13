# Issue tracker: GitHub

Issues and specs live in GitHub Issues for `DasNeo/Bannerlord.XmlPreview`.
Run the `gh` CLI from this repo.

## Conventions

- Create: `gh issue create --title "..." --body-file <file>`.
- Read: `gh issue view <number> --json number,title,body,labels,comments`.
- List: `gh issue list --state open --json number,title,body,labels`.
- Comment: `gh issue comment <number> --body-file <file>`.
- Apply or remove labels: `gh issue edit <number> --add-label "..."` or `--remove-label "..."`.
- Close: `gh issue close <number> --comment "..."`.

For multiline bodies, save the exact text to a temporary file and use `--body-file`.
Infer the repository from the Git remote.

## Pull requests as a triage surface

**PRs as a request surface: no.**

## Skill operations

"Publish to the issue tracker" means create a GitHub issue.
"Fetch the relevant ticket" means read the GitHub issue.

## Wayfinding

- Map: one issue labelled `wayfinder:map`, with Notes, Decisions-so-far, and Fog.
- Child tickets: link as GitHub sub-issues where supported. Otherwise add a task list to the map and `Part of #<map>` to each child.
- Types: `wayfinder:research`, `wayfinder:prototype`, `wayfinder:grilling`, or `wayfinder:task`.
- Blocking: use native GitHub issue dependencies where supported. API dependency operations require numeric database issue IDs, not issue numbers. Otherwise record `Blocked by: #<number>` in the child body.
- Frontier: choose the first open child in map order with no open blockers and no assignee.
- Claim: `gh issue edit <number> --add-assignee @me`.
- Resolve: comment with the result, close the child, and append a context pointer to the map's Decisions-so-far.
