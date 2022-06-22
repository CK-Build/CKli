# CI Auto Configuration

Currently CKli choose which CI a repository should use, based on a (currently) hardcoded rule:

- If the repository is public, then the repository use AppVeyor CI.

- If the repository is private:
  - On GitHub: We don't use GitHub for private repositories.
  - On GitLab: then we use the GitLab CI.
  - On Azure Devops: Not Implemented, will be available soon.

