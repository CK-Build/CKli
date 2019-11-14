# CKli

## What is CKli ?

CKli is a tool for <u>multi-repositories</u> stacks.
It allows to automate actions, on <u>Worlds</u> (a group of repositories), and concentrates information in a single place.

## Getting Started

### Prerequisites

- [.NET Core SDK 2.1.](https://dotnet.microsoft.com/)

### Installation

CKli is a [dotnet tool](https://docs.microsoft.com/en-us/dotnet/core/tools/global-tools). You can install it globally by running :

```powershell
#latest stable
dotnet tool install CKli -g
```

### Run CKli

If you installed CKli globally, you can run `ckli` in any command prompt to start it.  

:warning: On Linux, the Path environment variable of your shell need to be updated.

### First Run

Since there is no Stack defined yet, CKli initializes one Repository that contains the CK and CK-Build stacks.
Both of them are mapped to '/Dev/CK' by default: their repositories will be cloned in this folder.
(Note that you can 'run World/SetWorldMapping' command to update this default mapping.)

CK and CK-Build are public Stacks in a Public repository: they should be cloneable by anyone. This is not always the case: CKli can work with private
stacks that require authorizations: some **secrets will be required** (and of course even CK and CK-Build require secrets in order to push code).

CKli manages a [KeyVault](docs/KeyVault.md) where all your secrets are stored. You must enter a password to secure this key vault, and every time you
start CKli, you will be prompted for your KeyVault password. (If you trust your desktop security, this password doesn't need to be strong.)

Once the required KeyVault password is entered, press `enter`: 

```
--------- Worlds ---------
  - CK
     > 1 CK => /Dev/CK
  - CK-Build
     > 2 CK-Build => /Dev/CK
     > 3 CK-Build[NetCore2] => /Dev/CK-Build[NetCore2]
```

This lists the available Stacks and Worlds an to open (and clone if needed) a whole World, type its number and press `enter`.

### Commands

There is 5 <u>basic commands</u> available, that allow you to:

- `list` and discover available commands.
- `run` one or more of commands.
- clear the screen, with `cls`.
- manage your `secret` keys.
- `exit`.

These <u>basic commands</u> are available everywhere but the actual commands are the one that you `run` and they depend on the context.

`run` and `list` first parameter is a command name or a pattern with simple wildcards (* and ?).

For example, `list home*`  will return any command starting with `home` : 

```
Available Commands matching 'home*':
     Home/Close
     Home/DeleteStackDefinition
     Home/EnsureStackDefinition
     Home/SetWorldMapping
```

You can notice that the filtering is <u>case insensitive</u>.

 :information_source: `run` will only work on a set of commands with the same payload (parameters).

`> run home*`

Will output:

```
> Warn: Pattern 'home*' matches require 4 different payloads.
|  - Warn: (stackName, url, isPublic, mappedPath, branchName): Home/EnsureStackDefinition
|          (stackName): Home/DeleteStackDefinition
|          (worldFullName, mappedPath): Home/SetWorldMapping
|          <No payload>: Home/Close, Home/Refresh
```

### QuickStart: Cloning a World

Gets the URL of the stack you want to clone:

| Stack Name | Repository URL                                                      |            |
| ---------- | ------------------------------------------------------------------- | ---------- |
| CK         | https://github.com/signature-opensource/CK-Stack                    | **Public** |
| CK-Build   | https://github.com/signature-opensource/CK-Stack                    | **Public** |
| Engie      | https://invenietis@dev.azure.com/invenietis/Cofely/_git/Engie-Stack | Private    |
| SC         | https://gitlab.com/signature-code/signature-code-stack.git          | Private    |
| S-Mos      | https://gitlab.com/signature-mosaic/Signature-Mosaic-Stack          | Private    |

Type `run Home/EnsureStackRepository` (or `run *ensure*` since there is no other command with `Ensure` in its name) and press `enter`.

You now need to fill the argument of this command that is the url and whether it is a Public or a Private stack: 

```powershell
[required] - url:                                  #The Url of the stack you are cloning
[required] - isPublic:                             #true for public stack, false otherwise
```

If you see any warning, read them carefully: you may have a secret missing (and please note that the Public/Private flag is to handle
the secrets requirements only: if you don't have the authorizations to access a repository, it is useless to try to make it Public!).

Type `secret` to see all the secrets needed by CKli. You may not need to fill all of these, only those asked in the warning.

To set a secret, type `secret set SECRET_NAME`.

Once the required secrets entered, press `enter`: 

```
--------- Worlds ---------
  - CK
     > 1 CK => /Dev/CK
  - CK-Build
     > 2 CK-Build => /Dev/CK
     > 3 CK-Build[NetCore2] => /Dev/CK-Build[NetCore2]
  - SC
     > 4 SC => C:/dev/CK
```

Upon opening a World, CKli will clone all the repositories.
To open a world, type its number then press `enter`.

### Worlds And Stacks

A World is a group of repositories.

A Stack is a default World, with zero or more [Parallel World](docs/ParallelWorlds.md).

```bash
--------- Worlds ---------
  - CK #A Stack
     > 1 CK => /Dev/CK #A World
  - CK-Build #Another Stack
     > 2 CK-Build => /Dev/CK #A World
     > 3 CK-Build[NetCore2] => /Dev/CK-Build[NetCore2] #A Parallel World
```

The stacks are versioned in git repositories.

You can list the worlds by simply pressing `enter` instead of typing a command.

To open a World, type its number then press `enter`.

To close a world, run `Home/Close`.

To add a Stack, use the command `Home/EnsureStackDefinition`  

#### Add an existing Stack

CKli does not know where your stack are defined.  

##### Ensure Stack Definition

In this example, we will add the stack SC (Signature-Code).

SC is a private stack, if you don't have access to https://gitlab.com/signature-code/signature-code-stack.git, you can not reproduce this example.

Run `Home/EnsureStackDefinition`

Fill the parameters of the command like below :

```bash
> SC                                                        #stack's name
> https://gitlab.com/signature-code/signature-code-stack.git#Git URL containing the stack
> false                                                     #Whether the stack is public
> C:/dev/CK                                                 #local mapping
>                                                           #leave empty
> y                                                         #Confirm
```

:information_source: The local mapping is the location where the Stack will be cloned on your PC.

Signature-Code being a private stack, CKli will not be able to clone the repository automatically.

Press `Enter` to see the Worlds list. You can see that that CKli couldn't clone automatically the repository, because the PAT([Personal Access Token](https://docs.gitlab.com/ee/user/profile/personal_access_tokens.html)) for GitLab is missing.

##### Add the GitLab PAT

Create your PAT on GitLab: https://gitlab.com/profile/personal_access_tokens

_____

For Your Information, here are the URLs to create your PATs on other git services:

- GitHub: https://github.com/settings/tokens

- Azure
  
  - Invenietis: https://dev.azure.com/invenietis/_usersSettings/tokens
  - Signature-OpenSource: https://dev.azure.com/Signature-OpenSource/_usersSettings/tokens
  - Signature-Code: https://dev.azure.com/Signature-Code/_usersSettings/tokens

_____

Run `secret set GITLAB_GIT_WRITE_PAT`

Paste the previously obtained token

```bash
Enter 'GITLAB_GIT_WRITE_PAT' secret (empty to cancel): I2MjVkYzIxZWYwNWY2YWQ0ZGRmNDdjNWYy
```

Then press enter, and you secret is now stored in your [KeyVault](docs/KeyVault.md).

The stack you added will now be displayed.

```
--------- Worlds ---------
  - CK
     > 1 CK => /Dev/CK
  - CK-Build
     > 2 CK-Build => /Dev/CK
     > 3 CK-Build[NetCore2] => /Dev/CK-Build[NetCore2]
  - SC
     > 4 SC => C:/dev/CK
```

To open a World, you can type its number and press `enter`.

On World the opening, CKli will automatically clone all the repositories of SC.

:tada: You are now ready to operate on a World. To see what you can do, please head to the [Common Usages](docs/Common_Usage.md).

## See Also

- https://blog.7mind.io/role-based-repositories.html
- https://github.com/7mind/sbtgen
