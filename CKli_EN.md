# CKli

## What is CKli ?

CKli is a tool for <u>multi-repositories</u> stacks. It allow to automate actions, on <u>Worlds</u>(a group of repositories), and concentrate information in a single place.

## Getting Started

### Prerequisites

- [.NET Core Runtime 2.1.](https://dotnet.microsoft.com/)



### Installation

CKli is a [dotnet tool](https://docs.microsoft.com/en-us/dotnet/core/tools/global-tools). You can install it globally by running :

```powershell
#latest stable
dotnet tool install CKli -g
```



### Run CKli

If you installed CKli globally, you can run `ckli` in any command prompt to start it.  



### First Run

CKli will ask a password to secure your [KeyVault](docs/KeyVault.md), where all your secrets are stored. Every time you start CKli, your KeyVault password will be asked.

By default there is 2 stack available: CK and CK-Build.



### Commands

There is 4 <u>base commands</u> available, that allow you to:

- `run` a set of commands
- `list` a set of commands
- clear the prompt, with `cls`
- manage your `secret`
- `exit`

These <u>base commands</u> are available everywhere.

Different <u>commands</u> available, depending on the context.

The commands `run` and `list` works on a set of commands. You can <u>filter</u> those commands with wildcards and text.

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



### Worlds And Stacks

A World is a group of repositories.

A Stack is a World, with zero or more [Parallel World](docs/ParallelWorlds.md).

The stacks are versioned in git repositories. [Stacks Index](docs/StackIndex.md)

You can list the worlds by simply pressing `enter` instead of typing a command.

To open a World, type its number then press `enter`.

To close a world, run `Home/Close`.

To add a Stack, use the command `Home/EnsureStackDefinition`  



#### Add an existing Stack

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

Run `secret set GITLAB_GIT_WRITE_PAT`

Paste the previously obtained token
``` bash
Enter 'GITLAB_GIT_WRITE_PAT' secret (empty to cancel): I2MjVkYzIxZWYwNWY2YWQ0ZGRmNDdjNWYy
```

Then press enter, and you secret is now stored in your [KeyVault](docs/KeyVault.md).

CKli will also automatically clone the SC repository, and display the worlds in it.