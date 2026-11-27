# Security policy

## Supported versions

Only the latest release receives fixes.

## Reporting a vulnerability

If you find a security issue in RageForge itself (for example a crash or code execution when
opening a crafted file, or a problem with the update check), please report it privately through
GitHub's "Report a vulnerability" button on this repository, or contact the maintainer through
GitHub. Include steps to reproduce using synthetic files only.

## Out of scope

- Requests for, or reports containing, game encryption keys or ways to decrypt or bypass the
  protection of game files.
- Reports that include game files or extracted content.

## Updates

At startup the GUI asks the GitHub API over HTTPS whether a newer release of this repository
exists. It does not download or install anything: when an update exists, it opens the release page
in the browser, and you download and verify the package yourself.
