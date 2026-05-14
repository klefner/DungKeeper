# Release Runbook

## Release Types

| Type | Branch | Trigger |
|------|--------|---------|
| Feature release | `release/vX.Y.0` from `develop` | Planned milestone |
| Patch release | `release/vX.Y.Z` from `develop` | Bug fix batch |
| Hotfix | `hotfix/description` from `main` | Critical production bug |

## Standard Release Steps

1. Cut `release/vX.Y.Z` from `develop`
2. Update `CHANGELOG.md` — move Unreleased items to the version section
3. Update version in `ProjectSettings/ProjectVersion.txt` (Unity)
4. PR `release/vX.Y.Z` → `main` — CI must pass
5. Tag `main` with `vX.Y.Z` after merge
6. Back-merge: PR `main` → `develop` to capture any release-branch fixes
7. Build and distribute via Unity Cloud Build or GitHub Actions

## Hotfix Steps

1. Cut `hotfix/description` from `main`
2. Fix and commit
3. PR → `main` — CI must pass
4. Tag `main` with patch version
5. Back-merge: PR `main` → `develop`

## Rollback

Revert the merge commit on `main` and re-tag.

```bash
git revert -m 1 <merge-commit-sha>
git tag vX.Y.Z-rollback
```
