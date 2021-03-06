# The KeyVault

The KeyVault is a text file storing <u>secrets</u>, encrypted with a passphrase.

Example of a KeyVault:

```
-- Version: 2
-- Keys below can be removed if needed.
NUGET_ORG_PUSH_API_KEY
AZURE_FEED_SIGNATURE_OPENSOURCE_PAT
 > uiu+tq3ceDnXnr8ub498fzuVKP7qt3cbkRrLodESxuiPfOmnh3PXJMAYB+zKL4UxtJyB/1ucSIyJxakzcfxQ6bVNNgregCL8NvtH4+KzEuxU0a0jcRHp/p8g4zy020YV4cFBISJ7Shly8yzF5bVqn84dbqNUIskPdzKXe041rFaubDTddYZvJEahjdYhpI7TwIyewLMgmHA2ljJpl7nE+1pqxz+e+MHoqEc1w8OJv+w=
```

Lines starting with `--` are comments.

You can read 2 secrets names, as the comment point out, if you remove them, the result corresponding to it wont be parsed.

## Personal KeyVault

Your personal KeyVault is where your secrets are stored.

On the first run, CKli will ask for a passphrase and will use this to encrypt your secrets.

Every time your start CKli, this passphrase will be asked, to load all your secrets.

This KeyVault is stored at  `%LocalAppData%/CKli/Personal.KeyVault.txt`.

## Shared KeyVault

At this moment, there is only one Shared KeyVault: the CI/CD KeyVault.
This KeyVault is stored in the Shared State, next to the World config file: it belongs the the stack Git repository
and is shared by all the "users" of a World.

[Work In Progress]
